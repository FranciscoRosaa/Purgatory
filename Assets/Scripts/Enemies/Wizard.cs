using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

[DefaultExecutionOrder(531)]
public sealed class Wizard : CombatEnemy
{
    internal const int MaximumHealth = 85;
    internal const int OrbDamage = 4;
    internal const float ChargeDuration = 2.25f;
    internal const float FadeDuration = 0.45f;
    private const float ShortIdleDuration = 0.7f;
    private const float LongIdleDuration = 2f;
    private const float AttackDuration = 0.55f;
    private const float OrbSpeed = 6.5f;
    private const float OrbLifetime = 12f;
    private const float OrbRadius = 0.45f;
    private const float ContactRadius = OrbRadius + 0.35f;
    private const float TargetHeight = 1.2f;
    private const float TeleportRadius = 9f;

    [SerializeField] private Animator animator;
    [SerializeField] private Transform staffTip;
    [SerializeField] private AnimationClip idleClip, chargeClip, aimClip, shootClip;
    [SerializeField] private Material effectMaterial;

    internal enum Phase : byte { Charge, Attack, LongIdle, TeleportOut, TeleportIn, ShortIdle, Dead }
    internal enum OrbStatus : byte { None, Flying, Impact }
    internal struct OrbState
    {
        internal OrbStatus Status;
        internal Vector3 Position, Direction;
        internal float Age;
        internal byte Target;
        internal uint Id;
    }
    internal struct State
    {
        internal Phase Phase;
        internal Vector3 Position, Destination;
        internal float Yaw, Elapsed;
        internal int Health;
        internal uint HitSequence;
        internal ulong HitHistory;
        internal bool ChargeAfterTeleport;
        internal OrbState First, Second;
    }

    // Two slots cover the 12-second lifetime at the minimum 7.3-second casting interval.
    private readonly OrbState[] orbs = new OrbState[2];
    private WasdBlockMovement player;
    private GameplayHud hud;
    private CoopSession session;
    private EnemyDamageFeedback feedback;
    private WizardVfx effects;
    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private AnimationClip[] clips;
    private AnimationClipPlayable[] poses;
    private Transform leftFoot, rightFoot, hips, staffArm, staffHand;
    private Vector3 visualRest, hipRest, destination;
    private Quaternion visualRotation;
    private Phase phase = Phase.Charge;
    private float phaseStarted, poseChanged, previousPoseTime, whiteUntil;
    private int currentPose, previousPose;
    private bool chargeAfterTeleport, hasRemote;
    private uint hitSequence, shownHit, shotSequence;
    private ulong hitHistory;
    private State remote;
    internal override int Health { get; private protected set; } = MaximumHealth;
    internal override int HealthLimit => MaximumHealth;
    private bool IsReplica => session != null && session.InMatch && !session.IsHost;
    private float Age => Mathf.Max(0f, Time.time - phaseStarted);
    private float Opacity => phase switch
    {
        Phase.TeleportOut => 1f - Mathf.Clamp01(Age / FadeDuration),
        Phase.TeleportIn => Mathf.Clamp01(Age / FadeDuration),
        Phase.Dead => 0f,
        _ => 1f
    };

    private void Start()
    {
        if (animator == null || !animator.isHuman || staffTip == null ||
            idleClip == null || chargeClip == null || aimClip == null || shootClip == null || effectMaterial == null)
        {
            Debug.LogError("Wizard needs its humanoid rig, staff tip, four clips and effect material.", this);
            enabled = false;
            return;
        }
        player = FindAnyObjectByType<WasdBlockMovement>();
        hud = FindAnyObjectByType<GameplayHud>();
        session = FindAnyObjectByType<CoopSession>();
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        if (hud != null)
            feedback = new EnemyDamageFeedback((RectTransform)hud.transform, Camera.main, "Wizard",
                head.position.y - transform.position.y + 0.6f - 2.38f);
        leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        staffArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        staffHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        visualRest = animator.transform.localPosition;
        visualRotation = animator.transform.localRotation;
        hipRest = hips.localPosition;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        clips = new[] { idleClip, chargeClip, aimClip, shootClip };
        graph = PlayableGraph.Create("Wizard spell poses");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        mixer = AnimationMixerPlayable.Create(graph, clips.Length);
        poses = new AnimationClipPlayable[clips.Length];
        for (int i = 0; i < clips.Length; i++)
        {
            poses[i] = AnimationClipPlayable.Create(graph, clips[i]);
            poses[i].SetSpeed(0);
            poses[i].SetApplyFootIK(false);
            graph.Connect(poses[i], 0, mixer, i);
        }
        AnimationPlayableOutput.Create(graph, "Wizard", animator).SetSourcePlayable(mixer);
        graph.Play();
        effects = new WizardVfx(animator.transform, effectMaterial);
        if (!hasRemote) phaseStarted = Time.time;
    }

