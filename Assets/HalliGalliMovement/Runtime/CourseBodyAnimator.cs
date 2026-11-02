using System.Linq;
using UnityEngine;

namespace HalliGalli.Movement
{
    public sealed class CourseBodyAnimator:MonoBehaviour
    {
        public Transform model;public CharacterMotionSet motions;public AnimationClip seatedClip;
        public bool seated;Animation animationPlayer;SourceMotor motor;MeleeCombat combat;MeleeReceiver receiver;string locomotion="";
        void Start(){motor=GetComponent<SourceMotor>();combat=GetComponent<MeleeCombat>();receiver=GetComponent<MeleeReceiver>();animationPlayer=model.gameObject.AddComponent<Animation>();animationPlayer.playAutomatically=false;animationPlayer.cullingType=AnimationCullingType.AlwaysAnimate;
            foreach(var c in motions.bodyClips)animationPlayer.AddClip(c,c.name);animationPlayer.AddClip(seatedClip,"Seated");
            var bones=model.GetComponentsInChildren<Transform>();
            foreach(string name in new[]{"PushLeft","PushRight","Kick","HitFront","HitBack","HitLeft","HitRight","Knockback","LassoHold","LassoThrow","LassoPull"}){
                var s=animationPlayer[name];if(s==null)continue;s.layer=name.StartsWith("Hit")||name=="Knockback"?2:1;s.speed=0;s.wrapMode=WrapMode.ClampForever;
                s.AddMixingTransform(bones.First(t=>t.name=="Spine"),true);
                if(name=="Kick")s.AddMixingTransform(bones.First(t=>t.name=="RightUpLeg"),true);
            }
        }
        void Update(){if(!animationPlayer)return;string next=seated?"Seated":!motor.Grounded?(motor.Velocity.y>.5f?"Jump":"Fall"):motor.HorizontalSpeed<.15f?"Idle":motor.IsRunning?"RunForward":"WalkForward";
            if(next!=locomotion){animationPlayer.CrossFade(next,.07f);locomotion=next;}
            bool attacking=!seated&&combat.IsAttacking,reacting=!seated&&receiver.IsReacting&&!attacking;
            var lasso=GetComponent<LassoTool>();bool roping=lasso&&!seated&&!motor.Stunned&&!attacking&&!reacting&&lasso.Motion!="";
            foreach(AnimationState s in animationPlayer){if(s.layer>0){s.enabled=s.layer==1?(attacking&&s.name==combat.CurrentAttack.ToString()||roping&&s.name==lasso.Motion):reacting&&s.name==receiver.ReactionName;s.weight=s.enabled?1:0;if(s.enabled)s.time=s.layer==1?(roping?lasso.MotionTime:combat.Elapsed):receiver.ReactionElapsed;}
                else s.speed=next=="RunForward"?Mathf.Clamp(motor.HorizontalSpeed/7.5f,.45f,1.5f):next=="WalkForward"?Mathf.Clamp(motor.HorizontalSpeed/4.5f,.45f,1.5f):1;}
        }
    }
}
