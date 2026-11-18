using UnityEngine;

[RequireComponent(typeof(Animator), typeof(AudioSource))]
public sealed class PlayerFootsteps : MonoBehaviour
{
    [SerializeField] private AudioClip[] clips;
    private Animator animator;
    private AudioSource source;
    private int lastBeat = -1;
    private int nextClip;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        source = GetComponent<AudioSource>();
    }

    private void LateUpdate()
    {
        if (clips == null || clips.Length == 0 || !animator.GetBool("IsMoving"))
        {
            lastBeat = -1;
            return;
        }

        var state = animator.GetCurrentAnimatorStateInfo(0);
        if (!state.IsName("Sprint"))
            return;

        int beat = Mathf.FloorToInt(state.normalizedTime * 2f);
        if (beat == lastBeat)
            return;

        lastBeat = beat;
        var clip = clips[nextClip++ % clips.Length];
        if (clip == null)
            return;
        source.pitch = Random.Range(0.94f, 1.06f);
        source.PlayOneShot(clip, 0.5f);
    }
}
