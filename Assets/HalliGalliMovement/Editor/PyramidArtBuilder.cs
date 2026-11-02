using System.Collections.Generic;
using System.Linq;
using HalliGalli.Movement;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace HalliGalli.EditorTools
{
    public static class PyramidArtBuilder
    {
        static Material gold,lapis,ink,glow,stone;
        static readonly List<Light> lights=new List<Light>();
        public static Material Material(string name,Color color,bool emissive=false,bool sandstone=false)
        {
            string path=PyramidSurfBuilder.Art+"/"+name.Replace(" ","_")+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m){m=new Material(Shader.Find(sandstone?"HalliGalli/Sandstone":"Standard"));AssetDatabase.CreateAsset(m,path);}
            m.color=color;if(sandstone)PyramidStoneAssets.Configure(m,name.Contains("Polished"));else{m.SetFloat("_Glossiness",.24f);m.SetFloat("_Metallic",name.Contains("Gold")?.65f:0);}
            if(emissive){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",color*.9f);}
            EditorUtility.SetDirty(m);return m;
        }
        public static GameObject Box(Transform parent,string name,Vector3 position,Vector3 size,Material material,bool solid=false)
        {
            var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);obj.name=name;obj.transform.SetParent(parent);obj.transform.position=position;obj.transform.localScale=size;
            obj.GetComponent<Renderer>().sharedMaterial=material;if(!solid)Object.DestroyImmediate(obj.GetComponent<Collider>());return obj;
        }
        public static void Dress(CourseSession session,PyramidCourse pyramid,Material sandstone)
        {
            stone=sandstone;gold=Material("Aged Gold",new Color(.65f,.41f,.10f));lapis=Material("Lapis",new Color(.045f,.16f,.24f));
            ink=Material("Basalt",new Color(.065f,.075f,.08f));glow=Material("Torch Gold",new Color(1,.52f,.13f),true);lights.Clear();
            var architecture=new GameObject("Pyramid architecture").transform;
            var decoration=new GameObject("Pyramid decoration").transform;
            var surf=pyramid.GetComponent<SurfCourse>();
            // The shared bank builder also authors outlines; this course uses sparse
            // jump runes instead. Keep checkpoint markings and other courses unchanged.
            foreach(Transform child in surf.transform.Cast<Transform>().ToArray())
                if(child.name.StartsWith("Pyramid bank ")&&child.name.EndsWith(" edge"))Object.DestroyImmediate(child.gameObject);
            foreach(var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)){
                if(!t||!(t.name=="Table deck"||t.name=="Landing surface"||t.name=="Shared pad 2"||t.name=="Bell plinth"))continue;
                foreach(Transform child in t.Cast<Transform>().ToArray())if(child.name=="VARCO visual")Object.DestroyImmediate(child.gameObject);
                var render=t.GetComponent<Renderer>();if(render){render.enabled=true;render.sharedMaterial=stone;}
            }
            for(int i=0;i<pyramid.chambers.Length;i++){
                var room=pyramid.chambers[i];var shell=new GameObject(room.name).transform;shell.SetParent(architecture);
                var art=new GameObject(room.name+" decoration").transform;art.SetParent(decoration);
                var rotation=room.Rotation;var inverse=Quaternion.Inverse(rotation);
                Vector3 min=room.localMinimum,max=room.localMaximum,landing=inverse*(room.landing-room.origin);
                var section=surf.sections[room.firstSection];var last=surf.sections[room.lastSection];
                Vector3 firstPoint=inverse*(section.points[0]-room.origin),lastPoint=inverse*(last.points[last.points.Length-1]-room.origin);
                float roof=max.y,height=roof-min.y+18,width=max.x-min.x,depth=max.z-min.z;
                // The checkpoint courtyard stays open on both sides. Stop incoming
                // walls before the deck and begin the next corridor beyond its entry.
                float wallStart=i==0?min.z:Mathf.Max(min.z,1),wallEnd=i==pyramid.chambers.Length-1?max.z:lastPoint.z;
                float wallDepth=wallEnd-wallStart,wallCenter=(wallStart+wallEnd)*.5f;
                var pit=Box(shell,"Pyramid cursed pit "+i,new Vector3((min.x+max.x)*.5f,min.y,(min.z+max.z)*.5f),new Vector3(width,.5f,depth),ink,true);
                pit.AddComponent<SurfDivider>().session=session;
                Wall(shell,"West stone wall",new Vector3(min.x,min.y+height*.5f,wallCenter),new Vector3(1,height,wallDepth));
                Wall(shell,"East stone wall",new Vector3(max.x,min.y+height*.5f,wallCenter),new Vector3(1,height,wallDepth));
                if(i==0)Wall(shell,"Entrance back wall",new Vector3((min.x+max.x)*.5f,min.y+height*.5f,min.z),new Vector3(width,height,1));
                float center=(min.x+max.x)*.5f;
                foreach(int side in new[]{-1,1}){
                    var ceiling=Box(shell,"Corbelled pyramid vault",new Vector3(center+side*width*.25f,roof+width*.25f*Mathf.Tan(16*Mathf.Deg2Rad),(min.z+max.z)*.5f),new Vector3(width*.5f/Mathf.Cos(16*Mathf.Deg2Rad)+1,2,depth+1),stone,true);
                    ceiling.transform.rotation=Quaternion.Euler(0,0,-side*16);ceiling.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
                    Box(art,"Lapis cornice",new Vector3(side<0?min.x+.6f:max.x-.6f,roof-1,wallCenter),new Vector3(.35f,1.1f,wallDepth),lapis);
                    Box(art,"Gold cornice",new Vector3(side<0?min.x+.8f:max.x-.8f,roof-1.6f,wallCenter),new Vector3(.22f,.18f,wallDepth),gold);
                }
                for(float z=wallStart+10;z<wallEnd-14;z+=22)foreach(int side in new[]{-1,1}){
                    float wallX=side<0?min.x:max.x;
                    Column(art,new Vector3(wallX-side*2.1f,min.y+.5f,z),roof-min.y-.6f);
                    if(PyramidVarcoImporter.Ready)WallAsset(art,"ReliefPanel",new Vector3(wallX,roof-15,z+10),8,side<0?90:-90,wallX,side);
                    else{
                        var glyph=new GameObject("Carved ankh frieze").transform;glyph.SetParent(art);glyph.position=new Vector3(wallX-side*.57f,roof-8,z);
                        glyph.rotation=Quaternion.Euler(0,side<0?90:-90,0);Ankh(glyph,Vector3.zero,1.8f,lapis);
                    }
                }
                // Chamber transfers stay completely open. Gates are side-wall niches,
                // never meshes or collision apertures across a launch or landing.
                foreach(int side in new[]{-1,1}){
                    float wallX=side<0?min.x:max.x,yaw=side<0?90:-90;
                    Vector3 niche=new Vector3(wallX,landing.y,wallEnd-9);
                    if(PyramidVarcoImporter.Ready)WallAsset(art,"StoneGate",niche,9,yaw,wallX,side);
                    else Portal(art,new Vector3(wallX-side*.65f,niche.y,niche.z),7,8,-yaw);
                }
                if(i==pyramid.chambers.Length-1){
                    Wall(shell,"Burial chamber wall",new Vector3(center,min.y+height*.5f,max.z),new Vector3(width,height,1));
                    Sun(art,new Vector3(landing.x,landing.y+7,max.z-.6f),2.2f);
                    // The sarcophagus is a side-wall exhibit, leaving the bell deck visible.
                    Vector3 tomb=new Vector3(max.x-2.1f,landing.y-.02f,landing.z-8);
                    if(PyramidVarcoImporter.Ready)WallAsset(art,"Sarcophagus",tomb,4,-90,max.x,1);
                    else{
                        Box(art,"Sarcophagus",tomb+Vector3.up*.8f,new Vector3(2.3f,1.6f,2.2f),ink);
                        Box(art,"Sarcophagus gold lid",tomb+Vector3.up*1.66f,new Vector3(2.5f,.22f,2.4f),gold);
                    }
                }
                foreach(int side in new[]{-1,1}){
                    float wallX=side<0?min.x:max.x;
                    Vector3 statue=new Vector3(wallX-side*2.1f,landing.y-.3f,Mathf.Min(landing.z-17,wallEnd-5));
                    float support=statue.y-min.y;
                    Box(art,"Guardian stone foundation",new Vector3(statue.x,min.y+support*.5f,statue.z),new Vector3(3,support,3),stone);
                    if(PyramidVarcoImporter.Ready){
                        WallAsset(art,"Anubis",statue,8,side<0?90:-90,wallX,side);
                        WallTorch(art,new Vector3(wallX-side*.6f,firstPoint.y+2,firstPoint.z+14),side<0?90:-90,wallX,side);
                        WallTorch(art,new Vector3(wallX-side*.6f,landing.y+1,wallEnd-2),side<0?90:-90,wallX,side);
                    }else{
                        Torch(art,statue+Vector3.up*1.4f);
                        Vector3 foot=new Vector3(wallX-side*1.5f,min.y,firstPoint.z+14);
                        float h=firstPoint.y+3-min.y;Box(art,"Supported torch pillar",foot+Vector3.up*h*.5f,new Vector3(1.2f,h,1.2f),stone);
                        Torch(art,foot+Vector3.up*(h+1.4f));
                    }
                }
                // Pit decoration remains far below the riding surface and has no collision.
                for(int j=0;j<18;j++){
                    float x=Mathf.Lerp(min.x+5,max.x-5,(j%6)/5f),z=Mathf.Lerp(min.z+8,max.z-8,(j/6)/2f);
                    Spike(art,new Vector3(x,min.y+.25f,z),1.6f,2.6f);
                }
                // Bounds fitting above happens in the room's unrotated local frame.
                // Move each finished root once, including its solid pit and shell.
                shell.SetPositionAndRotation(room.origin,rotation);
                art.SetPositionAndRotation(room.origin,rotation);
            }
            pyramid.flames=lights.ToArray();
            var sun=new GameObject("Pyramid warm bounce").AddComponent<Light>();sun.type=LightType.Directional;sun.color=new Color(1,.94f,.82f);sun.intensity=.85f;sun.shadows=LightShadows.None;sun.transform.rotation=Quaternion.Euler(38,-28,0);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.36f,.38f,.40f);RenderSettings.ambientIntensity=1;
            RenderSettings.skybox=null;RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogColor=new Color(.18f,.19f,.20f);RenderSettings.fogStartDistance=75;RenderSettings.fogEndDistance=185;
        }
        static void Wall(Transform root,string name,Vector3 position,Vector3 size)
        {
            if(size.x<=0||size.y<=0||size.z<=0)throw new System.InvalidOperationException("Invalid pyramid wall "+name);
            Box(root,name,position,size,stone,true);
        }
        static void Column(Transform root,Vector3 foot,float height)
        {
            if(PyramidVarcoImporter.Ready){
                var generated=PyramidVarcoImporter.Place(root,"LotusColumn",foot,height);
                var b=VisualBounds(generated);float squeeze=Mathf.Min(1,3/Mathf.Max(b.size.x,b.size.z));
                var scale=generated.transform.localScale;generated.transform.localScale=new Vector3(scale.x*squeeze,scale.y,scale.z*squeeze);
                b=VisualBounds(generated);generated.transform.position+=new Vector3(foot.x-b.center.x,0,foot.z-b.center.z);return;
            }
            string path=PyramidSurfBuilder.Art+"/Meshes/LotusColumn.asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(!mesh){
                var vertices=new List<Vector3>();var triangles=new List<int>();
                float[] ys={0,.035f,.065f,.095f,.77f,.84f,.92f,.96f,1},rs={1.35f,1.35f,1.12f,.86f,.78f,1.02f,1.35f,1.42f,1.42f};
                for(int r=0;r<ys.Length;r++)for(int i=0;i<=48;i++){
                    float a=i*Mathf.PI*2/48,flute=r>=3&&r<=5?1+.035f*Mathf.Cos(a*16):1;
                    vertices.Add(new Vector3(Mathf.Cos(a)*rs[r]*flute,ys[r],Mathf.Sin(a)*rs[r]*flute));
                    if(r>0&&i<48){int n=r*49+i;triangles.AddRange(new[]{n-49,n,n+1,n-49,n+1,n-48});}
                }
                mesh=new Mesh{name="Fluted lotus column"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);
            }
            var obj=new GameObject("Lotus column",typeof(MeshFilter),typeof(MeshRenderer));obj.transform.SetParent(root);obj.transform.position=foot;obj.transform.localScale=new Vector3(1,height,1);
            obj.GetComponent<MeshFilter>().sharedMesh=mesh;obj.GetComponent<Renderer>().sharedMaterial=stone;
            foreach(float t in new[]{.105f,.76f,.90f,.965f}){
                var band=GameObject.CreatePrimitive(PrimitiveType.Cylinder);band.name="Painted column collar";band.transform.SetParent(root);band.transform.position=foot+Vector3.up*height*t;
                float radius=t>.85f?2.76f:1.78f;band.transform.localScale=new Vector3(radius,.14f,radius);band.GetComponent<Renderer>().sharedMaterial=t>.9f?gold:lapis;Object.DestroyImmediate(band.GetComponent<Collider>());
            }
        }
        static Bounds VisualBounds(GameObject obj)
        {
            var renderers=obj.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;
            foreach(var renderer in renderers.Skip(1))bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
        static GameObject WallAsset(Transform root,string key,Vector3 foot,float height,float yaw,float wallX,int side)
        {
            var obj=PyramidVarcoImporter.Place(root,key,foot,height,yaw);
            // Fit the complete rendered asset into the wall strip, not just its pivot
            // or first mesh. Even unusually reconstructed gates cannot hide a landing.
            var bounds=VisualBounds(obj);
            obj.transform.localScale*=Mathf.Min(1,3/Mathf.Max(.001f,bounds.size.x));
            bounds=VisualBounds(obj);
            float x=side<0?wallX+.6f-bounds.min.x:wallX-.6f-bounds.max.x;
            obj.transform.position+=new Vector3(x,foot.y-bounds.min.y,foot.z-bounds.center.z);
            return obj;
        }
        static void Portal(Transform root,Vector3 foot,float width,float height,float yaw)
        {
            var niche=new GameObject("Side-wall portal niche").transform;niche.SetParent(root);
            foreach(int side in new[]{-1,1}){
                Box(niche,"Portal carved jamb",new Vector3(side*(width*.5f+.5f),height*.5f,0),new Vector3(1.5f,height+1,1),stone);
                Box(niche,"Portal gold seam",new Vector3(side*(width*.5f+.45f),height*.5f,-.55f),new Vector3(.18f,height,.1f),gold);
                var glyph=new GameObject("Ankh portal relief").transform;glyph.SetParent(niche);glyph.position=new Vector3(side*(width*.5f+.5f),height*.55f,-.6f);Ankh(glyph,Vector3.zero,1.25f,gold);
            }
            Box(niche,"Portal lintel",new Vector3(0,height+.35f,0),new Vector3(width+3,1.2f,1),stone);
            Box(niche,"Portal lapis lintel",new Vector3(0,height+.35f,-.55f),new Vector3(width+2.7f,.48f,.1f),lapis);
            Sun(niche,new Vector3(0,height+.35f,-.65f),.55f);
            niche.position=foot;niche.rotation=Quaternion.Euler(0,yaw,0);
        }
        static void Torch(Transform root,Vector3 position)
        {
            Box(root,"Torch pedestal",position-Vector3.up*.7f,new Vector3(.55f,1.5f,.55f),ink);
            var bowl=GameObject.CreatePrimitive(PrimitiveType.Sphere);bowl.name="Gold brazier";bowl.transform.SetParent(root);bowl.transform.position=position;bowl.transform.localScale=new Vector3(1,.35f,1);bowl.GetComponent<Renderer>().sharedMaterial=gold;Object.DestroyImmediate(bowl.GetComponent<Collider>());
            var flame=GameObject.CreatePrimitive(PrimitiveType.Sphere);flame.name="Brazier ember";flame.transform.SetParent(root);flame.transform.position=position+Vector3.up*.35f;flame.transform.localScale=new Vector3(.36f,.75f,.36f);flame.GetComponent<Renderer>().sharedMaterial=glow;Object.DestroyImmediate(flame.GetComponent<Collider>());
            var light=flame.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(1,.58f,.20f);light.intensity=2.5f;light.range=21;light.shadows=LightShadows.None;lights.Add(light);
        }
        static void WallTorch(Transform root,Vector3 foot,float yaw,float wallX,int side)
        {
            var obj=WallAsset(root,"Brazier",foot,2.5f,yaw,wallX,side);
            var forward=Quaternion.Euler(0,yaw,0)*Vector3.forward;
            var ember=GameObject.CreatePrimitive(PrimitiveType.Sphere);ember.name="VARCO brazier ember";ember.transform.SetParent(root);
            var bounds=VisualBounds(obj);ember.transform.position=new Vector3(bounds.center.x,bounds.min.y+bounds.size.y*.66f,bounds.center.z)+forward*.3f;ember.transform.localScale=new Vector3(.38f,.15f,.38f);
            ember.GetComponent<Renderer>().sharedMaterial=glow;Object.DestroyImmediate(ember.GetComponent<Collider>());
            var light=ember.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(1,.68f,.33f);light.intensity=2.5f;light.range=21;light.shadows=LightShadows.None;lights.Add(light);
        }
        static void Spike(Transform root,Vector3 foot,float radius,float height,Material material=null)
        {
            string path=PyramidSurfBuilder.Art+"/Meshes/PyramidPoint.asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(!mesh){
                mesh=new Mesh{name="Four sided pyramid point"};var v=new List<Vector3>();var t=new List<int>();
                for(int i=0;i<4;i++){float a=i*Mathf.PI*.5f+Mathf.PI*.25f,b=a+Mathf.PI*.5f;int n=v.Count;v.AddRange(new[]{new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)),Vector3.up,new Vector3(Mathf.Cos(b),0,Mathf.Sin(b))});t.AddRange(new[]{n,n+1,n+2});}
                mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);
            }
            var obj=new GameObject("Pyramid point",typeof(MeshFilter),typeof(MeshRenderer));obj.transform.SetParent(root);obj.transform.position=foot;obj.transform.localScale=new Vector3(radius,height,radius);obj.GetComponent<MeshFilter>().sharedMesh=mesh;obj.GetComponent<Renderer>().sharedMaterial=material?material:gold;
        }
        static void Stroke(Transform parent,Vector3[] points,float width,Material material,bool loop=false)
        {
            var line=new GameObject("Inlaid symbol stroke").AddComponent<LineRenderer>();line.transform.SetParent(parent,false);line.useWorldSpace=false;line.loop=loop;line.positionCount=points.Length;line.SetPositions(points);line.widthMultiplier=width;line.numCapVertices=2;line.sharedMaterial=material;line.shadowCastingMode=ShadowCastingMode.Off;
        }
        static void Ankh(Transform root,Vector3 center,float scale,Material material)
        {
            var circle=Enumerable.Range(0,32).Select(i=>center+new Vector3(Mathf.Cos(i*Mathf.PI/16)*.36f,Mathf.Sin(i*Mathf.PI/16)*.48f+.48f,0)*scale).ToArray();
            Stroke(root,circle,.10f*scale,material,true);Stroke(root,new[]{center,center+Vector3.down*1.1f*scale},.13f*scale,material);Stroke(root,new[]{center+Vector3.left*.58f*scale,center+Vector3.right*.58f*scale},.13f*scale,material);
        }
        static void Sun(Transform root,Vector3 center,float radius)
        {
            var glyph=new GameObject("Winged sun disk").transform;glyph.SetParent(root);glyph.position=center;
            Stroke(glyph,Enumerable.Range(0,48).Select(i=>new Vector3(Mathf.Cos(i*Mathf.PI/24),Mathf.Sin(i*Mathf.PI/24),0)*radius).ToArray(),radius*.09f,gold,true);
            foreach(int side in new[]{-1,1})for(int i=0;i<6;i++)Stroke(glyph,new[]{new Vector3(side*radius*.85f,.2f*radius,0),new Vector3(side*radius*(1.8f+i*.23f),radius*(.4f-i*.15f),0)},radius*.08f,gold);
        }
    }
}
