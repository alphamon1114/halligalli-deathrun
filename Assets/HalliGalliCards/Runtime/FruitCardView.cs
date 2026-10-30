using System.Collections;
using UnityEngine;

namespace HalliGalli.Cards
{
    public sealed class FruitCardView:MonoBehaviour
    {
        public ClassicFruitDeck deck;
        public FruitCardDefinition definition;
        public MeshRenderer cardRenderer;
        public Transform visual;
        [SerializeField] bool faceDown;
        public bool FaceDown=>faceDown;
        public bool IsFlipping{get;private set;}
        Coroutine flip;
        Vector3 restingPosition;
        bool hasRestingPosition;
        MaterialPropertyBlock edgeProperties;
        void Awake(){Apply();}
        public void SetCard(FruitCardDefinition value){definition=value;ApplyMaterials();}
        void ApplyMaterials(){if(cardRenderer&&deck&&definition)cardRenderer.sharedMaterials=new[]{definition.faceMaterial,deck.backMaterial,deck.edgeMaterial};}
        public void Apply(){ApplyMaterials();if(visual){visual.localRotation=Quaternion.Euler(faceDown?180:0,0,0);if(hasRestingPosition)visual.localPosition=restingPosition;}}
        public void SetStackThickness(float thickness,int layer)
        {
            var mesh=visual.GetComponent<MeshFilter>().sharedMesh;
            var scale=visual.localScale;scale.y=thickness/mesh.bounds.size.y;visual.localScale=scale;
            if(edgeProperties==null)edgeProperties=new MaterialPropertyBlock();
            edgeProperties.SetColor("_Color",deck.edgeMaterial.color*(layer%2==0?1f:.82f));
            cardRenderer.SetPropertyBlock(edgeProperties,2);
        }
        public void SetFaceDown(bool value,bool animate=false)
        {
            if(flip!=null){StopCoroutine(flip);flip=null;}IsFlipping=false;faceDown=value;
            if(!animate||!Application.isPlaying||!isActiveAndEnabled){Apply();return;}
            flip=StartCoroutine(FlipTo(value));
        }
        IEnumerator FlipTo(bool value)
        {
            if(!hasRestingPosition){restingPosition=visual.localPosition;hasRestingPosition=true;}
            IsFlipping=true;Quaternion from=visual.localRotation,to=Quaternion.Euler(value?180:0,0,0);float elapsed=0;
            float halfLength=visual.GetComponent<MeshFilter>().sharedMesh.bounds.extents.z;
            while(elapsed<.24f){elapsed+=Time.deltaTime;float t=Mathf.Clamp01(elapsed/.24f);visual.localRotation=Quaternion.Slerp(from,to,Mathf.SmoothStep(0,1,t));
                // Raise the rotating edge above the table instead of clipping through its surface.
                visual.localPosition=restingPosition+Vector3.up*(Mathf.Abs((visual.localRotation*Vector3.forward).y)*halfLength);yield return null;}
            visual.localRotation=to;visual.localPosition=restingPosition;IsFlipping=false;flip=null;
        }
        void OnDisable(){if(flip!=null)StopCoroutine(flip);flip=null;IsFlipping=false;Apply();}
    }
}
