using System;
using System.Collections.Generic;
using UnityEngine;

namespace HalliGalli.Movement
{
    // Route data guides AI inputs and records passage. It never adds forces or rotates a human camera.
    [DefaultExecutionOrder(80)]
    public sealed class SurfCourse : MonoBehaviour
    {
        [Serializable]
        public sealed class Section
        {
            public Vector3[] points;
            public int side=-1;
            public bool twoSided;
            public float[] headings;
            public Vector3 entry, landing;
            public string landingName;
            public float halfWidth = 3;
            public float Length {
                get { float length = 0; for (int i = 1; i < points.Length; i++) length += Flat(points[i] - points[i - 1]).magnitude; return length; }
            }
            public Frame Sample(Vector3 position)
            {
                float best = float.PositiveInfinity, distance = 0; var frame = new Frame();
                for (int i = 0; i < points.Length - 1; i++) {
                    Vector3 delta = Flat(points[i + 1] - points[i]); float length = delta.magnitude;
                    float t = Vector3.Dot(Flat(position - points[i]), delta) / delta.sqrMagnitude;
                    float clamped = Mathf.Clamp01(t);
                    Vector3 center = Vector3.Lerp(points[i], points[i + 1], clamped);
                    float squared = Flat(position - center).sqrMagnitude;
                    if (squared < best) {
                        best = squared;
                        Vector3 forward = Quaternion.Euler(0, Mathf.LerpAngle(headings[i], headings[i + 1], clamped), 0) * Vector3.forward;
                        frame = new Frame { center = center, forward = forward, right = Vector3.Cross(Vector3.up, forward),
                            distance = distance + ((i == 0 && t < 0 || i == points.Length - 2 && t > 1) ? t : clamped) * length };
                    }
                    distance += length;
                }
                return frame;
            }
        }
        public struct Frame { public Vector3 center, forward, right; public float distance; }
        public sealed class Navigation { public int section, phase, bankSide; public void Reset() { section = phase = bankSide = 0; } }
        public struct Input { public Vector3 steering, forward; public bool jump; }
        sealed class Progress { public int respawn = -1, gates, contacts; }
        readonly Dictionary<CourseParticipant, Progress> progress = new Dictionary<CourseParticipant, Progress>();
        CourseRoster roster;
        public Section[] sections;
        public Transform finish;
        public float laneX = 3.2f;
        static Vector3 Flat(Vector3 v) => Vector3.ProjectOnPlane(v, Vector3.up);

