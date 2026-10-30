using System;
using System.Collections.Generic;
using System.Linq;

namespace HalliGalli.Cards
{
    public enum StandResult { Blocked, Valid, Penalized }

    // Owns the cards and turn state. Movement and UI never mutate these piles directly.
    public sealed class HalliGalliMatch
    {
        public sealed class Seat
        {
            internal readonly List<FruitCardInstance> draw=new List<FruitCardInstance>(),open=new List<FruitCardInstance>();
            public IReadOnlyList<FruitCardInstance> Draw=>draw;
            public IReadOnlyList<FruitCardInstance> Open=>open;
            public bool Seated{get;internal set;}=true;
            public bool MustSit{get;internal set;}
            public bool Eliminated{get;internal set;}
            public int LastPenaltyPaid{get;internal set;}
            internal bool eligible;
            public FruitCardInstance Top=>open.Count==0?null:open[open.Count-1];
        }
        readonly Seat[] seats;
        bool frozenFive;
        public IReadOnlyList<Seat> Seats=>seats;
        public int Turn{get;private set;}
        public int Revision{get;private set;}
        public int RevealSequence{get;private set;}
        public int Winner{get;private set;}=-1;
        public int LastCollector{get;private set;}=-1;
        public int LastCollectedCount{get;private set;}
        public bool Finished=>Winner>=0;
        public bool Paused{get;private set;}
        public bool Claimed{get;private set;}
        public bool EveryoneSeated=>seats.All(s=>s.Seated);
        public HalliGalliMatch(IReadOnlyList<FruitCardInstance>[] hands,int firstTurn=0)
        {
            if(hands==null||hands.Length<2||hands.Length>6)throw new ArgumentException("A match needs 2 to 6 seats.");
            if(firstTurn<0||firstTurn>=hands.Length)throw new ArgumentOutOfRangeException(nameof(firstTurn));
            seats=hands.Select(_=>new Seat()).ToArray();var ids=new HashSet<int>();
            for(int i=0;i<hands.Length;i++)foreach(var card in hands[i]){if(card==null||!card.Definition||!ids.Add(card.Serial))throw new ArgumentException("Missing or repeated card instance.");seats[i].draw.Add(card);}
            Turn=NextWithCards(firstTurn);
        }
        bool ValidSeat(int seat)=>seat>=0&&seat<seats.Length;
        int NextWithCards(int from){for(int n=0;n<seats.Length;n++){int i=(from+n)%seats.Length;if(!seats[i].Eliminated&&seats[i].draw.Count>0)return i;}return -1;}
        public bool HasExactFive()
        {
            int[] totals=new int[4];foreach(var seat in seats)if(seat.Top!=null)totals[(int)seat.Top.Definition.fruit]+=seat.Top.Definition.fruitCount;
            return totals.Any(n=>n==5);
        }
        public bool CanReveal(int seat)=>ValidSeat(seat)&&!Finished&&!Paused&&EveryoneSeated&&Turn==seat&&!seats[seat].Eliminated&&seats[seat].draw.Count>0;
        public bool Reveal(int seat)
        {
            if(!CanReveal(seat))return false;
            var s=seats[seat];s.open.Add(s.draw[0]);s.draw.RemoveAt(0);RevealSequence++;Turn=NextWithCards((seat+1)%seats.Length);
            if(seats.Length==2&&s.draw.Count==0){s.Eliminated=true;CheckWinner();}
            Revision++;return true;
        }
        public bool CanStand(int seat)=>ValidSeat(seat)&&!Finished&&!Claimed&&seats[seat].Seated&&!seats[seat].Eliminated;
        public StandResult Stand(int seat)
        {
            if(!CanStand(seat))return StandResult.Blocked;
            if(!Paused)frozenFive=HasExactFive();
            // Freeze before the penalty or any notification can advance another turn.
            Paused=true;var s=seats[seat];s.Seated=false;s.LastPenaltyPaid=0;s.eligible=frozenFive;s.MustSit=!frozenFive;
            if(!frozenFive){
                var recipients=Enumerable.Range(1,seats.Length-1).Select(n=>(seat+n)%seats.Length).Where(i=>!seats[i].Eliminated).ToArray();
                bool insufficient=s.draw.Count<recipients.Length;
                foreach(int other in recipients){if(s.draw.Count==0)break;seats[other].draw.Add(s.draw[0]);s.draw.RemoveAt(0);s.LastPenaltyPaid++;}
                if(insufficient||(s.draw.Count==0&&(seats.Length==2||s.open.Count==0)))s.Eliminated=true;
                Turn=NextWithCards(Math.Max(0,Turn));CheckWinner();
            }
            Revision++;return frozenFive?StandResult.Valid:StandResult.Penalized;
        }
        public bool Sit(int seat)
        {
            if(!ValidSeat(seat)||seats[seat].Seated)return false;
            var s=seats[seat];s.Seated=true;s.MustSit=false;s.eligible=false;
            if(EveryoneSeated){Paused=Finished;Claimed=false;frozenFive=false;}
            Revision++;return true;
        }
        public bool CanClaim(int seat)=>ValidSeat(seat)&&Paused&&!Finished&&!Claimed&&frozenFive&&!seats[seat].Seated&&seats[seat].eligible&&!seats[seat].MustSit&&!seats[seat].Eliminated;
        public bool Claim(int seat)
        {
            if(!CanClaim(seat))return false;
            LastCollectedCount=seats.Sum(s=>s.open.Count);LastCollector=seat;
            foreach(var s in seats){seats[seat].draw.AddRange(s.open);s.open.Clear();s.eligible=false;}
            Claimed=true;Turn=seat;
            foreach(var s in seats)if(s.draw.Count==0&&s.open.Count==0)s.Eliminated=true;
            CheckWinner();Revision++;return true;
        }
        void CheckWinner()
        {
            var active=Enumerable.Range(0,seats.Length).Where(i=>!seats[i].Eliminated).ToArray();if(active.Length!=1)return;
            Winner=active[0];var winner=seats[Winner];
            // Continue to a single owner, as requested for this deathrun variant.
            foreach(var s in seats){if(s!=winner){winner.draw.AddRange(s.draw);s.draw.Clear();}winner.draw.AddRange(s.open);s.open.Clear();}
            Paused=true;Turn=-1;
        }
        public IEnumerable<FruitCardInstance> AllCards()=>seats.SelectMany(s=>s.draw.Concat(s.open));

