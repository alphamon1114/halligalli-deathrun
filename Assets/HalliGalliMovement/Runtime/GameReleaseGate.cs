using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace HalliGalli.Movement
{
    [DefaultExecutionOrder(-32000)]
    public sealed class GameReleaseGate : MonoBehaviour
    {
        const int RequestTimeoutSeconds = 20;
        static GameReleaseGate instance;
        static bool allowed;
        static Action pendingStartup;
        readonly List<GameObject> heldRoots = new List<GameObject>();
        readonly List<GameObject> sceneRoots = new List<GameObject>();
        UnityWebRequest request;
        Camera backdrop;
        bool initialSceneReady, checking = true;
        float previousTimeScale;
        bool previousAudioPause, previousCursorVisible;
        CursorLockMode previousCursorLock;
        ReleaseCheckResult blockedReason;
        int attempt;

        public static bool IsAllowed {
            get {
#if UNITY_EDITOR
                return true;
#else
                return allowed;
#endif
            }
        }

        // Startup callbacks are retained only until this process's first live admission.
        internal static void RunWhenAllowed(Action startup)
        {
            if (IsAllowed) { startup(); return; }
            pendingStartup -= startup;
            pendingStartup += startup;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatic()
        {
            if (instance) SceneManager.sceneLoaded -= instance.HoldLoadedScene;
            instance = null;
            pendingStartup = null;
            allowed = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
#if !UNITY_EDITOR
            var root = new GameObject("Release admission gate");
            instance = root.AddComponent<GameReleaseGate>();
            DontDestroyOnLoad(root);
            instance.HoldBeforeAwake();
#endif
        }

        void HoldBeforeAwake()
        {
            previousTimeScale = Time.timeScale;
            previousAudioPause = AudioListener.pause;
            previousCursorVisible = Cursor.visible;
            previousCursorLock = Cursor.lockState;
            Time.timeScale = 0;
            AudioListener.pause = true;
            UnlockCursor();
            SceneManager.sceneLoaded += HoldLoadedScene;
            // BeforeSceneLoad objects exist but are not yet active in the hierarchy. An active-only
            // search or waiting for sceneLoaded would let their Awake/OnEnable work run too soon.
            foreach (var transform in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!transform.parent && transform.gameObject.scene.IsValid()) HoldRoot(transform.gameObject);
            backdrop = gameObject.AddComponent<Camera>();
            backdrop.clearFlags = CameraClearFlags.SolidColor;
            backdrop.backgroundColor = BellUI.Ink;
            backdrop.cullingMask = 0;
        }

        void HoldRoot(GameObject root)
        {
            if (root == gameObject || !root.activeSelf) return;
            heldRoots.Add(root);
            root.SetActive(false);
        }

        void HoldLoadedScene(Scene scene, LoadSceneMode mode)
        {
            if (allowed) return;
            sceneRoots.Clear();
            scene.GetRootGameObjects(sceneRoots);
            foreach (var root in sceneRoots) HoldRoot(root);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InitialSceneLoaded()
        {
            if (!instance) return;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                instance.HoldLoadedScene(SceneManager.GetSceneAt(i), LoadSceneMode.Additive);
            instance.initialSceneReady = true;
        }

        IEnumerator Start()
        {
            // AfterSceneLoad startup registration order is unspecified. Do not admit or invoke
            // callbacks until all initial scene hooks have had their chance to register.
            while (!initialSceneReady) yield return null;
            yield return CheckLatest();
        }

        IEnumerator CheckLatest()
        {
            checking = true;
            attempt++;
            Debug.Log("RELEASE_GATE_CHECKING version=" + GameRelease.Version + " protocol=" + CourseNetwork.Protocol + " attempt=" + attempt);
            if (!BeginRequest()) {
                Block(ReleaseCheckResult.NetworkError, 0);
                yield break;
            }
            double deadline = Time.realtimeSinceStartupAsDouble + RequestTimeoutSeconds;
            bool oversized = false, timedOut = false;
            while (!request.isDone) {
                oversized = request.downloadedBytes > GameRelease.MaxManifestCharacters * 4UL;
                timedOut = Time.realtimeSinceStartupAsDouble >= deadline;
                if (oversized || timedOut) { request.Abort(); break; }
                yield return null;
            }
            long responseCode = request.responseCode;
            bool succeeded = !timedOut && request.result == UnityWebRequest.Result.Success;
            var result = oversized ? ReleaseCheckResult.InvalidManifest :
                GameRelease.CheckLatestResponse(succeeded, responseCode, succeeded ? request.downloadHandler.text : null);
            request.Dispose();
            request = null;
            if (result == ReleaseCheckResult.Allowed) Admit();
            else Block(result, responseCode);
        }

        bool BeginRequest()
        {
            try {
                // A fresh request is required even after a successful launcher check or a retry.
                // Unique queries prevent a cached latest-release redirect from authorizing an old build.
                request = UnityWebRequest.Get(GameRelease.LatestManifestUrl + "?check=" + Guid.NewGuid().ToString("N"));
                request.timeout = RequestTimeoutSeconds;
                request.redirectLimit = 5;
                request.SetRequestHeader("Cache-Control", "no-cache, no-store, max-age=0");
                request.SetRequestHeader("Pragma", "no-cache");
                request.SetRequestHeader("Accept", "application/json");
                request.SendWebRequest();
                return true;
            } catch (Exception exception) {
                Debug.LogWarning("RELEASE_GATE_REQUEST_ERROR type=" + exception.GetType().Name);
                request?.Dispose();
                request = null;
                return false;
            }
        }

        void Block(ReleaseCheckResult reason, long responseCode)
        {
            checking = false;
            blockedReason = reason;
            Debug.LogWarning("RELEASE_GATE_BLOCKED version=" + GameRelease.Version + " protocol=" + CourseNetwork.Protocol +
                " reason=" + reason + " http=" + responseCode + " attempt=" + attempt + " heldRoots=" + heldRoots.Count);
        }

        void Admit()
        {
            if (allowed) return;
            allowed = true;
            checking = false;
            SceneManager.sceneLoaded -= HoldLoadedScene;
            backdrop.enabled = false;
            Time.timeScale = previousTimeScale;
            AudioListener.pause = previousAudioPause;
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
            // Restore activeSelf only for roots that were originally active; authored inactive
            // objects stay inactive. Their normal Awake/OnEnable/Start now run, once.
            foreach (var root in heldRoots) if (root) root.SetActive(true);
            Debug.Log("RELEASE_GATE_ALLOWED version=" + GameRelease.Version + " protocol=" + CourseNetwork.Protocol +
                " attempt=" + attempt + " restoredRoots=" + heldRoots.Count);
            heldRoots.Clear();
            var startup = pendingStartup;
            pendingStartup = null;
            startup?.Invoke();
            Destroy(gameObject);
        }

        void OnApplicationFocus(bool focused) { if (focused && !allowed) UnlockCursor(); }
        static void UnlockCursor() { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= HoldLoadedScene;
            if (request != null) { request.Abort(); request.Dispose(); request = null; }
            if (instance == this) instance = null;
            // Destruction/error is never permission to reactivate a blocked scene.
        }

        string Explanation()
        {
            if (checking) return "GitHub에서 최신 버전을 확인하고 있어요. 확인이 끝나야 게임을 시작할 수 있어요.";
            switch (blockedReason) {
                case ReleaseCheckResult.VersionMismatch:
                case ReleaseCheckResult.ProtocolMismatch:
                    return "최신 배포 버전과 이 게임의 버전이 달라 실행할 수 없어요. 게임을 종료하고 "+BellUI.GameTitle+" 런처로 업데이트해 주세요.";
                case ReleaseCheckResult.UnsupportedSchema:
                    return "업데이트 정보 형식이 변경되었어요. 배포 페이지에서 최신 "+BellUI.GameTitle+" 런처를 받아 주세요.";
                case ReleaseCheckResult.InvalidManifest:
                    return "배포 정보를 안전하게 확인하지 못했어요. 잠시 후 다시 확인하거나 배포 페이지를 확인해 주세요.";
                default:
                    return "인터넷 연결 또는 배포 서버를 확인할 수 없어요. 오프라인에서는 게임을 시작할 수 없어요. 연결 후 다시 시도해 주세요.";
            }
        }

        void OnGUI()
        {
            if (allowed) return;
            var matrix = GUI.matrix;
            var color = GUI.color;
            bool enabled = GUI.enabled;
            int depth = GUI.depth;
            GUI.depth = -10000;
            BellUI.Begin();
            float x = (BellUI.W - 860) / 2, y = (BellUI.H - 360) / 2;
            BellUI.Round(new Rect(0, 0, BellUI.W, BellUI.H), BellUI.Ink, 0);
            BellUI.Round(new Rect(x, y, 860, 360), BellUI.Surface, 26);
            BellUI.Text(new Rect(x + 32, y + 24, 796, 46), checking ? "최신 버전 확인 중" : "게임 실행이 차단되었어요", 30, BellUI.Paper, true);
            BellUI.Text(new Rect(x + 32, y + 80, 796, 96), Explanation(), 21, BellUI.Paper);
            BellUI.Text(new Rect(x + 32, y + 188, 796, 35), "설치 버전 " + GameRelease.Version + " · 네트워크 규격 " + CourseNetwork.Protocol, 16, BellUI.Muted);
            GUI.enabled = !checking;
            if (BellUI.Button(new Rect(x + 32, y + 266, 240, 54), "다시 확인", true)) StartCoroutine(CheckLatest());
            GUI.enabled = true;
            if (BellUI.Button(new Rect(x + 288, y + 266, 260, 54), "업데이트 배포 페이지")) Application.OpenURL(GameRelease.ReleasesUrl);
            if (BellUI.Button(new Rect(x + 564, y + 266, 264, 54), "게임 종료")) Application.Quit();
            GUI.matrix = matrix;
            GUI.color = color;
            GUI.enabled = enabled;
            GUI.depth = depth;
        }
    }
}
