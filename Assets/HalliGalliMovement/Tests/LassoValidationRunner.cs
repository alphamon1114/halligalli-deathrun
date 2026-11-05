#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalliGalli.Movement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class LassoValidationRunner:MonoBehaviour
{
    const int FixtureSeed=20261105;
    const float Step=.02f;
    [Serializable]class Check{public string name,detail;public bool passed;}
    [Serializable]class Report{public string utc;public int seed,passed,failed;public List<Check> checks;}
    readonly List<Check> checks=new List<Check>();
    CourseSession session;CourseParticipant[] players;GameObject floor;
    [SerializeField]bool exitWhenFinished;
    CourseParticipant A=>players[0];CourseParticipant B=>players[1];
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/HalliGalliMovement/Demo/BellRunCourse.unity");
        new GameObject("Automatic rope regression").AddComponent<LassoValidationRunner>().exitWhenFinished=true;EditorApplication.isPlaying=true;
    }
    IEnumerator Start()
    {
        var routine=Validate();while(true){bool more;object value=null;try{more=routine.MoveNext();if(more)value=routine.Current;}catch(Exception e){Test("Validation completed",()=>throw e);break;}if(!more)break;yield return value;}
        Time.timeScale=1;Save();Debug.Log("LASSO_VALIDATION_RESULT "+checks.Count(c=>c.passed)+"/"+checks.Count);
        if(Application.isBatchMode||exitWhenFinished)EditorApplication.Exit(checks.Any(c=>!c.passed)?1:0);else EditorApplication.isPlaying=false;
    }
    void Setup()
    {
        UnityEngine.Random.InitState(FixtureSeed);CourseNetwork.Instance.StartPractice();
        session=FindAnyObjectByType<CourseSession>();session.cardGame.botEnabled=false;session.cardGame.ReadInput=false;session.cardGame.shuffleOnReset=false;session.cardGame.seed=FixtureSeed;
        session.roster.StartNewGame(6);players=session.participants;
        foreach(var p in players){var bot=p.GetComponent<CourseRival>();if(bot)bot.enabled=false;var view=p.GetComponent<SourcePlayerController>();if(view){view.enabled=false;view.SetCursorCaptured(false);}p.Combat.readInput=false;p.Combat.simulateAutomatically=false;p.Lasso.SimulateAutomatically=false;}
        FindAnyObjectByType<CourseHUD>().ReadInput=false;
        floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Lasso validation floor";floor.transform.position=new Vector3(0,29.5f,0);floor.transform.localScale=new Vector3(60,1,60);
    }
    void Begin()
    {
        session.cardGame.enabled=false;session.ResetPractice();foreach(var p in players)p.Stand();
        for(int i=0;i<players.Length;i++)Place(players[i],new Vector3(-18+i*6,30.025f,-12));
        Place(A,new Vector3(0,30.025f,-4));Place(B,new Vector3(0,30.025f,4));Physics.SyncTransforms();
    }
    static void Place(CourseParticipant p,Vector3 position){p.Motor.Teleport(position);var control=p.GetComponent<SourcePlayerController>();if(control)control.ResetView(0,0);p.Motor.Simulate(default,Step);Physics.SyncTransforms();}
    static void Ticks(CourseParticipant p,int count,MovementCommand input=default){for(int i=0;i<count;i++)p.Motor.Simulate(input,Step);Physics.SyncTransforms();}
    static bool Throw(CourseParticipant a,CourseParticipant b)=>a.Lasso.TryThrow((LassoTool.Anchor(b)-a.Motor.CurrentEyePosition).normalized);
    static void Fly(CourseParticipant caster){for(int i=0;i<60&&caster.Lasso.Phase==LassoPhase.Flying;i++)caster.Lasso.Simulate(.01f);}
    void Attach(){Require(Throw(A,B),"throw rejected");Fly(A);Require(A.Lasso.Target==B&&A.Lasso.Phase==LassoPhase.Latched,"target not hit: "+A.Lasso.Phase);}
    static void FinishPull(CourseParticipant caster){caster.Lasso.Simulate(LassoTool.AutoPullDelay+Step);}
    static Vector3 Inward(CourseParticipant caster,CourseParticipant victim)=>Vector3.ProjectOnPlane(caster.transform.position-victim.transform.position,Vector3.up).normalized;
    void NoPull(string name,Action invalidate)
    {
        Begin();Attach();int pulls=A.Lasso.Pulls;invalidate();var velocity=B.Motor.Velocity;FinishPull(A);
        Test(name,()=>Require(!A.Lasso.ConnectionActive&&A.Lasso.Pulls==pulls&&B.Motor.Velocity==velocity,"invalid connection applied force"));
    }
    IEnumerator Validate()
    {
        yield return null;yield return null;Setup();yield return null;
        Test("Seated players cannot cast or spend cooldown",()=>Require(players.All(p=>!p.Lasso.TryThrow(Vector3.forward)&&p.Lasso.CooldownRemaining==0),"seated cast"));
        A.Stand();Test("Wrong card departure cannot cast",()=>Require(session.MustReturn(A)&&!A.Lasso.TryThrow(Vector3.forward)&&A.Lasso.CooldownRemaining==0,"penalty cast"));
        Begin();var control=A.GetComponent<SourcePlayerController>();control.ResetView(0,0);
        int casts=A.Lasso.Casts,hits=A.Lasso.Hits,pulls=A.Lasso.Pulls;var velocity=B.Motor.Velocity;
        Test("One Q starts a cast and its five second cooldown",()=>Require(A.Combat.ProcessLassoInput()&&A.Lasso.Casts==casts+1&&A.Lasso.CooldownRemaining==5&&!A.Combat.IsAttacking,"Q routing"));
        A.Lasso.Simulate(.05f);
        Test("A distant hit requires projectile travel",()=>Require(A.Lasso.Phase==LassoPhase.Flying&&A.Lasso.Target==null&&A.Lasso.Hits==hits&&B.Motor.Velocity==velocity,"instant hit"));
        control.ApplyLook(new Vector2(120,90));Fly(A);
        Test("Turning the camera does not redirect the projectile",()=>Require(A.Lasso.Target==B&&A.Lasso.Hits==hits+1,"rotated flight"));
        A.Lasso.Simulate(LassoTool.AutoPullDelay*.5f);
        Test("A successful hit briefly winds up without a second press",()=>Require(A.Lasso.Phase==LassoPhase.Latched&&A.Lasso.Pulls==pulls&&B.Motor.Velocity==velocity,"missing windup"));
        control.ApplyLook(new Vector2(-240,-180));var position=B.transform.position;var inward=Inward(A,B);FinishPull(A);
        Test("The same Q automatically applies one horizontal pull toward the caster",()=>Require(A.Lasso.Pulls==pulls+1&&A.Lasso.Phase==LassoPhase.Idle&&Vector3.Distance(B.Motor.Velocity-velocity,inward*LassoTool.PullSpeed)<.001f,"impulse="+A.Lasso.LastImpulse));
        Test("Ground pull preserves vertical velocity and never teleports",()=>Require(B.Motor.Velocity.y==velocity.y&&A.Lasso.LastImpulse.y==0&&B.Motor.Grounded&&B.transform.position==position,"position or vertical motion changed"));
        velocity=B.Motor.Velocity;bool repeat=A.Combat.ProcessLassoInput();A.Lasso.Simulate(.5f);
        Test("Repeated Q and later ticks cannot duplicate the pull",()=>Require(!repeat&&A.Lasso.Casts==casts+1&&A.Lasso.Pulls==pulls+1&&B.Motor.Velocity==velocity,"duplicate force"));

        Begin();Attach();position=B.transform.position;Ticks(B,3,new MovementCommand{move=Vector2.right});
        Test("Victim can move during windup",()=>Require(B.transform.position.x>position.x&&!B.Motor.Stunned&&!B.Motor.Staggered,"movement locked"));
        Ticks(B,1,new MovementCommand{jumpPressed=true,jumpHeld=true});velocity=B.Motor.Velocity;FinishPull(A);
        Test("Victim can jump and keeps that vertical velocity through the pull",()=>Require(velocity.y>0&&B.Motor.Velocity.y==velocity.y&&!B.Motor.Grounded,"jump replaced"));
        Test("Victim can counterattack immediately after the pull",()=>Require(B.Combat.TryPush(),"counterattack rejected"));
        float vy=B.Motor.Velocity.y;Ticks(B,10);
        Test("Gravity continues normally after an airborne pull",()=>Require(B.Motor.Velocity.y<vy,"gravity lost"));
        foreach(float elevation in new[]{-3f,3f})foreach(float vertical in new[]{-4f,5f}){
            Begin();Place(A,new Vector3(0,36,-4));Place(B,new Vector3(0,36+elevation,4));B.Motor.AddImpulse(Vector3.up*vertical);Attach();velocity=B.Motor.Velocity;FinishPull(A);
            Test("Elevation "+elevation+" and vertical momentum "+vertical+" add no lift",()=>Require(B.Motor.Velocity.y==velocity.y&&A.Lasso.LastImpulse.y==0&&Vector3.Dot(B.Motor.Velocity-velocity,Inward(A,B))>0,"velocity="+B.Motor.Velocity));
        }
        Begin();Place(B,new Vector3(0,30.025f,-2.8f));Attach();position=B.transform.position;float gap=Vector3.Distance(A.transform.position,position);FinishPull(A);
        Test("Nearby target is not placed at the caster's feet",()=>Require(B.transform.position==position&&Vector3.Distance(A.transform.position,B.transform.position)==gap,"forced delivery"));
        Ticks(B,1);Test("Nearby target moves through ordinary motor steps",()=>Require(Vector3.Distance(position,B.transform.position)>0&&Vector3.Distance(position,B.transform.position)<gap,"movement snapped"));

        Begin();Attach();position=B.transform.position;FinishPull(A);float highest=position.y;
        for(int i=0;i<25;i++){Ticks(B,1);highest=Mathf.Max(highest,B.transform.position.y);}
        float unopposed=position.z-B.transform.position.z;
        Test("Ground pull produces real travel without forced lift",()=>Require(unopposed>.1f&&highest-position.y<.05f&&B.Motor.Grounded&&B.Falls==0,"distance="+unopposed+"m / 0.50s; rise="+(highest-position.y)));
        checks[checks.Count-1].detail="distance="+unopposed.ToString("F3")+"m / 0.50s; rise="+(highest-position.y).ToString("F3")+"m";
        Begin();Attach();position=B.transform.position;FinishPull(A);Ticks(B,25,new MovementCommand{move=Vector2.up});float opposed=position.z-B.transform.position.z;
        Test("Countersteering reduces travel without locking input",()=>Require(opposed<unopposed-.05f&&B.Motor.Velocity.z>0&&!B.Motor.Stunned&&!B.Motor.Staggered,"distance="+opposed+"; unopposed="+unopposed));
        checks[checks.Count-1].detail="distance="+opposed.ToString("F3")+"m / 0.50s; unopposed="+unopposed.ToString("F3")+"m";
        Begin();Attach();B.Motor.AddImpulse(Vector3.forward*22);FinishPull(A);
        Test("Fast outward momentum still receives an inward pull",()=>Require(B.Motor.Velocity.z<0&&A.Lasso.LastImpulse.y==0,"outward momentum defeated pull"));
        Begin();Attach();FinishPull(A);
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="Post-pull collision wall";wall.transform.position=new Vector3(0,32,3);wall.transform.localScale=new Vector3(6,4,.25f);Physics.SyncTransforms();Ticks(B,25);
        Test("Pulled movement collides with ordinary solid walls",()=>Require(B.transform.position.z<3.9f&&B.transform.position.z>3.125f,"position="+B.transform.position));
        wall.SetActive(false);Destroy(wall);

        Begin();wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=new Vector3(0,31.5f,0);wall.transform.localScale=new Vector3(4,3,.25f);Physics.SyncTransforms();hits=A.Lasso.Hits;pulls=A.Lasso.Pulls;velocity=B.Motor.Velocity;Throw(A,B);Fly(A);FinishPull(A);
        Test("A wall blocks the projectile but still consumes cooldown",()=>Require(!A.Lasso.ConnectionActive&&A.Lasso.CooldownRemaining>0&&A.Lasso.Hits==hits&&A.Lasso.Pulls==pulls&&B.Motor.Velocity==velocity,"wall penetration"));
        wall.SetActive(false);NoPull("A wall entering the windup breaks the connection",()=>{wall.SetActive(true);Physics.SyncTransforms();});wall.SetActive(false);Destroy(wall);
        Begin();Place(B,new Vector3(0,30.025f,10));hits=A.Lasso.Hits;pulls=A.Lasso.Pulls;Throw(A,B);Fly(A);FinishPull(A);
        Test("Targets beyond twelve metres cannot be pulled",()=>Require(!A.Lasso.ConnectionActive&&A.Lasso.Hits==hits&&A.Lasso.Pulls==pulls,"range exceeded"));
        NoPull("Leaving tether range during windup cancels the pull",()=>Place(B,new Vector3(0,30.025f,14)));
        NoPull("Disabling hit eligibility during windup cancels the pull",()=>B.Receiver.canReceiveHits=false);
        NoPull("Seating the target during windup cancels the pull",()=>B.Sit());
        NoPull("Stunning the caster during windup cancels the pull",()=>A.Motor.ApplyStun(2,2));
        NoPull("Disabling caster actions during windup cancels the pull",()=>A.Combat.actionsEnabled=false);
        NoPull("Victim respawn cancels the pending pull without stale force",()=>{B.BeginFall();B.Respawn();});
        NoPull("Caster respawn cancels the pending pull without stale force",()=>{A.BeginFall();A.Respawn();});
        Begin();B.Sit();hits=A.Lasso.Hits;Throw(A,B);Fly(A);FinishPull(A);
        Test("Seated targets are ineligible",()=>Require(A.Lasso.Hits==hits&&!A.Lasso.ConnectionActive,"seated target hit"));
        Begin();B.BeginFall();hits=A.Lasso.Hits;Throw(A,B);Fly(A);FinishPull(A);
        Test("Respawning targets are ineligible",()=>Require(A.Lasso.Hits==hits&&!A.Lasso.ConnectionActive,"respawning target hit"));
        Begin();A.BeginFall();Test("Respawning caster cannot spend cooldown",()=>Require(!A.Lasso.TryThrow(Vector3.forward)&&A.Lasso.CooldownRemaining==0,"respawning cast"));
        Begin();Test("Invalid cast direction does not consume cooldown",()=>Require(!A.Lasso.TryThrow(Vector3.zero)&&!A.Lasso.TryThrow(new Vector3(float.NaN,0,1))&&A.Lasso.CooldownRemaining==0,"invalid aim accepted"));
        Begin();A.Lasso.TryThrow(Vector3.up);A.Lasso.Simulate(.5f);A.Lasso.Simulate(4.49f);
        Test("Miss remains on cooldown at 4.99 seconds",()=>Require(!A.Lasso.TryThrow(Vector3.up),"early cooldown"));
        A.Lasso.Simulate(.011f);Test("Miss can be recast after five seconds",()=>Require(A.Lasso.TryThrow(Vector3.up),"cooldown never expires"));
        casts=A.Lasso.Casts;for(int i=0;i<4;i++){A.Lasso.Simulate(5.01f);Require(A.Lasso.TryThrow(Vector3.up),"repeat throw");}
        Test("Repeated casts have no ammunition limit",()=>Require(A.Lasso.Casts==casts+4,"ammo limit"));
        Begin();Attach();A.Sit();A.Stand();Test("Individual seating cannot refund cooldown",()=>Require(!A.Lasso.ConnectionActive&&A.Lasso.CooldownRemaining>0,"seat exploit"));
        foreach(var p in players)p.Sit();Test("Everyone seated clears ropes and cooldown",()=>Require(players.All(p=>!p.Lasso.ConnectionActive&&p.Lasso.CooldownRemaining==0),"race cleanup"));
        Begin();Attach();var state=A.Lasso.CaptureNetworkState();var mirror=players[2];velocity=B.Motor.Velocity;mirror.Lasso.ApplyNetwork(state);mirror.Lasso.ApplyNetwork(state);
        Test("Repeated windup snapshots display the target without applying force",()=>Require(mirror.Lasso.Target==B&&mirror.Lasso.Phase==LassoPhase.Latched&&mirror.Lasso.CooldownRemaining==state.cooldown&&B.Motor.Velocity==velocity,"snapshot force"));
        FinishPull(A);state=A.Lasso.CaptureNetworkState();velocity=B.Motor.Velocity;mirror.Lasso.ApplyNetwork(state);mirror.Lasso.ApplyNetwork(state);
        Test("Repeated completed snapshots never duplicate the authoritative impulse",()=>Require(mirror.Lasso.Pulls==A.Lasso.Pulls&&!mirror.Lasso.ConnectionActive&&B.Motor.Velocity==velocity,"duplicate snapshot force"));
        Begin();Attach();pulls=A.Lasso.Pulls;A.Combat.TryPush();FinishPull(A);
        Test("Caster switching to melee cancels the pending rope",()=>Require(!A.Lasso.ConnectionActive&&A.Combat.IsAttacking&&A.Lasso.Pulls==pulls,"melee rope"));
        Begin();Attach();session.ResetPractice();Test("New round clears all pending ropes",()=>Require(players.All(p=>!p.Lasso.ConnectionActive&&p.Lasso.CooldownRemaining==0),"restart tether"));

        Begin();Place(B,new Vector3(.48f,30.025f,4));hits=A.Lasso.Hits;A.Lasso.TryThrow(Vector3.forward);Fly(A);FinishPull(A);
        Test("A stationary target near the reticle edge can be caught",()=>Require(A.Lasso.Hits==hits+1,"edge hit missed"));
        Begin();Place(B,new Vector3(0,30.025f,-3.4f));hits=A.Lasso.Hits;A.Lasso.TryThrow(Vector3.forward);Fly(A);FinishPull(A);
        Test("Point blank overlap catches the target",()=>Require(A.Lasso.Hits==hits+1,"overlap missed"));
        Begin();Place(B,new Vector3(-1,30.025f,4));hits=A.Lasso.Hits;A.Lasso.TryThrow(Vector3.forward);
        for(int i=0;i<25&&A.Lasso.Phase==LassoPhase.Flying;i++){Ticks(B,1,new MovementCommand{move=Vector2.right});A.Lasso.Simulate(Step);}
        Test("Leading a moving opponent can intersect its future position",()=>Require(A.Lasso.Hits==hits+1,"lead shot missed"));
        Begin();hits=A.Lasso.Hits;pulls=A.Lasso.Pulls;A.Lasso.TryThrow(Vector3.forward);
        for(int i=0;i<30;i++){Ticks(B,1,new MovementCommand{move=Vector2.right});A.Lasso.Simulate(Step);}
        Test("Sidestepping the projectile path avoids the automatic pull",()=>Require(A.Lasso.Hits==hits&&A.Lasso.Pulls==pulls&&!A.Lasso.ConnectionActive,"undodgeable projectile"));

        Begin();var audio=A.GetComponent<LassoAudio>();int throws=audio.Played(LassoSound.Throw),latches=audio.Played(LassoSound.Latch),sounds=audio.Played(LassoSound.Pull);Throw(A,B);
        Test("Accepted cast plays existing throw audio once",()=>Require(audio.ClipsReady&&audio.Played(LassoSound.Throw)==throws+1&&audio.Played(LassoSound.Latch)==latches,"throw audio"));
        Fly(A);Test("Real hit plays latch audio once before pulling",()=>Require(audio.Played(LassoSound.Latch)==latches+1&&audio.Played(LassoSound.Pull)==sounds,"hit audio"));
        FinishPull(A);Test("Automatic success plays pull audio once",()=>Require(audio.Played(LassoSound.Pull)==sounds+1,"pull audio"));
        A.Combat.ProcessLassoInput();FinishPull(A);A.Lasso.ApplyNetwork(A.Lasso.CaptureNetworkState());audio.Play(LassoSound.Pull,A.Lasso.Pulls,A.Motor.CurrentEyePosition);
        Test("Repeated input and duplicate sound events cannot replay success audio",()=>Require(audio.Played(LassoSound.Throw)==throws+1&&audio.Played(LassoSound.Latch)==latches+1&&audio.Played(LassoSound.Pull)==sounds+1,"duplicate sound"));
        Begin();latches=audio.Played(LassoSound.Latch);sounds=audio.Played(LassoSound.Pull);A.Lasso.TryThrow(Vector3.up);Fly(A);FinishPull(A);
        Test("Miss never plays latch or pull audio",()=>Require(audio.Played(LassoSound.Latch)==latches&&audio.Played(LassoSound.Pull)==sounds,"false hit sound"));
        Begin();Throw(B,A);Fly(B);Test("Local victim receives non-spatial hit feedback",()=>Require(B.GetComponent<LassoAudio>().Source(LassoSound.Latch).spatialBlend==0,"victim feedback"));
        session.ResetPractice();Test("Round reset stops rope audio",()=>Require(players.All(p=>new[]{LassoSound.Throw,LassoSound.Latch,LassoSound.Pull}.All(k=>!p.GetComponent<LassoAudio>().Source(k).isPlaying)),"stale audio"));
        if(Application.isBatchMode||SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)yield break;
        // Freeze only simulation while recording the genuine short windup, then advance it once.
        Begin();floor.SetActive(false);Place(A,new Vector3(0,.375f,5.7f));Place(B,new Vector3(-1.3f,.575f,12.3f));Attach();
        var aim=Quaternion.LookRotation(LassoTool.Anchor(B)-A.Motor.CurrentEyePosition).eulerAngles;
        control.ResetView(aim.y,Mathf.DeltaAngle(0,aim.x));control.SetCursorCaptured(true);control.UpdateView();
        yield return Capture("Lasso_FirstPerson");FinishPull(A);Ticks(B,3);control.UpdateView();yield return Capture("Lasso_Pull");
        Begin();Place(A,new Vector3(0,.375f,5.7f));Place(B,new Vector3(-1.3f,.575f,12.3f));control.ResetView(0,4);control.SetCursorCaptured(true);control.UpdateView();
        Require(Throw(B,A),"incoming preview rejected");Fly(B);Require(B.Lasso.Target==A,"incoming preview missed");yield return Capture("Lasso_Caught");
        FinishPull(B);Ticks(A,3);control.UpdateView();yield return Capture("Lasso_Released");
    }
    static IEnumerator Capture(string name){yield return null;yield return new WaitForEndOfFrame();Directory.CreateDirectory("Previews");var image=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes("Previews/"+name+".png",image.EncodeToPNG());Destroy(image);}
    void Save(){Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/lasso-validation.json",JsonUtility.ToJson(new Report{utc=DateTime.UtcNow.ToString("O"),seed=FixtureSeed,passed=checks.Count(c=>c.passed),failed=checks.Count(c=>!c.passed),checks=checks},true));}
    void Test(string name,Action action){try{action();checks.Add(new Check{name=name,passed=true,detail="passed"});}catch(Exception e){checks.Add(new Check{name=name,passed=false,detail=e.Message});Debug.LogError("FAIL "+name+": "+e.Message);}Save();}
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
}
#endif
