using System.Collections.Generic;
using UnityEngine;

namespace HalliGalli.Cards
{
    public sealed class CardTableDisplay:MonoBehaviour
    {
        public ClassicFruitDeck deck;
        public Transform[] drawAnchors,openAnchors;
        public Transform visualRoot;
        readonly Dictionary<int,FruitCardView> views=new Dictionary<int,FruitCardView>();
        public CardTurnDisplay TurnDisplay{get;private set;}
        public int ViewCount=>views.Count;
        // Exaggerate only the hidden pile's edges so its size is readable from the table camera.
        public static float CardThickness(bool faceDown)=>faceDown?.008f:.0024f;
        public Bounds CardBounds{get;private set;}
        public void Initialize(HalliGalliMatch match)
        {
            if(drawAnchors.Length!=match.Seats.Count||openAnchors.Length!=match.Seats.Count)throw new System.InvalidOperationException("Table anchors must match participant seats.");
            if(visualRoot){visualRoot.gameObject.SetActive(false);if(Application.isPlaying)Destroy(visualRoot.gameObject);else DestroyImmediate(visualRoot.gameObject);}
            visualRoot=new GameObject("Live card visuals").transform;visualRoot.SetParent(transform,false);views.Clear();
            foreach(var card in match.AllCards()){
                var view=Instantiate(deck.cardPrefab,visualRoot).GetComponent<FruitCardView>();view.name="Card "+card.Serial;view.SetCard(card.Definition);views.Add(card.Serial,view);
            }
            // Created at runtime for every map, including already-authored scenes.
            if(Application.isPlaying){TurnDisplay=GetComponent<CardTurnDisplay>();if(!TurnDisplay)TurnDisplay=gameObject.AddComponent<CardTurnDisplay>();TurnDisplay.Bind(this,match);}
            Refresh(match);
        }
        public void Refresh(HalliGalliMatch match)
        {
            for(int i=0;i<match.Seats.Count;i++){Layout(match.Seats[i].Draw,drawAnchors[i],true);Layout(match.Seats[i].Open,openAnchors[i],false);}
            bool first=true;var bounds=new Bounds(transform.position,Vector3.zero);
            foreach(var view in views.Values){if(first){bounds=view.cardRenderer.bounds;first=false;}else bounds.Encapsulate(view.cardRenderer.bounds);}
            CardBounds=bounds;
            if(TurnDisplay)TurnDisplay.Refresh(match);
        }
        void Layout(IReadOnlyList<FruitCardInstance> pile,Transform anchor,bool down)
        {
            for(int i=0;i<pile.Count;i++){
                float thickness=CardThickness(down);
                var view=views[pile[i].Serial];view.transform.SetPositionAndRotation(anchor.position+anchor.up*((i+.5f)*thickness),anchor.rotation);
                view.SetStackThickness(thickness,i);
                // Commit the visible face and rule state together; there is no hidden half-flip snapshot.
                view.SetFaceDown(down);
            }
        }
        public FruitCardView ViewFor(int serial)=>views[serial];
    }
}
