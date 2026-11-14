using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

[DefaultExecutionOrder(527)]
public sealed class TrueHealer : CombatEnemy
{
    private const int MaxHealth = 40;
    private const int HealAmount = 8;
    private const float HealRange = 25f;
    private const float HealCooldown = 3f;
    private const float SelfHealCooldown = 7f;
    private const float HealRelease = 0.22f;
    private const float RetreatRange = 7f;
    private const float FormationDistance = 4f;
    private const float MoveSpeed = 5f;
    private const float MoveBudget = 10f;
    private const float MoveCooldown = 3f;
    private static readonly float[] CourseAngles = { 0f, 35f, -35f, 70f, -70f, 90f, -90f };

    [SerializeField] private Animator animator;
    [SerializeField] private AnimationClip idleClip, runClip, healClip;
    [SerializeField] private Material effectMaterial;

    internal enum Phase : byte { Idle, Run, Heal, Dead }
    internal enum HealRecipient : byte { None, LungeMan, Giant, RedNinja, Self }

    internal struct State
    {
        internal Phase Phase;
        internal Vector3 Position;
        internal float Yaw, Elapsed;
        internal int Health;
        internal uint HitSequence, HealSequence;
        internal ulong HitHistory;
        internal HealRecipient Recipient;
        internal byte RestoredHealth;
    }

    private WasdBlockMovement player;
    private GameplayHud hud;
    private CoopSession session;
    private CombatEnemy[] allies;
    private CombatEnemy healTarget;
    private EnemyDamageFeedback feedback;
    private CharacterWhiteFlash flash;
    private HealerEffects effects;
    private Transform leftFoot, rightFoot;
    private Vector3 visualRestPosition;
    private float soleHeight;
    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private AnimationClipPlayable[] poses;
    private AnimationClip[] clips;
    private int currentPose, previousPose;
    private float poseChanged, previousPoseTime;
    private Phase phase;
    private float phaseStarted, nextMove, moveRemaining, nextHeal, nextSelfHeal;
    private bool healReleased, hasRemote;
    private State remote;
    private uint hitSequence, healSequence, shownHit, shownHeal;
    private ulong hitHistory;
    private HealRecipient recipient;
    private byte restoredHealth;

    internal override int Health { get; private protected set; } = MaxHealth;
    internal override int HealthLimit => MaxHealth;
    private bool IsReplica => session != null && session.InMatch && !session.IsHost;
    private float Age => Mathf.Max(0f, Time.time - phaseStarted);

    private void Start()
    {
        if (animator == null || !animator.isHuman || idleClip == null || runClip == null ||
            healClip == null || effectMaterial == null)
        {
            Debug.LogError("TrueHealer requires a humanoid rig, animation clips and a particle material.", this);
            enabled = false;
            return;
        }

        player = FindAnyObjectByType<WasdBlockMovement>();
        hud = FindAnyObjectByType<GameplayHud>();
        session = FindAnyObjectByType<CoopSession>();
        allies = FindObjectsByType<CombatEnemy>();
        if (player != null && player.TryGetGroundHeight(transform.position, out float ground))
        {
            var point = transform.position;
            point.y = ground;
            transform.position = point;
        }
        if (hud != null)
        {
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            float height = head.position.y - transform.position.y + 0.65f;
            feedback = new EnemyDamageFeedback((RectTransform)hud.transform, Camera.main, "TrueHealer", height - 2.38f);
        }
        flash = new CharacterWhiteFlash(animator.transform);
        effects = new HealerEffects(transform, effectMaterial);
        leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        visualRestPosition = animator.transform.localPosition;
        soleHeight = Mathf.Min(leftFoot.position.y, rightFoot.position.y) - transform.position.y;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        graph = PlayableGraph.Create("TrueHealer poses");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        clips = new[] { idleClip, runClip, healClip };
        poses = new AnimationClipPlayable[clips.Length];
        mixer = AnimationMixerPlayable.Create(graph, poses.Length);
        for (int i = 0; i < poses.Length; i++)
        {
            poses[i] = AnimationClipPlayable.Create(graph, clips[i]);
            poses[i].SetApplyFootIK(true);
            poses[i].SetSpeed(0);
            graph.Connect(poses[i], 0, mixer, i);
        }
        mixer.SetInputWeight(0, 1f);
        AnimationPlayableOutput.Create(graph, "TrueHealer", animator).SetSourcePlayable(mixer);
        graph.Play();
        phaseStarted = Time.time;
    }

