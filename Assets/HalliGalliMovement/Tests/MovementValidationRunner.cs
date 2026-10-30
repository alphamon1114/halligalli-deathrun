#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalliGalli.Movement;
using UnityEditor;
using UnityEngine;

public sealed class MovementValidationRunner : MonoBehaviour
{
    [Serializable] private sealed class Result { public string name; public bool passed; public string detail; }
    [Serializable] private sealed class Report { public string unityVersion; public int passed, failed; public List<Result> checks; }
    private readonly List<Result> results = new List<Result>();
    private GameObject world;
    private SourceMotor motor;
    private const float Dt = 0.015f;

    private IEnumerator Start()
    {
        yield return null;
        foreach (var c in FindObjectsByType<SourcePlayerController>(FindObjectsSortMode.None)) c.enabled = false;
        foreach (var d in FindObjectsByType<MovementDemo>(FindObjectsSortMode.None)) d.enabled = false;
        Run("Project physics tick is 66.7 Hz", () => Near(Time.fixedDeltaTime, Dt, 0.00001f));
        Run("Walk acceleration and ground contact", () => {
            Setup(); Tick(new MovementCommand { move = Vector2.up }, 67);
            Near(motor.HorizontalSpeed, 4.5f, 0.06f); Require(motor.Grounded, "not grounded");
            Require(motor.transform.position.z > 3.8f, "insufficient travel");
        });
        Run("Releasing crouch restores the normal walking speed", () => {
            Setup(); Tick(new MovementCommand { move = Vector2.up, crouch = true }, 40);
            Near(motor.HorizontalSpeed, 2.2f, 0.06f);
            Tick(new MovementCommand { move = Vector2.up }, 67);
            Near(motor.HorizontalSpeed, 4.5f, 0.06f);Require(!motor.IsRunning,"normal walking selected the running animation");
        });
        Run("Diagonal speed normalization", () => {
            Setup(); Tick(new MovementCommand { move = Vector2.one }, 67);
            Near(motor.HorizontalSpeed, 4.5f, 0.06f);
        });
        Run("Friction stops motion progressively", () => {
            Setup(); Tick(new MovementCommand { move = Vector2.up }, 67);
            float start = motor.transform.position.z;
            Tick(default, 1); Require(motor.HorizontalSpeed > 0.5f && motor.HorizontalSpeed < 4.5f, "not a progressive stop");
            Tick(default, 67); Near(motor.HorizontalSpeed, 0, 0.01f);
            float drift = motor.transform.position.z - start;
            Require(drift > 0.1f && drift < 1.5f, "drift=" + drift);
        });
        Run("Jump apex, landing, no held-key auto jump", () => {
            Setup(); int before = motor.JumpCount; float floor = motor.transform.position.y;
            Tick(new MovementCommand { jumpPressed = true, jumpHeld = true }, 1);
            float peak = motor.transform.position.y;
            for (int i = 0; i < 140; i++) { Tick(new MovementCommand { jumpHeld = true }, 1); peak = Mathf.Max(peak, motor.transform.position.y); }
            Near(peak - floor, 1f, 0.08f); Require(motor.Grounded, "did not land");
            Require(motor.JumpCount == before + 1, "held key repeated jump");
        });
        Run("A fresh press permits a second jump", () => {
            Setup(); Tick(new MovementCommand { jumpPressed = true }, 1); Tick(default, 100);
            Tick(new MovementCommand { jumpPressed = true }, 1);
            Require(motor.JumpCount == 2 && motor.Velocity.y > 0, "second press ignored");
        });
        Run("Landing consumes a recently buffered jump", () => {
            Setup(); motor.Teleport(new Vector3(100, 0.14f, 0)); motor.AddImpulse(Vector3.down * 2);
            Tick(new MovementCommand { jumpPressed = true }, 1); Tick(default, 6);
            Require(motor.JumpCount == 1 && motor.Velocity.y > 0, "landing jump buffer was lost");
        });
        Run("Air momentum and directional air strafe", () => {
            Setup(); motor.Teleport(new Vector3(100, 5, 0)); motor.AddImpulse(new Vector3(7, 0, 0));
            Tick(default, 8); Near(motor.Velocity.x, 7, 0.001f);
            Tick(new MovementCommand { move = Vector2.up }, 1);
            Near(motor.Velocity.x, 7, 0.001f); Require(motor.Velocity.z > 0, "no air steering");
            Require(motor.Velocity.z <= motor.profile.airWishSpeedCap + 0.001f, "directional cap exceeded");
            Tick(new MovementCommand { move = Vector2.left }, 1);
            Require(motor.Velocity.x > 0, "reversed instantly in air");
        });
        Run("Wall collision and sliding", () => {
            Setup(); Box(new Vector3(102, 2, 0), new Vector3(0.2f, 4, 30));
            Tick(new MovementCommand { move = Vector2.one }, 120);
            Require(motor.transform.position.x < 101.7f, "passed through wall");
            Require(motor.transform.position.z > 3, "could not slide along wall");
            Require(Mathf.Abs(motor.Velocity.x) < 0.15f, "velocity still pushes into wall");
        });
        Run("Ceiling cancels upward movement", () => {
            Setup(); Box(new Vector3(100, 2.1f, 0), new Vector3(10, 0.2f, 10));
            Tick(new MovementCommand { jumpPressed = true }, 1); float peak = 0;
            for (int i = 0; i < 80; i++) { Tick(default, 1); peak = Mathf.Max(peak, motor.transform.position.y); }
            Require(peak < 0.28f, "passed through ceiling: " + peak); Require(motor.Grounded, "did not land after ceiling hit");
        });
        Run("Crouch height, reduced speed and blocked uncrouch", () => {
            Setup(); Tick(new MovementCommand { crouch = true }, 15); Near(motor.BodyHeight, 1f, 0.001f);
            Box(new Vector3(100, 1.35f, 0), new Vector3(8, 0.3f, 5));
            Tick(default, 20); Near(motor.BodyHeight, 1f, 0.001f);
            Tick(new MovementCommand { crouch = true, move = Vector2.up }, 100);
            Near(motor.HorizontalSpeed, 2.2f, 0.06f);
            Tick(default, 20); Near(motor.BodyHeight, 1.8f, 0.001f);
        });
        Run("Trigger volumes do not prevent standing", () => {
            Setup(); Tick(new MovementCommand { crouch = true }, 15);
            Box(new Vector3(100, 1.35f, 0), new Vector3(8, 0.3f, 5)).GetComponent<Collider>().isTrigger = true;
            Tick(default, 20); Near(motor.BodyHeight, 1.8f, 0.001f);
        });
        Run("Air duck preserves head instead of adding jump velocity", () => {
            Setup(); motor.profile.gravity = 0.001f; motor.Teleport(new Vector3(100, 5, 0));
            float head = motor.transform.position.y + motor.BodyHeight;
            Tick(new MovementCommand { crouch = true }, 12);
            Near(motor.transform.position.y + motor.BodyHeight, head, 0.005f);
            Require(motor.Velocity.y <= 0, "duck added upward velocity");
            Tick(default, 12); Near(motor.transform.position.y, 5, 0.005f);
        });
        Run("Climb 20 cm stairs without jumping", () => {
            Setup();
            for (int i = 0; i < 4; i++) Box(new Vector3(100, (i + 1) * 0.1f, 1.5f + i), new Vector3(4, (i + 1) * 0.2f, 1));
            float peak = 0;
            for (int i = 0; i < 100; i++) { Tick(new MovementCommand { move = Vector2.up }, 1); peak = Mathf.Max(peak, motor.transform.position.y); }
            Require(peak > 0.7f, "stair peak=" + peak + ", end=" + motor.transform.position + ", velocity=" + motor.Velocity + ", grounded=" + motor.Grounded); Require(motor.JumpCount == 0, "unexpected jump");
        });
        Run("Walkable slope ascent", () => {
            Setup(); var ramp = Box(new Vector3(100, 1.5f, 3), new Vector3(5, 0.2f, 6));
            ramp.transform.rotation = Quaternion.Euler(-30, 0, 0); Physics.SyncTransforms();
            float peak = 0;
            for (int i = 0; i < 100; i++) { Tick(new MovementCommand { move = Vector2.up }, 1); peak = Mathf.Max(peak, motor.transform.position.y); }
            Require(peak > 1.8f, "ramp peak=" + peak);
        });
        Run("Steep slope cannot be walked up", () => {
            Setup(); var ramp = Box(new Vector3(100, 2.72f, 3), new Vector3(5, 0.2f, 6));
            ramp.transform.rotation = Quaternion.Euler(-65, 0, 0); Physics.SyncTransforms();
            float peak = 0;
            for (int i = 0; i < 100; i++) { Tick(new MovementCommand { move = Vector2.up }, 1); peak = Mathf.Max(peak, motor.transform.position.y); }
            Require(peak < 0.5f, "climbed steep ramp=" + peak);
        });
        Run("Leaving a ledge produces a fall", () => {
            Setup(false); Box(new Vector3(100, -0.5f, 0), new Vector3(4, 1, 2)); Tick(default, 10);
            Tick(new MovementCommand { move = Vector2.up }, 100);
            Require(!motor.Grounded && motor.transform.position.y < -1, "hovered beyond ledge");
        });
        Run("A steep slope is not treated as a standing surface", () => {
            Setup(); var ramp = Box(new Vector3(100, 2.72f, 3), new Vector3(5, 0.2f, 6));
            ramp.transform.rotation = Quaternion.Euler(-65, 0, 0); Physics.SyncTransforms();
            motor.Teleport(new Vector3(100, 6, 3));
            for (int i = 0; i < 150; i++) { Tick(default, 1); if (motor.transform.position.y > 0.6f) Require(!motor.Grounded, "standing on steep slope"); }
            Require(motor.transform.position.y < 0.2f, "did not slide to the floor");
        });
        Run("Jump height stable across simulation tick sizes", () => {
            float a = JumpPeak(0.01f); float b = JumpPeak(0.02f); Near(a, b, 0.04f);
        });
        Run("Mouse yaw accumulates deltas independent of frame splits", () => {
            Setup(); var camera = new GameObject("Test camera").AddComponent<Camera>();
            camera.transform.SetParent(motor.transform);
            var controller = motor.gameObject.AddComponent<SourcePlayerController>(); controller.enabled = false;
            controller.ResetView(); controller.ApplyLook(new Vector2(100, 0)); float one = motor.transform.eulerAngles.y;
            controller.ResetView(); controller.ApplyLook(new Vector2(50, 0)); controller.ApplyLook(new Vector2(50, 0));
            Near(motor.transform.eulerAngles.y, one, 0.001f); Near(one, 12, 0.001f);
        });
        if (world != null) world.SetActive(false);
        var character = FindFirstObjectByType<CharacterMotionDriver>();
        Run("Character and realistic hands connected", () => {
            Require(character && character.bodyModel && character.handsModel && character.handCamera, "missing character links");
            Require(character.motions.bodyClips.Length >= 25 && character.motions.handClips.Length >= 16, "motion library incomplete");
            Require(character.handsModel.GetComponentsInChildren<SkinnedMeshRenderer>().Length >= 2, "missing left/right skins");
            Require(character.handsModel.GetComponentsInChildren<Transform>().Count(t => !t.name.Contains("Nail") && (t.name.Contains("Thumb") || t.name.Contains("Index") || t.name.Contains("Middle") || t.name.Contains("Ring") || t.name.Contains("Little"))) == 30, "finger rig incomplete");
        });
        Run("All custom animation curves resolve to model bones", () => {
            foreach(bool hands in new[]{false,true}) {
                var target=hands?character.handsModel:character.bodyModel;
                foreach(var clip in hands?character.motions.handClips:character.motions.bodyClips) {
                    Require(clip && clip.length>.1f,"empty clip");
                    foreach(var binding in AnimationUtility.GetCurveBindings(clip)) Require(target.Find(binding.path)!=null,"unbound curve: "+binding.path);
                }
            }
        });
        Run("Custom poses remain finite and crouch fits tunnel", () => {
            var clone=Instantiate(character.bodyModel.gameObject);clone.transform.position=Vector3.zero;clone.transform.rotation=Quaternion.identity;
            try {
                float standing=0,crouching=0;
                foreach(string state in new[]{"Idle","WalkForward","RunForward","Jump","Fall","CrouchIdle","CrouchWalkForward","PunchLeft","PunchRight","Kick"}) {
                    var clip=character.motions.bodyClips.First(c=>c.name==state);clip.SampleAnimation(clone,clip.length*.25f);
                    float top=float.MinValue,bottom=float.MaxValue;
                    foreach(var skin in clone.GetComponentsInChildren<SkinnedMeshRenderer>()) {
                        var baked=new Mesh();skin.BakeMesh(baked,true);
                        foreach(var v in baked.vertices) {var p=skin.transform.TransformPoint(v);Require(!float.IsNaN(p.x+p.y+p.z),"invalid pose");top=Mathf.Max(top,p.y);bottom=Mathf.Min(bottom,p.y);}
                        Destroy(baked);
                    }
                    Require(top<2.1f&&bottom>-.18f,"pose outside body envelope: "+state+" top="+top+" bottom="+bottom);
                    if(state=="Idle")standing=top;if(state=="CrouchIdle")crouching=top;
                }
                Require(standing>1.65f&&crouching<1.10f,"crouch height="+crouching);
            } finally {Destroy(clone);}
        });
        if(character) {
            var cm=character.motor;cm.Teleport(new Vector3(0,.03f,-3));
            for(int i=0;i<15;i++){Physics.SyncTransforms();cm.Simulate(default,Dt);}
            yield return new WaitForSeconds(.3f);
            Run("Animation follows idle state",()=>Require(character.CurrentBodyState=="Idle","state="+character.CurrentBodyState));
            for(int i=0;i<30;i++){Physics.SyncTransforms();cm.Simulate(new MovementCommand{move=Vector2.up},Dt);}
            yield return null;yield return null;
            Run("Animation follows walking",()=>Require(character.CurrentBodyState=="WalkForward" && character.CurrentHandState=="Walk","walk state mismatch"));
            for(int i=0;i<20;i++){Physics.SyncTransforms();cm.Simulate(new MovementCommand{move=Vector2.up},Dt);}
            yield return null;yield return null;
            Run("Sustained movement keeps the normal walk animation",()=>Require(character.CurrentBodyState=="WalkForward" && character.CurrentHandState=="Walk" && Mathf.Abs(cm.HorizontalSpeed-4.5f)<.06f,"normal movement changed gait or speed"));
            cm.Simulate(new MovementCommand{jumpPressed=true},Dt);yield return null;yield return null;
            Run("Animation follows jump without moving capsule",()=>Require(character.CurrentBodyState=="Jump" && cm.Velocity.y>0,"jump state mismatch"));
            cm.Teleport(new Vector3(0,.03f,-3));
            for(int i=0;i<20;i++){Physics.SyncTransforms();cm.Simulate(new MovementCommand{crouch=true},Dt);}
            yield return null;yield return null;
            Run("Animation follows actual crouch height",()=>Require(character.CurrentBodyState=="CrouchIdle" && character.CurrentHandState=="CrouchIdle","crouch mismatch"));
            cm.Simulate(new MovementCommand{crouch=true,jumpPressed=true},Dt);yield return null;yield return null;
            Run("Air duck keeps the body compressed",()=>Require(!cm.Grounded && character.CurrentBodyState=="CrouchIdle" && character.CurrentHandState=="Jump","air duck pose mismatch"));
        }
        yield return CombatValidation.Run(Run,character);
        yield return ReactionValidation.Run(Run,character);
        int passed = results.FindAll(r => r.passed).Count;
        var report = new Report { unityVersion = Application.unityVersion, passed = passed, failed = results.Count - passed, checks = results };
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        Directory.CreateDirectory(Path.Combine(root, "TestResults"));
        File.WriteAllText(Path.Combine(root, "TestResults/movement-validation.json"), JsonUtility.ToJson(report, true));
        Debug.Log("MOVEMENT_VALIDATION_RESULT " + passed + "/" + results.Count);
        if (report.failed == 0)
        {
            Directory.CreateDirectory(Path.Combine(root, "Exports"));
            AssetDatabase.ExportPackage(new[]{"Assets/HalliGalliMovement","Assets/HalliGalliCards"}, Path.Combine(root, "Exports/HalliGalliMovement.unitypackage"), ExportPackageOptions.Recurse);
        }
        if (Application.isBatchMode) EditorApplication.Exit(report.failed == 0 ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }

    private void Setup(bool floor = true)
    {
        if (world != null) { world.SetActive(false); Destroy(world); }
        world = new GameObject("Test world");
        if (floor) Box(new Vector3(100, -0.5f, 0), new Vector3(80, 1, 80));
        var actor = new GameObject("Test actor", typeof(CharacterController));
        actor.transform.SetParent(world.transform);
        actor.transform.position = new Vector3(100, 0.02f, 0);
        motor = actor.AddComponent<SourceMotor>();
        Physics.SyncTransforms();
        Tick(default, 10);
    }

    private GameObject Box(Vector3 position, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.transform.SetParent(world.transform);
        go.transform.position = position;
        go.transform.localScale = size;
        Physics.SyncTransforms();
        return go;
    }

    private void Tick(MovementCommand command, int count, float dt = Dt)
    {
        for (int i = 0; i < count; i++) { Physics.SyncTransforms(); motor.Simulate(command, dt); command.jumpPressed = false; }
    }

    private float JumpPeak(float dt)
    {
        Setup(); float baseline = motor.transform.position.y;
        Tick(new MovementCommand { jumpPressed = true }, 1, dt); float peak = 0;
        for (int i = 0; i < Mathf.CeilToInt(1.2f / dt); i++) { Tick(default, 1, dt); peak = Mathf.Max(peak, motor.transform.position.y - baseline); }
        return peak;
    }

    private void Run(string name, Action check)
    {
        try { check(); results.Add(new Result { name = name, passed = true, detail = "passed" }); Debug.Log("PASS " + name); }
        catch (Exception e) { results.Add(new Result { name = name, passed = false, detail = e.Message }); Debug.LogError("FAIL " + name + ": " + e.Message); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Near(float actual, float expected, float tolerance) { Require(Mathf.Abs(actual - expected) <= tolerance, "expected " + expected + " +/- " + tolerance + ", actual " + actual); }
}
#endif
