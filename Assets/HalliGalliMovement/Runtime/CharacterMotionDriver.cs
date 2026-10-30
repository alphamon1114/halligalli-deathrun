using UnityEngine;
using System.Linq;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace HalliGalli.Movement
{
    [DefaultExecutionOrder(100)]
    public sealed class CharacterMotionDriver : MonoBehaviour
    {
        public SourceMotor motor;
        public Camera worldCamera, handCamera;
        public Transform bodyModel, handsModel;
        public Transform kickModel;
        public MeleeCombat combat;
        public MeleeReceiver receiver;
        public CharacterMotionSet motions;
        public bool thirdPerson;
        public AnimationClip seatedClip;
        [HideInInspector] public bool seated;
        SourcePlayerController viewController;
        LassoTool lasso;
        public const int BodyLayer=8, HandsLayer=9;
        public string CurrentBodyState {get; private set;} = "Idle";
        public string CurrentHandState {get; private set;} = "Idle";
        Animation bodyAnimation,handAnimation,kickAnimation;
        string locomotionBody="Idle";
        int lastAttackSequence=-1;
        bool wasGrounded;
        float landRemaining;
        readonly RaycastHit[] cameraHits=new RaycastHit[16];

        void Start()
        {
            bodyAnimation=Setup(bodyModel,motions.bodyClips);
            handAnimation=Setup(handsModel,motions.handClips);
            viewController=GetComponent<SourcePlayerController>();
            lasso=GetComponent<LassoTool>();
            if(seatedClip)bodyAnimation.AddClip(seatedClip,"Seated");
            wasGrounded=motor.Grounded;
            bodyAnimation.Play("Idle"); handAnimation.Play("Idle");
            foreach(string name in new[]{"PunchLeft","PunchRight","Kick","LassoHold","LassoThrow","LassoPull"}) {
                var s=bodyAnimation[name];if(s==null)continue;
                s.layer=1;s.speed=0;s.wrapMode=WrapMode.ClampForever;
                var bones=bodyModel.GetComponentsInChildren<Transform>();
                s.AddMixingTransform(bones.First(t=>t.name=="Spine"),true);
                if(name=="Kick")s.AddMixingTransform(bones.First(t=>t.name=="RightUpLeg"),true);
            }
            foreach(string name in new[]{"HitFront","HitBack","HitLeft","HitRight","Knockback"}) {
                var s=bodyAnimation[name];if(s==null)continue;
                s.layer=2;s.speed=0;s.wrapMode=WrapMode.ClampForever;
                s.AddMixingTransform(bodyModel.GetComponentsInChildren<Transform>().First(t=>t.name=="Spine"),true);
            }
            if(kickModel)kickAnimation=Setup(kickModel,motions.bodyClips);
        }
        static Animation Setup(Transform model,AnimationClip[] clips)
        {
            var a=model.GetComponent<Animation>();
            if(!a) a=model.gameObject.AddComponent<Animation>();
            a.playAutomatically=false; a.cullingType=AnimationCullingType.AlwaysAnimate;
            foreach(var clip in clips) if(clip) a.AddClip(clip,clip.name);
            return a;
        }
        void Update()
        {
            if(!bodyAnimation || !motor) return;
bool toggle=ControlBindings.Pressed(GameAction.BodyView);
            var localController=GetComponent<SourcePlayerController>();
            toggle=toggle&&localController&&localController.enabled&&localController.PointerActionsAllowed;
            if(toggle && Cursor.lockState==CursorLockMode.Locked) thirdPerson=!thirdPerson;
            if(!wasGrounded && motor.Grounded) landRemaining=.20f;
            wasGrounded=motor.Grounded;
            landRemaining=Mathf.Max(0,landRemaining-Time.deltaTime);
            Vector3 local=transform.InverseTransformDirection(motor.Velocity);
            float speed=motor.HorizontalSpeed;
            bool crouched=motor.BodyHeight < (motor.profile.standingHeight+motor.profile.crouchingHeight)*.5f;
            string body,hand;
            if(!motor.Grounded) {hand=motor.Velocity.y>.5f?"Jump":"Fall";body=crouched?"CrouchIdle":hand;}
            else if(landRemaining>0 && !crouched) body=hand="Land";
            else if(speed<.15f) body=hand=crouched?"CrouchIdle":"Idle";
            else {
                string gait=crouched?"CrouchWalk":motor.IsRunning?"Run":"Walk";
                string direction=Mathf.Abs(local.x)>Mathf.Abs(local.z)?(local.x>0?"Right":"Left"):(local.z>=0?"Forward":"Back");
                body=gait+direction; hand=gait;
            }
            if(seated){body="Seated";hand="Idle";}
            Change(bodyAnimation,body,locomotionBody);
            locomotionBody=body;
            bool attacking=combat && combat.IsAttacking;
            bool reacting=receiver && receiver.IsReacting && !attacking;
            string attack=attacking?combat.CurrentAttack.ToString():"";
            if(attacking) hand=attack;
            if(reacting)hand=receiver.ReactionName;
            bool roping=lasso&&!seated&&!motor.Stunned&&!attacking&&!reacting&&lasso.Motion!=""&&handAnimation[lasso.Motion]!=null;
            if(roping)hand=lasso.Motion;
            if(attacking && lastAttackSequence!=combat.AttackSequence) {
                handAnimation[hand].time=0;handAnimation.CrossFade(hand,.025f);
                lastAttackSequence=combat.AttackSequence;
            }
            Change(handAnimation,hand,CurrentHandState);
            CurrentBodyState=reacting?receiver.ReactionName:attacking?attack:roping?lasso.Motion:body;CurrentHandState=hand;
            float reference=crouched?motor.profile.crouchSpeed:motor.IsRunning?7.5f:motor.profile.moveSpeed;
            float rate=Mathf.Clamp(speed/Mathf.Max(reference,.1f),.45f,1.8f);
            foreach(AnimationState s in bodyAnimation) {
                if(s.layer==1) { s.enabled=attacking&&s.name==attack||roping&&s.name==lasso.Motion; s.weight=s.enabled?1:0;s.speed=0;if(s.enabled)s.time=roping?lasso.MotionTime:combat.Elapsed; }
                else if(s.layer==2) {s.enabled=reacting && s.name==receiver.ReactionName;s.weight=s.enabled?1:0;s.speed=0;if(s.enabled)s.time=receiver.ReactionElapsed;}
                else s.speed=s.name.Contains("Walk")||s.name.StartsWith("Run")?rate:1;
            }
            foreach(AnimationState s in handAnimation) s.speed=s.name.Contains("Walk")||s.name=="Run"?rate:1;
            if(attacking) {handAnimation[hand].speed=0;handAnimation[hand].time=combat.Elapsed;}
            if(reacting) {handAnimation[hand].speed=0;handAnimation[hand].time=receiver.ReactionElapsed;}
            if(roping){handAnimation.Play(hand);handAnimation[hand].speed=0;handAnimation[hand].time=lasso.MotionTime;handAnimation.Sample();}
            if(kickModel) {
                bool kicking=attacking && combat.CurrentAttack==MeleeAttack.Kick;
                kickModel.gameObject.SetActive(kicking && !thirdPerson);
                if(kicking && kickAnimation) {kickAnimation.Play("Kick");kickAnimation["Kick"].speed=0;kickAnimation["Kick"].time=combat.Elapsed;kickAnimation.Sample();}
            }
        }
        void Change(Animation animation,string next,string current)
        {
            if(next==current && animation.IsPlaying(next)) return;
            if(animation[next]==null) return;
            animation[next].time=0;
            animation.CrossFade(next,next=="Jump"?.055f:motions.blendTime);
        }
        void LateUpdate()
        {
            if(!worldCamera || !bodyModel || !handsModel) return;
            // Match the interpolated camera's translation; visual motion never moves the capsule.
            Vector3 eyeOffset=motor.CurrentEyePosition-transform.position;
            bodyModel.position=worldCamera.transform.position-eyeOffset-Vector3.up*(viewController?viewController.cameraHeightOffset:0);
            worldCamera.cullingMask &= ~(1<<HandsLayer);
            if(thirdPerson) worldCamera.cullingMask |= 1<<BodyLayer;
            else worldCamera.cullingMask &= ~(1<<BodyLayer);
            handCamera.enabled=!thirdPerson;
            if(!thirdPerson) return;
            Vector3 pivot=worldCamera.transform.position-Vector3.up*.20f;
            Vector3 direction=-worldCamera.transform.forward;
            float distance=2.8f;
            int count=Physics.SphereCastNonAlloc(pivot,.16f,direction,cameraHits,distance,~((1<<BodyLayer)|(1<<HandsLayer)),QueryTriggerInteraction.Ignore);
            for(int i=0;i<count;i++) if(!cameraHits[i].collider.transform.IsChildOf(transform)) distance=Mathf.Min(distance,Mathf.Max(.2f,cameraHits[i].distance-.06f));
            worldCamera.transform.position=pivot+direction*distance+worldCamera.transform.right*.35f;
        }
    }
}