    private void Update()
    {
        if (!IsReplica)
        {
            Tick(Time.time, Time.deltaTime);
            return;
        }
        if (!hasRemote)
            return;
        float blend = 1f - Mathf.Exp(-24f * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, remote.Position, blend);
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, remote.Yaw, 0f), blend);
    }

    internal void Tick(float now, float dt)
    {
        if (phase == Phase.Dead)
            return;
        if (phase == Phase.Heal)
        {
            AdvanceHeal(now, dt);
            return;
        }

        Vector3 origin = transform.position;
        Vector3 threat = player != null ? player.transform.position : origin;
        float threatDistance = player != null && hud != null && hud.Health > 0
            ? Planar(threat - origin).sqrMagnitude : float.PositiveInfinity;
        session?.FindNearestLivingPeer(origin, ref threat, ref threatDistance);
        bool retreat = threatDistance < RetreatRange * RetreatRange;
        CombatEnemy ally = FindAlly();
        Vector3 toAlly = ally != null ? Planar(ally.transform.position - origin) : Vector3.zero;
        bool canHealAlly = ally != null && ally.NeedsHealing && toAlly.sqrMagnitude <= HealRange * HealRange &&
            now >= nextHeal && (!retreat || now < nextMove);
        bool canHealSelf = NeedsHealing && now >= nextSelfHeal;
        if (canHealSelf || canHealAlly)
        {
            StopMoving(now);
            healTarget = canHealSelf ? this : ally;
            healReleased = false;
            if (healTarget != this)
                Face(toAlly, dt);
            ChangePhase(Phase.Heal, now);
            return;
        }

        Vector3 heading = toAlly.magnitude > FormationDistance ? toAlly.normalized : Vector3.zero;
        float distance = Mathf.Max(0f, toAlly.magnitude - FormationDistance);
        if (retreat)
        {
            Vector3 away = Planar(origin - threat).normalized;
            if (away == Vector3.zero)
                away = transform.forward;
            // Bias the retreat toward allies when that keeps the healer moving away from the player.
            heading = (away + heading * 0.55f).normalized;
            distance = MoveBudget;
        }
        if (heading == Vector3.zero || player == null)
        {
            StopMoving(now);
            return;
        }
        if (phase != Phase.Run)
        {
            if (now < nextMove)
                return;
            moveRemaining = MoveBudget;
            ChangePhase(Phase.Run, now);
        }
        if (!Step(heading, threat, retreat, Mathf.Min(distance, MoveSpeed * dt), dt) || moveRemaining < 0.001f)
            StopMoving(now);
    }

    private CombatEnemy FindAlly()
    {
        CombatEnemy closest = null, wounded = null;
        float closestDistance = float.PositiveInfinity, woundedDistance = float.PositiveInfinity;
        foreach (var ally in allies)
        {
            if (ally == null || ally == this || !ally.isActiveAndEnabled || ally.Health <= 0)
                continue;
            float distance = Planar(ally.transform.position - transform.position).sqrMagnitude;
            if (distance < closestDistance)
            {
                closest = ally;
                closestDistance = distance;
            }
            if (ally.NeedsHealing && distance < woundedDistance)
            {
                wounded = ally;
                woundedDistance = distance;
            }
        }
        return wounded != null ? wounded : closest;
    }

    private bool Step(Vector3 heading, Vector3 threat, bool retreat, float distance, float dt)
    {
        float step = Mathf.Min(moveRemaining, distance);
        var origin = transform.position;
        foreach (float angle in CourseAngles)
        {
            Vector3 course = Quaternion.Euler(0f, angle, 0f) * heading;
            if (retreat && Vector3.Dot(Planar(origin - threat), course) < -0.001f)
                continue;
            Vector3 next = origin + course * step;
            if ((player.ClampToMovementBoundary(next) - next).sqrMagnitude > 0.000001f ||
                !player.TryGetGroundHeight(next, out float ground))
                continue;
            next.y = ground;
            float traveled = Vector3.Distance(origin, next);
            if (traveled > moveRemaining)
            {
                next = Vector3.Lerp(origin, next, moveRemaining / traveled);
                traveled = moveRemaining;
            }
            transform.position = next;
            moveRemaining -= traveled;
            Face(course, dt);
            return true;
        }
        return false;
    }

    private void StopMoving(float now)
    {
        if (phase != Phase.Run)
            return;
        nextMove = now + MoveCooldown;
        ChangePhase(Phase.Idle, now);
    }

    private void AdvanceHeal(float now, float dt)
    {
        if (healTarget != null)
            Face(Planar(healTarget.transform.position - transform.position), dt);
        if (!healReleased && now - phaseStarted >= HealRelease)
        {
            healReleased = true;
            if (healTarget == this)
                nextSelfHeal = now + SelfHealCooldown;
            else
                nextHeal = now + HealCooldown;
            if (healTarget != null && Planar(healTarget.transform.position - transform.position).sqrMagnitude <= HealRange * HealRange)
            {
                int restored = healTarget.RestoreHealth(HealAmount);
                if (restored > 0)
                {
                    recipient = Identify(healTarget);
                    restoredHealth = (byte)restored;
                    healSequence++;
                }
            }
        }
        if (now - phaseStarted >= healClip.length)
            ChangePhase(Phase.Idle, now);
    }

    private void Face(Vector3 direction, float dt)
    {
        if (direction.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 720f * dt);
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
        if (Health == 0)
            ChangePhase(Phase.Dead, Time.time);
    }

    private void LateUpdate()
    {
        if (!graph.IsValid())
            return;
        uint unseen = hitSequence - shownHit;
        if (unseen > 0)
            flash.Pulse();
        for (int i = (int)System.Math.Min(unseen, 8u) - 1; i >= 0; i--)
        {
            byte hit = (byte)(hitHistory >> (i * 8));
            feedback?.ShowHit(transform.position, hit & 63, (LungeMan.CriticalHit)(hit >> 6));
        }
        shownHit = hitSequence;
        flash.Update();
        animator.gameObject.SetActive(phase != Phase.Dead || flash.Active);
        if (phase != Phase.Dead)
            Animate();
        if (shownHeal != healSequence)
        {
            shownHeal = healSequence;
            var target = recipient == HealRecipient.Self ? this
                : !IsReplica ? healTarget : System.Array.Find(allies, ally => ally != null && Identify(ally) == recipient);
            if (target != null)
            {
                float labelHeight = effects.Heal(target.transform);
                feedback?.ShowHeal(target.transform, labelHeight, restoredHealth);
            }
        }
        effects.Update();
        feedback?.Update(transform.position, Health, MaxHealth, true);
    }

    private static HealRecipient Identify(CombatEnemy ally) => ally switch
    {
        LungeMan => HealRecipient.LungeMan,
        GiantChargeAttack => HealRecipient.Giant,
        RedNinja => HealRecipient.RedNinja,
        TrueHealer => HealRecipient.Self,
        _ => HealRecipient.None
    };

    private void Animate()
    {
        int nextPose = phase == Phase.Run ? 1 : phase == Phase.Heal ? 2 : 0;
        if (nextPose != currentPose)
        {
            mixer.SetInputWeight(previousPose, 0f);
            previousPose = currentPose;
            previousPoseTime = (float)poses[previousPose].GetTime();
            currentPose = nextPose;
            poseChanged = Time.time;
        }
        float sinceChange = Time.time - poseChanged;
        float blend = Mathf.Clamp01(sinceChange / 0.12f);
        if (previousPose != currentPose)
        {
            mixer.SetInputWeight(previousPose, 1f - blend);
            poses[previousPose].SetTime(ClipTime(previousPose, previousPoseTime + sinceChange));
        }
        mixer.SetInputWeight(currentPose, previousPose == currentPose ? 1f : blend);
        poses[currentPose].SetTime(ClipTime(currentPose, Age));
        animator.transform.localPosition = visualRestPosition;
        graph.Evaluate(0f);
        float feet = Mathf.Min(leftFoot.position.y, rightFoot.position.y);
        animator.transform.position += Vector3.up * Mathf.Max(0f, transform.position.y + soleHeight - feet);
    }

    private float ClipTime(int pose, float age) => pose == 2
        ? Mathf.Min(age, clips[pose].length - 0.0001f) : Mathf.Repeat(age, clips[pose].length);

    internal State GetState() => new State
    {
        Phase = phase, Position = transform.position, Yaw = transform.eulerAngles.y, Elapsed = Age,
        Health = Health, HitSequence = hitSequence, HitHistory = hitHistory,
        HealSequence = healSequence, Recipient = recipient, RestoredHealth = restoredHealth
    };

    internal void ApplyState(State state)
    {
        if (!hasRemote)
        {
            transform.SetPositionAndRotation(state.Position, Quaternion.Euler(0f, state.Yaw, 0f));
            shownHit = state.HitSequence;
            shownHeal = state.HealSequence;
        }
        remote = state;
        hasRemote = true;
        phase = state.Phase;
        phaseStarted = Time.time - state.Elapsed;
        Health = Mathf.Clamp(state.Health, 0, MaxHealth);
        hitSequence = state.HitSequence;
        hitHistory = state.HitHistory;
        healSequence = state.HealSequence;
        recipient = state.Recipient;
        restoredHealth = state.RestoredHealth;
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
