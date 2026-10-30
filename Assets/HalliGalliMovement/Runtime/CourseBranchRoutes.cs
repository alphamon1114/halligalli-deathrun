using System;
using UnityEngine;

namespace HalliGalli.Movement
{
    // Canonical course waypoints; each participant keeps its own two entrance nodes.
    public sealed class CourseBranchRoutes : MonoBehaviour
    {
        public Transform[] left, right;
        public Transform summit;
        public bool sharedSpiral;
        public Vector3 peakCenter;
        [Serializable] public sealed class Shortcut { public int entryWaypoint,exitWaypoint; public Transform landing; }
        public Shortcut[] shortcuts=Array.Empty<Shortcut>();
        public Shortcut ShortcutAfter(int waypoint){foreach(var point in shortcuts)if(point.entryWaypoint==waypoint)return point;return null;}
        public int RouteLength(int seat,int count)=>(sharedSpiral||seat<(count+1)/2?left:right).Length+3;

        public void Apply(Transform[] route, int seat, int count)
        {
            var branch = sharedSpiral || seat < (count + 1) / 2 ? left : right;
            if (branch == null || route.Length != branch.Length + 3 || !summit)
                throw new InvalidOperationException("The split course needs two entrance nodes, a branch and a shared summit.");
            for (int i = 0; i < branch.Length; i++) {
                route[i + 2].position = branch[i].position;
                var source=branch[i].GetComponent<ClimbStep>();var target=route[i+2].GetComponent<ClimbStep>();
                if(source&&target){target.jump=source.jump;target.expert=source.expert;target.arrivalRadius=source.arrivalRadius;}
            }
            route[route.Length - 1].position = summit.position;
            var last=route[route.Length-1].GetComponent<ClimbStep>();if(last){last.jump=ClimbJump.Basic;last.expert=false;last.arrivalRadius=.85f;}
        }
    }
}
