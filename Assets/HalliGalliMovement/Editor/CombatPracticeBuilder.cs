using System.Linq;
using HalliGalli.Movement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace HalliGalli.EditorTools
{
    public static class CombatPracticeBuilder
    {
        public static void Build()
        {
            var scene=EditorSceneManager.OpenScene(CourseBuilder.ScenePath);
            EditorSceneManager.SaveScene(scene,CombatPracticeRoom.ScenePath);
            var session=Object.FindAnyObjectByType<CourseSession>();
            TutorialFerryBuilder.RemoveFromTemplate(session);
            foreach(var renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
                if(!renderer.GetComponentInParent<CourseParticipant>())renderer.enabled=false;
            foreach(var collider in Object.FindObjectsByType<Collider>(FindObjectsInactive.Include))
                if(!collider.GetComponentInParent<CourseParticipant>())collider.enabled=false;
            session.cardGame.enabled=false;session.cardGame.botEnabled=false;session.cardGame.display.enabled=false;
            if(session.cardGame.display.visualRoot)session.cardGame.display.visualRoot.gameObject.SetActive(false);
            session.checkpoints.enabled=false;session.checkpoints.points=new CourseCheckpoints.Point[0];
            var room=session.gameObject.AddComponent<CombatPracticeRoom>();room.session=session;session.practiceRoom=room;session.fallHeight=-6;
            for(int x=-3;x<=3;x++)for(int z=-2;z<=4;z++)
                Make("Practice floor",new Vector3(x*4,-.2f,z*4-2),new Vector3(4.02f,.4f,4.02f),"ConcreteFloor");
            for(int x=-3;x<=3;x++)Make("Practice back wall",new Vector3(x*4,3.8f,16),new Vector3(4.02f,8,.4f),"ConcreteWall");
            foreach(float x in new[]{-14f,14f})for(int z=-2;z<=4;z++)
                Make("Practice side wall",new Vector3(x,3.8f,z*4-2),new Vector3(4.02f,8,.4f),"ConcreteWall",90);
            // The open front edge makes it possible to practise throwing targets off a ledge.
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes=CourseMapsBuilder.BuildScenePaths().Select(p=>new EditorBuildSettingsScene(p,true)).ToArray();AssetDatabase.SaveAssets();
        }
        static void Make(string name,Vector3 position,Vector3 size,string asset,float yaw=0)
        {
            var go=new GameObject(name);go.transform.SetPositionAndRotation(position,Quaternion.Euler(0,yaw,0));go.AddComponent<BoxCollider>().size=size;
            var visual=(GameObject)PrefabUtility.InstantiatePrefab(EnvironmentAssetImporter.Prefab(asset));visual.transform.SetParent(go.transform,false);
            // Keep the original atlas UVs on the actual VARCO mesh.
            bool first=true;Bounds bounds=default;
            foreach(var filter in visual.GetComponentsInChildren<MeshFilter>()){
                var b=filter.sharedMesh.bounds;
                for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2){
                    var point=visual.transform.InverseTransformPoint(filter.transform.TransformPoint(b.center+Vector3.Scale(b.extents,new Vector3(x,y,z))));
                    if(first){bounds=new Bounds(point,Vector3.zero);first=false;}else bounds.Encapsulate(point);
                }
            }
            visual.transform.localScale=new Vector3(size.x/bounds.size.x,size.y/bounds.size.y,size.z/bounds.size.z);
            visual.transform.localPosition=-Vector3.Scale(bounds.center,visual.transform.localScale);
            foreach(var c in visual.GetComponentsInChildren<Collider>())Object.DestroyImmediate(c);
        }
    }
}
