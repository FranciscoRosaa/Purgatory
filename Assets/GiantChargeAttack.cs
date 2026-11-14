using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

[DefaultExecutionOrder(525)]
public sealed class GiantChargeAttack : CombatEnemy
{
    internal const int MaxHealth = 100;
    internal const int SprintDamage = 5;
    internal const int JumpDamage = 7;
    private const float JumpCooldown = 3.5f;
    private const float JumpRange = 8f;
    private const float JumpTakeoff = 0.52f;
    private const float JumpLanding = 1.53f;
    private const float JumpSquareSize = 6f;
    private const float ContactRadius = 1.25f;
    private const float RecoveryDuration = 0.48f;
    private const float LoopEntryFraction = 0.15f;

    [Header("Model and animation")]
    [SerializeField] private Transform visual;
    [SerializeField] private Animator animator;
    [SerializeField] private AnimationClip idleAnimation;
    [SerializeField] private AnimationClip sprintEnter;
    [SerializeField] private AnimationClip chargeAnimation;
    [SerializeField] private AnimationClip sprintExit;
    [SerializeField] private AnimationClip jumpAnimation;
    [SerializeField] private Material effectMaterial;
    [SerializeField] private Collider[] walkableGround;
    [SerializeField] private BoxCollider movementBoundary;
    [Header("Charge")]
    [SerializeField, Min(0.05f)] private float windupDuration = 1.2f;
    [SerializeField, Min(0.05f)] private float chargeDuration = 3f;
    [SerializeField, Min(0f)] private float chargeSpeed = 7.7f;
    [SerializeField, Min(0f)] private float cooldown = 4f;
    [SerializeField, Min(0f)] private float detectionRange = 18f;
    [SerializeField, Min(0f)] private float initialDelay = 1.5f;
    [SerializeField] private bool automaticAttack = true;
    [Header("Presentation")]
    [SerializeField, Min(0.1f)] private float auraRadius = 2.1f;
    [SerializeField, Min(0.05f)] private float animationSpeed = 1f;

    internal enum Phase : byte { Idle, Windup, Charge, Cooldown, Recovery, Dead, Jump }
    internal struct State
    {
        internal Phase Phase;
        internal Vector3 Position;
        internal float Yaw, Progress;
        internal int Health;
        internal uint HitSequence;
        internal ulong HitHistory;
        internal Vector3 JumpTarget;
        internal uint JumpImpact;
    }

    private WasdBlockMovement player;
    private GameplayHud hud;
    private CoopSession session;
    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private AnimationClipPlayable idlePose, attackPose, enterPose, exitPose, jumpPose;
    private GiantChargeVfx effects;
    private GiantJumpVfx jumpEffects;
    private EnemyDamageFeedback damageFeedback;
    private CharacterWhiteFlash hitFlash;
    private Transform leftFoot, rightFoot, chest;
    private Vector3 visualRestPosition, direction;
    private float phaseStarted, nextAttack;
    private Phase phase;
    private State remote;
    private bool hasRemote;
    private bool resolvedLocalSprintContact;
    private byte targetPeer;
    private uint sprintSequence, hitSequence, displayedHitSequence;
    private ulong hitHistory;
    private float recoveryPoseTime;
    private bool windupSoundPlayed;
    private int sprintStep = -1;
    private Vector3 jumpStart, jumpTarget;
    private bool jumpNext, jumpLanded, jumpSoundPlayed;
    private uint jumpImpact, displayedJumpImpact;

    private bool IsReplica => session != null && session.InMatch && !session.IsHost;
    internal override int Health { get; private protected set; } = MaxHealth;
    internal override int HealthLimit => MaxHealth;
    internal float WindupDuration => Mathf.Max(0.05f, windupDuration);
    internal float ChargeDuration => Mathf.Max(0.05f, chargeDuration);
    internal bool IsCharging => phase == Phase.Charge;

