using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using HalliGalli.Movement;

namespace HalliGalli.EditorTools
{
    public static class CombatAssetBuilder
    {
        static readonly string[] AttackStates={"PushLeft","PushRight","Kick"};
        [Serializable] sealed class KickMigrationReport
        {
            public bool success;
            public string error;
            public List<ClipMigration> clips=new();
            public List<SerializedMigration> serialized=new();
            public List<ProtectedAsset> protectedAssets=new();
        }
        [Serializable] sealed class ClipMigration
        {
            public string oldPath,path,guidBefore,guidAfter;
        }
        [Serializable] sealed class ProtectedAsset
        {
            public string path,beforeSha256,afterSha256;
        }
        [Serializable] sealed class SerializedMigration
        {
            public string path,beforeSha256,afterSha256,preservedDocumentsSha256;
            public int installedViewmodels,addedDocuments,unchangedDocuments;
            public bool changed,unrelatedDataPreserved;
            [NonSerialized] public string text;
            [NonSerialized] public bool byteOrderMark;
        }
        sealed class SerializedDocument
        {
            public long id;
            public int type;
            public string text;
        }

        [Serializable] sealed class PushMotionReport
        {
            public bool success;
            public string error;
            public List<ClipMigration> clips=new();
            public List<ProtectedAsset> protectedAssets=new();
        }

        [MenuItem("HalliGalli/Refresh Palm Pushes (Four Clips Only)")]
        public static void InstallPushMotions()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Refresh push motions outside Play mode.");
            var report=new PushMotionReport();
            var preview=EditorSceneManager.NewPreviewScene();
            try {
                string art=CharacterAssetBuilder.Art;
                var bank=AssetDatabase.LoadAssetAtPath<CharacterMotionSet>(art+"/CustomMotions.asset");
                if(!bank)throw new InvalidDataException("Custom motion bank is missing.");
                foreach(bool hands in new[]{false,true})foreach(string state in new[]{"PushLeft","PushRight"}) {
                    string path=art+"/Motions/"+(hands?"FP_":"Body_")+state+".anim";
                    var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if(!clip||!(hands?bank.handClips:bank.bodyClips).Contains(clip))
                        throw new InvalidDataException("Motion bank is missing "+path);
                    report.clips.Add(new ClipMigration{path=path,guidBefore=AssetDatabase.AssetPathToGUID(path)});
                }
                var changed=new HashSet<string>(report.clips.Select(c=>c.path),StringComparer.Ordinal);
                var protectedPaths=Directory.GetFiles(art+"/Motions").Select(p=>p.Replace('\\','/')).Where(p=>!changed.Contains(p))
                    .Concat(new[]{art+"/CustomMotions.asset",art+"/CustomMotions.asset.meta"})
                    .Concat(Directory.GetFiles(MovementProjectBuilder.Root+"/Demo","*.unity"))
                    .Concat(Directory.GetFiles(MovementProjectBuilder.Root+"/Prefabs","*.prefab",SearchOption.AllDirectories));
                foreach(string path in protectedPaths)
                    report.protectedAssets.Add(new ProtectedAsset{path=path.Replace('\\','/'),beforeSha256=Hash(File.ReadAllBytes(path))});
                foreach(bool hands in new[]{false,true}) {
                    var model=CharacterAssetBuilder.MakeModel(hands);
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(model,preview);
                    try {
                        foreach(var clip in CharacterAssetBuilder.BakePushes(model,hands))AssetDatabase.SaveAssetIfDirty(clip);
                    } finally {UnityEngine.Object.DestroyImmediate(model);}
                }
                foreach(var clip in report.clips) {
                    clip.guidAfter=AssetDatabase.AssetPathToGUID(clip.path);
                    if(clip.guidBefore!=clip.guidAfter)throw new InvalidOperationException("Push clip GUID changed: "+clip.path);
                }
                foreach(var item in report.protectedAssets) {
                    item.afterSha256=Hash(File.ReadAllBytes(item.path));
                    if(item.beforeSha256!=item.afterSha256)throw new InvalidOperationException("Protected asset changed: "+item.path);
                }
                report.success=true;
                Debug.Log("PALM_PUSH_MOTIONS_COMPLETE four clips; kick, locomotion, rope, motion bank and scenes preserved.");
            } catch(Exception exception) {
                report.error=exception.ToString();throw;
            } finally {
                EditorSceneManager.ClosePreviewScene(preview);
                Directory.CreateDirectory("TestResults");
                File.WriteAllText("TestResults/palm-push-regeneration.json",JsonUtility.ToJson(report,true));
            }
        }

