using UnityEngine;
using UnityEngine.Rendering;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DefaultExecutionOrder(550)]
[RequireComponent(typeof(WasdBlockMovement))]
public sealed class PlayerMeleeCombo : MonoBehaviour
{
    internal const int StrikeCount = 3;
    internal const float DefaultAttackCooldown = 1.25f;

    private static readonly int[] StrikeStates =
    {
        Animator.StringToHash("Base Layer.Sword_Light_A"),
        Animator.StringToHash("Base Layer.Sword_Light_B"),
        Animator.StringToHash("Base Layer.Sword_Light_C")
    };

    private static readonly int[] RecoveryStates =
    {
        Animator.StringToHash("Base Layer.Sword_Light_A_Rec"),
        Animator.StringToHash("Base Layer.Sword_Light_B_Rec"),
        Animator.StringToHash("Base Layer.Sword_Light_C_Rec")
    };
    private static readonly int AreaAttackState = Animator.StringToHash("Base Layer.M2_Area");
    private static readonly int AreaAnimationSpeed = Animator.StringToHash("M2AnimationSpeed");

    [System.Serializable]
    private struct SwingWindow
    {
        [Range(0f, 1f)] public float Start;
        [Range(0f, 1f)] public float Impact;
        [Range(0f, 1f)] public float End;

        internal SwingWindow(float start, float impact, float end)
        {
            Start = start;
            Impact = impact;
            End = end;
        }
    }

    [SerializeField, Min(0f), Tooltip("Seconds between the starts of consecutive M1 attacks.")]
    private float attackCooldown = DefaultAttackCooldown;
    [Header("M1 - single target around the player")]
    [SerializeField, Min(0.1f)] private float meleeRadius = 2.35f;
    [SerializeField, Min(0f)] private float maxTargetHeightDifference = 2f;
    [Header("M2 - expanding area wave")]
    [SerializeField, Min(0.1f)] private float areaRadius = 4f;
    [SerializeField, Min(0.1f)] private float areaDuration = 0.65f;
    [SerializeField, Min(0f)] private float areaCooldown = 3f;
    [SerializeField, Min(0)] private int areaDamage = 18;
    [SerializeField, Range(0f, 1f)] private float areaMovementMultiplier = 0.35f;
    [SerializeField] private AnimationClip areaAnimation;
    [Header("M2 VFX")]
    [SerializeField] private Material areaVfxMaterial;
    [SerializeField, Min(0.1f), Tooltip("Velocidade visual da onda do M2. 1 = normal, 1.3 = 30% mais rapido. Nao altera o dano nem o cooldown.")]
    private float areaVfxSpeed = 1.3f;
    [Header("Swing timing (normalized clip time)")]
    [SerializeField] private SwingWindow swingA = new SwingWindow(0.28f, 0.55f, 0.86f);
    [SerializeField] private SwingWindow swingB = new SwingWindow(0.28f, 0.54f, 0.83f);
    [SerializeField] private SwingWindow swingC = new SwingWindow(0.36f, 0.54f, 0.76f);

    private WasdBlockMovement movement;
    private Animator animator;
    private MeleeSlashPresentation presentation;
    private CoopSession session;
    private GameplayHud hud;
    private int strike = -1;
    private int nextStrike;
    private bool recovering;
    private bool damageApplied;
    private float nextAllowedAttack;
    private float nextAllowedAreaAttack;
    private bool areaAttacking;
    private float areaStartedAt;
    private PlayerAreaWave areaWave;

    internal float AttackCooldown => Mathf.Max(0f, attackCooldown);
    internal float AreaRadius => Mathf.Max(0.1f, areaRadius);
    internal float AreaDuration => Mathf.Max(0.1f, areaDuration);
    internal float AreaCooldown => Mathf.Max(AreaDuration, areaCooldown);
    internal uint AreaAttackSequence { get; private set; }
    internal Vector3 AreaAttackOrigin { get; private set; }
    internal float AreaAttackProgress => AreaAttackSequence == 0 ? 1f :
        Mathf.Clamp01((Time.time - areaStartedAt) / AreaDuration);

