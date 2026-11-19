using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(-85)]
public sealed class LibraryHazard : MonoBehaviour
{
    internal const float Interval = 34.5f;
    internal const float FlashLead = 0.18f;
    private const float FlashFade = 0.5f;
    [SerializeField] private Transform[] teleportPads;
    private LibraryWorld library;
    private WasdBlockMovement player;
    private GameplayHud hud;
    private CoopSession session;
    private Image overlay;
    private float nextTrigger, flashStarted;
    private Vector3 destination;
    private uint acknowledgement;
    private bool flashing, warped;

    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        library = GetComponent<LibraryWorld>();
        player = FindAnyObjectByType<WasdBlockMovement>();
        hud = FindAnyObjectByType<GameplayHud>();
        session = FindAnyObjectByType<CoopSession>();
        nextTrigger = Time.time + Interval;
    }

    private void Update() => Tick(Time.time);

    internal void Tick(float now)
    {
        if (flashing) AdvanceFlash(now);
        if (now < nextTrigger || library == null || LibraryWorld.Active != library ||
            session != null && session.InMatch && !session.IsHost) return;

        nextTrigger = now + Interval;
        if (player != null && hud != null && hud.Health > 0 &&
            TryChooseDestination(player.transform.position, out var point))
            BeginFlash(point, 0f, 0);
        session?.TriggerPeerLibraryHazard(this, now);
    }

    internal bool TryChooseDestination(Vector3 origin, out Vector3 point)
    {
        point = default;
        if (teleportPads == null || teleportPads.Length == 0) return false;
        int valid = 0;
        foreach (var pad in teleportPads)
        {
            if (pad == null || !pad.gameObject.activeInHierarchy) continue;
            var candidate = pad.position;
            var offset = candidate - origin;
            offset.y = 0f;
            if (offset.sqrMagnitude < 1f || library != null && library.BlocksMovement(candidate, 0.45f))
                continue;
            if (player != null)
            {
                if ((player.ClampToMovementBoundary(candidate, 0.5f) - candidate).sqrMagnitude > 0.000001f ||
                    !player.TryGetGroundHeight(candidate, out float ground)) continue;
                candidate.y = ground;
            }
            valid++;
            if (Random.Range(0, valid) == 0) point = candidate;
        }
        return valid > 0;
    }

    internal void BeginFlash(Vector3 target, float elapsed, uint sequence)
    {
        if (player == null || hud == null) return;
        destination = target;
        acknowledgement = sequence;
        flashStarted = Time.time - Mathf.Clamp(elapsed, 0f, FlashLead - 0.05f);
        flashing = true;
        warped = false;
        if (overlay == null)
        {
            var go = new GameObject("Library hazard white flash", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            overlay = go.GetComponent<Image>();
            overlay.transform.SetParent(hud.transform, false);
            var rect = overlay.rectTransform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.SetAsLastSibling();
            overlay.raycastTarget = false;
        }
        overlay.gameObject.SetActive(true);
        overlay.color = new Color(1f, 1f, 1f, 0f);
    }

    private void AdvanceFlash(float now)
    {
        float age = Mathf.Max(0f, now - flashStarted);
        if (overlay != null)
        {
            float alpha = age < FlashLead ? age / FlashLead : 1f - (age - FlashLead) / FlashFade;
            overlay.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
        }
        if (!warped && age >= FlashLead)
        {
            warped = true;
            player.WarpTo(destination);
            library?.RefreshRooms();
            if (acknowledgement != 0) session?.AcknowledgeLibraryHazard(acknowledgement);
        }
        if (age >= FlashLead + FlashFade)
        {
            flashing = false;
            if (overlay != null) overlay.gameObject.SetActive(false);
        }
    }

    private void OnDisable()
    {
        flashing = false;
        if (overlay != null) overlay.gameObject.SetActive(false);
    }
    private void OnDestroy()
    {
        if (overlay != null) Destroy(overlay.gameObject);
    }
}
