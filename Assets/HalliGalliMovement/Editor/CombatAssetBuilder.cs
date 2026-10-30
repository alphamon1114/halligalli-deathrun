using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using HalliGalli.Movement;

namespace HalliGalli.EditorTools
{
    public static class CombatAssetBuilder
    {
        public static GameObject MakeKickModel()
        {
            var model=CharacterAssetBuilder.MakeModel(false);model.name="FirstPersonKick";
            string folder=CharacterAssetBuilder.Art+"/Generated";Directory.CreateDirectory(folder);AssetDatabase.Refresh();
            foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>()) {
                var original=skin.sharedMesh;var weights=original.boneWeights;
                var leg=new HashSet<int>();
                for(int i=0;i<skin.bones.Length;i++)if(skin.bones[i] && new[]{"RightUpLeg","RightLeg","RightFoot","RightToeBase"}.Contains(skin.bones[i].name))leg.Add(i);
                float Score(int v) {var w=weights[v];return (leg.Contains(w.boneIndex0)?w.weight0:0)+(leg.Contains(w.boneIndex1)?w.weight1:0)+(leg.Contains(w.boneIndex2)?w.weight2:0)+(leg.Contains(w.boneIndex3)?w.weight3:0);}
                var triangles=new List<int>();
                if(weights.Length==original.vertexCount) {
                    var source=original.triangles;
                    for(int t=0;t<source.Length;t+=3)if(Score(source[t])>.65f && Score(source[t+1])>.65f && Score(source[t+2])>.65f) {
                        triangles.Add(source[t]);triangles.Add(source[t+1]);triangles.Add(source[t+2]);
                    }
                }
                if(triangles.Count==0){UnityEngine.Object.DestroyImmediate(skin);continue;}
                var mesh=UnityEngine.Object.Instantiate(original);mesh.name="RightLeg_Viewmodel";mesh.subMeshCount=1;mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();
                string path=folder+"/Kick_"+skin.name+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(old){EditorUtility.CopySerialized(mesh,old);UnityEngine.Object.DestroyImmediate(mesh);mesh=old;}else AssetDatabase.CreateAsset(mesh,path);
                skin.sharedMesh=mesh;skin.sharedMaterials=new[]{AssetDatabase.LoadAssetAtPath<Material>(CharacterAssetBuilder.Art+"/Materials/Body.mat")};
                skin.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;skin.receiveShadows=false;
            }
            foreach(var t in model.GetComponentsInChildren<Transform>())t.gameObject.layer=CharacterMotionDriver.HandsLayer;
            AssetDatabase.SaveAssets();return model;
        }

        public static void AddTrainingTargets(SourceMovementProfile profile)
        {
            foreach(var position in new[]{new Vector3(2.5f,.04f,-.5f),new Vector3(-2.5f,.04f,1.5f)}) {
                var go=new GameObject("Training opponent");go.transform.position=position;go.transform.rotation=Quaternion.Euler(0,180,0);
                go.AddComponent<CharacterController>();var motor=go.AddComponent<SourceMotor>();motor.profile=profile;
                var receiver=ReactionAssetBuilder.InstallAudio(go);var target=go.AddComponent<TrainingOpponent>();
                var body=CharacterAssetBuilder.MakeModel(false);body.transform.SetParent(go.transform,false);
                var motions=AssetDatabase.LoadAssetAtPath<CharacterMotionSet>(CharacterAssetBuilder.Art+"/CustomMotions.asset");
                motions.bodyClips.First(c=>c.name=="Idle").SampleAnimation(body,0);target.visual=body.transform;
                var reaction=go.AddComponent<HitReactionAnimator>();reaction.model=body.transform;reaction.motions=motions;reaction.receiver=receiver;
                var label=new GameObject("Target label").AddComponent<TextMesh>();label.transform.SetParent(go.transform,false);
                label.transform.localPosition=new Vector3(0,2.12f,0);label.text="TRAINING / 0";label.fontSize=64;label.characterSize=.03f;label.anchor=TextAnchor.MiddleCenter;label.color=new Color(1,.65f,.26f);target.label=label;
                label.transform.rotation=Quaternion.LookRotation(label.transform.position-new Vector3(0,1.7f,-3));
            }
        }

        [MenuItem("HalliGalli/Preview Combat")]
        public static void Capture()
        {
            EditorSceneManager.OpenScene(MovementProjectBuilder.ScenePath);
            var driver=GameObject.Find("Player").GetComponent<CharacterMotionDriver>();
            string folder=Path.GetFullPath("../outputs/combat_reel");Directory.CreateDirectory(folder);
            var body=CharacterAssetBuilder.MakeModel(false);body.transform.position=new Vector3(0,0,-50);
            foreach(var t in body.GetComponentsInChildren<Transform>())t.gameObject.layer=10;
            var studio=new GameObject("Combat studio camera").AddComponent<Camera>();
            studio.transform.position=body.transform.position+new Vector3(2.5f,1.55f,3.5f);studio.transform.LookAt(body.transform.position+new Vector3(0,.95f,.13f));
            studio.fieldOfView=33;studio.clearFlags=CameraClearFlags.SolidColor;studio.backgroundColor=new Color(.10f,.13f,.17f);studio.cullingMask=1<<10;
            int index=0;var frameList=new List<string>();
            foreach(string name in new[]{"PunchLeft","PunchRight","Kick"}) {
                var bc=driver.motions.bodyClips.First(c=>c.name==name);var hc=driver.motions.handClips.First(c=>c.name==name);
                int count=Mathf.CeilToInt(bc.length*24);
                for(int f=0;f<count+5;f++,index++) {
                    float time=Mathf.Min(f/24f,bc.length);bc.SampleAnimation(body,time);hc.SampleAnimation(driver.handsModel.gameObject,time);
                    driver.kickModel.gameObject.SetActive(name=="Kick");if(name=="Kick")bc.SampleAnimation(driver.kickModel.gameObject,time);
                    CharacterAssetBuilder.Render(studio,null,Path.Combine(folder,"Body_"+index.ToString("D3")+".png"));
                    CharacterAssetBuilder.Render(driver.worldCamera,driver.handCamera,Path.Combine(folder,"FP_"+index.ToString("D3")+".png"));
                    frameList.Add(name+","+time.ToString("F3",System.Globalization.CultureInfo.InvariantCulture));
                }
                float peak=bc.length*.42f;bc.SampleAnimation(body,peak);hc.SampleAnimation(driver.handsModel.gameObject,peak);
                if(name=="Kick")bc.SampleAnimation(driver.kickModel.gameObject,peak);
                CharacterAssetBuilder.Render(driver.worldCamera,driver.handCamera,"Previews/Combat_"+name+".png");
                CharacterAssetBuilder.Render(studio,null,"Previews/Combat_Body_"+name+".png");
            }
            File.WriteAllLines(Path.Combine(folder,"frames.csv"),frameList);
            Debug.Log("COMBAT_PREVIEW_COMPLETE frames="+index);
        }
        public static void PreviewAndBuild()
        {
            Capture();
            // Restore build texture settings after the authoring pass.
            foreach(string path in Directory.GetFiles(CharacterAssetBuilder.Art+"/Textures","*.png")) {
                var importer=AssetImporter.GetAtPath(path.Replace('\\','/')) as TextureImporter;if(!importer)continue;
                importer.isReadable=false;importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.compressionQuality=100;importer.SaveAndReimport();
            }
            MovementProjectBuilder.CaptureAndBuildWindows();
        }
    }
}
