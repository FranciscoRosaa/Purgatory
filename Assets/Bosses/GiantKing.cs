using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

[DefaultExecutionOrder(525)]
public sealed class GiantKing : CombatEnemy
{
    [Header("Boss")]
    [SerializeField] private string displayName = "GIGANTE REI";
    [SerializeField, Min(1)] private int maxHealth = 1200;
    [SerializeField, Min(0f)] private float chaseSpeed = 2.1f;
    [SerializeField, Min(.1f)] private float stopDistance = 2.8f;
    [SerializeField, Min(.1f)] private float skillRange = 24f;
    [SerializeField, Min(0f)] private float skillCooldown = 2.8f;
    [Header("Model and animation")]
    [SerializeField] private Transform visual;
    [SerializeField] private Animator animator;
    [SerializeField] private AnimationClip idleAnimation, walkAnimation, chargeAnimation, jumpAnimation;
    [SerializeField] private AnimationClip rockAnimation, rageAnimation;
    [SerializeField] private Material effectMaterial;
    [Header("Entrance - arena shockwave")]
    [SerializeField, Min(.2f)] private float entranceWindup = 2.5f;
    [SerializeField, Min(.05f), Tooltip("White warning before entrance damage. The player can use this cue to time a dodge.")]
    private float entranceDodgeCueDuration = .3f;
    [SerializeField, Min(1f)] private float safeEdgeWidth = 4.5f;
    [SerializeField, Min(0)] private int entranceDamage = 25;
    [Header("Sprint - same windup and effects as the Giant")]
    [SerializeField, Min(.1f)] private float sprintWindup = 1.2f;
    [SerializeField, Min(.1f)] private float sprintDuration = 3f;
    [SerializeField, Min(.1f)] private float sprintDistance = 10f;
    [SerializeField, Min(.1f)] private float rageSprintDistance = 20f;
    [SerializeField, Min(.1f)] private float contactRadius = 1.65f;
    [SerializeField, Min(0)] private int sprintDamage = 12;
    [Header("Jump")]
    [SerializeField, Min(.1f)] private float jumpRange = 14f;
    [SerializeField, Min(.1f)] private float jumpSquareSize = 11f;
    [SerializeField, Min(0f)] private float jumpHeight = 2.2f;
    [SerializeField, Min(.01f)] private float jumpTakeoff = .52f;
    [SerializeField, Min(.02f)] private float jumpLanding = 1.53f;
    [SerializeField, Min(0)] private int jumpDamage = 18;
    [SerializeField, Range(1,5)] private int rageJumpCount = 2;
    [Header("Bouncing stones")]
    [SerializeField] private Vector2Int pillarCount = new Vector2Int(7,10);
    [SerializeField] private Vector2Int ragePillarCount = new Vector2Int(14,20);
    [SerializeField, Min(1f)] private float pillarRadius = 14f;
    [SerializeField, Min(.5f)] private float pillarSquareSize = 3.2f;
    [SerializeField, Min(.2f)] private float pillarWarning = 1.1f;
    [SerializeField, Min(.02f)] private float pillarStagger = .13f;
    [SerializeField, Min(.1f)] private float pillarHeight = 4f;
    [SerializeField, Min(0)] private int pillarDamage = 15;
    [Header("Second phase")]
    [SerializeField, Range(.05f,.95f)] private float rageHealthFraction = .5f;
    [SerializeField, Min(.2f)] private float rageDuration = 2.2f;
    [SerializeField, Min(1f)] private float rageDamageMultiplier = 1.5f;
    [SerializeField, Range(.2f,1f)] private float rageCooldownMultiplier = .7f;
    [SerializeField, Range(.2f,1f)] private float rageWarningMultiplier = .8f;
    [SerializeField] private Collider[] walkableGround;
    [SerializeField] private BoxCollider movementBoundary;

