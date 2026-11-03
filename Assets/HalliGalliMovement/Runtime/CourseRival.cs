using UnityEngine;

namespace HalliGalli.Movement
{
    [DefaultExecutionOrder(-20)]
    public sealed class CourseRival:MonoBehaviour
    {
        public CourseParticipant participant;
        public Transform[] route;
        public bool attacksEnabled=true,autoDepart=true;
        public int Waypoint{get;private set;}
        public int CompletedTrips{get;private set;}
        [Range(0,4)] public float surfWideTurn;
        [Range(-1,1)] public int surfSidePreference;
        readonly SurfCourse.Navigation surfNavigation=new SurfCourse.Navigation();
        public int SurfSection=>surfNavigation.section;
        public int SurfPhase=>surfNavigation.phase;
        float departAt=-1,nextAttack,stuckTime;bool returning;int lastRespawn;Vector3 previous;
        int passingWaypoint=-1;
        CourseBranchRoutes.Shortcut passing;
        public int PassesTaken{get;private set;}
        Transform NavigationNode()
        {
            int index=Mathf.Clamp(Waypoint,0,route.Length-1);var normal=route[index];
            if(passingWaypoint!=index){passingWaypoint=index;passing=null;}
            var course=participant.session.roster.branches;
            if(returning || !course || !course.sharedSpiral)return normal;
            var alternative=course.ShortcutAfter(index-1);
            if(passing==null&&alternative!=null&&participant.Motor.Grounded){
                foreach(var other in participant.session.participants){
                    if(other==participant||other.IsSeated||other.IsRespawning)continue;
                    for(int blocked=index;blocked<alternative.exitWaypoint;blocked++){
                        Vector3 offset=other.transform.position-route[blocked].position;
                        if(Mathf.Abs(offset.y)<1&&Vector3.ProjectOnPlane(offset,Vector3.up).magnitude<1.25f){passing=alternative;PassesTaken++;break;}
                    }
                    if(passing!=null)break;
                }
            }
            return passing!=null?passing.landing:normal;
        }
        void Start(){participant.Initialize();previous=transform.position;}
        public CourseParticipant ClosestOpponent()
        {
            CourseParticipant nearest=null;float distance=float.PositiveInfinity;
            foreach(var other in participant.session.participants){
                if(other==participant||other.IsSeated||other.IsRespawning||!other.isActiveAndEnabled||Mathf.Abs(other.transform.position.y-transform.position.y)>.75f)continue;
                float squared=(other.transform.position-transform.position).sqrMagnitude;if(squared<distance){distance=squared;nearest=other;}
            }return nearest;
        }
        void FixedUpdate()
        {
            var session=participant.session;var motor=participant.Motor;
            if(participant.IsSeated){Waypoint=0;passingWaypoint=-1;passing=null;returning=false;surfNavigation.Reset();if(!session.HasCardRules&&autoDepart&&session.RunActive&&!session.BellRung){if(departAt<0)departAt=Time.time+.25f;if(Time.time>=departAt){participant.Stand();departAt=-1;}}return;}
            departAt=-1;if(participant.IsRespawning)return;
            if(lastRespawn!=participant.RespawnSequence){
                lastRespawn=participant.RespawnSequence;Waypoint=0;passingWaypoint=-1;passing=null;surfNavigation.Reset();returning=session.MustReturn(participant);
                if(participant.HasCheckpoint){
                    var checkpoint=session.checkpoints.Saved(participant);
                    Waypoint=Mathf.Clamp(checkpoint.resumeWaypoint,0,route.Length-1);
                    if(session.roster.surf)surfNavigation.section=checkpoint.surfGates;
                }
            }
            if(session.MustReturn(participant)&&!returning){returning=true;Waypoint=Mathf.Clamp(Waypoint-1,0,route.Length-1);}
            Vector3 target;bool advanced=false;
            if(returning&&(Waypoint<0||transform.position.z<.5f)){target=participant.standAnchor.position;if(Vector3.Distance(transform.position,target)<.7f){participant.Sit();CompletedTrips++;return;}}
            else {
                target=NavigationNode().position;
                Vector3 delta=target-transform.position;delta.y=0;
                var landingStep=NavigationNode().GetComponent<ClimbStep>();
                float arrival=landingStep?landingStep.arrivalRadius:.85f;
                if(motor.Grounded&&delta.magnitude<arrival&&Mathf.Abs(transform.position.y-target.y)<.35f){
                    if(returning){Waypoint--;}
                    else if(passing!=null){Waypoint=passing.exitWaypoint;passing=null;passingWaypoint=-1;advanced=true;}
                    else if(Waypoint<route.Length-1){Waypoint++;advanced=true;}
                    else {session.Ring(participant);if(participant.IsSeated)return;returning=session.BellRung;}
                    target=returning&&Waypoint<0?participant.standAnchor.position:NavigationNode().position;
                }
            }
            bool ferryWait=session.ferry&&!returning&&session.ferry.AdjustBotTarget(participant,Waypoint,ref target);
            if(session.clockwork&&!returning)ferryWait|=session.clockwork.AdjustBotTarget(participant,Waypoint,ref target);
            Vector3 toward=Vector3.ProjectOnPlane(target-transform.position,Vector3.up);
            Vector3 desired=Vector3.ClampMagnitude(toward*5.5f,motor.profile.moveSpeed);
            Vector3 steering=motor.Grounded?toward.normalized:desired-Vector3.ProjectOnPlane(motor.Velocity,Vector3.up);
            float inputScale=1;
            var climbStep=session.roster&&(session.roster.branches||session.factory||session.clockwork)&&!returning?NavigationNode().GetComponent<ClimbStep>():null;
            if(climbStep&&climbStep.jump==ClimbJump.Hop&&!motor.Grounded){
                Vector3 horizontal=Vector3.ProjectOnPlane(motor.Velocity,Vector3.up);
                if(horizontal.magnitude>4&&horizontal.magnitude<8f){
                    Vector3 forward=horizontal.normalized,right=Vector3.Cross(Vector3.up,forward);
                    float correction=Vector3.Dot(toward,right);
                    steering=right*(correction>=0?1:-1);
                }else steering=toward.normalized;
                float discriminant=motor.Velocity.y*motor.Velocity.y+2*motor.profile.gravity*(transform.position.y-target.y);
                float remaining=discriminant>=0?(motor.Velocity.y+Mathf.Sqrt(discriminant))/motor.profile.gravity:0;
                if(remaining>.03f&&horizontal.magnitude>1){
                    // Counter-strafe only as much as needed to land inside the ledge instead of overshooting it.
                    float desiredSpeed=(Vector3.Dot(toward,horizontal.normalized)+.25f)/remaining;
                    if(horizontal.magnitude>desiredSpeed){
                        steering=-horizontal.normalized;
                        inputScale=Mathf.Clamp01((horizontal.magnitude-desiredSpeed)/(motor.profile.airAcceleration*motor.profile.moveSpeed*Time.fixedDeltaTime));
                    }
                }
            }
            var surf=session.roster?session.roster.surf:null;
            bool surfing=surf&&!returning&&Waypoint>=3;
            SurfCourse.Input surfInput=default;
            if(surfing){
                surfInput=surf.Guide(participant,route,surfNavigation,surfWideTurn,surfSidePreference);steering=surfInput.steering;
                if(session.Ring(participant))return;
            }
            bool machineWait=motor.Grounded&&(session.factory&&session.factory.HoldForPress(transform.position,target)
                ||session.clockwork&&!returning&&session.clockwork.HoldForTrap(transform.position,target));
            if(machineWait||ferryWait&&toward.magnitude<.3f)steering=Vector3.zero;
            var opponent=ClosestOpponent();Vector3 toOpponent=opponent?Vector3.ProjectOnPlane(opponent.transform.position-transform.position,Vector3.up):Vector3.zero;
            bool duel=attacksEnabled&&opponent&&toOpponent.magnitude<1.8f;
            // Plant briefly while striking so the contact window faces the target; air momentum is retained.
            if(duel&&motor.Grounded&&participant.Combat.IsAttacking)steering=Vector3.zero;
            Vector3 face=duel?toOpponent:surfing?surfInput.forward:toward;float yaw=face.sqrMagnitude>.01f?Quaternion.LookRotation(face).eulerAngles.y:transform.eulerAngles.y;
            Vector3 local=Quaternion.Inverse(Quaternion.Euler(0,yaw,0))*steering.normalized*inputScale;
            // The row starts close to the edge; leave enough ground to accelerate before the first jump.
            float edgeLookAhead=session.roster&&Waypoint==0&&!returning?.32f:climbStep?.42f:.72f;
            bool gap=motor.Grounded&&toward.magnitude>1 && !Physics.Raycast(transform.position+Vector3.up*.5f+toward.normalized*edgeLookAhead,Vector3.down,1.05f,~((1<<8)|(1<<9)),QueryTriggerInteraction.Ignore);
            bool rise=climbStep&&motor.Grounded&&target.y-transform.position.y>.38f&&Physics.Raycast(transform.position+Vector3.up*.4f,toward.normalized,.85f,~((1<<8)|(1<<9)),QueryTriggerInteraction.Ignore);
            // Higher ledges need a normal jump-duck now that all racers use walking speed.
            bool highLedge=climbStep&&Waypoint>0&&target.y-route[Waypoint-1].position.y>.4f;
            bool duck=climbStep&&(climbStep.jump==ClimbJump.Duck||highLedge)&&!motor.Grounded&&motor.Velocity.y<4.8f;
            var priorStep=climbStep&&Waypoint>0?route[Waypoint-1].GetComponent<ClimbStep>():null;
            bool hop=advanced&&climbStep&&priorStep&&climbStep.jump==ClimbJump.Hop&&priorStep.jump==ClimbJump.Hop&&motor.Grounded
                &&motor.HorizontalSpeed>=7.2f&&Vector3.Dot(Vector3.ProjectOnPlane(motor.Velocity,Vector3.up).normalized,toward.normalized)>.95f;
            motor.Simulate(new MovementCommand{move=new Vector2(local.x,local.z),yaw=yaw,crouch=duck,jumpPressed=!machineWait&&!ferryWait&&(surfing?surfInput.jump:gap||hop||rise)},Time.fixedDeltaTime);
            if(duel&&Time.time>=nextAttack){bool kick=participant.Combat.AttackSequence%3==0;if(kick)participant.Combat.TryKick();else participant.Combat.TryPush();nextAttack=Time.time+(kick?1.45f:.95f);}
            if(Vector3.Distance(previous,transform.position)<.005f&&!duel&&!machineWait&&!ferryWait)stuckTime+=Time.fixedDeltaTime;else stuckTime=0;previous=transform.position;
            if(stuckTime>3){participant.BeginFall();stuckTime=0;}
        }
    }
}
