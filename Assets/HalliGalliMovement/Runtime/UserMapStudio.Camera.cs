using System.Collections.Generic;
using UnityEngine;

namespace HalliGalli.Movement
{
    public sealed partial class UserMapStudio
    {
        public enum StudioView { Top, Angled, Side }
        public bool AdvancedCamera { get; private set; }
        public StudioView CurrentStudioView { get; private set; } = StudioView.Top;
        public Camera EditorCamera => studioCamera;
        public Vector3 CameraFocus => cameraFocus;

        const float MinCameraZoom = 1, MaxCameraZoom = 2000, OrthographicDistance = 2000;
        Vector3 cameraFocus;
        Quaternion cameraRotation = Quaternion.Euler(90, 0, 0);
        float cameraZoom = 20;
        bool cameraInitialized, cameraFogSaved, cameraFogOverride;
        bool panning, applicationFocused = true;
        Vector2 panMouse, lastSelectionMouse;
        string lastSelectionId;
        float lastSelectionTime = float.NegativeInfinity;
        readonly List<Renderer> boundsRenderers = new List<Renderer>();

        bool CanNavigate => CanEdit && studioCamera && applicationFocused && pendingAction == null
            && !libraryOpen && !helpOpen && !UserMapStudioFileDialog.IsOpen;
        float CameraDistance => AdvancedCamera
            ? cameraZoom / Mathf.Tan(studioCamera.fieldOfView * .5f * Mathf.Deg2Rad) : OrthographicDistance;

        void InitializeStudioCamera()
        {
            UpdateCameraViewport();
            if (!cameraInitialized)
            {
                AdvancedCamera = false;
                CurrentStudioView = StudioView.Top;
                cameraRotation = ViewRotation(CurrentStudioView);
                FrameCamera(MapBounds());
                cameraInitialized = true;
            }
            else ApplyCameraPose();
        }

        void UpdateCameraViewport()
        {
            var viewport = ViewportRect;
            float scale = BellUI.Scale;
            var pixels = new Rect(viewport.x * scale, Screen.height - viewport.yMax * scale,
                Mathf.Max(1, viewport.width * scale), Mathf.Max(1, viewport.height * scale));
            if (studioCamera.pixelRect != pixels) studioCamera.pixelRect = pixels;
            studioCamera.aspect = pixels.width / pixels.height;
        }

        void ApplyCameraPose()
        {
            if (!studioCamera) return;
            studioCamera.orthographic = !AdvancedCamera;
            studioCamera.orthographicSize = cameraZoom;
            studioCamera.transform.SetPositionAndRotation(cameraFocus - cameraRotation * Vector3.forward * CameraDistance, cameraRotation);
        }

        static Quaternion ViewRotation(StudioView view)
        {
            switch (view)
            {
                case StudioView.Angled: return Quaternion.Euler(45, -35, 0);
                case StudioView.Side: return Quaternion.Euler(0, -90, 0);
                default: return Quaternion.Euler(90, 0, 0);
            }
        }

        public void SetStudioView(StudioView view)
        {
            if (!CanNavigate || (view != StudioView.Top && view != StudioView.Angled && view != StudioView.Side)) return;
            StopPointerInteraction();
            AdvancedCamera = false;
            CurrentStudioView = view;
            cameraRotation = ViewRotation(view);
            UpdateCameraViewport();
            var item = FindObject(selectedId);
            var bounds = item != null ? ObjectBounds(item) : MapBounds();
            if (item != null) cameraFocus = bounds.center;
            cameraZoom = FitCameraZoom(bounds, cameraFocus);
            ApplyCameraPose();
        }

        public void SetAdvancedCamera(bool enabled)
        {
            if (!CanNavigate || AdvancedCamera == enabled) return;
            StopPointerInteraction();
            AdvancedCamera = enabled;
            if (!enabled) cameraRotation = ViewRotation(CurrentStudioView);
            ApplyCameraPose();
        }

        public void FocusAll()
        {
            if (!CanNavigate) return;
            StopPointerInteraction();
            FrameCamera(MapBounds());
        }

        public void FocusStart()
        {
            if (!CanNavigate) return;
            StopPointerInteraction();
            FrameCamera(StartBounds());
        }

        public void FocusBell()
        {
            if (!CanNavigate) return;
            foreach (var item in draft.objects)
                if (item.kind == UserMapPart.Bell)
                {
                    StopPointerInteraction();
                    FrameCamera(ObjectBounds(item));
                    return;
                }
        }

        public void FocusSelected()
        {
            if (!CanNavigate) return;
            var item = FindObject(selectedId);
            if (item == null) { FocusAll(); return; }
            StopPointerInteraction();
            FrameCamera(ObjectBounds(item));
        }

        public void ZoomCamera(float steps)
        {
            if (!CanNavigate || float.IsNaN(steps) || float.IsInfinity(steps) || steps == 0) return;
            if (dragging) CancelDrag();
            cameraZoom = Mathf.Clamp(cameraZoom * Mathf.Pow(.85f, Mathf.Clamp(steps, -100, 100)), MinCameraZoom, MaxCameraZoom);
            ApplyCameraPose();
        }

        // Physical screen pixels, with +Y upwards, just like Input.mousePosition.
        public void PanCamera(Vector2 screenPixels)
        {
            if (!CanNavigate || float.IsNaN(screenPixels.x) || float.IsInfinity(screenPixels.x)
                || float.IsNaN(screenPixels.y) || float.IsInfinity(screenPixels.y)) return;
            if (dragging) CancelDrag();
            UpdateCameraViewport();
            float unitsPerPixel = 2 * cameraZoom / Mathf.Max(1, studioCamera.pixelHeight);
            cameraFocus -= (studioCamera.transform.right * screenPixels.x + studioCamera.transform.up * screenPixels.y) * unitsPerPixel;
            ApplyCameraPose();
        }

