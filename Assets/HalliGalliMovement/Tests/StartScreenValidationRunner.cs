#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalliGalli.Movement;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class StartScreenValidationRunner:MonoBehaviour
{
    static StartScreenValidationRunner instance;
    void Awake(){if(instance&&instance!=this){Destroy(gameObject);return;}instance=this;}
    void OnDestroy(){if(instance==this)instance=null;}
    [Serializable] sealed class Check {public string name;public bool passed;}
    [Serializable] sealed class Report {public int passed,failed;public List<Check> checks=new List<Check>();}
    readonly Report report=new Report();uint previousWidth,previousHeight;
    CourseNetwork network;CourseSession game;SourcePlayerController controller;
    IEnumerator Start()
    {
        if(instance!=this)yield break;
        DontDestroyOnLoad(gameObject);
        PlayModeWindow.GetRenderingResolution(out previousWidth,out previousHeight);
        PlayModeWindow.SetCustomRenderingResolution(1920,1080,"Bell Run start review");
        yield return null;yield return null;Bind();
        CheckThat("Normal launch opens home automatically",network.OverlayVisible&&network.CurrentEntryScreen==CourseNetwork.EntryScreen.Home);
        CheckThat("Home releases cursor and blocks player input",!controller.controlsEnabled&&controller.suppressCursorCapture&&Cursor.lockState==CursorLockMode.None);
        CheckThat("Home stops AI card decisions and rival movement",!game.cardGame.botEnabled&&game.participants.All(p=>!p.GetComponent<CourseRival>()||!p.GetComponent<CourseRival>().enabled));
        int revision=game.cardGame.Match.Revision;yield return new WaitForSeconds(1.2f);
        CheckThat("Waiting on home does not advance the match",game.cardGame.Match.Revision==revision);
        yield return Capture("UI_Start");
        PlayModeWindow.SetCustomRenderingResolution(1024,768,"Bell Run start review");yield return Capture("UI_Start_4x3");
        PlayModeWindow.SetCustomRenderingResolution(2560,1080,"Bell Run start review");yield return Capture("UI_Start_Ultrawide");
        PlayModeWindow.SetCustomRenderingResolution(1920,1080,"Bell Run start review");
        network.ShowCreateRoom();
        CheckThat("Create choice opens its form without connecting",network.CurrentEntryScreen==CourseNetwork.EntryScreen.Create&&!CourseNetwork.Active&&!network.Busy);
        yield return Capture("UI_Start_Create");
        network.ShowStartScreen();network.ShowJoinRoom();
        CheckThat("Invite choice opens its form without connecting",network.CurrentEntryScreen==CourseNetwork.EntryScreen.Join&&!CourseNetwork.Active&&!network.Busy);
        yield return Capture("UI_Start_Join");
        PlayModeWindow.SetCustomRenderingResolution(1024,768,"Bell Run start review");yield return Capture("UI_Start_Join_4x3");
        PlayModeWindow.SetCustomRenderingResolution(1920,1080,"Bell Run start review");
        network.ShowStartScreen();
        CheckThat("Back returns to home without starting gameplay",network.CurrentEntryScreen==CourseNetwork.EntryScreen.Home&&!controller.controlsEnabled);
        network.StartPractice();
        CheckThat("Practice immediately dismisses home and restores controls",!network.OverlayVisible&&!CourseNetwork.CapturesPointer&&controller.controlsEnabled&&!controller.suppressCursorCapture);
        CheckThat("Practice click cannot reveal a card in the same frame",!controller.PointerActionsAllowed);
        CheckThat("Practice restores the AI and combat simulation",game.cardGame.botEnabled&&game.participants.All(p=>p.Combat.simulateAutomatically&&(!p.GetComponent<CourseRival>()||p.GetComponent<CourseRival>().enabled)));
        yield return new WaitForSeconds(.5f);
        CheckThat("Practice starts seated with a legal card turn",game.EveryoneSeated&&game.cardGame.CanReveal(game.Local));
        game.cardGame.Reveal(game.Local);yield return Capture("UI_Start_Practice");
        network.ShowMenu();
        CheckThat("Practice can return to home and freeze again",network.OverlayVisible&&network.CurrentEntryScreen==CourseNetwork.EntryScreen.Home&&!controller.controlsEnabled&&!game.cardGame.botEnabled);
        string scene=game.gameObject.scene.path;yield return SceneManager.LoadSceneAsync(scene);yield return null;yield return null;Bind();
        CheckThat("Scene reload while in home keeps simulation and cursor paused",network.OverlayVisible&&!controller.controlsEnabled&&!game.cardGame.botEnabled&&Cursor.lockState==CursorLockMode.None);
        network.StartPractice();yield return SceneManager.LoadSceneAsync(scene);yield return null;yield return null;Bind();
        CheckThat("Practice scene change does not reopen the start screen",!network.OverlayVisible&&controller.controlsEnabled&&game.cardGame.botEnabled);
        Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/start-screen-validation.json",JsonUtility.ToJson(report,true));
        RestoreResolution();Debug.Log("START_SCREEN_VALIDATION "+report.passed+" passed / "+report.failed+" failed");EditorApplication.isPlaying=false;
    }
    void Bind(){network=CourseNetwork.Instance;game=network.Game;controller=game.Local.GetComponent<SourcePlayerController>();}
    void CheckThat(string name,bool passed){report.checks.Add(new Check{name=name,passed=passed});if(passed)report.passed++;else{report.failed++;Debug.LogError("START_SCREEN_CHECK_FAILED: "+name);}}
    IEnumerator Capture(string name)
    {
        yield return null;yield return null;yield return new WaitForEndOfFrame();
        Directory.CreateDirectory("Previews");var image=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes("Previews/"+name+".png",image.EncodeToPNG());Destroy(image);
    }
    void RestoreResolution(){if(previousWidth>0&&previousHeight>0){PlayModeWindow.SetCustomRenderingResolution(previousWidth,previousHeight,"Bell Run start review");previousWidth=previousHeight=0;}}
    void OnDisable(){RestoreResolution();}
}
#endif
