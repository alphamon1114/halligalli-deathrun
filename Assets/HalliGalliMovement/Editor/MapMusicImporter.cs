using System;
using UnityEditor;
using UnityEngine;

namespace HalliGalli.EditorTools
{
    public sealed class MapMusicImporter : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/HalliGalliMovement/Resources/MapMusic/", StringComparison.Ordinal) ||
                !assetPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return;
            var importer = (AudioImporter)assetImporter;
            importer.forceToMono = false;
            importer.ambisonic = false;
            importer.loadInBackground = false;
            var settings = importer.defaultSampleSettings;
            // PCM preserves the edited sample boundary; streaming avoids retaining two full-length decoded songs.
            settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            settings.loadType = AudioClipLoadType.Streaming;
            settings.preloadAudioData = false;
            importer.defaultSampleSettings = settings;
            importer.ClearSampleSettingOverride("Standalone");
        }
    }
}
