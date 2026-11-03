using System;
using UnityEngine;

namespace HalliGalli.Movement
{
    public sealed class ClockworkCourse : MonoBehaviour
    {
        public const string ScenePath="Assets/HalliGalliMovement/Demo/ClockworkCourse.unity";
        public CourseSession session;
        public ClockworkTrap[] traps=Array.Empty<ClockworkTrap>();
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
            return false;
        }
    }
}
