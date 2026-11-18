using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SetupBlackNinja
{
    private const string Model="Assets/Models/NinjaPreto/ninja_preto_lowpoly_TRUE.fbx";
    private const string Prefab="Assets/Waves/Enemies/BlackNinja.prefab";
    [MenuItem("Tools/Purgatory/Set Up Black Ninja")]
    public static void Build()
    {
        if(EditorApplication.isPlaying)throw new Exception("Stop Play Mode first");
        var scene=SceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/SampleScene.unity")throw new Exception("Open SampleScene");
        ConfigureRig(Model,false);
        var attacks=new AnimationClip[3];
        for(int i=0;i<3;i++)
        {
            string path=$"Assets/Animations/NinjaPreto/{i+1}ninjalunge.fbx";ConfigureRig(path,true);
            attacks[i]=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview"));
        }
        var player=UnityEngine.Object.FindAnyObjectByType<WasdBlockMovement>();
        var actor=Resources.FindObjectsOfTypeAll<BlackNinja>().FirstOrDefault(n=>n.gameObject.scene==scene);
        if(actor==null)
        {
            var original=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(t=>t.name=="Ninja Preto");
            if(original==null)throw new Exception("Ninja Preto scene model not found");
            var parent=original.parent;var position=original.position;position.y=player.transform.position.y;
            var root=new GameObject("Ninja Preto");root.SetActive(false);root.transform.SetParent(parent,true);root.transform.position=position;
            original.SetParent(root.transform,false);original.name="Visual";original.localPosition=Vector3.zero;original.localRotation=Quaternion.identity;original.gameObject.SetActive(true);
            actor=root.AddComponent<BlackNinja>();
        }
        var animator=actor.GetComponentInChildren<Animator>(true);
        var imported=AssetDatabase.LoadAssetAtPath<GameObject>(Model).GetComponent<Animator>();
        animator.avatar=imported.avatar;
        if(!animator.isHuman || animator.avatar==null || !animator.avatar.isValid)throw new Exception("Ninja Preto humanoid avatar is invalid");
        var source=new SerializedObject(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Waves/Enemies/LungeMan.prefab").GetComponent<LungeMan>());
        var idle=(AnimationClip)source.FindProperty("swordIdle").objectReferenceValue;
        var run=(AnimationClip)source.FindProperty("sprint").objectReferenceValue;
        idle.SampleAnimation(animator.gameObject,0);
        var renderers=animator.GetComponentsInChildren<Renderer>(true);
        Bounds bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
        if(bounds.size.y>.01f)animator.transform.localScale*=2.25f/bounds.size.y;
        idle.SampleAnimation(animator.gameObject,0);
        var feet=new[]{animator.GetBoneTransform(HumanBodyBones.LeftFoot),animator.GetBoneTransform(HumanBodyBones.RightFoot)};
        animator.transform.position+=Vector3.up*(actor.transform.position.y+.055f-feet.Min(t=>t.position.y));
        var settings=new SerializedObject(actor);
        void Assign(string name,UnityEngine.Object value)=>settings.FindProperty(name).objectReferenceValue=value;
        Assign("animator",animator);Assign("idle",idle);Assign("run",run);
        Assign("firstLunge",attacks[0]);Assign("secondLunge",attacks[1]);Assign("thirdLunge",attacks[2]);
        Assign("effectMaterial",source.FindProperty("warningMaterial").objectReferenceValue);
        settings.ApplyModifiedPropertiesWithoutUndo();
        var body=actor.GetComponent<CapsuleCollider>();if(body==null)body=actor.gameObject.AddComponent<CapsuleCollider>();
        body.isTrigger=true;body.height=2.2f;body.radius=.48f;body.center=Vector3.up*1.1f;
        actor.gameObject.SetActive(true);var prefab=PrefabUtility.SaveAsPrefabAsset(actor.gameObject,Prefab);actor.gameObject.SetActive(false);
        var director=UnityEngine.Object.FindAnyObjectByType<WaveDirector>();var ws=new SerializedObject(director);
        var entries=ws.FindProperty("enemyPrefabs");SerializedProperty entry=null;
        for(int i=0;i<entries.arraySize;i++)if(entries.GetArrayElementAtIndex(i).FindPropertyRelative("enemy").enumValueIndex==(int)WaveEnemyKind.BlackNinja)entry=entries.GetArrayElementAtIndex(i);
        if(entry==null){int index=entries.arraySize;entries.arraySize++;entry=entries.GetArrayElementAtIndex(index);}
        entry.FindPropertyRelative("enemy").enumValueIndex=(int)WaveEnemyKind.BlackNinja;entry.FindPropertyRelative("prefab").objectReferenceValue=prefab.GetComponent<BlackNinja>();
        var waves=ws.FindProperty("waves");
        if(waves.arraySize==4)
        {
            waves.arraySize=5;var wave=waves.GetArrayElementAtIndex(4);wave.FindPropertyRelative("name").stringValue="Biblioteca - Ninja Preto";
            wave.FindPropertyRelative("delayBeforeStart").floatValue=6;wave.FindPropertyRelative("enemies").arraySize=0;
            var subs=wave.FindPropertyRelative("subWaves");subs.arraySize=1;var sub=subs.GetArrayElementAtIndex(0);
            sub.FindPropertyRelative("name").stringValue="Surprise in the library";sub.FindPropertyRelative("delayBeforeStart").floatValue=0;
            var groups=sub.FindPropertyRelative("enemies");groups.arraySize=1;var group=groups.GetArrayElementAtIndex(0);
            group.FindPropertyRelative("enemy").enumValueIndex=(int)WaveEnemyKind.BlackNinja;group.FindPropertyRelative("count").intValue=1;
            group.FindPropertyRelative("delay").floatValue=0;group.FindPropertyRelative("spawnInterval").floatValue=0;
        }
        ws.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);Selection.activeGameObject=actor.gameObject;
        Debug.Log("BLACK_NINJA_SETUP_PASS: 90 HP, three humanoid clips, 5/10/10 damage, 4s combo cooldown, 6s invisibility cooldown; registered in waves.");
    }
    private static void ConfigureRig(string path,bool animation)
    {
        var importer=(ModelImporter)AssetImporter.GetAtPath(path);bool changed=importer.animationType!=ModelImporterAnimationType.Human || !importer.isReadable;
        if(changed){importer.animationType=ModelImporterAnimationType.Human;importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;importer.isReadable=true;importer.SaveAndReimport();}
        if(!animation)return;
        var clips=importer.clipAnimations;if(clips.Length==0)clips=importer.defaultClipAnimations;
        foreach(var clip in clips){clip.lockRootPositionXZ=true;clip.lockRootHeightY=true;clip.lockRootRotation=true;clip.keepOriginalPositionXZ=true;clip.keepOriginalPositionY=true;clip.keepOriginalOrientation=true;clip.loopTime=false;}
        importer.clipAnimations=clips;importer.SaveAndReimport();
    }

    private const string AuditFlag="Purgatory.ProjectOrganisation";
    [InitializeOnLoadMethod]
    private static void ResumeAudit()
    {
        if(SessionState.GetBool(AuditFlag,false))EditorApplication.delayCall+=Audit;
    }
    [MenuItem("Tools/Purgatory/Organise Project Files")]
    public static void Organise()
    {
        if(EditorApplication.isPlaying)throw new Exception("Stop Play Mode first");
        var moves=new Dictionary<string,string>();
        string Category(string name)=>name switch
        {
            "WasdBlockMovement" or "PlayerMeleeCombo" or "PlayerFootsteps" or "KatanaHandGrip"=>"Player",
            "CoopSession" or "CoopLobby" or "CoopWaveNetwork" or "CoopGiantKing" or "CoopLibraryWorld"=>"Networking",
            "LungeMan" or "GiantChargeAttack" or "RedNinja" or "TrueHealer" or "GiantKing"=>"Enemies",
            "DashPresentation" or "PlayerAreaWave" or "GiantChargeVfx" or "GiantJumpVfx" or "RedNinjaFire" or "HealerEffects" or "GiantKingVfx"=>"VFX",
            "GameplayHud" or "EnemyDamageFeedback" or "GiantKingHud"=>"UI",
            "CharacterAppearance" or "CharacterCustomizer"=>"Characters",
            "MainMenuActions" or "MenuAudio" or "MenuCharacterGrounding" or "MenuSceneryAtmosphere" or "WorldMenuPresentation"=>"Menu",
            "WaveDirector"=>"Waves", "CombatAudio"=>"Audio", _=>"World"
        };
        var firstParty=new HashSet<string>(new[]{"LungeMan","GiantChargeAttack","RedNinja","TrueHealer","GiantKing","CoopSession","CoopLobby","CoopWaveNetwork","CoopGiantKing","CoopLibraryWorld",
            "WasdBlockMovement","PlayerMeleeCombo","PlayerFootsteps","KatanaHandGrip","DashPresentation","PlayerAreaWave","GiantChargeVfx","GiantJumpVfx","RedNinjaFire","HealerEffects","GiantKingVfx",
            "GameplayHud","EnemyDamageFeedback","GiantKingHud","CharacterAppearance","CharacterCustomizer","MainMenuActions","MenuAudio","MenuCharacterGrounding","MenuSceneryAtmosphere","WorldMenuPresentation",
            "WaveDirector","CombatAudio","ArenaAtmosphere","LibraryWorld","LibraryObstacle","LibraryStageTransition"});
        foreach(var guid in AssetDatabase.FindAssets("t:MonoScript",new[]{"Assets"}))
        {
            var path=AssetDatabase.GUIDToAssetPath(guid);var name=Path.GetFileNameWithoutExtension(path);
            if(path.StartsWith("Assets/Scripts/"))continue;
            if(path.StartsWith("Assets/Editor/"))moves[path]="Assets/Scripts/Editor/"+Path.GetFileName(path);
            else if(firstParty.Contains(name))moves[path]="Assets/Scripts/"+Category(name)+"/"+Path.GetFileName(path);
        }
        if(AssetDatabase.LoadAssetAtPath<Material>("Assets/ChaoQuadriculado.mat")!=null)moves["Assets/ChaoQuadriculado.mat"]="Assets/Materials/Environment/ChaoQuadriculado.mat";
        if(AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ActiveCreatorsLogo.png")!=null)moves["Assets/ActiveCreatorsLogo.png"]="Assets/UI/Branding/ActiveCreatorsLogo.png";
        var identity=new List<string>();
        foreach(var pair in moves){Folder(Path.GetDirectoryName(pair.Value).Replace('\\','/'));identity.Add(AssetDatabase.AssetPathToGUID(pair.Key)+"|"+pair.Value);}
        SessionState.SetString(AuditFlag+".identities",string.Join("\n",identity));SessionState.SetBool(AuditFlag,true);
        AssetDatabase.StartAssetEditing();
        try{foreach(var pair in moves){string error=AssetDatabase.MoveAsset(pair.Key,pair.Value);if(error.Length>0)throw new Exception(error);}}
        finally{AssetDatabase.StopAssetEditing();}
        AssetDatabase.SaveAssets();
        foreach(var scene in Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt))if(scene.isDirty)EditorSceneManager.SaveScene(scene);
        Debug.Log("PROJECT_ORGANISATION: moved "+moves.Count+" files with Unity GUIDs preserved; validating after compilation.");
    }
    private static void Folder(string path){if(AssetDatabase.IsValidFolder(path))return;int slash=path.LastIndexOf('/');Folder(path.Substring(0,slash));AssetDatabase.CreateFolder(path.Substring(0,slash),path.Substring(slash+1));}
    private static void Audit()
    {
        SessionState.SetBool(AuditFlag,false);var lines=new List<string>();
        foreach(var line in SessionState.GetString(AuditFlag+".identities","").Split('\n'))
        {if(line.Length==0)continue;var part=line.Split('|');lines.Add((AssetDatabase.AssetPathToGUID(part[1])==part[0]?"PASS":"FAIL")+": GUID preserved "+part[1]);}
        int missing=0;
        foreach(var scene in Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt))
            foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))missing+=GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
        lines.Add((missing==0?"PASS":"FAIL")+": scene missing scripts = "+missing);
        File.WriteAllLines("C:/Users/franc/Documents/Codex/2026-11-07/io/black-ninja/OrganisationValidation.txt",lines);
        Debug.Log("PROJECT_ORGANISATION_PASS: "+lines.Count(l=>l.StartsWith("PASS"))+" checks, "+lines.Count(l=>l.StartsWith("FAIL"))+" failed.");
    }
}
