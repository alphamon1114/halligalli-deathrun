#if UNITY_EDITOR
using System;
using HalliGalli.Movement;
using UnityEngine;

// Replace device sampling, not movement or lesson logic. PracticeSurfArea still runs
// at order 90, after this normal SourceMotor step and before the runner's observer.
[DefaultExecutionOrder(0)]
public sealed class PracticeSurfValidationDriver : MonoBehaviour
{
    public enum Mode { Idle, Traverse, WalkOff, Crouch }
    public CourseParticipant player;
    public CombatPracticeRoom room;
    public PracticeSurfGeometry.Stage stage;
    public Mode mode;
    public MovementCommand lastCommand;
    public string error;
    public int simulatedTicks;

    void FixedUpdate()
    {
        if (!player || !room || !room.Active || CourseNetwork.CapturesPointer ||
            player.IsSeated || player.IsRespawning || error != null) return;
        try {
            lastCommand = Command();
            player.Motor.Simulate(lastCommand, Time.fixedDeltaTime);
            simulatedTicks++;
        } catch (Exception exception) {
            error = exception.ToString();
        }
    }

    MovementCommand Command()
    {
        if (mode == Mode.Crouch) return new MovementCommand { crouch = true };
        if (mode == Mode.WalkOff) return new MovementCommand { move = Vector2.left };
        if (mode != Mode.Traverse) return default;

        Vector3 position = player.transform.position;
        var motor = player.Motor;
        // Same bank-relative PD steering used by the official SurfCourse guide.
        // Stage starts define lane centers x=30.5, 52.5, 74.5. No velocity writes,
        // jump assistance, teleportation, checkpoint/progress writes or tuning.
        float right = Mathf.Clamp((stage.start.x - position.x) * 5 - motor.Velocity.x * 1.5f, -1, 1);
        if (position.z < 0)
            return new MovementCommand { move = Vector2.ClampMagnitude(new Vector2(right, 1), 1) };
        if (motor.Grounded) {
            Vector3 delta = stage.finish.center - position;
            return new MovementCommand { move = Vector2.ClampMagnitude(new Vector2(delta.x, delta.z), 1) };
        }
        // W is released past the run-in. D/A correction rides the bank and
        // continues through the real airborne gap until the receiving contact.
        return new MovementCommand { move = new Vector2(right, 0) };
    }
}
#endif