    private void Awake()
    {
        movement = GetComponent<WasdBlockMovement>();
        animator = transform.Find("Player_Character")?.GetComponent<Animator>();
        enabled = animator != null
            && System.Array.TrueForAll(StrikeStates, state => animator.HasState(0, state))
            && System.Array.TrueForAll(RecoveryStates, state => animator.HasState(0, state));
        if (!enabled)
            return;

        session = FindAnyObjectByType<CoopSession>();
        hud = FindAnyObjectByType<GameplayHud>();
        var blade = GetComponentInChildren<KatanaHandGrip>(true)?.BladeModel;
        var mesh = blade != null ? blade.GetComponent<MeshFilter>()?.sharedMesh : null;
        if (mesh != null && movement.WindMaterial != null)
            presentation = new MeleeSlashPresentation(blade, mesh, transform, movement.WindMaterial);
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        bool pressed = Mouse.current?.leftButton.wasPressedThisFrame == true;
        bool areaPressed = Mouse.current?.rightButton.wasPressedThisFrame == true;
#elif ENABLE_LEGACY_INPUT_MANAGER
        bool pressed = Input.GetMouseButtonDown(0);
        bool areaPressed = Input.GetMouseButtonDown(1);
#else
        bool pressed = false;
        bool areaPressed = false;
#endif
        if (areaAttacking)
        {
            if (areaWave == null || !areaWave.IsActive || (hud != null && hud.Health <= 0))
                EndAreaAttack();
            else
                return;
        }
        if (hud != null && hud.Health <= 0)
            return;

        if (areaPressed && TryBeginAreaAttack(Time.time))
            return;
        TickCombo(pressed, Time.time);
    }

    private void LateUpdate()
    {
        if (strike < 0 || recovering || movement.IsDodging)
        {
            presentation?.Update(false, false);
            return;
        }

        var state = animator.GetCurrentAnimatorStateInfo(0);
        var timing = strike == 0 ? swingA : strike == 1 ? swingB : swingC;
        bool isSwinging = state.fullPathHash == StrikeStates[strike];
        float time = state.normalizedTime;
        bool impact = isSwinging && time >= timing.Impact;
        if (impact && !damageApplied)
        {
            damageApplied = true;
            if (hud == null || hud.Health > 0)
            {
                if (session != null && session.InMatch && !session.IsHost)
                    session.QueueMeleeStrike(animator.transform.forward, strike);
                else
                    ApplyMeleeAttack(transform.position, animator.transform.forward, strike);
            }
        }

        presentation?.Update(isSwinging && time >= timing.Start && time <= timing.End, impact);
    }

    private void OnDisable()
    {
        presentation?.Clear();
        EndStrike();
        EndAreaAttack();
    }

    private void OnDestroy() => presentation?.Dispose();

    internal void ApplyMeleeAttack(Vector3 position, Vector3 forward, int attackIndex)
    {
        if (attackIndex < 0 || attackIndex >= StrikeCount)
            return;
        SelectMeleeTarget(position, forward, meleeRadius, maxTargetHeightDifference)?.ApplyMeleeStrike(attackIndex);
    }

    internal static LungeMan SelectMeleeTarget(Vector3 position, Vector3 forward, float radius, float heightDifference)
    {
        forward.y = 0f;
        forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        LungeMan best = null;
        float bestAlignment = float.NegativeInfinity;
        float bestDistance = float.PositiveInfinity;
        foreach (var candidate in Object.FindObjectsByType<LungeMan>())
        {
            if (!IsInAttackArea(candidate, position, radius, heightDifference))
                continue;
            Vector3 offset = candidate.transform.position - position;
            offset.y = 0f;
            float distance = offset.sqrMagnitude;
            float alignment = distance > 0.0001f ? Vector3.Dot(forward, offset.normalized) : 0f;
            if (alignment > bestAlignment + 0.001f ||
                (Mathf.Abs(alignment - bestAlignment) <= 0.001f && distance < bestDistance))
            {
                best = candidate;
                bestAlignment = alignment;
                bestDistance = distance;
            }
        }
        return best;
    }

