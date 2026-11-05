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
        public string utc,completedUtc,error,unityVersion;
        public string scope="Live runtime animation/camera capture and deterministic binding/input-handler checks; not native OS input, network or audio verification.";
        public bool complete,success;
        public int passed,failed;
        public List<Frame> frames=new();
        public List<Check> checks=new();
    }
    [Serializable] sealed class Check
    {
        public string name,detail;
        public bool passed;
    }
    [Serializable] sealed class Pose
    {
        public string elbowSource;
        public Vector3 wrist,palm,elbow,shoulder,offhand,palmNormal,palmViewport,fingerTopViewport;
        public float palmAheadOfElbow,wristAheadOfElbow,forearmForward,elbowLateralOffset,extensionRatio,palmFacing,minimumFingerStraightness;
    }
    [Serializable] sealed class Frame
    {
        public string path,detailPath,view,attack,phase,bodyState,handState,captureMode;
        public float elapsedSinceStart,combatElapsed,duration,contactStart,contactEnd;
        public bool kickVisible;
        public Vector3 bodyRightFoot,bodyLeftFoot,firstPersonRightFoot,firstPersonFootViewport,leftWrist,rightWrist;
        public Pose bodyPush,firstPersonPush;
    }
    // The FP rig has a wrist-pivoted forearm, not an elbow transform. Measure its
    // actual skinned far-end vertices rather than inventing an elbow at the pivot.
    sealed class ForearmProbe:IDisposable
    {
        readonly SkinnedMeshRenderer skin;
        readonly int[] indices;
        readonly Mesh baked=new Mesh();
        readonly List<Vector3> vertices=new();
        public ForearmProbe(Transform model,Transform forearm)
        {
            foreach(var candidate in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                int bone=Array.IndexOf(candidate.bones,forearm);
                if(bone<0)continue;
                var source=candidate.sharedMesh;
                var weights=source.boneWeights;var points=source.vertices;var bind=source.bindposes[bone];
                var owned=new List<int>();float farthest=0;
                for(int i=0;i<weights.Length;i++) {
                    var w=weights[i];
                    float weight=(w.boneIndex0==bone?w.weight0:0)+(w.boneIndex1==bone?w.weight1:0)
                        +(w.boneIndex2==bone?w.weight2:0)+(w.boneIndex3==bone?w.weight3:0);
                    if(weight<.8f)continue;
                    owned.Add(i);farthest=Mathf.Max(farthest,bind.MultiplyPoint3x4(points[i]).sqrMagnitude);
                }
                if(owned.Count<8||farthest<.01f)continue;
                skin=candidate;
                indices=owned.Where(i=>bind.MultiplyPoint3x4(points[i]).sqrMagnitude>=farthest*.81f).ToArray();
                return;
            }
            throw new InvalidOperationException("No measurable skinned forearm for "+forearm.name);
        }
        public Vector3 End()
        {
            skin.BakeMesh(baked);baked.GetVertices(vertices);Vector3 sum=Vector3.zero;
            foreach(int i in indices)sum+=vertices[i];
            return skin.transform.TransformPoint(sum/indices.Length);
        }
        public void Dispose(){DestroyImmediate(baked);}
    }

    static PushPreviewRunner instance;
    static readonly string[] Fingers={"Index","Middle","Ring","Little"};
    readonly Manifest manifest=new();
    Dictionary<string,Transform> bodyBones,handBones;
    ForearmProbe leftForearm,rightForearm;
    Camera detailCamera;
    bool finished,temporaryBindings;
    float deadline;

    void Awake()
    {
        if(instance&&instance!=this){Destroy(gameObject);return;}
        instance=this;DontDestroyOnLoad(gameObject);
        Application.runInBackground=true;Time.timeScale=1;
        deadline=Time.realtimeSinceStartup+120;
    }
    void Update()
    {
        if(instance==this&&!finished&&Time.realtimeSinceStartup>deadline)
            Finish(new TimeoutException("Push visual smoke exceeded 120 seconds."));
    }
    IEnumerator Start()
    {
        if(instance!=this)yield break;
        manifest.utc=DateTime.UtcNow.ToString("O");
        manifest.unityVersion=Application.unityVersion;
        var run=RunPreview();
        while(true) {
            object step;
            try {if(!run.MoveNext())break;step=run.Current;}
            catch(Exception exception){Finish(exception);yield break;}
            yield return step;
        }
        Finish(null);
    }
    IEnumerator RunPreview()
    {
        yield return null;yield return null;
        var session=FindAnyObjectByType<CourseSession>();
        if(!session||!session.Local)throw new InvalidOperationException("Push preview requires a loaded CourseSession.");
        session.cardGame.botEnabled=false;session.cardGame.ReadInput=false;session.cardGame.enabled=false;
        foreach(var participant in session.participants) {
            var bot=participant.GetComponent<CourseRival>();if(bot)bot.enabled=false;
            participant.Combat.readInput=false;participant.Combat.simulateAutomatically=false;
        }
        var hud=FindAnyObjectByType<CourseHUD>();if(hud)hud.ReadInput=false;
        var player=session.Local;player.Stand();
        player.Motor.Teleport(new Vector3(0,.375f,5.7f));player.Motor.Simulate(default,.015f);
        var controller=player.GetComponent<SourcePlayerController>();
        controller.controlsEnabled=false;controller.movementEnabled=false;controller.ResetView(0,4);
        controller.suppressCursorCapture=true;controller.SetCursorCaptured(false);
        var driver=player.GetComponent<CharacterMotionDriver>();var combat=player.Combat;
        bodyBones=driver.bodyModel.GetComponentsInChildren<Transform>(true).GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
        handBones=driver.handsModel.GetComponentsInChildren<Transform>(true).GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
        var skins=driver.handsModel.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        CheckThat("Runtime hands retain both 34-bone skins",skins.Length==2&&skins.All(s=>s.sharedMesh&&s.bones.Length==34&&s.bones.All(b=>b)),"renderers="+skins.Length);
        leftForearm=new ForearmProbe(driver.handsModel,handBones["L_Forearm"]);
        rightForearm=new ForearmProbe(driver.handsModel,handBones["R_Forearm"]);
        detailCamera=new GameObject("Push side-detail camera").AddComponent<Camera>();detailCamera.enabled=false;
        detailCamera.clearFlags=CameraClearFlags.SolidColor;detailCamera.backgroundColor=new Color(.10f,.13f,.17f);
        detailCamera.cullingMask=1<<CharacterMotionDriver.BodyLayer;detailCamera.fieldOfView=38;
        Directory.CreateDirectory("Previews");Directory.CreateDirectory("TestResults");
        // Combat time is stepped through its real API. Runtime Update/Animation/LateUpdate
        // still sample the live player; there is no authoring-pose or source-text substitute.
        foreach(bool thirdPerson in new[]{false,true}) {
            driver.thirdPerson=thirdPerson;combat.ResetCombat(resetCooldown:true);
            yield return new WaitForSeconds(.30f);
            var idle=Capture(driver,combat,MeleeAttack.None,"Idle",Time.time);
            var idleAnimation=driver.handsModel.GetComponent<Animation>()["Idle"];
            if(idleAnimation==null)throw new InvalidOperationException("Runtime hands have no idle animation.");
            float idleTravel=0;
            // Observe the whole breathing cycle: two samples can straddle the same
            // sinusoidal extremum and appear stationary despite a running animation.
            for(int sample=0;sample<8;sample++) {
                yield return new WaitForSeconds(idleAnimation.length/8);
                var left=driver.handsModel.InverseTransformPoint(handBones["L_Wrist"].position);
                var right=driver.handsModel.InverseTransformPoint(handBones["R_Wrist"].position);
                idleTravel=Mathf.Max(idleTravel,Vector3.Distance(idle.leftWrist,left),Vector3.Distance(idle.rightWrist,right));
            }
            Capture(driver,combat,MeleeAttack.None,"IdleMotion",Time.time);
            CheckThat(idle.view+" starts in live idle",idle.bodyState=="Idle"&&idle.handState=="Idle"&&!idle.kickVisible,"body="+idle.bodyState+", hands="+idle.handState);
            CheckThat(idle.view+" idle animation advances",idleTravel>.00005f,"maximum local wrist travel over a complete cycle="+idleTravel);
            foreach(var attack in new[]{MeleeAttack.PushLeft,MeleeAttack.PushRight,MeleeAttack.Kick}) {
                bool accepted=attack==MeleeAttack.Kick?combat.TryKick():combat.TryPush();
                if(!accepted||combat.CurrentAttack!=attack)throw new InvalidOperationException("Push preview could not start "+attack);
                float started=Time.time,elapsed=0;
                float duration=MeleeCombat.Duration(attack),contactStart=MeleeCombat.ContactStart(attack),contactEnd=MeleeCombat.ContactEnd(attack);
                var phases=new[]{"Anticipation","Contact","Recovery","Recovered"};
                var times=new[]{contactStart*.5f,(contactStart+contactEnd)*.5f,duration*.80f,duration+.04f};
                Frame anticipation=null,contact=null;
                for(int i=0;i<phases.Length;i++) {
                    Advance(combat,times[i]-elapsed);elapsed=times[i];
                    // Finish short hand crossfades and allow the actual animation system and
                    // camera LateUpdate to evaluate before CPU-baking that runtime pose.
                    yield return new WaitForSeconds(.06f);yield return null;
                    var frame=Capture(driver,combat,attack,phases[i],started);
                    if(i==0)anticipation=frame;
                    if(i==1) {
                        contact=frame;
                        if(attack==MeleeAttack.Kick)CheckKickContact(frame);
                        else CheckContact(frame,anticipation);
                    }
                    if(i==2&&attack!=MeleeAttack.Kick)
                        CheckThat(frame.view+" "+attack+" retracts during recovery",frame.firstPersonPush.wrist.z<contact.firstPersonPush.wrist.z-.15f&&frame.bodyPush.wrist.z<contact.bodyPush.wrist.z-.10f,"Live wrist depth must decrease in both models.");
                    if(i==3) {
                        CheckThat(frame.view+" "+attack+" returns to idle",frame.bodyState=="Idle"&&frame.handState=="Idle"&&!frame.kickVisible,"body="+frame.bodyState+", hands="+frame.handState);
                        if(attack==MeleeAttack.Kick)
                            CheckThat(frame.view+" kick restores supporting stance",frame.bodyRightFoot.y<.2f&&frame.bodyRightFoot.z<.3f,"rightFoot="+frame.bodyRightFoot);
                    }
                }
            }
        }
        var controls=ValidateControls(player,driver,controller);
        while(controls.MoveNext())yield return controls.Current;
        CheckThat("Capture includes both views of idle and all attack phases",manifest.frames.Count==28,"frames="+manifest.frames.Count);
    }
    IEnumerator ValidateControls(CourseParticipant player,CharacterMotionDriver driver,SourcePlayerController controller)
    {
        // Exercise the same binding evaluator and gameplay handlers as live input,
        // without OS key injection or changing the user's saved controls.
        temporaryBindings=true;ControlBindings.Defaults(false);
        var combat=player.Combat;var motor=player.Motor;
        driver.thirdPerson=false;combat.ResetCombat(resetCooldown:true);driver.receiver.ResetReaction();
        controller.controlsEnabled=true;controller.movementEnabled=true;
        CheckThat("Default combat controls remain LMB and F",ControlBindings.Get(GameAction.Primary,0)==(int)KeyCode.Mouse0&&ControlBindings.Get(GameAction.Kick,0)==(int)KeyCode.F,"No PlayerPrefs writes.");
        foreach(var attack in new[]{MeleeAttack.PushLeft,MeleeAttack.PushRight,MeleeAttack.Kick}) {
            motor.Teleport(new Vector3(0,.375f,5.7f));controller.ResetView(0,4);
            for(int i=0;i<20;i++)motor.Simulate(default,.015f);
            yield return new WaitForSeconds(.30f);
            int sequence=combat.AttackSequence;
            var key=attack==MeleeAttack.Kick?KeyCode.F:KeyCode.Mouse0;
            bool primary=Bound(GameAction.Primary,true,key),kick=Bound(GameAction.Kick,true,key);
            combat.ProcessPointerInput(primary,false,kick);
            CheckThat(attack+" starts through bound input handler",combat.CurrentAttack==attack&&combat.AttackSequence==sequence+1&&controller.actionsEnabled&&combat.actionsEnabled,"state="+combat.CurrentAttack);
            combat.ProcessPointerInput(primary,false,kick);
            CheckThat(attack+" windup rejects duplicate input",combat.CurrentAttack==attack&&combat.AttackSequence==sequence+1&&!combat.HasBufferedAttack,"sequence="+combat.AttackSequence);
            Advance(combat,.06f);
            var aim=controller.AimRotation;controller.ApplyLook(new Vector2(40,-20));controller.UpdateView();
            CheckThat(attack+" preserves mouse look",Quaternion.Angle(aim,controller.AimRotation)>3&&Quaternion.Angle(controller.playerCamera.transform.rotation,controller.AimRotation)<.01f,"rotation="+controller.playerCamera.transform.eulerAngles);
            var start=motor.transform.position;int jumps=motor.JumpCount;
            var movement=new MovementCommand{move=Bound(GameAction.Right,false,KeyCode.D)?Vector2.right:Vector2.zero,
                jumpPressed=Bound(GameAction.Jump,true,KeyCode.Space)};
            for(int i=0;i<5;i++) {
                motor.Simulate(movement,.015f);combat.SimulateAttack(.015f);movement.jumpPressed=false;
            }
            CheckThat(attack+" preserves movement and jump",motor.JumpCount==jumps+1&&motor.transform.position.y>start.y+.25f&&motor.Velocity.x>0&&!motor.Staggered&&!motor.Stunned,"travel="+(motor.transform.position-start)+", velocity="+motor.Velocity);
            yield return new WaitForSeconds(.06f);yield return null;
            CheckThat(attack+" keeps live animation while moving",driver.CurrentHandState==attack.ToString()&&driver.CurrentBodyState==attack.ToString(),"body="+driver.CurrentBodyState+", hands="+driver.CurrentHandState);
            motor.Teleport(new Vector3(0,.375f,5.7f));
            movement=new MovementCommand{move=Bound(GameAction.Forward,false,KeyCode.W)?Vector2.up:Vector2.zero,
                crouch=Bound(GameAction.Crouch,false,KeyCode.LeftControl)};
            for(int i=0;i<20;i++){motor.Simulate(movement,.015f);combat.SimulateAttack(.015f);}
            CheckThat(attack+" preserves crouch and walking",motor.BodyHeight<1.01f&&motor.Velocity.z>0&&combat.CurrentAttack==attack,"height="+motor.BodyHeight+", velocity="+motor.Velocity);
            Advance(combat,MeleeCombat.Duration(attack)+.04f);
            for(int i=0;i<30;i++)motor.Simulate(default,.015f);
            yield return new WaitForSeconds(.35f);
            CheckThat(attack+" resumes idle after moving recovery",!combat.IsAttacking&&driver.CurrentHandState=="Idle"&&driver.CurrentBodyState=="Idle"&&!driver.kickModel.gameObject.activeSelf,"body="+driver.CurrentBodyState+", hands="+driver.CurrentHandState);
        }
        ControlBindings.Reload();temporaryBindings=false;
    }
    static bool Bound(GameAction action,bool pressed,KeyCode key)
    {
        bool Match(KeyCode candidate)=>candidate==key;
        return ControlBindings.Evaluate(action,pressed,Match,Match,0);
    }
    static void Advance(MeleeCombat combat,float seconds)
    {
        while(seconds>.00001f){float step=Mathf.Min(.01f,seconds);combat.SimulateAttack(step);seconds-=step;}
    }
    Frame Capture(CharacterMotionDriver driver,MeleeCombat combat,MeleeAttack attack,string phase,float started)
    {
        string view=driver.thirdPerson?"Body":"FP";
        var frame=new Frame{path="Previews/Push_"+view+"_"+attack+"_"+phase+".png",view=view,attack=attack.ToString(),phase=phase,
            elapsedSinceStart=Time.time-started,combatElapsed=combat.Elapsed,duration=attack==MeleeAttack.None?0:MeleeCombat.Duration(attack),
            contactStart=attack==MeleeAttack.None?0:MeleeCombat.ContactStart(attack),contactEnd=attack==MeleeAttack.None?0:MeleeCombat.ContactEnd(attack),
            bodyState=driver.CurrentBodyState,handState=driver.CurrentHandState,captureMode="LiveRuntimePoseCameraRender"};
        RenderRuntimeFrame(driver.worldCamera,driver.thirdPerson?null:driver.handCamera,frame.path);
        if(driver.thirdPerson) {
            float sign=attack==MeleeAttack.PushLeft?-1:1;
            detailCamera.transform.position=driver.bodyModel.TransformPoint(new Vector3(sign*2.1f,1.50f,1.35f));
            detailCamera.transform.LookAt(driver.bodyModel.TransformPoint(new Vector3(0,1.10f,.20f)));
            frame.detailPath="Previews/Push_BodySide_"+attack+"_"+phase+".png";
            RenderRuntimeFrame(detailCamera,null,frame.detailPath);
        }
        frame.kickVisible=driver.kickModel&&driver.kickModel.gameObject.activeInHierarchy;
        frame.bodyRightFoot=driver.bodyModel.InverseTransformPoint(bodyBones["RightFoot"].position);
        frame.bodyLeftFoot=driver.bodyModel.InverseTransformPoint(bodyBones["LeftFoot"].position);
        frame.leftWrist=driver.handsModel.InverseTransformPoint(handBones["L_Wrist"].position);
        frame.rightWrist=driver.handsModel.InverseTransformPoint(handBones["R_Wrist"].position);
        if(attack==MeleeAttack.PushLeft||attack==MeleeAttack.PushRight) {
            bool left=attack==MeleeAttack.PushLeft;
            frame.bodyPush=Measure(driver,false,left);frame.firstPersonPush=Measure(driver,true,left);
        }
        if(frame.kickVisible) {
            var foot=driver.kickModel.GetComponentsInChildren<Transform>().First(t=>t.name=="RightFoot");
            frame.firstPersonRightFoot=driver.kickModel.InverseTransformPoint(foot.position);
            frame.firstPersonFootViewport=driver.handCamera.WorldToViewportPoint(foot.position);
        }
        manifest.frames.Add(frame);return frame;
    }
    Pose Measure(CharacterMotionDriver driver,bool hands,bool left)
    {
        var root=hands?driver.handsModel:driver.bodyModel;var bones=hands?handBones:bodyBones;
        string side=left?"L":"R",bodySide=left?"Left":"Right";
        var wrist=bones[hands?side+"_Wrist":bodySide+"Hand"];
        var offhand=bones[hands?(left?"R":"L")+"_Wrist":(left?"Right":"Left")+"Hand"];
        string prefix="HandLoRA_Bip01_"+side+"_Finger";
        var index=bones[hands?side+"_Index1":prefix+"1"];var little=bones[hands?side+"_Little1":prefix+"4"];
        Vector3 knuckles=(index.position+little.position)*.5f;
        Vector3 palm=(wrist.position+knuckles)*.5f;
        Vector3 normal=Vector3.Cross(knuckles-wrist.position,index.position-little.position).normalized*(left?-1:1);
        Vector3 elbow=hands?(left?leftForearm:rightForearm).End():bones[bodySide+"ForeArm"].position;
        var pose=new Pose{elbowSource=hands?"Mean actual skinned distal forearm vertices (wrist-pivot rig has no elbow bone)":"ForeArm joint",
            wrist=root.InverseTransformPoint(wrist.position),palm=root.InverseTransformPoint(palm),elbow=root.InverseTransformPoint(elbow),
            offhand=root.InverseTransformPoint(offhand.position),palmNormal=root.InverseTransformDirection(normal),
            palmViewport=(hands?driver.handCamera:driver.worldCamera).WorldToViewportPoint(palm),minimumFingerStraightness=1};
        pose.palmAheadOfElbow=pose.palm.z-pose.elbow.z;pose.wristAheadOfElbow=pose.wrist.z-pose.elbow.z;
        pose.forearmForward=(pose.wrist-pose.elbow).normalized.z;pose.elbowLateralOffset=Mathf.Abs(pose.wrist.x-pose.elbow.x);
        pose.palmFacing=pose.palmNormal.z;
        if(!hands) {
            var shoulder=bones[bodySide+"Arm"].position;pose.shoulder=root.InverseTransformPoint(shoulder);
            pose.extensionRatio=Vector3.Distance(shoulder,wrist.position)/(Vector3.Distance(shoulder,elbow)+Vector3.Distance(elbow,wrist.position));
        }
        for(int digit=0;digit<4;digit++) {
            string name=hands?side+"_"+Fingers[digit]:prefix+(digit+1);
            var a=bones[hands?name+"1":name];var b=bones[hands?name+"2":name+"1"];var c=bones[hands?name+"3":name+"2"];
            float straightness=Vector3.Distance(a.position,c.position)/(Vector3.Distance(a.position,b.position)+Vector3.Distance(b.position,c.position));
            pose.minimumFingerStraightness=Mathf.Min(pose.minimumFingerStraightness,straightness);
            var tip=(hands?driver.handCamera:driver.worldCamera).WorldToViewportPoint(c.position);
            if(digit==0||tip.y>pose.fingerTopViewport.y)pose.fingerTopViewport=tip;
        }
        return pose;
    }
    void CheckContact(Frame frame,Frame anticipation)
    {
        string prefix=frame.view+" "+frame.attack+" ";var body=frame.bodyPush;var fp=frame.firstPersonPush;
        CheckThat(prefix+"uses the live contact state",frame.bodyState==frame.attack&&frame.handState==frame.attack&&frame.combatElapsed>=frame.contactStart&&frame.combatElapsed<=frame.contactEnd,"combatElapsed="+frame.combatElapsed);
        CheckThat(prefix+"body palm leads elbow",body.palmAheadOfElbow>.18f&&body.wristAheadOfElbow>.16f&&body.forearmForward>.80f&&body.elbowLateralOffset<.11f,"palmLead="+body.palmAheadOfElbow+", forearmForward="+body.forearmForward+", lateral="+body.elbowLateralOffset);
        CheckThat(prefix+"elbow extends forward",body.extensionRatio>.93f&&body.extensionRatio-anticipation.bodyPush.extensionRatio>.12f&&body.wrist.z-anticipation.bodyPush.wrist.z>.15f,"reachRatio="+body.extensionRatio+", extensionGain="+(body.extensionRatio-anticipation.bodyPush.extensionRatio)+", wristTravel="+(body.wrist.z-anticipation.bodyPush.wrist.z));
        CheckThat(prefix+"FP palm leads skinned forearm",fp.palmAheadOfElbow>.16f&&fp.wristAheadOfElbow>.16f&&fp.forearmForward>.80f&&fp.wrist.z-anticipation.firstPersonPush.wrist.z>.25f,"palmLead="+fp.palmAheadOfElbow+", forearmForward="+fp.forearmForward+", wristTravel="+(fp.wrist.z-anticipation.firstPersonPush.wrist.z));
        CheckThat(prefix+"open palms face target",body.palmFacing>.85f&&fp.palmFacing>.85f&&body.minimumFingerStraightness>.98f&&fp.minimumFingerStraightness>.98f,"palmFacing body/FP="+body.palmFacing+"/"+fp.palmFacing+", fingerStraightness="+body.minimumFingerStraightness+"/"+fp.minimumFingerStraightness);
        CheckThat(prefix+"offhand stays low",body.offhand.y<1.12f&&fp.offhand.y<-.25f&&fp.offhand.z<.44f,"body="+body.offhand+", FP="+fp.offhand);
        CheckThat(prefix+"FP palm stays below clear reticle",fp.palmViewport.z>0&&fp.palmViewport.x>.12f&&fp.palmViewport.x<.88f&&fp.palmViewport.y>.08f&&fp.fingerTopViewport.y<.50f,"palmViewport="+fp.palmViewport+", topFinger="+fp.fingerTopViewport);
    }
    void CheckKickContact(Frame frame)
    {
        string prefix=frame.view+" kick ";
        CheckThat(prefix+"uses the live contact state",frame.bodyState=="Kick"&&frame.handState=="Kick"&&frame.combatElapsed>=frame.contactStart&&frame.combatElapsed<=frame.contactEnd,"combatElapsed="+frame.combatElapsed);
        CheckThat(prefix+"extends right foot and keeps support foot grounded",frame.bodyRightFoot.z>.70f&&frame.bodyRightFoot.y>.70f&&frame.bodyLeftFoot.y<.2f,"right="+frame.bodyRightFoot+", left="+frame.bodyLeftFoot);
        CheckThat(prefix+"keeps hands braced rather than shoving",frame.leftWrist.z<.5f&&frame.rightWrist.z<.5f,"left="+frame.leftWrist+", right="+frame.rightWrist);
        var viewport=frame.firstPersonFootViewport;
        bool visible=frame.view=="FP";
        CheckThat(prefix+"preserves first-person leg visibility",frame.kickVisible==visible&&(!visible||frame.firstPersonRightFoot.z>.70f&&frame.firstPersonRightFoot.y>.70f&&viewport.z>0&&viewport.x>0&&viewport.x<1&&viewport.y>0&&viewport.y<1),"visible="+frame.kickVisible+", viewport="+viewport);
    }
    void CheckThat(string name,bool passed,string detail)
    {
        manifest.checks.Add(new Check{name=name,passed=passed,detail=detail});
        if(!passed)Debug.LogError("PUSH_PREVIEW_CHECK_FAILED "+name+": "+detail);
    }
    static void RenderRuntimeFrame(Camera camera,Camera overlay,string path)
    {
        // Render the sampled runtime pose in both graphical and batch editors. This
        // does not depend on an active Game tab or asynchronous screenshot flushing.
        var skins=new List<(SkinnedMeshRenderer skin,GameObject visual,Mesh mesh)>();
        var target=new RenderTexture(1200,1000,24);var active=RenderTexture.active;
        var cameraTarget=camera.targetTexture;var overlayTarget=overlay?overlay.targetTexture:null;
        Texture2D image=null;
        try {
            foreach(var skin in FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None)) {
                if(!skin.enabled)continue;
                var mesh=new Mesh();skin.BakeMesh(mesh,true);
                var visual=new GameObject("Push preview skin",typeof(MeshFilter),typeof(MeshRenderer));
                visual.transform.SetParent(skin.transform,false);visual.layer=skin.gameObject.layer;
                visual.GetComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=visual.GetComponent<MeshRenderer>();var materials=skin.sharedMaterials;renderer.sharedMaterials=materials;
                // Shirt colors are per-renderer/per-slot, not stored in shared materials.
                var properties=new MaterialPropertyBlock();skin.GetPropertyBlock(properties);renderer.SetPropertyBlock(properties);
                for(int slot=0;slot<materials.Length;slot++){skin.GetPropertyBlock(properties,slot);renderer.SetPropertyBlock(properties,slot);}
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
    void Finish(Exception exception)
    {
        if(finished)return;finished=true;
        if(temporaryBindings){ControlBindings.Reload();temporaryBindings=false;}
        manifest.error=exception?.ToString();manifest.completedUtc=DateTime.UtcNow.ToString("O");
        manifest.complete=exception==null;manifest.passed=manifest.checks.Count(c=>c.passed);manifest.failed=manifest.checks.Count-manifest.passed;
        manifest.success=manifest.complete&&manifest.checks.Count>0&&manifest.failed==0;
        Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/push-preview.json",JsonUtility.ToJson(manifest,true));
        if(exception!=null)Debug.LogException(exception);
        Debug.Log("PUSH_PREVIEW_COMPLETE success="+manifest.success+" TestResults/push-preview.json");
        Time.timeScale=1;
        // ExecuteMethod automation must also terminate the graphical editor.
        EditorApplication.Exit(manifest.success?0:1);
    }
    void OnDestroy()
    {
        if(instance!=this)return;
        if(temporaryBindings){ControlBindings.Reload();temporaryBindings=false;}
        instance=null;leftForearm?.Dispose();rightForearm?.Dispose();
        if(detailCamera)Destroy(detailCamera.gameObject);Time.timeScale=1;
    }
}
#endif
