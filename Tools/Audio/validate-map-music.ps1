param(
    [string]$ProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')),
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe',
    [int]$TimeoutSeconds = 420
)
$ErrorActionPreference = 'Stop'
# Run in its own PowerShell process. Only the Unity process started below is targeted.
# Session mute is downstream of Unity DSP/OnAudioFilterRead. Never use endpoint mute.
# Invoke: powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tools/Audio/validate-map-music.ps1
# Exit 0 requires this run's completed playback + restart JSON AND confirmed OS-mute restoration.
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MapMusicValidationOutput {
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumerator {}
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint state, out IMMDeviceCollection devices);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object value);
        [PreserveSig] int OpenPropertyStore(uint access, out IntPtr properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2 {
        [PreserveSig] int GetAudioSessionControl(ref Guid id, uint flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(ref Guid id, uint flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
    }
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2 {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
        [PreserveSig] int GetGroupingParam(out Guid group);
        [PreserveSig] int SetGroupingParam(ref Guid group, ref Guid context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr events);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr events);
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetProcessId(out uint processId);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }
    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISimpleAudioVolume {
        [PreserveSig] int SetMasterVolume(float volume, ref Guid context);
        [PreserveSig] int GetMasterVolume(out float volume);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
    public sealed class SessionRecord {
        public string device, instance;
        public uint processId;
        public bool originalMute, mutedConfirmed, restoredConfirmed;
        public string restoreError;
        internal object native;
    }
    public sealed class SessionMute : IDisposable {
        readonly uint processId;
        readonly Dictionary<string, SessionRecord> retained = new Dictionary<string, SessionRecord>();
        readonly Dictionary<string, bool> originalBySession = new Dictionary<string, bool>();
        readonly List<SessionRecord> records = new List<SessionRecord>();
        Guid context = Guid.NewGuid();
        public SessionRecord[] Records { get { return records.ToArray(); } }
        public SessionMute(uint owner) { processId = owner; }
        static void Check(int hr) { Marshal.ThrowExceptionForHR(hr); }
        static void Release(object value) { if (value != null) Marshal.ReleaseComObject(value); }
        public int MuteOwnedSessions() {
            int found = 0;
            object enumerator = new MMDeviceEnumerator();
            IMMDeviceCollection devices = null;
            try {
                Check(((IMMDeviceEnumerator)enumerator).EnumAudioEndpoints(0, 1, out devices)); // render, active
                uint count; Check(devices.GetCount(out count));
                for (uint d = 0; d < count; d++) {
                    IMMDevice device = null;
                    object manager = null;
                    IAudioSessionEnumerator sessions = null;
                    try {
                        Check(devices.Item(d, out device));
                        string deviceId; Check(device.GetId(out deviceId));
                        Guid iid = typeof(IAudioSessionManager2).GUID;
                        Check(device.Activate(ref iid, 23, IntPtr.Zero, out manager));
                        Check(((IAudioSessionManager2)manager).GetSessionEnumerator(out sessions));
                        int n; Check(sessions.GetCount(out n));
                        for (int i = 0; i < n; i++) {
                            object raw = null;
                            bool keep = false;
                            try {
                                Check(sessions.GetSession(i, out raw));
                                var control = (IAudioSessionControl2)raw;
                                uint owner;
                                int hr = control.GetProcessId(out owner);
                                Check(hr);
                                if (owner != processId) continue;
                                // S_NO_SINGLE_PROCESS is not permission to mute other applications.
                                if (hr != 0) throw new InvalidOperationException("Unity has a cross-process audio session; refusing to mute it.");
                                string instance, identity;
                                Check(control.GetSessionInstanceIdentifier(out instance));
                                Check(control.GetSessionIdentifier(out identity));
                                string key = deviceId + "|" + instance;
                                var volume = (ISimpleAudioVolume)raw;
                                bool muted; Check(volume.GetMute(out muted));
                                SessionRecord record;
                                if (!retained.TryGetValue(key, out record)) {
                                    string identityKey = deviceId + "|" + identity;
                                    bool original;
                                    if (!originalBySession.TryGetValue(identityKey, out original)) {
                                        original = muted;
                                        originalBySession.Add(identityKey, original);
                                    }
                                    record = new SessionRecord { device = deviceId, instance = instance, processId = owner, originalMute = original, native = raw };
                                    retained.Add(key, record); records.Add(record); keep = true;
                                }
                                if (!muted) Check(volume.SetMute(true, ref context));
                                Check(volume.GetMute(out muted));
                                if (!muted) throw new InvalidOperationException("Owned Unity session did not acknowledge mute.");
                                record.mutedConfirmed = true;
                                found++;
                            } finally { if (!keep) Release(raw); }
                        }
                    } finally { Release(sessions); Release(manager); Release(device); }
                }
            } finally { Release(devices); Release(enumerator); }
            return found;
        }
        public void Dispose() {
            var errors = new List<string>();
            // Keep the session COM references until this point, including when Unity exited.
            foreach (var record in records) {
                if (record.native == null) continue;
                try {
                    var volume = (ISimpleAudioVolume)record.native;
                    Check(volume.SetMute(record.originalMute, ref context));
                    bool restored; Check(volume.GetMute(out restored));
                    record.restoredConfirmed = restored == record.originalMute;
                    if (!record.restoredConfirmed) throw new InvalidOperationException("Mute restoration was not acknowledged.");
                } catch (Exception e) { record.restoreError = e.Message; errors.Add(e.Message); }
                finally { Release(record.native); record.native = null; }
            }
            retained.Clear();
            if (errors.Count != 0) throw new InvalidOperationException("Audio session restoration failed: " + string.Join("; ", errors.ToArray()));
        }
    }
}
'@

function Assert-PlaybackReport {
    param([string]$Path, [string]$RunId, [int]$Owner, [DateTime]$StartedUtc, [DateTime]$ExitedUtc)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Playback report was not produced: $Path" }
    $result = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    foreach ($field in @('runId', 'processId', 'utc', 'completedUtc', 'status', 'passed', 'failed', 'limited',
            'playbackCompleted', 'persistenceCompleted', 'preferencesRestored', 'checks')) {
        if ($null -eq $result -or $result.PSObject.Properties.Name -notcontains $field -or $null -eq $result.$field) {
            throw "Playback report is missing $field; no complete run is claimed."
        }
    }
    if ($result.runId -cne $RunId -or $result.processId -ne $Owner) { throw 'Playback report belongs to a different run/process (stale evidence).' }
    $began = [DateTimeOffset]::MinValue
    $completed = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse([string]$result.utc, [ref]$began) -or
        -not [DateTimeOffset]::TryParse([string]$result.completedUtc, [ref]$completed) -or
        $began.UtcDateTime -lt $StartedUtc -or $completed -lt $began -or $completed.UtcDateTime -gt $ExitedUtc -or
        (Get-Item -LiteralPath $Path).LastWriteTimeUtc -lt $StartedUtc) {
        throw 'Playback report has missing, stale or inconsistent start/completion timestamps.'
    }
    if ($result.status -cne 'passed' -or $result.failed -ne 0 -or $result.limited -ne 0 -or $result.passed -le 0) {
        throw ("Playback report is not a pass: status={0}; passed={1}; failed={2}; limited={3}" -f
            $result.status, $result.passed, $result.failed, $result.limited)
    }
    foreach ($stage in @('playbackCompleted', 'persistenceCompleted', 'preferencesRestored')) {
        if ($result.$stage -isnot [bool] -or $result.$stage -ne $true) { throw "Playback report did not complete $stage." }
    }
    $checks = @($result.checks)
    if ($checks.Count -eq 0 -or $checks.Count -ne $result.passed) { throw 'Playback report has inconsistent or empty check totals.' }
    foreach ($check in $checks) {
        if ($null -eq $check -or [string]::IsNullOrWhiteSpace($check.name) -or
            $check.passed -isnot [bool] -or $check.passed -ne $true -or
            $check.limited -isnot [bool] -or $check.limited -ne $false) {
            throw 'Playback report contains a failed, limited or malformed check.'
        }
    }
    return $result
}

$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot)
if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity executable not found: $Unity" }
$log = Join-Path $ProjectRoot 'Logs/map-music-playback.log'
$evidence = Join-Path $ProjectRoot 'TestResults/map-music-os-audio.json'
$reportPath = Join-Path $ProjectRoot 'TestResults/map-music-playback.json'
$runId = [Guid]::NewGuid().ToString('N')
[IO.Directory]::CreateDirectory((Split-Path -Parent $log)) | Out-Null
[IO.Directory]::CreateDirectory((Split-Path -Parent $evidence)) | Out-Null
$ready = Join-Path $PSScriptRoot ('map-music-audio-ready-' + [Guid]::NewGuid().ToString('N') + '.txt')
$info = [Diagnostics.ProcessStartInfo]::new()
$info.FileName = $Unity
$info.UseShellExecute = $false
$info.WorkingDirectory = $ProjectRoot
$info.Arguments = '-projectPath "' + $ProjectRoot + '" -executeMethod MapMusicValidationRunner.Run -logFile "' + $log + '"'
$info.EnvironmentVariables['HALLIGALLI_MAP_MUSIC_AUDIO_READY'] = $ready
$info.EnvironmentVariables['HALLIGALLI_MAP_MUSIC_RUN_ID'] = $runId
$abort = $ready + '.abort'
$info.EnvironmentVariables['HALLIGALLI_MAP_MUSIC_ABORT'] = $abort
$process = $null
$mute = $null
$timer = [Diagnostics.Stopwatch]::StartNew()
$startedUtc = [DateTime]::UtcNow
$exitedUtc = $null
$unityExitCode = $null
$reportAccepted = $false
$failure = $null
$forcedTermination = $false
$exitCode = 1
$signalled = $false
try {
    $process = [Diagnostics.Process]::Start($info)
    Write-Output ('Owned Unity PID: ' + $process.Id)
    $mute = [MapMusicValidationOutput.SessionMute]::new([uint32]$process.Id)
    while (-not $process.HasExited) {
        if ($timer.Elapsed.TotalSeconds -ge $TimeoutSeconds) { throw "Owned Unity validation exceeded $TimeoutSeconds seconds; inspect $log" }
        $found = $mute.MuteOwnedSessions()
        if (-not $signalled -and $found -gt 0) {
            [IO.File]::WriteAllText($ready, [string]$process.Id)
            $signalled = $true
            Write-Output ('Confirmed OS-session mute; enabled actual Unity DSP validation at ' + $timer.Elapsed.TotalSeconds.ToString('F1') + 's')
        }
        if (-not $signalled -and $timer.Elapsed.TotalSeconds -ge 90) { throw 'No process-owned render audio session appeared within 90 seconds; Play mode remains gated.' }
        Start-Sleep -Milliseconds 100
    }
    $process.WaitForExit()
    $exitedUtc = [DateTime]::UtcNow
    $unityExitCode = $process.ExitCode
    if (-not $signalled) { throw 'Unity exited without establishing a muted output session. No audio validation is claimed.' }
    $result = Assert-PlaybackReport -Path $reportPath -RunId $runId -Owner $process.Id -StartedUtc $startedUtc -ExitedUtc $exitedUtc
    if ($unityExitCode -ne 0) { throw "Unity exited with code $unityExitCode despite a complete report; inspect $log" }
    $reportAccepted = $true
    $exitCode = 0
    Write-Output ("Complete playback and persistence report accepted: {0} passed, 0 failed, 0 limited; run {1}" -f $result.passed, $runId)
} catch {
    $failure = $_.Exception.Message
    $exitCode = 1
} finally {
    # Kill only the process this wrapper started, and never unmute a running validation.
    if ($null -ne $process -and -not $process.HasExited) {
        # The editor watchdog normally finishes first. On wrapper failure, give its
        # update callback a bounded chance to restore preferences and save failed JSON.
        try {
            [IO.File]::WriteAllText($abort, [string]$failure)
            $grace = [Diagnostics.Stopwatch]::StartNew()
            while (-not $process.HasExited -and $grace.Elapsed.TotalSeconds -lt 10) {
                if ($null -ne $mute) {
                    try { $null = $mute.MuteOwnedSessions() }
                    catch {
                        $failure += '; Cannot maintain owned-session mute while cancelling: ' + $_.Exception.Message
                        break
                    }
                }
                Start-Sleep -Milliseconds 100
            }
        } finally {
            if (-not $process.HasExited) {
                $forcedTermination = $true
                $failure = (@($failure, 'Editor did not acknowledge cancellation; forced termination cannot confirm preference restoration.') | Where-Object { $_ }) -join '; '
                $process.Kill()
                $process.WaitForExit()
            }
        }
    }
    if ($null -ne $process) {
        $unityExitCode = $process.ExitCode
        if ($null -eq $exitedUtc) { $exitedUtc = [DateTime]::UtcNow }
    }
    try {
        if ($null -ne $mute) { $mute.Dispose() }
    } catch {
        $failure = (@($failure, $_.Exception.Message) | Where-Object { $_ }) -join '; '
        $exitCode = 1
    } finally {
        $sessions = @()
        if ($null -ne $mute) { $sessions = @($mute.Records) }
        $audioEvidence = [ordered]@{
            utc = [DateTime]::UtcNow.ToString('O')
            startedUtc = $startedUtc.ToString('O')
            exitedUtc = $(if ($null -ne $exitedUtc) { $exitedUtc.ToString('O') } else { $null })
            runId = $runId
            status = $(if ($exitCode -eq 0) { 'passed' } else { 'failed' })
            error = $failure
            playbackReport = $reportPath
            playbackReportAccepted = $reportAccepted
            wrapperExitCode = $exitCode
            forcedTermination = $forcedTermination
            unityProcessId = $(if ($null -ne $process) { $process.Id } else { 0 })
            engineAudioDisabled = $false
            scope = 'Only process-owned Windows render sessions; no endpoint/global volume changes'
            gatedStartup = $signalled
            elapsedSeconds = $timer.Elapsed.TotalSeconds
            unityExitCode = $unityExitCode
            sessions = $sessions
        } | ConvertTo-Json -Depth 6
        [IO.File]::WriteAllText($evidence, $audioEvidence, [Text.UTF8Encoding]::new($false))
        if (Test-Path -LiteralPath $ready) { Remove-Item -LiteralPath $ready }
        if (Test-Path -LiteralPath $abort) { Remove-Item -LiteralPath $abort }
        if ($null -ne $process) { $process.Dispose() }
    }
}
if ($exitCode -ne 0) { [Console]::Error.WriteLine('MAP_MUSIC_VALIDATION_REJECTED: ' + $failure) }
exit $exitCode
