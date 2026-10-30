using UnityEngine;

namespace HalliGalli.Movement
{
    [DefaultExecutionOrder(200)]
    public sealed class FactoryCourse : MonoBehaviour
    {
        public const string ScenePath="Assets/HalliGalliMovement/Demo/FactoryCourse.unity";
        public CourseSession session;
        public FactoryPress[] presses;
        public FactorySurface[] movingPlatforms;
        void FixedUpdate()
        {
            if(!CourseNetwork.Authority||!session.RunActive)return;
            foreach(var p in session.participants){
                if(!p.Racing)continue;
                foreach(var press in presses)if(press.Intersects(p)){p.BeginFall();break;}
            }
        }
        // Bots use the same walk/jump commands and wait outside a press's strike zone.
        public bool HoldForPress(Vector3 feet,Vector3 target)
        {
            foreach(var press in presses){
                if(session.userMap){
                    var localFeet=press.transform.InverseTransformPoint(feet);
                    var localTarget=press.transform.InverseTransformPoint(target);
                    var localCentre=press.transform.InverseTransformPoint(press.raised);
                    float halfWidth=press.headCollider.size.x*press.head.lossyScale.x*.5f;
                    float halfDepth=press.headCollider.size.z*press.head.lossyScale.z*.5f;
                    float forward=localCentre.z-localFeet.z;
                    if(Mathf.Abs(localFeet.x-localCentre.x)<halfWidth+.05f&&forward>halfDepth+.25f&&forward<halfDepth+1.95f&&localTarget.z>localCentre.z
                        &&(press.Threatens(FactorySurface.Clock)||press.Threatens(FactorySurface.Clock+.65)))return true;
                    continue;
                }
                var centre=press.raised;float distance=centre.z-feet.z;
                if(Mathf.Abs(feet.x-centre.x)<1.8f&&distance>.8f&&distance<2.5f&&target.z>centre.z
                    &&(press.Threatens(FactorySurface.Clock)||press.Threatens(FactorySurface.Clock+.65)))return true;
            }
            return false;
        }
    }
}
