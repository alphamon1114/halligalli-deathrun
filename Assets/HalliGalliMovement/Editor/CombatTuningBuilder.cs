using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace HalliGalli.EditorTools
{
    public static class CombatTuningBuilder
    {
        public static void Validate()
        {
            // Keep the user's existing scene and art; only persist newly added defaults.
            var scene=EditorSceneManager.OpenScene(MovementProjectBuilder.ScenePath);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            string prefab=MovementProjectBuilder.Root+"/Prefabs/SourcePlayer.prefab";
            var root=PrefabUtility.LoadPrefabContents(prefab);
            try{PrefabUtility.SaveAsPrefabAsset(root,prefab);}finally{PrefabUtility.UnloadPrefabContents(root);}
            var validation=new GameObject("Movement validation");validation.AddComponent<MovementValidationRunner>();
            EditorApplication.isPlaying=true;
        }
    }
}
