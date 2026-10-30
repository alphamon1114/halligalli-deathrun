#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalliGalli.Movement;
using UnityEditor;
using UnityEngine;

public sealed class ControlsValidationRunner:MonoBehaviour
{
    [Serializable]class Check{public string name,detail;public bool passed;}
    [Serializable]class Report{public int passed,failed;public List<Check> checks;}
    readonly List<Check> checks=new List<Check>();
    string oldPrefs;bool hadPrefs,restored;uint oldWidth,oldHeight;
    IEnumerator Start()
    {
        hadPrefs=PlayerPrefs.HasKey(ControlBindings.PreferenceKey);oldPrefs=PlayerPrefs.GetString(ControlBindings.PreferenceKey,"");
        PlayModeWindow.GetRenderingResolution(out oldWidth,out oldHeight);
        var run=Validate();while(true){bool more;object next=null;try{more=run.MoveNext();if(more)next=run.Current;}catch(Exception e){CheckResult("Validation completes",false,e.ToString());break;}if(!more)break;yield return next;}
        Restore();Save();Debug.Log("CONTROLS_VALIDATION_RESULT "+checks.Count(c=>c.passed)+"/"+checks.Count);EditorApplication.isPlaying=false;
    }
    IEnumerator Validate()
    {
        yield return null;yield return null;
        var session=FindAnyObjectByType<CourseSession>();var hud=FindAnyObjectByType<CourseHUD>();
        hud.ReadInput=false;session.cardGame.enabled=false;session.cardGame.botEnabled=false;
        foreach(var p in session.roster.allParticipants){var bot=p.GetComponent<CourseRival>();if(bot)bot.enabled=false;p.Combat.readInput=false;}
        var player=session.Local;var controller=player.GetComponent<SourcePlayerController>();controller.controlsEnabled=false;
        ControlBindings.Defaults();
        bool Off(KeyCode k)=>false;
        bool Space(KeyCode k)=>k==KeyCode.Space;
        CheckResult("Defaults retain space and add wheel down",ControlBindings.Get(GameAction.Jump,0)==(int)KeyCode.Space&&ControlBindings.Get(GameAction.Jump,1)==ControlBindings.WheelDown,"");
        CheckResult("Wheel down presses jump",ControlBindings.Evaluate(GameAction.Jump,true,Off,Off,-120),"");
        CheckResult("Wheel never counts as held jump",!ControlBindings.Evaluate(GameAction.Jump,false,Off,Off,-120),"");
        CheckResult("Zero scroll gives no repeated pulse",!ControlBindings.Evaluate(GameAction.Jump,true,Off,Off,0),"");
        CheckResult("Keyboard and mouse wheel coexist",ControlBindings.Evaluate(GameAction.Jump,true,Space,Space,0)&&ControlBindings.Evaluate(GameAction.Jump,false,Space,Off,0),"");
        ControlBindings.Assign(GameAction.Jump,2,ControlBindings.WheelUp);
        CheckResult("Either wheel direction can trigger",ControlBindings.Evaluate(GameAction.Jump,true,Off,Off,1)&&ControlBindings.Evaluate(GameAction.Jump,true,Off,Off,-1),"");
        CheckResult("Cannot bind a pulse to a held action",!ControlBindings.Assign(GameAction.Forward,1,ControlBindings.WheelUp),"");
        CheckResult("Escape remains reserved for menus",!ControlBindings.Assign(GameAction.Kick,0,(int)KeyCode.Escape),"");
        CheckResult("Duplicate key does not silently replace another action",!ControlBindings.Assign(GameAction.Kick,0,(int)KeyCode.Q)&&ControlBindings.Hint(GameAction.Kick)=="F","");
        CheckResult("Explicit swap keeps both actions usable",ControlBindings.Assign(GameAction.Kick,0,(int)KeyCode.Q,true)&&ControlBindings.Hint(GameAction.Kick)=="Q"&&ControlBindings.Hint(GameAction.Lasso)=="F","");
        ControlBindings.Assign(GameAction.Interact,0,(int)KeyCode.G);ControlBindings.Assign(GameAction.Primary,0,(int)KeyCode.Mouse1);ControlBindings.Save();ControlBindings.Reload();
        CheckResult("Bindings survive a fresh load",ControlBindings.Hint(GameAction.Interact)=="G"&&ControlBindings.Hint(GameAction.Primary)=="RMB"&&ControlBindings.Get(GameAction.Jump,2)==ControlBindings.WheelUp,"");
        session.cardGame.enabled=true;session.ResetPractice();yield return null;
        CheckResult("Card and interact hints follow bindings",hud.ActionText.Contains("G")&&!hud.ActionText.Contains("[ E ]"),hud.ActionText);
        session.cardGame.enabled=false;
        PlayerPrefs.SetString(ControlBindings.PreferenceKey,"invalid json");ControlBindings.Reload();
        CheckResult("Malformed save safely restores defaults",ControlBindings.Hint(GameAction.Interact)=="E"&&ControlBindings.Hint(GameAction.Primary)=="LMB","");
        ControlSettings.Open();var panel=FindAnyObjectByType<ControlSettings>();
        CheckResult("Settings blocks gameplay actions",ControlSettings.BlocksInput&&!controller.PointerActionsAllowed&&!ControlBindings.Pressed(GameAction.Jump),"");
        panel.BeginCapture(GameAction.Kick,0);panel.AcceptInput((int)KeyCode.T);
        CheckResult("Capture applies and saves selected key",!panel.Listening&&ControlBindings.Hint(GameAction.Kick)=="T","");
        ControlBindings.Reload();CheckResult("Captured binding persists",ControlBindings.Hint(GameAction.Kick)=="T","");
        panel.BeginCapture(GameAction.Jump,2);panel.AcceptInput(ControlBindings.WheelUp);
        CheckResult("Capture accepts wheel as secondary binding",ControlBindings.Get(GameAction.Jump,2)==ControlBindings.WheelUp,"");
        PlayModeWindow.SetCustomRenderingResolution(1920,1080,"Controls review");yield return Capture("UI_KeyBindings");
        PlayModeWindow.SetCustomRenderingResolution(1024,768,"Controls review");yield return Capture("UI_KeyBindings_4x3");
        panel.BeginCapture(GameAction.Lasso,0);panel.AcceptInput((int)KeyCode.T);yield return Capture("UI_KeyBindings_Conflict");
        CheckResult("Conflicting capture waits for explicit choice",panel.Listening&&ControlBindings.Hint(GameAction.Lasso)=="Q","");
        ControlSettings.Close();CheckResult("Close does not leak its input frame",ControlSettings.BlocksInput,"");
        yield return null;yield return null;yield return null;yield return null;
        ControlBindings.Defaults();session.ResetPractice();yield return new WaitForFixedUpdate();player.Stand();
        player.Motor.Teleport(player.standAnchor.position);for(int i=0;i<5;i++)player.Motor.Simulate(default,.015f);
        int jumps=player.Motor.JumpCount;bool pressed=ControlBindings.Evaluate(GameAction.Jump,true,Off,Off,-1);
        player.Motor.Simulate(new MovementCommand{jumpPressed=pressed,jumpHeld=ControlBindings.Evaluate(GameAction.Jump,false,Off,Off,-1)},.015f);
        for(int i=0;i<120;i++)player.Motor.Simulate(default,.015f);
        CheckResult("One wheel pulse produces exactly one physical jump",player.Motor.JumpCount==jumps+1&&player.Motor.Grounded&&!player.Motor.profile.autoBunnyHop,"jumps="+(player.Motor.JumpCount-jumps));
        player.Motor.Simulate(new MovementCommand{jumpPressed=true},.015f);bool buffered=false;
        float ground=player.standAnchor.position.y;
        for(int i=0;i<150;i++){
            bool pulse=!buffered&&player.Motor.Velocity.y<0&&player.transform.position.y-ground<.15f&&!player.Motor.Grounded;
            if(pulse)buffered=true;player.Motor.Simulate(new MovementCommand{jumpPressed=pulse},.015f);
        }
        CheckResult("Landing-time wheel input uses the existing jump buffer",buffered&&player.Motor.JumpCount==jumps+3,"jumps="+(player.Motor.JumpCount-jumps));
        player.Motor.Simulate(new MovementCommand{jumpPressed=true},.015f);controller.SetCursorCaptured(false);
        CheckResult("Opening menu clears buffered jump",player.Motor.CaptureNetworkState().buffer<0,"");
        ControlBindings.Defaults();CheckResult("Restore defaults restores combat and interaction",ControlBindings.Hint(GameAction.Kick)=="F"&&ControlBindings.Hint(GameAction.Interact)=="E","");
    }
    IEnumerator Capture(string name){yield return null;yield return null;yield return new WaitForEndOfFrame();Directory.CreateDirectory("Previews");var image=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes("Previews/"+name+".png",image.EncodeToPNG());Destroy(image);}
    void CheckResult(string name,bool passed,string detail){checks.Add(new Check{name=name,passed=passed,detail=detail});if(!passed)Debug.LogError(name+" "+detail);Save();}
    void Save(){Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/controls-validation.json",JsonUtility.ToJson(new Report{passed=checks.Count(c=>c.passed),failed=checks.Count(c=>!c.passed),checks=checks},true));}
    void Restore(){if(restored)return;restored=true;ControlSettings.Close();if(hadPrefs)PlayerPrefs.SetString(ControlBindings.PreferenceKey,oldPrefs);else PlayerPrefs.DeleteKey(ControlBindings.PreferenceKey);PlayerPrefs.Save();ControlBindings.Reload();if(oldWidth>0)PlayModeWindow.SetCustomRenderingResolution(oldWidth,oldHeight,"Controls review");}
    void OnDisable(){Restore();}
}
#endif
