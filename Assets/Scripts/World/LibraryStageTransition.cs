using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(-100)]
public sealed class LibraryStageTransition : MonoBehaviour
{
    [SerializeField] private WaveDirector waves;
    [SerializeField] private LibraryWorld library;
    [SerializeField] private GameObject[] firstMap;
    [SerializeField,Min(1)] private int afterWave=4;
    [SerializeField,Min(.1f)] private float fadeDuration=.8f;
    private bool requested,entered;
    private Image overlay;
    private WasdBlockMovement player;
    private PlayerMeleeCombo combat;
    public bool EnteredLibrary=>entered;

    private void Start()
    {
        player=FindAnyObjectByType<WasdBlockMovement>();
        combat=player!=null?player.GetComponent<PlayerMeleeCombo>():null;
    }
    private void Update()
    {
        if(requested || waves==null || library==null)return;
        bool cleared=waves.CurrentWave>afterWave ||
            (waves.CurrentWave==afterWave && waves.Stage==WaveDirector.WaveStage.Complete);
        if(cleared)BeginTransition();
    }
    [ContextMenu("Preview library (Play Mode)")]
    public void BeginTransition()
    {
        if(!Application.isPlaying || requested || library==null)return;
        requested=true;StartCoroutine(Transition());
    }
    private IEnumerator Transition()
    {
        bool moveEnabled=player!=null && player.enabled;
        bool combatEnabled=combat!=null && combat.enabled;
        if(player!=null)player.enabled=false;
        if(combat!=null)combat.enabled=false;
        var hud=FindAnyObjectByType<GameplayHud>();
        if(hud!=null)
        {
            var go=new GameObject("Library transition",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
            overlay=go.GetComponent<Image>();overlay.transform.SetParent(hud.transform,false);
            var rect=overlay.rectTransform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;
            rect.offsetMin=rect.offsetMax=Vector2.zero;overlay.color=new Color(.025f,.018f,.015f,0f);
            overlay.raycastTarget=true;rect.SetAsLastSibling();
        }
        yield return Fade(0f,1f);
        foreach(var root in firstMap)if(root!=null)root.SetActive(false);
        library.gameObject.SetActive(true);
        if(player!=null)
        {
            player.SetEnvironment(new[]{library.WalkableFloor},library.WoodBoundary);
            var session=FindAnyObjectByType<CoopSession>();
            if(session!=null && session.InMatch)session.EnterLibrary(library.transform.position);
            else player.WarpTo(library.transform.position);
        }
        library.RefreshRooms();entered=true;
        yield return Fade(1f,0f);
        if(overlay!=null)Destroy(overlay.gameObject);
        if(player!=null)player.enabled=moveEnabled;
        if(combat!=null)combat.enabled=combatEnabled;
    }
    private IEnumerator Fade(float from,float to)
    {
        float began=Time.unscaledTime;
        while(Time.unscaledTime-began<fadeDuration)
        {
            if(overlay!=null)overlay.color=new Color(.025f,.018f,.015f,
                Mathf.Lerp(from,to,Mathf.SmoothStep(0f,1f,(Time.unscaledTime-began)/fadeDuration)));
            yield return null;
        }
        if(overlay!=null)overlay.color=new Color(.025f,.018f,.015f,to);
    }
    private void OnDestroy(){if(overlay!=null)Destroy(overlay.gameObject);}
}
