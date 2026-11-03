using System;
using UnityEngine;

namespace HalliGalli.Movement
{
    public enum ClockworkTrapKind { Spears, Arrows }
    public enum ClockworkTrapStage { Safe, Warning, Attack }

    public readonly struct ClockworkTrapSample
    {
        public readonly float cycle, progress, extension;
        public readonly ClockworkTrapStage stage;
        public ClockworkTrapSample(float cycle, ClockworkTrapStage stage, float progress, float extension)
        { this.cycle=cycle;this.stage=stage;this.progress=progress;this.extension=extension; }
    }

    // Evaluate after character movement. Only the authority resolves contacts; everyone animates the shared clock.
    [DefaultExecutionOrder(200)]
    public sealed class ClockworkTrap : MonoBehaviour
    {
        public ClockworkTrapKind kind;
        public CourseSession session;
        public float period=5.6f, phase, warningSeconds=.9f, attackSeconds=1.2f;
        public Vector3 zoneSize=new Vector3(8,2.4f,2);
        public Transform movingRoot;
        public Renderer warning;
        public float spearHeight=1.7f;
        // Authored individual shaft/tip volumes, aligned to the upright, unit-scale trap's local axes.
        public BoxCollider[] hitboxes=Array.Empty<BoxCollider>();
        public int Hits { get; private set; }
        public double LastHitTime { get; private set; } = -1;
        public int LastHitSeat { get; private set; } = -1;
        public float SafeSeconds => Period-WarningDuration-AttackDuration;
        float Period => Mathf.Max(.1f,period);
        float AttackDuration => Mathf.Clamp(attackSeconds,.01f,Period);
        float WarningDuration => Mathf.Clamp(warningSeconds,0,Period-AttackDuration);
        static readonly int ColorId=Shader.PropertyToID("_Color"), EmissionId=Shader.PropertyToID("_EmissionColor");
        Bounds[] volumes;
        Renderer[] projectiles;
        Vector3 authoredRoot;
        MaterialPropertyBlock tint;
        ClockworkTrapStage shownStage;
        bool initialized, shown, hasPrevious, projectilesShown;
        double previousTime;
        readonly ActorSample[] actors=new ActorSample[CourseNetwork.MaxParticipants];
        struct ActorSample
        {
            public CourseParticipant participant;
            public Vector3 feet;
            public float height;
            public int life;
            public bool exposed;
        }

        public ClockworkTrapSample Sample(double time)
        {
            double shifted=time+phase;
            float cycle=(float)(shifted-Math.Floor(shifted/Period)*Period);
            float safe=SafeSeconds;
            if(cycle<safe)return new ClockworkTrapSample(cycle,ClockworkTrapStage.Safe,safe>0?cycle/safe:0,0);
            if(cycle<safe+WarningDuration)return new ClockworkTrapSample(cycle,ClockworkTrapStage.Warning,(cycle-safe)/WarningDuration,0);
            float progress=Mathf.Clamp01((cycle-safe-WarningDuration)/AttackDuration);
            return new ClockworkTrapSample(cycle,ClockworkTrapStage.Attack,progress,Extension(progress));
        }

        float Extension(float progress)
            =>kind==ClockworkTrapKind.Arrows?progress:progress<.2f?progress/.2f:progress<=.8f?1:(1-progress)/.2f;

        // Three launches, half a slot in flight and half clear: 0.2 s crossing / 0.4 s cadence.
        const int ArrowShots=3;
        static bool ArrowFlight(float progress,out float flight)
        {
            float slot=progress*ArrowShots;
            flight=(slot-Mathf.Floor(slot))*2;
            return progress<1&&flight<1;
        }

        Vector3 RootAt(float progress, bool active)
        {
            var position=authoredRoot;
            if(kind==ClockworkTrapKind.Spears)position.y=-spearHeight*(1-(active?Extension(progress):0));
            else position.x=Mathf.Lerp(-zoneSize.x*.5f-1,zoneSize.x*.5f+1,active?progress:0);
            return position;
        }

        bool Initialize()
        {
            if(initialized)return true;
            if(!movingRoot)return false;
            authoredRoot=movingRoot.localPosition;
            volumes=new Bounds[hitboxes.Length];
            for(int i=0;i<hitboxes.Length;i++){
                var box=hitboxes[i];
                if(!box)continue;
                var half=box.size*.5f;
                var bounds=new Bounds(movingRoot.InverseTransformPoint(box.transform.TransformPoint(box.center-half)),Vector3.zero);
                for(int corner=1;corner<8;corner++){
                    var offset=new Vector3((corner&1)==0?-half.x:half.x,(corner&2)==0?-half.y:half.y,(corner&4)==0?-half.z:half.z);
                    bounds.Encapsulate(movingRoot.InverseTransformPoint(box.transform.TransformPoint(box.center+offset)));
                }
                volumes[i]=bounds;
                // These are authoring data, not solid obstacles or a second physics-trigger damage path.
                box.enabled=false;
            }
            if(kind==ClockworkTrapKind.Arrows)projectiles=movingRoot.GetComponentsInChildren<Renderer>(true);
            if(warning)tint=new MaterialPropertyBlock();
            initialized=true;
            return true;
        }

