using UnityEngine;

namespace HalliGalli.Movement
{
    public sealed partial class UserMapStudio
    {
        enum TransformTool { Move, Rotate, Scale }
        TransformTool transformTool;
        UserMapPart palettePart = UserMapPart.Block;
        bool placementMode, placementValid, looking, dragging, snap = true, orbiting, middlePanning;
        float snapSize = .5f, placementHeight, flySpeed = 6;
        Vector3 placementPoint;
        GameObject selectedRoot;
        Bounds selectedBounds;
        UserMapObject dragOriginal, dragPreview, placementTemplate;
        Vector2 dragMouse;
        Vector2 orbitMouse;
        Vector3 dragStartPoint;
        Plane dragPlane;
        int dragAxis = -1;
        readonly Vector3[] boxCorners = new Vector3[8];
        static readonly int[] BoxEdges = { 0, 1, 1, 3, 3, 2, 2, 0, 4, 5, 5, 7, 7, 6, 6, 4, 0, 4, 1, 5, 2, 6, 3, 7 };
        static readonly int[] RampEdges = { 0, 2, 2, 4, 4, 0, 1, 3, 3, 5, 5, 1, 0, 1, 2, 3, 4, 5 };
        static readonly int[] SurfEdges = { 0, 1, 1, 4, 4, 0, 2, 3, 3, 5, 5, 2, 0, 2, 1, 3, 4, 5 };
        static readonly Vector3[] Axes = { Vector3.right, Vector3.up, Vector3.forward };
        static readonly Color[] AxisColors = { new Color(1, .36f, .28f), new Color(.5f, .92f, .4f), new Color(.3f, .64f, 1) };

        static Vector3 DefaultScale(UserMapPart kind)
        {
            switch (kind)
            {
                case UserMapPart.Ramp: return new Vector3(4, 2, 5);
                case UserMapPart.Surf: return new Vector3(5, 3, 8);
                case UserMapPart.Checkpoint: return new Vector3(5, .4f, 4);
                case UserMapPart.MovingPlatform: return new Vector3(3, .5f, 3);
                case UserMapPart.Conveyor: return new Vector3(4, .5f, 6);
                case UserMapPart.Press: return new Vector3(3, 4, 2);
                case UserMapPart.Hazard: return new Vector3(4, .2f, 4);
                case UserMapPart.Bell:
                case UserMapPart.Waypoint:
                case UserMapPart.ItemBox: return Vector3.one;
                default: return new Vector3(4, 1, 4);
            }
        }

        static UserMapObject DefaultObject(UserMapPart kind, Vector3 position)
        {
            return new UserMapObject { kind = kind, position = position, rotation = Vector3.zero, scale = DefaultScale(kind),
                color = 0, travel = kind == UserMapPart.MovingPlatform ? new Vector3(4, 0, 0) : Vector3.zero, period = 4, speed = 2 };
        }

