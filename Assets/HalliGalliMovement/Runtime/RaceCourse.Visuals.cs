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
        readonly Transform[] shields=new Transform[MaxActors];
        Transform visualsRoot;
        Material metal,red,yellow,peelInner,flame,shieldMaterial,shieldEdge;
        Mesh coneMesh,ringMesh;
        void InitializeVisuals()
        {
            visualsRoot=new GameObject("Race items (visuals only)").transform;visualsRoot.SetParent(transform,false);
            metal=Material("Missile silver",new Color(.72f,.82f,.91f),.7f);
            red=Material("Missile warning",new Color(.94f,.13f,.09f),.3f);
            yellow=Material("Banana peel",new Color(1,.72f,.025f),.05f);
            peelInner=Material("Banana inner",new Color(1,.96f,.59f),0);
            flame=Material("Missile exhaust",new Color(1,.42f,.03f),0,true);
            shieldEdge=Material("Shield rings",new Color(.18f,1,.88f),.35f,true);
            shieldMaterial=Material("Shield shell",new Color(.14f,.86f,.94f,.12f),.05f,true);
            shieldMaterial.SetFloat("_Mode",3);shieldMaterial.SetInt("_SrcBlend",(int)BlendMode.SrcAlpha);shieldMaterial.SetInt("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);
            shieldMaterial.SetInt("_ZWrite",0);shieldMaterial.EnableKeyword("_ALPHABLEND_ON");shieldMaterial.renderQueue=3000;
            coneMesh=BuildCone();ringMesh=BuildRing();
            for(int i=0;i<MaxEntities;i++){
                var root=new GameObject(i<MaxMissiles?"Homing missile":"Banana peel").transform;root.SetParent(visualsRoot,false);entityVisuals[i]=root;
                if(i<MaxMissiles)BuildMissile(root);else BuildBanana(root);
                root.gameObject.SetActive(false);
            }
            for(int i=0;i<MaxActors;i++){
                var root=new GameObject("Temporary shield").transform;root.SetParent(visualsRoot,false);shields[i]=root;
                Shape(root,"Shield shell",PrimitiveType.Sphere,Vector3.zero,new Vector3(1.75f,2.05f,1.75f),shieldMaterial);
                MeshPart(root,"Shield orbit",ringMesh,Vector3.zero,new Vector3(.95f,.95f,.95f),shieldEdge);
                var second=MeshPart(root,"Shield orbit cross",ringMesh,Vector3.zero,new Vector3(.95f,1.06f,.95f),shieldEdge);second.localRotation=Quaternion.Euler(0,0,65);
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
        static Mesh BuildCone()
        {
            const int sides=16;var vertices=new Vector3[sides+2];var triangles=new int[sides*6];vertices[sides]=Vector3.up;vertices[sides+1]=Vector3.zero;
            for(int i=0;i<sides;i++){
                float angle=i*Mathf.PI*2/sides;vertices[i]=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));int next=(i+1)%sides;
                triangles[i*6]=i;triangles[i*6+1]=sides;triangles[i*6+2]=next;triangles[i*6+3]=next;triangles[i*6+4]=sides+1;triangles[i*6+5]=i;
            }
            var mesh=new Mesh{name="Race missile cone",vertices=vertices,triangles=triangles};mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        static Mesh BuildRing()
        {
            const int sides=40,tube=6;var vertices=new Vector3[sides*tube];var triangles=new int[sides*tube*6];
            for(int i=0;i<sides;i++)for(int j=0;j<tube;j++){
                float a=i*Mathf.PI*2/sides,b=j*Mathf.PI*2/tube,r=1+Mathf.Cos(b)*.017f;int v=i*tube+j;
                vertices[v]=new Vector3(Mathf.Cos(a)*r,Mathf.Sin(b)*.017f,Mathf.Sin(a)*r);
                int next=(i+1)%sides*tube+j,around=i*tube+(j+1)%tube,corner=(i+1)%sides*tube+(j+1)%tube,k=v*6;
                triangles[k]=v;triangles[k+1]=around;triangles[k+2]=next;triangles[k+3]=next;triangles[k+4]=around;triangles[k+5]=corner;
            }
            var mesh=new Mesh{name="Race shield orbit",vertices=vertices,triangles=triangles};mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        void Update(){RestoreBodies();}
        void RestoreBodies()
        {
            for(int i=0;i<bodies.Length;i++){
                ref var body=ref bodies[i];if(!body.applied)continue;
                if(body.model){body.model.localPosition=body.position;body.model.localRotation=body.rotation;}body.applied=false;
            }
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
                if(visualIds[i]!=e.id||CourseNetwork.Authority||e.item==RaceItem.Banana)visual.position=e.position;else visual.position=Vector3.Lerp(visual.position,e.position,blend);
                visualIds[i]=e.id;visual.rotation=Quaternion.LookRotation(e.direction.sqrMagnitude>.01f?e.direction:Vector3.forward,Vector3.up);
            }
            for(int i=0;i<MaxActors;i++){
                var p=Participant(i);ref var body=ref bodies[i];var shield=shields[i];bool eligible=Eligible(p);
                bool guarded=eligible&&actors[i].life==p.RespawnSequence&&actors[i].shieldUntil>now;
                if(shield.gameObject.activeSelf!=guarded)shield.gameObject.SetActive(guarded);
                if(guarded){shield.position=Anchor(p);shield.rotation=Quaternion.Euler(0,(float)(now*55%360),0);float flash=1+.14f*Mathf.Clamp01(1-(float)(now-actors[i].blockedAt)*4);shield.localScale=Vector3.one*flash;}
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
            if(metal)Destroy(metal);if(red)Destroy(red);if(yellow)Destroy(yellow);if(peelInner)Destroy(peelInner);if(flame)Destroy(flame);if(shieldMaterial)Destroy(shieldMaterial);if(shieldEdge)Destroy(shieldEdge);
            if(coneMesh)Destroy(coneMesh);if(ringMesh)Destroy(ringMesh);
        }
    }
}
