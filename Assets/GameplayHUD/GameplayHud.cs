using UnityEngine;
using UnityEngine.UI;

public sealed class GameplayHud : MonoBehaviour
{
    internal const int StartingHealth = 100;
    private const string CameraShakeKey = "Purgatory.Gameplay.CameraShake";
    private const float DamageFlashDuration = 0.55f;
    private const float DodgeFlashDuration = 0.28f;
    private const float DodgeLabelDuration = 0.65f;
    private const float ShakeDuration = 0.26f;

    [SerializeField] private RectTransform healthFill;
    [SerializeField] private Text healthLabel;
    [SerializeField] private Text timerLabel;
    [SerializeField] private Text waveLabel;
    [SerializeField, Min(1)] private int maxHealth = StartingHealth;
    [SerializeField, Min(0)] private int health = StartingHealth;
    [SerializeField, Min(1)] private int wave = 1;
    [SerializeField, Min(1)] private int totalWaves = 15;

    private double startTime;
    private int displayedSeconds = -1;
    private float damageTime = float.NegativeInfinity;
    private float dodgeTime = float.NegativeInfinity;
    private RawImage damageVignette;
    private Text dodgeLabel;
    private Transform dodgeTarget;
    private Texture2D vignetteTexture;
    private Camera gameplayCamera;
    private Transform cameraTransform;
    private Vector3 cameraOffset;

    internal int Health => health;

    internal static bool CameraShakeEnabled
    {
        get => PlayerPrefs.GetInt(CameraShakeKey, 1) != 0;
        set
        {
            PlayerPrefs.SetInt(CameraShakeKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    internal void TakeDamage(int amount) => SetHealth(health - amount);

    internal void ShowPerfectDodge(Transform target)
    {
        dodgeTarget = target;
        dodgeTime = Time.unscaledTime;
    }

    internal void SetHealth(int value)
    {
        int fullHealth = Mathf.Max(1, maxHealth);
        int newHealth = Mathf.Clamp(value, 0, fullHealth);
        if (newHealth < health)
            damageTime = Time.unscaledTime;
        health = newHealth;
        healthFill.anchorMax = new Vector2((float)health / fullHealth, 1f);
        healthLabel.text = $"{health} / {fullHealth}";
    }

    private void Start()
    {
        gameplayCamera = Camera.main;
        if (gameplayCamera != null)
            cameraTransform = gameplayCamera.transform;
        CreateDamageVignette();
        CreateDodgeLabel();
        SetHealth(health);
        int waveCount = Mathf.Max(1, totalWaves);
        waveLabel.text = $"{Mathf.Clamp(wave, 1, waveCount):D2}/{waveCount:D2}";
        startTime = Time.timeAsDouble;
        UpdateTimer(0);
    }

    private void Update() => UpdateTimer((int)(Time.timeAsDouble - startTime));

    private void LateUpdate()
    {
        float age = Time.unscaledTime - damageTime;
        float dodgeAge = Time.unscaledTime - dodgeTime;
        bool dodgeFlash = dodgeAge < DodgeFlashDuration && dodgeTime >= damageTime;
        damageVignette.enabled = dodgeFlash || age < DamageFlashDuration;
        if (dodgeFlash)
        {
            float fade = 1f - dodgeAge / DodgeFlashDuration;
            damageVignette.color = new Color(0.04f, 0.47f, 1f, 0.8f * fade * fade);
        }
        else if (damageVignette.enabled)
        {
            float fade = 1f - age / DamageFlashDuration;
            damageVignette.color = new Color(0.85f, 0.025f, 0.045f, 0.85f * fade * fade);
        }
        UpdateDodgeLabel(dodgeAge);

        ResetCameraOffset();
        if (cameraTransform == null || age >= ShakeDuration || !CameraShakeEnabled)
            return;

        float strength = 1f - age / ShakeDuration;
        var shake = new Vector3(Mathf.Cos(age * 91f) * 0.20f, Mathf.Sin(age * 113f) * 0.14f, 0f);
        cameraOffset = cameraTransform.localRotation * (shake * (strength * strength));
        cameraTransform.localPosition += cameraOffset;
    }

    private void CreateDodgeLabel()
    {
        var labelObject = new GameObject("Perfect Dodge", typeof(Text), typeof(Outline));
        dodgeLabel = labelObject.GetComponent<Text>();
        var rect = dodgeLabel.rectTransform;
        rect.SetParent(transform, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(180f, 55f);
        dodgeLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        dodgeLabel.fontSize = 32;
        dodgeLabel.fontStyle = FontStyle.Bold;
        dodgeLabel.alignment = TextAnchor.MiddleCenter;
        dodgeLabel.text = "DODGE";
        dodgeLabel.raycastTarget = false;
        dodgeLabel.enabled = false;
        var outline = labelObject.GetComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(3f, -3f);
    }

    private void UpdateDodgeLabel(float age)
    {
        if (dodgeTarget == null || gameplayCamera == null || age >= DodgeLabelDuration)
        {
            dodgeLabel.enabled = false;
            return;
        }

        Vector3 screen = gameplayCamera.WorldToScreenPoint(dodgeTarget.position + Vector3.up * 0.15f);
        if (screen.z <= 0f || !RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, screen, null, out var local))
        {
            dodgeLabel.enabled = false;
            return;
        }

        dodgeLabel.rectTransform.anchoredPosition = local + new Vector2(0f, -24f + 15f * age / DodgeLabelDuration);
        dodgeLabel.color = new Color(0.16f, 0.77f, 1f, 1f - Mathf.InverseLerp(0.36f, DodgeLabelDuration, age));
        dodgeLabel.enabled = true;
    }

    private void CreateDamageVignette()
    {
        const int size = 128;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            float vertical = Mathf.Abs(2f * y / (size - 1) - 1f);
            for (int x = 0; x < size; x++)
            {
                float horizontal = Mathf.Abs(2f * x / (size - 1) - 1f);
                float edge = Mathf.Max(horizontal, vertical);
                float opacity = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.64f, 1f, edge));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * opacity));
            }
        }

        vignetteTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Damage vignette mask",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        vignetteTexture.SetPixels32(pixels);
        vignetteTexture.Apply(false, true);

        damageVignette = new GameObject("Damage vignette", typeof(RawImage)).GetComponent<RawImage>();
        var rect = damageVignette.rectTransform;
        rect.SetParent(transform, false);
        rect.SetAsFirstSibling();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        damageVignette.texture = vignetteTexture;
        damageVignette.raycastTarget = false;
        damageVignette.enabled = false;
    }

    private void ResetCameraOffset()
    {
        if (cameraTransform != null && cameraOffset.sqrMagnitude > 0f)
            cameraTransform.localPosition -= cameraOffset;
        cameraOffset = Vector3.zero;
    }

    private void OnDisable()
    {
        ResetCameraOffset();
        if (damageVignette != null)
            damageVignette.enabled = false;
        if (dodgeLabel != null)
            dodgeLabel.enabled = false;
    }

    private void OnDestroy()
    {
        if (damageVignette != null)
            Destroy(damageVignette.gameObject);
        if (dodgeLabel != null)
            Destroy(dodgeLabel.gameObject);
        if (vignetteTexture != null)
            Destroy(vignetteTexture);
    }

    private void UpdateTimer(int elapsedSeconds)
    {
        if (elapsedSeconds == displayedSeconds)
            return;

        displayedSeconds = elapsedSeconds;
        timerLabel.text = $"{elapsedSeconds / 60:D2}:{elapsedSeconds % 60:D2}";
    }
}
