using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HalliGalli.EditorTools
{
    // Local, finite editor commands let the open project compile and validate without a second Unity process.
    [InitializeOnLoad]
    public static class CourseEditorJobs
    {
        static double next;static string pending;
        static string Folder=>Path.GetFullPath("../outputs/course-jobs");
        static CourseEditorJobs(){EditorApplication.update+=Update;EditorApplication.playModeStateChanged+=state=>{if(state==PlayModeStateChange.EnteredEditMode)SessionState.EraseBool("HalliGalli.SkipStartScreen");};}
        static void Update()
        {
            if(EditorApplication.timeSinceStartup<next || EditorApplication.isCompiling || EditorApplication.isUpdating)return;
            next=EditorApplication.timeSinceStartup+.5;
            string request=Path.Combine(Folder,"request.txt");
            if(pending==null && File.Exists(request)){
                AssetDatabase.Refresh();
                if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
                pending=File.ReadAllText(request).Trim();File.Delete(request);
            }
            if(pending==null)return;
            if(EditorApplication.isPlaying){EditorApplication.isPlaying=false;return;}
            string job=pending;pending=null;
            Directory.CreateDirectory(Folder);
            try{
                if(job=="pyramid-stone"){
                    PyramidStoneAssets.Apply();File.WriteAllText(Path.Combine(Folder,"status.txt"),"DISPATCHED pyramid-stone");return;
                }
                if(job=="pyramid-ui"){
                    EditorSceneManager.OpenScene(PyramidSurfBuilder.ScenePath);new GameObject("Pyramid UI review").AddComponent<PyramidPreviewRunner>();
                    EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Focus();EditorApplication.isPlaying=true;
                    File.WriteAllText(Path.Combine(Folder,"status.txt"),"DISPATCHED pyramid-ui");return;
                }
                if(job=="pyramid-varco"){
                    PyramidVarcoImporter.Import();File.WriteAllText(Path.Combine(Folder,"status.txt"),"DISPATCHED pyramid-varco");return;
                }
                if(job=="pyramid"||job=="pyramid-probe"||job=="pyramid-validate"||job=="pyramid-capture"){
                    if(job=="pyramid")PyramidSurfBuilder.Build();else if(job=="pyramid-capture")PyramidSurfBuilder.Capture();else PyramidSurfBuilder.Validate(job=="pyramid-probe");
                    File.WriteAllText(Path.Combine(Folder,"status.txt"),"DISPATCHED "+job);return;
                }
                if(job=="controls-validate"||job=="controls-and-barriers"){
                    if(job=="controls-and-barriers")SurfDividerBuilder.Install();
                    EditorSceneManager.OpenScene(CourseBuilder.ScenePath);new GameObject("Controls validation").AddComponent<ControlsValidationRunner>();
                    EditorApplication.isPlaying=true;File.WriteAllText(Path.Combine(Folder,"status.txt"),"DISPATCHED controls-validate");return;
                }
                if(job=="victory-preview"){
                    EditorSceneManager.OpenScene(CourseBuilder.ScenePath);new GameObject("Victory preview").AddComponent<VictoryPreviewRunner>();
                    EditorApplication.isPlaying=true;File.WriteAllText(Path.Combine(Folder,"status.txt"),"DISPATCHED victory-preview");return;
                }
                if(job=="victory-audio"){
                    VictoryAudioImporter.Import();File.WriteAllText(Path.Combine(Folder,"status.txt"),"DISPATCHED victory-audio");return;
                }
                if(job=="electric-preview"){
                    SurfDividerBuilder.CaptureElectricity();File.WriteAllText(Path.Combine(Folder,"status.txt"),"DISPATCHED electric-preview");return;
                }
                if(job=="electric-check"){
                    SurfDividerBuilder.CheckVisuals();File.WriteAllText(Path.Combine(Folder,"status.txt"),"DISPATCHED electric-check");return;
                }
                if(job=="surf-divider"){
                    SurfDividerBuilder.Install();File.WriteAllText(Path.Combine(Folder,"status.txt"),"DISPATCHED surf-divider");return;
                }
                if(job=="tutorial-ferry"||job=="ferry-validate"||job=="ferry-probe"||job=="ferry-capture"){
                    if(job=="tutorial-ferry")TutorialFerryBuilder.Build();else if(job=="ferry-capture")TutorialFerryBuilder.Capture();else TutorialFerryBuilder.Validate(job=="ferry-probe");
                    File.WriteAllText(Path.Combine(Folder,"status.txt"),"DISPATCHED "+job);return;
                }
                if(job=="factory"||job=="factory-validate"||job=="factory-probe"||job=="factory-capture"){
                    if(job=="factory")FactoryCourseBuilder.Build();
                    else if(job=="factory-capture")FactoryCourseBuilder.Capture();
                    else FactoryCourseBuilder.Validate(job=="factory-probe");
                    File.WriteAllText(Path.Combine(Folder,"status.txt"),"DISPATCHED "+job);return;
                }
                if(job=="export-package"){
                    Directory.CreateDirectory("Exports");AssetDatabase.ExportPackage(new[]{MovementProjectBuilder.Root,HalliGalli.CardEditor.CardAssetBuilder.Root},"Exports/HalliGalliMovement.unitypackage",ExportPackageOptions.Recurse);
                    File.WriteAllText(Path.Combine(Folder,"status.txt"),"DISPATCHED export-package");return;
                }
                if(job=="practice-build"){
                    CombatPracticeBuilder.Build();File.WriteAllText(Path.Combine(Folder,"status.txt"),"DISPATCHED practice-build");return;
                }
                if(job=="practice-validate"){
                    EditorSceneManager.OpenScene(CourseBuilder.ScenePath);new GameObject("Practice review").AddComponent<PracticeValidationRunner>();
                    EditorApplication.isPlaying=true;File.WriteAllText(Path.Combine(Folder,"status.txt"),"DISPATCHED practice-validate");return;
                }
                if(job=="resolve-packages"){
                    UnityEditor.PackageManager.Client.Resolve();
                    File.WriteAllText(Path.Combine(Folder,"status.txt"),"DISPATCHED resolve-packages");
                    return;
                }
                if(job=="lasso-motions"){
                    CharacterAssetBuilder.InstallLassoMotions();File.WriteAllText(Path.Combine(Folder,"status.txt"),"DISPATCHED lasso-motions");return;
                }
                if(job=="lasso-import"){
                    LassoAssetImporter.Import();File.WriteAllText(Path.Combine(Folder,"status.txt"),"DISPATCHED lasso-import");return;
                }
                if(job=="lasso-audio"){
                    LassoAudioImporter.Import();File.WriteAllText(Path.Combine(Folder,"status.txt"),"DISPATCHED lasso-audio");return;
                }
                if(job=="lasso-validate"){
                    EditorSceneManager.OpenScene(CourseBuilder.ScenePath);new GameObject("Lasso validation").AddComponent<LassoValidationRunner>();
                    EditorApplication.isPlaying=true;File.WriteAllText(Path.Combine(Folder,"status.txt"),"DISPATCHED lasso-validate");return;
                }
                if(job=="ui-preview"){
                    EditorSceneManager.OpenScene(CourseBuilder.ScenePath);new GameObject("UI preview").AddComponent<UIPreviewRunner>();
                    EditorApplication.isPlaying=true;File.WriteAllText(Path.Combine(Folder,"status.txt"),"DISPATCHED ui-preview");return;
                }
                if(job=="start-preview"){
                    EditorSceneManager.OpenScene(CourseBuilder.ScenePath);new GameObject("Start screen review").AddComponent<StartScreenValidationRunner>();
                    EditorApplication.isPlaying=true;File.WriteAllText(Path.Combine(Folder,"status.txt"),"DISPATCHED start-preview");return;
                }
                if(job=="room-bots-validate"){
                    EditorSceneManager.OpenScene(CourseBuilder.ScenePath);new GameObject("AI room review").AddComponent<RoomBotsValidationRunner>();
                    EditorApplication.isPlaying=true;File.WriteAllText(Path.Combine(Folder,"status.txt"),"DISPATCHED room-bots-validate");return;
                }
                if(job=="checkpoints"||job=="checkpoint-validate"||job=="checkpoint-capture"||job=="punch-preview"){
                    for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++){
                        var scene=UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                        if(scene.isDirty){Directory.CreateDirectory(MovementProjectBuilder.Root+"/Demo/Backups");EditorSceneManager.SaveScene(scene,MovementProjectBuilder.Root+"/Demo/Backups/"+scene.name+"_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".unity",true);}
                    }
                }
                if(job=="checkpoints"){
                    CheckpointBuilder.Install();File.WriteAllText(Path.Combine(Folder,"status.txt"),"DISPATCHED checkpoints");return;
                }
                if(job=="punch-preview"){
                    EditorSceneManager.OpenScene(CourseBuilder.ScenePath);new GameObject("Punch preview").AddComponent<PunchPreviewRunner>();
                    EditorApplication.isPlaying=true;File.WriteAllText(Path.Combine(Folder,"status.txt"),"DISPATCHED punch-preview");return;
                }
                if(job=="checkpoint-validate"){
                    EditorSceneManager.OpenScene(CourseBuilder.ScenePath);new GameObject("Checkpoint validation").AddComponent<CheckpointValidationRunner>();
                    EditorApplication.isPlaying=true;File.WriteAllText(Path.Combine(Folder,"status.txt"),"DISPATCHED checkpoint-validate");return;
                }
                if(job=="checkpoint-capture"){
                    CheckpointBuilder.Capture();File.WriteAllText(Path.Combine(Folder,"status.txt"),"DISPATCHED checkpoint-capture");return;
                }
                if(job=="network-preview"||job=="network-open"){
                    EditorSceneManager.OpenScene(CourseBuilder.ScenePath);
                    new GameObject("Online preview").AddComponent<NetworkPreviewRunner>().openOnly=job=="network-open";
                    EditorApplication.isPlaying=true;
                    File.WriteAllText(Path.Combine(Folder,"status.txt"),"DISPATCHED "+job);return;
                }
                if(job!="card-previews"&&job!="maps-menu-capture"&&job!="surf"&&job!="surf-probe"&&job!="surf-validate"&&job!="surf-capture"&&job!="climb-fork"&&job!="environment-place"&&job!="environment"&&job!="environment-capture"&&job!="maps-probe"&&job!="maps"&&job!="maps-validate"&&job!="maps-capture"&&job!="build-validate"&&job!="validate"&&job!="capture"&&job!="capture-build"&&job!="regression"&&job!="cards"&&job!="match"&&job!="match-validate"&&job!="roster"&&job!="roster-validate"&&job!="roster-capture"&&job!="open")throw new Exception("Unknown course job");
                for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++){
                    var scene=UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                    if(scene.isDirty){Directory.CreateDirectory(MovementProjectBuilder.Root+"/Demo/Backups");EditorSceneManager.SaveScene(scene,MovementProjectBuilder.Root+"/Demo/Backups/"+scene.name+"_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".unity",true);}
                }
                File.WriteAllText(Path.Combine(Folder,"status.txt"),"RUNNING "+job);
                if(job=="surf")SurfCourseBuilder.Build();
                else if(job=="surf-probe")SurfCourseBuilder.Validate(true);
                else if(job=="surf-validate")SurfCourseBuilder.Validate();
                else if(job=="surf-capture")SurfCourseBuilder.Capture();
                else if(job=="climb-fork")CourseMapsBuilder.BuildFork();
                else if(job=="environment"){EnvironmentAssetImporter.Import();EnvironmentArtBuilder.Install();}
                else if(job=="environment-place")EnvironmentArtBuilder.Install();
                else if(job=="environment-capture")EnvironmentArtBuilder.Capture();
                else if(job=="maps"){CourseMapsBuilder.InstallAndBuildClimb();CourseMapsBuilder.Validate();}
                else if(job=="maps-validate")CourseMapsBuilder.Validate();
                else if(job=="maps-probe")CourseMapsBuilder.Probe();
                else if(job=="maps-capture")CourseMapsBuilder.Capture();
                else if(job=="maps-menu-capture")CourseMapsBuilder.CaptureMenus();
                else if(job=="build-validate"){CourseBuilder.Build();CourseBuilder.Validate();}
                else if(job=="validate")CourseBuilder.Validate();
                else if(job=="capture-build")CourseBuilder.CaptureAndBuild();
                else if(job=="capture")CourseBuilder.Capture();
                else if(job=="cards")HalliGalli.CardEditor.CardAssetBuilder.BuildAndValidate();
                else if(job=="match")MatchIntegrationBuilder.InstallAndValidate();
                else if(job=="match-validate")MatchIntegrationBuilder.Validate();
                else if(job=="card-previews")MatchIntegrationBuilder.RefreshCardPreviews();
                else if(job=="roster")RosterBuilder.InstallAndValidate();
                else if(job=="roster-validate")RosterBuilder.Validate();
                else if(job=="roster-capture")RosterBuilder.Capture();
                else if(job=="regression")CombatTuningBuilder.Validate();
                else EditorSceneManager.OpenScene(CourseBuilder.ScenePath);
                File.WriteAllText(Path.Combine(Folder,"status.txt"),"DISPATCHED "+job);
            }catch(Exception e){File.WriteAllText(Path.Combine(Folder,"status.txt"),"FAILED "+job+"\n"+e);Debug.LogException(e);}
            finally{if(EditorApplication.isPlayingOrWillChangePlaymode&&job!="start-preview")SessionState.SetBool("HalliGalli.SkipStartScreen",true);}
        }
    }
}