    internal enum Phase : byte { Entrance, Chase, SprintWindup, Sprint, Jump, Rocks, Recovery, Enrage, Dead }
    internal struct State
    {
        internal Phase Phase;
        internal Vector3 Position, Target, Origin;
        internal float Yaw, Elapsed, HazardElapsed;
        internal int Health;
        internal bool Enraged;
        internal uint HitSequence, AttackSequence, HazardSeed, ImpactSequence;
        internal ulong HitHistory;
        internal byte JumpNumber;
    }
    internal sealed class Pillar
    {
        internal Vector3 Position;
        internal float WarningAt, ImpactAt;
        internal bool Resolved;
    }

    private WasdBlockMovement player;
    private GameplayHud hud;
    private CoopSession session;
    private Phase phase;
    private bool enraged, hasRemote, impacted, localSprintHit;
    private float phaseStarted, nextSkillAt, recoveryUntil, hazardStarted = float.NegativeInfinity;
    private int skill, jumpNumber;
    private Vector3 direction, target, origin, visualRest, hipsRest;
    private Transform hips, leftFoot, rightFoot, chest, head;
    private uint attackSequence, hazardSeed, impactSequence, shownImpact;
    private uint hitSequence, shownHits;
    private ulong hitHistory;
    private State remote;
    private List<Pillar> pillars;
    private readonly HashSet<byte> sprintPeers = new HashSet<byte>();
    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private AnimationClipPlayable[] poses;
    private AnimationClip[] clips;
    private GiantChargeVfx chargeVfx;
    private GiantJumpVfx jumpVfx;
    private GiantKingVfx vfx;
    private GiantKingHud bossHud;
    private EnemyDamageFeedback feedback;
    private CharacterWhiteFlash hitFlash;
    private Renderer[] modelRenderers;
    private bool[] modelRendererDefaults;

    internal override int Health { get; private protected set; }
    internal override int HealthLimit => Mathf.Max(1,maxHealth);
    internal bool Enraged => enraged;
    internal int SprintDamage => Damage(sprintDamage);
    internal float SprintDistance => enraged ? Mathf.Max(.1f,rageSprintDistance) : Mathf.Max(.1f,sprintDistance);
    internal float PillarWarning => Mathf.Max(.2f,pillarWarning) * (enraged ? rageWarningMultiplier : 1f);
    internal float PillarStagger => Mathf.Max(.02f,pillarStagger) * (enraged ? rageWarningMultiplier : 1f);
    internal float EntranceDamageDelay => Mathf.Max(.2f,entranceWindup)+Mathf.Max(.05f,entranceDodgeCueDuration);
    internal float JumpDuration => jumpAnimation != null ? Mathf.Max(jumpLanding+.1f,jumpAnimation.length) : 2.47f;
    internal float RockDuration => pillars != null && pillars.Count>0 ? pillars[pillars.Count-1].ImpactAt+1.35f : 1f;
    private bool IsReplica => session != null && session.InMatch && !session.IsHost;

