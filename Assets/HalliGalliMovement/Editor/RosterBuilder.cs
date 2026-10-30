using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using HalliGalli.Cards;
using HalliGalli.Movement;

namespace HalliGalli.EditorTools
{
    public static class RosterBuilder
    {
        const string Root=MovementProjectBuilder.Root;
        static readonly Color[] Colors={new Color(.06f,.8f,.9f),new Color(1,.46f,.15f),new Color(.52f,.85f,.22f),new Color(.75f,.4f,1),new Color(1,.4f,.65f),new Color(1,.83f,.22f)};
        [MenuItem("HalliGalli/Players/Install 2-6 Player Layout and Validate")]
        public static void InstallAndValidate(){Install();Validate();}
        public static void Install()
        {
            EditorSceneManager.OpenScene(CourseBuilder.ScenePath);
            var session=Object.FindAnyObjectByType<CourseSession>();var roster=session.roster;
            if(!roster){
                roster=session.gameObject.AddComponent<CourseRoster>();roster.session=session;session.roster=roster;
                var parent=new GameObject("Player row and lanes").transform;
                var template=session.participants.First(p=>!p.localPlayer);roster.allParticipants=new CourseParticipant[6];roster.allParticipants[0]=session.Local;roster.allParticipants[1]=template;
                roster.chairs=new Transform[6];roster.firstPlatforms=new Transform[6];roster.routeRoots=new Transform[6];roster.drawAnchors=new Transform[6];roster.openAnchors=new Transform[6];roster.cardLabels=new Transform[6];roster.startLabels=new Transform[6];
                foreach(var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)){
                    float z=t.position.z;
                    bool first=t.name.StartsWith("Lane pad ")||((t.name=="Edge stripe"||t.name=="Platform support")&&z>1.2f&&z<3.4f);
                    bool merge=t.name=="Shared pad 2"||((t.name=="Edge stripe"||t.name=="Platform support")&&z>4.5f&&z<6.9f);
                    if(t.name=="Chair seat"||t.name=="Chair back"||t.name=="Chair leg"||first||merge)Object.DestroyImmediate(t.gameObject);
                }
                var deck=Mat("CourseDeck");var dark=Mat("CourseDark");
                for(int i=0;i<6;i++){
                    if(i>1){var clone=Object.Instantiate(template.gameObject);roster.allParticipants[i]=clone.GetComponent<CourseParticipant>();}
                    var p=roster.allParticipants[i];p.name=i==0?"Player":"AI player "+(i+1);p.session=session;p.localPlayer=i==0;
                    if(i>1){p.seatAnchor=Anchor(parent,"Seat "+(i+1),Vector3.zero);p.standAnchor=Anchor(parent,"Start "+(i+1),Vector3.zero);p.retryAnchor=Anchor(parent,"Retry "+(i+1),Vector3.zero);}
                    var color=SeatMaterial(i);roster.chairs[i]=Chair(parent,i,color,dark);
                    roster.firstPlatforms[i]=new GameObject("First platform "+(i+1)).transform;roster.firstPlatforms[i].SetParent(parent);
                    Platform(roster.firstPlatforms[i],1.3f,2,deck,dark);
                    var route=new GameObject("Route seat "+(i+1)).transform;route.SetParent(parent);roster.routeRoots[i]=route;
                    Vector3[] nodes={new Vector3(0,.175f,2.3f),new Vector3(0,.375f,5.7f),new Vector3(0,.375f,9.1f),new Vector3(-1.3f,.575f,12.3f),new Vector3(1.3f,.475f,15.7f),new Vector3(0,.275f,19.1f),new Vector3(0,.275f,21.95f)};
                    for(int n=0;n<nodes.Length;n++)Anchor(route,"Landing "+n,nodes[n]);
                    roster.drawAnchors[i]=Anchor(parent,"Card draw "+(i+1),Vector3.zero);roster.openAnchors[i]=Anchor(parent,"Card open "+(i+1),Vector3.zero);
                    roster.cardLabels[i]=Label(parent,(i+1)+(i==0?" YOU":" AI"),Colors[i],.13f);roster.startLabels[i]=Label(parent,(i+1).ToString(),Colors[i],.4f);
                    var bot=p.GetComponent<CourseRival>();if(bot)bot.participant=p;
                }
                var mergeRoot=new GameObject("Wide merge platform").transform;mergeRoot.SetParent(parent);mergeRoot.position=new Vector3(0,.35f,5.7f);
                roster.mergeSurface=Platform(mergeRoot,9,2.2f,deck,dark);
                var top=GameObject.Find("Table top").transform;top.position=new Vector3(0,.79f,-5.1f);top.localScale=new Vector3(5.5f,.15f,5.2f);
                var felt=GameObject.Find("Table felt").transform;felt.position=new Vector3(0,.875f,-5.1f);felt.localScale=new Vector3(5.28f,.025f,4.98f);
                int leg=0;foreach(var t in Object.FindObjectsByType<Transform>().Where(t=>t.name=="Table leg")){t.position=new Vector3(leg%2==0?-2.5f:2.5f,.37f,leg<2?-7.4f:-2.8f);leg++;}
                foreach(var anchor in session.cardGame.display.drawAnchors.Concat(session.cardGame.display.openAnchors))Object.DestroyImmediate(anchor.gameObject);
            }
            if(!roster.startLine)roster.startLine=Box(roster.chairs[0].parent,"Common start line",Vector3.zero,Vector3.one,Mat("CourseWhite"),false);
            var startText=GameObject.Find("START");if(startText){startText.transform.position=new Vector3(-5,.035f,CourseRoster.StartZ);startText.GetComponent<TextMesh>().characterSize=.018f;}
            roster.ApplyLayout(6);
            foreach(var label in roster.cardLabels)label.GetComponent<TextMesh>().characterSize=.013f;
            foreach(var label in roster.startLabels)label.GetComponent<TextMesh>().characterSize=.04f;
            foreach(var p in roster.allParticipants){
                p.transform.SetPositionAndRotation(p.seatAnchor.position,p.seatAnchor.rotation);
                var driver=p.GetComponent<CharacterMotionDriver>();var body=p.GetComponent<CourseBodyAnimator>();
                if(driver)driver.seatedClip.SampleAnimation(driver.bodyModel.gameObject,0);else body.seatedClip.SampleAnimation(body.model.gameObject,0);
            }
            var localDriver=session.Local.GetComponent<CharacterMotionDriver>();localDriver.worldCamera.transform.SetPositionAndRotation(session.Local.seatAnchor.position+Vector3.up*1.23f,Quaternion.Euler(22,180,0));
            session.cardGame.display.Initialize(new HalliGalliMatch(session.cardGame.deck.Deal(6,session.cardGame.seed)));
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());AssetDatabase.SaveAssets();
        }
        static Material Mat(string name)=>AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/"+name+".mat");
        static Material SeatMaterial(int i){string path=Root+"/Materials/Seat"+(i+1)+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(!m){m=new Material(Shader.Find("Standard"));AssetDatabase.CreateAsset(m,path);}m.color=Colors[i];EditorUtility.SetDirty(m);return m;}
        static Transform Anchor(Transform parent,string name,Vector3 position){var t=new GameObject(name).transform;t.SetParent(parent);t.position=position;return t;}
        static Transform Box(Transform parent,string name,Vector3 pos,Vector3 size,Material material,bool solid=true){var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=pos;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=material;if(!solid)Object.DestroyImmediate(go.GetComponent<Collider>());return go.transform;}
        static Transform Chair(Transform parent,int seat,Material material,Material dark){var root=new GameObject("Chair "+(seat+1)).transform;root.SetParent(parent);Box(root,"Seat",new Vector3(0,.48f,0),new Vector3(.62f,.1f,.62f),material,false);Box(root,"Back",new Vector3(0,.88f,-.28f),new Vector3(.62f,.75f,.08f),material,false);foreach(float x in new[]{-.24f,.24f})foreach(float z in new[]{-.24f,.24f})Box(root,"Leg",new Vector3(x,.23f,z),new Vector3(.055f,.46f,.055f),dark,false);return root;}
        static Transform Platform(Transform parent,float width,float depth,Material material,Material dark){
            var floor=Box(parent,"Landing surface",Vector3.down*.22f,new Vector3(width,.44f,depth),material);
            Box(parent,"Support",Vector3.down*2.9f,new Vector3(.6f,5.3f,.6f),dark,false);return floor;
        }
        static Transform Label(Transform parent,string text,Color color,float size){var label=new GameObject("Label "+text).AddComponent<TextMesh>();label.transform.SetParent(parent);label.transform.rotation=Quaternion.Euler(90,0,0);label.text=text;label.fontSize=80;label.characterSize=size*.1f;label.anchor=TextAnchor.MiddleCenter;label.alignment=TextAlignment.Center;label.color=color;label.GetComponent<Renderer>().sharedMaterial=Mat("CourseText");return label.transform;}
        [MenuItem("HalliGalli/Players/Validate 2-6 Players")]
        public static void Validate(){EditorSceneManager.OpenScene(CourseBuilder.ScenePath);PlayerSettings.runInBackground=true;new GameObject("Roster validation").AddComponent<RosterValidationRunner>();EditorApplication.isPaused=false;EditorApplication.isPlaying=true;}
        public static void Capture(){EditorSceneManager.OpenScene(CourseBuilder.ScenePath);var camera=new GameObject("Row overview").AddComponent<Camera>();camera.fieldOfView=50;camera.cullingMask=~(1<<9);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=RenderSettings.fogColor;camera.transform.position=new Vector3(12,13,-16);camera.transform.LookAt(new Vector3(0,0,1));Directory.CreateDirectory("Previews");CharacterAssetBuilder.Render(camera,null,"Previews/Players_Overview.png");Object.DestroyImmediate(camera.gameObject);EditorSceneManager.OpenScene(CourseBuilder.ScenePath);}
    }
}
