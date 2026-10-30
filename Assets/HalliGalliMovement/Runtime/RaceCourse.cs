using System;
using UnityEngine;

namespace HalliGalli.Movement
{
    public enum RaceItem : byte { None, Missile, Banana, Shield }
    public enum RaceEffect : byte { None, Flip, Slip }

    // Only this map owns item simulation. Movement profiles and card/bell rules stay unchanged.
    [DefaultExecutionOrder(175), DisallowMultipleComponent]
    public sealed partial class RaceCourse : MonoBehaviour
    {
        public CourseSession session;
        public Transform[] itemBoxes=Array.Empty<Transform>();
        public float finishZ=220;
        public static RaceCourse Active {get;private set;}
        public const float BoxCooldown=8, ShieldSeconds=5, LockRange=45, FlipSeconds=.85f, SlipSeconds=1.1f;
        const int MaxActors=6, MaxMissiles=8, MaxEntities=32;
        const float MissileSpeed=21, MissileLifetime=5, BananaLifetime=22;
        public int Round {get;private set;}
        public int Leader=>FindLeader();
        static double Clock=>CourseNetwork.WorldTime;
        static int WorldMask=>~((1<<CharacterMotionDriver.BodyLayer)|(1<<CharacterMotionDriver.HandsLayer));
        internal struct ActorState : Unity.Netcode.INetworkSerializeByMemcpy
        {
            public int life,itemSequence,lockTarget,acquisitions,uses,flips,slips,blocked;
            public RaceItem item;
            public RaceEffect effect;
            public double shieldUntil,effectAt,effectUntil,blockedAt;
        }
        internal struct EntityState : Unity.Netcode.INetworkSerializeByMemcpy
        {
            public int id,owner,target,ownerLife,targetLife;
            public RaceItem item;
            public Vector3 position,direction;
            public double expires,armedAt;
        }
        readonly ActorState[] actors=new ActorState[MaxActors];
        readonly EntityState[] entities=new EntityState[MaxEntities];
        readonly float[] nextAI=new float[MaxActors];
        readonly RaycastHit[] worldHits=new RaycastHit[48];
        readonly Collider[] overlapHits=new Collider[32];
        double[] boxReady=Array.Empty<double>();
        Vector3[] boxRest=Array.Empty<Vector3>();
        Quaternion[] boxRotation=Array.Empty<Quaternion>();
        bool initialized,raceRunning;
        int entitySequence,acquisitions,missileHits,bananaHits,blockedHits;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatic(){Active=null;}
        void Awake(){Active=this;}
        void Start()
        {
            if(!session)session=GetComponent<CourseSession>();
            EnsureInitialized();
            session.onDeparture.AddListener(BeginRound);
            session.onBellRung.AddListener(EndRound);
            session.onEveryoneSeated.AddListener(EndRound);
        }
        void EnsureInitialized()
        {
            if(initialized||!session)return;
            boxReady=new double[itemBoxes.Length];boxRest=new Vector3[itemBoxes.Length];boxRotation=new Quaternion[itemBoxes.Length];
            for(int i=0;i<itemBoxes.Length;i++)if(itemBoxes[i]){boxRest[i]=itemBoxes[i].position;boxRotation[i]=itemBoxes[i].rotation;}
            for(int i=0;i<actors.Length;i++){actors[i].lockTarget=-1;actors[i].life=-1;actors[i].blockedAt=-100;}
            InitializeVisuals();initialized=true;
        }
        void OnDestroy()
        {
            if(session){session.onDeparture.RemoveListener(BeginRound);session.onBellRung.RemoveListener(EndRound);session.onEveryoneSeated.RemoveListener(EndRound);}
            RestoreBodies();DisposeVisuals();if(Active==this)Active=null;
        }
        bool Live=>session&&session.RunActive&&!session.BellRung&&(!session.HasCardRules||!session.cardGame.Match.Finished)&&(!CourseNetwork.Active||CourseNetwork.Instance.Playing);
        bool Eligible(CourseParticipant p)=>p&&p.session==session&&p.isActiveAndEnabled&&p.Racing&&p.Receiver.canReceiveHits&&(!session.HasCardRules||session.cardGame.Match.CanClaim(session.cardGame.Index(p)));
        int Seat(CourseParticipant p)=>session&&session.participants!=null?Array.IndexOf(session.participants,p):-1;
        CourseParticipant Participant(int seat)=>session&&session.participants!=null&&seat>=0&&seat<session.participants.Length?session.participants[seat]:null;
        public RaceItem HeldItem(CourseParticipant p){int i=Seat(p);return i>=0&&i<MaxActors?actors[i].item:RaceItem.None;}
        public int ItemSequence(CourseParticipant p){int i=Seat(p);return i>=0&&i<MaxActors?actors[i].itemSequence:0;}
        public float ShieldRemaining(CourseParticipant p){int i=Seat(p);return i>=0&&i<MaxActors?Mathf.Max(0,(float)(actors[i].shieldUntil-Clock)):0;}
        public float EffectRemaining(CourseParticipant p){int i=Seat(p);return i>=0&&i<MaxActors?Mathf.Max(0,(float)(actors[i].effectUntil-Clock)):0;}
        public int Place(CourseParticipant p)
        {
            if(!Eligible(p))return 0;int place=1,seat=Seat(p);
            for(int i=0;i<session.participants.Length;i++){var other=session.participants[i];if(other!=p&&Eligible(other)&&(other.transform.position.z>p.transform.position.z||other.transform.position.z==p.transform.position.z&&i<seat))place++;}
            return place;
        }
        int FindLeader()
        {
            int best=-1;float z=float.NegativeInfinity;
            if(!session||session.participants==null)return best;
            for(int i=0;i<session.participants.Length;i++){var p=session.participants[i];if(Eligible(p)&&p.transform.position.z>z){best=i;z=p.transform.position.z;}}
            return best;
        }
        void BeginRound(){if(CourseNetwork.Authority)ResetRound(true);}
        void EndRound(){ResetRound(false);}
        public void ResetRound(bool running=false)
        {
            EnsureInitialized();if(!initialized)return;
            if(CourseNetwork.Authority)Round++;
            raceRunning=running;
            Array.Clear(boxReady,0,boxReady.Length);Array.Clear(entities,0,entities.Length);
            for(int i=0;i<MaxActors;i++)ClearActor(i);
            RestoreBodies();
        }
        public void ClearParticipant(CourseParticipant p)
        {
            int seat=Seat(p);if(seat<0||seat>=MaxActors)return;ClearActor(seat);
            for(int i=0;i<entities.Length;i++)if(entities[i].item!=RaceItem.None&&(entities[i].owner==seat||entities[i].target==seat))entities[i]=default;
        }
        void ClearActor(int i)
        {
            ref var state=ref actors[i];state.item=RaceItem.None;state.itemSequence++;state.shieldUntil=state.effectUntil=0;state.effect=RaceEffect.None;state.lockTarget=-1;
            var p=Participant(i);state.life=p?p.RespawnSequence:-1;
            if(p){p.Motor.ClearRaceSlip();if(p.Motor.Stunned)p.Motor.ClearStun();}
        }
        void FixedUpdate()
        {
            EnsureInitialized();if(!initialized||!CourseNetwork.Authority)return;
            if(!Live){if(raceRunning)ResetRound();return;}
            if(!raceRunning)ResetRound(true);
            double now=Clock;
            for(int i=0;i<session.participants.Length;i++){
                var p=Participant(i);ref var state=ref actors[i];
                if(!Eligible(p)||state.life!=p.RespawnSequence){if(state.item!=RaceItem.None||state.shieldUntil>0||state.effectUntil>0||state.life!=p.RespawnSequence)ClearParticipant(p);continue;}
                if(state.effectUntil<=now){state.effect=RaceEffect.None;state.effectUntil=0;}
                if(state.shieldUntil<=now)state.shieldUntil=0;
            }
            CollectBoxes(now);
            for(int i=0;i<entities.Length;i++)if(entities[i].item!=RaceItem.None)SimulateEntity(i,now,Time.fixedDeltaTime);
            for(int i=0;i<session.participants.Length;i++){
                var p=Participant(i);if(!Eligible(p))continue;
                var bot=p.GetComponent<CourseRival>();if(bot&&bot.enabled)UseAI(i,p,now);
            }
        }
        void CollectBoxes(double now)
        {
            int leader=FindLeader();
            for(int b=0;b<itemBoxes.Length;b++){
                if(!itemBoxes[b]||boxReady[b]>now)continue;
                int nearest=-1;float distance=1.15f*1.15f;
                for(int i=0;i<session.participants.Length;i++){
                    var p=Participant(i);if(!Eligible(p)||actors[i].item!=RaceItem.None||p.Motor.Stunned)continue;
                    Vector3 delta=p.transform.position-boxRest[b];if(Mathf.Abs(delta.y)>1.7f)continue;
                    float squared=delta.x*delta.x+delta.z*delta.z;if(squared<distance){nearest=i;distance=squared;}
                }
                if(nearest<0)continue;
                ref var state=ref actors[nearest];state.item=nearest==leader?RaceItem.Shield:UnityEngine.Random.value<.5f?RaceItem.Missile:RaceItem.Banana;
                state.itemSequence++;state.acquisitions++;state.lockTarget=-1;acquisitions++;boxReady[b]=now+BoxCooldown;
                nextAI[nearest]=(float)now+UnityEngine.Random.Range(.45f,.9f);
            }
        }
        public CourseParticipant LockTarget(CourseParticipant actor,Vector3 aim)
        {
            if(!Eligible(actor)||!ValidAim(aim))return null;
            aim.Normalize();Vector3 origin=Anchor(actor);CourseParticipant target=null;float score=float.NegativeInfinity;
            for(int i=0;i<session.participants.Length;i++){
                var p=Participant(i);if(p==actor||!Eligible(p))continue;
                Vector3 delta=Anchor(p)-origin;float distance=delta.magnitude;if(distance<.1f||distance>LockRange)continue;
                float dot=Vector3.Dot(aim,delta/distance);if(dot<.75f||Vector3.Dot(actor.transform.forward,delta)<=0)continue;
                float candidate=dot*2-distance/LockRange;
                if(candidate<=score||WorldDistance(origin,delta/distance,distance,.06f)<distance)continue;
                target=p;score=candidate;
            }
            return target;
        }
        public void UseItem(CourseParticipant actor,Vector3 aim)
        {
            if(!CourseNetwork.Authority||!Live||!Eligible(actor)||actor.Motor.Stunned||actor.Motor.Staggered||!ValidAim(aim))return;
            int seat=Seat(actor);if(seat<0||seat>=MaxActors)return;ref var state=ref actors[seat];
            if(state.life!=actor.RespawnSequence)return;
            double now=Clock;int slot=-1;
            switch(state.item){
                case RaceItem.Missile:
                    var target=LockTarget(actor,aim);if(!target)return;slot=FreeEntity(0,MaxMissiles);if(slot<0)return;
                    int targetSeat=Seat(target);Vector3 direction=(Anchor(target)-Anchor(actor)).normalized;
                    entities[slot]=new EntityState{id=++entitySequence,item=RaceItem.Missile,owner=seat,target=targetSeat,ownerLife=actor.RespawnSequence,targetLife=target.RespawnSequence,
                        position=Anchor(actor)+direction*.55f,direction=direction,expires=now+MissileLifetime};state.lockTarget=targetSeat;break;
                case RaceItem.Banana:
                    if(!actor.Motor.Grounded)return;slot=FreeEntity(MaxMissiles,MaxEntities);if(slot<0)return;
                    Vector3 behind=actor.transform.position-actor.transform.forward*1.15f;
                    if(WorldDistance(actor.transform.position+Vector3.up*.2f,-actor.transform.forward,1.15f,.12f)<1.15f||!GroundPoint(behind,out var ground))return;
                    entities[slot]=new EntityState{id=++entitySequence,item=RaceItem.Banana,owner=seat,target=-1,ownerLife=actor.RespawnSequence,
                        position=ground+Vector3.up*.045f,direction=actor.transform.forward,armedAt=now+.85f,expires=now+BananaLifetime};break;
                case RaceItem.Shield:state.shieldUntil=now+ShieldSeconds;break;
                default:return;
            }
            state.item=RaceItem.None;state.itemSequence++;state.uses++;
        }
        int FreeEntity(int begin,int end){for(int i=begin;i<end;i++)if(entities[i].item==RaceItem.None)return i;return -1;}
        static bool ValidAim(Vector3 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)&&v.sqrMagnitude>.01f&&v.sqrMagnitude<10000;
        static Vector3 Anchor(CourseParticipant p)=>p.transform.position+Vector3.up*(p.Motor.BodyHeight*.58f);
        void SimulateEntity(int index,double now,float dt)
        {
            ref var e=ref entities[index];var owner=Participant(e.owner);
            if(e.expires<=now||!Eligible(owner)||owner.RespawnSequence!=e.ownerLife){e=default;return;}
            if(e.item==RaceItem.Banana){
                for(int i=0;i<session.participants.Length;i++){
                    var p=Participant(i);if(!Eligible(p)||!p.Motor.Grounded||i==e.owner&&now<e.armedAt)continue;
                    Vector3 delta=p.transform.position-e.position;
                    if(Mathf.Abs(delta.y)>.6f||delta.x*delta.x+delta.z*delta.z>.8f*.8f)continue;
                    Impact(i,RaceEffect.Slip,e.direction,now);e=default;return;
                }
                return;
            }
            var target=Participant(e.target);
            if(!Eligible(target)||target.RespawnSequence!=e.targetLife){e=default;return;}
            Vector3 toward=Anchor(target)-e.position;
            if(toward.sqrMagnitude>.001f)e.direction=Vector3.RotateTowards(e.direction,toward.normalized,5.5f*dt,0).normalized;
            float travel=MissileSpeed*dt,obstacle=WorldDistance(e.position,e.direction,travel,.14f);
            int hit=-1;float nearest=obstacle;
            for(int i=0;i<session.participants.Length;i++){
                var p=Participant(i);if(i==e.owner||!Eligible(p))continue;
                float distance=CapsuleDistance(p,e.position,e.direction,travel);
                if(distance<=nearest){nearest=distance;hit=i;}
            }
            e.position+=e.direction*Mathf.Min(travel,nearest);
            if(hit>=0){Impact(hit,RaceEffect.Flip,e.direction,now);e=default;return;}
            if(obstacle<=travel)e=default;
        }
        void Impact(int seat,RaceEffect effect,Vector3 direction,double now)
        {
            ref var state=ref actors[seat];var p=Participant(seat);
            if(state.shieldUntil>now){state.blocked++;state.blockedAt=now;blockedHits++;return;}
            if(state.effectUntil>now)return;
            direction=Vector3.ProjectOnPlane(effect==RaceEffect.Slip?p.Motor.Velocity:direction,Vector3.up);
            if(direction.sqrMagnitude<.1f)direction=p.transform.forward;direction.Normalize();
            float duration=effect==RaceEffect.Flip?FlipSeconds:SlipSeconds;
            if(effect==RaceEffect.Flip){
                if(!p.Motor.ApplyStun(duration,.2f))return;
                p.Motor.AddImpulse(direction*3.5f+Vector3.up*6.3f);state.flips++;missileHits++;
            }else{p.Motor.ApplyRaceSlip(direction*Mathf.Max(7,p.Motor.HorizontalSpeed),duration);state.slips++;bananaHits++;}
            state.effect=effect;state.effectAt=now;state.effectUntil=now+duration;
            p.Combat.InterruptAttack();if(p.Lasso)p.Lasso.CancelConnection();
        }
        void UseAI(int seat,CourseParticipant p,double now)
        {
            ref var state=ref actors[seat];if(state.item==RaceItem.None||now<nextAI[seat]||p.Motor.Stunned)return;
            nextAI[seat]=(float)now+.3f;
            if(state.item==RaceItem.Missile){var target=LockTarget(p,p.transform.forward);if(target)UseItem(p,Anchor(target)-Anchor(p));return;}
            bool follower=false;
            for(int i=0;i<session.participants.Length;i++){
                var other=Participant(i);if(other==p||!Eligible(other))continue;Vector3 delta=other.transform.position-p.transform.position;
                if(delta.sqrMagnitude<18*18&&Vector3.Dot(p.transform.forward,delta)<-1&&Mathf.Abs(delta.x)<3){follower=true;break;}
            }
            if(state.item==RaceItem.Banana){if(follower)UseItem(p,p.transform.forward);return;}
            bool incoming=false;
            for(int i=0;i<MaxMissiles;i++)if(entities[i].item==RaceItem.Missile&&entities[i].target==seat&&(entities[i].position-p.transform.position).sqrMagnitude<20*20){incoming=true;break;}
            if(state.shieldUntil<=now&&(incoming||follower))UseItem(p,p.transform.forward);
        }
        float WorldDistance(Vector3 origin,Vector3 direction,float distance,float radius)
        {
            int overlaps=Physics.OverlapSphereNonAlloc(origin,radius,overlapHits,WorldMask,QueryTriggerInteraction.Ignore);
            if(overlaps==overlapHits.Length)return 0;
            for(int i=0;i<overlaps;i++)if(!overlapHits[i].GetComponentInParent<CourseParticipant>())return 0;
            int count=Physics.SphereCastNonAlloc(origin,radius,direction,worldHits,distance,WorldMask,QueryTriggerInteraction.Ignore);
            float nearest=distance+.01f;if(count==worldHits.Length)return 0;
            for(int i=0;i<count;i++)if(!worldHits[i].collider.GetComponentInParent<CourseParticipant>())nearest=Mathf.Min(nearest,worldHits[i].distance);
            return nearest;
        }
        bool GroundPoint(Vector3 position,out Vector3 point)
        {
            int count=Physics.RaycastNonAlloc(position+Vector3.up*.6f,Vector3.down,worldHits,1.6f,WorldMask,QueryTriggerInteraction.Ignore);
            float nearest=float.PositiveInfinity;point=default;
            for(int i=0;i<count;i++)if(worldHits[i].normal.y>.7f&&!worldHits[i].collider.GetComponentInParent<CourseParticipant>()&&worldHits[i].distance<nearest){nearest=worldHits[i].distance;point=worldHits[i].point;}
            return !float.IsPositiveInfinity(nearest);
        }
        static float CapsuleDistance(CourseParticipant p,Vector3 origin,Vector3 direction,float step)
        {
            float bodyRadius=p.Motor.profile.radius,radius=bodyRadius+.2f;
            Vector3 bottom=p.transform.position+Vector3.up*bodyRadius,top=p.transform.position+Vector3.up*(p.Motor.BodyHeight-bodyRadius);
            Vector3 axis=new Vector3(bottom.x,Mathf.Clamp(origin.y,bottom.y,top.y),bottom.z);
            if((origin-axis).sqrMagnitude<=radius*radius)return 0;
            float best=Mathf.Min(SphereDistance(origin,direction,bottom,radius),SphereDistance(origin,direction,top,radius));
            Vector3 flat=new Vector3(direction.x,0,direction.z),delta=new Vector3(origin.x-bottom.x,0,origin.z-bottom.z);
            float a=flat.sqrMagnitude,b=Vector3.Dot(delta,flat),c=delta.sqrMagnitude-radius*radius,d=b*b-a*c;
            if(a>.00001f&&d>=0){float t=(-b-Mathf.Sqrt(d))/a,y=origin.y+direction.y*t;if(t>=0&&y>=bottom.y&&y<=top.y)best=Mathf.Min(best,t);}
            return best<=step?best:float.PositiveInfinity;
        }
        static float SphereDistance(Vector3 origin,Vector3 direction,Vector3 center,float radius)
        {
            Vector3 delta=origin-center;float b=Vector3.Dot(delta,direction),d=b*b-delta.sqrMagnitude+radius*radius;
            if(d<0)return float.PositiveInfinity;float t=-b-Mathf.Sqrt(d);return t>=0?t:float.PositiveInfinity;
        }
    }
}
