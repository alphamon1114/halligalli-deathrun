using UnityEngine;

namespace HalliGalli.Movement
{
    public sealed partial class UserMapStudio
    {
        public bool FirstPersonBuild { get; private set; } = true;
        public bool BuildCursorCaptured { get; private set; }
        public bool BuildNoclip { get; private set; } = true;
        public int BuildFlySpeedIndex { get; private set; } = 1;
        public bool BuildPawnActive => buildMotor && buildBody && buildBody.enabled && FirstPersonBuild && !BuildNoclip && !testing;
        public Vector3 BuildPawnPosition => BuildPawnActive ? buildMotor.transform.position : buildPosition - Vector3.up * BuildEyeHeight;
        public Vector3 BuildPawnVelocity => buildMotor ? buildMotor.Velocity : Vector3.zero;
        public bool BuildPawnGrounded => BuildPawnActive && buildMotor.Grounded;
        public SourceMovementProfile BuildMovementProfile => buildProfile;
        public Ray BuildAimRay => studioCamera
            ? studioCamera.ScreenPointToRay(BuildCursorCaptured ? (Vector3)studioCamera.pixelRect.center : Input.mousePosition)
            : default;

        SourceMovementProfile buildProfile;
        SourceMotor buildMotor;
        CharacterController buildBody;
        Vector3 buildPosition, buildStartFeet;
        Quaternion buildRotation = Quaternion.identity;
        float buildStartYaw;
        bool buildPoseInitialized, buildPendingJump, buildJumpHeld, buildShortcutHeld;
        int buildPointerBlockedFrame = -1;
        MovementCommand buildCommand;
        readonly Collider[] buildClearanceHits = new Collider[32];

        bool BuildMenuOpen => partsMenuOpen || fileMenuOpen || libraryOpen || helpOpen || mapInspector || easyContextOpen
            || pendingAction != null || UserMapStudioFileDialog.IsOpen || ControlSettings.BlocksInput;
        float BuildFlySpeed => BuildFlySpeedIndex == 0 ? 3 : BuildFlySpeedIndex == 2 ? 18 : 8;
        float BuildEyeHeight => buildProfile ? buildProfile.standingEyeHeight : 1.65f;

        void BindBuildNavigation(CourseSession game, SourcePlayerController controller)
        {
            var motor = controller ? controller.GetComponent<SourceMotor>() : null;
            buildProfile = motor && motor.profile ? motor.profile : UserMapRuntime.Current ? UserMapRuntime.Current.movementProfile : null;
            var start = game && game.Local ? game.Local.standAnchor : null;
            buildStartFeet = start ? start.position : new Vector3(0, .025f, -1);
            buildStartYaw = start ? start.eulerAngles.y : 0;
        }

        void InitializeFirstPersonPose()
        {
            if (!buildPoseInitialized)
            {
                buildPosition = buildStartFeet + Vector3.up * BuildEyeHeight;
                buildRotation = Quaternion.Euler(0, buildStartYaw, 0);
                buildPoseInitialized = true;
                if (!placementMode)
                {
                    placementMode = true;
                    CreatePlacementTemplate();
                }
            }
            ApplyFirstPersonPose();
            if (!BuildNoclip && !ActivateBuildWalking()) BuildNoclip = true;
        }

        void ApplyFirstPersonPose()
        {
            if (!studioCamera) return;
            studioCamera.orthographic = false;
            studioCamera.fieldOfView = buildProfile ? buildProfile.fieldOfView : 85;
            studioCamera.transform.SetPositionAndRotation(buildPosition, buildRotation);
        }

        public void SetFirstPersonBuild(bool enabled)
        {
            if (!CanNavigate || FirstPersonBuild == enabled) return;
            RememberBuildPose();
            StopPointerInteraction();
            FirstPersonBuild = enabled;
            if (enabled) InitializeFirstPersonPose();
            else
            {
                DisableBuildPawn();
                AdvancedCamera = false;
                cameraRotation = ViewRotation(CurrentStudioView);
                FrameCamera(MapBounds());
            }
            GUIUtility.keyboardControl = 0;
        }

