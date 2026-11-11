using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HalliGalli.Movement
{
    public sealed partial class CourseNetwork
    {
        public bool IsCustomizationOpen { get; private set; }
        int customizationBlockedThrough=-1;
        bool customizationOnline;
        CourseSession customizationGame;
        Vector2 accessoryScroll;
        CharacterCustomizationPreview customizationPreview;

        public void ShowCustomization()
        {
            if(IsCustomizationOpen||!CanEditAppearance||!game||ControlSettings.IsOpen)return;
            IsCustomizationOpen=true;customizationOnline=online;customizationGame=game;
            accessoryScroll=Vector2.zero;customizationBlockedThrough=Time.frameCount+1;
            GUIUtility.keyboardControl=0;
            var controller=game.Local.GetComponent<SourcePlayerController>();
            if(controller){controller.SetCursorCaptured(false);controller.suppressCursorCapture=true;}
            SceneManager.sceneUnloaded+=CustomizationSceneUnloaded;
            var driver=game.Local.GetComponent<CharacterMotionDriver>();
            var body=game.Local.GetComponent<CourseBodyAnimator>();
            var model=driver&&driver.bodyModel?driver.bodyModel:body?body.model:null;
            var motions=driver?driver.motions:body?body.motions:null;
            if(model){
                customizationPreview=new CharacterCustomizationPreview();
                customizationPreview.Create(model,motions);
                customizationPreview.Refresh(SelectedShirtColor,SelectedAccessory);
            }
        }

        public void CloseCustomization()
        {
            if(!IsCustomizationOpen&&customizationPreview==null)return;
            IsCustomizationOpen=false;customizationBlockedThrough=Time.frameCount+1;
            SceneManager.sceneUnloaded-=CustomizationSceneUnloaded;
            customizationGame=null;GUIUtility.keyboardControl=0;
            customizationPreview?.Dispose();customizationPreview=null;
        }

        void CustomizationSceneUnloaded(Scene scene)
        {
            if(!customizationGame||scene==customizationGame.gameObject.scene)CloseCustomization();
        }

        void OnDisable(){CloseCustomization();}
        void LateUpdate()
        {
            if(!IsCustomizationOpen)return;
            if(!game||game!=customizationGame||online!=customizationOnline||leaving||Playing||loading||
                UserMapStudio.IsOpen||!OverlayVisible){CloseCustomization();return;}
            if(!ControlSettings.BlocksInput&&Input.GetKeyDown(KeyCode.Escape)){CloseCustomization();return;}
            customizationPreview?.Refresh(SelectedShirtColor,SelectedAccessory);
        }

        void DrawCustomizationScreen(float x,float y)
        {
            BellUI.Round(new Rect(0,0,BellUI.W,BellUI.H),new Color(.045f,.05f,.04f),0);
            BellUI.Text(new Rect(x,y+8,750,46),"내 캐릭터 꾸미기",32,BellUI.Paper,true);
            BellUI.Text(new Rect(x,y+61,840,28),"티셔츠와 치장 아이템으로 나만의 모습을 골라요.",16,BellUI.Muted);
            GUI.enabled=true;
            if(BellUI.Button(new Rect(x+884,y+16,156,44),"뒤로",size:17)){CloseCustomization();return;}
            if(Event.current.type==EventType.KeyDown&&Event.current.keyCode==KeyCode.Escape){
                Event.current.Use();CloseCustomization();return;
            }
            BellUI.Line(new Vector2(x,y+104),new Vector2(x+1040,y+104),new Color(.4f,.38f,.29f,.3f),1);

            BellUI.Round(new Rect(x,y+126,400,454),BellUI.Surface,24);
            var portrait=new Rect(x+16,y+140,368,380);
            if(customizationPreview!=null&&customizationPreview.Texture)
                GUI.DrawTexture(portrait,customizationPreview.Texture,ScaleMode.StretchToFill,false);
            else BellUI.Text(portrait,"캐릭터 미리보기를 불러올 수 없어요.",16,BellUI.Muted,false,TextAnchor.MiddleCenter);
            BellUI.Text(new Rect(x+24,y+531,352,25),"내 캐릭터 · 미리보기",16,BellUI.Paper,true,TextAnchor.MiddleCenter);

            float right=x+440;
            BellUI.Text(new Rect(right,y+127,600,30),"티셔츠 색상",22,BellUI.Paper,true);
            BellUI.Text(new Rect(right,y+166,600,24),"선택한 색 · "+PlayerShirtColors.NameAt(SelectedShirtColor),14,BellUI.Muted);
            for(int i=0;i<PlayerShirtColors.Count;i++){
                var rect=new Rect(right+(i%4)*153,y+205+(i/4)*78,141,66);
                DrawCustomizationColor(rect,i);
            }

            BellUI.Text(new Rect(right,y+375,600,30),"치장 아이템",22,BellUI.Paper,true);
            BellUI.Text(new Rect(right,y+414,600,24),"착용 중 · "+PlayerAccessories.NameAt(SelectedAccessory),14,BellUI.Muted);
            int rows=(PlayerAccessories.Count+2)/3;
            var viewport=new Rect(right,y+451,600,129);
            float contentWidth=rows>2?580:600,cellWidth=(contentWidth-24)/3;
            accessoryScroll=GUI.BeginScrollView(viewport,accessoryScroll,new Rect(0,0,contentWidth,rows*65-9),false,false);
            for(int i=0;i<PlayerAccessories.Count;i++){
                GUI.enabled=CanSelectAccessory(i);
                var rect=new Rect((i%3)*(cellWidth+12),(i/3)*65,cellWidth,56);
                if(BellUI.Button(rect,PlayerAccessories.NameAt(i),selected:i==SelectedAccessory,size:17))SelectAccessory(i);
            }
            GUI.EndScrollView();GUI.enabled=true;

            string hint=!CanEditAppearance?"지금은 외형을 변경할 수 없어요.":pendingShirtRequest!=0?"방장이 티셔츠 색상 변경을 확인하고 있어요…":
                online?"방에 확정된 모습이 표시돼요. 사용 중인 색상은 선택할 수 없어요.":"선택한 모습은 자동으로 저장돼요. 대기방에서도 바꿀 수 있어요.";
            BellUI.Text(new Rect(x,y+602,1040,30),hint,15,BellUI.Muted,false,TextAnchor.MiddleCenter);
        }

        void DrawCustomizationColor(Rect rect,int index)
        {
            bool selected=index==SelectedShirtColor,available=CanSelectShirtColor(index);
            bool occupied=online&&manager&&!selected&&ShirtOccupied(index,manager.LocalClientId);
            GUI.enabled=available;
            bool hover=available&&rect.Contains(Event.current.mousePosition);
            BellUI.Round(rect,selected?new Color(.27f,.24f,.18f):hover?new Color(.24f,.25f,.23f):BellUI.Surface,14);
            BellUI.Round(rect,selected?BellUI.Gold:new Color(.3f,.32f,.28f,.5f),14,selected?2:1);
            var swatch=new Rect(rect.x+12,rect.y+18,30,30);
            Color shirt=PlayerShirtColors.ColorAt(index);
            BellUI.Round(swatch,occupied?Color.Lerp(BellUI.Ink,shirt,.35f):shirt,15);
            if(selected)BellUI.Icon("check",new Rect(swatch.x+6,swatch.y+6,18,18),shirt.grayscale>.55f?BellUI.Ink:BellUI.Paper);
            BellUI.Text(new Rect(rect.x+52,rect.y+10,81,24),PlayerShirtColors.NameAt(index),16,occupied?BellUI.Muted:BellUI.Paper,selected);
            BellUI.Text(new Rect(rect.x+52,rect.y+35,81,21),selected?"선택됨":occupied?"사용 중":available?"선택 가능":"변경 대기",12,selected?BellUI.Gold:BellUI.Muted);
            if(GUI.Button(rect,GUIContent.none,GUIStyle.none))SelectShirtColor(index);
            GUI.enabled=true;
        }

        // Only the authored body visual is cloned; no controller, participant or live actor is moved.
        sealed class CharacterCustomizationPreview : System.IDisposable
        {
            const int PreviewLayer=31;
            static readonly int ShirtColorId=Shader.PropertyToID(PlayerShirtAppearance.ColorProperty);
            readonly List<Mesh> meshes=new List<Mesh>();
            readonly List<PreviewSkin> skins=new List<PreviewSkin>();
            readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
            GameObject root,worn;
            Transform head;
            Camera camera;
            RenderTexture texture;
            int shirtIndex=-1,accessoryIndex=-1;
            public RenderTexture Texture=>texture;

            sealed class PreviewSkin
            {
                public MeshRenderer renderer;
                public Material[] materials;
            }

            public void Create(Transform source,CharacterMotionSet motions)
            {
                root=new GameObject("Character customization preview"){hideFlags=HideFlags.HideAndDontSave};
                root.SetActive(false);root.transform.position=new Vector3(10000,10000,10000);
                var model=Instantiate(source.gameObject,root.transform,false);
                model.name="Customization body";model.SetActive(true);
                model.transform.localPosition=Vector3.zero;model.transform.localRotation=Quaternion.identity;
                model.transform.localScale=source.lossyScale;
                foreach(var behaviour in model.GetComponentsInChildren<Behaviour>(true))behaviour.enabled=false;
                foreach(var collider in model.GetComponentsInChildren<Collider>(true))collider.enabled=false;
                if(motions)foreach(var clip in motions.bodyClips)
                    if(clip&&clip.name=="Idle"){clip.SampleAnimation(model,0);break;}
                foreach(var bone in model.GetComponentsInChildren<Transform>(true))if(bone.name=="Head"){head=bone;break;}
                // Remove the clone's previously worn prefab before installing the selected item.
                if(head)for(int i=head.childCount-1;i>=0;i--){
                    var child=head.GetChild(i);
                    for(int item=0;item<PlayerAccessories.Count;item++){
                        var prefab=PlayerAccessories.Prefab(item);
                        if(!prefab||child.name!=prefab.name+"(Clone)")continue;
                        child.gameObject.SetActive(false);child.SetParent(root.transform,false);Destroy(child.gameObject);break;
                    }
                }
                var shirtMaterial=Resources.Load<Material>(PlayerShirtAppearance.MaterialResource);
                foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)){
                    if(!skin.sharedMesh)continue;
                    var mesh=new Mesh{name="Customization body pose",hideFlags=HideFlags.HideAndDontSave};
                    skin.BakeMesh(mesh,true);meshes.Add(mesh);skin.enabled=false;
                    var visual=new GameObject("Customization baked skin",typeof(MeshFilter),typeof(MeshRenderer));
                    visual.transform.SetParent(skin.transform,false);
                    visual.GetComponent<MeshFilter>().sharedMesh=mesh;
                    var renderer=visual.GetComponent<MeshRenderer>();
                    var materials=skin.sharedMaterials;
                    for(int slot=0;slot<materials.Length;slot++){
                        var material=materials[slot];
                        if(shirtMaterial&&material&&material.mainTexture==shirtMaterial.mainTexture&&
                            (material.shader.name=="Standard"||material.shader==shirtMaterial.shader))materials[slot]=shirtMaterial;
                    }
                    renderer.sharedMaterials=materials;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows=false;skins.Add(new PreviewSkin{renderer=renderer,materials=materials});
                }
                SetPreviewLayer(model.transform);
                camera=new GameObject("Customization camera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform,false);camera.transform.localPosition=new Vector3(0,1.3f,4);
                camera.transform.localRotation=Quaternion.Euler(0,180,0);camera.enabled=false;
                camera.orthographic=true;camera.orthographicSize=.72f;camera.nearClipPlane=.05f;camera.farClipPlane=12;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=BellUI.Surface;
                camera.cullingMask=1<<PreviewLayer;camera.allowHDR=false;camera.allowMSAA=true;camera.useOcclusionCulling=false;
                AddLight(new Vector3(-1.6f,2.7f,2.5f),1.25f,new Color(1,.93f,.82f));
                AddLight(new Vector3(1.6f,1.8f,2),.85f,new Color(.78f,.88f,1));
                texture=new RenderTexture(736,760,24,RenderTextureFormat.ARGB32){name="Customization portrait",hideFlags=HideFlags.HideAndDontSave,antiAliasing=2};
                texture.Create();camera.targetTexture=texture;
            }

            void AddLight(Vector3 position,float intensity,Color color)
            {
                var light=new GameObject("Customization portrait light").AddComponent<Light>();
                light.transform.SetParent(root.transform,false);light.transform.localPosition=position;
                light.type=LightType.Point;light.range=8;light.intensity=intensity;light.color=color;
                light.cullingMask=1<<PreviewLayer;light.shadows=LightShadows.None;
            }

            static void SetPreviewLayer(Transform transform)
            {
                transform.gameObject.layer=PreviewLayer;
                for(int i=0;i<transform.childCount;i++)SetPreviewLayer(transform.GetChild(i));
            }

            public void Refresh(int shirt,int accessory)
            {
                if(!root||!camera||!texture||shirt==shirtIndex&&accessory==accessoryIndex)return;
                if(shirt!=shirtIndex){
                    shirtIndex=shirt;
                    properties.Clear();properties.SetColor(ShirtColorId,PlayerShirtColors.ColorAt(shirt));
                    foreach(var skin in skins)for(int slot=0;slot<skin.materials.Length;slot++)
                        if(skin.materials[slot]&&skin.materials[slot].HasProperty(ShirtColorId))skin.renderer.SetPropertyBlock(properties,slot);
                }
                if(accessory!=accessoryIndex){
                    accessoryIndex=accessory;
                    if(worn){worn.SetActive(false);Destroy(worn);}worn=null;
                    var prefab=PlayerAccessories.Prefab(accessory);
                    if(prefab&&head){worn=Instantiate(prefab,head,false);SetPreviewLayer(worn.transform);}
                }
                // The preview and its lights exist only during this camera's render, never in a world camera.
                var previous=RenderTexture.active;
                try{root.SetActive(true);camera.Render();}
                finally{root.SetActive(false);RenderTexture.active=previous;}
            }

            public void Dispose()
            {
                if(root)root.SetActive(false);
                if(camera)camera.targetTexture=null;
                if(texture){texture.Release();Destroy(texture);texture=null;}
                foreach(var mesh in meshes)if(mesh)Destroy(mesh);
                meshes.Clear();skins.Clear();
                if(root)Destroy(root);root=null;camera=null;head=null;worn=null;
            }
        }
    }
}
