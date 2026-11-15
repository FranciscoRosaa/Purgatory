using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WaveDirector))]
public sealed class WaveDirectorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.HelpBox("Configure Waves > Sub Waves > Enemies. Killing an enemy immediately releases one enemy of the same type from the next sub-wave, even while other enemies remain alive. Each configured enemy spawns once. Other enemies unlock after the previous sub-wave clears. The wave advances only after every sub-wave is defeated.", MessageType.Info);
        DrawPropertiesExcluding(serializedObject, "m_Script");
        serializedObject.ApplyModifiedProperties();
        if (Application.isPlaying)
        {
            var director = (WaveDirector)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("State", director.Stage.ToString());
            EditorGUILayout.LabelField("Wave", $"{director.CurrentWave} / {director.TotalWaves}");
            EditorGUILayout.LabelField("Sub-wave", $"{director.CurrentSubWave} / {director.TotalSubWaves}");
            EditorGUILayout.LabelField("Alive", director.AliveEnemies.ToString());
            EditorGUILayout.LabelField("Waiting to spawn", director.EnemiesWaitingToSpawn.ToString());
            if (director.Stage == WaveDirector.WaveStage.Stopped && GUILayout.Button("Start waves")) director.StartWaves();
            Repaint();
        }
    }
}
