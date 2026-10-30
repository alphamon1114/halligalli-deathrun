#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalliGalli.Movement;
using UnityEditor;
using UnityEngine;

public sealed class RoomBotsValidationRunner:MonoBehaviour
{
    static RoomBotsValidationRunner instance;
    [Serializable] sealed class Check {public string name;public bool passed;}
    [Serializable] sealed class Report {public int passed,failed;public List<Check> checks=new List<Check>();}
    readonly Report report=new Report();CourseNetwork network;uint width,height;
    void Awake(){if(instance&&instance!=this){Destroy(gameObject);return;}instance=this;DontDestroyOnLoad(gameObject);}
    IEnumerator Start()
    {
        if(instance!=this)yield break;
        PlayModeWindow.GetRenderingResolution(out width,out height);PlayModeWindow.SetCustomRenderingResolution(1920,1080,"Bell Run AI room review");
        yield return null;yield return null;network=CourseNetwork.Instance;network.ShowStartScreen();yield return Capture("UI_Start");
        network.ShowCreateRoom();yield return Capture("UI_Start_Create");
        var connect=network.Connect(true);while(!connect.IsCompleted)yield return null;
        CheckThat("Real Relay room starts with one human and no automatic bots",network.IsHost&&network.HumanCount==1&&network.BotCount==0);
        if(!network.IsHost){Finish();yield break;}
        CheckThat("One human alone cannot start",!network.CanStart);
        CheckThat("Host can add exactly one AI",network.AddBot()&&network.Members.Count==2&&network.BotCount==1);
        CheckThat("One human and one ready AI can start",network.CanStart&&network.Members[1].ready);
        network.SelectMap("first-steps");
        ulong first=network.Members[1].id;
        yield return Capture("UI_Room_AI_One");
        for(int i=0;i<4;i++)CheckThat("Additional AI "+(i+2)+" is added individually",network.AddBot());
        CheckThat("Five bots and one human fill six seats",network.Members.Count==6&&network.BotCount==5&&!network.CanAddBot&&!network.AddBot());
        CheckThat("Bot identities are unique",network.Members.Select(m=>m.id).Distinct().Count()==6);
        CheckThat("Removing a human through bot controls is rejected",!network.RemoveBot(network.Members[0].id));
        yield return Capture("UI_Room_AI_Full");
        foreach(var bot in network.Members.Where(m=>m.bot&&m.id!=first).ToArray())network.RemoveBot(bot.id);
        CheckThat("Removing bots retains host and the selected opponent",network.Members.Count==2&&network.Members[1].id==first&&network.CanAddBot);
        network.SelectMap("first-steps");network.StartMatch();CheckThat("Roster edits are blocked while loading",!network.AddBot()&&!network.RemoveBot(first));
        yield return AwaitPlaying("first-steps");if(!network.Playing){yield return Close();Finish();yield break;}
        var game=network.Game;
        CheckThat("Only the host's AI seat runs a rival controller",game.participants.Length==2&&game.participants[1].GetComponent<CourseRival>().enabled&&!game.Local.GetComponent<CourseRival>()&&game.cardGame.botEnabled);
        CheckThat("Human and AI receive a two-seat 56-card deck",game.cardGame.Match.Seats.Sum(s=>s.Draw.Count)==56);
        CheckThat("Roster edits are blocked during play",!network.AddBot()&&!network.RemoveBot(first));
        // Choose a legal first flip that leaves an AI turn, rather than an immediate five-fruit race.
        for(int seed=0;seed<40;seed++){
            game.cardGame.shuffleOnReset=false;game.cardGame.seed=seed;game.ResetPractice();game.cardGame.Match.Reveal(0);
            if(!game.cardGame.Match.HasExactFive())break;
        }
        float until=Time.realtimeSinceStartup+4;while(game.cardGame.Match.RevealSequence<2&&Time.realtimeSinceStartup<until)yield return null;
        CheckThat("AI reveals its own card after the human turn",game.cardGame.Match.RevealSequence>=2);
        for(int n=0;n<55&&!game.cardGame.Match.HasExactFive();n++)game.cardGame.Match.Reveal(game.cardGame.Match.Turn);
        game.Local.Stand();until=Time.realtimeSinceStartup+4;
        while(game.participants[1].IsSeated&&Time.realtimeSinceStartup<until)yield return null;
        CheckThat("AI stands when the table contains a legal five",!game.participants[1].IsSeated);
        Vector3 before=game.participants[1].transform.position;yield return new WaitForSeconds(.5f);
        CheckThat("AI moves along the obstacle course",Vector3.Distance(before,game.participants[1].transform.position)>.3f);
        network.ReturnToRoom();
        CheckThat("Returning to the room freezes AI and preserves its slot",!network.Playing&&!game.cardGame.botEnabled&&!game.participants[1].GetComponent<CourseRival>().enabled&&network.Members[1].id==first&&network.CanStart);
        network.AddBot();network.AddBot();yield return Capture("UI_Room_AI");
        PlayModeWindow.SetCustomRenderingResolution(1024,768,"Bell Run AI room review");yield return Capture("UI_Room_AI_4x3");PlayModeWindow.SetCustomRenderingResolution(1920,1080,"Bell Run AI room review");
        foreach(string map in new[]{"summit","surf"}){
            network.SelectMap(map);network.StartMatch();yield return AwaitPlaying(map);
            if(!network.Playing)break;
            CheckThat(map+": all three AI controllers use this map's route",network.Game.participants.Length==4&&network.Game.participants.Skip(1).All(p=>p.GetComponent<CourseRival>().enabled&&p.GetComponent<CourseRival>().route.Length>0));
            network.ReturnToRoom();
        }
        yield return Close();CheckThat("Closing the room clears bot slots and returns home",!CourseNetwork.Active&&network.BotCount==0&&network.OverlayVisible);
        Finish();
    }
    IEnumerator AwaitPlaying(string map){float until=Time.realtimeSinceStartup+30;while(!network.Playing&&Time.realtimeSinceStartup<until)yield return null;CheckThat(map+": starts without waiting for a fake AI connection (actual="+network.Game.maps.current.id+")",network.Playing&&network.Game.maps.current.id==map);}
    IEnumerator Close(){var close=network.Leave();while(!close.IsCompleted)yield return null;yield return null;yield return null;}
    void CheckThat(string name,bool passed){report.checks.Add(new Check{name=name,passed=passed});if(passed)report.passed++;else{report.failed++;Debug.LogError("ROOM_BOTS_FAILED: "+name);}}
    IEnumerator Capture(string name){yield return null;yield return null;yield return new WaitForEndOfFrame();Directory.CreateDirectory("Previews");var texture=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes("Previews/"+name+".png",texture.EncodeToPNG());Destroy(texture);}
    void Finish(){Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/room-bots-validation.json",JsonUtility.ToJson(report,true));Debug.Log("ROOM_BOTS_VALIDATION "+report.passed+" passed / "+report.failed+" failed");Restore();EditorApplication.isPlaying=false;}
    void Restore(){if(width>0&&height>0){PlayModeWindow.SetCustomRenderingResolution(width,height,"Bell Run AI room review");width=height=0;}}
    void OnDisable(){Restore();}
    void OnDestroy(){if(instance==this)instance=null;}
}
#endif
