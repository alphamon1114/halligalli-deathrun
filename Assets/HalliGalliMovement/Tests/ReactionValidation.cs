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
        void Reset(){c.ResetCombat();r.ResetReaction();m.Teleport(new Vector3(0,.03f,-3));for(int i=0;i<20;i++)m.Simulate(default,.015f);}
        Reset();yield return new WaitForSeconds(.25f);
        check("Impact audio assigned on player and both opponents",()=>{
            foreach(var receiver in UnityEngine.Object.FindObjectsByType<MeleeReceiver>(FindObjectsSortMode.None)){
                Need(receiver.audioSet && receiver.impactSource,"missing sound setup: "+receiver.name);
                Need(receiver.audioSet.punches.Length==3 && receiver.audioSet.kicks.Length==3,"missing variants");
                Need(receiver.impactSource.spatialBlend==1 && !receiver.impactSource.playOnAwake,"not spatial impact audio");
            }
            foreach(var clip in r.audioSet.punches.Concat(r.audioSet.kicks)){
                Need(clip && clip.length>.1f && clip.length<1,"invalid impact duration");
                var data=new float[clip.samples*clip.channels];Need(clip.GetData(data,0),"audio unreadable");
                Need(data.Max(v=>Mathf.Abs(v))>.1f && data.Max(v=>Mathf.Abs(v))<.99f,"silent or clipped sound");
            }
        });
        check("Punch allows immediate movement and jump without knockback",()=>{
            var before=m.transform.position;int sounds=r.SoundsPlayed;
            r.Receive(MeleeAttack.PunchLeft,Vector3.back,m.CurrentEyePosition);
            Need(!m.Staggered && r.ReactionName=="HitFront" && r.LastImpulse==Vector3.zero,"punch response");
            for(int i=0;i<5;i++)m.Simulate(new MovementCommand{move=Vector2.up,jumpPressed=true},.015f);
            Need(m.transform.position.y>before.y+.25f && m.HorizontalSpeed>0,"punch blocked movement or jump");
            Need(r.SoundsPlayed==sounds+1 && r.audioSet.punches.Contains(r.LastSound),"incorrect punch sound");
        });
        check("Only kick briefly blocks attacks then restores control",()=>{
            Reset();r.Receive(MeleeAttack.PunchLeft,Vector3.back,m.CurrentEyePosition);
            Need(c.TryPunch(),"punch blocked counterattack");c.InterruptAttack();
            r.Receive(MeleeAttack.Kick,Vector3.forward,m.CurrentEyePosition);
            Need(!c.TryKick(),"attack accepted while staggered");
            for(int i=0;i<10;i++)m.Simulate(default,.015f);
            Need(!m.Staggered && c.TryPunch(),"control did not recover");
            c.InterruptAttack();
        });
        check("Hit cancels windup and a queued follow-up attack",()=>{
            Reset();c.TryPunch();c.SimulateAttack(.30f);Need(c.TryPunch(),"buffer setup");
            r.Receive(MeleeAttack.PunchRight,Vector3.left,m.CurrentEyePosition);c.SimulateAttack(1);
            Need(!c.IsAttacking && r.ReactionName=="HitRight","interrupted attack survived");
        });
        check("Punch directions select four authored reactions",()=>{
            var dirs=new[]{Vector3.back,Vector3.forward,Vector3.right,Vector3.left};
            var names=new[]{"HitFront","HitBack","HitLeft","HitRight"};
            for(int i=0;i<dirs.Length;i++){r.Receive(MeleeAttack.PunchLeft,dirs[i],m.CurrentEyePosition);Need(r.ReactionName==names[i],names[i]);}
        });
        check("Punch preserves gravity and allows air steering",()=>{
            Reset();m.Teleport(new Vector3(0,5,-3));m.AddImpulse(Vector3.right*3);
            r.Receive(MeleeAttack.PunchLeft,Vector3.back,m.CurrentEyePosition);
            float height=m.transform.position.y;
            for(int i=0;i<6;i++)m.Simulate(new MovementCommand{move=Vector2.up},.015f);
            Need(m.transform.position.y<height && m.Velocity.x>2.9f && m.Velocity.z>.1f,"air steering or momentum blocked");
        });
        check("Kick applies physical knockback and its own impact sound",()=>{
            Reset();int sounds=r.SoundsPlayed;float z=m.transform.position.z;
            r.Receive(MeleeAttack.Kick,Vector3.forward,m.CurrentEyePosition);
            Need(r.LastImpulse.z==6 && r.LastImpulse.y==4f && r.ReactionName=="Knockback","kick impulse");
            for(int i=0;i<8;i++)m.Simulate(default,.015f);
            Need(m.transform.position.z>z+.4f,"kick did not move capsule");
            Need(r.SoundsPlayed==sounds+1 && r.audioSet.kicks.Contains(r.LastSound),"incorrect kick sound");
        });
        Reset();yield return new WaitForSeconds(.25f);
        var spine=character.bodyModel.GetComponentsInChildren<Transform>().First(t=>t.name=="Spine");
        var wrist=character.handsModel.GetComponentsInChildren<Transform>().First(t=>t.name=="L_Wrist");
        var restSpine=spine.localRotation;var restWrist=wrist.position;
        r.Receive(MeleeAttack.PunchLeft,Vector3.back,m.CurrentEyePosition);
        yield return new WaitForSeconds(.09f);
        check("Runtime hit moves body and first-person hands",()=>{
            Need(character.CurrentBodyState=="HitFront" && character.CurrentHandState=="HitFront","reaction priority");
            Need(Quaternion.Angle(spine.localRotation,restSpine)>4,"spine did not recoil");
            Need(Vector3.Distance(wrist.position,restWrist)>.025f,"hands did not recoil");
        });
        r.Receive(MeleeAttack.PunchRight,Vector3.left,m.CurrentEyePosition);
        yield return new WaitForSeconds(.06f);
        check("Repeated hit restarts and redirects reaction",()=>Need(character.CurrentBodyState=="HitRight" && r.ReactionElapsed<.2f,"reaction did not restart"));
        yield return new WaitForSeconds(.55f);
        check("Reaction returns to idle without a stuck pose",()=>Need(!r.IsReacting && character.CurrentHandState=="Idle" && character.CurrentBodyState=="Idle","stuck reaction"));
        Reset();for(int i=0;i<20;i++)m.Simulate(new MovementCommand{crouch=true},.015f);
        r.Receive(MeleeAttack.PunchLeft,Vector3.back,m.CurrentEyePosition);yield return new WaitForSeconds(.55f);
        check("Reaction returns to crouch and preserves capsule height",()=>Need(character.CurrentBodyState=="CrouchIdle" && m.BodyHeight<1.01f,"lost crouch stance"));
        Reset();
        yield return CameraAndMovement(check,character);
    }

    static IEnumerator CameraAndMovement(Action<string,Action> check,CharacterMotionDriver character)
    {
        var m=character.motor;var r=character.receiver;var controller=m.GetComponent<SourcePlayerController>();
        r.ResetReaction();m.Teleport(new Vector3(0,.03f,-3));controller.ResetView();
        for(int i=0;i<25;i++)m.Simulate(new MovementCommand{move=Vector2.up},.015f);
        check("Repeated punches do not reduce normal walking speed",()=>{
            for(int i=0;i<12;i++){
                r.Receive(MeleeAttack.PunchLeft,Vector3.back,m.CurrentEyePosition);
                m.Simulate(new MovementCommand{move=Vector2.up},.015f);
                Need(Mathf.Abs(m.HorizontalSpeed-4.5f)<.1f && !m.Staggered,"movement locked");
            }
        });
        r.ResetReaction();m.Teleport(new Vector3(0,.03f,-3));controller.ResetView();controller.UpdateView();
        r.Receive(MeleeAttack.PunchLeft,Vector3.right,m.CurrentEyePosition);
        yield return new WaitForSeconds(.075f);controller.UpdateView();
        check("Punch narrows vision without rotating camera or movement heading",()=>{
            Need(r.PunchBlinkAmount>.9f,"blink missing");
            Need(Quaternion.Angle(controller.playerCamera.transform.rotation,controller.AimRotation)<.01f,"punch changed camera aim");
            Need(Quaternion.Angle(m.transform.rotation,Quaternion.identity)<.01f,"movement heading changed");
        });
        float firstBlink=r.LastBlinkTime;int blinks=r.BlinksPlayed;
        r.Receive(MeleeAttack.PunchRight,Vector3.left,m.CurrentEyePosition);
        check("Repeated hits do not restart or extend the blink",()=>Need(r.LastBlinkTime==firstBlink&&r.BlinksPlayed==blinks,"blink extended"));
        controller.ApplyLook(new Vector2(40,-20));var intended=controller.AimRotation;
        controller.UpdateView();
        check("Mouse look still responds while vision is narrowed",()=>Need(Quaternion.Angle(intended,Quaternion.identity)>3 && r.PunchBlinkAmount>.5f && Quaternion.Angle(controller.playerCamera.transform.rotation,intended)<.01f,"mouse look locked"));
        while(Time.time<firstBlink+.32f){
            r.Receive(MeleeAttack.PunchLeft,Vector3.right,m.CurrentEyePosition);
            yield return null;
        }
        check("Vision is fully restored after 0.3 seconds even under repeated hits",()=>Need(r.PunchBlinkAmount==0 && r.BlinksPlayed==blinks,"vision did not recover"));
        while(Time.time<firstBlink+.70f){r.ReceiveNetwork(MeleeAttack.PunchRight,Vector3.left,m.CurrentEyePosition);yield return null;}
        check("Network hits also respect the clear-vision recovery window",()=>Need(r.PunchBlinkAmount==0&&r.BlinksPlayed==blinks,"recovery window extended or bypassed"));
        while(Time.time<firstBlink+.77f)yield return null;
        r.ReceiveNetwork(MeleeAttack.PunchRight,Vector3.left,m.CurrentEyePosition);
        yield return new WaitForSeconds(.075f);
        check("A new network punch can blink after recovery expires",()=>Need(r.PunchBlinkAmount>.9f&&r.BlinksPlayed==blinks+1,"next blink missing"));
        yield return new WaitForSeconds(.25f);controller.UpdateView();
        check("Blink finishes without camera drift",()=>Need(r.PunchBlinkAmount==0 && Quaternion.Angle(controller.playerCamera.transform.rotation,intended)<.01f,"camera failed to recover"));
        r.ClearPunchBlink();r.Receive(MeleeAttack.PunchLeft,Vector3.back,m.CurrentEyePosition);yield return new WaitForSeconds(.08f);
        controller.ResetView();controller.UpdateView();
        check("Reset clears vision restriction immediately",()=>Need(r.PunchBlinkAmount==0 && Quaternion.Angle(controller.playerCamera.transform.rotation,Quaternion.identity)<.01f,"reset retained blink"));
        r.ResetReaction();r.Receive(MeleeAttack.Kick,Vector3.forward,m.CurrentEyePosition);
        yield return new WaitForSeconds(.075f);
        check("Kick keeps its knockback and does not trigger a blink",()=>Need(r.PunchBlinkAmount==0&&r.LastImpulse.sqrMagnitude>0,"kick blink"));
        r.ResetReaction();m.Teleport(new Vector3(0,.03f,-3));
    }
}
#endif
