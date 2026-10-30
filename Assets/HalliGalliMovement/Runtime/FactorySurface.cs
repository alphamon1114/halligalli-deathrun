using UnityEngine;

namespace HalliGalli.Movement
{
    // Analytic motion uses the shared network clock, including on late-joining clients.
    [DefaultExecutionOrder(-100)]
    public sealed class FactorySurface : MonoBehaviour
    {
        public Vector3 origin, travel, beltVelocity;
        public float period=4, phase;
        public Transform[] beltMarkers;
        public float beltLength;
        public Transform[] routeMarkers;
        public float standingOffset=.225f;
        public static double Clock => CourseNetwork.WorldTime;
        public Vector3 PositionAt(double time) => origin+travel*Mathf.Sin((float)((time/period+phase)*System.Math.PI*2));
        public Vector3 Displacement(double time,float dt) => PositionAt(time)-PositionAt(time-dt)+beltVelocity*dt;
        void FixedUpdate()
        {
            transform.position=PositionAt(Clock);
            if(routeMarkers!=null)foreach(var node in routeMarkers)if(node)node.position=transform.position+Vector3.up*standingOffset;
            if(beltMarkers!=null&&beltLength>0)for(int i=0;i<beltMarkers.Length;i++)
                if(beltMarkers[i])beltMarkers[i].localPosition=Vector3.up*.25f+beltVelocity.normalized*(Mathf.Repeat(i*beltLength/beltMarkers.Length+(float)Clock*beltVelocity.magnitude,beltLength)-beltLength*.5f);
            Physics.SyncTransforms();
        }
    }
}
