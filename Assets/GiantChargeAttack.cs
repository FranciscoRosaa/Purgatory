using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

[DefaultExecutionOrder(525)]
public sealed class GiantChargeAttack : MonoBehaviour
{
    [Header("Model and animation")]
    [SerializeField] private Transform visual;
    [SerializeField] private Animator animator;
    [SerializeField] private AnimationClip idleAnimation;
    [SerializeField] private AnimationClip chargeAnimation;
    [SerializeField] private Material effectMaterial;
    [SerializeField] private Collider[] walkableGround;
    [SerializeField] private BoxCollider movementBoundary;
    [Header("Charge")]
    [SerializeField, Min(0.05f)] private float windupDuration = 1.2f;
    [SerializeField, Min(0.05f)] private float chargeDuration = 3f;
    [SerializeField, Min(0f)] private float chargeSpeed = 7f;
    [SerializeField, Min(0f)] private float cooldown = 4f;
    [SerializeField, Min(0f)] private float detectionRange = 18f;
    [SerializeField, Min(0f)] private float initialDelay = 1.5f;
    [SerializeField] private bool automaticAttack = true;
    [Header("Presentation")]
    [SerializeField, Min(0.1f)] private float auraRadius = 2.1f;
    [SerializeField, Range(0f, 0.5f)] private float windupPoseTime = 0.18f;
    [SerializeField, Min(0.05f)] private float animationSpeed = 1f;

    internal enum Phase : byte { Idle, Windup, Charge, Cooldown }
    internal struct State
    {
        internal Phase Phase;
        internal Vector3 Position;
        internal float Yaw, Progress;
    }

    private WasdBlockMovement player;
    private GameplayHud hud;
    private CoopSession session;
    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private AnimationClipPlayable idlePose, attackPose;
    private GiantChargeVfx effects;
    private Transform leftFoot, rightFoot;
    private Vector3 visualRestPosition, direction;
    private float phaseStarted, nextAttack;
    private Phase phase;
    private State remote;
    private bool hasRemote;

    private bool IsReplica => session != null && session.InMatch && !session.IsHost;
    internal float WindupDuration => Mathf.Max(0.05f, windupDuration);
    internal float ChargeDuration => Mathf.Max(0.05f, chargeDuration);
    internal bool IsCharging => phase == Phase.Charge;

    private void Start()
    {
        if (visual == null || animator == null || !animator.isHuman ||
            chargeAnimation == null || idleAnimation == null)
        {
            Debug.LogError("Gigante: assign the humanoid model and charge/idle clips.", this);
            enabled = false;
            return;
        }
        player = FindAnyObjectByType<WasdBlockMovement>();
        hud = FindAnyObjectByType<GameplayHud>();
        session = FindAnyObjectByType<CoopSession>();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        visualRestPosition = visual.localPosition;
        effects = new GiantChargeVfx(transform, effectMaterial, auraRadius);
        graph = PlayableGraph.Create("Gigante charge poses");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        mixer = AnimationMixerPlayable.Create(graph, 2);
        idlePose = AnimationClipPlayable.Create(graph, idleAnimation);
        attackPose = AnimationClipPlayable.Create(graph, chargeAnimation);
        idlePose.SetApplyFootIK(true);
        attackPose.SetApplyFootIK(true);
        graph.Connect(idlePose, 0, mixer, 0);
        graph.Connect(attackPose, 0, mixer, 1);
        mixer.SetInputWeight(0, 1f);
        var output = AnimationPlayableOutput.Create(graph, "Gigante", animator);
        output.SetSourcePlayable(mixer);
        graph.Play();
        nextAttack = Time.time + initialDelay;
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
                    TryBeginAttack(now);
                break;
            case Phase.Windup:
                if (now >= phaseStarted + WindupDuration)
                {
                    phase = Phase.Charge;
                    phaseStarted = now;
                    effects?.BeginCharge(transform.position);
                }
                break;
            case Phase.Charge:
                float remaining = ChargeDuration - (now - phaseStarted - deltaTime);
                AdvanceCharge(Mathf.Min(Mathf.Max(0f, remaining), deltaTime));
                if (now >= phaseStarted + ChargeDuration)
                {
                    phase = Phase.Cooldown;
                    phaseStarted = now;
                    nextAttack = now + cooldown;
                }
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
        Vector3 target = player != null ? player.transform.position : transform.position;
        float nearest = player != null && (hud == null || hud.Health > 0)
            ? Planar(target - transform.position).sqrMagnitude : float.PositiveInfinity;
        session?.FindNearestLivingPeer(transform.position, ref target, ref nearest);
        if (float.IsPositiveInfinity(nearest) || nearest > detectionRange * detectionRange)
            return false;
        direction = Planar(target - transform.position);
        direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
        transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        phase = Phase.Windup;
        phaseStarted = now;
        return true;
    }

