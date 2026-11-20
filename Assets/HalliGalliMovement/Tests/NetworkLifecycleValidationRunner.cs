#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalliGalli.Movement;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class NetworkLifecycleValidationRunner:MonoBehaviour
{
    [Serializable] sealed class Check {public string name,detail;public bool passed;}
    [Serializable] sealed class Report {public string utc;public int passed,failed;public List<Check> checks;}
    static NetworkLifecycleValidationRunner instance;
    readonly List<Check> checks=new List<Check>();
    CourseNetwork network;
    int loadedScenes;
    string savedNickname;
    bool hadNickname,restoreNickname;

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/HalliGalliMovement/Demo/BellRunCourse.unity");
        new GameObject("Network lifecycle regression").AddComponent<NetworkLifecycleValidationRunner>();
        EditorApplication.isPlaying=true;
    }
    void Awake()
    {
        if(instance&&instance!=this){Destroy(gameObject);return;}
        instance=this;DontDestroyOnLoad(gameObject);SceneManager.sceneLoaded+=SceneLoaded;
        hadNickname=PlayerPrefs.HasKey("HalliGalli.Nickname");
        savedNickname=PlayerPrefs.GetString("HalliGalli.Nickname","");
        restoreNickname=true;
    }
    void OnDestroy(){SceneManager.sceneLoaded-=SceneLoaded;if(instance==this){RestorePreferences();instance=null;}}
    void RestorePreferences()
    {
        if(!restoreNickname)return;
        if(hadNickname)PlayerPrefs.SetString("HalliGalli.Nickname",savedNickname);
        else PlayerPrefs.DeleteKey("HalliGalli.Nickname");
        PlayerPrefs.Save();restoreNickname=false;
    }
    void SceneLoaded(Scene scene,LoadSceneMode mode){loadedScenes++;}
    IEnumerator Start()
    {
        if(instance!=this)yield break;
        var routine=Validate();
        while(true){
            bool more;object next=null;
            try{more=routine.MoveNext();if(more)next=routine.Current;}
            catch(Exception e){CheckThat("Lifecycle runner completes",false,e.ToString());break;}
            if(!more)break;
            yield return next;
        }
        if(network){var close=network.Leave();while(!close.IsCompleted)yield return null;}
        RestorePreferences();
        var report=new Report{utc=DateTime.UtcNow.ToString("O"),passed=checks.Count(c=>c.passed),failed=checks.Count(c=>!c.passed),checks=checks};
        Directory.CreateDirectory("TestResults");
        File.WriteAllText("TestResults/network-lifecycle.json",JsonUtility.ToJson(report,true));
        Debug.Log("NETWORK_LIFECYCLE_RESULT "+report.passed+"/"+checks.Count);
        EditorApplication.Exit(report.failed==0?0:1);
    }
    IEnumerator Validate()
    {
        yield return null;yield return null;
        network=CourseNetwork.Instance;
        if(!network)throw new InvalidOperationException("Network runtime missing");
        for(int round=0;round<3;round++){
            network.StartLoopback(true,17845,"Lifecycle regression");
            yield return null;
            if(!network.IsHost)throw new InvalidOperationException("Loopback host did not start");
            network.AddBot();network.StartMatch();
            float deadline=Time.realtimeSinceStartup+20;
            if(round==1)CheckThat("Leaving during map loading is exercised",network.IsLoading&&!network.Playing);
            else{
                while(!network.Playing&&Time.realtimeSinceStartup<deadline)yield return null;
                CheckThat("Round "+round+": fresh room starts a real match",network.Playing&&network.Members.Count==2);
            }
            int scenesBefore=loadedScenes;
            var first=network.Leave();
            var second=network.Leave();
            CheckThat("Round "+round+": concurrent Leave waits for existing teardown",!first.IsCompleted&&!second.IsCompleted);
            CheckThat("Round "+round+": authoring stays blocked during teardown",network.Busy&&!CourseNetwork.Active);
            network.StartLoopback(true,17846,"Must not start");
            CheckThat("Round "+round+": new room cannot start during teardown",!CourseNetwork.Active);
            deadline=Time.realtimeSinceStartup+20;
            while(!first.IsCompleted&&Time.realtimeSinceStartup<deadline)yield return null;
            if(!first.IsCompleted)throw new TimeoutException("Leave did not finish");
            first.GetAwaiter().GetResult();second.GetAwaiter().GetResult();
            CheckThat("Round "+round+": awaited Leave includes replacement home scene",loadedScenes>scenesBefore&&network.Game&&network.Game.gameObject.scene.isLoaded);
            CheckThat("Round "+round+": awaited Leave releases network and roster",NetworkManager.Singleton==null&&!CourseNetwork.Active&&!network.Busy&&network.Members.Count==0);
            // No extra settling frames: the next loop immediately creates the next room.
        }
    }
    void CheckThat(string name,bool passed,string detail="")
    {
        checks.Add(new Check{name=name,passed=passed,detail=detail});
        if(!passed)Debug.LogError("NETWORK_LIFECYCLE_FAILED: "+name+" "+detail);
    }
}
#endif
