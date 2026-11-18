using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

[DefaultExecutionOrder(528)]
public sealed class BlackNinja : CombatEnemy
{
    [Header("Health and roaming")]
    [SerializeField,Min(1)] private int maxHealth=90;
    [SerializeField,Min(0)] private float roamSpeed=2.6f;
    [SerializeField,Min(0)] private float approachSpeed=5.6f;
    [SerializeField,Min(1)] private float roamRadius=6f;
    [SerializeField,Min(.5f)] private float attackRange=9f;
    [Header("Invisibility")]
    [SerializeField,Min(0)] private float invisibilityCooldown=6f;
    [SerializeField,Min(.1f)] private float invisibleDuration=2.8f;
    [SerializeField,Min(.05f)] private float fadeDuration=.45f;
    [SerializeField,Min(.05f)] private float revealDuration=.18f;
    [Header("Big lunge - three consecutive attacks")]
    [SerializeField,Min(0)] private float comboCooldown=4f;
    [SerializeField,Min(.05f)] private float longWindup=.85f;
    [SerializeField,Min(.05f)] private float shortWindup=.48f;
    [SerializeField,Min(.05f)] private float longDuration=.22f;
    [SerializeField,Min(.05f)] private float shortDuration=.24f;
    [SerializeField,Min(.2f)] private float longDistance=12f;
    [SerializeField,Min(.2f)] private float shortDistance=4f;
    [SerializeField,Min(.1f)] private float longWidth=2.4f;
    [SerializeField,Min(.1f)] private float shortWidth=4.2f;
    [SerializeField,Min(0)] private int longDamage=5;
    [SerializeField,Min(0)] private int shortDamage=10;
    [SerializeField,Min(0)] private float betweenLunges=.12f;
    [SerializeField,Min(0)] private float recoveryDuration=.45f;
    [Header("Model, animations and effects")]
    [SerializeField] private Animator animator;
    [SerializeField] private AnimationClip idle,run,firstLunge,secondLunge,thirdLunge;
    [SerializeField] private Material effectMaterial;

