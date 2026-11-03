using HalliGalli.Movement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HalliGalli.EditorTools
{
    public static class ClockworkCourseVerification
    {
        [MenuItem("HalliGalli/Maps/Validate Clockwork Course")]
        public static void Validate()
        {
            EditorSceneManager.OpenScene(ClockworkCourse.ScenePath);
            SessionState.SetBool("HalliGalli.SkipStartScreen",true);
            new GameObject("Clockwork validation").AddComponent<ClockworkValidationRunner>();
            PlayerSettings.runInBackground=true;
            EditorApplication.isPaused=false;
            EditorApplication.isPlaying=true;
        }
    }
}
