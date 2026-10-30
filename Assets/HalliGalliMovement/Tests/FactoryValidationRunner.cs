#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalliGalli.Movement;
using UnityEditor;
using UnityEngine;

public sealed class FactoryValidationRunner:MonoBehaviour
{
    public bool probeOnly;
    [Serializable]class Check{public string name,detail;public bool passed;}
    [Serializable]class Report{public int passed,failed;public List<Check> checks;}
    readonly List<Check> checks=new List<Check>();
    IEnumerator Start()
    {
        var run=Validate();while(true){bool more;object next=null;try{more=run.MoveNext();if(more)next=run.Current;}catch(Exception e){Test("Runner completes",false,e.ToString());break;}if(!more)break;yield return next;}
        Time.timeScale=1;Save();Debug.Log("FACTORY_VALIDATION_RESULT "+checks.Count(c=>c.passed)+"/"+checks.Count);EditorApplication.isPlaying=false;
    }
    IEnumerator Validate()
    {
        yield return null;yield return null;
        var session=FindAnyObjectByType<CourseSession>();var roster=session.roster;var factory=session.factory;
        session.cardGame.enabled=false;session.cardGame.botEnabled=false;FindAnyObjectByType<CourseHUD>().ReadInput=false;
        var controller=session.Local.GetComponent<SourcePlayerController>();controller.controlsEnabled=false;controller.SetCursorCaptured(false);
        foreach(var p in roster.allParticipants){p.Combat.readInput=false;var b=p.GetComponent<CourseRival>();if(b){b.enabled=false;b.attacksEnabled=false;}}
        Time.timeScale=4;
        Test("Factory appears as fourth playable map",session.maps.current.id=="factory"&&session.maps.catalog.maps.Count(m=>m.playable)==4,"map="+session.maps.current.id);
        Test("Two shuttles, two presses, two conveyors and safe checkpoint",factory.movingPlatforms.Length==2&&factory.presses.Length==2&&FindObjectsByType<FactorySurface>().Count(s=>s.beltVelocity.sqrMagnitude>0)==2&&session.checkpoints.points.Length==1,"machinery installed");
        var runner=roster.allParticipants[1];var bot=runner.GetComponent<CourseRival>();
        foreach(int count in probeOnly?new[]{6}:new[]{2,3,6}){
            roster.StartNewGame(count);yield return null;foreach(var p in session.participants){var b=p.GetComponent<CourseRival>();if(b)b.enabled=false;}
            Test(count+" player starts have equal first-jump distance",session.participants.Select(p=>Vector3.Distance(p.standAnchor.position,roster.firstPlatforms[Array.IndexOf(session.participants,p)].position)).Max()-session.participants.Select(p=>Vector3.Distance(p.standAnchor.position,roster.firstPlatforms[Array.IndexOf(session.participants,p)].position)).Min()<.001f,"same row and first ledges");
            var starts=session.participants.Select(p=>p.standAnchor.position).ToArray();
            for(int seat=0;seat<(probeOnly?1:count);seat++){
                session.ResetPractice();yield return new WaitForFixedUpdate();
                bot.route=roster.RouteForSeat(seat);runner.standAnchor.position=starts[seat];runner.retryAnchor.position=runner.standAnchor.position;
                runner.Stand();bot.enabled=true;float start=Time.time,end=start+65,next=0;var trace=new List<string>();
                while(!session.BellRung&&Time.time<end){
                    if(Time.time>=next){next=Time.time+.5f;trace.Add((Time.time-start).ToString("F1")+" w="+bot.Waypoint+" p="+runner.transform.position+" v="+runner.Motor.Velocity+" f="+runner.Falls);}
                    yield return new WaitForFixedUpdate();
                }
                bot.enabled=false;float seconds=Time.time-start;
                Test(count+" seats, start "+seat+": physically completes machine route",session.BellRinger==runner&&runner.Falls<=1&&seconds>12&&seconds<50,"seconds="+seconds+" falls="+runner.Falls+"\n"+string.Join("\n",trace));
                if(probeOnly)yield break;
            }
        }
        roster.StartNewGame(6);yield return null;foreach(var p in roster.allParticipants){var b=p.GetComponent<CourseRival>();if(b)b.enabled=false;}
        session.ResetPractice();runner.Stand();var belt=FindObjectsByType<FactorySurface>().First(s=>s.name=="Cross conveyor");
        runner.Motor.Teleport(belt.transform.position+Vector3.up*.225f);Vector3 before=runner.transform.position;
        for(int i=0;i<50;i++){runner.Motor.Simulate(default,Time.fixedDeltaTime);yield return new WaitForFixedUpdate();}
        Test("Standing rider is carried by conveyor",runner.transform.position.x-before.x>.65f&&runner.Motor.Grounded,"delta="+(runner.transform.position-before));
        var shuttle=factory.movingPlatforms[0];runner.Motor.Teleport(shuttle.transform.position+Vector3.up*.225f);
        for(int i=0;i<100;i++){runner.Motor.Simulate(default,Time.fixedDeltaTime);yield return new WaitForFixedUpdate();}
        Test("Rider stays supported on a moving shuttle",Mathf.Abs(runner.transform.position.z-shuttle.transform.position.z)<.12f&&runner.Motor.Grounded,"relative="+(runner.transform.position-shuttle.transform.position));
        runner.Motor.Teleport(belt.transform.position+Vector3.up*2);before=runner.transform.position;
        for(int i=0;i<8;i++){runner.Motor.Simulate(default,Time.fixedDeltaTime);yield return new WaitForFixedUpdate();}
        Test("Conveyor does not drag airborne players",Mathf.Abs(runner.transform.position.x-before.x)<.01f,"air delta="+(runner.transform.position-before));
        var point=session.checkpoints.points[0];runner.Motor.Teleport(point.spawn.position);
        for(int i=0;i<4;i++){runner.Motor.Simulate(default,Time.fixedDeltaTime);yield return new WaitForFixedUpdate();}
        Test("Grounded maintenance landing saves checkpoint",runner.CheckpointIndex==0,"checkpoint="+runner.CheckpointIndex);
        var press=factory.presses[0];press.enabled=false;
        Test("Amber warning precedes downward stroke",press.Threatens(press.period*.5)&&press.Extension(press.period*.5)==0,"warning at half cycle");
        press.Apply(press.period*.75);runner.Motor.Teleport(new Vector3(10,.725f,17));yield return new WaitForFixedUpdate();yield return null;
        Test("Press contact triggers recovery",runner.IsRespawning&&runner.Falls==1,"falls="+runner.Falls);
        yield return new WaitForSeconds(.85f);
        Test("Press victim returns to safe checkpoint",!runner.IsRespawning&&Vector3.Distance(runner.transform.position,point.spawn.position)<.1f,"position="+runner.transform.position);
        press.enabled=true;runner.Motor.AddImpulse(Vector3.right*5);bool requested=runner.RequestRespawn();yield return new WaitForSeconds(.85f);
        Test("R recovery keeps checkpoint and stops momentum",requested&&runner.CheckpointIndex==0&&runner.Motor.HorizontalSpeed<.1f&&Vector3.Distance(runner.transform.position,point.spawn.position)<.1f,"R recovered");
        float originalPhase=press.phase;
        foreach(bool crouch in new[]{false,true}){
            session.ResetPractice();yield return new WaitForFixedUpdate();runner.Stand();
            if(crouch)runner.SetCheckpoint(0,0);
            press.phase=Mathf.Repeat(.5f-(float)(FactorySurface.Clock/press.period),1);
            press.Apply(FactorySurface.Clock);runner.Motor.Teleport(new Vector3(10,.725f,17));
            float deadline=Time.time+2;
            while(!runner.IsRespawning&&Time.time<deadline){runner.Motor.Simulate(new MovementCommand{crouch=crouch},Time.fixedDeltaTime);yield return new WaitForFixedUpdate();}
            Test("Descending press catches moving capsule crouch="+crouch,runner.IsRespawning&&runner.Falls==1&&runner.transform.position.y>0,"position="+runner.transform.position+" falls="+runner.Falls);
            yield return new WaitForSeconds(.85f);
            Vector3 expected=crouch?point.spawn.position:runner.retryAnchor.position;
            Test("Crush returns to checkpoint or start crouch="+crouch,!runner.IsRespawning&&Vector3.Distance(runner.transform.position,expected)<.15f,"position="+runner.transform.position);
        }
        press.phase=originalPhase;
        session.ResetPractice();Test("Bell reset clears saved checkpoint",runner.CheckpointIndex==-1&&session.EveryoneSeated,"round reset");
    }
    void Test(string name,bool passed,string detail){checks.Add(new Check{name=name,passed=passed,detail=detail});if(!passed)Debug.LogError(name+" "+detail);Save();}
    void Save(){Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/"+(probeOnly?"factory-probe":"factory-validation")+".json",JsonUtility.ToJson(new Report{passed=checks.Count(c=>c.passed),failed=checks.Count(c=>!c.passed),checks=checks},true));}
}
#endif
