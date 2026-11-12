using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// The wave stays at the cast origin while the player can continue moving.
public sealed class PlayerAreaWave : MonoBehaviour
{
    private readonly HashSet<LungeMan> hitEnemies = new HashSet<LungeMan>();
    private LungeMan[] candidates;
    private AreaWavePresentation presentation;
    private float startedAt, duration, radius, heightDifference;
    private float vfxSpeed;
    private int damage;
    private bool applyDamage;
    private bool damageFinished;

    internal bool IsActive => Time.time < startedAt + duration;

    internal static PlayerAreaWave Spawn(Vector3 origin, float radius, float duration, int damage,
        float heightDifference, Material template, bool applyDamage, float progress = 0f, float vfxSpeed = 1.3f)
    {
        var root = new GameObject("M2 White Shockwave");
        root.transform.position = origin;
        var wave = root.AddComponent<PlayerAreaWave>();
        wave.radius = Mathf.Max(0.1f, radius);
        wave.duration = Mathf.Max(0.1f, duration);
        wave.damage = Mathf.Max(0, damage);
        wave.heightDifference = Mathf.Max(0f, heightDifference);
        wave.applyDamage = applyDamage;
        wave.vfxSpeed = Mathf.Max(0.1f, vfxSpeed);
        wave.startedAt = Time.time - Mathf.Clamp01(progress) * wave.duration;
        wave.candidates = applyDamage ? Object.FindObjectsByType<LungeMan>() : null;
        wave.presentation = new AreaWavePresentation(root.transform, template);
        wave.Advance(Time.time);
        return wave;
    }

    private void Update()
    {
        Advance(Time.time);
        if (!IsActive && Time.time >= startedAt + duration / vfxSpeed)
            Destroy(gameObject);
    }

    internal void Advance(float now)
    {
        float age = Mathf.Max(0f, (now - startedAt) / duration);
        float progress = Mathf.Clamp01(age);
        float currentRadius = radius * progress;
        presentation.Update(Mathf.Clamp01(age * vfxSpeed), radius);
        if (applyDamage && !damageFinished)
        {
            foreach (var enemy in candidates)
            {
                if (hitEnemies.Contains(enemy) ||
                    !PlayerMeleeCombo.IsInAttackArea(enemy, transform.position, currentRadius, heightDifference))
                    continue;
                hitEnemies.Add(enemy);
                enemy.ApplyAreaStrike(damage);
            }
            damageFinished = progress >= 1f;
        }
    }

    private void OnDestroy() => presentation?.Dispose();
}

internal sealed class AreaWavePresentation
{
    private const int Segments = 160;
    private const int Rows = 9;
    private static readonly float[] Opacity = { 0f, 0.05f, 0.25f, 0.85f, 1f, 0.85f, 0.25f, 0.05f, 0f };
    private readonly Mesh ring;
    private readonly Mesh flash;
    private readonly Material material;
    private readonly Transform flashTransform;
    private readonly Vector3[] vertices = new Vector3[(Segments + 1) * Rows];
    private readonly Color[] colors = new Color[(Segments + 1) * Rows];
    private readonly Color[] flashColors;