        void Start(){Apply(CourseNetwork.WorldTime);}
        void OnDisable(){hasPrevious=false;}
        public void Apply(double time)
        {
            if(!Initialize())return;
            var sample=Sample(time);
            float motion=sample.progress;
            bool active=sample.stage==ClockworkTrapStage.Attack;
            if(active&&kind==ClockworkTrapKind.Arrows)active=ArrowFlight(sample.progress,out motion);
            movingRoot.localPosition=RootAt(motion,active);
            if(projectiles!=null&&(!shown||projectilesShown!=active)){
                foreach(var renderer in projectiles)renderer.enabled=active;
                projectilesShown=active;
            }
            if(shown&&shownStage==sample.stage)return;
            shown=true;shownStage=sample.stage;
            if(warning){
                Color color=sample.stage==ClockworkTrapStage.Safe?new Color(.16f,.8f,.3f)
                    :sample.stage==ClockworkTrapStage.Warning?new Color(1,.58f,.04f):new Color(1,.08f,.025f);
                tint.SetColor(ColorId,color);tint.SetColor(EmissionId,color*1.7f);warning.SetPropertyBlock(tint);
            }
        }

        void FixedUpdate()
        {
            double now=CourseNetwork.WorldTime;
            Apply(now);
            if(!initialized||!CourseNetwork.Authority||!session||session.participants==null){hasPrevious=false;return;}
            bool continuous=hasPrevious&&now>=previousTime;
            for(int i=0;i<session.participants.Length&&i<actors.Length;i++){
                var participant=session.participants[i];
                if(!participant)continue;
                var motor=participant.Motor;
                var feet=participant.transform.position;
                var prior=actors[i];
                // Physical hazards also affect wrong-card departures and players returning to their seats.
                bool exposed=!participant.IsSeated&&!participant.IsRespawning;
                if(exposed){
                    bool swept=continuous&&prior.participant==participant&&prior.life==participant.RespawnSequence&&prior.exposed;
                    bool hit=swept?SweptIntersectsCapsule(prior.feet,feet,motor.profile.radius,prior.height,motor.BodyHeight,previousTime,now)
                        :IntersectsCapsule(feet,motor.profile.radius,motor.BodyHeight,now);
                    if(hit){participant.BeginFall();Hits++;LastHitTime=now;LastHitSeat=i;}
                }
                actors[i]=new ActorSample{participant=participant,feet=feet,height=motor.BodyHeight,life=participant.RespawnSequence,exposed=!participant.IsSeated&&!participant.IsRespawning};
            }
            hasPrevious=true;previousTime=now;
        }

        public bool IntersectsCapsule(Vector3 feet,float radius,float height,double time)
        {
            if(!Initialize())return false;
            var sample=Sample(time);
            if(sample.stage!=ClockworkTrapStage.Attack)return false;
            float motion=sample.progress;
            if(kind==ClockworkTrapKind.Arrows&&!ArrowFlight(sample.progress,out motion))return false;
            var localFeet=transform.InverseTransformPoint(feet);
            return Contact(localFeet,localFeet,radius,height,height,motion,motion);
        }

        // Split on attack boundaries: an arrow's end-to-start reset is NEVER a crossing projectile.
        // Player and projectile motion are both swept; life changes use a point sample instead of teleport sweeps.
        public bool SweptIntersectsCapsule(Vector3 fromFeet,Vector3 toFeet,float radius,float fromHeight,float toHeight,double fromTime,double toTime)
        {
            if(toTime<=fromTime)return IntersectsCapsule(toFeet,radius,toHeight,toTime);
            if(!Initialize())return false;
            Vector3 from=transform.InverseTransformPoint(fromFeet),to=transform.InverseTransformPoint(toFeet);
            double cycle=Math.Floor((fromTime+phase)/Period);
            double attackStart=(cycle+1)*Period-AttackDuration-phase;
            for(;attackStart<=toTime;attackStart+=Period){
                double start=Math.Max(fromTime,attackStart),end=Math.Min(toTime,attackStart+AttackDuration);
                if(end<start)continue;
                if(kind==ClockworkTrapKind.Arrows){
                    double slot=AttackDuration/ArrowShots,flight=slot*.5;
                    for(int shot=0;shot<ArrowShots;shot++){
                        double launch=attackStart+shot*slot;
                        if(ContactInterval(from,to,radius,fromHeight,toHeight,fromTime,toTime,launch,
                            Math.Max(start,launch),Math.Min(end,launch+flight),flight))return true;
                    }
                }else{
                    // Spear travel is linear up, held, linear down; no missed peak across a long simulation step.
                    double rise=attackStart+AttackDuration*.2,fall=attackStart+AttackDuration*.8;
                    if(start<rise&&ContactInterval(from,to,radius,fromHeight,toHeight,fromTime,toTime,attackStart,start,Math.Min(end,rise)))return true;
                    if(end>=rise&&start<=fall&&ContactInterval(from,to,radius,fromHeight,toHeight,fromTime,toTime,attackStart,Math.Max(start,rise),Math.Min(end,fall)))return true;
                    if(end>fall&&ContactInterval(from,to,radius,fromHeight,toHeight,fromTime,toTime,attackStart,Math.Max(start,fall),end))return true;
                }
            }
            return false;
        }

