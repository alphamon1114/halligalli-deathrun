using UnityEngine;

namespace HalliGalli.Movement
{
    public enum CourseMapStyle { Jump, Climb, Surf, Factory }

    [CreateAssetMenu(menuName="HalliGalli/Course Map",fileName="CourseMap")]
    public sealed class CourseMapDefinition:ScriptableObject
    {
        public string id,displayName;
        public CourseMapStyle style;
        [Range(1,5)] public int difficulty=1;
        [TextArea] public string description;
        public bool playable;
        [Tooltip("Full scene path included in the Windows build.")]
        public string scenePath;
        public string thumbnail;
        public SourceMovementProfile movementProfile;
        public string StyleLabel=>style==CourseMapStyle.Factory?"기계실":style==CourseMapStyle.Climb?"등산":style==CourseMapStyle.Surf?"서프":"점프";
        public string DifficultyLabel=>difficulty==1?"입문":difficulty==2?"보통":difficulty==3?"어려움":difficulty==4?"매우 어려움":"전문가";
    }
}