    internal static bool IsInAttackArea(LungeMan enemy, Vector3 center, float radius, float heightDifference)
    {
        if (enemy == null || !enemy.isActiveAndEnabled || enemy.Health <= 0)
            return false;
        var offset = enemy.transform.position - center;
        if (Mathf.Abs(offset.y) > heightDifference)
            return false;
        offset.y = 0f;
        return offset.sqrMagnitude <= radius * radius;
    }

    private bool TryBeginAreaAttack(float now)
    {
        if (areaAttacking || movement.IsDodging || now < nextAllowedAreaAttack ||
            (strike >= 0 && !recovering && !damageApplied))
            return false;

        EndStrike();
        presentation?.Clear();
        areaAttacking = true;
        areaStartedAt = now;
        AreaAttackOrigin = transform.position;
        AreaAttackSequence++;
        nextAllowedAreaAttack = now + AreaCooldown;
        nextAllowedAttack = Mathf.Max(nextAllowedAttack, now + AreaDuration);
        movement.SlowForAttack(areaMovementMultiplier, AreaDuration);
        movement.FlashAreaAttack();
        PlayAreaAnimation(animator);
        bool authoritative = session == null || !session.InMatch || session.IsHost;
        areaWave = SpawnAreaWave(AreaAttackOrigin, authoritative);
        return true;
    }

    internal PlayerAreaWave SpawnAreaWave(Vector3 origin, bool applyDamage, float progress = 0f) =>
        PlayerAreaWave.Spawn(origin, AreaRadius, AreaDuration, areaDamage, maxTargetHeightDifference,
            areaVfxMaterial != null ? areaVfxMaterial : movement.WindMaterial,
            applyDamage, progress, Mathf.Max(0.1f, areaVfxSpeed));

    internal void PlayAreaAnimation(Animator target, float progress = 0f)
    {
        if (target == null)
            return;
        if (areaAnimation != null && target.HasState(0, AreaAttackState))
        {
            target.SetFloat(AreaAnimationSpeed, areaAnimation.length / AreaDuration);
            target.CrossFadeInFixedTime(AreaAttackState, 0.05f, 0, Mathf.Clamp01(progress) * areaAnimation.length);
        }
        else
            target.CrossFadeInFixedTime(StrikeStates[2], 0.05f, 0, 0f);
    }

    private void EndAreaAttack()
    {
        if (!areaAttacking)
            return;
        areaAttacking = false;
        if (areaWave != null && areaWave.IsActive)
            Destroy(areaWave.gameObject);
        areaWave = null;
        movement?.ClearAttackSlow();
        if (animator != null && animator.isActiveAndEnabled)
            animator.CrossFadeInFixedTime(animator.GetBool("IsMoving") ? "Base Layer.Sprint" : "Base Layer.Idle", 0.10f, 0, 0f);
    }

    private void OnDrawGizmosSelected()
    {
        DrawAttackCircle(Mathf.Max(0.1f, meleeRadius), new Color(1f, 0.7f, 0.15f));
        DrawAttackCircle(AreaRadius, Color.white);
    }

    private void DrawAttackCircle(float radius, Color color)
    {
        Gizmos.color = color;
        Vector3 center = transform.position + Vector3.up * 0.06f;
        Vector3 previous = center + Vector3.forward * radius;
        const int segments = 64;
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 next = center + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }

    private void TickCombo(bool pressed, float now)
    {
        if (movement.IsDodging)
        {
            EndStrike();
            return;
        }

        if (pressed && now >= nextAllowedAttack && (strike < 0 || recovering || damageApplied))
        {
            PlayStrike(now);
            return;
        }
        if (strike < 0)
            return;

        var state = animator.GetCurrentAnimatorStateInfo(0);
        int expected = recovering ? RecoveryStates[strike] : StrikeStates[strike];
        if (state.fullPathHash != expected)
        {
            EndStrike();
            return;
        }

        if (!recovering && state.normalizedTime >= 1f)
        {
            recovering = true;
            animator.Play(RecoveryStates[strike], 0, 0f);
        }
    }

