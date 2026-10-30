using HalliGalli.Movement;
using UnityEngine;
using UnityEditor;
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
            bool side=size.x<size.z;float width=side?size.z:size.x,depth=side?size.x:size.z;
            int columns=Mathf.CeilToInt(width/12),rows=Mathf.CeilToInt(size.y/12);
            var rotate=Quaternion.Euler(0,side?90:0,0);
            for(int c=0;c<columns;c++)for(int r=0;r<rows;r++){
                var local=rotate*new Vector3(-width/2+width*(c+.5f)/columns,-size.y/2+size.y*(r+.5f)/rows,0);
                var visual=EnvironmentArtBuilder.Place("ConcreteWall",wall.transform,local,new Vector3(width/columns+.015f,size.y/rows+.015f,depth),rotate,"Bulkhead panel");
                foreach(var collider in visual.GetComponentsInChildren<Collider>())Object.DestroyImmediate(collider);
            }
            // Amber vertical joints make the blocking plane readable at speed.
            var material=AssetDatabase.LoadAssetAtPath<Material>(MovementProjectBuilder.Root+"/Materials/FactoryAmber.mat");
            for(int i=0;i<=columns;i++){
                var strip=GameObject.CreatePrimitive(PrimitiveType.Cube);strip.name="Bulkhead seam";strip.transform.SetParent(wall.transform,false);
                strip.transform.localPosition=rotate*new Vector3(-width/2+width*i/columns,0,0);
                strip.transform.localRotation=rotate;strip.transform.localScale=new Vector3(.10f,size.y,depth+.035f);
                strip.GetComponent<Renderer>().sharedMaterial=material;Object.DestroyImmediate(strip.GetComponent<Collider>());
            }
        }
    }
}
