#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using HalliGalli.Movement;

public static class CombatValidation
{
    static void Need(bool value,string message) {if(!value)throw new Exception(message);}
    sealed class Arena : IDisposable
    {
        public GameObject root;public SourceMotor actor,target;public MeleeCombat combat;public MeleeReceiver receiver;
        public Arena(float targetZ=1)
        {
            root=new GameObject("Combat test arena");root.transform.position=new Vector3(150,0,0);
            Solid(new Vector3(0,-.5f,0),new Vector3(20,1,20));
            actor=Actor("Attacker",Vector3.zero);target=Actor("Target",new Vector3(0,0,targetZ));
            receiver=target.gameObject.AddComponent<MeleeReceiver>();
            combat=actor.gameObject.AddComponent<MeleeCombat>();combat.readInput=false;combat.simulateAutomatically=false;
            var camera=new GameObject("Aim").AddComponent<Camera>();camera.enabled=false;camera.transform.SetParent(actor.transform,false);combat.aimCamera=camera;
            Physics.SyncTransforms();
            // Register newly spawned CharacterControllers with the query scene before
            // running a complete synthetic attack in this same render frame.
            var simulation=Physics.simulationMode;
            try {Physics.simulationMode=SimulationMode.Script;Physics.Simulate(.015f);}
            finally {Physics.simulationMode=simulation;}
        }
        SourceMotor Actor(string name,Vector3 position)
        {
            var go=new GameObject(name,typeof(CharacterController));go.transform.SetParent(root.transform,false);go.transform.localPosition=position+Vector3.up*.03f;
            var m=go.AddComponent<SourceMotor>();m.Initialize();Physics.SyncTransforms();for(int i=0;i<10;i++)m.Simulate(default,.015f);return m;
        }
        public GameObject Solid(Vector3 position,Vector3 size)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.transform.SetParent(root.transform,false);go.transform.localPosition=position;go.transform.localScale=size;Physics.SyncTransforms();return go;
        }
        public void Advance(float seconds) {int count=Mathf.CeilToInt(seconds/.01f);for(int i=0;i<count;i++)combat.SimulateAttack(.01f);}
        public void Dispose(){root.SetActive(false);UnityEngine.Object.Destroy(root);}
    }
    public static IEnumerator Run(Action<string,Action> check,CharacterMotionDriver character)
    {
        check("Quick push alternates and rejected spam cannot skip a hand",()=>{
            using var a=new Arena();Need(a.combat.TryPush() && a.combat.CurrentAttack==MeleeAttack.PushLeft,"first hand");
            Need(!a.combat.TryPush(),"spam accepted during windup");a.Advance(.6f);
            Need(a.combat.TryPush() && a.combat.CurrentAttack==MeleeAttack.PushRight,"second hand");a.Advance(.6f);
            Need(a.combat.TryKick(),"kick rejected");a.Advance(1.15f);
            Need(a.combat.TryPush() && a.combat.CurrentAttack==MeleeAttack.PushLeft,"kick changed hand order");
        });
        check("A recovery click buffers exactly one attack without automatic repeat",()=>{
            using var a=new Arena();a.combat.TryPush();a.Advance(.42f);
            Need(a.combat.TryPush() && !a.combat.TryPush(),"buffer capacity");a.Advance(.15f);
            Need(a.combat.CurrentAttack==MeleeAttack.PushRight,"buffer did not start");a.Advance(1.2f);
            Need(!a.combat.IsAttacking && a.combat.AttackSequence==2,"unexpected repeat");
        });
        check("Kick cooldown rejects repeated and buffered F without blocking quick pushes",()=>{
            using var a=new Arena();var c=a.combat;
            Need(c.TryKick()&&c.KickCooldownRemaining==MeleeCombat.KickCooldown,"kick start did not spend two seconds");
            c.SimulateAttack(.96f);float remaining=c.KickCooldownRemaining;
            for(int i=0;i<12;i++)Need(!c.TryKick()&&!c.HasBufferedAttack,"F entered the recovery buffer during cooldown");
            Need(c.KickCooldownRemaining==remaining,"rejected F extended the cooldown");
            Need(c.TryPush(),"cooldown blocked a buffered quick push");c.SimulateAttack(.15f);
            Need(c.CurrentAttack==MeleeAttack.PushLeft&&c.AttackSequence==2&&c.KickCooldownRemaining>0,"quick push did not start after recovery");
            c.SimulateAttack(.42f);
            Need(!c.TryKick()&&!c.HasBufferedAttack,"quick recovery buffered F before expiry");c.SimulateAttack(.14f);
            c.SimulateAttack(c.KickCooldownRemaining-.01f);
            Need(!c.IsAttacking&&c.KickCooldownRemaining>0&&!c.TryKick()&&c.AttackSequence==2,"F started before two seconds");
            c.SimulateAttack(.02f);
            Need(c.TryKick()&&c.AttackSequence==3&&c.KickCooldownRemaining==MeleeCombat.KickCooldown,"original cooldown deadline was extended");
        });
        check("A ready buffered kick spends cooldown only when it actually starts",()=>{
            using var a=new Arena();var c=a.combat;
            Need(c.TryPush()&&!c.TryKick()&&c.KickCooldownRemaining==0,"rejected windup input spent cooldown");
            c.SimulateAttack(.42f);
            Need(c.TryKick()&&c.HasBufferedAttack&&c.AttackSequence==1&&c.KickCooldownRemaining==0,"buffer spent cooldown before starting");
            Need(!c.TryKick()&&c.KickCooldownRemaining==0,"duplicate buffer spent cooldown");
            c.SimulateAttack(.14f);
            Need(c.CurrentAttack==MeleeAttack.Kick&&c.AttackSequence==2&&!c.HasBufferedAttack&&c.KickCooldownRemaining==MeleeCombat.KickCooldown,"buffer did not start a fresh cooldown");
        });
        check("Cancelling a queued kick is free but cancelling a started kick preserves cooldown",()=>{
            using var a=new Arena();var c=a.combat;
            c.TryPush();c.SimulateAttack(.42f);Need(c.TryKick(),"buffer setup");c.InterruptAttack();
            Need(!c.HasBufferedAttack&&c.KickCooldownRemaining==0&&c.TryKick(),"cancelled buffer spent cooldown");
            c.SimulateAttack(.2f);float remaining=c.KickCooldownRemaining;
            c.InterruptAttack();c.ResetCombat();c.enabled=false;c.enabled=true;
            Need(!c.IsAttacking&&c.KickCooldownRemaining==remaining&&!c.TryKick(),"cancellation refunded a started cooldown");
            c.SimulateAttack(remaining+.01f);Need(c.TryKick(),"cancelled attack cooldown never expired");
        });
        check("Respawn and individual seating preserve kick cooldown while round and match resets clear it",()=>{
            using var a=new Arena();var session=a.root.AddComponent<CourseSession>();
            var player=a.actor.gameObject.AddComponent<CourseParticipant>();var other=a.target.gameObject.AddComponent<CourseParticipant>();
            session.participants=new[]{player,other};
            foreach(var p in session.participants){p.session=session;p.seatAnchor=p.standAnchor=p.retryAnchor=a.root.transform;p.Combat.simulateAutomatically=false;p.Combat.readInput=false;}
            session.NotifyStand(player);var c=player.Combat;
            Need(c.TryKick(),"initial kick");c.SimulateAttack(.2f);float remaining=c.KickCooldownRemaining;
            Need(player.RequestRespawn()&&!c.IsAttacking&&!c.TryKick()&&c.KickCooldownRemaining==remaining,"fall refunded cooldown");
            c.SimulateAttack(.7f);player.Respawn();
            Need(!player.IsRespawning&&c.KickCooldownRemaining>1&&!c.TryKick()&&c.TryPush(),"respawn bypassed cooldown or blocked quick push");
            remaining=c.KickCooldownRemaining;player.Sit();Need(player.Stand(),"individual stand");
            Need(c.KickCooldownRemaining==remaining&&!c.TryKick(),"individual seating refunded cooldown");
            other.Sit();player.Sit();
            Need(session.EveryoneSeated&&c.KickCooldownRemaining==0,"round reset retained cooldown");
            Need(player.Stand()&&c.TryKick(),"new round was not ready");session.ResetPractice();
            Need(c.KickCooldownRemaining==0&&!c.IsAttacking,"new match retained cooldown");
        });
        check("Authoritative attack snapshots restore cooldown without restarting attacks or impulses",()=>{
            using var a=new Arena();var c=a.combat;Vector3 velocity=a.actor.Velocity;
            c.ApplyNetworkAttack(MeleeAttack.Kick,.4f,7,0,1.6f);
            c.ApplyNetworkAttack(MeleeAttack.Kick,.4f,7,0,1.6f);
            Need(c.AttackSequence==7&&c.Elapsed==.4f&&c.KickCooldownRemaining==1.6f&&a.actor.Velocity==velocity&&a.receiver.ReceivedHits==0,"snapshot restarted attack, cooldown or physics");
            c.ApplyNetworkAttack(MeleeAttack.None,0,7,0,.6f);c.SimulateAttack(.2f);
            Need(Mathf.Abs(c.KickCooldownRemaining-.4f)<.001f&&!c.TryKick(),"idle snapshot cooldown did not count down");
            c.ApplyNetworkAttack(MeleeAttack.None,0,7,0,.25f);
            Need(c.KickCooldownRemaining==.25f&&c.AttackSequence==7,"snapshot did not replace remaining time");
            c.SimulateAttack(.26f);Need(c.TryKick()&&c.AttackSequence==8,"restored cooldown did not expire");
        });
        check("Quick push contacts only in its contact window and only once",()=>{
            using var a=new Arena();a.combat.TryPush();a.Advance(.10f);Need(a.receiver.ReceivedHits==0,"hit during windup");
            a.Advance(.07f);Need(a.receiver.ReceivedHits==1,"contact missing");a.Advance(.5f);
            Need(a.receiver.ReceivedHits==1 && a.combat.HitCount==1 && a.receiver.LastImpulse.z==4.5f && a.receiver.LastImpulse.y==0,"duplicate hit or wrong push");
            Need(!a.target.Staggered&&!a.target.Stunned&&a.target.RaceLaunchRemaining==0,"push locked control");
        });
        check("Kick has longer windup and recovery",()=>{
            using var a=new Arena();a.combat.TryKick();a.Advance(.30f);
            Need(a.receiver.ReceivedHits==0,"kick hit before its windup");
            a.Advance(.06f);Need(a.receiver.ReceivedHits==1,"kick contact missing");a.Advance(.25f);
            Need(a.combat.IsAttacking&&!a.combat.TryPush(),"kick recovery ended too early");
            a.Advance(.55f);Need(!a.combat.IsAttacking,"kick recovery never ended");
        });
        check("Kick reaches farther than quick push",()=>{
            using var a=new Arena(1.95f);a.combat.TryPush();a.Advance(.6f);Need(a.receiver.ReceivedHits==0,"quick push too long");
            a.combat.TryKick();a.Advance(.4f);Need(a.receiver.ReceivedHits==1 && a.receiver.LastImpulse.z==22.5f,"kick range/strength");
        });
        check("Protected contact does not add impulse, hit credit or a hit marker",()=>{
            using var a=new Arena();a.combat.TryPush();a.Advance(.6f);
            int received=a.receiver.ReceivedHits,hits=a.combat.HitCount;Vector3 velocity=a.target.Velocity;float hitAt=a.receiver.LastHitTime;
            a.combat.TryKick();a.Advance(.4f);
            Need(a.receiver.ReceivedHits==received&&a.combat.HitCount==hits&&a.combat.HitMarkerRemaining==0,"protected contact counted");
            Need(a.target.Velocity==velocity&&a.receiver.LastHitTime==hitAt,"protected contact changed physics/reaction");
        });
        check("Solid wall blocks either attack without moving target",()=>{
            foreach(bool kick in new[]{false,true}){
                using var a=new Arena(1.35f);a.Solid(new Vector3(0,1,.65f),new Vector3(3,2,.15f));
                if(kick)a.combat.TryKick();else a.combat.TryPush();
                a.Advance(1.2f);Need(a.receiver.ReceivedHits==0&&a.combat.HitCount==0&&a.target.HorizontalSpeed==0,"hit through wall");
            }
        });
        check("Targets behind and beyond reach are not hit",()=>{
            using(var a=new Arena(-1)){a.combat.TryKick();a.Advance(1.2f);Need(a.receiver.ReceivedHits==0,"rear hit");}
            using(var a=new Arena(3)){a.combat.TryKick();a.Advance(1.2f);Need(a.receiver.ReceivedHits==0,"distant hit");}
        });
        check("Trigger volumes do not block pushes",()=>{
            using var a=new Arena();var trigger=a.Solid(new Vector3(0,1,.6f),new Vector3(2,2,.1f));trigger.GetComponent<Collider>().isTrigger=true;
            a.combat.TryPush();a.Advance(.6f);Need(a.receiver.ReceivedHits==1,"trigger blocked push");
        });
        check("Long physics step cannot skip either contact window",()=>{
            using(var a=new Arena()){a.combat.TryPush();a.combat.SimulateAttack(.6f);Need(a.receiver.ReceivedHits==1,"skipped quick contact");}
            using(var a=new Arena()){a.combat.TryKick();a.combat.SimulateAttack(1.2f);Need(a.receiver.ReceivedHits==1,"skipped kick contact");}
        });
        check("Movement continues during attack and push moves opponent",()=>{
            using var a=new Arena();a.combat.TryKick();a.Advance(.36f);float z=a.target.transform.position.z;
            for(int i=0;i<10;i++){a.target.Simulate(default,.015f);a.actor.Simulate(new MovementCommand{move=Vector2.right},.015f);}
            Need(a.target.transform.position.z>z+.1f && a.actor.HorizontalSpeed>1,"motion was locked");
        });
        check("Grounded push and kick displace, with stronger kick travel and no launch",()=>{
            using var a=new Arena();Vector3 start=a.target.transform.position;float quickDistance=0;
            foreach(var attack in new[]{MeleeAttack.PushLeft,MeleeAttack.Kick}){
                a.receiver.ResetReaction();a.target.Teleport(start);for(int i=0;i<10;i++)a.target.Simulate(default,.015f);
                start=a.target.transform.position;
                Need(a.receiver.Receive(attack,Vector3.forward+Vector3.up,a.target.CurrentEyePosition),"push rejected");
                Need(a.receiver.LastImpulse.y==0&&!a.target.Staggered&&!a.target.Stunned&&a.target.RaceLaunchRemaining==0,"launch or control lock");
                float peak=0;
                for(int i=0;i<100;i++){a.target.Simulate(default,.015f);peak=Mathf.Max(peak,a.target.transform.position.y-start.y);}
                float distance=a.target.transform.position.z-start.z;
                Debug.Log("PUSH_TRAVEL attack="+attack+" metres="+distance+" height="+peak);
                Need(distance>.05f&&peak<.06f&&a.target.Grounded,"push failed to remain horizontal");
                if(attack==MeleeAttack.PushLeft)quickDistance=distance;
                else Need(distance>.3f&&distance>quickDistance*2,"kick not meaningfully stronger: quick="+quickDistance+" kick="+distance);
            }
        });
        check("Kick still collides with a nearby solid wall",()=>{
            using var a=new Arena();a.Solid(new Vector3(0,2,1.8f),new Vector3(8,4,.2f));
            a.receiver.Receive(MeleeAttack.Kick,Vector3.forward,a.target.CurrentEyePosition);
            for(int i=0;i<80;i++)a.target.Simulate(default,.015f);
            Need(a.target.transform.localPosition.z<1.5f&&Mathf.Abs(a.target.Velocity.z)<.1f,"push crossed wall");
        });
        check("Network presentation adds no impulse and restored host physics predicts identically",()=>{
            using var a=new Arena();var mirror=a.actor.gameObject.AddComponent<MeleeReceiver>();
            a.receiver.Receive(MeleeAttack.Kick,Vector3.forward,a.target.CurrentEyePosition);
            var state=a.target.CaptureNetworkState();Vector3 offset=Vector3.right*5;state.position+=offset;
            a.actor.RestoreNetworkState(state);Vector3 velocity=a.actor.Velocity;int hits=mirror.ReceivedHits;
            mirror.ReceiveNetwork(MeleeAttack.Kick,Vector3.forward,a.actor.CurrentEyePosition);
            mirror.ReceiveNetwork(MeleeAttack.PushRight,Vector3.left,a.actor.CurrentEyePosition);
            Need(a.actor.Velocity==velocity&&mirror.ReceivedHits==hits+2&&mirror.PushProtectionRemaining==0,"client re-applied physics or filtered accepted events");
            for(int i=0;i<12;i++){
                var command=new MovementCommand{move=Vector2.right,jumpPressed=i==0};
                a.actor.Simulate(command,.015f);a.target.Simulate(command,.015f);
            }
            Need(Vector3.Distance(a.actor.transform.position-offset,a.target.transform.position)<.01f&&Vector3.Distance(a.actor.Velocity,a.target.Velocity)<.01f,"host/client push prediction diverged");
        });
        if(character && character.combat) {
            var c=character.combat;c.readInput=false;c.ResetCombat(resetCooldown:true);character.receiver.ResetReaction();
            character.motor.Teleport(new Vector3(0,.03f,-3));for(int i=0;i<15;i++)character.motor.Simulate(default,.015f);
            c.TryPush();yield return new WaitForSeconds(.10f);
            check("Push animation has priority over idle",()=>Need(character.CurrentHandState=="PushLeft" && character.CurrentBodyState=="PushLeft","attack animation replaced"));
            yield return new WaitForSeconds(.06f);
            check("Runtime one-hand push extends wrist in both views",()=>{
                var hand=character.handsModel.GetComponentsInChildren<Transform>().First(t=>t.name=="L_Wrist");
                var bodyHand=character.bodyModel.GetComponentsInChildren<Transform>().First(t=>t.name=="LeftHand");
                Need(character.handsModel.InverseTransformPoint(hand.position).z>.58f,"first person palm did not extend");
                Need(character.bodyModel.InverseTransformPoint(bodyHand.position).z>.35f,"body upper layer did not extend");
            });
            yield return new WaitForSeconds(.5f);
            check("Hands return to movement after quick push",()=>Need(character.CurrentHandState=="Idle","did not resume idle"));
            c.TryKick();yield return new WaitForSeconds(.37f);
            check("Kick extends the right foot in both views while hands brace naturally",()=>{
                Need(character.CurrentHandState=="Kick"&&character.CurrentBodyState=="Kick","kick state missing");
                Need(character.kickModel&&character.kickModel.gameObject.activeSelf==!character.thirdPerson,"kick viewmodel visibility");
                var right=character.bodyModel.GetComponentsInChildren<Transform>().First(t=>t.name=="RightFoot");
                var left=character.bodyModel.GetComponentsInChildren<Transform>().First(t=>t.name=="LeftFoot");
                Vector3 foot=character.bodyModel.InverseTransformPoint(right.position);
                Need(foot.z>.70f&&foot.y>.70f,"body right-leg layer did not kick forward");
                Need(character.bodyModel.InverseTransformPoint(left.position).y<.2f,"support foot left the ground");
                if(!character.thirdPerson) {
                    var viewFoot=character.kickModel.GetComponentsInChildren<Transform>().First(t=>t.name=="RightFoot");
                    Vector3 local=character.kickModel.InverseTransformPoint(viewFoot.position);
                    Vector3 viewport=character.handCamera.WorldToViewportPoint(viewFoot.position);
                    Need(local.z>.70f&&local.y>.70f,"first-person leg did not reach contact");
                    Need(viewport.z>0&&viewport.x>0&&viewport.x<1&&viewport.y>0&&viewport.y<1,"first-person foot is outside the camera");
                }
                foreach(string bone in new[]{"L_Wrist","R_Wrist"}) {
                    var wrist=character.handsModel.GetComponentsInChildren<Transform>().First(t=>t.name==bone);
                    Need(character.handsModel.InverseTransformPoint(wrist.position).z<.5f,"kick still shows a two-palm shove");
                }
            });
            yield return new WaitForSeconds(.85f);
            check("Kick restores the leg and locomotion after recovery",()=>{
                Need(character.CurrentHandState=="Idle"&&character.CurrentBodyState=="Idle"&&!c.IsAttacking,"kick did not recover");
                Need(character.kickModel&&!character.kickModel.gameObject.activeSelf,"first-person leg remained visible");
                var foot=character.bodyModel.GetComponentsInChildren<Transform>().First(t=>t.name=="RightFoot");
                Vector3 local=character.bodyModel.InverseTransformPoint(foot.position);
                Need(local.y<.2f&&local.z<.3f,"body leg remained in contact pose");
            });
        }
    }
}
#endif
