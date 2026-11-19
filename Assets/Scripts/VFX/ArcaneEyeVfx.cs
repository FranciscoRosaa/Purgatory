using UnityEngine;
using UnityEngine.Rendering;

internal sealed class ArcaneEyeVfx
{
    private readonly GameObject root;
    private readonly MeshRenderer beam, core, warning;
    private readonly MeshRenderer[] falls = new MeshRenderer[12];
    private readonly MeshRenderer[] markers = new MeshRenderer[12];
    private readonly MeshRenderer[] impacts = new MeshRenderer[12];
    private readonly Material material;
    private readonly MaterialPropertyBlock tint = new MaterialPropertyBlock();
    private readonly Light chargeLight;
    private readonly MeshRenderer aura;
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    internal ArcaneEyeVfx(Transform owner, Material template)
    {
        root = new GameObject("Arcane Eye laser effects");
        root.transform.SetParent(owner, false);
        material = new Material(template);
        material.mainTexture = Texture2D.whiteTexture;
        warning = Shape("Laser warning", PrimitiveType.Cube);
        beam = Shape("Purple laser", PrimitiveType.Cube);
        core = Shape("White laser core", PrimitiveType.Cube);
        aura = Shape("Charging eye glow", PrimitiveType.Sphere);
        for (int i = 0; i < falls.Length; i++)
        {
            markers[i] = Shape("Falling laser warning " + i, PrimitiveType.Cube);
            falls[i] = Shape("Falling laser " + i, PrimitiveType.Cube);
            impacts[i] = Shape("Laser impact flash " + i, PrimitiveType.Sphere);
        }
        chargeLight = new GameObject("Arcane charge light").AddComponent<Light>();
        chargeLight.transform.SetParent(root.transform, false);
        chargeLight.color = new Color(0.73f, 0.04f, 1f);
        chargeLight.range = 6f;
        chargeLight.enabled = false;
    }

    private MeshRenderer Shape(string name, PrimitiveType type)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(root.transform, false);
        var collider = go.GetComponent<Collider>();
        collider.enabled = false;
        Object.Destroy(collider);
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        go.SetActive(false);
        return renderer;
    }

    private void Show(MeshRenderer shape, bool visible, Vector3 point, Vector3 size, Color color, Quaternion rotation)
    {
        shape.gameObject.SetActive(visible);
        if (!visible) return;
        shape.transform.SetPositionAndRotation(point, rotation);
        shape.transform.localScale = size;
        tint.SetColor(BaseColor, color);
        shape.SetPropertyBlock(tint);
    }

    internal void Update(ArcaneEye.Phase phase, ArcaneEye.Attack attack, Vector3 origin, Vector3 direction,
        float age, float charge, Vector3[] points, int count, Vector3 eye)
    {
        root.SetActive(phase != ArcaneEye.Phase.Dead);
        if (!root.activeSelf) return;
        bool firing = phase == ArcaneEye.Phase.Beam;
        bool aiming = phase == ArcaneEye.Phase.Warning;
        bool falling = phase == ArcaneEye.Phase.Falling;
        var orientation = direction.sqrMagnitude > 0.001f ? Quaternion.LookRotation(direction) : Quaternion.identity;
        float width = ArcaneEye.BeamWidth;
        Vector3 center = origin + direction * (ArcaneEye.BeamLength * 0.5f);
        Show(warning, aiming, center + Vector3.up * 0.035f,
            new Vector3(width, 0.035f, ArcaneEye.BeamLength),
            Color.Lerp(new Color(0.8f, 0.48f, 1f, 0.2f), new Color(1f, 0.63f, 1f, 0.65f),
                Mathf.Clamp01(age / ArcaneEye.WarningDuration)), orientation);
        float expansion = Mathf.SmoothStep(0.06f, 1f, Mathf.Clamp01(age / 0.14f)) *
            (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ArcaneEye.BeamDuration - 0.18f, ArcaneEye.BeamDuration, age)));
        float pulse = 1f + Mathf.Sin(age * 53f) * 0.045f;
        float thickness = expansion * pulse;
        Vector3 beamStart = origin + Vector3.up * 2.3f;
        Vector3 beamEnd = origin + direction * ArcaneEye.BeamLength + Vector3.up * 1.1f;
        Vector3 beamCenter = (beamStart + beamEnd) * 0.5f;
        Quaternion beamRotation = Quaternion.LookRotation(beamEnd - beamStart);
        float length = Vector3.Distance(beamStart, beamEnd);
        Show(beam, firing, beamCenter,
            new Vector3(width * thickness, 0.85f * thickness, length), new Color(1.45f, 0.04f, 2.5f, 0.7f), beamRotation);
        Show(core, firing, beamCenter,
            new Vector3(width * 0.34f * thickness, 0.32f * thickness, length),
            new Color(3f, 1.8f, 3.5f, 0.95f), beamRotation);
        bool charging = phase == ArcaneEye.Phase.Charge;
        float glowSize = charging ? 0.25f + charge * 0.65f : thickness * (0.8f + Mathf.Exp(-age * 18f));
        Show(aura, charging || firing, eye + Vector3.up * 2.3f,
            Vector3.one * glowSize, new Color(1.3f, 0.15f, 2.3f, 0.45f), Quaternion.identity);
        chargeLight.enabled = phase == ArcaneEye.Phase.Charge || firing;
        chargeLight.transform.position = eye + Vector3.up * 2.3f;
        chargeLight.intensity = firing ? (5f + Mathf.Exp(-age * 16f) * 8f) * expansion : charge * 4f;
        for (int i = 0; i < falls.Length; i++)
        {
            float release = ArcaneEye.FallWarning + i * ArcaneEye.FallInterval;
            bool showMarker = falling && i < count && age < release;
            bool showLaser = falling && i < count && age >= release && age < release + ArcaneEye.FallDuration;
            float laserAge = age - release;
            float growth = Mathf.SmoothStep(0.05f, 1f, Mathf.Clamp01(laserAge / 0.055f));
            float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, ArcaneEye.FallDuration, laserAge));
            float progress = Mathf.Clamp01(age / release);
            Show(markers[i], showMarker, points[i] + Vector3.up * 0.04f,
                new Vector3(ArcaneEye.FallWidth, 0.04f, ArcaneEye.FallWidth),
                Color.Lerp(new Color(1f, 0.05f, 0.08f, 0.18f), new Color(1f, 0.04f, 0.1f, 0.75f), progress), Quaternion.identity);
            Show(falls[i], showLaser, points[i] + Vector3.up * 4f,
                new Vector3(ArcaneEye.FallWidth * 0.65f * growth * fade, 8f, ArcaneEye.FallWidth * 0.65f * growth * fade),
                new Color(2.6f, 0.16f, 3.5f, 0.9f), Quaternion.identity);
            float impactSize = Mathf.Lerp(0.15f, ArcaneEye.FallWidth * 1.35f, Mathf.Clamp01(laserAge / 0.2f));
            Show(impacts[i], falling && i < count && laserAge >= 0f && laserAge < 0.27f,
                points[i] + Vector3.up * 0.12f, new Vector3(impactSize, 0.18f, impactSize),
                new Color(1.8f, 0.3f, 2.5f, (1f - Mathf.Clamp01(laserAge / 0.27f)) * 0.8f), Quaternion.identity);
        }
    }

    internal void Hide() => root.SetActive(false);
    internal void Dispose()
    {
        Object.Destroy(root);
        Object.Destroy(material);
    }
}