    private void Start()
    {
        if (visual == null || animator == null || !animator.isHuman ||
            chargeAnimation == null || idleAnimation == null || effectMaterial == null)
        {
            Debug.LogError("Gigante: assign the humanoid model, idle/sprint clips and particle material.", this);
            enabled = false;
            return;
        }
        player = FindAnyObjectByType<WasdBlockMovement>();
        hud = FindAnyObjectByType<GameplayHud>();
        session = FindAnyObjectByType<CoopSession>();
        if (hud != null)
        {
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            float barHeight = head != null ? head.position.y - transform.position.y + 0.65f : 4f;
            damageFeedback = new EnemyDamageFeedback((RectTransform)hud.transform, Camera.main,
                "Gigante", barHeight - 2.38f);
        }
        hitFlash = new CharacterWhiteFlash(visual);
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        visualRestPosition = visual.localPosition;
        effects = new GiantChargeVfx(transform, effectMaterial, auraRadius);
        graph = PlayableGraph.Create("Gigante charge poses");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        mixer = AnimationMixerPlayable.Create(graph, 5);
        idlePose = ConnectPose(idleAnimation, 0);
        attackPose = ConnectPose(chargeAnimation, 1);
        if (sprintEnter != null) enterPose = ConnectPose(sprintEnter, 2);
        if (sprintExit != null) exitPose = ConnectPose(sprintExit, 3);
        if (jumpAnimation != null)
        {
            jumpPose = ConnectPose(jumpAnimation, 4);
            jumpPose.SetApplyFootIK(false);
            jumpEffects = new GiantJumpVfx(transform, effectMaterial);
        }
        for (int i = 1; i < mixer.GetInputCount(); i++)
            mixer.SetInputWeight(i, 0f);
        mixer.SetInputWeight(0, 1f);
        var output = AnimationPlayableOutput.Create(graph, "Gigante", animator);
        output.SetSourcePlayable(mixer);
        graph.Play();
        nextAttack = Time.time + initialDelay;
    }

    private AnimationClipPlayable ConnectPose(AnimationClip clip, int input)
    {
        var pose = AnimationClipPlayable.Create(graph, clip);
        pose.SetApplyFootIK(true);
        graph.Connect(pose, 0, mixer, input);
        return pose;
    }

    private void Update()
    {
        if (IsReplica)
        {
            if (hasRemote)
            {
                transform.position = Vector3.Lerp(transform.position, remote.Position,
                    1f - Mathf.Exp(-22f * Time.deltaTime));
                transform.rotation = Quaternion.Euler(0f, remote.Yaw, 0f);
            }
            return;
        }
        Tick(Time.time, Time.deltaTime);
    }

    internal void Tick(float now, float deltaTime)
    {
        switch (phase)
        {
            case Phase.Idle:
                if (automaticAttack && now >= nextAttack)
                {
                    if (!jumpNext || !TryBeginJump(now))
                        TryBeginAttack(now);
                }
                break;
            case Phase.Jump:
                AdvanceJump(now);
                break;
            case Phase.Windup:
                if (!AimAtSelectedTarget())
                {
                    phase = Phase.Cooldown;
                    nextAttack = now + cooldown;
                    jumpNext = true;
                    break;
                }
                if (now >= phaseStarted + WindupDuration)
                {
                    phase = Phase.Charge;
                    phaseStarted = now;
                    CheckSprintContacts(transform.position, transform.position);
                }
                break;
            case Phase.Charge:
                float remaining = ChargeDuration - (now - phaseStarted - deltaTime);
                bool moved = AdvanceCharge(Mathf.Min(Mathf.Max(0f, remaining), deltaTime));
                if (!moved || now >= phaseStarted + ChargeDuration)
                {
                    recoveryPoseTime = LoopTime(Mathf.Clamp(now - phaseStarted, 0f, ChargeDuration));
                    phase = Phase.Recovery;
                    phaseStarted = now;
                    nextAttack = now + cooldown;
                    jumpNext = true;
                }
                break;
            case Phase.Recovery:
                if (now >= phaseStarted + ExitDuration)
                    phase = Phase.Cooldown;
                break;
            case Phase.Cooldown:
                if (now >= nextAttack)
                    phase = Phase.Idle;
                break;
        }
    }

    [ContextMenu("Start charge (Play Mode)")]
    private void StartChargeFromInspector()
    {
        if (Application.isPlaying && !IsReplica && (phase == Phase.Idle || phase == Phase.Cooldown))
            TryBeginAttack(Time.time);
    }

    private bool TryBeginAttack(float now)
    {
        if (now < nextAttack || (phase != Phase.Idle && phase != Phase.Cooldown))
            return false;

        if (!FindTarget(out var target))
            return false;
        direction = transform.forward;
        AimAt(target);
        sprintSequence++;
        resolvedLocalSprintContact = false;
        phase = Phase.Windup;
        phaseStarted = now;
        return true;
    }

    [ContextMenu("Start jump attack (Play Mode)")]
    private void StartJumpFromInspector()
    {
        if (Application.isPlaying && !IsReplica)
            TryBeginJump(Time.time);
    }

