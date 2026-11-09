#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using HalliGalli.Movement;

public static class ReactionValidation
{
    static void Need(bool value,string message){if(!value)throw new Exception(message);}
    public static IEnumerator Run(Action<string,Action> check,CharacterMotionDriver character)
    {
        if(!character)yield break;
        var m=character.motor;var r=character.receiver;var c=character.combat;
        void Reset(){c.ResetCombat(resetCooldown:true);r.ResetReaction();m.Teleport(new Vector3(0,.03f,-3));for(int i=0;i<20;i++)m.Simulate(default,.015f);}
        Reset();yield return new WaitForSeconds(.25f);
        check("Push and kick permit immediate movement, jump and counterattack",()=>{
            foreach(var attack in new[]{MeleeAttack.PushLeft,MeleeAttack.Kick}){
                Reset();var before=m.transform.position;int sounds=r.SoundsPlayed;
                Need(r.Receive(attack,Vector3.back,m.CurrentEyePosition),"push rejected");
                Need(!m.Staggered&&!m.Stunned&&m.RaceLaunchRemaining==0&&r.LastImpulse.y==0,"push locked control or launched");
                Need(c.TryPush(),"counterattack blocked");
                for(int i=0;i<5;i++)m.Simulate(new MovementCommand{move=Vector2.right,jumpPressed=i==0},.015f);
                Need(m.transform.position.y>before.y+.25f&&m.Velocity.x>0,"push blocked movement or jump");
                var soundsForAttack=attack==MeleeAttack.Kick?r.audioSet.strongPushes:r.audioSet.pushes;
                Need(r.SoundsPlayed==sounds+1&&soundsForAttack.Contains(r.LastSound),"incorrect push sound");
            }
        });
        check("Accepted kick does not cancel windup or one buffered follow-up",()=>{
            Reset();c.TryPush();c.SimulateAttack(.42f);Need(c.TryPush(),"buffer setup");
            Need(r.Receive(MeleeAttack.Kick,Vector3.left,m.CurrentEyePosition),"kick rejected");
            Need(c.IsAttacking&&c.HasBufferedAttack,"incoming kick interrupted attack");
            c.SimulateAttack(.15f);
            Need(c.CurrentAttack==MeleeAttack.PushRight&&c.AttackSequence>1,"buffered counterattack lost");
        });
        check("Quick push directions select four authored body reactions",()=>{
            Reset();var dirs=new[]{Vector3.back,Vector3.forward,Vector3.right,Vector3.left};
            var names=new[]{"HitFront","HitBack","HitLeft","HitRight"};
            for(int i=0;i<dirs.Length;i++){r.ResetReaction();r.Receive(MeleeAttack.PushLeft,dirs[i],m.CurrentEyePosition);Need(r.ReactionName==names[i],names[i]);}
        });
        check("Push preserves gravity, prior momentum and air steering",()=>{
            Reset();m.Teleport(new Vector3(0,5,-3));m.AddImpulse(Vector3.right*3);
            r.Receive(MeleeAttack.PushLeft,Vector3.forward,m.CurrentEyePosition);
            float height=m.transform.position.y;
            for(int i=0;i<6;i++)m.Simulate(new MovementCommand{move=Vector2.up},.015f);
            Need(m.transform.position.y<height&&m.Velocity.x>2.9f&&m.Velocity.z>=3.5f,"air steering or momentum blocked");
        });
        check("Protected hits do not replay sound, reactions or impulse",()=>{
            Reset();r.Receive(MeleeAttack.PushLeft,Vector3.forward,m.CurrentEyePosition);
            int hits=r.ReceivedHits,sounds=r.SoundsPlayed;float hitAt=r.LastHitTime;Vector3 velocity=m.Velocity;
            for(int i=0;i<12;i++)Need(!r.Receive(MeleeAttack.Kick,Vector3.left,m.CurrentEyePosition),"protected hit accepted");
            Need(r.ReceivedHits==hits&&r.SoundsPlayed==sounds&&r.LastHitTime==hitAt&&m.Velocity==velocity,"repeat changed accepted hit");
            Need(c.TryKick(),"protection blocked target input");
        });
        Reset();yield return new WaitForSeconds(.25f);
        var spine=character.bodyModel.GetComponentsInChildren<Transform>().First(t=>t.name=="Spine");
        var wrist=character.handsModel.GetComponentsInChildren<Transform>().First(t=>t.name=="L_Wrist");
        var restSpine=spine.localRotation;var restWrist=wrist.position;
        r.Receive(MeleeAttack.PushLeft,Vector3.back,m.CurrentEyePosition);
        yield return new WaitForSeconds(.09f);
        check("Runtime accepted push moves body and first-person hands",()=>{
            Need(character.CurrentBodyState=="HitFront"&&character.CurrentHandState=="HitFront","reaction missing");
            Need(Quaternion.Angle(spine.localRotation,restSpine)>4,"spine did not react");
            Need(Vector3.Distance(wrist.position,restWrist)>.025f,"hands did not react");
        });
        float acceptedAt=r.LastHitTime;int acceptedHits=r.ReceivedHits;
        while(Time.time<acceptedAt+MeleeReceiver.PushProtectionDuration-.04f){
            r.Receive(MeleeAttack.PushRight,Vector3.left,m.CurrentEyePosition);
            yield return null;
        }
        check("Repeated pushes cannot restart or extend receiver protection",()=>Need(r.LastHitTime==acceptedAt&&r.ReceivedHits==acceptedHits&&r.ReactionName=="HitFront","protection refreshed or reaction redirected"));
        while(Time.time<acceptedAt+MeleeReceiver.PushProtectionDuration+.02f)yield return null;
        check("Protection expires on the first accepted hit deadline",()=>Need(r.Receive(MeleeAttack.PushRight,Vector3.left,m.CurrentEyePosition)&&r.ReceivedHits==acceptedHits+1,"repeat attempts extended protection"));
        var tick=new WaitForFixedUpdate();float settleUntil=Time.time+.55f;
        while(Time.time<settleUntil){m.Simulate(default,Time.fixedDeltaTime);yield return tick;}
        yield return null;
        check("Reaction returns to idle without a stuck pose",()=>Need(!r.IsReacting&&character.CurrentHandState=="Idle"&&character.CurrentBodyState=="Idle","stuck reaction"));
        Reset();for(int i=0;i<20;i++)m.Simulate(new MovementCommand{crouch=true},.015f);
        r.Receive(MeleeAttack.PushLeft,Vector3.back,m.CurrentEyePosition);
        settleUntil=Time.time+.55f;
        while(Time.time<settleUntil){m.Simulate(new MovementCommand{crouch=true},Time.fixedDeltaTime);yield return tick;}
        yield return null;
        check("Reaction returns to crouch and preserves capsule height",()=>Need(character.CurrentBodyState=="CrouchIdle"&&m.BodyHeight<1.01f,"lost crouch stance"));
        Reset();yield return CameraAndMovement(check,character);Reset();
    }