        void Update()
        {
            if (closing || busy || !applicationFocused) { StopPointerInteraction(); return; }
            if (testing)
            {
                if (Input.GetKeyDown(KeyCode.F8)) { EndTest(); return; }
                if (playController)
                {
                    playController.suppressCursorCapture = true;
                    if (Input.GetKeyDown(KeyCode.Escape)) playController.SetCursorCaptured(false);
                    if (!Input.GetKeyDown(KeyCode.Escape) && Input.GetMouseButtonDown(0)
                        && Cursor.lockState != CursorLockMode.Locked && !TestHudRect.Contains(BellUI.MousePoint))
                        playController.SetCursorCaptured(true);
                }
                return;
            }
            if (!CanNavigate) { StopPointerInteraction(); return; }
            UpdateCameraViewport();
            if (FirstPersonBuild) { UpdateFirstPersonInput(); return; }
            bool inView = ViewportRect.Contains(BellUI.MousePoint);
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (partsMenuOpen) { SetPartsMenuOpen(false); return; }
                if (dragging) { CancelDrag(); return; }
                placementMode = false; EraseMode = false; StopPointerInteraction(); GUIUtility.keyboardControl = 0;
                return;
            }
            if (partsMenuOpen && inView && Input.GetMouseButtonDown(0))
            { SetPartsMenuOpen(false); return; }
            if (AdvancedCamera && Input.GetMouseButtonDown(1) && inView)
            {
                StopPointerInteraction();
                GUIUtility.keyboardControl = 0;
                looking = true; Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            }
            if (looking && !Input.GetMouseButton(1)) StopCameraLook();
            if (!AdvancedCamera && inView && Input.GetMouseButtonDown(1))
            {
                StopPointerInteraction();
                GUIUtility.keyboardControl = 0;
                orbiting = true; orbitMouse = Input.mousePosition;
            }
            if (inView && !looking && Input.GetMouseButtonDown(2))
            {
                StopPointerInteraction();
                GUIUtility.keyboardControl = 0;
                middlePanning = true; panMouse = Input.mousePosition;
            }
            if ((inView || looking) && Mathf.Abs(Input.mouseScrollDelta.y) > .01f)
            {
                if (AirBuild && !looking) AdjustAirBuildDistance(Input.mouseScrollDelta.y * 2);
                else ZoomCamera(Input.mouseScrollDelta.y);
            }
            if (GUIUtility.keyboardControl == 0)
            {
                if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.Plus) || Input.GetKeyDown(KeyCode.KeypadPlus)) ZoomCamera(1);
                if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus)) ZoomCamera(-1);
            }
            if (Input.GetKeyDown(KeyCode.F8)) { BeginTest(); return; }
            if (looking)
            {
                UpdateAdvancedCamera();
                placementValid = false;
                return;
            }
            if (orbiting)
            {
                Vector2 mouse = Input.mousePosition;
                if (Input.GetMouseButton(1) || Input.GetMouseButtonUp(1)) OrbitCamera(mouse - orbitMouse);
                orbitMouse = mouse;
                if (!Input.GetMouseButton(1)) orbiting = false;
                placementValid = false;
                return;
            }
            if (middlePanning)
            {
                Vector2 mouse = Input.mousePosition;
                if (Input.GetMouseButton(2) || Input.GetMouseButtonUp(2)) PanCamera(mouse - panMouse);
                panMouse = mouse;
                if (!Input.GetMouseButton(2)) middlePanning = false;
                placementValid = false;
                return;
            }
            if (dragging)
            {
                if (Input.GetMouseButton(0) || Input.GetMouseButtonUp(0)) UpdateDrag();
                if (Input.GetMouseButtonUp(0)) FinishDrag();
                else if (!Input.GetMouseButton(0)) CancelDrag();
                return;
            }
            if (panning)
            {
                Vector2 mouse = Input.mousePosition;
                if (Input.GetMouseButton(0) || Input.GetMouseButtonUp(0)) PanCamera(mouse - panMouse);
                panMouse = mouse;
                if (!Input.GetMouseButton(0)) panning = false;
                placementValid = false;
                return;
            }
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl && Input.GetKeyDown(KeyCode.S))
            {
                if (CanSaveFromUI) SaveMap();
                else Status = "저장하려면 입력을 적용하고 F8 클리어 검증부터 완료해 주세요.";
            }
            if (GUIUtility.keyboardControl == 0)
            {
                if (ctrl)
                {
                    if (Input.GetKeyDown(KeyCode.Z)) { if (Input.GetKey(KeyCode.LeftShift)) Redo(); else Undo(); }
                    if (Input.GetKeyDown(KeyCode.Y)) Redo();
                    if (Input.GetKeyDown(KeyCode.D)) DuplicateSelected();
                    if (Input.GetKeyDown(KeyCode.N)) NewMap();
                    if (Input.GetKeyDown(KeyCode.O)) OpenLibrary();
                }
                else
                {
                    if (Input.GetKeyDown(KeyCode.Delete)) DeleteSelected();
                    if (Input.GetKeyDown(KeyCode.Q)) SetAirBuild(!AirBuild);
                    if (Input.GetKeyDown(KeyCode.Alpha1)) SelectBuildPart(UserMapPart.Block);
                    if (Input.GetKeyDown(KeyCode.Alpha2)) SelectBuildPart(UserMapPart.Ramp);
                    if (Input.GetKeyDown(KeyCode.Alpha3)) SelectBuildPart(UserMapPart.Checkpoint);
                    if (Input.GetKeyDown(KeyCode.Alpha4)) SelectBuildPart(UserMapPart.MovingPlatform);
                    if (Input.GetKeyDown(KeyCode.Alpha5)) SelectBuildPart(UserMapPart.ItemBox);
                    if (Input.GetKeyDown(KeyCode.Alpha6)) SelectBuildPart(UserMapPart.Bell);
                    if (EasyBuild)
                    {
                        if (Input.GetKeyDown(KeyCode.R)) RotateBuild90();
                    }
                    else
                    {
                        if (Input.GetKeyDown(KeyCode.W)) transformTool = TransformTool.Move;
                        if (Input.GetKeyDown(KeyCode.E)) transformTool = TransformTool.Rotate;
                        if (Input.GetKeyDown(KeyCode.R))
                        {
                            if (FindObject(selectedId)?.kind == UserMapPart.ItemBox) Status = "아이템 박스 크기는 (1, 1, 1)로 고정되어 있어요.";
                            else transformTool = TransformTool.Scale;
                        }
                        if (Input.GetKeyDown(KeyCode.G)) snap = !snap;
                    }
                    if (Input.GetKeyDown(KeyCode.F)) FocusSelected();
                    if (Input.GetKeyDown(KeyCode.F1)) helpOpen = true;
                }
            }
            if (!CanNavigate) { StopPointerInteraction(); return; }
            if (!inView) { placementValid = false; return; }
            if (placementMode && !EraseMode) UpdatePlacement();
            if (Input.GetMouseButtonDown(0))
            {
                GUIUtility.keyboardControl = 0;
                if (EraseMode) EraseAtRay(studioCamera.ScreenPointToRay(Input.mousePosition));
                else if (placementMode) PlaceBuildPreview();
                else BeginSelection();
            }
        }

        void UpdateFirstPersonInput()
        {
            if (BuildCursorCaptured && (BuildMenuOpen || GUIUtility.keyboardControl != 0
                || Cursor.lockState != CursorLockMode.Locked && !Application.isBatchMode))
                SetBuildCursorCaptured(false);
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (BuildCursorCaptured) { SetBuildCursorCaptured(false); return; }
                if (partsMenuOpen) { SetPartsMenuOpen(false); return; }
                fileMenuOpen = easyContextOpen = mapInspector = false;
                placementMode = EraseMode = false;
                StopPointerInteraction();
                GUIUtility.keyboardControl = 0;
                return;
            }
            if (Input.GetKeyDown(KeyCode.F8)) { BeginTest(); return; }
            if (GUIUtility.keyboardControl != 0) return;
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                SetBuildCursorCaptured(!BuildCursorCaptured);
                return;
            }
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shortcut = false;
            if (ctrl)
            {
                if (Input.GetKeyDown(KeyCode.S))
                {
                    if (CanSaveFromUI) SaveMap();
                    else Status = "저장하려면 입력을 적용하고 F8 클리어 검증부터 완료해 주세요.";
                    shortcut = true;
                }
                if (Input.GetKeyDown(KeyCode.Z)) { if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) Redo(); else Undo(); shortcut = true; }
                if (Input.GetKeyDown(KeyCode.Y)) { Redo(); shortcut = true; }
                if (Input.GetKeyDown(KeyCode.D)) { DuplicateSelected(); shortcut = true; }
                if (Input.GetKeyDown(KeyCode.N)) { NewMap(); shortcut = true; }
                if (Input.GetKeyDown(KeyCode.O)) { OpenLibrary(); shortcut = true; }
            }
            if (shortcut) buildShortcutHeld = true;
            if (!ctrl || !(Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.Z)
                || Input.GetKey(KeyCode.Y) || Input.GetKey(KeyCode.N) || Input.GetKey(KeyCode.O))) buildShortcutHeld = false;
            if (!CanNavigate) { StopPointerInteraction(); return; }
            if (!BuildCursorCaptured)
            {
                placementValid = placementAvailable = false;
                if (!shortcut && ViewportRect.Contains(BellUI.MousePoint) && Input.GetMouseButtonDown(0))
                    SetBuildCursorCaptured(true);
                return;
            }
            if (buildPointerBlockedFrame == Time.frameCount) return;
            if (!ctrl)
            {
                if (Input.GetKeyDown(KeyCode.F1)) { helpOpen = true; SetBuildCursorCaptured(false); return; }
                if (Input.GetKeyDown(KeyCode.F)) { FocusSelected(); return; }
                if (Input.GetKeyDown(KeyCode.V)) SetBuildNoclip(!BuildNoclip);
                if (Input.GetKeyDown(KeyCode.Q)) SetAirBuild(!AirBuild);
                if (Input.GetKeyDown(KeyCode.R)) RotateBuild90();
                UserMapPart? part = null;
                if (Input.GetKeyDown(KeyCode.Alpha1)) part = UserMapPart.Block;
                if (Input.GetKeyDown(KeyCode.Alpha2)) part = UserMapPart.Ramp;
                if (Input.GetKeyDown(KeyCode.Alpha3)) part = UserMapPart.Checkpoint;
                if (Input.GetKeyDown(KeyCode.Alpha4)) part = UserMapPart.MovingPlatform;
                if (Input.GetKeyDown(KeyCode.Alpha5)) part = UserMapPart.ItemBox;
                if (Input.GetKeyDown(KeyCode.Alpha6)) part = UserMapPart.Bell;
                if (part.HasValue)
                {
                    SelectBuildPart(part.Value);
                    SetBuildCursorCaptured(true);
                    return;
                }
            }
            if (!BuildCursorCaptured || BuildMenuOpen) return;
            if (AirBuild && Mathf.Abs(Input.mouseScrollDelta.y) > .01f)
                AdjustAirBuildDistance(Input.mouseScrollDelta.y * 2);
            float forward = (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0);
            float right = (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0);
            bool jump = Input.GetKey(KeyCode.Space);
            float vertical = BuildNoclip ? (jump ? 1 : 0) - (ctrl ? 1 : 0) : ControlBindings.Held(GameAction.Crouch) ? -1 : 0;
            Vector3 movement = buildShortcutHeld ? Vector3.zero : new Vector3(right, vertical, forward);
            NavigateBuild(new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10,
                movement, Time.unscaledDeltaTime, jump && !buildShortcutHeld);
            if (placementMode && !EraseMode) UpdatePlacement();
            if (!ctrl && Input.GetKeyDown(KeyCode.Delete))
            {
                DeleteBuildTarget();
                return;
            }
            if (Input.GetMouseButtonDown(1))
            {
                string id = ObjectAt(BuildAimRay);
                bool selected = id != null && SelectObject(id);
                SetBuildCursorCaptured(false);
                easyContextOpen = selected;
                return;
            }
            if (Input.GetMouseButtonDown(0))
            {
                if (EraseMode) EraseAtRay(BuildAimRay);
                else if (placementMode) PlaceBuildPreview();
                else
                {
                    string id = ObjectAt(BuildAimRay);
                    if (id != null && SelectObject(id))
                    {
                        SetBuildCursorCaptured(false);
                        easyContextOpen = true;
                    }
                }
            }
        }

        public bool DeleteBuildTarget()
        {
            if (!CanNavigate || !FirstPersonBuild || !BuildCursorCaptured) return false;
            string id = ObjectAt(BuildAimRay);
            if (id == null) return false;
            bool resumePlacement = placementMode;
            if (!SelectObject(id)) return false;
            bool deleted = DeleteSelected();
            placementMode = resumePlacement;
            return deleted;
        }

        void OnApplicationFocus(bool focused)
        {
            applicationFocused = focused;
            if (focused) return;
            StopPointerInteraction();
            if (testing && playController) playController.SetCursorCaptured(false);
        }

        void StopCameraLook()
        {
            SetBuildCursorCaptured(false);
            looking = false;
            panning = orbiting = middlePanning = false;
            if (testing && !busy) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void ChoosePalette(UserMapPart part) => SelectBuildPart(part);

        void UpdatePlacement() => PreviewBuild(BuildAimRay);

        void RefreshSelectionRoot()
        {
            selectedRoot = UserMapRuntime.Current && !string.IsNullOrEmpty(selectedId) ? UserMapRuntime.Current.RootFor(selectedId) : null;
            var item = FindObject(selectedId);
            selectedBounds = item != null ? ObjectBounds(item) : new Bounds();
        }

        string ObjectAt(Ray ray)
        {
            if (!UserMapRuntime.Current) return null;
            bool hitSurface = Physics.Raycast(ray, out var hit, studioCamera.farClipPlane, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            float nearest = hitSurface ? hit.distance : studioCamera.farClipPlane;
            string selected = null;
            foreach (var item in draft.objects)
            {
                var root = UserMapRuntime.Current.RootFor(item.id);
                if (!root) continue;
                if (hitSurface && hit.distance <= nearest && (hit.transform == root.transform || hit.transform.IsChildOf(root.transform)))
                    selected = item.id;
                // Pickups have no physical collider; their visible bounds still participate in selection.
                if (item.kind == UserMapPart.ItemBox && ObjectBounds(item).IntersectRay(ray, out float distance) && distance < nearest)
                {
                    nearest = distance;
                    selected = item.id;
                }
            }
            return selected;
        }

        // Selection uses physical screen coordinates and the same click timing as live input.
        public bool SelectAtScreenPoint(Vector2 screenPoint, float clickTime)
        {
            if (!CanNavigate || dragging || !studioCamera.pixelRect.Contains(screenPoint)) return false;
            string id = ObjectAt(studioCamera.ScreenPointToRay(screenPoint));
            if (id == null) { lastSelectionId = null; return false; }
            bool doubleClick = IsSelectionDoubleClick(id, screenPoint, clickTime);
            if (id != selectedId && !SelectObject(id)) return true;
            placementMode = false;
            lastSelectionId = id;
            lastSelectionTime = clickTime;
            lastSelectionMouse = screenPoint;
            if (doubleClick) FocusSelected();
            return true;
        }

        bool IsSelectionDoubleClick(string id, Vector2 screenPoint, float clickTime)
        {
            return id != null && id == lastSelectionId && clickTime >= lastSelectionTime && clickTime - lastSelectionTime <= .35f
                && (screenPoint - lastSelectionMouse).sqrMagnitude <= 36 * BellUI.Scale * BellUI.Scale;
        }

        void BeginSelection()
        {
            var ray = studioCamera.ScreenPointToRay(Input.mousePosition);
            var item = FindObject(selectedId);
            int axis = !EasyBuild && item != null ? PickAxis(item.position) : -1;
            if (axis >= 0 && IsSelectionDoubleClick(selectedId, Input.mousePosition, Time.unscaledTime)
                && SelectAtScreenPoint(Input.mousePosition, Time.unscaledTime)) return;
            if (axis < 0)
            {
                if (!SelectAtScreenPoint(Input.mousePosition, Time.unscaledTime))
                {
                    panning = true;
                    panMouse = Input.mousePosition;
                }
                return;
            }
            if (!ApplyInspectorFields()) return;
            item = FindObject(selectedId);
            if (item == null) return;
            StopPointerInteraction();
            dragOriginal = CopyObject(item); dragPreview = CopyObject(item);
            dragRootScale = selectedRoot ? selectedRoot.transform.localScale : Vector3.one;
            dragMouse = BellUI.MousePoint; dragAxis = axis;
            // A plane containing the chosen axis and facing the camera also works in side view.
            Vector3 normal = Vector3.ProjectOnPlane(studioCamera.transform.forward, Axes[axis]);
            if (normal.sqrMagnitude < .0001f) return;
            dragPlane = new Plane(normal.normalized, item.position);
            bool intersects = dragPlane.Raycast(ray, out float distance);
            if (!intersects && transformTool == TransformTool.Move) return;
            dragStartPoint = intersects ? ray.GetPoint(distance) : item.position;
            dragging = true;
        }

        void UpdateDrag()
        {
            if (dragOriginal == null || !selectedRoot) { dragging = false; return; }
            Vector2 delta = BellUI.MousePoint - dragMouse;
            if (transformTool == TransformTool.Move)
            {
                Ray ray = studioCamera.ScreenPointToRay(Input.mousePosition);
                if (!dragPlane.Raycast(ray, out float distance)) return;
                Vector3 motion = ray.GetPoint(distance) - dragStartPoint;
                if (dragAxis >= 0) motion = Axes[dragAxis] * Vector3.Dot(motion, Axes[dragAxis]);
                Vector3 point = dragOriginal.position + motion;
                if (snap)
                {
                    if (dragAxis < 0 || dragAxis == 0) point.x = Snap(point.x, snapSize);
                    if (dragAxis == 1) point.y = Snap(point.y, snapSize);
                    if (dragAxis < 0 || dragAxis == 2) point.z = Snap(point.z, snapSize);
                }
                dragPreview.position = point;
            }
            else if (transformTool == TransformTool.Rotate)
            {
                int axis = dragAxis >= 0 ? dragAxis : 1;
                if (YawOnly(dragOriginal.kind)) axis = 1;
                float angle = delta.x * .6f;
                if (snap) angle = Snap(angle, 15);
                Vector3 rotation = dragOriginal.rotation;
                rotation[axis] = Mathf.Repeat(rotation[axis] + angle + 180, 360) - 180;
                dragPreview.rotation = rotation;
            }
            else
            {
                float change = 1 + delta.x * .01f;
                Vector3 scale = dragOriginal.scale;
                for (int i = 0; i < 3; i++)
                    if (dragAxis < 0 || dragAxis == i)
                        scale[i] = Mathf.Max(.1f, snap ? Snap(dragOriginal.scale[i] * change, .1f) : dragOriginal.scale[i] * change);
                dragPreview.scale = scale;
            }
            selectedRoot.transform.SetPositionAndRotation(dragPreview.position, Quaternion.Euler(dragPreview.rotation));
            // Roots may use dimensioned child meshes rather than localScale; use a relative preview delta.
            selectedRoot.transform.localScale = Vector3.Scale(new Vector3(dragPreview.scale.x / dragOriginal.scale.x,
                dragPreview.scale.y / dragOriginal.scale.y, dragPreview.scale.z / dragOriginal.scale.z), dragRootScale);
        }

        Vector3 dragRootScale = Vector3.one;

        void FinishDrag()
        {
            dragging = false;
            var value = dragPreview;
            RestoreDragRoot();
            if (value != null) UpdateObject(value);
            dragOriginal = dragPreview = null;
        }

        void CancelDrag()
        {
            dragging = false;
            RestoreDragRoot();
            dragOriginal = dragPreview = null;
        }

        void RestoreDragRoot()
        {
            if (!selectedRoot || dragOriginal == null) return;
            selectedRoot.transform.SetPositionAndRotation(dragOriginal.position, Quaternion.Euler(dragOriginal.rotation));
            selectedRoot.transform.localScale = dragRootScale;
        }

        static bool YawOnly(UserMapPart part)
        {
            return part == UserMapPart.Checkpoint || part == UserMapPart.MovingPlatform || part == UserMapPart.Conveyor
                || part == UserMapPart.Press || part == UserMapPart.Bell || part == UserMapPart.Waypoint || part == UserMapPart.ItemBox;
        }

        static float Snap(float value, float increment) => Mathf.Round(value / increment) * increment;
        float AxisLength(Vector3 origin)
        {
            float height = studioCamera.orthographic ? studioCamera.orthographicSize
                : Mathf.Max(.1f, Vector3.Dot(origin - studioCamera.transform.position, studioCamera.transform.forward))
                    * Mathf.Tan(studioCamera.fieldOfView * .5f * Mathf.Deg2Rad);
            return Mathf.Clamp(height * 150 * BellUI.Scale / Mathf.Max(1, studioCamera.pixelHeight), .35f, 200);
        }

        int PickAxis(Vector3 origin)
        {
            var item = FindObject(selectedId);
            if (EasyBuild) return -1;
            bool itemBox = item != null && item.kind == UserMapPart.ItemBox;
            if (itemBox && transformTool == TransformTool.Scale) return -1;
            if (!Project(origin, out Vector2 start)) return -1;
            float length = AxisLength(origin), best = 12;
            int chosen = -1;
            for (int i = 0; i < 3; i++)
            {
                if (itemBox && transformTool == TransformTool.Rotate && i != 1) continue;
                if (!Project(origin + Axes[i] * length, out Vector2 end)) continue;
                Vector2 line = end - start;
                if (line.sqrMagnitude < 100) continue;
                float t = Mathf.Clamp01(Vector2.Dot(BellUI.MousePoint - start, line) / Mathf.Max(.001f, line.sqrMagnitude));
                float distance = Vector2.Distance(BellUI.MousePoint, start + line * t);
                if (distance < best && t > .15f) { best = distance; chosen = i; }
            }
            return chosen;
        }

        bool Project(Vector3 point, out Vector2 guiPoint)
        {
            var screen = studioCamera.WorldToScreenPoint(point);
            guiPoint = new Vector2(screen.x, Screen.height - screen.y) / BellUI.Scale;
            return screen.z > studioCamera.nearClipPlane;
        }

        void DrawWorldGuides()
        {
            if (!studioCamera || Event.current.type != EventType.Repaint) return;
            if (placementMode && placementAvailable && CanNavigate && !EraseMode && !looking && !orbiting
                && !middlePanning && !panning && (FirstPersonBuild ? BuildCursorCaptured : ViewportRect.Contains(BellUI.MousePoint)))
            {
                var item = placementTemplate;
                Vector3 center = placementPoint;
                Color color = placementValid ? BellUI.Gold : new Color(1, .25f, .22f);
                DrawPlacementWire(item, center, color);
                if (Project(center, out Vector2 point))
                    BellUI.Text(new Rect(point.x + 16, point.y - 35, 320, 70),
                        placementValid ? PartName(item.kind) + " 미리보기\n클릭하여 배치" : "여기에는 놓을 수 없어요\n" + buildPreviewError,
                        15, color, shadow: true);
            }
            var selected = dragging ? dragPreview : FindObject(selectedId);
            if (selected == null) return;
            var bounds = selected.kind == UserMapPart.ItemBox
                ? new Bounds(selected.position + UserMapObject.ItemBoxBoundsCenter, UserMapObject.ItemBoxBoundsSize)
                : dragging ? new Bounds(selected.position, selected.scale) : selectedBounds;
            DrawWireBox(bounds.center, bounds.size + Vector3.one * .05f, Quaternion.identity, BellUI.Cool);
            if (FirstPersonBuild || EasyBuild || EraseMode) return;
            if (selected.kind == UserMapPart.ItemBox && transformTool == TransformTool.Scale) return;
            float length = AxisLength(selected.position);
            if (!Project(selected.position, out Vector2 start)) return;
            for (int i = 0; i < 3; i++)
            {
                if (selected.kind == UserMapPart.ItemBox && transformTool == TransformTool.Rotate && i != 1) continue;
                if (!Project(selected.position + Axes[i] * length, out Vector2 end)) continue;
                if ((end - start).sqrMagnitude < 100) continue;
                BellUI.Line(start, end, AxisColors[i], 3);
                BellUI.Round(new Rect(end.x - 6, end.y - 6, 12, 12), AxisColors[i], 6);
                BellUI.Text(new Rect(end.x + 9, end.y - 12, 24, 24), i == 0 ? "X" : i == 1 ? "Y" : "Z", 15, AxisColors[i], true, shadow: true);
            }
            if (dragging)
                BellUI.Text(new Rect(start.x + 15, start.y + 16, 300, 25), "놓아서 적용 · Esc 취소", 14, BellUI.Gold, shadow: true);
        }

        void DrawPlacementWire(UserMapObject item, Vector3 center, Color color)
        {
            Vector3 size = item.scale;
            Quaternion rotation = Quaternion.Euler(item.rotation);
            if (item.kind == UserMapPart.Ramp || item.kind == UserMapPart.Surf)
            {
                Vector3 half = size * .5f;
                boxCorners[0] = center + rotation * new Vector3(-half.x, -half.y, -half.z);
                boxCorners[1] = center + rotation * new Vector3(half.x, -half.y, -half.z);
                boxCorners[2] = center + rotation * new Vector3(-half.x, -half.y, half.z);
                boxCorners[3] = center + rotation * new Vector3(half.x, -half.y, half.z);
                bool ramp = item.kind == UserMapPart.Ramp;
                boxCorners[4] = center + rotation * new Vector3(ramp ? -half.x : 0, half.y, ramp ? half.z : -half.z);
                boxCorners[5] = center + rotation * new Vector3(ramp ? half.x : 0, half.y, half.z);
                var edges = ramp ? RampEdges : SurfEdges;
                for (int i = 0; i < edges.Length; i += 2) DrawWorldLine(boxCorners[edges[i]], boxCorners[edges[i + 1]], color);
                return;
            }
            if (item.kind == UserMapPart.Bell) { size = Vector3.Scale(size, new Vector3(.76f, 1.1f, .76f)); center.y += size.y * .5f; }
            if (item.kind == UserMapPart.Press) { size += new Vector3(.8f, 1, 0); center.y += size.y * .5f; }
            if (item.kind == UserMapPart.ItemBox) { size = UserMapObject.ItemBoxBoundsSize; center += UserMapObject.ItemBoxBoundsCenter; rotation = Quaternion.identity; }
            DrawWireBox(center, size, rotation, color);
            if (item.kind == UserMapPart.MovingPlatform)
            {
                Color pathColor = placementValid ? BellUI.Cool : color;
                DrawWorldLine(center - item.travel, center + item.travel, pathColor);
                DrawWireBox(center + item.travel, size, rotation, pathColor);
                DrawWireBox(center - item.travel, size, rotation, pathColor);
            }
        }

        void DrawWorldLine(Vector3 from, Vector3 to, Color color)
        {
            if (Project(from, out Vector2 a) && Project(to, out Vector2 b)) BellUI.Line(a, b, color, 2);
        }

        void DrawWireBox(Vector3 center, Vector3 size, Quaternion rotation, Color color)
        {
            for (int i = 0; i < 8; i++)
                boxCorners[i] = center + rotation * Vector3.Scale(size * .5f,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            for (int i = 0; i < BoxEdges.Length; i += 2)
                if (Project(boxCorners[BoxEdges[i]], out Vector2 from) && Project(boxCorners[BoxEdges[i + 1]], out Vector2 to))
                    BellUI.Line(from, to, color, 2);
        }
    }
}
