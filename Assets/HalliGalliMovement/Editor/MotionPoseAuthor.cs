using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using HalliGalli.Movement;

namespace HalliGalli.EditorTools
{
    // Original pose authoring: contact/passing foot arcs, two-bone limb solves,
    // opposing arm swing, and separate restrained first-person hand poses.
    // No imported animation data is read by this authoring tool.
    public sealed class MotionPoseAuthor
    {
        readonly Transform root;
        public readonly Transform[] bones;
        readonly Dictionary<string, Transform> named;
        readonly Dictionary<Transform, Vector3> positions = new();
        readonly Dictionary<Transform, Quaternion> rotations = new();
        readonly Dictionary<Transform, Quaternion> worldRotations = new();
        readonly Dictionary<Transform, Vector3> worldPositions = new();
        public MotionPoseAuthor(GameObject model)
        {
            root = model.transform;
            bones = model.GetComponentsInChildren<Transform>().Where(t => t != root && !t.GetComponent<Renderer>()).ToArray();
            named = bones.GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            foreach(var t in bones) {
                positions[t] = t.localPosition; rotations[t] = t.localRotation;
                worldRotations[t] = Quaternion.Inverse(root.rotation) * t.rotation;
                worldPositions[t] = root.InverseTransformPoint(t.position);
            }
        }
        public void Reset() { foreach(var t in bones) { t.localPosition=positions[t]; t.localRotation=rotations[t]; } }
        Transform B(string name) => named.TryGetValue(name,out var b) ? b : null;
        Vector3 P(Vector3 p) => root.TransformPoint(p);
        Vector3 V(Vector3 p) => root.TransformDirection(p);
        void Turn(string name, Vector3 euler) { var b=B(name); if(b) b.rotation=Quaternion.Euler(V(euler)) * b.rotation; }

        public void Body(string state, float phase, Vector2 direction)
        {
            if(state.StartsWith("Lasso")){LassoBody(state,phase);return;}
            if(state.StartsWith("Hit") || state=="Knockback"){ReactionBody(state,phase);return;}
            if(state.StartsWith("Push") || state=="Kick") { AttackBody(state,phase);return; }
            Reset();
            bool crouch=state.Contains("Crouch"), walk=state.Contains("Walk"), run=state.Contains("Run");
            bool moving=walk||run, jump=state=="Jump", fall=state=="Fall", land=state=="Land";
            float a=phase*Mathf.PI*2, stride=run?.60f:crouch?.22f:.34f;
            float compression=land ? Mathf.Sin(Mathf.PI*phase)*.17f : 0;
            float lift=moving ? (run?.04f:.015f)*(1-Mathf.Cos(a*2)) : .003f*Mathf.Sin(a);
            var hips=B("Hips");
            // A deep squat fits the actual 1.0 m crouched capsule.
            hips.position += V(new Vector3(0,-(crouch?.66f:0)-compression+lift,crouch?-.13f:0));
            Turn("Spine",new Vector3(crouch?17:run?8:2,0,moving?Mathf.Sin(a)*(run?2:1):0));
            Turn("Spine1",new Vector3(crouch?17:run?5:0,moving?Mathf.Sin(a)*(run?5:3):0,0));
            Turn("Neck",new Vector3(crouch?-20:run?-10:-2,0,0));
            for(int i=0;i<2;i++)
            {
                string side=i==0?"Left":"Right"; float sign=i==0?-1:1;
                float cycle=Mathf.Repeat(phase+i*.5f,1);
                float contact=run?.43f:.60f;
                float travel,raise;
                if(cycle<contact) { travel=Mathf.Lerp(stride,-stride,cycle/contact); raise=0; }
                else {float t=(cycle-contact)/(1-contact); travel=Mathf.Lerp(-stride,stride,t*t*(3-2*t)); raise=Mathf.Sin(t*Mathf.PI)*(run?.26f:crouch?.07f:.12f);}
                if(!moving){travel=0;raise=0;}
                Vector3 foot=worldPositions[B(side+"Foot")];
                foot += new Vector3(direction.x*travel,raise,direction.y*travel);
                if(crouch) foot.x=sign*.21f;
                if(jump) foot += new Vector3(0,i==0?.20f:.12f,i==0?.12f:-.16f);
                if(fall) foot += new Vector3(0,.08f,i==0?.05f:-.07f);
                Solve(B(side+"UpLeg"),B(side+"Leg"),B(side+"Foot"),P(foot),P(new Vector3(sign*.24f,.55f,1.1f)));
                B(side+"Foot").rotation=root.rotation*worldRotations[B(side+"Foot")];
                // Relaxed athletic arms: elbows bend forward, hands never hang in a T pose.
                float swing=moving?-Mathf.Cos(a+i*Mathf.PI)*(run?.22f:crouch?.07f:.14f):.004f*Mathf.Sin(a+i);
                Vector3 hand=new Vector3(sign*(run?.25f:.27f),run?1.12f:1.00f,run?.23f:.09f);
                hand.z+=swing;
                hand.y+=moving?Mathf.Max(0,swing)*.45f:0;
                if(crouch) hand=new Vector3(sign*.27f,.48f,.29f+swing);
                if(jump) hand=new Vector3(sign*.30f,1.16f,.24f);
                if(fall) hand=new Vector3(sign*.36f,1.09f,.22f);
                if(land) hand.y-=compression*.6f;
                Solve(B(side+"Arm"),B(side+"ForeArm"),B(side+"Hand"),P(hand),P(new Vector3(sign*.45f,crouch?.65f:1.15f,-.22f)));
                // Turn the palm inward while keeping a soft fist.
                var wrist=B(side+"Hand");
                Vector3 restFinger=V(new Vector3(sign,0,0));
                Vector3 targetFinger=V(new Vector3(0,run?-.3f:-.9f,run?.9f:.3f));
                Vector3 targetPalm=V(new Vector3(-sign,0,0));
                Quaternion aim=Quaternion.LookRotation(targetFinger,targetPalm)*Quaternion.Inverse(Quaternion.LookRotation(restFinger,V(Vector3.forward)));
                wrist.rotation=aim*root.rotation*worldRotations[wrist];
                CurlBody(side,run?60:jump?32:45);
            }
        }

