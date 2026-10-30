using UnityEditor;

namespace HalliGalli.EditorTools
{
    public sealed class UIAssetImporter:AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if(!assetPath.Contains("HalliGalliMovement/Resources/UI/"))return;
            bool icon=assetPath.Contains("/Icons/");
            var importer=(TextureImporter)assetImporter;
            importer.textureType=TextureImporterType.Default;importer.maxTextureSize=icon?256:1024;
            importer.mipmapEnabled=false;importer.isReadable=false;
            importer.alphaIsTransparency=icon;
            importer.textureCompression=icon?TextureImporterCompression.Uncompressed:TextureImporterCompression.Compressed;
        }
    }
}
