using System;
using UnityEngine;

namespace HalliGalli.Movement
{
    public enum UserMapPart { Block, Ramp, Surf, Checkpoint, MovingPlatform, Conveyor, Press, Hazard, Bell, Waypoint }

    [Serializable]
    public sealed class UserMapObject
    {
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
