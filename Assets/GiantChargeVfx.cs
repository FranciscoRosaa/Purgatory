using UnityEngine;

internal sealed class GiantChargeVfx
{
    private readonly Transform actor;
    private readonly GameObject root;
    private readonly InwardChargeSmoke smoke;
    private readonly ParticleSystem dust;
    private readonly Material material;
    private readonly Texture2D texture;
    private Vector3 lastDust;
    private float dustDistance;
    private bool wasCharging;

    internal GiantChargeVfx(Transform actor, Material template, float radius)
    {
        this.actor = actor;
        root = new GameObject("Gigante Sprint VFX");
        root.transform.SetParent(actor, false);
        texture = SmokeTexture();
        material = new Material(template) { mainTexture = texture };
        material.SetColor("_BaseColor", Color.white);
        smoke = new InwardChargeSmoke(root.transform, material, radius);
        dust = CreateCloud(root.transform, material, "Footstep dust", 40);
    }

    internal static ParticleSystem CreateCloud(Transform parent, Material material, string name, int capacity)
    {
        var cloud = DashPresentation.CreateWind(parent, material, name);
        cloud.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.Billboard;
        var main = cloud.main;
        main.maxParticles = capacity;
        var fade = cloud.colorOverLifetime;
        fade.color = new Gradient
        {
            alphaKeys = new[]
            {
                new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.16f),
                new GradientAlphaKey(0.72f, 0.55f), new GradientAlphaKey(0f, 1f)
            }
        };
        return cloud;
    }

    internal void Update(bool winding, bool charging, float progress, float windupDuration, Vector3 direction, float deltaTime,
        Vector3 chest, Transform leftFoot, Transform rightFoot)
    {
        smoke.Update(chest, actor.position.y, winding ? (1f - progress) * windupDuration : 0f, progress, deltaTime);

        if (charging)
        {
            if (!wasCharging)
            {
                lastDust = actor.position;
                dustDistance = 0f;
            }
            dustDistance += Vector3.Distance(actor.position, lastDust);
            lastDust = actor.position;
            const float spacing = 0.52f;
            int count = Mathf.Min(4, Mathf.FloorToInt(dustDistance / spacing));
            dustDistance -= count * spacing;
            for (int i = 0; i < count; i++)
            {
                float leftHeight = leftFoot != null ? leftFoot.position.y : float.PositiveInfinity;
                float rightHeight = rightFoot != null ? rightFoot.position.y : float.PositiveInfinity;
                float lowest = Mathf.Min(leftHeight, rightHeight);
                if (leftHeight <= lowest + 0.18f) EmitFootDust(leftFoot, direction);
                if (rightHeight <= lowest + 0.18f) EmitFootDust(rightFoot, direction);
            }
        }
        wasCharging = charging;
    }

    private void EmitFootDust(Transform foot, Vector3 direction)
    {
        if (foot == null)
            return;
        var point = foot.position - direction * Random.Range(0.08f, 0.23f);
        point.y = actor.position.y + 0.12f;
        Emit(dust, point, -direction * Random.Range(0.45f, 0.85f) + Vector3.up * 0.25f,
            new Color(0.59f, 0.52f, 0.41f, Random.Range(0.22f, 0.35f)),
            Random.Range(0.36f, 0.59f), Random.Range(0.3f, 0.48f));
    }

    internal static void Emit(ParticleSystem cloud, Vector3 position, Vector3 velocity, Color color,
        float size, float lifetime)
    {
        if (!cloud.isPlaying) cloud.Play();
        cloud.Emit(new ParticleSystem.EmitParams
        {
            position = position, velocity = velocity, startColor = color,
            startSize = size, startLifetime = lifetime, rotation = Random.Range(0f, 360f)
        }, 1);
    }

    internal static Texture2D SmokeTexture()
    {
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        { name = "Gigante soft smoke", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float noise = Mathf.PerlinNoise(x * 0.095f, y * 0.095f);
                float fade = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)), 1.25f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, fade * Mathf.Lerp(0.22f, 1f, noise));
            }
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return texture;
    }

    internal void Reset()
    {
        smoke.Clear();
        dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        dustDistance = 0f;
        wasCharging = false;
    }

    internal void Dispose()
    {
        Object.Destroy(material);
        Object.Destroy(texture);
        Object.Destroy(root);
    }
}

internal sealed class InwardChargeSmoke
{
    private readonly float radius, scale;
    private readonly ParticleSystem particles;
    private float budget;

    internal InwardChargeSmoke(Transform parent, Material material, float radius, float scale = 1f)
    {
        this.radius = radius;
        this.scale = scale;
        particles = GiantChargeVfx.CreateCloud(parent, material, "Inward black smoke", 100);
    }

    internal void Update(Vector3 chest, float ground, float chargeLeft, float progress, float deltaTime)
    {
        if (chargeLeft <= 0.16f)
        {
            budget = 0f;
            return;
        }
        budget += deltaTime * Mathf.Lerp(30f, 62f, progress);
        int count = Mathf.Min(12, Mathf.FloorToInt(budget));
        budget -= count;
        for (int i = 0; i < count; i++)
        {
            float angle = Random.value * Mathf.PI * 2f;
            var radial = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            var origin = chest + radial * Random.Range(radius * 0.55f, radius * 1.25f);
            origin.y = Mathf.Lerp(ground + 0.14f, chest.y + 0.8f * scale, Random.value);
            float life = Mathf.Min(chargeLeft, Random.Range(0.32f, 0.54f));
            GiantChargeVfx.Emit(particles, origin, (chest - origin) / life, new Color(0.022f, 0.023f, 0.026f, 0.82f),
                Random.Range(0.65f, 1.35f) * scale, life);
        }
    }

    internal void Clear()
    {
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        budget = 0f;
    }
}
