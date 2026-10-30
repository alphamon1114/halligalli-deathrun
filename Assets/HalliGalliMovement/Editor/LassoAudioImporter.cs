using System.IO;
using UnityEditor;
using UnityEngine;

namespace HalliGalli.EditorTools
{
    public static class LassoAudioImporter
    {
        public static void Import()
        {
            const string root="Assets/HalliGalliMovement/Resources/LassoAudio/";
            foreach(string name in new[]{"Throw","Latch","Pull"})if(!File.Exists(root+name+".wav"))throw new FileNotFoundException("Missing VARCO sound: "+name);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach(string name in new[]{"Throw","Latch","Pull"}){
                var importer=(AudioImporter)AssetImporter.GetAtPath(root+name+".wav");
                importer.forceToMono=true;importer.loadInBackground=false;
                var settings=importer.defaultSampleSettings;settings.loadType=AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat=AudioCompressionFormat.PCM;settings.preloadAudioData=true;importer.defaultSampleSettings=settings;importer.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();Debug.Log("LASSO_AUDIO_IMPORTED");
        }
    }
}
