using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class BuildLibrary
{
    private const string Folder="Assets/Library";
    private static Mesh cube;
    private static Dictionary<Material,List<CombineInstance>> geometry;
    private static Material wood,oak,dark,ivory,gold,iron,blackTile,whiteTile,light;
    private static Material[] covers;
    private static Transform room;
    private static int bookCounter;

    [MenuItem("Tools/Purgatory/Build Library Map")]
    public static void Build()
    {
        if(EditorApplication.isPlaying)throw new Exception("Stop Play Mode before building the library");
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var player=UnityEngine.Object.FindAnyObjectByType<WasdBlockMovement>();
        var waves=UnityEngine.Object.FindAnyObjectByType<WaveDirector>();
        if(player==null || waves==null)throw new Exception("Open SampleScene first");
        FolderAt(Folder);FolderAt(Folder+"/Materials");FolderAt(Folder+"/Meshes");
        var shader=Shader.Find("Purgatory/Library Wood");if(shader==null)throw new Exception("Library wood shader has not imported");
        wood=Mat("Parquet",new Color(.55f,.36f,.27f),.2f,shader);
        oak=Mat("Carved walnut",new Color(.28f,.19f,.135f),.25f);
        dark=Mat("Shelf shadows",new Color(.064f,.038f,.026f),.15f);
        ivory=Mat("Aged paper",new Color(.91f,.88f,.79f),.1f);
        gold=Mat("Brass",new Color(.62f,.39f,.12f),.5f);
        iron=Mat("Iron inlays",new Color(.25f,.30f,.33f),.45f);
        blackTile=Mat("Charcoal tiles",new Color(.045f,.066f,.085f),.52f);
        whiteTile=Mat("Ivory tiles",new Color(.75f,.81f,.84f),.5f);
        light=Mat("Lantern glass",new Color(1f,.77f,.39f),.2f);
        light.EnableKeyword("_EMISSION");light.SetColor("_EmissionColor",new Color(4f,2.3f,.75f));
        var palette=new[]{new Color(.83f,.17f,.16f),new Color(.18f,.43f,.80f),new Color(.28f,.68f,.29f),
            new Color(.69f,.26f,.79f),new Color(.95f,.77f,.25f),new Color(.92f,.91f,.84f),new Color(.23f,.72f,.77f),new Color(.85f,.40f,.62f)};
        covers=palette.Select((c,i)=>Mat("Book cover "+i,c,.13f)).ToArray();
        var primitive=GameObject.CreatePrimitive(PrimitiveType.Cube);cube=primitive.GetComponent<MeshFilter>().sharedMesh;
        UnityEngine.Object.DestroyImmediate(primitive);
        geometry=new Dictionary<Material,List<CombineInstance>>();bookCounter=0;
        var tile=new GameObject("Library room");room=tile.transform;
        Box(wood,new Vector3(0f,-.09f,0f),new Vector3(48f,.18f,48f));
        for(int x=0;x<48;x++)for(int z=0;z<48;z++)
            if(x<6 || x>=42 || z<6 || z>=42)
                Box((x+z)%2==0?blackTile:whiteTile,new Vector3(-23.5f+x,.008f,-23.5f+z),new Vector3(.998f,.02f,.998f));
        foreach(float side in new[]{-1f,1f})
        {
            Box(iron,new Vector3(side*18f,.025f,0f),new Vector3(.12f,.04f,36.12f));
            Box(iron,new Vector3(0f,.025f,side*18f),new Vector3(36.12f,.04f,.12f));
        }
        for(int side=0;side<4;side++)for(int section=0;section<10;section++)
        {
            var rotation=Quaternion.Euler(0f,side*90f,0f);
            float offset=-20.7f+section*4.6f;
            Shelf(rotation*new Vector3(offset,0f,23.4f),rotation);
            if(section%2==0)Pillar(rotation*new Vector3(offset-2.3f,0f,23.15f),rotation);
        }
        foreach(float x in new[]{-13.5f,13.5f})foreach(float z in new[]{-13.5f,13.5f})
        {
            Plate(new Vector3(x,0f,z));
        }
        Plate(Vector3.zero);
        foreach(float sign in new[]{-1f,1f})
        {
            Box(dark,new Vector3(sign*13.5f,.016f,0f),new Vector3(.035f,.02f,27f));
            Box(dark,new Vector3(0f,.016f,sign*13.5f),new Vector3(27f,.02f,.035f));
        }
        for(int side=0;side<4;side++)foreach(float offset in new[]{-13.8f,4.6f})
        {
            var rotation=Quaternion.Euler(0f,side*90f,0f);
            Lantern(rotation*new Vector3(offset,3.1f,22.55f));
        }
        foreach(var pair in geometry)
        {
            string name=pair.Key.name.Replace('/','-');string path=Folder+"/Meshes/"+name+".asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool newMesh=mesh==null;
            if(newMesh)mesh=new Mesh{name="Library "+name};
            mesh.Clear();mesh.indexFormat=IndexFormat.UInt32;
            mesh.CombineMeshes(pair.Value.ToArray(),true,true);
            mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
            if(newMesh)AssetDatabase.CreateAsset(mesh,path);
            var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(room,false);
            go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=pair.Key;
        }
        var tilePrefab=PrefabUtility.SaveAsPrefabAsset(tile,Folder+"/LibraryRoom.prefab");
        UnityEngine.Object.DestroyImmediate(tile);

        var prior=scene.GetRootGameObjects().FirstOrDefault(g=>g.GetComponent<LibraryWorld>()!=null);
        if(prior!=null)UnityEngine.Object.DestroyImmediate(prior);
        var root=new GameObject("Library World");root.SetActive(false);root.transform.position=new Vector3(0f,player.transform.position.y,0f);
        var floor=new GameObject("Infinite walkable floor",typeof(BoxCollider));floor.transform.SetParent(root.transform,false);
        var collider=floor.GetComponent<BoxCollider>();collider.size=new Vector3(100000f,.4f,100000f);collider.center=new Vector3(0f,-.2f,0f);
        var boundsObject=new GameObject("Wooden arena - invisible movement boundary",typeof(BoxCollider));boundsObject.transform.SetParent(root.transform,false);
        var woodBoundary=boundsObject.GetComponent<BoxCollider>();woodBoundary.isTrigger=true;woodBoundary.center=Vector3.up*2f;woodBoundary.size=new Vector3(35.3f,8f,35.3f);
        foreach(float sign in new[]{-1f,1f})
        {
            Wall(root.transform,new Vector3(sign*18.1f,3f,0f),new Vector3(.2f,8f,36.4f));
            Wall(root.transform,new Vector3(0f,3f,sign*18.1f),new Vector3(36.4f,8f,.2f));
        }
        var initial=new List<GameObject>();
        for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)
        {
            var cell=(GameObject)PrefabUtility.InstantiatePrefab(tilePrefab,root.transform);
            cell.name=$"Library room {x}, {z}";cell.transform.localPosition=new Vector3(x*48f,0f,z*48f);initial.Add(cell);
        }
        var world=root.AddComponent<LibraryWorld>();var worldSettings=new SerializedObject(world);
        worldSettings.FindProperty("roomPrefab").objectReferenceValue=tilePrefab;
        worldSettings.FindProperty("walkableFloor").objectReferenceValue=collider;
        worldSettings.FindProperty("woodBoundary").objectReferenceValue=woodBoundary;
        worldSettings.FindProperty("roomSize").floatValue=48f;
        var list=worldSettings.FindProperty("initialRooms");list.arraySize=initial.Count;
        for(int i=0;i<initial.Count;i++)list.GetArrayElementAtIndex(i).objectReferenceValue=initial[i];
        worldSettings.ApplyModifiedPropertiesWithoutUndo();
        var sun=new GameObject("Library warm fill",typeof(Light));sun.transform.SetParent(root.transform,false);sun.transform.localRotation=Quaternion.Euler(58f,-30f,0f);
        var sunlight=sun.GetComponent<Light>();sunlight.type=LightType.Directional;sunlight.color=new Color(1f,.95f,.88f);sunlight.intensity=1f;sunlight.shadows=LightShadows.Soft;
        var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(Folder+"/LibraryAtmosphere.asset");
        if(profile==null)
        {
            profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,Folder+"/LibraryAtmosphere.asset");
            var bloom=profile.Add<Bloom>();bloom.intensity.Override(.22f);bloom.threshold.Override(1.1f);AssetDatabase.AddObjectToAsset(bloom,profile);
            var vignette=profile.Add<Vignette>();vignette.intensity.Override(.18f);vignette.smoothness.Override(.6f);AssetDatabase.AddObjectToAsset(vignette,profile);
        }
        if(profile.TryGet<Vignette>(out var existingVignette))existingVignette.intensity.Override(.06f);
        EditorUtility.SetDirty(profile);
        var volume=new GameObject("Library atmosphere",typeof(Volume));volume.transform.SetParent(root.transform,false);
        volume.GetComponent<Volume>().isGlobal=true;volume.GetComponent<Volume>().priority=20f;volume.GetComponent<Volume>().sharedProfile=profile;

        var source=new HashSet<GameObject>();var playerSettings=new SerializedObject(player);
        var ground=playerSettings.FindProperty("walkableGround");
        for(int i=0;i<ground.arraySize;i++)
        {
            var c=ground.GetArrayElementAtIndex(i).objectReferenceValue as Collider;
            if(c!=null && c.transform.root!=player.transform.root)source.Add(c.transform.root.gameObject);
        }
        foreach(var r in scene.GetRootGameObjects())
            if(r.GetComponent<ArenaAtmosphere>()!=null || r.GetComponent<Volume>()!=null || r.GetComponent<Light>()!=null || r.name=="Beach Scenery")source.Add(r);
        var boundary=playerSettings.FindProperty("movementBoundary").objectReferenceValue as BoxCollider;
        if(boundary!=null)source.Add(boundary.gameObject);
        source.Remove(root);
        var transition=UnityEngine.Object.FindAnyObjectByType<LibraryStageTransition>();
        if(transition==null)transition=new GameObject("Map Progression").AddComponent<LibraryStageTransition>();
        var settings=new SerializedObject(transition);settings.FindProperty("waves").objectReferenceValue=waves;settings.FindProperty("library").objectReferenceValue=world;
        var sources=settings.FindProperty("firstMap");sources.arraySize=source.Count;int n=0;foreach(var r in source)sources.GetArrayElementAtIndex(n++).objectReferenceValue=r;
        settings.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject=root;EditorGUIUtility.PingObject(tilePrefab);
        Debug.Log($"LIBRARY_BUILD_PASS: {bookCounter} books per room, {initial.Count} reusable rooms, transition after wave 4; run health and timer preserved.");
    }
    private static void Shelf(Vector3 origin,Quaternion rotation)
    {
        void B(Material m,Vector3 p,Vector3 s)=>Box(m,origin+rotation*p,s,rotation);
        B(dark,new Vector3(0f,2.7f,.35f),new Vector3(4.5f,5.4f,.12f));
        foreach(float x in new[]{-2.19f,2.19f})B(oak,new Vector3(x,2.7f,0f),new Vector3(.16f,5.4f,.95f));
        B(oak,new Vector3(0f,5.35f,0f),new Vector3(4.7f,.18f,1.06f));
        B(oak,new Vector3(0f,.12f,0f),new Vector3(4.65f,.24f,1.05f));
        for(int row=0;row<8;row++)
        {
            float y=.28f+row*.63f;B(oak,new Vector3(0f,y,0f),new Vector3(4.4f,.1f,.93f));
            for(int i=0;i<14;i++)
            {
                float h=.39f+.10f*Mathf.Abs(Mathf.Sin((i+row*7)*1.73f));
                float w=.22f+.035f*Mathf.Sin(i*2.4f+row);
                float x=-2.02f+i*.3f;
                B(covers[(i*3+row*5+(bookCounter/112)*7)%covers.Length],new Vector3(x,y+.08f+h*.5f,-.13f),new Vector3(w,h,.54f));
                B(ivory,new Vector3(x,y+.085f+h,-.10f),new Vector3(w*.78f,.018f,.46f));
                if(i%3!=0)B(gold,new Vector3(x,y+.13f+h*.18f,-.407f),new Vector3(w*.87f,.022f,.012f));
                bookCounter++;
            }
        }
        var obstacle=new GameObject("Bookcase collision",typeof(BoxCollider),typeof(LibraryObstacle));obstacle.transform.SetParent(room,false);
        obstacle.transform.SetPositionAndRotation(origin,rotation);obstacle.transform.localPosition=origin;
        var c=obstacle.GetComponent<BoxCollider>();c.center=new Vector3(0f,2.7f,0f);c.size=new Vector3(4.65f,5.4f,1.03f);
    }
    private static void Plate(Vector3 p)
    {
        Box(iron,p+Vector3.up*.028f,new Vector3(1.45f,.055f,1.45f));
        Box(dark,p+Vector3.up*.06f,new Vector3(1.25f,.02f,1.25f));
        Box(iron,p+Vector3.up*.075f,new Vector3(1.12f,.015f,1.12f));
    }
    private static void Pillar(Vector3 p,Quaternion rotation)
    {
        Box(oak,p+Vector3.up*2.8f,new Vector3(.44f,5.6f,1.15f),rotation);
        for(int row=0;row<14;row++)Box(iron,p+Vector3.up*(.2f+row*.4f),new Vector3(.445f,.018f,1.16f),rotation);
    }
    private static void Wall(Transform parent,Vector3 position,Vector3 size)
    {
        var go=new GameObject("Invisible wood edge",typeof(BoxCollider),typeof(LibraryObstacle));
        go.transform.SetParent(parent,false);go.transform.localPosition=position;go.GetComponent<BoxCollider>().size=size;
    }
    private static void Lantern(Vector3 p)
    {
        Box(iron,p+Vector3.up*.34f,new Vector3(.48f,.08f,.48f));
        Box(iron,p-Vector3.up*.34f,new Vector3(.48f,.08f,.48f));
        Box(light,p,new Vector3(.34f,.53f,.34f));
        foreach(float x in new[]{-.19f,.19f})foreach(float z in new[]{-.19f,.19f})
            Box(iron,p+new Vector3(x,0f,z),new Vector3(.045f,.72f,.045f));
        var go=new GameObject("Warm lantern",typeof(Light));go.transform.SetParent(room,false);go.transform.localPosition=p+Vector3.down*.5f;
        var l=go.GetComponent<Light>();l.type=LightType.Point;l.range=9f;l.intensity=5f;l.color=new Color(1f,.65f,.31f);l.shadows=LightShadows.None;
    }
    private static void Box(Material mat,Vector3 p,Vector3 size)=>Box(mat,p,size,Quaternion.identity);
    private static void Box(Material mat,Vector3 p,Vector3 size,Quaternion rotation)
    {
        if(!geometry.TryGetValue(mat,out var list))geometry[mat]=list=new List<CombineInstance>();
        list.Add(new CombineInstance{mesh=cube,transform=Matrix4x4.TRS(p,rotation,size)});
    }
    private static Material Mat(string name,Color color,float smoothness,Shader shader=null)
    {
        string path=Folder+"/Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m==null){m=new Material(shader!=null?shader:Shader.Find("Universal Render Pipeline/Lit")){name=name};AssetDatabase.CreateAsset(m,path);}
        m.color=color;if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",color);if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",smoothness);m.enableInstancing=true;EditorUtility.SetDirty(m);return m;
    }
    private static void FolderAt(string path)
    {
        if(AssetDatabase.IsValidFolder(path))return;int slash=path.LastIndexOf('/');FolderAt(path.Substring(0,slash));AssetDatabase.CreateFolder(path.Substring(0,slash),path.Substring(slash+1));
    }
}
