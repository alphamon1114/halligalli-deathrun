using System;
using System.Collections.Generic;
using UnityEngine;

namespace HalliGalli.Cards
{
    [Serializable]
    public sealed class FruitCardInstance
    {
        [SerializeField] int serial;
        [SerializeField] FruitCardDefinition definition;
        public int Serial=>serial;
        public FruitCardDefinition Definition=>definition;
        public FruitCardInstance(int serial,FruitCardDefinition definition){this.serial=serial;this.definition=definition;}
    }

    [CreateAssetMenu(menuName="HalliGalli/Cards/Classic 56-card deck")]
    public sealed class ClassicFruitDeck:ScriptableObject
    {
        public const int TotalCards=56;
        public FruitCardDefinition[] definitions;
        public Texture2D backTexture;
        public Material backMaterial,edgeMaterial;
        public GameObject cardPrefab;
        static readonly int[] Distribution={5,3,3,2,1};
        public static int CopiesFor(int fruitCount){if(fruitCount<1||fruitCount>5)throw new ArgumentOutOfRangeException(nameof(fruitCount));return Distribution[fruitCount-1];}
        public void Validate()
        {
            if(definitions==null||definitions.Length!=20)throw new InvalidOperationException("Classic deck needs 20 card types.");
            var seen=new HashSet<int>();var ids=new HashSet<string>();int total=0;
            foreach(var c in definitions){
                if(!c||string.IsNullOrWhiteSpace(c.cardId))throw new InvalidOperationException("Missing card definition or id.");
                if((int)c.fruit<0||(int)c.fruit>3||c.fruitCount<1||c.fruitCount>5)throw new InvalidOperationException("Invalid fruit or count: "+c.cardId);
                if(!seen.Add((int)c.fruit*5+c.fruitCount)||!ids.Add(c.cardId))throw new InvalidOperationException("Duplicate card type: "+c.cardId);
                if(c.copiesInDeck!=CopiesFor(c.fruitCount))throw new InvalidOperationException("Wrong classic multiplicity: "+c.cardId);
                if(!c.faceTexture||!c.faceMaterial)throw new InvalidOperationException("Missing face art: "+c.cardId);
                total+=c.copiesInDeck;
            }
            if(total!=TotalCards||!backTexture||!backMaterial||!edgeMaterial)throw new InvalidOperationException("Incomplete classic deck.");
        }
        public FruitCardDefinition Find(FruitKind fruit,int count)
        {
            foreach(var c in definitions)if(c&&c.fruit==fruit&&c.fruitCount==count)return c;
            throw new ArgumentException("Card definition is absent: "+fruit+" "+count);
        }
        public List<FruitCardInstance> CreateOrdered()
        {
            Validate();var cards=new List<FruitCardInstance>(TotalCards);
            foreach(FruitKind fruit in Enum.GetValues(typeof(FruitKind)))for(int count=1;count<=5;count++){
                var definition=Find(fruit,count);for(int n=0;n<definition.copiesInDeck;n++)cards.Add(new FruitCardInstance(cards.Count,definition));
            }
            return cards;
        }
        public List<FruitCardInstance> CreateShuffled(int seed)
        {
            var cards=CreateOrdered();var random=new System.Random(seed);
            for(int i=cards.Count-1;i>0;i--){int j=random.Next(i+1);var swap=cards[i];cards[i]=cards[j];cards[j]=swap;}
            return cards;
        }
        // Deal every card in turn. 3, 5 and 6 seats intentionally receive uneven piles.
        public List<FruitCardInstance>[] Deal(int playerCount,int seed,int firstSeat=0)
        {
            if(playerCount<2||playerCount>6)throw new ArgumentOutOfRangeException(nameof(playerCount));
            if(firstSeat<0||firstSeat>=playerCount)throw new ArgumentOutOfRangeException(nameof(firstSeat));
            var piles=new List<FruitCardInstance>[playerCount];for(int i=0;i<playerCount;i++)piles[i]=new List<FruitCardInstance>();
            var cards=CreateShuffled(seed);for(int i=0;i<cards.Count;i++)piles[(firstSeat+i)%playerCount].Add(cards[i]);
            return piles;
        }
    }
}
