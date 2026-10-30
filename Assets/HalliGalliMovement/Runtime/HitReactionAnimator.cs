using UnityEngine;

namespace HalliGalli.Movement
{
    // NPC presentation; the local player layers the same clips over locomotion.
    public sealed class HitReactionAnimator : MonoBehaviour
    {
        public Transform model;
        public MeleeReceiver receiver;
        public CharacterMotionSet motions;
        Animation animationPlayer;
        string state="";
        void Start()
        {
            animationPlayer=model.GetComponent<Animation>();if(!animationPlayer)animationPlayer=model.gameObject.AddComponent<Animation>();
            animationPlayer.playAutomatically=false;animationPlayer.cullingType=AnimationCullingType.AlwaysAnimate;
            foreach(var clip in motions.bodyClips)animationPlayer.AddClip(clip,clip.name);
        }
        void Update()
        {
            if(!animationPlayer)return;
            string next=receiver.IsReacting?receiver.ReactionName:"Idle";
            if(next!=state){animationPlayer.CrossFade(next,.035f);state=next;}
            var s=animationPlayer[next];if(s==null)return;
            s.speed=next=="Idle"?1:0;if(next!="Idle")s.time=receiver.ReactionElapsed;
        }
    }
}