    private bool FindTarget(out Vector3 target)
    {
        target = player != null ? player.transform.position : transform.position;
        float nearest = player != null && (hud == null || hud.Health > 0)
            ? Planar(target - transform.position).sqrMagnitude : float.PositiveInfinity;
        targetPeer = session?.FindNearestLivingPeer(transform.position, ref target, ref nearest) ?? 0;
        return nearest <= detectionRange * detectionRange;
    }

    private bool TryBeginJump(float now)
    {
        if (!jumpPose.IsValid() || now < nextAttack || (phase != Phase.Idle && phase != Phase.Cooldown) ||
            !FindTarget(out var target))
            return false;

        direction = transform.forward;
        AimAt(target);
        jumpStart = jumpTarget = transform.position;
        float distance = Mathf.Min(JumpRange, Planar(target - jumpStart).magnitude);
        int steps = Mathf.CeilToInt(distance / 0.25f);
        for (int i = 1; i <= steps; i++)
        {
            var point = jumpStart + direction * (distance * i / steps);
            if (!IsWithinBoundary(point) || !TryGround(point, out float ground))
                break;
            point.y = ground;
            jumpTarget = point;
        }

        jumpLanded = false;
        jumpNext = false;
        phase = Phase.Jump;
        phaseStarted = now;
        return true;
    }

    private void AdvanceJump(float now)
    {
        float elapsed = now - phaseStarted;
        float travel = Mathf.InverseLerp(JumpTakeoff, JumpLanding, elapsed);
        transform.position = Vector3.Lerp(jumpStart, jumpTarget, travel);
        if (!jumpLanded && elapsed >= JumpLanding)
        {
            jumpLanded = true;
            jumpImpact++;
            nextAttack = now + JumpCooldown;
            if (player != null && hud != null && hud.Health > 0 && IsInJumpSquare(player.transform.position))
            {
                if (player.IsDodging)
                    hud.ShowPerfectDodge(player.transform);
                else
                    hud.TakeDamage(JumpDamage);
            }
            session?.ResolvePeersInGiantJump(this);
        }
        if (elapsed >= jumpAnimation.length)
            phase = Phase.Cooldown;
    }

    internal bool IsInJumpSquare(Vector3 point)
    {
        var offset = point - jumpTarget;
        float halfSize = JumpSquareSize * 0.5f;
        return Mathf.Abs(offset.y) <= 2f &&
            Mathf.Abs(Vector3.Dot(offset, transform.right)) <= halfSize &&
            Mathf.Abs(Vector3.Dot(offset, transform.forward)) <= halfSize;
    }

    private bool IsWithinBoundary(Vector3 point)
    {
        if (movementBoundary == null || !movementBoundary.enabled)
            return true;
        point.y = movementBoundary.bounds.center.y;
        return (movementBoundary.ClosestPoint(point) - point).sqrMagnitude <= 0.0001f;
    }

    private bool AimAtSelectedTarget()
    {
        if (targetPeer != 0)
        {
            if (session == null || !session.TryGetLivingPeerPosition(targetPeer, out var position))
                return false;
            AimAt(position);
        }
        else
        {
            if (player == null || (hud != null && hud.Health <= 0))
                return false;
            AimAt(player.transform.position);
        }
        return true;
    }

    private void AimAt(Vector3 position)
    {
        Vector3 offset = Planar(position - transform.position);
        if (offset.sqrMagnitude <= 0.0001f)
            return;
        direction = offset.normalized;
        transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
    }

    private bool AdvanceCharge(float elapsed)
    {
        float distance = Mathf.Max(0f, chargeSpeed) * Mathf.Max(0f, elapsed);
        int steps = Mathf.CeilToInt(distance / 0.2f);
        if (steps == 0)
            return true;
        for (int i = 0; i < steps; i++)
        {
            Vector3 previous = transform.position;
            Vector3 next = previous + direction * (distance / steps);
            if (!IsWithinBoundary(next))
                return false;
            if (!TryGround(next, out float ground))
                return false;
            next.y = ground;
            transform.position = next;
            CheckSprintContacts(previous, next);
        }
        // The heading locks when the sprint begins, leaving room to dodge.
        transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        return true;
    }

    private void CheckSprintContacts(Vector3 start, Vector3 end)
    {
        if (!resolvedLocalSprintContact && player != null && hud != null && hud.Health > 0 &&
            TrySprintContact(start, end, player.transform.position, out var push))
        {
            resolvedLocalSprintContact = true;
            if (player.IsDodging)
                hud.ShowPerfectDodge(player.transform);
            else
            {
                hud.TakeDamage(SprintDamage);
                player.ApplySprintKnockback(push);
                session?.RegisterLocalSprintHit();
            }
        }
        session?.ResolvePeersInGiantSprint(this, sprintSequence, start, end);
    }

