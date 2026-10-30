#if UNITY_EDITOR
using System.Collections;
using System.IO;
using HalliGalli.Movement;
using UnityEditor;
using UnityEngine;

public sealed class PunchPreviewRunner:MonoBehaviour
{
    IEnumerator Start()
    {
        yield return null;yield return null;
        var session=FindAnyObjectByType<CourseSession>();
        session.cardGame.botEnabled=false;session.cardGame.ReadInput=false;session.cardGame.enabled=false;
        foreach(var p in session.participants){var bot=p.GetComponent<CourseRival>();if(bot)bot.enabled=false;p.Combat.readInput=false;}
        FindAnyObjectByType<CourseHUD>().ReadInput=false;
        var player=session.Local;player.Stand();
        player.Motor.Teleport(new Vector3(0,.375f,5.7f));player.Motor.Simulate(default,.015f);
        var controller=player.GetComponent<SourcePlayerController>();
        controller.controlsEnabled=false;controller.movementEnabled=false;controller.ResetView(0,4);controller.SetCursorCaptured(true);
        yield return null;
        Directory.CreateDirectory("Previews");
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Previews/Punch_Clear.png");yield return new WaitForEndOfFrame();
        player.Receiver.Receive(MeleeAttack.PunchLeft,Vector3.back,player.Motor.CurrentEyePosition);
        yield return new WaitForSeconds(.06f);Time.timeScale=0;
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Previews/Punch_Blink.png");yield return new WaitForEndOfFrame();
        Time.timeScale=1;yield return new WaitForSeconds(.30f);
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Previews/Punch_Recovered.png");yield return new WaitForEndOfFrame();
        player.Receiver.Receive(MeleeAttack.PunchRight,Vector3.left,player.Motor.CurrentEyePosition);
        if(player.Receiver.PunchBlinkAmount!=0)Debug.LogError("Punch preview recovery immunity failed");
        Debug.Log("PUNCH_PREVIEW_COMPLETE");EditorApplication.isPlaying=false;
    }
    void OnDisable(){Time.timeScale=1;}
}
#endif
