using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class LibraryValidation
{
    private const string Key="Purgatory.LibraryValidation";
    private const string Report="C:/Users/franc/Documents/Codex/2026-11-07/io/library/LibraryValidation.txt";
    private const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    private static readonly List<string> results=new List<string>();
    private static int step;
    private static double at;
    private static WaveDirector waves;
    private static WasdBlockMovement player;
    private static GameplayHud hud;
    private static LibraryStageTransition transition;
    private static double timer;
    private static int errors;
    static LibraryValidation(){EditorApplication.playModeStateChanged+=State;}
    [MenuItem("Tools/Purgatory/Test Library Transition")]
    public static void Run()
    {
        if(EditorApplication.isPlaying)return;
        SessionState.SetBool(Key,true);
        EditorSceneManager.playModeStartScene=null;
        EditorApplication.isPlaying=true;
    }
    [MenuItem("Tools/Purgatory/Finish Library Preview")]
    public static void Finish(){EditorApplication.isPlaying=false;}
    private static void State(PlayModeStateChange state)
    {
        if(state==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key,false))
        {
            results.Clear();step=0;errors=0;at=EditorApplication.timeSinceStartup+1.5;
            Application.logMessageReceived+=Log;EditorApplication.update+=Tick;
        }
        if(state==PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update-=Tick;Application.logMessageReceived-=Log;
            SessionState.SetBool(Key,false);
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/MainMenu.unity");
        }
    }
    private static void Log(string message,string trace,LogType type)
    {if(type==LogType.Exception || type==LogType.Error){errors++;results.Add("ERROR: "+message);}}
    private static object Get(object instance,string name)=>instance.GetType().GetField(name,Flags).GetValue(instance);
    private static void Set(object instance,string name,object value)=>instance.GetType().GetField(name,Flags).SetValue(instance,value);
    private static object Call(object instance,string name,params object[] args)=>instance.GetType().GetMethod(name,Flags).Invoke(instance,args);
    private static void Check(bool condition,string name)
    {results.Add((condition?"PASS: ":"FAIL: ")+name);}
    private static void Tick()
    {
        if(!EditorApplication.isPlaying || EditorApplication.timeSinceStartup<at)return;
        try
        {
            if(step==0)
            {
                waves=UnityEngine.Object.FindAnyObjectByType<WaveDirector>();
                player=UnityEngine.Object.FindAnyObjectByType<WasdBlockMovement>();
                hud=UnityEngine.Object.FindAnyObjectByType<GameplayHud>();
                transition=UnityEngine.Object.FindAnyObjectByType<LibraryStageTransition>();
                Check(waves!=null && player!=null && hud!=null && transition!=null,"Scene has player, HUD, director and transition");
                Check(!transition.EnteredLibrary && LibraryWorld.Active==null,"Library remains inactive during earlier waves");
                waves.enabled=false;Call(waves,"ClearSpawned");
                Set(waves,"waveIndex",3);Set(waves,"stage",WaveDirector.WaveStage.Countdown);Set(waves,"nextWaveAt",0f);
                Call(waves,"Tick",Time.time);
                step=1;at=EditorApplication.timeSinceStartup+.25;return;
            }
            if(step==1)
            {
                var boss=UnityEngine.Object.FindAnyObjectByType<GiantKing>();
                Check(boss!=null && waves.AliveEnemies==1,"Real Giant King spawned from configured fourth wave");
                boss.enabled=false;Call(waves,"Tick",Time.time);
                Check(waves.Stage!=WaveDirector.WaveStage.Complete && !transition.EnteredLibrary,"Living boss prevents transition");
                Call(hud,"SetHealth",73);timer=(double)Get(hud,"startTime");
                typeof(GiantKing).GetProperty("Health",Flags).SetValue(boss,0);
                Call(waves,"Tick",Time.time);
                Check(waves.CurrentWave>4 || waves.CurrentWave==4 && waves.Stage==WaveDirector.WaveStage.Complete,"Boss defeat completes fourth wave");
                Call(waves,"RefreshHud");
                step=2;at=EditorApplication.timeSinceStartup+3;return;
            }
            if(step==2)
            {
                var library=LibraryWorld.Active;
                Check(transition.EnteredLibrary && library!=null,"Fourth wave completion automatically enters library");
                Check(((GameObject[])Get(transition,"firstMap")).All(g=>g==null || !g.activeSelf),"Outdoor environment disabled");
                Check(((Collider[])Get(player,"walkableGround")).Single()==library.WalkableFloor && (BoxCollider)Get(player,"movementBoundary")==library.WoodBoundary,"Player uses library floor and wooden arena boundary");
                Check(player.enabled && player.GetComponent<PlayerMeleeCombo>().enabled,"Movement and combat restored after fade");
                Check((int)Get(hud,"health")==73 && (double)Get(hud,"startTime")==timer,"Health and run timer preserved");
                var origin=library.transform.position;
                var parquet=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Library/Meshes/Parquet.asset");
                Check(parquet!=null && Mathf.Abs(parquet.bounds.size.x-48f)<.01f,"Updated room geometry spans 48 metres");
                Check(!library.BlocksMovement(origin) && library.BlocksMovement(origin+new Vector3(2.3f,0f,23.4f)),"Open wooden floor and continuous solid bookshelves");
                player.enabled=false;
                Call(player,"WarpTo",origin);
                Check((bool)Call(player,"TryMove",Vector3.right,true),"Movement remains free inside wood");
                foreach(var direction in new[]{Vector3.right,Vector3.left,Vector3.forward,Vector3.back})
                {
                    Call(player,"WarpTo",origin+direction*17.4f);
                    Call(player,"TryMove",direction*8f,true);
                    var local=player.transform.position-origin;
                    Check(Mathf.Abs(local.x)<=17.651f && Mathf.Abs(local.z)<=17.651f,"Walking cannot enter checkerboard: "+direction);
                    Call(player,"WarpTo",origin+direction*17.4f);
                    Call(player,"BeginDash",direction);
                    for(int frame=0;frame<80 && (float)Get(player,"dashTimeLeft")>0f;frame++)Call(player,"AdvanceDash");
                    local=player.transform.position-origin;
                    Check(Mathf.Abs(local.x)<=17.651f && Mathf.Abs(local.z)<=17.651f,"Actual dash cannot cross wood edge: "+direction);
                }
                Call(player,"WarpTo",origin+new Vector3(17.4f,0f,17.4f));
                Call(player,"TryMove",new Vector3(10f,0f,10f),true);
                var corner=player.transform.position-origin;
                Check(corner.x<=17.651f && corner.z<=17.651f,"Diagonal movement cannot escape at corners");
                Check(library.WoodBoundary.GetComponent<Renderer>()==null && library.LoadedRooms==9,"Boundary invisible and surrounding library modules retained");
                Call(player,"WarpTo",origin);library.RefreshRooms();player.enabled=true;
                Check(!ShaderUtil.ShaderHasError(Shader.Find("Purgatory/Library Wood")),"Parquet shader compiled successfully");
                step=3;at=EditorApplication.timeSinceStartup+1;return;
            }
            Check(errors==0,"No runtime errors during validation");
            File.WriteAllLines(Report,results);Debug.Log("LIBRARY_VALIDATION: "+results.Count(r=>r.StartsWith("PASS"))+" passed, "+results.Count(r=>r.StartsWith("FAIL"))+" failed. Preview remains in Play Mode.");
            EditorApplication.update-=Tick;Application.logMessageReceived-=Log;
        }
        catch(Exception ex)
        {
            results.Add("FAIL: "+ex);File.WriteAllLines(Report,results);Debug.LogException(ex);
            EditorApplication.update-=Tick;Application.logMessageReceived-=Log;
        }
    }
}
