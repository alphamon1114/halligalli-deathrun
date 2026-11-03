using System;
using UnityEngine;

namespace HalliGalli.Movement
{
    [DefaultExecutionOrder(90)]
    public sealed class CourseCheckpoints : MonoBehaviour
    {
        [Serializable]
        public sealed class Point
        {
            public Transform spawn;
            public BoxCollider landing;
            public int resumeWaypoint, surfGates;
            public bool surfLanes;
        }
        public CourseSession session;
        public Point[] points = Array.Empty<Point>();
        public bool orderedPassage;

        public Point Saved(CourseParticipant participant) => participant.CheckpointIndex >= 0 && participant.CheckpointIndex < points.Length ? points[participant.CheckpointIndex] : null;
        public Vector3 SpawnPosition(CourseParticipant participant)
        {
            var point = Saved(participant);
            return point.spawn.position + (point.surfLanes ? Vector3.right * participant.CheckpointSide * session.roster.surf.laneX : Vector3.zero);
        }
        void FixedUpdate()
        {
            if (CourseNetwork.Active && !CourseNetwork.Authority || !session.RunActive) return;
            foreach (var participant in session.participants) {
                if (participant.IsSeated || participant.IsRespawning || session.MustReturn(participant)) continue;
                if (orderedPassage) {
                    // The host records passage over the next plaza, not contact with every surf bank.
                    int next = participant.CheckpointIndex + 1;
                    if (next >= points.Length) continue;
                    var point = points[next];
                    if (!point.spawn || !point.landing || !point.landing.enabled) continue;
                    var bounds = point.landing.bounds;
                    var position = participant.transform.position;
                    if (position.x < bounds.min.x || position.x > bounds.max.x || position.z < bounds.min.z || position.z > bounds.max.z || position.y < bounds.max.y - .15f) continue;
                    participant.SetCheckpoint(next, 0);
                    RestoreSurfProgress(participant);
                    continue;
                }
                if (!participant.Motor.Grounded) continue;
                for (int index = 0; index < points.Length; index++) {
                    if (participant.CheckpointIndex == index) continue;
                    var point = points[index];
                    if (!point.spawn || !point.landing || !point.landing.enabled) continue;
                    if (point.surfGates > 0 && (!session.roster.surf || session.roster.surf.GatesPassed(participant) < point.surfGates)) continue;
                    var bounds = point.landing.bounds;var position = participant.transform.position;
                    if (position.x < bounds.min.x || position.x > bounds.max.x || position.z < bounds.min.z || position.z > bounds.max.z || Mathf.Abs(position.y - bounds.max.y) > .15f) continue;
                    // Airborne passage and contact with the side of the ledge do not save a checkpoint.
                    if (!Physics.Raycast(position + Vector3.up * .12f, Vector3.down, out var hit, .3f, ~((1 << 8) | (1 << 9)), QueryTriggerInteraction.Ignore) || hit.collider != point.landing) continue;
                    participant.SetCheckpoint(index, point.surfLanes ? (position.x < 0 ? -1 : 1) : 0);
                }
            }
        }
        public void RestoreSurfProgress(CourseParticipant participant)
        {
            if (session.roster && session.roster.surf)
                session.roster.surf.RestoreCheckpointProgress(participant, participant.HasCheckpoint ? Saved(participant).surfGates : 0);
        }
    }
}
