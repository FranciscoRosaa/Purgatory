using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

internal static class MainMenuActions
{
    private const string MenuScene = "Assets/Scenes/MainMenu.unity";
    private const string GameScene = "Assets/Scenes/SampleScene.unity";
    private static bool showIntro;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        showIntro = true;
        SceneManager.sceneLoaded -= BindButtons;
        SceneManager.sceneLoaded += BindButtons;
    }

    private static void BindButtons(Scene scene, LoadSceneMode mode)
    {
        if (scene.path == GameScene)
        {
            if (CharacterProfiles.HasSlot(CharacterProfiles.ActiveSlot))
            {
                var character = GameObject.Find("Personagem");
                CharacterProfiles.ApplyTint(character, CharacterProfiles.Tint(CharacterProfiles.Active.Color));
            }
            return;
        }
        if (scene.path != MenuScene)
            return;

        GameObject canvas = null;
        GameObject preview = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "Menu Canvas")
                canvas = root;
            if (root.name == "Personagem em exposição")
                preview = root;
        }

        var layout = canvas.transform.Find("Layout");
        var actions = layout.Find("Actions").gameObject;
        var picker = layout.Find("Mode Picker").gameObject;
        var customization = layout.Find("Character Customizer").gameObject;
        var lobby = canvas.transform.Find("Lobby").gameObject;
        var session = Object.FindAnyObjectByType<CoopSession>();
        if (session == null)
        {
            var network = new GameObject("Co-op Session");
            Object.DontDestroyOnLoad(network);
            session = network.AddComponent<CoopSession>();
        }
        lobby.AddComponent<CoopLobby>().Setup(lobby.transform.Find("Roster"), session);
        var play = actions.transform.Find("Play").GetComponent<Button>();
        var solo = picker.transform.Find("Play Solo").GetComponent<Button>();
        var leave = lobby.transform.Find("Roster/Leave").GetComponent<Button>();
        var camera = Camera.main;
        Vector3 menuPosition = preview.transform.position;
        Color menuBackground = camera.backgroundColor;
        var menuName = layout.Find("Preview caption").GetComponent<Text>();
        var lobbyName = lobby.transform.Find("Preview").GetComponent<Text>();
        string initialMenuName = menuName.text;
        string initialLobbyName = lobbyName.text;
        menuName.supportRichText = lobbyName.supportRichText = false;

        void PreviewCharacter(CharacterProfile profile)
        {
            CharacterProfiles.ApplyTint(preview, CharacterProfiles.Tint(profile.Color));
            menuName.text = lobbyName.text = profile.Name;
        }

        void RestorePreview()
        {
            if (CharacterProfiles.HasSlot(CharacterProfiles.ActiveSlot))
            {
                PreviewCharacter(CharacterProfiles.Active);
                return;
            }
            CharacterProfiles.ApplyTint(preview, CharacterProfiles.Tint(0));
            menuName.text = initialMenuName;
            lobbyName.text = initialLobbyName;
        }

        RestorePreview();
        var customizer = customization.AddComponent<CharacterCustomizer>();

        void ShowModes()
        {
            actions.SetActive(false);
            customization.SetActive(false);
            picker.SetActive(true);
            RestorePreview();
            EventSystem.current?.SetSelectedGameObject(solo.gameObject);
        }

        customizer.Setup(PreviewCharacter, ShowModes);

        void ShowMainMenu()
        {
            lobby.SetActive(false);
            layout.gameObject.SetActive(true);
            picker.SetActive(false);
            customization.SetActive(false);
            actions.SetActive(true);
            preview.transform.position = menuPosition;
            camera.backgroundColor = menuBackground;
            EventSystem.current?.SetSelectedGameObject(play.gameObject);
        }

        void Customize(string label, System.Action next)
        {
            picker.SetActive(false);
            customizer.Open(label, next);
        }

        play.onClick.AddListener(ShowModes);
        solo.onClick.AddListener(() => Customize("Start Solo", () =>
            SceneManager.LoadScene(GameScene, LoadSceneMode.Single)));
        picker.transform.Find("Back").GetComponent<Button>().onClick.AddListener(ShowMainMenu);
        void ShowLobby()
        {
            customization.SetActive(false);
            layout.gameObject.SetActive(false);
            lobby.SetActive(true);
            preview.transform.position = new Vector3(4.7f, 0f, menuPosition.z);
            camera.backgroundColor = new Color32(15, 23, 39, 255);
            EventSystem.current?.SetSelectedGameObject(lobby.transform.Find("Roster/Host Button").gameObject);
        }

        picker.transform.Find("Play With Friends").GetComponent<Button>().onClick.AddListener(() =>
            Customize("Continue to Lobby", ShowLobby));
        leave.onClick.AddListener(() => { session.Stop(); ShowMainMenu(); });
        actions.transform.Find("Exit").GetComponent<Button>().onClick.AddListener(Exit);

        var intro = canvas.transform.Find("Active Creators Intro").gameObject;
        intro.SetActive(showIntro);
        if (showIntro)
        {
            showIntro = false;
            var group = layout.GetComponent<CanvasGroup>();
            group.interactable = false;
            EventSystem.current?.SetSelectedGameObject(null);
            session.StartCoroutine(FadeIntro(intro.GetComponent<CanvasGroup>(), group, play));
        }
    }

    private static IEnumerator FadeIntro(CanvasGroup intro, CanvasGroup menu, Button play)
    {
#if !UNITY_EDITOR
        while (!UnityEngine.Rendering.SplashScreen.isFinished)
            yield return null;
#endif
        yield return new WaitForSecondsRealtime(1.5f);
        const float fadeDuration = 0.85f;
        float start = Time.unscaledTime;
        while (Time.unscaledTime - start < fadeDuration)
        {
            intro.alpha = 1f - Mathf.SmoothStep(0f, 1f, (Time.unscaledTime - start) / fadeDuration);
            yield return null;
        }
        intro.gameObject.SetActive(false);
        menu.interactable = true;
        EventSystem.current?.SetSelectedGameObject(play.gameObject);
    }

    private static void Exit()
    {
        Debug.Log("o jogo fechou");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
