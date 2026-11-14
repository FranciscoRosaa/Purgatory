using UnityEngine;

internal sealed class RedNinjaFire
{
    private readonly Material sparksMaterial, glowMaterial, smokeMaterial, coreMaterial;
    private readonly Texture2D glowTexture;
    private readonly ParticleSystem embers, flames, burst, corona;
    private readonly InwardChargeSmoke smoke;
    private readonly Transform core;
    private readonly Light light;
    private RedNinja.Phase previousPhase;
    private RedNinja.FireStatus previousFire;
    private uint displayedShot;
    private int lastStep = -1;
    private float nextEmber;
    private Vector3 lastFlamePosition;
    private readonly Transform root;

    internal RedNinjaFire(Transform owner, Material template, Shader fireballShader)
    {
        root = new GameObject("Red Ninja fire effects").transform;
        root.SetParent(owner, false);
        glowTexture = GiantChargeVfx.SmokeTexture();
        sparksMaterial = new Material(template) { mainTexture = Texture2D.whiteTexture };
        glowMaterial = new Material(template) { mainTexture = glowTexture };
        glowMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        smokeMaterial = new Material(template) { mainTexture = glowTexture };
        smoke = new InwardChargeSmoke(root, smokeMaterial, 1.55f, 0.73f);
        flames = CreateParticles("Fire wake", glowMaterial);
        var flameRenderer = flames.GetComponent<ParticleSystemRenderer>();
        flameRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        flameRenderer.lengthScale = 1.65f;
        flameRenderer.velocityScale = 0.11f;
        embers = CreateParticles("Embers", sparksMaterial);
        burst = CreateParticles("Fire impact", glowMaterial);
        corona = CreateParticles("Fireball flames", glowMaterial);
        var coronaRenderer = corona.GetComponent<ParticleSystemRenderer>();
        coronaRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        coronaRenderer.lengthScale = 1.2f;
        coronaRenderer.velocityScale = 0.2f;

        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Fireball core";
        core = sphere.transform;
        core.SetParent(root, false);
        var collider = sphere.GetComponent<Collider>();
        collider.enabled = false;
        Object.Destroy(collider);
        coreMaterial = new Material(fireballShader);
        var coreRenderer = sphere.GetComponent<MeshRenderer>();
        coreRenderer.sharedMaterial = coreMaterial;
        coreRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sphere.SetActive(false);

        light = new GameObject("Fire light").AddComponent<Light>();
        light.transform.SetParent(root, false);
        light.type = LightType.Point;
        light.color = new Color(1f, 0.24f, 0.035f);
        light.range = 4.5f;
        light.enabled = false;
    }

