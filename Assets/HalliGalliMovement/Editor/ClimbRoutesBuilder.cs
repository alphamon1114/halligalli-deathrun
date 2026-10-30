using System;
using System.Collections.Generic;
using System.Linq;
using HalliGalli.Movement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace HalliGalli.EditorTools
{
    public static class ClimbRoutesBuilder
    {
        public static readonly Vector3 PeakCenter=new Vector3(0,0,15.5f);
        public static void Configure()
        {
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var session=Object.FindAnyObjectByType<CourseSession>();var roster=session.roster;
            var previous=GameObject.Find("Summit climbing course");if(previous)Object.DestroyImmediate(previous);
            foreach(var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)){
                if(!t)continue;
                if((t.name.StartsWith("Shared pad ")||t.name=="Edge stripe"||t.name=="Platform support")&&t.position.z>7
                    ||t.name=="Wall buttress"||new[]{"03","04","05","06","07"}.Contains(t.name))Object.DestroyImmediate(t.gameObject);
            }
            var root=new GameObject("Summit climbing course").transform;
            var course=root.gameObject.AddComponent<CourseBranchRoutes>();roster.branches=course;roster.surf=null;
            course.sharedSpiral=true;course.peakCenter=PeakCenter;
            var rock=AssetDatabase.LoadAssetAtPath<Material>(MovementProjectBuilder.Root+"/Materials/ClimbRock.mat");
            const int count=11;
            // Deliberately uneven cliff ledges: broad regrouping shelves alternate with
            // shallow landings, diagonal transfers and exposed corner jumps.
            float[] angles={0,32,63,103,135,178,211,247,285,324,360};
            float[] radii={6.5f,6.3f,6.4f,5.8f,6,5.4f,5.2f,5.4f,4.75f,4.9f,4.4f};
            float[] heights={1.1f,1.65f,2.1f,2.8f,3.05f,3.6f,4.3f,4.65f,5.3f,5.75f,6.15f};
            float[] widths={4.8f,2.5f,3.4f,2.1f,3.5f,3.8f,2.2f,3.1f,2.1f,3.4f,3.6f};
            float[] depths={2,1.4f,1.05f,1.5f,1.05f,2.2f,1.35f,1,1.5f,1.05f,1.9f};
            float[] twists={0,8,80,-20,12,0,-15,-70,-25,65,0};
            var route=new Transform[count];
            for(int i=0;i<count;i++){
                float angle=-angles[i]*Mathf.Deg2Rad,radius=radii[i];
                var radial=new Vector3(Mathf.Sin(angle),0,-Mathf.Cos(angle));
                var point=PeakCenter+radial*radius+Vector3.up*heights[i];
                var tangent=new Vector3(-Mathf.Cos(angle),0,-Mathf.Sin(angle));
                var rotation=i==0||i==count-1?Quaternion.identity:Quaternion.LookRotation(tangent)*Quaternion.Euler(0,twists[i],0);
                var surface=Landing(root,"M"+(i+1).ToString("00"),point,new Vector3(widths[i],.45f,depths[i]),rotation,rock);
                route[i]=Node(root,"M"+(i+1).ToString("00"),point+Vector3.up*.025f);
                route[i].gameObject.AddComponent<ClimbStep>().arrivalRadius=Mathf.Min(.65f,depths[i]*.4f);
                Edge(surface,new Color(.48f,.52f,.46f));
            }
            course.left=course.right=route;
            var passing=new List<CourseBranchRoutes.Shortcut>();
            foreach(int entry in new[]{1,5}){
                float angle=-(entry==1?84:233)*Mathf.Deg2Rad;
                var radial=new Vector3(Mathf.Sin(angle),0,-Mathf.Cos(angle));
                var point=PeakCenter+radial*(entry==1?4.3f:3.6f)+Vector3.up*(entry==1?2.5f:4.65f);
                // The first shortcut needs a longer takeoff/landing strip at walking speed;
                // its narrow width still exposes the racer to missed landings and kicks.
                var surface=Landing(root,"Shortcut "+entry,point,new Vector3(1.35f,.45f,entry==1?3f:1.8f),Quaternion.LookRotation(Vector3.Cross(radial,Vector3.up)),rock);
                Edge(surface,new Color(.85f,.74f,.46f));
                var node=Node(root,"Shortcut ledge "+entry,point+Vector3.up*.025f);
                var hint=node.gameObject.AddComponent<ClimbStep>();hint.arrivalRadius=.5f;hint.jump=ClimbJump.Duck;
                passing.Add(new CourseBranchRoutes.Shortcut{entryWaypoint=entry+2,exitWaypoint=entry+5,landing=node});
            }
            course.shortcuts=passing.ToArray();
            var peak=PeakCenter+Vector3.up*6.9f;
            Landing(root,"Summit",peak,new Vector3(4.6f,.5f,4.6f),Quaternion.identity,rock);
            course.summit=Node(root,"Shared summit",peak+Vector3.up*.025f);
            BuildCore(root,rock);
            for(int lane=0;lane<6;lane++){
                var laneRoot=roster.routeRoots[lane];
                foreach(var old in laneRoot.Cast<Transform>().ToArray())Object.DestroyImmediate(old.gameObject);
                for(int i=0;i<count+3;i++)Node(laneRoot,"Landing "+i,Vector3.zero).gameObject.AddComponent<ClimbStep>();
            }
            roster.ApplyLayout(6);
            GameObject.Find("Bell plinth").transform.position=peak+new Vector3(0,.35f,.7f);
            session.bellTarget.position=peak+new Vector3(0,.7f,.7f);
            GameObject.Find("RING").transform.position=peak+new Vector3(0,.4f,.37f);
            var title=GameObject.Find("BELL RUN");title.GetComponent<TextMesh>().text="SUMMIT";title.transform.position=peak+new Vector3(0,3,3);
            var subtitle=GameObject.Find("JUMP  /  BRAWL  /  RING");subtitle.GetComponent<TextMesh>().text="CLIMB / PASS / RING";subtitle.transform.position=peak+new Vector3(0,2,3);
            var floor=GameObject.Find("Pit floor").transform;floor.position=new Vector3(0,-9.2f,12);floor.localScale=new Vector3(44,.4f,52);session.fallHeight=-7;
            foreach(string name in new[]{"Left arena wall","Right arena wall","Far arena wall"}){
                var wall=GameObject.Find(name).transform;bool end=name=="Far arena wall";
                wall.position=new Vector3(end?0:name=="Left arena wall"?-22:22,7,end?38:12);
                wall.localScale=end?new Vector3(44,32,.4f):new Vector3(.4f,32,52);
            }
            CourseMapsBuilder.InstallInCurrentScene("summit");
            var definition=CourseMapsBuilder.Catalog.maps.Single(m=>m.id=="summit");
            definition.description="좁은 바위턱과 엇갈린 착지점을 따라 봉우리를 오릅니다. 넓은 쉼터와 위험한 숏컷에서 추월을 노리세요.";
            EditorUtility.SetDirty(definition);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        }
        static void BuildCore(Transform root,Material fallback)
        {
            const int sides=16;float[] heights={-9,-2,1.6f,4.4f,6.65f},radii={5.3f,4.4f,3.25f,2.6f,1.8f};
            Vector2[] offsets={Vector2.zero,new Vector2(.2f,-.3f),new Vector2(-.2f,.15f),new Vector2(.1f,.2f),Vector2.zero};
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            for(int ring=0;ring<heights.Length;ring++)for(int i=0;i<=sides;i++){
                float a=i*2*Mathf.PI/sides,irregular=1+.11f*Mathf.Sin(a*3)+.06f*Mathf.Cos(a*5+ring*.45f);
                vertices.Add(new Vector3(Mathf.Sin(a)*radii[ring]*irregular+offsets[ring].x,heights[ring],Mathf.Cos(a)*radii[ring]*irregular+offsets[ring].y));uv.Add(new Vector2(i*4f/sides,heights[ring]/3));
            }
            for(int ring=0;ring<heights.Length-1;ring++)for(int i=0;i<sides;i++){
                int a=ring*(sides+1)+i,b=a+sides+1;
                triangles.AddRange(new[]{a,a+1,b,a+1,b+1,b});
            }
            int top=vertices.Count;vertices.Add(new Vector3(0,6.65f,0));uv.Add(Vector2.zero);
            for(int i=0;i<sides;i++)triangles.AddRange(new[]{top,(heights.Length-1)*(sides+1)+i,(heights.Length-1)*(sides+1)+i+1});
            string path=MovementProjectBuilder.Root+"/Art/Environment/SummitCore.asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(!mesh){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,path);}else mesh.Clear();
            mesh.name="Single summit rock";mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
            var core=new GameObject("Summit rock core",typeof(MeshFilter),typeof(MeshRenderer),typeof(MeshCollider));
            core.transform.SetParent(root);core.transform.position=PeakCenter;core.GetComponent<MeshFilter>().sharedMesh=mesh;core.GetComponent<MeshCollider>().sharedMesh=mesh;
            var model=EnvironmentAssetImporter.Prefab("MountainRock");
            core.GetComponent<Renderer>().sharedMaterial=model?model.GetComponentInChildren<Renderer>().sharedMaterial:fallback;
        }
        static Transform Landing(Transform parent,string id,Vector3 point,Vector3 size,Quaternion rotation,Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name="Climb landing "+id;go.transform.SetParent(parent);go.transform.SetPositionAndRotation(point-Vector3.up*size.y*.5f,rotation);go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=material;return go.transform;
        }
        static void Edge(Transform surface,Color color)
        {
            string path=MovementProjectBuilder.Root+"/Materials/SummitTrail.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material){material=new Material(Shader.Find("Sprites/Default"));AssetDatabase.CreateAsset(material,path);}
            var line=new GameObject("Rock lip").AddComponent<LineRenderer>();line.transform.SetParent(surface,false);line.useWorldSpace=false;line.loop=true;line.positionCount=4;line.widthMultiplier=.025f;line.sharedMaterial=material;line.startColor=line.endColor=color;
            line.SetPositions(new[]{new Vector3(-.46f,.515f,-.46f),new Vector3(.46f,.515f,-.46f),new Vector3(.46f,.515f,.46f),new Vector3(-.46f,.515f,.46f)});
        }
        static Transform Node(Transform parent,string name,Vector3 p){var t=new GameObject(name).transform;t.SetParent(parent);t.position=p;return t;}
        public static void RefreshLabels(){CourseMapsBuilder.BuildFork();}
    }
}
