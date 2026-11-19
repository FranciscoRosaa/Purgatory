using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class BlackNinjaValidation
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    private const string Key="Purgatory.BlackNinjaTest";
    private static readonly List<string> results=new List<string>();
    private static double at;
    private static int stage,errors;
    private static BlackNinja actor;
    private static WasdBlockMovement player;
    private static GameplayHud hud;
    static BlackNinjaValidation(){EditorApplication.playModeStateChanged+=State;}
    [MenuItem("Tools/Purgatory/Test Black Ninja")]
    public static void Run(){if(EditorApplication.isPlaying)return;SessionState.SetBool(Key,true);EditorSceneManager.playModeStartScene=null;EditorApplication.isPlaying=true;}
    private static void State(PlayModeStateChange state)
    {
        if(state==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key,false))
        {results.Clear();stage=0;errors=0;at=EditorApplication.timeSinceStartup+1;Application.logMessageReceived+=Log;EditorApplication.update+=Tick;}
        if(state==PlayModeStateChange.EnteredEditMode)
        {
            if(SessionState.GetBool(Key,false) && results.Count>0){Check(errors==0,"Leaving Play Mode cleans up effects without errors");SaveReport();}
            EditorApplication.update-=Tick;Application.logMessageReceived-=Log;SessionState.SetBool(Key,false);EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/MainMenu.unity");
        }
    }
    private static void Log(string text,string trace,LogType type){if(type is LogType.Error or LogType.Exception){errors++;results.Add("ERROR: "+text);}}
    private static object Get(object target,string name)=>target.GetType().GetField(name,Flags).GetValue(target);
    private static void Set(object target,string name,object value)=>target.GetType().GetField(name,Flags).SetValue(target,value);
    private static object Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method,Flags).Invoke(target,args);
    private static object Property(object target,string name)=>target.GetType().GetProperty(name,Flags).GetValue(target);
    private static void Check(bool ok,string message)=>results.Add((ok?"PASS: ":"FAIL: ")+message);
    private static void Tick()
    {
        if(!EditorApplication.isPlaying || EditorApplication.timeSinceStartup<at)return;
        try
        {
            if(stage==0)
            {
                var waves=UnityEngine.Object.FindAnyObjectByType<WaveDirector>();waves.enabled=false;Call(waves,"ClearSpawned");
                player=UnityEngine.Object.FindAnyObjectByType<WasdBlockMovement>();hud=UnityEngine.Object.FindAnyObjectByType<GameplayHud>();
                UnityEngine.Object.FindAnyObjectByType<LibraryStageTransition>().BeginTransition();stage=1;at=EditorApplication.timeSinceStartup+2.3;return;
            }
            if(stage==1)
            {
                var waves=UnityEngine.Object.FindAnyObjectByType<WaveDirector>();
                Set(waves,"waveIndex",4);Set(waves,"stage",WaveDirector.WaveStage.Countdown);Set(waves,"nextWaveAt",0f);
                Call(waves,"Tick",Time.time);Call(waves,"Tick",Time.time);
                actor=UnityEngine.Object.FindAnyObjectByType<BlackNinja>();
                Check(actor!=null && waves.CurrentWave==5 && waves.AliveEnemies==1,"Configured library wave spawns the Black Ninja");
                stage=2;at=EditorApplication.timeSinceStartup+.3;return;
            }
            if(stage==2)
            {
                actor.enabled=false;player.enabled=false;
                actor.transform.position=player.transform.position+Vector3.forward*5;
                Check((int)Property(actor,"Health")==90,"Ninja starts with 90 HP");
                var animator=(Animator)Get(actor,"animator");Check(animator.isHuman && animator.avatar.isValid,"Model has a valid humanoid rig");
                var animations=new[]{(AnimationClip)Get(actor,"firstLunge"),(AnimationClip)Get(actor,"secondLunge"),(AnimationClip)Get(actor,"thirdLunge")};
                Check(animations.Distinct().Count()==3 && animations.Select((c,i)=>AssetDatabase.GetAssetPath(c).EndsWith($"/{i+1}ninjalunge.fbx")).All(v=>v),"All three requested animations assigned in order");
                Check((float)Get(actor,"invisibilityCooldown")==6 && (float)Get(actor,"comboCooldown")==4,"Invisibility and lunge cooldowns are 6 and 4 seconds");
                float now=Time.time;Set(actor,"nextAttack",now+30);Set(actor,"nextInvisibility",now+30);Set(actor,"nextRoam",now+30);
                Set(actor,"roamTarget",actor.transform.position+Vector3.right*5);
                var start=actor.transform.position;Call(actor,"Tick",now,.2f);
                Check((actor.transform.position-start).sqrMagnitude>.01f,"Random-roaming state moves independently of player");
                Call(actor,"BeginInvisibility",now);Call(actor,"Tick",now+.1f,.1f);
                float fading=(float)Get(actor,"opacity");Check(fading>0 && fading<1,"Invisibility fades gradually");
                for(int i=0;i<6;i++)Call(actor,"Tick",now+.15f+i*.1f,.1f);
                Check((float)Get(actor,"opacity")==0 && (actor.transform.position-start).sqrMagnitude>1,"Ninja still moves while completely invisible");
                Call(actor,"LateUpdate");Check(animator.GetComponentsInChildren<Renderer>().All(r=>!r.enabled),"Invisible model and shadow are hidden");
                float hiddenUntil=(float)Get(actor,"hiddenUntil");Check(Mathf.Abs((float)Get(actor,"nextInvisibility")-hiddenUntil-6)<.001f,"Invisibility cannot immediately reactivate");
                actor.transform.position=player.transform.position+Vector3.forward*5;
                Set(actor,"strike",0);Call(actor,"BeginWindup",Time.time,player.transform.position);
                Check(!(bool)Get(actor,"hidden"),"Surprise attack reveals the ninja during the visible warning");
                Call(actor,"Tick",Time.time,.2f);Call(actor,"LateUpdate");
                var effects=Get(actor,"effects");Check(((GameObject)Get(effects,"warning")).activeSelf,"Long lunge has a visible ground telegraph");
                float firstLength=(float)Get(actor,"length");Check(firstLength>8 && (float)Property(actor,"CurrentWidth")==2.4f,"First telegraph uses a long narrow rectangle");
                int health=(int)Property(hud,"Health");
                Call(actor,"Release",Time.time);Check(health-(int)Property(hud,"Health")==5,"First lunge deals exactly 5 damage");
                Call(actor,"LateUpdate");Check(((IList)Get(effects,"echoes")).Count>0,"First dash produces motion-blur afterimages");
                float end=Time.time+(float)Property(actor,"Duration")+.01f;Call(actor,"Tick",end,.1f);
                Check(Get(actor,"phase").ToString()=="Between","First lunge flows into the second");
                for(int strike=1;strike<=2;strike++)
                {
                    end+=.15f;Call(actor,"Tick",end,.1f);
                    Check((int)Get(actor,"strike")==strike && Get(actor,"phase").ToString()=="Windup","Consecutive lunge "+(strike+1)+" has its own windup");
                    Check((float)Get(actor,"length")<=4.001f && (float)Property(actor,"CurrentWidth")==4.2f,"Short lunge "+(strike+1)+" uses wider shorter telegraph");
                    var origin=(Vector3)Get(actor,"origin");var direction=(Vector3)Get(actor,"direction");Call(player,"WarpTo",origin+direction*1.5f);
                    health=(int)Property(hud,"Health");Call(actor,"Release",end);
                    Check(health-(int)Property(hud,"Health")==10,"Lunge "+(strike+1)+" deals exactly 10 damage");
                    end+=(float)Property(actor,"Duration")+.01f;Call(actor,"Tick",end,.1f);
                }
                end+=.5f;Call(actor,"Tick",end,.1f);
                Check(Get(actor,"phase").ToString()=="Roam" && Mathf.Abs((float)Get(actor,"nextAttack")-end-4)<.001f,"Three-hit combo ends with a four-second cooldown");
                Call(actor,"ApplyMeleeStrike",0);Call(actor,"LateUpdate");
                Check((int)Property(actor,"Health")<=80 && (int)Property(actor,"Health")>=75,"Player M1 damages the Ninja and supports critical hits");
                Check(GameObject.Find("Ninja Preto Combat UI")!=null,"Health bar and damage-number UI created");
                TestNetwork();
                Check((int)WaveEnemyKind.GiantKing==4 && (int)WaveEnemyKind.BlackNinja==5,"Existing enemy indices preserved and Black Ninja selectable in waves");
                Call(actor,"ApplyAreaStrike",1000);Check((int)Property(actor,"Health")==0 && Get(actor,"phase").ToString()=="Dead","Enemy death stops the combo and awards wave completion");
                UnityEngine.Object.Destroy(actor.gameObject);Call(player,"WarpTo",LibraryWorld.Active.transform.position);Call(hud,"SetHealth",100);player.enabled=true;
                var preview=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Waves/Enemies/BlackNinja.prefab"),player.transform.position+Vector3.forward*7,Quaternion.identity);
                preview.name="Ninja Preto - combat preview";
                stage=3;at=EditorApplication.timeSinceStartup+1;return;
            }
            Check(errors==0,"No runtime errors during Ninja validation");
            SaveReport();
            Debug.Log("BLACK_NINJA_VALIDATION: "+results.Count(r=>r.StartsWith("PASS"))+" passed, "+results.Count(r=>r.StartsWith("FAIL"))+" failed.");
            EditorApplication.update-=Tick;
        }
        catch(Exception ex)
        {
            results.Add("FAIL: "+ex);File.WriteAllLines("C:/Users/franc/Documents/Codex/2026-11-07/io/black-ninja/NinjaValidation.txt",results);
            Debug.LogException(ex);EditorApplication.update-=Tick;Application.logMessageReceived-=Log;
        }
    }
    private static void SaveReport()=>File.WriteAllLines("C:/Users/franc/Documents/Codex/2026-11-07/io/black-ninja/NinjaValidation.txt",results);
    private static void TestNetwork()
    {
        var session=typeof(BlackNinja).Assembly.GetType("CoopSession");var state=Call(actor,"GetState");
        var writer=new DataStreamWriter(74,Allocator.Temp);var args=new object[]{writer,state};
        session.GetMethod("WriteBlackNinja",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);writer=(DataStreamWriter)args[0];
        Check(writer.Length==74,"Co-op snapshot has the declared packet length");
        var reader=new DataStreamReader(writer.AsNativeArray());var readArgs=new object[]{reader};
        var roundTrip=session.GetMethod("ReadBlackNinja",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,readArgs);
        Check((int)Get(roundTrip,"Health")== (int)Get(state,"Health") && (byte)Get(roundTrip,"Strike")== (byte)Get(state,"Strike") && (float)Get(roundTrip,"Opacity")== (float)Get(state,"Opacity"),"Co-op serialisation preserves health, strike and invisibility");
    }
}