    internal void Update(RedNinja.Phase phase, Vector3 hand, Vector3 chest, float age, float chargeLeft, uint shot,
        RedNinja.FireStatus fire, Vector3 position, Vector3 direction)
    {
        if (phase != previousPhase)
        {
            if (phase == RedNinja.Phase.Enter)
                CombatAudio.NinjaCharge(hand);
            else if (phase == RedNinja.Phase.Cancel)
            {
                smoke.Clear();
                CombatAudio.NinjaFizzle(hand);
                EmitBurst(hand, 9, 1.2f);
            }
            else if (phase == RedNinja.Phase.Dead)
                smoke.Clear();
            lastStep = -1;
            previousPhase = phase;
        }
        if (phase == RedNinja.Phase.Flee)
        {
            int step = Mathf.FloorToInt(Mathf.Max(age, 0f) * 3f);
            if (step > lastStep)
            {
                CombatAudio.NinjaStep(root.position);
                lastStep = step;
            }
        }
        if (shot != displayedShot)
        {
            lastFlamePosition = position;
            smoke.Clear();
            CombatAudio.NinjaFire(fire == RedNinja.FireStatus.Flying ? position : hand);
        }
        if (fire == RedNinja.FireStatus.Impact && (previousFire != fire || shot != displayedShot))
        {
            EmitBurst(position, 38, 6f);
            CombatAudio.NinjaImpact(position);
        }
        displayedShot = shot;
        previousFire = fire;

        bool flying = fire == RedNinja.FireStatus.Flying;
        bool charging = chargeLeft > 0f;
        smoke.Update(chest, root.position.y, chargeLeft, 1f - Mathf.Clamp01(chargeLeft / 1.6f), Time.deltaTime);
        core.gameObject.SetActive(charging || flying);
        light.enabled = charging || flying;
        if (!light.enabled)
            return;

        Vector3 center = flying ? position : hand;
        float scale = flying ? RedNinja.FireDiameter
            : Mathf.Lerp(0.12f, 0.63f, 1f - Mathf.Clamp01(chargeLeft / 1.55f)) * RedNinja.FireVisualScale;
        core.position = center;
        core.localScale = Vector3.one * scale;
        if (flying)
            core.rotation = Quaternion.LookRotation(direction);
        light.transform.position = center;
        light.intensity = (1.65f + Mathf.Sin(Time.time * 35f) * 0.25f) * scale;
        if (Time.time < nextEmber)
            return;
        nextEmber = Time.time + 0.018f;
        for (int i = 0; i < 3; i++)
        {
            Vector3 radial = Random.onUnitSphere;
            Vector3 drift = radial * (scale * 0.55f) + Vector3.up * (scale * 1.6f);
            if (flying)
                drift -= direction * 1.3f;
            Emit(corona, center + radial * (scale * 0.42f), drift,
                scale * Random.Range(0.38f, 0.72f), Random.Range(0.12f, 0.24f),
                Color.Lerp(new Color(1f, 0.16f, 0.012f, 0.68f), new Color(1f, 0.64f, 0.055f, 0.84f), Random.value));
        }
        if (flying)
        {
            int samples = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(lastFlamePosition, center) / 0.24f), 1, 8);
            for (int i = 1; i <= samples; i++)
            {
                var origin = Vector3.Lerp(lastFlamePosition, center, (float)i / samples);
                EmitWake(origin, direction);
            }
            lastFlamePosition = center;
        }
        Vector3 scatter = Random.insideUnitSphere * (flying ? 0.88f : 0.18f) * RedNinja.FireVisualScale;
        Emit(embers, center + scatter, scatter * 3.8f + Vector3.up * 0.5f - (flying ? direction * 0.8f : Vector3.zero),
            Random.Range(0.035f, 0.09f), Random.Range(0.15f, 0.32f), new Color(1f, 0.42f, 0.045f, 0.9f));
    }

    private void EmitWake(Vector3 center, Vector3 direction)
    {
        const float scale = RedNinja.FireVisualScale;
        Vector3 spread = Vector3.ProjectOnPlane(Random.insideUnitSphere, direction) * (0.82f * scale);
        var tail = center - direction * (Random.Range(0.48f, 0.9f) * scale) + spread;
        Emit(flames, tail, -direction * Random.Range(1.8f, 3.9f) + spread * 1.8f,
            Random.Range(0.7f, 1.25f) * scale, Random.Range(0.17f, 0.32f), new Color(1f, 0.17f, 0.012f, 0.74f));
        Emit(flames, Vector3.Lerp(tail, center, 0.32f), -direction * 1.7f + spread,
            Random.Range(0.48f, 0.92f) * scale, Random.Range(0.12f, 0.24f), new Color(1f, 0.63f, 0.08f, 0.78f));
    }

    private ParticleSystem CreateParticles(string name, Material material)
    {
        var particles = new GameObject(name).AddComponent<ParticleSystem>();
        particles.transform.SetParent(root, false);
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var settings = particles.main;
        settings.playOnAwake = false;
        settings.simulationSpace = ParticleSystemSimulationSpace.World;
        settings.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        settings.maxParticles = 96;
        var emission = particles.emission;
        emission.enabled = false;
        var shape = particles.shape;
        shape.enabled = false;
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        var fade = particles.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.35f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
        particles.Play();
        return particles;
    }

    private void EmitBurst(Vector3 center, int count, float speed)
    {
        for (int i = 0; i < count; i++)
            Emit(burst, center, Random.insideUnitSphere * speed + Vector3.up * 0.5f,
                Random.Range(0.15f, 0.45f), Random.Range(0.18f, 0.42f),
                Color.Lerp(new Color(1f, 0.09f, 0.02f, 0.7f), new Color(1f, 0.59f, 0.08f, 0.9f), Random.value));
    }

    private static void Emit(ParticleSystem particles, Vector3 position, Vector3 velocity,
        float size, float lifetime, Color color) => particles.Emit(new ParticleSystem.EmitParams
        {
            position = position, velocity = velocity, startSize = size, startLifetime = lifetime, startColor = color
        }, 1);

    internal void Dispose()
    {
        Object.Destroy(root.gameObject);
        Object.Destroy(glowTexture);
        Object.Destroy(glowMaterial);
        Object.Destroy(sparksMaterial);
        Object.Destroy(smokeMaterial);
        Object.Destroy(coreMaterial);
    }
}
