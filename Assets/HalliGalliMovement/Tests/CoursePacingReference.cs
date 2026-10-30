#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

// Internal measurements only; no completion-time UI is added to the game.
[Serializable]
public sealed class CoursePacingReference
{
    public float tutorialSeconds, tutorialDistance;
    public void Save()
    {
        Directory.CreateDirectory("TestResults");
        File.WriteAllText("TestResults/compact-course-reference.json", JsonUtility.ToJson(this, true));
    }
    public static CoursePacingReference Load() => JsonUtility.FromJson<CoursePacingReference>(File.ReadAllText("TestResults/compact-course-reference.json"));
}
#endif
