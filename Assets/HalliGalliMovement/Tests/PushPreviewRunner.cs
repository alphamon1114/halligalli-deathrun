#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalliGalli.Movement;
using UnityEditor;
using UnityEngine;

public sealed class PushPreviewRunner:MonoBehaviour
{
    [Serializable] sealed class Manifest
    {
        public List<Frame> frames=new();
    }
    [Serializable] sealed class Frame
    {
        public string path,view,attack,phase,bodyState,handState,captureMode;
        public float elapsedSinceStart,combatElapsed,duration,contactStart,contactEnd;
        public bool kickVisible;
        public Vector3 bodyRightFoot,bodyLeftFoot,firstPersonRightFoot,firstPersonFootViewport;
    }
    readonly Manifest manifest=new();

    IEnumerator Start()
    {
        yield return null;yield return null;
        var session=FindAnyObjectByType<CourseSession>();
        session.cardGame.botEnabled=false;session.cardGame.ReadInput=false;session.cardGame.enabled=false;
        foreach(var p in session.participants){var bot=p.GetComponent<CourseRival>();if(bot)bot.enabled=false;p.Combat.readInput=false;}
        FindAnyObjectByType<CourseHUD>().ReadInput=false;
        var player=session.Local;player.Stand();
        player.Motor.Teleport(new Vector3(0,.375f,5.7f));player.Motor.Simulate(default,.015f);
        var controller=player.GetComponent<SourcePlayerController>();
        controller.controlsEnabled=false;controller.movementEnabled=false;controller.ResetView(0,4);controller.SetCursorCaptured(true);
        var driver=player.GetComponent<CharacterMotionDriver>();
        var combat=player.Combat;
        Directory.CreateDirectory("Previews");Directory.CreateDirectory("TestResults");
        yield return null;
        foreach(bool thirdPerson in new[]{false,true}) {
            driver.thirdPerson=thirdPerson;yield return null;
            foreach(var attack in new[]{MeleeAttack.PushLeft,MeleeAttack.PushRight,MeleeAttack.Kick}) {
                while(attack==MeleeAttack.Kick&&combat.KickCooldownRemaining>0)yield return null;
                bool accepted=attack==MeleeAttack.Kick?combat.TryKick():combat.TryPush();
                if(!accepted||combat.CurrentAttack!=attack)throw new InvalidOperationException("Push preview could not start "+attack);
                float started=Time.time;
                float contact=(MeleeCombat.ContactStart(attack)+MeleeCombat.ContactEnd(attack))*.5f;
                while(combat.IsAttacking&&combat.Elapsed<contact)yield return null;
                if(!combat.IsAttacking||combat.Elapsed>MeleeCombat.ContactEnd(attack))
                    throw new InvalidOperationException("Push preview missed the contact window for "+attack);
                yield return Capture(driver,combat,attack,"Contact",started);
                while(combat.IsAttacking)yield return null;
                yield return new WaitForSeconds(.16f);
                yield return Capture(driver,combat,attack,"Recovered",started);
            }
        }
        File.WriteAllText("TestResults/push-preview.json",JsonUtility.ToJson(manifest,true));
        Debug.Log("PUSH_PREVIEW_COMPLETE TestResults/push-preview.json");
        if(Application.isBatchMode)EditorApplication.Exit(0);else EditorApplication.isPlaying=false;
    }
    IEnumerator Capture(CharacterMotionDriver driver,MeleeCombat combat,MeleeAttack attack,string phase,float started)
    {
        string view=driver.thirdPerson?"Body":"FP";
        var frame=new Frame{path="Previews/Push_"+view+"_"+attack+"_"+phase+".png",view=view,attack=attack.ToString(),phase=phase,
            elapsedSinceStart=Time.time-started,combatElapsed=combat.Elapsed,duration=MeleeCombat.Duration(attack),
            contactStart=MeleeCombat.ContactStart(attack),contactEnd=MeleeCombat.ContactEnd(attack)};
        Time.timeScale=0;
        if(Application.isBatchMode) {
            yield return null;
            frame.bodyState=driver.CurrentBodyState;frame.handState=driver.CurrentHandState;frame.captureMode="RuntimeCameraRender";
            RenderRuntimeFrame(driver,frame.path);
        } else {
            yield return new WaitForEndOfFrame();
            frame.bodyState=driver.CurrentBodyState;frame.handState=driver.CurrentHandState;frame.captureMode="ScreenCapture";
            ScreenCapture.CaptureScreenshot(frame.path);
            yield return new WaitForEndOfFrame();
        }
        frame.kickVisible=driver.kickModel&&driver.kickModel.gameObject.activeInHierarchy;
        frame.bodyRightFoot=driver.bodyModel.InverseTransformPoint(driver.bodyModel.GetComponentsInChildren<Transform>().First(t=>t.name=="RightFoot").position);
        frame.bodyLeftFoot=driver.bodyModel.InverseTransformPoint(driver.bodyModel.GetComponentsInChildren<Transform>().First(t=>t.name=="LeftFoot").position);
        if(frame.kickVisible) {
            var foot=driver.kickModel.GetComponentsInChildren<Transform>().First(t=>t.name=="RightFoot");
            frame.firstPersonRightFoot=driver.kickModel.InverseTransformPoint(foot.position);
            frame.firstPersonFootViewport=driver.handCamera.WorldToViewportPoint(foot.position);
        }
        manifest.frames.Add(frame);Time.timeScale=1;
    }
    static void RenderRuntimeFrame(CharacterMotionDriver driver,string path)
    {
        // Editor batch mode does not resume WaitForEndOfFrame; render the live sampled pose explicitly.
        var skins=new List<(SkinnedMeshRenderer skin,GameObject visual,Mesh mesh)>();
        var target=new RenderTexture(1200,1000,24);var active=RenderTexture.active;
        var camera=driver.worldCamera;var overlay=driver.thirdPerson?null:driver.handCamera;
        var cameraTarget=camera.targetTexture;var overlayTarget=overlay?overlay.targetTexture:null;
        Texture2D image=null;
        try {
            foreach(var skin in FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None)) {
                if(!skin.enabled)continue;
                var mesh=new Mesh();skin.BakeMesh(mesh,true);
                var visual=new GameObject("Push preview skin",typeof(MeshFilter),typeof(MeshRenderer));
                visual.transform.SetParent(skin.transform,false);visual.layer=skin.gameObject.layer;
                visual.GetComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=visual.GetComponent<MeshRenderer>();renderer.sharedMaterials=skin.sharedMaterials;
                renderer.shadowCastingMode=skin.shadowCastingMode;renderer.receiveShadows=skin.receiveShadows;
                skin.enabled=false;skins.Add((skin,visual,mesh));
            }
            camera.targetTexture=target;camera.Render();
            if(overlay){overlay.targetTexture=target;overlay.Render();}
            RenderTexture.active=target;image=new Texture2D(1200,1000,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,1200,1000),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
        } finally {
            camera.targetTexture=cameraTarget;if(overlay)overlay.targetTexture=overlayTarget;RenderTexture.active=active;
            if(image)DestroyImmediate(image);target.Release();DestroyImmediate(target);
            foreach(var item in skins){item.skin.enabled=true;DestroyImmediate(item.visual);DestroyImmediate(item.mesh);}
        }
    }
    void OnDisable(){Time.timeScale=1;}
}
#endif
