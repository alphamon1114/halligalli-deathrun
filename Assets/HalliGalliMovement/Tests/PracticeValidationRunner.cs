#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalliGalli.Movement;
using UnityEditor;
using UnityEngine;
public sealed class PracticeValidationRunner:MonoBehaviour
{
    static PracticeValidationRunner instance;
    [Serializable]class Check{public string name;public bool passed;}
    [Serializable]class Report{public int passed,failed;public List<Check> checks=new List<Check>();}
    readonly Report report=new Report();
    void Awake(){if(instance&&instance!=this){Destroy(gameObject);return;}instance=this;DontDestroyOnLoad(gameObject);}
    IEnumerator Start()
    {
        if(instance!=this)yield break;yield return null;yield return null;
        var network=CourseNetwork.Instance;network.ShowStartScreen();yield return Shot("UI_Start");network.ShowCreateRoom();yield return Shot("UI_Start_Create");
        network.StartTraining();float end=Time.realtimeSinceStartup+20;
        while((!network.Game.practiceRoom||!network.Game.practiceRoom.Active)&&Time.realtimeSinceStartup<end)yield return null;
        var session=network.Game;
        CheckThat("Training opens offline without a Relay room",session.practiceRoom&&session.practiceRoom.Active&&!CourseNetwork.Active&&!network.OverlayVisible);
        if(!session.practiceRoom){Finish();yield break;}
        var local=session.Local;var targets=session.participants.Where(p=>!p.localPlayer).ToArray();
        CheckThat("Five standing targets have no navigation or attack input",targets.Length==5&&targets.All(p=>!p.IsSeated&&!p.GetComponent<CourseRival>().enabled&&!p.Combat.readInput)&&!session.HasCardRules);
        var positions=targets.Select(p=>p.transform.position).ToArray();yield return new WaitForSeconds(1);
        CheckThat("Targets remain still until hit",targets.Select((p,i)=>Vector3.Distance(p.transform.position,positions[i])<.03f).All(x=>x));
        var target=targets[2];local.Motor.Teleport(target.transform.position+Vector3.back*1.2f);local.GetComponent<SourcePlayerController>().ResetView(0);
        local.Combat.readInput=false;local.Combat.networkAim=Vector3.forward;local.Combat.TryKick();yield return new WaitForSeconds(.5f);
        CheckThat("Kick moves a stationary training target horizontally",Vector3.ProjectOnPlane(target.transform.position-target.retryAnchor.position,Vector3.up).magnitude>.25f&&target.Receiver.LastImpulse.y==0&&!target.Motor.Staggered);
        yield return new WaitForSeconds(3.8f);
        CheckThat("Target automatically returns after being knocked away",Vector3.Distance(target.transform.position,target.retryAnchor.position)<.1f&&!target.IsRespawning);
        local.Motor.Teleport(new Vector3(0,.025f,-6));local.Combat.ResetCombat();local.Lasso.EndRace();
        int hits=local.Lasso.Hits,pulls=local.Lasso.Pulls;var targetStart=target.transform.position;var inward=Vector3.ProjectOnPlane(local.transform.position-targetStart,Vector3.up).normalized;
        bool cast=local.Lasso.TryThrow((LassoTool.Anchor(target)-local.Motor.CurrentEyePosition).normalized);yield return new WaitForSeconds(.75f);
        CheckThat("Single cast hits and automatically pulls the practice target once",cast&&local.Lasso.Hits==hits+1&&local.Lasso.Pulls==pulls+1&&!local.Lasso.ConnectionActive);
        CheckThat("Practice pull moves target toward player without lift",Vector3.Dot(target.transform.position-targetStart,inward)>.05f&&local.Lasso.LastImpulse.y==0&&target.transform.position.y<targetStart.y+.05f);
        float cooldown=local.Lasso.CooldownRemaining;int respawns=local.RespawnSequence;
        FindAnyObjectByType<CourseHUD>().ProcessActions(false,false,true);yield return new WaitForSeconds(.85f);
        CheckThat("R resets only local position and preserves cooldown",local.RespawnSequence==respawns+1&&Vector3.Distance(local.transform.position,local.retryAnchor.position)<.1f&&local.Lasso.CooldownRemaining>cooldown-1.2f&&!local.Lasso.ConnectionActive&&session.practiceRoom.Active);
        local.GetComponent<SourcePlayerController>().ResetView(0,4);local.GetComponent<SourcePlayerController>().UpdateView();yield return Shot("UI_Practice");
        network.ShowStartScreen();CheckThat("Returning home stops practice simulation",!session.practiceRoom.Active&&network.OverlayVisible);
        Finish();
    }
    IEnumerator Shot(string name){yield return new WaitForSeconds(.15f);yield return new WaitForEndOfFrame();Directory.CreateDirectory("Previews");ScreenCapture.CaptureScreenshot("Previews/"+name+".png");yield return new WaitForEndOfFrame();}
    void CheckThat(string name,bool passed){report.checks.Add(new Check{name=name,passed=passed});if(!passed)Debug.LogError(name);}
    void Finish(){report.passed=report.checks.Count(c=>c.passed);report.failed=report.checks.Count-report.passed;Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/practice-validation.json",JsonUtility.ToJson(report,true));Debug.Log("PRACTICE_VALIDATION "+report.passed+"/"+report.checks.Count);EditorApplication.isPlaying=false;}
}
#endif
