#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalliGalli.Cards;
using HalliGalli.Movement;
using UnityEditor;
using UnityEngine;

public sealed class RosterValidationRunner:MonoBehaviour
{
    [Serializable]class Check{public string name;public bool passed;public string detail;}
    [Serializable]class Report{public int passed,failed;public List<Check> checks;public float[] firstLandingSeconds;}
    readonly List<Check> checks=new List<Check>();readonly List<float> landingTimes=new List<float>();
    CourseSession session;CourseRoster roster;CourseCardGame game;SourcePlayerController controller;
    IEnumerator Start(){var routine=Validate();while(true){bool more=false;object next=null;try{more=routine.MoveNext();if(more)next=routine.Current;}catch(Exception e){Test("Runner completed",()=>throw e);break;}if(!more)break;yield return next;}Time.timeScale=1;int passed=checks.Count(c=>c.passed);Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/roster-validation.json",JsonUtility.ToJson(new Report{passed=passed,failed=checks.Count-passed,checks=checks,firstLandingSeconds=landingTimes.ToArray()},true));Debug.Log("ROSTER_VALIDATION_RESULT "+passed+"/"+checks.Count);EditorApplication.isPlaying=false;}
    void Bots(bool enabled){foreach(var p in roster.allParticipants){var bot=p.GetComponent<CourseRival>();if(bot){bot.enabled=enabled;bot.attacksEnabled=false;}}}
    IEnumerator Validate()
    {
        yield return null;yield return null;
        session=FindAnyObjectByType<CourseSession>();roster=session.roster;game=session.cardGame;controller=session.Local.GetComponent<SourcePlayerController>();controller.controlsEnabled=false;controller.captureCursorOnStart=false;controller.SetCursorCaptured(false);game.ReadInput=false;game.botEnabled=false;game.shuffleOnReset=false;Time.timeScale=3;Bots(false);
        foreach(int count in new[]{2,3,4,5,6,2,6}){
            roster.StartNewGame(count);yield return new WaitForSeconds(.4f);
            Test(count+" seats: active actors, chairs, decks and cards match",()=>Require(session.participants.Length==count&&roster.allParticipants.Count(p=>p.gameObject.activeSelf)==count&&roster.chairs.Count(t=>t.gameObject.activeSelf)==count&&game.Match.Seats.Count==count&&game.Match.AllCards().Count()==56&&game.display.ViewCount==56&&game.Match.Seats.Max(s=>s.Draw.Count)-game.Match.Seats.Min(s=>s.Draw.Count)<=1,"roster/deal"));
            Test(count+" seats: straight row and equal approach, gap, height and launch direction",()=>{
                Require(roster.startLine&&Mathf.Abs(roster.startLine.position.z-CourseRoster.StartZ)<.001f&&Mathf.Abs(roster.startLine.localScale.x-count*CourseRoster.SeatSpacing)<.001f,"visible start line differs from spawn points");
                foreach(var p in session.participants){int i=game.Index(p);var first=roster.firstPlatforms[i];Require(Mathf.Abs(p.seatAnchor.position.z-CourseRoster.SeatZ)<.001f&&Mathf.Abs(p.standAnchor.position.z-CourseRoster.StartZ)<.001f&&Mathf.Abs(p.standAnchor.position.x-first.position.x)<.001f,"row alignment");Require(Mathf.Abs(Vector3.ProjectOnPlane(first.position-p.standAnchor.position,Vector3.up).magnitude-3)<.001f&&Mathf.Abs(first.position.z-1-1.3f)<.001f&&Mathf.Abs(first.position.y-.15f)<.001f&&Vector3.Dot(p.standAnchor.forward,Vector3.forward)>.999f,"unequal start");}
            });
            Test(count+" seats: evenly spaced circular open cards without pile overlaps",()=>{
                float radius=count==2?.66f:1.18f;var anchors=game.display.openAnchors;
                for(int i=0;i<count;i++){var a=anchors[i].position-CourseRoster.CardCenter;var b=anchors[(i+1)%count].position-CourseRoster.CardCenter;Require(Mathf.Abs(a.magnitude-radius)<.001f&&Mathf.Abs(Vector3.Angle(a,b)-360f/count)<.01f,"ring spacing");}
                var points=anchors.Concat(game.display.drawAnchors).Select(t=>t.position).ToArray();for(int i=0;i<points.Length;i++)for(int j=i+1;j<points.Length;j++)Require(Mathf.Abs(points[i].x-points[j].x)>.48f||Mathf.Abs(points[i].z-points[j].z)>.746f,"overlapping card piles");
            });
            var before=game.Match.Seats.Select(s=>s.Draw.Count).ToArray();session.Local.Stand();
            Test(count+" seats: wrong stand pays every other active seat once",()=>Require(game.Match.Seats[0].LastPenaltyPaid==count-1&&game.Match.Seats[0].Draw.Count==before[0]-count+1&&Enumerable.Range(1,count-1).All(i=>game.Match.Seats[i].Draw.Count==before[i]+1)&&game.Match.Paused,"multiplayer penalty"));session.Local.Sit();
        }
        roster.StartNewGame(6);yield return new WaitForSeconds(.4f);
        for(int i=0;i<6;i++){Require(game.Reveal(session.participants[i]),"turn "+i);yield return new WaitForSeconds(.25f);}
        Test("Six reveal turns reach each named seat exactly once",()=>Require(game.Match.Turn==0&&game.Match.Seats.All(s=>s.Open.Count==1)&&game.Match.RevealSequence==6,"turn rotation"));
        foreach(float aspect in new[]{4f/3,16f/9,21f/9}){controller.playerCamera.aspect=aspect;yield return null;Test("Six public cards readable clear of HUD at "+aspect.ToString("F2"),()=>{
            foreach(var anchor in game.display.openAnchors)foreach(float x in new[]{-.24f,.24f})foreach(float z in new[]{-.373f,.373f}){var v=controller.playerCamera.WorldToViewportPoint(anchor.position+new Vector3(x,.02f,z));Require(v.z>0&&v.x>.15f&&v.x<.85f&&v.y>.22f&&v.y<.79f,"open card outside safe area: "+v);}
        });}controller.playerCamera.ResetAspect();
        // A reveal that creates a five must not let later bots in the loop stand in the same frame.
        int seed=Enumerable.Range(0,10000).First(n=>{var m=new HalliGalliMatch(game.deck.Deal(6,n));m.Reveal(0);if(m.HasExactFive())return false;m.Reveal(1);return m.HasExactFive();});
        game.seed=seed;roster.StartNewGame(6);yield return new WaitForSeconds(.4f);game.Reveal(session.Local);game.botEnabled=true;Bots(true);
        float end=Time.time+3;while(game.Match.RevealSequence<2&&Time.time<end)yield return null;
        Test("AI reveal gives everybody a reaction window before any stand",()=>Require(game.Match.RevealSequence==2&&game.Match.HasExactFive()&&session.EveryoneSeated,"same-frame AI stand"));yield return new WaitForSeconds(.4f);
        Test("Reaction window remains open without automatic player departure",()=>Require(session.EveryoneSeated,"early AI stand"));
        end=Time.time+2;while(session.participants.Skip(1).Any(p=>p.IsSeated)&&Time.time<end)yield return null;
        Test("All five bots can join a frozen valid race",()=>Require(session.Local.IsSeated&&session.participants.Skip(1).All(p=>!p.IsSeated)&&game.Match.Paused&&game.Match.RevealSequence==2,"AI race join"));
        session.Local.Stand();end=Time.time+45;while(session.BellCount==0&&Time.time<end)yield return new WaitForFixedUpdate();game.botEnabled=false;Bots(false);
        Test("Six-person race collects the pile once and immediately seats all six",()=>Require(session.BellCount==1&&session.EveryoneSeated&&game.Match.LastCollectedCount==2&&game.Match.AllCards().Count()==56&&!game.Match.Paused&&game.Match.Seats.All(s=>s.Open.Count==0),"six-person bell, count="+session.BellCount));
        yield return new WaitForSeconds(.4f);Test("Winner continues the six-person reveal order",()=>Require(game.Match.Turn==game.Match.LastCollector&&game.Reveal(session.participants[game.Match.Turn]),"winner turn"));

        // Isolate actual jumps in each lane; other bodies remain seated and cannot block the trial.
        roster.StartNewGame(6);game.enabled=false;Vector3[] starts=session.participants.Select(p=>p.standAnchor.position).ToArray();var runner=session.participants[1];var botRunner=runner.GetComponent<CourseRival>();
        for(int lane=0;lane<6;lane++){
            Bots(false);session.ResetPractice();runner.standAnchor.position=starts[lane];runner.retryAnchor.position=starts[lane];botRunner.route=roster.RouteForSeat(lane);runner.Stand();botRunner.enabled=true;
            float began=-1,landed=-1;int jumps=runner.Motor.JumpCount;end=Time.time+30;
            while(!session.BellRung&&Time.time<end){if(!runner.IsSeated&&began<0)began=Time.time;if(botRunner.Waypoint>=1&&runner.transform.position.z>1.3f&&landed<0)landed=Time.time;yield return new WaitForFixedUpdate();}
            landingTimes.Add(landed-began);int index=lane;
            Test("Lane "+(lane+1)+" physically clears first pad and reaches bell without falling",()=>Require(session.BellRinger==runner&&runner.Falls==0&&runner.Motor.JumpCount-jumps>=7&&landed>began,"waypoint="+botRunner.Waypoint+" position="+runner.transform.position+" falls="+runner.Falls+" jumps="+(runner.Motor.JumpCount-jumps)+" firstSeconds="+(landed-began)));
        }
        Test("First landing times are equal within one small physics tolerance",()=>Require(landingTimes.Max()-landingTimes.Min()<.08f,"times="+string.Join(",",landingTimes)));
        Bots(false);game.enabled=true;roster.StartNewGame(6);PrepareFive();var attacker=session.participants[1];var victim=session.participants[2];session.Local.Stand();attacker.Stand();victim.Stand();attacker.Motor.Teleport(new Vector3(-.7f,.38f,5.7f));victim.Motor.Teleport(new Vector3(.6f,.38f,5.7f));
        var fighter=attacker.GetComponent<CourseRival>();int received=victim.Receiver.ReceivedHits;fighter.attacksEnabled=true;fighter.enabled=true;
        Test("AI chooses nearby AI rather than targeting only the human",()=>Require(fighter.ClosestOpponent()==victim,"target selection"));end=Time.time+1.8f;while(victim.Receiver.ReceivedHits==received&&Time.time<end)yield return new WaitForFixedUpdate();
        Test("AI can actually hit another AI",()=>Require(victim.Receiver.ReceivedHits>received,"AI hit missed"));Bots(false);
        roster.StartNewGame(6);yield return new WaitForSeconds(.4f);for(int i=0;i<6;i++)game.Match.Reveal(i);game.tableView.Focus();yield return new WaitForSeconds(.4f);
        Capture();ScreenCapture.CaptureScreenshot("Previews/Players_UI.png");yield return new WaitForSeconds(.3f);
    }
    void PrepareFive(){int guard=0;while(!game.Match.HasExactFive()&&game.Match.Turn>=0&&guard++<56)game.Match.Reveal(game.Match.Turn);Require(game.Match.HasExactFive(),"fixture five");}
    void Capture(){Directory.CreateDirectory("Previews");var camera=controller.playerCamera;var old=camera.targetTexture;var active=RenderTexture.active;var rt=RenderTexture.GetTemporary(1600,1000,24);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var texture=new Texture2D(1600,1000,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,1600,1000),0,0);texture.Apply();File.WriteAllBytes("Previews/Players_Cards.png",texture.EncodeToPNG());camera.targetTexture=old;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);Destroy(texture);}
    void Test(string name,Action action){try{action();checks.Add(new Check{name=name,passed=true,detail="passed"});Debug.Log("PASS "+name);}catch(Exception e){checks.Add(new Check{name=name,passed=false,detail=e.Message});Debug.LogError("FAIL "+name+": "+e.Message);}}
    static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
}
#endif
