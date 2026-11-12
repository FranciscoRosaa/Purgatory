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
    [Header("Swing timing (normalized clip time)")]
    [SerializeField] private SwingWindow swingA = new SwingWindow(0.28f, 0.55f, 0.86f);
    [SerializeField] private SwingWindow swingB = new SwingWindow(0.28f, 0.54f, 0.83f);
    [SerializeField] private SwingWindow swingC = new SwingWindow(0.36f, 0.54f, 0.76f);

    private WasdBlockMovement movement;
    private Animator animator;
    private MeleeSlashPresentation presentation;
    private LungeMan enemy;
    private int strike = -1;
    private int nextStrike;
    private bool recovering;
    private bool damageApplied;
    private float nextAllowedAttack;

    internal float AttackCooldown => Mathf.Max(0f, attackCooldown);

    private void Awake()
    {
        movement = GetComponent<WasdBlockMovement>();
        animator = transform.Find("Player_Character")?.GetComponent<Animator>();
        enabled = animator != null
            && System.Array.TrueForAll(StrikeStates, state => animator.HasState(0, state))
            && System.Array.TrueForAll(RecoveryStates, state => animator.HasState(0, state));
        if (!enabled)
            return;

        enemy = FindAnyObjectByType<LungeMan>();
        var blade = GetComponentInChildren<KatanaHandGrip>(true)?.BladeModel;
        var mesh = blade != null ? blade.GetComponent<MeshFilter>()?.sharedMesh : null;
        if (mesh != null && movement.WindMaterial != null)
            presentation = new MeleeSlashPresentation(blade, mesh, transform, movement.WindMaterial);
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        bool pressed = Mouse.current?.leftButton.wasPressedThisFrame == true;
#elif ENABLE_LEGACY_INPUT_MANAGER
        bool pressed = Input.GetMouseButtonDown(0);
#else
        bool pressed = false;
#endif
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
            enemy?.ReceivePlayerStrike(transform.position, animator.transform.forward, strike);
        }

        presentation?.Update(isSwinging && time >= timing.Start && time <= timing.End, impact);
    }

    private void OnDisable() => presentation?.Clear();

    private void OnDestroy() => presentation?.Dispose();

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
