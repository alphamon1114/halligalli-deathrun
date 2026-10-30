using System;
using UnityEngine;

namespace HalliGalli.Movement
{
    public enum UserMapPart { Block, Ramp, Surf, Checkpoint, MovingPlatform, Conveyor, Press, Hazard, Bell, Waypoint, ItemBox = 10 }

    [Serializable]
    public sealed class UserMapObject
    {
        public const float ItemBoxCenterHeight = 1.05f;
        // Includes the floor anchor, crate trim, and the full bob/spin envelope during play.
        public static readonly Vector3 ItemBoxBoundsCenter = new Vector3(0, .8f, 0);
        public static readonly Vector3 ItemBoxBoundsSize = new Vector3(1.28f, 1.6f, 1.28f);

        public string id;
        public UserMapPart kind;
        public Vector3 position, rotation;
        public Vector3 scale = Vector3.one;
        public int color;
        public Vector3 travel;
        public float period = 4, speed = 2;
    }

    [Serializable]
    public sealed class UserMapData
    {
        public int version = 1;
        public string id, name, description;
        public float fallHeight = -20;
        public UserMapObject[] objects;
    }
}
