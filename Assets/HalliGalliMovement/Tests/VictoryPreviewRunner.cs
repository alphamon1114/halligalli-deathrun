#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HalliGalli.Cards;
using HalliGalli.Movement;
using UnityEditor;
using UnityEngine;

public sealed class VictoryPreviewRunner:MonoBehaviour
{
    [Serializable] sealed class Check {public string name;public bool passed;}
    [Serializable] sealed class Report {public int passed,failed;public List<Check> checks=new List<Check>();}
    readonly Report report=new Report();uint width,height;CourseNetwork network;string previousNickname;
    FieldInfo nickname;CourseSession session;CourseHUD hud;
    IEnumerator Start()
    {
        var run=Preview();while(true){bool more;object next=null;try{more=run.MoveNext();if(more)next=run.Current;}catch(Exception e){CheckThat(e.ToString(),false);break;}if(!more)break;yield return next;}
        Restore();Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/victory-validation.json",JsonUtility.ToJson(report,true));
        Debug.Log("VICTORY_VALIDATION_RESULT "+report.passed+"/"+report.checks.Count);EditorApplication.isPlaying=false;
    }
    IEnumerator Preview()
    {
        PlayModeWindow.GetRenderingResolution(out width,out height);PlayModeWindow.SetCustomRenderingResolution(1920,1080,"Victory banner preview");
        yield return null;yield return null;
        session=FindAnyObjectByType<CourseSession>();hud=FindAnyObjectByType<CourseHUD>();network=CourseNetwork.Instance;
        // Preview names stay in memory; never overwrite the player's saved nickname.
        nickname=typeof(CourseNetwork).GetField("nickname",BindingFlags.Instance|BindingFlags.NonPublic);previousNickname=(string)nickname.GetValue(network);
        nickname.SetValue(network,"종치러왔어요");hud.ReadInput=false;
        session.roster.StartNewGame(2);yield return null;Prepare();
        CheckThat("Ongoing match has no victory banner",!hud.VictoryVisible&&hud.WinnerNickname=="");
        CheckThat("Ongoing match is silent",hud.VictorySoundsPlayed==0);
        CheckThat("VARCO victory clip loads before the result",hud.VictoryAudioReady);
        FinishMatch(0);yield return new WaitForSeconds(.6f);
        CheckThat("Local win uses nickname without seat or self suffix",hud.VictoryVisible&&hud.WinnerNickname=="종치러왔어요");
        CheckThat("Local winner reveal plays exactly once",hud.VictorySoundsPlayed==1);
        var audio=hud.VictoryAudioSource;
        CheckThat("Result sound is short, non-looping and independent of player position",audio&&audio.clip&&audio.clip.length>0.2f&&audio.clip.length<=3&&audio.spatialBlend==0&&!audio.loop&&!audio.playOnAwake&&audio.volume<=.7f);
        CheckThat("Heading and offline restart action describe the result",hud.StatusText.StartsWith("승자")&&hud.VictoryAction=="다시하기"&&hud.CanContinueAfterVictory);
        yield return Capture("UI_Victory");
        nickname.SetValue(network,"가나다라마바사아자차카타파하우승");
        PlayModeWindow.SetCustomRenderingResolution(1024,768,"Victory long name preview");yield return Capture("UI_Victory_LongName_4x3");
        CheckThat("Maximum length Korean nickname is retained",hud.WinnerNickname=="가나다라마바사아자차카타파하우승");
        PlayModeWindow.SetCustomRenderingResolution(2560,1080,"Victory ultrawide preview");yield return Capture("UI_Victory_Ultrawide");
        CheckThat("Repeated result frames do not replay the sound",hud.VictorySoundsPlayed==1);
        session.ResetPractice();yield return null;
        CheckThat("New match removes the result",!hud.VictoryVisible&&hud.WinnerNickname=="");
        CheckThat("New match stops the previous sound",audio&&!audio.isPlaying);
        FinishMatch(1);yield return new WaitForSeconds(.5f);
        CheckThat("Opponent win names the opponent",hud.VictoryVisible&&hud.WinnerNickname=="AI 2");
        CheckThat("An opponent winner also plays one result sound",hud.VictorySoundsPlayed==2);
        PlayModeWindow.SetCustomRenderingResolution(1920,1080,"Victory opponent preview");yield return Capture("UI_Victory_Opponent");
        session.roster.StartNewGame(6);yield return null;Prepare();FinishMatch(5);yield return new WaitForSeconds(.5f);
        CheckThat("Six-player result resolves the final seat",hud.VictoryVisible&&session.cardGame.Match.Winner==5&&hud.WinnerNickname=="AI 6");
        CheckThat("Next match can play its own result sound",hud.VictorySoundsPlayed==3);
        yield return Capture("UI_Victory_SixPlayers");session.ResetPractice();
    }
    void Prepare()
    {
        session.cardGame.botEnabled=false;session.cardGame.ReadInput=false;
        foreach(var p in session.participants){var bot=p.GetComponent<CourseRival>();if(bot)bot.enabled=false;p.Combat.readInput=false;var c=p.GetComponent<SourcePlayerController>();if(c){c.controlsEnabled=false;c.SetCursorCaptured(true);}}
    }
    void FinishMatch(int winner)
    {
        var match=new HalliGalliMatch(session.cardGame.deck.Deal(session.participants.Length,42));
        for(int n=0;n<1000&&!match.Finished;n++)for(int seat=0;seat<match.Seats.Count;seat++)if(seat!=winner){match.Stand(seat);match.Sit(seat);}
        if(!match.Finished||match.Winner!=winner)throw new Exception("Unable to finish through standing penalties");
        session.cardGame.ApplyNetworkState(match.ExportPublicState(session.cardGame.deck));session.cardGame.tableView.Focus();
    }
    IEnumerator Capture(string name){yield return null;yield return null;yield return new WaitForEndOfFrame();Directory.CreateDirectory("Previews");var texture=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes("Previews/"+name+".png",texture.EncodeToPNG());Destroy(texture);}
    void CheckThat(string name,bool passed){report.checks.Add(new Check{name=name,passed=passed});if(passed)report.passed++;else report.failed++;}
    void Restore(){if(network&&nickname!=null)nickname.SetValue(network,previousNickname);if(width>0&&height>0){PlayModeWindow.SetCustomRenderingResolution(width,height,"Victory banner preview");width=height=0;}}
    void OnDisable(){Restore();}
}
#endif