        bool ContactInterval(Vector3 from,Vector3 to,float radius,float fromHeight,float toHeight,double fromTime,double toTime,double attackStart,double start,double end,double duration=0)
        {
            if(end<start)return false;
            if(duration<=0)duration=AttackDuration;
            float a=(float)((start-fromTime)/(toTime-fromTime)),b=(float)((end-fromTime)/(toTime-fromTime));
            return Contact(Vector3.LerpUnclamped(from,to,a),Vector3.LerpUnclamped(from,to,b),radius,Mathf.Lerp(fromHeight,toHeight,a),Mathf.Lerp(fromHeight,toHeight,b),
                Mathf.Clamp01((float)((start-attackStart)/duration)),Mathf.Clamp01((float)((end-attackStart)/duration)));
        }

        bool Contact(Vector3 from,Vector3 to,float radius,float fromHeight,float toHeight,float fromProgress,float toProgress)
        {
            var rootFrom=RootAt(fromProgress,true);var rootTo=RootAt(toProgress,true);
            // Broad phase rejects the other five crossings before testing their individual shafts.
            float reach=radius+1;
            if(Mathf.Min(from.z,to.z)>zoneSize.z*.5f+reach||Mathf.Max(from.z,to.z)<-zoneSize.z*.5f-reach
                ||Mathf.Min(from.x,to.x)>zoneSize.x*.5f+reach||Mathf.Max(from.x,to.x)<-zoneSize.x*.5f-reach)return false;
            from.y+=radius;to.y+=radius;
            for(int i=0;i<volumes.Length;i++){
                var volume=volumes[i];
                if(volume.size==Vector3.zero)continue;
                Vector3 minA=volume.min+rootFrom,maxA=volume.max+rootFrom,minB=volume.min+rootTo,maxB=volume.max+rootTo;
                if(kind==ClockworkTrapKind.Spears){
                    if(Mathf.Max(maxA.y,maxB.y)<=.001f)continue;
                    minA.y=Mathf.Max(0,minA.y);minB.y=Mathf.Max(0,minB.y);
                    maxA.y=Mathf.Max(0,maxA.y);maxB.y=Mathf.Max(0,maxB.y);
                }
                // A vertical capsule is a sphere against a box extended downward by its centre-line length.
                minA.y-=Mathf.Max(0,fromHeight-2*radius);minB.y-=Mathf.Max(0,toHeight-2*radius);
                if(SweptSphereBox(from,to,radius,minA,maxA,minB,maxB))return true;
            }
            return false;
        }

        // Exact rounded-box distance, not an expanded-AABB corner test. Bounds may also move/extend linearly.
        static bool SweptSphereBox(Vector3 from,Vector3 to,float radius,Vector3 minA,Vector3 maxA,Vector3 minB,Vector3 maxB)
        {
            Vector3 low=from-minA,high=from-maxA;
            Vector3 lowDelta=to-minB-low,highDelta=to-maxB-high;
            float start=0,radiusSquared=radius*radius;
            for(int interval=0;interval<7;interval++){
                float end=1;
                for(int axis=0;axis<3;axis++){
                    NextBoundary(low[axis],lowDelta[axis],start,ref end);
                    NextBoundary(high[axis],highDelta[axis],start,ref end);
                }
                float middle=(start+end)*.5f,quadratic=0,linear=0,constant=0;
                for(int axis=0;axis<3;axis++){
                    float value=0,delta=0;
                    if(low[axis]+lowDelta[axis]*middle<0){value=low[axis];delta=lowDelta[axis];}
                    else if(high[axis]+highDelta[axis]*middle>0){value=high[axis];delta=highDelta[axis];}
                    quadratic+=delta*delta;linear+=value*delta;constant+=value*value;
                }
                float t=quadratic>0?Mathf.Clamp(-linear/quadratic,start,end):start;
                if(constant+2*linear*t+quadratic*t*t<=radiusSquared)return true;
                if(end>=1)break;
                start=end;
            }
            return false;
        }

        static void NextBoundary(float value,float delta,float start,ref float end)
        {
            if(Mathf.Abs(delta)<.000001f)return;
            float t=-value/delta;
            if(t>start+.000001f&&t<end)end=t;
        }
    }
}
