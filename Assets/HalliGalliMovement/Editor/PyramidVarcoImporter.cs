using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HalliGalli.EditorTools
{
    public static class PyramidVarcoImporter
    {
        public const string Root=PyramidSurfBuilder.Art+"/VARCO";
        public static readonly string[] Keys={"StoneGate","LotusColumn","Anubis","ReliefPanel","Sarcophagus","Brazier"};
        public static GameObject Prefab(string key)=>AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/"+key+".prefab");
        public static bool Ready=>Keys.All(key=>Prefab(key));
        [Serializable]class Placement{public string key;public int instances;public bool textured,collidersAbsent;}
        [Serializable]class PlacementReport{public string flow;public int usedPoints;public Placement[] assets;}
        public static void WritePlacementReport()
        {
            if(!Ready)return;
            var all=UnityEngine.Object.FindObjectsByType<Transform>();
            var entries=Keys.Select(key=>{
                var instances=all.Where(t=>t.name=="VARCO "+key).ToArray();
                return new Placement{key=key,instances=instances.Length,
                    textured=instances.All(t=>t.GetComponentsInChildren<Renderer>().All(r=>r.sharedMaterials.All(m=>m&&m.mainTexture))),
                    collidersAbsent=instances.All(t=>t.GetComponentsInChildren<Collider>(true).Length==0)};
            }).ToArray();
            if(entries.Any(e=>e.instances==0||!e.textured||!e.collidersAbsent))throw new InvalidDataException("Pyramid VARCO placement validation failed");
            Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/pyramid-varco-placement.json",JsonUtility.ToJson(new PlacementReport{flow="f3bda240-6222-43a4-a214-a85fdffa8af1",usedPoints=1500,assets=entries},true));
        }

        [MenuItem("HalliGalli/Maps/Import Pyramid VARCO Models")]
        public static void Import()
        {
            string prepared=Path.GetFullPath("../outputs/varco_pyramid/prepared");
            foreach(string key in Keys){
                string folder=Path.Combine(prepared,key);
                if(!File.Exists(Path.Combine(folder,key+".fbx"))||!File.Exists(Path.Combine(folder,"asset.json")))
                    throw new FileNotFoundException("Prepare the approved VARCO model first: "+key);
            }
            Directory.CreateDirectory(Root+"/Prefabs");
            foreach(string key in Keys){
                string folder=Root+"/"+key;Directory.CreateDirectory(folder);
                foreach(string file in Directory.GetFiles(Path.Combine(prepared,key)))File.Copy(file,Path.Combine(folder,Path.GetFileName(file)),true);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach(string key in Keys)EnvironmentAssetImporter.ImportOne(key,Root);
            AssetDatabase.SaveAssets();PyramidSurfBuilder.Build();
            Debug.Log("PYRAMID_VARCO_IMPORTED six private 2K PBR assets");
        }

        // Each prefab has a bottom-centre pivot. Fit uniformly, then align its actual
        // renderer bounds so differently reconstructed assets never float above a base.
        public static GameObject Place(Transform parent,string key,Vector3 foot,float height,float yaw=0)
        {
            var obj=(GameObject)PrefabUtility.InstantiatePrefab(Prefab(key),parent);obj.name="VARCO "+key;
            obj.transform.localPosition=Vector3.zero;obj.transform.localRotation=Quaternion.identity;obj.transform.localScale=Vector3.one;
            var renderers=obj.GetComponentsInChildren<Renderer>();
            Bounds b=renderers[0].bounds;foreach(var r in renderers.Skip(1))b.Encapsulate(r.bounds);
            obj.transform.localScale=Vector3.one*height/Mathf.Max(.001f,b.size.y);
            obj.transform.rotation=Quaternion.Euler(0,yaw,0);
            b=renderers[0].bounds;foreach(var r in renderers.Skip(1))b.Encapsulate(r.bounds);
            obj.transform.position+=foot-new Vector3(b.center.x,b.min.y,b.center.z);
            foreach(var c in obj.GetComponentsInChildren<Collider>(true))UnityEngine.Object.DestroyImmediate(c);
            return obj;
        }
    }
}