    private void Awake()
    {
        Health=HealthLimit;
        modelRenderers=visual!=null ? visual.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
        modelRendererDefaults=new bool[modelRenderers.Length];
        for(int i=0;i<modelRenderers.Length;i++) modelRendererDefaults[i]=modelRenderers[i].enabled;
        SetModelVisible(false);
    }
    private void SetModelVisible(bool visible)
    {
        if(modelRenderers==null) return;
        for(int i=0;i<modelRenderers.Length;i++)
        {
            var renderer=modelRenderers[i];
            if(renderer==null) continue;
            bool enabled=visible && modelRendererDefaults[i];
            if(renderer.enabled!=enabled) renderer.enabled=enabled;
        }
    }
    internal override void ConfigureForWave(WasdBlockMovement source)
    {
        if(source==null) return;
        player=source;
        walkableGround=source.WalkableGround;
        movementBoundary=source.MovementBoundary;
    }
    private void Start()
    {
        player=FindAnyObjectByType<WasdBlockMovement>();
        hud=FindAnyObjectByType<GameplayHud>();
        session=FindAnyObjectByType<CoopSession>();
        if(player!=null && (walkableGround==null || walkableGround.Length==0)) ConfigureForWave(player);
        if(!hasRemote && !IsReplica)
        {
            Arena(out var arenaCenter,out _,out _);
            transform.position=arenaCenter;
            if(FindTarget(out var point)) Face(point);
        }
        if(visual==null || animator==null || !animator.isHuman || idleAnimation==null ||
            walkAnimation==null || chargeAnimation==null || jumpAnimation==null || effectMaterial==null)
        { Debug.LogError("Gigante Rei: missing humanoid model, animations or VFX material.",this); enabled=false; return; }
        animator.applyRootMotion=false;
        animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        hips=animator.GetBoneTransform(HumanBodyBones.Hips);
        head=animator.GetBoneTransform(HumanBodyBones.Head);
        chest=animator.GetBoneTransform(HumanBodyBones.Chest);
        leftFoot=animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        rightFoot=animator.GetBoneTransform(HumanBodyBones.RightFoot);
        visualRest=visual.localPosition;
        if(hips!=null) hipsRest=hips.localPosition;
        clips=new[]{idleAnimation,walkAnimation,chargeAnimation,jumpAnimation,rockAnimation??jumpAnimation,rageAnimation??idleAnimation};
        graph=PlayableGraph.Create("Gigante Rei poses");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        mixer=AnimationMixerPlayable.Create(graph,clips.Length);
        poses=new AnimationClipPlayable[clips.Length];
        for(int i=0;i<clips.Length;i++)
        {
            poses[i]=AnimationClipPlayable.Create(graph,clips[i]);
            poses[i].SetApplyFootIK(false);
            graph.Connect(poses[i],0,mixer,i);
        }
        var output=AnimationPlayableOutput.Create(graph,"Gigante Rei",animator);
        output.SetSourcePlayable(mixer); graph.Play();
        chargeVfx=new GiantChargeVfx(transform,effectMaterial,2.7f);
        jumpVfx=new GiantJumpVfx(transform,effectMaterial);
        vfx=new GiantKingVfx(transform,effectMaterial);
        hitFlash=new CharacterWhiteFlash(visual);
        if(hud!=null)
        {
            bossHud=new GiantKingHud((RectTransform)hud.transform,displayName);
            feedback=new EnemyDamageFeedback((RectTransform)hud.transform,Camera.main,displayName,2f);
        }
        if(!hasRemote) { ChangePhase(Phase.Entrance,Time.time); attackSequence++; }
    }

