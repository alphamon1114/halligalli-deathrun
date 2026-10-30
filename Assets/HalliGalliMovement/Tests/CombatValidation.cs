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
        check("Punch alternates and rejected spam cannot skip a hand",()=>{
            using var a=new Arena();Need(a.combat.TryPunch() && a.combat.CurrentAttack==MeleeAttack.PunchLeft,"first hand");
            Need(!a.combat.TryPunch(),"spam accepted during windup");a.Advance(.45f);
            Need(a.combat.TryPunch() && a.combat.CurrentAttack==MeleeAttack.PunchRight,"second hand");a.Advance(.45f);
            Need(a.combat.TryKick(),"kick rejected");a.Advance(.75f);
            Need(a.combat.TryPunch() && a.combat.CurrentAttack==MeleeAttack.PunchLeft,"kick changed punch order");
        });
        check("A recovery click buffers exactly one attack without automatic repeat",()=>{
            using var a=new Arena();a.combat.TryPunch();a.Advance(.30f);
            Need(a.combat.TryPunch() && !a.combat.TryPunch(),"buffer capacity");a.Advance(.15f);
            Need(a.combat.CurrentAttack==MeleeAttack.PunchRight,"buffer did not start");a.Advance(1);
            Need(!a.combat.IsAttacking && a.combat.AttackSequence==2,"unexpected repeat");
        });
        check("Punch contacts only in its contact window and only once",()=>{
            using var a=new Arena();a.combat.TryPunch();a.Advance(.10f);Need(a.receiver.ReceivedHits==0,"hit during windup");
            a.Advance(.09f);Need(a.receiver.ReceivedHits==1,"contact missing origin="+a.actor.CurrentEyePosition+" target="+a.target.transform.position+" bounds="+a.target.GetComponent<CharacterController>().bounds+" overlaps="+string.Join(";",Physics.OverlapSphere(a.target.transform.position,3).Select(c=>c.name+":"+c.bounds))+" cast="+string.Join(";",Physics.SphereCastAll(a.actor.CurrentEyePosition-Vector3.up*.16f,.18f,Vector3.forward,1.25f).Select(h=>h.collider.name+":"+h.distance)));a.Advance(.4f);
            Need(a.receiver.ReceivedHits==1 && a.receiver.LastImpulse==Vector3.zero && !a.target.Staggered,"duplicate hit or punch movement lock");
        });
        check("Kick reaches farther and pushes harder than punch",()=>{
            using var a=new Arena(1.95f);a.combat.TryPunch();a.Advance(.45f);Need(a.receiver.ReceivedHits==0,"punch too long");
            a.combat.TryKick();a.Advance(.4f);Need(a.receiver.ReceivedHits==1 && a.receiver.LastImpulse.z>5.9f,"kick range/strength");
        });
        check("Solid wall blocks attack without pushing target",()=>{
            using var a=new Arena(1.35f);a.Solid(new Vector3(0,1,.65f),new Vector3(3,2,.15f));
            a.combat.TryKick();a.Advance(.75f);Need(a.receiver.ReceivedHits==0,"hit through wall");
        });
        check("Targets behind and beyond reach are not hit",()=>{
            using(var a=new Arena(-1)){a.combat.TryKick();a.Advance(.75f);Need(a.receiver.ReceivedHits==0,"rear hit");}
            using(var a=new Arena(3)){a.combat.TryKick();a.Advance(.75f);Need(a.receiver.ReceivedHits==0,"distant hit");}
        });
        check("Trigger volumes do not block melee",()=>{
            using var a=new Arena();var trigger=a.Solid(new Vector3(0,1,.6f),new Vector3(2,2,.1f));trigger.GetComponent<Collider>().isTrigger=true;
            a.combat.TryPunch();a.Advance(.45f);Need(a.receiver.ReceivedHits==1,"trigger blocked attack");
        });
        check("Long physics step cannot skip contact",()=>{
            using var a=new Arena();a.combat.TryPunch();a.combat.SimulateAttack(.3f);Need(a.receiver.ReceivedHits==1,"skipped contact window");
        });
        check("Movement continues during attack and impulse moves opponent",()=>{
            using var a=new Arena();a.combat.TryKick();a.Advance(.35f);float z=a.target.transform.position.z;
            for(int i=0;i<10;i++){a.target.Simulate(default,.015f);a.actor.Simulate(new MovementCommand{move=Vector2.right},.015f);}
            Need(a.target.transform.position.z>z+.1f && a.actor.HorizontalSpeed>1,"motion was locked");
        });
        check("Grounded kick lifts the target and travels about three metres",()=>{
            using var a=new Arena();Vector3 start=a.target.transform.position;
            a.receiver.Receive(MeleeAttack.Kick,Vector3.forward,a.target.CurrentEyePosition);
            Need(a.target.Velocity.y>3.9f,"ground adhesion cancelled launch");
            float peak=0;
            for(int i=0;i<100;i++){a.target.Simulate(default,.015f);peak=Mathf.Max(peak,a.target.transform.position.y-start.y);}
            float distance=a.target.transform.position.z-start.z;
            Debug.Log("KICK_TRAVEL metres="+distance+" height="+peak);
            Need(distance>2.7f && distance<3.3f && peak>.3f && a.target.Grounded,"kick travel="+distance+", peak="+peak);
        });
        check("Stronger kick still collides with walls",()=>{
            using var a=new Arena();a.Solid(new Vector3(0,2,3),new Vector3(8,4,.2f));
            a.receiver.Receive(MeleeAttack.Kick,Vector3.forward,a.target.CurrentEyePosition);
            for(int i=0;i<80;i++)a.target.Simulate(default,.015f);
            Need(a.target.transform.position.z<2.65f && Mathf.Abs(a.target.Velocity.z)<.1f,"kick crossed wall");
        });
        if(character && character.combat) {
            var c=character.combat;c.readInput=false;c.ResetCombat();
            character.motor.Teleport(new Vector3(0,.03f,-3));for(int i=0;i<15;i++)character.motor.Simulate(default,.015f);
            c.TryPunch();yield return new WaitForSeconds(.10f);
            check("Attack animation has priority over idle",()=>Need(character.CurrentHandState=="PunchLeft" && character.CurrentBodyState=="PunchLeft","attack animation replaced"));
            yield return new WaitForSeconds(.07f);
            check("Runtime punch extends the actual wrist bones",()=>{
                var hand=character.handsModel.GetComponentsInChildren<Transform>().First(t=>t.name=="L_Wrist");
                var bodyHand=character.bodyModel.GetComponentsInChildren<Transform>().First(t=>t.name=="LeftHand");
                Need(character.handsModel.InverseTransformPoint(hand.position).z>.58f,"first person wrist did not extend");
                Need(character.bodyModel.InverseTransformPoint(bodyHand.position).z>.35f,"body upper layer did not extend");
            });
            yield return new WaitForSeconds(.5f);
            check("Hands return to movement after punch",()=>Need(character.CurrentHandState=="Idle","did not resume idle"));
            c.TryKick();yield return new WaitForSeconds(.30f);
            check("First person kick shows character leg",()=>Need(character.kickModel && character.kickModel.gameObject.activeInHierarchy && character.kickModel.GetComponentsInChildren<SkinnedMeshRenderer>().Length>0,"kick model missing"));
            check("Runtime kick extends foot in both views",()=>{
                foreach(var model in new[]{character.bodyModel,character.kickModel}){
                    var foot=model.GetComponentsInChildren<Transform>().First(t=>t.name=="RightFoot");
                    Need(model.InverseTransformPoint(foot.position).z>.50f,"foot did not extend: "+model.name);
                }
            });
            yield return new WaitForSeconds(.5f);
            check("Kick viewmodel hides after recovery",()=>Need(!character.kickModel.gameObject.activeSelf && character.CurrentHandState=="Idle","kick did not recover"));
        }
    }
}
#endif
