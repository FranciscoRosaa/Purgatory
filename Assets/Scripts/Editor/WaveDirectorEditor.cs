using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WaveDirector))]
public sealed class WaveDirectorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.HelpBox("For each wave, set Enemies with a type and total count, then Max Concurrent Enemies. Enemies are drawn randomly from the remaining list until the limit is reached. Each death opens a slot for another random enemy. The wave ends when the entire list is defeated.", MessageType.Info);
        DrawPropertiesExcluding(serializedObject, "m_Script");
        serializedObject.ApplyModifiedProperties();
        if (Application.isPlaying)
        {
            var director = (WaveDirector)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("State", director.Stage.ToString());
            EditorGUILayout.LabelField("Wave", $"{director.CurrentWave} / {director.TotalWaves}");
            EditorGUILayout.LabelField("Enemy limit", director.CurrentEnemyLimit.ToString());
            EditorGUILayout.LabelField("Alive", director.AliveEnemies.ToString());
            EditorGUILayout.LabelField("Waiting to spawn", director.EnemiesWaitingToSpawn.ToString());
            if (director.Stage == WaveDirector.WaveStage.Stopped && GUILayout.Button("Start waves")) director.StartWaves();
            Repaint();
        }
    }
}
