using System.Linq;
using UnityEngine;

namespace HalliGalli.Movement
{
    // VARCO supplies the held coil and honda loop; this strand is animated between
    // the hand and the actual swept projectile / server-confirmed target.
    [DefaultExecutionOrder(180)]
    public sealed class LassoVisual:MonoBehaviour
    {
        CourseParticipant owner;LassoTool tool;CharacterMotionDriver driver;
        Transform coil,loop,left,right,bodyLeft,bodyRight;
        LineRenderer[] strands;Material hemp;
        void Start()
        {
            owner=GetComponent<CourseParticipant>();tool=GetComponent<LassoTool>();driver=GetComponent<CharacterMotionDriver>();
            if(driver&&driver.handsModel){left=Bone(driver.handsModel,"L_Wrist");right=Bone(driver.handsModel,"R_Wrist");}
            var body=driver?driver.bodyModel:GetComponent<CourseBodyAnimator>()?.model;
            if(body){bodyLeft=Bone(body,"LeftHand");bodyRight=Bone(body,"RightHand");}
            coil=Model("Coil");loop=Model("Loop");
            hemp=new Material(Shader.Find("Standard")){color=new Color(.56f,.39f,.20f)};hemp.SetFloat("_Glossiness",.12f);
            strands=new LineRenderer[3];
            for(int i=0;i<3;i++){
                var go=new GameObject("Lasso braided strand "+i);go.transform.SetParent(transform,false);
                var l=go.AddComponent<LineRenderer>();strands[i]=l;l.sharedMaterial=hemp;l.widthMultiplier=.008f;l.numCornerVertices=2;l.numCapVertices=2;
                l.positionCount=81;l.useWorldSpace=true;l.enabled=false;
            }
        }
        static Transform Bone(Transform root,string name)=>root.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name==name);
        Transform Model(string name)
        {
            var prefab=Resources.Load<GameObject>("Lasso/"+name);
            if(!prefab)return null;
            var result=Instantiate(prefab,transform).transform;result.name="VARCO Lasso "+name;result.gameObject.SetActive(false);return result;
        }
        void LateUpdate()
        {
            if(strands==null||!owner)return;
            bool visible=!owner.IsSeated&&!owner.IsRespawning&&(tool.ConnectionActive||tool.GestureActive)&&!owner.Motor.Stunned;
            bool fp=owner.localPlayer&&driver&&!driver.thirdPerson;
            var rotation=fp?driver.worldCamera.transform.rotation:Quaternion.LookRotation(owner.Combat.networkAim??transform.forward);
            var l=fp?left:bodyLeft;var r=fp?right:bodyRight;
            Vector3 grip=l?l.position:owner.Motor.CurrentEyePosition+rotation*new Vector3(-.24f,-.25f,.35f);
            Vector3 start=r?r.position:owner.Motor.CurrentEyePosition+rotation*new Vector3(.22f,-.2f,.4f);
            if(coil){coil.gameObject.SetActive(visible);if(visible){coil.SetPositionAndRotation(grip+rotation*new Vector3(0,-.17f,.045f),rotation*Quaternion.Euler(0,12,-12));Layer(coil,fp?CharacterMotionDriver.HandsLayer:0);}}
            float retraction=Mathf.Clamp01((Time.time-tool.PullAt)/.30f);
            Vector3 end=tool.Phase==LassoPhase.Latched&&tool.Target?LassoTool.Anchor(tool.Target):tool.Tip;
            if(!tool.ConnectionActive)end=Vector3.Lerp(end,start,retraction);
            bool extended=visible&&(tool.ConnectionActive||Time.time-tool.PullAt<.3f);
            if(loop){loop.gameObject.SetActive(extended);if(extended){
                var facing=tool.Phase==LassoPhase.Latched?Quaternion.Euler(90,0,0):Quaternion.LookRotation(tool.CastDirection.sqrMagnitude>.01f?tool.CastDirection:transform.forward);
                loop.SetPositionAndRotation(end,facing);loop.localScale=Vector3.one*(tool.Phase==LassoPhase.Latched?1.45f:1);
            }}
            float length=Vector3.Distance(start,end);Vector3 axis=(end-start).normalized;
            Vector3 side=Vector3.Cross(axis,Vector3.up).normalized;if(side.sqrMagnitude<.01f)side=Vector3.right;
            Vector3 up=Vector3.Cross(side,axis).normalized;
            for(int strand=0;strand<3;strand++){
                var line=strands[strand];line.enabled=extended;
                if(!extended)continue;
                for(int i=0;i<81;i++){
                    float t=i/80f,twist=t*length*30+strand*Mathf.PI*2/3;
                    float sag=Mathf.Sin(t*Mathf.PI)*Mathf.Min(.3f,length*.025f)*(tool.Phase==LassoPhase.Latched?1:.25f);
                    line.SetPosition(i,Vector3.Lerp(start,end,t)-Vector3.up*sag+(side*Mathf.Cos(twist)+up*Mathf.Sin(twist))*.004f);
                }
            }
        }
        static void Layer(Transform model,int layer){foreach(var r in model.GetComponentsInChildren<Renderer>())r.gameObject.layer=layer;}
        void OnDestroy(){if(hemp)Destroy(hemp);}
    }
}
