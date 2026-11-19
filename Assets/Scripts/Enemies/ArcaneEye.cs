using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(530)]
public sealed class ArcaneEye : CombatEnemy
{
    internal const int MaximumHealth = 150;
    internal const float ChargeDuration = 2f;
    internal const float WarningDuration = 0.7f;
    internal const float BeamDuration = 3f;
    internal const float BeamLength = 32f;
    internal const float BeamWidth = 1.3f;
    internal const float FallWarning = 0.65f;
    internal const float FallInterval = 0.12f;
    internal const float FallDuration = 0.18f;
    internal const float FallWidth = 2f;
    private const float AttackRange = 13f;
    private const float InternalCooldown = 1.5f;
    private const float HoverHeight = 0.9f;
    [SerializeField] private Transform visual;
    [SerializeField] private Material effectMaterial;

    internal enum Phase : byte { Wander, Follow, Charge, Warning, Beam, Falling, Cooldown, Dead }
    internal enum Attack : byte { Laser, Scattered, Chain }
    internal struct State
    {
        internal Phase Phase;
        internal Vector3 Position, Origin, Direction;
        internal float Yaw, Elapsed;
        internal int Health, Seed;
        internal Attack Attack;
        internal uint HitSequence, AttackSequence;
        internal ulong HitHistory;
    }
    private sealed class Contact
    {
        internal float BeamTime;
        internal uint Falls;
        internal bool BeamDodge;
    }
    private readonly Dictionary<byte, Contact> contacts = new Dictionary<byte, Contact>();
    private readonly Vector3[] fallPoints = new Vector3[12];
    private WasdBlockMovement player;
    private GameplayHud hud;
    private CoopSession session;
    private EnemyDamageFeedback feedback;
    private CharacterWhiteFlash flash;
    private ArcaneEyeVfx effects;
    private Phase phase;
    private Attack attack;
    private Vector3 origin, direction = Vector3.forward, wanderDirection, visualRest, lastLaser;
    private Quaternion visualRestRotation;
    private float phaseStarted, wanderDuration;
    private uint hitSequence, shownHits, attackSequence;
    private ulong hitHistory;
    private int seed, attackCycle;
    private bool hasRemote;
    private State remote;
    internal override int Health { get; private protected set; } = MaximumHealth;
    internal override int HealthLimit => MaximumHealth;
    private bool IsReplica => session != null && session.InMatch && !session.IsHost;
    private float Age => Mathf.Max(0f, Time.time - phaseStarted);
    internal int FallCount => attack == Attack.Scattered ? 7 : 12;
    internal Vector3 FallPoint(int index) => fallPoints[index];

    private void Start()
    {
        if (visual == null || effectMaterial == null)
        {
            Debug.LogError("Olho Arcano needs its visual and effect material.", this);
            enabled = false;
            return;
        }
        player = FindAnyObjectByType<WasdBlockMovement>();
        hud = FindAnyObjectByType<GameplayHud>();
        session = FindAnyObjectByType<CoopSession>();
        visualRest = visual.localPosition + Vector3.up * HoverHeight;
        visualRestRotation = visual.localRotation;
        var bounds = visual.GetComponentInChildren<Renderer>().bounds;
        if (hud != null)
            feedback = new EnemyDamageFeedback((RectTransform)hud.transform, Camera.main,
                "Arcane Eye", bounds.max.y - transform.position.y + HoverHeight + 0.45f - 2.38f);
        flash = new CharacterWhiteFlash(visual);
        effects = new ArcaneEyeVfx(transform, effectMaterial);
        BeginWander(Time.time);
    }