        void FrameCamera(Bounds bounds)
        {
            UpdateCameraViewport();
            cameraFocus = bounds.center;
            cameraZoom = FitCameraZoom(bounds, cameraFocus);
            ApplyCameraPose();
        }

        float FitCameraZoom(Bounds bounds, Vector3 pivot)
        {
            Vector3 right = cameraRotation * Vector3.right, up = cameraRotation * Vector3.up;
            Vector3 offset = bounds.center - pivot;
            float width = Mathf.Abs(Vector3.Dot(offset, right)) + ProjectedExtent(bounds.extents, right);
            float height = Mathf.Abs(Vector3.Dot(offset, up)) + ProjectedExtent(bounds.extents, up);
            float size = Mathf.Max(height, width / Mathf.Max(.01f, studioCamera.aspect)) * 1.15f + .5f;
            if (AdvancedCamera)
            {
                Vector3 forward = cameraRotation * Vector3.forward;
                size += (Mathf.Abs(Vector3.Dot(offset, forward)) + ProjectedExtent(bounds.extents, forward))
                    * Mathf.Tan(studioCamera.fieldOfView * .5f * Mathf.Deg2Rad);
            }
            return Mathf.Clamp(size, MinCameraZoom, MaxCameraZoom);
        }

        static float ProjectedExtent(Vector3 extent, Vector3 axis)
        {
            return Mathf.Abs(axis.x) * extent.x + Mathf.Abs(axis.y) * extent.y + Mathf.Abs(axis.z) * extent.z;
        }

        static Bounds StartBounds()
        {
            // The preserved template deck spans x=-6..6, z=-8..0, including its table and supports.
            return new Bounds(new Vector3(0, -2, -4), new Vector3(12, 7.2f, 8));
        }

        Bounds MapBounds()
        {
            var bounds = StartBounds();
            if (draft == null) return bounds;
            foreach (var item in draft.objects)
            {
                var itemBounds = ObjectBounds(item);
                bounds.Encapsulate(itemBounds);
                if (item.kind != UserMapPart.MovingPlatform) continue;
                bounds.Encapsulate(new Bounds(itemBounds.center + item.travel, itemBounds.size));
                bounds.Encapsulate(new Bounds(itemBounds.center - item.travel, itemBounds.size));
            }
            return bounds;
        }

        Bounds ObjectBounds(UserMapObject item)
        {
            var root = UserMapRuntime.Current ? UserMapRuntime.Current.RootFor(item.id) : null;
            var bounds = new Bounds();
            bool found = false;
            if (root)
            {
                root.GetComponentsInChildren(false, boundsRenderers);
                foreach (var renderer in boundsRenderers)
                {
                    if (!renderer.enabled) continue;
                    if (!found) { bounds = renderer.bounds; found = true; }
                    else bounds.Encapsulate(renderer.bounds);
                }
                boundsRenderers.Clear();
            }
            if (found) return bounds;
            Quaternion inverseRotation = Quaternion.Inverse(Quaternion.Euler(item.rotation));
            Vector3 half = item.scale * .5f;
            return new Bounds(item.position, new Vector3(
                ProjectedExtent(half, inverseRotation * Vector3.right),
                ProjectedExtent(half, inverseRotation * Vector3.up),
                ProjectedExtent(half, inverseRotation * Vector3.forward)) * 2);
        }

        void UpdateAdvancedCamera()
        {
            var angles = studioCamera.transform.eulerAngles;
            float pitch = angles.x > 180 ? angles.x - 360 : angles.x;
            pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * 2.2f, -89.5f, 89.5f);
            cameraRotation = Quaternion.Euler(pitch, angles.y + Input.GetAxisRaw("Mouse X") * 2.2f, 0);
            float forward = (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0);
            float right = (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0);
            float up = (Input.GetKey(KeyCode.E) ? 1 : 0) - (Input.GetKey(KeyCode.Q) ? 1 : 0);
            Vector3 direction = cameraRotation * new Vector3(right, 0, forward) + Vector3.up * up;
            Vector3 position = studioCamera.transform.position + Vector3.ClampMagnitude(direction, 1) * flySpeed
                * (Input.GetKey(KeyCode.LeftShift) ? 3 : 1) * Time.unscaledDeltaTime;
            cameraFocus = position + cameraRotation * Vector3.forward * CameraDistance;
            ApplyCameraPose();
        }

        void StopPointerInteraction()
        {
            StopCameraLook();
            if (dragging) CancelDrag();
            placementValid = false;
            lastSelectionId = null;
        }

        void OnDisable()
        {
            StopPointerInteraction();
            RestoreCameraFog();
        }

        // Only this editor camera ignores gameplay fog; never change the test camera's rendering.
        void BeforeStudioCameraRender(Camera camera)
        {
            if (camera != studioCamera || cameraFogOverride) return;
            cameraFogSaved = RenderSettings.fog;
            cameraFogOverride = true;
            RenderSettings.fog = false;
        }

        void AfterStudioCameraRender(Camera camera)
        {
            if (camera != studioCamera || !cameraFogOverride) return;
            RestoreCameraFog();
        }

        void RestoreCameraFog()
        {
            if (!cameraFogOverride) return;
            RenderSettings.fog = cameraFogSaved;
            cameraFogOverride = false;
        }
    }
}
