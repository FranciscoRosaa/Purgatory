using UnityEngine;

internal static class CombatAudio
{
    private const string Folder = "CombatAudio/";
    private static AudioClip[] swings, hits;
    private static AudioClip lungeCut, giantWindup, giantStep, shout;
    private static AudioClip giantJump, giantLanding, playerHurt;
    private static int lastSwing = -1, lastHit = -1;

    internal static void Swing(Vector3 position, int strike)
    {
        swings ??= LoadVariants("KatanaSwing");
        Play(Pick(swings, ref lastSwing), position, 0.48f + 0.05f * strike, 1.13f - strike * 0.13f);
    }

    internal static void Hit(Vector3 position, int strike)
    {
        hits ??= LoadVariants("KatanaHit");
        Play(Pick(hits, ref lastHit), position, 0.53f + 0.05f * strike, 1.10f - strike * 0.11f);
    }

    internal static void LungeCut(Vector3 position) =>
        Play(lungeCut ??= Load("LungeDashCut"), position, 0.69f);

    internal static void GiantWindup(Vector3 position) =>
        Play(giantWindup ??= Load("GiantSprintWindup"), position, 0.48f);

    internal static void GiantStep(Vector3 position) =>
        Play(giantStep ??= Load("GiantSprintStep"), position, 0.60f, Random.Range(0.92f, 1.07f));

    internal static void Shout(Vector3 position) =>
        Play(shout ??= Load("PlayerM2Shout"), position, 0.53f);

    internal static void GiantJump(Vector3 position) =>
        Play(giantJump ??= Load("GiantJump"), position, 0.64f);

    internal static void GiantLanding(Vector3 position) =>
        Play(giantLanding ??= Load("GiantLanding"), position, 0.79f);

    internal static void PlayerHurt() =>
        Play(playerHurt ??= Load("PlayerHurt"), Vector3.zero, 0.57f, spatialBlend: 0f);

    private static AudioClip ninjaCharge, ninjaFire, ninjaImpact, ninjaFizzle, ninjaStep;

    internal static void NinjaCharge(Vector3 position) =>
        Play(ninjaCharge ??= Load("NinjaCharge"), position, 0.5f);

    internal static void NinjaFire(Vector3 position) =>
        Play(ninjaFire ??= Load("NinjaFire"), position, 0.65f);

    internal static void NinjaImpact(Vector3 position) =>
        Play(ninjaImpact ??= Load("NinjaImpact"), position, 0.63f);

    internal static void NinjaFizzle(Vector3 position) =>
        Play(ninjaFizzle ??= Load("NinjaFizzle"), position, 0.48f);

    internal static void NinjaStep(Vector3 position) =>
        Play(ninjaStep ??= Load("NinjaStep"), position, 0.22f, Random.Range(0.92f, 1.08f));

    private static AudioClip Load(string name) => Resources.Load<AudioClip>(Folder + name);

    private static AudioClip[] LoadVariants(string prefix)
    {
        var clips = new AudioClip[5];
        for (int i = 0; i < clips.Length; i++)
            clips[i] = Load(prefix + (i + 1));
        return clips;
    }

    private static AudioClip Pick(AudioClip[] clips, ref int previous)
    {
        int index = Random.Range(0, previous < 0 ? clips.Length : clips.Length - 1);
        if (previous >= 0 && index >= previous) index++;
        previous = index;
        return clips[index];
    }

    private static void Play(AudioClip clip, Vector3 position, float volume, float pitch = 1f, float spatialBlend = 1f)
    {
        if (clip == null)
            return;

        var emitter = new GameObject(clip.name + " audio");
        emitter.transform.position = position;
        var source = emitter.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = spatialBlend;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 9f;
        source.maxDistance = 31f;
        source.dopplerLevel = 0f;
        source.volume = volume;
        source.pitch = pitch;
        source.clip = clip;
        source.Play();
        Object.Destroy(emitter, clip.length / pitch + 0.1f);
    }
}
