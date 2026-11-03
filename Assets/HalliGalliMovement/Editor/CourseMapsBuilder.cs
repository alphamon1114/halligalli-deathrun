using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using HalliGalli.Movement;
using Object=UnityEngine.Object;

namespace HalliGalli.EditorTools
{
    public static class CourseMapsBuilder
    {
        public const string Root=MovementProjectBuilder.Root;
        public const string Maps=Root+"/Maps";
        public const string CatalogPath=Maps+"/CourseMaps.asset";
        public const string ClimbScene=Root+"/Demo/ClimbCourse.unity";
        public static CourseMapCatalog Catalog=>AssetDatabase.LoadAssetAtPath<CourseMapCatalog>(CatalogPath);
        static CourseMapDefinition Definition(string id,string title,CourseMapStyle style,int difficulty,string description,string scene)
        {
            string path=Maps+"/"+id+".asset";var map=AssetDatabase.LoadAssetAtPath<CourseMapDefinition>(path);
            if(map)return map;
            map=ScriptableObject.CreateInstance<CourseMapDefinition>();map.id=id;map.displayName=title;map.style=style;map.difficulty=difficulty;map.description=description;map.scenePath=scene;map.playable=!string.IsNullOrEmpty(scene);
            map.movementProfile=AssetDatabase.LoadAssetAtPath<SourceMovementProfile>(Root+"/Settings/DefaultMovement.asset");
            AssetDatabase.CreateAsset(map,path);return map;
        }
        static CourseMapCatalog EnsureCatalog()
        {
            Directory.CreateDirectory(Maps);AssetDatabase.Refresh();
            var defaults=new[]{
                Definition("first-steps","첫걸음",CourseMapStyle.Jump,1,"기본 점프와 방해를 익히는 입문 코스.",CourseBuilder.ScenePath),
                Definition("summit","정상 쟁탈",CourseMapStyle.Climb,3,"봉우리를 돌아 오르는 공통 코스와 짧은 숏컷.",ClimbScene),
                Definition("factory","폐공장",CourseMapStyle.Factory,2,"컨베이어와 왕복 발판을 건너 압착기를 뚫는 기계실.",""),
                Definition("precision","정밀 점프",CourseMapStyle.Jump,2,"짧은 착지 구간과 연속 방향 전환을 중심으로 만들 예정입니다.",""),
                Definition("surf","서프",CourseMapStyle.Surf,4,"경사면을 타며 속도를 이어가는 코스를 만들 예정입니다.","")
            };
            var catalog=Catalog;if(!catalog){catalog=ScriptableObject.CreateInstance<CourseMapCatalog>();catalog.maps=defaults;AssetDatabase.CreateAsset(catalog,CatalogPath);}
            else {catalog.maps=(catalog.maps??Array.Empty<CourseMapDefinition>()).Concat(defaults).Distinct().ToArray();EditorUtility.SetDirty(catalog);}
            return catalog;
        }
        public static void InstallInCurrentScene(string id="first-steps")
        {
            var session=Object.FindAnyObjectByType<CourseSession>();if(!session||!session.roster)return;
            var maps=session.GetComponent<CourseMapController>();if(!maps)maps=session.gameObject.AddComponent<CourseMapController>();
            maps.session=session;maps.catalog=EnsureCatalog();maps.current=maps.catalog.maps.Single(m=>m.id==id);session.maps=maps;
            CheckpointBuilder.InstallCurrent();
            EditorSceneManager.MarkSceneDirty(session.gameObject.scene);
        }
        [MenuItem("HalliGalli/Maps/Install Maps and Build Climb")]
        public static void InstallAndBuildClimb()
        {
            EditorSceneManager.OpenScene(CourseBuilder.ScenePath);InstallInCurrentScene();EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            if(!File.Exists(ClimbScene))BuildClimb();
            EditorBuildSettings.scenes=BuildScenePaths().Select(p=>new EditorBuildSettingsScene(p,true)).ToArray();AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(ClimbScene);
        }
        static void BuildClimb()
        {
            EditorSceneManager.OpenScene(CourseBuilder.ScenePath);
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();EditorSceneManager.SaveScene(scene,ClimbScene);
            ConfigureFork();
        }
        [MenuItem("HalliGalli/Maps/Build Spiral Summit")]
        public static void BuildFork()
        {
            if(!File.Exists(ClimbScene))BuildClimb();
            else {
                EditorSceneManager.OpenScene(ClimbScene);
                string backup=Root+"/Demo/Backups";Directory.CreateDirectory(backup);
                EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),backup+"/ClimbCourse_BeforeFork_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".unity",true);
                ConfigureFork();
            }
            EditorBuildSettings.scenes=BuildScenePaths().Select(p=>new EditorBuildSettingsScene(p,true)).ToArray();
            EnvironmentArtBuilder.InstallClimb();
        }
        static void ConfigureFork(){TutorialFerryBuilder.RemoveFromTemplate(Object.FindAnyObjectByType<CourseSession>());ClimbRoutesBuilder.Configure();}
        static Material Material(string name,Color color){string path=Root+"/Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m)return m;m=new Material(Shader.Find("Standard"));m.color=color;AssetDatabase.CreateAsset(m,path);return m;}
        static Transform Box(Transform parent,string name,Vector3 position,Vector3 size,Material material,bool solid){var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent);go.transform.position=position;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=material;if(!solid)Object.DestroyImmediate(go.GetComponent<Collider>());return go.transform;}
        static Transform Waypoint(Transform parent,string name,Vector3 position)
        {
            var node=new GameObject(name).transform;node.SetParent(parent);node.position=position;return node;
        }
        static void LandingLabel(Transform root,string text,Vector3 position)
        {
            var label=new GameObject("Climb marker "+text).AddComponent<TextMesh>();label.transform.SetParent(root);label.transform.position=position+new Vector3(0,.025f,0);label.transform.rotation=Quaternion.Euler(90,0,0);label.text=text;label.fontSize=80;label.characterSize=.025f;label.anchor=TextAnchor.MiddleCenter;label.color=Color.white;label.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/CourseText.mat");
        }
        public static string[] BuildScenePaths()
        {
            var catalog=Catalog;if(!catalog)return new[]{CourseBuilder.ScenePath,MovementProjectBuilder.ScenePath}.Concat(File.Exists(UserMapRuntime.ScenePath)?new[]{UserMapRuntime.ScenePath}:Array.Empty<string>()).ToArray();
            var maps=catalog.maps.Where(m=>m).ToArray();if(maps.Select(m=>m.id).Distinct().Count()!=maps.Length)throw new InvalidOperationException("Map IDs must be unique.");
            foreach(var map in maps.Where(m=>m.playable))if(!map.movementProfile||!AssetDatabase.LoadAssetAtPath<SceneAsset>(map.scenePath))throw new InvalidOperationException("Playable map has no movement profile or scene: "+map.id);
            return new[]{CourseBuilder.ScenePath}.Concat(maps.Where(m=>m.playable).Select(m=>m.scenePath)).Concat(new[]{MovementProjectBuilder.ScenePath}).Concat(System.IO.File.Exists(CombatPracticeRoom.ScenePath)?new[]{CombatPracticeRoom.ScenePath}:new string[0]).Concat(File.Exists(UserMapRuntime.ScenePath)?new[]{UserMapRuntime.ScenePath}:Array.Empty<string>()).Distinct().ToArray();
        }
        [MenuItem("HalliGalli/Maps/Validate Map Switching and Climb")]
        public static void Validate(){EditorSceneManager.OpenScene(CourseBuilder.ScenePath);new GameObject("Maps validation").AddComponent<MapsValidationRunner>();PlayerSettings.runInBackground=true;EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Focus();EditorApplication.isPaused=false;EditorApplication.isPlaying=true;}
        public static void Probe(){EditorSceneManager.OpenScene(CourseBuilder.ScenePath);new GameObject("Maps probe").AddComponent<MapsValidationRunner>().probeOnly=true;PlayerSettings.runInBackground=true;EditorApplication.isPaused=false;EditorApplication.isPlaying=true;}
        public static void CaptureMenus(){EditorSceneManager.OpenScene(ClimbScene);new GameObject("Map menu preview").AddComponent<MapsValidationRunner>().captureOnly=true;PlayerSettings.runInBackground=true;EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Focus();EditorApplication.isPaused=false;EditorApplication.isPlaying=true;}
        public static void Capture()
        {
            EnvironmentArtBuilder.Capture();
        }
    }
}