    private void AdvanceCharge(float elapsed)
    {
        float distance = Mathf.Max(0f, chargeSpeed) * Mathf.Max(0f, elapsed);
        int steps = Mathf.CeilToInt(distance / 0.2f);
        if (steps == 0)
            return;
        for (int i = 0; i < steps; i++)
        {
            Vector3 next = transform.position + direction * (distance / steps);
            if (movementBoundary != null && movementBoundary.enabled)
            {
                var sample = next;
                sample.y = movementBoundary.bounds.center.y;
                var constrained = movementBoundary.ClosestPoint(sample);
                constrained.y = next.y;
                if ((constrained - next).sqrMagnitude > 0.0001f)
                    break;
            }
            if (!TryGround(next, out float ground))
                break;
            next.y = ground;
            transform.position = next;
        }
        // The heading is captured once, before windup; the player cannot steer it.
        transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
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

    private float Duration => phase == Phase.Windup ? WindupDuration : ChargeDuration;
    private float Progress => phase == Phase.Windup || phase == Phase.Charge
        ? Mathf.Clamp01((Time.time - phaseStarted) / Duration) : 0f;

    private void LateUpdate()
    {
        if (!graph.IsValid())
            return;
        float progress = Progress;
        bool winding = phase == Phase.Windup;
        bool charging = phase == Phase.Charge;
        float blend = winding ? Mathf.SmoothStep(0f, 1f, progress) : charging ? 1f : 0f;
        idlePose.SetTime(Mathf.Repeat(Time.time, idleAnimation.length));
        float poseTime = winding ? chargeAnimation.length * windupPoseTime * blend
            : chargeAnimation.length * windupPoseTime + (Time.time - phaseStarted) * animationSpeed;
        attackPose.SetTime(Mathf.Repeat(Mathf.Max(0f, poseTime), chargeAnimation.length));
        mixer.SetInputWeight(0, 1f - blend);
        mixer.SetInputWeight(1, blend);
        visual.localPosition = visualRestPosition;
        graph.Evaluate(0f);
        float lowest = float.PositiveInfinity;
        if (leftFoot != null)
            lowest = Mathf.Min(lowest, leftFoot.position.y - animator.leftFeetBottomHeight);
        if (rightFoot != null)
            lowest = Mathf.Min(lowest, rightFoot.position.y - animator.rightFeetBottomHeight);
        if (!float.IsInfinity(lowest))
            visual.position += Vector3.up * Mathf.Max(0f, transform.position.y + 0.02f - lowest);
        effects?.Update(winding, charging, progress, direction, Time.deltaTime);
    }

    internal State GetState() => new State
    {
        Phase = phase, Position = transform.position, Yaw = transform.eulerAngles.y, Progress = Progress
    };

    internal void ApplyState(State state)
    {
        if (!hasRemote)
            transform.position = state.Position;
        remote = state;
        hasRemote = true;
        phase = state.Phase;
        phaseStarted = Time.time - state.Progress * Duration;
        direction = Quaternion.Euler(0f, state.Yaw, 0f) * Vector3.forward;
    }

    private void OnDisable() => effects?.Reset();
    private void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
        effects?.Dispose();
    }
    private static Vector3 Planar(Vector3 value) => new Vector3(value.x, 0f, value.z);
}
