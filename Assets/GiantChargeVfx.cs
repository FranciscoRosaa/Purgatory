using UnityEngine;
using UnityEngine.Rendering;

internal sealed class GiantChargeVfx
{
    private const int Segments = 96;
    private readonly Transform actor;
    private readonly GameObject root;
    private readonly Mesh aura;
    private readonly MeshRenderer auraRenderer;
    private readonly Vector3[] vertices = new Vector3[(Segments + 1) * 4];
    private readonly Color[] colors = new Color[(Segments + 1) * 4];
    private readonly ParticleSystem smoke;
    private readonly Material material;
    private readonly Material auraMaterial;
    private readonly Texture2D texture;
    private readonly float radius;
    private Vector3 lastCloud;
    private float emission;
    private bool wasCharging;

    internal GiantChargeVfx(Transform actor, Material template, float radius)
    {
        this.actor = actor;
        this.radius = radius;
        root = new GameObject("Gigante Charge VFX");
        root.transform.SetParent(actor, false);
        texture = SmokeTexture();
        material = new Material(template) { mainTexture = texture };
        material.SetColor("_BaseColor", Color.white);
        if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);
        auraMaterial = new Material(material) { mainTexture = Texture2D.whiteTexture };
        smoke = DashPresentation.CreateWind(root.transform, material, "Gigante Smoke and Wind");
        var renderer = smoke.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        var main = smoke.main;
        main.maxParticles = 160;
        var auraObject = new GameObject("Black Charge Aura");
        auraObject.transform.SetParent(root.transform, false);
        aura = new Mesh { name = "Gigante jagged windup aura" };
        aura.MarkDynamic();
        aura.vertices = vertices;
        var triangles = new int[Segments * 3 * 6];
        int t = 0;
        for (int s = 0; s < Segments; s++)
            for (int r = 0; r < 3; r++)
            {
                int a = s * 4 + r, b = a + 4;
                triangles[t++] = a; triangles[t++] = b; triangles[t++] = a + 1;
                triangles[t++] = a + 1; triangles[t++] = b; triangles[t++] = b + 1;
            }
        aura.triangles = triangles;
        auraObject.AddComponent<MeshFilter>().sharedMesh = aura;
        auraRenderer = auraObject.AddComponent<MeshRenderer>();
        auraRenderer.sharedMaterial = auraMaterial;
        auraRenderer.shadowCastingMode = ShadowCastingMode.Off;
        auraRenderer.receiveShadows = false;
        auraRenderer.enabled = false;
    }

    internal void BeginCharge(Vector3 position) => lastCloud = position;

    internal void Update(bool winding, bool charging, float progress, Vector3 direction, float deltaTime)
    {
        auraRenderer.enabled = winding;
        if (winding)
        {
            DrawAura(progress);
            emission += deltaTime * 34f;
            int count = Mathf.Min(12, Mathf.FloorToInt(emission));
            emission -= count;
            for (int i = 0; i < count; i++)
            {
                float angle = Random.value * Mathf.PI * 2f;
                var side = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                Emit(actor.position + side * Random.Range(0.5f, radius),
                    -side * 0.5f + Vector3.up * Random.Range(1f, 2f),
                    new Color(0.008f, 0.012f, 0.015f, 0.70f), Random.Range(0.45f, 0.85f), 0.38f);
            }
        }
        if (charging)
        {
            if (!wasCharging) BeginCharge(actor.position);
            float distance = Vector3.Distance(actor.position, lastCloud);
            int count = Mathf.Clamp(Mathf.CeilToInt(distance / 0.28f), 0, 16);
            var lateral = Vector3.Cross(Vector3.up, direction);
            for (int i = 0; i < count; i++)
            {
                var point = Vector3.Lerp(lastCloud, actor.position, (i + 1f) / count);
                for (int puff = 0; puff < 2; puff++)
                    Emit(point - direction * 0.9f + lateral * Random.Range(-0.45f, 0.45f) +
                        Vector3.up * Random.Range(0.45f, 1.15f),
                        -direction * Random.Range(1.5f, 3.5f) + Vector3.up * 0.5f,
                        new Color(0.84f, 1f, 1f, 0.8f), Random.Range(0.7f, 1.3f), Random.Range(0.18f, 0.32f));
            }
            lastCloud = actor.position;
        }
        else if (!winding)
            emission = 0f;
        wasCharging = charging;
    }

    private void DrawAura(float progress)
    {
        float size = radius * Mathf.Lerp(0.65f, 1f, progress);
        for (int s = 0; s <= Segments; s++)
        {
            float angle = s * Mathf.PI * 2f / Segments;
            float tip = 0.8f + Mathf.Pow(Mathf.Max(0f, Mathf.Sin(angle * 14f + Time.time * 0.5f)), 5f) * 0.55f;
            for (int r = 0; r < 4; r++)
            {
                int index = s * 4 + r;
                float fraction = r == 0 ? 0f : r == 1 ? 0.56f : r == 2 ? 0.84f : 1.05f;
                float distance = size * tip * fraction;
                vertices[index] = new Vector3(Mathf.Sin(angle) * distance, 0.07f, Mathf.Cos(angle) * distance);
                float alpha = r == 0 ? 0.08f : r == 1 ? 0.55f : r == 2 ? 0.62f : 0f;
                colors[index] = new Color(0.006f, 0.01f, 0.014f, alpha * Mathf.Lerp(0.45f, 1f, progress));
            }
        }
        aura.vertices = vertices;
        aura.colors = colors;
        aura.RecalculateBounds();
    }

    private void Emit(Vector3 position, Vector3 velocity, Color color, float size, float lifetime)
    {
        if (!smoke.isPlaying) smoke.Play();
        smoke.Emit(new ParticleSystem.EmitParams
        {
            position = position, velocity = velocity, startColor = color,
            startSize = size, startLifetime = lifetime, rotation = Random.Range(0f, 360f)
        }, 1);
    }

    private static Texture2D SmokeTexture()
    {
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        { name = "Gigante soft smoke", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float fade = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)), 1.4f);
                pixels[y * size + x] = new Color(1f, 1f, 1f,
                    fade * (0.55f + Mathf.PerlinNoise(x * 0.13f, y * 0.13f) * 0.45f));
            }
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return texture;
    }

    internal void Reset()
    {
        auraRenderer.enabled = false;
        smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        emission = 0f;
        wasCharging = false;
    }

    internal void Dispose()
    {
        Object.Destroy(aura);
        Object.Destroy(material);
        Object.Destroy(auraMaterial);
        Object.Destroy(texture);
        Object.Destroy(root);
    }
}
