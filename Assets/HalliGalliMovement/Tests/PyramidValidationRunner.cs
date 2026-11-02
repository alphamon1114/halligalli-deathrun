#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalliGalli.Movement;
using UnityEditor;
using UnityEngine;

public sealed class PyramidValidationRunner:MonoBehaviour
{
    public bool probeOnly;
    [Serializable]class Check{public string name,detail;public bool passed;}
    [Serializable]class Report{public string utc;public int passed,failed;public List<Check> checks;}
    readonly List<Check> checks=new List<Check>();
    IEnumerator Start()
    {
        var run=Validate();while(true){bool more;object next=null;try{more=run.MoveNext();if(more)next=run.Current;}catch(Exception e){Test("Runner completes",false,e.ToString());break;}if(!more)break;yield return next;}
        Time.timeScale=1;Save();Debug.Log("PYRAMID_VALIDATION_RESULT "+checks.Count(c=>c.passed)+"/"+checks.Count);
        if(Application.isBatchMode){EditorApplication.Exit(checks.Any(c=>!c.passed)?1:0);yield break;}
        EditorApplication.isPlaying=false;
    }
    IEnumerator Validate()
    {
        yield return null;yield return null;
        var session=FindAnyObjectByType<CourseSession>();var roster=session.roster;var surf=roster.surf;var pyramid=surf.GetComponent<PyramidCourse>();
        session.cardGame.enabled=false;session.cardGame.botEnabled=false;FindAnyObjectByType<CourseHUD>().ReadInput=false;
        var control=session.Local.GetComponent<SourcePlayerController>();control.controlsEnabled=false;control.SetCursorCaptured(false);
        foreach(var p in roster.allParticipants){p.Combat.readInput=false;var b=p.GetComponent<CourseRival>();if(b){b.enabled=false;b.attacksEnabled=false;}}
        Time.timeScale=5;
        Test("Five authored chambers and twenty-five two-sided triangular ramps",pyramid.chambers.Length==5&&surf.sections.Length==25&&surf.sections.All(s=>s.twoSided)&&FindObjectsByType<MeshCollider>().Count(c=>c.name.StartsWith("Pyramid bank "))==25,"");
        bool faces=true,aligned=true;
        for(int i=0;i<surf.sections.Length;i++){
            var s=surf.sections[i];int j=(s.points.Length-1)/2;var center=Vector3.Lerp(s.points[j],s.points[j+1],.5f);var right=Quaternion.Euler(0,Mathf.LerpAngle(s.headings[j],s.headings[j+1],.5f),0)*Vector3.right;
            var collider=GameObject.Find("Pyramid bank "+i).GetComponent<MeshCollider>();
            foreach(int side in new[]{-1,1}){
                var origin=center+right*(side*3.2f)+Vector3.up*10;
                faces &= collider.Raycast(new Ray(origin,Vector3.down),out var hit,20)&&Mathf.Abs(hit.point.y-center.y)<.1f&&Vector3.Dot(hit.normal,right)*side>.65f&&hit.normal.y>.4f&&hit.normal.y<.7f;
            }
            if(i>0&&surf.sections[i-1].landing==Vector3.zero){var previous=surf.sections[i-1];var lateral=Quaternion.Euler(0,previous.headings.Last(),0)*Vector3.right;aligned &= Mathf.Abs(Vector3.Dot(s.points[0]-previous.points.Last(),lateral))<.01f;}
        }
        Test("Both faces slope down away from the ridge and have collision",faces,"");
        Test("Transfers retain the same lanes without forced side swaps",aligned,"");
        float length=surf.sections.Sum(s=>s.Length),travel=length;
        var gaps=new List<float>();var elevationChanges=new List<float>();int rises=0,drops=0;bool monotonic=true;
        foreach(var s in surf.sections){
            bool up=false,down=false;
            for(int j=1;j<s.points.Length;j++){float dy=s.points[j].y-s.points[j-1].y;up|=dy>.0001f;down|=dy<-.0001f;}
            monotonic&=!(up&&down);if(up)rises++;if(down)drops++;
        }
        for(int i=1;i<surf.sections.Length;i++){
            var previous=surf.sections[i-1];Vector3 end=previous.points.Last(),next=surf.sections[i].points[0];
            if(previous.landing==Vector3.zero){
                gaps.Add(Vector3.Dot(next-end,Quaternion.Euler(0,previous.headings.Last(),0)*Vector3.forward));
                elevationChanges.Add(next.y-end.y);
                travel+=Vector3.Distance(end,next);
            }else travel+=Vector3.Distance(end,previous.landing)+Vector3.Distance(previous.landing,next);
        }
        travel+=Vector3.Distance(surf.sections.Last().points.Last(),surf.finish.position);
        Test("Ramp distance is approximately seventy percent of previous course",length>=644*.67f&&length<=644*.73f,"metres="+length);
        Test("Complete route including transfers is approximately seventy percent",travel>=875*.65f&&travel<=875*.75f,"metres="+travel);
        Test("Ascending ramps have no recessed middle trough",monotonic&&rises>=5&&drops>=5,"ascending="+rises+" descending="+drops);
        int turns=0;
        for(int i=1;i<pyramid.chambers.Length;i++){
            float a=surf.sections[pyramid.chambers[i-1].lastSection].headings.Last();
            float b=surf.sections[pyramid.chambers[i].firstSection].headings[0];
            if(Mathf.Abs(Mathf.Abs(Mathf.DeltaAngle(a,b))-90)<.01f)turns++;
        }
        Test("Checkpoint decks connect quarter-turn course legs",turns>=2,"quarter turns="+turns);
        Test("Transfers retain varied readable jump distances",gaps.Count(g=>g>1)>=8&&gaps.Select(g=>Mathf.Round(g*10)).Distinct().Count()>=3,string.Join(", ",gaps));
        Test("Transfers include higher and lower receiving ramps",elevationChanges.Any(y=>y>.25f)&&elevationChanges.Any(y=>y<-1),"height changes="+string.Join(", ",elevationChanges));
        Test("Four shared checkpoints match completed chambers",session.checkpoints.points.Select(p=>p.surfGates).SequenceEqual(new[]{4,9,14,20}),"");
        Test("Original surf physics retained",surf.sections.All(s=>s.halfWidth>=2.4f)&&session.Local.Motor.profile.moveSpeed==4.5f&&!session.Local.Motor.profile.autoBunnyHop&&session.Local.Motor.profile.maximumAirSpeed==0,"");
        var decoration=GameObject.Find("Pyramid decoration");var cues=GameObject.Find("Pyramid course cues");
        Test("Decoration and route cues cannot collide with racers",decoration&&cues&&decoration.GetComponentsInChildren<Collider>(true).Length==0&&cues.GetComponentsInChildren<Collider>(true).Length==0,"");
        var runner=roster.allParticipants[1];var bot=runner.GetComponent<CourseRival>();
        foreach(int count in new[]{2,3,6}){
            roster.StartNewGame(count);yield return null;
            foreach(var p in roster.allParticipants){var b=p.GetComponent<CourseRival>();if(b)b.enabled=false;}
            Test(count+" seats have equal first jump distance",session.participants.All(p=>Mathf.Abs(p.standAnchor.position.z+.7f)<.01f)&&roster.firstPlatforms.Take(count).All(p=>Mathf.Abs(p.position.z-2.3f)<.01f),"");
        }
        var starts=roster.allParticipants.Select(p=>p.standAnchor.position).ToArray();
        foreach(int seat in probeOnly?new[]{0,5}:new[]{0,2,5}){
            session.ResetPractice();yield return new WaitForFixedUpdate();
            bot.surfSidePreference=seat%2==0?-1:1;bot.route=roster.RouteForSeat(seat);runner.standAnchor.position=starts[seat];runner.retryAnchor.position=starts[seat];runner.Stand();
            bot.enabled=true;float begin=Time.time,deadline=begin+160,next=0;var trace=new List<string>();bool launched=false;
            while(!session.BellRung&&Time.time<deadline&&runner.Falls==0){
                launched|=!runner.Motor.Grounded&&!runner.Motor.Surfing&&runner.Motor.Velocity.y>1;
                if(Time.time>=next){next=Time.time+.5f;trace.Add((Time.time-begin).ToString("F2")+" s="+bot.SurfSection+"/"+bot.SurfPhase+" g="+surf.GatesPassed(runner)+" p="+runner.transform.position+" v="+runner.Motor.Velocity);}
                yield return new WaitForFixedUpdate();
            }
            bot.enabled=false;float seconds=Time.time-begin;
            Test("Start "+seat+" completes every bank without falls",session.BellRinger==runner&&surf.Completed(runner)&&runner.Falls==0,"seconds="+seconds+" falls="+runner.Falls+"\n"+string.Join("\n",trace));
            if(!probeOnly)Test("Start "+seat+" launches upward through real movement physics",launched,"seconds="+seconds);
        }
        if(probeOnly)yield break;
        for(int cp=0;cp<session.checkpoints.points.Length;cp++){
            session.ResetPractice();yield return new WaitForFixedUpdate();runner.Stand();var saved=session.checkpoints.points[cp];
            runner.SetCheckpoint(cp,0);surf.RestoreCheckpointProgress(runner,saved.surfGates);runner.Motor.Teleport(saved.spawn.position+Vector3.up*3);runner.RequestRespawn();yield return new WaitForSeconds(.85f);
            Test("Checkpoint "+cp+" restores R with correct gates",!runner.IsRespawning&&Vector3.Distance(runner.transform.position,saved.spawn.position)<.15f&&surf.GatesPassed(runner)==saved.surfGates,"");
            bot.surfSidePreference=cp%2==0?-1:1;bot.route=roster.RouteForSeat(0);bot.enabled=true;float deadline=Time.time+120;
            while(!session.BellRung&&runner.Falls==1&&Time.time<deadline)yield return new WaitForFixedUpdate();
            bot.enabled=false;Test("Checkpoint "+cp+" can finish normally",session.BellRinger==runner&&runner.Falls==1&&surf.Completed(runner),"gates="+surf.GatesPassed(runner)+" section="+bot.SurfSection+" position="+runner.transform.position);
        }
        for(int i=0;i<pyramid.chambers.Length;i++){
            session.ResetPractice();yield return new WaitForFixedUpdate();runner.Stand();var room=pyramid.chambers[i];
            var localPit=new Vector3(room.localMinimum.x+6,room.localMinimum.y+3,(room.localMinimum.z+room.localMaximum.z)*.5f);
            runner.Motor.Teleport(room.origin+Quaternion.Euler(0,room.heading,0)*localPit);
            float end=Time.time+1.5f;while(!runner.IsRespawning&&Time.time<end){runner.Motor.Simulate(default,Time.fixedDeltaTime);yield return new WaitForFixedUpdate();}
            Test("Chamber "+i+" pit recovers rather than reaching another route",runner.IsRespawning&&runner.Falls==1,"y="+runner.transform.position.y);
        }
        session.ResetPractice();yield return new WaitForFixedUpdate();runner.Stand();runner.Motor.Teleport(surf.finish.position);runner.Motor.Simulate(default,.02f);
        Test("Skipping to the royal bell cannot win",!session.CanRing(runner)&&surf.GatesPassed(runner)==0,"");
        Test("Sandstone shader supported",Shader.Find("HalliGalli/Sandstone").isSupported,"");
        foreach(var material in Resources.FindObjectsOfTypeAll<Material>().Where(m=>m.shader&&m.shader.name=="HalliGalli/Sandstone"))
            Test("No sandstone shader compile errors",!UnityEditor.ShaderUtil.GetShaderMessages(material.shader).Any(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error),material.name);
        session.ResetPractice();
    }
    void Test(string name,bool passed,string detail){checks.Add(new Check{name=name,passed=passed,detail=detail});if(!passed)Debug.LogError(name+" "+detail);Save();}
    void Save(){Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/"+(probeOnly?"pyramid-probe":"pyramid-validation")+".json",JsonUtility.ToJson(new Report{utc=DateTime.UtcNow.ToString("O"),passed=checks.Count(c=>c.passed),failed=checks.Count(c=>!c.passed),checks=checks},true));}
}
#endif
