using UnityEngine;
using UnityEngine.Rendering;

internal sealed class HealerEffects
{
    private const float Duration = 0.85f;
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private readonly Transform aura;
    private readonly MeshRenderer auraRenderer;
    private readonly Light glow;
    private readonly Material material;
    private readonly Texture2D texture;
    private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
    private Transform recipient;
    private Vector3 glowOffset;
    private float glowHeight, glowWidth, started;

    internal HealerEffects(Transform healer, Material template)
    {
        texture = CreateGlowTexture();
        material = new Material(template) { mainTexture = texture };
        material.SetFloat("_DstBlend", (float)BlendMode.One);

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "TrueHealer green healing glow";
        aura = quad.transform;
        aura.SetParent(healer, false);
        var collider = quad.GetComponent<Collider>();
        collider.enabled = false;
        Object.Destroy(collider);
        auraRenderer = quad.GetComponent<MeshRenderer>();
        auraRenderer.sharedMaterial = material;
        auraRenderer.shadowCastingMode = ShadowCastingMode.Off;
        auraRenderer.receiveShadows = false;

        glow = new GameObject("Healing light").AddComponent<Light>();
        glow.transform.SetParent(aura, false);
        glow.type = LightType.Point;
        glow.color = new Color(0.15f, 1f, 0.27f);
        glow.shadows = LightShadows.None;
        quad.SetActive(false);
    }

    internal float Heal(Transform target)
    {
        recipient = target;
        Bounds bounds = new Bounds(target.position + Vector3.up, new Vector3(1f, 2f, 1f));
        var animator = target.GetComponentInChildren<Animator>();
        if (animator != null)
        {
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head != null)
            {
                float height = Mathf.Max(1f, head.position.y - target.position.y + 0.32f);
                bounds = new Bounds(target.position + Vector3.up * (height * 0.5f),
                    new Vector3(height * 0.6f, height, height * 0.6f));
            }
        }
        glowOffset = bounds.center - target.position;
        glowHeight = bounds.size.y * 1.45f;
        glowWidth = bounds.size.x * 1.85f;
        glow.range = bounds.size.y * 1.7f;
        started = Time.time;
        aura.gameObject.SetActive(true);
        return bounds.max.y - target.position.y + 0.15f;
    }

    internal void Update()
    {
        if (!aura.gameObject.activeSelf)
            return;
        float progress = (Time.time - started) / Duration;
        if (recipient == null || progress >= 1f)
        {
            aura.gameObject.SetActive(false);
            return;
        }

        aura.position = recipient.position + glowOffset;
        if (Camera.main != null)
            aura.rotation = Camera.main.transform.rotation;
        float expansion = Mathf.Lerp(0.93f, 1.15f, progress);
        aura.localScale = new Vector3(glowWidth * expansion, glowHeight * expansion, 1f);
        float fade = 1f - Mathf.SmoothStep(0f, 1f, progress);
        properties.SetColor(BaseColor, new Color(0.24f, 1.45f, 0.37f, fade * 0.74f));
        auraRenderer.SetPropertyBlock(properties);
        glow.intensity = 3.4f * fade;
    }

    private static Texture2D CreateGlowTexture()
    {
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        { name = "Soft heal glow", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                var offset = new Vector2(x + 0.5f, y + 0.5f) * (2f / size) - Vector2.one;
                float edge = 1f - Mathf.SmoothStep(0.27f, 1f, offset.magnitude);
                pixels[y * size + x] = new Color(1f, 1f, 1f, edge * edge);
            }
        }
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return texture;
    }

    internal void Dispose()
    {
        Object.Destroy(aura.gameObject);
        Object.Destroy(material);
        Object.Destroy(texture);
    }
}
