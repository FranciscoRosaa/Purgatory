using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed class GiantKingVfx
{
    private sealed class Warning
    {
        internal GameObject Root;
        internal MeshRenderer Fill,Border;
    }
    private sealed class Stone
    {
        internal Warning Warning;
        internal Transform Rock;
        internal bool DustEmitted;
    }
    private readonly Transform actor;
    private readonly GameObject root,rage;
    private readonly Material surface,rockMaterial,glowMaterial,smokeMaterial;
    private readonly Mesh square,border,rockMesh;
    private readonly Warning entrance;
    private readonly List<Stone> stones=new List<Stone>();
    private readonly LineRenderer ring;
    private readonly Transform leftEye,rightEye;
    private readonly Light rageLight;
    private readonly ParticleSystem dust,embers;
    private readonly Texture2D smokeTexture;
    private readonly MaterialPropertyBlock tint=new MaterialPropertyBlock();
    private bool wasRaged;
    private float emberBudget;

    internal GiantKingVfx(Transform actor,Material template)
    {
        this.actor=actor;
        root=new GameObject("Giant King effects");root.transform.SetParent(actor,false);
        surface=new Material(template){name="King warnings",mainTexture=Texture2D.whiteTexture};
        surface.SetColor("_BaseColor",Color.white);surface.SetFloat("_Cull",(float)CullMode.Off);
        rockMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="King stone"};
        rockMaterial.color=new Color(.32f,.29f,.27f);rockMaterial.SetFloat("_Smoothness",.08f);
        glowMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit")){name="King red aura"};
        glowMaterial.color=new Color(3f,.015f,.008f);
        square=Square(false);border=Square(true);rockMesh=Rock();
        entrance=MakeWarning("Arena entrance warning");
        smokeTexture=GiantChargeVfx.SmokeTexture();
        smokeMaterial=new Material(surface){name="King dust",mainTexture=smokeTexture};
        dust=GiantChargeVfx.CreateCloud(root.transform,smokeMaterial,"Pillar eruption dust",240);
        rage=new GameObject("Phase two rage");rage.transform.SetParent(root.transform,false);
        ring=new GameObject("Red rage ring",typeof(LineRenderer)).GetComponent<LineRenderer>();
        ring.transform.SetParent(rage.transform,false);ring.sharedMaterial=glowMaterial;
        ring.useWorldSpace=true;ring.loop=true;ring.positionCount=96;ring.widthMultiplier=.075f;
        ring.shadowCastingMode=ShadowCastingMode.Off;ring.receiveShadows=false;
        leftEye=Eye("Left glowing eye");rightEye=Eye("Right glowing eye");
        rageLight=new GameObject("Rage glow",typeof(Light)).GetComponent<Light>();rageLight.transform.SetParent(rage.transform,false);
        rageLight.type=LightType.Point;rageLight.color=new Color(1f,.015f,.005f);rageLight.range=7f;rageLight.intensity=4f;
        rageLight.shadows=LightShadows.None;
        embers=GiantChargeVfx.CreateCloud(rage.transform,smokeMaterial,"Red rage embers",70);
        Reset();
    }
    private Transform Eye(string name)
    {
        var obj=GameObject.CreatePrimitive(PrimitiveType.Sphere);obj.name=name;obj.transform.SetParent(rage.transform,false);
        Object.Destroy(obj.GetComponent<Collider>());obj.transform.localScale=new Vector3(.15f,.075f,.075f);
        obj.GetComponent<MeshRenderer>().sharedMaterial=glowMaterial;return obj.transform;
    }
    private Warning MakeWarning(string name)
    {
        var w=new Warning{Root=new GameObject(name)};w.Root.transform.SetParent(root.transform,false);
        w.Fill=Surface("Red fill",w.Root.transform,square,surface);
        w.Border=Surface("Red border",w.Root.transform,border,surface);
        w.Border.transform.localPosition=Vector3.up*.008f;w.Root.SetActive(false);return w;
    }
    private MeshRenderer Surface(string name,Transform parent,Mesh mesh,Material material)
    {
        var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent,false);
        go.GetComponent<MeshFilter>().sharedMesh=mesh;var r=go.GetComponent<MeshRenderer>();r.sharedMaterial=material;
        r.shadowCastingMode=material==rockMaterial?ShadowCastingMode.On:ShadowCastingMode.Off;r.receiveShadows=material==rockMaterial;return r;
    }
    private void Show(Warning w,bool visible,Vector3 p,Quaternion r,Vector2 size,float progress)
    {
        w.Root.SetActive(visible);if(!visible)return;
        w.Root.transform.SetPositionAndRotation(p+Vector3.up*.055f,r);w.Root.transform.localScale=new Vector3(size.x,1f,size.y);
        float t=Mathf.Clamp01(progress);
        Tint(w.Fill,Color.Lerp(new Color(.9f,.008f,.08f,.48f),new Color(1f,.003f,.055f,.84f),t));
        Tint(w.Border,new Color(1f,.01f,.035f,.95f));
    }
    private void Tint(MeshRenderer r,Color color){tint.SetColor("_BaseColor",color);r.SetPropertyBlock(tint);}
    internal void UpdateEntrance(bool active,Vector3 position,Quaternion rotation,Vector2 size,float progress,bool dodgeCue)
    {
        Show(entrance,active,position,rotation,size,progress);
        if(!active || !dodgeCue) return;
        Tint(entrance.Fill,new Color(1f,1f,1f,.78f));
        Tint(entrance.Border,Color.white);
    }
    internal void UpdatePillars(IReadOnlyList<GiantKing.Pillar> plans,float elapsed,float size,float height)
    {
        int count=plans?.Count??0;
        while(stones.Count<count)
        {
            var warning=MakeWarning("Pillar warning "+(stones.Count+1));
            var rock=Surface("Rising stone pillar "+(stones.Count+1),root.transform,rockMesh,rockMaterial).transform;
            rock.gameObject.SetActive(false);stones.Add(new Stone{Warning=warning,Rock=rock});
        }
        for(int i=0;i<stones.Count;i++)
        {
            var s=stones[i];
            if(i>=count){s.Warning.Root.SetActive(false);s.Rock.gameObject.SetActive(false);s.DustEmitted=false;continue;}
            var p=plans[i];float age=elapsed-p.ImpactAt;
            bool warning=elapsed>=p.WarningAt && age<0f;
            Show(s.Warning,warning,p.Position,Quaternion.identity,Vector2.one*size,
                Mathf.InverseLerp(p.WarningAt,p.ImpactAt,elapsed));
            bool rock=age>=0f && age<1.35f;s.Rock.gameObject.SetActive(rock);
            if(age<0f)s.DustEmitted=false;
            if(!rock)continue;
            float rise=Mathf.SmoothStep(0f,1f,Mathf.Clamp01(age/.16f));
            float retreat=Mathf.InverseLerp(.8f,1.35f,age);
            float exposed=rise*(1f-retreat);
            s.Rock.position=p.Position+Vector3.up*(-height*(1f-exposed));
            s.Rock.rotation=Quaternion.Euler(0f,(i*137.5f)%360f,0f);
            s.Rock.localScale=new Vector3(size*.82f,height,size*.82f);
            if(!s.DustEmitted)
            {
                s.DustEmitted=true;
                for(int j=0;j<12;j++)
                {
                    float angle=j*Mathf.PI*2f/12f;var dir=new Vector3(Mathf.Sin(angle),0f,Mathf.Cos(angle));
                    GiantChargeVfx.Emit(dust,p.Position+Vector3.up*.08f+dir*size*.3f,dir*1.8f+Vector3.up*.65f,
                        new Color(.55f,.48f,.4f,.6f),UnityEngine.Random.Range(.5f,.9f),UnityEngine.Random.Range(.5f,.9f));
                }
            }
        }
    }
    internal void UpdateRage(bool active,Vector3 head)
    {
        rage.SetActive(active);if(!active){wasRaged=false;return;}
        float radius=2.7f+.10f*Mathf.Sin(Time.time*5f);
        for(int i=0;i<ring.positionCount;i++)
        {float a=i*Mathf.PI*2f/ring.positionCount;ring.SetPosition(i,actor.position+new Vector3(Mathf.Sin(a)*radius,.06f,Mathf.Cos(a)*radius));}
        leftEye.position=head+actor.forward*.28f-actor.right*.17f+Vector3.up*.03f;
        rightEye.position=head+actor.forward*.28f+actor.right*.17f+Vector3.up*.03f;
        leftEye.rotation=rightEye.rotation=actor.rotation;
        rageLight.transform.position=head;
        rageLight.intensity=3f+Mathf.Sin(Time.time*7f)*.8f;
        emberBudget+=Time.deltaTime*24f;
        int count=!wasRaged?28:Mathf.Min(4,Mathf.FloorToInt(emberBudget));
        emberBudget-=Mathf.Floor(emberBudget);
        for(int i=0;i<count;i++)
        {
            float angle=UnityEngine.Random.value*Mathf.PI*2f;
            var p=actor.position+new Vector3(Mathf.Sin(angle)*1.2f,UnityEngine.Random.Range(.4f,2.8f),Mathf.Cos(angle)*1.2f);
            GiantChargeVfx.Emit(embers,p,Vector3.up*UnityEngine.Random.Range(.6f,1.3f),new Color(.8f,.015f,.01f,.5f),
                UnityEngine.Random.Range(.15f,.35f),UnityEngine.Random.Range(.3f,.6f));
        }
        wasRaged=true;
    }
    private static Mesh Square(bool outline)
    {
        var outer=new[]{new Vector3(-.5f,0f,-.5f),new Vector3(-.5f,0f,.5f),new Vector3(.5f,0f,.5f),new Vector3(.5f,0f,-.5f)};
        var v=new List<Vector3>(outer);var t=new List<int>();
        if(!outline)t.AddRange(new[]{0,1,2,0,2,3});
        else {foreach(var p in outer)v.Add(p*.975f);for(int i=0;i<4;i++){int j=(i+1)%4;t.AddRange(new[]{i,j,j+4,i,j+4,i+4});}}
        var m=new Mesh{name=outline?"King warning border":"King warning square"};m.SetVertices(v);m.SetTriangles(t,0);m.RecalculateNormals();return m;
    }
    private static Mesh Rock()
    {
        const int sides=7;var v=new List<Vector3>();var t=new List<int>();
        for(int ring=0;ring<3;ring++)for(int i=0;i<sides;i++)
        {float a=i*Mathf.PI*2f/sides;float radius=ring==2?.32f:ring==1?.48f:.46f;
            v.Add(new Vector3(Mathf.Sin(a)*radius,ring==0?0f:ring==1?.7f:.94f+.06f*Mathf.Sin(i*2.3f),Mathf.Cos(a)*radius));}
        for(int r=0;r<2;r++)for(int i=0;i<sides;i++)
        {int a=r*sides+i,b=r*sides+(i+1)%sides;t.AddRange(new[]{a,b,b+sides,a,b+sides,a+sides});}
        for(int i=1;i<sides-1;i++)t.AddRange(new[]{2*sides,2*sides+i,2*sides+i+1});
        // Split vertices so each face receives a crisp low-poly normal.
        var flat=new Vector3[t.Count];var indices=new int[t.Count];for(int i=0;i<t.Count;i++){flat[i]=v[t[i]];indices[i]=i;}
        var mesh=new Mesh{name="Faceted rising stone"};mesh.vertices=flat;mesh.triangles=indices;mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }
    internal void Reset()
    {
        entrance.Root.SetActive(false);rage.SetActive(false);wasRaged=false;emberBudget=0f;
        foreach(var s in stones){s.Warning.Root.SetActive(false);s.Rock.gameObject.SetActive(false);s.DustEmitted=false;}
        dust.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);embers.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
    }
    internal void Dispose()
    {
        if(root!=null)Object.Destroy(root);
        Object.Destroy(surface);Object.Destroy(rockMaterial);Object.Destroy(glowMaterial);Object.Destroy(smokeMaterial);
        Object.Destroy(square);Object.Destroy(border);Object.Destroy(rockMesh);Object.Destroy(smokeTexture);
    }
}