    internal bool TrySprintContact(Vector3 start, Vector3 end, Vector3 point, out Vector3 push)
    {
        var segment = Planar(end - start);
        float fraction = segment.sqrMagnitude > 0f
            ? Mathf.Clamp01(Vector3.Dot(Planar(point - start), segment) / segment.sqrMagnitude) : 0f;
        Vector3 center = Vector3.Lerp(start, end, fraction);
        Vector3 offset = Planar(point - center);
        push = direction.sqrMagnitude > 0f ? direction : transform.forward;
        return Mathf.Abs(point.y - center.y) <= 2f && offset.sqrMagnitude <= ContactRadius * ContactRadius;
    }

    private bool TryGround(Vector3 point, out float height)
    {
        height = point.y;
        if (walkableGround == null || walkableGround.Length == 0)
            return true;
        var ray = new Ray(point + Vector3.up * 2f, Vector3.down);
        foreach (var ground in walkableGround)
            if (ground != null && ground.Raycast(ray, out var hit, 4f))
            {
                height = hit.point.y;
                return true;
            }
        return false;
    }

    private float ExitDuration => sprintExit != null ? Mathf.Max(0.05f, sprintExit.length) : RecoveryDuration;
    private float Duration => phase switch
    {
        Phase.Windup => WindupDuration,
        Phase.Recovery => ExitDuration,
        Phase.Jump => jumpAnimation != null ? jumpAnimation.length : 1f,
        _ => ChargeDuration
    };
    private float Progress => phase == Phase.Windup || phase == Phase.Charge || phase == Phase.Recovery || phase == Phase.Jump
        ? Mathf.Clamp01((Time.time - phaseStarted) / Duration) : 0f;
    private float LoopTime(float elapsed) => elapsed * animationSpeed +
        (sprintEnter == null ? chargeAnimation.length * LoopEntryFraction : 0f);


    private void LateUpdate()
    {
        if (!graph.IsValid())
            return;
        UpdateJumpEffects();
        UpdateDamageFeedback();
        hitFlash.Update();
        visual.gameObject.SetActive(phase != Phase.Dead || hitFlash.Active);
        if (phase == Phase.Dead)
        {
            effects?.Reset();
            return;
        }
        float progress = Progress;
        bool winding = phase == Phase.Windup;
        bool charging = phase == Phase.Charge;
        bool recovering = phase == Phase.Recovery;
        if (winding && !windupSoundPlayed && progress < 0.4f)
            CombatAudio.GiantWindup(transform.position);
        windupSoundPlayed = winding;
        float blend = Mathf.SmoothStep(0f, 1f, progress);
        float weight = 0f, enterWeight = 0f, exitWeight = 0f;
        float sprintTime = 0f;
        float jumpWeight = 0f;
        if (winding && sprintEnter != null)
        {
            enterWeight = Mathf.Clamp01(progress * 12f);
            enterPose.SetTime(Mathf.Min(sprintEnter.length - 0.0001f, sprintEnter.length * progress));
        }
        else if (winding)
        {
            weight = blend;
            sprintTime = LoopTime(0f) * blend;
        }
        else if (charging)
        {
            weight = 1f;
            sprintTime = LoopTime(Time.time - phaseStarted);
        }
        else if (recovering && sprintExit != null)
        {
            exitWeight = 1f - Mathf.InverseLerp(0.8f, 1f, progress);
            exitPose.SetTime(Mathf.Min(sprintExit.length - 0.0001f, sprintExit.length * progress));
        }
        else if (recovering)
        {
            weight = 1f - blend;
            sprintTime = recoveryPoseTime + 0.18f * (1f - (1f - progress) * (1f - progress)) * animationSpeed;
        }
        else if (phase == Phase.Jump && jumpPose.IsValid())
        {
            jumpWeight = Mathf.Min(Mathf.InverseLerp(0f, 0.07f, progress),
                1f - Mathf.InverseLerp(0.88f, 1f, progress));
            jumpPose.SetTime(Mathf.Min(jumpAnimation.length - 0.0001f, progress * jumpAnimation.length));
        }
        idlePose.SetTime(Mathf.Repeat(Time.time, idleAnimation.length));
        attackPose.SetTime(Mathf.Repeat(Mathf.Max(0f, sprintTime), chargeAnimation.length));
        mixer.SetInputWeight(0, 1f - weight - enterWeight - exitWeight - jumpWeight);
        mixer.SetInputWeight(1, weight);
        mixer.SetInputWeight(2, enterWeight);
        mixer.SetInputWeight(3, exitWeight);
        mixer.SetInputWeight(4, jumpWeight);
        visual.localPosition = visualRestPosition;
        graph.Evaluate(0f);
        float lowest = float.PositiveInfinity;
        if (leftFoot != null)
            lowest = Mathf.Min(lowest, leftFoot.position.y - animator.leftFeetBottomHeight);
        if (rightFoot != null)
            lowest = Mathf.Min(lowest, rightFoot.position.y - animator.rightFeetBottomHeight);
        if (!float.IsInfinity(lowest))
            visual.position += Vector3.up * Mathf.Max(0f, transform.position.y + 0.02f - lowest);
        if (charging)
        {
            int step = Mathf.FloorToInt(Mathf.Max(0f, sprintTime) / chargeAnimation.length * 2f);
            if (step > sprintStep)
            {
                var foot = step % 2 == 0 ? leftFoot : rightFoot;
                CombatAudio.GiantStep(foot != null ? foot.position : transform.position);
                sprintStep = step;
            }
        }
        else
            sprintStep = -1;
        effects?.Update(winding, charging, progress, WindupDuration, direction, Time.deltaTime,
            chest != null ? chest.position : transform.position + Vector3.up * 2f, leftFoot, rightFoot);
    }

