using HalliGalli.Movement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace HalliGalli.EditorTools
{
    public static class SurfBulkheadBuilder
    {
        public static void InstallCurrent()
        {
            var old=GameObject.Find("Surf bulkheads");if(old)Object.DestroyImmediate(old);
            var course=Object.FindAnyObjectByType<SurfCourse>();
            var root=new GameObject("Surf bulkheads").transform;root.SetParent(course.transform);
            // Upper entry is a room with one exit: the end of the first bank. No side/back escape.
            Wall(root,"Entry left bulkhead",new Vector3(-10,6.3f,7.125f),new Vector3(.8f,23.4f,46.25f));
            Wall(root,"Entry right bulkhead",new Vector3(10,6.3f,7.125f),new Vector3(.8f,23.4f,46.25f));
            Wall(root,"Entry rear bulkhead",new Vector3(0,6.3f,-16),new Vector3(20,.8f+22.6f,.8f));
            // A full-height sector wall blocks direct drops/flights into the late banks.
            Wall(root,"Late sector bulkhead",new Vector3(-9,-24.5f,44),new Vector3(126,85,1.2f));
            // The sole opening follows the actual outer curve, with generous surf headroom.
            Wall(root,"Curve portal header",new Vector3(-90,-2.5f,44),new Vector3(36,41,1.2f));
            Wall(root,"Curve portal sill",new Vector3(-90,-53.5f,44),new Vector3(36,27,1.2f));
            Wall(root,"East perimeter bulkhead",new Vector3(54,-24.5f,69),new Vector3(1,85,186));
            Wall(root,"West perimeter bulkhead",new Vector3(-108,-24.5f,69),new Vector3(1,85,186));
            Wall(root,"North perimeter bulkhead",new Vector3(-27,-24.5f,162),new Vector3(162,85,1));
            Wall(root,"South perimeter bulkhead",new Vector3(-27,-24.5f,-24),new Vector3(162,85,1));
            var roof=new GameObject("Surf collision ceiling").AddComponent<BoxCollider>();roof.transform.SetParent(root);
            roof.center=new Vector3(-27,18.5f,69);roof.size=new Vector3(162,1,186);
        }
        static void Wall(Transform parent,string name,Vector3 center,Vector3 size)
        {
            var wall=new GameObject(name).AddComponent<BoxCollider>();wall.transform.SetParent(parent);wall.transform.position=center;wall.size=size;
            // A single quiet visual keeps the entire blocking plane readable without a wall grid.
            SurfAppearanceBuilder.InstallBulkheadVisual(wall);
        }
    }
}