    private void PlayStrike(float now)
    {
        strike = nextStrike;
        nextStrike = (nextStrike + 1) % StrikeCount;
        nextAllowedAttack = now + AttackCooldown;
        recovering = false;
        damageApplied = false;
        animator.Play(StrikeStates[strike], 0, 0f);
        movement.FlashMelee();
        presentation?.Begin(strike == StrikeCount - 1);
    }

    private void EndStrike()
    {
        strike = -1;
        recovering = false;
    }
}

internal sealed class MeleeSlashPresentation
{
    private static readonly Color Ice = new Color(0.30f, 0.77f, 1f);
    private static readonly Color Gold = new Color(1f, 0.53f, 0.14f);
    private readonly TrailRenderer trail;
    private readonly ParticleSystem sparks;
    private readonly Material trailMaterial;
    private readonly Gradient normalGradient = SlashGradient(Ice);
    private readonly Gradient finisherGradient = SlashGradient(Gold);
    private Vector3 previousTip;
    private bool finisher;
    private bool impactPlayed;

    internal MeleeSlashPresentation(Transform blade, Mesh mesh, Transform player, Material material)
    {
        var tip = new GameObject("M1 Blade Trail");
        tip.transform.SetParent(blade, false);
        var bounds = mesh.bounds;
        tip.transform.localPosition = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        previousTip = tip.transform.position;

        trailMaterial = new Material(material) { mainTexture = Texture2D.whiteTexture };
        trail = tip.AddComponent<TrailRenderer>();
        trail.sharedMaterial = trailMaterial;
        trail.shadowCastingMode = ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.minVertexDistance = 0.018f;
        trail.widthCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
        trail.emitting = false;

        var burst = new GameObject("M1 Blade Sparks");
        burst.transform.SetParent(player, false);
        sparks = burst.AddComponent<ParticleSystem>();
        sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = sparks.main;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 48;
        main.gravityModifier = 0.4f;
        var emission = sparks.emission;
        emission.enabled = false;
        var shape = sparks.shape;
        shape.enabled = false;
        var fade = sparks.colorOverLifetime;
        fade.enabled = true;
        fade.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
        });

        var renderer = sparks.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 1.6f;
        renderer.velocityScale = 0.13f;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        sparks.Play();
    }

    internal void Begin(bool lastStrike)
    {
        finisher = lastStrike;
        impactPlayed = false;
        trail.emitting = false;
        trail.Clear();
        trail.time = finisher ? 0.22f : 0.13f;
        trail.widthMultiplier = finisher ? 0.21f : 0.13f;
        trail.colorGradient = finisher ? finisherGradient : normalGradient;
    }

    internal void Update(bool tracing, bool impact)
    {
        trail.emitting = tracing;
        Vector3 tip = trail.transform.position;
        if (impact && !impactPlayed)
        {
            impactPlayed = true;
            if (!sparks.isPlaying)
                sparks.Play();

            Vector3 direction = (tip - previousTip).normalized;
            Color accent = finisher ? Gold : Ice;
            int count = finisher ? 12 : 6;
            for (int i = 0; i < count; i++)
            {
                sparks.Emit(new ParticleSystem.EmitParams
                {
                    position = tip,
                    velocity = direction * Random.Range(1.8f, 4.1f) + Random.insideUnitSphere * 1.3f,
                    startColor = Color.Lerp(accent, Color.white, Random.value * 0.55f),
                    startSize = finisher ? 0.065f : 0.042f,
                    startLifetime = Random.Range(0.12f, finisher ? 0.28f : 0.21f)
                }, 1);
            }
        }
        previousTip = tip;
    }

    internal void Clear()
    {
        trail.emitting = false;
        trail.Clear();
        sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    internal void Dispose()
    {
        if (trail != null)
            Object.Destroy(trail.gameObject);
        if (sparks != null)
            Object.Destroy(sparks.gameObject);
        Object.Destroy(trailMaterial);
    }

    private static Gradient SlashGradient(Color color)
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(color, 0.56f) },
            new[] { new GradientAlphaKey(0.96f, 0f), new GradientAlphaKey(0.60f, 0.45f), new GradientAlphaKey(0f, 1f) });
        return gradient;
    }
}
