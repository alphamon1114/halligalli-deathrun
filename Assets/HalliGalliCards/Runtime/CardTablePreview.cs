using UnityEngine;

namespace HalliGalli.Cards
{
    // Asset preview only; the course's departure/bell events do not yet resolve a card match.
    public sealed class CardTablePreview:MonoBehaviour
    {
        public ClassicFruitDeck deck;
        public FruitCardView[] openCards;
        public int[] faceDownCounts={27,27};
        public const string Purpose="Art preview: two example open cards plus 54 face-down cards. Not a live match.";
    }
}