        public void Apply(Transform[] route, int seat, int count)
        {
            float side = sections[0].twoSided ? (seat%2==0?-1:1) : sections[0].side;
            route[2].position = new Vector3(side * laneX, .375f, 6.1f);
            route[3].position = sections[0].points[0] + new Vector3(side * laneX, .3f, 1);
            route[4].position = finish.position;
        }
        Progress State(CourseParticipant p)
        {
            if (!progress.TryGetValue(p, out var state)) progress[p] = state = new Progress();
            if (p.IsSeated || state.respawn != p.RespawnSequence) { state.respawn = p.RespawnSequence; state.gates = state.contacts = 0; }
            return state;
        }
        public int GatesPassed(CourseParticipant p) => State(p).gates;
        public bool Completed(CourseParticipant p) => GatesPassed(p) == sections.Length;
        public void ApplyNetworkProgress(CourseParticipant p,int gates){State(p).gates=gates;}
        public void RestoreCheckpointProgress(CourseParticipant p,int gates){var state=State(p);state.gates=Mathf.Clamp(gates,0,sections.Length);state.contacts=0;}
        void FixedUpdate()
        {
            if(CourseNetwork.Active&&!CourseNetwork.Authority)return;
            if (!roster) roster = FindAnyObjectByType<CourseRoster>();
            if (!roster || sections == null) return;
            foreach (var p in roster.allParticipants) {
                if (!p || !p.isActiveAndEnabled) continue;
                var state = State(p);
                if (p.IsSeated || p.IsRespawning || state.gates >= sections.Length) continue;
                var section = sections[state.gates]; var frame = section.Sample(p.transform.position);
                Vector3 offset = p.transform.position - frame.center;
                float rawLateral = Vector3.Dot(offset, frame.right);
                float lateral = section.twoSided ? Mathf.Abs(rawLateral) : rawLateral * section.side;
                bool onRoute = lateral > laneX-section.halfWidth-.4f && lateral < laneX+section.halfWidth+.4f && offset.y > -5.2f && offset.y < 5.8f;
                if (onRoute && p.Motor.Surfing && frame.distance > 0 && frame.distance < section.Length + .5f) state.contacts++;
                if (onRoute && state.contacts >= 5 && frame.distance >= section.Length - .65f && frame.distance <= section.Length + 3) {
                    state.gates++; state.contacts = 0;
                }
            }
        }
        public Input Guide(CourseParticipant p, Transform[] route, Navigation nav, float wideTurn = 0, int preferredSide = 0)
        {
            Vector3 position = p.transform.position; var motor = p.Motor;
            var section = sections[nav.section]; var frame = section.Sample(position);
            float side = RideSide(section,frame,position,nav,preferredSide);
            if (nav.phase == 0 && frame.distance >= -.2f) nav.phase = 1;
            if (nav.phase == 1 && frame.distance > section.Length + .05f) {
                if (section.landing==Vector3.zero && nav.section<sections.Length-1) {
                    nav.section++;section=sections[nav.section];frame=section.Sample(position);side=RideSide(section,frame,position,nav,preferredSide);
                } else nav.phase=2;
            }
            if (nav.phase == 2 && nav.section<sections.Length-1) {
                Vector3 landing=section.landing;
                if(nav.section==0)landing+=Vector3.forward*wideTurn;
                if(motor.Grounded&&Mathf.Abs(position.y-landing.y)<.4f){
                    if(wideTurn>0&&nav.section==0&&Flat(landing-position).magnitude>.4f)
                        return new Input{steering=Flat(landing-position).normalized,forward=Flat(landing-position).normalized};
                    nav.section++;nav.phase=0;section=sections[nav.section];frame=section.Sample(position);side=RideSide(section,frame,position,nav,preferredSide);
                }else return new Input{steering=nav.section==0?AirToward(p,landing):LandingInput(p,landing,frame),forward=frame.forward};
            }
            if (nav.phase == 2) {
                Vector3 delta = Flat(finish.position - position);
                float lateral = Vector3.Dot(position - frame.center, frame.right);
                return new Input { steering = motor.Grounded ? delta.normalized : LandingInput(p,finish.position,frame), forward = frame.forward };
            }
            if (nav.phase == 0) {
                Vector3 entry = section.entry + frame.right * side * laneX;
                Vector3 approach = Flat(entry-position);
                // Once past the alignment point, keep going; a distance-only test pulls bots back to it.
                bool align = motor.Grounded && (Vector3.Dot(approach,frame.forward)>.2f || Mathf.Abs(Vector3.Dot(approach,frame.right))>.55f);
                Vector3 target = align ? entry : section.points[0] + frame.right * side * laneX + frame.forward;
                Vector3 delta = Flat(target - position);
                bool gap = motor.Grounded && delta.magnitude > 1 && !Physics.Raycast(position + Vector3.up * .5f + delta.normalized * .72f, Vector3.down, 1.05f, ~((1 << 8) | (1 << 9)), QueryTriggerInteraction.Ignore);
                return new Input { steering = motor.Grounded ? delta.normalized : AirToward(p, target), forward = frame.forward, jump = gap };
            }
            // Bank-relative air strafing; the curve and gravity supply the launch velocity.
            float offset = Vector3.Dot(position - frame.center, frame.right);
            float strafe = Mathf.Clamp((side * laneX - offset) * 5 - Vector3.Dot(motor.Velocity, frame.right) * 1.5f, -1, 1);
            return new Input { steering = frame.right * strafe, forward = frame.forward };
        }
        static int RideSide(Section section,Frame frame,Vector3 position,Navigation nav,int preferred)
        {
            if(!section.twoSided)return section.side;
            if(nav.bankSide==0){
                float lateral=Vector3.Dot(position-frame.center,frame.right);
                nav.bankSide=preferred!=0?(preferred<0?-1:1):Mathf.Abs(lateral)>.4f?(lateral<0?-1:1):section.side;
            }
            return nav.bankSide;
        }
        static Vector3 AirToward(CourseParticipant p, Vector3 target) => Vector3.ClampMagnitude(Flat(target - p.transform.position) * 5.5f, p.Motor.profile.moveSpeed) - Flat(p.Motor.Velocity);
        static Vector3 LandingInput(CourseParticipant p,Vector3 target,Frame frame)
        {
            var delta=Flat(target-p.transform.position);
            if(Vector3.Dot(delta,frame.forward)<2)return AirToward(p,target);
            return frame.right*Mathf.Clamp(Vector3.Dot(delta,frame.right)*5-Vector3.Dot(p.Motor.Velocity,frame.right)*1.5f,-1,1);
        }
    }
}