        public void SetBuildCursorCaptured(bool captured)
        {
            if (captured && (!CanNavigate || !FirstPersonBuild || ControlSettings.BlocksInput)) return;
            if (captured)
            {
                partsMenuOpen = fileMenuOpen = easyContextOpen = mapInspector = false;
                GUIUtility.keyboardControl = 0;
                UpdateCameraViewport();
                if (!BuildNoclip && !BuildPawnActive && !ActivateBuildWalking()) BuildNoclip = true;
            }
            bool wasCaptured = BuildCursorCaptured;
            if (BuildCursorCaptured != captured) buildPointerBlockedFrame = Time.frameCount;
            BuildCursorCaptured = captured;
            if (captured && !wasCaptured) buildJumpHeld = Input.GetKey(KeyCode.Space);
            if (!captured)
            {
                buildCommand = default;
                buildPendingJump = buildJumpHeld = buildShortcutHeld = false;
                if (wasCaptured) FreezeBuildMotor();
            }
            if (testing && !busy) return;
            Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !captured;
        }

        public void SetBuildFlySpeed(int index)
        {
            if (!CanNavigate || index < 0 || index > 2) return;
            BuildFlySpeedIndex = index;
        }

        public void SetBuildNoclip(bool enabled)
        {
            if (!CanNavigate || !FirstPersonBuild || BuildNoclip == enabled) return;
            buildCommand = default;
            buildPendingJump = false;
            if (enabled)
            {
                DisableBuildPawn();
                BuildNoclip = true;
                Status = "자유 비행 · V로 걷기/점프 · Space 위로 · Ctrl 아래로";
            }
            else if (ActivateBuildWalking())
            {
                BuildNoclip = false;
                Status = "걷기/점프 · 실제 이동 물리로 간격을 확인하세요. 클리어 인증은 F8에서만 가능해요.";
            }
            else Status = "몸이 들어갈 빈 공간으로 이동한 뒤 V를 누르세요. 걷기는 벽이나 발판 안에서 시작할 수 없어요.";
        }

        bool EnsureBuildPawn()
        {
            if (buildMotor) return true;
            if (!buildProfile) return false;
            // Inactive creation assigns the exact player profile before SourceMotor.Awake initializes its capsule.
            var pawn = new GameObject("Studio walking preview") { hideFlags = HideFlags.DontSave, layer = 2 };
            pawn.SetActive(false);
            buildBody = pawn.AddComponent<CharacterController>();
            buildBody.enabled = false;
            buildMotor = pawn.AddComponent<SourceMotor>();
            buildMotor.profile = buildProfile;
            pawn.SetActive(true);
            buildMotor.Initialize();
            buildBody.enabled = false;
            return true;
        }

        bool BuildCapsuleClear(Vector3 feet)
        {
            if (!buildProfile || !buildBody) return false;
            Physics.SyncTransforms();
            float radius = Mathf.Max(.05f, buildProfile.radius - buildProfile.skinWidth);
            int count = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * buildProfile.radius,
                feet + Vector3.up * (buildProfile.standingHeight - buildProfile.radius), radius,
                buildClearanceHits, buildProfile.collisionMask, QueryTriggerInteraction.Ignore);
            if (count == buildClearanceHits.Length) return false;
            for (int i = 0; i < count; i++)
            {
                var collider = buildClearanceHits[i];
                if (collider && collider != buildBody && !Physics.GetIgnoreLayerCollision(2, collider.gameObject.layer)
                    && !Physics.GetIgnoreCollision(buildBody, collider)) return false;
            }
            return true;
        }

        bool ActivateBuildWalking()
        {
            if (!EnsureBuildPawn()) return false;
            Vector3 feet = buildPosition - Vector3.up * BuildEyeHeight;
            if (!BuildCapsuleClear(feet)) return false;
            buildBody.enabled = false;
            buildBody.height = buildProfile.standingHeight;
            buildBody.center = Vector3.up * buildBody.height * .5f;
            buildMotor.Teleport(feet);
            buildMotor.ClearStun();
            buildMotor.transform.rotation = Quaternion.Euler(0, buildRotation.eulerAngles.y, 0);
            buildCommand = new MovementCommand { yaw = buildRotation.eulerAngles.y };
            buildPendingJump = false;
            buildJumpHeld = Input.GetKey(KeyCode.Space);
            return true;
        }

        void FreezeBuildMotor()
        {
            if (!buildMotor) return;
            var state = buildMotor.CaptureNetworkState();
            state.velocity = Vector3.zero;
            state.buffer = -1;
            buildMotor.RestoreNetworkState(state);
        }

        void DisableBuildPawn()
        {
            FreezeBuildMotor();
            if (buildBody) buildBody.enabled = false;
            buildCommand = default;
            buildPendingJump = false;
            buildJumpHeld = false;
        }

