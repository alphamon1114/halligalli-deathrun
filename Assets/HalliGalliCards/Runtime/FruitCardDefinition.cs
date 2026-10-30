using UnityEngine;

namespace HalliGalli.Cards
{
    public enum FruitKind { Strawberry, Banana, Lime, Plum }

    [CreateAssetMenu(menuName="HalliGalli/Cards/Card definition")]
    public sealed class FruitCardDefinition:ScriptableObject
    {
        public string cardId;
        public FruitKind fruit;
        [Range(1,5)] public int fruitCount=1;
        [Min(1)] public int copiesInDeck=5;
        public Texture2D faceTexture;
        public Material faceMaterial;
    }
}
