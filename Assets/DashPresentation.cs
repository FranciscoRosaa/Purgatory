using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed class DashPresentation
{
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

    private readonly Transform visual;
    private readonly SkinnedMeshRenderer[] skins;
    private readonly Renderer[] renderers;
    private readonly MaterialPropertyBlock[] originalAppearance;
    private readonly MaterialPropertyBlock whiteAppearance = new MaterialPropertyBlock();
    private readonly Mesh pose = new Mesh { name = "Dash ground probe" };
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly ParticleSystem wind;
    private Vector3 lastWindPosition;
    private bool flashing;
    private Vector3 uncorrectedPosition;
    private Vector3 correctedPosition;
    private bool lifted;

    internal DashPresentation(Transform visual, Transform player, Material windMaterial)
    {
        this.visual = visual;
        skins = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        renderers = visual.GetComponentsInChildren<Renderer>(true);
        originalAppearance = new MaterialPropertyBlock[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
            originalAppearance[i] = new MaterialPropertyBlock();

        if (windMaterial != null)
            wind = CreateWind(player, windMaterial);
    }

    internal void Begin(Vector3 position, Vector3 direction)
    {
        SetFlash(true);
        lastWindPosition = position;
        EmitWind(position, direction, 8);
    }

    internal void Update(Vector3 position, Vector3 direction, float groundHeight, bool rolling, bool dashing)
    {
        ClearGroundLift();
        if (rolling || dashing)
        {
            float lift = groundHeight + 0.015f - LowestVertex();
            if (lift > 0f)
            {
                uncorrectedPosition = visual.localPosition;
                visual.position += Vector3.up * lift;
                correctedPosition = visual.localPosition;
                lifted = true;
            }
        }

        SetFlash(dashing);
        if (dashing && (position - lastWindPosition).sqrMagnitude >= 0.36f)
        {
            EmitWind(position, direction, 4);
            lastWindPosition = position;
        }
    }

    private void ClearGroundLift()
    {
        if (lifted && visual != null && visual.localPosition == correctedPosition)
            visual.localPosition = uncorrectedPosition;
        lifted = false;
    }

    private float LowestVertex()
    {
        float lowest = float.PositiveInfinity;
        foreach (var skin in skins)
        {
            if (!skin.enabled || !skin.gameObject.activeInHierarchy || skin.sharedMesh == null)
                continue;

            // Compensate for the rig scale before transforming the baked vertices to world space.
            skin.BakeMesh(pose, true);
            pose.GetVertices(vertices);
            var localToWorld = skin.localToWorldMatrix;
            foreach (var vertex in vertices)
                lowest = Mathf.Min(lowest, localToWorld.MultiplyPoint3x4(vertex).y);
        }
        return lowest;
    }

    private void SetFlash(bool active)
    {
        if (active == flashing)
            return;

        flashing = active;
        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null)
                continue;

            if (active)
            {
                renderer.GetPropertyBlock(originalAppearance[i]);
                renderer.GetPropertyBlock(whiteAppearance);
                whiteAppearance.SetColor(BaseColor, new Color(1.75f, 1.75f, 1.75f, 1f));
                whiteAppearance.SetTexture(BaseMap, Texture2D.whiteTexture);
                renderer.SetPropertyBlock(whiteAppearance);
            }
            else
            {
                renderer.SetPropertyBlock(originalAppearance[i]);
            }
        }
    }

    private static ParticleSystem CreateWind(Transform parent, Material material)
    {
        var effect = new GameObject("Dash Wind");
        effect.transform.SetParent(parent, false);
        var particles = effect.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = particles.main;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        main.maxParticles = 64;

        var emission = particles.emission;
        emission.enabled = false;
        var shape = particles.shape;
        shape.enabled = false;

        var fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        var color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = fade;

        var renderer = effect.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 3.2f;
        renderer.velocityScale = 0.16f;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        particles.Play();
        return particles;
    }

    private void EmitWind(Vector3 position, Vector3 direction, int count)
    {
        if (wind == null)
            return;

        if (!wind.isPlaying)
            wind.Play();
        Vector3 side = Vector3.Cross(Vector3.up, direction);
        for (int i = 0; i < count; i++)
        {
            float lateral = Random.Range(0.24f, 0.68f) * (i % 2 == 0 ? -1f : 1f);
            wind.Emit(new ParticleSystem.EmitParams
            {
                position = position - direction * Random.Range(0.15f, 0.6f) +
                    side * lateral + Vector3.up * Random.Range(0.18f, 1.15f),
                velocity = -direction * Random.Range(3.2f, 6f) + side * lateral,
                startSize = Random.Range(0.10f, 0.18f),
                startLifetime = Random.Range(0.17f, 0.32f),
                startColor = new Color32(220, 249, 255, 230)
            }, 1);
        }
    }

    internal void Reset()
    {
        SetFlash(false);
        ClearGroundLift();
        if (wind != null)
            wind.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    internal void Dispose()
    {
        Object.Destroy(pose);
        if (wind != null)
            Object.Destroy(wind.gameObject);
    }
}
