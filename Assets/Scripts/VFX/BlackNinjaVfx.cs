using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed class BlackNinjaVfx
{
    private sealed class Skin
    {
        internal Renderer Renderer;
        internal Material[] Original,Faded;
        internal bool Visible,Fading;
    }
    private sealed class Echo
    {
        internal GameObject Object;
        internal Mesh Mesh;
        internal MeshRenderer Renderer;
        internal float Started;
    }
    private readonly Transform visual;
    private readonly List<Skin> skins=new List<Skin>();
    private readonly List<Echo> echoes=new List<Echo>();
    private readonly GameObject warning;
    private readonly Mesh quad;
    private readonly MeshRenderer track,fill;
    private readonly Material material;
    private readonly MaterialPropertyBlock tint=new MaterialPropertyBlock();
    private readonly ParticleSystem wind;
    private float nextEcho;
    private uint shownAttack;
    internal BlackNinjaVfx(Transform visual,Material template)
    {
        this.visual=visual;
        foreach(var renderer in visual.GetComponentsInChildren<Renderer>(true))
        {
            var original=renderer.sharedMaterials;var faded=new Material[original.Length];
            for(int i=0;i<original.Length;i++)
            {
                faded[i]=new Material(original[i]){name=original[i].name+" - Ninja fade",renderQueue=3000};
                faded[i].SetFloat("_Surface",1);faded[i].SetFloat("_Blend",0);faded[i].SetFloat("_ZWrite",0);
                faded[i].SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);faded[i].SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
                faded[i].SetFloat("_AlphaClip",0);faded[i].EnableKeyword("_SURFACE_TYPE_TRANSPARENT");faded[i].DisableKeyword("_ALPHATEST_ON");
            }
            skins.Add(new Skin{Renderer=renderer,Original=original,Faded=faded,Visible=renderer.enabled});
        }
        material=new Material(template){mainTexture=Texture2D.whiteTexture,name="Ninja warning and motion echo"};
        warning=new GameObject("Ninja Preto - red attack warning");
        quad=new Mesh{name="Ninja lunge telegraph"};
        quad.vertices=new[]{new Vector3(-.5f,0,0),new Vector3(-.5f,0,1),new Vector3(.5f,0,1),new Vector3(.5f,0,0)};
        quad.uv=new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right};quad.colors=new[]{Color.white,Color.white,Color.white,Color.white};
        quad.triangles=new[]{0,1,2,0,2,3};quad.RecalculateNormals();
        track=Strip("Full warning",0);fill=Strip("Red charge",.006f);warning.SetActive(false);
        wind=DashPresentation.CreateWind(visual.parent,template,"Ninja dash - blurred wind");
    }
    private MeshRenderer Strip(string name,float lift)
    {
        var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(warning.transform,false);go.transform.localPosition=Vector3.up*lift;
        go.GetComponent<MeshFilter>().sharedMesh=quad;var renderer=go.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;
        renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;return renderer;
    }
    internal void Update(bool showWarning,Vector3 origin,Vector3 direction,float width,float length,float progress,bool blurred,uint attack,float opacity,bool white,bool alive)
    {
        warning.SetActive(showWarning && alive && length>.01f);
        if(showWarning)
        {
            warning.transform.SetPositionAndRotation(origin+Vector3.up*.045f,Quaternion.LookRotation(direction));
            track.transform.localScale=new Vector3(width,1,length);fill.transform.localScale=new Vector3(width,1,length*progress);
            Tint(track,new Color(.95f,.10f,.15f,.24f));Tint(fill,new Color(1,.025f,.045f,Mathf.Lerp(.30f,.72f,progress)));
        }
        Fade(opacity,white,alive);
        if(attack!=shownAttack){shownAttack=attack;if(alive)CombatAudio.LungeCut(visual.position);}
        if(blurred && alive && Time.time>=nextEcho)
        {
            nextEcho=Time.time+.035f;MakeEcho();
            if(!wind.isPlaying)wind.Play();
            var side=Vector3.Cross(Vector3.up,direction);
            for(int i=0;i<8;i++)wind.Emit(new ParticleSystem.EmitParams{
                position=visual.position+Vector3.up*UnityEngine.Random.Range(.2f,1.7f)+side*UnityEngine.Random.Range(-.6f,.6f),
                velocity=-direction*UnityEngine.Random.Range(6f,13f),startLifetime=.16f,startSize=.12f,
                startColor=new Color(.82f,.91f,1f,.65f)},1);
        }
        for(int i=echoes.Count-1;i>=0;i--)
        {
            float age=(Time.time-echoes[i].Started)/.18f;
            if(age>=1){Object.Destroy(echoes[i].Object);Object.Destroy(echoes[i].Mesh);echoes.RemoveAt(i);continue;}
            Tint(echoes[i].Renderer,new Color(.6f,.72f,.83f,(1-age)*.28f));
        }
    }
    private void Fade(float opacity,bool white,bool alive)
    {
        foreach(var skin in skins)
        {
            if(skin.Renderer==null)continue;
            bool fading=opacity<.999f;
            if(skin.Fading!=fading){skin.Fading=fading;skin.Renderer.sharedMaterials=fading?skin.Faded:skin.Original;}
            skin.Renderer.enabled=skin.Visible && opacity>.003f;
            skin.Renderer.shadowCastingMode=opacity>.95f && alive?ShadowCastingMode.On:ShadowCastingMode.Off;
            for(int i=0;i<skin.Original.Length;i++)
            {
                var source=skin.Original[i];tint.Clear();
                var color=white?new Color(1.75f,1.75f,1.75f,opacity):source.GetColor("_BaseColor");color.a=opacity;
                tint.SetColor("_BaseColor",color);tint.SetTexture("_BaseMap",white?Texture2D.whiteTexture:source.GetTexture("_BaseMap")??Texture2D.whiteTexture);
                skin.Renderer.SetPropertyBlock(tint,i);
            }
        }
    }
    private void MakeEcho()
    {
        var pieces=new List<CombineInstance>();var baked=new List<Mesh>();
        foreach(var skin in skins)
        {
            Mesh mesh=null;
            if(skin.Renderer is SkinnedMeshRenderer skinned){mesh=new Mesh();skinned.BakeMesh(mesh);baked.Add(mesh);}
            else {var filter=skin.Renderer.GetComponent<MeshFilter>();if(filter!=null)mesh=filter.sharedMesh;}
            if(mesh==null)continue;
            for(int sub=0;sub<mesh.subMeshCount;sub++)pieces.Add(new CombineInstance{mesh=mesh,subMeshIndex=sub,transform=skin.Renderer.localToWorldMatrix});
        }
        if(pieces.Count==0)return;
        var combined=new Mesh{name="Ninja motion blur silhouette",indexFormat=IndexFormat.UInt32};combined.CombineMeshes(pieces.ToArray(),true,true);
        foreach(var mesh in baked)Object.Destroy(mesh);
        var go=new GameObject("Ninja dash afterimage",typeof(MeshFilter),typeof(MeshRenderer));go.GetComponent<MeshFilter>().sharedMesh=combined;
        var renderer=go.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        Tint(renderer,new Color(.6f,.72f,.83f,.28f));echoes.Add(new Echo{Object=go,Mesh=combined,Renderer=renderer,Started=Time.time});
    }
    private void Tint(Renderer renderer,Color color){tint.Clear();tint.SetColor("_BaseColor",color);renderer.SetPropertyBlock(tint);}
    internal void Hide(){if(warning!=null)warning.SetActive(false);if(wind!=null)wind.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);}
    internal void Dispose()
    {
        foreach(var skin in skins){if(skin.Renderer!=null){skin.Renderer.sharedMaterials=skin.Original;skin.Renderer.SetPropertyBlock(null);}foreach(var m in skin.Faded)Object.Destroy(m);}
        foreach(var echo in echoes){Object.Destroy(echo.Object);Object.Destroy(echo.Mesh);}echoes.Clear();
        Object.Destroy(warning);Object.Destroy(quad);Object.Destroy(material);if(wind!=null)Object.Destroy(wind.gameObject);
    }
}
