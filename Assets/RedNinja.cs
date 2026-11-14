using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

[DefaultExecutionOrder(526)]
public sealed class RedNinja : CombatEnemy
{
    internal const int MaxHealth = 35;
    internal const int FireDamage = 2;
    private const float FleeRange = 8f;
    private const float FleeDistance = 10f;
    private const float FleeSpeed = 6f;
    private const float FleeCooldown = 5f;
    private const float CastRange = 17f;
    internal const float AimDuration = 0.9f;
    private const float CastCooldown = 2.8f;
    internal const float ReleaseTime = 0.17f;
    private const float FireSpeed = 12f;
    internal const float FireVisualScale = 0.7f;
    internal const float FireDiameter = 1.9f * FireVisualScale;
    private const float PlayerAimHeight = 1.15f;
    private const float HitRadius = 1.5f;
    private static readonly float[] EscapeAngles = { 0f, 35f, -35f, 70f, -70f };

    [SerializeField] private Animator animator;
    [SerializeField] private AnimationClip idle, sprint, spellEnter, spellAim, spellShoot, spellExit;
    [SerializeField] private Material fireMaterial;
    [SerializeField] private Shader fireballShader;

    internal enum Phase : byte { Idle, Flee, Enter, Aim, Shoot, Exit, Cancel, Dead }
    internal enum FireStatus : byte { None, Flying, Impact }

    internal struct State
    {
        internal Phase Phase;
        internal Vector3 Position;
        internal float Yaw, Elapsed;
        internal int Health;
        internal uint HitSequence, ShotSequence;
        internal ulong HitHistory;
        internal FireStatus Fire;
        internal Vector3 FirePosition, FireDirection;
    }

    private WasdBlockMovement player;
    private Vector3 previousPlayerPosition;
    private GameplayHud hud;
    private CoopSession session;
    private EnemyDamageFeedback feedback;
    private CharacterWhiteFlash hitFlash;
    private RedNinjaFire effects;
    private Transform hand, chest, leftFoot, rightFoot;
    private Vector3 visualRestPosition;
    private float leftSole, rightSole;
    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private AnimationClip[] clips;
    private AnimationClipPlayable[] poses;
    private int currentPose, previousPose;
    private float blendStarted, previousPoseTime;

    private Phase phase;
    private float phaseStarted, nextFlee, nextCast, fleeRemaining, lastSnapshotTime;
    private byte targetPeer;
    private Vector3 lockedTarget;
    private bool released;
    private FireStatus fire;
    private Vector3 firePosition, fireDirection;
    private uint shotSequence, hitSequence, displayedHit;
    private ulong hitHistory;
    private State remote;
    private bool hasRemote;

    internal override int Health { get; private protected set; } = MaxHealth;
    internal override int HealthLimit => MaxHealth;
    private bool IsReplica => session != null && session.InMatch && !session.IsHost;
    private bool LocalAlive => player != null && hud != null && hud.Health > 0;
    private float Elapsed => Time.time - phaseStarted;
    private Vector3 SpellHand => hand.position;

    private void Start()
    {
        if (animator == null || !animator.isHuman || idle == null || sprint == null ||
            spellEnter == null || spellAim == null || spellShoot == null || spellExit == null ||
            fireMaterial == null || fireballShader == null)
        {
            Debug.LogError("Red Ninja needs a humanoid rig, six clips and a fire material.", this);
            enabled = false;
            return;
        }

        player = FindAnyObjectByType<WasdBlockMovement>();
        if (player != null)
            previousPlayerPosition = player.transform.position;
        hud = FindAnyObjectByType<GameplayHud>();
        session = FindAnyObjectByType<CoopSession>();
        if (player != null && player.TryGetGroundHeight(transform.position, out float ground))
        {
            var position = transform.position;
            position.y = ground;
            transform.position = position;
        }
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        if (hud != null)
            feedback = new EnemyDamageFeedback((RectTransform)hud.transform, Camera.main, "Red Ninja",
                head.position.y - transform.position.y + 0.65f - 2.38f);
        hitFlash = new CharacterWhiteFlash(animator.transform);
        effects = new RedNinjaFire(transform, fireMaterial, fireballShader);
        hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        visualRestPosition = animator.transform.localPosition;
        leftSole = leftFoot.position.y - transform.position.y;
        rightSole = rightFoot.position.y - transform.position.y;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        clips = new[] { idle, sprint, spellEnter, spellAim, spellShoot, spellExit };
        graph = PlayableGraph.Create("Red Ninja poses");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        mixer = AnimationMixerPlayable.Create(graph, clips.Length);
        poses = new AnimationClipPlayable[clips.Length];
        for (int i = 0; i < clips.Length; i++)
        {
            poses[i] = AnimationClipPlayable.Create(graph, clips[i]);
            poses[i].SetApplyFootIK(true);
            poses[i].SetSpeed(0);
            graph.Connect(poses[i], 0, mixer, i);
        }
        mixer.SetInputWeight(0, 1f);
        AnimationPlayableOutput.Create(graph, "Red Ninja", animator).SetSourcePlayable(mixer);
        graph.Play();
        phaseStarted = Time.time;
        nextCast = Time.time + 1f;
    }

