using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

[DefaultExecutionOrder(520)]
public sealed class LungeMan : MonoBehaviour
{
    internal const int MaxHealth = 50;
    private const int HitHistorySize = 8;
    private const int DamageMask = 0x3f;
    private const float MoveSpeed = 3.15f;

    [SerializeField] private Transform visual;
    [SerializeField] private Animator animator;
    [SerializeField] private AnimationClip swordIdle;
    [SerializeField] private AnimationClip sprint;
    [SerializeField] private AnimationClip lungeAttack;
    [SerializeField] private Material warningMaterial;
    [SerializeField] private Collider[] walkableGround;
    [SerializeField, Min(1f)] private float distance = 6f;
    [SerializeField, Min(0.1f)] private float width = 2.1f;
    [SerializeField, Min(0.01f)] private float windupDuration = 1.5f;
    [SerializeField, Min(0.01f)] private float lungeDuration = 0.24f;
    [SerializeField, Min(0f)] private float cooldown = 3.5f;
    [SerializeField, Min(0.1f)] private float approachRange = 3.35f;
    [SerializeField, Min(0f)] private float retreatDuration = 0.55f;
    [SerializeField, Range(0f, 1f)] private float criticalChance = 0.15f;

    [Header("Player M1 Damage")]
    [SerializeField, Min(0), FormerlySerializedAs("meleeDamage")] private int firstMeleeDamage = 10;
    [SerializeField, Min(0)] private int secondMeleeDamage = 11;
    [SerializeField, Min(0)] private int thirdMeleeDamage = 12;

    internal enum Phase : byte { Pursuit, Windup, Lunge, Dead, Retreat, Waiting }
    internal enum CriticalHit : byte { None, Medium, High }

    internal struct State
    {
        internal Phase Phase;
        internal Vector3 Position;
        internal float Yaw;
        internal Vector3 Origin;
        internal float Length;
        internal float Progress;
        internal int Health;
        internal uint HitSequence;
        internal ulong HitHistory;
    }

    private GameplayHud hud;
    private WasdBlockMovement player;
    private CoopSession session;
    private LungeWarning warning;
    private EnemyDamageFeedback damageFeedback;
    private CharacterWhiteFlash hitFlash;
    private uint hitSequence, displayedHitSequence;
    private ulong hitHistory;
    private PlayableGraph poseGraph;
    private AnimationMixerPlayable poseMixer;
    private AnimationClipPlayable[] poses;
    private Transform leftFoot;
    private Transform rightFoot;
    private Vector3 visualRestPosition;
    private ParticleSystem lungeWind;
    private Vector3 lastWindPosition;
    private bool wasLunging;
    private Phase phase;
    private Vector3 origin;
    private Vector3 direction;
    private float length;
    private float phaseStarted;
    private float nextAttack;
    private float displayedMotion;
    private Vector3 lastPosition;
    private State remote;
    private bool hasRemote;

    internal int Health { get; private set; } = MaxHealth;
    private bool IsReplica => session != null && session.InMatch && !session.IsHost;

    private void Start()
    {
        player = FindAnyObjectByType<WasdBlockMovement>();
        hud = FindAnyObjectByType<GameplayHud>();
        damageFeedback = new EnemyDamageFeedback((RectTransform)hud.transform, Camera.main);
        hitFlash = new CharacterWhiteFlash(visual);
        session = FindAnyObjectByType<CoopSession>();
        warning = new LungeWarning(transform, warningMaterial);
        lungeWind = DashPresentation.CreateWind(transform, warningMaterial, "Lunge Wind");
        leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        visualRestPosition = visual.localPosition;
        CreatePoseGraph();
        lastPosition = transform.position;
        nextAttack = Time.time + 0.75f;
    }

    private void Update()
    {
        if (IsReplica)
        {
            if (hasRemote)
                UpdateReplica();
            return;
        }

        float now = Time.time;
        switch (phase)
        {
            case Phase.Pursuit:
                Pursue(now);
                break;
            case Phase.Windup:
                if (now >= phaseStarted + windupDuration)
                    Release(now);
                break;
            case Phase.Lunge:
                float progress = Mathf.Clamp01((now - phaseStarted) / lungeDuration);
                MoveAlongLane(progress);
                if (progress >= 1f)
                {
                    phase = Phase.Retreat;
                    phaseStarted = now;
                    nextAttack = now + cooldown;
                }
                break;
            case Phase.Retreat:
                Retreat(now);
                break;
            case Phase.Waiting:
                if (now >= nextAttack)
                {
                    phase = Phase.Pursuit;
                    Pursue(now);
                }
                break;
        }
    }

