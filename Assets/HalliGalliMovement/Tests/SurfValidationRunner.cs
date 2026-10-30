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

public sealed class SurfValidationRunner : MonoBehaviour
{
    public bool probeOnly;
    [Serializable] class Check { public string name; public bool passed; public string detail; }
    [Serializable] class Report { public int passed, failed; public List<Check> checks; }
    readonly List<Check> checks = new List<Check>();
    CourseSession session; CourseCardGame game; CourseRoster roster;
    string lastDetail="passed";
    void Bots(bool enabled) { foreach (var p in roster.allParticipants) { var bot = p.GetComponent<CourseRival>(); if (bot) { bot.enabled = enabled; bot.attacksEnabled = false; } } }
    IEnumerator Start()
    {
        var routine = Validate();
        while (true) {
            bool more; object next = null;
            try { more = routine.MoveNext(); if (more) next = routine.Current; }
            catch (Exception e) { Test("Runner completed", () => throw e); break; }
            if (!more) break; yield return next;
        }
        Time.timeScale = 1; Save(); Debug.Log("SURF_VALIDATION_RESULT " + checks.Count(c => c.passed) + "/" + checks.Count);
        EditorApplication.isPlaying = false;
    }
    void Save()
    {
        Directory.CreateDirectory("TestResults");
        File.WriteAllText("TestResults/" + (probeOnly ? "surf-probe" : "surf-validation") + ".json", JsonUtility.ToJson(new Report { passed = checks.Count(c => c.passed), failed = checks.Count(c => !c.passed), checks = checks }, true));
    }
    IEnumerator Validate()
    {
        yield return null; yield return null;
        session = FindAnyObjectByType<CourseSession>(); game = session.cardGame; roster = session.roster;
        FindAnyObjectByType<CourseHUD>().ReadInput=false;
        var controller = session.Local.GetComponent<SourcePlayerController>(); controller.controlsEnabled = false; controller.SetCursorCaptured(false); controller.captureCursorOnStart = false;
        game.ReadInput = false; game.botEnabled = false; game.shuffleOnReset = false; Bots(false); Time.timeScale = 5;
        var reference=CoursePacingReference.Load();
        Test("Compact surf retains the drop, reverse run and rising right curve",()=>{
            var s=roster.surf.sections;
            Require(s.Length==3&&s.Sum(section=>section.Length)<reference.tutorialDistance*1.7f,"surf ramp length");
            Require(Vector3.Dot(s[0].Sample(s[0].points[0]).forward,s[1].Sample(s[1].points[0]).forward)<-.99f&&s[0].points.Last().y-s[0].landing.y>5,"drop or reverse direction missing");
            Require(s[2].headings.Last()-s[2].headings.First()>85&&s[2].points.Last().y>s[2].points.Min(p=>p.y)+2,"rising curve missing");
        });
        Test("Surf is selectable with its own movement profile", () => Require(session.maps.current.id == "surf" && session.maps.CanSelect(session.maps.current) && session.participants.All(p => p.Motor.profile.surfEnabled) && session.maps.catalog.maps.Where(m => m.id == "summit" || m.id == "first-steps").All(m => !m.movementProfile.surfEnabled), "catalog/profile"));
        Test("Six actual steep ramp colliders and a common bell landing exist", () => {
            var ramps = FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Where(c => c.name.StartsWith("Surf ramp ")).ToArray();
            Require(ramps.Length == 6, "ramp count");
            foreach (var ramp in ramps) { var n = ramp.sharedMesh.normals[0]; Require(n.y > .5f && n.y < .65f, "ramp is not a surf slope"); }
            Require(roster.surf && session.fallHeight < -15 && roster.surf.finish.position.y > session.fallHeight + 5, "finish or fall zone");
        });
        game.enabled = false; var runner = roster.allParticipants[1]; var botRunner = runner.GetComponent<CourseRival>();
        if (!probeOnly) {
            foreach(float side in new[]{-1f,1f}) foreach(float speed in new[]{14f,10f}) {
                session.ResetPractice(); runner.Stand();
                var testMotor=runner.Motor; var nav=new SurfCourse.Navigation { section=2,phase=1 };
                testMotor.Teleport(new Vector3(-9,-15.15f,-4+side*3.2f));testMotor.AddImpulse(Vector3.left*speed);
                var fixture=roster.RouteForSeat(side>0?0:5);
                bool landed=false, launched=false, fell=false;float launchSpeed=0,launchUp=0;
                for(int tick=0;tick<360;tick++) {
                    var input=roster.surf.Guide(runner,fixture,nav);
                    float yaw=Quaternion.LookRotation(input.forward).eulerAngles.y;
                    var local=Quaternion.Inverse(Quaternion.Euler(0,yaw,0))*input.steering.normalized;
                    testMotor.Simulate(new MovementCommand { move=new Vector2(local.x,local.z),yaw=yaw },.015f);
                    if(!launched&&runner.transform.position.x<-14.2f) { launched=true;launchSpeed=testMotor.HorizontalSpeed;launchUp=testMotor.Velocity.y; }
                    if(testMotor.Grounded&&runner.transform.position.x<-16.6f) { landed=true;break; }
                    if(runner.transform.position.y<session.fallHeight) { fell=true;break; }
                }
                Test("Physical kicker side "+side+" at "+speed+" m/s "+(speed>12?"lands":"falls short"),
                    ()=>Require(speed>12?landed&&launched&&launchUp>0:!landed&&fell,
                    "landed="+landed+" fell="+fell+" exit="+launchSpeed+" up="+launchUp+" position="+runner.transform.position));
            }
            session.ResetPractice();runner.Stand();runner.Motor.Teleport(roster.surf.finish.position);
            runner.Motor.Simulate(default,.015f);
            Test("Dropping directly onto the finish cannot skip the surf course",()=>Require(!session.CanRing(runner)&&roster.surf.GatesPassed(runner)==0,"skipped course accepted"));
            session.ResetPractice(); runner.Stand(); var motor = runner.Motor;
            motor.Teleport(new Vector3(3.2f, .6f, 10)); motor.AddImpulse(new Vector3(0, -2, 8));
            int contacts = motor.SurfContactTicks, jumps = motor.JumpCount; int airborneContacts = 0;
            for (int i = 0; i < 75; i++) { motor.Simulate(new MovementCommand { move = Vector2.right, jumpPressed = true }, .015f); if (motor.Surfing && !motor.Grounded) airborneContacts++; }
            Test("Holding into a ramp surfs without ground friction or repeated jumps", () => Require(motor.SurfContactTicks - contacts > 15 && airborneContacts > 15 && motor.JumpCount == jumps && motor.Velocity.z >= 7.8f && !motor.Grounded, "contacts=" + (motor.SurfContactTicks - contacts) + " airborne=" + airborneContacts + " position=" + runner.transform.position + " velocity=" + motor.Velocity));
            float heldX = runner.transform.position.x;
            motor.Teleport(new Vector3(3.2f, .6f, 10)); motor.AddImpulse(new Vector3(0, -2, 8));
            for (int i = 0; i < 75; i++) motor.Simulate(default, .015f);
            Test("Releasing lateral input slides downhill instead of sticking to the ramp", () => Require(runner.transform.position.x < heldX - .3f, "release x=" + runner.transform.position.x + " held x=" + heldX));
            var before = motor.Velocity; motor.AddImpulse(new Vector3(-6, 2, 0));
            Test("A surf rider retains forward momentum when knocked away", () => Require(motor.Velocity.x < before.x - 5.9f && Mathf.Abs(motor.Velocity.z - before.z) < .001f && !motor.Grounded, "knockback overwritten"));
        }
        foreach (int count in probeOnly ? new[] { 6 } : new[] { 2, 3, 4, 5, 6 }) {
            roster.StartNewGame(count); yield return new WaitForFixedUpdate();
            var starts = session.participants.Select(p => p.standAnchor.position).ToArray();
            Test(count + " players retain equal first jumps and both surf entrances", () => Require(Enumerable.Range(0, count).All(i => Mathf.Abs(Vector3.ProjectOnPlane(roster.firstPlatforms[i].position - starts[i], Vector3.up).magnitude - 3) < .001f) && roster.RouteForSeat(0)[3].position.x < 0 && roster.RouteForSeat(count - 1)[3].position.x > 0, "entrance layout"));
            for (int seat = 0; seat < (probeOnly ? 1 : count); seat++) {
                Bots(false); session.ResetPractice(); yield return new WaitForFixedUpdate();
                botRunner.route = roster.RouteForSeat(seat); runner.standAnchor.position = starts[seat]; runner.retryAnchor.position = starts[seat];
                runner.Stand(); botRunner.enabled = true;
                float start = Time.time, end = Time.time + 35, nextTrace = 0; int contacts = runner.Motor.SurfContactTicks;
                var trace = new List<string>(); var sections = new HashSet<int>(); int airborne = 0; float peakSpeed = 0;
                while (!session.BellRung && Time.time < end) {
                    var pos = runner.transform.position;
                    if (runner.Motor.Surfing) sections.Add(botRunner.SurfSection);
                    if (!runner.Motor.Grounded && !runner.Motor.Surfing && botRunner.SurfPhase == 2) airborne++;
                    peakSpeed = Mathf.Max(peakSpeed, runner.Motor.HorizontalSpeed);
                    if (Time.time >= nextTrace) { nextTrace = Time.time + .3f; trace.Add((Time.time - start).ToString("F2") + " w=" + botRunner.Waypoint + " s=" + botRunner.SurfSection + "/" + botRunner.SurfPhase + " gates=" + roster.surf.GatesPassed(runner) + " surf=" + runner.Motor.Surfing + " ground=" + runner.Motor.Grounded + " p=" + pos + " v=" + runner.Motor.Velocity); }
                    yield return new WaitForFixedUpdate();
                }
                lastDetail="seconds="+(Time.time-start).ToString("F2")+" tutorialRatio="+((Time.time-start)/reference.tutorialSeconds).ToString("F2");
                Test(count + " players, lane " + (seat + 1) + " physically surfs all three ramps and reaches the bell", () => Require(session.BellRinger == runner && runner.Falls == 0 && runner.Motor.SurfContactTicks - contacts > 40 && sections.Count == 3 && airborne > 3 && Time.time-start>=reference.tutorialSeconds*1.3f && Time.time-start<=reference.tutorialSeconds*2.5f, "falls=" + runner.Falls + " sections=" + sections.Count + " contacts=" + (runner.Motor.SurfContactTicks - contacts) + " peak=" + peakSpeed + " "+lastDetail+"\n" + string.Join("\n", trace)));
            }
        }
        if (probeOnly) yield break;
        Bots(false);game.enabled=false;roster.StartNewGame(6);yield return new WaitForFixedUpdate();
        var chaser=roster.allParticipants[2];var chaseBot=chaser.GetComponent<CourseRival>();
        session.ResetPractice();yield return new WaitForFixedUpdate();
        botRunner.route=roster.RouteForSeat(5);runner.standAnchor.position=roster.allParticipants[5].standAnchor.position;runner.retryAnchor.position=runner.standAnchor.position;
        chaseBot.route=roster.RouteForSeat(0);chaser.standAnchor.position=roster.allParticipants[0].standAnchor.position;chaser.retryAnchor.position=chaser.standAnchor.position;
        botRunner.surfWideTurn=4;runner.Stand();botRunner.enabled=true;
        yield return new WaitForSeconds(.65f);
        chaser.Stand();chaseBot.enabled=true;float chaseDeadline=Time.time+25;
        while(!session.BellRung&&Time.time<chaseDeadline)yield return new WaitForFixedUpdate();
        Test("A later rider can overtake through a tighter turn and inside line",()=>Require(session.BellRinger==chaser&&chaser.Falls==0,"winner="+(session.BellRinger?session.BellRinger.name:"none")+" chaser falls="+chaser.Falls));
        botRunner.surfWideTurn=0;Bots(false);
        Bots(false); game.enabled = true; roster.StartNewGame(6); yield return new WaitForSeconds(.4f);
        int guard = 0; while (!game.Match.HasExactFive() && game.Match.Turn >= 0 && guard++ < 56) game.Match.Reveal(game.Match.Turn);
        Require(game.Match.HasExactFive(), "five fixture");
        session.Local.Stand(); runner.Stand(); botRunner.enabled = true; float deadline = Time.time + 35;
        while (session.BellCount == 0 && Time.time < deadline) yield return new WaitForFixedUpdate(); Bots(false);
        Test("Surf bell collects cards and immediately seats every player", () => Require(session.BellCount == 1 && session.EveryoneSeated && !game.Match.Paused && game.Match.LastCollector == 1 && game.Match.AllCards().Count() == 56, "bell/card integration"));
        roster.StartNewGame(6); yield return new WaitForSeconds(.4f); guard = 0;
        while (!game.Match.HasExactFive() && game.Match.Turn >= 0 && guard++ < 56) game.Match.Reveal(game.Match.Turn);
        Bots(true); foreach (var p in session.participants.Skip(1)) p.GetComponent<CourseRival>().attacksEnabled = true;
        game.botEnabled = true; session.Local.Stand(); deadline = Time.time + 75;
        while (session.BellCount == 0 && Time.time < deadline) yield return new WaitForFixedUpdate();
        Bots(false); game.botEnabled = false;
        Test("Five attacking AI complete a real surf race", () => Require(session.BellCount == 1 && session.EveryoneSeated && game.Match.AllCards().Count() == 56, "race did not finish"));
        game.enabled = false; session.ResetPractice(); runner.Stand();
        runner.Motor.Teleport(new Vector3(0, session.fallHeight - 1, 30)); int falls = runner.Falls;
        yield return new WaitForSeconds(1);
        Test("A fall returns the rider to the original entrance", () => Require(runner.Falls == falls + 1 && !runner.IsRespawning && Vector3.Distance(runner.transform.position, runner.retryAnchor.position) < .2f, "fall/respawn"));
        var hud = FindAnyObjectByType<CourseHUD>(); hud.OpenMapMenu(); yield return new WaitForEndOfFrame();
        var shot = ScreenCapture.CaptureScreenshotAsTexture(); Directory.CreateDirectory("Previews"); File.WriteAllBytes("Previews/Surf_Menu.png", shot.EncodeToPNG()); Destroy(shot);
    }
    void Test(string name, Action action)
    {
        try { action(); checks.Add(new Check { name = name, passed = true, detail = lastDetail }); Debug.Log("PASS " + name); }
        catch (Exception e) { checks.Add(new Check { name = name, passed = false, detail = e.Message }); Debug.LogError("FAIL " + name + ": " + e.Message); }
        lastDetail="passed";Save();
    }
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
#endif