    internal AreaWavePresentation(Transform parent, Material template)
    {
        material = template != null ? new Material(template) :
            new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        material.name = "M2 White Energy (runtime)";
        material.mainTexture = Texture2D.whiteTexture;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", new Color(3f, 3f, 3f, 1f));
        if (material.HasProperty("_Cull"))
            material.SetFloat("_Cull", (float)CullMode.Off);

        ring = new Mesh { name = "M2 soft energy ring" };
        ring.MarkDynamic();
        ring.vertices = vertices;
        var triangles = new int[Segments * (Rows - 1) * 6];
        int t = 0;
        for (int segment = 0; segment < Segments; segment++)
        {
            for (int row = 0; row < Rows - 1; row++)
            {
                int a = segment * Rows + row;
                int b = a + Rows;
                triangles[t++] = a; triangles[t++] = b; triangles[t++] = a + 1;
                triangles[t++] = a + 1; triangles[t++] = b; triangles[t++] = b + 1;
            }
        }
        ring.triangles = triangles;
        CreateRenderer("M2 Expanding Energy Ring", parent, ring);

        // A short four-point flare at the cast origin, as in the reference.
        flash = new Mesh { name = "M2 cast flare" };
        var points = new Vector3[25];
        var faces = new int[24 * 3];
        flashColors = new Color[25];
        for (int i = 0; i < 24; i++)
        {
            float angle = i * Mathf.PI * 2f / 24;
            float length = i % 6 == 0 ? 1.25f : i % 3 == 0 ? 0.24f : 0.10f;
            points[i + 1] = new Vector3(Mathf.Sin(angle) * length, Mathf.Cos(angle) * length, 0f);
            faces[i * 3] = 0;
            faces[i * 3 + 1] = i + 1;
            faces[i * 3 + 2] = (i + 1) % 24 + 1;
        }
        flash.vertices = points;
        flash.triangles = faces;
        flashTransform = CreateRenderer("M2 Initial Flash", parent, flash).transform;
        flashTransform.localPosition = Vector3.up * 0.55f;
        flash.RecalculateBounds();
    }

    private MeshRenderer CreateRenderer(string name, Transform parent, Mesh mesh)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = root.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return renderer;
    }

    internal void Update(float progress, float radius)
    {
        float expansion = 1f - Mathf.Pow(1f - progress, 1.6f);
        float currentRadius = radius * expansion;
        float halfWidth = Mathf.Min(currentRadius * 0.65f, Mathf.Lerp(0.72f, 0.065f, progress));
        float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.56f, 1f, progress));
        float dissolve = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 1f, progress));
        for (int segment = 0; segment <= Segments; segment++)
        {
            float angle = segment * Mathf.PI * 2f / Segments;
            float sin = Mathf.Sin(angle), cos = Mathf.Cos(angle);
            float noise = Mathf.PerlinNoise(cos * 3.3f + 8f, sin * 3.3f + 8f + progress * 0.3f);
            float arcAlpha = 1f - Mathf.SmoothStep(0f, 0.16f, dissolve * 0.9f - noise);
            float ripple = (Mathf.Sin(angle * 7f + progress * 9f) + Mathf.Sin(angle * 13f - progress * 7f) * 0.35f)
                * halfWidth * 0.10f;
            float localWidth = halfWidth * (0.85f + noise * 0.35f);
            for (int row = 0; row < Rows; row++)
            {
                int index = segment * Rows + row;
                float offset = row / (float)(Rows - 1) * 2f - 1f;
                float distance = Mathf.Max(0f, currentRadius + ripple + offset * localWidth);
                vertices[index] = new Vector3(sin * distance, 0.13f + Mathf.Sin(angle * 5f) * 0.008f, cos * distance);
                Color tint = Mathf.Abs(offset) < 0.4f ? Color.white : new Color(0.70f, 1f, 1f);
                tint.a = Opacity[row] * fade * arcAlpha;
                colors[index] = tint;
            }
        }
        ring.vertices = vertices;
        ring.colors = colors;
        ring.RecalculateBounds();

        float flashAlpha = 1f - Mathf.SmoothStep(0f, 0.19f, progress);
        flashColors[0] = new Color(1f, 1f, 1f, flashAlpha);
        for (int i = 1; i < flashColors.Length; i++)
            flashColors[i] = new Color(0.8f, 1f, 1f, flashAlpha * 0.65f);
        flash.colors = flashColors;
        flashTransform.localScale = Vector3.one * Mathf.Lerp(0.8f, 0.2f, Mathf.Clamp01(progress / 0.19f));
        if (Camera.main != null)
            flashTransform.rotation = Camera.main.transform.rotation;
    }

    internal void Dispose()
    {
        Object.Destroy(ring);
        Object.Destroy(flash);
        Object.Destroy(material);
    }
}
