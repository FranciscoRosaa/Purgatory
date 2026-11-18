using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public sealed class WorldMenuPresentation : MonoBehaviour
{
    private sealed class ButtonMotion
    {
        public Button Button;
        public RectTransform Rect;
        public Vector3 Position;
        public CanvasGroup Group;
        public int Order;
    }

    private sealed class PanelMotion
    {
        public GameObject Object;
        public RectTransform Rect;
        public Vector3 Position;
        public CanvasGroup Group;
        public bool WasActive;
        public float Opened;
    }

    private readonly List<ButtonMotion> buttons = new List<ButtonMotion>();
    private readonly List<PanelMotion> panels = new List<PanelMotion>();
    private Canvas canvas;
    private Camera menuCamera;
    private Transform layout, actions, settings, credits, intro, subtitle;
    private Vector3 canvasPosition, cameraPosition, previewPosition, canvasScale;
    private Transform preview, customization;
    private Quaternion previewRotation;
    private float characterYaw;

    public void RotateCharacter(float degrees) => characterYaw += degrees;
    public void ResetCharacterRotation() => characterYaw = 0f;
    private Quaternion canvasRotation, cameraRotation;
    private bool reducedMotion;
    private float entrance = -1f;
    private const string MotionKey = "Purgatory.Menu.ReducedMotion";
    private const string VolumeKey = "Purgatory.Audio.Volume";

    private void Start()
    {
        canvas = GetComponent<Canvas>();
        menuCamera = canvas.worldCamera;
        canvasPosition = transform.position;
        canvasRotation = transform.rotation;
        canvasScale = transform.localScale;
        cameraPosition = menuCamera.transform.position;
        cameraRotation = menuCamera.transform.rotation;
        layout = transform.Find("Layout");
        actions = layout.Find("Actions");
        customization = layout.Find("Character Customizer");
        preview = GameObject.Find("Personagem em exposição").transform;
        previewPosition = preview.position;
        previewRotation = preview.rotation;
        subtitle = layout.Find("World subtitle");
        settings = layout.Find("Settings Panel");
        credits = layout.Find("Credits Panel");
        var introObject = GameObject.Find("Active Creators Intro");
        intro = introObject != null ? introObject.transform : null;
        reducedMotion = PlayerPrefs.GetInt(MotionKey, 0) != 0;
        AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));

        int order = 0;
        foreach (var button in GetComponentsInChildren<Button>(true))
        {
            var rect = (RectTransform)button.transform;
            var group = button.GetComponent<CanvasGroup>();
            if (group == null) group = button.gameObject.AddComponent<CanvasGroup>();
            buttons.Add(new ButtonMotion { Button = button, Rect = rect,
                Position = rect.localPosition, Group = group, Order = order++ });
        }
        foreach (var name in new[] { "Actions", "Mode Picker", "Character Customizer", "Settings Panel", "Credits Panel" })
        {
            var panel = layout.Find(name);
            var group = panel.GetComponent<CanvasGroup>();
            if (group == null) group = panel.gameObject.AddComponent<CanvasGroup>();
            panels.Add(new PanelMotion { Object = panel.gameObject, Rect = (RectTransform)panel,
                Position = panel.localPosition, Group = group });
        }

        actions.Find("Options").GetComponent<Button>().onClick.AddListener(() => Open(settings));
        actions.Find("Credits").GetComponent<Button>().onClick.AddListener(() => Open(credits));
        settings.Find("Back").GetComponent<Button>().onClick.AddListener(CloseExtra);
        credits.Find("Back").GetComponent<Button>().onClick.AddListener(CloseExtra);
        settings.Find("Volume Minus").GetComponent<Button>().onClick.AddListener(() => ChangeVolume(-0.1f));
        settings.Find("Volume Plus").GetComponent<Button>().onClick.AddListener(() => ChangeVolume(0.1f));
        settings.Find("Motion").GetComponent<Button>().onClick.AddListener(ToggleMotion);
        settings.Find("Camera Shake").GetComponent<Button>().onClick.AddListener(() =>
        {
            GameplayHud.CameraShakeEnabled = !GameplayHud.CameraShakeEnabled;
            RefreshSettings();
        });
        settings.Find("Fullscreen").GetComponent<Button>().onClick.AddListener(() =>
        {
            Screen.fullScreen = !Screen.fullScreen;
            RefreshSettings();
        });
        RefreshSettings();
    }

    private void Open(Transform panel)
    {
        RefreshSettings();
        actions.gameObject.SetActive(false);
        settings.gameObject.SetActive(panel == settings);
        credits.gameObject.SetActive(panel == credits);
        EventSystem.current?.SetSelectedGameObject(panel.Find("Back").gameObject);
    }

    private void CloseExtra()
    {
        settings.gameObject.SetActive(false);
        credits.gameObject.SetActive(false);
        actions.gameObject.SetActive(true);
        EventSystem.current?.SetSelectedGameObject(actions.Find("Play").gameObject);
    }

    private void ChangeVolume(float delta)
    {
        AudioListener.volume = Mathf.Clamp01(AudioListener.volume + delta);
        PlayerPrefs.SetFloat(VolumeKey, AudioListener.volume);
        PlayerPrefs.Save();
        RefreshSettings();
    }

    private void ToggleMotion()
    {
        reducedMotion = !reducedMotion;
        PlayerPrefs.SetInt(MotionKey, reducedMotion ? 1 : 0);
        PlayerPrefs.Save();
        RefreshSettings();
    }

    private void RefreshSettings()
    {
        settings.Find("Volume Value").GetComponent<Text>().text = Mathf.RoundToInt(AudioListener.volume * 100f) + "%";
        settings.Find("Motion/Label").GetComponent<Text>().text = "REDUCED MOTION     " + (reducedMotion ? "ON" : "OFF");
        settings.Find("Camera Shake/Label").GetComponent<Text>().text = "CAMERA SHAKE     " + (GameplayHud.CameraShakeEnabled ? "ON" : "OFF");
        settings.Find("Fullscreen/Label").GetComponent<Text>().text = "FULLSCREEN     " + (Screen.fullScreen ? "ON" : "OFF");
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        bool escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        bool escape = Input.GetKeyDown(KeyCode.Escape);
#else
        bool escape = false;
#endif
        if (!escape) return;
        if (settings.gameObject.activeSelf || credits.gameObject.activeSelf) CloseExtra();
        else if (layout.Find("Character Customizer").gameObject.activeSelf)
            layout.Find("Character Customizer/Back").GetComponent<Button>().onClick.Invoke();
        else if (layout.Find("Mode Picker").gameObject.activeSelf)
            layout.Find("Mode Picker/Back").GetComponent<Button>().onClick.Invoke();
    }

    private void LateUpdate()
    {
        if (menuCamera == null) return;
        if (subtitle != null && subtitle.gameObject.activeSelf != actions.gameObject.activeSelf)
            subtitle.gameObject.SetActive(actions.gameObject.activeSelf);
        float time = Time.unscaledTime;
        float dt = Time.unscaledDeltaTime;
        bool covered = intro != null && intro.gameObject.activeSelf;
        if (!covered && entrance < 0f) entrance = time;
        float age = entrance < 0f ? 0f : time - entrance;
        Vector2 pointer = new Vector2(Screen.width, Screen.height) * 0.5f;
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null) pointer = Mouse.current.position.ReadValue();
#elif ENABLE_LEGACY_INPUT_MANAGER
        pointer = Input.mousePosition;