    private void UpdateJumpEffects()
    {
        if (jumpEffects == null)
            return;
        bool jumping = phase == Phase.Jump;
        float elapsed = jumping ? Progress * Duration : 0f;
        if (jumping && !jumpSoundPlayed && elapsed >= JumpTakeoff)
        {
            jumpSoundPlayed = true;
            if (elapsed < JumpLanding)
                CombatAudio.GiantJump(transform.position);
        }
        else if (!jumping)
            jumpSoundPlayed = false;
        float warning = Mathf.InverseLerp(JumpTakeoff, JumpLanding, elapsed);
        jumpEffects.ShowWarning(jumping && elapsed >= JumpTakeoff && elapsed < JumpLanding,
            jumpTarget, transform.rotation, JumpSquareSize, warning);
        if (jumpImpact != displayedJumpImpact)
        {
            displayedJumpImpact = jumpImpact;
            jumpEffects.Impact(jumpTarget);
            CombatAudio.GiantLanding(jumpTarget);
        }
        jumpEffects.Update();
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
        if (Health == 0)
            phase = Phase.Dead;
    }

    private void UpdateDamageFeedback()
    {
        uint unseen = hitSequence > displayedHitSequence ? hitSequence - displayedHitSequence : 0;
        if (unseen > 0)
            hitFlash.Pulse();
        for (int i = (int)System.Math.Min(unseen, 8u) - 1; i >= 0; i--)
        {
            byte hit = (byte)(hitHistory >> (i * 8));
            damageFeedback?.ShowHit(transform.position, hit & 63, (LungeMan.CriticalHit)(hit >> 6));
        }
        displayedHitSequence = hitSequence;
        damageFeedback?.Update(transform.position, Health, MaxHealth, true);
    }

    internal State GetState() => new State
    {
        Phase = phase, Position = transform.position, Yaw = transform.eulerAngles.y, Progress = Progress,
        Health = Health, HitSequence = hitSequence, HitHistory = hitHistory,
        JumpTarget = jumpTarget, JumpImpact = jumpImpact
    };

    internal void ApplyState(State state)
    {
        if (!hasRemote)
            transform.position = state.Position;
        if (phase != Phase.Recovery && state.Phase == Phase.Recovery)
            recoveryPoseTime = LoopTime(phase == Phase.Charge ? Mathf.Clamp01(remote.Progress) * ChargeDuration : 0f);
        remote = state;
        hasRemote = true;
        phase = state.Phase;
        Health = Mathf.Clamp(state.Health, 0, MaxHealth);
        hitSequence = state.HitSequence;
        hitHistory = state.HitHistory;
        jumpTarget = state.JumpTarget;
        jumpImpact = state.JumpImpact;
        phaseStarted = Time.time - state.Progress * Duration;
        direction = Quaternion.Euler(0f, state.Yaw, 0f) * Vector3.forward;
    }

    private void OnDisable()
    {
        windupSoundPlayed = false;
        sprintStep = -1;
        jumpSoundPlayed = false;
        jumpEffects?.Reset();
        effects?.Reset();
        damageFeedback?.Hide();
        hitFlash?.Reset();
    }
    private void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
        effects?.Dispose();
        jumpEffects?.Dispose();
        damageFeedback?.Dispose();
    }
    private static Vector3 Planar(Vector3 value) => new Vector3(value.x, 0f, value.z);
}
