using System.IO;
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HalliGalli.EditorTools
{
    public static class PyramidStoneAssets
    {
        const string Folder=PyramidSurfBuilder.Art+"/StoneScan";
        [Serializable] class Check { public string name; public bool passed; public string detail; }
        [Serializable] class Report { public int passed,failed; public Check[] checks; }
        public static void Apply()
        {
            var wall=PyramidArtBuilder.Material("Sandstone",Color.white,false,true);
            var ramp=PyramidArtBuilder.Material("Polished limestone",Color.white,false,true);
            AssetDatabase.SaveAssets();PyramidSurfBuilder.Capture();
            var messages=ShaderUtil.GetShaderMessages(wall.shader);
            var checks=new[]{
                new Check{name="Stone shader compiles",passed=wall.shader.isSupported&&!messages.Any(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error),detail=string.Join("\n",messages.Select(m=>m.message))},
                new Check{name="Wall has all four scanned maps",passed=new[]{"_MainTex","_BumpMap","_MaskMap","_HeightMap"}.All(p=>wall.GetTexture(p)&&wall.GetTexture(p).width==4096)},
                new Check{name="Normal map imported as normal data",passed=((TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(wall.GetTexture("_BumpMap")))).textureType==TextureImporterType.NormalMap},
                new Check{name="Wall and ramp have different relief",passed=wall.GetFloat("_BumpScale")>ramp.GetFloat("_BumpScale")},
                new Check{name="CC0 attribution included",passed=File.Exists(Folder+"/LICENSE.txt")}
            };
            Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/pyramid-stone-validation.json",JsonUtility.ToJson(new Report{passed=checks.Count(c=>c.passed),failed=checks.Count(c=>!c.passed),checks=checks},true));
            if(checks.Any(c=>!c.passed))throw new InvalidOperationException("Stone material validation failed");
        }
        public static void Configure(Material material,bool polished)
        {
            material.shader=Shader.Find("HalliGalli/Sandstone");
            material.SetTexture("_MainTex",Texture("diff_4k.jpg",true,false));
            material.SetTexture("_BumpMap",Texture("nor_gl_4k.png",false,true));
            material.SetTexture("_MaskMap",Texture("arm_4k.jpg",false,false));
            material.SetTexture("_HeightMap",Texture("disp_4k.jpg",false,false));
            material.color=polished?new Color(.99f,.97f,.91f):new Color(.95f,.93f,.89f);
            material.SetFloat("_TileMetres",polished?6:8);
            material.SetFloat("_BumpScale",polished?.35f:1.1f);
            material.SetFloat("_Parallax",polished?.003f:.012f);
            material.SetFloat("_Polish",polished?.6f:.25f);
        }
        static Texture2D Texture(string suffix,bool colour,bool normal)
        {
            string path=Folder+"/sandstone_blocks_05_"+suffix;
            if(!File.Exists(path))throw new FileNotFoundException("Missing CC0 stone scan",path);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            var type=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
            if(importer.textureType!=type||importer.sRGBTexture!=colour||importer.maxTextureSize!=4096||importer.anisoLevel!=8||importer.compressionQuality!=100){
                importer.textureType=type;importer.sRGBTexture=colour;importer.maxTextureSize=4096;
                importer.anisoLevel=8;importer.compressionQuality=100;importer.wrapMode=TextureWrapMode.Repeat;
                importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.mipmapEnabled=true;importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
