using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed class DashPresentation
{
    private readonly Transform visual;
    private readonly SkinnedMeshRenderer[] skins;
    private readonly CharacterWhiteFlash flash;
    private readonly Mesh pose = new Mesh { name = "Dash ground probe" };
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly ParticleSystem wind;
    private Vector3 lastWindPosition;
    private Vector3 uncorrectedPosition;
    private Vector3 correctedPosition;
    private bool lifted;

    internal DashPresentation(Transform visual, Transform player, Material windMaterial)
    {
        this.visual = visual;
        skins = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        flash = new CharacterWhiteFlash(visual);

        if (windMaterial != null)
            wind = CreateWind(player, windMaterial);
    }

    internal void Begin(Vector3 position, Vector3 direction)
    {
        flash.Update(true);
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

        flash.Update(dashing);
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

    internal void FlashMelee() => flash.Pulse();

    internal static ParticleSystem CreateWind(Transform parent, Material material, string name = "Dash Wind")
    {
        var effect = new GameObject(name);
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
        flash.Reset();
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

internal sealed class CharacterWhiteFlash
{
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
    private const float PulseDuration = 0.09f;

    private readonly Renderer[] renderers;
    private readonly MaterialPropertyBlock[] original;
    private readonly MaterialPropertyBlock white = new MaterialPropertyBlock();
    private float pulseUntil;

    internal bool Active { get; private set; }

    internal CharacterWhiteFlash(Transform visual)
    {
        renderers = visual.GetComponentsInChildren<Renderer>(true);
        original = new MaterialPropertyBlock[renderers.Length];
        for (int i = 0; i < original.Length; i++)
            original[i] = new MaterialPropertyBlock();
    }

    internal void Pulse()
    {
        pulseUntil = Time.time + PulseDuration;
        SetWhite(true);
    }

    internal void Update(bool sustained = false) => SetWhite(sustained || Time.time < pulseUntil);

    internal void Reset()
    {
        pulseUntil = 0f;
        SetWhite(false);
    }

    private void SetWhite(bool active)
    {
        if (active == Active)
            return;

        Active = active;
        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null)
                continue;
            if (!active)
            {
                renderer.SetPropertyBlock(original[i]);
                continue;
            }

            renderer.GetPropertyBlock(original[i]);
            renderer.GetPropertyBlock(white);
            white.SetColor(BaseColor, new Color(1.75f, 1.75f, 1.75f, 1f));
            white.SetTexture(BaseMap, Texture2D.whiteTexture);
            renderer.SetPropertyBlock(white);
        }
    }
}
