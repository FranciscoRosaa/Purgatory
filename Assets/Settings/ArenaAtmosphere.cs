using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class ArenaAtmosphere : MonoBehaviour
{
    [Header("Distance Fog")]
    [SerializeField] private bool enableFog = true;
    [SerializeField] private Color fogColor = new Color32(184, 221, 224, 255);
    [SerializeField, Min(0f)] private float fogStart = 24f;
    [SerializeField, Min(0f)] private float fogEnd = 78f;

    [Header("Ambient Light")]
    [SerializeField] private Color skyLight = new Color32(188, 216, 226, 255);
    [SerializeField] private Color horizonLight = new Color32(160, 195, 182, 255);
    [SerializeField] private Color groundLight = new Color32(102, 125, 108, 255);

    private void OnEnable()
    {
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        Apply();
    }

    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall -= Apply;
#endif
    }

    private void OnActiveSceneChanged(Scene previous, Scene current) => Apply();

    private void OnValidate()
    {
        fogStart = Mathf.Max(0f, fogStart);
        fogEnd = Mathf.Max(fogStart + 0.01f, fogEnd);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall -= Apply;
        UnityEditor.EditorApplication.delayCall += Apply;
#endif
    }

    private void Apply()
    {
        if (this == null || !isActiveAndEnabled || gameObject.scene != SceneManager.GetActiveScene())
            return;

        RenderSettings.fog = enableFog;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogStartDistance = fogStart;
        RenderSettings.fogEndDistance = fogEnd;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = skyLight;
        RenderSettings.ambientEquatorColor = horizonLight;
        RenderSettings.ambientGroundColor = groundLight;
    }
}
