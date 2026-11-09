using UnityEditor;
using UnityEditor.SceneManagement;

[InitializeOnLoad]
internal static class PlayFromMainMenu
{
    static PlayFromMainMenu()
    {
        EditorSceneManager.playModeStartScene =
            AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/MainMenu.unity");
    }
}
