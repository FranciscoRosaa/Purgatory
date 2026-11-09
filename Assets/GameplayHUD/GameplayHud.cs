using UnityEngine;
using UnityEngine.UI;

public sealed class GameplayHud : MonoBehaviour
{
    [SerializeField] private RectTransform healthFill;
    [SerializeField] private Text healthLabel;
    [SerializeField] private Text timerLabel;
    [SerializeField] private Text waveLabel;
    [SerializeField, Min(1)] private int maxHealth = 100;
    [SerializeField, Min(0)] private int health = 100;
    [SerializeField, Min(1)] private int wave = 1;
    [SerializeField, Min(1)] private int totalWaves = 15;

    private double startTime;
    private int displayedSeconds = -1;

    private void Start()
    {
        int fullHealth = Mathf.Max(1, maxHealth);
        int currentHealth = Mathf.Clamp(health, 0, fullHealth);
        healthFill.anchorMax = new Vector2((float)currentHealth / fullHealth, 1f);
        healthLabel.text = $"{currentHealth} / {fullHealth}";

        int waveCount = Mathf.Max(1, totalWaves);
        waveLabel.text = $"{Mathf.Clamp(wave, 1, waveCount):D2}/{waveCount:D2}";
        startTime = Time.timeAsDouble;
        UpdateTimer(0);
    }

    private void Update() => UpdateTimer((int)(Time.timeAsDouble - startTime));

    private void UpdateTimer(int elapsedSeconds)
    {
        if (elapsedSeconds == displayedSeconds)
            return;

        displayedSeconds = elapsedSeconds;
        timerLabel.text = $"{elapsedSeconds / 60:D2}:{elapsedSeconds % 60:D2}";
    }
}