        [MenuItem("HalliGalli/Restore Kick (Kick Assets Only)")]
        public static void InstallKickMotions()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Install kick motions outside Play mode.");
            for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
                if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save open scenes before installing kick motions.");
            if(PrefabStageUtility.GetCurrentPrefabStage()!=null)
                throw new InvalidOperationException("Close Prefab mode before installing kick motions.");
            var report=new KickMigrationReport();
            var setup=EditorSceneManager.GetSceneManagerSetup();
            string templatePath=AssetDatabase.GenerateUniqueAssetPath(CharacterAssetBuilder.Art+"/Generated/KickMigration.prefab");
            try {
                string art=CharacterAssetBuilder.Art;
                var bank=AssetDatabase.LoadAssetAtPath<CharacterMotionSet>(art+"/CustomMotions.asset");
                if(!bank)throw new InvalidDataException("Custom motion bank is missing.");
                var protectedPaths=Directory.GetFiles(art+"/Motions")
                    .Where(p=>!new[]{"Body_","FP_"}.Any(prefix=>new[]{"StrongPush","Kick"}.Any(state=>Path.GetFileName(p)==prefix+state+".anim"||Path.GetFileName(p)==prefix+state+".anim.meta")))
                    .Concat(AssetDatabase.FindAssets("t:SourceMovementProfile",new[]{MovementProjectBuilder.Root})
                        .Select(AssetDatabase.GUIDToAssetPath).SelectMany(p=>new[]{p,p+".meta"}))
                    .Concat(new[]{art+"/CustomMotions.asset",art+"/CustomMotions.asset.meta",MovementProjectBuilder.Root+"/Runtime/SourceMotor.cs"});
                foreach(string path in protectedPaths)
                    report.protectedAssets.Add(new ProtectedAsset{path=path.Replace('\\','/'),beforeSha256=Hash(File.ReadAllBytes(path))});
                foreach(string prefix in new[]{"Body_","FP_"}) {
                    string oldPath=art+"/Motions/"+prefix+"StrongPush.anim",path=art+"/Motions/"+prefix+"Kick.anim";
                    bool oldExists=File.Exists(oldPath),newExists=File.Exists(path);
                    if(oldExists==newExists)throw new InvalidDataException("Expected exactly one attack clip at "+oldPath+" or "+path);
                    string source=oldExists?oldPath:path;
                    var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(source);
                    if(!clip||!(prefix=="Body_"?bank.bodyClips:bank.handClips).Contains(clip))
                        throw new InvalidDataException("Motion bank is missing "+source);
                    report.clips.Add(new ClipMigration{oldPath=oldPath,path=path,guidBefore=AssetDatabase.AssetPathToGUID(source)});
                }
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var kick=MakeKickModel();
                try {
                    if(PrefabUtility.IsPartOfPrefabInstance(kick))
                        PrefabUtility.UnpackPrefabInstance(kick,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                    kick.transform.localPosition=new Vector3(0,-1.32f,.18f);kick.SetActive(false);
                    PrefabUtility.SaveAsPrefabAsset(kick,templatePath);
                } finally {UnityEngine.Object.DestroyImmediate(kick);}
                var template=Documents(File.ReadAllText(templatePath));
                string driverGuid=AssetDatabase.AssetPathToGUID(MovementProjectBuilder.Root+"/Runtime/CharacterMotionDriver.cs");
                var paths=Directory.GetFiles(MovementProjectBuilder.Root+"/Demo","*.unity")
                    .Concat(Directory.GetFiles(MovementProjectBuilder.Root+"/Prefabs","*.prefab",SearchOption.AllDirectories))
                    .OrderBy(p=>p,StringComparer.Ordinal).ToArray();
                foreach(string path in paths)report.serialized.Add(PlanKickInstallation(path,driverGuid,template));
                foreach(var clip in report.clips)if(File.Exists(clip.oldPath)) {
                    string error=AssetDatabase.MoveAsset(clip.oldPath,clip.path);
                    if(!string.IsNullOrEmpty(error))throw new IOException(error);
                }
                foreach(bool hands in new[]{false,true}) {
                    var model=CharacterAssetBuilder.MakeModel(hands);
                    try {
                        foreach(var clip in CharacterAssetBuilder.BakeKick(model,hands))AssetDatabase.SaveAssetIfDirty(clip);
                    } finally {UnityEngine.Object.DestroyImmediate(model);}
                }
                foreach(var clip in report.clips) {
                    clip.guidAfter=AssetDatabase.AssetPathToGUID(clip.path);
                    if(clip.guidBefore!=clip.guidAfter)throw new InvalidOperationException("Attack clip GUID changed: "+clip.path);
                }
                // Only the driver's reference, the socket child list and new visual documents change.
                // No scene/prefab reserialization, course generation or quick-push baking occurs.
                foreach(var item in report.serialized)if(item.changed) {
                    File.WriteAllText(item.path,item.text,new UTF8Encoding(item.byteOrderMark));
                    item.afterSha256=Hash(File.ReadAllBytes(item.path));
                    AssetDatabase.ImportAsset(item.path,ImportAssetOptions.ForceSynchronousImport);
                }
                foreach(var item in report.protectedAssets) {
                    item.afterSha256=Hash(File.ReadAllBytes(item.path));
                    if(item.beforeSha256!=item.afterSha256)throw new InvalidOperationException("Protected asset changed: "+item.path);
                }
                report.success=true;
            } catch(Exception exception) {
                report.error=exception.ToString();throw;
            } finally {
                if(File.Exists(templatePath))AssetDatabase.DeleteAsset(templatePath);
                Directory.CreateDirectory("TestResults");
                File.WriteAllText("TestResults/kick-motion-migration.json",JsonUtility.ToJson(report,true));
                if(setup.Any(s=>s.isLoaded&&s.isActive))EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
            Debug.Log("KICK_MOTION_MIGRATION_COMPLETE TestResults/kick-motion-migration.json");
        }

        static string Hash(byte[] bytes)
        {
            using var sha=SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
        }
        static long Reference(string text,string field)
        {
            var match=Regex.Match(text,@"(?m)^  "+Regex.Escape(field)+@": \{fileID: (-?\d+)\}\r?$");
            return match.Success?long.Parse(match.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture):0;
        }
        static bool Script(SerializedDocument document,string guid) =>
            document.type==114&&Regex.IsMatch(document.text,@"(?m)^  m_Script: \{fileID: 11500000, guid: "+guid+@", type: 3\}\r?$");
        static List<SerializedDocument> Documents(string text)
        {
            var headers=Regex.Matches(text,@"(?m)^--- !u!(\d+) &(-?\d+)(?: stripped)?\r?$");
            if(headers.Count==0)throw new InvalidDataException("Expected text-serialized Unity asset.");
            var documents=new List<SerializedDocument>();
            for(int i=0;i<headers.Count;i++) {
                int end=i+1<headers.Count?headers[i+1].Index:text.Length;
                documents.Add(new SerializedDocument{id=long.Parse(headers[i].Groups[2].Value,System.Globalization.CultureInfo.InvariantCulture),
                    type=int.Parse(headers[i].Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture),text=text.Substring(headers[i].Index,end-headers[i].Index)});
            }
            return documents;
        }
        static SerializedMigration PlanKickInstallation(string path,string driverGuid,List<SerializedDocument> template)
        {
            byte[] bytes=File.ReadAllBytes(path);string text=File.ReadAllText(path);
            var report=new SerializedMigration{path=path.Replace('\\','/'),beforeSha256=Hash(bytes),
                byteOrderMark=bytes.Length>=3&&bytes[0]==0xef&&bytes[1]==0xbb&&bytes[2]==0xbf};
            var documents=Documents(text);var byId=documents.ToDictionary(d=>d.id);
            var replacements=new Dictionary<long,string>();var additions=new StringBuilder();
            var usedIds=new HashSet<long>(byId.Keys);long nextId=900000000000000000;
            string newline=text.Contains("\r\n")?"\r\n":"\n";
            var templateRoot=template.Single(d=>d.type==4&&Reference(d.text,"m_Father")==0);
            foreach(var driver in documents.Where(d=>Script(d,driverGuid))) {
                long hands=Reference(driver.text,"handsModel"),existing=Reference(driver.text,"kickModel");
                if(!byId.TryGetValue(hands,out var handTransform)||!byId.TryGetValue(Reference(handTransform.text,"m_Father"),out var socket))
                    throw new InvalidDataException("Missing hand socket in "+path);
                if(existing!=0) {
                    if(!byId.TryGetValue(existing,out var transform)||Reference(transform.text,"m_Father")!=socket.id||
                        !byId.TryGetValue(Reference(transform.text,"m_GameObject"),out var go)||
                        !Regex.IsMatch(go.text,@"(?m)^  m_Name: FirstPersonKick\r?$"))
                        throw new InvalidDataException("Unrecognized kick viewmodel in "+path);
                    continue;
                }
                var ids=new Dictionary<long,long>();
                foreach(var document in template) {
                    if(document.type!=1&&document.type!=4&&document.type!=137)
                        throw new InvalidDataException("Kick template contains nonvisual data.");
                    while(usedIds.Contains(nextId))nextId++;
                    ids.Add(document.id,nextId);usedIds.Add(nextId++);
                }
                long rootId=ids[templateRoot.id];
                foreach(var document in template) {
                    string added=Regex.Replace(document.text,@"(?m)^(--- !u!\d+ &)-?\d+",m=>m.Groups[1].Value+ids[document.id]);
                    added=Regex.Replace(added,@"\{fileID: (-?\d+)\}",m=>{
                        long id=long.Parse(m.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture);
                        return ids.TryGetValue(id,out long mapped)?"{fileID: "+mapped+"}":m.Value;
                    });
                    if(document.id==templateRoot.id)
                        added=Regex.Replace(added,@"(?m)^  m_Father: \{fileID: 0\}\r?$","  m_Father: {fileID: "+socket.id+"}");
                    additions.Append(added.Replace("\r\n","\n").Replace("\n",newline));
                }
                string driverText=driver.text;
                if(Regex.IsMatch(driverText,@"(?m)^  kickModel:"))
                    driverText=Regex.Replace(driverText,@"(?m)^  kickModel:[^\r\n]*","  kickModel: {fileID: "+rootId+"}");
                else driverText=Regex.Replace(driverText,@"(?m)^  handsModel:[^\r\n]*\r?\n",m=>m.Value+"  kickModel: {fileID: "+rootId+"}"+newline);
                replacements.Add(driver.id,driverText);
                string socketText=replacements.TryGetValue(socket.id,out var changed)?changed:socket.text;
                if(!Regex.IsMatch(socketText,@"(?m)^  m_Children:(?: \[\])?\r?$"))
                    throw new InvalidDataException("Unrecognized hand socket children in "+path);
                replacements[socket.id]=Regex.Replace(socketText,@"(?m)^  m_Children:(?: \[\])?\r?\n",
                    "  m_Children:"+newline+"  - {fileID: "+rootId+"}"+newline);
                report.installedViewmodels++;report.addedDocuments+=template.Count;
            }
            var output=new StringBuilder(text.Substring(0,text.IndexOf("--- !u!",StringComparison.Ordinal)));
            var preserved=new StringBuilder();
            foreach(var document in documents) {
                if(replacements.TryGetValue(document.id,out var changed))output.Append(changed);
                else {output.Append(document.text);preserved.Append(document.text);report.unchangedDocuments++;}
            }
            output.Append(additions);report.text=output.ToString();report.changed=report.text!=text;
            report.afterSha256=report.changed?Hash(new UTF8Encoding(report.byteOrderMark).GetPreamble().Concat(Encoding.UTF8.GetBytes(report.text)).ToArray()):report.beforeSha256;
            report.preservedDocumentsSha256=Hash(Encoding.UTF8.GetBytes(preserved.ToString()));
            report.unrelatedDataPreserved=true;
            return report;
        }

        public static GameObject MakeKickModel()
        {
            var model=CharacterAssetBuilder.MakeModel(false);model.name="FirstPersonKick";
            string folder=CharacterAssetBuilder.Art+"/Generated";Directory.CreateDirectory(folder);AssetDatabase.Refresh();
            foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>()) {
                var original=skin.sharedMesh;var weights=original.boneWeights;
                var leg=new HashSet<int>();
                for(int i=0;i<skin.bones.Length;i++)
                    if(skin.bones[i]&&new[]{"RightUpLeg","RightLeg","RightFoot","RightToeBase"}.Contains(skin.bones[i].name))leg.Add(i);
                float Score(int v) {var w=weights[v];return (leg.Contains(w.boneIndex0)?w.weight0:0)+(leg.Contains(w.boneIndex1)?w.weight1:0)+(leg.Contains(w.boneIndex2)?w.weight2:0)+(leg.Contains(w.boneIndex3)?w.weight3:0);}
                var triangles=new List<int>();
                if(weights.Length==original.vertexCount) {
                    var source=original.triangles;
                    for(int t=0;t<source.Length;t+=3)
                        if(Score(source[t])>.65f&&Score(source[t+1])>.65f&&Score(source[t+2])>.65f) {
                            triangles.Add(source[t]);triangles.Add(source[t+1]);triangles.Add(source[t+2]);
                        }
                }
                if(triangles.Count==0){UnityEngine.Object.DestroyImmediate(skin);continue;}
                string path=folder+"/Kick_"+skin.name+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(!mesh) {
                    mesh=UnityEngine.Object.Instantiate(original);mesh.name="RightLeg_Viewmodel";
                    mesh.subMeshCount=1;mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);
                }
                skin.sharedMesh=mesh;skin.sharedMaterials=new[]{AssetDatabase.LoadAssetAtPath<Material>(CharacterAssetBuilder.Art+"/Materials/Body.mat")};
                skin.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;skin.receiveShadows=false;
                AssetDatabase.SaveAssetIfDirty(mesh);
            }
            foreach(var t in model.GetComponentsInChildren<Transform>())t.gameObject.layer=CharacterMotionDriver.HandsLayer;
            return model;
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

