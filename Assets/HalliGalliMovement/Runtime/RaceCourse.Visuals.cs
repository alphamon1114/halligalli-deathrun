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
        readonly Transform[] laserBeams=new Transform[MaxEntities-BallEnd];
        readonly Transform[] laserImpacts=new Transform[MaxEntities-BallEnd];
        readonly Transform[] shields=new Transform[MaxActors];
        Transform visualsRoot;
        Material metal,red,yellow,peelInner,flame,shieldMaterial,shieldEdge,laserMaterial;
        Mesh coneMesh,ringMesh,ballStripeMesh;
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
            laserMaterial=Material("Laser beam",new Color(1,.12f,.48f),0,true);
            laserMaterial.SetColor("_EmissionColor",new Color(1,.12f,.48f)*3);
            shieldMaterial.SetFloat("_Mode",3);shieldMaterial.SetInt("_SrcBlend",(int)BlendMode.SrcAlpha);shieldMaterial.SetInt("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);
            shieldMaterial.SetInt("_ZWrite",0);shieldMaterial.EnableKeyword("_ALPHABLEND_ON");shieldMaterial.renderQueue=3000;
            coneMesh=BuildCone();ringMesh=BuildRing();ballStripeMesh=BuildBallStripes();
            for(int i=0;i<MaxEntities;i++){
                var root=new GameObject(i<MaxMissiles?"Homing missile":i<BananaEnd?"Banana peel":i<BallEnd?"Rolling metal ball":"Straight laser").transform;root.SetParent(visualsRoot,false);entityVisuals[i]=root;
                if(i<MaxMissiles)BuildMissile(root);else if(i<BananaEnd)BuildBanana(root);else if(i<BallEnd)BuildRollingBall(root);else BuildLaser(root,i-BallEnd);
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
        void BuildRollingBall(Transform root)
        {
            Shape(root,"Large metal ball",PrimitiveType.Sphere,Vector3.zero,Vector3.one*1.5f,metal);
            MeshPart(root,"Yellow rolling stripes",ballStripeMesh,Vector3.zero,Vector3.one,yellow);
            MeshPart(root,"Red equator",ringMesh,Vector3.zero,Vector3.one*.755f,red);
        }
        void BuildLaser(Transform root,int index)
        {
            var beam=Shape(root,"Thin laser beam",PrimitiveType.Cylinder,Vector3.zero,new Vector3(.065f,0,.065f),laserMaterial);
            beam.localRotation=Quaternion.Euler(90,0,0);laserBeams[index]=beam;
            Shape(root,"Laser origin",PrimitiveType.Sphere,Vector3.zero,Vector3.one*.18f,laserMaterial);
            laserImpacts[index]=Shape(root,"Laser impact",PrimitiveType.Sphere,Vector3.zero,Vector3.one*.24f,laserMaterial);
        }
        static Mesh BuildBallStripes()
        {
            const int sides=40,rows=3;var vertices=new Vector3[2*sides*rows];var triangles=new int[2*sides*(rows-1)*6];
            for(int band=0;band<2;band++)for(int row=0;row<rows;row++)for(int side=0;side<sides;side++){
                float latitude=(band==0?-.46f:.46f)+(row/(float)(rows-1)-.5f)*.24f;
                float angle=side*Mathf.PI*2/sides;int v=(band*rows+row)*sides+side;
                float radius=Mathf.Cos(latitude)*.754f;
                vertices[v]=new Vector3(Mathf.Cos(angle)*radius,Mathf.Sin(latitude)*.754f,Mathf.Sin(angle)*radius);
                if(row==rows-1)continue;
                int next=(band*rows+row)*sides+(side+1)%sides,above=v+sides,corner=next+sides,k=((band*(rows-1)+row)*sides+side)*6;
                triangles[k]=v;triangles[k+1]=above;triangles[k+2]=next;triangles[k+3]=next;triangles[k+4]=above;triangles[k+5]=corner;
            }
            var mesh=new Mesh{name="Race rolling ball stripes",vertices=vertices,triangles=triangles};mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
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
        void RestoreBoxVisuals()
        {
            for(int i=0;i<itemBoxes.Length;i++){
                var box=itemBoxes[i];if(!box)continue;
                box.SetPositionAndRotation(boxRest[i],boxRotation[i]);box.gameObject.SetActive(true);
            }
            for(int i=0;i<entityVisuals.Length;i++)if(entityVisuals[i])entityVisuals[i].gameObject.SetActive(false);
            for(int i=0;i<shields.Length;i++)if(shields[i])shields[i].gameObject.SetActive(false);
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
                bool fresh=visualIds[i]!=e.id;Vector3 previous=visual.position;
                if(fresh||CourseNetwork.Authority||e.item==RaceItem.Banana||e.item==RaceItem.Laser)visual.position=e.position;else visual.position=Vector3.Lerp(visual.position,e.position,blend);
                Vector3 direction=e.direction.sqrMagnitude>.01f?e.direction:Vector3.forward;
                if(e.item==RaceItem.RollingBall){
                    if(fresh)visual.rotation=Quaternion.LookRotation(direction,Vector3.up);
                    else{
                        Vector3 travel=visual.position-previous;travel.y=0;float distance=travel.magnitude;
                        if(distance>.0001f)visual.rotation=Quaternion.AngleAxis(distance/.75f*Mathf.Rad2Deg,Vector3.Cross(Vector3.up,travel)/distance)*visual.rotation;
                    }
                }else{
                    visual.rotation=Quaternion.LookRotation(direction,e.item==RaceItem.Laser&&Mathf.Abs(direction.y)>.99f?Vector3.forward:Vector3.up);
                    if(e.item==RaceItem.Laser){
                        int index=i-BallEnd;float distance=Mathf.Clamp(e.distance,0,45);
                        var beam=laserBeams[index];beam.localPosition=new Vector3(0,0,distance*.5f);beam.localScale=new Vector3(.065f,distance*.5f,.065f);
                        laserImpacts[index].localPosition=new Vector3(0,0,distance);
                    }
                }
                visualIds[i]=e.id;
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
                Quaternion tumble=actors[i].effect==RaceEffect.Flip?Quaternion.AngleAxis(360*t,p.transform.right):actors[i].effect==RaceEffect.Push?Quaternion.AngleAxis(-18*Mathf.Sin(t*Mathf.PI),p.transform.right):Quaternion.AngleAxis(-65*Mathf.Sin(t*Mathf.PI),p.transform.right)*Quaternion.AngleAxis(13*Mathf.Sin(t*Mathf.PI*2),p.transform.forward);
                Vector3 centre=body.model.position+Vector3.up*.9f;
                body.model.position=centre+tumble*(body.model.position-centre);body.model.rotation=tumble*body.model.rotation;
            }
        }
        void DisposeVisuals()
        {
            if(visualsRoot)Destroy(visualsRoot.gameObject);
            if(metal)Destroy(metal);if(red)Destroy(red);if(yellow)Destroy(yellow);if(peelInner)Destroy(peelInner);if(flame)Destroy(flame);if(shieldMaterial)Destroy(shieldMaterial);if(shieldEdge)Destroy(shieldEdge);
            if(laserMaterial)Destroy(laserMaterial);
            if(coneMesh)Destroy(coneMesh);if(ringMesh)Destroy(ringMesh);if(ballStripeMesh)Destroy(ballStripeMesh);
        }
    }
}