    internal enum Phase:byte { Roam,Approach,Windup,Lunge,Between,Recovery,Dead }
    internal struct State
    {
        internal Phase Phase;
        internal Vector3 Position,Origin,Direction;
        internal float Yaw,Elapsed,Length,Opacity;
        internal int Health;
        internal byte Strike;
        internal uint HitSequence,AttackSequence;
        internal ulong HitHistory;
    }
    private WasdBlockMovement player;
    private GameplayHud hud;
    private CoopSession session;
    private EnemyDamageFeedback feedback;
    private BlackNinjaVfx effects;
    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private AnimationClipPlayable[] poses;
    private AnimationClip[] clips;
    private Transform hips,leftFoot,rightFoot;
    private Vector3 visualRest,hipsRest,lastPosition,roamTarget,origin,direction=Vector3.forward;
    private float phaseStarted,nextAttack,nextInvisibility,hiddenUntil,nextRoam,length;
    private float opacity=1f,flashUntil;
    private int strike;
    private Phase phase;
    private uint hitSequence,shownHits,attackSequence;
    private ulong hitHistory;
    private State remote;
    private bool hasRemote,hidden;
    internal override int Health {get;private protected set;}
    internal override int HealthLimit=>Mathf.Max(1,maxHealth);
    internal float Opacity=>opacity;
    internal int CurrentDamage=>strike==0?longDamage:shortDamage;
    internal float CurrentWidth=>strike==0?longWidth:shortWidth;
    internal float Windup=>Mathf.Max(.05f,strike==0?longWindup:shortWindup);
    internal float Duration=>Mathf.Max(.05f,strike==0?longDuration:shortDuration);
    private bool IsReplica=>session!=null && session.InMatch && !session.IsHost;
    private float Age=>Mathf.Max(0,Time.time-phaseStarted);
    private void Awake(){Health=HealthLimit;}
    private void Start()
    {
        if(animator==null || !animator.isHuman || idle==null || run==null || firstLunge==null || secondLunge==null || thirdLunge==null || effectMaterial==null)
        {Debug.LogError("Ninja Preto: missing Humanoid avatar, animations or effects material.",this);enabled=false;return;}
        player=FindAnyObjectByType<WasdBlockMovement>();hud=FindAnyObjectByType<GameplayHud>();session=FindAnyObjectByType<CoopSession>();
        var head=animator.GetBoneTransform(HumanBodyBones.Head);
        if(hud!=null)feedback=new EnemyDamageFeedback((RectTransform)hud.transform,Camera.main,"Ninja Preto",head.position.y-transform.position.y+.6f-2.38f);
        hips=animator.GetBoneTransform(HumanBodyBones.Hips);leftFoot=animator.GetBoneTransform(HumanBodyBones.LeftFoot);rightFoot=animator.GetBoneTransform(HumanBodyBones.RightFoot);
        visualRest=animator.transform.localPosition;hipsRest=hips.localPosition;
        animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        clips=new[]{idle,run,firstLunge,secondLunge,thirdLunge};poses=new AnimationClipPlayable[clips.Length];
        graph=PlayableGraph.Create("Ninja Preto - in-place poses");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);mixer=AnimationMixerPlayable.Create(graph,clips.Length);
        for(int i=0;i<clips.Length;i++){poses[i]=AnimationClipPlayable.Create(graph,clips[i]);poses[i].SetSpeed(0);poses[i].SetApplyFootIK(false);graph.Connect(poses[i],0,mixer,i);}
        AnimationPlayableOutput.Create(graph,"Ninja Preto",animator).SetSourcePlayable(mixer);graph.Play();
        effects=new BlackNinjaVfx(animator.transform,effectMaterial);
        lastPosition=transform.position;phaseStarted=Time.time;nextAttack=Time.time+1.5f;nextInvisibility=Time.time+2.2f;
    }
    private void Update()
    {
        if(IsReplica)
        {
            if(!hasRemote)return;
            float blend=1-Mathf.Exp(-25*Time.deltaTime);
            transform.position=Vector3.Lerp(transform.position,remote.Position,blend);transform.rotation=Quaternion.Slerp(transform.rotation,Quaternion.Euler(0,remote.Yaw,0),blend);
            return;
        }
        Tick(Time.time,Time.deltaTime);
    }
    internal void Tick(float now,float dt)
    {
        if(Health<=0){Reveal();Change(Phase.Dead,now);opacity=Mathf.MoveTowards(opacity,0,dt*6);return;}
        if(hidden && now>=hiddenUntil)Reveal();
        if(!hidden && now>=nextInvisibility && phase is Phase.Roam or Phase.Approach)BeginInvisibility(now);
        opacity=Mathf.MoveTowards(opacity,hidden?0f:1f,dt/Mathf.Max(.05f,hidden?fadeDuration:revealDuration));
        float age=Mathf.Max(0,now-phaseStarted);
        switch(phase)
        {
            case Phase.Roam:
                if(now>=nextAttack && FindTarget(out _,out _)){Change(Phase.Approach,now);break;}
                if(now>=nextRoam || Flat(roamTarget-transform.position).sqrMagnitude<.4f)
                {
                    var center=FindTarget(out var point,out _)?point:transform.position;
                    float angle=UnityEngine.Random.value*Mathf.PI*2;
                    roamTarget=center+new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle))*UnityEngine.Random.Range(2f,roamRadius);
                    if(player!=null)roamTarget=player.ClampToMovementBoundary(roamTarget,1f);
                    nextRoam=now+UnityEngine.Random.Range(.8f,1.8f);
                }
                MoveToward(roamTarget,roamSpeed,dt);break;
            case Phase.Approach:
                if(!FindTarget(out var target,out float distance)){Change(Phase.Roam,now);break;}
                if(distance<=attackRange){strike=0;BeginWindup(now,target);}
                else MoveToward(target,approachSpeed,dt);
                break;
            case Phase.Windup:
                if(age>=Windup)Release(now);break;
            case Phase.Lunge:
                float progress=Mathf.Clamp01(age/Duration);
                if(TryGround(origin+direction*length*progress,out var next))transform.position=next;
                if(progress>=1)Change(strike<2?Phase.Between:Phase.Recovery,now);
                break;
            case Phase.Between:
                if(age>=betweenLunges){strike++;BeginWindup(now,FindTarget(out target,out _)?target:transform.position+direction*4);}
                break;
            case Phase.Recovery:
                if(age>=recoveryDuration){nextAttack=now+comboCooldown;nextRoam=0;Change(Phase.Roam,now);}
                break;
        }
    }
    internal void BeginInvisibility(float now)
    {
        hidden=true;hiddenUntil=now+invisibleDuration;nextInvisibility=hiddenUntil+invisibilityCooldown;
    }
    private void Reveal(){hidden=false;}
    private bool FindTarget(out Vector3 target,out float distance)
    {
        target=player!=null?player.transform.position:transform.position;
        float nearest=player!=null && hud!=null && hud.Health>0?Flat(target-transform.position).sqrMagnitude:float.PositiveInfinity;
        session?.FindNearestLivingPeer(transform.position,ref target,ref nearest);distance=Mathf.Sqrt(nearest);return !float.IsInfinity(nearest);
    }
    private void MoveToward(Vector3 target,float speed,float dt)
    {
        var offset=Flat(target-transform.position);if(offset.sqrMagnitude<.01f)return;
        var heading=offset.normalized;transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(heading),720*dt);
        var next=transform.position+heading*Mathf.Min(offset.magnitude,speed*dt);
        if(player!=null)next=player.ClampToMovementBoundary(next,.4f);
        if(TryGround(next,out next) && (LibraryWorld.Active==null || !LibraryWorld.Active.BlocksMovement(next,.35f)))transform.position=next;
    }
    internal void BeginWindup(float now,Vector3 target)
    {
        Reveal();origin=transform.position;direction=Flat(target-origin).normalized;if(direction.sqrMagnitude<.01f)direction=transform.forward;
        transform.rotation=Quaternion.LookRotation(direction);length=0;
        float reach=strike==0?longDistance:shortDistance;
        int steps=Mathf.CeilToInt(reach/.2f);
        for(int i=1;i<=steps;i++)
        {
            var point=origin+direction*(reach*i/steps);
            if(player!=null && (player.ClampToMovementBoundary(point,.4f)-point).sqrMagnitude>.001f)break;
            if(!TryGround(point,out point) || (LibraryWorld.Active!=null && LibraryWorld.Active.BlocksMovement(point,.35f)))break;
            length=reach*i/steps;
        }
        Change(Phase.Windup,now);
    }
    internal void Release(float now)
    {
        attackSequence++;Change(Phase.Lunge,now);
        var lane=new LungeMan.AttackLane(origin,direction,length,CurrentWidth);
        if(hud!=null && hud.Health>0 && player!=null)
        {
            bool perfect=player.TryGetDash(out var start,out float age) && age<=WasdBlockMovement.PerfectDodgeWindow && lane.Intersects(start,player.transform.position);
            if(perfect)hud.ShowPerfectDodge(player.transform);
            else if(!player.IsDodging && lane.Contains(player.transform.position))hud.TakeDamage(CurrentDamage);
        }
        session?.ResolvePeersInBlackLane(lane,CurrentDamage);
    }
    private bool TryGround(Vector3 point,out Vector3 grounded)
    {
        grounded=point;if(player==null)return true;
        if(!player.TryGetGroundHeight(point,out float y))return false;grounded.y=y;return true;
    }
    private void Change(Phase next,float now){phase=next;phaseStarted=now;}
    internal override void ApplyMeleeStrike(int value){if(value>=0 && value<PlayerMeleeCombo.StrikeCount)ApplyAreaStrike(10+value);}
    internal override void ApplyAreaStrike(int damage)
    {
        if(Health<=0 || damage<=0 || IsReplica)return;
        Health-=RollPlayerDamage(damage,Health,out byte hit);hitHistory=(hitHistory<<8)|hit;hitSequence++;
        if(Health<=0){Reveal();Change(Phase.Dead,Time.time);}
    }
    private void LateUpdate()
    {
        if(!graph.IsValid())return;
        uint unseen=hitSequence-shownHits;
        if(unseen>0){flashUntil=Time.time+.09f;Reveal();}
        for(int i=(int)Math.Min(unseen,8u)-1;i>=0;i--){byte hit=(byte)(hitHistory>>(8*i));feedback?.ShowHit(transform.position,hit&63,(LungeMan.CriticalHit)(hit>>6));}
        shownHits=hitSequence;
        feedback?.Update(transform.position,Health,HealthLimit,hitSequence>0);
        if(opacity<.08f || phase==Phase.Dead)feedback?.Hide();
        Animate();
        effects.Update(phase==Phase.Windup || phase==Phase.Lunge,origin,direction,CurrentWidth,length,phase==Phase.Windup?Mathf.Clamp01(Age/Windup):1f,
            phase==Phase.Lunge && strike==0,attackSequence,opacity,Time.time<flashUntil,Health>0);
        lastPosition=transform.position;
    }
    private void Animate()
    {
        bool attack=phase is Phase.Windup or Phase.Lunge or Phase.Between or Phase.Recovery;
        float moving=Flat(transform.position-lastPosition).magnitude/Mathf.Max(.001f,Time.deltaTime);
        int pose=attack?2+strike:moving>.2f?1:0;
        float time;
        if(attack)
        {
            float fraction=phase==Phase.Windup?.14f*Mathf.Clamp01(Age/Windup):phase==Phase.Lunge?.14f+.56f*Mathf.Clamp01(Age/Duration):.70f+.3f*Mathf.Clamp01(Age/Mathf.Max(.05f,recoveryDuration));
            time=clips[pose].length*Mathf.Min(.999f,fraction);
        }
        else time=Mathf.Repeat(Time.time*(pose==1?1.05f:1f),clips[pose].length);
        for(int i=0;i<poses.Length;i++)mixer.SetInputWeight(i,i==pose?1:0);
        poses[pose].SetTime(time);animator.transform.localPosition=visualRest;graph.Evaluate(0);
        var hip=hips.localPosition;hip.x=hipsRest.x;hip.z=hipsRest.z;hips.localPosition=hip;
        float lowest=Mathf.Min(leftFoot.position.y,rightFoot.position.y);
        animator.transform.position+=Vector3.up*Mathf.Max(0,transform.position.y+.035f-lowest);
    }
    internal State GetState()=>new State{Phase=phase,Position=transform.position,Yaw=transform.eulerAngles.y,Elapsed=Age,Origin=origin,Direction=direction,Length=length,
        Health=Health,Strike=(byte)strike,Opacity=opacity,HitSequence=hitSequence,HitHistory=hitHistory,AttackSequence=attackSequence};
    internal void ApplyState(State state)
    {
        if(!hasRemote)transform.SetPositionAndRotation(state.Position,Quaternion.Euler(0,state.Yaw,0));
        remote=state;hasRemote=true;phase=state.Phase;phaseStarted=Time.time-state.Elapsed;origin=state.Origin;direction=state.Direction;length=state.Length;
        Health=state.Health;strike=state.Strike;opacity=state.Opacity;hitSequence=state.HitSequence;hitHistory=state.HitHistory;attackSequence=state.AttackSequence;
    }
    private void OnDisable(){effects?.Hide();feedback?.Hide();}
    private void OnDestroy(){if(graph.IsValid())graph.Destroy();effects?.Dispose();feedback?.Dispose();}
    private static Vector3 Flat(Vector3 value)=>new Vector3(value.x,0,value.z);
}
