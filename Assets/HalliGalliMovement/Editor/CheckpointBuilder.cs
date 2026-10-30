using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalliGalli.Movement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace HalliGalli.EditorTools
{
    public static class CheckpointBuilder
    {
        const string RootName="Course checkpoints";
        [MenuItem("HalliGalli/Maps/Install Midcourse Checkpoints")]
        public static void Install()
        {
            foreach(var path in new[]{CourseBuilder.ScenePath,CourseMapsBuilder.ClimbScene,SurfCourseBuilder.ScenePath}){
                var scene=EditorSceneManager.OpenScene(path);
                string backup=MovementProjectBuilder.Root+"/Demo/Backups";Directory.CreateDirectory(backup);
                EditorSceneManager.SaveScene(scene,backup+"/"+scene.name+"_BeforeCheckpoints_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".unity",true);
                InstallCurrent();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();EnvironmentArtBuilder.Capture();SurfCourseBuilder.Capture();
            EditorSceneManager.OpenScene(CourseMapsBuilder.ClimbScene);
        }
        public static void InstallCurrent()
        {
            var session=Object.FindAnyObjectByType<CourseSession>();if(!session||!session.roster)return;
            var previous=GameObject.Find(RootName);if(previous)Object.DestroyImmediate(previous);
            var checkpoints=new GameObject(RootName).AddComponent<CourseCheckpoints>();checkpoints.session=session;session.checkpoints=checkpoints;
            var points=new List<CourseCheckpoints.Point>();
            if(session.ferry){TutorialFerryBuilder.InstallCheckpoint(session);return;}
            if(session.factory){FactoryCourseBuilder.InstallCheckpoint(session);return;}
            if(session.roster.surf){
                var surf=session.roster.surf;
                for(int i=0;i<surf.sections.Length-1;i++) {
                    var section=surf.sections[i];
                    if(section.landing==Vector3.zero)continue;
                    string name=string.IsNullOrEmpty(section.landingName)?(i==0?"Surf turn landing":"Surf midway landing"):section.landingName;
                    points.Add(Make(checkpoints,name,GameObject.Find(name).GetComponent<BoxCollider>(),section.landing,surf.sections[i+1].headings[0],3,i+1));
                }
            }else if(session.roster.branches){
                var fork=session.roster.branches;
                foreach(var branch in fork.sharedSpiral?new[]{fork.left}:new[]{fork.left,fork.right}){
                    int mid=branch.Length/2;
                    var node=branch[mid];var next=branch[mid+1];
                    points.Add(Make(checkpoints,node.name,GameObject.Find("Climb landing "+node.name).GetComponent<BoxCollider>(),node.position,Quaternion.LookRotation(next.position-node.position).eulerAngles.y,mid+2));
                }
            }else{
                var route=session.roster.RouteForSeat(0);var node=route[3];
                points.Add(Make(checkpoints,"Tutorial middle",GameObject.Find("Shared pad 4").GetComponent<BoxCollider>(),node.position,Quaternion.LookRotation(route[4].position-node.position).eulerAngles.y,3));
            }
            checkpoints.points=points.ToArray();
        }
        static CourseCheckpoints.Point Make(CourseCheckpoints owner,string name,BoxCollider landing,Vector3 position,float yaw,int waypoint,int gates=0,bool surf=false)
        {
            var spawn=new GameObject(name).transform;spawn.SetParent(owner.transform);spawn.SetPositionAndRotation(position,Quaternion.Euler(0,yaw,0));
            var bounds=landing.bounds;
            if(surf){foreach(int side in new[]{-1,1})Marker(owner.transform,position+Vector3.right*side*owner.session.roster.surf.laneX,new Vector2(2,2),true);}
            else Marker(owner.transform,position,new Vector2(Mathf.Min(bounds.size.x*.8f,3),Mathf.Min(bounds.size.z*.8f,3)));
            return new CourseCheckpoints.Point{spawn=spawn,landing=landing,resumeWaypoint=waypoint,surfGates=gates,surfLanes=surf};
        }
        static void Marker(Transform root,Vector3 center,Vector2 size,bool reverse=false)
        {
            string path=MovementProjectBuilder.Root+"/Materials/Checkpoint.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material){material=new Material(Shader.Find("Standard"));material.color=new Color(.2f,.95f,1);material.EnableKeyword("_EMISSION");material.SetColor("_EmissionColor",new Color(.1f,.7f,.8f));AssetDatabase.CreateAsset(material,path);}
            var border=new GameObject("Checkpoint outline").AddComponent<LineRenderer>();border.transform.SetParent(root);
            border.useWorldSpace=true;border.loop=true;border.positionCount=4;border.widthMultiplier=.065f;border.sharedMaterial=material;
            center+=Vector3.up*.035f;
            border.SetPositions(new[]{center+new Vector3(-size.x/2,0,-size.y/2),center+new Vector3(size.x/2,0,-size.y/2),center+new Vector3(size.x/2,0,size.y/2),center+new Vector3(-size.x/2,0,size.y/2)});
            var label=new GameObject("Checkpoint marker").AddComponent<TextMesh>();label.transform.SetParent(root);label.transform.position=center+Vector3.forward*size.y*(reverse?-.3f:.3f);
            label.transform.rotation=Quaternion.Euler(90,reverse?180:0,0);label.text="CHECK";label.fontSize=80;label.characterSize=.016f;label.anchor=TextAnchor.MiddleCenter;label.color=new Color(.4f,1,1);
            label.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(MovementProjectBuilder.Root+"/Materials/CourseText.mat");
        }
        public static void Capture()
        {
            Directory.CreateDirectory("Previews");
            foreach(var path in new[]{CourseBuilder.ScenePath,CourseMapsBuilder.ClimbScene,SurfCourseBuilder.ScenePath}){
                EditorSceneManager.OpenScene(path);var session=Object.FindAnyObjectByType<CourseSession>();
                var camera=new GameObject("Checkpoint preview").AddComponent<Camera>();camera.cullingMask=~(1<<CharacterMotionDriver.HandsLayer);
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=RenderSettings.fogColor;camera.fieldOfView=60;
                for(int i=0;i<session.checkpoints.points.Length;i++){
                    var point=session.checkpoints.points[i];var target=point.spawn.position;
                    if(point.surfLanes)target+=Vector3.left*session.roster.surf.laneX;
                    camera.transform.position=target+new Vector3(2.6f,3.2f,point.surfLanes?3.8f:-3.8f);camera.transform.LookAt(target+Vector3.up*.1f);
                    CharacterAssetBuilder.Render(camera,null,"Previews/Checkpoint_"+session.maps.current.id+"_"+i+".png");
                }
                Object.DestroyImmediate(camera.gameObject);
            }
            EditorSceneManager.OpenScene(CourseMapsBuilder.ClimbScene);
        }
    }
}
