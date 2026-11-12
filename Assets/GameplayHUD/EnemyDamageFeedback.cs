using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

internal sealed class EnemyDamageFeedback
{
    private const float NumberLifetime = 0.9f;

    private sealed class FloatingHit
    {
        internal Text Label;
        internal Vector3 Anchor;
        internal float Started;
        internal float Side;
        internal float Stagger;
        internal Color Tint;
    }

    private readonly RectTransform canvas;
    private readonly Camera camera;
    private readonly RectTransform root;
    private readonly RectTransform bar;
    private readonly RectTransform fill;
    private readonly Font font;
    private readonly List<FloatingHit> hits = new List<FloatingHit>();
    private float nextSide = 1f;

    internal EnemyDamageFeedback(RectTransform canvas, Camera camera)
    {
        this.canvas = canvas;
        this.camera = camera;
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        root = CreateRect("LungeMan Combat UI", canvas);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;

        bar = CreateRect("Health Bar", root);
        bar.sizeDelta = new Vector2(104f, 20f);
        AddImage(bar, new Color32(10, 12, 12, 255));

        var track = CreateRect("Track", bar);
        track.anchorMin = Vector2.zero;
        track.anchorMax = Vector2.one;
        track.offsetMin = new Vector2(4f, 4f);
        track.offsetMax = new Vector2(-4f, -4f);
        AddImage(track, new Color32(91, 98, 100, 255));

        fill = CreateRect("Red Fill", track);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        fill.offsetMin = fill.offsetMax = Vector2.zero;
        AddImage(fill, new Color32(224, 36, 40, 255));
        bar.gameObject.SetActive(false);
    }

    internal void ShowHit(Vector3 position, int amount, LungeMan.CriticalHit critical)
    {
        if (root == null)
            return;

        var rect = CreateRect("Damage Number", root);
        rect.sizeDelta = new Vector2(160f, 72f);
        var label = rect.gameObject.AddComponent<Text>();
        label.font = font;
        label.fontStyle = FontStyle.Bold;
        label.fontSize = critical == LungeMan.CriticalHit.High ? 48
            : critical == LungeMan.CriticalHit.Medium ? 42 : 36;
        label.alignment = TextAnchor.MiddleCenter;
        label.raycastTarget = false;
        label.text = $"-{amount}";
        var outline = rect.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color32(12, 10, 8, 255);
        outline.effectDistance = new Vector2(3f, -3f);

        Color tint = critical == LungeMan.CriticalHit.High ? new Color32(255, 223, 88, 255)
            : critical == LungeMan.CriticalHit.Medium ? new Color32(255, 155, 87, 255)
            : new Color32(255, 240, 220, 255);
        float now = Time.time;
        float stagger = 0f;
        foreach (var active in hits)
            if (active.Side == nextSide && now - active.Started < 0.25f)
                stagger = Mathf.Max(stagger, active.Stagger + 46f);

        hits.Add(new FloatingHit
        {
            Label = label,
            Anchor = position + Vector3.up * 1.85f,
            Started = now,
            Side = nextSide,
            Stagger = stagger,
            Tint = tint
        });
        nextSide = -nextSide;
    }

    internal void Update(Vector3 position, int health, int maxHealth, bool hasTakenDamage)
    {
        if (root == null)
            return;

        root.gameObject.SetActive(true);
        bool visible = hasTakenDamage && health > 0
            && Place(bar, position + Vector3.up * 2.38f, Vector2.zero);
        bar.gameObject.SetActive(visible);
        fill.anchorMax = new Vector2((float)health / maxHealth, 1f);

        for (int i = hits.Count - 1; i >= 0; i--)
        {
            var hit = hits[i];
            float progress = (Time.time - hit.Started) / NumberLifetime;
            if (progress >= 1f)
            {
                Object.Destroy(hit.Label.gameObject);
                hits.RemoveAt(i);
                continue;
            }

            float sideways = 62f + 19f * progress + hit.Stagger * 0.6f;
            var offset = new Vector2(hit.Side * sideways, 8f + hit.Stagger + 52f * progress);
            var rect = hit.Label.rectTransform;
            rect.gameObject.SetActive(Place(rect, hit.Anchor, offset));
            rect.localScale = Vector3.one * Mathf.Lerp(1.25f, 1f, Mathf.Clamp01(progress * 7f));
            var color = hit.Tint;
            color.a = 1f - Mathf.InverseLerp(0.64f, 1f, progress);
            hit.Label.color = color;
        }
    }

    internal void Hide()
    {
        if (root != null)
            root.gameObject.SetActive(false);
    }

    internal void Dispose()
    {
        if (root != null)
            Object.Destroy(root.gameObject);
    }

    private bool Place(RectTransform rect, Vector3 world, Vector2 offset)
    {
        if (camera == null)
            return false;

        Vector3 screen = camera.WorldToScreenPoint(world);
        if (screen.z <= 0f || !RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, null, out var local))
            return false;

        rect.anchoredPosition = local + offset;
        return true;
    }

    private static RectTransform CreateRect(string name, RectTransform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        return rect;
    }

    private static void AddImage(RectTransform rect, Color color)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }
}
