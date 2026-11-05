using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace HalliGalli.Movement
{
    // Sample this frame's view direction before MeleeCombat handles the Q cast.
    [DefaultExecutionOrder(-10), DisallowMultipleComponent, RequireComponent(typeof(SourceMotor))]
    public sealed class SourcePlayerController : MonoBehaviour
    {
        public Camera playerCamera;
        public bool controlsEnabled = true;
        public bool captureCursorOnStart = true;
        [HideInInspector] public bool movementEnabled=true, actionsEnabled=true;
        [HideInInspector] public float cameraHeightOffset;
        public bool lookSuppressed{get;set;}
        public bool suppressCursorCapture{get;set;}
        public SourceMotor Motor { get; private set; }
        private MovementCommand command;
        private bool pendingJump, hasFocus = true;
        int pointerBlockedFrame=-1;
        public bool PointerActionsAllowed=>!ControlSettings.BlocksInput&&controlsEnabled&&hasFocus&&Cursor.lockState==CursorLockMode.Locked&&pointerBlockedFrame!=Time.frameCount;
        private float yaw, pitch;
        public Quaternion AimRotation => Quaternion.Euler(pitch,yaw,0);
        public float Pitch=>pitch;
        Vector3 networkViewOffset;
        public void SmoothNetworkCorrection(Vector3 oldPosition,bool teleport)
        {
            networkViewOffset=teleport?Vector3.zero:Vector3.ClampMagnitude(networkViewOffset+oldPosition-transform.position,1.5f);
        }

        private void Awake()
        {
            Motor = GetComponent<SourceMotor>();
            Motor.Initialize();
            yaw = transform.eulerAngles.y;
            if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>();
            if (playerCamera != null) playerCamera.fieldOfView = Motor.profile.fieldOfView;
        }

        private void Start()
        {
            if (captureCursorOnStart && !Application.isBatchMode) SetCursorCaptured(true);
        }

        private void Update()
        {
            ReadDevices(out Vector2 move, out Vector2 mouse, out bool crouch,
                out bool pressed, out bool held, out bool escape, out bool clicked);
            if (escape) SetCursorCaptured(false);
            else if (clicked && hasFocus && controlsEnabled && !suppressCursorCapture && !CourseNetwork.CapturesPointer) SetCursorCaptured(true);
            bool active = !ControlSettings.BlocksInput && controlsEnabled && hasFocus && Cursor.lockState == CursorLockMode.Locked && Time.timeScale > 0;
            if (active)
            {
                if(!lookSuppressed)ApplyLook(mouse);
                command = movementEnabled?new MovementCommand { move = move, crouch = crouch, jumpHeld = held, yaw = yaw }:new MovementCommand{yaw=yaw};
                pendingJump = movementEnabled && (pendingJump || pressed);
            }
            else
            {
                command = new MovementCommand { yaw = yaw };
                pendingJump = false;
            }
        }

        private void FixedUpdate()
        {
            command.jumpPressed = pendingJump;
            pendingJump = false;
            if(CourseNetwork.Active){CourseNetwork.Instance.LocalMovement(command,pitch);return;}
            if(movementEnabled)Motor.Simulate(command, Time.fixedDeltaTime);
        }

        private void LateUpdate() => UpdateView();

        public void UpdateView()
        {
            if (playerCamera == null) return;
            float fraction = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
            networkViewOffset=Vector3.Lerp(networkViewOffset,Vector3.zero,1-Mathf.Exp(-15*Time.deltaTime));
            playerCamera.transform.position = Vector3.Lerp(Motor.PreviousEyePosition, Motor.CurrentEyePosition, fraction)+Vector3.up*cameraHeightOffset+networkViewOffset;
            playerCamera.transform.rotation = AimRotation;
            float fov=Motor.profile.fieldOfView;
            playerCamera.fieldOfView=Mathf.Lerp(playerCamera.fieldOfView,fov,1-Mathf.Exp(-18*Time.deltaTime));
        }

        public void ApplyLook(Vector2 mouseDelta)
        {
            // Mouse deltas already describe this frame's movement; do not multiply by deltaTime.
            yaw = Mathf.Repeat(yaw + mouseDelta.x * Motor.profile.mouseSensitivity, 360);
            pitch = Mathf.Clamp(pitch - mouseDelta.y * Motor.profile.mouseSensitivity,
                -Motor.profile.pitchLimit, Motor.profile.pitchLimit);
            transform.rotation = Quaternion.Euler(0, yaw, 0);
        }

        public void ResetView(float yawDegrees = 0,float pitchDegrees=0)
        {
            pointerBlockedFrame=Time.frameCount;
            yaw = yawDegrees;
            pitch = Mathf.Clamp(pitchDegrees,-Motor.profile.pitchLimit,Motor.profile.pitchLimit);
            command.yaw = yaw;
            pendingJump = false;
        }

        public void SetCursorCaptured(bool captured)
        {
            if(!captured){pendingJump=false;command=new MovementCommand{yaw=yaw};if(Motor)Motor.ClearJumpBuffer();}
            if(!captured||Cursor.lockState!=CursorLockMode.Locked)pointerBlockedFrame=Time.frameCount;
            Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !captured;
        }

        private void OnApplicationFocus(bool focused)
        {
            hasFocus = focused;
            if (!focused) { pendingJump = false; command = new MovementCommand { yaw = yaw }; SetCursorCaptured(false); }
        }

        private void OnDisable(){SetCursorCaptured(false);}

        private static void ReadDevices(out Vector2 move, out Vector2 mouse,
            out bool crouch, out bool pressed, out bool held, out bool escape, out bool clicked)
        {
            mouse = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10;
            escape=Input.GetKeyDown(KeyCode.Escape);clicked=Input.GetMouseButtonDown(0);
            move=ControlBindings.Movement;
            crouch=ControlBindings.Held(GameAction.Crouch);
            pressed=ControlBindings.Pressed(GameAction.Jump);
            held=ControlBindings.Held(GameAction.Jump);
            if(ControlSettings.BlocksInput){mouse=Vector2.zero;escape=clicked=false;}
        }
    }
}