    private void Pursue(float now)
    {
        if (!TryGetTarget(out var target, out float range))
            return;

        Vector3 toward = range > 0.01f ? Planar(target - transform.position) / range : transform.forward;
        if (range <= Mathf.Min(approachRange, distance * 0.7f))
        {
            if (now < nextAttack || BeginWindup(toward, now))
                return;

            // Back up if the edge of the map leaves too little room for the charge.
            Face(-toward);
            StepOnGround(-toward, Time.deltaTime);
            return;
        }
        Face(toward);
        StepOnGround(toward, Time.deltaTime);
    }

    private void Retreat(float now)
    {
        float remaining = retreatDuration - (now - phaseStarted);
        if (remaining <= 0f || !TryGetTarget(out var target, out float range))
        {
            phase = Phase.Waiting;
            return;
        }

        Vector3 away = range > 0.01f ? Planar(transform.position - target) / range : direction;
        Face(away);
        if (!StepOnGround(away, Mathf.Min(remaining, Time.deltaTime)))
            phase = Phase.Waiting;
    }

    private bool TryGetTarget(out Vector3 target, out float range)
    {
        target = player.transform.position;
        float nearest = hud.Health > 0 ? Planar(target - transform.position).sqrMagnitude : float.PositiveInfinity;
        session?.FindNearestLivingPeer(transform.position, ref target, ref nearest);
        range = Mathf.Sqrt(nearest);
        return !float.IsPositiveInfinity(nearest);
    }

