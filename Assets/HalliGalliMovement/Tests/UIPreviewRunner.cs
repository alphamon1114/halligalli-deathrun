#if UNITY_EDITOR
using System.Collections;
using System.IO;
using HalliGalli.Movement;
using UnityEditor;
using UnityEngine;

public sealed class UIPreviewRunner:MonoBehaviour
{
    uint previousWidth,previousHeight;CourseSession session;CourseHUD hud;SourcePlayerController control;
    IEnumerator Start()
    {
        PlayModeWindow.GetRenderingResolution(out previousWidth,out previousHeight);
        PlayModeWindow.SetCustomRenderingResolution(1920,1080,"Bell Run UI review");
        yield return null;yield return null;
        session=FindAnyObjectByType<CourseSession>();hud=FindAnyObjectByType<CourseHUD>();hud.ReadInput=false;
        session.cardGame.botEnabled=false;session.cardGame.ReadInput=false;session.cardGame.shuffleOnReset=false;session.cardGame.seed=20261105;
        session.roster.StartNewGame(6);yield return null;
        foreach(var p in session.participants){var bot=p.GetComponent<CourseRival>();if(bot)bot.enabled=false;p.Combat.readInput=false;p.Lasso.SimulateAutomatically=false;var c=p.GetComponent<SourcePlayerController>();if(c){c.controlsEnabled=false;c.movementEnabled=false;}}
        control=session.Local.GetComponent<SourcePlayerController>();control.SetCursorCaptured(true);
        yield return new WaitForSeconds(.4f);
        for(int i=0;i<6;i++){session.cardGame.Reveal(session.participants[session.cardGame.Match.Turn]);yield return new WaitForSeconds(.2f);}
        session.cardGame.tableView.Focus();yield return new WaitForSeconds(.4f);yield return Capture("UI_Table");
        // Show real first-person gameplay and real ability states, not a static HUD mock-up.
        session.cardGame.enabled=false;foreach(var p in session.participants)p.Stand();
        var a=session.Local;var b=session.participants[1];a.Motor.Teleport(new Vector3(0,.375f,5.7f));b.Motor.Teleport(new Vector3(-1.3f,.575f,12.3f));
        a.Motor.Simulate(default,.02f);b.Motor.Simulate(default,.02f);Physics.SyncTransforms();control.ResetView(0,4);control.UpdateView();
        yield return Capture("UI_Race");
        a.Lasso.TryThrow(Vector3.up);a.Lasso.Simulate(1.5f);yield return Capture("UI_Cooldown");
        a.Lasso.EndRace();b.Lasso.TryThrow((LassoTool.Anchor(a)-b.Motor.CurrentEyePosition).normalized);b.Lasso.Simulate(.4f);
        yield return Capture("UI_Lasso");b.Lasso.Simulate(LassoTool.AutoPullDelay);yield return Capture("UI_LassoReleased");
        control.SetCursorCaptured(false);yield return Capture("UI_Pause");
        hud.OpenHelp();yield return Capture("UI_Controls");ControlSettings.Close();
        hud.OpenMapMenu();yield return Capture("UI_Maps");hud.CloseMapMenu();
        PlayModeWindow.SetCustomRenderingResolution(1024,768,"Bell Run UI review");hud.OpenHelp();yield return Capture("UI_Controls_4x3");ControlSettings.Close();
        control.SetCursorCaptured(true);yield return Capture("UI_Race_4x3");
        PlayModeWindow.SetCustomRenderingResolution(2560,1080,"Bell Run UI review");yield return Capture("UI_Race_Ultrawide");
        PlayModeWindow.SetCustomRenderingResolution(1920,1080,"Bell Run UI review");
        CourseNetwork.Instance.ShowMenu();yield return Capture("UI_Online");
        RestoreResolution();Debug.Log("UI_PREVIEW_COMPLETE");EditorApplication.isPlaying=false;
    }
    IEnumerator Capture(string name)
    {
        yield return null;yield return null;yield return new WaitForEndOfFrame();
        Directory.CreateDirectory("Previews");var image=ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes("Previews/"+name+".png",image.EncodeToPNG());Destroy(image);
    }
    void RestoreResolution(){if(previousWidth>0&&previousHeight>0){PlayModeWindow.SetCustomRenderingResolution(previousWidth,previousHeight,"Bell Run UI review");previousWidth=previousHeight=0;}}
    void OnDisable(){RestoreResolution();}
}
#endif
