#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalliGalli.Movement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Observe after PracticeSurfArea (90); the separate input driver simulates at 0.
[DefaultExecutionOrder(100)]
public sealed class PracticeValidationRunner : MonoBehaviour
{
    const string ReportPath = "TestResults/practice-validation.json";
    const string State = "HalliGalli.PracticeValidation.";
    const float RuntimeSeconds = 360, EditorSeconds = 420, AttemptSeconds = 20;
    const int MaximumAttempts = 3;
    static PracticeValidationRunner instance;

    [Serializable] sealed class Check { public string name, detail; public bool passed; }
    [Serializable] sealed class Capture { public string name, path, utc, method, limitation; public int stage; }
    [Serializable] sealed class Sample
    {
        public int tick, respawnSequence;
        public float seconds;
        public Vector3 position, velocity, surfNormal;
        public Vector2 move;
        public bool grounded, surfing, inCatch, inFinish, completed;
        public string state;
    }
    [Serializable] sealed class Trial
    {
        public string kind, startedUtc, completedUtc, outcome;
        public int stage, attempt, ticks, respawnStart, respawnEnd, fallsStart, fallsEnd, surfTicks, airTicks, catchTicks, groundTicks;
        public float seconds, wallSeconds, firstSurfSeconds = -1, firstGapSeconds = -1, firstCatchSeconds = -1, finishSeconds = -1;
        public bool finished, completed, startedGrounded, crossedGap, caughtBank, injectedFixture;
        public Vector3 startPosition, endPosition;
        public List<Sample> trajectory = new List<Sample>();
        public List<Sample> transitions = new List<Sample>();
    }
    [Serializable] sealed class StageResult
    {
        public int stage, attempts, retries;
        public bool completed;
        public float seconds;
    }
    [Serializable] sealed class ProfileSnapshot
    {
        public string label, actualProfile, expectedProfile;
        public int stage;
        public float radius, height, skinWidth, stepOffset, slopeLimit, fieldOfView;
        public Vector3 center;
        public bool capsuleEnabled, matched;
    }
    [Serializable] sealed class Report
    {
        public string runId, utc, completedUtc, status, unityVersion, graphicsDevice, invocation, traversalMethod;
        public bool batchMode;
        public float fixedDeltaTime;
        public int passed, failed;
        public List<Check> checks = new List<Check>();
        public List<Capture> captures = new List<Capture>();
        public List<Trial> trials = new List<Trial>();
        public List<StageResult> stages = new List<StageResult>();
        public List<ProfileSnapshot> profiles = new List<ProfileSnapshot>();
        public List<string> limitations = new List<string>();
    }

    [SerializeField] bool exitEditor;
    Report report;
    CourseNetwork network;
    CourseSession session;
    CourseParticipant local;
    CourseParticipant[] targets;
    CourseHUD hud;
    PracticeSurfArea surf;
    PracticeSurfGeometry geometry;
    PracticeSurfValidationDriver driver;
    SourcePlayerController controller;
    SourceMovementProfile combatProfile, surfProfile;
    Trial activeTrial, lastTrial;
    bool stopAtFirstBank, finished, originalControllerEnabled, originalHudInput;
    float airborneSeconds;
    double deadline, trialStarted;
    readonly WaitForFixedUpdate fixedStep = new WaitForFixedUpdate();

