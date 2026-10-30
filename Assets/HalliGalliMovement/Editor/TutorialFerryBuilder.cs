using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using HalliGalli.Movement;
using Object=UnityEngine.Object;

namespace HalliGalli.EditorTools
{
    public static class TutorialFerryBuilder
    {
        const string Root=MovementProjectBuilder.Root;
        public static void RemoveFromTemplate(CourseSession session)
        {
            if(!session||!session.ferry)return;
            Object.DestroyImmediate(session.ferry.gameObject);session.ferry=null;
        }
        public static void Build()
        {
            var scene=EditorSceneManager.OpenScene(CourseBuilder.ScenePath);
            Directory.CreateDirectory(Root+"/Demo/Backups");EditorSceneManager.SaveScene(scene,Root+"/Demo/Backups/BellRunCourse_BeforeFerry_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".unity",true);
            var session=Object.FindAnyObjectByType<CourseSession>();RemoveFromTemplate(session);
            foreach(var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)){
                if(!t)continue;
                if(new[]{"Shared pad 4","Shared pad 5","Shared pad 6","Shared pad 7","04","05","06","07"}.Contains(t.name)
                    ||(t.name=="Edge stripe"||t.name=="Platform support")&&t.position.z>11)Object.DestroyImmediate(t.gameObject);
            }
            var course=new GameObject("Tutorial ferry crossing").AddComponent<TutorialFerryCourse>();course.session=session;session.ferry=course;
            course.boardingDock=Pad(course.transform,"Shared pad 4",new Vector3(0,.55f,12),new Vector2(4.8f,3));
            var ferry=Pad(course.transform,"Shared pad 5",new Vector3(0,.55f,15.2f),new Vector2(4.2f,2.6f));
            course.carrier=ferry.gameObject.AddComponent<FactorySurface>();course.carrier.origin=new Vector3(0,.35f,18);course.carrier.travel=Vector3.forward*2.8f;course.carrier.period=8.4f;course.carrier.phase=.75f;
            var second=Pad(course.transform,"Shared pad 6",new Vector3(0,.55f,29.4f),new Vector2(4.2f,2.6f));
            course.secondCarrier=second.gameObject.AddComponent<FactorySurface>();course.secondCarrier.origin=new Vector3(0,.35f,26.6f);course.secondCarrier.travel=Vector3.forward*2.8f;course.secondCarrier.period=8.4f;course.secondCarrier.phase=.25f;
            course.arrivalDock=Pad(course.transform,"Shared pad 7",new Vector3(0,.55f,33),new Vector2(6,3));
            course.waitingPoint=new Vector3(0,.575f,12.25f);
            foreach(int side in new[]{-1,1}){
                foreach(var surface in new[]{course.carrier,course.secondCarrier})Rail(course.transform,new Vector3(side*2.25f,-.05f,surface.origin.z),new Vector3(.14f,.2f,8.2f));
                var marker=new GameObject("Ferry departure lamp").AddComponent<Light>();marker.transform.SetParent(course.transform);marker.transform.position=new Vector3(side*2.2f,1.1f,13.2f);marker.type=LightType.Point;marker.range=2.5f;marker.intensity=1.1f;marker.color=new Color(.95f,.67f,.25f);
            }
            var movingNodes=new List<Transform>();var secondNodes=new List<Transform>();
            foreach(var routeRoot in session.roster.routeRoots){
                var route=routeRoot.Cast<Transform>().ToArray();
                route[3].position=course.waitingPoint;
                route[4].position=course.carrier.transform.position+Vector3.up*.225f;movingNodes.Add(route[4]);
                route[5].position=course.secondCarrier.transform.position+Vector3.up*.225f;secondNodes.Add(route[5]);route[6].position=new Vector3(0,.575f,32.95f);
            }
            course.carrier.routeMarkers=movingNodes.ToArray();
            course.secondCarrier.routeMarkers=secondNodes.ToArray();
            session.bellTarget.position=new Vector3(0,1.25f,33.8f);GameObject.Find("Bell plinth").transform.position=new Vector3(0,.9f,33.8f);
            var ring=GameObject.Find("RING");if(ring)ring.transform.position=new Vector3(0,.95f,33.47f);
            var far=GameObject.Find("Far arena wall");if(far)far.transform.position=new Vector3(0,0,36);
            foreach(string name in new[]{"Left arena wall","Right arena wall"}){var wall=GameObject.Find(name);if(wall){wall.transform.position=new Vector3(wall.transform.position.x,0,12);wall.transform.localScale=new Vector3(.4f,9,50);}}
            var floor=GameObject.Find("Pit floor");floor.transform.position=new Vector3(0,-6.5f,12);floor.transform.localScale=new Vector3(28,1,50);
            EnvironmentArtBuilder.RefreshTutorialShell();
            CheckpointBuilder.InstallCurrent();session.maps.current.description="기본 점프와 엇박으로 왕복하는 두 발판. 가까워지는 순간을 잡아 갈아타세요.";EditorUtility.SetDirty(session.maps.current);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();Capture();
        }
        public static void InstallCheckpoint(CourseSession session)
        {
            var cp=session.checkpoints;var course=session.ferry;
            var spawn=new GameObject("Ferry waiting checkpoint").transform;spawn.SetParent(cp.transform);spawn.position=course.waitingPoint;
            cp.points=new[]{new CourseCheckpoints.Point{spawn=spawn,landing=course.boardingDock,resumeWaypoint=3}};
            var line=new GameObject("Checkpoint outline").AddComponent<LineRenderer>();line.transform.SetParent(cp.transform);line.positionCount=4;line.loop=true;line.widthMultiplier=.055f;
            line.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Checkpoint.mat");
            line.SetPositions(new[]{new Vector3(-1.55f,.59f,10.9f),new Vector3(1.55f,.59f,10.9f),new Vector3(1.55f,.59f,12.9f),new Vector3(-1.55f,.59f,12.9f)});
        }
        static BoxCollider Pad(Transform parent,string name,Vector3 top,Vector2 size)
        {
            var go=new GameObject(name);go.transform.SetParent(parent);go.transform.position=top-Vector3.up*.2f;
            var box=go.AddComponent<BoxCollider>();box.size=new Vector3(size.x,.4f,size.y);
            var visual=(GameObject)PrefabUtility.InstantiatePrefab(EnvironmentAssetImporter.Prefab("LandingPlatform"));visual.transform.SetParent(go.transform,false);
            bool first=true;Bounds bounds=default;
            foreach(var f in visual.GetComponentsInChildren<MeshFilter>()){
                var b=f.sharedMesh.bounds;for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2){
                    var point=visual.transform.InverseTransformPoint(f.transform.TransformPoint(b.center+Vector3.Scale(b.extents,new Vector3(x,y,z))));if(first){bounds=new Bounds(point,Vector3.zero);first=false;}else bounds.Encapsulate(point);
                }
            }
            visual.transform.localScale=new Vector3(size.x/bounds.size.x,.4f/bounds.size.y,size.y/bounds.size.z);visual.transform.localPosition=-Vector3.Scale(bounds.center,visual.transform.localScale);
            foreach(var c in visual.GetComponentsInChildren<Collider>())Object.DestroyImmediate(c);return box;
        }
        static void Rail(Transform parent,Vector3 at,Vector3 size)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name="Ferry guide rail";go.transform.SetParent(parent);go.transform.position=at;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/FactorySteel.mat");Object.DestroyImmediate(go.GetComponent<Collider>());
        }
        public static void Capture()
        {
            EditorSceneManager.OpenScene(CourseBuilder.ScenePath);Directory.CreateDirectory("Previews");
            var cam=new GameObject("Ferry preview").AddComponent<Camera>();cam.cullingMask=~(1<<CharacterMotionDriver.HandsLayer);cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=RenderSettings.fogColor;cam.fieldOfView=65;
            cam.transform.position=new Vector3(11,10,13);cam.transform.LookAt(new Vector3(0,.4f,23));CharacterAssetBuilder.Render(cam,null,"Previews/Tutorial_Ferry.png");
            var course=Object.FindAnyObjectByType<TutorialFerryCourse>();
            foreach(var surface in new[]{course.carrier,course.secondCarrier})surface.transform.position=surface.PositionAt(course.carrier.period*.5);
            CharacterAssetBuilder.Render(cam,null,"Previews/Tutorial_Transfer.png");
            cam.transform.position=new Vector3(11.5f,9.8f,-10.7f);cam.transform.LookAt(new Vector3(0,.3f,10));CharacterAssetBuilder.Render(cam,null,"Previews/Tutorial_Overview.png");Object.DestroyImmediate(cam.gameObject);
            File.Copy("Previews/Tutorial_Overview.png",Root+"/Resources/UI/Maps/Jump.png",true);AssetDatabase.Refresh();EditorSceneManager.OpenScene(CourseBuilder.ScenePath);
        }
        public static void Validate(bool probe=false)
        {
            EditorSceneManager.OpenScene(CourseBuilder.ScenePath);new GameObject("Ferry validation").AddComponent<TutorialFerryValidationRunner>().probeOnly=probe;
            PlayerSettings.runInBackground=true;EditorApplication.isPaused=false;EditorApplication.isPlaying=true;
        }
    }
}
