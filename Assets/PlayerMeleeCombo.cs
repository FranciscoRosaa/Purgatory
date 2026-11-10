using UnityEngine;
using UnityEngine.Rendering;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DefaultExecutionOrder(550)]
[RequireComponent(typeof(WasdBlockMovement))]
public sealed class PlayerMeleeCombo : MonoBehaviour
{
    private static readonly int[] StrikeStates =
    {
        Animator.StringToHash("Base Layer.Sword_Light_A"),
        Animator.StringToHash("Base Layer.Sword_Light_B"),
        Animator.StringToHash("Base Layer.Sword_Light_C"),
        Animator.StringToHash("Base Layer.Sword_Light_D")
    };

    private static readonly int[] RecoveryStates =
    {
        Animator.StringToHash("Base Layer.Sword_Light_A_Rec"),
        Animator.StringToHash("Base Layer.Sword_Light_B_Rec"),
        Animator.StringToHash("Base Layer.Sword_Light_C_Rec")
    };

    // Swing windows and impact frames in normalized clip time.
    private static readonly (float Start, float Impact, float End)[] SwingTiming =
    {
        (0.28f, 0.55f, 0.86f),
        (0.28f, 0.54f, 0.83f),
        (0.36f, 0.54f, 0.76f),
        (0.07f, 0.16f, 0.32f)
    };

    [SerializeField, Min(0f)] private float finisherCooldown = 0.65f;

    private WasdBlockMovement movement;
    private Animator animator;
    private Transform head;
    private Quaternion headFacingForward;
    private KatanaHandGrip katanaGrip;
    private MeleeSlashPresentation presentation;
    private int strike = -1;
    private bool queued;
    private bool recovering;
    private float nextAllowedAttack;

    private void Awake()
    {
        movement = GetComponent<WasdBlockMovement>();
        animator = transform.Find("Player_Character")?.GetComponent<Animator>();
        enabled = animator != null
            && System.Array.TrueForAll(StrikeStates, state => animator.HasState(0, state))
            && System.Array.TrueForAll(RecoveryStates, state => animator.HasState(0, state));

        if (enabled)
        {
            if (animator.isHuman)
            {
                head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head != null)
                    headFacingForward = Quaternion.Inverse(animator.transform.rotation) * head.rotation;
            }

            katanaGrip = GetComponentInChildren<KatanaHandGrip>(true);
            var blade = katanaGrip?.BladeModel;
            var mesh = blade != null ? blade.GetComponent<MeshFilter>()?.sharedMesh : null;
            if (mesh != null && movement.WindMaterial != null)
                presentation = new MeleeSlashPresentation(blade, mesh, transform, movement.WindMaterial);
        }
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
        var timing = SwingTiming[strike];
        bool isSwinging = state.fullPathHash == StrikeStates[strike];
        float time = state.normalizedTime;
        if (isSwinging && strike == StrikeStates.Length - 1)
        {
            AimFinisherBlade(time);
            AimFinisherHead(time);
        }

        presentation?.Update(isSwinging && time >= timing.Start && time <= timing.End,
            isSwinging && time >= timing.Impact);
    }

    private void AimFinisherHead(float time)
    {
        if (head == null)
            return;

        // Use the avatar's forward-facing head pose without moving the neck or torso.
        float weight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.08f, time));
        var facing = animator.transform.rotation * headFacingForward;
        head.rotation = Quaternion.Slerp(head.rotation, facing, weight);
    }

    private void AimFinisherBlade(float time)
    {
        float aimIn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.03f, 0.11f, time));
        float aimOut = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.29f, 0.43f, time));
        float weight = aimIn * aimOut;
        if (weight <= 0f)
            return;

        // Sweep across the player's front, pointing straight ahead at impact.
        float yaw = Mathf.Lerp(55f, -55f, Mathf.InverseLerp(0.06f, 0.26f, time));
        var direction = Quaternion.AngleAxis(yaw, Vector3.up) * animator.transform.forward;
        katanaGrip?.AimBlade(direction, weight);
    }

    private void OnDisable() => presentation?.Clear();

    private void OnDestroy() => presentation?.Dispose();

    private void TickCombo(bool pressed, float now)
    {
        bool finisher = strike == StrikeStates.Length - 1;
        if (movement.IsDodging)
        {
            EndCombo(now, finisher);
            return;
        }

        if (strike < 0)
        {
            if (pressed && now >= nextAllowedAttack)
                PlayStrike(0);
            return;
        }

        var state = animator.GetCurrentAnimatorStateInfo(0);
        if (recovering || finisher)
        {
            int endingState = recovering ? RecoveryStates[strike] : StrikeStates[strike];
            if (state.fullPathHash != endingState)
                EndCombo(now, finisher);
            return;
        }

        if (state.normalizedTime < 1f)
        {
            queued |= pressed;
            return;
        }

        // Follow the authored recovery when no continuation was queued during the strike.
        if (queued)
        {
            PlayStrike(strike + 1);
        }
        else
        {
            recovering = true;
            animator.Play(RecoveryStates[strike], 0, 0f);
        }
    }

    private void PlayStrike(int index)
    {
        strike = index;
        queued = false;
        animator.Play(StrikeStates[index], 0, 0f);
        presentation?.Begin(index == StrikeStates.Length - 1);
    }

    private void EndCombo(float now, bool finisher)
    {
        if (finisher)
            nextAllowedAttack = now + finisherCooldown;

        strike = -1;
        queued = false;
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
