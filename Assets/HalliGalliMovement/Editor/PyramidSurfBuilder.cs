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
        [MenuItem("HalliGalli/Maps/Rebuild Pyramid Flow In Place")]
        public static void Build()
        {
            if(!File.Exists(ScenePath))throw new InvalidOperationException("The saved pyramid scene is required; this entry never rebuilds a template scene.");
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(scene.path!=ScenePath)scene=EditorSceneManager.OpenScene(ScenePath);
            Directory.CreateDirectory("Build/SurfRevision/Backups");
            if(scene.isDirty)EditorSceneManager.SaveScene(scene);
            File.Copy(ScenePath,"Build/SurfRevision/Backups/Pyramid_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".unity",true);
            var session=Object.FindAnyObjectByType<CourseSession>();
            if(!session||!session.roster||!session.roster.surf||!session.roster.surf.GetComponent<PyramidCourse>())
                throw new InvalidOperationException("The existing pyramid session and route must be present.");
            var roster=session.roster;var surf=roster.surf;var pyramid=surf.GetComponent<PyramidCourse>();
            var root=surf.transform;
            // Retain the session, participants, runtime components, BGM, hands and route-root identities.
            foreach(Transform child in root.Cast<Transform>().ToArray())
                if(child.name.StartsWith("Pyramid bank ")||child.name.StartsWith("Pyramid landing ")||child.name=="Bell landing")
                    Object.DestroyImmediate(child.gameObject);
            foreach(string name in new[]{"Pyramid architecture","Pyramid decoration","Pyramid course cues","Pyramid warm bounce"}){
                var previous=GameObject.Find(name);if(previous)Object.DestroyImmediate(previous);
            }
            Directory.CreateDirectory(Art+"/Meshes");AssetDatabase.Refresh();
            var stone=PyramidArtBuilder.Material("Sandstone",new Color(.66f,.60f,.48f),false,true);
            var ramp=PyramidArtBuilder.Material("Polished limestone",new Color(.77f,.72f,.59f),false,true);
            var edge=PyramidArtBuilder.Material("Turquoise inlay",new Color(.17f,.75f,.72f),true);
            var sections=new List<SurfCourse.Section>();var rooms=new List<PyramidCourse.Chamber>();
            Vector3 start=new Vector3(0,-.6f,8.5f);
            for(int room=0;room<5;room++){
                int first=sections.Count;var roomSections=PyramidRouteLayout.Room(room,start);sections.AddRange(roomSections);
                var d=sections.Last();var end=d.points.Last();var frame=d.Sample(end);
                const float deckSize=20,deckGap=3;
                bool rising=d.points.Last().y>d.points[0].y;
                var landing=end+frame.forward*(deckGap+deckSize*.5f)+Vector3.up*(rising?.525f:-1.1f);
                string deckName="Pyramid landing "+room;
                var deck=PyramidArtBuilder.Box(root,deckName,landing+Vector3.down*.225f,new Vector3(deckSize,.4f,deckSize),stone,true);
                deck.transform.rotation=Quaternion.LookRotation(frame.forward);
                if(room<4){d.landing=landing;d.landingName=deckName;}
                else surf.finish=Node(root,"Bell landing",landing);
                var rotation=Quaternion.Euler(0,PyramidRouteLayout.Headings[room],0);
                var inverse=Quaternion.Inverse(rotation);
                var bounds=new Bounds(Vector3.zero,Vector3.zero);
                foreach(var section in roomSections)for(int j=0;j<section.points.Length;j++){
                    var right=Quaternion.Euler(0,section.headings[j],0)*Vector3.right;
                    foreach(int side in new[]{-1,1}){
                        var edgePoint=section.points[j]+right*(side*(surf.laneX+section.halfWidth));
                        edgePoint.y-=section.halfWidth*Mathf.Tan(55*Mathf.Deg2Rad);
                        bounds.Encapsulate(inverse*(edgePoint-start));
                    }
                    bounds.Encapsulate(inverse*(section.points[j]+Vector3.up*surf.laneX*Mathf.Tan(55*Mathf.Deg2Rad)-start));
                }
                foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})
                    bounds.Encapsulate(inverse*(landing+frame.right*x*deckSize*.5f+frame.forward*z*deckSize*.5f-start));
                var minimum=bounds.min-new Vector3(8,13,2);
                var maximum=bounds.max+new Vector3(8,14,6);
                if(room==0)minimum.z=-24.5f;
                var chamber=new PyramidCourse.Chamber{name=PyramidRouteLayout.Names[room],firstSection=first,lastSection=sections.Count-1,landing=landing,
                    origin=start,heading=PyramidRouteLayout.Headings[room],localMinimum=minimum,localMaximum=maximum};
                SetWorldBounds(chamber);rooms.Add(chamber);
                if(room<4){
                    var nextForward=Quaternion.Euler(0,PyramidRouteLayout.Headings[room+1],0)*Vector3.forward;
                    start=landing+nextForward*(deckSize*.5f+1.5f)-Vector3.up*1.1f;
                }
            }
            surf.sections=sections.ToArray();pyramid.chambers=rooms.ToArray();
            var bankMeshes=new HashSet<string>();
            for(int i=0;i<sections.Count;i++){
                int first=i;var run=sections[i];
                // Joined descent/launch pieces share one collider: no internal end-cap wall.
                while(i+1<sections.Count&&run.landing==Vector3.zero&&run.points.Last()==sections[i+1].points[0]
                    &&Mathf.Approximately(run.halfWidth,sections[i+1].halfWidth)){
                    var next=sections[++i];
                    run=new SurfCourse.Section{points=run.points.Concat(next.points.Skip(1)).ToArray(),headings=run.headings.Concat(next.headings.Skip(1)).ToArray(),
                        side=run.side,twoSided=run.twoSided,halfWidth=run.halfWidth,landing=next.landing};
                }
                string name="Pyramid bank "+first;
                SurfCourseBuilder.BuildRamp(root,name,run.side,run,ramp,edge,Art+"/Meshes");
                bankMeshes.Add(Art+"/Meshes/"+name.Replace(" ","_")+".asset");
            }
            // Only route endpoints change; do not reapply saved seats, shirts or accessories.
            for(int seat=0;seat<roster.PlayerCount;seat++)
                surf.Apply(roster.RouteForSeat(seat),seat,roster.PlayerCount);
            var finalSection=sections.Last();var finalFrame=finalSection.Sample(finalSection.points.Last());
            var finalRotation=Quaternion.LookRotation(finalFrame.forward,Vector3.up);
            surf.finish.rotation=finalRotation;
            var plinth=GameObject.Find("Bell plinth").transform;
            plinth.position=surf.finish.position+finalFrame.forward+Vector3.up*.325f;plinth.rotation=finalRotation;
            session.bellTarget.position=surf.finish.position+finalFrame.forward+Vector3.up*.675f;session.bellTarget.rotation=finalRotation;
            GameObject.Find("RING").transform.position=surf.finish.position+finalFrame.forward*.67f+Vector3.up*.375f;
            GameObject.Find("RING").transform.rotation=finalRotation*Quaternion.Euler(0,180,0);
            session.fallHeight=rooms.Min(r=>r.minimum.y)-4;
            // Neither the movement profile nor map catalog installation needs to be regenerated.
            Register(AssetDatabase.LoadAssetAtPath<SourceMovementProfile>(SurfCourseBuilder.ProfilePath));
            CheckpointBuilder.InstallCurrent();session.checkpoints.orderedPassage=true;
            PyramidArtBuilder.Dress(session,pyramid,stone);
            PyramidCourseObstacles.Install(pyramid);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            RemoveUnreferencedMeshes(bankMeshes);
            Directory.CreateDirectory("TestResults");
            File.WriteAllText("TestResults/pyramid-layout.json",JsonUtility.ToJson(new Layout{banks=bankMeshes.Count,length=sections.Sum(s=>s.Length),rooms=rooms.ToArray(),sections=sections.ToArray()},true));
            Debug.Log("PYRAMID_BUILD_COMPLETE");
        }
        static void RemoveUnreferencedMeshes(HashSet<string> used)
        {
            var candidates=AssetDatabase.FindAssets("t:Mesh",new[]{Art+"/Meshes"}).Select(AssetDatabase.GUIDToAssetPath)
                .Where(path=>{
                    string name=Path.GetFileNameWithoutExtension(path);
                    if(name=="LotusColumn"||name=="PyramidPoint")return true;
                    if(!name.StartsWith("Pyramid_bank_"))return false;
                    return !used.Contains(path);
                }).ToArray();
            if(candidates.Length==0)return;
            var candidateSet=new HashSet<string>(candidates);
            var roots=AssetDatabase.GetAllAssetPaths().Where(path=>path.StartsWith("Assets/")&&!AssetDatabase.IsValidFolder(path)&&!candidateSet.Contains(path)).ToArray();
            var referenced=new HashSet<string>(AssetDatabase.GetDependencies(roots,true));
            foreach(string path in candidates)if(!referenced.Contains(path))AssetDatabase.DeleteAsset(path);
        }
        [Serializable]class Layout{public int banks;public float length;public PyramidCourse.Chamber[] rooms;public SurfCourse.Section[] sections;}
        static void Register(SourceMovementProfile profile)
        {
            string path=Root+"/Maps/pyramid.asset";var map=AssetDatabase.LoadAssetAtPath<CourseMapDefinition>(path);
            if(!map){map=ScriptableObject.CreateInstance<CourseMapDefinition>();AssetDatabase.CreateAsset(map,path);}
            map.id=Id;map.displayName="왕의 회랑";map.style=CourseMapStyle.Surf;map.difficulty=3;
            map.description="긴 외벽 도약과 중앙 홀의 공중 방향 전환, 수문 아래층에서 높은 착지면으로. 속도와 착지 각도를 맞춰 일곱 공중 연결과 네 기둥형 체크포인트를 지나 왕의 종으로.";
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
        public static void Probe()=>Validate(true);
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
            // Oblique cutaways show folded rooms without hiding a return leg behind its approach.
            // Keep route cues visible, but remove the shell and wall dressing only for capture.
            var architecture=GameObject.Find("Pyramid architecture");var decoration=GameObject.Find("Pyramid decoration");
            var shell=Object.FindObjectsByType<Renderer>().Where(r=>r.enabled&&
                (architecture&&r.transform.IsChildOf(architecture.transform)||decoration&&r.transform.IsChildOf(decoration.transform))).ToArray();
            bool fog=RenderSettings.fog;GameObject overview=null;Material directionMaterial=null;
            try{
                foreach(var r in shell)r.enabled=false;RenderSettings.fog=false;
                cam.orthographic=true;cam.aspect=1.2f;
                for(int i=0;i<pyramid.chambers.Length;i++){
                    var room=pyramid.chambers[i];
                    var roomBounds=new Bounds((room.minimum+room.maximum)*.5f,room.maximum-room.minimum);
                    var view=room.Rotation*Quaternion.Euler(42,-38,0);
                    var inverse=Quaternion.Inverse(view);var projected=new Bounds(Vector3.zero,Vector3.zero);
                    foreach(int x in new[]{-1,1})foreach(int y in new[]{-1,1})foreach(int z in new[]{-1,1})
                        projected.Encapsulate(inverse*Vector3.Scale(roomBounds.extents,new Vector3(x,y,z)));
                    cam.orthographicSize=Mathf.Max(projected.extents.y,projected.extents.x/cam.aspect)*1.08f;
                    cam.transform.SetPositionAndRotation(roomBounds.center-view*Vector3.forward*(roomBounds.size.magnitude+30),view);
                    Render(cam,"Pyramid_Course_"+(i+1));
                }
                // Follow the actual curved path; an endpoint chord would misrepresent the U-turn.
                var bounds=new Bounds(pyramid.chambers[0].minimum,Vector3.zero);
                foreach(var room in pyramid.chambers){bounds.Encapsulate(room.minimum);bounds.Encapsulate(room.maximum);}
                overview=new GameObject("Pyramid preview directions");
                directionMaterial=new Material(Shader.Find("Unlit/Color")){color=new Color(1,.72f,.2f)};
                float arrowY=bounds.max.y+2;
                foreach(var room in pyramid.chambers){
                    var path=new List<Vector3>();
                    for(int i=room.firstSection;i<=room.lastSection;i++){
                        var section=surf.sections[i];
                        for(int j=0;j<section.points.Length;j+=10){
                            var right=Quaternion.Euler(0,section.headings[j],0)*Vector3.right;
                            var point=section.points[j]-right*(surf.laneX+section.halfWidth+4);point.y=arrowY;path.Add(point);
                        }
                    }
                    var end=surf.sections[room.lastSection];var frame=end.Sample(end.points.Last());
                    var tip=end.points.Last()-frame.right*(surf.laneX+end.halfWidth+4);tip.y=arrowY;
                    path.Add(tip);path.Add(tip-frame.forward*5+frame.right*3);path.Add(tip);path.Add(tip-frame.forward*5-frame.right*3);
                    var arrow=new GameObject("Stage "+(room.firstSection+1)+" direction").AddComponent<LineRenderer>();
                    arrow.transform.SetParent(overview.transform);arrow.useWorldSpace=true;arrow.widthMultiplier=.85f;arrow.numCapVertices=2;
                    arrow.sharedMaterial=directionMaterial;arrow.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                    arrow.positionCount=path.Count;arrow.SetPositions(path.ToArray());
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