    // Unity.exe -batchmode -disable-audio -projectPath <repo> -executeMethod
    // PracticeValidationRunner.Run -logFile <log>. Do not add -quit or -nographics.
    // Omit -batchmode for actual Game-view HUD/selector captures. Both modes exit.
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run PracticeValidationRunner.Run from edit mode.");
        var initial = NewReport();
        WriteReport(initial);
        SessionState.SetBool(State + "active", true);
        SessionState.SetFloat(State + "deadline", (float)EditorApplication.timeSinceStartup + EditorSeconds);
        SessionState.SetFloat(State + "runDeadline", (float)EditorApplication.timeSinceStartup + EditorSeconds);
        InstallWatchdog();
        try {
            EditorSceneManager.OpenScene("Assets/HalliGalliMovement/Demo/BellRunCourse.unity");
            new GameObject("Practice validation").AddComponent<PracticeValidationRunner>().exitEditor = true;
            Application.runInBackground = true;
            EditorApplication.isPaused = false;
            if (!Application.isBatchMode) EditorApplication.ExecuteMenuItem("Window/General/Game");
            EditorApplication.isPlaying = true;
        } catch (Exception exception) { AbortEditor("Practice launcher opens BellRunCourse", exception.ToString()); }
    }

    static Report NewReport()
    {
        return new Report {
            runId = Guid.NewGuid().ToString("N"), utc = DateTime.UtcNow.ToString("O"), status = "starting",
            unityVersion = Application.unityVersion, batchMode = Application.isBatchMode,
            graphicsDevice = SystemInfo.graphicsDeviceType.ToString(),
            invocation = "Unity.exe [-batchmode] -disable-audio -projectPath <repo> -executeMethod PracticeValidationRunner.Run -logFile <log> (no -quit, no -nographics)",
            traversalMethod = "SelectStage start; one MovementCommand/SourceMotor.Simulate per existing fixed tick; W run-in then bank-right PD air strafe toward start.x (30.5/52.5/74.5), ground steering and real PracticeSurfArea completion. No injected velocity, teleport, progress, gravity/collision/profile/time-step changes during traversal. Explicit negative landing fixtures and original combat smoke use Teleport outside traversal."
        };
    }

    [InitializeOnLoadMethod]
    static void InstallWatchdog()
    {
        EditorApplication.update -= Watchdog;
        EditorApplication.update += Watchdog;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
    }
    static void Watchdog()
    {
        if (SessionState.GetBool(State + "active", false) &&
            EditorApplication.timeSinceStartup > SessionState.GetFloat(State + "deadline", float.MaxValue))
            AbortEditor("Practice validation finishes within its deadline", "Editor watchdog expired (including play-mode load or graphical frame capture).");
    }
    static void PlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(State + "active", false))
            AbortEditor("Practice validation reaches a terminal report", "Play mode ended before validation finished.");
    }
    static void AbortEditor(string name, string detail)
    {
        SessionState.SetBool(State + "active", false);
        try {
            var saved = File.Exists(ReportPath) ? JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath)) : NewReport();
            saved.checks.Add(new Check { name = name, detail = detail, passed = false });
            saved.status = "failed"; saved.completedUtc = DateTime.UtcNow.ToString("O");
            WriteReport(saved);
        } finally { EditorApplication.Exit(1); }
    }

    void Awake()
    {
        if (instance && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }
    void OnDestroy() { if (instance == this) instance = null; }

    IEnumerator Start()
    {
        if (instance != this) yield break;
        report = exitEditor ? JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath)) : NewReport();
        report.status = "running";
        report.fixedDeltaTime = Time.fixedDeltaTime;
        report.limitations.Add("Synthetic MovementCommand inputs validate consumer-visible physics and routing, not hardware keyboard/mouse delivery or human difficulty. Negative landing fixtures are explicitly marked injectedFixture and are never counted as successful traversal.");
        if (Application.isBatchMode)
            report.limitations.Add("Batch captures use synchronous Camera.Render/RenderTexture only. IMGUI practice HUD, success text and selector are NOT present or visually validated; run graphical mode for real Game-view captures. No WaitForEndOfFrame is used in batch.");
        report.limitations.Add("This no-audio smoke does not validate BGM DSP or subjective audio. Screenshots are evidence for visual review, not automated OCR/layout assertions.");
        Save();
        deadline = Time.realtimeSinceStartupAsDouble + RuntimeSeconds;
        var stack = new Stack<IEnumerator>();
        stack.Push(Validate());
        while (stack.Count > 0) {
            bool more = false;
            object next = null;
            try {
                if (Time.realtimeSinceStartupAsDouble > deadline) throw new TimeoutException("Practice runtime exceeded 360 seconds.");
                if (driver && driver.error != null) throw new InvalidOperationException(driver.error);
                more = stack.Peek().MoveNext();
                if (more) next = stack.Peek().Current;
            } catch (Exception exception) {
                CheckThat("Practice runtime completes without exception", false, exception.ToString());
                break;
            }
            if (!more) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
            if (next is IEnumerator child) { stack.Push(child); continue; }
            yield return next;
        }
        while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
        Finish();
    }

    IEnumerator Validate()
    {
        yield return Await(() => CourseNetwork.Instance && CourseNetwork.Instance.Game, 15, "BellRunCourse network bootstrap");
        yield return null;
        network = CourseNetwork.Instance;
        network.ShowStartScreen();
        yield return Shot("UI_Start");
        network.ShowCreateRoom();
        yield return Shot("UI_Start_Create");
        network.StartTraining();
        yield return Await(() => network.Game && network.Game.practiceRoom && network.Game.practiceRoom.Active, 30, "StartTraining opens the practice room");
        session = network.Game;
        local = session.Local;
        controller = local.GetComponent<SourcePlayerController>();
        hud = FindAnyObjectByType<CourseHUD>();
        Require(controller && hud, "Training must have the real player controller and HUD.");
        originalControllerEnabled = controller.enabled;
        originalHudInput = hud.ReadInput;
        controller.SetCursorCaptured(false);
        controller.enabled = false; // Prevent a second device-driven motor step.
        hud.ReadInput = false;
        local.Combat.readInput = false;
        driver = gameObject.AddComponent<PracticeSurfValidationDriver>();
        driver.player = local; driver.room = session.practiceRoom;
        combatProfile = local.Motor.profile;
        surf = session.practiceRoom.Surf;
        geometry = FindAnyObjectByType<PracticeSurfGeometry>();
        CheckThat("Training opens offline through the player-facing path", session.practiceRoom.Active && !CourseNetwork.Active && !network.OverlayVisible && !session.HasCardRules);
        targets = session.participants.Where(p => p && !p.localPlayer).ToArray();
        Require(targets.Length > 0, "Training needs a target to exercise combat, not a specific target count.");
        yield return CombatSmoke("Before surf");
        yield return Shot("UI_Practice");
        CheckThat("Training exposes usable surf lessons", surf && surf.Available && geometry && geometry.Stages != null);
        if (surf && surf.Available && geometry && geometry.Stages != null) {
            surfProfile = session.maps.catalog.maps.First(m => m && m.id == "surf").movementProfile;
            for (int stage = 0; stage < 3; stage++) {
                Require(stage < geometry.Stages.Length, "Requested surf lesson is missing: " + stage);
                yield return CheckProfileSwitch(stage);
                Select(stage, "Traversal selection");
                yield return Shot("UI_Practice_Surf" + (stage + 1) + "_Selector", stage);
                CaptureStage(stage);
                yield return Traverse(stage);
                yield return Shot("UI_Practice_Surf" + (stage + 1) + (surf.Completed ? "_Success" : "_Failed"), stage);
                yield return ResetWithR(stage);
                yield return FallRecovery(stage);
                yield return RejectWrongDestinations(stage);
                if (stage > 0) yield return RejectCatchBypass(stage);
                Select(-1, "Return to combat after lesson " + (stage + 1));
                CheckTargets("Lesson " + (stage + 1) + " leaves dummy practice intact");
            }
            yield return CombatSmoke("After surf");
            Select(2, "Surf selected before home");
        }
        network.ShowStartScreen();
        CheckThat("Home stops practice and restores combat selection", !session.practiceRoom.Active && network.OverlayVisible && (!surf || surf.SelectedStage == -1));
        CheckProfile("Home restores combat movement and capsule", combatProfile);
        if (surf) CheckThat("Home restores combat start anchors", Vector3.Distance(local.standAnchor.position, new Vector3(0, .025f, -6)) < .01f && Vector3.Distance(local.retryAnchor.position, local.standAnchor.position) < .01f);
        yield return Shot("UI_Practice_ReturnHome");
    }

    IEnumerator CombatSmoke(string prefix)
    {
        CheckTargets(prefix + ": targets stand without navigation or attack input");
        var positions = targets.Select(p => p.transform.position).ToArray();
        yield return new WaitForSecondsRealtime(1);
        CheckThat(prefix + ": targets remain still until hit", targets.Select((p, i) => Vector3.Distance(p.transform.position, positions[i]) < .03f).All(value => value));
        var target = targets.OrderBy(p => Mathf.Abs(p.transform.position.x)).ThenBy(p => p.transform.position.z).First();
        int hits = local.Combat.HitCount;
        local.Motor.Teleport(target.transform.position + Vector3.back * 1.2f);
        controller.ResetView(0);
        local.Combat.networkAim = Vector3.forward;
        bool kicked = local.Combat.TryKick();
        yield return new WaitForSecondsRealtime(.5f);
        CheckThat(prefix + ": kick moves a stationary target horizontally", kicked && local.Combat.HitCount == hits + 1 &&
            Vector3.ProjectOnPlane(target.transform.position - target.retryAnchor.position, Vector3.up).magnitude > .25f &&
            Mathf.Abs(target.Receiver.LastImpulse.y) < .0001f && !target.Motor.Staggered,
            "accepted=" + kicked + ", displacement=" + (target.transform.position - target.retryAnchor.position) + ", impulse=" + target.Receiver.LastImpulse);
        yield return WaitUntilBounded(() => Vector3.Distance(target.transform.position, target.retryAnchor.position) < .1f && !target.IsRespawning, 5);
        CheckThat(prefix + ": displaced target automatically returns", Vector3.Distance(target.transform.position, target.retryAnchor.position) < .1f && !target.IsRespawning);
        local.Motor.Teleport(local.standAnchor.position);
        local.Combat.ResetCombat(); local.Lasso.EndRace();
        int lassoHits = local.Lasso.Hits, pulls = local.Lasso.Pulls;
        Vector3 targetStart = target.transform.position;
        Vector3 inward = Vector3.ProjectOnPlane(local.transform.position - targetStart, Vector3.up).normalized;
        bool cast = local.Lasso.TryThrow((LassoTool.Anchor(target) - local.Motor.CurrentEyePosition).normalized);
        yield return new WaitForSecondsRealtime(.75f);
        CheckThat(prefix + ": one cast hits and automatically pulls once", cast && local.Lasso.Hits == lassoHits + 1 && local.Lasso.Pulls == pulls + 1 && !local.Lasso.ConnectionActive);
        CheckThat(prefix + ": practice pull moves the target inward without lift", Vector3.Dot(target.transform.position - targetStart, inward) > .05f && Mathf.Abs(local.Lasso.LastImpulse.y) < .0001f && target.transform.position.y < targetStart.y + .05f);
        float cooldown = local.Lasso.CooldownRemaining, resetAt = Time.time;
        int respawns = local.RespawnSequence;
        int[] otherRespawns = targets.Select(p => p.RespawnSequence).ToArray();
        hud.ProcessActions(false, false, true);
        yield return WaitUntilBounded(() => local.RespawnSequence != respawns && !local.IsRespawning, 2);
        CheckThat(prefix + ": R resets only local position and preserves cooldown", local.RespawnSequence == respawns + 1 &&
            Vector3.Distance(local.transform.position, local.retryAnchor.position) < .1f &&
            local.Lasso.CooldownRemaining >= Mathf.Max(0, cooldown - (Time.time - resetAt)) - .08f &&
            local.Lasso.CooldownRemaining <= cooldown && !local.Lasso.ConnectionActive && session.practiceRoom.Active &&
            targets.Select((p, i) => p.RespawnSequence == otherRespawns[i]).All(value => value));
        yield return WaitUntilBounded(() => targets.All(p => !p.IsRespawning && Vector3.Distance(p.transform.position, p.retryAnchor.position) < .1f), 5);
        CheckTargets(prefix + ": combat targets recover for further practice");
        controller.ResetView(0, 4); controller.UpdateView();
    }

    IEnumerator CheckProfileSwitch(int stage)
    {
        Select(-1, "Combat before crouch");
        driver.mode = PracticeSurfValidationDriver.Mode.Crouch;
        yield return FixedSeconds(.35f);
        CheckThat("Combat crouch is real before lesson " + (stage + 1), local.Motor.IsCrouched);
        Select(stage, "Crouched combat to surf " + (stage + 1));
        driver.mode = PracticeSurfValidationDriver.Mode.Crouch;
        yield return FixedSeconds(.35f);
        CheckThat("Surf crouch is real before returning from lesson " + (stage + 1), local.Motor.IsCrouched);
        Select(-1, "Crouched surf to combat " + (stage + 1));
        yield return FixedSeconds(.1f);
    }

    void Select(int stage, string label)
    {
        driver.mode = PracticeSurfValidationDriver.Mode.Idle;
        surf.SelectStage(stage);
        if (stage >= 0) driver.stage = geometry.Stages[stage];
        CheckThat(label + ": selection resets completion and checkpoints", surf.SelectedStage == stage && !surf.Completed && local.CheckpointIndex == -1 && !local.IsRespawning && !local.IsSeated);
        CheckProfile(label + ": movement profile/capsule/FOV restored", stage < 0 ? combatProfile : surfProfile);
        Vector3 expected = stage < 0 ? new Vector3(0, .025f, -6) : geometry.Stages[stage].start;
        CheckThat(label + ": correct spawn and retry anchors", Vector3.Distance(local.transform.position, expected) < .01f && Vector3.Distance(local.retryAnchor.position, expected) < .01f && Vector3.Distance(local.standAnchor.position, expected) < .01f && local.Motor.Velocity.sqrMagnitude < .0001f);
    }

    void CheckProfile(string label, SourceMovementProfile expected)
    {
        var capsule = local.GetComponent<CharacterController>();
        var snapshot = new ProfileSnapshot {
            label = label, stage = surf ? surf.SelectedStage : -1, actualProfile = local.Motor.profile.name,
            expectedProfile = expected.name, radius = capsule.radius, height = capsule.height,
            center = capsule.center, skinWidth = capsule.skinWidth, stepOffset = capsule.stepOffset,
            slopeLimit = capsule.slopeLimit, fieldOfView = controller.playerCamera.fieldOfView, capsuleEnabled = capsule.enabled
        };
        snapshot.matched = capsule.enabled && local.Motor.profile == expected &&
            Near(capsule.radius, expected.radius) && Near(capsule.height, expected.standingHeight) &&
            Vector3.Distance(capsule.center, Vector3.up * (expected.standingHeight * .5f)) < .001f &&
            Near(capsule.skinWidth, expected.skinWidth) && Near(capsule.stepOffset, expected.stepHeight) &&
            Near(capsule.slopeLimit, expected.surfEnabled ? 90 : expected.slopeLimit) && Near(snapshot.fieldOfView, expected.fieldOfView);
        report.profiles.Add(snapshot);
        CheckThat(label, snapshot.matched, JsonUtility.ToJson(snapshot));
    }

    IEnumerator Traverse(int stage)
    {
        var result = new StageResult { stage = stage };
        report.stages.Add(result);
        Trial success = null;
        for (int attempt = 1; attempt <= MaximumAttempts; attempt++) {
            Select(stage, "Lesson " + (stage + 1) + " attempt " + attempt);
            yield return RunTrial(stage, "traversal", attempt, PracticeSurfValidationDriver.Mode.Traverse, AttemptSeconds);
            result.attempts = attempt; result.retries = attempt - 1;
            if (lastTrial.completed) { success = lastTrial; break; }
        }
        result.completed = success != null;
        result.seconds = success != null ? success.seconds : lastTrial.seconds;
        string detail = "attempts=" + result.attempts + ", retries=" + result.retries + ", outcome=" + lastTrial.outcome + ", seconds=" + result.seconds + ", endpoint=" + lastTrial.endPosition;
        CheckThat("Lesson " + (stage + 1) + " completes from SelectStage using normal motor collision", success != null && success.respawnEnd == success.respawnStart && success.fallsEnd == success.fallsStart, detail);
        CheckThat("Lesson " + (stage + 1) + " records start ground, surf contact and real grounded finish", success != null && success.startedGrounded && success.surfTicks > 0 && success.finishSeconds >= 0 && success.trajectory.Last().grounded && success.trajectory.Last().inFinish, detail);
        if (stage > 0)
            CheckThat("Lesson " + (stage + 1) + " crosses air gap and catches receiving bank before finishing", success != null && success.crossedGap && success.caughtBank && success.firstGapSeconds > success.firstSurfSeconds && success.firstCatchSeconds > success.firstGapSeconds && success.finishSeconds >= success.firstCatchSeconds, detail);
    }

    IEnumerator ResetWithR(int stage)
    {
        driver.mode = PracticeSurfValidationDriver.Mode.Idle;
        int respawns = local.RespawnSequence;
        var targetRespawns = targets.Select(p => p.RespawnSequence).ToArray();
        hud.ProcessActions(false, false, true);
        yield return WaitUntilBounded(() => local.RespawnSequence != respawns && !local.IsRespawning, 2);
        yield return fixedStep;
        CheckThat("Lesson " + (stage + 1) + ": HUD R resets the selected stage, not the room", local.RespawnSequence == respawns + 1 && surf.SelectedStage == stage && !surf.Completed && session.practiceRoom.Active && !network.OverlayVisible &&
            Vector3.Distance(local.transform.position, geometry.Stages[stage].start) < .1f && local.Motor.HorizontalSpeed < .01f &&
            targets.Select((p, i) => p.RespawnSequence == targetRespawns[i]).All(value => value));
    }

    IEnumerator FallRecovery(int stage)
    {
        Select(stage, "Physical fall setup");
        int[] others = targets.Select(p => p.RespawnSequence).ToArray();
        yield return RunTrial(stage, "walk-off-fall-recovery", 1, PracticeSurfValidationDriver.Mode.WalkOff, 8);
        CheckThat("Lesson " + (stage + 1) + ": walking off the start deck falls and restores that stage", lastTrial.startedGrounded && lastTrial.airTicks > 0 && lastTrial.respawnEnd == lastTrial.respawnStart + 1 && lastTrial.fallsEnd == lastTrial.fallsStart + 1 &&
            surf.SelectedStage == stage && !surf.Completed && session.practiceRoom.Active && Vector3.Distance(local.transform.position, geometry.Stages[stage].start) < .1f && local.Motor.HorizontalSpeed < .01f &&
            targets.Select((p, i) => p.RespawnSequence == others[i]).All(value => value), "outcome=" + lastTrial.outcome + ", endpoint=" + lastTrial.endPosition);
    }

    IEnumerator RejectWrongDestinations(int stage)
    {
        Select(stage, "Unearned finish setup");
        PlaceOnFinish(stage);
        yield return RunTrial(stage, "unearned-own-finish", 1, PracticeSurfValidationDriver.Mode.Idle, .4f, injected: true);
        CheckThat("Lesson " + (stage + 1) + ": standing at its finish without surfing grants no success", !lastTrial.completed && local.Motor.Grounded && geometry.Stages[stage].finish.Contains(local.transform.position) && surf.SelectedStage == stage && !surf.Completed);
        Select(stage, "Wrong destination setup");
        int wrong = (stage + 1) % 3;
        PlaceOnFinish(wrong);
        yield return RunTrial(stage, "wrong-stage-finish-" + wrong, 1, PracticeSurfValidationDriver.Mode.Idle, .4f, injected: true);
        CheckThat("Lesson " + (stage + 1) + ": another stage's finish cannot grant success", !lastTrial.completed && !surf.Completed && surf.SelectedStage == stage && lastTrial.respawnEnd == lastTrial.respawnStart + 1 && Vector3.Distance(local.transform.position, geometry.Stages[stage].start) < .1f);
    }

    IEnumerator RejectCatchBypass(int stage)
    {
        Select(stage, "Receiving-bank bypass setup");
        yield return RunTrial(stage, "catch-bypass-real-approach", 1, PracticeSurfValidationDriver.Mode.Traverse, 8, firstBank: true);
        bool touched = lastTrial.surfTicks > 0;
        CheckThat("Lesson " + (stage + 1) + ": bypass fixture actually reaches the approach bank", touched && lastTrial.respawnStart == lastTrial.respawnEnd, "outcome=" + lastTrial.outcome);
        if (!touched) yield break;
        // Deliberately invalid transition: no receiving-bank collision. This is
        // separate from the complete, uninjected traversal recorded above.
        PlaceOnFinish(stage);
        yield return RunTrial(stage, "catch-bypass-finish", 1, PracticeSurfValidationDriver.Mode.Idle, .4f, injected: true);
        CheckThat("Lesson " + (stage + 1) + ": approach contact alone cannot replace receiving-bank catch", !lastTrial.completed && !surf.Completed && local.Motor.Grounded && geometry.Stages[stage].finish.Contains(local.transform.position));
    }

    void PlaceOnFinish(int stage)
    {
        Bounds finish = geometry.Stages[stage].finish;
        local.Motor.Teleport(new Vector3(geometry.Stages[stage].start.x, finish.min.y + .2f, finish.center.z));
    }

    IEnumerator RunTrial(int stage, string kind, int attempt, PracticeSurfValidationDriver.Mode mode, float seconds, bool firstBank = false, bool injected = false)
    {
        var trial = new Trial {
            kind = kind, stage = stage, attempt = attempt, startedUtc = DateTime.UtcNow.ToString("O"),
            startPosition = local.transform.position, respawnStart = local.RespawnSequence, fallsStart = local.Falls,
            injectedFixture = injected
        };
        report.trials.Add(trial);
        activeTrial = trial; stopAtFirstBank = firstBank; airborneSeconds = 0;
        trialStarted = Time.realtimeSinceStartupAsDouble;
        driver.stage = geometry.Stages[stage]; driver.mode = mode;
        Save();
        int maximumTicks = Mathf.CeilToInt(seconds / Time.fixedDeltaTime);
        while (!trial.finished && trial.ticks < maximumTicks && Time.realtimeSinceStartupAsDouble - trialStarted < seconds + 3)
            yield return fixedStep;
        if (!trial.finished) EndTrial(trial, "timeout");
        activeTrial = null;
        driver.mode = PracticeSurfValidationDriver.Mode.Idle;
        lastTrial = trial;
        Save();
    }

    void FixedUpdate()
    {
        if (activeTrial == null || activeTrial.finished || !local || !surf) return;
        var trial = activeTrial;
        var motor = local.Motor;
        var stage = geometry.Stages[trial.stage];
        trial.ticks++;
        trial.seconds = trial.ticks * Time.fixedDeltaTime;
        if (motor.Grounded) {
            trial.groundTicks++;
            if (local.transform.position.z < 0 && trial.firstSurfSeconds < 0) trial.startedGrounded = true;
        } else if (!motor.Surfing) trial.airTicks++;
        if (motor.Surfing) {
            trial.surfTicks++;
            if (trial.firstSurfSeconds < 0) trial.firstSurfSeconds = trial.seconds;
            if (trial.crossedGap && stage.catchZone.Contains(local.transform.position)) {
                trial.catchTicks++; trial.caughtBank = true;
                if (trial.firstCatchSeconds < 0) trial.firstCatchSeconds = trial.seconds;
            }
            airborneSeconds = 0;
        } else if (trial.firstSurfSeconds >= 0 && !motor.Grounded) {
            airborneSeconds += Time.fixedDeltaTime;
            if (airborneSeconds >= .1f) {
                trial.crossedGap = true;
                if (trial.firstGapSeconds < 0) trial.firstGapSeconds = trial.seconds;
            }
        } else airborneSeconds = 0;
        bool inFinish = stage.finish.Contains(local.transform.position);
        if (motor.Grounded && inFinish && trial.finishSeconds < 0) trial.finishSeconds = trial.seconds;
        var sample = new Sample {
            tick = trial.ticks, seconds = trial.seconds, position = local.transform.position, velocity = motor.Velocity,
            surfNormal = motor.SurfNormal, move = driver.lastCommand.move, grounded = motor.Grounded, surfing = motor.Surfing,
            inCatch = stage.catchZone.Contains(local.transform.position), inFinish = inFinish,
            completed = surf.Completed, respawnSequence = local.RespawnSequence,
            state = local.RespawnSequence != trial.respawnStart ? "respawn" : surf.Completed ? "completed" :
                motor.Grounded ? (inFinish ? "finish-ground" : "ground") : motor.Surfing ?
                (trial.caughtBank ? "receiving-bank-catch" : "surf-bank") : trial.firstSurfSeconds >= 0 ? "post-bank-air" : "run-in-air"
        };
        trial.trajectory.Add(sample);
        if (trial.transitions.Count == 0 || trial.transitions[trial.transitions.Count - 1].state != sample.state)
            trial.transitions.Add(sample);
        if (surf.Completed) { trial.completed = true; EndTrial(trial, "real-completion"); }
        else if (local.RespawnSequence != trial.respawnStart) EndTrial(trial, "respawn");
        else if (stopAtFirstBank && motor.Surfing) EndTrial(trial, "approach-bank-contact");
    }

    void EndTrial(Trial trial, string outcome)
    {
        trial.finished = true; trial.outcome = outcome;
        trial.completedUtc = DateTime.UtcNow.ToString("O");
        trial.wallSeconds = (float)(Time.realtimeSinceStartupAsDouble - trialStarted);
        trial.endPosition = local.transform.position;
        trial.respawnEnd = local.RespawnSequence; trial.fallsEnd = local.Falls;
        driver.mode = PracticeSurfValidationDriver.Mode.Idle;
    }

    void LateUpdate()
    {
        if (controller && !controller.enabled) controller.UpdateView();
    }

    void CheckTargets(string label)
    {
        CheckThat(label, targets.All(p => !p.IsSeated && !p.IsRespawning && !p.Combat.readInput &&
            (!p.GetComponent<CourseRival>() || !p.GetComponent<CourseRival>().enabled) &&
            Vector3.Distance(p.transform.position, p.retryAnchor.position) < .1f) && !session.HasCardRules,
            "observed targets=" + targets.Length + "; count is not an assertion");
    }

    IEnumerator Shot(string name, int stage = -1)
    {
        bool previousInput = hud && hud.ReadInput;
        if (hud) hud.ReadInput = true; // Show the actual enabled selector, not a replica.
        if (controller) controller.SetCursorCaptured(false);
        yield return null;
        yield return null;
        if (!Application.isBatchMode) {
            if (exitEditor) SessionState.SetFloat(State + "deadline", Mathf.Min(SessionState.GetFloat(State + "runDeadline", float.MaxValue), (float)EditorApplication.timeSinceStartup + 15));
            yield return new WaitForEndOfFrame();
        }
        try {
            Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "Captures require a graphics device; do not use -nographics.");
            string path = CapturePath(name);
            if (Application.isBatchMode) {
                Camera camera = controller ? controller.playerCamera : network.Game.Local.GetComponent<SourcePlayerController>().playerCamera;
                RenderCamera(camera, path);
                report.captures.Add(new Capture { name = name, path = path, stage = stage, utc = DateTime.UtcNow.ToString("O"), method = "Camera.Render", limitation = "World only; IMGUI HUD/selector absent in batch. See stage overview capture for geometry." });
            } else {
                Texture2D image = ScreenCapture.CaptureScreenshotAsTexture();
                Require(image, "Game-view capture returned no image.");
                try { File.WriteAllBytes(path, image.EncodeToPNG()); }
                finally { Destroy(image); }
                report.captures.Add(new Capture { name = name, path = path, stage = stage, utc = DateTime.UtcNow.ToString("O"), method = "ScreenCapture/GameView", limitation = "Actual production IMGUI; requires human visual review." });
            }
            CheckThat(name + " capture is written", File.Exists(path) && new FileInfo(path).Length > 0, path);
        } catch (Exception exception) { CheckThat(name + " capture is written", false, exception.ToString()); }
        finally {
            if (hud) hud.ReadInput = previousInput;
            if (exitEditor) SessionState.SetFloat(State + "deadline", SessionState.GetFloat(State + "runDeadline", float.MaxValue));
        }
    }

    void CaptureStage(int stage)
    {
        var cameraObject = new GameObject("Practice geometry validation camera");
        try {
            Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "Stage rendering requires a graphics device.");
            var camera = cameraObject.AddComponent<Camera>();
            camera.CopyFrom(controller.playerCamera);
            camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 28;
            camera.nearClipPlane = .1f; camera.farClipPlane = 250; camera.cullingMask = ~((1 << CharacterMotionDriver.BodyLayer) | (1 << CharacterMotionDriver.HandsLayer));
            var lane = geometry.Stages[stage];
            Vector3 target = new Vector3(lane.start.x, (lane.start.y + lane.finish.min.y) * .5f, (lane.start.z + lane.finish.max.z) * .5f);
            camera.transform.position = target + new Vector3(-17, 22, -30);
            camera.transform.LookAt(target);
            string path = CapturePath("Practice_Surf" + (stage + 1) + "_Geometry");
            RenderCamera(camera, path);
            report.captures.Add(new Capture { name = "Surf geometry " + (stage + 1), path = path, stage = stage, utc = DateTime.UtcNow.ToString("O"), method = "Camera.Render/stage-overview", limitation = "Real runtime stage geometry; no HUD or selector." });
            CheckThat("Lesson " + (stage + 1) + " geometry capture is written", File.Exists(path) && new FileInfo(path).Length > 0, path);
        } catch (Exception exception) { CheckThat("Lesson " + (stage + 1) + " geometry capture is written", false, exception.ToString()); }
        finally { Destroy(cameraObject); }
    }

    string CapturePath(string name)
    {
        string directory = "Previews/PracticeValidation/" + report.runId;
        Directory.CreateDirectory(directory);
        return directory + "/" + name + ".png";
    }

    static void RenderCamera(Camera camera, string path)
    {
        Require(camera, "Runtime player camera is missing.");
        var target = new RenderTexture(1280, 800, 24);
        RenderTexture previous = RenderTexture.active, cameraTarget = camera.targetTexture;
        Texture2D image = null;
        try {
            target.Create(); camera.targetTexture = target; camera.Render();
            RenderTexture.active = target;
            image = new Texture2D(1280, 800, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        } finally {
            camera.targetTexture = cameraTarget; RenderTexture.active = previous;
            if (image) Destroy(image);
            target.Release(); Destroy(target);
        }
    }

    IEnumerator FixedSeconds(float seconds)
    {
        int ticks = Mathf.CeilToInt(seconds / Time.fixedDeltaTime);
        for (int tick = 0; tick < ticks; tick++) yield return fixedStep;
    }
    IEnumerator WaitUntilBounded(Func<bool> condition, float seconds)
    {
        double until = Time.realtimeSinceStartupAsDouble + seconds;
        while (!condition() && Time.realtimeSinceStartupAsDouble < until) yield return null;
    }
    IEnumerator Await(Func<bool> condition, float seconds, string label)
    {
        yield return WaitUntilBounded(condition, seconds);
        Require(condition(), label + " timed out after " + seconds + " seconds.");
    }
    static bool Near(float a, float b) => Mathf.Abs(a - b) < .001f;
    static void Require(bool value, string detail) { if (!value) throw new InvalidOperationException(detail); }
    void CheckThat(string name, bool passed, string detail = "")
    {
        report.checks.Add(new Check { name = name, passed = passed, detail = detail });
        if (!passed) Debug.LogError("PRACTICE_VALIDATION_CHECK_FAILED " + name + ": " + detail);
        Save();
    }
    void Save() { WriteReport(report); }
    static void WriteReport(Report value)
    {
        value.passed = value.checks.Count(check => check.passed);
        value.failed = value.checks.Count - value.passed;
        Directory.CreateDirectory("TestResults");
        File.WriteAllText(ReportPath, JsonUtility.ToJson(value, true));
    }
    void Finish()
    {
        if (finished) return;
        finished = true;
        if (activeTrial != null && !activeTrial.finished) EndTrial(activeTrial, "runner-aborted");
        if (driver) driver.enabled = false;
        if (controller) controller.enabled = originalControllerEnabled;
        if (hud) hud.ReadInput = originalHudInput;
        report.completedUtc = DateTime.UtcNow.ToString("O");
        report.status = report.checks.Any(check => !check.passed) ? "failed" : "passed";
        Save();
        SessionState.SetBool(State + "active", false);
        Debug.Log("PRACTICE_VALIDATION " + report.status + " " + report.passed + "/" + report.checks.Count + " report=" + ReportPath);
        if (exitEditor) EditorApplication.Exit(report.failed == 0 ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }
}
#endif
