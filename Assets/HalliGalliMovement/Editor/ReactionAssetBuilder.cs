using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using HalliGalli.Movement;

namespace HalliGalli.EditorTools
{
    public static class ReactionAssetBuilder
    {
        public const string AudioFolder=MovementProjectBuilder.Root+"/Audio";
        [MenuItem("HalliGalli/Prepare Impact Audio")]
        public static void PrepareAudio()
        {
            Directory.CreateDirectory(AudioFolder);AssetDatabase.Refresh();
            foreach(string path in Directory.GetFiles(AudioFolder,"*.wav")){
                var importer=(AudioImporter)AssetImporter.GetAtPath(path.Replace('\\','/'));
                var settings=importer.defaultSampleSettings;settings.loadType=AudioClipLoadType.DecompressOnLoad;settings.compressionFormat=AudioCompressionFormat.PCM;
                settings.preloadAudioData=true;importer.defaultSampleSettings=settings;importer.forceToMono=true;importer.SaveAndReimport();
            }
            string asset=AudioFolder+"/ImpactSounds.asset";
            var set=AssetDatabase.LoadAssetAtPath<ImpactAudioSet>(asset);
            if(!set){set=ScriptableObject.CreateInstance<ImpactAudioSet>();AssetDatabase.CreateAsset(set,asset);}
            set.pushes=Directory.GetFiles(AudioFolder,"Punch*.wav").OrderBy(p=>p).Select(p=>AssetDatabase.LoadAssetAtPath<AudioClip>(p.Replace('\\','/'))).ToArray();
            set.strongPushes=Directory.GetFiles(AudioFolder,"Kick*.wav").OrderBy(p=>p).Select(p=>AssetDatabase.LoadAssetAtPath<AudioClip>(p.Replace('\\','/'))).ToArray();
            EditorUtility.SetDirty(set);AssetDatabase.SaveAssets();
        }
        public static MeleeReceiver InstallAudio(GameObject actor)
        {
            var receiver=actor.GetComponent<MeleeReceiver>();if(!receiver)receiver=actor.AddComponent<MeleeReceiver>();
            receiver.audioSet=AssetDatabase.LoadAssetAtPath<ImpactAudioSet>(AudioFolder+"/ImpactSounds.asset");
            var source=new GameObject("ImpactAudio").AddComponent<AudioSource>();source.transform.SetParent(actor.transform,false);source.transform.localPosition=Vector3.up*1.3f;
            source.playOnAwake=false;source.loop=false;source.spatialBlend=1;source.dopplerLevel=0;source.minDistance=1.5f;source.maxDistance=18;source.rolloffMode=AudioRolloffMode.Linear;
            receiver.impactSource=source;return receiver;
        }
        public static void PrepareAndValidate()
        {
            PrepareAudio();CharacterAssetBuilder.PrepareAndValidate();
        }
        public static void Capture()
        {
            EditorSceneManager.OpenScene(MovementProjectBuilder.ScenePath);
            var driver=GameObject.Find("Player").GetComponent<CharacterMotionDriver>();
            var body=CharacterAssetBuilder.MakeModel(false);body.transform.position=new Vector3(0,0,-50);
            foreach(var t in body.GetComponentsInChildren<Transform>())t.gameObject.layer=10;
            var camera=new GameObject("Reaction studio").AddComponent<Camera>();camera.transform.position=body.transform.position+new Vector3(2.4f,1.6f,3.7f);
            camera.transform.LookAt(body.transform.position+Vector3.up*.9f);camera.fieldOfView=33;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.10f,.13f,.17f);camera.cullingMask=1<<10;
            string folder=Path.GetFullPath("../outputs/reaction_reel");Directory.CreateDirectory(folder);int index=0;var labels=new System.Collections.Generic.List<string>();
            foreach(string name in new[]{"HitFront","HitLeft","HitBack","Knockback"}){
                var bc=driver.motions.bodyClips.First(c=>c.name==name);var hc=driver.motions.handClips.First(c=>c.name==name);
                for(int f=0;f<22;f++,index++){
                    float time=Mathf.Min(f/30f,bc.length);bc.SampleAnimation(body,time);hc.SampleAnimation(driver.handsModel.gameObject,time);
                    CharacterAssetBuilder.Render(camera,null,Path.Combine(folder,"Body_"+index.ToString("D3")+".png"));
                    CharacterAssetBuilder.Render(driver.worldCamera,driver.handCamera,Path.Combine(folder,"FP_"+index.ToString("D3")+".png"));labels.Add(name);
                    if(f==2){CharacterAssetBuilder.Render(camera,null,"Previews/Reaction_"+name+".png");}
                }
            }
            File.WriteAllLines(Path.Combine(folder,"frames.txt"),labels);
        }
        public static void PreviewAndBuild()
        {
            Capture();
            foreach(string path in Directory.GetFiles(CharacterAssetBuilder.Art+"/Textures","*.png")){
                var importer=AssetImporter.GetAtPath(path.Replace('\\','/')) as TextureImporter;if(!importer)continue;
                importer.isReadable=false;importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.compressionQuality=100;importer.SaveAndReimport();
            }
            MovementProjectBuilder.CaptureAndBuildWindows();
        }
    }
}