    static IEnumerator CameraAndMovement(Action<string,Action> check,CharacterMotionDriver character)
    {
        var m=character.motor;var r=character.receiver;var controller=m.GetComponent<SourcePlayerController>();
        foreach(var attack in new[]{MeleeAttack.PushLeft,MeleeAttack.Kick}){
            r.ResetReaction();m.Teleport(new Vector3(0,.03f,-3));controller.ResetView();controller.UpdateView();
            float fieldOfView=controller.playerCamera.fieldOfView;Rect viewport=controller.playerCamera.rect;
            r.Receive(attack,Vector3.right,m.CurrentEyePosition);
            yield return new WaitForSeconds(.075f);controller.UpdateView();
            check(attack+" keeps camera aim, view and movement heading",()=>{
                Need(Quaternion.Angle(controller.playerCamera.transform.rotation,controller.AimRotation)<.01f,"push changed camera aim");
                Need(Quaternion.Angle(m.transform.rotation,Quaternion.identity)<.01f,"movement heading changed");
                Need(controller.playerCamera.fieldOfView==fieldOfView&&controller.playerCamera.rect==viewport,"push restricted view");
            });
            controller.ApplyLook(new Vector2(40,-20));var intended=controller.AimRotation;controller.UpdateView();
            check(attack+" leaves mouse look responsive during reaction",()=>Need(Quaternion.Angle(intended,Quaternion.identity)>0&&Quaternion.Angle(controller.playerCamera.transform.rotation,intended)<.01f,"mouse look locked"));
            for(int i=0;i<20;i++)m.Simulate(new MovementCommand{crouch=true,move=Vector2.up},.015f);
            check(attack+" leaves crouch and walking input available",()=>Need(m.BodyHeight<1.01f&&m.Velocity.z>0&&!m.Staggered&&!m.Stunned,"crouch/walk blocked"));
        }
        r.ResetReaction();m.Teleport(new Vector3(0,.03f,-3));
    }
}
#endif
