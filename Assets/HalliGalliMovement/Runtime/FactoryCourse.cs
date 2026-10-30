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
                var centre=press.raised;float distance=centre.z-feet.z;
                if(Mathf.Abs(feet.x-centre.x)<1.8f&&distance>.8f&&distance<2.5f&&target.z>centre.z
                    &&(press.Threatens(FactorySurface.Clock)||press.Threatens(FactorySurface.Clock+.65)))return true;
            }
            return false;
        }
    }
}