    private void Face(Vector3 facing) =>
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(facing), 720f * Time.deltaTime);

    private bool StepOnGround(Vector3 facing, float stepTime)
    {
        if (!TryGround(transform.position + facing * (MoveSpeed * stepTime), out var ground))
            return false;
        transform.position = ground;
        return true;
    }

    private bool BeginWindup(Vector3 facing, float now)
    {
        origin = transform.position;
        direction = facing;
        length = 0f;
        // Shorten both the warning and the charge if the lane would leave the map.
        int steps = Mathf.CeilToInt(distance / 0.2f);
        for (int i = 1; i <= steps; i++)
        {
            float advance = distance * i / steps;
            if (!TryGround(origin + direction * advance, out _))
                break;
            length = advance;
        }
        if (length < 1f)
            return false;

        phase = Phase.Windup;
        phaseStarted = now;
        transform.rotation = Quaternion.LookRotation(direction);
        return true;
    }

    private void Release(float now)
    {
        phase = Phase.Lunge;
        phaseStarted = now;
        if (hud.Health > 0)
        {
            Vector3 position = player.transform.position;
            bool perfect = player.TryGetDash(out var start, out float age) && age <= WasdBlockMovement.PerfectDodgeWindow
                && IsDashInLane(start, position);
            if (perfect)
                hud.ShowPerfectDodge(player.transform);
            else if (IsInLane(position))
                hud.TakeDamage(RollLungeDamage());
        }
        session?.DamagePeersInLane(this, now);
    }

    internal AttackLane CurrentLane => new AttackLane(origin, direction, length, width);
    internal bool IsInLane(Vector3 point) => CurrentLane.Contains(point);
    internal bool IsDashInLane(Vector3 start, Vector3 end) => CurrentLane.Intersects(start, end);

    internal readonly struct AttackLane
    {
        private readonly Vector3 origin, direction, side;
        private readonly float length, halfWidth;

        internal AttackLane(Vector3 origin, Vector3 direction, float length, float width)
        {
            this.origin = origin;
            this.direction = direction;
            this.length = length;
            side = Vector3.Cross(Vector3.up, direction);
            halfWidth = width * 0.5f;
        }

        internal bool Contains(Vector3 point)
        {
            Vector3 offset = point - origin;
            float forward = Vector3.Dot(offset, direction);
            return forward >= 0f && forward <= length && Mathf.Abs(Vector3.Dot(offset, side)) <= halfWidth;
        }

        internal bool Intersects(Vector3 start, Vector3 end)
        {
            var from = start - origin;
            var to = end - origin;
            float enter = 0f, exit = 1f;
            return ClipSegment(Vector3.Dot(from, direction), Vector3.Dot(to, direction), 0f, length, ref enter, ref exit)
                && ClipSegment(Vector3.Dot(from, side), Vector3.Dot(to, side), -halfWidth, halfWidth, ref enter, ref exit);
        }
    }

    private static bool ClipSegment(float start, float end, float min, float max, ref float enter, ref float exit)
    {
        float delta = end - start;
        if (Mathf.Abs(delta) < 0.00001f)
            return start >= min && start <= max;

        float a = (min - start) / delta;
        float b = (max - start) / delta;
        enter = Mathf.Max(enter, Mathf.Min(a, b));
        exit = Mathf.Min(exit, Mathf.Max(a, b));
        return enter <= exit;
    }

    internal int RollLungeDamage() => Random.value < criticalChance ? 5 : 3;

    private void MoveAlongLane(float progress)
    {
        if (TryGround(origin + direction * (length * progress), out var point))
            transform.position = point;
    }

    private bool TryGround(Vector3 point, out Vector3 grounded)
    {
        var ray = new Ray(point + Vector3.up * 2f, Vector3.down);
        foreach (var ground in walkableGround)
        {
            if (ground != null && ground.Raycast(ray, out var hit, 4f))
            {
                grounded = hit.point;
                return true;
            }
        }
        grounded = point;
        return false;
    }

    internal void ApplyMeleeStrike(int strike)
    {
        int baseDamage = strike switch
        {
            0 => firstMeleeDamage,
            1 => secondMeleeDamage,
            2 => thirdMeleeDamage,
            _ => 0
        };
        ApplyPlayerDamage(baseDamage);
    }

    internal void ApplyAreaStrike(int damage) => ApplyPlayerDamage(damage);

    private void ApplyPlayerDamage(int baseDamage)
    {
        if (Health <= 0 || baseDamage <= 0 || IsReplica)
            return;

        float roll = Random.value;
        var critical = roll < 0.05f ? CriticalHit.High : roll < 0.15f ? CriticalHit.Medium : CriticalHit.None;
        float multiplier = critical == CriticalHit.High ? 1.44f : critical == CriticalHit.Medium ? 1.16f : 1f;
        int damage = Mathf.Min(Health, Mathf.RoundToInt(Mathf.Min(baseDamage, MaxHealth) * multiplier));
        Health -= damage;
        // One byte per confirmed hit: six bits for HP lost, two for the critical tier.
        hitHistory = (hitHistory << 8) | (uint)(damage | (int)critical << 6);
        hitSequence++;
        if (Health == 0)
            phase = Phase.Dead;
    }

    internal State GetState() => new State
    {
        Phase = phase,
        Position = transform.position,
        Yaw = transform.eulerAngles.y,
        Origin = origin,
        Length = length,
        Progress = AttackProgress,
        Health = Health,
        HitSequence = hitSequence,
        HitHistory = hitHistory
    };

    internal void ApplyState(State state)
    {
        if (!hasRemote)
            transform.position = state.Position;
        remote = state;
        hasRemote = true;
        phase = state.Phase;
        Health = state.Health;
        hitSequence = state.HitSequence;
        hitHistory = state.HitHistory;
        origin = state.Origin;
        length = state.Length;
        direction = Quaternion.Euler(0f, state.Yaw, 0f) * Vector3.forward;
        phaseStarted = Time.time - state.Progress * AttackDuration;
    }

    private void UpdateReplica()
    {
        transform.rotation = Quaternion.Euler(0f, remote.Yaw, 0f);
        if (phase == Phase.Lunge)
            MoveAlongLane(AttackProgress);
        else if (phase == Phase.Windup)
            transform.position = remote.Position;
        else
            transform.position = Vector3.Lerp(transform.position, remote.Position, 1f - Mathf.Exp(-20f * Time.deltaTime));
    }

    private float AttackDuration => phase == Phase.Windup ? windupDuration : lungeDuration;
    private float AttackProgress => phase == Phase.Windup || phase == Phase.Lunge
        ? Mathf.Clamp01((Time.time - phaseStarted) / AttackDuration) : 0f;

    private void LateUpdate()
    {
        if (warning == null)
            return;

        bool alive = phase != Phase.Dead;
        bool lunging = phase == Phase.Lunge;
        float progress = AttackProgress;
        warning.Show(phase == Phase.Windup || lunging, origin, direction, width, length, lunging ? 1f : progress);
        UpdateWind(lunging);
        UpdateDamageFeedback();
        hitFlash.Update();
        visual.gameObject.SetActive(alive || hitFlash.Active);
        if (!alive)
            return;

        bool canRun = phase == Phase.Pursuit || phase == Phase.Retreat;
        float movement = canRun && Planar(transform.position - lastPosition).sqrMagnitude > 0.000001f ? 1f : 0f;
        displayedMotion = Mathf.MoveTowards(displayedMotion, movement, Time.deltaTime * 7f);
        SamplePose(progress);
        KeepBootsAboveGround();
        lastPosition = transform.position;
    }

    private void UpdateDamageFeedback()
    {
        uint unseen = hitSequence > displayedHitSequence ? hitSequence - displayedHitSequence : 0;
        if (unseen > 0)
            hitFlash.Pulse();
        int newHits = (int)System.Math.Min(unseen, (uint)HitHistorySize);
        for (int i = newHits - 1; i >= 0; i--)
        {
            byte hit = (byte)(hitHistory >> (i * 8));
            damageFeedback.ShowHit(transform.position, hit & DamageMask, (CriticalHit)(hit >> 6));
        }
        displayedHitSequence = hitSequence;
        damageFeedback.Update(transform.position, Health, MaxHealth, hitSequence > 0);
    }

    private void CreatePoseGraph()
    {
        poseGraph = PlayableGraph.Create("LungeMan poses");
        poseGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var clips = new[] { swordIdle, sprint, lungeAttack };
        poseMixer = AnimationMixerPlayable.Create(poseGraph, clips.Length);
        poses = new AnimationClipPlayable[clips.Length];
        for (int i = 0; i < clips.Length; i++)
        {
            poses[i] = AnimationClipPlayable.Create(poseGraph, clips[i]);
            poses[i].SetApplyFootIK(true);
            poseGraph.Connect(poses[i], 0, poseMixer, i);
        }
        poseMixer.SetInputWeight(0, 1f);
        var output = AnimationPlayableOutput.Create(poseGraph, "LungeMan", animator);
        output.SetSourcePlayable(poseMixer);
        poseGraph.Play();
    }

    private void SamplePose(float progress)
    {
        const int idlePose = 0;
        const int sprintPose = 1;
        const int attackPose = 2;
        bool attacking = phase == Phase.Windup || phase == Phase.Lunge;
        int activePose = attacking ? attackPose : displayedMotion > 0.1f ? sprintPose : idlePose;
        poses[idlePose].SetTime(Mathf.Repeat(Time.time, swordIdle.length));
        poses[sprintPose].SetTime(Mathf.Repeat(Time.time * 0.9f, sprint.length));
        if (attacking)
        {
            // The authored swing reaches contact at about 24% of the source clip.
            float time = phase == Phase.Windup ? Mathf.SmoothStep(0f, 0.24f, progress)
                : Mathf.Lerp(0.24f, 1f, progress);
            poses[attackPose].SetTime(lungeAttack.length * time);
        }
        float blend = 1f - Mathf.Exp(-22f * Time.deltaTime);
        for (int i = 0; i < poses.Length; i++)
            poseMixer.SetInputWeight(i, Mathf.Lerp(poseMixer.GetInputWeight(i), i == activePose ? 1f : 0f, blend));
        poseGraph.Evaluate(0f);
    }

    private void KeepBootsAboveGround()
    {
        float lowest = Mathf.Min(SoleHeight(leftFoot, animator.leftFeetBottomHeight),
            SoleHeight(rightFoot, animator.rightFeetBottomHeight));
        visual.localPosition = visualRestPosition + Vector3.up * Mathf.Max(0f, 0.01f - lowest);
    }

    private float SoleHeight(Transform foot, float depth)
    {
        // Measure the heel and toe of the knight's boot in rig space.
        var toRig = animator.transform.worldToLocalMatrix * foot.localToWorldMatrix;
        float heel = toRig.MultiplyPoint3x4(new Vector3(0f, -depth, -0.095f)).y;
        float toe = toRig.MultiplyPoint3x4(new Vector3(0f, -depth, 0.29f)).y;
        return Mathf.Min(heel, toe);
    }

    private void UpdateWind(bool lunging)
    {
        if (!lunging)
        {
            wasLunging = false;
            return;
        }

        if (!wasLunging)
        {
            wasLunging = true;
            lastWindPosition = origin;
            EmitWind(origin, 16);
        }

        int steps = Mathf.Min(10, Mathf.FloorToInt(Vector3.Dot(transform.position - lastWindPosition, direction) / 0.5f));
        for (int i = 0; i < steps; i++)
        {
            lastWindPosition += direction * 0.5f;
            EmitWind(lastWindPosition, 4);
        }
    }

    private void EmitWind(Vector3 position, int count)
    {
        if (!lungeWind.isPlaying)
            lungeWind.Play();

        var side = Vector3.Cross(Vector3.up, direction);
        for (int i = 0; i < count; i++)
        {
            float lateral = Random.Range(0.25f, width * 0.5f) * (i % 2 == 0 ? 1f : -1f);
            lungeWind.Emit(new ParticleSystem.EmitParams
            {
                position = position + side * lateral + Vector3.up * Random.Range(0.16f, 1.2f),
                velocity = direction * Random.Range(7f, 13f) + side * lateral,
                startSize = Random.Range(0.09f, 0.21f),
                startLifetime = Random.Range(0.12f, 0.29f),
                startColor = i % 3 == 0 ? new Color32(255, 160, 98, 225) : new Color32(255, 235, 217, 225)
            }, 1);
        }
    }

    private void OnDisable()
    {
        warning?.Hide();
        damageFeedback?.Hide();
        hitFlash?.Reset();
        if (lungeWind != null)
            lungeWind.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        wasLunging = false;
    }

    private void OnDestroy()
    {
        if (poseGraph.IsValid())
            poseGraph.Destroy();
        warning?.Dispose();
        damageFeedback?.Dispose();
    }

    private static Vector3 Planar(Vector3 value) => new Vector3(value.x, 0f, value.z);

    private sealed class LungeWarning
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private readonly GameObject root;
        private readonly Mesh mesh;
        private readonly Material material;
        private readonly MeshRenderer background;
        private readonly MeshRenderer fill;
        private readonly MaterialPropertyBlock tint = new MaterialPropertyBlock();

        internal LungeWarning(Transform parent, Material template)
        {
            root = new GameObject("Lunge warning");
            root.transform.SetParent(parent, false);
            mesh = new Mesh { name = "Lunge lane" };
            mesh.vertices = new[] { new Vector3(-0.5f, 0f, 0f), new Vector3(-0.5f, 0f, 1f),
                new Vector3(0.5f, 0f, 1f), new Vector3(0.5f, 0f, 0f) };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            material = new Material(template) { mainTexture = Texture2D.whiteTexture };
            background = CreateStrip("Lane", 0f);
            fill = CreateStrip("Charge fill", 0.009f);
            SetColor(background, new Color(0.92f, 0.13f, 0.19f, 0.27f));
            Hide();
        }

        private MeshRenderer CreateStrip(string name, float lift)
        {
            var strip = new GameObject(name);
            strip.transform.SetParent(root.transform, false);
            strip.transform.localPosition = Vector3.up * lift;
            strip.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = strip.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        internal void Show(bool visible, Vector3 origin, Vector3 direction, float width, float length, float progress)
        {
            root.SetActive(visible);
            if (!visible)
                return;

            root.transform.SetPositionAndRotation(origin + Vector3.up * 0.035f, Quaternion.LookRotation(direction));
            background.transform.localScale = new Vector3(width, 1f, length);
            fill.enabled = progress > 0f;
            fill.transform.localScale = new Vector3(width, 1f, length * progress);
            SetColor(fill, Color.Lerp(new Color(1f, 0.25f, 0.27f, 0.4f), new Color(1f, 0.02f, 0.06f, 0.79f), progress));
        }

        private void SetColor(MeshRenderer renderer, Color color)
        {
            tint.SetColor(BaseColor, color);
            renderer.SetPropertyBlock(tint);
        }

        internal void Hide() => root.SetActive(false);

        internal void Dispose()
        {
            if (root != null)
                Object.Destroy(root);
            Object.Destroy(mesh);
            Object.Destroy(material);
        }
    }
}