        void CurlBody(string side,float amount,float opposition=0)
        {
            string prefix="HandLoRA_Bip01_"+(side=="Left"?"L":"R")+"_Finger";
            var wrist=B(side+"Hand");
            Quaternion delta=wrist.rotation*Quaternion.Inverse(root.rotation*worldRotations[wrist]);
            Vector3 palm=delta*V(Vector3.forward);
            if(opposition>0) {
                var thumb=B(prefix+"0");var next=B(prefix+"01");
                Vector3 along=Vector3.ProjectOnPlane(next.position-thumb.position,palm);
                Vector3 toward=Vector3.ProjectOnPlane(delta*V(new Vector3(side=="Left"?-1:1,0,0)),palm);
                thumb.rotation=Quaternion.Slerp(Quaternion.identity,Quaternion.FromToRotation(along,toward),opposition*.85f)*thumb.rotation;
            }
            for(int f=0;f<5;f++) for(int j=0;j<3;j++) {
                var t=B(prefix+f+(j==0?"":j.ToString())); if(!t) continue;
                Vector3 along=t.childCount>0?t.GetChild(0).position-t.position:delta*V(new Vector3(side=="Left"?-1:1,0,0));
                Vector3 axis=Vector3.Cross(along,palm).normalized;
                float factor=f==0?Mathf.Lerp(.8f,j==0?.35f:j==1?.45f:.25f,opposition):j==1?1:.65f;
                t.rotation=Quaternion.AngleAxis(amount*factor,axis)*t.rotation;
            }
            if(opposition>0) {
                var a=B(prefix+"0");var b=B(prefix+"01");var c=B(prefix+"02");
                Vector3 across=Vector3.Lerp(B(prefix+"12").position,B(prefix+"22").position,.55f)+palm*.005f;
                Solve(a,b,c,Vector3.Lerp(c.position,across,opposition),a.position+delta*V(Vector3.up)*.04f+palm*.05f);
            }
        }