    private void Update()
    {
        if(IsReplica)
        {
            if(hasRemote)
            {
                transform.position=Vector3.Lerp(transform.position,remote.Position,1f-Mathf.Exp(-22f*Time.deltaTime));
                transform.rotation=Quaternion.Euler(0f,remote.Yaw,0f);
            }
            return;
        }
        Tick(Time.time,Time.deltaTime);
    }
    private void ChangePhase(Phase value,float now) { phase=value; phaseStarted=now; impacted=false; }
    internal void Tick(float now,float delta)
    {
        if(Health<=0 || phase==Phase.Dead) return;
        if(phase!=Phase.Entrance && !enraged && Health<=HealthLimit*rageHealthFraction)
        {
            enraged=true; pillars=null; hazardStarted=float.NegativeInfinity;
            ChangePhase(Phase.Enrage,now); attackSequence++;
            return;
        }
        float age=now-phaseStarted;
        switch(phase)
        {
            case Phase.Entrance:
                if(age>=EntranceDamageDelay)
                {
                    Arena(out var c,out var r,out var s);
                    HitBox(c,r,s,Damage(entranceDamage)); impactSequence++;
                    Recover(now,.9f);
                }
                break;
            case Phase.Chase:
                if(!FindTarget(out var point)) break;
                Face(point);
                float distance=Flat(point-transform.position).magnitude;
                if(distance>stopDistance) Move(direction*Mathf.Max(0f,chaseSpeed)*Mathf.Max(0f,delta));
                if(now>=nextSkillAt && distance<=skillRange)
                {
                    target=point; origin=transform.position; attackSequence++;
                    int chosen=skill++%3;
                    if(chosen==0) { sprintPeers.Clear(); localSprintHit=false; ChangePhase(Phase.SprintWindup,now); CombatAudio.GiantWindup(transform.position); }
                    else if(chosen==1) BeginJump(now,0);
                    else BeginRocks(now);
                }
                break;
            case Phase.SprintWindup:
                if(FindTarget(out var aim)) Face(aim);
                if(age>=Mathf.Max(.1f,sprintWindup))
                { ChangePhase(Phase.Sprint,now); CheckSprint(transform.position,transform.position); }
                break;
            case Phase.Sprint:
                float duration=Mathf.Max(.1f,sprintDuration);
                float step=Mathf.Min(Mathf.Max(0f,delta),Mathf.Max(0f,duration-(age-delta)));
                bool moved=Move(direction*(SprintDistance/duration)*step,true);
                if(!moved || age>=duration) Recover(now,.65f);
                break;
            case Phase.Jump:
                float flight=Mathf.InverseLerp(jumpTakeoff,Mathf.Max(jumpTakeoff+.05f,jumpLanding),age);
                transform.position=Vector3.Lerp(origin,target,flight);
                if(!impacted && age>=jumpLanding)
                {
                    impacted=true; impactSequence++;
                    HitBox(target,transform.rotation,Vector2.one*Mathf.Max(.1f,jumpSquareSize),Damage(jumpDamage));
                }
                if(age>=JumpDuration)
                {
                    if(enraged && jumpNumber+1<rageJumpCount) { attackSequence++; BeginJump(now,jumpNumber+1); }
                    else Recover(now,.65f);
                }
                break;
            case Phase.Rocks:
                if(pillars!=null)
                    foreach(var p in pillars)
                        if(!p.Resolved && now-hazardStarted>=p.ImpactAt)
                        { p.Resolved=true; HitBox(p.Position,Quaternion.identity,Vector2.one*pillarSquareSize,Damage(pillarDamage)); }
                if(age>=RockDuration) {pillars=null;Recover(now,.65f);}
                break;
            case Phase.Recovery:
                if(now>=recoveryUntil) ChangePhase(Phase.Chase,now);
                break;
            case Phase.Enrage:
                if(age>=Mathf.Max(.2f,rageDuration)) { ChangePhase(Phase.Chase,now); nextSkillAt=now+.25f; }
                break;
        }
    }
    private void Recover(float now,float duration)
    {
        ChangePhase(Phase.Recovery,now);
        recoveryUntil=now+duration;
        nextSkillAt=recoveryUntil+Mathf.Max(0f,skillCooldown)*(enraged?rageCooldownMultiplier:1f);
    }
    private bool FindTarget(out Vector3 point)
    {
        point=player!=null?player.transform.position:transform.position;
        float nearest=player!=null && (hud==null || hud.Health>0) ? Flat(point-transform.position).sqrMagnitude : float.PositiveInfinity;
        session?.FindNearestLivingPeer(transform.position,ref point,ref nearest);
        return !float.IsPositiveInfinity(nearest);
    }
    private void Face(Vector3 point)
    {
        var offset=Flat(point-transform.position);
        if(offset.sqrMagnitude<.0001f) return;
        direction=offset.normalized;
        transform.rotation=Quaternion.LookRotation(direction,Vector3.up);
    }
    private bool Move(Vector3 displacement,bool sprint=false)
    {
        int steps=Mathf.CeilToInt(displacement.magnitude/.2f);
        for(int i=0;i<steps;i++)
        {
            var start=transform.position;
            var next=start+displacement/steps;
            var clamped=Clamp(next,1.7f);
            if(Flat(clamped-next).sqrMagnitude>.0025f || !Ground(next,out float y)) return false;
            next.y=y; transform.position=next;
            if(sprint) CheckSprint(start,next);
        }
        return true;
    }
    private void BeginJump(float now,int number)
    {
        jumpNumber=number;
        origin=transform.position;
        if(FindTarget(out var point)) Face(point);
        target=Clamp(origin+direction*Mathf.Min(jumpRange,Flat(point-origin).magnitude),2f);
        if(Ground(target,out float y)) target.y=y; else target=origin;
        ChangePhase(Phase.Jump,now);
        CombatAudio.GiantJump(transform.position);
    }
    private void BeginRocks(float now)
    {
        origin=transform.position;
        hazardSeed=(uint)UnityEngine.Random.Range(1,int.MaxValue);
        hazardStarted=now;
        CreatePillars();
        ChangePhase(Phase.Rocks,now);
    }
    private void CreatePillars()
    {
        var random=new System.Random(unchecked((int)hazardSeed));
        var range=enraged?ragePillarCount:pillarCount;
        int min=Mathf.Clamp(range.x,1,40), max=Mathf.Clamp(range.y,min,40);
        int count=random.Next(min,max+1);
        pillars=new List<Pillar>(count);
        for(int i=0;i<count;i++)
        {
            Vector3 candidate=origin;
            for(int attempt=0;attempt<32;attempt++)
            {
                float angle=(float)random.NextDouble()*Mathf.PI*2f;
                float radius=Mathf.Sqrt((float)random.NextDouble())*Mathf.Max(1f,pillarRadius);
                // Some warnings aim near the target; the rest surround the King.
                var center=i%3==0?target:origin;
                candidate=Clamp(center+new Vector3(Mathf.Sin(angle),0f,Mathf.Cos(angle))*radius,pillarSquareSize*.6f);
                if(Ground(candidate,out float y)) candidate.y=y;
                bool overlap=false;
                foreach(var p in pillars) if(Flat(p.Position-candidate).sqrMagnitude<pillarSquareSize*pillarSquareSize*1.1f) {overlap=true;break;}
                if(!overlap) break;
            }
            pillars.Add(new Pillar{Position=candidate,WarningAt=i*PillarStagger,ImpactAt=i*PillarStagger+PillarWarning});
        }
    }
    private Vector3 Clamp(Vector3 point,float inset)
    {
        if(player!=null) return player.ClampToMovementBoundary(point,inset);
        return point;
    }
    private bool Ground(Vector3 point,out float height)
    {
        height=point.y;
        if(walkableGround==null || walkableGround.Length==0) return true;
        var ray=new Ray(point+Vector3.up*8f,Vector3.down);
        foreach(var c in walkableGround)
            if(c!=null && c.Raycast(ray,out var hit,20f)) {height=hit.point.y;return true;}
        return false;
    }
    internal void Arena(out Vector3 center,out Quaternion rotation,out Vector2 size)
    {
        if(movementBoundary!=null)
        {
            center=movementBoundary.transform.TransformPoint(movementBoundary.center);
            rotation=movementBoundary.transform.rotation;
            var scale=movementBoundary.transform.lossyScale;
            size=new Vector2(Mathf.Max(1f,movementBoundary.size.x*Mathf.Abs(scale.x)-2f*safeEdgeWidth),
                Mathf.Max(1f,movementBoundary.size.z*Mathf.Abs(scale.z)-2f*safeEdgeWidth));
        }
        else {center=transform.position;rotation=Quaternion.identity;size=Vector2.one*50f;}
        if(Ground(center,out float y)) center.y=y;
    }
    internal static bool Contains(Vector3 point,Vector3 center,Quaternion rotation,Vector2 size)
    {
        var local=Quaternion.Inverse(rotation)*(point-center);
        return Mathf.Abs(local.y)<=3f && Mathf.Abs(local.x)<=size.x*.5f && Mathf.Abs(local.z)<=size.y*.5f;
    }
    private int Damage(int amount) => Mathf.Max(0,Mathf.RoundToInt(amount*(enraged?rageDamageMultiplier:1f)));
    private void HitBox(Vector3 center,Quaternion rotation,Vector2 size,int damage)
    {
        if(player!=null && hud!=null && hud.Health>0 && Contains(player.transform.position,center,rotation,size))
        {
            if(player.IsDodging) hud.ShowPerfectDodge(player.transform);
            else hud.TakeDamage(damage);
        }
        session?.ResolvePeersInKingBox(center,rotation,size,damage);
    }
    internal bool SprintContact(Vector3 start,Vector3 end,Vector3 point)
    {
        var segment=Flat(end-start);
        float t=segment.sqrMagnitude>.0001f?Mathf.Clamp01(Vector3.Dot(Flat(point-start),segment)/segment.sqrMagnitude):0f;
        var closest=Vector3.Lerp(start,end,t);
        return Mathf.Abs(point.y-closest.y)<=3f && Flat(point-closest).sqrMagnitude<=contactRadius*contactRadius;
    }
    internal bool MarkSprintPeer(byte id) => sprintPeers.Add(id);
    private void CheckSprint(Vector3 start,Vector3 end)
    {
        if(!localSprintHit && player!=null && hud!=null && hud.Health>0 && SprintContact(start,end,player.transform.position))
        {
            localSprintHit=true;
            if(player.IsDodging) hud.ShowPerfectDodge(player.transform);
            else {hud.TakeDamage(SprintDamage);player.ApplySprintKnockback(direction);session?.RegisterLocalSprintHit();}
        }
        session?.ResolvePeersInKingSprint(this,start,end,direction);
    }

