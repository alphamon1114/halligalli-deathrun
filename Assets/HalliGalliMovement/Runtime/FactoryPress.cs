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
            var course=GetComponentInParent<FactoryCourse>();
            if(!CourseNetwork.Authority||!course||!course.session.RunActive)return;
            foreach(var p in course.session.participants)if(p.Racing&&Intersects(p,area))p.BeginFall();
        }
        public bool Intersects(CourseParticipant p)
            =>Intersects(p,headCollider.bounds);
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