        public void Hands(string state,float phase)
        {
            if(state.StartsWith("Lasso")){LassoHands(state,phase);return;}
            if(state.StartsWith("Hit") || state=="Knockback"){ReactionHands(state,phase);return;}
            if(state.StartsWith("Push") || state=="Kick") { AttackHands(state,phase);return; }
            Reset();
            bool run=state=="Run", walk=state=="Walk", crouch=state.Contains("Crouch");
            float a=phase*Mathf.PI*2;
            for(int i=0;i<2;i++) {
                string side=i==0?"L":"R"; float sign=i==0?-1:1;
                float swing=Mathf.Sin(a+i*Mathf.PI)*(run?.055f:walk?.016f:state=="CrouchWalk"?.009f:.0015f);
                float y=-.235f+(run?.025f:0)+(crouch?-.018f:0)+swing*.55f;
                float z=.43f+swing;
                float pitch=-10+(run?8:0)+swing*100;
                float grip=run?.72f:walk?.50f:.45f;
                if(state=="Jump") {y+=.045f;z+=.025f;pitch=-20;grip=.10f;}
                if(state=="Fall") {y+=.025f;pitch=-14;grip=.08f;}
                if(state=="Land") {float impact=Mathf.Sin(Mathf.PI*phase);y-=.045f*impact;pitch+=12*impact;}
                var wrist=B(side+"_Wrist");
                if(!wrist) throw new Exception("Missing hand rig: "+side);
                // Canonical hand mesh: fingers +Z, dorsal surface +Y.
                Quaternion q=Quaternion.Euler(pitch,sign*10,-sign*32);
                wrist.position=P(new Vector3(sign*.235f,y,z));
                wrist.rotation=root.rotation*q*worldRotations[wrist];
                CurlHand(side,grip);
            }
        }

