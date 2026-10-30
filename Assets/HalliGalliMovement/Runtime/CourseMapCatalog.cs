using UnityEngine;

namespace HalliGalli.Movement
{
    [CreateAssetMenu(menuName="HalliGalli/Course Map Catalog",fileName="CourseMaps")]
    public sealed class CourseMapCatalog:ScriptableObject
    {
        public CourseMapDefinition[] maps;
        public bool Contains(CourseMapDefinition map)=>map&&maps!=null&&System.Array.IndexOf(maps,map)>=0;
    }
}
