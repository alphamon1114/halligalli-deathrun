using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HalliGalli.Movement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HalliGalli.EditorTools
{
    public static class EnvironmentArtBuilder
    {
        const string ArtRootName = "VARCO environment";
        const string VisualName = "VARCO visual";
        static Transform art;
        static T[] All<T>() where T : Object => Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        [MenuItem("HalliGalli/Environment/Install Interior and Mountain Art")]
        public static void Install() => InstallScenes(new[] { CourseBuilder.ScenePath, CourseMapsBuilder.ClimbScene });
        public static void InstallClimb() => InstallScenes(new[] { CourseMapsBuilder.ClimbScene });
        public static void InstallSurf() => InstallScenes(new[] { SurfCourseBuilder.ScenePath });
        static void InstallScenes(string[] scenes)
        {
            foreach (var key in EnvironmentAssetImporter.Keys)
                if (!EnvironmentAssetImporter.Prefab(key)) throw new FileNotFoundException("Import VARCO prefab first: " + key);
            Directory.CreateDirectory("TestResults");
            var report = new List<string>();
            foreach (var path in scenes) {
                EditorSceneManager.OpenScene(path);
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                string backup = MovementProjectBuilder.Root + "/Demo/Backups";
                Directory.CreateDirectory(backup);
                EditorSceneManager.SaveScene(scene, backup + "/" + scene.name + "_BeforeEnvironment_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".unity", true);
                string before = GameplaySignature();
                try {
                    bool mountain = path == CourseMapsBuilder.ClimbScene;
                    InstallScene(mountain, path == SurfCourseBuilder.ScenePath);
                    Physics.SyncTransforms();
                    if (before != GameplaySignature()) throw new InvalidOperationException("Environment installation changed gameplay geometry");
                    if (art.GetComponentsInChildren<Collider>(true).Length != 0) throw new InvalidOperationException("Decoration must not add colliders");
                    foreach (var renderer in All<MeshRenderer>().Where(r => r.enabled && IsArt(r.transform)))
                        if (renderer.sharedMaterials.Any(m => !m || !m.shader || m.shader.name == "Hidden/InternalErrorShader"))
                            throw new InvalidDataException("Missing environment material: " + renderer.name);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    report.Add(scene.name + ": PASS - original colliders, starts, cards and route anchors unchanged");
                } catch {
                    EditorSceneManager.OpenScene(path);
                    throw;
                }
            }
            File.WriteAllLines(scenes.Length == 1 ? scenes[0] == SurfCourseBuilder.ScenePath ? "TestResults/surf-environment-install.txt" : "TestResults/climb-environment-install.txt" : "TestResults/environment-install.txt", report);
            AssetDatabase.SaveAssets();
            Capture();
            EditorSceneManager.OpenScene(CourseMapsBuilder.ClimbScene);
        }

        static void InstallScene(bool mountain, bool surf = false)
        {
            foreach (var t in All<Transform>().Where(t => t.name == ArtRootName || t.name == VisualName).ToArray())
                if (t) Object.DestroyImmediate(t.gameObject);
            art = new GameObject(ArtRootName).transform;
            var session = Object.FindAnyObjectByType<CourseSession>();
            var roster = session.roster;
            foreach (var chair in roster.chairs) {
                Hide(chair);
                Place("WorkshopChair", chair, new Vector3(0, .5f, 0), new Vector3(.66f, 1, .64f), Quaternion.identity, VisualName);
            }
            foreach (var t in All<Transform>().Where(t => t.name == "Table top" || t.name == "Table felt" || t.name == "Table leg")) Hide(t, false);
            Place("CardTable", art, new Vector3(0, .441f, -5.1f), new Vector3(5.5f, .882f, 5.2f), Quaternion.identity);
            Hide(session.bellTarget);
            Place("ServiceBell", session.bellTarget, new Vector3(0, .15f, 0), new Vector3(.57f, .3f, .57f), Quaternion.identity, VisualName);
            foreach (var t in All<Transform>().Where(t => t.name == "Bell plinth")) Replace(t, mountain ? "MountainRock" : "ConcreteColumn");
            foreach (var t in All<Transform>().Where(t => t.name == "Table deck")) Replace(t, mountain ? "RockLedge" : "ConcreteFloor");
            foreach (var t in All<Transform>().Where(t => t.name == "Surf finish" || t.name == "Surf turn landing" || t.name == "Surf midway landing" || t.name == "Surf transfer landing")) Replace(t, "ConcreteFloor");
            foreach (var t in All<Transform>().Where(t => t.name == "Landing surface" || t.name.StartsWith("Shared pad ") || t.name.StartsWith("Climb landing ")))
                Replace(t, mountain ? "RockLedge" : "LandingPlatform");
            foreach (var t in All<Transform>().Where(t => t.name == "Tower core")) Replace(t, "MountainRock");

            string[] strips = { "Pit warning", "Wall light", "Cliff support" };
            foreach (var t in All<Transform>().Where(t => strips.Contains(t.name))) Hide(t, false);
            foreach (var t in All<Transform>().Where(t => t.name == "Platform support" || t.name == "Support")) {
                if (mountain) Hide(t, false);
                else Replace(t, "ConcreteColumn");
            }
            foreach (var t in All<Transform>().Where(t => t.name == "Pit floor" || t.name.EndsWith("arena wall") || t.name == "Wall buttress")) Hide(t, false);
            foreach (var text in All<TextMesh>().Where(t => t.name == "BELL RUN" || t.name == "JUMP  /  BRAWL  /  RING"))
                text.GetComponent<Renderer>().enabled = false;
            if (surf) SurfInterior(); else if (mountain) Mountain(); else Interior();
            Lighting(mountain);
            if (surf) { RenderSettings.fogStartDistance = 120; RenderSettings.fogEndDistance = 240; }
        }

        static bool IsArt(Transform t)
        {
            for (; t; t = t.parent) if (t.name == VisualName || t.name == ArtRootName) return true;
            return false;
        }
        static void Hide(Transform t, bool children = true)
        {
            foreach (var r in children ? t.GetComponentsInChildren<Renderer>(true) : t.GetComponents<Renderer>()) r.enabled = false;
        }
        static void Replace(Transform t, string key)
        {
            Hide(t, false);
            var box = t.GetComponent<BoxCollider>();
            Place(key, t, box ? box.center : Vector3.zero, box ? box.size : Vector3.one, Quaternion.identity, VisualName);
        }
        static Bounds LocalBounds(Transform root)
        {
            bool first = true; Bounds result = default;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true)) {
                var b = filter.sharedMesh.bounds;
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2) {
                    var point = root.InverseTransformPoint(filter.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3(x, y, z))));
                    if (first) { result = new Bounds(point, Vector3.zero); first = false; } else result.Encapsulate(point);
                }
            }
            if (first || result.size.x <= 0 || result.size.y <= 0 || result.size.z <= 0) throw new InvalidDataException("Invalid prefab bounds: " + root.name);
            return result;
        }
        internal static Transform Place(string key, Transform parent, Vector3 center, Vector3 size, Quaternion rotation, string name = null)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(EnvironmentAssetImporter.Prefab(key));
            instance.name = name ?? key;
            var root = instance.transform;
            root.SetParent(parent, false);
            var bounds = LocalBounds(root);
            var scale = new Vector3(size.x / bounds.size.x, size.y / bounds.size.y, size.z / bounds.size.z);
            root.localRotation = rotation;
            root.localScale = scale;
            root.localPosition = center - rotation * Vector3.Scale(bounds.center, scale);
            return root;
        }
        static void Prop(string key, Vector3 center, Vector3 size, float yaw = 0) => Place(key, art, center, size, Quaternion.Euler(0, yaw, 0));

        public static void RefreshTutorialShell()
        {
            art=GameObject.Find(ArtRootName).transform;
            foreach(var child in art.Cast<Transform>().ToArray())if(child.name!="CardTable")Object.DestroyImmediate(child.gameObject);
            Interior();
        }
        static void Interior()
        {
            var session=Object.FindAnyObjectByType<CourseSession>();bool extended=session&&session.ferry&&session.ferry.secondCarrier;
            // Shared models keep the large room economical; gameplay lives on the original collision surfaces.
            for (int x = 0; x < 7; x++) for (int z = 0; z < (extended?13:11); z++)
                Prop("ConcreteFloor", new Vector3(-12 + x * 4, -6.08f, -12 + z * 4), new Vector3(4.02f, .16f, 4.02f));
            foreach (int side in new[] { -1, 1 }) {
                for (int i = 0; i < (extended?12:10); i++)
                    Prop("ConcreteWall", new Vector3(side * 13, 2.75f, -10 + i * 4), new Vector3(4.02f, 17.5f, .28f), side * 90);
                for (int i = 0; i < (extended?7:6); i++) {
                    Prop("ConcreteColumn", new Vector3(side * 12.55f, 2.7f, -6 + i * 6), new Vector3(.7f, 17.4f, .55f));
                    if (i % 2 == 0) Prop("UtilityPipes", new Vector3(side * 12.25f, 2.3f, -5 + i * 6), new Vector3(.9f, 4.5f, .3f), side * 90);
                }
            }
            foreach (float z in new[] { -12f, extended?36f:28f })
                for (int i = 0; i < 7; i++) Prop("ConcreteWall", new Vector3(-12 + i * 4, 2.75f, z), new Vector3(4.02f, 17.5f, .28f), z < 0 ? 180 : 0);
            for (int x = 0; x < 7; x++) for (int z = 0; z < (extended?12:10); z++)
                Place("ConcreteFloor", art, new Vector3(-12 + x * 4, 11.55f, -10 + z * 4), new Vector3(4.02f, .2f, 4.02f), Quaternion.Euler(180, 0, 0));
            for (int i = 0; i < (extended?6:5); i++) foreach (int side in new[] { -1, 1 }) {
                Prop("CeilingLight", new Vector3(side * 4.8f, 10.9f, -5 + i * 6.5f), new Vector3(2.4f, .22f, .58f));
                Lamp(new Vector3(side * 4.8f, 9.7f, -5 + i * 6.5f), new Color(.86f, .9f, .78f), 2, 14);
            }
            Lamp(new Vector3(0, 4.8f, -5.1f), new Color(1, .9f, .76f), 1.5f, 7);
        }

        static void Mountain()
        {
            for(int x=-2;x<=2;x++)for(int z=-1;z<=3;z++)
                Prop("RockLedge",new Vector3(x*14,-9.25f+x*.003f+z*.002f,z*14),new Vector3(16,1.5f,16));
            Prop("MountainRock",new Vector3(0,-4.9f,-4.5f),new Vector3(13,8.7f,9),-35);
            var roster=Object.FindAnyObjectByType<CourseRoster>();
            foreach(var first in roster.firstPlatforms)Place("MountainRock",first,new Vector3(0,-4.7f,0),new Vector3(.85f,8.5f,1.25f),Quaternion.identity,VisualName);
            Place("MountainRock",roster.mergeSurface,new Vector3(0,-9.6f,0),new Vector3(1,18,.8f),Quaternion.identity,VisualName);
            var center=roster.branches.peakCenter;
            // One continuous climbable rock face; the VARCO kit forms its base and projecting ledges.
            Prop("MountainRock",center+new Vector3(0,-5,0),new Vector3(9,8,9),25);
            foreach(var ledge in All<Transform>().Where(t=>t.name.StartsWith("Climb landing "))){
                var bounds=ledge.GetComponent<BoxCollider>().bounds;
                Vector3 radial=Vector3.ProjectOnPlane(bounds.center-center,Vector3.up);
                float radius=radial.magnitude;if(radius<1)continue;radial/=radius;
                float inner=Mathf.Lerp(3.7f,2.1f,Mathf.Clamp01(bounds.max.y/6.75f));
                var shoulder=center+radial*((inner+radius)*.5f);shoulder.y=bounds.min.y-.9f;
                Prop("MountainRock",shoulder,new Vector3(Mathf.Min(1.4f,ledge.localScale.x*.5f),1.7f,radius-inner+.45f),Quaternion.LookRotation(radial).eulerAngles.y);
            }
            // Low foothills and trees frame the single peak without hiding the upper race.
            for(int i=0;i<10;i++){
                float angle=(i*36+12)*Mathf.Deg2Rad;
                var p=center+new Vector3(Mathf.Sin(angle)*17,-7,Mathf.Cos(angle)*17);
                Prop("MountainRock",p,new Vector3(8,3.5f,8),i*37);
                if(i%2==0)Prop("MountainPine",p+new Vector3(1,3.2f,0),new Vector3(3.8f,6,3.8f),i*51);
            }
            RenderSettings.fogStartDistance=40;RenderSettings.fogEndDistance=100;
        }
        static void SurfInterior()
        {
            var course=Object.FindAnyObjectByType<SurfCourse>();
            var session=Object.FindAnyObjectByType<CourseSession>();
            float bottom=session.fallHeight-2, ceiling=16;
            var bounds=new Bounds(new Vector3(0,0,-8),Vector3.one*16);
            foreach(var section in course.sections)foreach(var point in section.points)bounds.Encapsulate(point);
            bounds.Encapsulate(course.finish.position);
            int x0=Mathf.FloorToInt((bounds.min.x-12)/18),x1=Mathf.CeilToInt((bounds.max.x+12)/18);
            int z0=Mathf.FloorToInt((bounds.min.z-12)/18),z1=Mathf.CeilToInt((bounds.max.z+12)/18);
            // Existing VARCO kit is instanced around the whole course; decoration never has collision.
            for(int x=x0;x<x1;x++)for(int z=z0;z<z1;z++){
                Prop("ConcreteFloor",new Vector3((x+.5f)*18,bottom,(z+.5f)*18),new Vector3(18.02f,.2f,18.02f));
                Place("ConcreteFloor",art,new Vector3((x+.5f)*18,ceiling,(z+.5f)*18),new Vector3(18.02f,.2f,18.02f),Quaternion.Euler(180,0,0));
            }
            foreach(float x in new[]{x0*18f,x1*18f})for(int z=z0;z<z1;z++)
                Prop("ConcreteWall",new Vector3(x,(bottom+ceiling)/2,(z+.5f)*18),new Vector3(18.02f,ceiling-bottom,.28f),x<0?-90:90);
            foreach(float z in new[]{z0*18f,z1*18f})for(int x=x0;x<x1;x++)
                Prop("ConcreteWall",new Vector3((x+.5f)*18,(bottom+ceiling)/2,z),new Vector3(18.02f,ceiling-bottom,.28f),z<0?180:0);
            for(int x=x0+1;x<x1;x+=2)for(int z=z0+1;z<z1;z+=2){
                Prop("CeilingLight",new Vector3(x*18,ceiling-.5f,z*18),new Vector3(2.4f,.22f,.58f));
                Lamp(new Vector3(x*18,ceiling-2,z*18),new Color(.75f,.82f,1),.9f,32);
            }
            foreach(var section in course.sections) {
                foreach(int i in new[]{0,section.points.Length-1})foreach(int side in new[]{section.side}) {
                    Vector3 pos=section.points[i]+Quaternion.Euler(0,section.headings[i],0)*Vector3.right*side*6.1f;
                    float top=pos.y+3.3f;
                    Prop("ConcreteColumn",new Vector3(pos.x,(bottom+top)*.5f,pos.z),new Vector3(.6f,top-bottom,.6f));
                    Lamp(pos+Vector3.up*5,new Color(.64f,.5f,1),.8f,11);
                }
            }
            foreach(float z in new[]{-.5f,-7.5f})Prop("ConcreteColumn",new Vector3(-21,-21.7f,z),new Vector3(.8f,16.6f,1));
            Lamp(new Vector3(0,4.5f,-5),new Color(1,.9f,.76f),1.5f,8);
            Lamp(new Vector3(0,-5,22),new Color(.6f,.8f,1),1.2f,12);
            Lamp(new Vector3(-20,-5,-4),new Color(1,.83f,.55f),2,12);
        }
        static void Lamp(Vector3 position, Color color, float intensity, float range)
        {
            var light = new GameObject("Fixture light").AddComponent<Light>();
            light.transform.SetParent(art); light.transform.position = position;
            light.type = LightType.Point; light.color = color; light.intensity = intensity; light.range = range;
            light.shadows = LightShadows.None;
        }
        static void Lighting(bool mountain)
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = mountain ? new Color(.51f, .57f, .62f) : new Color(.24f, .27f, .25f);
            RenderSettings.ambientEquatorColor = mountain ? new Color(.35f, .39f, .37f) : new Color(.18f, .20f, .18f);
            RenderSettings.ambientGroundColor = mountain ? new Color(.22f, .25f, .2f) : new Color(.1f, .12f, .11f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = mountain ? new Color(.55f, .61f, .65f) : new Color(.19f, .22f, .2f);
            RenderSettings.fogStartDistance = mountain ? 80 : 35;
            RenderSettings.fogEndDistance = mountain ? 230 : 85;
            RenderSettings.skybox = null;
            foreach (var light in All<Light>().Where(l => l.type == LightType.Directional)) {
                bool key = light.name == "Key";
                light.intensity = mountain ? (key ? .85f : .2f) : (key ? .45f : .12f);
                light.color = key ? new Color(1, .96f, .87f) : new Color(.63f, .72f, .81f);
                light.shadows = key ? LightShadows.Soft : LightShadows.None;
                if (key) { light.shadowBias = .03f; light.shadowNormalBias = .25f; }
            }
            foreach (var camera in All<Camera>().Where(c => c.gameObject.layer != CharacterMotionDriver.HandsLayer))
                camera.backgroundColor = RenderSettings.fogColor;
        }

        static string V(Vector3 v) => string.Join(",", new[] { v.x, v.y, v.z }.Select(f => f.ToString("R", CultureInfo.InvariantCulture)));
        static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;
        static string GameplaySignature()
        {
            var lines = new List<string>();
            foreach (var collider in All<Collider>()) {
                string extra = collider is BoxCollider box ? V(box.center) + "|" + V(box.size) : "";
                lines.Add(PathOf(collider.transform) + "|" + collider.GetType().Name + "|" + V(collider.transform.position) + "|" + V(collider.transform.lossyScale) + "|" + collider.transform.rotation + "|" + collider.enabled + "|" + collider.isTrigger + "|" + extra);
            }
            var roster = Object.FindAnyObjectByType<CourseRoster>();
            foreach (var t in roster.drawAnchors.Concat(roster.openAnchors).Concat(roster.routeRoots.SelectMany(r => r.Cast<Transform>()))
                .Concat(roster.allParticipants.SelectMany(p => new[] { p.seatAnchor, p.standAnchor, p.retryAnchor })))
                lines.Add("anchor|" + PathOf(t) + "|" + V(t.position) + "|" + t.rotation);
            return string.Join("\n", lines.OrderBy(line => line, StringComparer.Ordinal));
        }

        [MenuItem("HalliGalli/Environment/Capture Both Maps")]
        public static void Capture()
        {
            Directory.CreateDirectory("Previews");
            foreach (var path in new[] { CourseBuilder.ScenePath, CourseMapsBuilder.ClimbScene }) {
                EditorSceneManager.OpenScene(path);
                bool mountain = path == CourseMapsBuilder.ClimbScene;
                string prefix = mountain ? "Mountain" : "Interior";
                var camera = new GameObject("Environment preview").AddComponent<Camera>();
                camera.cullingMask = ~(1 << CharacterMotionDriver.HandsLayer);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = RenderSettings.fogColor; camera.fieldOfView = 57;
                camera.transform.position = mountain ? new Vector3(22, 19, -10) : new Vector3(11.5f, 9.8f, -10.7f);
                camera.transform.LookAt(mountain ? new Vector3(0, 1.5f, 13) : new Vector3(0, .3f, 7));
                CharacterAssetBuilder.Render(camera, null, "Previews/" + prefix + "_Overview.png");
                File.Copy("Previews/" + prefix + "_Overview.png", mountain ? "Previews/Climb_Overview.png" : "Previews/Course_Overview.png", true);
                if (!mountain) File.Copy("Previews/" + prefix + "_Overview.png", "Previews/Players_Overview.png", true);
                camera.transform.position = new Vector3(3.8f, 3.6f, -8.3f); camera.transform.LookAt(new Vector3(0, .7f, -4));
                CharacterAssetBuilder.Render(camera, null, "Previews/" + prefix + "_Table.png");
                camera.transform.position = mountain ? new Vector3(0, 2.03f, 2.8f) : new Vector3(0, 1.97f, 5.4f);
                camera.transform.rotation = Quaternion.Euler(mountain ? -15 : 4, 0, 0);
                if (mountain) camera.transform.LookAt(new Vector3(0, 3.5f, 15.5f));
                CharacterAssetBuilder.Render(camera, null, "Previews/" + prefix + "_Route.png");
                if(mountain){
                    var course=Object.FindAnyObjectByType<CourseRoster>().branches;
                    foreach(var passing in course.shortcuts){
                        var target=passing.landing.position;
                        var outward=Vector3.ProjectOnPlane(target-course.peakCenter,Vector3.up).normalized;
                        camera.transform.position=target+outward*8+Vector3.up*7;camera.transform.LookAt(target+Vector3.up*.25f);
                        CharacterAssetBuilder.Render(camera,null,"Previews/Mountain_Shortcut_"+passing.entryWaypoint+".png");
                    }
                    camera.transform.position=new Vector3(-19,17,30);camera.transform.LookAt(new Vector3(0,3,15.5f));
                    CharacterAssetBuilder.Render(camera,null,"Previews/Mountain_Back.png");
                    camera.fieldOfView=75;camera.transform.position=course.left[2].position+Vector3.up*1.65f;
                    camera.transform.LookAt(course.left[3].position+Vector3.up*.25f);
                    CharacterAssetBuilder.Render(camera,null,"Previews/Mountain_Exposed.png");camera.fieldOfView=57;
                }
                var bell = Object.FindAnyObjectByType<CourseSession>().bellTarget;
                camera.transform.position = bell.position + new Vector3(.8f, .65f, -1.15f);
                camera.transform.LookAt(bell.position + Vector3.up * .12f);
                CharacterAssetBuilder.Render(camera, null, "Previews/" + prefix + "_Bell.png");
                Object.DestroyImmediate(camera.gameObject);
            }
            EditorSceneManager.OpenScene(CourseMapsBuilder.ClimbScene);
        }
    }
}
