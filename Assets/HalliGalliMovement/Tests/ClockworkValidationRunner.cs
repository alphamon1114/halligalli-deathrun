#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalliGalli.Movement;
using UnityEditor;
using UnityEngine;

public sealed class ClockworkValidationRunner : MonoBehaviour
{
    [Serializable] sealed class Check { public string name,detail;public bool passed; }
    [Serializable] sealed class Report { public string utc;public int passed,failed;public List<Check> checks; }
    readonly List<Check> checks=new List<Check>();
    readonly WaitForFixedUpdate fixedStep=new WaitForFixedUpdate();
    CourseSession session;
    CourseParticipant runner;
    CourseRival bot;
    IEnumerator Start()
    {
        var run=Validate();
        while(true){
            bool more;object next=null;
            try{more=run.MoveNext();if(more)next=run.Current;}
            catch(Exception exception){Test("Validation completes",false,exception.ToString());break;}
            if(!more)break;
            yield return next;
        }
        Time.timeScale=1;Save();
        Debug.Log("CLOCKWORK_VALIDATION_RESULT "+checks.Count(c=>c.passed)+"/"+checks.Count);
        if(Application.isBatchMode)EditorApplication.Exit(checks.Exists(c=>!c.passed)?1:0);
        else EditorApplication.isPlaying=false;
    }

