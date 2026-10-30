#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using HalliGalli.Movement;

public sealed class CourseValidationRunner:MonoBehaviour
{
    [Serializable]class Check{public string name;public bool passed;public string detail;}
    [Serializable]class Report{public int passed,failed;public List<Check> checks;}
    readonly List<Check> results=new List<Check>();
    CourseSession session;CourseParticipant player,rival;CourseRival bot;SourcePlayerController controller;
    IEnumerator Start()
    {
        yield return null;yield return null;
        session=FindFirstObjectByType<CourseSession>();if(session.roster)session.roster.StartNewGame(2);player=session.Local;rival=session.participants.First(p=>p!=player);bot=rival.GetComponent<CourseRival>();controller=player.GetComponent<SourcePlayerController>();controller.controlsEnabled=false;controller.captureCursorOnStart=false;controller.SetCursorCaptured(false);FindFirstObjectByType<CourseHUD>().enabled=false;
        // These checks isolate navigation/physics; MatchValidationRunner covers the integrated card rules.
        if(session.cardGame)session.cardGame.enabled=false;
        bot.enabled=false;Time.timeScale=3;
        Run("Both players begin seated with attacks and movement disabled",()=>Require(session.EveryoneSeated&&!session.RunActive&&!controller.movementEnabled&&!player.Combat.TryKick()&&!player.Receiver.canReceiveHits,"seat gating"));
        int departures=0,resumes=0,rings=0;session.onDeparture.AddListener(()=>departures++);session.onEveryoneSeated.AddListener(()=>resumes++);session.onBellRung.AddListener(()=>rings++);
        player.Stand();rival.Stand();yield return new WaitForFixedUpdate();
        Run("Standing enables movement and sends one departure event",()=>Require(session.RunActive&&controller.movementEnabled&&controller.actionsEnabled&&departures==1,"departure count="+departures));
        Run("Bell cannot be rung from the table",()=>Require(!session.Ring(player),"remote ring accepted"));
        player.Sit();Run("One seated player does not resume table phase",()=>Require(session.RunActive&&resumes==0,"resumed early"));rival.Sit();Run("All seated resets the run and resumes once",()=>Require(!session.RunActive&&session.EveryoneSeated&&resumes==1,"resume missing"));
        player.Stand();player.Motor.Teleport(new Vector3(0,.28f,21.95f));yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();
        Run("Bell rings once, leaving movement and session active",()=>Require(session.Ring(player)&&session.RunActive&&controller.movementEnabled&&!session.Ring(player)&&rings==1&&session.BellCount==1,"bell transition"));
        player.Sit();Run("Returning to all seats allows another run",()=>Require(!session.BellRung&&!session.RunActive&&session.EveryoneSeated,"cannot replay"));
        player.Stand();player.Motor.Teleport(new Vector3(0,-4,7));yield return null;yield return null;
        Run("Falling disables actions during the respawn delay",()=>Require(player.IsRespawning&&!controller.actionsEnabled&&player.Falls==1,"fall detection"));yield return new WaitForSeconds(.8f);
        Run("Fall returns player standing at own course entrance",()=>Require(!player.IsRespawning&&!player.IsSeated&&Vector3.Distance(player.transform.position,player.retryAnchor.position)<.15f&&controller.movementEnabled,"respawn="+player.transform.position));
        session.ResetPractice();player.Stand();rival.Stand();controller.enabled=false;player.Motor.Teleport(new Vector3(-1.1f,.38f,5.7f));rival.Motor.Teleport(new Vector3(0,.38f,5.7f));controller.ResetView(90);controller.UpdateView();
        for(int i=0;i<5;i++){player.Motor.Simulate(new MovementCommand{yaw=90},.015f);rival.Motor.Simulate(default,.015f);}controller.UpdateView();Physics.SyncTransforms();
        int hits=player.Combat.HitCount;player.Combat.TryKick();float end=Time.time+2;
        while(Time.time<end&&rival.Falls==0){rival.Motor.Simulate(default,Time.fixedDeltaTime);yield return new WaitForFixedUpdate();}
        Run("A real kick knocks the opponent off the shared platform",()=>Require(player.Combat.HitCount==hits+1&&rival.Falls==1,"hits="+(player.Combat.HitCount-hits)+" falls="+rival.Falls+" pos="+rival.transform.position));
        yield return new WaitForSeconds(.8f);Run("Kicked opponent returns to their entrance",()=>Require(!rival.IsRespawning&&Vector3.Distance(rival.transform.position,rival.retryAnchor.position)<.15f,"rival respawn"));
        Run("Existing kick tuning remains 3 metre configuration",()=>Require(Mathf.Abs(player.Receiver.kickHorizontalSpeed-6)<.001f&&Mathf.Abs(player.Receiver.kickLiftSpeed-4)<.001f,"kick tuning changed"));
        Run("Bell sound and custom seated clip are assigned",()=>Require(session.bellAudio.clip&&player.GetComponent<CharacterMotionDriver>().seatedClip&&rival.GetComponent<CourseBodyAnimator>().seatedClip,"missing asset"));
        // Traverse both entrance lanes using the same command-driven motor as the local player.
        foreach(int side in new[]{1,-1}){
            bot.enabled=false;session.ResetPractice();controller.enabled=false;player.Stand();
            if(session.roster){int lane=side<0?0:1;bot.route=session.roster.RouteForSeat(lane);rival.standAnchor.position=new Vector3(side*.75f,.025f,CourseRoster.StartZ);rival.retryAnchor.position=rival.standAnchor.position;}else{bot.route=GameObject.Find(side<0?"Left route":"Right route").GetComponentsInChildren<Transform>().Skip(1).ToArray();rival.standAnchor.position=new Vector3(side*2.55f,.025f,-6);rival.retryAnchor.position=new Vector3(side*1.8f,.025f,-.7f);}
            bot.attacksEnabled=false;bot.enabled=true;float begin=Time.time;end=Time.time+35;int beforeJumps=rival.Motor.JumpCount;int beforeTrips=bot.CompletedTrips;
            while(!session.BellRung&&Time.time<end){yield return new WaitForFixedUpdate();}
            float travel=Time.time-begin;
            Debug.Log("COURSE_TRAVEL side="+side+" seconds="+travel.ToString("F2"));
            Run((side<0?"Left":"Right")+" lane reaches bell with physical jumps and no fall",()=>Require(session.BellRinger==rival&&rival.Falls==0&&rival.Motor.JumpCount-beforeJumps>=7,"time="+travel.ToString("F2")+" falls="+rival.Falls+" waypoint="+bot.Waypoint+" pos="+rival.transform.position+" jumps="+(rival.Motor.JumpCount-beforeJumps)));
            end=Time.time+35;while(!rival.IsSeated&&Time.time<end){yield return new WaitForFixedUpdate();}
            Run((side<0?"Left":"Right")+" route returns to its seat",()=>Require(rival.IsSeated&&bot.CompletedTrips>beforeTrips&&rival.Falls==0,"falls="+rival.Falls+" waypoint="+bot.Waypoint+" pos="+rival.transform.position));
        }
        bot.enabled=false;session.ResetPractice();controller.enabled=true;player.Stand();rival.Stand();player.Motor.Teleport(new Vector3(0,.38f,5.7f));rival.Motor.Teleport(new Vector3(-1.2f,.38f,5.7f));
        var target=new GameObject("Duel route target").transform;target.position=new Vector3(2,.375f,5.7f);bot.route=new[]{target};bot.attacksEnabled=true;bot.enabled=true;int received=player.Receiver.ReceivedHits;end=Time.time+2;
        while(player.Receiver.ReceivedHits==received&&Time.time<end)yield return new WaitForFixedUpdate();
        Run("Practice rival can strike the local player",()=>Require(player.Receiver.ReceivedHits>received,"AI attacks missed"));
        bot.enabled=false;session.ResetPractice();
        Time.timeScale=1;int passed=results.Count(r=>r.passed);Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/course-validation.json",JsonUtility.ToJson(new Report{passed=passed,failed=results.Count-passed,checks=results},true));Debug.Log("COURSE_VALIDATION_RESULT "+passed+"/"+results.Count);
        EditorApplication.isPlaying=false;
    }
    void Run(string name,Action action){try{action();results.Add(new Check{name=name,passed=true,detail="passed"});Debug.Log("PASS "+name);}catch(Exception e){results.Add(new Check{name=name,passed=false,detail=e.Message});Debug.LogError("FAIL "+name+": "+e.Message);}}
    static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
}
#endif
