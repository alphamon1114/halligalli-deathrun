using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using HalliGalli.Cards;
using HalliGalli.EditorTools;

namespace HalliGalli.CardEditor
{
    public static class CardAssetBuilder
    {
        public const string Root="Assets/HalliGalliCards";
        public const string DeckPath=Root+"/Data/Classic56.asset";
        public const string GalleryPath=Root+"/Demo/CardGallery.unity";
        public static bool HasDeck=>AssetDatabase.LoadAssetAtPath<ClassicFruitDeck>(DeckPath);
        [MenuItem("HalliGalli/Cards/Build, Install and Validate Classic Deck")]
        public static void BuildAndValidate()
        {
            BuildAssets();EditorSceneManager.OpenScene(CourseBuilder.ScenePath);InstallTableInCurrentScene();EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.OpenScene(CourseBuilder.ScenePath);Validate();CaptureTable();CreateGallery();
            EditorSceneManager.OpenScene(CourseBuilder.ScenePath);
            Directory.CreateDirectory("Exports");AssetDatabase.ExportPackage(new[]{Root+"/Runtime",Root+"/Data",Root+"/Art",Root+"/Prefabs",Root+"/Materials",Root+"/Meshes",Root+"/Demo",Root+"/README_KO.md",Root+"/classic-deck.json"},"Exports/HalliGalliClassicCards.unitypackage",ExportPackageOptions.Recurse);
            Debug.Log("CLASSIC_CARDS_READY");
        }
        public static void BuildAssets()
        {
            foreach(string folder in new[]{"Data","Materials","Prefabs","Meshes","Demo"})Directory.CreateDirectory(Root+"/"+folder);AssetDatabase.Refresh();
            foreach(string path in Directory.GetFiles(Root+"/Art","*.png")){
                var importer=(TextureImporter)AssetImporter.GetAtPath(path.Replace('\\','/'));importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;importer.alphaIsTransparency=true;importer.npotScale=TextureImporterNPOTScale.None;importer.mipmapEnabled=true;importer.maxTextureSize=2048;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=8;importer.SaveAndReimport();
            }
            var deck=AssetDatabase.LoadAssetAtPath<ClassicFruitDeck>(DeckPath);if(!deck){deck=ScriptableObject.CreateInstance<ClassicFruitDeck>();AssetDatabase.CreateAsset(deck,DeckPath);}
            var definitions=new List<FruitCardDefinition>();
            foreach(FruitKind fruit in Enum.GetValues(typeof(FruitKind)))for(int count=1;count<=5;count++){
                string name=fruit+"_"+count,path=Root+"/Data/"+name+".asset";var card=AssetDatabase.LoadAssetAtPath<FruitCardDefinition>(path);if(!card){card=ScriptableObject.CreateInstance<FruitCardDefinition>();AssetDatabase.CreateAsset(card,path);}
                card.cardId=name.ToLowerInvariant();card.fruit=fruit;card.fruitCount=count;card.copiesInDeck=ClassicFruitDeck.CopiesFor(count);card.faceTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Art/"+name+".png");card.faceMaterial=Material(name,card.faceTexture,Color.white);EditorUtility.SetDirty(card);definitions.Add(card);
            }
            deck.definitions=definitions.ToArray();deck.backTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Art/CardBack.png");deck.backMaterial=Material("CardBack",deck.backTexture,Color.white);deck.edgeMaterial=Material("CardEdge",null,new Color(.95f,.94f,.90f));
            var root=new GameObject("Fruit Card");var visual=new GameObject("Card Mesh",typeof(MeshFilter),typeof(MeshRenderer));visual.transform.SetParent(root.transform,false);visual.GetComponent<MeshFilter>().sharedMesh=BuildMesh();
            var view=root.AddComponent<FruitCardView>();view.deck=deck;view.definition=definitions[0];view.visual=visual.transform;view.cardRenderer=visual.GetComponent<MeshRenderer>();view.Apply();
            deck.cardPrefab=PrefabUtility.SaveAsPrefabAsset(root,Root+"/Prefabs/FruitCard.prefab");UnityEngine.Object.DestroyImmediate(root);EditorUtility.SetDirty(deck);AssetDatabase.SaveAssets();deck.Validate();
        }
        static Material Material(string name,Texture2D texture,Color color)
        {
            string path=Root+"/Materials/"+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(!mat){mat=new Material(Shader.Find("Standard"));AssetDatabase.CreateAsset(mat,path);}mat.color=color;mat.mainTexture=texture;mat.SetFloat("_Glossiness",.18f);mat.SetFloat("_Metallic",0);EditorUtility.SetDirty(mat);return mat;
        }
        static Mesh BuildMesh()
        {
            const float w=.48f,h=.48f*87/56,r=.024f,half=.0012f;var border=new List<Vector2>();
            for(int corner=0;corner<4;corner++){
                float cx=(corner==0||corner==3?1:-1)*(w/2-r),cz=(corner<2?1:-1)*(h/2-r);
                for(int step=0;step<=8;step++){float angle=(corner*90+step*90/8f)*Mathf.Deg2Rad;border.Add(new Vector2(cx+Mathf.Cos(angle)*r,cz+Mathf.Sin(angle)*r));}
            }
            int n=border.Count;var vertices=new List<Vector3>();var uv=new List<Vector2>();var top=new List<int>();var bottom=new List<int>();var sides=new List<int>();
            vertices.Add(new Vector3(0,half,0));uv.Add(Vector2.one*.5f);foreach(var p in border){vertices.Add(new Vector3(p.x,half,p.y));uv.Add(new Vector2(p.x/w+.5f,p.y/h+.5f));}
            int b=vertices.Count;vertices.Add(new Vector3(0,-half,0));uv.Add(Vector2.one*.5f);foreach(var p in border){vertices.Add(new Vector3(p.x,-half,p.y));uv.Add(new Vector2(p.x/w+.5f,.5f-p.y/h));}
            for(int i=0;i<n;i++){
                int next=(i+1)%n;top.AddRange(new[]{0,next+1,i+1});bottom.AddRange(new[]{b,b+i+1,b+next+1});
                int s=vertices.Count;Vector2 p=border[i],q=border[next];vertices.AddRange(new[]{new Vector3(p.x,half,p.y),new Vector3(q.x,half,q.y),new Vector3(p.x,-half,p.y),new Vector3(q.x,-half,q.y)});uv.AddRange(new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one});sides.AddRange(new[]{s,s+1,s+2,s+1,s+3,s+2});
            }
            var mesh=new Mesh{name="Rounded card 56x87 ratio"};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.subMeshCount=3;mesh.SetTriangles(top,0);mesh.SetTriangles(bottom,1);mesh.SetTriangles(sides,2);mesh.RecalculateNormals();mesh.RecalculateBounds();
            string path=Root+"/Meshes/RoundedCard.asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing){EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);mesh=existing;}else AssetDatabase.CreateAsset(mesh,path);EditorUtility.SetDirty(mesh);return mesh;
        }
        static FruitCardView Spawn(ClassicFruitDeck deck,FruitCardDefinition card,Vector3 position,Transform parent,bool faceDown)
        {
            var go=(GameObject)PrefabUtility.InstantiatePrefab(deck.cardPrefab);go.name=(faceDown?"Face down":"Open")+" card";go.transform.SetParent(parent);go.transform.localPosition=position;go.transform.localRotation=Quaternion.identity;var view=go.GetComponent<FruitCardView>();view.SetCard(card);view.SetFaceDown(faceDown);PrefabUtility.RecordPrefabInstancePropertyModifications(view);PrefabUtility.RecordPrefabInstancePropertyModifications(view.visual);PrefabUtility.RecordPrefabInstancePropertyModifications(view.cardRenderer);return view;
        }
        public static void InstallTableInCurrentScene()
        {
            if(UnityEngine.Object.FindFirstObjectByType<HalliGalli.Movement.CourseCardGame>()){
                MatchIntegrationBuilder.InstallInCurrentScene();return;
            }
            var deck=AssetDatabase.LoadAssetAtPath<ClassicFruitDeck>(DeckPath);if(!deck)return;
            foreach(var old in UnityEngine.Object.FindObjectsByType<CardTablePreview>(FindObjectsInactive.Include))UnityEngine.Object.DestroyImmediate(old.gameObject);
            // Replace only the exact temporary props created by the original course builder.
            foreach(var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))if(t.name=="Open card - practice only"||t.name=="Closed card deck"||t.name=="Fruit")UnityEngine.Object.DestroyImmediate(t.gameObject);
            var root=new GameObject("Classic 56-card table preview");var preview=root.AddComponent<CardTablePreview>();preview.deck=deck;
            var remaining=deck.CreateOrdered();var a=remaining.First(c=>c.Definition.fruit==FruitKind.Strawberry&&c.Definition.fruitCount==3);remaining.Remove(a);var b=remaining.First(c=>c.Definition.fruit==FruitKind.Strawberry&&c.Definition.fruitCount==2);remaining.Remove(b);
            preview.openCards=new[]{Spawn(deck,a.Definition,new Vector3(-.48f,.891f,-4.5f),root.transform,false),Spawn(deck,b.Definition,new Vector3(.48f,.891f,-4.5f),root.transform,false)};
            for(int i=0;i<remaining.Count;i++){int seat=i%2,layer=i/2;Spawn(deck,remaining[i].Definition,new Vector3(seat==0?-1.28f:1.28f,.890f+layer*.0025f,-4.85f),root.transform,true);}
            preview.faceDownCounts=new[]{27,27};EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
        public static void CaptureTable()
        {
            Directory.CreateDirectory("Previews");var driver=GameObject.Find("Player").GetComponent<HalliGalli.Movement.CharacterMotionDriver>();driver.worldCamera.transform.rotation=Quaternion.Euler(24,0,0);CharacterAssetBuilder.Render(driver.worldCamera,driver.handCamera,"Previews/Cards_Table_FirstPerson.png");
            var camera=new GameObject("Card table review").AddComponent<Camera>();camera.cullingMask=~(1<<HalliGalli.Movement.CharacterMotionDriver.HandsLayer);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.06f,.10f,.14f);camera.transform.position=new Vector3(0,3.5f,-6.4f);camera.transform.LookAt(new Vector3(0,.89f,-4.65f));camera.fieldOfView=62;CharacterAssetBuilder.Render(camera,null,"Previews/Cards_Table.png");UnityEngine.Object.DestroyImmediate(camera.gameObject);
        }
        public static void CreateGallery()
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);scene.name="CardGallery";RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.85f,.85f,.85f);RenderSettings.fog=false;
            var light=new GameObject("Soft key").AddComponent<Light>();light.type=LightType.Directional;light.intensity=.3f;light.transform.eulerAngles=new Vector3(70,0,0);
            var deck=AssetDatabase.LoadAssetAtPath<ClassicFruitDeck>(DeckPath);var root=new GameObject("20 card faces").transform;
            foreach(var definition in deck.definitions)Spawn(deck,definition,new Vector3((definition.fruitCount-3)*.65f,0,1.5f-(int)definition.fruit),root,false);
            var camera=new GameObject("Gallery camera",typeof(Camera),typeof(AudioListener)).GetComponent<Camera>();camera.transform.position=Vector3.up*6;camera.transform.LookAt(Vector3.zero,Vector3.forward);camera.orthographic=true;camera.orthographicSize=2.15f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.055f,.10f,.15f);
            EditorSceneManager.SaveScene(scene,GalleryPath);CharacterAssetBuilder.Render(camera,null,"Previews/Cards_Unity_Gallery.png");
        }
        [Serializable]sealed class Check{public string name;public bool passed;public string detail;}
        [Serializable]sealed class Report{public int passed,failed;public List<Check> checks;}
        static void Validate()
        {
            var checks=new List<Check>();var deck=AssetDatabase.LoadAssetAtPath<ClassicFruitDeck>(DeckPath);
            void Test(string name,Action action){try{action();checks.Add(new Check{name=name,passed=true,detail="passed"});}catch(Exception e){checks.Add(new Check{name=name,passed=false,detail=e.Message});}}
            void Require(bool value,string detail){if(!value)throw new Exception(detail);}
            Test("Exactly 20 face definitions and 56 physical cards",()=>{deck.Validate();Require(deck.definitions.Length==20&&deck.CreateOrdered().Count==56,"deck size");});
            foreach(FruitKind fruit in Enum.GetValues(typeof(FruitKind)))Test(fruit+" has 14 cards in 5,3,3,2,1 distribution",()=>{var cards=deck.CreateOrdered().Where(c=>c.Definition.fruit==fruit).ToArray();Require(cards.Length==14,"fruit total");int[] expected={5,3,3,2,1};for(int n=1;n<=5;n++)Require(cards.Count(c=>c.Definition.fruitCount==n)==expected[n-1],"count="+n);});
            Test("All 56 card instances have unique identities",()=>Require(deck.CreateOrdered().Select(c=>c.Serial).Distinct().Count()==56,"duplicate instance"));
            Test("Shuffle is reproducible and preserves all identities",()=>{var a=deck.CreateShuffled(503);var b=deck.CreateShuffled(503);Require(a.Select(c=>c.Serial).SequenceEqual(b.Select(c=>c.Serial)),"not reproducible");Require(a.Select(c=>c.Serial).OrderBy(i=>i).SequenceEqual(Enumerable.Range(0,56)),"missing or repeated card");Require(!a.Select(c=>c.Serial).SequenceEqual(Enumerable.Range(0,56)),"not shuffled");});
            int[][] distributions={new[]{28,28},new[]{19,19,18},new[]{14,14,14,14},new[]{12,11,11,11,11},new[]{10,10,9,9,9,9}};
            for(int players=2;players<=6;players++){int n=players;Test("Round-robin deal for "+n+" players uses all 56",()=>{var piles=deck.Deal(n,81);Require(piles.Select(p=>p.Count).SequenceEqual(distributions[n-2]),"wrong distribution");Require(piles.SelectMany(p=>p).Select(c=>c.Serial).Distinct().Count()==56,"card lost or duplicated");});}
            Test("Rotating first recipient rotates the remainder",()=>Require(deck.Deal(3,81,2).Select(p=>p.Count).SequenceEqual(new[]{19,18,19}),"remainder bias"));
            Test("Invalid player counts are rejected",()=>{foreach(int n in new[]{0,1,7}){bool rejected=false;try{deck.Deal(n,0);}catch(ArgumentOutOfRangeException){rejected=true;}Require(rejected,"accepted "+n);}});
            Test("Every face contains exactly the intended number of icons",()=>{foreach(var card in deck.definitions){string svg=File.ReadAllText(Root+"/Art/VectorSource/"+card.fruit+"_"+card.fruitCount+".svg");Require(System.Text.RegularExpressions.Regex.Matches(svg,"class=\"fruit-instance\"").Count==card.fruitCount,card.cardId);Require(!svg.Contains("<text"),"face contains a number hint");}});
            Test("All face textures use 56:87 proportions and full resolution",()=>{foreach(var card in deck.definitions)Require(card.faceTexture.width==1120&&card.faceTexture.height==1740,card.cardId);});
            Test("Card mesh has distinct front, common back, and edges",()=>{var view=deck.cardPrefab.GetComponent<FruitCardView>();var mesh=view.visual.GetComponent<MeshFilter>().sharedMesh;Require(mesh.subMeshCount==3&&mesh.normals[0].y>.99f,"top normal or submeshes");Require(Mathf.Abs(mesh.bounds.size.x/mesh.bounds.size.z-56f/87)<.001f,"card ratio");Require(view.cardRenderer.sharedMaterials[1]==deck.backMaterial,"back material mismatch");});
            Test("Course table contains the complete deck and correct initial faces",()=>{var live=UnityEngine.Object.FindFirstObjectByType<CardTableDisplay>();var preview=UnityEngine.Object.FindFirstObjectByType<CardTablePreview>();var views=live?live.GetComponentsInChildren<FruitCardView>():preview.GetComponentsInChildren<FruitCardView>();Require(views.Length==56&&views.Count(v=>!v.FaceDown)==(live?0:2),"table count");foreach(var card in deck.definitions)Require(views.Count(v=>v.definition==card)==card.copiesInDeck,card.cardId);foreach(var v in views)Require(v.cardRenderer.sharedMaterials[1]==deck.backMaterial,"back leaks identity");});
            Test("Face-down view points the front away from the viewer",()=>{var go=UnityEngine.Object.Instantiate(deck.cardPrefab);try{var view=go.GetComponent<FruitCardView>();view.SetFaceDown(true);Require(Vector3.Dot(view.visual.up,go.transform.up)<-.99f,"front still faces up");view.SetFaceDown(false);Require(Vector3.Dot(view.visual.up,go.transform.up)>.99f,"front not restored");}finally{UnityEngine.Object.DestroyImmediate(go);}});
            int passed=checks.Count(c=>c.passed);Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/cards-validation.json",JsonUtility.ToJson(new Report{passed=passed,failed=checks.Count-passed,checks=checks},true));Debug.Log("CARDS_VALIDATION_RESULT "+passed+"/"+checks.Count);if(passed!=checks.Count)throw new Exception("Card validation failed; see TestResults/cards-validation.json");
        }
    }
}