    IEnumerator Validate()
    {
        yield return null;yield return null;
        session=FindAnyObjectByType<CourseSession>();
        session.cardGame.enabled=false;session.cardGame.botEnabled=false;
        var hud=FindAnyObjectByType<CourseHUD>();hud.ReadInput=false;
        var controller=session.Local.GetComponent<SourcePlayerController>();controller.controlsEnabled=false;controller.SetCursorCaptured(false);
        foreach(var participant in session.roster.allParticipants){
            participant.Combat.readInput=false;
            var rival=participant.GetComponent<CourseRival>();
            if(rival){rival.enabled=false;rival.attacksEnabled=false;rival.autoDepart=false;}
        }
        Time.timeScale=4;
        session.roster.StartNewGame(6);yield return fixedStep;
        Test("All six original seats can depart",session.participants.Length==6&&session.EveryoneSeated
            &&session.participants.All(p=>p.Stand())&&session.participants.Select(p=>p.transform.position).Distinct().Count()==6,
            "participants="+session.participants.Length);
        session.ResetPractice();
        session.roster.StartNewGame(2);yield return fixedStep;
        runner=session.roster.allParticipants[1];bot=runner.GetComponent<CourseRival>();bot.enabled=false;
        var course=session.clockwork;
        var spear=course.traps.First(t=>t.kind==ClockworkTrapKind.Spears);
        var arrows=course.traps.First(t=>t.kind==ClockworkTrapKind.Arrows);
        foreach(var trap in course.traps)SampleChecks(trap);

        foreach(var trap in new[]{spear,arrows}){
            session.ResetPractice();runner.Stand();
            double deadline=CourseNetwork.WorldTime+trap.period+1;
            while((trap.Sample(CourseNetwork.WorldTime).stage!=ClockworkTrapStage.Safe||trap.Sample(CourseNetwork.WorldTime).cycle>.15f)&&CourseNetwork.WorldTime<deadline)
                yield return fixedStep;
            runner.Motor.Teleport(trap.transform.TransformPoint(new Vector3(0,.025f,0)));
            bool safe=true,warned=false,hitDuringAttack=false;
            int hits=trap.Hits;
            deadline=CourseNetwork.WorldTime+trap.period;
            while(!runner.IsRespawning&&CourseNetwork.WorldTime<deadline){
                var sample=trap.Sample(CourseNetwork.WorldTime);
                if(sample.stage==ClockworkTrapStage.Warning)warned=true;
                if(sample.stage!=ClockworkTrapStage.Attack&&runner.Falls!=0)safe=false;
                runner.Motor.Simulate(default,Time.fixedDeltaTime);
                yield return fixedStep;
            }
            if(runner.IsRespawning)hitDuringAttack=trap.Sample(trap.LastHitTime).stage==ClockworkTrapStage.Attack;
            Test(trap.kind+" actual safe and warning traversal space is nonlethal",safe&&warned,"warningObserved="+warned);
            Test(trap.kind+" actual exposed projectile contact triggers one fall",runner.IsRespawning&&runner.Falls==1&&trap.Hits==hits+1&&hitDuringAttack,
                "falls="+runner.Falls+" hits="+(trap.Hits-hits)+" stage="+trap.Sample(trap.LastHitTime).stage);
            yield return new WaitForSeconds(.8f);
            Test(trap.kind+" victim respawns at the original start",!runner.IsRespawning&&Vector3.Distance(runner.transform.position,runner.retryAnchor.position)<.15f,
                "position="+runner.transform.position);
        }

        for(int index=0;index<session.checkpoints.points.Length;index++){
            session.ResetPractice();runner.Stand();
            var point=session.checkpoints.points[index];
            runner.Motor.Teleport(point.spawn.position);
            for(int step=0;step<5;step++){runner.Motor.Simulate(default,Time.fixedDeltaTime);yield return fixedStep;}
            Test("Grounded safe deck saves checkpoint "+index,runner.CheckpointIndex==index,"saved="+runner.CheckpointIndex);
            runner.BeginFall();yield return new WaitForSeconds(.8f);
            Test("Fall restores checkpoint "+index,!runner.IsRespawning&&runner.CheckpointIndex==index&&Vector3.Distance(runner.transform.position,point.spawn.position)<.15f,
                "position="+runner.transform.position);
        }

        // No teleport, warp, phase override, forced checkpoint, or motor impulse in this complete route.
        session.ResetPractice();yield return fixedStep;runner.Stand();bot.enabled=true;
        int checkpointMask=0;float started=Time.time,nextTrace=0;var trace=new List<string>();
        while(!session.BellRung&&Time.time-started<90){
            if(runner.CheckpointIndex>=0)checkpointMask|=1<<runner.CheckpointIndex;
            if(Time.time>=nextTrace){nextTrace=Time.time+.5f;trace.Add((Time.time-started).ToString("F1")+" w="+bot.Waypoint+" p="+runner.transform.position+" falls="+runner.Falls);}
            yield return fixedStep;
        }
        bot.enabled=false;
        Test("Solo bot physically walks and waits through all six crossings to ring",session.BellRinger==runner&&runner.Falls==0,
            "seconds="+(Time.time-started)+" falls="+runner.Falls+"\n"+string.Join("\n",trace));
        Test("Physical route visits both safe checkpoints",checkpointMask==3,"visitedMask="+checkpointMask);
        if(session.BellRung){
            int hits=spear.Hits,falls=runner.Falls;
            runner.Motor.Teleport(spear.transform.TransformPoint(new Vector3(0,.025f,0)));
            yield return new WaitForSeconds(spear.period+.1f);
            Test("Returning bell winner is immune to trap damage",runner.Falls==falls&&spear.Hits==hits,"falls="+runner.Falls);
        }
        session.ResetPractice();
        int seatedHits=spear.Hits;
        runner.Motor.Teleport(spear.transform.TransformPoint(new Vector3(0,.025f,0)));
        yield return new WaitForSeconds(spear.period+.1f);
        Test("Seated participants are excluded even inside attack geometry",runner.IsSeated&&runner.Falls==0&&spear.Hits==seatedHits,"hits="+(spear.Hits-seatedHits));
        runner.Stand();session.ApplyNetworkState(false,session.BellCount);
        runner.Motor.Teleport(spear.transform.TransformPoint(new Vector3(0,.025f,0)));
        yield return new WaitForSeconds(spear.period+.1f);
        Test("No trap damage outside RunActive",!runner.IsRespawning&&runner.Falls==0&&!session.RunActive,"falls="+runner.Falls);
        session.ResetPractice();
    }

