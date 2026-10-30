using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HalliGalli.EditorTools
{
    public static class LassoAssetImporter
    {
        public static void Import()
        {
            const string root="Assets/HalliGalliMovement/Art/Lasso";
            const string resources="Assets/HalliGalliMovement/Resources/Lasso";
            Directory.CreateDirectory(resources);
            foreach(string key in new[]{"Coil","Loop"}){
                string source=Path.GetFullPath("../outputs/varco_lasso/prepared/"+key),folder=root+"/"+key;
                if(!File.Exists(source+"/"+key+".fbx"))throw new FileNotFoundException("VARCO lasso output missing: "+key);
                Directory.CreateDirectory(folder);foreach(string file in Directory.GetFiles(source))File.Copy(file,folder+"/"+Path.GetFileName(file),true);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var info=JsonUtility.FromJson<EnvironmentAssetImporter.AssetInfo>(File.ReadAllText(folder+"/asset.json"));
                if(info.flowId!="f3bda240-6222-43a4-a214-a85fdffa8af1")throw new InvalidDataException("Unexpected VARCO workflow");
                string modelPath=folder+"/"+key+".fbx";var importer=(ModelImporter)AssetImporter.GetAtPath(modelPath);
                importer.importAnimation=false;importer.animationType=ModelImporterAnimationType.None;importer.importCameras=false;importer.importLights=false;
                importer.addCollider=false;importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.SaveAndReimport();
                var materials=info.materials.ToDictionary(m=>m.name,m=>EnvironmentAssetImporter.BuildMaterial(folder,key,m));
                var prefab=new GameObject(key);var geometry=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath),prefab.transform,false);
                try{
                    var renderers=geometry.GetComponentsInChildren<MeshRenderer>();
                    foreach(var r in renderers){
                        var mesh=info.meshes.FirstOrDefault(m=>m.name==r.name||m.name==r.GetComponent<MeshFilter>().sharedMesh.name);
                        if(mesh==null&&info.meshes.Length==1&&renderers.Length==1)mesh=info.meshes[0];
                        if(mesh==null)throw new InvalidDataException("Unmapped rope mesh: "+r.name);
                        r.sharedMaterials=mesh.materialSlots.Select(n=>materials[n]).ToArray();
                    }
                    PrefabUtility.SaveAsPrefabAsset(prefab,resources+"/"+key+".prefab");
                }finally{UnityEngine.Object.DestroyImmediate(prefab);}
            }
            AssetDatabase.SaveAssets();Debug.Log("LASSO_ASSETS_COMPLETE");
        }
    }
}
