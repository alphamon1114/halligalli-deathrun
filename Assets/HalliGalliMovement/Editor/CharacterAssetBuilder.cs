using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using HalliGalli.Movement;
using System.Collections.Generic;

namespace HalliGalli.EditorTools
{
    public static class CharacterAssetBuilder
    {
        public const string Art = MovementProjectBuilder.Root + "/Art";
        public static void Inspect()
        {
            AssetDatabase.Refresh();
            ConfigureModel(Art + "/Player_Bald.fbx");
            var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Art + "/Player_Bald.fbx"));
            var lines = go.GetComponentsInChildren<Transform>().Select(t => t.name + " | " + t.position.ToString("F4") + " | " + t.localScale.ToString("F4"));
            Directory.CreateDirectory("TestResults");
            File.WriteAllLines("TestResults/imported-body.txt", lines);
            foreach(var r in go.GetComponentsInChildren<Renderer>()) Debug.Log(r.name + " bounds=" + r.bounds);
            UnityEngine.Object.DestroyImmediate(go);
        }

        public static void ConfigureModel(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new Exception("Missing model: " + path);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.isReadable = true;
            importer.importBlendShapes = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.SaveAndReimport();
        }

        [MenuItem("HalliGalli/Prepare Character Assets")]
        public static void Prepare()
        {
            Directory.CreateDirectory(Art+"/Motions");
            Directory.CreateDirectory(Art+"/Materials");
            AssetDatabase.Refresh();
            ConfigureModel(Art+"/Player_Bald.fbx");
            ConfigureModel(Art+"/FP_Hands.fbx");
            SkinMaterial("Body","Body_BaseColor","Body_ORM",null,Color.white);
            // Match the existing character forearm's measured median skin tone.
            SkinMaterial("Hands","Hands_BaseColor","Hands_ORM","Hands_Normal",new Color(.941f,.974f,.975f));
            var nail=AssetDatabase.LoadAssetAtPath<Material>(Art+"/Materials/Nails.mat");
            if(!nail){nail=new Material(Shader.Find("Standard"));AssetDatabase.CreateAsset(nail,Art+"/Materials/Nails.mat");}
            nail.color=new Color(.77f,.63f,.55f);nail.SetFloat("_Glossiness",.40f);
            var set=AssetDatabase.LoadAssetAtPath<CharacterMotionSet>(Art+"/CustomMotions.asset");
            if(!set){set=ScriptableObject.CreateInstance<CharacterMotionSet>();AssetDatabase.CreateAsset(set,Art+"/CustomMotions.asset");}
            var body=MakeModel(false); var hands=MakeModel(true);
            set.bodyClips=Bake(body,false);set.handClips=Bake(hands,true);
            UnityEngine.Object.DestroyImmediate(body);UnityEngine.Object.DestroyImmediate(hands);
            EditorUtility.SetDirty(set); AssetDatabase.SaveAssets();
        }

        public static void PrepareAndValidate()
        {
            Prepare();MovementProjectBuilder.BuildAndValidate();
        }

        public static void PreviewIntegrated()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(MovementProjectBuilder.ScenePath);
            var driver=GameObject.Find("Player").GetComponent<CharacterMotionDriver>();
            Directory.CreateDirectory("Previews");
            foreach(string name in new[]{"Idle","Walk","Run","Jump","Fall","CrouchIdle"}) {
                var clip=driver.motions.handClips.First(c=>c.name==name);clip.SampleAnimation(driver.handsModel.gameObject,clip.length*.14f);
                Render(driver.worldCamera,driver.handCamera,"Previews/FP_"+name+".png");
            }
            PreviewBody();
        }

        public static void CaptureReel()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(MovementProjectBuilder.ScenePath);
            var driver=GameObject.Find("Player").GetComponent<CharacterMotionDriver>();
            string folder=Path.GetFullPath("../outputs/character_reel");Directory.CreateDirectory(folder);
            var body=MakeModel(false);body.transform.position=new Vector3(0,0,-50);
            var studio=new GameObject("Studio camera").AddComponent<Camera>();studio.transform.position=body.transform.position+new Vector3(2,1.45f,3.2f);studio.transform.LookAt(body.transform.position+Vector3.up*.9f);studio.fieldOfView=32;
            studio.clearFlags=CameraClearFlags.SolidColor;studio.backgroundColor=new Color(.10f,.13f,.17f);studio.cullingMask=1<<10;
            foreach(var t in body.GetComponentsInChildren<Transform>())t.gameObject.layer=10;
            int index=0;
            foreach(string state in new[]{"Idle","Walk","Run","Jump","Fall","Land","CrouchWalk"}) {
                string bodyState=state=="Walk"||state=="Run"||state=="CrouchWalk"?state+"Forward":state;
                var bc=driver.motions.bodyClips.First(c=>c.name==bodyState);var hc=driver.motions.handClips.First(c=>c.name==state);
                float duration=state=="Idle"?.7f:state=="Walk"?1.36f:state=="Run"?1:state=="CrouchWalk"?1:state=="Land"?.24f:.4f;
                int count=Mathf.CeilToInt(duration*15);
                for(int f=0;f<count;f++,index++) {
                    float time=f/15f;bc.SampleAnimation(body,Mathf.Repeat(time,bc.length));hc.SampleAnimation(driver.handsModel.gameObject,Mathf.Repeat(time,hc.length));
                    Render(studio,null,Path.Combine(folder,"Body_"+index.ToString("D3")+".png"));
                    Render(driver.worldCamera,driver.handCamera,Path.Combine(folder,"FP_"+index.ToString("D3")+".png"));
                }
            }
            File.WriteAllText(Path.Combine(folder,"frames.json"),"{\"fps\":15,\"frames\":"+index+"}");
        }

        public static void FinalPreviewAndBuild()
        {
            PreviewIntegrated();CaptureReel();PreviewHandAnatomy();
            foreach(string path in Directory.GetFiles(Art+"/Textures","*.png")) {
                var importer=AssetImporter.GetAtPath(path.Replace('\\','/')) as TextureImporter;
                if(importer==null)continue;
                importer.isReadable=false;importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.compressionQuality=100;importer.SaveAndReimport();
            }
            MovementProjectBuilder.CaptureAndBuildWindows();
        }

        public static void PreviewHandAnatomy()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.55f,.57f,.60f);
            var light=new GameObject("Hand review key").AddComponent<Light>();light.type=LightType.Directional;light.intensity=.85f;light.transform.rotation=Quaternion.Euler(45,-30,0);
            var fill=new GameObject("Hand review fill").AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=.85f;fill.transform.rotation=Quaternion.Euler(-45,150,0);
            var model=MakeModel(true);var author=new MotionPoseAuthor(model);
            var camera=new GameObject("Hand review camera").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=.27f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.10f,.13f,.17f);
            Directory.CreateDirectory("Previews");
            foreach(string state in new[]{"Open","Relaxed","Fist"}) {
                author.ReviewHands(state=="Open"?0:state=="Relaxed"?.30f:1);
                foreach(string view in new[]{"Dorsal","Palm"}) {
                    camera.transform.position=new Vector3(0,view=="Dorsal"?2:-2,.01f);camera.transform.LookAt(new Vector3(0,0,.01f),Vector3.forward);
                    Render(camera,null,"Previews/Hands_"+state+"_"+view+".png");
                }
            }
            string frames=Path.GetFullPath("../outputs/realistic_hands_v3/UnityGripFrames");Directory.CreateDirectory(frames);
            camera.transform.position=new Vector3(.0f,-1.4f,.8f);camera.transform.LookAt(new Vector3(0,0,.04f),Vector3.forward);
            for(int f=0;f<30;f++) {
                author.ReviewHands((1-Mathf.Cos(f/30f*Mathf.PI*2))*.5f);
                Render(camera,null,Path.Combine(frames,"Grip_"+f.ToString("D3")+".png"));
            }
        }

        public static void PreviewBody()
        {
            Directory.CreateDirectory(Art+"/Motions");Directory.CreateDirectory(Art+"/Materials");AssetDatabase.Refresh();
            ConfigureModel(Art+"/Player_Bald.fbx");SkinMaterial("Body","Body_BaseColor","Body_ORM",null,Color.white);
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.48f,.50f,.54f);
            var light=new GameObject("Key").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.2f;light.transform.rotation=Quaternion.Euler(35,-35,0);
            var camera=new GameObject("Preview camera").AddComponent<Camera>();camera.transform.position=new Vector3(2,1.45f,3.2f);camera.transform.LookAt(new Vector3(0,.9f,0));camera.fieldOfView=32;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.10f,.13f,.17f);
            var body=MakeModel(false);var clips=Bake(body,false);
            Directory.CreateDirectory("Previews");
            foreach(string name in new[]{"Idle","WalkForward","RunForward","CrouchIdle","Jump"}) {
                var clip=clips.First(c=>c.name==name);clip.SampleAnimation(body,clip.length*.14f);
                Render(camera,null,"Previews/Body_"+name+".png");
            }
            AssetDatabase.SaveAssets();
        }

        public static void Render(Camera camera,Camera overlay,string path)
        {
            // Camera.Render can reuse the previous skinning result within an editor frame.
            // CPU-bake the currently sampled pose for deterministic offline previews.
            var baked=new List<(SkinnedMeshRenderer skin,GameObject go,Mesh mesh)>();
            foreach(var skin in UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None)) {
                if(!skin.enabled)continue;
                var mesh=new Mesh();skin.BakeMesh(mesh,true);
                var go=new GameObject("Preview skin",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(skin.transform,false);go.layer=skin.gameObject.layer;
                go.GetComponent<MeshFilter>().sharedMesh=mesh;var mr=go.GetComponent<MeshRenderer>();mr.sharedMaterials=skin.sharedMaterials;mr.shadowCastingMode=skin.shadowCastingMode;mr.receiveShadows=skin.receiveShadows;
                skin.enabled=false;baked.Add((skin,go,mesh));
            }
            var rt=new RenderTexture(1200,1000,24);var old=RenderTexture.active;
            camera.targetTexture=rt;camera.Render();
            if(overlay){overlay.targetTexture=rt;overlay.Render();overlay.targetTexture=null;}
            RenderTexture.active=rt;
            var image=new Texture2D(1200,1000,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1200,1000),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
            camera.targetTexture=null;RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(image);rt.Release();UnityEngine.Object.DestroyImmediate(rt);
            foreach(var item in baked){item.skin.enabled=true;UnityEngine.Object.DestroyImmediate(item.go);UnityEngine.Object.DestroyImmediate(item.mesh);}
        }

        static void SkinMaterial(string name,string albedo,string orm,string normal,Color tint)
        {
            string path=Art+"/Materials/"+name+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!mat){mat=new Material(Shader.Find("Standard"));AssetDatabase.CreateAsset(mat,path);}
            mat.color=tint;mat.mainTexture=Texture(albedo,true);
            var packed=Texture(orm,false);
            if(packed){
                var src=packed.GetPixels32();var metal=new Color32[src.Length];var ao=new Color32[src.Length];
                for(int i=0;i<src.Length;i++) {metal[i]=new Color32(src[i].b,0,0,(byte)(255-src[i].g));ao[i]=new Color32(src[i].r,src[i].r,src[i].r,255);}
                SaveTexture(name+"_MetalSmooth",packed.width,packed.height,metal);
                SaveTexture(name+"_AO",packed.width,packed.height,ao);
                mat.SetTexture("_MetallicGlossMap",Texture(name+"_MetalSmooth",false));mat.EnableKeyword("_METALLICGLOSSMAP");
                mat.SetFloat("_GlossMapScale",.75f);mat.SetTexture("_OcclusionMap",Texture(name+"_AO",false));mat.SetFloat("_OcclusionStrength",.7f);
            }
            if(normal!=null && File.Exists(Art+"/Textures/"+normal+".png")) {
                var ti=(TextureImporter)AssetImporter.GetAtPath(Art+"/Textures/"+normal+".png");
                ti.textureType=TextureImporterType.NormalMap;ti.maxTextureSize=4096;ti.SaveAndReimport();
                mat.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(ti.assetPath));mat.SetFloat("_BumpScale",.14f);mat.EnableKeyword("_NORMALMAP");
            }
            EditorUtility.SetDirty(mat);
        }
        static Texture2D Texture(string name,bool srgb)
        {
            string path=Art+"/Textures/"+name+".png";
            var ti=AssetImporter.GetAtPath(path) as TextureImporter;if(ti==null)return null;
            ti.sRGBTexture=srgb;ti.isReadable=true;ti.maxTextureSize=4096;ti.textureCompression=TextureImporterCompression.Uncompressed;ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static void SaveTexture(string name,int width,int height,Color32[] pixels)
        {
            var t=new Texture2D(width,height,TextureFormat.RGBA32,false,true);t.SetPixels32(pixels);t.Apply();
            string path=Art+"/Textures/"+name+".png";File.WriteAllBytes(path,t.EncodeToPNG());UnityEngine.Object.DestroyImmediate(t);AssetDatabase.ImportAsset(path);
        }
        public static GameObject MakeModel(bool hands)
        {
            var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Art+(hands?"/FP_Hands.fbx":"/Player_Bald.fbx")));
            go.name=hands?"FirstPersonHands":"BodyVisual";
            var animator=go.GetComponent<Animator>();if(animator) UnityEngine.Object.DestroyImmediate(animator);
            var skin=AssetDatabase.LoadAssetAtPath<Material>(Art+"/Materials/"+(hands?"Hands":"Body")+".mat");
            var nail=AssetDatabase.LoadAssetAtPath<Material>(Art+"/Materials/Nails.mat");
            foreach(var r in go.GetComponentsInChildren<SkinnedMeshRenderer>()) {
                r.sharedMaterials=Enumerable.Repeat(r.name.Contains("Nail")?(nail?nail:skin):skin,r.sharedMaterials.Length).ToArray();
                r.updateWhenOffscreen=true;r.quality=SkinQuality.Bone4;
                if(hands) {r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;r.receiveShadows=false;}
            }
            return go;
        }
        public static void InstallLassoMotions()
        {
            var set=AssetDatabase.LoadAssetAtPath<CharacterMotionSet>(Art+"/CustomMotions.asset");
            var body=MakeModel(false);var hands=MakeModel(true);
            set.bodyClips=set.bodyClips.Where(c=>!c.name.StartsWith("Lasso")).Concat(Bake(body,false,true)).ToArray();
            set.handClips=set.handClips.Where(c=>!c.name.StartsWith("Lasso")).Concat(Bake(hands,true,true)).ToArray();
            UnityEngine.Object.DestroyImmediate(body);UnityEngine.Object.DestroyImmediate(hands);
            EditorUtility.SetDirty(set);AssetDatabase.SaveAssets();Debug.Log("LASSO_MOTIONS_COMPLETE");
        }
        internal static AnimationClip[] BakeKick(GameObject model,bool hands) => Bake(model,hands,false,new[]{"Kick"});
        static AnimationClip[] Bake(GameObject model,bool hands,bool lassoOnly=false,string[] states=null)
        {
            var author=new MotionPoseAuthor(model);var list=new List<AnimationClip>();
            foreach(string state in states??(lassoOnly?new[]{"LassoHold","LassoThrow","LassoPull"}:new[]{"Idle","Walk","Run","CrouchIdle","CrouchWalk","Jump","Fall","Land","PushLeft","PushRight","Kick","HitFront","HitBack","HitLeft","HitRight","Knockback","LassoHold","LassoThrow","LassoPull"})) {
                bool directional=!hands&&(state=="Walk"||state=="Run"||state=="CrouchWalk");
                string[] directions=directional?new[]{"Forward","Back","Left","Right"}:new[]{""};
                foreach(string dir in directions) {
                    float duration=state=="Idle"||state=="CrouchIdle"?2:state=="Run"?.5f:state=="Walk"?.68f:state=="CrouchWalk"?1:state=="Land"?.24f:.6f;
                    bool attack=state.StartsWith("Push")||state=="Kick";
                    bool reaction=state.StartsWith("Hit")||state=="Knockback";
                    if(attack)duration=MeleeCombat.Duration((MeleeAttack)Enum.Parse(typeof(MeleeAttack),state));
                    if(reaction)duration=state=="Knockback"?.42f:.48f;
                    if(state.StartsWith("Lasso"))duration=state=="LassoThrow"?.4f:.45f;
                    string name=state+dir;
                    var clip=new AnimationClip {name=name,legacy=true,frameRate=60,wrapMode=state=="Land"||attack||reaction?WrapMode.ClampForever:WrapMode.Loop};
                    var curves=new AnimationCurve[author.bones.Length,7];
                    for(int b=0;b<author.bones.Length;b++)for(int c=0;c<7;c++)curves[b,c]=new AnimationCurve();
                    int frames=Mathf.CeilToInt(duration*(attack||reaction?60:30));
                    Vector2 direction=dir=="Back"?Vector2.down:dir=="Left"?Vector2.left:dir=="Right"?Vector2.right:Vector2.up;
                    for(int f=0;f<=frames;f++) {
                        float phase=f/(float)frames;
                        if(hands)author.Hands(state,phase);else author.Body(state,phase,direction);
                        for(int b=0;b<author.bones.Length;b++) {
                            var t=author.bones[b];var p=t.localPosition;var q=t.localRotation;
                            float[] values={p.x,p.y,p.z,q.x,q.y,q.z,q.w};
                            for(int c=0;c<7;c++) curves[b,c].AddKey(phase*duration,values[c]);
                        }
                    }
                    string[] properties={"localPosition.x","localPosition.y","localPosition.z","localRotation.x","localRotation.y","localRotation.z","localRotation.w"};
                    for(int b=0;b<author.bones.Length;b++)for(int c=0;c<7;c++) {
                        var curve=curves[b,c];for(int k=0;k<curve.length;k++) {AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);}
                        clip.SetCurve(AnimationUtility.CalculateTransformPath(author.bones[b],model.transform),typeof(Transform),properties[c],curve);
                    }
                    clip.EnsureQuaternionContinuity();
                    string path=Art+"/Motions/"+(hands?"FP_":"Body_")+name+".anim";
                    var old=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if(old){EditorUtility.CopySerialized(clip,old);UnityEngine.Object.DestroyImmediate(clip);clip=old;}else AssetDatabase.CreateAsset(clip,path);
                    clip.name=name;EditorUtility.SetDirty(clip);list.Add(clip);
                }
            }
            author.Reset();return list.ToArray();
        }

        public static void Install(GameObject player,Camera camera,Transform socket)
        {
            var set=AssetDatabase.LoadAssetAtPath<CharacterMotionSet>(Art+"/CustomMotions.asset");
            if(!set) throw new Exception("Run HalliGalli/Prepare Character Assets before building the scene.");
            var body=MakeModel(false);body.transform.SetParent(player.transform,false);
            var hands=MakeModel(true);hands.transform.SetParent(socket,false);
            var kick=CombatAssetBuilder.MakeKickModel();kick.transform.SetParent(socket,false);
            kick.transform.localPosition=new Vector3(0,-1.32f,.18f);kick.SetActive(false);
            foreach(var t in body.GetComponentsInChildren<Transform>())t.gameObject.layer=CharacterMotionDriver.BodyLayer;
            foreach(var t in hands.GetComponentsInChildren<Transform>())t.gameObject.layer=CharacterMotionDriver.HandsLayer;
            foreach(var clip in set.bodyClips)if(clip.name=="Idle")clip.SampleAnimation(body,0);
            foreach(var clip in set.handClips)if(clip.name=="Idle")clip.SampleAnimation(hands,0);
            camera.cullingMask &= ~((1<<CharacterMotionDriver.BodyLayer)|(1<<CharacterMotionDriver.HandsLayer));
            var hc=new GameObject("HandsCamera",typeof(Camera)).GetComponent<Camera>();hc.transform.SetParent(camera.transform,false);
            hc.clearFlags=CameraClearFlags.Depth;hc.cullingMask=1<<CharacterMotionDriver.HandsLayer;
            hc.depth=camera.depth+1;hc.fieldOfView=65;hc.nearClipPlane=.01f;hc.farClipPlane=3;hc.allowHDR=false;
            var driver=player.AddComponent<CharacterMotionDriver>();driver.motor=player.GetComponent<SourceMotor>();driver.worldCamera=camera;driver.handCamera=hc;
            driver.bodyModel=body.transform;driver.handsModel=hands.transform;driver.motions=set;
            driver.kickModel=kick.transform;
            var combat=player.AddComponent<MeleeCombat>();combat.aimCamera=camera;driver.combat=combat;
            driver.receiver=ReactionAssetBuilder.InstallAudio(player);
        }
    }
}