        [Serializable] public sealed class PublicSeat
        {
            public int drawCount,penalty;
            public int[] open;
            public bool seated,mustSit,eliminated,eligible;
        }
        [Serializable] public sealed class PublicState
        {
            public PublicSeat[] seats;
            public int turn,revision,reveals,winner,collector,collected;
            public bool paused,claimed,five;
        }
        // Only counts and exposed faces cross the network. The shuffle and hidden faces stay on the host.
        public PublicState ExportPublicState(ClassicFruitDeck deck)=>new PublicState{
            seats=seats.Select(s=>new PublicSeat{drawCount=s.draw.Count,open=s.open.Select(c=>Array.IndexOf(deck.definitions,c.Definition)).ToArray(),
                seated=s.Seated,mustSit=s.MustSit,eliminated=s.Eliminated,eligible=s.eligible,penalty=s.LastPenaltyPaid}).ToArray(),
            turn=Turn,revision=Revision,reveals=RevealSequence,winner=Winner,collector=LastCollector,collected=LastCollectedCount,
            paused=Paused,claimed=Claimed,five=frozenFive
        };
        public static HalliGalliMatch FromPublicState(PublicState state,ClassicFruitDeck deck)
        {
            if(state?.seats==null||state.seats.Length<2||state.seats.Length>6)throw new ArgumentException("Invalid public seats.");
            int serial=0;
            var hands=state.seats.Select(s=>{
                if(s.drawCount<0||s.drawCount>56||s.open==null||s.open.Length>56)throw new ArgumentException("Invalid pile.");
                return (IReadOnlyList<FruitCardInstance>)Enumerable.Range(0,s.drawCount).Select(_=>new FruitCardInstance(serial++,deck.definitions[0])).ToArray();
            }).ToArray();
            var result=new HalliGalliMatch(hands);
            for(int i=0;i<state.seats.Length;i++){
                var src=state.seats[i];var dst=result.seats[i];
                foreach(int face in src.open){if(face<0||face>=deck.definitions.Length)throw new ArgumentException("Invalid face.");dst.open.Add(new FruitCardInstance(serial++,deck.definitions[face]));}
                dst.Seated=src.seated;dst.MustSit=src.mustSit;dst.Eliminated=src.eliminated;dst.eligible=src.eligible;dst.LastPenaltyPaid=src.penalty;
            }
            if(serial!=ClassicFruitDeck.TotalCards)throw new ArgumentException("Card conservation failed.");
            result.Turn=state.turn;result.Revision=state.revision;result.RevealSequence=state.reveals;result.Winner=state.winner;
            result.LastCollector=state.collector;result.LastCollectedCount=state.collected;result.Paused=state.paused;
            result.Claimed=state.claimed;result.frozenFive=state.five;return result;
        }
    }
}