        static float Ease(float a,float b,float t) => Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,t));
        public void LassoHands(string state,float phase)
        {
            Reset();
            float throwOut=Ease(.08f,.45f,phase)*(1-Ease(.5f,1,phase));
            float yank=Ease(0,.28f,phase)*(1-Ease(.45f,1,phase));
            for(int i=0;i<2;i++){
                string side=i==0?"L":"R";float sign=i==0?-1:1;
                Vector3 position=new Vector3(sign*.23f,-.21f,.43f);
                if(i==1&&state=="LassoThrow")position+=new Vector3(-.13f,.12f,.24f)*throwOut;
                if(state=="LassoPull")position+=new Vector3(i==0?.08f:-.13f,.04f,-.22f)*yank;
                var wrist=B(side+"_Wrist");wrist.position=P(position);
                wrist.rotation=root.rotation*Quaternion.Euler(-12,sign*8,-sign*(state=="LassoPull"?45:28))*worldRotations[wrist];
                CurlHand(side,i==0?.70f:state=="LassoThrow"?Mathf.Lerp(.85f,.15f,throwOut):.8f);
            }
        }
        public void LassoBody(string state,float phase)
        {
            Body("Idle",0,Vector2.up);
            float outwards=state=="LassoThrow"?Ease(.08f,.45f,phase)*(1-Ease(.5f,1,phase)):0;
            float yank=state=="LassoPull"?Ease(0,.28f,phase)*(1-Ease(.45f,1,phase)):0;
            Turn("Spine",new Vector3(-10*yank,-8*outwards,0));
            for(int i=0;i<2;i++){
                string side=i==0?"Left":"Right";float sign=i==0?-1:1;
                Vector3 position=new Vector3(sign*.26f,1.25f,.29f);
                if(i==1)position+=new Vector3(-.1f,.16f,.30f)*outwards;
                position+=new Vector3(-sign*.09f,.07f,-.20f)*yank;
                Solve(B(side+"Arm"),B(side+"ForeArm"),B(side+"Hand"),P(position),P(new Vector3(sign*.5f,1.1f,-.15f)));
                string prefix="HandLoRA_Bip01_"+(i==0?"L":"R")+"_Finger";
                foreach(var t in bones)if(t.name.StartsWith(prefix))t.localRotation=rotations[t];
                CurlBody(side,i==0?75:Mathf.Lerp(85,20,outwards),.7f);
            }
        }
        static MeleeAttack PushAttack(string state) => state=="PushLeft"?MeleeAttack.PushLeft:MeleeAttack.PushRight;
        static float PushExtension(MeleeAttack attack,float time)
        {
            float start=MeleeCombat.ContactStart(attack),end=MeleeCombat.ContactEnd(attack);
            return Ease(start*.5f,start,time)*(1-Ease(end,MeleeCombat.Duration(attack)*.72f,time));
        }
        static float PushEnvelope(MeleeAttack attack,float time) =>
            Ease(0,MeleeCombat.ContactStart(attack),time)*(1-Ease(MeleeCombat.Duration(attack)*.65f,MeleeCombat.Duration(attack),time));
        static Vector3 PushPath(MeleeAttack attack,float time,Vector3 idle,Vector3 chamber,Vector3 contact,Vector3 retract)
        {
            float start=MeleeCombat.ContactStart(attack),end=MeleeCombat.ContactEnd(attack),duration=MeleeCombat.Duration(attack);
            if(time<start*.5f)return Vector3.Lerp(idle,chamber,Ease(0,start*.5f,time));
            if(time<start)return Vector3.Lerp(chamber,contact,Ease(start*.5f,start,time));
            if(time<end)return contact;
            if(time<duration*.72f)return Vector3.Lerp(contact,retract,Ease(end,duration*.72f,time));
            return Vector3.Lerp(retract,idle,Ease(duration*.72f,duration,time));
        }
        public void AttackHands(string state,float phase)
        {
            if(state=="Kick"){KickHands(phase);return;}
            Reset();
            var attack=PushAttack(state);
            float time=phase*MeleeCombat.Duration(attack),envelope=PushEnvelope(attack,time);
            for(int i=0;i<2;i++) {
                string side=i==0?"L":"R";float sign=i==0?-1:1;
                bool pushing=state==(i==0?"PushLeft":"PushRight");
                var idle=new Vector3(sign*.235f,-.235f,.43f);
                Vector3 p=pushing?PushPath(attack,time,idle,new Vector3(sign*.25f,-.25f,.36f),
                    new Vector3(sign*.17f,-.24f,.71f),new Vector3(sign*.24f,-.25f,.40f)):
                    Vector3.Lerp(idle,new Vector3(sign*.27f,-.29f,.38f),envelope);
                // Extend the palm below the crosshair; the other hand stays at the hip.
                var idleRotation=Quaternion.Euler(-10,sign*10,-sign*32);
                var palmRotation=Quaternion.Euler(-88,sign*5,-sign*4);
                var wrist=B(side+"_Wrist");wrist.position=P(p);
                wrist.rotation=root.rotation*Quaternion.Slerp(idleRotation,palmRotation,pushing?envelope:0)*worldRotations[wrist];
                CurlHand(side,Mathf.Lerp(.45f,pushing?.02f:.45f,envelope));
                if(pushing) {
                    // The viewmodel forearm is a child pivoted AT the wrist, not an elbow.
                    // Counter-rotate it independently: raising an open hand must not raise
                    // the entire rigid forearm into a vertical/lateral striking surface.
                    var forearm=B(side+"_Forearm");
                    Vector3 elbow=P(new Vector3(sign*.29f,-.40f,.20f));
                    Quaternion extended=Quaternion.FromToRotation(V(Vector3.back),elbow-wrist.position)
                        *root.rotation*worldRotations[forearm];
                    forearm.rotation=Quaternion.Slerp(forearm.rotation,extended,envelope);
                }
            }
        }
        public void AttackBody(string state,float phase)
        {
            if(state=="Kick"){KickBody(phase);return;}
            Body("Idle",0,Vector2.up);
            var attack=PushAttack(state);
            float time=phase*MeleeCombat.Duration(attack),envelope=PushEnvelope(attack,time),extension=PushExtension(attack,time);
            float sideSign=state=="PushLeft"?-1:1;
            // Weight comes from the torso. Feet remain planted and runtime leg locomotion is untouched.
            Turn("Spine",new Vector3(4*extension,sideSign*8*extension,0));
            Turn("Spine1",new Vector3(2*extension,0,0));
            Turn("Neck",new Vector3(-3*extension,0,0));
            for(int i=0;i<2;i++) {
                string side=i==0?"Left":"Right";float sign=i==0?-1:1;
                bool pushing=state=="Push"+side;
                Vector3 idle=new Vector3(sign*.27f,1,.09f);
                var upper=B(side+"Arm");var lower=B(side+"ForeArm");var wrist=B(side+"Hand");
                float reach=Vector3.Distance(upper.position,lower.position)+Vector3.Distance(lower.position,wrist.position);
                // Keep the goal reachable, nearly straight ahead of the shoulder. A low
                // elbow pole makes the elbow unfold forward instead of sweeping sideways.
                Vector3 contact=root.InverseTransformPoint(upper.position)
                    +new Vector3(-sign*.025f,-.09f,.48f).normalized*(reach*.96f);
                Vector3 p=pushing?PushPath(attack,time,idle,new Vector3(sign*.24f,1.22f,.20f),
                    contact,new Vector3(sign*.27f,1.12f,.20f)):
                    Vector3.Lerp(idle,new Vector3(sign*.29f,1.02f,.12f),envelope);
                Vector3 pole=Vector3.Lerp(new Vector3(sign*.45f,1.15f,-.22f),
                    new Vector3(sign*.25f,.98f,.12f),pushing?envelope:0);
                Solve(upper,lower,wrist,P(p),P(pole));
                Vector3 finger=Vector3.Slerp(new Vector3(0,-.9f,.3f),Vector3.up,pushing?envelope:0);
                Vector3 palm=Vector3.Slerp(new Vector3(-sign,0,0),Vector3.forward,pushing?envelope:0);
                Quaternion aim=Quaternion.LookRotation(V(finger),V(palm))*Quaternion.Inverse(Quaternion.LookRotation(V(new Vector3(sign,0,0)),V(Vector3.forward)));
                wrist.rotation=aim*root.rotation*worldRotations[wrist];
                string prefix="HandLoRA_Bip01_"+(i==0?"L":"R")+"_Finger";
                foreach(var t in bones)if(t.name.StartsWith(prefix))t.localRotation=rotations[t];
                CurlBody(side,Mathf.Lerp(45,pushing?3:45,envelope));
                if(pushing)for(int digit=1;digit<=4;digit++) {
                    // The imported fingers have asymmetric rest bends. Open their
                    // joints along each proximal phalanx rather than treating rest as straight.
                    var knuckle=B(prefix+digit);
                    Vector3 along=knuckle.GetChild(0).position-knuckle.position;
                    for(int joint=1;joint<=2;joint++) {
                        var phalanx=B(prefix+digit+joint);
                        if(phalanx.childCount==0)continue;
                        var open=Quaternion.FromToRotation(phalanx.GetChild(0).position-phalanx.position,along);
                        phalanx.rotation=Quaternion.Slerp(Quaternion.identity,open,envelope)*phalanx.rotation;
                    }
                }
            }
        }
        void KickHands(float phase)
        {
            Reset();
            float time=phase*MeleeCombat.Duration(MeleeAttack.Kick);
            float envelope=PushEnvelope(MeleeAttack.Kick,time),extension=PushExtension(MeleeAttack.Kick,time);
            for(int i=0;i<2;i++) {
                string side=i==0?"L":"R";float sign=i==0?-1:1;
                var wrist=B(side+"_Wrist");
                wrist.position=P(Vector3.Lerp(new Vector3(sign*.235f,-.235f,.43f),new Vector3(sign*.29f,-.16f,.37f),envelope));
                var rotation=Quaternion.Euler(Mathf.Lerp(-10,-28,extension),sign*Mathf.Lerp(10,-6,extension),-sign*Mathf.Lerp(32,3,extension));
                wrist.rotation=root.rotation*rotation*worldRotations[wrist];
                CurlHand(side,Mathf.Lerp(.45f,.85f,envelope));
            }
        }
        void KickBody(float phase)
        {
            Body("Idle",0,Vector2.up);
            float time=phase*MeleeCombat.Duration(MeleeAttack.Kick);
            float envelope=PushEnvelope(MeleeAttack.Kick,time),extension=PushExtension(MeleeAttack.Kick,time);
            Turn("Spine",new Vector3(-9*extension,0,0));
            for(int i=0;i<2;i++) {
                string side=i==0?"Left":"Right";float sign=i==0?-1:1;
                Vector3 hand=Vector3.Lerp(new Vector3(sign*.27f,1,.09f),new Vector3(sign*.28f,1.25f,.25f),envelope);
                Solve(B(side+"Arm"),B(side+"ForeArm"),B(side+"Hand"),P(hand),P(new Vector3(sign*.5f,1.1f,-.10f)));
                var wrist=B(side+"Hand");
                Vector3 finger=Vector3.Slerp(new Vector3(0,-.9f,.3f),Vector3.forward,envelope);
                Vector3 palm=Vector3.Slerp(new Vector3(-sign,0,0),Vector3.down,extension);
                Quaternion aim=Quaternion.LookRotation(V(finger),V(palm))*Quaternion.Inverse(Quaternion.LookRotation(V(new Vector3(sign,0,0)),V(Vector3.forward)));
                wrist.rotation=aim*root.rotation*worldRotations[wrist];
                string prefix="HandLoRA_Bip01_"+(i==0?"L":"R")+"_Finger";
                foreach(var bone in bones)if(bone.name.StartsWith(prefix))bone.localRotation=rotations[bone];
                CurlBody(side,Mathf.Lerp(45,100,envelope),envelope);
            }
            // Original right-foot chamber, comic forward extension and retract, retimed to contact.
            var foot=B("RightFoot");
            Vector3 target=PushPath(MeleeAttack.Kick,time,worldPositions[foot],new Vector3(.17f,.55f,.15f),
                new Vector3(.16f,.94f,.86f),new Vector3(.18f,.55f,.19f));
            Solve(B("RightUpLeg"),B("RightLeg"),foot,P(target),P(new Vector3(.25f,1.2f,.9f)));
            foot.rotation=root.rotation*Quaternion.Euler(-24*extension,0,0)*worldRotations[foot];
        }

        public static float Recoil(float phase)
        {
            if(phase<.16f)return Mathf.SmoothStep(0,1,phase/.16f);
            if(phase<.48f)return Mathf.Lerp(1,-.18f,Mathf.SmoothStep(0,1,(phase-.16f)/.32f));
            return Mathf.Lerp(-.18f,0,Mathf.SmoothStep(0,1,(phase-.48f)/.52f));
        }
        public void ReactionBody(string state,float phase)
        {
            Body("Idle",0,Vector2.up);
            float r=Recoil(phase),guard=Ease(0,.18f,phase)*(1-Ease(.45f,1,phase));
            bool knockback=state=="Knockback";
            float pitch=state=="HitBack"?17:state=="HitFront"||knockback?-18:-5;
            float roll=state=="HitLeft"?-16:state=="HitRight"?16:4;
            B("Hips").position+=V(new Vector3(0,-.035f*Mathf.Max(0,r),0));
            Turn("Spine",new Vector3(pitch*r,roll*.35f*r,roll*r));
            Turn("Spine1",new Vector3(pitch*.35f*r,-roll*.2f*r,roll*.25f*r));
            Turn("Neck",new Vector3(-pitch*.3f*r,roll*.3f*r,-roll*.3f*r));
            for(int i=0;i<2;i++) {
                string side=i==0?"Left":"Right";float sign=i==0?-1:1;
                Vector3 hand=Vector3.Lerp(new Vector3(sign*.27f,1,.09f),new Vector3(sign*(knockback?.39f:.29f),knockback?1.16f:1.29f,.23f),guard);
                hand.x+=-roll*.003f*r;
                Solve(B(side+"Arm"),B(side+"ForeArm"),B(side+"Hand"),P(hand),P(new Vector3(sign*.52f,1.12f,-.15f)));
                var foot=B(side+"Foot");Solve(B(side+"UpLeg"),B(side+"Leg"),foot,P(worldPositions[foot]),P(new Vector3(sign*.24f,.5f,.9f)));
                foot.rotation=root.rotation*worldRotations[foot];
            }
        }
        public void Seated(float phase)
        {
            Body("Idle",0,Vector2.up);
            B("Hips").position+=V(new Vector3(0,-.42f+.002f*Mathf.Sin(phase*Mathf.PI*2),0));
            Turn("Spine",new Vector3(5,0,0));
            for(int i=0;i<2;i++){
                string side=i==0?"Left":"Right";float sign=i==0?-1:1;
                var foot=B(side+"Foot");
                Solve(B(side+"UpLeg"),B(side+"Leg"),foot,P(new Vector3(sign*.17f,.08f,.40f)),P(new Vector3(sign*.2f,.5f,1)));
                foot.rotation=root.rotation*worldRotations[foot];
                Solve(B(side+"Arm"),B(side+"ForeArm"),B(side+"Hand"),P(new Vector3(sign*.24f,.86f,.43f)),P(new Vector3(sign*.46f,.75f,.05f)));
            }
        }
        public void ReactionHands(string state,float phase)
        {
            Hands("Idle",0);
            float r=Recoil(phase),guard=Ease(0,.18f,phase)*(1-Ease(.45f,1,phase));
            float lateral=state=="HitLeft"?.065f:state=="HitRight"?-.065f:0;
            for(int i=0;i<2;i++) {
                string side=i==0?"L":"R";float sign=i==0?-1:1;var wrist=B(side+"_Wrist");
                wrist.position+=V(new Vector3(lateral*r,guard*.06f,-r*.08f));
                wrist.rotation=root.rotation*Quaternion.Euler(-10-guard*12,sign*10,-sign*32+r*(state=="HitLeft"?-10:state=="HitRight"?10:sign*8))*worldRotations[wrist];
            }
        }

        // Review the same deformations used by the playable model in its neutral
        // frame, so the palm, dorsal surface and closed grip can all be inspected.
        public void ReviewHands(float grip)
        {
            Reset();CurlHand("L",grip);CurlHand("R",grip);
        }

        void CurlHand(string side,float grip)
        {
            var wrist=B(side+"_Wrist");
            Quaternion delta=wrist.rotation*Quaternion.Inverse(root.rotation*worldRotations[wrist]);
            Vector3 palm=delta*V(Vector3.down),forward=delta*V(Vector3.forward);
            float[] bend={80,100,48};
            foreach(string digit in new[]{"Index","Middle","Ring","Little"}) {
                var first=B(side+"_"+digit+"1");var second=B(side+"_"+digit+"2");
                Vector3 along=second.position-first.position;
                var adduct=Quaternion.FromToRotation(Vector3.ProjectOnPlane(along,palm),Vector3.ProjectOnPlane(forward,palm));
                first.rotation=Quaternion.Slerp(Quaternion.identity,adduct,grip*.9f)*first.rotation;
                Vector3 axis=Vector3.Cross(second.position-first.position,palm).normalized;
                for(int j=1;j<=3;j++){var joint=B(side+"_"+digit+j);joint.rotation=Quaternion.AngleAxis(bend[j-1]*grip,axis)*joint.rotation;}
            }
            var thumb=B(side+"_Thumb1");
            thumb.rotation=Quaternion.AngleAxis((side=="R"?1:-1)*48*grip,-palm)*thumb.rotation;
            Vector3 thumbAxis=Vector3.Cross(B(side+"_Thumb2").position-thumb.position,palm).normalized;
            float[] thumbBend={30,30,12};
            for(int j=1;j<=3;j++){var joint=B(side+"_Thumb"+j);joint.rotation=Quaternion.AngleAxis(thumbBend[j-1]*grip,thumbAxis)*joint.rotation;}
        }

        static void Solve(Transform upper,Transform lower,Transform end,Vector3 target,Vector3 pole)
        {
            float l1=Vector3.Distance(upper.position,lower.position),l2=Vector3.Distance(lower.position,end.position);
            Vector3 delta=target-upper.position; float d=Mathf.Clamp(delta.magnitude,Mathf.Abs(l1-l2)+.001f,l1+l2-.001f);
            Vector3 forward=delta.normalized;
            Vector3 bend=Vector3.ProjectOnPlane(pole-upper.position,forward).normalized;
            float x=(l1*l1-l2*l2+d*d)/(2*d), y=Mathf.Sqrt(Mathf.Max(0,l1*l1-x*x));
            Vector3 elbow=upper.position+forward*x+bend*y;
            upper.rotation=Quaternion.FromToRotation(lower.position-upper.position,elbow-upper.position)*upper.rotation;
            lower.rotation=Quaternion.FromToRotation(end.position-lower.position,target-lower.position)*lower.rotation;
        }
    }
}