#endif
        var offset = new Vector2(Mathf.Clamp(pointer.x / Mathf.Max(1, Screen.width) - 0.5f, -0.5f, 0.5f),
            Mathf.Clamp(pointer.y / Mathf.Max(1, Screen.height) - 0.5f, -0.5f, 0.5f));
        float blend = 1f - Mathf.Exp(-9f * dt);
        bool characterView = customization.gameObject.activeInHierarchy;
        foreach (var name in new[] { "Title", "Preview caption", "Preview rule", "Footer note" })
            layout.Find(name).gameObject.SetActive(!characterView);
        preview.rotation = Quaternion.Slerp(preview.rotation, previewRotation * Quaternion.Euler(0f, characterView ? characterYaw : 0f, 0f), reducedMotion ? 1f : blend);
        float characterOffset = previewPosition.x - cameraPosition.x;
        var viewOffset = characterView ? new Vector3(characterOffset, -0.8f, -2.85f) : Vector3.zero;
        var targetCamera = cameraPosition + (characterView ? new Vector3(characterOffset, -0.8f, 1.2f) : Vector3.zero) + (reducedMotion ? Vector3.zero : new Vector3(offset.x * 0.12f, offset.y * 0.07f, 0));
        menuCamera.transform.position = Vector3.Lerp(menuCamera.transform.position, targetCamera, blend);
        menuCamera.transform.rotation = cameraRotation;
        menuCamera.fieldOfView = 2f * Mathf.Atan(Mathf.Tan(36f * Mathf.Deg2Rad * 0.5f) * Mathf.Max(1f, 1.85f / menuCamera.aspect)) * Mathf.Rad2Deg;
        transform.position = Vector3.Lerp(transform.position, canvasPosition + viewOffset + (reducedMotion ? Vector3.zero : new Vector3(0, Mathf.Sin(time * 0.8f) * 0.025f, 0)), reducedMotion ? 1f : blend);
        transform.localScale = Vector3.Lerp(transform.localScale, canvasScale * (characterView ? 0.3875f : 1f), reducedMotion ? 1f : blend);
        transform.rotation = Quaternion.Slerp(transform.rotation, (characterView ? Quaternion.identity : canvasRotation) * (reducedMotion ? Quaternion.identity : Quaternion.Euler(Mathf.Sin(time * 0.57f) * 0.3f, Mathf.Sin(time * 0.41f) * 0.5f, 0)), reducedMotion ? 1f : blend);

        foreach (var panel in panels)
        {
            bool active = panel.Object.activeInHierarchy;
            if (active && !panel.WasActive) panel.Opened = time;
            panel.WasActive = active;
            if (!active) continue;
            float progress = reducedMotion ? 1f : Mathf.Clamp01((time - panel.Opened) / 0.3f);
            float ease = 1f - Mathf.Pow(1f - progress, 3f);
            panel.Rect.localPosition = panel.Position + Vector3.right * ((1f - ease) * 45f);
            panel.Group.alpha = ease;
        }

        foreach (var item in buttons)
        {
            if (item.Rect == null || !item.Button.gameObject.activeInHierarchy) continue;
            bool main = item.Button.transform.parent == actions;
            float reveal = reducedMotion || !main ? 1f : Mathf.Clamp01((age - item.Order * 0.075f) / 0.45f);
            float ease = 1f - Mathf.Pow(1f - reveal, 3f);
            item.Group.alpha = ease;
            item.Rect.localPosition = item.Position + Vector3.right * ((1f - ease) * 55f);
        }
    }

    private void OnDestroy()
    {
        if (menuCamera != null)
        {
            menuCamera.transform.position = cameraPosition;
            menuCamera.transform.rotation = cameraRotation;
        }
    }
}

