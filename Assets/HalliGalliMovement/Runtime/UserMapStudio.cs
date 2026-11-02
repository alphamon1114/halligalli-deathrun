using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HalliGalli.Movement
{
    [DefaultExecutionOrder(250)]
    public sealed partial class UserMapStudio : MonoBehaviour
    {
        public static UserMapStudio Current { get; private set; }
        public static bool IsOpen => Current && !Current.closing;
        public static bool IsTesting => IsOpen && Current.testing;
        public UserMapData Draft => draft;
        public bool IsBusy => busy;
        public bool HasUnsavedChanges => draft != null && (snapshot != savedSnapshot || MapFieldsChanged || InspectorFieldsChanged);
        public bool HasPendingConfirmation => pendingAction != null;
        public string SelectedId => selectedId;
        public string Status { get; private set; } = "";
        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;

        const int HistoryLimit = 64;
        readonly List<string> undo = new List<string>();
        readonly List<string> redo = new List<string>();
        UserMapData draft;
        string snapshot, savedSnapshot, selectedId, returnScene;
        bool busy, testing, closing;
        Action pendingAction;
        string pendingLabel;
        Camera sceneCamera, studioCamera;
        AudioListener sceneListener;
        SourcePlayerController playController;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatic() { Current = null; }

        public static void Open()
        {
            if (Current || CourseNetwork.Active) return;
            var network = CourseNetwork.Instance;
            if (network && (network.Busy || network.CurrentEntryScreen != CourseNetwork.EntryScreen.Home)) return;
            var studio = new GameObject("User map studio").AddComponent<UserMapStudio>();
            studio.returnScene = SceneManager.GetActiveScene().path;
            if (network && network.Game && network.Game.maps && network.Game.maps.catalog)
                foreach (var map in network.Game.maps.catalog.maps)
                    if (map && map.playable && !string.IsNullOrEmpty(map.scenePath) && map.scenePath != UserMapRuntime.ScenePath)
                    { studio.returnScene = map.scenePath; break; }
            studio.ReplaceDraft(UserMapStorage.CreateDefault(), false);
            studio.StartCoroutine(studio.LoadDraftScene(false));
        }

        void Awake()
        {
            if (Current && Current != this) { Destroy(gameObject); return; }
            Current = this;
            DontDestroyOnLoad(gameObject);
            Camera.onPreRender += BeforeStudioCameraRender;
            Camera.onPostRender += AfterStudioCameraRender;
        }

        void OnDestroy()
        {
            Camera.onPreRender -= BeforeStudioCameraRender;
            Camera.onPostRender -= AfterStudioCameraRender;
            RestoreCameraFog();
            DestroyBuildPawn();
            StopClearAttempt();
            if (Current == this) Current = null;
            if (studioCamera) Destroy(studioCamera.gameObject);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        IEnumerator LoadDraftScene(bool play)
        {
            busy = true;
            RememberBuildPose();
            StopClearAttempt();
            StopPointerInteraction();
            DestroyBuildPawn();
            RestoreCameraFog();
            if (studioCamera)
            {
                Destroy(studioCamera.gameObject);
                studioCamera = null;
            }
            testing = play;
            Status = play ? "솔로 클리어 검증을 준비하고 있어요…" : "편집기를 준비하고 있어요…";
            AsyncOperation load = null;
            try
            {
                UserMapRuntime.Pending = UserMapStorage.Clone(draft);
                load = SceneManager.LoadSceneAsync(UserMapRuntime.ScenePath, LoadSceneMode.Single);
                if (load == null) throw new InvalidOperationException("사용자 맵 씬을 불러오지 못했어요.");
            }
            catch (Exception e)
            {
                UserMapRuntime.Pending = null;
                testing = false;
                Status = e.Message;
            }
            if (load != null)
            {
                yield return load;
                // Session.Start initializes seats and the network restores the offline state first.
                yield return null;
                try
                {
                    var runtime = UserMapRuntime.Current;
                    if (!runtime) throw new InvalidOperationException("사용자 맵 런타임이 없어요. 사용자 맵 씬을 확인해 주세요.");
                    runtime.SetEditing(!play);
                    if (CourseNetwork.Instance) CourseNetwork.Instance.SetStudioPlay(play);
                    BindCamera(play);
                    RefreshSelectionRoot();
                    Status = play ? "출발점부터 혼자 이동해 E로 종을 울리세요. F8로 편집에 돌아갑니다." : ClearVerificationStatus;
                }
                catch (Exception e) { Status = e.Message; }
            }
            busy = false;
        }

        void BindCamera(bool play)
        {
            var game = CourseNetwork.Instance ? CourseNetwork.Instance.Game : FindAnyObjectByType<CourseSession>();
            var controller = game && game.Local ? game.Local.GetComponent<SourcePlayerController>() : null;
            sceneCamera = controller ? controller.playerCamera : Camera.main;
            sceneListener = sceneCamera ? sceneCamera.GetComponent<AudioListener>() : null;
            var character = controller ? controller.GetComponent<CharacterMotionDriver>() : null;
            if (character) {
                character.enabled = play;
                if (character.handCamera) character.handCamera.enabled = play && !character.thirdPerson;
            }
            if (play)
            {
                PrepareClearAttempt(game);
                playController = controller;
                if (controller) controller.suppressCursorCapture = true;
                if (game && game.cardGame && game.cardGame.tableView) game.cardGame.tableView.ClearFocus();
                if (game && game.Local) game.Local.Stand();
                if (sceneCamera) sceneCamera.enabled = true;
                if (sceneListener) sceneListener.enabled = true;
                return;
            }
            if (sceneCamera) sceneCamera.enabled = false;
            if (sceneListener) sceneListener.enabled = false;
            var cameraObject = new GameObject("Studio navigation camera");
            studioCamera = cameraObject.AddComponent<Camera>();
            if (sceneCamera) studioCamera.CopyFrom(sceneCamera);
            studioCamera.enabled = true;
            studioCamera.rect = new Rect(0, 0, 1, 1);
            studioCamera.nearClipPlane = .05f;
            studioCamera.farClipPlane = 10000;
            studioCamera.fieldOfView = 65;
            cameraObject.AddComponent<AudioListener>();
            BindBuildNavigation(game, controller);
            InitializeStudioCamera();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void ReplaceDraft(UserMapData data, bool saved)
        {
            UserMapStorage.Validate(data);
            draft = UserMapStorage.Clone(data);
            snapshot = UserMapStorage.Serialize(draft);
            savedSnapshot = saved ? snapshot : null;
            ResetDraftClearance(saved);
            selectedId = null;
            undo.Clear(); redo.Clear();
            SyncMapFields(); SyncInspectorFields();
            StopPointerInteraction();
            DestroyBuildPawn();
            buildPoseInitialized = false;
            BuildNoclip = true;
            placementMode = placementValid = placementAvailable = hasBuildRay = EraseMode = false;
            if (UserMapRuntime.Current && !busy && !testing)
            {
                UserMapRuntime.Current.Rebuild(UserMapStorage.Clone(draft));
                UserMapRuntime.Current.SetEditing(true);
            }
            RefreshSelectionRoot();
            cameraInitialized = false;
            if (studioCamera && !testing) InitializeStudioCamera();
        }

        public void NewMap()
        {
            if (!CanEdit) return;
            RequestDiscard("새 맵 만들기", () =>
            {
                ReplaceDraft(UserMapStorage.CreateDefault(), false);
                Status = "새 코스예요. 기본 발판과 종이 있어 바로 테스트할 수 있어요.";
            });
        }

        public void OpenMap(UserMapData data)
        {
            if (!CanEdit || data == null) return;
            try
            {
                var copy = UserMapStorage.Clone(data);
                UserMapStorage.Validate(copy);
                RequestDiscard("다른 맵 열기", () => { ReplaceDraft(copy, true); libraryOpen = false; Status = "맵을 열었어요: " + draft.name; });
            }
            catch (Exception e) { Status = e.Message; }
        }

        public bool SaveMap()
        {
            if (!CanEdit || !ApplyPendingFields() || !RequireDraftClearance()) return false;
            try
            {
                string path = UserMapStorage.Save(draft);
                savedSnapshot = snapshot;
                Status = "저장했어요: " + path;
                if (libraryOpen) RefreshLibrary();
                return true;
            }
            catch (Exception e) { Status = e.Message; return false; }
        }

        public bool ImportMap(string path)
        {
            if (!CanEdit || string.IsNullOrEmpty(path)) return false;
            bool imported = false;
            RequestDiscard("파일 가져오기", () =>
            {
                try
                {
                    var data = UserMapStorage.LoadFile(path);
                    UserMapDownloads.Store(data);
                    UserMapStorage.AssignDraftIdentity(data);
                    ReplaceDraft(data, false);
                    libraryOpen = false;
                    Status = "원본은 ‘받은 맵’에 보관되어 방에서 바로 선택할 수 있어요. 편집 사본은 F8로 혼자 클리어한 뒤 저장하세요.";
                    imported = true;
                }
                catch (Exception e) { Status = e.Message; }
            });
            return imported;
        }

        public void OpenDownloadedMap(string hash)
        {
            if (!CanEdit) return;
            RequestDiscard("받은 맵 사본 편집", () =>
            {
                try
                {
                    if (!UserMapDownloads.TryLoad(hash, out var data))
                        throw new InvalidOperationException("받은 맵 원본이 없거나 손상되었어요. 파일을 다시 가져오세요.");
                    UserMapStorage.AssignDraftIdentity(data);
                    ReplaceDraft(data, false);
                    libraryOpen = false;
                    Status = "원본은 그대로 보관돼요. 새 편집 사본을 저장하거나 내보내려면 F8로 직접 클리어하세요.";
                }
                catch (Exception e) { Status = e.Message; }
            });
        }

        public bool ExportDownloadedMap(string hash, string path)
        {
            if (!CanEdit || string.IsNullOrEmpty(path)) return false;
            try
            {
                UserMapDownloads.Export(hash, path);
                Status = "받은 원본을 내보냈어요: " + path + " · 직접 클리어 인증은 포함되지 않아요.";
                return true;
            }
            catch (Exception e) { Status = e.Message; return false; }
        }

        public bool ExportMap(string path)
        {
            if (!CanEdit || string.IsNullOrEmpty(path) || !ApplyPendingFields() || !RequireDraftClearance()) return false;
            try
            {
                UserMapStorage.Export(draft, path);
                Status = "내보냈어요: " + path + " · 이 .bellmap 파일 하나만 친구에게 보내세요.";
                return true;
            }
            catch (Exception e) { Status = e.Message; return false; }
        }

        public void BeginTest()
        {
            if (!CanEdit || pendingAction != null) return;
            StopPointerInteraction();
            if (!ApplyPendingFields()) return;
            try { UserMapStorage.Validate(draft); }
            catch (Exception e) { Status = e.Message; return; }
            libraryOpen = helpOpen = false;
            partsMenuOpen = fileMenuOpen = easyContextOpen = mapInspector = false;
            placementMode = placementValid = placementAvailable = hasBuildRay = EraseMode = false;
            StartCoroutine(LoadDraftScene(true));
        }

        public void EndTest()
        {
            if (!testing || busy) return;
            StartCoroutine(LoadDraftScene(false));
        }

        public void Close()
        {
            if (busy || closing) return;
            if (testing) { StartCoroutine(ReturnAndClose()); return; }
            RequestDiscard("홈으로 나가기", () => StartCoroutine(CloseScene()));
        }

        IEnumerator ReturnAndClose()
        {
            yield return LoadDraftScene(false);
            Close();
        }

        IEnumerator CloseScene()
        {
            busy = true;
            StopPointerInteraction();
            DestroyBuildPawn();
            AsyncOperation load = null;
            try
            {
                if (string.IsNullOrEmpty(returnScene) || returnScene == UserMapRuntime.ScenePath)
                    throw new InvalidOperationException("돌아갈 공식 코스가 없어요.");
                closing = true;
                UserMapRuntime.Pending = null;
                load = SceneManager.LoadSceneAsync(returnScene, LoadSceneMode.Single);
                if (load == null) throw new InvalidOperationException("홈 씬을 불러오지 못했어요.");
            }
            catch (Exception e) { closing = false; busy = false; Status = e.Message; }
            if (load == null) yield break;
            yield return load;
            yield return null;
            if (CourseNetwork.Instance) CourseNetwork.Instance.ShowStartScreen();
            Destroy(gameObject);
        }

        bool CanEdit => draft != null && !busy && !testing && !closing;

        void RequestDiscard(string label, Action action)
        {
            if (pendingAction != null) return;
            StopPointerInteraction();
            if (!HasUnsavedChanges) { action(); return; }
            pendingLabel = label;
            pendingAction = action;
        }

        public void ConfirmPending(bool saveChanges)
        {
            if (pendingAction == null) return;
            if (saveChanges && !SaveMap()) return;
            var action = pendingAction;
            pendingAction = null;
            action();
        }

        public void CancelPending() { pendingAction = null; }

        bool Commit(UserMapData next, bool mapFields = false)
        {
            if (!CanEdit) return false;
            try
            {
                string json = UserMapStorage.Serialize(next);
                if (json == snapshot) { if (mapFields) SyncMapFields(); SyncInspectorFields(); return true; }
                PushHistory(undo, snapshot);
                redo.Clear();
                draft = next;
                snapshot = json;
                if (mapFields) SyncMapFields();
                RebuildDraft();
                return true;
            }
            catch (Exception e) { Status = e.Message; return false; }
        }

        static void PushHistory(List<string> history, string value)
        {
            if (history.Count == HistoryLimit) history.RemoveAt(0);
            history.Add(value);
        }

        void RebuildDraft()
        {
            InvalidateDraftClearance();
            if (UserMapRuntime.Current)
            {
                UserMapRuntime.Current.Rebuild(UserMapStorage.Clone(draft));
                UserMapRuntime.Current.SetEditing(true);
            }
            if (FindObject(selectedId) == null) selectedId = null;
            RefreshSelectionRoot();
            SyncInspectorFields();
        }

        public void Undo() { RestoreHistory(undo, redo); }
        public void Redo() { RestoreHistory(redo, undo); }

        void RestoreHistory(List<string> from, List<string> to)
        {
            if (!CanEdit || from.Count == 0) return;
            var restored = UserMapStorage.Parse(from[from.Count - 1]);
            PushHistory(to, snapshot);
            snapshot = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            draft = restored;
            SyncMapFields();
            RebuildDraft();
            Status = "편집 기록을 복원했어요.";
        }

        public bool SelectObject(string id)
        {
            if (!CanEdit || !ApplyInspectorFields()) return false;
            var item = FindObject(id);
            selectedId = item != null ? id : null;
            placementMode = false;
            RefreshSelectionRoot();
            SyncInspectorFields();
            return item != null;
        }

        UserMapObject FindObject(string id)
        {
            if (draft == null || string.IsNullOrEmpty(id)) return null;
            foreach (var item in draft.objects) if (item.id == id) return item;
            return null;
        }

        public bool UpdateObject(UserMapObject value)
        {
            if (!CanEdit || value == null) return false;
            var next = UserMapStorage.Clone(draft);
            for (int i = 0; i < next.objects.Length; i++)
                if (next.objects[i].id == value.id)
                {
                    next.objects[i] = CopyObject(value);
                    return Commit(next);
                }
            return false;
        }

        static UserMapObject CopyObject(UserMapObject item)
        {
            return new UserMapObject { id = item.id, kind = item.kind, position = item.position, rotation = item.rotation,
                scale = item.scale, color = item.color, travel = item.travel, period = item.period, speed = item.speed };
        }

        public string PlaceObject(UserMapPart kind, Vector3 position)
        {
            if (!CanEdit || !ApplyInspectorFields()) return null;
            if (kind == UserMapPart.Bell)
                foreach (var item in draft.objects)
                    if (item.kind == UserMapPart.Bell)
                    {
                        var bell = CopyObject(item); bell.position = position;
                        if (!UpdateObject(bell)) return null;
                        selectedId = bell.id; RefreshSelectionRoot(); SyncInspectorFields();
                        return bell.id;
                    }
            var next = UserMapStorage.Clone(draft);
            var part = DefaultObject(kind, position);
            part.id = Guid.NewGuid().ToString("N");
            var objects = new UserMapObject[next.objects.Length + 1];
            Array.Copy(next.objects, objects, next.objects.Length);
            objects[objects.Length - 1] = part;
            next.objects = objects;
            if (!Commit(next)) return null;
            selectedId = part.id; RefreshSelectionRoot(); SyncInspectorFields();
            return part.id;
        }

        public bool DuplicateSelected()
        {
            if (!CanEdit || !ApplyInspectorFields()) return false;
            var item = FindObject(selectedId);
            if (item == null) return false;
            if (item.kind == UserMapPart.Bell) { Status = "종은 맵마다 하나예요. 위치를 옮겨 주세요."; return false; }
            var next = UserMapStorage.Clone(draft);
            var copy = CopyObject(item);
            copy.id = Guid.NewGuid().ToString("N");
            copy.position += Vector3.right * (snap ? snapSize : 1);
            var objects = new UserMapObject[next.objects.Length + 1];
            Array.Copy(next.objects, objects, next.objects.Length); objects[objects.Length - 1] = copy;
            next.objects = objects;
            if (!Commit(next)) return false;
            SelectObject(copy.id);
            return true;
        }

        public bool DeleteSelected()
        {
            var item = FindObject(selectedId);
            if (!CanEdit || item == null) return false;
            if (item.kind == UserMapPart.Bell) { Status = "완주 판정을 위해 종 하나는 꼭 필요해요. 삭제 대신 옮겨 주세요."; return false; }
            var next = UserMapStorage.Clone(draft);
            var objects = new List<UserMapObject>(next.objects);
            objects.RemoveAll(value => value.id == selectedId);
            next.objects = objects.ToArray();
            return Commit(next);
        }

        public bool MoveSelectedInList(int direction)
        {
            if (!CanEdit || direction == 0 || !ApplyInspectorFields()) return false;
            var next = UserMapStorage.Clone(draft);
            int index = Array.FindIndex(next.objects, value => value.id == selectedId);
            int target = index + Math.Sign(direction);
            if (index < 0 || target < 0 || target >= next.objects.Length) return false;
            var item = next.objects[index]; next.objects[index] = next.objects[target]; next.objects[target] = item;
            return Commit(next);
        }

        public bool SetMapInfo(string name, string description, float fallHeight)
        {
            if (!CanEdit) return false;
            var next = UserMapStorage.Clone(draft);
            next.name = name; next.description = description; next.fallHeight = fallHeight;
            return Commit(next, true);
        }

        static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        static bool TryNumber(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
