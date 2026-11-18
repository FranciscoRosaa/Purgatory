using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class MenuAudio : MonoBehaviour
{
    private static MenuAudio instance;
    private AudioSource clicks, music;
    private AudioClip clickClip, musicClip;
    private float musicTarget;
    private const float MusicVolume = 0.045f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if (instance != null) return;
        var root = new GameObject("Menu Audio");
        DontDestroyOnLoad(root);
        instance = root.AddComponent<MenuAudio>();
    }

    private void Awake()
    {
        clicks = gameObject.AddComponent<AudioSource>();
        clicks.playOnAwake = false;
        clicks.spatialBlend = 0f;
        clicks.volume = 0.2f;
        clicks.priority = 32;
        clickClip = CreateClick();

        music = gameObject.AddComponent<AudioSource>();
        music.playOnAwake = false;
        music.spatialBlend = 0f;
        music.loop = true;
        music.volume = 0f;
        music.priority = 128;
        musicClip = CreateMusic();
        music.clip = musicClip;
        SceneManager.sceneLoaded += BindScene;
    }

    private void BindScene(Scene scene, LoadSceneMode mode)
    {
        foreach (var root in scene.GetRootGameObjects())
            foreach (var button in root.GetComponentsInChildren<Button>(true))
                button.onClick.AddListener(PlayClick);

        musicTarget = scene.path.EndsWith("/MainMenu.unity", StringComparison.Ordinal) ? MusicVolume : 0f;
        if (musicTarget > 0f && !music.isPlaying) music.Play();
    }

    public void PlayClick()
    {
        if (clicks != null) clicks.PlayOneShot(clickClip);
    }

    private void Update()
    {
        music.volume = Mathf.MoveTowards(music.volume, musicTarget, Time.unscaledDeltaTime * 0.055f);
        if (musicTarget == 0f && music.volume == 0f && music.isPlaying) music.Stop();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= BindScene;
        if (clickClip != null) Destroy(clickClip);
        if (musicClip != null) Destroy(musicClip);
        if (instance == this) instance = null;
    }

    public static AudioClip CreateClick()
    {
        const int rate = 44100;
        var samples = new float[(int)(rate * 0.065f)];
        var random = new System.Random(17);
        float filtered = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float time = i / (float)rate;
            filtered = Mathf.Lerp(filtered, (float)random.NextDouble() * 2f - 1f, 0.55f);
            float attack = Mathf.Min(1f, time / 0.0008f);
            samples[i] = attack * (
                Mathf.Sin(time * Mathf.PI * 2f * 720f) * Mathf.Exp(-time * 115f) * 0.38f +
                Mathf.Sin(time * Mathf.PI * 2f * 330f) * Mathf.Exp(-time * 160f) * 0.14f +
                filtered * Mathf.Exp(-time * 320f) * 0.18f);
        }
        var clip = AudioClip.Create("UI soft wooden click", samples.Length, 1, rate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    public static AudioClip CreateMusic()
    {
        const int rate = 22050, seconds = 32;
        const int frames = rate * seconds;
        var dry = new float[frames * 2];
        int[][] chords =
        {
            new[] { 45, 52, 60, 71 },
            new[] { 41, 48, 57, 64 },
            new[] { 38, 45, 53, 64 },
            new[] { 40, 47, 57, 65 }
        };
        var frequencies = new float[4, 4];
        for (int c = 0; c < 4; c++)
            for (int n = 0; n < 4; n++)
                frequencies[c, n] = Mathf.Round(440f * Mathf.Pow(2f, (chords[c][n] - 69) / 12f) * seconds) / seconds;

        float[] times = { 1f, 3.5f, 6.5f, 9f, 12f, 14.5f, 17f, 20f, 22.5f, 25f, 28f, 30.5f };
        int[] notes = { 64, 71, 60, 64, 69, 67, 65, 64, 69, 71, 65, 64 };
        var pitches = new float[notes.Length];
        for (int i = 0; i < notes.Length; i++)
            pitches[i] = Mathf.Round(440f * Mathf.Pow(2f, (notes[i] - 69) / 12f) * seconds) / seconds;

        float Pad(int chord, float time, float phase)
        {
            float value = 0f;
            for (int n = 0; n < 4; n++)
            {
                float angle = time * Mathf.PI * 2f * frequencies[chord, n];
                value += Mathf.Sin(angle + n * 0.7f + phase) * 0.024f;
                value += Mathf.Sin(angle * 2f + phase) * 0.003f;
            }
            return value;
        }

        for (int i = 0; i < frames; i++)
        {
            float time = i / (float)rate;
            int chord = (int)(time / 8f);
            int previous = (chord + 3) % 4;
            float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((time % 8f) / 2f));
            float breath = 0.9f + Mathf.Sin(time * Mathf.PI * 2f / seconds) * 0.08f;
            float left = Mathf.Lerp(Pad(previous, time, 0f), Pad(chord, time, 0f), blend) * breath;
            float right = Mathf.Lerp(Pad(previous, time, 0.12f), Pad(chord, time, 0.12f), blend) * breath;
            float drone = Mathf.Sin(time * Mathf.PI * 2f * 55f) * 0.009f;
            left += drone;
            right += drone;
            for (int n = 0; n < times.Length; n++)
            {
                float age = time - times[n];
                if (age < 0f) age += seconds;
                if (age > 6f) continue;
                float envelope = (1f - Mathf.Exp(-age * 85f)) * Mathf.Exp(-age * 1.1f);
                float tone = Mathf.Sin(time * Mathf.PI * 2f * pitches[n]) +
                    Mathf.Sin(time * Mathf.PI * 4f * pitches[n]) * 0.13f;
                float note = tone * envelope * 0.032f;
                left += note * (n % 2 == 0 ? 1f : 0.7f);
                right += note * (n % 2 == 0 ? 0.7f : 1f);
            }
            dry[i * 2] = left;
            dry[i * 2 + 1] = right;
        }

        var samples = new float[dry.Length];
        float peak = 0f;
        for (int i = 0; i < frames; i++)
            for (int c = 0; c < 2; c++)
            {
                int index = i * 2 + c;
                samples[index] = dry[index] +
                    dry[((i - 6615 + frames) % frames) * 2 + 1 - c] * 0.25f +
                    dry[((i - 10337 + frames) % frames) * 2 + c] * 0.14f;
                peak = Mathf.Max(peak, Mathf.Abs(samples[index]));
            }
        float gain = peak > 0f ? 0.58f / peak : 1f;
        for (int i = 0; i < samples.Length; i++) samples[i] *= gain;

        var clip = AudioClip.Create("Stillwater Gate - Quiet Vigil", frames, 2, rate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
