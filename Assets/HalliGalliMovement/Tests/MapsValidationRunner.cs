#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalliGalli.Movement;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class MapsValidationRunner:MonoBehaviour
{
    public bool probeOnly,captureOnly;
    static MapsValidationRunner active;
    void Awake(){if(active&&active!=this){Destroy(gameObject);return;}active=this;}
    void OnDestroy(){if(active==this)active=null;}
    [Serializable]class Check{public string name;public bool passed;public string detail;}
    [Serializable]class Report{public int passed,failed;public List<Check> checks;}
    readonly List<Check> checks=new List<Check>();
    CourseSession session;CourseCardGame game;CourseHUD hud;SourcePlayerController controller;
    float tutorialSeconds,tutorialDistance;
    static float RouteDistance(Vector3 start,Transform[] route){float distance=0;foreach(var node in route){distance+=Vector3.Distance(start,node.position);start=node.position;}return distance;}
    IEnumerator MeasureTutorial()
    {
        game.enabled=false;session.roster.StartNewGame(6);yield return new WaitForFixedUpdate();
        var runner=session.participants[1];var bot=runner.GetComponent<CourseRival>();
        var starts=session.participants.Select(p=>p.standAnchor.position).ToArray();
        foreach(int lane in new[]{0,5}){
            Bots(false);session.ResetPractice();yield return new WaitForFixedUpdate();
            bot.route=session.roster.RouteForSeat(lane);runner.standAnchor.position=starts[lane];runner.retryAnchor.position=starts[lane];
            runner.Stand();bot.enabled=true;float start=Time.time,end=start+20;
            while(!session.BellRung&&Time.time<end)yield return new WaitForFixedUpdate();
            float seconds=Time.time-start,distance=RouteDistance(starts[lane],bot.route);
            tutorialSeconds+=seconds*.5f;tutorialDistance+=distance*.5f;
            lastDetail="seconds="+seconds.ToString("F2")+" distance="+distance.ToString("F2");
            Test("Tutorial reference lane "+(lane+1)+" reaches the bell without a fall",()=>Require(session.BellRinger==runner&&runner.Falls==0,"tutorial reference traversal failed"));
        }
        new CoursePacingReference{tutorialSeconds=tutorialSeconds,tutorialDistance=tutorialDistance}.Save();
        Bots(false);game.enabled=true;
    }
    IEnumerator Start()
    {
        if(captureOnly){yield return CaptureMenus();EditorApplication.isPlaying=false;yield break;}
        DontDestroyOnLoad(gameObject);var routine=Validate();
        while(true){bool more=false;object next=null;try{more=routine.MoveNext();if(more)next=routine.Current;}catch(Exception e){Test("Runner completed",()=>throw e);break;}if(!more)break;yield return next;}
        Time.timeScale=1;int passed=checks.Count(c=>c.passed);Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/maps-validation.json",JsonUtility.ToJson(new Report{passed=passed,failed=checks.Count-passed,checks=checks},true));Debug.Log("MAPS_VALIDATION_RESULT "+passed+"/"+checks.Count);EditorApplication.isPlaying=false;
    }
    IEnumerator CaptureMenus()
    {
        DontDestroyOnLoad(gameObject);yield return null;yield return null;Bind();
        var surf=session.maps.catalog.maps.Single(m=>m.id=="surf");
        foreach(string name in new[]{"Maps_Menu","Surf_Menu"}){
            if(name=="Surf_Menu")yield return Switch(surf,6);
            hud.OpenMapMenu();yield return null;yield return new WaitForEndOfFrame();
            Directory.CreateDirectory("Previews");var shot=ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes("Previews/"+name+".png",shot.EncodeToPNG());Destroy(shot);hud.CloseMapMenu();
        }
        Debug.Log("MAP_MENU_CAPTURE_COMPLETE");
    }
    void Bind()
    {
        session=FindAnyObjectByType<CourseSession>();game=session.cardGame;hud=FindAnyObjectByType<CourseHUD>();hud.ReadInput=false;controller=session.Local.GetComponent<SourcePlayerController>();controller.controlsEnabled=false;controller.captureCursorOnStart=false;controller.SetCursorCaptured(false);game.ReadInput=false;game.botEnabled=false;game.shuffleOnReset=false;Bots(false);
    }
    void Bots(bool enabled){foreach(var p in session.roster.allParticipants){var bot=p.GetComponent<CourseRival>();if(bot){bot.enabled=enabled;bot.attacksEnabled=false;}}}
    IEnumerator Switch(CourseMapDefinition map,int players)
    {
        Require(session.maps.TryStart(map,players),"map selection rejected");float end=Time.realtimeSinceStartup+15;
        while(SceneManager.GetActiveScene().path!=map.scenePath&&Time.realtimeSinceStartup<end)yield return null;
        Require(SceneManager.GetActiveScene().path==map.scenePath,"scene did not load");yield return null;yield return null;Bind();
    }
    IEnumerator Validate()
    {
        yield return null;yield return null;Bind();Time.timeScale=5;
        var catalog=session.maps.catalog;var easy=catalog.maps.Single(m=>m.id=="first-steps");var climb=catalog.maps.Single(m=>m.id=="summit");
        Test("Easy map remains the entry difficulty and three real maps are selectable",()=>Require(session.maps.current==easy&&easy.difficulty==1&&climb.difficulty>easy.difficulty&&session.maps.CanSelect(easy)&&session.maps.CanSelect(climb)&&catalog.maps.Count(m=>m.playable)==3,"catalog"));
        var match=game.Match;
        Test("Planned maps and invalid player counts cannot reset or load a game",()=>Require(catalog.maps.Where(m=>!m.playable).All(m=>!session.maps.TryStart(m,6))&&!session.maps.TryStart(easy,1)&&!session.maps.TryStart(easy,7)&&game.Match==match,"invalid start mutated the match"));
        var outsider=ScriptableObject.CreateInstance<CourseMapDefinition>();outsider.playable=true;outsider.scenePath=easy.scenePath;outsider.movementProfile=easy.movementProfile;
        Test("Only registered maps can be selected",()=>Require(!session.maps.TryStart(outsider,6),"unregistered map"));Destroy(outsider);
        hud.OpenMapMenu();yield return null;int revision=game.Match.Revision;hud.ProcessActions(true,true,true);
        Test("Map menu captures the cursor and prevents game actions underneath",()=>Require(controller.suppressCursorCapture&&Cursor.lockState!=CursorLockMode.Locked&&session.EveryoneSeated&&game.Match.Revision==revision,"menu click-through"));hud.CloseMapMenu();
        Test("Restarting the current map applies the chosen roster",()=>Require(session.maps.TryStart(easy,3)&&session.participants.Length==3&&session.EveryoneSeated&&game.Match.AllCards().Count()==56,"same-map reset"));
        yield return MeasureTutorial();
        yield return Switch(climb,6);
        Test("Switch to climb loads its scene, six players and all 56 cards",()=>Require(session.maps.current==climb&&session.roster.PlayerCount==6&&session.participants.Length==6&&session.EveryoneSeated&&game.Match.AllCards().Count()==56,"climb scene initialization"));
        Test("Climb retains the original movement, kicks and equal entry distances",()=>Require(session.participants.All(p=>p.Motor.profile==climb.movementProfile&&Mathf.Abs(p.Receiver.kickHorizontalSpeed-6)<.001f)&&Enumerable.Range(0,6).All(i=>Mathf.Abs(Vector3.ProjectOnPlane(session.roster.firstPlatforms[i].position-session.participants[i].standAnchor.position,Vector3.up).magnitude-3)<.001f),"profile/entrance changed"));
        Test("One shared spiral replaces difficulty branches and includes two local shortcuts",()=>{
            var fork=session.roster.branches;
            Require(fork&&fork.sharedSpiral&&fork.left.SequenceEqual(fork.right)&&fork.left.Length<=12,"shared route");
            Require(fork.shortcuts.Length==2&&fork.shortcuts.All(s=>s.exitWaypoint-s.entryWaypoint==3),"short local rejoins");
            Require(GameObject.Find("Climb landing M06").transform.localScale.x>=3.6f,"checkpoint shelf too narrow to pass");
            Require(!FindObjectsByType<TextMesh>(FindObjectsInactive.Include).Any(t=>t.text.Contains("EASY")||t.text.Contains("HARD")),"obsolete difficulty label");
            Require(Vector3.Distance(session.bellTarget.position,fork.summit.position)<1,"shared bell");
        });
        Test("The compact path completes a full rising turn around one solid peak",()=>{
            var fork=session.roster.branches;
            float distance=RouteDistance(session.participants[0].standAnchor.position,session.roster.RouteForSeat(0));
            Require(distance>tutorialDistance*1.7f&&distance<tutorialDistance*2.3f,"distance ratio="+distance/tutorialDistance);
            float rotation=0;
            for(int i=1;i<fork.left.Length;i++){
                var a=fork.left[i-1].position-fork.peakCenter;var b=fork.left[i].position-fork.peakCenter;
                rotation+=Mathf.DeltaAngle(Mathf.Atan2(a.x,a.z)*Mathf.Rad2Deg,Mathf.Atan2(b.x,b.z)*Mathf.Rad2Deg);
                Require(b.y>a.y,"route descends");
            }
            Require(Mathf.Abs(rotation)>350&&fork.summit.position.y>6&&Vector3.ProjectOnPlane(fork.summit.position-fork.peakCenter,Vector3.up).magnitude<.1f,"not a full summit spiral");
            Require(GameObject.Find("Summit rock core").GetComponent<MeshCollider>(),"rock face has no collision");
            Require(!GameObject.Find("Wall buttress"),"tutorial wall supports obstruct the compact routes");
        });
        game.enabled=false;var runner=session.participants[1];var botRunner=runner.GetComponent<CourseRival>();float mainSeconds=0;int mainJumps=0;
        foreach(int count in probeOnly?new[]{2,6}:new[]{2,3,4,5,6}){
          session.roster.StartNewGame(count);yield return new WaitForFixedUpdate();
          var starts=session.participants.Select(p=>p.standAnchor.position).ToArray();
          Test(count+" players have identical shared waypoints after equal entrances",()=>{
            for(int seat=0;seat<count;seat++){
                var route=session.roster.RouteForSeat(seat);
                Require(route.Length==session.roster.branches.RouteLength(seat,count)&&Vector3.Distance(route[0].position,new Vector3(starts[seat].x,.175f,2.3f))<.001f&&Vector3.Distance(route.Last().position,session.roster.branches.summit.position)<.001f,"entrance or summit");
                Require(route.Skip(2).Select(t=>t.position).SequenceEqual(session.roster.RouteForSeat(0).Skip(2).Select(t=>t.position)),"separate route assignment");
                var bot=session.participants[seat].GetComponent<CourseRival>();if(bot)Require(bot.route.SequenceEqual(route),"bot route not refreshed");
            }
          });
          var laneTimes=new List<float>();
          for(int lane=0;lane<count;lane++){ if(probeOnly&&lane!=0&&lane!=count-1)continue;
            Bots(false);session.ResetPractice();yield return new WaitForFixedUpdate();botRunner.route=session.roster.RouteForSeat(lane);runner.standAnchor.position=starts[lane];runner.retryAnchor.position=starts[lane];runner.Stand();botRunner.enabled=true;
            float start=Time.time,end=Time.time+25;int jumps=runner.Motor.JumpCount,duckTicks=0,immediateJumps=0,lastJump=jumps,groundTicks=0;float peakSpeed=0;
            var trace=new List<string>();int oldWaypoint=-1,oldJumps=-1,oldFalls=-1;bool oldGrounded=false;
            while(!session.BellRung&&Time.time<end){
                if(runner.Motor.IsCrouched)duckTicks++;peakSpeed=Mathf.Max(peakSpeed,runner.Motor.HorizontalSpeed);
                if(runner.Motor.JumpCount!=lastJump){if(groundTicks==1)immediateJumps++;groundTicks=0;lastJump=runner.Motor.JumpCount;}if(runner.Motor.Grounded)groundTicks++;
                if(botRunner.Waypoint!=oldWaypoint||runner.Motor.JumpCount!=oldJumps||runner.Falls!=oldFalls||runner.Motor.Grounded!=oldGrounded){trace.Add((Time.time-start).ToString("F2")+" w="+botRunner.Waypoint+" j="+(runner.Motor.JumpCount-jumps)+" f="+runner.Falls+" g="+runner.Motor.Grounded+" p="+runner.transform.position+" v="+runner.Motor.Velocity);oldWaypoint=botRunner.Waypoint;oldJumps=runner.Motor.JumpCount;oldFalls=runner.Falls;oldGrounded=runner.Motor.Grounded;}
                yield return new WaitForFixedUpdate();
            }
            laneTimes.Add(Time.time-start);
            if(count==6&&lane==0){mainSeconds=Time.time-start;mainJumps=runner.Motor.JumpCount-jumps;}
            lastDetail="seconds="+(Time.time-start).ToString("F2")+" tutorialRatio="+((Time.time-start)/tutorialSeconds).ToString("F2")+" jumps="+(runner.Motor.JumpCount-jumps)+" peakSpeed="+peakSpeed+" duckTicks="+duckTicks+" immediateJumps="+immediateJumps;
            Test(count+" players, climb lane "+(lane+1)+" reaches summit with real jumps and no fall",()=>Require(session.BellRinger==runner&&runner.Falls==0&&runner.Motor.JumpCount-jumps>=8&&Time.time-start>=tutorialSeconds*1.3f&&Time.time-start<=tutorialSeconds*2.5f,lastDetail+" waypoint="+botRunner.Waypoint+" position="+runner.transform.position+" falls="+runner.Falls+"\n"+string.Join("\n",trace)));
          }
          if(!probeOnly)Test(count+" players: starting lanes have comparable travel times",()=>Require(laneTimes.Max()-laneTimes.Min()<1.3f,"lane spread="+(laneTimes.Max()-laneTimes.Min())));
        }
        Bots(false);game.enabled=false;session.roster.StartNewGame(6);yield return new WaitForFixedUpdate();
        botRunner.route=session.roster.RouteForSeat(0);runner.standAnchor.position=session.participants[0].standAnchor.position;runner.retryAnchor.position=runner.standAnchor.position;
        var blocker=session.participants[2];
        foreach(var shortcut in session.roster.branches.shortcuts){
            Bots(false);session.ResetPractice();yield return new WaitForFixedUpdate();runner.Stand();blocker.Stand();
            blocker.Motor.Teleport(botRunner.route[shortcut.entryWaypoint+1].position);blocker.Motor.Simulate(default,.015f);
            int passes=botRunner.PassesTaken;int jumps=runner.Motor.JumpCount;float start=Time.time;botRunner.enabled=true;
            var trace=new List<string>();int previousWaypoint=-1,previousJump=-1;float nextTrace=0;
            while(!session.BellRung&&runner.Falls==0&&Time.time<start+18){
                if(Time.time>=nextTrace||previousWaypoint!=botRunner.Waypoint||previousJump!=runner.Motor.JumpCount){trace.Add((Time.time-start).ToString("F2")+" w="+botRunner.Waypoint+" j="+(runner.Motor.JumpCount-jumps)+" g="+runner.Motor.Grounded+" p="+runner.transform.position+" v="+runner.Motor.Velocity);nextTrace=Time.time+.25f;previousWaypoint=botRunner.Waypoint;previousJump=runner.Motor.JumpCount;}
                yield return new WaitForFixedUpdate();
            }Bots(false);
            lastDetail="seconds="+(Time.time-start).ToString("F2")+" jumps="+(runner.Motor.JumpCount-jumps)+" waypoint="+botRunner.Waypoint+" position="+runner.transform.position;
            Test("Shortcut "+shortcut.entryWaypoint+" physically passes a blocking opponent and rejoins the summit route",()=>Require(session.BellRinger==runner&&runner.Falls==0&&botRunner.PassesTaken==passes+1,lastDetail+"\n"+string.Join("\n",trace)));
            Test("Shortcut "+shortcut.entryWaypoint+" saves a jump and reaches the bell faster than the shared path",()=>Require(session.BellRinger==runner&&runner.Falls==0&&runner.Motor.JumpCount-jumps<mainJumps&&Time.time-start<mainSeconds-.1f,"shortcut="+(Time.time-start)+" main="+mainSeconds+" jumps="+(runner.Motor.JumpCount-jumps)+" mainJumps="+mainJumps+"\n"+string.Join("\n",trace)));
        }
        if(probeOnly)yield break;
        foreach(int side in new[]{-1,1}){
            Bots(false);session.ResetPractice();yield return new WaitForFixedUpdate();runner.Stand();blocker.Stand();
            var ledge=GameObject.Find("Climb landing M06").transform;var middle=session.roster.branches.left[5].position;
            blocker.Motor.Teleport(middle);blocker.Motor.Simulate(default,.015f);
            runner.Motor.Teleport(middle+ledge.right*side*1.25f-ledge.forward*.65f);
            runner.Motor.Simulate(default,.015f);float end=Time.time+1;
            while(Vector3.Dot(runner.transform.position-middle,ledge.forward)<.55f&&Time.time<end){
                runner.Motor.Simulate(new MovementCommand{move=Vector2.up,yaw=ledge.eulerAngles.y},Time.fixedDeltaTime);
                yield return new WaitForFixedUpdate();
            }
            Test("A racer can physically pass a central blocker on ledge side "+side,()=>Require(runner.Motor.Grounded&&runner.Falls==0&&Vector3.Dot(runner.transform.position-middle,ledge.forward)>=.55f&&Vector3.Distance(blocker.transform.position,middle)<.1f,"blocked or slipped off the shared ledge"));
        }
        foreach(int ledgeIndex in new[]{2,7})foreach(bool kicked in new[]{false,true}){
            Bots(false);session.ResetPractice();yield return new WaitForFixedUpdate();runner.Stand();
            var node=session.roster.branches.left[ledgeIndex];runner.Motor.Teleport(node.position);runner.Motor.Simulate(default,.015f);
            var direction=Vector3.ProjectOnPlane((kicked?node.position-session.roster.branches.peakCenter:session.roster.branches.left[ledgeIndex+1].position-node.position),Vector3.up).normalized;
            if(kicked)runner.Receiver.Receive(MeleeAttack.Kick,direction,runner.transform.position+Vector3.up);
            float end=Time.time+4;
            while(runner.Falls==0&&Time.time<end){
                runner.Motor.Simulate(new MovementCommand{move=kicked?Vector2.zero:Vector2.up,yaw=Quaternion.LookRotation(direction).eulerAngles.y},Time.fixedDeltaTime);
                yield return new WaitForFixedUpdate();
            }
            Test((kicked?"An outward kick":"Missing the jump")+" on exposed ledge "+node.name+" causes a real fall",()=>Require(runner.Falls==1,"the ledge had an unintended safety floor"));
        }
        Bots(false);game.enabled=true;session.roster.StartNewGame(6);yield return new WaitForSeconds(.4f);
        int guard=0;while(!game.Match.HasExactFive()&&game.Match.Turn>=0&&guard++<56)game.Match.Reveal(game.Match.Turn);Require(game.Match.HasExactFive(),"five fixture");
        session.Local.Stand();runner.Stand();botRunner.enabled=true;float deadline=Time.time+70;
        while(session.BellCount==0&&Time.time<deadline)yield return new WaitForFixedUpdate();Bots(false);
        Test("Summit bell collects the pile and immediately seats all six",()=>Require(session.BellCount==1&&session.EveryoneSeated&&!game.Match.Paused&&game.Match.LastCollector==1&&game.Match.Seats.All(s=>s.Open.Count==0)&&game.Match.AllCards().Count()==56,"climb bell/card rules"));
        session.roster.StartNewGame(6);yield return new WaitForSeconds(.4f);guard=0;while(!game.Match.HasExactFive()&&game.Match.Turn>=0&&guard++<56)game.Match.Reveal(game.Match.Turn);Require(game.Match.HasExactFive(),"six-racer fixture");
        Bots(true);foreach(var p in session.participants.Skip(1))p.GetComponent<CourseRival>().attacksEnabled=true;game.botEnabled=true;session.Local.Stand();deadline=Time.time+75;
        bool leftUsed=false,rightUsed=false;
        while(session.BellCount==0&&Time.time<deadline){
            foreach(var p in session.participants.Skip(1))if(p.transform.position.z>8.5f){leftUsed|=p.transform.position.x<0;rightUsed|=p.transform.position.x>0;}
            yield return new WaitForFixedUpdate();
        }
        Bots(false);game.botEnabled=false;
        Test("All five AI compete with attacks enabled and finish a six-person climb race",()=>Require(session.BellCount==1&&session.EveryoneSeated&&!game.Match.Paused&&game.Match.AllCards().Count()==56,"simultaneous climb did not finish; positions="+string.Join("; ",session.participants.Select(p=>p.transform.position+" falls="+p.Falls))));
        Test("The simultaneous race climbs around both sides of the peak",()=>Require(leftUsed&&rightUsed,"race did not wind around the peak"));
        // Test a real fight on the summit using the same physical combat system as the easy map.
        game.enabled=false;session.ResetPractice();var victim=session.participants[2];runner.Stand();victim.Stand();
        Vector3 peak=session.roster.branches.summit.position;
        runner.Motor.Teleport(peak+new Vector3(-.6f,.005f,-.3f));victim.Motor.Teleport(peak+new Vector3(.6f,.005f,-.3f));
        var target=new GameObject("Summit fight target").transform;target.position=peak+new Vector3(1.8f,0,-.3f);botRunner.route=new[]{target};botRunner.attacksEnabled=true;botRunner.enabled=true;int received=victim.Receiver.ReceivedHits;deadline=Time.time+2;
        while(victim.Receiver.ReceivedHits==received&&Time.time<deadline)yield return new WaitForFixedUpdate();
        Test("Participants can physically attack on the summit",()=>Require(victim.Receiver.ReceivedHits>received,"summit combat missed"));Bots(false);Destroy(target.gameObject);game.enabled=true;
        yield return Switch(easy,4);
        Test("Switching back preserves four players and restores the easy course",()=>Require(session.maps.current==easy&&session.participants.Length==4&&session.roster.RouteForSeat(0).Length==7&&session.EveryoneSeated&&game.Match.AllCards().Count()==56,"return to easy"));
        var surf=catalog.maps.Single(m=>m.id=="surf");yield return Switch(surf,4);
        Test("Loading surf preserves four players, card rules and selects surf physics",()=>Require(session.maps.current==surf&&session.roster.surf&&session.participants.Length==4&&session.EveryoneSeated&&game.Match.AllCards().Count()==56&&session.participants.All(p=>p.Motor.profile.surfEnabled)&&session.roster.RouteForSeat(0)[3].position.x<0&&session.roster.RouteForSeat(3)[3].position.x>0,"surf scene transition"));
        yield return Switch(easy,4);
        Test("Leaving surf restores the ordinary movement profile",()=>Require(!session.roster.surf&&session.participants.All(p=>p.Motor.profile==easy.movementProfile&&!p.Motor.profile.surfEnabled)&&session.EveryoneSeated,"surf settings leaked"));
        Test("Only one validation runner survives returning to the original editor scene",()=>Require(FindObjectsByType<MapsValidationRunner>(FindObjectsSortMode.None).Length==1,"duplicate runner"));
        hud.OpenMapMenu();yield return new WaitForSeconds(.3f);yield return new WaitForEndOfFrame();Directory.CreateDirectory("Previews");var screenshot=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes("Previews/Maps_Menu.png",screenshot.EncodeToPNG());Destroy(screenshot);hud.CloseMapMenu();
    }
    string lastDetail="passed";
    void Test(string name,Action action){try{action();checks.Add(new Check{name=name,passed=true,detail=lastDetail});Debug.Log("PASS "+name);}catch(Exception e){checks.Add(new Check{name=name,passed=false,detail=e.Message});Debug.LogError("FAIL "+name+": "+e.Message);}lastDetail="passed";Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/maps-validation.json",JsonUtility.ToJson(new Report{passed=checks.Count(c=>c.passed),failed=checks.Count(c=>!c.passed),checks=checks},true));}
    static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
}
#endif
