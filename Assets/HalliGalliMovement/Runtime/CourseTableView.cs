using HalliGalli.Cards;
using UnityEngine;

namespace HalliGalli.Movement
{
    // Run after the normal view and body updates, so moving the camera never moves the seated body.
    [DefaultExecutionOrder(200),DisallowMultipleComponent]
    public sealed class CourseTableView:MonoBehaviour
    {
        public CourseParticipant participant;
        public CardTableDisplay table;
        [Range(30,70)] public float fieldOfView=50;
        [Min(1)] public float minimumHeight=2.55f;
        [Range(0,.5f)] public float transitionSeconds=.22f;
        public bool Focused{get;private set;}
        SourcePlayerController controller;
        CharacterMotionDriver driver;
        Vector3 startPosition;Quaternion startRotation;
        float startedAt,normalFov;

        void Awake(){Initialize();}
        void Initialize(){if(!controller)controller=GetComponent<SourcePlayerController>();if(!driver)driver=GetComponent<CharacterMotionDriver>();}
        public void Focus()
        {
            Initialize();if(Focused||!participant||!participant.IsSeated||!table||!controller||!controller.playerCamera)return;
            controller.UpdateView();
            var camera=controller.playerCamera;startPosition=camera.transform.position;startRotation=camera.transform.rotation;normalFov=camera.fieldOfView;startedAt=Time.time;
            Focused=true;controller.lookSuppressed=true;
        }
        public void ClearFocus()
        {
            if(!Focused)return;Focused=false;
            if(controller){controller.lookSuppressed=false;if(controller.playerCamera)controller.playerCamera.fieldOfView=normalFov;controller.UpdateView();}
            if(driver&&driver.handCamera)driver.handCamera.enabled=!driver.thirdPerson;
        }
        void LateUpdate()
        {
            if(!Focused)return;
            if(!participant.IsSeated||!table){ClearFocus();return;}
            var camera=controller.playerCamera;
            var bounds=new Bounds(table.openAnchors[0].position,Vector3.zero);
            foreach(var anchor in table.openAnchors)bounds.Encapsulate(anchor.position);
            foreach(var anchor in table.drawAnchors)bounds.Encapsulate(anchor.position);
            if(table.ViewCount>0)bounds.Encapsulate(table.CardBounds);
            // A small tilt exposes stack edges while keeping fruit faces readable.
            Quaternion rotation=Quaternion.Euler(70,0,0);
            bounds.Expand(new Vector3(.72f,.08f,1.2f));
            float tangent=Mathf.Tan(fieldOfView*.5f*Mathf.Deg2Rad);
            float distance=minimumHeight/Mathf.Sin(70*Mathf.Deg2Rad);
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2){
                var point=Quaternion.Inverse(rotation)*Vector3.Scale(bounds.extents,new Vector3(x,y,z));
                distance=Mathf.Max(distance,Mathf.Abs(point.y)/tangent-point.z,Mathf.Abs(point.x)/(tangent*Mathf.Max(.25f,camera.aspect))-point.z);
            }
            Vector3 position=bounds.center-rotation*Vector3.forward*distance;
            float t=transitionSeconds<=0?1:Mathf.SmoothStep(0,1,Mathf.Clamp01((Time.time-startedAt)/transitionSeconds));
            camera.transform.SetPositionAndRotation(Vector3.Lerp(startPosition,position,t),Quaternion.Slerp(startRotation,rotation,t));
            camera.fieldOfView=Mathf.Lerp(normalFov,fieldOfView,t);
            if(driver&&driver.handCamera)driver.handCamera.enabled=false;
        }
        void OnDisable(){ClearFocus();}
    }
}
