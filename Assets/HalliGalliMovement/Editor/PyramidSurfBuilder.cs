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
    public static class PyramidSurfBuilder
    {
        public const string Root=MovementProjectBuilder.Root;
        public const string Art=Root+"/Art/Pyramid";
        public const string ScenePath=Root+"/Demo/PyramidSurfCourse.unity";
        public const string Id="pyramid";
        [MenuItem("HalliGalli/Maps/Build Pyramid Surf")]
        public static void Build()
        {
            if(File.Exists(ScenePath)){
                var prior=EditorSceneManager.OpenScene(ScenePath);Directory.CreateDirectory(Root+"/Demo/Backups");
                EditorSceneManager.SaveScene(prior,Root+"/Demo/Backups/Pyramid_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".unity",true);
            }
            var scene=EditorSceneManager.OpenScene(CourseBuilder.ScenePath);EditorSceneManager.SaveScene(scene,ScenePath);
            var session=Object.FindAnyObjectByType<CourseSession>();var roster=session.roster;
            TutorialFerryBuilder.RemoveFromTemplate(session);
            foreach(var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)){
                if(!t)continue;
                if((t.name.StartsWith("Shared pad ")||t.name=="Edge stripe"||t.name=="Platform support")&&t.position.z>7
                    ||t.name=="Wall buttress"||new[]{"03","04","05","06","07"}.Contains(t.name))Object.DestroyImmediate(t.gameObject);
            }
            var environment=GameObject.Find("VARCO environment");
            foreach(Transform child in environment.transform.Cast<Transform>().ToArray())if(child.name!="CardTable")Object.DestroyImmediate(child.gameObject);
            foreach(var light in Object.FindObjectsByType<Light>())Object.DestroyImmediate(light);
            foreach(string name in new[]{"Pit floor","Left arena wall","Right arena wall","Far arena wall"}){
                var obj=GameObject.Find(name);if(obj)obj.SetActive(false);
            }
            Directory.CreateDirectory(Art+"/Meshes");AssetDatabase.Refresh();
            var stone=PyramidArtBuilder.Material("Sandstone",new Color(.66f,.60f,.48f),false,true);
            var ramp=PyramidArtBuilder.Material("Polished limestone",new Color(.77f,.72f,.59f),false,true);
            var edge=PyramidArtBuilder.Material("Turquoise inlay",new Color(.17f,.75f,.72f),true);
            var root=new GameObject("Pyramid surf course").transform;
            var surf=root.gameObject.AddComponent<SurfCourse>();roster.surf=surf;roster.branches=null;
            var pyramid=root.gameObject.AddComponent<PyramidCourse>();
            var sections=new List<SurfCourse.Section>();var rooms=new List<PyramidCourse.Chamber>();
            Vector3 start=new Vector3(0,-.6f,8.5f);
            for(int room=0;room<5;room++){
                int first=sections.Count;sections.AddRange(PyramidRouteLayout.Room(room,start));
                var d=sections.Last();var end=d.points.Last();var landing=end+new Vector3(0,room==4?2:.525f,10.5f);
                string deckName="Pyramid landing "+room;
                PyramidArtBuilder.Box(root,deckName,landing+Vector3.down*.225f,new Vector3(18,.4f,12),stone,true);
                if(room<4){d.landing=landing;d.landingName=deckName;}
                else surf.finish=Node(root,"Bell landing",landing);
                var points=sections.Skip(first).SelectMany(s=>s.points).ToArray();
                rooms.Add(new PyramidCourse.Chamber{name=PyramidRouteLayout.Names[room],firstSection=first,lastSection=sections.Count-1,landing=landing,
                    minimum=new Vector3(points.Min(p=>p.x)-14,points.Min(p=>p.y)-13,room==0?-16:start.z-1.2f),
                    maximum=new Vector3(points.Max(p=>p.x)+14,start.y+18,end.z+16.8f)});
                if(room<4)start=new Vector3(end.x,end.y-.6f,end.z+18);
            }
            surf.sections=sections.ToArray();pyramid.chambers=rooms.ToArray();
            for(int i=0;i<sections.Count;i++)SurfCourseBuilder.BuildRamp(root,"Pyramid bank "+i,sections[i].side,sections[i],ramp,edge,Art+"/Meshes");
            foreach(var route in roster.routeRoots){foreach(Transform old in route.Cast<Transform>().ToArray())Object.DestroyImmediate(old.gameObject);for(int i=0;i<5;i++)Node(route,"Landing "+i,Vector3.zero);}
            roster.ApplyLayout(6);
            GameObject.Find("Bell plinth").transform.position=surf.finish.position+new Vector3(0,.325f,1);
            session.bellTarget.position=surf.finish.position+new Vector3(0,.675f,1);
            GameObject.Find("RING").transform.position=surf.finish.position+new Vector3(0,.375f,.67f);
            GameObject.Find("RING").transform.rotation=Quaternion.Euler(0,180,0);
            session.fallHeight=rooms.Min(r=>r.minimum.y)-4;
            var profile=AssetDatabase.LoadAssetAtPath<SourceMovementProfile>(SurfCourseBuilder.ProfilePath);
            Register(profile);CourseMapsBuilder.InstallInCurrentScene(Id);
            foreach(var p in roster.allParticipants)p.GetComponent<SourceMotor>().profile=profile;
            PyramidArtBuilder.Dress(session,pyramid,stone);
            PyramidCourseObstacles.Install(pyramid,stone);
            PyramidVarcoImporter.WritePlacementReport();
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            EditorBuildSettings.scenes=CourseMapsBuilder.BuildScenePaths().Select(p=>new EditorBuildSettingsScene(p,true)).ToArray();
            Directory.CreateDirectory("TestResults");
            File.WriteAllText("TestResults/pyramid-layout.json",JsonUtility.ToJson(new Layout{banks=sections.Count,length=sections.Sum(s=>s.Length),rooms=rooms.ToArray(),sections=sections.ToArray()},true));
            Capture();Debug.Log("PYRAMID_BUILD_COMPLETE");
        }
        [Serializable]class Layout{public int banks;public float length;public PyramidCourse.Chamber[] rooms;public SurfCourse.Section[] sections;}
        static void Register(SourceMovementProfile profile)
        {
            string path=Root+"/Maps/pyramid.asset";var map=AssetDatabase.LoadAssetAtPath<CourseMapDefinition>(path);
            if(!map){map=ScriptableObject.CreateInstance<CourseMapDefinition>();AssetDatabase.CreateAsset(map,path);}
            map.id=Id;map.displayName="왕의 회랑";map.style=CourseMapStyle.Surf;map.difficulty=4;
            map.description="양면 삼각 서프. 편한 면을 골라 급강하와 나선 회랑을 지나 왕의 묘실로.";
            map.scenePath=ScenePath;map.movementProfile=profile;map.playable=true;map.thumbnail="Pyramid";EditorUtility.SetDirty(map);
            var catalog=CourseMapsBuilder.Catalog;if(!catalog.maps.Contains(map)){catalog.maps=catalog.maps.Append(map).ToArray();EditorUtility.SetDirty(catalog);}
        }
        static Transform Node(Transform root,string name,Vector3 position){var t=new GameObject(name).transform;t.SetParent(root);t.position=position;return t;}
        public static void Validate(bool probe=false)
        {
            EditorSceneManager.OpenScene(ScenePath);new GameObject("Pyramid validation").AddComponent<PyramidValidationRunner>().probeOnly=probe;
            PlayerSettings.runInBackground=true;EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Focus();EditorApplication.isPaused=false;EditorApplication.isPlaying=true;
        }
        public static void Capture()
        {
            EditorSceneManager.OpenScene(ScenePath);Directory.CreateDirectory("Previews");
            var pyramid=Object.FindAnyObjectByType<PyramidCourse>();var surf=pyramid.GetComponent<SurfCourse>();
            var cam=new GameObject("Pyramid review camera").AddComponent<Camera>();cam.cullingMask=~(1<<CharacterMotionDriver.HandsLayer);cam.fieldOfView=70;
            cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=RenderSettings.fogColor;
            cam.transform.position=new Vector3(0,7,2);cam.transform.LookAt(new Vector3(0,-3,28));Render(cam,"Pyramid_Entrance");
            cam.transform.position=surf.sections[0].points[0]+new Vector3(0,9,-7);cam.transform.LookAt(surf.sections[0].points[0]+new Vector3(0,-1,14));Render(cam,"Pyramid_Triangle_Choice");
            for(int i=0;i<pyramid.chambers.Length;i++){
                var room=pyramid.chambers[i];var section=surf.sections[room.firstSection+1];
                cam.transform.position=surf.sections[room.firstSection].points[0]+new Vector3(0,12,-5);
                cam.transform.LookAt(section.points[section.points.Length/2]+Vector3.up*2);Render(cam,"Pyramid_Chamber_"+(i+1));
            }
            var last=pyramid.chambers.Last();cam.transform.position=last.landing+new Vector3(9,5,-10);cam.transform.LookAt(last.landing+Vector3.up*3);Render(cam,"Pyramid_Tomb");
            var middle=pyramid.chambers[1];cam.transform.position=middle.landing+new Vector3(11,7,-14);cam.transform.LookAt(middle.landing+new Vector3(0,5,4));Render(cam,"Pyramid_Portal");
            var first=pyramid.chambers[0];cam.transform.position=new Vector3(first.minimum.x+8,first.maximum.y-10,first.minimum.z+27);
            cam.transform.LookAt(new Vector3(first.minimum.x+.5f,first.maximum.y-10,first.minimum.z+23));Render(cam,"Pyramid_Stone_Detail");
            // Inspect the actual approaching view of each different course challenge.
            int[] offsets={0,1,2,4,4};
            for(int i=0;i<pyramid.chambers.Length;i++){
                var room=pyramid.chambers[i];var s=surf.sections[room.firstSection+offsets[i]];
                int at=i==1?12:i>=3?s.points.Length-7:0;
                Vector3 p=i==0?Vector3.Lerp(s.points[0],s.points[1],.42f):i==2?Vector3.Lerp(s.points[0],s.points[1],.85f):s.points[at];
                var frame=s.Sample(p);cam.transform.position=p+frame.right*(s.side*surf.laneX)+Vector3.up*1.65f;
                Vector3 target=cam.transform.position+frame.forward*20+Vector3.down*4;
                if(i==1){int ahead=Mathf.Min(s.points.Length-1,at+30);target=s.points[ahead]+Quaternion.Euler(0,s.headings[ahead],0)*Vector3.right*(s.side*surf.laneX)+Vector3.up*1.3f;}
                if(i==2||i==3){var next=surf.sections[room.firstSection+offsets[i]+1];var f=next.Sample(next.points[0]);target=next.points[0]+f.right*(next.side*surf.laneX)+f.forward*8+Vector3.up;}
                if(i==4)target=room.landing+Vector3.up*1.2f;
                cam.transform.LookAt(target);Render(cam,"Pyramid_FirstPerson_"+(i+1));
            }
            // Cutaway previews expose route height and transfers; restore every renderer before leaving.
            var shell=Object.FindObjectsByType<Renderer>().Where(r=>r.enabled&&(r.name.Contains("stone wall")||r.name.Contains("back wall")||r.name.Contains("chamber wall")||r.name.Contains("pyramid vault")||r.name.Contains("Portal left closure")||r.name.Contains("Portal right closure")||r.name=="Portal header"||r.name=="Portal sill")).ToArray();
            foreach(var r in shell)r.enabled=false;bool fog=RenderSettings.fog;RenderSettings.fog=false;
            cam.fieldOfView=55;
            for(int i=0;i<pyramid.chambers.Length;i++){
                var room=pyramid.chambers[i];var points=surf.sections.Skip(room.firstSection).Take(room.lastSection-room.firstSection+1).SelectMany(s=>s.points).ToArray();
                Vector3 center=new Vector3((room.minimum.x+room.maximum.x)*.5f,points.Average(p=>p.y),(room.minimum.z+room.maximum.z)*.5f);
                float radius=Mathf.Max(room.maximum.x-room.minimum.x,room.maximum.z-room.minimum.z);
                cam.transform.position=center+new Vector3(radius*.65f,radius*.85f,-radius*.65f);cam.transform.LookAt(center);Render(cam,"Pyramid_Course_"+(i+1));
            }
            RenderSettings.fog=fog;foreach(var r in shell)r.enabled=true;
            Object.DestroyImmediate(cam.gameObject);
            File.Copy("Previews/Pyramid_Chamber_1.png",Root+"/Resources/UI/Maps/Pyramid.png",true);AssetDatabase.Refresh();EditorSceneManager.OpenScene(ScenePath);
        }
        static void Render(Camera c,string name)=>CharacterAssetBuilder.Render(c,null,"Previews/"+name+".png");
    }
}