    private void LateUpdate()
    {
        if(!graph.IsValid()) return;
        float elapsed=Mathf.Max(0f,Time.time-phaseStarted);
        bool dead=phase==Phase.Dead;
        bossHud?.Update(Health,HealthLimit,enraged,phase==Phase.Enrage?elapsed:float.PositiveInfinity,!dead);
        PresentHits();
        visual.gameObject.SetActive(!dead);
        SetModelVisible(!dead && phase!=Phase.Entrance);
        if(dead) {vfx.Reset();chargeVfx.Reset();jumpVfx.Reset();return;}
        int pose=phase==Phase.Chase?1:phase==Phase.Sprint || phase==Phase.SprintWindup?2:
            phase==Phase.Jump || phase==Phase.Entrance?3:phase==Phase.Rocks?4:phase==Phase.Enrage?5:0;
        for(int i=0;i<poses.Length;i++) mixer.SetInputWeight(i,i==pose?1f:0f);
        float time=elapsed;
        if(phase==Phase.SprintWindup) time=clips[2].length*.15f*Mathf.SmoothStep(0f,1f,elapsed/Mathf.Max(.1f,sprintWindup));
        else if(phase==Phase.Entrance) time=clips[3].length*Mathf.Clamp01(elapsed/EntranceDamageDelay);
        else if(phase==Phase.Enrage) time=clips[5].length*Mathf.Clamp01(elapsed/Mathf.Max(.2f,rageDuration));
        else if(phase==Phase.Rocks) time=Mathf.Repeat(elapsed,clips[4].length);
        else if(phase==Phase.Chase && (!FindTarget(out var p) || Flat(p-transform.position).magnitude<=stopDistance)) {pose=0;for(int i=0;i<poses.Length;i++)mixer.SetInputWeight(i,i==0?1f:0f);}
        if(pose==0 || pose==1 || pose==2 && phase==Phase.Sprint) time=Mathf.Repeat(time,clips[pose].length);
        poses[pose].SetTime(Mathf.Min(clips[pose].length-.0001f,time));
        visual.localPosition=visualRest;
        graph.Evaluate(0f);
        visual.localRotation=Quaternion.identity;
        if(hips!=null) hips.localPosition=new Vector3(hipsRest.x,hips.localPosition.y,hipsRest.z);
        if(phase==Phase.Jump)
        {
            float t=Mathf.InverseLerp(jumpTakeoff,Mathf.Max(jumpTakeoff+.05f,jumpLanding),elapsed);
            visual.position+=Vector3.up*Mathf.Sin(t*Mathf.PI)*jumpHeight;
        }
        float lowest=float.PositiveInfinity;
        if(leftFoot!=null) lowest=Mathf.Min(lowest,leftFoot.position.y-animator.leftFeetBottomHeight);
        if(rightFoot!=null) lowest=Mathf.Min(lowest,rightFoot.position.y-animator.rightFeetBottomHeight);
        if(!float.IsInfinity(lowest)) visual.position+=Vector3.up*Mathf.Max(0f,transform.position.y+.02f-lowest);
        float windup=Mathf.Max(.1f,sprintWindup);
        chargeVfx.Update(phase==Phase.SprintWindup,phase==Phase.Sprint,Mathf.Clamp01(elapsed/windup),windup,direction,
            Time.deltaTime,chest!=null?chest.position:transform.position+Vector3.up*2f,leftFoot,rightFoot);
        jumpVfx.ShowWarning(phase==Phase.Jump && elapsed<jumpLanding,target,transform.rotation,jumpSquareSize,
            Mathf.Clamp01(elapsed/Mathf.Max(.02f,jumpLanding)));
        if(impactSequence!=shownImpact) {shownImpact=impactSequence;jumpVfx.Impact(phase==Phase.Jump?target:transform.position);CombatAudio.GiantLanding(transform.position);}
        jumpVfx.Update();
        Arena(out var center,out var rotation,out var size);
        vfx.UpdateEntrance(phase==Phase.Entrance,center,rotation,size,elapsed/Mathf.Max(.2f,entranceWindup),
            elapsed>=Mathf.Max(.2f,entranceWindup));
        vfx.UpdateRage(enraged,head!=null?head.position:transform.position+Vector3.up*3f);
        vfx.UpdatePillars(pillars,Time.time-hazardStarted,pillarSquareSize,pillarHeight);
    }
    private void PresentHits()
    {
        uint unseen=hitSequence>shownHits?hitSequence-shownHits:0;
        if(unseen>0) hitFlash.Pulse();
        for(int i=(int)Math.Min(unseen,8u)-1;i>=0;i--)
        {
            byte hit=(byte)(hitHistory>>(i*8));
            feedback?.ShowHit(transform.position,hit&63,(LungeMan.CriticalHit)(hit>>6));
        }
        shownHits=hitSequence;
        feedback?.Update(transform.position,Health,HealthLimit,false);
        hitFlash.Update();
    }
    internal override void ApplyMeleeStrike(int strike)
    { if(strike>=0 && strike<PlayerMeleeCombo.StrikeCount) ApplyAreaStrike(10+strike); }
    internal override void ApplyAreaStrike(int damage)
    {
        if(Health<=0 || damage<=0 || IsReplica) return;
        Health-=RollPlayerDamage(damage,Health,out byte hit);
        hitHistory=(hitHistory<<8)|hit;hitSequence++;
        if(Health==0) {pillars=null;phase=Phase.Dead;}
    }
    internal State GetState() => new State{Phase=phase,Position=transform.position,Yaw=transform.eulerAngles.y,
        Elapsed=Mathf.Max(0f,Time.time-phaseStarted),Health=Health,Enraged=enraged,HitSequence=hitSequence,HitHistory=hitHistory,
        Target=target,Origin=origin,AttackSequence=attackSequence,HazardSeed=hazardSeed,
        HazardElapsed=pillars!=null?Mathf.Max(0f,Time.time-hazardStarted):-1f,JumpNumber=(byte)jumpNumber,ImpactSequence=impactSequence};
    internal void ApplyState(State state)
    {
        if(!hasRemote) transform.position=state.Position;
        remote=state;hasRemote=true;phase=state.Phase;Health=Mathf.Clamp(state.Health,0,HealthLimit);
        enraged=state.Enraged;phaseStarted=Time.time-state.Elapsed;
        target=state.Target;origin=state.Origin;attackSequence=state.AttackSequence;
        direction=Quaternion.Euler(0f,state.Yaw,0f)*Vector3.forward;
        hitSequence=state.HitSequence;hitHistory=state.HitHistory;jumpNumber=state.JumpNumber;impactSequence=state.ImpactSequence;
        if(state.HazardElapsed<0f) pillars=null;
        else
        {
            bool changed=hazardSeed!=state.HazardSeed || pillars==null;
            hazardSeed=state.HazardSeed;hazardStarted=Time.time-state.HazardElapsed;
            if(changed) CreatePillars();
        }
    }
    private void OnDisable()
    {
        bossHud?.Hide();feedback?.Hide();hitFlash?.Reset();
        chargeVfx?.Reset();jumpVfx?.Reset();vfx?.Reset();
    }
    private void OnDestroy()
    {
        if(graph.IsValid()) graph.Destroy();
        bossHud?.Dispose();feedback?.Dispose();chargeVfx?.Dispose();jumpVfx?.Dispose();vfx?.Dispose();
    }
    private static Vector3 Flat(Vector3 v) => new Vector3(v.x,0f,v.z);
}
