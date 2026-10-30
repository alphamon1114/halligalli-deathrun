#if UNITY_EDITOR
using System.Collections;
using System.IO;
using HalliGalli.Movement;
using UnityEditor;
using UnityEngine;

public sealed class PyramidPreviewRunner:MonoBehaviour
{
    uint oldWidth,oldHeight;
    IEnumerator Start()
    {
        yield return null;yield return null;
        PlayModeWindow.GetRenderingResolution(out oldWidth,out oldHeight);
        var network=CourseNetwork.Instance;network.StartPractice();
        var hud=FindAnyObjectByType<CourseHUD>();hud.ReadInput=false;hud.OpenMapMenu();
        PlayModeWindow.SetCustomRenderingResolution(1920,1080,"Pyramid UI review");yield return Capture("Pyramid_MapsMenu");
        PlayModeWindow.SetCustomRenderingResolution(1024,768,"Pyramid UI review");yield return Capture("Pyramid_MapsMenu_4x3");
        hud.CloseMapMenu();network.StartLoopback(true,17898,"PyramidReview");
        yield return new WaitForSeconds(.4f);network.SelectMap("pyramid");network.AddBot();network.AddBot();
        PlayModeWindow.SetCustomRenderingResolution(1920,1080,"Pyramid UI review");yield return Capture("Pyramid_Room");
        PlayModeWindow.SetCustomRenderingResolution(1024,768,"Pyramid UI review");yield return Capture("Pyramid_Room_4x3");
        var leave=network.Leave();while(!leave.IsCompleted)yield return null;
        Debug.Log("PYRAMID_UI_CAPTURE_COMPLETE");EditorApplication.isPlaying=false;
    }
    IEnumerator Capture(string name)
    {
        yield return null;yield return null;yield return new WaitForEndOfFrame();
        var image=ScreenCapture.CaptureScreenshotAsTexture();Directory.CreateDirectory("Previews");File.WriteAllBytes("Previews/"+name+".png",image.EncodeToPNG());Destroy(image);
    }
    void OnDisable(){if(oldWidth>0)PlayModeWindow.SetCustomRenderingResolution(oldWidth,oldHeight,"Bell Run");}
}
#endif
