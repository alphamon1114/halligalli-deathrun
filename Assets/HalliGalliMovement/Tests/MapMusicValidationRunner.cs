#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using HalliGalli.Movement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class MapMusicValidationRunner : MonoBehaviour
{
    const string ReportPath = "TestResults/map-music-playback.json";
    const string Preference = "HalliGalli.MusicVolume";
    const string State = "HalliGalli.MapMusicValidation.";
    const float DefaultVolume = .30f;
    const float PersistedVolume = .61f;
    static readonly string[] MapIds = { "first-steps", "summit", "surf", "factory", "pyramid", "item-race", "clockwork" };
    static MapMusicValidationRunner active;
    void Awake()
    {
        if (active && active != this) { Destroy(gameObject); return; }
        active = this;
        DontDestroyOnLoad(gameObject);
    }
    void OnDestroy()
    {
        if (active != this) return;
        active = null;
        string phase = SessionState.GetString(State + "phase", "");
        if (phase == "initial" || phase == "persistence")
            Abort("Validation runner survives until completion", "Runner was destroyed during " + phase + ".", true);
    }

    [Serializable] sealed class Check
    {
        public string name, detail;
        public bool passed, limited;
    }
    [Serializable] sealed class Measurement
    {
        public string map, kind, method, file;
        public int sampleRate, channels, samples;
        public float seconds, rmsDbfs, peakDbfs, loudest50msDbfs, longestSilenceSeconds;
        public double dspStart, dspEnd;
        public bool audible;
    }
    [Serializable] sealed class Report
    {
        public string utc, completedUtc, unityVersion, status, screenshot, runId;
        public int passed, failed, limited, outputSampleRate, processId;
        public bool playbackCompleted, persistenceCompleted, preferencesRestored;
        public string speakerMode;
        public float defaultMusicVolume;
        public List<Check> checks = new List<Check>();
        public List<Measurement> measurements = new List<Measurement>();
        public List<string> limits = new List<string>();
    }

    [SerializeField] bool persistencePass;
    Report report = new Report();
    CourseSession session;
    CourseHUD hud;
    AudioListener listener;
    MapMusicValidationAudioProbe probe;
    readonly float[] output = new float[2048];
    readonly float[] sourceOutput = new float[1024];
    readonly List<float> fallbackSamples = new List<float>(2048 * 240);
    AudioSource[] ownedSources;
    MapMusicPlayer originalPlayer;
    bool requestingReload;
    bool? audibleOutput;
    uint previousWidth, previousHeight;
    double deadline;
    Measurement lastMeasurement;

    // Invoke in graphical Unity without -quit/-batchmode/-nographics/-disable-audio.
    // The editor owns completion across both Play sessions; scene backups contain no runner.
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetString(State + "phase", "").Length != 0)
            throw new InvalidOperationException("Run MapMusicValidationRunner.Run from an idle edit mode.");
        SessionState.SetBool(State + "hadVolume", PlayerPrefs.HasKey(Preference));
        SessionState.SetFloat(State + "volume", PlayerPrefs.GetFloat(Preference, DefaultVolume));
        SessionState.SetBool(State + "hadNickname", PlayerPrefs.HasKey("HalliGalli.Nickname"));
        SessionState.SetString(State + "nickname", PlayerPrefs.GetString("HalliGalli.Nickname", "Player"));
        SessionState.SetBool(State + "editorMute", EditorUtility.audioMasterMute);
        SessionState.SetBool(State + "background", Application.runInBackground);
        SessionState.SetBool(State + "paused", EditorApplication.isPaused);
        SessionState.SetBool(State + "playOptionsEnabled", EditorSettings.enterPlayModeOptionsEnabled);
        SessionState.SetInt(State + "playOptions", (int)EditorSettings.enterPlayModeOptions);
        SessionState.SetBool(State + "preferencesSaved", true);
        SessionState.SetString(State + "phase", "initializing");
        SessionState.SetFloat(State + "deadline", (float)EditorApplication.timeSinceStartup + 300);
        string runId = Environment.GetEnvironmentVariable("HALLIGALLI_MAP_MUSIC_RUN_ID");
        SessionState.SetString(State + "runId", string.IsNullOrEmpty(runId) ? Guid.NewGuid().ToString("N") : runId);
        SessionState.SetString(State + "utc", DateTime.UtcNow.ToString("O"));
        InstallContinuation();
        try {
            Save(NewReport());
            // Persistence means a real runtime/domain restart, regardless of remembered fast-play options.
            EditorSettings.enterPlayModeOptionsEnabled = false;
            // The external Windows wrapper mutes only this process's output session.
            // Do not enter Play mode until it confirms the mute; DSP remains enabled.
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HALLIGALLI_MAP_MUSIC_AUDIO_READY"))) {
                SessionState.SetString(State + "phase", "awaiting-os-audio-mute");
                EditorApplication.update += StartAfterAudioSessionMute;
            } else Launch(false);
        } catch (Exception e) { Abort("Validation launches without an exception", e.ToString(), true); }
    }

    static Report NewReport()
    {
        var saved = new Report {
            utc = SessionState.GetString(State + "utc", DateTime.UtcNow.ToString("O")),
            runId = SessionState.GetString(State + "runId", ""),
            processId = System.Diagnostics.Process.GetCurrentProcess().Id,
            unityVersion = Application.unityVersion, defaultMusicVolume = DefaultVolume, status = "running"
        };
        saved.limits.Add("Mixer samples measure engine output, not physical speakers. Bell balance is measured at 2m using the production received-bell event; traversal, subjective masking and hardware listening are not claimed.");
        return saved;
    }

    static Report LoadReport()
    {
        var saved = JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath));
        if (saved == null || saved.runId != SessionState.GetString(State + "runId", "") ||
            saved.processId != System.Diagnostics.Process.GetCurrentProcess().Id)
            throw new InvalidOperationException("Playback report does not belong to this validation process/run.");
        return saved;
    }

    static void StartAfterAudioSessionMute()
    {
        string path = Environment.GetEnvironmentVariable("HALLIGALLI_MAP_MUSIC_AUDIO_READY");
        if (!File.Exists(path)) return;
        string owner;
        try { owner = File.ReadAllText(path).Trim(); }
        catch (IOException) { return; }
        if (owner != System.Diagnostics.Process.GetCurrentProcess().Id.ToString()) return;
        EditorApplication.update -= StartAfterAudioSessionMute;
        try { Launch(false); }
        catch (Exception e) { Abort("Validation launches after OS mute", e.ToString(), true); }
    }

    [InitializeOnLoadMethod]
    static void InstallContinuation()
    {
        EditorApplication.playModeStateChanged -= ContinueAfterReload;
        EditorApplication.playModeStateChanged += ContinueAfterReload;
        EditorApplication.update -= CheckEditorDeadline;
        EditorApplication.update += CheckEditorDeadline;
        EditorApplication.wantsToQuit -= BeforeEditorQuit;
        EditorApplication.wantsToQuit += BeforeEditorQuit;
        EditorApplication.quitting -= EditorQuitting;
        EditorApplication.quitting += EditorQuitting;
        EditorApplication.update -= StartAfterAudioSessionMute;
        if (SessionState.GetString(State + "phase", "") == "awaiting-os-audio-mute")
            EditorApplication.update += StartAfterAudioSessionMute;
    }

    static void ContinueAfterReload(PlayModeStateChange state)
    {
        string phase = SessionState.GetString(State + "phase", "");
        if (phase.Length == 0 || phase == "finishing") return;
        Debug.Log("MAP_MUSIC_VALIDATION_LIFECYCLE " + state + "; phase=" + phase);
        if (state == PlayModeStateChange.EnteredPlayMode &&
            (phase == "entering-initial" || phase == "entering-persistence")) {
            bool persistence = phase == "entering-persistence";
            SessionState.SetString(State + "phase", persistence ? "persistence" : "initial");
            try {
                new GameObject("Map music playback validation").AddComponent<MapMusicValidationRunner>().persistencePass = persistence;
            } catch (Exception e) { Abort("Runtime validation runner starts", e.ToString(), true); }
        } else if (state == PlayModeStateChange.EnteredEditMode) {
            if (phase == "reload") {
                SessionState.SetString(State + "phase", "continuing-persistence");
            } else Abort("Both playback sessions reach completion", "Unexpected return to edit mode during " + phase + ".", true);
        }
    }

    static void LaunchPersistence()
    {
        if (SessionState.GetString(State + "phase", "") != "continuing-persistence") return;
        try { Launch(true); }
        catch (Exception e) { Abort("Persistence session launches", e.ToString(), true); }
    }

    static bool BeforeEditorQuit() { EditorQuitting(); return true; }
    static void EditorQuitting()
    {
        string phase = SessionState.GetString(State + "phase", "");
        if (phase.Length != 0 && phase != "finishing")
            Abort("Editor remains open until validation completes", "Editor shutdown requested during " + phase + ".", false);
    }

    static void CheckEditorDeadline()
    {
        string phase = SessionState.GetString(State + "phase", "");
        if (phase.Length == 0 || phase == "finishing") return;
        // Editor delayCall can be consumed by a domain transition. Resume from
        // the persisted phase on a settled editor update instead.
        if (phase == "continuing-persistence" && !EditorApplication.isPlayingOrWillChangePlaymode &&
            !EditorApplication.isCompiling && !EditorApplication.isUpdating) {
            LaunchPersistence();
            return;
        }
        // Let the wrapper request a failed, preference-restoring shutdown before resorting to Kill.
        string abortPath = Environment.GetEnvironmentVariable("HALLIGALLI_MAP_MUSIC_ABORT");
        if (!string.IsNullOrEmpty(abortPath) && File.Exists(abortPath)) {
            Abort("Silent wrapper remains available through completion", "The owning wrapper requested cancellation during " + phase + ".", true);
            return;
        }
        if (EditorApplication.timeSinceStartup >= SessionState.GetFloat(State + "deadline", float.MaxValue))
            Abort("Graphical validation finishes before the editor deadline",
                "Editor watchdog expired during " + phase + ", including any suspended frame capture or play-mode reload.", true);
    }

    static void Abort(string name, string detail, bool exit)
    {
        if (SessionState.GetString(State + "phase", "").Length == 0 ||
            SessionState.GetString(State + "phase", "") == "finishing") return;
        Report saved;
        try { saved = LoadReport(); }
        catch (Exception e) { saved = NewReport(); detail += "\nReport recovery: " + e; }
        saved.checks.Add(new Check { name = name, detail = detail });
        Debug.LogError("MAP_MUSIC_VALIDATION_FAILED " + name + ": " + detail);
        Complete(saved, exit);
    }

    static void Launch(bool persistence)
    {
        SessionState.SetString(State + "phase", persistence ? "entering-persistence" : "entering-initial");
        EditorSceneManager.OpenScene("Assets/HalliGalliMovement/Demo/BellRunCourse.unity");
        EditorUtility.audioMasterMute = false;
        Application.runInBackground = true;
        EditorApplication.isPaused = false;
        EditorApplication.ExecuteMenuItem("Window/General/Game");
        EditorApplication.isPlaying = true;
    }

    IEnumerator Start()
    {
        if (active != this) yield break;
        deadline = Time.realtimeSinceStartupAsDouble + 240;
        try {
            report = LoadReport();
            if (persistencePass) Require(report.playbackCompleted && report.status == "restarting-play-mode-for-persistence",
                "Persistence session must continue a completed playback pass.");
            else {
                var config = AudioSettings.GetConfiguration();
                report.outputSampleRate = config.sampleRate;
                report.speakerMode = config.speakerMode.ToString();
            }
            report.status = persistencePass ? "checking-persistence" : "running";
            Save();
        } catch (Exception e) {
            Abort("Validation report continues across Play sessions", e.ToString(), true);
            yield break;
        }
        Time.timeScale = 1;
        // Drive nested iterators here too: a failed child must save evidence and exit, not
        // leave a graphical executeMethod process waiting indefinitely in Play mode.
        var stack = new Stack<IEnumerator>();
        stack.Push(persistencePass ? ValidatePersistence() : Validate());
        while (stack.Count > 0) {
            object next = null;
            bool moved = false;
            try {
                if (Time.realtimeSinceStartupAsDouble > deadline) throw new TimeoutException("Validation exceeded 240 seconds.");
                moved = stack.Peek().MoveNext();
                if (moved) next = stack.Peek().Current;
                else (stack.Pop() as IDisposable)?.Dispose();
            } catch (Exception e) {
                CheckThat("Validation completes without an exception", false, e.ToString());
                break;
            }
            if (!moved) continue;
            if (next is IEnumerator child) { stack.Push(child); continue; }
            yield return next;
        }
        while (stack.Count > 0) {
            try { (stack.Pop() as IDisposable)?.Dispose(); }
            catch (Exception e) { CheckThat("Validation iterator cleanup completes", false, e.ToString()); }
        }
        if (requestingReload) yield break;
        Finish();
    }

    IEnumerator Validate()
    {
        yield return Await(() => CourseNetwork.Instance && MapMusicPlayer.Instance && FindAnyObjectByType<CourseSession>(), 10, "Runtime bootstrap");
        yield return null; // Let the production network Start finish opening the home screen.
        CourseNetwork.Instance.StartPractice();
        yield return null;
        Bind();
        MapMusicPlayer.Volume = DefaultVolume;
        double initialDsp = AudioSettings.dspTime;
        yield return new WaitForSecondsRealtime(.25f);
        if (AudioSettings.outputSampleRate <= 0 || AudioSettings.dspTime <= initialDsp)
            Limit("Audio device and DSP availability", "No advancing audio DSP was observed. Playback timing/output checks require a working Unity audio device; listener pause=" + AudioListener.pause + ".");
        yield return Await(Ready, 8, "Initial music playback");
        ownedSources = Sources();
        originalPlayer = MapMusicPlayer.Instance;
        CheckThat("One persistent player owns exactly two reusable music sources", ownedSources.Length == 2 && PlayerCount() == 1);
        CheckThat("All seven playable map definitions exist", MapIds.All(id => session.maps.catalog.maps.Any(m => m && m.id == id && m.playable)));
        yield return CaptureAudio("first-steps", "initial-output", .7f);
        RecordOutput("Initial music reaches the actual mixer output", lastMeasurement);

        // The initial scene is already first-steps. Route away and back so every named
        // map, including first-steps, is actually loaded through CourseMapController.
        yield return SwitchMap("summit", false);
        foreach (string id in MapIds) {
            yield return SwitchMap(id, true);
            yield return CheckPlayback(id);
            yield return CheckLoops(id);
            yield return CheckContinuity(id + ": respawn does not restart music", () => {
                Require(session.Local.Stand(), "Local participant could not stand.");
                Require(session.Local.RequestRespawn(), "Respawn request was rejected.");
                session.Local.Respawn();
            });
            yield return CheckContinuity(id + ": round reset does not restart music", session.ResetPractice);
            yield return CheckContinuity(id + ": reselecting the same map does not restart music", () =>
                Require(session.maps.TryStart(session.maps.current, 2), "Same-map selection was rejected."));
            yield return MeasureBalance(id);
        }
        yield return CheckVolume();
        // Several further real scene loads must reuse the same two source instances.
        foreach (string id in new[] { "surf", "first-steps", "summit", "first-steps" }) yield return SwitchMap(id, true);
        yield return MeasureCombatBalance();
        yield return CheckHomeAndRoom();
        MapMusicPlayer.Volume = PersistedVolume;
        yield return new WaitForSecondsRealtime(1.1f);
        CheckThat("Changed volume is stored for the next play session", Mathf.Abs(PlayerPrefs.GetFloat(Preference, -1) - PersistedVolume) < .001f);
        report.playbackCompleted = true;
        report.status = "restarting-play-mode-for-persistence";
        Save();
        RestoreResolution();
        requestingReload = true;
        SessionState.SetString(State + "phase", "reload");
        EditorApplication.isPlaying = false;
    }

    IEnumerator ValidatePersistence()
    {
        yield return Await(() => MapMusicPlayer.Instance && CourseNetwork.Instance, 10, "Fresh runtime bootstrap");
        CheckThat("Volume survives a complete exit and re-entry into Play mode", Mathf.Abs(MapMusicPlayer.Volume - PersistedVolume) < .001f,
            "Expected " + PersistedVolume + "; fresh runtime reported " + MapMusicPlayer.Volume + ".");
        yield return null;
        CourseNetwork.Instance.StartPractice();
        yield return null;
        Bind();
        yield return Await(Ready, 8, "Playback after preference reload");
        var source = MapMusicPlayer.Instance.CurrentSource;
        int sample = source.timeSamples;
        yield return new WaitForSecondsRealtime(.3f);
        CheckThat("Reloaded volume controls the genuinely playing source", source.isPlaying && source.timeSamples > sample && Mathf.Abs(source.volume - PersistedVolume) < .005f);
        report.persistenceCompleted = true;
    }

    void Bind()
    {
        session = FindAnyObjectByType<CourseSession>();
        Require(session && session.maps && session.maps.current, "Loaded scene has no mapped CourseSession.");
        session.cardGame.botEnabled = false;
        session.cardGame.ReadInput = false;
        session.cardGame.enabled = false;
        if (session.cardGame.tableView) session.cardGame.tableView.enabled = false;
        hud = FindAnyObjectByType<CourseHUD>();
        if (hud) hud.ReadInput = false;
        foreach (var participant in session.roster.allParticipants) {
            participant.Initialize();
            var bot = participant.GetComponent<CourseRival>();
            if (bot) bot.enabled = false;
            participant.Combat.readInput = false;
            participant.Combat.simulateAutomatically = false;
            var control = participant.GetComponent<SourcePlayerController>();
            if (control) { control.SetCursorCaptured(false); control.enabled = false; }
        }
        listener = FindObjectsByType<AudioListener>(FindObjectsSortMode.None).FirstOrDefault(l => l.isActiveAndEnabled);
        if (listener) {
            probe = listener.GetComponent<MapMusicValidationAudioProbe>();
            if (!probe) probe = listener.gameObject.AddComponent<MapMusicValidationAudioProbe>();
        } else probe = null;
    }

    static AudioSource[] Sources() => MapMusicPlayer.Instance ? MapMusicPlayer.Instance.GetComponentsInChildren<AudioSource>(true) : Array.Empty<AudioSource>();
    static int PlayerCount() => FindObjectsByType<MapMusicPlayer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
    bool Ready()
    {
        var music = MapMusicPlayer.Instance;
        return music && session && music.CurrentMapId == session.maps.current.id && music.CurrentSource && music.CurrentSource.isPlaying &&
            music.CurrentSource.clip && Mathf.Abs(music.CurrentSource.volume - MapMusicPlayer.Volume) < .005f && Sources().Count(s => s.isPlaying) == 1;
    }

    IEnumerator SwitchMap(string id, bool inspectTransition)
    {
        var map = session.maps.catalog.maps.Single(m => m && m.id == id);
        var old = MapMusicPlayer.Instance.CurrentSource;
        string previous = MapMusicPlayer.Instance.CurrentMapId;
        bool accepted = session.maps.TryStart(map, 2);
        CheckThat(id + ": player-facing map selection accepts the map", accepted);
        if (!accepted) yield break;
        float until = Time.realtimeSinceStartup + 25;
        bool overlap = false, realOverlap = false;
        float maxOldSignal = 0, maxNewSignal = 0, oldAtOverlap = 0, newAtOverlap = 0;
        while (Time.realtimeSinceStartup < until) {
            var current = MapMusicPlayer.Instance.CurrentSource;
            if (old && current && current != old && old.isPlaying && current.isPlaying && old.volume > .005f && current.volume > .005f &&
                old.volume < MapMusicPlayer.Volume - .005f && current.volume < MapMusicPlayer.Volume - .005f) {
                overlap = true;
                oldAtOverlap = old.volume; newAtOverlap = current.volume;
                float a = SourceRms(old), b = SourceRms(current);
                maxOldSignal = Mathf.Max(maxOldSignal, a); maxNewSignal = Mathf.Max(maxNewSignal, b);
                realOverlap |= a > .000001f && b > .000001f;
            }
            if (SceneManager.GetActiveScene().path == map.scenePath && current && current.clip && MapMusicPlayer.Instance.CurrentMapId == id &&
                current.isPlaying && Mathf.Abs(current.volume - MapMusicPlayer.Volume) < .005f && (!old || old == current || !old.isPlaying)) break;
            yield return null;
        }
        CheckThat(id + ": actual map scene finishes loading", SceneManager.GetActiveScene().path == map.scenePath, SceneManager.GetActiveScene().path);
        yield return null;
        yield return null;
        Bind();
        CheckThat(id + ": mapped track replaces the previous track", Ready(), "previous=" + previous + "; current=" + MapMusicPlayer.Instance.CurrentMapId);
        if (inspectTransition && previous != id) {
            CheckThat(previous + " -> " + id + ": intermediate fade contains both playing sources", overlap,
                "old gain=" + oldAtOverlap + "; new gain=" + newAtOverlap);
            if (audibleOutput == true) CheckThat(previous + " -> " + id + ": both fading sources emit real audio samples", realOverlap,
                "old RMS=" + maxOldSignal + "; new RMS=" + maxNewSignal);
            else Limit(previous + " -> " + id + ": audible crossfade", "Playing/gain overlap was observed separately; audio output was not established for this transition.");
            CheckThat(previous + " -> " + id + ": outgoing source stops after the fade", old && old != MapMusicPlayer.Instance.CurrentSource && !old.isPlaying);
        }
        CheckThat(id + ": switches never accumulate players or audio sources", PlayerCount() == 1 && MapMusicPlayer.Instance == originalPlayer &&
            Sources().Length == 2 && Sources().All(s => ownedSources.Contains(s)) && Sources().Count(s => s.isPlaying) == 1);
    }

    IEnumerator CheckPlayback(string id)
    {
        var source = MapMusicPlayer.Instance.CurrentSource;
        var expected = Resources.Load<AudioClip>("MapMusic/" + id);
        CheckThat(id + ": resource mapping is the actual current clip", source && expected && source.clip == expected,
            expected ? expected.name + "; " + expected.channels + "ch; " + expected.frequency + "Hz; " + expected.length.ToString("F3") + "s" : "Missing Resources clip.");
        Require(source && source.clip, "No playable source for " + id);
        CheckThat(id + ": music is looping stereo 2D playback", source.loop && source.spatialBlend == 0 && source.clip.channels == 2);
        source.timeSamples = Mathf.Min(source.clip.samples / 4, source.clip.frequency * 5);
        int before = source.timeSamples;
        double start = AudioSettings.dspTime;
        yield return new WaitForSecondsRealtime(.35f);
        CheckThat(id + ": audio sample position genuinely advances", source.isPlaying && source.timeSamples > before,
            "samples=" + before + " -> " + source.timeSamples + "; DSP elapsed=" + (AudioSettings.dspTime - start).ToString("F4"));
        yield return CaptureAudio(id, "playing", .7f);
        RecordOutput(id + ": actual mixer output contains music", lastMeasurement);
    }

    IEnumerator CheckLoops(string id)
    {
        var source = MapMusicPlayer.Instance.CurrentSource;
        for (int cycle = 1; cycle <= 3; cycle++) {
            source.timeSamples = source.clip.samples - Mathf.CeilToInt(source.clip.frequency * .45f);
            yield return new WaitForSecondsRealtime(.08f);
            if (cycle == 1 && probe) probe.Begin(AudioSettings.outputSampleRate, 1.4f);
            int previous = source.timeSamples;
            bool wrapped = false, stopped = false;
            float until = Time.realtimeSinceStartup + 1.1f;
            while (Time.realtimeSinceStartup < until) {
                Require(source == MapMusicPlayer.Instance.CurrentSource && source.clip,
                    id + ": loop transport changed to " + MapMusicPlayer.Instance.CurrentMapId +
                    "; scene=" + SceneManager.GetActiveScene().path + "; overlay=" + CourseNetwork.Instance.OverlayVisible);
                stopped |= !source.isPlaying;
                int sample = source.timeSamples;
                if (previous > source.clip.samples / 2 && sample < source.clip.samples / 2) wrapped = true;
                previous = sample;
                yield return null;
            }
            CheckThat(id + ": natural loop " + cycle + " wraps without stopping", wrapped && !stopped && source == MapMusicPlayer.Instance.CurrentSource,
                "Seeked near end once; observed natural wrap=" + wrapped + "; observed stopped frame=" + stopped + ".");
            if (cycle == 1 && probe) {
                var data = probe.End(out int count, out int channels);
                if (count > 0) AddMeasurement(id, "natural-loop", "continuous AudioListener OnAudioFilterRead", data, count, channels, AudioSettings.outputSampleRate, 0, 0, true);
            }
        }
    }

    IEnumerator CheckContinuity(string name, Action action)
    {
        var source = MapMusicPlayer.Instance.CurrentSource;
        source.timeSamples = Mathf.Min(source.clip.samples / 3, source.clip.frequency * 8);
        yield return new WaitForSecondsRealtime(.12f);
        int before = source.timeSamples;
        action();
        bool restarted = source != MapMusicPlayer.Instance.CurrentSource || !source.isPlaying || source.timeSamples < before;
        float until = Time.realtimeSinceStartup + .25f;
        while (Time.realtimeSinceStartup < until) {
            restarted |= source != MapMusicPlayer.Instance.CurrentSource || !source.isPlaying || source.timeSamples < before;
            yield return null;
        }
        CheckThat(name, !restarted && source.timeSamples > before, "samples=" + before + " -> " + source.timeSamples);
    }

    IEnumerator MeasureBalance(string id)
    {
        Require(session.bellAudio && session.bellAudio.clip, "Map bell audio is missing: " + id);
        if (listener) listener.transform.position = session.bellTarget.position + Vector3.back * 2;
        var volumes = SfxVolumes();
        var otherMutes = volumes.Keys.Where(s => s != session.bellAudio).ToDictionary(s => s, s => s.mute);
        foreach (var source in otherMutes.Keys) source.mute = true;
        session.bellAudio.Stop();
        MapMusicPlayer.Volume = DefaultVolume;
        var music = MapMusicPlayer.Instance.CurrentSource;
        music.timeSamples = Mathf.Min(music.clip.samples / 4, music.clip.frequency * 5);
        yield return new WaitForSecondsRealtime(.2f);
        yield return CaptureAudio(id, "music-only-at-bell", 1);
        var baseline = lastMeasurement;
        MapMusicPlayer.Volume = 0;
        yield return new WaitForSecondsRealtime(.2f);
        yield return CaptureAudio(id, "bell-only", 1, PlayBellEvent);
        var bell = lastMeasurement;
        session.bellAudio.Stop();
        MapMusicPlayer.Volume = DefaultVolume;
        music.timeSamples = Mathf.Min(music.clip.samples / 4, music.clip.frequency * 5);
        yield return new WaitForSecondsRealtime(.2f);
        yield return CaptureAudio(id, "music-and-bell", 1, PlayBellEvent);
        var mixed = lastMeasurement;
        CheckThat(id + ": music controls leave all SFX source volumes unchanged", SfxUnchanged(volumes));
        if (baseline.audible && bell.audible && mixed.audible) {
            float advantage = bell.loudest50msDbfs - baseline.loudest50msDbfs;
            CheckThat(id + ": real bell attack remains prominent over default music", advantage >= 3,
                "At 2m from bell, loudest 50ms bell=" + bell.loudest50msDbfs.ToString("F2") + "dBFS; music=" + baseline.loudest50msDbfs.ToString("F2") + "dBFS; difference=" + advantage.ToString("F2") + "dB (minimum 3dB).");
            CheckThat(id + ": actual bell plus music mixer output does not clip", mixed.peakDbfs < -.01f,
                "Mixed sample peak=" + mixed.peakDbfs.ToString("F2") + "dBFS. Not a true-peak or subjective masking guarantee.");
        } else Limit(id + ": audible bell/music balance", "Real mixer output was unavailable or silent; source volume properties are not substituted for acoustic evidence.");
        session.bellAudio.Stop();
        session.ResetPractice();
        foreach (var pair in otherMutes) if (pair.Key) pair.Key.mute = pair.Value;
    }

    void PlayBellEvent()
    {
        int before = session.BellCount;
        // Exercise the production received-bell event, including its real spatial source.
        // Course traversal/claim eligibility is intentionally not part of this audio test.
        session.ApplyNetworkState(session.RunActive, before + 1);
        if (listener) listener.transform.position = session.bellTarget.position + Vector3.back * 2;
        CheckThat(session.maps.current.id + ": real bell event starts the existing bell source", session.BellCount == before + 1 && session.bellAudio.isPlaying);
    }

    IEnumerator MeasureCombatBalance()
    {
        session.ResetPractice();
        Require(session.Local.Stand(), "Combat audio fixture cannot stand.");
        var actor = session.Local;
        var rope = actor.GetComponent<LassoAudio>();
        Require(listener && rope && rope.ClipsReady && actor.Receiver.impactSource, "Combat audio sources unavailable.");
        listener.transform.position = actor.Motor.CurrentEyePosition;
        var volumes = SfxVolumes();
        var muted = volumes.Keys.ToDictionary(s => s, s => s.mute);
        int sequence = 1000;
        foreach (string kind in new[] { "push-impact", "kick-impact", "rope-throw", "rope-latch", "rope-pull" }) {
            var effect = kind.StartsWith("rope-") ? rope.Source(kind == "rope-throw" ? LassoSound.Throw : kind == "rope-latch" ? LassoSound.Latch : LassoSound.Pull) : actor.Receiver.impactSource;
            foreach (var source in muted.Keys) { source.Stop(); source.mute = source != effect; }
            var music = MapMusicPlayer.Instance.CurrentSource;
            MapMusicPlayer.Volume = DefaultVolume;
            music.timeSamples = music.clip.frequency * 5;
            yield return new WaitForSecondsRealtime(.2f);
            yield return CaptureAudio("first-steps", kind + "-music-only", .9f);
            var baseline = lastMeasurement;
            Action trigger = () => {
                if (kind.StartsWith("rope-"))
                    rope.Play(kind == "rope-throw" ? LassoSound.Throw : kind == "rope-latch" ? LassoSound.Latch : LassoSound.Pull, ++sequence, listener.transform.position, true);
                else actor.Receiver.ReceiveNetwork(kind == "kick-impact" ? MeleeAttack.Kick : MeleeAttack.PushLeft, Vector3.forward, actor.transform.position);
                Require(effect.isPlaying, kind + " production sound did not start.");
            };
            MapMusicPlayer.Volume = 0;
            yield return new WaitForSecondsRealtime(.2f);
            yield return CaptureAudio("first-steps", kind + "-only", .9f, trigger);
            var effectOnly = lastMeasurement;
            effect.Stop();
            MapMusicPlayer.Volume = DefaultVolume;
            music.timeSamples = music.clip.frequency * 5;
            yield return new WaitForSecondsRealtime(.2f);
            yield return CaptureAudio("first-steps", kind + "-mixed", .9f, trigger);
            var mixed = lastMeasurement;
            CheckThat(kind + ": real local feedback remains prominent over default music",
                baseline.audible && effectOnly.audible && effectOnly.loudest50msDbfs >= baseline.loudest50msDbfs + 3,
                "50ms effect/music difference=" + (effectOnly.loudest50msDbfs - baseline.loudest50msDbfs).ToString("F2") + "dB; local received-event audio path.");
            CheckThat(kind + ": real effect plus music does not clip", mixed.audible && mixed.peakDbfs < -.01f,
                "Mixed sample peak=" + mixed.peakDbfs.ToString("F2") + "dBFS.");
            effect.Stop();
        }
        CheckThat("Combat balance checks leave SFX source volumes unchanged", SfxUnchanged(volumes));
        foreach (var pair in muted) if (pair.Key) pair.Key.mute = pair.Value;
        session.ResetPractice();
    }

    IEnumerator CheckVolume()
    {
        var values = SfxVolumes();
        var previousMutes = values.Keys.ToDictionary(s => s, s => s.mute);
        foreach (var sfx in previousMutes.Keys) sfx.mute = true;
        var source = MapMusicPlayer.Instance.CurrentSource;
        source.timeSamples = Mathf.Min(source.clip.samples / 3, source.clip.frequency * 5);
        MapMusicPlayer.Volume = 0;
        yield return new WaitForSecondsRealtime(.25f);
        int before = source.timeSamples;
        yield return CaptureAudio(session.maps.current.id, "volume-zero", .7f);
        CheckThat("Zero music volume leaves loop transport running", source.isPlaying && source.timeSamples > before && Sources().All(s => Mathf.Approximately(s.volume, 0)));
        CheckThat("Zero music volume is persisted without changing SFX volume", PlayerPrefs.GetFloat(Preference, -1) == 0 && SfxUnchanged(values));
        if (audibleOutput == true) CheckThat("Zero volume removes music from the real mixer output", !lastMeasurement.audible && lastMeasurement.peakDbfs < -70,
            "Sample peak=" + lastMeasurement.peakDbfs.ToString("F2") + "dBFS.");
        else Limit("Audible volume-zero test", "No previously established nonzero mixer output.");
        MapMusicPlayer.Volume = DefaultVolume;
        yield return new WaitForSecondsRealtime(.25f);
        yield return CaptureAudio(session.maps.current.id, "volume-restored", .7f);
        RecordOutput("Restoring the slider restores actual music output", lastMeasurement);
        CheckThat("Restoring music volume preserves SFX settings", SfxUnchanged(values));
        foreach (var pair in previousMutes) if (pair.Key) pair.Key.mute = pair.Value;
    }

    IEnumerator CheckHomeAndRoom()
    {
        var network = CourseNetwork.Instance;
        network.ShowStartScreen();
        yield return Await(() => Sources().All(s => !s.isPlaying), 4, "Home music fade-out");
        CheckThat("Home screen stops map music", network.OverlayVisible && string.IsNullOrEmpty(MapMusicPlayer.Instance.CurrentMapId) && !MapMusicPlayer.Instance.CurrentSource);
        yield return CaptureSettings();
        network.StartPractice();
        yield return null;
        Bind();
        yield return Await(Ready, 8, "Resume from home");
        CheckThat("Returning from home resumes the selected map music", Ready());
        network.ShowStartScreen();
        ushort port;
        using (var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0))) port = (ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port;
        network.StartLoopback(true, port, "Music validation");
        yield return Await(() => network.IsHost && network.HumanCount == 1, 12, "Local host room");
        Require(network.AddBot(), "Host could not add the second participant.");
        yield return Await(() => network.CanStart, 8, "Ready host room");
        yield return Await(() => Sources().All(s => !s.isPlaying), 4, "Room silence");
        CheckThat("Real local waiting room has no map music", CourseNetwork.Active && !network.Playing && Sources().All(s => !s.isPlaying));
        network.SelectMap("first-steps");
        network.StartMatch();
        yield return Await(() => network.Playing, 25, "Local room match start");
        Bind();
        yield return Await(Ready, 8, "Online match music start");
        CheckThat("Starting a real local room match plays its map track", MapMusicPlayer.Instance.CurrentMapId == "first-steps" && Ready());
        network.ShowMenu();
        yield return new WaitForSecondsRealtime(1);
        CheckThat("Opening the online pause menu does not stop a running match track", network.Playing && Ready());
        network.ReturnToRoom();
        yield return Await(() => Sources().All(s => !s.isPlaying), 4, "Return-to-room fade-out");
        CheckThat("ReturnToRoom stops both map sources", !network.Playing && !MapMusicPlayer.Instance.CurrentSource && Sources().All(s => !s.isPlaying));
        yield return Await(() => network.CanStart, 8, "Room ready for replay");
        network.StartMatch();
        yield return Await(() => network.Playing, 25, "Room replay start");
        Bind();
        yield return Await(Ready, 8, "Room replay music start");
        CheckThat("Starting another room round resumes music without extra sources", Ready() && PlayerCount() == 1 && Sources().Length == 2 && Sources().All(s => ownedSources.Contains(s)));
        var leaving = network.Leave();
        yield return Await(() => leaving.IsCompleted, 12, "Leave local room");
        leaving.GetAwaiter().GetResult();
        yield return Await(() => Sources().All(s => !s.isPlaying), 4, "Leave-room music stop");
        CheckThat("Leaving the room returns to silent home", !CourseNetwork.Active && network.OverlayVisible && Sources().All(s => !s.isPlaying));
    }

    IEnumerator CaptureSettings()
    {
        if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) {
            Limit("Actual music settings screenshot", "Run graphically without -batchmode/-nographics to render the genuine home music slider.");
            yield break;
        }
        PlayModeWindow.GetRenderingResolution(out previousWidth, out previousHeight);
        PlayModeWindow.SetCustomRenderingResolution(1920, 1080, "Map music settings validation");
        EditorApplication.ExecuteMenuItem("Window/General/Game");
        yield return null;
        yield return null;
        yield return new WaitForEndOfFrame();
        Directory.CreateDirectory("Previews");
        var image = ScreenCapture.CaptureScreenshotAsTexture();
        Require(image, "Game view screenshot capture returned no texture.");
        report.screenshot = "Previews/MapMusic_Settings.png";
        File.WriteAllBytes(report.screenshot, image.EncodeToPNG());
        CheckThat("Real home settings including the music slider are captured", image.width == 1920 && image.height == 1080, report.screenshot);
        Destroy(image);
        RestoreResolution();
    }

    IEnumerator CaptureAudio(string map, string kind, float seconds, Action trigger = null)
    {
        fallbackSamples.Clear();
        int rate = AudioSettings.outputSampleRate;
        double start = AudioSettings.dspTime;
        if (probe) probe.Begin(rate, seconds + .3f);
        trigger?.Invoke();
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until) {
            AudioListener.GetOutputData(output, 0);
            fallbackSamples.AddRange(output);
            yield return null;
        }
        double end = AudioSettings.dspTime;
        float[] data = probe ? probe.End(out _, out _) : null;
        int count = probe ? probe.Count : 0, channels = probe ? probe.Channels : 1;
        bool continuous = count > 0;
        if (!continuous) { data = fallbackSamples.ToArray(); count = data.Length; channels = 1; }
        lastMeasurement = AddMeasurement(map, kind,
            continuous ? "continuous AudioListener OnAudioFilterRead" : "AudioListener.GetOutputData mono frame snapshots (may overlap or have gaps)",
            data, count, channels, rate, start, end, continuous);
        if (!continuous && !report.limits.Contains("Continuous audio capture callbacks were unavailable; frame-snapshot measurements are not a continuous recording."))
            report.limits.Add("Continuous audio capture callbacks were unavailable; frame-snapshot measurements are not a continuous recording.");
    }

    Measurement AddMeasurement(string map, string kind, string method, float[] data, int count, int channels, int rate, double start, double end, bool continuous)
    {
        channels = Mathf.Max(1, channels); rate = Mathf.Max(1, rate);
        double sum = 0, windowSum = 0, loudest = 0;
        float peak = 0;
        int silentFrames = 0, longestSilence = 0, windowCount = 0;
        int window = Mathf.Max(channels, rate / 20 * channels);
        for (int i = 0; i < count; i += channels) {
            float framePeak = 0;
            for (int c = 0; c < channels && i + c < count; c++) {
                float sample = data[i + c];
                float absolute = Mathf.Abs(sample);
                peak = Mathf.Max(peak, absolute); framePeak = Mathf.Max(framePeak, absolute);
                sum += (double)sample * sample; windowSum += (double)sample * sample;
                if (++windowCount == window) { loudest = Math.Max(loudest, windowSum / windowCount); windowSum = 0; windowCount = 0; }
            }
            silentFrames = framePeak < .00001f ? silentFrames + 1 : 0;
            longestSilence = Mathf.Max(longestSilence, silentFrames);
        }
        if (windowCount > 0) loudest = Math.Max(loudest, windowSum / windowCount);
        var measurement = new Measurement {
            map = map, kind = kind, method = method, sampleRate = rate, channels = channels, samples = count,
            seconds = count / (float)(channels * rate), rmsDbfs = Db(Math.Sqrt(sum / Math.Max(1, count))), peakDbfs = Db(peak),
            loudest50msDbfs = Db(Math.Sqrt(loudest)), longestSilenceSeconds = longestSilence / (float)rate,
            dspStart = start, dspEnd = end, audible = peak > .00001f
        };
        if (continuous && count > 0) {
            Directory.CreateDirectory("TestResults/MapMusicAudio");
            measurement.file = "TestResults/MapMusicAudio/" + map + "-" + kind + ".wav";
            WriteWave(measurement.file, data, count, channels, rate);
        }
        report.measurements.Add(measurement);
        return measurement;
    }

    void RecordOutput(string name, Measurement measurement)
    {
        if (measurement.audible) {
            audibleOutput = true;
            CheckThat(name, true, measurement.method + "; peak=" + measurement.peakDbfs.ToString("F2") + "dBFS; RMS=" + measurement.rmsDbfs.ToString("F2") + "dBFS.");
        } else if (audibleOutput == true) {
            CheckThat(name, false, "Earlier mixer output was nonzero, but this playing track produced no measurable output.");
        } else {
            audibleOutput = false;
            Limit(name, "No nonzero listener samples. DSP elapsed=" + (measurement.dspEnd - measurement.dspStart).ToString("F4") +
                "s; output rate=" + AudioSettings.outputSampleRate + "; active listener=" + (listener != null) +
                ". Cannot establish audible playback/device availability; source transport is tested separately.");
        }
    }

    static float Db(double amplitude) => (float)(20 * Math.Log10(Math.Max(amplitude, .000000001)));
    float SourceRms(AudioSource source)
    {
        source.GetOutputData(sourceOutput, 0);
        double sum = 0;
        foreach (float sample in sourceOutput) sum += (double)sample * sample;
        return (float)Math.Sqrt(sum / sourceOutput.Length);
    }
    Dictionary<AudioSource, float> SfxVolumes()
    {
        var musicSources = Sources();
        return FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(s => !musicSources.Contains(s)).ToDictionary(s => s, s => s.volume);
    }
    static bool SfxUnchanged(Dictionary<AudioSource, float> values) => values.All(pair => pair.Key && Mathf.Approximately(pair.Key.volume, pair.Value));

    static IEnumerator Await(Func<bool> condition, float seconds, string operation)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < until) yield return null;
        Require(condition(), operation + " timed out after " + seconds + " seconds. map=" + MapMusicPlayer.Instance?.CurrentMapId +
            "; source=" + MapMusicPlayer.Instance?.CurrentSource?.isPlaying + "; overlay=" + CourseNetwork.Instance?.OverlayVisible);
    }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    void CheckThat(string name, bool passed, string detail = "")
    {
        report.checks.Add(new Check { name = name, passed = passed, detail = detail });
        if (!passed) Debug.LogError("MAP_MUSIC_VALIDATION_FAILED " + name + ": " + detail);
        Save();
    }
    void Limit(string name, string detail)
    {
        report.checks.Add(new Check { name = name, limited = true, detail = detail });
        if (!report.limits.Contains(detail)) report.limits.Add(detail);
        Save();
    }
    void Save() => Save(report);
    static void Save(Report saved)
    {
        saved.passed = saved.checks.Count(c => c.passed);
        saved.failed = saved.checks.Count(c => !c.passed && !c.limited);
        saved.limited = saved.checks.Count(c => c.limited);
        Directory.CreateDirectory("TestResults");
        File.WriteAllText(ReportPath, JsonUtility.ToJson(saved, true));
    }
    void RestoreResolution()
    {
        if (previousWidth == 0 || previousHeight == 0) return;
        PlayModeWindow.SetCustomRenderingResolution(previousWidth, previousHeight, "Map music validation");
        previousWidth = previousHeight = 0;
    }
    static void RestorePreferences()
    {
        if (!SessionState.GetBool(State + "preferencesSaved", false)) return;
        MapMusicPlayer.Volume = SessionState.GetFloat(State + "volume", DefaultVolume);
        if (SessionState.GetBool(State + "hadVolume", false)) PlayerPrefs.SetFloat(Preference, SessionState.GetFloat(State + "volume", DefaultVolume));
        else PlayerPrefs.DeleteKey(Preference);
        if (SessionState.GetBool(State + "hadNickname", false)) PlayerPrefs.SetString("HalliGalli.Nickname", SessionState.GetString(State + "nickname", "Player"));
        else PlayerPrefs.DeleteKey("HalliGalli.Nickname");
        PlayerPrefs.Save();
        EditorUtility.audioMasterMute = SessionState.GetBool(State + "editorMute", false);
        Application.runInBackground = SessionState.GetBool(State + "background", false);
        EditorApplication.isPaused = SessionState.GetBool(State + "paused", false);
        EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt(State + "playOptions", 0);
        EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(State + "playOptionsEnabled", false);
        SessionState.EraseBool(State + "preferencesSaved");
    }
    void Finish()
    {
        if (!report.playbackCompleted || !report.persistenceCompleted)
            CheckThat("Both playback and persisted-settings sessions complete", false,
                "playbackCompleted=" + report.playbackCompleted + "; persistenceCompleted=" + report.persistenceCompleted);
        Complete(report, true);
    }
    static void Complete(Report saved, bool exit)
    {
        SessionState.SetString(State + "phase", "finishing");
        EditorApplication.update -= StartAfterAudioSessionMute;
        EditorApplication.delayCall -= LaunchPersistence;
        try {
            if (active) active.RestoreResolution();
            RestorePreferences();
            saved.preferencesRestored = true;
        } catch (Exception e) {
            saved.checks.Add(new Check { name = "Original music and editor preferences are restored", detail = e.ToString() });
            Debug.LogException(e);
        }
        saved.completedUtc = DateTime.UtcNow.ToString("O");
        saved.status = saved.checks.Any(c => !c.passed && !c.limited) ? "failed" :
            saved.checks.Any(c => c.limited) ? "incomplete-audio-or-graphics-evidence" : "passed";
        try {
            Save(saved);
            Debug.Log("MAP_MUSIC_VALIDATION_RESULT " + saved.passed + " passed; " + saved.failed + " failed; " + saved.limited +
                " limited; run=" + saved.runId + "; " + ReportPath);
        } finally {
            SessionState.EraseString(State + "phase");
            if (exit) EditorApplication.Exit(saved.failed > 0 ? 1 : saved.limited > 0 ? 2 : 0);
        }
    }
    void OnDisable() { RestoreResolution(); }

    static void WriteWave(string path, float[] samples, int count, int channels, int rate)
    {
        using (var writer = new BinaryWriter(File.Create(path))) {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1);
            writer.Write((short)channels); writer.Write(rate); writer.Write(rate * channels * 2); writer.Write((short)(channels * 2)); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(count * 2);
            for (int i = 0; i < count; i++) writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(samples[i], -1, 1) * 32767));
        }
    }
}

// Attached to the actual scene AudioListener, never to a synthetic sound source.
// The audio thread only copies into a bounded buffer; analysis and disk I/O stay on main.
public sealed class MapMusicValidationAudioProbe : MonoBehaviour
{
    readonly object gate = new object();
    float[] samples;
    bool recording;
    public int Count { get; private set; }
    public int Channels { get; private set; }

    public void Begin(int rate, float seconds)
    {
        int capacity = Mathf.CeilToInt(Mathf.Max(1, rate) * seconds) * 8;
        var buffer = samples != null && samples.Length >= capacity ? samples : new float[capacity];
        lock (gate) {
            samples = buffer;
            Count = 0; Channels = 0; recording = true;
        }
    }
    public float[] End(out int count, out int channels)
    {
        lock (gate) { recording = false; count = Count; channels = Channels; return samples; }
    }
    void OnAudioFilterRead(float[] data, int channels)
    {
        lock (gate) {
            if (!recording || channels <= 0 || channels > 8) return;
            if (Channels != 0 && Channels != channels) { recording = false; return; }
            Channels = channels;
            int count = Math.Min(data.Length, samples.Length - Count);
            count -= count % channels;
            Array.Copy(data, 0, samples, Count, count);
            Count += count;
        }
    }
}
#endif
