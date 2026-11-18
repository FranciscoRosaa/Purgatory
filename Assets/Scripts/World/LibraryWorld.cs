using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(-90)]
public sealed class LibraryWorld : MonoBehaviour
{
    public static LibraryWorld Active { get; private set; }
    [SerializeField] private GameObject roomPrefab;
    [SerializeField] private Collider walkableFloor;
    [SerializeField] private BoxCollider woodBoundary;
    [SerializeField] private GameObject[] initialRooms;
    [SerializeField,Min(12f)] private float roomSize=48f;
    [SerializeField,Range(1,3)] private int visibleRadius=1;
    [SerializeField] private Color fogColor=new Color(.53f,.65f,.69f);
    private readonly Dictionary<Vector2Int,GameObject> rooms=new Dictionary<Vector2Int,GameObject>();
    private readonly Stack<GameObject> pool=new Stack<GameObject>();
    private readonly HashSet<Vector2Int> needed=new HashSet<Vector2Int>();
    private readonly List<Vector2Int> release=new List<Vector2Int>();
    private readonly List<Vector3> focuses=new List<Vector3>();
    private readonly Collider[] contacts=new Collider[32];
    private WasdBlockMovement player;
    private CoopSession session;
    private float nextRefresh;
    public int LoadedRooms=>rooms.Count;
    public float RoomSize=>roomSize;
    public Collider WalkableFloor=>walkableFloor;
    public BoxCollider WoodBoundary=>woodBoundary;

    private void OnEnable()
    {
        if(!Application.isPlaying)return;
        Active=this;
        player=FindAnyObjectByType<WasdBlockMovement>();
        session=FindAnyObjectByType<CoopSession>();
        if(rooms.Count==0 && initialRooms!=null)
            foreach(var room in initialRooms)
                if(room!=null)
                {
                    var local=room.transform.localPosition;
                    rooms[new Vector2Int(Mathf.RoundToInt(local.x/roomSize),Mathf.RoundToInt(local.z/roomSize))]=room;
                }
        ApplyAtmosphere();
        RefreshRooms();
    }
    private void OnDisable(){if(Active==this)Active=null;}
    private void Update()
    {
        if(Time.unscaledTime<nextRefresh)return;
        nextRefresh=Time.unscaledTime+.2f;
        RefreshRooms();
    }
    public void RefreshRooms()
    {
        if(roomPrefab==null)return;
        focuses.Clear();
        focuses.Add(player!=null?player.transform.position:transform.position);
        session?.AddLibraryFocuses(focuses);
        needed.Clear();
        foreach(var point in focuses)
        {
            var local=transform.InverseTransformPoint(point);
            var center=new Vector2Int(Mathf.FloorToInt(local.x/roomSize+.5f),Mathf.FloorToInt(local.z/roomSize+.5f));
            for(int x=-visibleRadius;x<=visibleRadius;x++)
                for(int z=-visibleRadius;z<=visibleRadius;z++)needed.Add(center+new Vector2Int(x,z));
        }
        release.Clear();
        foreach(var pair in rooms)if(!needed.Contains(pair.Key))release.Add(pair.Key);
        foreach(var key in release)
        {
            var room=rooms[key];rooms.Remove(key);room.SetActive(false);pool.Push(room);
        }
        foreach(var key in needed)
        {
            if(rooms.ContainsKey(key))continue;
            var room=pool.Count>0?pool.Pop():Instantiate(roomPrefab,transform,false);
            room.name=$"Library room {key.x}, {key.y}";
            room.transform.localPosition=new Vector3(key.x*roomSize,0f,key.y*roomSize);
            room.transform.localRotation=Quaternion.identity;
            room.SetActive(true);rooms.Add(key,room);
        }
        Physics.SyncTransforms();
    }
    public bool BlocksMovement(Vector3 feet,float radius=.32f)
    {
        int count=Physics.OverlapCapsuleNonAlloc(feet+Vector3.up*.38f,feet+Vector3.up*1.35f,
            radius,contacts,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        for(int i=0;i<count;i++)
            if(contacts[i]!=null && contacts[i].GetComponent<LibraryObstacle>()!=null)return true;
        return false;
    }
    private void ApplyAtmosphere()
    {
        RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;
        RenderSettings.fogColor=fogColor;RenderSettings.fogStartDistance=58f;RenderSettings.fogEndDistance=125f;
        RenderSettings.ambientMode=AmbientMode.Trilight;
        RenderSettings.ambientSkyColor=new Color(.64f,.70f,.74f);
        RenderSettings.ambientEquatorColor=new Color(.54f,.58f,.60f);
        RenderSettings.ambientGroundColor=new Color(.36f,.36f,.35f);
        var camera=Camera.main;
        if(camera!=null){camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=fogColor;}
    }
}
