using UnityEngine;

public sealed class MenuSceneryAtmosphere : MonoBehaviour
{
    public Light[] lanterns;
    public Transform[] flames;
    public Transform[] foliage;
    private float[] intensities;
    private Vector3[] flameScales, flamePositions;
    private Quaternion[] foliageRotations;

    private void Start()
    {
        intensities = new float[lanterns.Length];
        for (int i = 0; i < lanterns.Length; i++) intensities[i] = lanterns[i].intensity;
        flameScales = new Vector3[flames.Length];
        flamePositions = new Vector3[flames.Length];
        for (int i = 0; i < flames.Length; i++)
        {
            flameScales[i] = flames[i].localScale;
            flamePositions[i] = flames[i].localPosition;
        }
        foliageRotations = new Quaternion[foliage.Length];
        for (int i = 0; i < foliage.Length; i++) foliageRotations[i] = foliage[i].localRotation;
    }

    private void Update()
    {
        float time = Time.unscaledTime;
        bool still = PlayerPrefs.GetInt("Purgatory.Menu.ReducedMotion", 0) != 0;
        for (int i = 0; i < lanterns.Length; i++)
            lanterns[i].intensity = intensities[i] * (still ? 1f :
                1f + Mathf.Sin(time * 4.1f + i * 2.3f) * 0.055f +
                Mathf.Sin(time * 7.3f + i) * 0.025f);
        for (int i = 0; i < flames.Length; i++)
        {
            float stretch = still ? 1f : 1f + Mathf.Sin(time * 5.3f + i * 2f) * 0.06f;
            var scale = flameScales[i];
            scale.y *= stretch;
            flames[i].localScale = scale;
            // Keep the flame base on its bowl while the tip flickers.
            flames[i].localPosition = flamePositions[i] +
                Vector3.up * ((scale.y - flameScales[i].y) * 0.5f);
        }
        for (int i = 0; i < foliage.Length; i++)
            foliage[i].localRotation = foliageRotations[i] * Quaternion.Euler(0f, 0f,
                still ? 0f : Mathf.Sin(time * 0.9f + i * 1.73f) * 1.4f);
    }
}
