using UnityEngine;

namespace HalliGalli.Movement
{
    [DefaultExecutionOrder(-90)]
    public sealed class FactoryPress : MonoBehaviour
    {
        public Transform head;
        public BoxCollider headCollider;
        public Renderer warning;
        public Vector3 raised;
        public float stroke=3.25f,period=4.8f,phase;
        public bool orientedContact;
        public CourseSession contactSession;
        MaterialPropertyBlock tint;
        public float Cycle(double time)=>Mathf.Repeat((float)(time/period)+phase,1);
        // Long open interval, amber warning, fast downward stroke, then slow retraction.
        public float Extension(double time)
        {
            float t=Cycle(time);
            if(t<.58f)return 0;
            if(t<.68f)return Mathf.SmoothStep(0,1,(t-.58f)/.1f);
            if(t<.82f)return 1;
            return 1-Mathf.SmoothStep(0,1,(t-.82f)/.18f);
        }
        public bool Threatens(double time)=>Cycle(time)>.43f;
        public void Apply(double time)
        {
            head.position=raised-Vector3.up*stroke*Extension(time);
            if(warning){
                if(tint==null)tint=new MaterialPropertyBlock();
                float cycle=Cycle(time);
                Color color=cycle>=.58f?new Color(1,.13f,.04f):cycle>.43f?new Color(1,.55f,.04f):new Color(.25f,.75f,.65f);
                tint.SetColor("_Color",color);tint.SetColor("_EmissionColor",color*1.7f);warning.SetPropertyBlock(tint);
            }
        }
        void FixedUpdate()
        {
            var previous=headCollider.bounds;
            Apply(FactorySurface.Clock);Physics.SyncTransforms();
            // Resolve crushing before the character motor can push the capsule out of the head.
            // Include the downward sweep so a fast stroke cannot step over a crouched player.
            var area=headCollider.bounds;
            if(area.min.y<previous.min.y)area.Encapsulate(previous);
            if(!CourseNetwork.Authority)return;
            if(contactSession){
                foreach(var participant in contactSession.participants)
                    if(participant&&participant.isActiveAndEnabled&&!participant.IsSeated&&!participant.IsRespawning
                        &&(orientedContact?IntersectsRotated(participant,Mathf.Max(0,previous.max.y-headCollider.bounds.max.y)):Intersects(participant,area)))participant.BeginFall();
                return;
            }
            var course=GetComponentInParent<FactoryCourse>();
            if(!course||!course.session.RunActive)return;
            foreach(var p in course.session.participants)
                if(p.Racing&&(orientedContact?IntersectsRotated(p,Mathf.Max(0,previous.max.y-headCollider.bounds.max.y)):Intersects(p,area)))p.BeginFall();
        }
        public bool Intersects(CourseParticipant p)
            =>orientedContact?IntersectsRotated(p,0):Intersects(p,headCollider.bounds);
        bool IntersectsRotated(CourseParticipant participant,float upwardSweep)
        {
            // User presses rotate around Y. Test the upright capsule against that oriented box,
            // extending its vertical interval through the downward stroke without an AABB corner kill.
            Vector3 scale=head.lossyScale;
            Vector3 half=Vector3.Scale(headCollider.size,new Vector3(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z)))*.5f;
            Vector3 centre=head.TransformPoint(headCollider.center)+Vector3.up*(upwardSweep*.5f);
            half.y+=upwardSweep*.5f;
            Vector3 feet=Quaternion.Inverse(head.rotation)*(participant.transform.position-centre);
            if(feet.y>=half.y-.01f)return false;
            float radius=participant.Motor.profile.radius;
            float contactRadius=radius+participant.Motor.profile.skinWidth+.01f;
            float lower=feet.y+radius,upper=feet.y+participant.Motor.BodyHeight-radius;
            float dx=Mathf.Max(Mathf.Abs(feet.x)-half.x,0),dz=Mathf.Max(Mathf.Abs(feet.z)-half.z,0);
            float dy=Mathf.Max(Mathf.Max(-half.y-upper,lower-half.y),0);
            return dx*dx+dy*dy+dz*dz<=contactRadius*contactRadius;
        }
        static bool Intersects(CourseParticipant p,Bounds b)
        {
            var feet=p.transform.position;float r=p.Motor.profile.radius;
            float skin=p.Motor.profile.skinWidth+.01f;
            // Standing on top of the head is not being crushed beneath it.
            return feet.x+r>b.min.x&&feet.x-r<b.max.x&&feet.z+r>b.min.z&&feet.z-r<b.max.z
                &&feet.y<b.max.y-.01f&&feet.y+p.Motor.BodyHeight+skin>=b.min.y;
        }
    }
}