    void SampleChecks(ClockworkTrap trap)
    {
        double origin=-trap.phase;
        double warning=origin+trap.SafeSeconds+trap.warningSeconds*.5;
        double attack=origin+trap.period-trap.attackSeconds;
        Vector3 centre=trap.transform.TransformPoint(new Vector3(0,.025f,0));
        float radius=runner.Motor.profile.radius,height=runner.Motor.BodyHeight;
        var mid=trap.Sample(attack+trap.attackSeconds*.5);
        Test(trap.name+" deterministic safe-warning-attack cycle",
            trap.Sample(origin+.1).stage==ClockworkTrapStage.Safe&&trap.Sample(warning).stage==ClockworkTrapStage.Warning
            &&mid.stage==ClockworkTrapStage.Attack&&trap.Sample(origin+trap.period+.1).stage==ClockworkTrapStage.Safe
            &&Mathf.Abs(trap.Sample(attack+trap.attackSeconds*.5+trap.period*100000d).extension-mid.extension)<.0001f,
            "period="+trap.period+" safe="+trap.SafeSeconds);
        Test(trap.name+" no safe or warning collision",!trap.IntersectsCapsule(centre,radius,height,origin+.1)
            &&!trap.IntersectsCapsule(centre,radius,height,warning),"capsule="+centre);
        Test(trap.name+" active geometry contacts standing capsule",trap.IntersectsCapsule(centre,radius,height,attack+trap.attackSeconds*.5),"capsule="+centre);
        Test(trap.name+" no whole-room or overhead kill",!trap.IntersectsCapsule(centre+Vector3.forward*3,radius,height,attack+trap.attackSeconds*.5)
            &&!trap.IntersectsCapsule(centre+Vector3.up*3,radius,height,attack+trap.attackSeconds*.5),"outside and above geometry");
        if(trap.kind==ClockworkTrapKind.Arrows){
            double before=attack+trap.attackSeconds*.1,after=attack+trap.attackSeconds*.9;
            Test(trap.name+" fast arrow sweep catches between endpoint samples",!trap.IntersectsCapsule(centre,radius,height,before)
                &&!trap.IntersectsCapsule(centre,radius,height,after)&&trap.SweptIntersectsCapsule(centre,centre,radius,height,height,before,after),"actual individual arrow volumes");
            Test(trap.name+" projectile reset does not sweep backwards through room",!trap.SweptIntersectsCapsule(centre,centre,radius,height,height,
                attack+trap.attackSeconds*.98,origin+trap.period+.1),"end-to-start discontinuity excluded");
            Test(trap.name+" far side remains safe before arrow arrives",!trap.IntersectsCapsule(trap.transform.TransformPoint(new Vector3(2,.025f,0)),radius,height,before),"attack is spatial, not a firing-zone trigger");
        }else{
            Test(trap.name+" retracted spear never damages above its exposed tip",!trap.IntersectsCapsule(centre+Vector3.up*.5f,radius,height,attack+trap.attackSeconds*.01)
                &&trap.IntersectsCapsule(centre+Vector3.up*.5f,radius,height,attack+trap.attackSeconds*.5),"same capsule, different exposed length");
            Test(trap.name+" individual spear gaps are not a filled room collider",!trap.IntersectsCapsule(centre,.03f,height,attack+trap.attackSeconds*.5),"small capsule between the centre columns");
            Test(trap.name+" moving capsule cannot tunnel through exposed spears",trap.SweptIntersectsCapsule(centre-Vector3.forward*3,centre+Vector3.forward*3,
                radius,height,height,attack+trap.attackSeconds*.3,attack+trap.attackSeconds*.6),"swept player movement");
        }
    }

    void Test(string name,bool passed,string detail)
    {
        checks.Add(new Check{name=name,passed=passed,detail=detail});
        if(!passed)Debug.LogError(name+": "+detail);
        Save();
    }
    void Save()
    {
        Directory.CreateDirectory("TestResults");
        File.WriteAllText("TestResults/clockwork-validation.json",JsonUtility.ToJson(new Report{utc=DateTime.UtcNow.ToString("O"),passed=checks.Count(c=>c.passed),failed=checks.Count(c=>!c.passed),checks=checks},true));
    }
}
#endif
