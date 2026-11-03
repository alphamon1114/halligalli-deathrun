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

        // Every orientation gets an actual stationary victim, in addition to sampled geometry regressions.
        foreach(var trap in course.traps){
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
        session.ResetPractice();yield return fixedStep;
        // Synchronize departure to the authored ferry clock without changing any machine's phase.
        double departure=Math.Ceiling((CourseNetwork.WorldTime+.1)/10)*10;
        while(CourseNetwork.WorldTime<departure)yield return fixedStep;
        runner.Stand();bot.enabled=true;
        int checkpointMask=0,trapMask=0,pressMask=0,jumpMask=0,turnMask=0;
        int previousJumps=runner.Motor.JumpCount,lastWaypoint=0;
        float started=Time.time,nextTrace=0,stationaryWait=0,boardingWait=0,ridingTime=0,carriedDistance=0;
        bool boarded=false,exited=false;
        var trace=new List<string>();
        var ferry=course.ferries[0];var carrierBox=ferry.carrier.GetComponent<BoxCollider>();
        Vector3 previousFeet=runner.transform.position,previousCarrier=ferry.carrier.transform.position;
        bool previousAboard=false;
        while(!session.BellRung&&Time.time-started<150){
            Vector3 feet=runner.transform.position;
            if(runner.CheckpointIndex>=0)checkpointMask|=1<<runner.CheckpointIndex;
            for(int index=0;index<course.traps.Length;index++){
                var trap=course.traps[index];var before=trap.transform.InverseTransformPoint(previousFeet);var after=trap.transform.InverseTransformPoint(feet);
                if(before.z<=0&&after.z>0&&Mathf.Abs(after.x)<trap.zoneSize.x*.5f&&Mathf.Abs(after.y)<.3f)trapMask|=1<<index;
            }
            for(int index=0;index<course.presses.Length;index++){
                var press=course.presses[index];var rotation=Quaternion.Inverse(press.head.rotation);
                var before=rotation*(previousFeet-press.raised);var after=rotation*(feet-press.raised);
                if(before.z<=0&&after.z>0&&Mathf.Abs(after.x)<4&&feet.y<.3f)pressMask|=1<<index;
            }
            if(runner.Motor.JumpCount>previousJumps){
                if(bot.Waypoint==3)jumpMask|=1;
                if(bot.Waypoint==5)jumpMask|=2;
                if(bot.Waypoint==34)jumpMask|=4;
                previousJumps=runner.Motor.JumpCount;
            }
            if(bot.Waypoint!=lastWaypoint){
                if(bot.Waypoint==10)turnMask|=1;
                if(bot.Waypoint==19)turnMask|=2;
                if(bot.Waypoint==26)turnMask|=4;
                lastWaypoint=bot.Waypoint;
            }
            var destination=bot.route[Mathf.Clamp(bot.Waypoint,0,bot.route.Length-1)].position;
            bool machineWait=runner.Motor.Grounded&&course.HoldForTrap(feet,destination);
            bool ferryWait=course.AdjustBotTarget(runner,bot.Waypoint,ref destination);
            if(machineWait&&runner.Motor.HorizontalSpeed<.15f)stationaryWait+=Time.fixedDeltaTime;
            if(ferryWait&&bot.Waypoint==ferry.boardWaypoint&&runner.Motor.HorizontalSpeed<.15f)boardingWait+=Time.fixedDeltaTime;
            bool aboard=runner.Motor.Grounded&&Physics.Raycast(feet+Vector3.up*.15f,Vector3.down,out var support,.4f,
                ~((1<<8)|(1<<9)),QueryTriggerInteraction.Ignore)&&support.collider==carrierBox;
            if(aboard){
                boarded=true;ridingTime+=Time.fixedDeltaTime;
                if(previousAboard&&runner.Motor.HorizontalSpeed<.15f){
                    var platformDelta=ferry.carrier.transform.position-previousCarrier;
                    var playerDelta=feet-previousFeet;
                    if((playerDelta-platformDelta).magnitude<.025f)carriedDistance+=platformDelta.magnitude;
                }
            }
            if(boarded&&bot.Waypoint>ferry.exitWaypoint&&runner.Motor.Grounded)exited=true;
            if(Time.time>=nextTrace){
                nextTrace=Time.time+.5f;
                trace.Add((Time.time-started).ToString("F1")+" w="+bot.Waypoint+" p="+feet+" grounded="+runner.Motor.Grounded
                    +" jumps="+runner.Motor.JumpCount+" aboard="+aboard+" falls="+runner.Falls);
            }
            previousFeet=feet;previousCarrier=ferry.carrier.transform.position;previousAboard=aboard;
            yield return fixedStep;
        }
        bot.enabled=false;
        string routeDetail="seconds="+(Time.time-started)+" falls="+runner.Falls+" departure="+departure+"\n"+string.Join("\n",trace);
        Test("Solo bot physically completes mixed fortress and rings without falling",session.BellRinger==runner&&runner.Falls==0,routeDetail);
        Test("Physical route visits all three safe checkpoints",checkpointMask==7,"visitedMask="+checkpointMask);
        Test("Physical route crosses all six oriented traps",trapMask==63,"crossedMask="+trapMask);
        Test("Physical route crosses both oriented presses",pressMask==3,"crossedMask="+pressMask);
        Test("Physical route jumps both starter gaps and final gap",jumpMask==7,"jumpMask="+jumpMask);
        Test("Physical route makes right and both left turns",turnMask==7,"turnMask="+turnMask);
        Test("Physical route waits outside active machines",stationaryWait>.25f,"stationarySeconds="+stationaryWait);
        Test("Physical route waits for the boarding ferry",boardingWait>.25f,"boardingWaitSeconds="+boardingWait);
        Test("Physical route boards and rides the real moving collider",boarded&&ridingTime>1,"ridingSeconds="+ridingTime);
        Test("Physical route is carried without walking on the ferry",carriedDistance>3,"carriedMetres="+carriedDistance);
        Test("Physical route disembarks onto the far dock",exited,"exited="+exited);
        if(session.BellRung){
            int falls=runner.Falls;
            runner.Motor.Teleport(spear.transform.TransformPoint(new Vector3(0,.025f,0)));
            yield return new WaitForSeconds(spear.period+1);
            Test("Returning bell winner still respawns on physical trap contact",runner.Falls==falls+1&&!runner.IsRespawning,"falls="+runner.Falls);
            foreach(var press in course.presses){
                falls=runner.Falls;
                runner.Motor.Teleport(new Vector3(press.raised.x,.025f,press.raised.z));
                yield return new WaitForSeconds(press.period+1);
                Test(press.name+" returning bell winner is physically crushed and respawned",runner.Falls==falls+1&&!runner.IsRespawning,"falls="+runner.Falls);
            }
        }
        session.ResetPractice();
        int seatedHits=spear.Hits;
        runner.Motor.Teleport(spear.transform.TransformPoint(new Vector3(0,.025f,0)));
        yield return new WaitForSeconds(spear.period+.1f);
        Test("Seated participants are excluded even inside attack geometry",runner.IsSeated&&runner.Falls==0&&spear.Hits==seatedHits,"hits="+(spear.Hits-seatedHits));
        runner.Stand();session.ApplyNetworkState(false,session.BellCount);
        runner.Motor.Teleport(spear.transform.TransformPoint(new Vector3(0,.025f,0)));
        yield return new WaitForSeconds(spear.period+1);
        Test("Standing player still takes trap damage outside RunActive",!runner.IsRespawning&&runner.Falls==1&&!session.RunActive,"falls="+runner.Falls);
        foreach(var press in course.presses){
            session.ResetPractice();
            runner.Motor.Teleport(new Vector3(press.raised.x,.025f,press.raised.z));
            yield return new WaitForSeconds(press.period+.1f);
            Test(press.name+" seated participant is excluded from crushing",runner.IsSeated&&runner.Falls==0,"falls="+runner.Falls);
            session.ResetPractice();runner.Stand();
            var checkpoint=session.checkpoints.points[0];
            runner.Motor.Teleport(checkpoint.spawn.position);
            for(int step=0;step<5;step++){runner.Motor.Simulate(default,Time.fixedDeltaTime);yield return fixedStep;}
            double deadline=CourseNetwork.WorldTime+press.period+1;
            while(press.Cycle(CourseNetwork.WorldTime)>.1f&&CourseNetwork.WorldTime<deadline)yield return fixedStep;
            runner.Motor.Teleport(new Vector3(press.raised.x,.025f,press.raised.z));
            bool openWasSafe=true,sawOpen=false;
            deadline=CourseNetwork.WorldTime+press.period+1;
            while(!runner.IsRespawning&&CourseNetwork.WorldTime<deadline){
                if(press.Cycle(CourseNetwork.WorldTime)<.43f){sawOpen=true;openWasSafe&=runner.Falls==0;}
                runner.Motor.Simulate(default,Time.fixedDeltaTime);yield return fixedStep;
            }
            Test(press.name+" actual oriented press is safe while raised",openWasSafe&&sawOpen,"openObserved="+sawOpen);
            Test(press.name+" descending physical head crushes standing player once",runner.IsRespawning&&runner.Falls==1,"falls="+runner.Falls);
            yield return new WaitForSeconds(.8f);
            Test(press.name+" physical crush respawns at saved safe checkpoint",!runner.IsRespawning&&runner.CheckpointIndex==0
                &&Vector3.Distance(runner.transform.position,checkpoint.spawn.position)<.15f,"position="+runner.transform.position);
            session.ResetPractice();runner.Stand();session.ApplyNetworkState(false,session.BellCount);
            runner.Motor.Teleport(new Vector3(press.raised.x,.025f,press.raised.z));
            yield return new WaitForSeconds(press.period+1);
            Test(press.name+" standing player is crushed outside RunActive",runner.Falls==1&&!runner.IsRespawning&&!session.RunActive,"falls="+runner.Falls);
        }
        session.ResetPractice();runner.Stand();
        double carryDeadline=CourseNetwork.WorldTime+ferry.carrier.period+1;
        while((ferry.carrier.PositionAt(CourseNetwork.WorldTime+.5)-ferry.carrier.PositionAt(CourseNetwork.WorldTime)).magnitude<1.5f
            &&CourseNetwork.WorldTime<carryDeadline)yield return fixedStep;
        runner.Motor.Teleport(ferry.carrier.transform.position+Vector3.up*ferry.carrier.standingOffset);
        runner.Motor.Simulate(default,Time.fixedDeltaTime);yield return fixedStep;
        Vector3 carryStart=runner.transform.position,shipStart=ferry.carrier.transform.position;
        int carryJumps=runner.Motor.JumpCount;bool stayedGrounded=true;float carryError=0;
        for(int step=0;step<25;step++){
            runner.Motor.Simulate(default,Time.fixedDeltaTime);yield return fixedStep;
            stayedGrounded&=runner.Motor.Grounded;
            carryError=Mathf.Max(carryError,((runner.transform.position-carryStart)-(ferry.carrier.transform.position-shipStart)).magnitude);
        }
        Test("Idle capsule is physically carried by the moving platform",stayedGrounded&&runner.Falls==0&&runner.Motor.JumpCount==carryJumps
            &&Vector3.Distance(runner.transform.position,carryStart)>1&&carryError<.15f,"distance="+Vector3.Distance(runner.transform.position,carryStart)+" relativeError="+carryError);
        session.cardGame.enabled=true;
        foreach(var trap in new[]{spear,arrows}){
            session.ResetPractice();
            bool stood=runner.Stand();
            Test(trap.kind+" wrong-card stand is a returning player",stood&&session.MustReturn(runner)&&!runner.Racing,"mustReturn="+session.MustReturn(runner));
            runner.Motor.Teleport(trap.transform.TransformPoint(new Vector3(0,.025f,0)));
            yield return new WaitForSeconds(trap.period+1);
            Test(trap.kind+" wrong-card stand is hit and respawned",runner.Falls==1&&!runner.IsRespawning
                &&Vector3.Distance(runner.transform.position,runner.standAnchor.position)<.15f,"falls="+runner.Falls+" position="+runner.transform.position);
        }
        foreach(var press in course.presses){
            session.ResetPractice();runner.Stand();
            Test(press.name+" wrong-card stand is a returning player",session.MustReturn(runner)&&!runner.Racing,"mustReturn="+session.MustReturn(runner));
            runner.Motor.Teleport(new Vector3(press.raised.x,.025f,press.raised.z));
            yield return new WaitForSeconds(press.period+1);
            Test(press.name+" wrong-card stand is crushed and respawned",runner.Falls==1&&!runner.IsRespawning
                &&Vector3.Distance(runner.transform.position,runner.standAnchor.position)<.15f,"falls="+runner.Falls+" position="+runner.transform.position);
        }
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
        double contactTime=attack+trap.attackSeconds*(trap.kind==ClockworkTrapKind.Arrows?1d/12:.5);
        Test(trap.name+" deterministic safe-warning-attack cycle",
            trap.Sample(origin+.1).stage==ClockworkTrapStage.Safe&&trap.Sample(warning).stage==ClockworkTrapStage.Warning
            &&mid.stage==ClockworkTrapStage.Attack&&trap.Sample(origin+trap.period+.1).stage==ClockworkTrapStage.Safe
            &&Mathf.Abs(trap.Sample(attack+trap.attackSeconds*.5+trap.period*100000d).extension-mid.extension)<.0001f,
            "period="+trap.period+" safe="+trap.SafeSeconds);
        Test(trap.name+" no safe or warning collision",!trap.IntersectsCapsule(centre,radius,height,origin+.1)
            &&!trap.IntersectsCapsule(centre,radius,height,warning),"capsule="+centre);
        Test(trap.name+" active geometry contacts standing capsule",trap.IntersectsCapsule(centre,radius,height,contactTime),"capsule="+centre);
        Test(trap.name+" no whole-room or overhead kill",!trap.IntersectsCapsule(centre+trap.transform.forward*3,radius,height,contactTime)
            &&!trap.IntersectsCapsule(centre+Vector3.up*3,radius,height,contactTime),"outside and above geometry");
        if(trap.kind==ClockworkTrapKind.Arrows){
            double before=attack+trap.attackSeconds*.01,after=attack+trap.attackSeconds*.16;
            Test(trap.name+" fast arrow sweep catches between endpoint samples",!trap.IntersectsCapsule(centre,radius,height,before)
                &&!trap.IntersectsCapsule(centre,radius,height,after)&&trap.SweptIntersectsCapsule(centre,centre,radius,height,height,before,after),"actual individual arrow volumes");
            Test(trap.name+" projectile reset does not sweep backwards through room",!trap.SweptIntersectsCapsule(centre,centre,radius,height,height,
                attack+trap.attackSeconds*.98,origin+trap.period+.1),"end-to-start discontinuity excluded");
            Test(trap.name+" far side remains safe before arrow arrives",!trap.IntersectsCapsule(trap.transform.TransformPoint(new Vector3(2,.025f,0)),radius,height,before),"attack is spatial, not a firing-zone trigger");
            double slot=trap.attackSeconds/3d;
            var renderers=trap.movingRoot.GetComponentsInChildren<Renderer>(true);
            for(int shot=0;shot<3;shot++){
                double launch=attack+shot*slot,centreTime=launch+slot*.25,gap=launch+slot*.75;
                trap.Apply(centreTime);
                Test(trap.name+" burst shot "+(shot+1)+" visibly crosses and hits",
                    renderers.All(r=>r.enabled)&&trap.IntersectsCapsule(centre,radius,height,centreTime),"shot="+(shot+1));
                trap.Apply(gap);
                Test(trap.name+" burst gap "+(shot+1)+" is invisible and harmless",
                    renderers.All(r=>!r.enabled)&&!trap.IntersectsCapsule(centre,radius,height,gap)
                    &&!trap.SweptIntersectsCapsule(centre,centre,radius,height,height,launch+slot*.6,launch+slot*.95),"gap="+gap);
                if(shot<2)Test(trap.name+" burst reset "+(shot+1)+" cannot hit backwards",
                    !trap.SweptIntersectsCapsule(centre,centre,radius,height,height,launch+slot*.95,launch+slot*1.05),"separate projectile launches");
            }
            trap.Apply(attack+slot*.05);Vector3 near=trap.movingRoot.position;
            trap.Apply(attack+slot*.45);Vector3 far=trap.movingRoot.position;
            float speed=Vector3.Distance(near,far)/(float)(slot*.4);
            Test(trap.name+" rapid arrows cross at 50 metres per second",Mathf.Abs(speed-50)<.01f,"speed="+speed);
            Test(trap.name+" post-burst green window is clear for crossing",
                !trap.SweptIntersectsCapsule(centre-trap.transform.forward*3,centre+trap.transform.forward*3,radius,height,height,
                    origin+trap.period+.1,origin+trap.period+trap.SafeSeconds-.1),"safeSeconds="+trap.SafeSeconds);
        }else{
            Test(trap.name+" retracted spear never damages above its exposed tip",!trap.IntersectsCapsule(centre+Vector3.up*.5f,radius,height,attack+trap.attackSeconds*.01)
                &&trap.IntersectsCapsule(centre+Vector3.up*.5f,radius,height,attack+trap.attackSeconds*.5),"same capsule, different exposed length");
            Test(trap.name+" individual spear gaps are not a filled room collider",!trap.IntersectsCapsule(centre,.03f,height,attack+trap.attackSeconds*.5),"small capsule between the centre columns");
            Test(trap.name+" moving capsule cannot tunnel through exposed spears",trap.SweptIntersectsCapsule(centre-trap.transform.forward*3,centre+trap.transform.forward*3,
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
