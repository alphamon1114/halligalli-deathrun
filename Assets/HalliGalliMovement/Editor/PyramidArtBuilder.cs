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
            var space=new RouteSpace(pyramid,surf);
            var bounds=space.RidingBounds;
            // A single low plane cannot cut through a lower return beneath an upper room.
            var pit=Box(architecture,"Pyramid shared cursed pit",new Vector3(bounds.center.x,bounds.min.y-12.25f,bounds.center.z),
                new Vector3(bounds.size.x+200,.5f,bounds.size.z+200),ink,true);
            pit.AddComponent<SurfDivider>().session=session;
            for(int i=0;i<pyramid.chambers.Length;i++){
                var room=pyramid.chambers[i];var art=new GameObject(room.name).transform;art.SetParent(decoration);
                switch(i){
                    case 0: Entrance(art,room,surf,space);break;
                    case 1: Exterior(art,room,surf,space);break;
                    case 2: BrokenHall(art,room,surf,space);break;
                    case 3: Watergate(art,room,surf,space);break;
                    case 4: Temple(art,room,surf,space);break;
                }
            }
            pyramid.flames=lights.ToArray();
            var sun=new GameObject("Pyramid warm bounce").AddComponent<Light>();sun.type=LightType.Directional;sun.color=new Color(1,.94f,.82f);sun.intensity=.95f;sun.shadows=LightShadows.None;sun.transform.rotation=Quaternion.Euler(38,-28,0);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.43f,.46f,.49f);RenderSettings.ambientIntensity=1;
            RenderSettings.skybox=null;RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogColor=new Color(.43f,.49f,.53f);RenderSettings.fogStartDistance=110;RenderSettings.fogEndDistance=380;
        }
        internal static SurfCourse.Frame FrameAt(SurfCourse.Section section,float t)
        {
            float sample=Mathf.Clamp01(t)*(section.points.Length-1);int i=Mathf.Min((int)sample,section.points.Length-2);
            var forward=Quaternion.Euler(0,Mathf.LerpAngle(section.headings[i],section.headings[i+1],sample-i),0)*Vector3.forward;
            return new SurfCourse.Frame{center=Vector3.Lerp(section.points[i],section.points[i+1],sample-i),
                forward=forward,right=Vector3.Cross(Vector3.up,forward)};
        }
        static SurfCourse.Frame RoomFrame(PyramidCourse.Chamber room,SurfCourse surf,float t,out float width)
        {
            float length=0;
            for(int i=room.firstSection;i<=room.lastSection;i++)length+=surf.sections[i].Length;
            float distance=length*t;
            for(int i=room.firstSection;i<=room.lastSection;i++){
                var section=surf.sections[i];float sectionLength=section.Length;
                if(distance<=sectionLength||i==room.lastSection){
                    width=surf.laneX+section.halfWidth;
                    return FrameAt(section,distance/sectionLength);
                }
                distance-=sectionLength;
            }
            width=surf.laneX+surf.sections[room.lastSection].halfWidth;
            return FrameAt(surf.sections[room.lastSection],1);
        }
        internal static bool IsAirTransfer(SurfCourse.Section from,SurfCourse.Section to)
        {
            return from.landing==Vector3.zero&&from.points[from.points.Length-1]!=to.points[0];
        }
        // World-space volumes include every section, not just the room being decorated.
        // Conservative segment bounds keep ruins clear of both banks and crossing flights.
        internal sealed class RouteSpace
        {
            readonly List<Bounds> reserved=new List<Bounds>();
            public Bounds RidingBounds{get;private set;}
            public RouteSpace(PyramidCourse course,SurfCourse surf)
            {
                var riding=new Bounds(surf.sections[0].points[0],Vector3.zero);
                float slope=Mathf.Tan(55*Mathf.Deg2Rad);
                for(int s=0;s<surf.sections.Length;s++){
                    var section=surf.sections[s];
                    var previous=BankBounds(section,0,surf.laneX,slope);
                    for(int i=1;i<section.points.Length;i++){
                        var next=BankBounds(section,i,surf.laneX,slope);var segment=previous;segment.Encapsulate(next);
                        riding.Encapsulate(segment);
                        ReservePassage(segment,6);
                        previous=next;
                    }
                    if(s+1<surf.sections.Length&&IsAirTransfer(section,surf.sections[s+1])){
                        var next=surf.sections[s+1];
                        ReserveFlight(section.points[section.points.Length-1],next.points[0],
                            surf.laneX+Mathf.Max(section.halfWidth,next.halfWidth),slope);
                    }
                }
                foreach(var room in course.chambers){
                    var first=surf.sections[room.firstSection];var last=surf.sections[room.lastSection];
                    var frame=FrameAt(last,1);
                    var deck=new Bounds(room.landing-Vector3.up*.225f,new Vector3(0,.4f,0));
                    for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)
                        deck.Encapsulate(room.landing+frame.right*x*10+frame.forward*z*10);
                    riding.Encapsulate(deck);ReservePassage(deck,8);
                    ReserveFlight(last.points[last.points.Length-1],room.landing,surf.laneX+last.halfWidth,slope);
                    ReserveFlight(first.entry,first.points[0],surf.laneX+first.halfWidth,slope);
                }
                // Include actual generated mesh thickness as well as the analytical bank profile.
                foreach(var renderer in surf.GetComponentsInChildren<MeshRenderer>())riding.Encapsulate(renderer.bounds);
                RidingBounds=riding;
            }
            static Bounds BankBounds(SurfCourse.Section section,int i,float lane,float slope)
            {
                var center=section.points[i];var right=Quaternion.Euler(0,section.headings[i],0)*Vector3.right;
                float width=lane+section.halfWidth;
                var bounds=new Bounds(center+Vector3.up*lane*slope,Vector3.zero);
                bounds.Encapsulate(center-right*width-Vector3.up*section.halfWidth*slope);
                bounds.Encapsulate(center+right*width-Vector3.up*section.halfWidth*slope);
                return bounds;
            }
            void ReservePassage(Bounds bounds,float above)
            {
                bounds.SetMinMax(bounds.min-new Vector3(2.5f,1,2.5f),bounds.max+new Vector3(2.5f,above,2.5f));
                reserved.Add(bounds);
            }
            void ReserveFlight(Vector3 from,Vector3 to,float width,float slope)
            {
                var bounds=new Bounds(from,Vector3.zero);bounds.Encapsulate(to);
                float length=Vector3.ProjectOnPlane(to-from,Vector3.up).magnitude;
                // Leave headroom for the launch apex, not merely the line joining the endpoints.
                bounds.SetMinMax(bounds.min-new Vector3(width+2.5f,width*slope+2,width+2.5f),
                    bounds.max+new Vector3(width+2.5f,Mathf.Max(12,length*.65f),width+2.5f));
                reserved.Add(bounds);
            }
            public void ReserveDecoration(Transform root)
            {
                if(!root)return;
                foreach(var renderer in root.GetComponentsInChildren<Renderer>())reserved.Add(renderer.bounds);
            }
            public void Place(Transform root,SurfCourse.Frame frame,Vector3 direction,float offset,float height)
            {
                root.SetPositionAndRotation(frame.center+direction*offset+Vector3.up*height,Quaternion.LookRotation(frame.forward,Vector3.up));
                var renderers=root.GetComponentsInChildren<Renderer>();
                var bounds=renderers[0].bounds;
                for(int i=1;i<renderers.Length;i++)bounds.Encapsulate(renderers[i].bounds);
                // Moving monotonically outward leaves each intersected volume permanently.
                // Use actual renderer bounds, including imported meshes and rotated roof fragments.
                while(true){
                    float shift=0;
                    foreach(var occupied in reserved){
                        if(!bounds.Intersects(occupied))continue;
                        float x=float.PositiveInfinity,z=float.PositiveInfinity;
                        if(direction.x>.001f)x=(occupied.max.x-bounds.min.x+.5f)/direction.x;
                        else if(direction.x<-.001f)x=(occupied.min.x-bounds.max.x-.5f)/direction.x;
                        if(direction.z>.001f)z=(occupied.max.z-bounds.min.z+.5f)/direction.z;
                        else if(direction.z<-.001f)z=(occupied.min.z-bounds.max.z-.5f)/direction.z;
                        shift=Mathf.Max(shift,Mathf.Min(x,z));
                    }
                    if(shift==0)break;
                    var delta=direction*shift;root.position+=delta;bounds.center+=delta;
                }
                reserved.Add(bounds);
            }
        }
        static Transform Landmark(Transform parent,string name)
        {
            var root=new GameObject(name).transform;root.SetParent(parent);return root;
        }
        static int Outside(SurfCourse.Section section)=>Mathf.DeltaAngle(section.headings[0],section.headings[section.headings.Length-1])>=0?-1:1;
        static void Pylon(Transform root,Vector3 foot,float height,float width=5)
        {
            Box(root,"Weathered pylon base",foot+Vector3.up*height*.21f,new Vector3(width+2,height*.42f,7),stone);
            Box(root,"Weathered pylon crown",foot+Vector3.up*height*.7f,new Vector3(width,height*.58f,6),stone);
            Box(root,"Single lapis inset",foot+Vector3.up*height*.67f-Vector3.forward*3.04f,new Vector3(width*.32f,height*.34f,.12f),lapis);
        }
        static void Entrance(Transform art,PyramidCourse.Chamber room,SurfCourse surf,RouteSpace space)
        {
            var first=surf.sections[room.firstSection];var frame=FrameAt(first,.08f);
            for(int side=-1;side<=1;side+=2){
                var gate=Landmark(art,"Open entrance gate shoulder");
                Pylon(gate,Vector3.zero,22,7);
                Torch(gate,new Vector3(-side*4,13,0));
                space.Place(gate,frame,frame.right*side,surf.laneX+first.halfWidth+10,-12);
            }
        }
        static void Exterior(Transform art,PyramidCourse.Chamber room,SurfCourse surf,RouteSpace space)
        {
            var frame=RoomFrame(room,surf,.42f,out float width);
            int side=Outside(surf.sections[room.firstSection]);
            var ruin=Landmark(art,"Outer bend stepped sanctuary");
            Box(ruin,"Stepped ruin foundation",Vector3.up*5,new Vector3(26,10,30),stone);
            Box(ruin,"Stepped ruin terrace",Vector3.up*12,new Vector3(19,4,23),stone);
            Pylon(ruin,Vector3.up*14,14,8);
            space.Place(ruin,frame,frame.right*side,width+20,-14);
        }
        static void BrokenHall(Transform art,PyramidCourse.Chamber room,SurfCourse surf,RouteSpace space)
        {
            for(int side=-1;side<=1;side+=2){
                var frame=RoomFrame(room,surf,side<0?.32f:.76f,out float width);
                var gallery=Landmark(art,"Isolated broken gallery window");
                Pylon(gallery,new Vector3(0,0,-11),24,6);
                Pylon(gallery,new Vector3(0,0,11),20,6);
                Box(gallery,"Broken gallery low sill",new Vector3(0,2,0),new Vector3(7,4,23),stone);
                var slab=Box(gallery,"Fractured side roof remnant",new Vector3(side*2,22,7),new Vector3(9,2.5f,14),stone);
                slab.transform.rotation=Quaternion.Euler(0,0,side*8);
                space.Place(gallery,frame,frame.right*side,width+11,-14);
            }
        }
        static void Watergate(Transform art,PyramidCourse.Chamber room,SurfCourse surf,RouteSpace space)
        {
            var frame=RoomFrame(room,surf,.4f,out float width);
            int side=Outside(surf.sections[room.firstSection]);
            var sluice=Landmark(art,"Open watergate aqueduct remnant");
            Pylon(sluice,new Vector3(0,0,-10),17,5);
            Pylon(sluice,new Vector3(0,0,10),17,5);
            Box(sluice,"Side aqueduct lintel",new Vector3(0,18,0),new Vector3(6,2,27),stone);
            Box(sluice,"Ancient sluice recess",new Vector3(-side*3.04f,8,-10),new Vector3(.12f,9,4),ink);
            Torch(sluice,new Vector3(-side*4,12,10));
            space.Place(sluice,frame,frame.right*side,width+12,-14);
        }
        static void Temple(Transform art,PyramidCourse.Chamber room,SurfCourse surf,RouteSpace space)
        {
            var frame=FrameAt(surf.sections[room.lastSection],1);frame.center=room.landing;
            for(int side=-1;side<=1;side+=2){
                var guardian=Landmark(art,"Royal guardian terrace");
                Box(guardian,"Temple guardian foundation",Vector3.up*6,new Vector3(10,12,11),stone);
                if(PyramidVarcoImporter.Ready)PyramidVarcoImporter.Place(guardian,"Anubis",Vector3.up*12,8,side<0?65:-65);
                else Pylon(guardian,Vector3.up*12,9,5);
                Torch(guardian,new Vector3(-side*4,14,-3));
                space.Place(guardian,frame,frame.right*side,22,-12);
            }
            var shrine=Landmark(art,"Open royal sun shrine");
            Box(shrine,"Royal shrine backing",Vector3.up*12,new Vector3(16,24,4),stone);
            Box(shrine,"Royal shrine crown",Vector3.up*26,new Vector3(10,4,4),stone);
            var disk=GameObject.CreatePrimitive(PrimitiveType.Cylinder);disk.name="Royal sun disk";disk.transform.SetParent(shrine);
            disk.transform.position=new Vector3(0,21,-2.2f);disk.transform.rotation=Quaternion.Euler(90,0,0);disk.transform.localScale=new Vector3(6,.16f,6);
            disk.GetComponent<Renderer>().sharedMaterial=gold;Object.DestroyImmediate(disk.GetComponent<Collider>());
            space.Place(shrine,frame,frame.forward,24,-12);
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
