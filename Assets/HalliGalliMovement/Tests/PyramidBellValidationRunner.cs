#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using HalliGalli.Movement;
public sealed class PyramidBellValidationRunner:MonoBehaviour
{
    [Serializable]class Check {public string name,detail;public bool passed;}
    [Serializable]class Report {public string utc;public int passed,failed;public List<Check> checks;}
    readonly List<Check> checks=new List<Check>();
    public static void Run(){EditorSceneManager.OpenScene("Assets/HalliGalliMovement/Demo/PyramidSurfCourse.unity");new GameObject("Bell and checkpoint regression").AddComponent<PyramidBellValidationRunner>();EditorApplication.isPlaying=true;}
    IEnumerator Start(){
        var routine=Validate();while(true){bool more;object next=null;try{more=routine.MoveNext();if(more)next=routine.Current;}catch(Exception e){CheckResult("Runner completes",false,e.ToString());break;}if(!more)break;yield return next;}
        Time.timeScale=1;Save();EditorApplication.Exit(checks.Any(c=>!c.passed)?1:0);
    }
    IEnumerator Validate(){
        yield return null;yield return null;
        CourseNetwork.Instance.StartPractice();yield return null;
        var session=FindAnyObjectByType<CourseSession>();var surf=session.roster.surf;var hud=FindAnyObjectByType<CourseHUD>();
        session.cardGame.botEnabled=false;session.cardGame.shuffleOnReset=false;session.cardGame.seed=20261020;hud.ReadInput=false;session.roster.StartNewGame(2);
        session.Local.GetComponent<SourcePlayerController>().controlsEnabled=false;
        foreach(var p in session.roster.allParticipants){p.Initialize();p.Combat.readInput=false;var bot=p.GetComponent<CourseRival>();if(bot)bot.enabled=false;}
        foreach(var r in session.bellTarget.GetComponentsInChildren<Renderer>())if(r.enabled)
            CheckResult("Visible bell remains at its interaction target",Vector3.Distance(r.bounds.center,session.bellTarget.position)<.4f,"target="+session.bellTarget.position+" bounds="+r.bounds);
        Time.timeScale=5;
        foreach(int seat in new[]{0,1}){
            session.ResetPractice();yield return new WaitForFixedUpdate();
            int reveals=0;while(!session.cardGame.Match.HasExactFive()&&reveals++<1000)session.cardGame.Match.Reveal(session.cardGame.Match.Turn);
            var runner=session.participants[seat];runner.Stand();
            runner.Motor.Teleport(surf.finish.position);runner.Motor.Simulate(default,Time.fixedDeltaTime);yield return new WaitForFixedUpdate();
            CheckResult("Seat "+seat+" cannot skip directly to bell",!session.CanRing(runner));
            runner.Motor.Teleport(session.checkpoints.points[3].spawn.position+Vector3.up*2);runner.Motor.Simulate(default,Time.fixedDeltaTime);yield return new WaitForFixedUpdate();
            CheckResult("Seat "+seat+" cannot skip preceding checkpoints",runner.CheckpointIndex==-1);
            for(int index=0;index<session.checkpoints.points.Length;index++){
                var point=session.checkpoints.points[index];
                runner.Motor.Teleport(point.spawn.position+Vector3.up*2);
                runner.Motor.Simulate(default,Time.fixedDeltaTime);yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();
                CheckResult("Seat "+seat+" airborne checkpoint "+index,!runner.Motor.Grounded&&runner.CheckpointIndex==index,"saved="+runner.CheckpointIndex+" gates="+surf.GatesPassed(runner));
                if(seat==0)hud.ProcessActions(false,false,true);else runner.RequestRespawn();
                yield return new WaitForSeconds(.85f);
                CheckResult("Seat "+seat+" R returns to checkpoint "+index,!runner.IsRespawning&&runner.CheckpointIndex==index&&Vector3.Distance(runner.transform.position,point.spawn.position)<.15f,"position="+runner.transform.position);
                // Set the arrival fixture for the next independent checkpoint even in the failing-before run.
                runner.SetCheckpoint(index,0);surf.RestoreCheckpointProgress(runner,point.surfGates);
            }
            runner.Motor.Teleport(session.checkpoints.points[0].spawn.position);runner.Motor.Simulate(default,Time.fixedDeltaTime);yield return new WaitForFixedUpdate();
            CheckResult("Seat "+seat+" backtracking preserves furthest checkpoint",runner.CheckpointIndex==3);
            runner.SetCheckpoint(session.checkpoints.points.Length-1,0);surf.RestoreCheckpointProgress(runner,session.checkpoints.points.Last().surfGates);
            runner.Motor.Teleport(surf.finish.position);
            for(int i=0;i<5;i++){runner.Motor.Simulate(default,Time.fixedDeltaTime);yield return new WaitForFixedUpdate();}
            CheckResult("Seat "+seat+" valid checkpoint route can ring without touching every bank",session.CanRing(runner),"grounded="+runner.Motor.Grounded+" gates="+surf.GatesPassed(runner)+" claim="+session.cardGame.Match.CanClaim(seat));
            if(seat==0){
                if(!Application.isBatchMode&&SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null){
                    var control=runner.GetComponent<SourcePlayerController>();
                    var direction=session.bellTarget.position+Vector3.up*.15f-runner.Motor.CurrentEyePosition;
                    control.ResetView(Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg,-Mathf.Atan2(direction.y,new Vector2(direction.x,direction.z).magnitude)*Mathf.Rad2Deg);
                    control.SetCursorCaptured(true);control.UpdateView();
                    yield return new WaitForEndOfFrame();
                    Directory.CreateDirectory("Previews");ScreenCapture.CaptureScreenshot("Previews/Pyramid_BellPrompt.png");
                    yield return new WaitForEndOfFrame();yield return null;
                }
                hud.Interact();
            }else session.Ring(runner);
            CheckResult("Seat "+seat+" interaction awards cards and reseats both players",session.BellCount==1&&session.cardGame.Match.LastCollector==seat&&session.cardGame.Match.LastCollectedCount>0&&session.participants.All(p=>p.IsSeated));
        }
        session.ResetPractice();yield return new WaitForFixedUpdate();
        var local=session.Local;local.Stand();local.Motor.Teleport(session.checkpoints.points[0].spawn.position+Vector3.up*2);local.Motor.Simulate(default,Time.fixedDeltaTime);yield return new WaitForFixedUpdate();
        CheckResult("Wrong card departure cannot save a checkpoint",local.CheckpointIndex==-1);
        local.SetCheckpoint(session.checkpoints.points.Length-1,0);surf.RestoreCheckpointProgress(local,surf.sections.Length);local.Motor.Teleport(surf.finish.position);local.Motor.Simulate(default,Time.fixedDeltaTime);
        CheckResult("Wrong card departure still cannot claim the bell",!session.CanRing(local));
        session.ResetPractice();yield return new WaitForFixedUpdate();
        for(int attempts=0;!session.cardGame.Match.HasExactFive()&&attempts<1000;attempts++)session.cardGame.Match.Reveal(session.cardGame.Match.Turn);
        if(!session.cardGame.Match.HasExactFive())throw new InvalidOperationException("Fixed deck failed to produce a legal departure.");
        local.Stand();var first=session.checkpoints.points[0];local.Motor.Teleport(first.spawn.position-Vector3.up*2);local.Motor.Simulate(default,Time.fixedDeltaTime);yield return new WaitForFixedUpdate();
        CheckResult("Passing underneath a checkpoint does not save it",local.CheckpointIndex==-1);
    }
    void CheckResult(string name,bool passed,string detail=""){checks.Add(new Check{name=name,passed=passed,detail=detail});Save();}
    void Save(){Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/pyramid-bell-checkpoints.json",JsonUtility.ToJson(new Report{utc=DateTime.UtcNow.ToString("O"),passed=checks.Count(c=>c.passed),failed=checks.Count(c=>!c.passed),checks=checks},true));}
}
#endif
