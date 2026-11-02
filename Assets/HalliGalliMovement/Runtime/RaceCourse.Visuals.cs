using UnityEngine;
using UnityEngine.Rendering;

namespace HalliGalli.Movement
{
    public sealed partial class RaceCourse
    {
        struct BodyVisual
        {
            public CourseParticipant actor;
            public Transform model;
            public Vector3 position;
            public Quaternion rotation;
            public bool applied;
        }
        readonly BodyVisual[] bodies=new BodyVisual[MaxActors];
        readonly Transform[] entityVisuals=new Transform[MaxEntities];
        readonly int[] visualIds=new int[MaxEntities];
        Transform visualsRoot;
        Material metal,red,yellow,peelInner,flame,barricadeOrange,barricadeBlack;
        Mesh coneMesh,barricadeBoxMesh,barricadeStripeMesh;
        void InitializeVisuals()
        {
            visualsRoot=new GameObject("Race items (visuals only)").transform;visualsRoot.SetParent(transform,false);
            metal=Material("Missile silver",new Color(.72f,.82f,.91f),.7f);
            red=Material("Missile warning",new Color(.94f,.13f,.09f),.3f);
            yellow=Material("Banana peel",new Color(1,.72f,.025f),.05f);
            peelInner=Material("Banana inner",new Color(1,.96f,.59f),0);
            flame=Material("Missile exhaust",new Color(1,.42f,.03f),0,true);
            barricadeOrange=Material("Barricade warning orange",new Color(1,.32f,.015f),.1f);
            barricadeBlack=Material("Barricade warning black",new Color(.025f,.03f,.035f),.15f);
            coneMesh=BuildCone();barricadeBoxMesh=BuildBarricadeBox();barricadeStripeMesh=BuildBarricadeStripes();
            for(int i=0;i<MaxEntities;i++){
                var root=new GameObject(i<MaxMissiles?"Homing missile":i<BananaEnd?"Banana peel":"Placed barricade").transform;root.SetParent(visualsRoot,false);entityVisuals[i]=root;
                if(i<MaxMissiles)BuildMissile(root);else if(i<BananaEnd)BuildBanana(root);else BuildBarricade(root);
                root.gameObject.SetActive(false);
            }
        }
        static Material Material(string name,Color color,float metallic,bool glow=false)
        {
            var result=new Material(Shader.Find("Standard")){name=name,color=color};result.SetFloat("_Metallic",metallic);result.SetFloat("_Glossiness",.45f);
            if(glow){result.EnableKeyword("_EMISSION");result.SetColor("_EmissionColor",new Color(color.r,color.g,color.b)*.45f);}
            return result;
        }
        static Transform Shape(Transform parent,string name,PrimitiveType type,Vector3 position,Vector3 scale,Material material)
        {
            var go=GameObject.CreatePrimitive(type);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=scale;
            var collider=go.GetComponent<Collider>();collider.enabled=false;Destroy(collider);
            var renderer=go.GetComponent<Renderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            return go.transform;
        }
        static Transform MeshPart(Transform parent,string name,Mesh mesh,Vector3 position,Vector3 scale,Material material)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=scale;
            go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            return go.transform;
        }
        void BuildMissile(Transform root)
        {
            var body=Shape(root,"Silver fuselage",PrimitiveType.Capsule,Vector3.zero,new Vector3(.24f,.43f,.24f),metal);body.localRotation=Quaternion.Euler(90,0,0);
            var nose=MeshPart(root,"Red pointed nose",coneMesh,new Vector3(0,0,.4f),new Vector3(.18f,.28f,.18f),red);nose.localRotation=Quaternion.Euler(90,0,0);
            for(int i=0;i<4;i++){
                float angle=i*90*Mathf.Deg2Rad;var fin=Shape(root,"Tail fin",PrimitiveType.Cube,new Vector3(Mathf.Cos(angle)*.17f,Mathf.Sin(angle)*.17f,-.25f),new Vector3(.3f,.035f,.24f),red);
                fin.localRotation=Quaternion.Euler(0,0,i*90);
            }
            var exhaust=MeshPart(root,"Hot exhaust",coneMesh,new Vector3(0,0,-.45f),new Vector3(.12f,.45f,.12f),flame);exhaust.localRotation=Quaternion.Euler(-90,0,0);
        }
        void BuildBanana(Transform root)
        {
            Shape(root,"Peel centre",PrimitiveType.Sphere,new Vector3(0,.09f,0),new Vector3(.19f,.23f,.19f),peelInner);
            for(int arm=0;arm<3;arm++){
                float yaw=arm*120;var peel=new GameObject("Curved banana peel").transform;peel.SetParent(root,false);peel.localRotation=Quaternion.Euler(0,yaw,0);
                for(int segment=0;segment<4;segment++){
                    float t=segment/3f;var strip=Shape(peel,"Yellow curved segment",PrimitiveType.Capsule,new Vector3(0,.12f+Mathf.Sin(t*Mathf.PI)*.12f,.08f+t*.42f),new Vector3(.11f,.105f,.075f),yellow);
                    strip.localRotation=Quaternion.Euler(48+t*75,0,0);
                }
            }
            var stem=Shape(root,"Banana stem",PrimitiveType.Capsule,new Vector3(.025f,.29f,0),new Vector3(.075f,.14f,.075f),yellow);stem.localRotation=Quaternion.Euler(0,0,-18);
        }
        void BuildBarricade(Transform root)
        {
            MeshPart(root,"Orange warning panel",barricadeBoxMesh,new Vector3(0,.62f,0),new Vector3(1,.6f,.5f),barricadeOrange);
            MeshPart(root,"Black diagonal warning stripes",barricadeStripeMesh,Vector3.zero,Vector3.one,barricadeBlack);
            MeshPart(root,"Top panel frame",barricadeBoxMesh,new Vector3(0,.96f,0),new Vector3(1,.08f,.56f),barricadeBlack);
            MeshPart(root,"Bottom panel frame",barricadeBoxMesh,new Vector3(0,.28f,0),new Vector3(1,.08f,.56f),barricadeBlack);
            for(int side=-1;side<=1;side+=2){
                MeshPart(root,"Panel side frame",barricadeBoxMesh,new Vector3(side*.47f,.62f,0),new Vector3(.06f,.6f,.56f),barricadeBlack);
                MeshPart(root,"Upright support",barricadeBoxMesh,new Vector3(side*.38f,.49f,0),new Vector3(.065f,.9f,.4f),barricadeBlack);
                MeshPart(root,"Wide support foot",barricadeBoxMesh,new Vector3(side*.38f,.04f,0),new Vector3(.18f,.08f,1),barricadeOrange);
            }
        }
        static Mesh BuildBarricadeBox()
        {
            var vertices=new Vector3[24];var normals=new Vector3[24];var triangles=new int[36];
            for(int face=0;face<6;face++){
                Vector3 normal=face==0?Vector3.right:face==1?Vector3.left:face==2?Vector3.up:face==3?Vector3.down:face==4?Vector3.forward:Vector3.back;
                Vector3 right=face<2?Vector3.forward:Vector3.right,up=Vector3.Cross(normal,right),centre=normal*.5f;int v=face*4,k=face*6;
                vertices[v]=centre-(right+up)*.5f;vertices[v+1]=centre+(right-up)*.5f;vertices[v+2]=centre+(right+up)*.5f;vertices[v+3]=centre+(-right+up)*.5f;
                for(int corner=0;corner<4;corner++)normals[v+corner]=normal;
                triangles[k]=v;triangles[k+1]=v+1;triangles[k+2]=v+2;triangles[k+3]=v;triangles[k+4]=v+2;triangles[k+5]=v+3;
            }
            var mesh=new Mesh{name="Race barricade box",vertices=vertices,normals=normals,triangles=triangles};mesh.RecalculateBounds();return mesh;
        }
        static Mesh BuildBarricadeStripes()
        {
            var face=new[]{
                new Vector3(-.44f,.53f,0),new Vector3(-.28f,.89f,0),new Vector3(-.44f,.89f,0),
                new Vector3(-.36f,.35f,0),new Vector3(-.20f,.35f,0),new Vector3(.04f,.89f,0),new Vector3(-.12f,.89f,0),
                new Vector3(-.04f,.35f,0),new Vector3(.12f,.35f,0),new Vector3(.36f,.89f,0),new Vector3(.20f,.89f,0),
                new Vector3(.28f,.35f,0),new Vector3(.44f,.35f,0),new Vector3(.44f,.71f,0)};
            var indices=new[]{0,1,2,3,4,5,3,5,6,7,8,9,7,9,10,11,12,13};
            var vertices=new Vector3[face.Length*2];var normals=new Vector3[vertices.Length];var triangles=new int[indices.Length*2];
            for(int side=0;side<2;side++){
                Vector3 normal=side==0?Vector3.forward:Vector3.back;int offset=side*face.Length;
                for(int i=0;i<face.Length;i++){vertices[offset+i]=face[i]+normal*.255f;normals[offset+i]=normal;}
                for(int i=0;i<indices.Length;i+=3){
                    int k=side*indices.Length+i;triangles[k]=offset+indices[i];triangles[k+1]=offset+indices[i+(side==0?1:2)];triangles[k+2]=offset+indices[i+(side==0?2:1)];
                }
            }
            var mesh=new Mesh{name="Race barricade warning stripes",vertices=vertices,normals=normals,triangles=triangles};mesh.RecalculateBounds();return mesh;
        }
        static Mesh BuildCone()
        {
            const int sides=16;var vertices=new Vector3[sides+2];var triangles=new int[sides*6];vertices[sides]=Vector3.up;vertices[sides+1]=Vector3.zero;
            for(int i=0;i<sides;i++){
                float angle=i*Mathf.PI*2/sides;vertices[i]=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));int next=(i+1)%sides;
                triangles[i*6]=i;triangles[i*6+1]=sides;triangles[i*6+2]=next;triangles[i*6+3]=next;triangles[i*6+4]=sides+1;triangles[i*6+5]=i;
            }
            var mesh=new Mesh{name="Race missile cone",vertices=vertices,triangles=triangles};mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        void Update(){RestoreBodies();}
        void RestoreBodies()
        {
            for(int i=0;i<bodies.Length;i++){
                ref var body=ref bodies[i];if(!body.applied)continue;
                if(body.model){body.model.localPosition=body.position;body.model.localRotation=body.rotation;}body.applied=false;
            }
        }
        void RestoreBoxVisuals()
        {
            for(int i=0;i<itemBoxes.Length;i++){
                var box=itemBoxes[i];if(!box)continue;
                box.SetPositionAndRotation(boxRest[i],boxRotation[i]);box.gameObject.SetActive(true);
            }
            for(int i=0;i<entityVisuals.Length;i++)if(entityVisuals[i])entityVisuals[i].gameObject.SetActive(false);
        }
        void LateUpdate()
        {
            if(!initialized)return;double now=Clock;
            for(int i=0;i<itemBoxes.Length;i++){
                var box=itemBoxes[i];if(!box)continue;bool visible=boxReady[i]<=now;
                if(box.gameObject.activeSelf!=visible)box.gameObject.SetActive(visible);
                if(visible){box.position=boxRest[i]+Vector3.up*(Mathf.Sin((float)now*2+i*.7f)*.1f);box.rotation=boxRotation[i]*Quaternion.Euler(0,(float)(now*40%360),0);}
            }
            float blend=1-Mathf.Exp(-24*Time.deltaTime);
            for(int i=0;i<entities.Length;i++){
                var e=entities[i];var visual=entityVisuals[i];bool visible=e.item!=RaceItem.None&&e.expires>now&&Live;
                if(visual.gameObject.activeSelf!=visible)visual.gameObject.SetActive(visible);if(!visible)continue;
                bool fresh=visualIds[i]!=e.id;
                if(fresh||CourseNetwork.Authority||e.item!=RaceItem.Missile)visual.position=e.position;else visual.position=Vector3.Lerp(visual.position,e.position,blend);
                Vector3 direction=e.direction.sqrMagnitude>.01f?e.direction:Vector3.forward;
                visual.rotation=Quaternion.LookRotation(direction,Vector3.up);
                if(e.item==RaceItem.Barricade)visual.localScale=e.size;
                visualIds[i]=e.id;
            }
            for(int i=0;i<MaxActors;i++){
                var p=Participant(i);ref var body=ref bodies[i];bool eligible=Eligible(p);
                if(!eligible||actors[i].life!=p.RespawnSequence||actors[i].effectUntil<=now)continue;
                if(body.actor!=p||!body.model){body.actor=p;var driver=p.GetComponent<CharacterMotionDriver>();var remote=p.GetComponent<CourseBodyAnimator>();body.model=driver?driver.bodyModel:remote?remote.model:null;}
                if(!body.model)continue;
                body.position=body.model.localPosition;body.rotation=body.model.localRotation;body.applied=true;
                float duration=(float)(actors[i].effectUntil-actors[i].effectAt),t=Mathf.Clamp01((float)(now-actors[i].effectAt)/Mathf.Max(.01f,duration));
                Quaternion tumble=actors[i].effect==RaceEffect.Flip?Quaternion.AngleAxis(360*t,p.transform.right):Quaternion.AngleAxis(-65*Mathf.Sin(t*Mathf.PI),p.transform.right)*Quaternion.AngleAxis(13*Mathf.Sin(t*Mathf.PI*2),p.transform.forward);
                Vector3 centre=body.model.position+Vector3.up*.9f;
                body.model.position=centre+tumble*(body.model.position-centre);body.model.rotation=tumble*body.model.rotation;
            }
        }
        void DisposeVisuals()
        {
            if(visualsRoot)Destroy(visualsRoot.gameObject);
            if(metal)Destroy(metal);if(red)Destroy(red);if(yellow)Destroy(yellow);if(peelInner)Destroy(peelInner);if(flame)Destroy(flame);
            if(barricadeOrange)Destroy(barricadeOrange);if(barricadeBlack)Destroy(barricadeBlack);
            if(coneMesh)Destroy(coneMesh);if(barricadeBoxMesh)Destroy(barricadeBoxMesh);if(barricadeStripeMesh)Destroy(barricadeStripeMesh);
        }
    }
}
