using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed class GiantJumpVfx
{
    private const float Lifetime = 2f;
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private readonly GameObject root, warning;
    private readonly MeshRenderer fill, outline, crater;
    private readonly Material surface, dustMaterial;
    private readonly Mesh square, border, craterMesh;
    private readonly Texture2D dustTexture;
    private readonly ParticleSystem dust;
    private readonly MaterialPropertyBlock tint = new MaterialPropertyBlock();
    private float impactTime = float.NegativeInfinity;

    internal GiantJumpVfx(Transform owner, Material template)
    {
        root = new GameObject("Gigante jump effects");
        root.transform.SetParent(owner, false);
        surface = new Material(template) { mainTexture = Texture2D.whiteTexture };
        surface.SetFloat("_Cull", (float)CullMode.Off);
        surface.SetColor(BaseColor, Color.white);
        dustTexture = GiantChargeVfx.SmokeTexture();
        dustMaterial = new Material(surface) { mainTexture = dustTexture };

        warning = new GameObject("Jump landing square");
        warning.transform.SetParent(root.transform, false);
        square = CreateSquare(0.5f, 0f);
        border = CreateSquare(0.5f, 0.475f);
        fill = CreateSurface("Warning fill", warning.transform, square);
        outline = CreateSurface("Warning outline", warning.transform, border);
        outline.transform.localPosition = Vector3.up * 0.006f;
        craterMesh = CreateCrater();
        crater = CreateSurface("Impact crater", root.transform, craterMesh);

        dust = DashPresentation.CreateWind(root.transform, dustMaterial, "Jump landing dust");
        dust.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.Billboard;
        var fade = dust.colorOverLifetime;
        fade.color = new Gradient
        {
            alphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f),
                new GradientAlphaKey(0.6f, 0.50f), new GradientAlphaKey(0f, 1f) }
        };
        Reset();
    }

    internal void ShowWarning(bool visible, Vector3 position, Quaternion rotation, float size, float progress)
    {
        warning.SetActive(visible);
        if (!visible)
            return;

        warning.transform.SetPositionAndRotation(position + Vector3.up * 0.048f, rotation);
        warning.transform.localScale = new Vector3(size, 1f, size);
        Tint(fill, Color.Lerp(new Color(0.72f, 0.07f, 0.095f, 0.18f),
            new Color(1f, 0.025f, 0.045f, 0.69f), progress));
        Tint(outline, new Color(1f, 0.12f, 0.15f, Mathf.Lerp(0.62f, 0.96f, progress)));
    }

    internal void Impact(Vector3 position)
    {
        crater.transform.position = position + Vector3.up * 0.025f;
        crater.enabled = true;
        impactTime = Time.time;
        dust.transform.position = position;
        if (!dust.isPlaying) dust.Play();
        for (int i = 0; i < 26; i++)
        {
            float angle = i * Mathf.PI * 2f / 26f + Random.Range(-0.09f, 0.09f);
            var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            dust.Emit(new ParticleSystem.EmitParams
            {
                position = position + Vector3.up * 0.15f + direction * Random.Range(0.28f, 0.9f),
                velocity = direction * Random.Range(0.55f, 1.25f) + Vector3.up * Random.Range(0.22f, 0.56f),
                startSize = Random.Range(0.40f, 0.78f),
                startLifetime = Random.Range(1.35f, Lifetime),
                startColor = new Color(0.60f, 0.48f, 0.34f, Random.Range(0.35f, 0.60f)),
                rotation = Random.Range(0f, 360f)
            }, 1);
        }
    }

    internal void Update()
    {
        float age = Time.time - impactTime;
        crater.enabled = age < Lifetime;
        if (crater.enabled)
            Tint(crater, new Color(1f, 1f, 1f, 1f - Mathf.InverseLerp(0.95f, Lifetime, age)));
    }

    private MeshRenderer CreateSurface(string name, Transform parent, Mesh mesh)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = obj.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = surface;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    private void Tint(MeshRenderer renderer, Color color)
    {
        tint.SetColor(BaseColor, color);
        renderer.SetPropertyBlock(tint);
    }

    private static Mesh CreateSquare(float outer, float inner)
    {
        var mesh = new Mesh { name = inner == 0f ? "Landing square" : "Landing square outline" };
        Vector3[] Corners(float size) => new[]
        {
            new Vector3(-size, 0f, -size), new Vector3(-size, 0f, size),
            new Vector3(size, 0f, size), new Vector3(size, 0f, -size)
        };
        var vertices = new List<Vector3>(Corners(outer));
        var triangles = new List<int>();
        if (inner == 0f)
            triangles.AddRange(new[] { 0, 1, 2, 0, 2, 3 });
        else
        {
            vertices.AddRange(Corners(inner));
            for (int i = 0; i < 4; i++)
            {
                int next = (i + 1) % 4;
                triangles.AddRange(new[] { i, next, next + 4, i, next + 4, i + 4 });
            }
        }
        FillMesh(mesh, vertices, triangles, null);
        return mesh;
    }

    private static Mesh CreateCrater()
    {
        const int sides = 24;
        var vertices = new List<Vector3> { Vector3.zero };
        var colors = new List<Color> { new Color(0.13f, 0.09f, 0.06f, 0.78f) };
        var triangles = new List<int>();
        for (int ring = 0; ring < 3; ring++)
        {
            for (int i = 0; i < sides; i++)
            {
                float angle = i * Mathf.PI * 2f / sides;
                float variation = Mathf.Sin(i * 2.3f) * 0.13f;
                float radius = 0.67f + 0.35f * ring + variation;
                float height = ring == 1 ? 0.12f + 0.10f * Mathf.Abs(Mathf.Sin(i * 1.6f)) : 0.008f;
                vertices.Add(new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius));
                colors.Add(ring == 0 ? new Color(0.21f, 0.15f, 0.10f, 0.9f) :
                    ring == 1 ? new Color(0.39f + variation, 0.28f, 0.18f, 0.94f) :
                    new Color(0.34f, 0.26f, 0.18f, 0.08f));
            }
        }
        for (int i = 0; i < sides; i++)
        {
            int a = 1 + i, b = 1 + (i + 1) % sides;
            triangles.AddRange(new[] { 0, b, a });
            for (int ring = 0; ring < 2; ring++)
            {
                triangles.AddRange(new[] { a, b, b + sides, a, b + sides, a + sides });
                a += sides;
                b += sides;
            }
        }
        var mesh = new Mesh { name = "Crater dirt and broken rim" };
        FillMesh(mesh, vertices, triangles, colors);
        return mesh;
    }

    private static void FillMesh(Mesh mesh, List<Vector3> vertices, List<int> triangles, List<Color> colors)
    {
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.uv = new Vector2[vertices.Count];
        if (colors == null)
        {
            colors = new List<Color>(vertices.Count);
            foreach (var _ in vertices) colors.Add(Color.white);
        }
        mesh.SetColors(colors);
        mesh.RecalculateBounds();
    }

    internal void Reset()
    {
        warning.SetActive(false);
        crater.enabled = false;
        impactTime = float.NegativeInfinity;
        dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    internal void Dispose()
    {
        Object.Destroy(root);
        Object.Destroy(surface);
        Object.Destroy(dustMaterial);
        Object.Destroy(square);
        Object.Destroy(border);
        Object.Destroy(craterMesh);
        Object.Destroy(dustTexture);
    }
}
