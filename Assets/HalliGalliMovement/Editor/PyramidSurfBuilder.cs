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
                int first=sections.Count;var roomSections=PyramidRouteLayout.Room(room,start);sections.AddRange(roomSections);
                var d=sections.Last();var end=d.points.Last();var frame=d.Sample(end);
                const float deckSize=20,deckGap=3;
                var landing=end+frame.forward*(deckGap+deckSize*.5f)+Vector3.up*(room==4?.75f:.525f);
                string deckName="Pyramid landing "+room;
                PyramidArtBuilder.Box(root,deckName,landing+Vector3.down*.225f,new Vector3(deckSize,.4f,deckSize),stone,true);
                if(room<4){d.landing=landing;d.landingName=deckName;}
                else surf.finish=Node(root,"Bell landing",landing);
                var rotation=Quaternion.Euler(0,PyramidRouteLayout.Headings[room],0);
                var inverse=Quaternion.Inverse(rotation);
                var points=roomSections.SelectMany(s=>s.points).Select(p=>inverse*(p-start)).ToArray();
                float halfSpan=Mathf.Max(14,roomSections.Max(s=>surf.laneX+s.halfWidth)+7.2f);
                float lowest=roomSections.Min(s=>s.points.Min(p=>p.y)-start.y-s.halfWidth*Mathf.Tan(55*Mathf.Deg2Rad));
                var localLanding=inverse*(landing-start);
                var chamber=new PyramidCourse.Chamber{name=PyramidRouteLayout.Names[room],firstSection=first,lastSection=sections.Count-1,landing=landing,
                    origin=start,heading=PyramidRouteLayout.Headings[room],
                    localMinimum=new Vector3(-halfSpan,lowest-13,room==0?-24.5f:-1.2f),
                    localMaximum=new Vector3(halfSpan,points.Max(p=>p.y)+18,localLanding.z+deckSize*.5f+6)};
                SetWorldBounds(chamber);rooms.Add(chamber);
                if(room<4){
                    var nextForward=Quaternion.Euler(0,PyramidRouteLayout.Headings[room+1],0)*Vector3.forward;
                    start=landing+nextForward*(deckSize*.5f+1.5f)-Vector3.up*1.1f;
                }
            }
            surf.sections=sections.ToArray();pyramid.chambers=rooms.ToArray();
            for(int i=0;i<sections.Count;i++)SurfCourseBuilder.BuildRamp(root,"Pyramid bank "+i,sections[i].side,sections[i],ramp,edge,Art+"/Meshes");
            foreach(var route in roster.routeRoots){foreach(Transform old in route.Cast<Transform>().ToArray())Object.DestroyImmediate(old.gameObject);for(int i=0;i<5;i++)Node(route,"Landing "+i,Vector3.zero);}
            roster.ApplyLayout(6);
            var finalSection=sections.Last();var finalFrame=finalSection.Sample(finalSection.points.Last());
            var finalRotation=Quaternion.LookRotation(finalFrame.forward,Vector3.up);
            surf.finish.rotation=finalRotation;
            var plinth=GameObject.Find("Bell plinth").transform;
            plinth.position=surf.finish.position+finalFrame.forward+Vector3.up*.325f;plinth.rotation=finalRotation;
            session.bellTarget.position=surf.finish.position+finalFrame.forward+Vector3.up*.675f;session.bellTarget.rotation=finalRotation;
            GameObject.Find("RING").transform.position=surf.finish.position+finalFrame.forward*.67f+Vector3.up*.375f;
            GameObject.Find("RING").transform.rotation=finalRotation*Quaternion.Euler(0,180,0);
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
            map.id=Id;map.displayName="왕의 회랑";map.style=CourseMapStyle.Surf;map.difficulty=3;
            map.description="내리막 가속과 오르막 도약, 높낮이가 다른 착지. 넓은 체크포인트에서 직각으로 꺾이는 다섯 회랑을 넘어 왕의 종으로.";
            map.scenePath=ScenePath;map.movementProfile=profile;map.playable=true;map.thumbnail="Pyramid";EditorUtility.SetDirty(map);
            var catalog=CourseMapsBuilder.Catalog;if(!catalog.maps.Contains(map)){catalog.maps=catalog.maps.Append(map).ToArray();EditorUtility.SetDirty(catalog);}
        }
        static Transform Node(Transform root,string name,Vector3 position){var t=new GameObject(name).transform;t.SetParent(root);t.position=position;return t;}
        static void SetWorldBounds(PyramidCourse.Chamber room)
        {
            Vector3 half=(room.localMaximum-room.localMinimum)*.5f,center=room.ToWorld((room.localMinimum+room.localMaximum)*.5f);
            Vector3 right=room.Rotation*Vector3.right,forward=room.Rotation*Vector3.forward;
            var extent=new Vector3(Mathf.Abs(right.x)*half.x+Mathf.Abs(forward.x)*half.z,half.y,
                Mathf.Abs(right.z)*half.x+Mathf.Abs(forward.z)*half.z);
            room.minimum=center-extent;room.maximum=center+extent;
        }
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
            var entrance=surf.sections[0];var firstRotation=pyramid.chambers[0].Rotation;
            cam.transform.position=entrance.entry+firstRotation*new Vector3(0,7,-4);cam.transform.LookAt(PointAt(entrance,.5f));Render(cam,"Pyramid_Entrance");
            cam.transform.position=entrance.points[0]+firstRotation*new Vector3(0,9,-7);cam.transform.LookAt(PointAt(entrance,.35f));Render(cam,"Pyramid_Triangle_Choice");
            for(int i=0;i<pyramid.chambers.Length;i++){
                var room=pyramid.chambers[i];var section=surf.sections[room.firstSection];
                cam.transform.position=section.points[0]+room.Rotation*new Vector3(10,12,-5);
                cam.transform.LookAt(PointAt(section,.8f)+Vector3.up*2);Render(cam,"Pyramid_Chamber_"+(i+1));
            }
            var last=pyramid.chambers.Last();cam.transform.position=last.landing+last.Rotation*new Vector3(9,5,-10);cam.transform.LookAt(last.landing+Vector3.up*3);Render(cam,"Pyramid_Tomb");
            var middle=pyramid.chambers[1];cam.transform.position=middle.landing+middle.Rotation*new Vector3(11,7,-14);cam.transform.LookAt(middle.landing+middle.Rotation*new Vector3(0,5,4));Render(cam,"Pyramid_Portal");
            var first=pyramid.chambers[0];cam.transform.position=first.ToWorld(new Vector3(first.localMinimum.x+8,first.localMaximum.y-10,first.localMinimum.z+27));
            cam.transform.LookAt(first.ToWorld(new Vector3(first.localMinimum.x+.5f,first.localMaximum.y-10,first.localMinimum.z+23)));Render(cam,"Pyramid_Stone_Detail");
            // Show each room's largest gap from a real riding lane, independent of mesh tessellation.
            for(int i=0;i<pyramid.chambers.Length;i++){
                var room=pyramid.chambers[i];int approach=room.firstSection;float widest=0;
                for(int index=room.firstSection;index<room.lastSection;index++){
                    float gap=Vector3.Distance(surf.sections[index].points.Last(),surf.sections[index+1].points[0]);
                    if(gap>widest){widest=gap;approach=index;}
                }
                var section=surf.sections[approach];var next=surf.sections[approach+1];
                Vector3 p=PointAt(section,.9f);var frame=section.Sample(p);
                cam.transform.position=p+frame.right*(section.side*surf.laneX)+Vector3.up*1.65f;
                Vector3 target=PointAt(next,.2f);var targetFrame=next.Sample(target);
                cam.transform.LookAt(target+targetFrame.right*(section.side*surf.laneX)+Vector3.up*1.3f);
                Render(cam,"Pyramid_FirstPerson_"+(i+1));
            }
            // Side cutaways expose the vertical launch/catch profile and every real gap.
            // Keep route cues visible, but remove the shell and wall dressing only for capture.
            var architecture=GameObject.Find("Pyramid architecture");var decoration=GameObject.Find("Pyramid decoration");
            var shell=Object.FindObjectsByType<Renderer>().Where(r=>r.enabled&&
                (architecture&&r.transform.IsChildOf(architecture.transform)||decoration&&r.transform.IsChildOf(decoration.transform))).ToArray();
            bool fog=RenderSettings.fog;GameObject overview=null;Material directionMaterial=null;
            try{
                foreach(var r in shell)r.enabled=false;RenderSettings.fog=false;
                cam.orthographic=true;cam.aspect=1.2f;
                for(int i=0;i<pyramid.chambers.Length;i++){
                    var room=pyramid.chambers[i];var roomSections=surf.sections.Skip(room.firstSection).Take(room.lastSection-room.firstSection+1).ToArray();
                    float low=roomSections.Min(s=>s.points.Min(p=>p.y)-s.halfWidth*Mathf.Tan(55*Mathf.Deg2Rad));
                    float high=roomSections.Max(s=>s.points.Max(p=>p.y))+surf.laneX*Mathf.Tan(55*Mathf.Deg2Rad);
                    float near=room.ToLocal(roomSections[0].points[0]).z,far=room.ToLocal(room.landing).z+10;
                    Vector3 center=room.ToWorld(new Vector3(0,(low+high)*.5f-room.origin.y,(near+far)*.5f));
                    cam.orthographicSize=Mathf.Max((high-low)*.5f,(far-near)/(2*cam.aspect))*1.08f;
                    cam.transform.position=center+room.Rotation*Vector3.right*(far-near+30);cam.transform.LookAt(center);
                    Render(cam,"Pyramid_Course_"+(i+1));
                }
                // The entire five-stage route in one north-up cutaway, with temporary
                // arrows outside the riding faces so every checkpoint turn stays legible.
                var bounds=new Bounds(pyramid.chambers[0].minimum,Vector3.zero);
                foreach(var room in pyramid.chambers){bounds.Encapsulate(room.minimum);bounds.Encapsulate(room.maximum);}
                overview=new GameObject("Pyramid preview directions");
                directionMaterial=new Material(Shader.Find("Unlit/Color")){color=new Color(1,.72f,.2f)};
                float arrowY=bounds.max.y+2;
                foreach(var room in pyramid.chambers){
                    Vector3 forward=room.Rotation*Vector3.forward,right=room.Rotation*Vector3.right;
                    Vector3 from=surf.sections[room.firstSection].points[0]+right*10+forward*6;
                    Vector3 tip=surf.sections[room.lastSection].points.Last()+right*10-forward*4;
                    from.y=tip.y=arrowY;
                    var arrow=new GameObject("Stage "+(room.firstSection+1)+" direction").AddComponent<LineRenderer>();
                    arrow.transform.SetParent(overview.transform);arrow.useWorldSpace=true;arrow.widthMultiplier=.85f;arrow.numCapVertices=2;
                    arrow.sharedMaterial=directionMaterial;arrow.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                    arrow.positionCount=5;arrow.SetPositions(new[]{from,tip,tip-forward*5+right*3,tip,tip-forward*5-right*3});
                }
                cam.orthographicSize=Mathf.Max(bounds.size.z*.5f,bounds.size.x/(2*cam.aspect))*1.08f;
                cam.transform.position=new Vector3(bounds.center.x,arrowY+100,bounds.center.z);
                cam.transform.rotation=Quaternion.Euler(90,0,0);cam.farClipPlane=1000;
                cam.backgroundColor=new Color(.055f,.045f,.035f);Render(cam,"Pyramid_Overview");
            }finally{
                RenderSettings.fog=fog;foreach(var r in shell)if(r)r.enabled=true;
                if(overview)Object.DestroyImmediate(overview);if(directionMaterial)Object.DestroyImmediate(directionMaterial);
                Object.DestroyImmediate(cam.gameObject);
            }
            File.Copy("Previews/Pyramid_Chamber_1.png",Root+"/Resources/UI/Maps/Pyramid.png",true);AssetDatabase.Refresh();EditorSceneManager.OpenScene(ScenePath);
        }
        static Vector3 PointAt(SurfCourse.Section section,float fraction)
        {
            float remaining=section.Length*Mathf.Clamp01(fraction);
            for(int i=1;i<section.points.Length;i++){
                float length=Vector3.ProjectOnPlane(section.points[i]-section.points[i-1],Vector3.up).magnitude;
                if(remaining<=length)return Vector3.Lerp(section.points[i-1],section.points[i],remaining/length);
                remaining-=length;
            }
            return section.points[section.points.Length-1];
        }
        static void Render(Camera c,string name)=>CharacterAssetBuilder.Render(c,null,"Previews/"+name+".png");
    }
}
