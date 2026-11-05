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
            ink=Material("Basalt",new Color(.12f,.14f,.15f));glow=Material("Torch Gold",new Color(1,.52f,.13f),true);lights.Clear();
            var architecture=new GameObject("Pyramid architecture").transform;
            var decoration=new GameObject("Pyramid decoration").transform;
            var surf=pyramid.GetComponent<SurfCourse>();
            foreach(Transform child in surf.transform.Cast<Transform>().ToArray())
                if(child.name.StartsWith("Pyramid bank ")&&child.name.EndsWith(" edge"))Object.DestroyImmediate(child.gameObject);
            foreach(var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)){
                if(!t||!(t.name=="Table deck"||t.name=="Landing surface"||t.name=="Shared pad 2"||t.name=="Bell plinth"))continue;
                var render=t.GetComponent<Renderer>();if(render){render.enabled=true;render.sharedMaterial=stone;}
            }
            for(int i=0;i<pyramid.chambers.Length;i++){
                var room=pyramid.chambers[i];var shell=new GameObject(room.name).transform;shell.SetParent(architecture);
                var art=new GameObject(room.name+" decoration").transform;art.SetParent(decoration);
                var first=surf.sections[room.firstSection];
                Vector3 min=room.localMinimum,max=room.localMaximum;
                var pit=Box(shell,"Pyramid cursed pit "+i,new Vector3((min.x+max.x)*.5f,min.y,(min.z+max.z)*.5f),
                    new Vector3(max.x-min.x,.5f,max.z-min.z),ink,true);
                pit.AddComponent<SurfDivider>().session=session;
                // Only the remote kill floor has a collider. The full swept route and its
                // transfers stay open; grounded ruins stand beyond the chamber's side bounds.
                switch(i){
                    case 0: Entrance(shell,art,room,first);break;
                    case 1: Exterior(shell,room,first);break;
                    case 2: BrokenHall(shell,art,room);break;
                    case 3: Watergate(shell,art,room,first);break;
                    case 4: Temple(shell,art,room,first);break;
                }
                TransferLandmarks(shell,room,surf);
                shell.SetPositionAndRotation(room.origin,room.Rotation);
                art.SetPositionAndRotation(room.origin,room.Rotation);
            }
            pyramid.flames=lights.ToArray();
            var sun=new GameObject("Pyramid warm bounce").AddComponent<Light>();sun.type=LightType.Directional;sun.color=new Color(1,.94f,.82f);sun.intensity=.95f;sun.shadows=LightShadows.None;sun.transform.rotation=Quaternion.Euler(38,-28,0);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.43f,.46f,.49f);RenderSettings.ambientIntensity=1;
            RenderSettings.skybox=null;RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogColor=new Color(.43f,.49f,.53f);RenderSettings.fogStartDistance=110;RenderSettings.fogEndDistance=380;
        }
        static Vector3 At(PyramidCourse.Chamber room,SurfCourse.Section section,float t)
        {
            float sample=Mathf.Clamp01(t)*(section.points.Length-1);int i=Mathf.Min((int)sample,section.points.Length-2);
            return room.ToLocal(Vector3.Lerp(section.points[i],section.points[i+1],sample-i));
        }
        static void Span(Transform root,string name,Vector3 a,Vector3 b,float width,float height,Material material)
        {
            var delta=b-a;var obj=Box(root,name,(a+b)*.5f,new Vector3(width,height,delta.magnitude),material);
            obj.transform.rotation=Quaternion.LookRotation(delta,Vector3.up);
        }
        static void SideWall(Transform root,string name,PyramidCourse.Chamber room,int side,float from,float to,float top,float width=4)
        {
            float floor=room.localMinimum.y+.25f;
            float x=side<0?room.localMinimum.x-5:room.localMaximum.x+5;
            Box(root,name,new Vector3(x,(floor+top)*.5f,(from+to)*.5f),new Vector3(width,top-floor,Mathf.Abs(to-from)),stone);
        }
        static void SidePier(Transform root,PyramidCourse.Chamber room,int side,float z,float top,float width=5)
        {
            float x=side<0?room.localMinimum.x-6:room.localMaximum.x+6;
            var foot=new Vector3(x,room.localMinimum.y+.25f,z);
            Pylon(root,foot,top-foot.y,width);
        }
        internal static bool IsAirTransfer(SurfCourse.Section from,SurfCourse.Section to)
        {
            return from.landing==Vector3.zero&&from.points[from.points.Length-1]!=to.points[0];
        }
        static void TransferLandmarks(Transform root,PyramidCourse.Chamber room,SurfCourse surf)
        {
            for(int i=room.firstSection;i<room.lastSection;i++){
                var from=surf.sections[i];var to=surf.sections[i+1];
                if(!IsAirTransfer(from,to))continue;
                var departure=room.ToLocal(from.points[from.points.Length-1]);
                var destination=room.ToLocal(to.points[0]);
                // One grounded, unlit pier marks the departure's level. The catch carries
                // the turquoise beacons; nothing bridges the open space between the two.
                int side=destination.x>=departure.x?-1:1;
                SidePier(root,room,side,departure.z,departure.y+2,3);
            }
        }
        static void Pylon(Transform root,Vector3 foot,float height,float width=5)
        {
            Box(root,"Weathered pylon base",foot+Vector3.up*height*.21f,new Vector3(width+2,height*.42f,7),stone);
            Box(root,"Weathered pylon crown",foot+Vector3.up*height*.7f,new Vector3(width,height*.58f,6),stone);
            Box(root,"Single lapis inset",foot+Vector3.up*height*.67f-Vector3.forward*3.04f,new Vector3(width*.32f,height*.34f,.12f),lapis);
        }
        static void Entrance(Transform shell,Transform art,PyramidCourse.Chamber room,SurfCourse.Section first)
        {
            float end=At(room,first,.38f).z;
            foreach(int side in new[]{-1,1}){
                SidePier(shell,room,side,1,17,7);
                SideWall(shell,"Entrance retaining wall",room,side,1,end,14);
                float x=side<0?room.localMinimum.x-2:room.localMaximum.x+2;
                Torch(art,new Vector3(x,2,4));
            }
            // Broken gate shoulders frame the entrance without a lintel or roof over the route.
        }
        static void Exterior(Transform shell,PyramidCourse.Chamber room,SurfCourse.Section first)
        {
            // A sparse outside ruin leaves the opposite skyline and every airborne catch open.
            float near=At(room,first,.1f).z,far=At(room,first,.8f).z;
            SideWall(shell,"Exterior retaining ruin",room,-1,near,far,7);
            var horizon=new Vector3(room.localMinimum.x-27,room.localMinimum.y+.25f,(near+far)*.5f);
            Box(shell,"Distant stepped ruin foundation",horizon+Vector3.up*9,new Vector3(28,18,35),stone);
            Box(shell,"Distant stepped ruin crown",horizon+Vector3.up*23,new Vector3(18,10,23),stone);
            SidePier(shell,room,-1,far+10,13,6);
        }
        static void BrokenHall(Transform shell,Transform art,PyramidCourse.Chamber room)
        {
            var min=room.localMinimum;var max=room.localMaximum;
            float z=Mathf.Lerp(min.z,max.z,.34f),top=max.y-5;
            foreach(int side in new[]{-1,1}){
                float x=side<0?min.x-6:max.x+6;
                SideWall(shell,"Broken great hall side mass",room,side,z-13,z+13,top,6);
                // Roof fragments project outward, never toward the launch/catch sight line.
                var slab=Box(shell,"Fractured side roof remnant",new Vector3(x+side*3,top+1.3f,z),new Vector3(9,2.5f,23),stone);
                slab.transform.rotation=Quaternion.Euler(0,0,side*8);
                var rubble=Box(art,"Fallen hall monolith",new Vector3(x+side*3,min.y+3.75f,z+27),new Vector3(7,7,19),stone);
                rubble.transform.rotation=Quaternion.Euler(0,side*12,0);
            }
        }
        static void Watergate(Transform shell,Transform art,PyramidCourse.Chamber room,SurfCourse.Section first)
        {
            float near=At(room,first,.1f).z,far=At(room,first,.65f).z;
            foreach(int side in new[]{-1,1}){
                float x=side<0?room.localMinimum.x-5:room.localMaximum.x+5;
                SideWall(shell,"Watergate sluice abutment",room,side,near,far,10,5);
                // Surviving aqueducts run along the banks, not across the flight corridor.
                Span(shell,"Watergate side aqueduct",new Vector3(x,11,near),new Vector3(x,11,far),5,2,stone);
                Box(shell,"Ancient sluice side recess",new Vector3(x-side*2.55f,2,(near+far)*.5f),new Vector3(.12f,10,4),ink);
                Torch(art,new Vector3(x-side*2.7f,3,far-2));
            }
        }
        static void Temple(Transform shell,Transform art,PyramidCourse.Chamber room,SurfCourse.Section first)
        {
            float near=At(room,first,.08f).z,far=At(room,first,.8f).z;
            foreach(int side in new[]{-1,1})SideWall(shell,"Processional terrace wall",room,side,near,far,3);
            Vector3 landing=room.ToLocal(room.landing);
            float rear=room.localMaximum.z+7;
            foreach(int side in new[]{-1,1}){
                SidePier(shell,room,side,landing.z+6,landing.y+18,7);
                float x=side<0?room.localMinimum.x-2:room.localMaximum.x+2;
                Torch(art,new Vector3(x,landing.y+3,landing.z+3));
                if(PyramidVarcoImporter.Ready){
                    float statueX=side<0?room.localMinimum.x-6:room.localMaximum.x+6;
                    float baseY=room.localMinimum.y+.25f,statueY=landing.y-.3f;
                    Box(shell,"Temple guardian foundation",new Vector3(statueX,(baseY+statueY)*.5f,landing.z-5),new Vector3(8,statueY-baseY,8),stone);
                    PyramidVarcoImporter.Place(art,"Anubis",new Vector3(statueX,statueY,landing.z-5),8,side<0?65:-65);
                }
            }
            // The sanctuary terminates beyond the full landing bounds; no roof crosses its approach.
            float floor=room.localMinimum.y+.25f,top=landing.y+20;
            float center=(room.localMinimum.x+room.localMaximum.x)*.5f;
            float width=room.localMaximum.x-room.localMinimum.x+12;
            Box(shell,"Royal temple rear wall",new Vector3(center,(floor+top)*.5f,rear),new Vector3(width,top-floor,3),stone);
            Box(shell,"Royal temple crown",new Vector3(center,top+2,rear),new Vector3(width*.6f,4,3),stone);
            // A single large sun disk replaces the repeated small friezes and gold outlines.
            var disk=GameObject.CreatePrimitive(PrimitiveType.Cylinder);disk.name="Royal sun disk";disk.transform.SetParent(art);
            disk.transform.position=new Vector3(landing.x,landing.y+10,rear-1.65f);disk.transform.rotation=Quaternion.Euler(90,0,0);disk.transform.localScale=new Vector3(6,.16f,6);
            disk.GetComponent<Renderer>().sharedMaterial=gold;Object.DestroyImmediate(disk.GetComponent<Collider>());
        }
        static void Torch(Transform root,Vector3 position)
        {
            Box(root,"Brazier stone pedestal",position-Vector3.up*1.3f,new Vector3(1,2.6f,1),ink);
            var bowl=GameObject.CreatePrimitive(PrimitiveType.Sphere);bowl.name="Gold brazier";bowl.transform.SetParent(root);bowl.transform.position=position;bowl.transform.localScale=new Vector3(1.3f,.4f,1.3f);bowl.GetComponent<Renderer>().sharedMaterial=gold;Object.DestroyImmediate(bowl.GetComponent<Collider>());
            var flame=GameObject.CreatePrimitive(PrimitiveType.Sphere);flame.name="Brazier ember";flame.transform.SetParent(root);flame.transform.position=position+Vector3.up*.35f;flame.transform.localScale=new Vector3(.46f,.9f,.46f);flame.GetComponent<Renderer>().sharedMaterial=glow;Object.DestroyImmediate(flame.GetComponent<Collider>());
            var light=flame.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(1,.58f,.20f);light.intensity=2.5f;light.range=24;light.shadows=LightShadows.None;lights.Add(light);
        }
    }
}