        [MenuItem("HalliGalli/Preview Pushes and Kick")]
        public static void Capture()
        {
            EditorSceneManager.OpenScene(MovementProjectBuilder.ScenePath);
            var driver=GameObject.Find("Player").GetComponent<CharacterMotionDriver>();
            string folder=Path.GetFullPath("../outputs/push_reel");Directory.CreateDirectory(folder);Directory.CreateDirectory("Previews");
            var body=CharacterAssetBuilder.MakeModel(false);body.transform.position=new Vector3(0,0,-50);
            foreach(var t in body.GetComponentsInChildren<Transform>())t.gameObject.layer=10;
            var studio=new GameObject("Combat studio camera").AddComponent<Camera>();
            studio.transform.position=body.transform.position+new Vector3(2.5f,1.55f,3.5f);studio.transform.LookAt(body.transform.position+new Vector3(0,.95f,.13f));
            studio.fieldOfView=33;studio.clearFlags=CameraClearFlags.SolidColor;studio.backgroundColor=new Color(.10f,.13f,.17f);studio.cullingMask=1<<10;
            int index=0;var frameList=new List<string>();
            foreach(string name in AttackStates) {
                var bc=driver.motions.bodyClips.First(c=>c.name==name);var hc=driver.motions.handClips.First(c=>c.name==name);
                int count=Mathf.CeilToInt(bc.length*24);
                for(int f=0;f<count+5;f++,index++) {
                    float time=Mathf.Min(f/24f,bc.length);bc.SampleAnimation(body,time);hc.SampleAnimation(driver.handsModel.gameObject,time);
                    driver.kickModel.gameObject.SetActive(name=="Kick");
                    if(name=="Kick")bc.SampleAnimation(driver.kickModel.gameObject,time);
                    CharacterAssetBuilder.Render(studio,null,Path.Combine(folder,"Body_"+index.ToString("D3")+".png"));
                    CharacterAssetBuilder.Render(driver.worldCamera,driver.handCamera,Path.Combine(folder,"FP_"+index.ToString("D3")+".png"));
                    frameList.Add(name+","+time.ToString("F3",System.Globalization.CultureInfo.InvariantCulture));
                }
                var attack=(MeleeAttack)Enum.Parse(typeof(MeleeAttack),name);
                float peak=(MeleeCombat.ContactStart(attack)+MeleeCombat.ContactEnd(attack))*.5f;
                bc.SampleAnimation(body,peak);hc.SampleAnimation(driver.handsModel.gameObject,peak);
                if(name=="Kick")bc.SampleAnimation(driver.kickModel.gameObject,peak);
                CharacterAssetBuilder.Render(driver.worldCamera,driver.handCamera,"Previews/Combat_"+name+".png");
                CharacterAssetBuilder.Render(studio,null,"Previews/Combat_Body_"+name+".png");
            }
            File.WriteAllLines(Path.Combine(folder,"frames.csv"),frameList);
            UnityEngine.Object.DestroyImmediate(body);UnityEngine.Object.DestroyImmediate(studio.gameObject);
            Debug.Log("PUSH_PREVIEW_COMPLETE frames="+index);
        }
        [MenuItem("HalliGalli/Capture Runtime Pushes and Kick")]
        public static void CaptureRuntime()
        {
            EditorSceneManager.OpenScene(CourseBuilder.ScenePath);
            SessionState.SetBool("HalliGalli.SkipStartScreen",true);
            new GameObject("Push and kick preview").AddComponent<PushPreviewRunner>();
            EditorApplication.isPlaying=true;
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
