using UnityEngine;
using UnityEngine.Rendering;

internal sealed class WizardVfx
{
    private sealed class Skin
    {
        internal Renderer Renderer;
        internal Material[] Original, Transparent;
        internal bool Faded;
    }
    private readonly Skin[] skins;
    private readonly GameObject root;
    private readonly MeshRenderer charge;
    private readonly MeshRenderer[] orbs = new MeshRenderer[2];
    private readonly Light[] lights = new Light[2];
    private readonly uint[] shownIds = new uint[2];
    private readonly Wizard.OrbStatus[] shownStatus = new Wizard.OrbStatus[2];
    private readonly Material material, glowMaterial;
    private readonly Texture2D texture;
    private readonly ParticleSystem glow;
    private readonly MaterialPropertyBlock tint = new MaterialPropertyBlock();
    private float nextGlow;
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

    internal WizardVfx(Transform visual, Material template)
    {
        var renderers = visual.GetComponentsInChildren<Renderer>(true);
        skins = new Skin[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            var original = renderers[i].sharedMaterials;
            var transparent = new Material[original.Length];
            for (int j = 0; j < original.Length; j++)
            {
                var mat = new Material(original[j]) { name = "Wizard teleport fade", renderQueue = 3000 };
                mat.SetFloat("_Surface", 1f); mat.SetFloat("_ZWrite", 0f);
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_ALPHATEST_ON");
                transparent[j] = mat;
            }
            skins[i] = new Skin { Renderer = renderers[i], Original = original, Transparent = transparent };
        }
        root = new GameObject("Wizard light spell VFX");
        root.transform.SetParent(visual.parent, false);
        material = new Material(template) { mainTexture = Texture2D.whiteTexture };
        texture = GiantChargeVfx.SmokeTexture();
        glowMaterial = new Material(template) { mainTexture = texture };
        glowMaterial.SetFloat("_DstBlend", (float)BlendMode.One);
        charge = Sphere("Light charging on staff");
        for (int i = 0; i < orbs.Length; i++)
        {
            orbs[i] = Sphere("Homing light orb " + i);
            var lightObject = new GameObject("Orb light " + i);
            lightObject.transform.SetParent(root.transform, false);
            lights[i] = lightObject.AddComponent<Light>();
            lights[i].color = new Color(0.8f, 0.76f, 1f);
            lights[i].range = 4f;
            lights[i].intensity = 3f;
            lights[i].enabled = false;
        }
        glow = GiantChargeVfx.CreateCloud(root.transform, glowMaterial, "Soft white orb glow", 128);
    }

    private MeshRenderer Sphere(string name)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
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

    internal void Update(float opacity, bool white, bool alive, float progress, Vector3 staff, Wizard.OrbState[] state)
    {
        root.SetActive(alive);
        foreach (var skin in skins)
        {
            bool faded = opacity < 0.999f;
            if (faded != skin.Faded)
            {
                skin.Renderer.sharedMaterials = faded ? skin.Transparent : skin.Original;
                skin.Faded = faded;
            }
            skin.Renderer.enabled = alive && opacity > 0.002f;
            skin.Renderer.shadowCastingMode = opacity > 0.95f && alive ? ShadowCastingMode.On : ShadowCastingMode.Off;
            for (int i = 0; i < skin.Original.Length; i++)
            {
                var source = skin.Original[i];
                tint.Clear();
                Color color = white ? new Color(2f, 2f, 2f, opacity) : source.GetColor(BaseColor);
                color.a = opacity;
                tint.SetColor(BaseColor, color);
                tint.SetTexture(BaseMap, white ? Texture2D.whiteTexture : source.GetTexture(BaseMap) ?? Texture2D.whiteTexture);
                skin.Renderer.SetPropertyBlock(tint, i);
            }
        }
        bool emit = Time.time >= nextGlow;
        if (emit) nextGlow = Time.time + 0.035f;
        Show(charge, alive && progress > 0f, staff, Mathf.Lerp(0.1f, 0.6f, progress));
        if (alive && progress > 0f && emit)
            Glow(staff, Mathf.Lerp(0.3f, 1.05f, progress), 0.15f);
        for (int i = 0; i < orbs.Length; i++)
        {
            bool flying = alive && state[i].Status == Wizard.OrbStatus.Flying;
            Show(orbs[i], flying, state[i].Position, 0.9f);
            lights[i].enabled = flying;
            lights[i].transform.position = state[i].Position;
            if (flying && emit) Glow(state[i].Position, 1.65f, 0.14f);
            if (alive && state[i].Status == Wizard.OrbStatus.Impact &&
                (shownIds[i] != state[i].Id || shownStatus[i] != state[i].Status))
            {
                for (int p = 0; p < 16; p++)
                    GiantChargeVfx.Emit(glow, state[i].Position, Random.insideUnitSphere * 3.4f,
                        new Color(1.5f, 1.25f, 2f, 0.7f), Random.Range(0.15f, 0.5f), Random.Range(0.15f, 0.35f));
            }
            shownIds[i] = state[i].Id;
            shownStatus[i] = state[i].Status;
        }
    }

    private void Show(MeshRenderer sphere, bool show, Vector3 position, float size)
    {
        sphere.gameObject.SetActive(show);
        if (!show) return;
        sphere.transform.position = position;
        sphere.transform.localScale = Vector3.one * size;
        tint.Clear();
        tint.SetColor(BaseColor, new Color(3f, 2.8f, 3.5f, 1f));
        sphere.SetPropertyBlock(tint);
    }
    private void Glow(Vector3 position, float size, float life) =>
        GiantChargeVfx.Emit(glow, position, Vector3.zero, new Color(0.95f, 0.8f, 1.4f, 0.5f), size, life);
    internal void Hide() => root.SetActive(false);
    internal void Dispose()
    {
        foreach (var skin in skins)
        {
            if (skin.Renderer != null)
            {
                skin.Renderer.sharedMaterials = skin.Original;
                skin.Renderer.SetPropertyBlock(null);
            }
            foreach (var mat in skin.Transparent) Object.Destroy(mat);
        }
        Object.Destroy(root);
        Object.Destroy(material);
        Object.Destroy(glowMaterial);
        Object.Destroy(texture);
    }
}