    private void Update()
    {
        if (!IsReplica)
        {
            Tick(Time.time, Time.deltaTime);
            return;
        }
        if (!hasRemote) return;
        if (phase == Phase.TeleportIn || Vector3.Distance(transform.position, remote.Position) > 3f)
            transform.position = remote.Position;
        else
            transform.position = Vector3.Lerp(transform.position, remote.Position, 1f - Mathf.Exp(-24f * Time.deltaTime));
        transform.rotation = Quaternion.Euler(0f, remote.Yaw, 0f);
        for (int i = 0; i < orbs.Length; i++)
            if (orbs[i].Status == OrbStatus.Flying)
                orbs[i].Position += orbs[i].Direction * (OrbSpeed * Time.deltaTime);
    }

    internal void Tick(float now, float dt)
    {
        for (int i = 0; i < orbs.Length; i++)
            if (orbs[i].Status == OrbStatus.Flying) AdvanceOrb(i, dt);
        float age = Mathf.Max(0f, now - phaseStarted);
        switch (phase)
        {
            case Phase.Charge:
                if (FindTarget(out var target, out _)) Face(target, dt);
                if (age >= ChargeDuration)
                {
                    if (FindTarget(out target, out byte id)) ReleaseOrb(id, target);
                    Change(Phase.Attack, now);
                }
                break;
            case Phase.Attack:
                if (age >= AttackDuration) Change(Phase.LongIdle, now);
                break;
            case Phase.LongIdle:
                if (age >= LongIdleDuration) BeginTeleport(now, false);
                break;
            case Phase.ShortIdle:
                if (age >= ShortIdleDuration) BeginTeleport(now, true);
                break;
            case Phase.TeleportOut:
                if (age >= FadeDuration)
                {
                    transform.position = destination;
                    Change(Phase.TeleportIn, now);
                }
                break;
            case Phase.TeleportIn:
                if (age >= FadeDuration) Change(chargeAfterTeleport ? Phase.Charge : Phase.ShortIdle, now);
                break;
        }
    }

    private bool FindTarget(out Vector3 point, out byte id)
    {
        point = player != null ? player.transform.position : transform.position;
        float nearest = player != null && hud != null && hud.Health > 0
            ? Flat(point - transform.position).sqrMagnitude : float.PositiveInfinity;
        id = session != null ? session.FindNearestLivingPeer(transform.position, ref point, ref nearest) : (byte)0;
        return !float.IsInfinity(nearest);
    }

    private bool TargetPosition(byte id, out Vector3 point)
    {
        if (id == 0)
        {
            point = player != null ? player.transform.position + Vector3.up * TargetHeight : default;
            return player != null && hud != null && hud.Health > 0;
        }
        if (session != null && session.TryGetLivingPeerPosition(id, out point))
        {
            if (player != null && player.TryGetGroundHeight(point, out float y)) point.y = y;
            else point.y = transform.position.y;
            point += Vector3.up * TargetHeight;
            return true;
        }
        point = default;
        return false;
    }

    private void ReleaseOrb(byte target, Vector3 point)
    {
        int slot = (int)(shotSequence % 2u);
        shotSequence++;
        Vector3 start = staffTip.position;
        if (player != null) start = player.ClampToMovementBoundary(start, OrbRadius + 0.05f);
        point.y = start.y;
        Vector3 direction = (point - start).normalized;
        if (direction.sqrMagnitude < 0.001f) direction = transform.forward;
        orbs[slot] = new OrbState
        {
            Status = OrbStatus.Flying, Position = start, Direction = direction, Target = target, Id = shotSequence
        };
    }

