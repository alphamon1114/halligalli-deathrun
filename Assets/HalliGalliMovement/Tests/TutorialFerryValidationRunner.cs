#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalliGalli.Movement;
using UnityEditor;
using UnityEngine;

public sealed class TutorialFerryValidationRunner:MonoBehaviour
{
    public bool probeOnly;
    [Serializable]class Check{public string name,detail;public bool passed;}
    [Serializable]class Report{public int passed,failed;public List<Check> checks;}
    readonly List<Check> checks=new List<Check>();
    IEnumerator Start()
    {
        var run=Validate();while(true){bool more;object next=null;try{more=run.MoveNext();if(more)next=run.Current;}catch(Exception e){Test("Runner completes",false,e.ToString());break;}if(!more)break;yield return next;}
        Time.timeScale=1;Save();Debug.Log("FERRY_VALIDATION_RESULT "+checks.Count(c=>c.passed)+"/"+checks.Count);EditorApplication.isPlaying=false;
    }
    IEnumerator Validate()
    {
        yield return null;yield return null;
        var session=FindAnyObjectByType<CourseSession>();var roster=session.roster;var ferry=session.ferry;
        session.cardGame.enabled=false;session.cardGame.botEnabled=false;FindAnyObjectByType<CourseHUD>().ReadInput=false;
        var controller=session.Local.GetComponent<SourcePlayerController>();controller.controlsEnabled=false;controller.SetCursorCaptured(false);
        foreach(var p in roster.allParticipants){p.Combat.readInput=false;var b=p.GetComponent<CourseRival>();if(b){b.enabled=false;b.attacksEnabled=false;}}
        Time.timeScale=4;
        Test("Tutorial keeps its map and card setup",session.maps.current.id=="first-steps"&&ferry&&session.checkpoints.points.Length==1,"tutorial ferry installed");
        Test("Crossing requires the ferry at ordinary walking speed",ferry.arrivalDock.bounds.min.z-ferry.boardingDock.bounds.max.z>7,"dock gap="+(ferry.arrivalDock.bounds.min.z-ferry.boardingDock.bounds.max.z));
        Test("Shared ferry carries several racers with open edges",ferry.carrier.GetComponent<BoxCollider>().size.x>=4&&ferry.boardingDock.size.x>=4.5f,"shared deck widths");
        Test("Two long-travel ferries run in opposite phase",ferry.secondCarrier&&ferry.carrier.travel.z*2>=5.5f&&ferry.secondCarrier.travel.z*2>=5.5f&&Mathf.Abs(ferry.carrier.phase-ferry.secondCarrier.phase)==.5f,"two 5.6m strokes");
        float nearest=float.MaxValue,farthest=0;
        for(int sample=0;sample<100;sample++){double t=sample*ferry.carrier.period/100;float gap=ferry.secondCarrier.PositionAt(t).z-ferry.carrier.PositionAt(t).z-2.6f;nearest=Mathf.Min(nearest,gap);farthest=Mathf.Max(farthest,gap);}
        Test("Transfer opens briefly without decks overlapping",nearest>.25f&&nearest<.7f&&farthest>10,"gap range="+nearest+".."+farthest);
        var runner=roster.allParticipants[1];var bot=runner.GetComponent<CourseRival>();bool observedWait=false,observedTransferWait=false;
        foreach(int count in probeOnly?new[]{6}:new[]{2,3,6}){
            roster.StartNewGame(count);yield return null;foreach(var p in session.participants){var b=p.GetComponent<CourseRival>();if(b)b.enabled=false;}
            var starts=session.participants.Select(p=>p.standAnchor.position).ToArray();
            Test(count+" seats retain equal first-jump distance",starts.Select((v,i)=>Vector3.Distance(v,roster.firstPlatforms[i].position)).Max()-starts.Select((v,i)=>Vector3.Distance(v,roster.firstPlatforms[i].position)).Min()<.001f,"equal starts");
            for(int seat=0;seat<(probeOnly?1:count);seat++){
                session.ResetPractice();yield return new WaitForFixedUpdate();
                bot.route=roster.RouteForSeat(seat);runner.standAnchor.position=starts[seat];runner.retryAnchor.position=runner.standAnchor.position;
                runner.Stand();bot.enabled=true;float start=Time.time,end=start+40,next=0;var trace=new List<string>();
                while(!session.BellRung&&Time.time<end){
                    if(bot.Waypoint==4&&runner.Motor.Grounded&&runner.Motor.HorizontalSpeed<.1f&&runner.transform.position.z<13.5f)observedWait=true;
                    if(bot.Waypoint==5&&runner.Motor.Grounded&&runner.Motor.HorizontalSpeed<.1f)observedTransferWait=true;
                    if(Time.time>=next){next=Time.time+.5f;trace.Add((Time.time-start).ToString("F1")+" w="+bot.Waypoint+" p="+runner.transform.position+" ferry="+ferry.carrier.transform.position+" v="+runner.Motor.Velocity+" f="+runner.Falls);}
                    yield return new WaitForFixedUpdate();
                }
                bot.enabled=false;
                Test(count+" seats, start "+seat+": waits, transfers and reaches bell",session.BellRinger==runner&&runner.Falls==0&&Time.time-start<40,"seconds="+(Time.time-start)+" falls="+runner.Falls+"\n"+string.Join("\n",trace));
                if(probeOnly)yield break;
                yield return new WaitForSeconds(.9f);
            }
        }
        Test("Outbound phase creates an actual waiting opportunity",observedWait,"observed grounded waiting before boarding");
        Test("Opposite phase creates a transfer timing window",observedTransferWait,"observed waiting on first ferry for the second");
        roster.StartNewGame(6);yield return null;foreach(var p in roster.allParticipants){var b=p.GetComponent<CourseRival>();if(b)b.enabled=false;}
        session.ResetPractice();runner.Stand();runner.Motor.Teleport(ferry.waitingPoint);
        for(int i=0;i<4;i++){runner.Motor.Simulate(default,Time.fixedDeltaTime);yield return new WaitForFixedUpdate();}
        Test("Waiting dock saves checkpoint",runner.CheckpointIndex==0,"checkpoint="+runner.CheckpointIndex);
        runner.Motor.Teleport(ferry.carrier.transform.position+Vector3.up*.225f);
        for(int i=0;i<150;i++){runner.Motor.Simulate(default,Time.fixedDeltaTime);yield return new WaitForFixedUpdate();}
        Test("Stationary rider is carried fore and aft",runner.Motor.Grounded&&Mathf.Abs(runner.transform.position.z-ferry.carrier.transform.position.z)<.15f,"relative="+(runner.transform.position-ferry.carrier.transform.position));
        runner.Motor.Teleport(ferry.secondCarrier.transform.position+Vector3.up*.225f);
        for(int i=0;i<150;i++){runner.Motor.Simulate(default,Time.fixedDeltaTime);yield return new WaitForFixedUpdate();}
        Test("Second ferry also carries a stationary rider",runner.Motor.Grounded&&Mathf.Abs(runner.transform.position.z-ferry.secondCarrier.transform.position.z)<.15f,"relative="+(runner.transform.position-ferry.secondCarrier.transform.position));
        runner.Motor.AddImpulse(Vector3.right*12+Vector3.up*3);
        float deadline=Time.time+4;while(runner.Falls==0&&Time.time<deadline){if(!runner.IsRespawning)runner.Motor.Simulate(default,Time.fixedDeltaTime);yield return new WaitForFixedUpdate();}
        yield return new WaitForSeconds(.85f);
        Test("Racer can be knocked off ferry and returns to waiting dock",runner.Falls==1&&!runner.IsRespawning&&Vector3.Distance(runner.transform.position,ferry.waitingPoint)<.1f,"position="+runner.transform.position);
        runner.RequestRespawn();yield return new WaitForSeconds(.85f);
        Test("Manual R recovery uses the same waiting dock",runner.Falls==2&&runner.CheckpointIndex==0&&Vector3.Distance(runner.transform.position,ferry.waitingPoint)<.1f,"R checkpoint preserved");
    }
    void Test(string name,bool passed,string detail){checks.Add(new Check{name=name,passed=passed,detail=detail});if(!passed)Debug.LogError(name+" "+detail);Save();}
    void Save(){Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/"+(probeOnly?"ferry-probe":"ferry-validation")+".json",JsonUtility.ToJson(new Report{passed=checks.Count(c=>c.passed),failed=checks.Count(c=>!c.passed),checks=checks},true));}
}
#endif
