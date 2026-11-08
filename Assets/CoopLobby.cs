using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal sealed class CoopLobby : MonoBehaviour
{
    private CoopSession session;
    private Text hint, hostLabel, status, players;
    private InputField invite;
    private Image inviteBackground;
    private Outline inviteOutline;
    private bool inviteFocused;

    private static readonly Color32 FieldBackground = new Color32(19, 29, 42, 255);
    private static readonly Color32 FieldBorder = new Color32(101, 123, 147, 255);
    private static readonly Color32 FocusBackground = new Color32(32, 46, 63, 255);
    private static readonly Color32 FocusAccent = new Color32(245, 176, 99, 255);
    private static readonly Color32 HintColor = new Color32(166, 183, 201, 255);
    private Button host, stopHosting, join, copy, start;
    private float copiedUntil;

    internal void Setup(Transform roster, CoopSession connection)
    {
        session = connection;
        players = roster.Find("Players").GetComponent<Text>();
        hostLabel = roster.Find("Host").GetComponent<Text>();
        status = roster.Find("Empty state").GetComponent<Text>();
        hint = roster.Find("Invite code").GetComponent<Text>();
        invite = roster.Find("Invite Code Input").GetComponent<InputField>();
        inviteBackground = (Image)invite.targetGraphic;
        inviteOutline = invite.GetComponent<Outline>();

        host = roster.Find("Host Button").GetComponent<Button>();
        stopHosting = roster.Find("Stop Hosting Button").GetComponent<Button>();
        join = roster.Find("Join Button").GetComponent<Button>();
        copy = roster.Find("Invite").GetComponent<Button>();
        start = roster.Find("Start Game Button").GetComponent<Button>();

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var inputModule = FindAnyObjectByType<EventSystem>().GetComponent<BaseInputModule>();
        if (inputModule.inputOverride == null)
            inputModule.inputOverride = inputModule.gameObject.AddComponent<InviteCodeInputAdapter>();
#endif

        host.onClick.AddListener(session.Host);
        stopHosting.onClick.AddListener(StopHosting);
        invite.onValueChanged.AddListener(_ => UpdateInviteField());
        join.onClick.AddListener(() => session.Join(invite.text));
        copy.onClick.AddListener(CopyCode);
        start.onClick.AddListener(session.StartGame);
        Update();
    }

    private void StopHosting()
    {
        session.Stop("Hosting stopped. You can host again or join a friend.");
        copiedUntil = 0f;
        Update();
        EventSystem.current?.SetSelectedGameObject(host.gameObject);
    }

    private void CopyCode()
    {
        if (string.IsNullOrEmpty(session.Invite))
            return;
        GUIUtility.systemCopyBuffer = session.Invite;
        status.text = "Invite copied.";
        copiedUntil = Time.unscaledTime + 2f;
    }

    private void UpdateInviteField()
    {
        string code = invite.text.Trim().ToUpperInvariant();
        bool valid = CoopSession.IsInviteCode(code);
        join.interactable = !session.Active && valid;
        bool focused = invite.interactable && (invite.isFocused ||
            EventSystem.current?.currentSelectedGameObject == invite.gameObject);
        if (focused != inviteFocused)
        {
            inviteFocused = focused;
            inviteBackground.color = focused ? FocusBackground : FieldBackground;
            inviteOutline.effectColor = focused ? FocusAccent : FieldBorder;
            inviteOutline.effectDistance = focused ? new Vector2(3f, -3f) : new Vector2(1f, -1f);
        }

        hint.color = focused ? FocusAccent : HintColor;
        hint.text = code.Length > 0 && !valid ? "Use letters and numbers only." :
            focused ? "Type here or press Ctrl+V to paste." : "Click the field to enter your code.";
    }

    private void Update()
    {
        players.text = "PLAYERS   " + session.Count + " / 4";
        hostLabel.text = session.IsHost ? "You / Host" :
            session.Count > 0 ? "Connected to host" : "No active session";
        if (Time.unscaledTime >= copiedUntil)
            status.text = session.Status;

        host.gameObject.SetActive(!session.IsHost);
        stopHosting.gameObject.SetActive(session.IsHost);
        host.interactable = invite.interactable = !session.Active;
        copy.interactable = !string.IsNullOrEmpty(session.Invite);
        start.interactable = session.CanStart;
        UpdateInviteField();
    }
}

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
// uGUI's InputField uses BaseInput for IME, which defaults to the legacy input API.
// Relay codes use ASCII, so the field does not need IME composition.
internal sealed class InviteCodeInputAdapter : BaseInput
{
    public override string compositionString => string.Empty;
    public override IMECompositionMode imeCompositionMode { get => IMECompositionMode.Off; set { } }
    public override Vector2 compositionCursorPos { get => Vector2.zero; set { } }
}
#endif
