using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalliGalli.Movement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HalliGalli.EditorTools
{
    public static class SurfCourseBuilder
    {
        public const string Root = MovementProjectBuilder.Root;
        public const string ScenePath = Root + "/Demo/SurfCourse.unity";
        public const string ProfilePath = Root + "/Settings/SurfMovement.asset";
        const string MeshFolder = Root + "/Art/Surf";

        [MenuItem("HalliGalli/Maps/Build Surf Course")]
        public static void Build()
        {
            if (File.Exists(ScenePath)) {
                EditorSceneManager.OpenScene(ScenePath);
                string backups = Root + "/Demo/Backups"; Directory.CreateDirectory(backups);
                EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), backups + "/SurfCourse_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".unity", true);
            }
            EditorSceneManager.OpenScene(CourseBuilder.ScenePath);
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene, ScenePath);
            var session = Object.FindAnyObjectByType<CourseSession>();
            TutorialFerryBuilder.RemoveFromTemplate(session);
            var roster = session.roster;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)) {
                if (!t) continue;
                if ((t.name.StartsWith("Shared pad ") || t.name == "Edge stripe" || t.name == "Platform support") && t.position.z > 7
                    || t.name == "Wall buttress" || new[] { "03", "04", "05", "06", "07" }.Contains(t.name))
                    Object.DestroyImmediate(t.gameObject);
            }
            var root = new GameObject("Surf course").transform;
            var course = root.gameObject.AddComponent<SurfCourse>(); roster.surf = course; roster.branches = null;
            Directory.CreateDirectory(MeshFolder); AssetDatabase.Refresh();
            var concrete = EnvironmentAssetImporter.Prefab("ConcreteFloor").GetComponentInChildren<MeshRenderer>().sharedMaterial;
            var accent = Accent();
            // Three shared recovery decks separate momentum sections. No teleports, boosts or speed caps.
            course.sections = new[] {
                Straight(new Vector3(0,-.6f,8.5f),new Vector3(0,-4.8f,30.5f),0,new Vector3(0,.375f,6.1f)),
                Straight(new Vector3(0,-12,29),new Vector3(0,-18,8),180,new Vector3(0,-11.175f,31.5f)),
                Curve(new Vector3(0,-18,8),180,14,-20,-17.5f,6),
                Straight(new Vector3(-31,-18.3f,-6),new Vector3(-66,-25.3f,-6),270,new Vector3(-28.5f,-17.175f,-6)),
                Curve(new Vector3(-66,-25.3f,-6),270,18,-28,0,0),
                Straight(new Vector3(-84,-28,12),new Vector3(-84,-32,36),0,Vector3.zero),
                Curve(new Vector3(-84,-32,36),0,18,-35,-32.5f,8),
                Straight(new Vector3(-43,-33.3f,60.4f),new Vector3(-7,-39.8f,60.4f),90,new Vector3(-45.5f,-32.175f,60.4f),1),
                Curve(new Vector3(-7,-39.8f,60.4f),90,23,-42.8f,0,0,-70),
                // The final transfer is open air; entering too low loses the next bank.
                Straight(new Vector3(14.613f,-42.8f,75.534f),new Vector3(24.19f,-48.3f,101.845f),20,Vector3.zero,1),
                Curve(new Vector3(25.90f,-51,106.544f),20,22,-54,-51,8,-110)
            };
            course.sections[0].landing=new Vector3(3.2f,-11.175f,34);
            course.sections[0].landingName="Surf turn landing";
            course.sections[2].landing=new Vector3(-26,-17.175f,-9.2f);
            course.sections[2].landingName="Surf midway landing";
            course.sections[6].landing=new Vector3(-48,-32.175f,57.2f);
            course.sections[6].landingName="Surf transfer landing";
            course.sections[9].halfWidth=2.4f;
            course.sections[10].halfWidth=2.4f;
            for (int segment=0;segment<course.sections.Length;segment++)
                BuildRamp(root,"Surf ramp "+(segment+1),course.sections[segment].side,course.sections[segment],concrete,accent);
            Platform(root,"Surf turn landing",new Vector3(0,-11.4f,35.5f),new Vector3(12,.4f,10));
            Platform(root,"Surf midway landing",new Vector3(-26,-17.4f,-6),new Vector3(7,.4f,12));
            Platform(root,"Surf transfer landing",new Vector3(-48,-32.4f,56.5f),new Vector3(9,.4f,12));
            Vector3 exit=course.sections.Last().points.Last();
            Vector3 finish=exit+new Vector3(-10.5f,.525f,3.2f);
            Platform(root,"Surf finish",new Vector3(finish.x,finish.y-.225f,exit.z),new Vector3(9,.4f,12));
            course.finish=Node(root,"Bell landing",finish);
            for (int i = 0; i < roster.routeRoots.Length; i++) {
                var route = roster.routeRoots[i];
                foreach (var old in route.Cast<Transform>().ToArray()) Object.DestroyImmediate(old.gameObject);
                for (int j = 0; j < 5; j++) Node(route, "Landing " + j, Vector3.zero);
            }
            roster.ApplyLayout(6);
            GameObject.Find("Bell plinth").transform.position = finish+new Vector3(-1,.325f,0);
            session.bellTarget.position = finish+new Vector3(-1,.675f,0);
            GameObject.Find("RING").transform.position = finish+new Vector3(-.67f,.375f,0);
            GameObject.Find("RING").transform.rotation = Quaternion.Euler(0, 90, 0);
            session.fallHeight = -65;
            var floor = GameObject.Find("Pit floor").transform;
            floor.position = new Vector3(-30,-67.2f,55); floor.localScale=new Vector3(142,.4f,174);
            foreach (string wallName in new[] { "Left arena wall", "Right arena wall", "Far arena wall" }) {
                var wall = GameObject.Find(wallName).transform;
                wall.gameObject.SetActive(false);
            }
            var profile = AssetDatabase.LoadAssetAtPath<SourceMovementProfile>(ProfilePath);
            if (!profile) { profile = Object.Instantiate(AssetDatabase.LoadAssetAtPath<SourceMovementProfile>(Root + "/Settings/DefaultMovement.asset")); AssetDatabase.CreateAsset(profile, ProfilePath); }
            profile.surfEnabled = true; profile.airAcceleration = 80; profile.airWishSpeedCap = .8f; profile.maximumAirSpeed = 0;
            EditorUtility.SetDirty(profile);
            CourseMapsBuilder.InstallInCurrentScene("surf");
            var map = CourseMapsBuilder.Catalog.maps.Single(m => m.id == "surf");
            map.displayName = "서프 수로"; map.description = "낙하 반전, 좌우 뱅크 전환, 끊어진 경사면. 속도를 살려 마지막 도약까지.";
            map.scenePath = ScenePath; map.playable = true; map.movementProfile = profile; EditorUtility.SetDirty(map);
            foreach (var p in roster.allParticipants) p.GetComponent<SourceMotor>().profile = profile;
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            EditorBuildSettings.scenes = CourseMapsBuilder.BuildScenePaths().Select(p => new EditorBuildSettingsScene(p, true)).ToArray();
            EnvironmentArtBuilder.InstallSurf();
            EditorSceneManager.OpenScene(ScenePath);CheckpointBuilder.InstallCurrent();SurfDividerBuilder.InstallCurrent();EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Capture();
        }

        internal static SurfCourse.Section Straight(Vector3 start,Vector3 end,float yaw,Vector3 entry,int side=-1)
            =>new SurfCourse.Section{points=new[]{start,end},headings=new[]{yaw,yaw},entry=entry,side=side};
        internal static SurfCourse.Section Curve(Vector3 start,float yaw,float radius,float bottom,float exitY,float kicker,float turn=90)
        {
            var rotation=Quaternion.Euler(0,yaw,0);var points=new List<Vector3>();var angles=new List<float>();
            int steps=Mathf.CeilToInt(Mathf.Abs(turn)*.6f);
            for(int i=0;i<=steps;i++){
                float t=i/(float)steps,a=t*Mathf.Abs(turn)*Mathf.Deg2Rad;
                points.Add(start+rotation*new Vector3(Mathf.Sign(turn)*radius*(1-Mathf.Cos(a)),Mathf.Lerp(0,bottom-start.y,Mathf.SmoothStep(0,1,t)),radius*Mathf.Sin(a)));
                angles.Add(yaw+turn*t);
            }
            Vector3 end=points.Last();
            if(kicker>0)for(int i=1;i<=24;i++){
                float t=i/24f;points.Add(end+Quaternion.Euler(0,yaw+turn,0)*Vector3.forward*kicker*t+Vector3.up*(exitY-bottom)*t*t);angles.Add(yaw+turn);
            }
            return new SurfCourse.Section{points=points.ToArray(),headings=angles.ToArray(),side=-(int)Mathf.Sign(turn)};
        }
        static Transform Node(Transform parent, string name, Vector3 position)
        {
            var t = new GameObject(name).transform; t.SetParent(parent); t.position = position; return t;
        }
        static void Platform(Transform root, string name, Vector3 position, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            go.transform.SetParent(root); go.transform.position = position; go.transform.localScale = size;
        }
        static Material Accent()
        {
            string path = MeshFolder + "/SurfEdge.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
            m.color = new Color(.56f, .38f, .9f); m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", new Color(.3f, .12f, .65f));
            EditorUtility.SetDirty(m); return m;
        }
        internal static void BuildRamp(Transform parent, string name, int side, SurfCourse.Section section, Material material, Material accent,string meshFolder=null)
        {
            if(section.twoSided){BuildTriangle(parent,name,section,material,accent,meshFolder);return;}
            float slope = Mathf.Tan(55 * Mathf.Deg2Rad);
            float low=3.2f-section.halfWidth, high=3.2f+section.halfWidth;
            Vector3 At(float x, int i) => section.points[i] + Quaternion.Euler(0, section.headings[i], 0) * Vector3.right * side * x + Vector3.up * (x - 3.2f) * slope;
            var down = Vector3.down * .5f;
            var vertices = new List<Vector3>(); var triangles = new List<int>(); var uv = new List<Vector2>();
            for (int i = 0; i < section.points.Length - 1; i++) {
                var a = At(low,i); var b = At(high,i); var c = At(high,i+1); var d = At(low,i+1);
                var right = Quaternion.Euler(0,section.headings[i],0)*Vector3.right*side;
                Quad(vertices,triangles,uv,a,b,c,d,Vector3.up);
                Quad(vertices,triangles,uv,a+down,d+down,c+down,b+down,Vector3.down);
                Quad(vertices,triangles,uv,a,d,d+down,a+down,-right);
                Quad(vertices,triangles,uv,b,b+down,c+down,c,right);
                if(i==0)Quad(vertices,triangles,uv,a,a+down,b+down,b,-(Quaternion.Euler(0,section.headings[i],0)*Vector3.forward));
                if(i==section.points.Length-2)Quad(vertices,triangles,uv,d,c,c+down,d+down,Quaternion.Euler(0,section.headings[i+1],0)*Vector3.forward);
            }
            // The generated floor uses an atlas. Map to its intact top-face island.
            for (int i = 0; i < uv.Count; i++) uv[i] = new Vector2(.54f, .54f) + uv[i] * .44f;
            var mesh = SaveMesh(name, vertices, triangles, uv,meshFolder);
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            go.transform.SetParent(parent); go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = material; go.GetComponent<MeshCollider>().sharedMesh = mesh;
            vertices.Clear(); triangles.Clear(); uv.Clear();
            foreach (float x in new[] { low+.05f, high-.15f }) for(int i=0;i<section.points.Length-1;i++)
                Quad(vertices, triangles, uv, At(x, i) + Vector3.up * .015f, At(x + .1f, i) + Vector3.up * .015f,
                    At(x + .1f, i+1) + Vector3.up * .015f, At(x, i+1) + Vector3.up * .015f, Vector3.up);
            int last=section.points.Length-1;
            // A visible strip marks each required passage, without collision or a speed bonus.
            Quad(vertices,triangles,uv,At(low,last)+Vector3.up*.02f,At(high,last)+Vector3.up*.02f,
                At(high,last)+Vector3.up*.02f-(Quaternion.Euler(0,section.headings[last],0)*Vector3.forward*.18f),
                At(low,last)+Vector3.up*.02f-(Quaternion.Euler(0,section.headings[last],0)*Vector3.forward*.18f),Vector3.up);
            var edge = new GameObject(name + " edge", typeof(MeshFilter), typeof(MeshRenderer));
            edge.transform.SetParent(parent); edge.GetComponent<MeshFilter>().sharedMesh = SaveMesh(name + " edge", vertices, triangles, uv,meshFolder);
            edge.GetComponent<MeshRenderer>().sharedMaterial = accent;
        }
        static void BuildTriangle(Transform parent,string name,SurfCourse.Section section,Material material,Material accent,string folder)
        {
            float slope=Mathf.Tan(55*Mathf.Deg2Rad),width=3.2f+section.halfWidth;
            Vector3 At(float x,int i)=>section.points[i]+Quaternion.Euler(0,section.headings[i],0)*Vector3.right*x+Vector3.up*(3.2f-Mathf.Abs(x))*slope;
            var vertices=new List<Vector3>();var triangles=new List<int>();var uv=new List<Vector2>();
            int last=section.points.Length-1;
            for(int i=0;i<last;i++){
                var a=At(-width,i);var b=At(0,i);var c=At(width,i);
                var d=At(-width,i+1);var e=At(0,i+1);var f=At(width,i+1);
                Quad(vertices,triangles,uv,a,b,e,d,Vector3.up);
                Quad(vertices,triangles,uv,b,c,f,e,Vector3.up);
                Quad(vertices,triangles,uv,a,d,f,c,Vector3.down);
            }
            void Cap(int i,Vector3 outward){
                var a=At(-width,i);var b=At(0,i);var c=At(width,i);
                if(Vector3.Dot(Vector3.Cross(b-a,c-a),outward)<0){var swap=b;b=c;c=swap;}
                int n=vertices.Count;vertices.AddRange(new[]{a,b,c});uv.AddRange(new[]{Vector2.zero,Vector2.up,Vector2.right});triangles.AddRange(new[]{n,n+1,n+2});
            }
            Cap(0,-(Quaternion.Euler(0,section.headings[0],0)*Vector3.forward));
            Cap(last,Quaternion.Euler(0,section.headings[last],0)*Vector3.forward);
            var mesh=SaveMesh(name,vertices,triangles,uv,folder);
            var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer),typeof(MeshCollider));go.transform.SetParent(parent);
            go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=material;go.GetComponent<MeshCollider>().sharedMesh=mesh;
            vertices.Clear();triangles.Clear();uv.Clear();
            foreach(float x in new[]{-width+.08f,-.1f,0,width-.18f})for(int i=0;i<last;i++)
                Quad(vertices,triangles,uv,At(x,i)+Vector3.up*.02f,At(x+.1f,i)+Vector3.up*.02f,At(x+.1f,i+1)+Vector3.up*.02f,At(x,i+1)+Vector3.up*.02f,Vector3.up);
            var edge=new GameObject(name+" edge",typeof(MeshFilter),typeof(MeshRenderer));edge.transform.SetParent(parent);
            edge.GetComponent<MeshFilter>().sharedMesh=SaveMesh(name+" edge",vertices,triangles,uv,folder);edge.GetComponent<MeshRenderer>().sharedMaterial=accent;
        }
        static void Quad(List<Vector3> vertices, List<int> triangles, List<Vector2> uv, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0) { var swap = b; b = d; d = swap; }
            int i = vertices.Count; vertices.AddRange(new[] { a, b, c, d });
            uv.AddRange(new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up });
            triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
        }
        static Mesh SaveMesh(string name, List<Vector3> vertices, List<int> triangles, List<Vector2> uv,string folder=null)
        {
            string path = (folder??MeshFolder) + "/" + name.Replace(" ", "_") + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!mesh) { mesh = new Mesh { name = name }; AssetDatabase.CreateAsset(mesh, path); }
            mesh.Clear(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetUVs(0, uv);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.RecalculateTangents(); EditorUtility.SetDirty(mesh); return mesh;
        }
        public static void Validate(bool probe = false)
        {
            EditorSceneManager.OpenScene(ScenePath);
            new GameObject("Surf validation").AddComponent<SurfRevisionValidationRunner>().probeOnly = probe;
            PlayerSettings.runInBackground = true;
            EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Focus();
            EditorApplication.isPaused = false; EditorApplication.isPlaying = true;
        }
        [MenuItem("HalliGalli/Maps/Capture Surf Course")]
        public static void Capture()
        {
            EditorSceneManager.OpenScene(ScenePath); Directory.CreateDirectory("Previews");
            var electricity = Object.FindAnyObjectByType<SurfDividerElectricity>();
            if (electricity) electricity.Apply(1.25f);
            var camera = new GameObject("Surf overview").AddComponent<Camera>();
            camera.cullingMask = ~(1 << CharacterMotionDriver.HandsLayer); camera.fieldOfView = 75;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = RenderSettings.fogColor;
            // Cutaway only for the preview camera, restored immediately; gameplay keeps the room intact.
            var shell=GameObject.Find("VARCO environment").transform.Cast<Transform>()
                .Where(t=>t.name.Contains("ConcreteWall")||t.name.Contains("CeilingLight")||t.position.y>14).ToArray();
            foreach(var t in shell)t.gameObject.SetActive(false);
            var bulkheads=GameObject.Find("Surf bulkheads");if(bulkheads)bulkheads.SetActive(false);
            bool previewFog=RenderSettings.fog;RenderSettings.fog=false;
            camera.fieldOfView=55;
            camera.transform.position = new Vector3(96,92,-70); camera.transform.LookAt(new Vector3(-26,-25,60));
            CharacterAssetBuilder.Render(camera, null, "Previews/Surf_Overview.png");
            camera.orthographic=true;camera.orthographicSize=92;
            camera.transform.position=new Vector3(-30,110,65);camera.transform.rotation=Quaternion.Euler(90,0,0);
            CharacterAssetBuilder.Render(camera,null,"Previews/Surf_Layout.png");
            camera.orthographic=false;camera.fieldOfView=75;
            RenderSettings.fog=previewFog;
            foreach(var t in shell)t.gameObject.SetActive(true);
            if(bulkheads)bulkheads.SetActive(true);
            camera.transform.position=new Vector3(0,1.65f,-.7f);camera.transform.LookAt(new Vector3(0,-3,25));
            CharacterAssetBuilder.Render(camera,null,"Previews/Surf_StartBarriers.png");
            camera.transform.position=new Vector3(0,-4,37);camera.transform.LookAt(new Vector3(-15,-15,44));
            CharacterAssetBuilder.Render(camera,null,"Previews/Surf_SectorBarrier.png");
            camera.transform.position = new Vector3(17,7,43); camera.transform.LookAt(new Vector3(0,-8,20));
            CharacterAssetBuilder.Render(camera, null, "Previews/Surf_Divider.png");
            camera.transform.position = new Vector3(0, 2.05f, 6); camera.transform.LookAt(new Vector3(0, -5, 24));
            CharacterAssetBuilder.Render(camera, null, "Previews/Surf_Entry.png");
            camera.transform.position = new Vector3(9,-.5f,9); camera.transform.LookAt(new Vector3(9,-5.2f,18));
            CharacterAssetBuilder.Render(camera, null, "Previews/Surf_Electricity.png");
            camera.transform.position = new Vector3(-43,-15,21); camera.transform.LookAt(new Vector3(-77,-30,38));
            CharacterAssetBuilder.Render(camera, null, "Previews/Surf_Ramp.png");
            var surf=Object.FindAnyObjectByType<SurfCourse>();var last=surf.sections.Last();
            Vector3 launch=last.points.Last();
            camera.transform.position = launch+new Vector3(24,18,-12); camera.transform.LookAt(launch+new Vector3(-3,0,3));
            CharacterAssetBuilder.Render(camera,null,"Previews/Surf_Launch.png");
            camera.transform.position = last.points[0]+new Vector3(14,16,-9); camera.transform.LookAt(last.points[0]);
            CharacterAssetBuilder.Render(camera,null,"Previews/Surf_Transfer.png");
            var driver = GameObject.Find("Player").GetComponent<CharacterMotionDriver>();
            var approach=surf.sections[surf.sections.Length-2];var aim=Quaternion.Euler(0,approach.headings.Last(),0);
            driver.worldCamera.transform.position = approach.points.Last()+aim*new Vector3(3.2f,2.2f,-3);
            driver.worldCamera.transform.rotation = Quaternion.Euler(12,approach.headings.Last(),0);
            var pose = driver.motions.handClips.FirstOrDefault(c => c.name == "Fall");
            if (pose) pose.SampleAnimation(driver.handsModel.gameObject, .15f);
            driver.handCamera.enabled = true;
            CharacterAssetBuilder.Render(driver.worldCamera, driver.handCamera, "Previews/Surf_FirstPerson.png");
            Object.DestroyImmediate(camera.gameObject);
            File.Copy("Previews/Surf_Overview.png",Root+"/Resources/UI/Maps/Surf.png",true);AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(ScenePath);
        }
    }
}
