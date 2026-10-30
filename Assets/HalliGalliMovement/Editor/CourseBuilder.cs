using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using HalliGalli.Movement;

namespace HalliGalli.EditorTools
{
    public static class CourseBuilder
    {
        const string Root=MovementProjectBuilder.Root;
        public const string ScenePath=Root+"/Demo/BellRunCourse.unity";
        static Material dark,deck,cyan,orange,white,gold,wood;
        [MenuItem("HalliGalli/Create Bell Run Course")]
        public static void Build()
        {
            AssetDatabase.Refresh();var seatClip=BakeSeat();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);scene.name="BellRunCourse";
            dark=Mat("CourseDark",new Color(.055f,.08f,.115f));deck=Mat("CourseDeck",new Color(.19f,.27f,.33f));cyan=Mat("CourseCyan",new Color(.06f,.69f,.79f));orange=Mat("CourseOrange",new Color(1,.38f,.12f));white=Mat("CourseWhite",new Color(.85f,.91f,.90f));gold=Mat("CourseGold",new Color(.92f,.61f,.16f),.8f,.75f);wood=Mat("CourseWood",new Color(.32f,.16f,.075f));
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.46f,.53f,.61f);
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogColor=new Color(.055f,.09f,.14f);RenderSettings.fogStartDistance=45;RenderSettings.fogEndDistance=90;
            Light("Key",new Vector3(0,9,8),new Vector3(48,-35,0),1.15f,Color.white,LightType.Directional);
            Light("Warm rim",new Vector3(4,8,20),new Vector3(30,160,0),.55f,new Color(1,.72f,.45f),LightType.Directional);
            Platform("Table deck",new Vector3(0,0,-4),new Vector2(12,8),deck,cyan);
            Box("Pit floor",new Vector3(0,-6.5f,8),new Vector3(28,1,42),dark);
            for(int i=0;i<12;i++){float z=-8+i*3;Box("Pit warning",new Vector3(-7,-5.97f,z),new Vector3(.2f,.02f,1.2f),orange,false);Box("Pit warning",new Vector3(7,-5.97f,z),new Vector3(.2f,.02f,1.2f),orange,false);}
            Box("Left arena wall",new Vector3(-13,0,9),new Vector3(.4f,9,42),dark);Box("Right arena wall",new Vector3(13,0,9),new Vector3(.4f,9,42),dark);Box("Far arena wall",new Vector3(0,0,28),new Vector3(26,9,.4f),dark);
            for(int i=0;i<6;i++)foreach(int side in new[]{-1,1}){float z=-6+i*6;Box("Wall light",new Vector3(side*12.75f,2,z),new Vector3(.06f,3,.13f),side<0?cyan:orange,false);Box("Wall buttress",new Vector3(side*12.6f,0,z+.3f),new Vector3(.7f,8,.4f),deck);}
            Box("Table top",new Vector3(0,.79f,-4.65f),new Vector3(3.5f,.15f,1.5f),wood);
            Box("Table felt",new Vector3(0,.875f,-4.65f),new Vector3(3.28f,.025f,1.32f),Mat("CourseFelt",new Color(.08f,.27f,.23f)),false);
            foreach(float x in new[]{-1.45f,1.45f})foreach(float z in new[]{-5.15f,-4.15f})Box("Table leg",new Vector3(x,.37f,z),new Vector3(.14f,.74f,.14f),dark);
            if(!HalliGalli.CardEditor.CardAssetBuilder.HasDeck){Card(-.62f,3);Card(.62f,2);}
            for(int side=-1;side<=1;side+=2){float x=side*.9f;Chair(x,-6.15f,side<0?cyan:orange);if(!HalliGalli.CardEditor.CardAssetBuilder.HasDeck)Box("Closed card deck",new Vector3(x,.92f,-5.02f),new Vector3(.35f,.075f,.47f),side<0?cyan:orange);}
            if(HalliGalli.CardEditor.CardAssetBuilder.HasDeck)HalliGalli.CardEditor.CardAssetBuilder.InstallTableInCurrentScene();
            Label("BELL RUN",new Vector3(0,2.8f,27.6f),1.1f,white.color);
            Label("JUMP  /  BRAWL  /  RING",new Vector3(0,1.55f,27.6f),.36f,cyan.color);
            Label("START",new Vector3(0,.035f,-.6f),.65f,white.color,true);
            var session=new GameObject("Course session").AddComponent<CourseSession>();
            Vector3[] nodes={new Vector3(0,.35f,5.7f),new Vector3(0,.35f,9.1f),new Vector3(-1.3f,.55f,12.3f),new Vector3(1.3f,.45f,15.7f),new Vector3(0,.25f,19.1f),new Vector3(0,.25f,22.1f)};
            Vector2[] sizes={new Vector2(4.6f,2.2f),new Vector2(1.4f,2.1f),new Vector2(2.2f,2),new Vector2(2.2f,2),new Vector2(3.8f,2.2f),new Vector2(6,3)};
            for(int i=0;i<nodes.Length;i++){Platform("Shared pad "+(i+2),nodes[i],sizes[i],deck,i==1?orange:cyan);Label((i+2).ToString("00"),nodes[i]+new Vector3(0,.02f,-sizes[i].y*.25f),.36f,white.color,true);}
            foreach(int side in new[]{-1,1})Platform("Lane pad "+side,new Vector3(side*1.8f,.15f,2.3f),new Vector2(2.4f,2),deck,side<0?cyan:orange);
            Box("Bell plinth",new Vector3(0,.6f,22.8f),new Vector3(.65f,.7f,.65f),dark);
            var bell=Bell(new Vector3(0,.95f,22.8f));session.bellTarget=bell.transform;session.bellButton=bell.transform.Find("Button");
            var audio=bell.AddComponent<AudioSource>();audio.clip=AssetDatabase.LoadAssetAtPath<AudioClip>(Root+"/Audio/ServiceBell.wav");audio.playOnAwake=false;audio.spatialBlend=1;audio.minDistance=3;audio.maxDistance=45;audio.dopplerLevel=0;session.bellAudio=audio;
            Label("RING",new Vector3(0,.65f,22.47f),.17f,gold.color);
            var profile=AssetDatabase.LoadAssetAtPath<SourceMovementProfile>(Root+"/Settings/DefaultMovement.asset");
            var player=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/SourcePlayer.prefab"));player.name="Player";
            var driver=player.GetComponent<CharacterMotionDriver>();driver.seatedClip=seatClip;
            driver.worldCamera.backgroundColor=RenderSettings.fogColor;
            var local=player.AddComponent<CourseParticipant>();local.localPlayer=true;local.session=session;Anchors(local,-1);
            player.transform.SetPositionAndRotation(local.seatAnchor.position,Quaternion.identity);
            seatClip.SampleAnimation(driver.bodyModel.gameObject,0);
            driver.worldCamera.transform.localPosition=Vector3.up*(profile.standingEyeHeight-.42f);
            var rival=new GameObject("Practice rival");var cc=rival.AddComponent<CharacterController>();cc.height=profile.standingHeight;cc.radius=profile.radius;cc.center=Vector3.up*profile.standingHeight/2;cc.skinWidth=profile.skinWidth;cc.stepOffset=profile.stepHeight;cc.slopeLimit=profile.slopeLimit;cc.minMoveDistance=0;
            rival.AddComponent<SourceMotor>().profile=profile;var combat=rival.AddComponent<MeleeCombat>();combat.readInput=false;ReactionAssetBuilder.InstallAudio(rival);
            var model=CharacterAssetBuilder.MakeModel(false);model.transform.SetParent(rival.transform,false);var animator=rival.AddComponent<CourseBodyAnimator>();animator.model=model.transform;animator.motions=driver.motions;animator.seatedClip=seatClip;
            var other=rival.AddComponent<CourseParticipant>();other.session=session;Anchors(other,1);rival.transform.SetPositionAndRotation(other.seatAnchor.position,Quaternion.identity);seatClip.SampleAnimation(model,0);
            var bot=rival.AddComponent<CourseRival>();bot.participant=other;bot.route=Route(1,nodes);session.participants=new[]{local,other};
            // The other start lane is retained as scene data for validation and future remote players.
            Route(-1,nodes);
            new GameObject("Course HUD").AddComponent<CourseHUD>().session=session;
            if(HalliGalli.CardEditor.CardAssetBuilder.HasDeck)MatchIntegrationBuilder.InstallInCurrentScene();
            EditorSceneManager.SaveScene(scene,ScenePath);EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true),new EditorBuildSettingsScene(MovementProjectBuilder.ScenePath,true)};
            PlayerSettings.productName="HalliGalli Deathrun - Bell Run";AssetDatabase.SaveAssets();Debug.Log("BELL_RUN_CREATED");
            if(HalliGalli.CardEditor.CardAssetBuilder.HasDeck){RosterBuilder.Install();if(CourseMapsBuilder.Catalog){CourseMapsBuilder.InstallInCurrentScene();EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());}}
        }
        static Transform[] Route(int side,Vector3[] nodes)
        {
            var root=new GameObject(side<0?"Left route":"Right route").transform;
            var points=new[]{new Vector3(side*1.8f,.15f,2.3f)}.Concat(nodes).ToArray();points[points.Length-1].z=21.95f;
            return points.Select((p,i)=>{var t=new GameObject("Landing "+i).transform;t.SetParent(root);t.position=p+Vector3.up*.025f;return t;}).ToArray();
        }
        static void Anchors(CourseParticipant p,int side)
        {
            p.seatAnchor=Anchor((side<0?"Player":"Rival")+" seat",new Vector3(side*.9f,.025f,-6.15f));
            p.standAnchor=Anchor((side<0?"Player":"Rival")+" stand",new Vector3(side*2.55f,.025f,-6.0f));
            p.retryAnchor=Anchor((side<0?"Player":"Rival")+" retry",new Vector3(side*1.8f,.025f,-.7f));
        }
        static Transform Anchor(string name,Vector3 position){var t=new GameObject(name).transform;t.position=position;return t;}
        static Material Mat(string name,Color color,float metal=0,float smooth=.25f){string path=Root+"/Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(!m){m=new Material(Shader.Find("Standard"));AssetDatabase.CreateAsset(m,path);}m.color=color;m.SetFloat("_Metallic",metal);m.SetFloat("_Glossiness",smooth);EditorUtility.SetDirty(m);return m;}
        static GameObject Box(string name,Vector3 pos,Vector3 size,Material mat,bool solid=true){var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.position=pos;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=mat;if(!solid)UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());return go;}
        static void Platform(string name,Vector3 top,Vector2 size,Material mat,Material edge)
        {
            Box(name,top-Vector3.up*.22f,new Vector3(size.x,.44f,size.y),mat);
            foreach(int s in new[]{-1,1}){Box("Edge stripe",top+new Vector3(s*(size.x/2-.06f),.006f,0),new Vector3(.1f,.012f,size.y),edge,false);Box("Edge stripe",top+new Vector3(0,.006f,s*(size.y/2-.06f)),new Vector3(size.x,.012f,.1f),edge,false);}
            Box("Platform support",top-Vector3.up*2.9f,new Vector3(Mathf.Min(.8f,size.x*.5f),5.3f,Mathf.Min(.8f,size.y*.5f)),dark,false);
        }
        static void Chair(float x,float z,Material mat)
        {
            Box("Chair seat",new Vector3(x,.48f,z),new Vector3(.62f,.1f,.62f),mat,false);Box("Chair back",new Vector3(x,.88f,z-.28f),new Vector3(.62f,.75f,.08f),mat,false);
            foreach(float dx in new[]{-.24f,.24f})foreach(float dz in new[]{-.24f,.24f})Box("Chair leg",new Vector3(x+dx,.23f,z+dz),new Vector3(.055f,.46f,.055f),dark,false);
        }
        static void Card(float x,int count)
        {
            Box("Open card - practice only",new Vector3(x,.904f,-4.5f),new Vector3(.48f,.018f,.69f),white,false);
            var red=Mat("CourseFruit",new Color(.85f,.09f,.075f));
            for(int i=0;i<count;i++){var fruit=GameObject.CreatePrimitive(PrimitiveType.Sphere);fruit.name="Fruit";fruit.transform.position=new Vector3(x+(i%2==0?-.105f:.105f),.925f,-4.65f+i/2*.24f);fruit.transform.localScale=new Vector3(.12f,.025f,.14f);fruit.GetComponent<Renderer>().sharedMaterial=red;UnityEngine.Object.DestroyImmediate(fruit.GetComponent<Collider>());}
        }
        static void Label(string text,Vector3 pos,float size,Color color,bool floor=false){var t=new GameObject(text).AddComponent<TextMesh>();t.text=text;t.anchor=TextAnchor.MiddleCenter;t.alignment=TextAlignment.Center;t.characterSize=size*.1f;t.fontSize=100;t.color=color;t.transform.position=pos;if(floor)t.transform.rotation=Quaternion.Euler(90,0,0);var renderer=t.GetComponent<Renderer>();string path=Root+"/Materials/CourseText.mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(!mat){mat=new Material(Shader.Find("HalliGalli/WorldText"));mat.mainTexture=renderer.sharedMaterial.mainTexture;AssetDatabase.CreateAsset(mat,path);}renderer.sharedMaterial=mat;}
        static void Light(string name,Vector3 pos,Vector3 rot,float intensity,Color color,LightType type){var l=new GameObject(name).AddComponent<Light>();l.transform.position=pos;l.transform.eulerAngles=rot;l.type=type;l.intensity=intensity;l.color=color;l.shadows=LightShadows.Soft;}
        static GameObject Bell(Vector3 pos)
        {
            var root=new GameObject("Service bell");root.transform.position=pos;
            var verts=new System.Collections.Generic.List<Vector3>();var triangles=new System.Collections.Generic.List<int>();
            Vector2[] profile={new Vector2(.25f,0),new Vector2(.26f,.035f),new Vector2(.245f,.06f),new Vector2(.23f,.12f),new Vector2(.19f,.19f),new Vector2(.12f,.23f),new Vector2(.045f,.245f)};
            int segments=64;for(int r=0;r<profile.Length;r++)for(int i=0;i<=segments;i++){float a=i*Mathf.PI*2/segments;verts.Add(new Vector3(Mathf.Cos(a)*profile[r].x,profile[r].y,Mathf.Sin(a)*profile[r].x));if(r>0&&i>0){int b=r*(segments+1)+i;triangles.AddRange(new[]{b,b-1,b-segments-2,b,b-segments-2,b-segments-1});}}
            var mesh=new Mesh{name="Service bell dome"};mesh.SetVertices(verts);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();string path=Root+"/Art/ServiceBell.asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing){EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);mesh=existing;}else AssetDatabase.CreateAsset(mesh,path);
            var shell=new GameObject("Dome",typeof(MeshFilter),typeof(MeshRenderer));shell.transform.SetParent(root.transform,false);shell.GetComponent<MeshFilter>().sharedMesh=mesh;shell.GetComponent<Renderer>().sharedMaterial=gold;
            var button=GameObject.CreatePrimitive(PrimitiveType.Cylinder);button.name="Button";button.transform.SetParent(root.transform,false);button.transform.localPosition=Vector3.up*.275f;button.transform.localScale=new Vector3(.085f,.025f,.085f);button.GetComponent<Renderer>().sharedMaterial=gold;UnityEngine.Object.DestroyImmediate(button.GetComponent<Collider>());
            var basis=GameObject.CreatePrimitive(PrimitiveType.Cylinder);basis.name="Bell base";basis.transform.SetParent(root.transform,false);basis.transform.localPosition=Vector3.up*.01f;basis.transform.localScale=new Vector3(.57f,.035f,.57f);basis.GetComponent<Renderer>().sharedMaterial=dark;UnityEngine.Object.DestroyImmediate(basis.GetComponent<Collider>());
            return root;
        }
        static AnimationClip BakeSeat()
        {
            var model=CharacterAssetBuilder.MakeModel(false);var author=new MotionPoseAuthor(model);var clip=new AnimationClip{name="Seated",legacy=true,frameRate=30,wrapMode=WrapMode.Loop};var curves=new AnimationCurve[author.bones.Length,7];
            for(int b=0;b<author.bones.Length;b++)for(int c=0;c<7;c++)curves[b,c]=new AnimationCurve();
            for(int f=0;f<=60;f++){author.Seated(f/60f);for(int b=0;b<author.bones.Length;b++){var t=author.bones[b];var p=t.localPosition;var q=t.localRotation;float[] v={p.x,p.y,p.z,q.x,q.y,q.z,q.w};for(int c=0;c<7;c++)curves[b,c].AddKey(f/30f,v[c]);}}
            string[] properties={"localPosition.x","localPosition.y","localPosition.z","localRotation.x","localRotation.y","localRotation.z","localRotation.w"};
            for(int b=0;b<author.bones.Length;b++)for(int c=0;c<7;c++)clip.SetCurve(AnimationUtility.CalculateTransformPath(author.bones[b],model.transform),typeof(Transform),properties[c],curves[b,c]);
            clip.EnsureQuaternionContinuity();string path=CharacterAssetBuilder.Art+"/Motions/Body_Seated.anim";var old=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);if(old){EditorUtility.CopySerialized(clip,old);UnityEngine.Object.DestroyImmediate(clip);clip=old;}else AssetDatabase.CreateAsset(clip,path);UnityEngine.Object.DestroyImmediate(model);EditorUtility.SetDirty(clip);return clip;
        }
        public static void Validate(){EditorSceneManager.OpenScene(ScenePath);new GameObject("Course validation").AddComponent<CourseValidationRunner>();EditorApplication.isPlaying=true;}
        public static void Capture()
        {
            EditorSceneManager.OpenScene(ScenePath);Directory.CreateDirectory("Previews");
            var driver=GameObject.Find("Player").GetComponent<CharacterMotionDriver>();var seat=driver.GetComponent<CourseParticipant>().seatAnchor;driver.worldCamera.transform.rotation=Quaternion.Euler(22,seat.eulerAngles.y,0);CharacterAssetBuilder.Render(driver.worldCamera,driver.handCamera,"Previews/Course_Seated.png");
            driver.handCamera.enabled=false;var camera=new GameObject("Course overview").AddComponent<Camera>();camera.cullingMask=~(1<<CharacterMotionDriver.HandsLayer);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=RenderSettings.fogColor;camera.fieldOfView=48;camera.transform.position=new Vector3(21,22,-23);camera.transform.LookAt(new Vector3(0,0,7));CharacterAssetBuilder.Render(camera,null,"Previews/Course_Overview.png");
            if(GameObject.Find("VARCO environment")){camera.transform.position=new Vector3(11.5f,9.8f,-10.7f);camera.transform.LookAt(new Vector3(0,.3f,7));CharacterAssetBuilder.Render(camera,null,"Previews/Course_Overview.png");}
            camera.transform.position=new Vector3(3.8f,2.3f,-8);camera.transform.LookAt(new Vector3(0,.8f,-5.6f));CharacterAssetBuilder.Render(camera,null,"Previews/Course_Table.png");var bellPosition=UnityEngine.Object.FindAnyObjectByType<CourseSession>().bellTarget.position;camera.transform.position=bellPosition+new Vector3(1.3f,.85f,-1.5f);camera.transform.LookAt(bellPosition);CharacterAssetBuilder.Render(camera,null,"Previews/Course_Bell.png");
            driver.worldCamera.transform.position=new Vector3(0,1.97f,5.4f);driver.worldCamera.transform.rotation=Quaternion.Euler(7,0,0);driver.handCamera.enabled=true;CharacterAssetBuilder.Render(driver.worldCamera,driver.handCamera,"Previews/Course_FirstPerson.png");
            EditorSceneManager.OpenScene(ScenePath);
        }
        public static void CaptureAndBuild()
        {
            Capture();
            string folder=Path.GetFullPath("Build/Windows");Directory.CreateDirectory(folder);PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=CourseMapsBuilder.BuildScenePaths(),locationPathName=Path.Combine(folder,"HalliGalliMovement.exe"),target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
            Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/windows-build.txt",report.summary.result+"\nerrors="+report.summary.totalErrors+"\nwarnings="+report.summary.totalWarnings+"\n");
            var buildMessages=new System.Text.StringBuilder();
            foreach(var step in report.steps)foreach(var message in step.messages)
                if(message.type==LogType.Warning||message.type==LogType.Error||message.type==LogType.Exception)buildMessages.AppendLine(message.type+": "+message.content);
            File.WriteAllText("TestResults/windows-build-messages.txt",buildMessages.ToString());
            if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Windows build failed");
            Directory.CreateDirectory("Exports");AssetDatabase.ExportPackage(new[]{Root,HalliGalli.CardEditor.CardAssetBuilder.Root},"Exports/HalliGalliMovement.unitypackage",ExportPackageOptions.Recurse);Debug.Log("BELL_RUN_BUILD_SUCCEEDED");
        }
    }
}
