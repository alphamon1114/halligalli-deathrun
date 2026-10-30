using UnityEngine;

namespace HalliGalli.Movement
{
    [RequireComponent(typeof(SourceMotor), typeof(MeleeReceiver))]
    public sealed class TrainingOpponent : MonoBehaviour
    {
        public Transform visual;
        public TextMesh label;
        SourceMotor motor; MeleeReceiver receiver; Vector3 spawn; Quaternion rest; bool returning; float yaw;
        void Start() { motor = GetComponent<SourceMotor>(); receiver = GetComponent<MeleeReceiver>(); spawn = transform.position; rest = visual.localRotation; yaw=transform.eulerAngles.y; }
        void FixedUpdate()
        {
            motor.Simulate(new MovementCommand{yaw=yaw}, Time.fixedDeltaTime);
            if (receiver.LastHitTime > 0 && Time.time - receiver.LastHitTime < 3) returning = true;
            if (transform.position.y < -3 || (returning && Time.time - receiver.LastHitTime > 3)) ResetTarget();
        }
        void LateUpdate()
        {
            if (label) { label.text = "TRAINING / " + receiver.ReceivedHits; var camera = Camera.main; if (camera) label.transform.rotation = Quaternion.LookRotation(label.transform.position - camera.transform.position); }
        }
        public void ResetTarget() { if (motor) motor.Teleport(spawn); if(receiver)receiver.ResetReaction();returning = false; }
    }
}
