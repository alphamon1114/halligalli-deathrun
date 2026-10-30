#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalliGalli.Movement;
using UnityEditor;
using UnityEngine;

public sealed class SurfRevisionValidationRunner:MonoBehaviour
{
    public bool probeOnly;
    [Serializable]class Check{public string name,detail;public bool passed;}
    [Serializable]class Report{public int passed,failed;public List<Check> checks;}
    readonly List<Check> checks=new List<Check>();
    IEnumerator Start()
    {
        var run=Validate();while(true){bool more;object next=null;try{more=run.MoveNext();if(more)next=run.Current;}catch(Exception e){Test("Runner completes",false,e.ToString());break;}if(!more)break;yield return next;}
        Time.timeScale=1;Save();Debug.Log("SURF_VALIDATION_RESULT "+checks.Count(c=>c.passed)+"/"+checks.Count);EditorApplication.isPlaying=false;
    }
    IEnumerator Validate()
    {
        yield return null;yield return null;
        var session=FindAnyObjectByType<CourseSession>();var roster=session.roster;var surf=roster.surf;
        session.cardGame.enabled=false;session.cardGame.botEnabled=false;FindAnyObjectByType<CourseHUD>().ReadInput=false;
        var control=session.Local.GetComponent<SourcePlayerController>();control.controlsEnabled=false;control.SetCursorCaptured(false);
        foreach(var p in roster.allParticipants){p.Combat.readInput=false;var b=p.GetComponent<CourseRival>();if(b){b.enabled=false;b.attacksEnabled=false;}}
        Time.timeScale=5;
        var ramps=FindObjectsByType<MeshCollider>().Where(c=>c.name.StartsWith("Surf ramp ")).ToArray();
        Test("Eleven open single banks",surf.sections.Length==11&&ramps.Length==11,"sections="+surf.sections.Length+" colliders="+ramps.Length);
        Test("Continuous course has reversal, right curves and left curves",surf.sections.Sum(s=>s.Length)>330&&surf.sections.Count(s=>s.headings.Last()-s.headings.First()>60)==3&&surf.sections.Count(s=>s.headings.Last()-s.headings.First()<-60)==2&&Vector3.Dot(surf.sections[0].Sample(Vector3.zero).forward,surf.sections[1].Sample(Vector3.zero).forward)<-.99f,"length="+surf.sections.Sum(s=>s.Length));
        Test("Three common recovery decks",session.checkpoints.points.Select(c=>c.surfGates).SequenceEqual(new[]{1,3,7}),"checkpoint count="+session.checkpoints.points.Length);
        Test("Final aerial transfer and narrower bank require line control",Vector3.Distance(surf.sections[9].points.Last(),surf.sections[10].points[0])>5&&surf.sections[10].halfWidth==2.4f,"gap="+Vector3.Distance(surf.sections[9].points.Last(),surf.sections[10].points[0]));
        var runner=roster.allParticipants[1];var bot=runner.GetComponent<CourseRival>();
        foreach(int count in probeOnly?new[]{6}:new[]{2,3,6}){
            roster.StartNewGame(count);yield return null;
            foreach(var p in session.participants){var b=p.GetComponent<CourseRival>();if(b)b.enabled=false;}
            var starts=roster.allParticipants.Select(p=>p.standAnchor.position).ToArray();
            for(int seat=0;seat<(probeOnly?1:count);seat++){
                session.ResetPractice();yield return new WaitForFixedUpdate();
                bot.route=roster.RouteForSeat(seat);runner.standAnchor.position=starts[seat];runner.retryAnchor.position=runner.standAnchor.position;
                runner.Stand();bot.enabled=true;float start=Time.time,end=start+65,next=0;var trace=new List<string>();
                while(!session.BellRung&&Time.time<end){
                    if(Time.time>=next){next=Time.time+.6f;trace.Add((Time.time-start).ToString("F1")+" w="+bot.Waypoint+" s="+bot.SurfSection+"/"+bot.SurfPhase+" g="+surf.GatesPassed(runner)+" p="+runner.transform.position+" v="+runner.Motor.Velocity);}
                    yield return new WaitForFixedUpdate();
                }
                bot.enabled=false;float seconds=Time.time-start;
                Test(count+" seats, start "+seat+": physically reaches bell through all banks around 30 seconds",session.BellRinger==runner&&runner.Falls==0&&surf.Completed(runner)&&seconds>=27&&seconds<36,"seconds="+seconds+" falls="+runner.Falls+"\n"+string.Join("\n",trace));
                if(probeOnly)yield break;
            }
        }
        roster.StartNewGame(6);yield return null;foreach(var p in roster.allParticipants){var b=p.GetComponent<CourseRival>();if(b)b.enabled=false;}
        session.ResetPractice();runner.Stand();runner.Motor.Teleport(surf.finish.position);runner.Motor.Simulate(default,.02f);
        Test("Cannot skip banks and ring by dropping on the finish",!session.CanRing(runner),"gates="+surf.GatesPassed(runner));
        var checkpoint=session.checkpoints.points[1];runner.SetCheckpoint(1,0);surf.RestoreCheckpointProgress(runner,3);
        runner.Motor.Teleport(checkpoint.spawn.position+Vector3.up*2);runner.Motor.AddImpulse(Vector3.right*15);runner.RequestRespawn();yield return new WaitForSeconds(.8f);
        Test("Manual recovery returns to midway without losing valid gates",!runner.IsRespawning&&Vector3.Distance(runner.transform.position,checkpoint.spawn.position)<.2f&&surf.GatesPassed(runner)==3&&runner.Motor.HorizontalSpeed<.1f,"position="+runner.transform.position);
        for(int index=0;index<session.checkpoints.points.Length;index++){
            session.ResetPractice();yield return new WaitForFixedUpdate();runner.Stand();
            var saved=session.checkpoints.points[index];runner.SetCheckpoint(index,0);surf.RestoreCheckpointProgress(runner,saved.surfGates);
            runner.Motor.Teleport(saved.spawn.position+Vector3.up*3);runner.RequestRespawn();yield return new WaitForSeconds(.85f);
            Test("Checkpoint "+index+" recovers with saved passage and stopped velocity",!runner.IsRespawning&&Vector3.Distance(runner.transform.position,saved.spawn.position)<.15f&&surf.GatesPassed(runner)==saved.surfGates&&runner.Motor.HorizontalSpeed<.1f,"gates="+surf.GatesPassed(runner));
            bot.route=roster.RouteForSeat(0);bot.enabled=true;float deadline=Time.time+40;
            while(!session.BellRung&&runner.Falls==1&&Time.time<deadline)yield return new WaitForFixedUpdate();
            bot.enabled=false;
            Test("Checkpoint "+index+" can finish using normal movement",session.BellRinger==runner&&runner.Falls==1&&surf.Completed(runner),"falls="+runner.Falls+" gates="+surf.GatesPassed(runner));
        }
        // Isolate the final kicker: set only the initial velocity, then use the normal motor and steering.
        foreach(float speed in new[]{6f,24f}){
            session.ResetPractice();yield return new WaitForFixedUpdate();runner.Stand();
            var last=surf.sections.Last();int index=last.points.Length-25;
            var facing=Quaternion.Euler(0,last.headings[index],0);
            runner.Motor.Teleport(last.points[index]+facing*Vector3.right*last.side*surf.laneX+Vector3.up*.4f);
            runner.Motor.AddImpulse(facing*Vector3.forward*speed);surf.RestoreCheckpointProgress(runner,surf.sections.Length-1);
            var nav=new SurfCourse.Navigation{section=surf.sections.Length-1,phase=1};
            float deadline=Time.time+6;
            while(Time.time<deadline&&!runner.IsRespawning&&!session.BellRung){
                var input=surf.Guide(runner,bot.route,nav);float yaw=Quaternion.LookRotation(input.forward).eulerAngles.y;
                Vector3 local=Quaternion.Inverse(Quaternion.Euler(0,yaw,0))*input.steering.normalized;
                runner.Motor.Simulate(new MovementCommand{move=new Vector2(local.x,local.z),yaw=yaw},Time.fixedDeltaTime);
                session.Ring(runner);yield return new WaitForFixedUpdate();
            }
            bool rolledBack=Vector3.Dot(runner.transform.position-last.points[index],facing*Vector3.forward)<-2;
            Test("Final launch at "+speed+" m/s "+(speed<10?"rolls back or falls before clearing the gap":"reaches the bell"),speed<10?!session.BellRung&&(runner.Falls>0||rolledBack):session.BellRinger==runner&&runner.Falls==0,"position="+runner.transform.position+" falls="+runner.Falls+" rolledBack="+rolledBack);
        }
        var divider=GameObject.Find("Surf lane divider").GetComponent<BoxCollider>();
        Test("Horizontal divider covers right drop and leaves turn open",!divider.isTrigger&&divider.bounds.size.y<.5f&&divider.bounds.max.x>17&&divider.bounds.min.z<6&&divider.bounds.max.z<30.5f,"bounds="+divider.bounds);
        foreach(float z in new[]{6f,19f,28f})foreach(float x in new[]{0f,3f,12f}){
            session.ResetPractice();yield return new WaitForFixedUpdate();runner.Stand();
            bool saved=x==12; if(saved){runner.SetCheckpoint(0,0);surf.RestoreCheckpointProgress(runner,1);}
            Vector3 spawn=saved?session.checkpoints.SpawnPosition(runner):runner.retryAnchor.position;
            runner.Motor.Teleport(new Vector3(x,-2.8f,z));
            float deadline=Time.time+1.5f;
            while(!runner.IsRespawning&&Time.time<deadline){runner.Motor.Simulate(default,Time.fixedDeltaTime);yield return new WaitForFixedUpdate();}
            Test("Right-hand drop hits horizontal divider x="+x+" z="+z,runner.IsRespawning&&runner.Falls==1&&runner.transform.position.y>-6,"position="+runner.transform.position);
            yield return new WaitForSeconds(.85f);
            Test("Divider recovery preserves legitimate progress x="+x+" z="+z,!runner.IsRespawning&&runner.Falls==1&&Vector3.Distance(runner.transform.position,spawn)<.15f&&surf.GatesPassed(runner)==(saved?1:0),"position="+runner.transform.position+" gates="+surf.GatesPassed(runner));
        }
        foreach(float speed in new[]{12f,28f}){
            session.ResetPractice();yield return new WaitForFixedUpdate();runner.Stand();runner.Motor.Teleport(new Vector3(3,-2.8f,19));runner.Motor.AddImpulse(Vector3.right*speed);
            float deadline=Time.time+1.5f;
            while(!runner.IsRespawning&&Time.time<deadline){runner.Motor.Simulate(default,Time.fixedDeltaTime);yield return new WaitForFixedUpdate();}
            Test("Fast sideways fall cannot go around separator speed="+speed,runner.IsRespawning&&runner.Falls==1&&runner.transform.position.y>-6,"position="+runner.transform.position);
        }
        session.ResetPractice();yield return new WaitForFixedUpdate();runner.Stand();
        divider.enabled=false;
        try{
            runner.Motor.Teleport(new Vector3(3,-2.8f,19));int contacts=runner.Motor.SurfContactTicks;
            for(int tick=0;tick<105;tick++)runner.Motor.Simulate(default,.015f);
            Test("Removing separator reproduces lower-bank skip",runner.Motor.SurfContactTicks>contacts,"new lower-bank contacts="+(runner.Motor.SurfContactTicks-contacts));
        }finally{divider.enabled=true;}
        // Physical skip regressions: do not rely on bell-gate rejection after landing on a later bank.
        var bulkhead=GameObject.Find("Late sector bulkhead").GetComponent<BoxCollider>();
        foreach(float x in new[]{-65f,0f,30f}){
            session.ResetPractice();yield return new WaitForFixedUpdate();runner.Stand();
            runner.Motor.Teleport(new Vector3(x,-8,39));runner.Motor.AddImpulse(Vector3.forward*28);
            float farthest=39;
            for(int tick=0;tick<70;tick++){runner.Motor.Simulate(default,.015f);farthest=Mathf.Max(farthest,runner.transform.position.z);}
            Test("Early-to-late flight physically stopped at x="+x,farthest<bulkhead.bounds.min.z,"furthest z="+farthest);
        }
        session.ResetPractice();yield return new WaitForFixedUpdate();runner.Stand();
        bulkhead.enabled=false;
        try{
            runner.Motor.Teleport(new Vector3(0,-8,39));runner.Motor.AddImpulse(Vector3.forward*28);
            for(int tick=0;tick<70;tick++)runner.Motor.Simulate(default,.015f);
            Test("Removing sector wall reproduces early-to-late flight",runner.transform.position.z>55,"position="+runner.transform.position);
        }finally{bulkhead.enabled=true;}
        var exits=new[]{new Vector3(-8.5f,2,-1),new Vector3(8.5f,2,-1),new Vector3(0,2,-14)};
        var pushes=new[]{Vector3.left,Vector3.right,Vector3.back};
        for(int exit=0;exit<exits.Length;exit++){
            session.ResetPractice();yield return new WaitForFixedUpdate();runner.Stand();
            runner.Motor.Teleport(exits[exit]);runner.Motor.AddImpulse(pushes[exit]*28);
            float deadline=Time.time+2;int contacts=runner.Motor.SurfContactTicks;
            while(!runner.IsRespawning&&Time.time<deadline){runner.Motor.Simulate(default,Time.fixedDeltaTime);yield return new WaitForFixedUpdate();}
            Test("Entry side/back escape "+exit+" recovers before reaching lower bank",runner.IsRespawning&&runner.Falls==1&&runner.transform.position.y>-6&&runner.Motor.SurfContactTicks==contacts,"position="+runner.transform.position);
            yield return new WaitForSeconds(.85f);
            Test("Entry escape "+exit+" returns to start without progress",!runner.IsRespawning&&surf.GatesPassed(runner)==0&&Vector3.Distance(runner.transform.position,runner.retryAnchor.position)<.15f,"position="+runner.transform.position);
        }
        session.ResetPractice();
    }
    void Test(string name,bool passed,string detail){checks.Add(new Check{name=name,passed=passed,detail=detail});if(!passed)Debug.LogError(name+" "+detail);Save();}
    void Save(){Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/"+(probeOnly?"surf-probe":"surf-validation")+".json",JsonUtility.ToJson(new Report{passed=checks.Count(c=>c.passed),failed=checks.Count(c=>!c.passed),checks=checks},true));}
}
#endif