    private void Update()
    {
        if (IsReplica)
        {
            if (!hasRemote)
                return;
            float blend = 1f - Mathf.Exp(-24f * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, remote.Position, blend);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, remote.Yaw, 0f), blend);
            if (fire == FireStatus.Flying)
            {
                firePosition += fireDirection * (FireSpeed * Time.deltaTime);
                Vector3 estimate = remote.FirePosition + fireDirection * (FireSpeed * (Time.time - lastSnapshotTime));
                firePosition = Vector3.Lerp(firePosition, estimate, blend);
            }
            return;
        }

        Tick(Time.time, Time.deltaTime);
    }

    internal void Tick(float now, float dt)
    {
        if (fire == FireStatus.Flying)
            AdvanceFire(dt);
        if (player != null)
            previousPlayerPosition = player.transform.position;

        float age = now - phaseStarted;
        switch (phase)
        {
            case Phase.Idle:
                if (!FindTarget(out var target, out float distance))
                    break;
                if (distance <= FleeRange * FleeRange && now >= nextFlee)
                {
                    fleeRemaining = FleeDistance;
                    ChangePhase(Phase.Flee, now);
                }
                else if (distance <= CastRange * CastRange && now >= nextCast && fire != FireStatus.Flying)
                {
                    Face(target, dt);
                    ChangePhase(Phase.Enter, now);
                }
                break;
            case Phase.Flee:
                if (fleeRemaining <= 0.001f || !FindTarget(out target, out _) || !FleeFrom(target, dt))
                {
                    nextFlee = now + FleeCooldown;
                    ChangePhase(Phase.Idle, now);
                }
                break;
            case Phase.Enter:
            case Phase.Aim:
                if (!TrySelectedTarget(out target))
                {
                    FinishCast(now, cancelled: true);
                    break;
                }
                Face(target, dt);
                if (phase == Phase.Enter && age >= spellEnter.length)
                    ChangePhase(Phase.Aim, now);
                else if (phase == Phase.Aim && age >= AimDuration)
                {
                    lockedTarget = target;
                    released = false;
                    ChangePhase(Phase.Shoot, now);
                }
                break;
            case Phase.Shoot:
                if (!released && age >= ReleaseTime)
                {
                    released = true;
                    // Keep the projectile at chest height across the arena, even past its target.
                    const float clearance = FireDiameter * 0.5f + 0.05f;
                    firePosition = player != null ? player.ClampToMovementBoundary(SpellHand, clearance) : SpellHand;
                    Vector3 aim = player != null ? player.ClampToMovementBoundary(lockedTarget, clearance) : lockedTarget;
                    fireDirection = Planar(aim - firePosition).normalized;
                    if (fireDirection == Vector3.zero)
                        fireDirection = transform.forward;
                    fire = FireStatus.Flying;
                    shotSequence++;
                }
                if (age >= spellShoot.length)
                    FinishCast(now, cancelled: false);
                break;
            case Phase.Exit:
            case Phase.Cancel:
                if (age >= spellExit.length)
                    ChangePhase(Phase.Idle, now);
                break;
        }
    }

    private bool FindTarget(out Vector3 target, out float distance)
    {
        target = LocalAlive ? player.transform.position : default;
        distance = LocalAlive ? Planar(target - transform.position).sqrMagnitude : float.PositiveInfinity;
        targetPeer = session != null ? session.FindNearestLivingPeer(transform.position, ref target, ref distance) : (byte)0;
        return !float.IsInfinity(distance);
    }

    private bool TrySelectedTarget(out Vector3 target)
    {
        if (targetPeer != 0 && session != null)
        {
            if (!session.TryGetLivingPeerPosition(targetPeer, out target))
                return false;
            target.y = player != null ? player.transform.position.y : transform.position.y;
            return true;
        }
        target = LocalAlive ? player.transform.position : default;
        return LocalAlive;
    }

    private bool FleeFrom(Vector3 target, float dt)
    {
        var position = transform.position;
        var away = Planar(position - target).normalized;
        if (away == Vector3.zero)
            away = transform.forward;
        float step = Mathf.Min(fleeRemaining, FleeSpeed * dt);
        if (step <= 0f)
            return true;

        foreach (float angle in EscapeAngles)
        {
            Vector3 heading = Quaternion.Euler(0f, angle, 0f) * away;
            Vector3 next = position + heading * step;
            if (player != null)
            {
                if ((player.ClampToMovementBoundary(next) - next).sqrMagnitude > 0.000001f ||
                    !player.TryGetGroundHeight(next, out float ground))
                    continue;
                next.y = ground;
            }
            // Count the actual path, including its slope, against the burst limit.
            float moved = Vector3.Distance(position, next);
            if (moved > fleeRemaining)
            {
                next = Vector3.Lerp(position, next, fleeRemaining / moved);
                moved = fleeRemaining;
            }
            transform.position = next;
            fleeRemaining -= moved;
            Face(next + heading, dt);
            return true;
        }
        return false;
    }

    private void Face(Vector3 target, float dt)
    {
        var direction = Planar(target - transform.position);
        if (direction.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(direction), 720f * dt);
    }

    private void AdvanceFire(float dt)
    {
        var end = firePosition + fireDirection * (FireSpeed * dt);
        bool reachesBoundary = player != null && !InsideFireBoundary(end);
        if (reachesBoundary)
        {
            // Clip the swept segment to the inside edge of the arena before testing hits.
            float inside = 0f, outside = 1f;
            for (int i = 0; i < 12; i++)
            {
                float midpoint = (inside + outside) * 0.5f;
                if (InsideFireBoundary(Vector3.Lerp(firePosition, end, midpoint)))
                    inside = midpoint;
                else
                    outside = midpoint;
            }
            end = Vector3.Lerp(firePosition, end, inside);
        }
        float localFraction = float.PositiveInfinity;
        bool dodging = LocalAlive && player.IsDodging;
        if (LocalAlive)
        {
            Vector3 feet = player.transform.position;
            Vector3 relativeStart = firePosition;
            Vector3 playerTravel = feet - previousPlayerPosition;
            if (dodging && !reachesBoundary && playerTravel.sqrMagnitude <= player.DashReach * player.DashReach)
                relativeStart += playerTravel; // Sweep against the player's motion during the dash.
            localFraction = ContactFraction(relativeStart, end, feet);
        }
        if (session != null && session.TryResolvePeerFireContact(firePosition, end, localFraction, out float peerFraction))
        {
            firePosition = Vector3.Lerp(firePosition, end, peerFraction);
            fire = FireStatus.Impact;
        }
        else if (localFraction <= 1f)
        {
            if (dodging)
                hud.ShowPerfectDodge(player.transform);
            else
                hud.TakeDamage(FireDamage);
            firePosition = Vector3.Lerp(firePosition, end, localFraction);
            fire = FireStatus.Impact;
        }
        else
        {
            firePosition = end;
            if (reachesBoundary)
                fire = FireStatus.Impact;
        }
    }

    private bool InsideFireBoundary(Vector3 center) =>
        (player.ClampToMovementBoundary(center, FireDiameter * 0.5f) - center).sqrMagnitude < 0.000001f;

    internal static float ContactFraction(Vector3 start, Vector3 end, Vector3 feet)
    {
        var segment = end - start;
        Vector3 offset = feet + Vector3.up * PlayerAimHeight - start;
        float separation = offset.sqrMagnitude - HitRadius * HitRadius;
        if (separation <= 0f)
            return 0f;
        float length = segment.sqrMagnitude;
        float projection = Vector3.Dot(offset, segment);
        float discriminant = projection * projection - length * separation;
        if (length <= 0f || projection <= 0f || discriminant < 0f)
            return float.PositiveInfinity;
        float contact = (projection - Mathf.Sqrt(discriminant)) / length;
        return contact <= 1f ? contact : float.PositiveInfinity;
    }

    private void FinishCast(float now, bool cancelled)
    {
        nextCast = now + CastCooldown;
        ChangePhase(cancelled ? Phase.Cancel : Phase.Exit, now);
    }

    private void ChangePhase(Phase next, float now)
    {
        phase = next;
        phaseStarted = now;
    }

    internal override void ApplyMeleeStrike(int strike)
    {
        if (strike >= 0 && strike < PlayerMeleeCombo.StrikeCount)
            ApplyAreaStrike(10 + strike);
    }

    internal override void ApplyAreaStrike(int damage)
    {
        if (Health <= 0 || damage <= 0 || IsReplica)
            return;
        Health -= RollPlayerDamage(damage, Health, out byte hit);
        hitHistory = (hitHistory << 8) | hit;
        hitSequence++;
        if (Health <= 0)
            ChangePhase(Phase.Dead, Time.time);
        else if (phase == Phase.Aim)
            FinishCast(Time.time, cancelled: true);
    }

    private void LateUpdate()
    {
        if (!graph.IsValid())
            return;
        UpdateDamageFeedback();
        animator.gameObject.SetActive(phase != Phase.Dead || hitFlash.Active);
        if (phase != Phase.Dead)
            Animate();
        float chargeLeft = phase switch
        {
            Phase.Enter => spellEnter.length - Elapsed + AimDuration + ReleaseTime,
            Phase.Aim => AimDuration - Elapsed + ReleaseTime,
            Phase.Shoot when !released => ReleaseTime - Elapsed,
            _ => 0f
        };
        effects.Update(phase, SpellHand, chest != null ? chest.position : transform.position + Vector3.up,
            Elapsed, chargeLeft, shotSequence, fire, firePosition, fireDirection);
    }

    private void Animate()
    {
        int nextPose = phase switch
        {
            Phase.Flee => 1, Phase.Enter => 2, Phase.Aim => 3,
            Phase.Shoot => 4, Phase.Exit or Phase.Cancel => 5, _ => 0
        };
        if (nextPose != currentPose)
        {
            mixer.SetInputWeight(previousPose, 0f);
            previousPose = currentPose;
            previousPoseTime = (float)poses[previousPose].GetTime();
            currentPose = nextPose;
            blendStarted = Time.time;
        }
        float blend = Mathf.Clamp01((Time.time - blendStarted) / 0.08f);
        if (previousPose != currentPose)
        {
            mixer.SetInputWeight(previousPose, 1f - blend);
            poses[previousPose].SetTime(ClipTime(previousPose, previousPoseTime + (Time.time - blendStarted)));
        }
        mixer.SetInputWeight(currentPose, previousPose == currentPose ? 1f : blend);
        poses[currentPose].SetTime(ClipTime(currentPose, Elapsed));
        animator.transform.localPosition = visualRestPosition;
        graph.Evaluate(0f);
        float lowest = Mathf.Min(leftFoot.position.y - leftSole, rightFoot.position.y - rightSole);
        animator.transform.position += Vector3.up * Mathf.Max(0f, transform.position.y - lowest);
    }

    private float ClipTime(int pose, float elapsed)
    {
        float time = Mathf.Max(elapsed, 0f);
        float length = clips[pose].length;
        return pose is 0 or 1 or 3 ? Mathf.Repeat(time, length) : Mathf.Min(time, length - 0.0001f);
    }

    private void UpdateDamageFeedback()
    {
        uint unseen = hitSequence - displayedHit;
        if (unseen > 0)
            hitFlash.Pulse();
        for (int i = (int)System.Math.Min(unseen, 8u) - 1; i >= 0; i--)
        {
            byte hit = (byte)(hitHistory >> (i * 8));
            feedback?.ShowHit(transform.position, hit & 63, (LungeMan.CriticalHit)(hit >> 6));
        }
        displayedHit = hitSequence;
        hitFlash.Update();
        feedback?.Update(transform.position, Health, MaxHealth, true);
    }

    internal State GetState() => new State
    {
        Phase = phase, Position = transform.position, Yaw = transform.eulerAngles.y, Elapsed = Elapsed,
        Health = Health, HitSequence = hitSequence, HitHistory = hitHistory,
        ShotSequence = shotSequence, Fire = fire, FirePosition = firePosition, FireDirection = fireDirection
    };

    internal void ApplyState(State state)
    {
        if (!hasRemote)
        {
            transform.SetPositionAndRotation(state.Position, Quaternion.Euler(0f, state.Yaw, 0f));
            displayedHit = state.HitSequence;
        }
        if (shotSequence != state.ShotSequence || state.Fire != FireStatus.Flying)
            firePosition = state.FirePosition;
        remote = state;
        hasRemote = true;
        phase = state.Phase;
        lastSnapshotTime = Time.time;
        phaseStarted = lastSnapshotTime - state.Elapsed;
        Health = state.Health;
        hitSequence = state.HitSequence;
        hitHistory = state.HitHistory;
        shotSequence = state.ShotSequence;
        fire = state.Fire;
        fireDirection = state.FireDirection;
    }

    private void OnDestroy()
    {
        if (graph.IsValid())
            graph.Destroy();
        effects?.Dispose();
        feedback?.Dispose();
    }

    private static Vector3 Planar(Vector3 value) => new Vector3(value.x, 0f, value.z);
}
