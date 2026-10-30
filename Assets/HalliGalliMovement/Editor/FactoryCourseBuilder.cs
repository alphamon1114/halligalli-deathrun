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
    public static class FactoryCourseBuilder
    {
        const string Root=MovementProjectBuilder.Root;
        static Transform root;
        static Material steel,rubber,amber,dark;
        [MenuItem("HalliGalli/Maps/Build Factory Course")]
        public static void Build()
        {
            var scene=EditorSceneManager.OpenScene(CourseBuilder.ScenePath);
            EditorSceneManager.SaveScene(scene,FactoryCourse.ScenePath);
            var session=Object.FindAnyObjectByType<CourseSession>();var roster=session.roster;
            TutorialFerryBuilder.RemoveFromTemplate(session);
            // Retain the table, row starts, cards and first two landings unchanged.
            foreach(var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)){
                if(!t)continue;
                if(t.name=="VARCO environment"){
                    foreach(var child in t.Cast<Transform>().ToArray())if(child.name!="CardTable")Object.DestroyImmediate(child.gameObject);
                    continue;
                }
                if((t.name.StartsWith("Shared pad ")||t.name=="Edge stripe"||t.name=="Platform support")&&t.position.z>7
                    ||t.name=="Wall buttress"||t.name.EndsWith("arena wall"))Object.DestroyImmediate(t.gameObject);
            }
            root=new GameObject("Factory machinery").transform;
            steel=Mat("FactorySteel",new Color(.22f,.28f,.27f),.65f);
            rubber=Mat("FactoryRubber",new Color(.075f,.09f,.085f),.05f);
            dark=Mat("FactoryDark",new Color(.065f,.075f,.07f),.5f);
            amber=Mat("FactoryAmber",new Color(.9f,.55f,.12f),.35f,true);
            var course=root.gameObject.AddComponent<FactoryCourse>();course.session=session;session.factory=course;
            var first=Belt("Cross conveyor",new Vector3(0,.35f,8.4f),new Vector2(4.8f,3.5f),Vector3.right*1.15f);
            Platform("Loading dock",new Vector3(0,.35f,12.3f),new Vector2(5,4));
            var a=Platform("Shuttle A",new Vector3(4.8f,.35f,13),new Vector2(3.2f,3.4f));
            var movingA=a.gameObject.AddComponent<FactorySurface>();movingA.origin=a.position;movingA.travel=Vector3.forward*.8f;movingA.period=4.2f;
            Rails(a.position,new Vector2(3.2f,5.3f));
            Platform("Turn dock",new Vector3(10,.7f,13),new Vector2(4.4f,4));
            Belt("Press conveyor",new Vector3(10,.7f,18.2f),new Vector2(5.2f,6.6f),Vector3.back*.85f);
            course.presses=new[]{Press(new Vector3(10,.7f,17),0),Press(new Vector3(10,.7f,20),.5f)};
            var checkpoint=Platform("Maintenance landing",new Vector3(10,.7f,23.5f),new Vector2(5,4.2f));
            Platform("Upper ledge A",new Vector3(5.8f,1.15f,25),new Vector2(3.2f,3.4f));
            Platform("Upper ledge B",new Vector3(1.2f,1.6f,25),new Vector2(3.2f,3.4f));
            var b=Platform("Shuttle B",new Vector3(-3.2f,1.6f,25),new Vector2(3.2f,3.2f));
            var movingB=b.gameObject.AddComponent<FactorySurface>();movingB.origin=b.position;movingB.travel=Vector3.forward*.7f;movingB.period=3.6f;movingB.phase=.25f;
            Rails(b.position,new Vector2(3.2f,5));
            Platform("Bell catwalk",new Vector3(-7.8f,1.6f,27.1f),new Vector2(4,8));
            course.movingPlatforms=new[]{movingA,movingB};
            var nodes=new[]{new Vector3(0,.175f,2.3f),new Vector3(0,.375f,5.7f),new Vector3(0,.375f,9.2f),new Vector3(0,.375f,13),new Vector3(4.8f,.375f,13),new Vector3(10,.725f,13),new Vector3(10,.725f,15.1f),new Vector3(10,.725f,18.4f),new Vector3(10,.725f,21.6f),new Vector3(10,.725f,23.7f),new Vector3(5.8f,1.175f,25),new Vector3(1.2f,1.625f,25),new Vector3(-3.2f,1.625f,25),new Vector3(-7.8f,1.625f,25),new Vector3(-7.8f,1.625f,29.5f)};
            var markersA=new List<Transform>();var markersB=new List<Transform>();
            foreach(var route in roster.routeRoots){
                foreach(var old in route.Cast<Transform>().ToArray())Object.DestroyImmediate(old.gameObject);
                for(int i=0;i<nodes.Length;i++){
                    var node=Node(route,"Factory landing "+i,nodes[i]);node.gameObject.AddComponent<ClimbStep>().arrivalRadius=i==6?.3f:.7f;
                    if(i==4)markersA.Add(node);if(i==12)markersB.Add(node);
                }
            }
            movingA.routeMarkers=markersA.ToArray();movingB.routeMarkers=markersB.ToArray();
            roster.ApplyLayout(6);session.fallHeight=-4.3f;
            GameObject.Find("Bell plinth").transform.position=new Vector3(-7.8f,1.95f,30.5f);
            session.bellTarget.position=new Vector3(-7.8f,2.3f,30.5f);
            var ring=GameObject.Find("RING");if(ring)ring.SetActive(false);
            var floor=GameObject.Find("Pit floor").transform;floor.position=new Vector3(0,-6.2f,11);floor.localScale=new Vector3(36,.4f,48);
            BuildRoom();
            CourseMapsBuilder.InstallInCurrentScene("factory");
            var map=CourseMapsBuilder.Catalog.maps.Single(m=>m.id=="factory");map.scenePath=FactoryCourse.ScenePath;map.playable=true;EditorUtility.SetDirty(map);

            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            EditorBuildSettings.scenes=CourseMapsBuilder.BuildScenePaths().Select(p=>new EditorBuildSettingsScene(p,true)).ToArray();Capture();
        }
        public static void InstallCheckpoint(CourseSession session,BoxCollider landing=null)
        {
            if(!landing)landing=GameObject.Find("Maintenance landing").GetComponent<BoxCollider>();
            var cp=session.checkpoints;
            var spawn=Node(cp.transform,"Factory maintenance checkpoint",new Vector3(10,.725f,23.5f));spawn.rotation=Quaternion.Euler(0,270,0);
            cp.points=new[]{new CourseCheckpoints.Point{spawn=spawn,landing=landing,resumeWaypoint=9}};
            var line=new GameObject("Checkpoint outline").AddComponent<LineRenderer>();line.transform.SetParent(cp.transform);line.loop=true;line.positionCount=4;line.widthMultiplier=.055f;
            line.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Checkpoint.mat");
            line.SetPositions(new[]{new Vector3(8.6f,.74f,22.4f),new Vector3(11.4f,.74f,22.4f),new Vector3(11.4f,.74f,24.6f),new Vector3(8.6f,.74f,24.6f)});
        }
        static Transform Node(Transform parent,string name,Vector3 at){var t=new GameObject(name).transform;t.SetParent(parent);t.position=at;return t;}
        static Material Mat(string name,Color color,float metallic,bool emission=false)
        {
            string path=Root+"/Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m){m=new Material(Shader.Find("Standard"));AssetDatabase.CreateAsset(m,path);}
            m.color=color;m.SetFloat("_Metallic",metallic);m.SetFloat("_Glossiness",.32f);if(emission){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",color*.4f);}EditorUtility.SetDirty(m);return m;
        }
        static Transform Box(Transform parent,string name,Vector3 at,Vector3 size,Material mat,bool solid=false)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent);go.transform.position=at;go.transform.localScale=new Vector3(size.x/parent.lossyScale.x,size.y/parent.lossyScale.y,size.z/parent.lossyScale.z);go.GetComponent<Renderer>().sharedMaterial=mat;if(!solid)Object.DestroyImmediate(go.GetComponent<Collider>());return go.transform;
        }
        static Transform Platform(string name,Vector3 top,Vector2 size)
        {
            var go=new GameObject(name);go.transform.SetParent(root);go.transform.position=top-Vector3.up*.2f;
            go.AddComponent<BoxCollider>().size=new Vector3(size.x,.4f,size.y);
            Place("LandingPlatform",go.transform,Vector3.zero,new Vector3(size.x,.4f,size.y));
            return go.transform;
        }
        static FactorySurface Belt(string name,Vector3 top,Vector2 size,Vector3 velocity)
        {
            var t=Platform(name,top,size);var surface=t.gameObject.AddComponent<FactorySurface>();surface.origin=t.position;surface.beltVelocity=velocity;
            Box(t,"Rubber belt",top+Vector3.up*.012f,new Vector3(size.x-.15f,.02f,size.y-.12f),rubber);
            for(float z=-size.y/2+.12f;z<size.y/2;z+=.35f)Box(t,"Belt rib",top+new Vector3(0,.028f,z),new Vector3(size.x-.17f,.025f,.03f),steel);
            foreach(float end in new[]{-1f,1f}){
                var roller=GameObject.CreatePrimitive(PrimitiveType.Cylinder);roller.name="Conveyor roller";roller.transform.SetParent(t);roller.transform.position=top+new Vector3(0,-.18f,end*size.y/2);roller.transform.rotation=Quaternion.Euler(0,0,90);roller.transform.localScale=new Vector3(.32f,size.x*.5f,.32f);roller.GetComponent<Renderer>().sharedMaterial=steel;Object.DestroyImmediate(roller.GetComponent<Collider>());
            }
            surface.beltLength=(Mathf.Abs(velocity.x)>.1f?size.x:size.y)-1;
            surface.beltMarkers=new Transform[3];
            for(int i=0;i<3;i++){
                var group=Node(t,"Moving belt arrow",top+Vector3.up*.05f);surface.beltMarkers[i]=group;
                var pos=group.position;var rotation=Quaternion.LookRotation(velocity);
                foreach(int sign in new[]{-1,1}){
                    var stripe=Box(group,"Arrow stroke",pos+rotation*new Vector3(sign*.25f,0,0),new Vector3(.09f,.02f,.7f),amber);
                    stripe.rotation=rotation*Quaternion.Euler(0,-sign*45,0);
                }
                group.localPosition=Vector3.up*.25f+velocity.normalized*(i-1)*surface.beltLength/3;
            }
            return surface;
        }
        static void Rails(Vector3 centre,Vector2 size)
        {
            foreach(float side in new[]{-1f,1f})Box(root,"Shuttle guide rail",centre+new Vector3(side*(size.x/2+.1f),-.4f,0),new Vector3(.12f,.17f,size.y),steel);
            Place("UtilityPipes",root,centre+Vector3.down*1.1f,new Vector3(size.x,1.1f,size.y));
        }
        static FactoryPress Press(Vector3 at,float phase)
        {
            var t=Node(root,"Hydraulic press",at);var press=t.gameObject.AddComponent<FactoryPress>();press.phase=phase;
            foreach(int side in new[]{-1,1}){
                Box(t,"Press upright",at+new Vector3(side*3,2.65f,0),new Vector3(.35f,5.3f,.65f),steel,true);
                Place("UtilityPipes",t,new Vector3(side*3,2.6f,.4f),new Vector3(.35f,4,.4f));
            }
            Box(t,"Press crossbeam",at+Vector3.up*5.2f,new Vector3(6.4f,.5f,1.3f),steel);
            press.raised=at+Vector3.up*3.8f;press.stroke=3.25f;
            press.head=Box(t,"Press head",press.raised,new Vector3(3.5f,.8f,1.1f),dark,true);press.headCollider=press.head.GetComponent<BoxCollider>();
            Box(press.head,"Press hazard band",press.raised+new Vector3(0,-.2f,-.56f),new Vector3(3.5f,.15f,.025f),amber);
            // Reparent while preserving world scale, so a piston and stripe follow the head.
            Box(press.head,"Piston",press.raised+Vector3.up*.9f,new Vector3(.35f,1.4f,.35f),steel);
            press.warning=Box(t,"Press signal",at+new Vector3(-2.4f,3.1f,-.2f),new Vector3(.2f,.35f,.2f),amber).GetComponent<Renderer>();
            for(int i=-3;i<=3;i++)Box(t,"Hazard threshold",at+new Vector3(i*.6f,.025f,-.75f),new Vector3(.32f,.02f,.12f),amber);
            return press;
        }
        static Transform Place(string key,Transform parent,Vector3 local,Vector3 size,float yaw=0)
        {
            var visual=(GameObject)PrefabUtility.InstantiatePrefab(EnvironmentAssetImporter.Prefab(key));visual.transform.SetParent(parent,false);
            bool first=true;Bounds bounds=default;
            foreach(var f in visual.GetComponentsInChildren<MeshFilter>()){
                var b=f.sharedMesh.bounds;for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2){
                    var point=visual.transform.InverseTransformPoint(f.transform.TransformPoint(b.center+Vector3.Scale(b.extents,new Vector3(x,y,z))));if(first){bounds=new Bounds(point,Vector3.zero);first=false;}else bounds.Encapsulate(point);
                }
            }
            visual.transform.localRotation=Quaternion.Euler(0,yaw,0);visual.transform.localScale=new Vector3(size.x/bounds.size.x,size.y/bounds.size.y,size.z/bounds.size.z);
            visual.transform.localPosition=local-visual.transform.localRotation*Vector3.Scale(bounds.center,visual.transform.localScale);
            foreach(var c in visual.GetComponentsInChildren<Collider>())Object.DestroyImmediate(c);return visual.transform;
        }
        static void BuildRoom()
        {
            for(int x=-4;x<=4;x++)for(int z=-3;z<=8;z++)Place("ConcreteFloor",root,new Vector3(x*4,-6.05f,z*4),new Vector3(4.03f,.18f,4.03f));
            foreach(int side in new[]{-1,1})for(int z=-2;z<=8;z++)Place("ConcreteWall",root,new Vector3(side*18,3,z*4),new Vector3(4.03f,18,.3f),90);
            for(int x=-4;x<=4;x++)Place("ConcreteWall",root,new Vector3(x*4,3,34),new Vector3(4.03f,18,.3f));
            for(int z=-4;z<=32;z+=6){
                foreach(int side in new[]{-1,1}){
                    Place("ConcreteColumn",root,new Vector3(side*17.4f,3,z),new Vector3(.7f,18,.7f));
                    Place("UtilityPipes",root,new Vector3(side*16.8f,4,z),new Vector3(.8f,5,.5f),90);
                }
                Box(root,"Roof truss",new Vector3(0,10,z),new Vector3(35,.45f,.35f),steel);
            }
            for(int x=-12;x<=12;x+=12)for(int z=4;z<=28;z+=12){
                Place("CeilingLight",root,new Vector3(x,8.6f,z),new Vector3(1.3f,.35f,.7f));
                var light=new GameObject("Factory warm fixture").AddComponent<Light>();light.transform.SetParent(root);light.transform.position=new Vector3(x,8.2f,z);light.type=LightType.Point;light.range=17;light.intensity=1.8f;light.color=new Color(1,.72f,.42f);
            }
            // Background storage and pipes give scale without adding playable bypasses.
            foreach(float x in new[]{-14f,15f})for(int z=9;z<30;z+=8)Place("UtilityPipes",root,new Vector3(x,-2,z),new Vector3(2.5f,7,2.5f));
            RenderSettings.ambientLight=new Color(.24f,.28f,.27f);RenderSettings.fog=true;RenderSettings.fogColor=new Color(.095f,.12f,.115f);RenderSettings.fogStartDistance=30;RenderSettings.fogEndDistance=75;
        }
        public static void Capture()
        {
            EditorSceneManager.OpenScene(FactoryCourse.ScenePath);Directory.CreateDirectory("Previews");
            var camera=new GameObject("Factory overview").AddComponent<Camera>();camera.cullingMask=~(1<<CharacterMotionDriver.HandsLayer);camera.fieldOfView=72;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=RenderSettings.fogColor;
            camera.transform.position=new Vector3(-16,8,-4);camera.transform.LookAt(new Vector3(1,.8f,19));CharacterAssetBuilder.Render(camera,null,"Previews/Factory_Overview.png");
            camera.transform.position=new Vector3(6,3,11.8f);camera.transform.LookAt(new Vector3(10,2,20));CharacterAssetBuilder.Render(camera,null,"Previews/Factory_Presses.png");
            camera.transform.position=new Vector3(0,2.1f,6);camera.transform.LookAt(new Vector3(3,.7f,13));CharacterAssetBuilder.Render(camera,null,"Previews/Factory_Entry.png");
            Object.DestroyImmediate(camera.gameObject);
            File.Copy("Previews/Factory_Overview.png",Root+"/Resources/UI/Maps/Factory.png",true);AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(FactoryCourse.ScenePath);
        }
        public static void Validate(bool probe=false)
        {
            EditorSceneManager.OpenScene(FactoryCourse.ScenePath);new GameObject("Factory review").AddComponent<FactoryValidationRunner>().probeOnly=probe;
            PlayerSettings.runInBackground=true;EditorApplication.isPaused=false;EditorApplication.isPlaying=true;
        }
    }
}