    private void Update()
    {
        if (IsReplica)
        {
            if (!hasRemote) return;
            float blend = 1f - Mathf.Exp(-22f * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, remote.Position, blend);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, remote.Yaw, 0f), blend);
            return;
        }
        Tick(Time.time, Time.deltaTime);
    }

    internal void Tick(float now, float dt)
    {
        float age = Mathf.Max(0f, now - phaseStarted);
        switch (phase)
        {
            case Phase.Wander:
                Move(wanderDirection, 2.2f, dt);
                if (age >= wanderDuration) Change(Phase.Follow, now);
                break;
            case Phase.Follow:
                if (!FindTarget(out var target, out float distance))
                {
                    BeginWander(now);
                    break;
                }
                var offset = Flat(target - transform.position);
                Face(offset, dt);
                if (distance > AttackRange * AttackRange)
                    Move(offset.normalized, 3.2f, dt);
                else
                {
                    // Alternate the uncharged scatter, charged beam and charged column chain.
                    attack = (attackCycle++ % 3) switch { 0 => Attack.Scattered, 1 => Attack.Laser, _ => Attack.Chain };
                    origin = transform.position;
                    direction = offset.sqrMagnitude > 0.001f ? offset.normalized : transform.forward;
                    contacts.Clear();
                    seed = Random.Range(1, int.MaxValue);
                    attackSequence++;
                    if (attack == Attack.Scattered)
                    {
                        BuildFallPoints();
                        Change(Phase.Falling, now);
                    }
                    else Change(Phase.Charge, now);
                }
                break;
            case Phase.Charge:
                if (FindTarget(out target, out _))
                {
                    direction = Flat(target - origin).normalized;
                    if (direction.sqrMagnitude < 0.001f) direction = transform.forward;
                    Face(direction, dt);
                }
                if (age >= ChargeDuration)
                {
                    if (attack == Attack.Chain && Flat(lastLaser - origin).sqrMagnitude > 0.1f)
                        direction = Flat(lastLaser - origin).normalized;
                    BuildFallPoints();
                    transform.rotation = Quaternion.LookRotation(direction);
                    Change(Phase.Warning, now);
                }
                break;
            case Phase.Warning:
                if (age >= WarningDuration)
                    Change(attack == Attack.Laser ? Phase.Beam : Phase.Falling, now);
                break;
            case Phase.Beam:
            case Phase.Falling:
                ResolveLocal(age, dt);
                session?.ResolvePeersInArcaneAttack(this, age, dt);
                float duration = phase == Phase.Beam ? BeamDuration : FallWarning + (FallCount - 1) * FallInterval + FallDuration;
                if (age >= duration) Change(Phase.Cooldown, now);
                break;
            case Phase.Cooldown:
                if (age >= InternalCooldown) BeginWander(now);
                break;
        }
    }

    private void BeginWander(float now)
    {
        float angle = Random.Range(0f, Mathf.PI * 2f);
        wanderDirection = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
        wanderDuration = Random.Range(1f, 1.5f);
        Change(Phase.Wander, now);
    }

    private bool FindTarget(out Vector3 target, out float distance)
    {
        target = player != null ? player.transform.position : transform.position;
        distance = player != null && hud != null && hud.Health > 0
            ? Flat(target - transform.position).sqrMagnitude : float.PositiveInfinity;
        session?.FindNearestLivingPeer(transform.position, ref target, ref distance);
        return !float.IsInfinity(distance);
    }

    private void Move(Vector3 heading, float speed, float dt)
    {
        Vector3 next = transform.position + heading * (speed * dt);
        if (player != null)
        {
            next = player.ClampToMovementBoundary(next, 0.9f);
            if (!player.TryGetGroundHeight(next, out float height)) return;
            next.y = height;
        }
        if (LibraryWorld.Active != null && LibraryWorld.Active.BlocksMovement(next, 0.65f)) return;
        transform.position = next;
        Face(heading, dt);
    }

    private void Face(Vector3 heading, float dt)
    {
        if (heading.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(heading), 220f * dt);
    }

    private void BuildFallPoints()
    {
        var random = new System.Random(seed);
        Vector3 nearest = origin;
        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < FallCount; i++)
        {
            Vector3 point;
            if (attack == Attack.Scattered)
            {
                float angle = (i + (float)random.NextDouble() * 0.6f) * Mathf.PI * 2f / 7f;
                float radius = 3f + (float)random.NextDouble() * 9f;
                point = origin + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
            }
            else point = origin + direction * (2.5f + i * 2.35f);
            if (player != null)
            {
                point = player.ClampToMovementBoundary(point, FallWidth * 0.5f);
                if (player.TryGetGroundHeight(point, out float ground)) point.y = ground;
            }
            fallPoints[i] = point;
            float distance = Flat(point - origin).sqrMagnitude;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = point;
            }
        }
        if (attack == Attack.Scattered) lastLaser = nearest;
    }

    private void ResolveLocal(float age, float dt)
    {
        if (player == null || hud == null || hud.Health <= 0) return;
        int damage = ResolveContact(0, player.transform.position, player.IsDodging, age, dt, out bool dodge);
        if (dodge) hud.ShowPerfectDodge(player.transform);
        if (damage > 0) hud.TakeDamage(damage);
    }

    internal int ResolveContact(byte id, Vector3 feet, bool dodging, float age, float dt, out bool dodge)
    {
        dodge = false;
        if (!contacts.TryGetValue(id, out var contact))
        {
            contact = new Contact();
            contacts.Add(id, contact);
        }
        if (phase == Phase.Beam)
        {
            if (age - Mathf.Max(0f, dt) >= BeamDuration || !BeamContains(feet)) return 0;
            if (dodging)
            {
                dodge = !contact.BeamDodge;
                contact.BeamDodge = true;
                return 0;
            }
            float activeDt = Mathf.Clamp(age, 0f, BeamDuration) -
                Mathf.Clamp(age - Mathf.Max(0f, dt), 0f, BeamDuration);
            contact.BeamTime += activeDt;
            int ticks = Mathf.FloorToInt((contact.BeamTime + 0.00001f) / 1f);
            contact.BeamTime -= ticks;
            return ticks * 2;
        }
        if (phase != Phase.Falling) return 0;
        int damage = 0;
        for (int i = 0; i < FallCount; i++)
        {
            float release = FallWarning + i * FallInterval;
            if (age < release || age - Mathf.Max(0f, dt) > release + FallDuration ||
                (contact.Falls & (1u << i)) != 0 || !FallContains(fallPoints[i], feet)) continue;
            contact.Falls |= 1u << i;
            if (dodging) dodge = true;
            else damage += 2;
        }
        return damage;
    }

    internal bool BeamContains(Vector3 feet)
    {
        Vector3 offset = Flat(feet - origin);
        float along = Vector3.Dot(offset, direction);
        return Mathf.Abs(feet.y - origin.y) <= 3f && along >= 0f && along <= BeamLength &&
            Mathf.Abs(Vector3.Dot(offset, Vector3.Cross(Vector3.up, direction))) <= BeamWidth * 0.5f + 0.35f;
    }

    private static bool FallContains(Vector3 center, Vector3 feet)
    {
        Vector3 offset = feet - center;
        return Mathf.Abs(offset.y) <= 3f && Mathf.Abs(offset.x) <= FallWidth * 0.5f + 0.3f &&
            Mathf.Abs(offset.z) <= FallWidth * 0.5f + 0.3f;
    }

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
        if (Health <= 0) Change(Phase.Dead, Time.time);
    }

    private void LateUpdate()
    {
        if (effects == null) return;
        uint unseen = hitSequence - shownHits;
        if (unseen > 0) flash.Pulse();
        for (int i = (int)System.Math.Min(unseen, 8u) - 1; i >= 0; i--)
        {
            byte hit = (byte)(hitHistory >> (i * 8));
            feedback?.ShowHit(transform.position, hit & 63, (LungeMan.CriticalHit)(hit >> 6));
        }
        shownHits = hitSequence;
        flash.Update();
        feedback?.Update(transform.position, Health, MaximumHealth, true);
        visual.gameObject.SetActive(Health > 0 || flash.Active);
        float charge = phase == Phase.Charge ? Mathf.Clamp01(Age / ChargeDuration) : 0f;
        visual.localPosition = visualRest + Vector3.up * (Mathf.Sin(Time.time * 2.5f) * 0.12f + charge * 0.16f);
        visual.localRotation = Quaternion.Euler(-charge * 14f, 0f, 0f) * visualRestRotation;
        effects.Update(phase, attack, origin, direction, Age, charge, fallPoints, FallCount, transform.position);
    }

    private void Change(Phase next, float now) { phase = next; phaseStarted = now; }
    internal State GetState() => new State
    {
        Phase = phase, Attack = attack, Position = transform.position, Yaw = transform.eulerAngles.y, Elapsed = Age,
        Health = Health, Origin = origin, Direction = direction, Seed = seed,
        HitSequence = hitSequence, HitHistory = hitHistory, AttackSequence = attackSequence
    };
    internal void ApplyState(State state)
    {
        if (!hasRemote) transform.SetPositionAndRotation(state.Position, Quaternion.Euler(0f, state.Yaw, 0f));
        bool rebuild = !hasRemote || state.AttackSequence != attackSequence;
        remote = state;
        hasRemote = true;
        phase = state.Phase;
        attack = state.Attack;
        phaseStarted = Time.time - state.Elapsed;
        Health = state.Health;
        origin = state.Origin;
        direction = state.Direction;
        hitSequence = state.HitSequence;
        hitHistory = state.HitHistory;
        attackSequence = state.AttackSequence;
        seed = state.Seed;
        if (rebuild || phase == Phase.Warning) BuildFallPoints();
    }
    private void OnDisable() { effects?.Hide(); feedback?.Hide(); }
    private void OnDestroy() { effects?.Dispose(); feedback?.Dispose(); }
    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
