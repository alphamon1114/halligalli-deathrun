using System;
using UnityEngine;

namespace HalliGalli.Movement
{
    public sealed class ClockworkCourse : MonoBehaviour
    {
        public const string ScenePath="Assets/HalliGalliMovement/Demo/ClockworkCourse.unity";
        public CourseSession session;
        public ClockworkTrap[] traps=Array.Empty<ClockworkTrap>();
        public FactoryPress[] presses=Array.Empty<FactoryPress>();
        public FactorySurface[] movingPlatforms=Array.Empty<FactorySurface>();
        public Ferry[] ferries=Array.Empty<Ferry>();
        [Serializable] public sealed class Ferry
        {
            public FactorySurface carrier;
            public BoxCollider boardingDock,arrivalDock;
            public Transform waitingPoint;
            public int boardWaypoint,exitWaypoint;
            [NonSerialized] internal BoxCollider surface;
        }
        float walkSpeed=4.5f;
        void Start()
        {
            if(session&&session.participants!=null&&session.participants.Length>0)
                walkSpeed=session.participants[0].Motor.profile.moveSpeed;
        }

        // Brake on the safe approach, never in the crossing. Reserve enough green time to clear the far edge.
        // The additional approach margin covers normal ground braking and acceleration without changing the motor.
        public bool HoldForTrap(Vector3 feet,Vector3 target)
        {
            double now=CourseNetwork.WorldTime;
            foreach(var trap in traps){
                if(!trap||!trap.isActiveAndEnabled)continue;
                var localFeet=trap.transform.InverseTransformPoint(feet);
                var localTarget=trap.transform.InverseTransformPoint(target);
                if(Mathf.Abs(localFeet.x)>trap.zoneSize.x*.5f+.5f||localFeet.z*localTarget.z>=0)continue;
                float halfDepth=trap.zoneSize.z*.5f;
                float approach=Mathf.Abs(localFeet.z)-halfDepth;
                if(approach<.65f||approach>3.25f)continue;
                float clearance=(Mathf.Abs(localFeet.z)+halfDepth+.5f)/walkSpeed+.3f;
                var sample=trap.Sample(now);
                if(sample.stage!=ClockworkTrapStage.Safe||sample.cycle+clearance>=trap.SafeSeconds)return true;
            }
            foreach(var press in presses){
                if(!press||!press.isActiveAndEnabled)continue;
                var rotation=Quaternion.Inverse(press.head.rotation);
                var localFeet=rotation*(feet-press.raised);
                var localTarget=rotation*(target-press.raised);
                var half=Vector3.Scale(press.headCollider.size,press.head.lossyScale)*.5f;
                if(Mathf.Abs(localFeet.x)>Mathf.Abs(half.x)+.5f||localFeet.z*localTarget.z>=0)continue;
                float approach=Mathf.Abs(localFeet.z)-Mathf.Abs(half.z);
                if(approach<.65f||approach>2.25f)continue;
                float clearance=(Mathf.Abs(localFeet.z)+Mathf.Abs(half.z)+.5f)/walkSpeed+.3f;
                if(press.Cycle(now)+clearance/press.period>=.43f)return true;
            }
            return false;
        }

        // Project the actual oriented dock/ship boxes onto the crossing direction, not world Z.
        static float Extent(BoxCollider box,Vector3 axis)
        {
            var half=box.size*.5f;var t=box.transform;
            return Mathf.Abs(Vector3.Dot(t.TransformVector(Vector3.right*half.x),axis))
                +Mathf.Abs(Vector3.Dot(t.TransformVector(Vector3.up*half.y),axis))
                +Mathf.Abs(Vector3.Dot(t.TransformVector(Vector3.forward*half.z),axis));
        }
        public bool AdjustBotTarget(CourseParticipant participant,int waypoint,ref Vector3 target)
        {
            if(!participant.Motor.Grounded)return false;
            foreach(var ferry in ferries){
                if(ferry==null||!ferry.carrier||!ferry.boardingDock||!ferry.arrivalDock||!ferry.waitingPoint
                    ||waypoint!=ferry.boardWaypoint&&waypoint!=ferry.exitWaypoint)continue;
                if(!ferry.surface)ferry.surface=ferry.carrier.GetComponent<BoxCollider>();
                if(!ferry.surface)continue;
                var near=ferry.boardingDock.transform.TransformPoint(ferry.boardingDock.center);
                var far=ferry.arrivalDock.transform.TransformPoint(ferry.arrivalDock.center);
                var forward=Vector3.ProjectOnPlane(far-near,Vector3.up).normalized;
                float nearEdge=Vector3.Dot(near,forward)+Extent(ferry.boardingDock,forward);
                float farEdge=Vector3.Dot(far,forward)-Extent(ferry.arrivalDock,forward);
                var centre=ferry.surface.transform.TransformPoint(ferry.surface.center);
                float half=Extent(ferry.surface,forward),position=Vector3.Dot(centre,forward);
                double now=FactorySurface.Clock;
                float future=position+Vector3.Dot(ferry.carrier.PositionAt(now+.9)-ferry.carrier.transform.position,forward);
                var feet=participant.transform.position;
                var local=ferry.surface.transform.InverseTransformPoint(feet)-ferry.surface.center;
                var size=ferry.surface.size*.5f;
                bool aboard=Mathf.Abs(local.x)<size.x&&Mathf.Abs(local.z)<size.z
                    &&Mathf.Abs(feet.y-ferry.surface.bounds.max.y)<.2f;
                if(waypoint==ferry.boardWaypoint&&!aboard&&Vector3.Dot(feet,forward)<nearEdge+.3f
                    &&(position-half-nearEdge>.65f||future-half-nearEdge>1.2f)){
                    target=ferry.waitingPoint.position;return true;
                }
                if(waypoint==ferry.exitWaypoint&&Vector3.Dot(feet,forward)<farEdge-.15f
                    &&(farEdge-position-half>.65f||farEdge-future-half>1.2f)){
                    target=ferry.carrier.transform.position+Vector3.up*ferry.carrier.standingOffset;return true;
                }
            }
            return false;
        }
    }
}
