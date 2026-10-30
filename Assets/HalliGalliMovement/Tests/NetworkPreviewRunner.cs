#if UNITY_EDITOR
using System.Collections;
using System.IO;
using HalliGalli.Movement;
using UnityEditor;
using UnityEngine;

public sealed class NetworkPreviewRunner:MonoBehaviour
{
    public bool openOnly;
    uint previousWidth,previousHeight;
    IEnumerator Start()
    {
        DontDestroyOnLoad(gameObject);
        yield return null;yield return null;
        var network=CourseNetwork.Instance;network.ShowMenu();
        if(openOnly){Destroy(gameObject);yield break;}
        PlayModeWindow.GetRenderingResolution(out previousWidth,out previousHeight);PlayModeWindow.SetCustomRenderingResolution(1920,1080,"Bell Run UI review");
        yield return Capture("Online_Menu");
        var task=network.Connect(true);
        while(!task.IsCompleted)yield return null;
        yield return Capture("Online_Room");
        string result=CourseNetwork.Active&&network.IsHost&&!string.IsNullOrEmpty(network.InviteCode)?"PASSED: editor created a real Unity Relay room":"FAILED: "+network.Status;
        Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/network-editor-preview.txt",result);
        task=network.Leave();while(!task.IsCompleted)yield return null;
        yield return null;
        Debug.Log("ONLINE_PREVIEW "+result);
        EditorApplication.isPlaying=false;
    }
    IEnumerator Capture(string name)
    {
        yield return null;yield return new WaitForEndOfFrame();
        Directory.CreateDirectory("Previews");var image=ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes("Previews/"+name+".png",image.EncodeToPNG());Destroy(image);
    }
    void OnDisable(){if(previousWidth>0&&previousHeight>0)PlayModeWindow.SetCustomRenderingResolution(previousWidth,previousHeight,"Bell Run UI review");}
}
#endif
