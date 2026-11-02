#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalliGalli.Movement;
using UnityEditor;
using UnityEngine;

public sealed class LassoValidationRunner:MonoBehaviour
{
    [Serializable]class Check{public string name,detail;public bool passed;}
    [Serializable]class Report{public int passed,failed;public List<Check> checks;}
    readonly List<Check> checks=new List<Check>();
    CourseSession session;CourseParticipant[] players;GameObject floor;
    CourseParticipant A=>players[0];CourseParticipant B=>players[1];
    IEnumerator Start()
    {
        var routine=Validate();while(true){bool more;object value=null;try{more=routine.MoveNext();if(more)value=routine.Current;}catch(Exception e){Test("Validation completed",()=>throw e);break;}if(!more)break;yield return value;}
        Time.timeScale=1;Save();Debug.Log("LASSO_VALIDATION_RESULT "+checks.Count(c=>c.passed)+"/"+checks.Count);EditorApplication.isPlaying=false;
    }
    void Setup()
    {
        session=FindAnyObjectByType<CourseSession>();session.cardGame.botEnabled=false;session.cardGame.ReadInput=false;session.cardGame.shuffleOnReset=false;
        session.roster.StartNewGame(6);players=session.participants;
        foreach(var p in players){var bot=p.GetComponent<CourseRival>();if(bot)bot.enabled=false;var view=p.GetComponent<SourcePlayerController>();if(view){view.enabled=false;view.SetCursorCaptured(false);}p.Combat.readInput=false;p.Lasso.SimulateAutomatically=false;}
        FindAnyObjectByType<CourseHUD>().ReadInput=false;
        floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Lasso validation floor";floor.transform.position=new Vector3(0,29.5f,0);floor.transform.localScale=new Vector3(50,1,50);
    }
    void Begin()
    {
        session.cardGame.enabled=false;session.ResetPractice();foreach(var p in players)p.Stand();
        for(int i=0;i<players.Length;i++)Place(players[i],new Vector3(-18+i*6,30.025f,-12));
        Place(A,new Vector3(0,30.025f,-4));Place(B,new Vector3(0,30.025f,4));Physics.SyncTransforms();
    }
    static void Place(CourseParticipant p,Vector3 position){p.Motor.Teleport(position);p.Motor.Simulate(default,.02f);Physics.SyncTransforms();}
    static void Ticks(CourseParticipant p,int count,MovementCommand input=default){for(int i=0;i<count;i++)p.Motor.Simulate(input,.02f);Physics.SyncTransforms();}
    static bool Throw(CourseParticipant a,CourseParticipant b)=>a.Lasso.TryThrow((LassoTool.Anchor(b)-a.Motor.CurrentEyePosition).normalized);
    void Attach(){Require(Throw(A,B),"throw rejected");A.Lasso.Simulate(.4f);Require(A.Lasso.Target==B,"target not latched: "+A.Lasso.Phase);}
    IEnumerator Validate()
    {
        yield return null;yield return null;Setup();yield return null;
        Test("Three distinct VARCO rope clips load",()=>{
            var sound=A.GetComponent<LassoAudio>();Require(sound&&sound.ClipsReady,"missing VARCO sounds");
            var clips=new[]{LassoSound.Throw,LassoSound.Latch,LassoSound.Pull}.Select(k=>sound.Source(k).clip).ToArray();
            Require(clips.Distinct().Count()==3&&clips.All(c=>c.length>.08f&&c.length<3&&c.samples>0),"invalid audio clips");
        });
        Test("Seated players cannot use a rope",()=>Require(players.All(p=>!p.Lasso.TryUse(Vector3.forward)&&p.Lasso.CooldownRemaining==0),"seated cast"));
        A.Stand();Test("Wrong standing cannot spend cooldown",()=>Require(session.MustReturn(A)&&!A.Lasso.TryUse(Vector3.forward)&&A.Lasso.CooldownRemaining==0,"penalty cast"));
        Begin();Test("Throw starts five second cooldown immediately",()=>Require(Throw(A,B)&&A.Lasso.CooldownRemaining==5&&A.Lasso.Phase==LassoPhase.Flying,"cast state"));
        A.Lasso.Simulate(.05f);Test("The projectile travels instead of instantly hitting",()=>Require(A.Lasso.Target==null&&Mathf.Abs(Vector3.Distance(A.Lasso.Tip,A.Lasso.CastOrigin)-1.4f)<.001f,"instant hit"));
        A.transform.rotation=Quaternion.Euler(0,90,0);A.Lasso.Simulate(.35f);
        Test("Flight stays straight after the caster turns",()=>Require(A.Lasso.Target==B,"homing / rotated flight"));
        var before=B.transform.position;Ticks(B,8,new MovementCommand{move=Vector2.right});
        Test("Latched victim can still walk",()=>Require(B.transform.position.x>before.x+.1f&&!B.Motor.Stunned&&!B.Motor.Staggered,"movement lock"));
        Ticks(B,1,new MovementCommand{jumpPressed=true,jumpHeld=true});
        Test("Latched victim can still jump",()=>Require(B.Motor.Velocity.y>0&&!B.Motor.Grounded,"jump lock"));
        var velocity=B.Motor.Velocity;
        Test("Second press uses the swing direction and retains existing momentum",()=>Require(A.Lasso.TryPull(Vector3.right)&&Vector3.Distance(B.Motor.Velocity,velocity+Vector3.right*16+Vector3.up*3.5f)<.001f&&A.Lasso.Phase==LassoPhase.Idle,"pull impulse"));
        Test("The same connection cannot pull twice",()=>Require(!A.Lasso.TryPull(Vector3.right),"duplicate impulse"));
        Test("A pulled rope cannot immediately cast again",()=>Require(!A.Lasso.TryThrow(Vector3.forward),"cooldown bypass"));
        Begin();Place(A,new Vector3(0,30.025f,4));Place(B,new Vector3(0,30.025f,-4));Attach();
        Test("Targets behind the caster can be hit",()=>Require(A.Lasso.Target==B,"rear target"));
        velocity=B.Motor.Velocity;A.Lasso.TryPull(Vector3.up);
        Test("Upward aim launches the victim upward",()=>Require(B.Motor.Velocity.y>14&&!B.Motor.Grounded,"upward fling"));
        float vy=B.Motor.Velocity.y;Ticks(B,10);Test("Gravity continues after the pull",()=>Require(B.Motor.Velocity.y<vy-3.5f,"gravity lost"));
        Begin();Place(A,new Vector3(-1,30.025f,0));Place(B,new Vector3(0,30.025f,2));Attach();A.Lasso.TryPull(Vector3.back);
        for(int i=0;i<18;i++){Ticks(A,1,new MovementCommand{move=Vector2.up});Ticks(B,1,new MovementCommand{move=Vector2.up});}
        Test("A pull can reverse order on a flat platform without a fall",()=>Require(A.transform.position.z>B.transform.position.z&&A.Falls==0&&B.Falls==0,"positions "+A.transform.position+" / "+B.transform.position));
        Begin();var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=new Vector3(0,31.5f,0);wall.transform.localScale=new Vector3(4,3,.25f);Physics.SyncTransforms();
        Throw(A,B);A.Lasso.Simulate(.5f);
        Test("A wall blocks the projectile and still consumes cooldown",()=>Require(!A.Lasso.ConnectionActive&&A.Lasso.CooldownRemaining>4&&!B.Motor.Stunned,"wall penetration"));
        wall.SetActive(false);Begin();Attach();wall.SetActive(true);Physics.SyncTransforms();A.Lasso.Simulate(.02f);
        Test("A wall crossing an attached rope breaks it",()=>Require(!A.Lasso.ConnectionActive&&!A.Lasso.TryPull(Vector3.forward),"wall tether"));
        Destroy(wall);yield return null;
        Begin();Place(B,new Vector3(0,30.025f,9));Throw(A,B);A.Lasso.Simulate(.5f);
        Test("Targets farther than twelve metres are not reached",()=>Require(!A.Lasso.ConnectionActive,"range"));
        Begin();Attach();Place(B,new Vector3(0,30.025f,14));A.Lasso.Simulate(.02f);
        Test("Moving beyond sixteen metres breaks the connection",()=>Require(!A.Lasso.ConnectionActive,"stretch range"));
        Begin();A.Lasso.TryThrow(Vector3.up);A.Lasso.Simulate(.5f);A.Lasso.Simulate(4.49f);
        Test("A miss still cannot be recast at 4.99 seconds",()=>Require(!A.Lasso.TryThrow(Vector3.up),"early cooldown"));
        A.Lasso.Simulate(.011f);Test("A miss can be recast after five seconds",()=>Require(A.Lasso.TryThrow(Vector3.up),"cooldown expiry"));
        int casts=A.Lasso.Casts;for(int i=0;i<4;i++){A.Lasso.Simulate(5.01f);Require(A.Lasso.TryThrow(Vector3.up),"repeat throw");}
        Test("Repeated casts have no ammunition limit",()=>Require(A.Lasso.Casts==casts+4,"ammo limit"));
        Begin();Attach();A.Motor.ApplyStun(2,2);A.Lasso.Simulate(.02f);
        Test("A stunned caster releases the rope and cannot pull",()=>Require(!A.Lasso.ConnectionActive&&!A.Lasso.TryUse(Vector3.forward),"stunned rope"));
        Begin();Attach();B.Motor.ApplyStun(2,2);A.Lasso.TryPull(Vector3.back);Ticks(B,4);
        Test("A stunned victim can still be flung with momentum",()=>Require(B.Motor.Stunned&&B.Motor.Velocity.z<-11&&!B.Motor.Grounded,"stunned victim impulse"));
        Begin();Attach();B.BeginFall();Test("Victim falling immediately releases the rope",()=>Require(!A.Lasso.ConnectionActive&&A.Lasso.CooldownRemaining>0,"fall tether"));yield return new WaitForSeconds(.8f);
        Begin();Attach();A.BeginFall();Test("Caster falling releases without refunding cooldown",()=>Require(!A.Lasso.ConnectionActive&&A.Lasso.CooldownRemaining>0,"caster fall"));yield return new WaitForSeconds(.8f);
        Begin();Attach();A.Sit();A.Stand();Test("Sitting during a race does not reset cooldown",()=>Require(!A.Lasso.ConnectionActive&&A.Lasso.CooldownRemaining>0,"seat exploit"));
        foreach(var p in players)p.Sit();Test("Everyone seated clears rope and cooldown",()=>Require(players.All(p=>!p.Lasso.ConnectionActive&&p.Lasso.CooldownRemaining==0),"race cleanup"));
        Begin();Attach();var state=A.Lasso.CaptureNetworkState();var mirror=players[2];velocity=B.Motor.Velocity;mirror.Lasso.ApplyNetwork(state);mirror.Lasso.ApplyNetwork(state);
        Test("Snapshots restore target and cooldown without duplicate impulses",()=>Require(mirror.Lasso.Target==B&&mirror.Lasso.Phase==LassoPhase.Latched&&mirror.Lasso.CooldownRemaining==state.cooldown&&B.Motor.Velocity==velocity,"snapshot"));
        Begin();Attach();A.Combat.TryPush();Test("Switching to melee releases the tether",()=>Require(!A.Lasso.ConnectionActive&&A.Combat.IsAttacking,"melee rope"));
        Begin();A.GetComponent<SourcePlayerController>().ResetView(0,0);Test("Q input routes to the lasso",()=>Require(A.Combat.ProcessLassoInput()&&!A.Combat.IsAttacking,"Q routing"));
        Begin();Attach();session.ResetPractice();Test("New game clears all live connections",()=>Require(players.All(p=>!p.Lasso.ConnectionActive),"restart tether"));
        Begin();Attach();var pullStart=B.transform.position;
        Test("Default pull travels toward caster, not camera forward",()=>Require(A.Lasso.TryPull(Vector3.zero)&&A.Lasso.LastImpulse.z< -11,"wrong default pull"));
        Begin();Attach();B.Motor.AddImpulse(Vector3.forward*22);A.Lasso.TryPull(Vector3.zero);
        Test("Default pull overcomes fast movement away from caster",()=>Require(B.Motor.Velocity.z< -11,"opposing surf momentum wins"));
        Begin();Attach();pullStart=B.transform.position;A.Lasso.TryPull(Vector3.zero);Ticks(B,15);
        float unopposedDistance=pullStart.z-B.transform.position.z;
        Test("Default pull stays between 4.4 and 5.2 metres in 0.3 seconds",()=>Require(unopposedDistance>=4.4f&&unopposedDistance<=5.2f,"distance="+unopposedDistance));
        checks[checks.Count-1].detail="distance="+unopposedDistance.ToString("F3")+"m in 0.3s";
        Begin();Attach();Ticks(B,10,new MovementCommand{move=Vector2.up});pullStart=B.transform.position;A.Lasso.TryPull(Vector3.zero);Ticks(B,18,new MovementCommand{move=Vector2.up});
        float opposedDistance=pullStart.z-B.transform.position.z;
        Test("Opposing movement limits the pull to 1.8-3 metres without cancelling it",()=>Require(opposedDistance>=1.8f&&opposedDistance<=3&&!B.Motor.Staggered&&!B.Motor.Stunned,"distance="+opposedDistance+" velocity="+B.Motor.Velocity));
        checks[checks.Count-1].detail="distance="+opposedDistance.ToString("F3")+"m in 0.36s; movement remains enabled";
        Begin();Attach();var catchWall=GameObject.CreatePrimitive(PrimitiveType.Cube);catchWall.name="Strong pull collision check";catchWall.transform.position=new Vector3(3,33,4);catchWall.transform.localScale=new Vector3(.3f,6,5);Physics.SyncTransforms();
        A.Lasso.TryPull(Vector3.right);Ticks(B,24);
        Test("Sideways fling still collides with a solid wall",()=>Require(B.transform.position.x>2&&B.transform.position.x<2.7f,"position="+B.transform.position));
        Destroy(catchWall);yield return null;yield return null;
        Begin();Attach();var gestureControl=A.GetComponent<SourcePlayerController>();gestureControl.ResetView(0,0);gestureControl.ApplyLook(new Vector2(120,0));
        Test("Sideways mouse flick drives the pull",()=>Require(A.Combat.ProcessLassoInput()&&A.Lasso.LastImpulse.x>10,"side gesture"));
        Begin();Attach();gestureControl.ResetView(90,0);gestureControl.ApplyLook(new Vector2(0,120));
        Test("Upward flick launches upward regardless of facing",()=>Require(A.Combat.ProcessLassoInput()&&A.Lasso.LastImpulse.y>14,"up gesture"));
        Begin();Attach();gestureControl.ResetView(0,0);gestureControl.ApplyLook(new Vector2(120,0));gestureControl.ApplyLook(Vector2.zero);
        Test("Stopping the mouse before pulling restores the default inward pull",()=>Require(A.Combat.ProcessLassoInput()&&A.Lasso.LastImpulse.z< -15&&Mathf.Abs(A.Lasso.LastImpulse.x)<.01f,"stale swing direction="+A.Lasso.LastImpulse));
        Begin();Attach();gestureControl.ResetView(0,0);gestureControl.ApplyLook(new Vector2(120,0));yield return null;
        Test("An earlier frame's swing cannot steer a later pull",()=>Require(A.Combat.ProcessLassoInput()&&A.Lasso.LastImpulse.z< -15,"previous-frame swing used"));
        Begin();Attach();gestureControl.ResetView(0,0);gestureControl.ApplyLook(new Vector2(.01f,0));
        Test("Tiny aim adjustments keep the pull directed toward the caster",()=>Require(A.Combat.ProcessLassoInput()&&A.Lasso.LastImpulse.z< -15,"mouse jitter flung sideways"));
        Begin();Attach();gestureControl.ResetView(0,0);gestureControl.ApplyLook(new Vector2(120,0));gestureControl.ApplyLook(new Vector2(-120,0));
        Test("Changing swing direction uses the current swing instead of old accumulated input",()=>Require(A.Combat.ProcessLassoInput()&&A.Lasso.LastImpulse.x< -15,"old swing won="+A.Lasso.LastImpulse));
        Begin();Place(B,new Vector3(.48f,30.025f,4));A.Lasso.TryThrow(Vector3.forward);A.Lasso.Simulate(.4f);
        Test("A stationary target near the reticle edge is forgiving",()=>Require(A.Lasso.Target==B,"edge hit missed"));
        Begin();Place(B,new Vector3(0,30.025f,-3.4f));A.Lasso.TryThrow(Vector3.forward);A.Lasso.Simulate(.02f);
        Test("Point blank overlap catches the target",()=>Require(A.Lasso.Target==B,"initial overlap missed"));
        Begin();Place(B,new Vector3(-1,30.025f,4));A.Lasso.TryThrow(Vector3.forward);
        string leadTrace="";for(int i=0;i<20;i++){Ticks(B,1,new MovementCommand{move=Vector2.right});A.Lasso.Simulate(.02f);leadTrace+=i+":"+B.transform.position+" tip="+A.Lasso.Tip+" "+A.Lasso.Phase+"; ";}
        Test("Leading a moving opponent intersects its future position",()=>Require(A.Lasso.Target==B,"lead shot missed "+leadTrace));
        Begin();A.Lasso.TryThrow(Vector3.forward);
        string dodgeTrace="";for(int i=0;i<25;i++){Ticks(B,1,new MovementCommand{move=Vector2.right});A.Lasso.Simulate(.02f);dodgeTrace+=i+":"+B.transform.position+" "+A.Lasso.Phase+"; ";}
        Test("Sidestepping out of the projectile path dodges it",()=>Require(A.Lasso.Target==null&&A.Lasso.Phase==LassoPhase.Idle,"undodgeable / homing "+dodgeTrace));
        Begin();Attach();var motion=A.GetComponent<CharacterMotionDriver>().motions;
        Test("Original first and third person rope clips are installed",()=>Require(new[]{"LassoHold","LassoThrow","LassoPull"}.All(n=>motion.handClips.Any(c=>c.name==n)&&motion.bodyClips.Any(c=>c.name==n)),"missing clips"));
        Begin();var audio=A.GetComponent<LassoAudio>();int throws=audio.Played(LassoSound.Throw),hits=audio.Played(LassoSound.Latch),pulls=audio.Played(LassoSound.Pull);
        Throw(A,B);
        Test("Throw audio plays once on accepted cast",()=>Require(audio.Played(LassoSound.Throw)==throws+1&&audio.Played(LassoSound.Latch)==hits,"throw sound timing"));
        A.Lasso.Simulate(.4f);
        Test("Latch audio plays only when the rope reaches the victim",()=>Require(A.Lasso.Target==B&&audio.Played(LassoSound.Latch)==hits+1&&audio.Source(LassoSound.Latch).spatialBlend==1,"latch sound timing / position"));
        A.Lasso.TryPull(Vector3.right);
        Test("Pull audio plays once on the second action",()=>Require(audio.Played(LassoSound.Pull)==pulls+1&&audio.Source(LassoSound.Pull).spatialBlend==0,"pull sound timing"));
        A.Lasso.TryUse(Vector3.forward);A.Lasso.TryPull(Vector3.right);A.Lasso.ApplyNetwork(A.Lasso.CaptureNetworkState());
        audio.Play(LassoSound.Pull,A.Lasso.Pulls,A.Motor.CurrentEyePosition);
        Test("Cooldown, duplicate events and pose reconciliation cannot replay sounds",()=>Require(audio.Played(LassoSound.Throw)==throws+1&&audio.Played(LassoSound.Latch)==hits+1&&audio.Played(LassoSound.Pull)==pulls+1,"duplicate sound"));
        Begin();hits=audio.Played(LassoSound.Latch);pulls=audio.Played(LassoSound.Pull);A.Lasso.TryThrow(Vector3.up);A.Lasso.Simulate(.5f);A.Lasso.TryPull(Vector3.forward);
        Test("Missed ropes never play latch or pull audio",()=>Require(audio.Played(LassoSound.Latch)==hits&&audio.Played(LassoSound.Pull)==pulls,"false hit sound"));
        Begin();Throw(B,A);B.Lasso.Simulate(.4f);
        Test("The local victim hears latch audio clearly",()=>Require(B.GetComponent<LassoAudio>().Source(LassoSound.Latch).spatialBlend==0,"victim feedback"));
        session.ResetPractice();
        Test("Round reset stops every rope sound",()=>Require(players.All(p=>new[]{LassoSound.Throw,LassoSound.Latch,LassoSound.Pull}.All(k=>!p.GetComponent<LassoAudio>().Source(k).isPlaying)),"stale audio"));
        // Capture first-person assembly using real models, lighting and HUD.
        Begin();Destroy(floor);Place(A,new Vector3(0,.375f,5.7f));Place(B,new Vector3(-1.3f,.575f,12.3f));A.Lasso.EndRace();Attach();
        var control=A.GetComponent<SourcePlayerController>();var aim=Quaternion.LookRotation(LassoTool.Anchor(B)-A.Motor.CurrentEyePosition).eulerAngles;
        control.ResetView(aim.y,Mathf.DeltaAngle(0,aim.x));control.SetCursorCaptured(true);
        for(float until=Time.time+.65f;Time.time<until;){control.UpdateView();yield return null;}
        Directory.CreateDirectory("Previews");yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Previews/Lasso_FirstPerson.png");yield return new WaitForEndOfFrame();
        A.Lasso.TryPull((Vector3.right+Vector3.up*.2f).normalized);yield return new WaitForSeconds(.12f);yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Previews/Lasso_Pull.png");yield return new WaitForEndOfFrame();yield return null;
        // Preview the incoming-rope warning from the actual victim's camera.
        A.Lasso.EndRace();B.Lasso.EndRace();Place(A,new Vector3(0,.375f,5.7f));Place(B,new Vector3(-1.3f,.575f,12.3f));
        control.ResetView(0,4);control.SetCursorCaptured(true);control.UpdateView();
        Require(Throw(B,A),"victim preview throw rejected");B.Lasso.Simulate(.4f);Require(B.Lasso.Target==A,"victim preview did not latch");
        for(float until=Time.time+.3f;Time.time<until;){control.UpdateView();yield return null;}
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Previews/Lasso_Caught.png");yield return new WaitForEndOfFrame();
        B.Lasso.CancelConnection();yield return null;control.UpdateView();
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Previews/Lasso_Released.png");yield return new WaitForEndOfFrame();
    }
    void Save(){Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/lasso-validation.json",JsonUtility.ToJson(new Report{passed=checks.Count(c=>c.passed),failed=checks.Count(c=>!c.passed),checks=checks},true));}
    void Test(string name,Action action){try{action();checks.Add(new Check{name=name,passed=true,detail="passed"});}catch(Exception e){checks.Add(new Check{name=name,passed=false,detail=e.Message});Debug.LogError("FAIL "+name+": "+e.Message);}Save();}
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
}
#endif
