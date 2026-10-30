using UnityEngine;

namespace HalliGalli.Movement
{
    public sealed class TutorialFerryCourse:MonoBehaviour
    {
        public CourseSession session;
        public FactorySurface carrier,secondCarrier;
        public BoxCollider boardingDock,arrivalDock;
        public Vector3 waitingPoint;
        public bool AdjustBotTarget(CourseParticipant p,int waypoint,ref Vector3 target)
        {
            if(!p.Motor.Grounded)return false;
            var ferry=carrier.GetComponent<BoxCollider>().bounds;
            double now=FactorySurface.Clock;
            if(waypoint==4&&p.transform.position.z<boardingDock.bounds.max.z+.35f&&(ferry.min.z-boardingDock.bounds.max.z>1.2f||carrier.PositionAt(now+.6).z-ferry.extents.z-boardingDock.bounds.max.z>1.6f)){
                target=waitingPoint;return true;
            }
            if(secondCarrier&&waypoint==5){
                var next=secondCarrier.GetComponent<BoxCollider>().bounds;
                float futureGap=secondCarrier.PositionAt(now+.65).z-carrier.PositionAt(now+.65).z-ferry.extents.z-next.extents.z;
                if(p.transform.position.z<next.min.z&&(next.min.z-ferry.max.z>1.3f||futureGap>1.6f)){
                    target=carrier.transform.position+Vector3.up*carrier.standingOffset;return true;
                }
            }
            var last=secondCarrier?secondCarrier:carrier;var lastBounds=last.GetComponent<BoxCollider>().bounds;
            if(waypoint==(secondCarrier?6:5)&&p.transform.position.z<arrivalDock.bounds.min.z&&(arrivalDock.bounds.min.z-lastBounds.max.z>1.2f||arrivalDock.bounds.min.z-last.PositionAt(now+.6).z-lastBounds.extents.z>1.6f)){
                target=last.transform.position+Vector3.up*last.standingOffset;return true;
            }
            return false;
        }
    }
}