    private void AdvanceOrb(int index, float dt)
    {
        var orb = orbs[index];
        orb.Age = Mathf.Min(OrbLifetime, orb.Age + Mathf.Max(0f, dt));
        if (orb.Age >= OrbLifetime || !TargetPosition(orb.Target, out var target))
        {
            orb.Status = OrbStatus.None;
            orbs[index] = orb;
            return;
        }
        Vector3 aim = (target - orb.Position).normalized;
        orb.Direction = Vector3.RotateTowards(orb.Direction, aim, 3.6f * dt, 0f).normalized;
        var end = orb.Position + orb.Direction * (OrbSpeed * dt);
        bool boundary = false;
        if (player != null)
        {
            var clamped = player.ClampToMovementBoundary(end, OrbRadius);
            boundary = (clamped - end).sqrMagnitude > 0.000001f;
            end = clamped;
        }
        float contact = player != null && hud != null && hud.Health > 0
            ? ContactFraction(orb.Position, end, player.transform.position) : float.PositiveInfinity;
        if (session != null && session.TryResolveWizardOrbContact(orb.Position, end, contact, out float peerContact))
        {
            orb.Position = Vector3.Lerp(orb.Position, end, peerContact);
            orb.Status = OrbStatus.Impact;
        }
        else if (contact <= 1f)
        {
            if (player.IsDodging) hud.ShowPerfectDodge(player.transform);
            else hud.TakeDamage(OrbDamage);
            orb.Position = Vector3.Lerp(orb.Position, end, contact);
            orb.Status = OrbStatus.Impact;
        }
        else
        {
            orb.Position = end;
            if (boundary) orb.Status = OrbStatus.Impact;
        }
        orbs[index] = orb;
    }

    internal static float ContactFraction(Vector3 start, Vector3 end, Vector3 feet)
    {
        Vector3 segment = end - start, offset = feet + Vector3.up * TargetHeight - start;
        float separation = offset.sqrMagnitude - ContactRadius * ContactRadius;
        if (separation <= 0f) return 0f;
        float length = segment.sqrMagnitude, projection = Vector3.Dot(offset, segment);
        float discriminant = projection * projection - length * separation;
        if (length <= 0f || projection <= 0f || discriminant < 0f) return float.PositiveInfinity;
        float t = (projection - Mathf.Sqrt(discriminant)) / length;
        return t <= 1f ? t : float.PositiveInfinity;
    }

    private void BeginTeleport(float now, bool chargeNext)
    {
        chargeAfterTeleport = chargeNext;
        destination = transform.position;
        for (int i = 0; i < 32; i++)
        {
            Vector2 offset = Random.insideUnitCircle * TeleportRadius;
            if (offset.sqrMagnitude < 9f) continue;
            var candidate = transform.position + new Vector3(offset.x, 0f, offset.y);
            if (player != null)
            {
                if ((player.ClampToMovementBoundary(candidate, 0.8f) - candidate).sqrMagnitude > 0.000001f ||
                    !player.TryGetGroundHeight(candidate, out float height)) continue;
                candidate.y = height;
                if (Vector3.Distance(candidate, player.transform.position) < 2.5f) continue;
            }
            if (LibraryWorld.Active != null && LibraryWorld.Active.BlocksMovement(candidate, 0.7f)) continue;
            destination = candidate;
            break;
        }
        Change(Phase.TeleportOut, now);
    }

    private void Face(Vector3 target, float dt)
    {
        var offset = Flat(target - transform.position);
        if (offset.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(offset), 360f * dt);
    }

    private void Change(Phase next, float now) { phase = next; phaseStarted = now; }
    internal override void ApplyMeleeStrike(int strike)
    {
        if (strike >= 0 && strike < PlayerMeleeCombo.StrikeCount) ApplyAreaStrike(10 + strike);
    }
    internal override void ApplyAreaStrike(int damage)
    {
        if (Health <= 0 || damage <= 0 || IsReplica) return;
        Health -= RollPlayerDamage(damage, Health, out byte hit);
        hitHistory = (hitHistory << 8) | hit;
        hitSequence++;
        if (Health == 0)
        {
            Change(Phase.Dead, Time.time);
            for (int i = 0; i < orbs.Length; i++) orbs[i].Status = OrbStatus.None;
        }
    }

