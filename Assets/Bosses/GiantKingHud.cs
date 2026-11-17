using UnityEngine;
using UnityEngine.UI;

internal sealed class GiantKingHud
{
    private readonly RectTransform root,fill;
    private readonly Text value,title;
    private readonly Image rageFlash;
    private int shownHealth=-1,shownMaximum=-1;
    internal GiantKingHud(RectTransform canvas,string name)
    {
        root=Rect("Giant King boss HUD",canvas);
        root.anchorMin=new Vector2(.1f,1f);root.anchorMax=new Vector2(.9f,1f);
        root.pivot=new Vector2(.5f,1f);root.anchoredPosition=new Vector2(0f,-34f);root.sizeDelta=new Vector2(0f,94f);
        var font=canvas.GetComponentInChildren<Text>()?.font ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        title=Label("Boss name",root,font,34,true);
        title.rectTransform.anchorMin=new Vector2(0f,1f);title.rectTransform.anchorMax=Vector2.one;
        title.rectTransform.pivot=new Vector2(.5f,1f);title.rectTransform.anchoredPosition=Vector2.zero;
        title.rectTransform.sizeDelta=new Vector2(0f,42f);title.text=name;
        var bar=Rect("Boss health",root);
        bar.anchorMin=new Vector2(0f,1f);bar.anchorMax=Vector2.one;bar.pivot=new Vector2(.5f,1f);
        bar.anchoredPosition=new Vector2(0f,-49f);bar.sizeDelta=new Vector2(0f,40f);
        Image(bar,new Color32(8,12,10,255));
        var track=Rect("Track",bar);Stretch(track,3f);
        Image(track,new Color32(77,42,43,255));
        fill=Rect("Red fill",track);Stretch(fill,0f);Image(fill,new Color32(197,9,13,255));
        value=Label("Boss HP",bar,font,23,true);Stretch(value.rectTransform,0f);
        var overlay=Rect("Boss rage flash",canvas);Stretch(overlay,0f);overlay.SetAsFirstSibling();
        rageFlash=Image(overlay,new Color(1f,0f,0f,0f));rageFlash.enabled=false;
    }
    private static RectTransform Rect(string name,Transform parent)
    {
        var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);return r;
    }
    private static void Stretch(RectTransform r,float inset)
    {r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=Vector2.one*inset;r.offsetMax=-Vector2.one*inset;}
    private static Image Image(RectTransform rect,Color tint)
    {var i=rect.gameObject.AddComponent<Image>();i.color=tint;i.raycastTarget=false;return i;}
    private static Text Label(string name,Transform parent,Font font,int size,bool bold)
    {
        var r=Rect(name,parent);var t=r.gameObject.AddComponent<Text>();t.font=font;t.fontSize=size;
        t.fontStyle=bold?FontStyle.Bold:FontStyle.Normal;t.color=Color.white;t.alignment=TextAnchor.MiddleCenter;
        t.raycastTarget=false;t.horizontalOverflow=HorizontalWrapMode.Overflow;t.verticalOverflow=VerticalWrapMode.Overflow;
        var o=r.gameObject.AddComponent<Outline>();o.effectColor=Color.black;o.effectDistance=Vector2.one*2f;return t;
    }
    internal void Update(int health,int maximum,bool enraged,float enrageAge,bool visible)
    {
        if(root==null || fill==null || value==null || title==null || rageFlash==null) return;
        root.gameObject.SetActive(visible);
        if(health!=shownHealth || maximum!=shownMaximum)
        {shownHealth=health;shownMaximum=maximum;fill.anchorMax=new Vector2((float)health/Mathf.Max(1,maximum),1f);value.text=$"{health} / {maximum}";}
        title.color=enraged?new Color(1f,.36f,.30f):Color.white;
        float alpha=enraged && visible ? .055f+.27f*(1f-Mathf.Clamp01(enrageAge/1.4f)) : 0f;
        rageFlash.enabled=alpha>0f;rageFlash.color=new Color(.9f,0f,.015f,alpha);
    }
    internal void Hide(){if(root!=null)root.gameObject.SetActive(false);if(rageFlash!=null)rageFlash.enabled=false;}
    internal void Dispose(){if(root!=null)Object.Destroy(root.gameObject);if(rageFlash!=null)Object.Destroy(rageFlash.gameObject);}
}
