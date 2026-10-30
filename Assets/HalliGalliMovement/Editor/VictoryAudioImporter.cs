using System.IO;
using UnityEditor;
using UnityEngine;

namespace HalliGalli.EditorTools
{
    public static class VictoryAudioImporter
    {
        public static void Import()
        {
            const string path="Assets/HalliGalliMovement/Resources/UIAudio/Victory.wav";
            if(!File.Exists(path))throw new FileNotFoundException("Missing actual VARCO victory export",path);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var importer=(AudioImporter)AssetImporter.GetAtPath(path);
            importer.forceToMono=false;importer.loadInBackground=false;
            var settings=importer.defaultSampleSettings;settings.loadType=AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat=AudioCompressionFormat.PCM;settings.preloadAudioData=true;
            importer.defaultSampleSettings=settings;importer.SaveAndReimport();
            var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if(!clip||clip.length<.2f||clip.length>3)throw new System.InvalidOperationException("Winner sound must be between 0.2 and 3 seconds");
            AssetDatabase.SaveAssets();Debug.Log("VICTORY_AUDIO_IMPORTED "+clip.length.ToString("F3")+"s");
        }
    }
}