    private void LateUpdate()
    {
        if (!graph.IsValid()) return;
        uint unseen = hitSequence - shownHit;
        if (unseen > 0) whiteUntil = Time.time + 0.09f;
        for (int i = (int)System.Math.Min(unseen, 8u) - 1; i >= 0; i--)
        {
            byte hit = (byte)(hitHistory >> (i * 8));
            feedback?.ShowHit(transform.position, hit & 63, (LungeMan.CriticalHit)(hit >> 6));
        }
        shownHit = hitSequence;
        feedback?.Update(transform.position, Health, MaximumHealth, true);
        if (Opacity < 0.05f) feedback?.Hide();
        Animate();
        effects.Update(Opacity, Time.time < whiteUntil, Health > 0,
            phase == Phase.Charge ? Mathf.Clamp01(Age / ChargeDuration) : 0f, staffTip.position, orbs);
    }

    private void Animate()
    {
        int next = phase == Phase.Charge ? (Age < chargeClip.length ? 1 : 2) : phase == Phase.Attack ? 3 : 0;
        if (next != currentPose)
        {
            mixer.SetInputWeight(previousPose, 0f);
            previousPose = currentPose;
            previousPoseTime = (float)poses[previousPose].GetTime();
            currentPose = next;
            poseChanged = Time.time;
        }
        float blend = Mathf.Clamp01((Time.time - poseChanged) / 0.1f);
        if (previousPose != currentPose)
        {
            mixer.SetInputWeight(previousPose, 1f - blend);
            poses[previousPose].SetTime(Mathf.Min(previousPoseTime, clips[previousPose].length - 0.0001f));
        }
        mixer.SetInputWeight(currentPose, currentPose == previousPose ? 1f : blend);
        float age = currentPose == 2 ? Mathf.Max(0f, Age - chargeClip.length) : Age;
        poses[currentPose].SetTime(currentPose is 0 or 2 ? Mathf.Repeat(age, clips[currentPose].length)
            : Mathf.Min(age, clips[currentPose].length - 0.0001f));
        animator.transform.SetLocalPositionAndRotation(visualRest, visualRotation);
        graph.Evaluate(0f);
        var hip = hips.localPosition;
        hip.x = hipRest.x; hip.z = hipRest.z;
        hips.localPosition = hip;
        float lift = phase == Phase.Charge ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Age / 0.6f))
            : phase == Phase.Attack ? 1f - Mathf.Clamp01(Age / AttackDuration) : 0f;
        if (lift > 0f)
        {
            Vector3 armDirection = staffHand.position - staffArm.position;
            Vector3 raised = transform.right * 0.45f + Vector3.up * 0.3f + transform.forward * 0.18f;
            staffArm.rotation = Quaternion.Slerp(Quaternion.identity,
                Quaternion.FromToRotation(armDirection, raised), lift) * staffArm.rotation;
            Vector3 staffDirection = staffTip.position - staffHand.position;
            staffHand.rotation = Quaternion.Slerp(Quaternion.identity,
                Quaternion.FromToRotation(staffDirection, Vector3.up), lift) * staffHand.rotation;
        }
        float feet = Mathf.Min(leftFoot.position.y, rightFoot.position.y);
        animator.transform.position += Vector3.up * Mathf.Max(0f, transform.position.y + 0.03f - feet);
    }

    internal State GetState() => new State
    {
        Phase = phase, Position = transform.position, Yaw = transform.eulerAngles.y, Elapsed = Age,
        Destination = destination, ChargeAfterTeleport = chargeAfterTeleport, Health = Health,
        HitSequence = hitSequence, HitHistory = hitHistory, First = orbs[0], Second = orbs[1]
    };
    internal void ApplyState(State state)
    {
        if (!hasRemote || state.Phase == Phase.TeleportIn)
            transform.SetPositionAndRotation(state.Position, Quaternion.Euler(0f, state.Yaw, 0f));
        remote = state;
        hasRemote = true;
        phase = state.Phase;
        phaseStarted = Time.time - state.Elapsed;
        Health = state.Health;
        destination = state.Destination;
        chargeAfterTeleport = state.ChargeAfterTeleport;
        hitSequence = state.HitSequence;
        hitHistory = state.HitHistory;
        orbs[0] = state.First; orbs[1] = state.Second;
    }
    private void OnDisable() { effects?.Hide(); feedback?.Hide(); }
    private void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
        effects?.Dispose();
        feedback?.Dispose();
    }
    private static Vector3 Flat(Vector3 value) => new Vector3(value.x, 0f, value.z);
}
