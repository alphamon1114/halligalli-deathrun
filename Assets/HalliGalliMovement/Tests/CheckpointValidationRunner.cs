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

public sealed class CheckpointValidationRunner:MonoBehaviour
{
    [Serializable]class Check{public string name,detail;public bool passed;}
    [Serializable]class Report{public int passed,failed;public List<Check> checks;}
    static CheckpointValidationRunner active;
    readonly List<Check> checks=new List<Check>();
    CourseSession session;CourseCardGame game;
    void Awake(){if(active&&active!=this){Destroy(gameObject);return;}active=this;}
    void OnDestroy(){if(active==this)active=null;}
    IEnumerator Start()
    {
        DontDestroyOnLoad(gameObject);var routine=Validate();
        while(true){bool more;object value=null;try{more=routine.MoveNext();if(more)value=routine.Current;}catch(Exception e){Test("Validation completed",()=>throw e);break;}if(!more)break;yield return value;}
        Time.timeScale=1;Save();Debug.Log("CHECKPOINT_VALIDATION_RESULT "+checks.Count(c=>c.passed)+"/"+checks.Count);EditorApplication.isPlaying=false;
    }
    void Bind()
    {
        session=FindAnyObjectByType<CourseSession>();game=session.cardGame;game.ReadInput=false;game.botEnabled=false;game.shuffleOnReset=false;
        var controller=session.Local.GetComponent<SourcePlayerController>();controller.enabled=false;controller.captureCursorOnStart=false;controller.SetCursorCaptured(false);
        FindAnyObjectByType<CourseHUD>().ReadInput=false;Bots(false);Time.timeScale=5;
    }
    void Bots(bool enabled){foreach(var p in session.roster.allParticipants){var bot=p.GetComponent<CourseRival>();if(bot){bot.enabled=enabled;bot.attacksEnabled=false;}}}
    IEnumerator Validate()
    {
        yield return null;yield return null;Bind();var catalog=session.maps.catalog;
        foreach(string mapId in new[]{"first-steps","summit","surf"}){
            var map=catalog.maps.Single(m=>m.id==mapId);
            if(session.maps.current!=map){Require(session.maps.TryStart(map,6),"map load rejected");while(SceneManager.GetActiveScene().path!=map.scenePath)yield return null;yield return null;yield return null;Bind();}
            Test(mapId+": visible midcourse markers add no collision",()=>Require(session.checkpoints&&session.checkpoints.points.Length==1&&session.checkpoints.GetComponentsInChildren<Collider>().Length==0,"checkpoint installation"));
            game.enabled=false;session.roster.StartNewGame(6);yield return new WaitForFixedUpdate();
            var runner=session.participants[1];var bot=runner.GetComponent<CourseRival>();
            runner.Stand();runner.BeginFall();yield return new WaitForSeconds(.85f);
            Test(mapId+": falling before the checkpoint still returns to the start",()=>Require(!runner.HasCheckpoint&&Vector3.Distance(runner.transform.position,runner.retryAnchor.position)<.1f,"early fall destination"));
            session.ResetPractice();runner.Stand();var first=session.checkpoints.points[0];
            runner.Motor.Teleport(first.spawn.position+Vector3.up*1.5f);runner.Motor.Simulate(default,.015f);yield return new WaitForFixedUpdate();
            Test(mapId+": passing above a marker does not save it",()=>Require(!runner.HasCheckpoint,"air checkpoint"));
            if(mapId=="surf"){
                runner.Motor.Teleport(first.spawn.position);runner.Motor.Simulate(default,.015f);yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();
                Test("surf: reaching the checkpoint without the first surf gate is rejected",()=>Require(!runner.HasCheckpoint,"surf gate skip"));
            }
            foreach(int count in new[]{2,6}){
                session.roster.StartNewGame(count);yield return new WaitForFixedUpdate();
                var starts=session.participants.Select(p=>p.standAnchor.position).ToArray();
                foreach(int lane in new[]{0,count-1}){
                    Bots(false);session.ResetPractice();yield return new WaitForFixedUpdate();
                    bot.route=session.roster.RouteForSeat(lane);runner.standAnchor.position=starts[lane];runner.retryAnchor.position=starts[lane];runner.Stand();bot.enabled=true;
                    float deadline=Time.time+18;
                    while(!runner.HasCheckpoint&&Time.time<deadline)yield return new WaitForFixedUpdate();
                    string prefix=mapId+" "+count+"p lane "+(lane+1);
                    Test(prefix+": real traversal saves a personal checkpoint",()=>Require(runner.HasCheckpoint&&runner.Falls==0&&session.participants.Where(p=>p!=runner).All(p=>p.CheckpointIndex==-1),"checkpoint not reached or shared"));
                    if(!runner.HasCheckpoint){Bots(false);continue;}
                    int saved=runner.CheckpointIndex;var point=session.checkpoints.Saved(runner);var expected=session.checkpoints.SpawnPosition(runner);
                    if(mapId=="surf"){
                        deadline=Time.time+10;while(session.roster.surf.GatesPassed(runner)<2&&Time.time<deadline)yield return new WaitForFixedUpdate();
                        Test(prefix+": reached the second surf gate before falling",()=>Require(session.roster.surf.GatesPassed(runner)==2,"second gate missing"));
                    }
                    bot.enabled=false;runner.BeginFall();yield return new WaitForSeconds(.3f);
                    Test(prefix+": a fall still costs time and disables combat",()=>Require(runner.IsRespawning&&!runner.Combat.TryKick()&&!runner.Receiver.canReceiveHits,"fall penalty removed"));
                    yield return new WaitForSeconds(.55f);
                    Test(prefix+": respawn restores the checkpoint facing with zero momentum",()=>Require(runner.HasCheckpoint&&runner.CheckpointIndex==saved&&!runner.IsRespawning&&Vector3.Distance(runner.transform.position,expected)<.1f&&runner.Motor.Velocity.sqrMagnitude<.001f&&Mathf.Abs(Mathf.DeltaAngle(runner.transform.eulerAngles.y,point.spawn.eulerAngles.y))<.1f,"respawn pose/velocity"));
                    if(mapId=="surf"){
                        Test(prefix+": later gates are rolled back to the saved landing",()=>Require(session.roster.surf.GatesPassed(runner)==1,"stale gates after respawn"));
                        runner.Motor.Teleport(session.roster.surf.finish.position);runner.Motor.Simulate(default,.015f);
                        Test(prefix+": checkpoint cannot bypass the remaining surf gates",()=>Require(!session.CanRing(runner),"finish shortcut accepted"));
                        runner.Motor.Teleport(expected);
                    }
                    bot.enabled=true;deadline=Time.time+15;
                    while(!session.BellRung&&Time.time<deadline)yield return new WaitForFixedUpdate();Bots(false);
                    Test(prefix+": the racer physically completes the rest of the course after falling",()=>Require(session.BellRinger==runner&&runner.Falls==1,"resume navigation; waypoint="+bot.Waypoint+" position="+runner.transform.position+" falls="+runner.Falls));
                    runner.Sit();Test(prefix+": sitting clears the saved point",()=>Require(runner.CheckpointIndex==-1,"checkpoint leaked into next run"));
                }
            }
            Bots(false);game.enabled=true;session.roster.StartNewGame(6);yield return new WaitForSeconds(.4f);
            runner.Stand();runner.Motor.Teleport(first.spawn.position);runner.Motor.Simulate(default,.015f);
            if(session.roster.surf)session.roster.surf.RestoreCheckpointProgress(runner,1);
            yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();
            Test(mapId+": wrong standing cannot save a checkpoint",()=>Require(game.MustReturn(runner)&&!runner.HasCheckpoint&&runner.CheckpointIndex==-1,"penalty bypass"));
            runner.BeginFall();yield return new WaitForSeconds(.85f);
            Test(mapId+": a penalized player still returns beside their chair",()=>Require(Vector3.Distance(runner.transform.position,runner.standAnchor.position)<.1f&&game.MustReturn(runner),"wrong stand respawn"));
            session.roster.StartNewGame(6);yield return new WaitForSeconds(.4f);
            for(int i=0;i<56&&!game.Match.HasExactFive();i++)game.Match.Reveal(game.Match.Turn);
            Require(game.Match.HasExactFive(),"exposed five fixture");
            runner.Stand();bot.enabled=true;float end=Time.time+18;
            while(!runner.HasCheckpoint&&Time.time<end)yield return new WaitForFixedUpdate();
            Test(mapId+": a valid card race enables checkpoints",()=>Require(runner.HasCheckpoint&&game.Match.Paused,"valid race save"));
            runner.BeginFall();end=Time.time+20;
            while(session.BellCount==0&&Time.time<end)yield return new WaitForFixedUpdate();Bots(false);
            Test(mapId+": ringing after a fall awards cards, seats all and clears checkpoints",()=>Require(session.BellCount==1&&session.EveryoneSeated&&!game.Match.Paused&&game.Match.AllCards().Count()==56&&session.participants.All(p=>p.CheckpointIndex==-1),"bell/reset integration"));
            session.roster.StartNewGame(2);
            Test(mapId+": restarting with a different roster clears checkpoint state",()=>Require(session.participants.All(p=>p.CheckpointIndex==-1&&p.Falls==0),"restart state"));
        }
    }
    void Save(){Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/checkpoint-validation.json",JsonUtility.ToJson(new Report{passed=checks.Count(c=>c.passed),failed=checks.Count(c=>!c.passed),checks=checks},true));}
    void Test(string name,Action action){try{action();checks.Add(new Check{name=name,passed=true,detail="passed"});Debug.Log("PASS "+name);}catch(Exception e){checks.Add(new Check{name=name,passed=false,detail=e.Message});Debug.LogError("FAIL "+name+": "+e.Message);}Save();}
    static void Require(bool condition,string detail){if(!condition)throw new Exception(detail);}
}
#endif