        void DestroyBuildPawn()
        {
            DisableBuildPawn();
            if (buildMotor)
            {
                buildMotor.gameObject.SetActive(false);
                Destroy(buildMotor.gameObject);
            }
            buildMotor = null;
            buildBody = null;
        }

        // Look is in mouse-device units (as in SourcePlayerController.ApplyLook); movement is local X/Z,
        // with Y for noclip elevation or negative Y for walking crouch. Walking queues the native fixed tick.
        public void NavigateBuild(Vector2 lookDelta, Vector3 move, float dt, bool jump)
        {
            if (!CanNavigate || !FirstPersonBuild || !BuildCursorCaptured || BuildMenuOpen
                || !float.IsFinite(dt) || dt <= 0 || !FiniteBuildVector(move)
                || !float.IsFinite(lookDelta.x) || !float.IsFinite(lookDelta.y)) return;
            var angles = buildRotation.eulerAngles;
            float sensitivity = (buildProfile ? buildProfile.mouseSensitivity : .12f) * ControlSettings.MouseSensitivityMultiplier;
            float limit = buildProfile ? buildProfile.pitchLimit : 88;
            buildRotation = Quaternion.Euler(Mathf.Clamp(Mathf.DeltaAngle(0, angles.x) - lookDelta.y * sensitivity, -limit, limit),
                Mathf.Repeat(angles.y + lookDelta.x * sensitivity, 360), 0);
            if (BuildNoclip)
            {
                Vector3 direction = buildRotation * new Vector3(move.x, 0, move.z) + Vector3.up * move.y;
                buildPosition += Vector3.ClampMagnitude(direction, 1) * BuildFlySpeed * Mathf.Min(dt, .1f);
            }
            else if (BuildPawnActive)
            {
                buildCommand = new MovementCommand { move = new Vector2(move.x, move.z), crouch = move.y < 0,
                    jumpHeld = jump, yaw = buildRotation.eulerAngles.y };
                buildPendingJump |= jump && !buildJumpHeld;
            }
            buildJumpHeld = jump;
            ApplyFirstPersonPose();
        }

        static bool FiniteBuildVector(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        void FixedUpdate() => SimulateBuildWalking(Time.fixedDeltaTime);

        // Diagnostics and live input advance the same native motor; never a participant or clear attempt.
        public void SimulateBuildWalking(float dt)
        {
            if (!BuildPawnActive || !CanNavigate || !BuildCursorCaptured || BuildMenuOpen || Time.timeScale <= 0
                || !float.IsFinite(dt) || dt <= 0) return;
            buildCommand.jumpPressed = buildPendingJump;
            buildPendingJump = false;
            buildMotor.Simulate(buildCommand, dt);
            buildPosition = buildMotor.CurrentEyePosition;
            if (!FiniteBuildVector(buildPosition) || buildMotor.transform.position.y < draft.fallHeight)
                RecoverBuildFall();
            ApplyFirstPersonPose();
        }

        void UpdateFirstPersonCamera()
        {
            if (!CanEdit || !studioCamera || !FirstPersonBuild) return;
            if (BuildPawnActive && BuildCursorCaptured && !BuildMenuOpen)
            {
                float fraction = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
                studioCamera.transform.SetPositionAndRotation(Vector3.Lerp(buildMotor.PreviousEyePosition, buildMotor.CurrentEyePosition, fraction), buildRotation);
            }
            else ApplyFirstPersonPose();
        }

        void RememberBuildPose()
        {
            if (!FirstPersonBuild || !studioCamera) return;
            buildPosition = studioCamera.transform.position;
            buildRotation = studioCamera.transform.rotation;
        }

        void RecoverBuildFall()
        {
            DisableBuildPawn();
            buildPosition = buildStartFeet + Vector3.up * BuildEyeHeight;
            buildRotation = Quaternion.Euler(0, buildStartYaw, 0);
            if (!ActivateBuildWalking()) BuildNoclip = true;
            Status = "편집 중 추락해 출발점으로 돌아왔어요. 클리어 인증에는 영향을 주지 않아요.";
        }

        void FrameFirstPerson(Bounds bounds)
        {
            DisableBuildPawn();
            buildRotation = Quaternion.Euler(20, buildRotation.eulerAngles.y, 0);
            float distance = Mathf.Clamp(bounds.extents.magnitude * 1.5f + 2, 3, 60);
            buildPosition = bounds.center - buildRotation * Vector3.forward * distance;
            if (!BuildNoclip && !ActivateBuildWalking()) BuildNoclip = true;
            ApplyFirstPersonPose();
        }
    }
}
