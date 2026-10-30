using HalliGalli.Cards;
using UnityEngine;

namespace HalliGalli.Movement
{
    public sealed class CourseCardGame:MonoBehaviour
    {
        public CourseSession session;
        public ClassicFruitDeck deck;
        public CardTableDisplay display;
        public CourseTableView tableView;
        public bool shuffleOnReset=true,botEnabled=true;
        public int seed=20261020;
        [Min(.25f)] public float botRevealDelay=1.2f,botStandDelay=.9f;
        public HalliGalliMatch Match{get;private set;}
        public float ReadyAt{get;private set;}
        public float LastClaimTime{get;private set;}=-100;
        public bool ReadInput{get;set;}=true;
        public void ApplyNetworkState(HalliGalliMatch.PublicState state)
        {
            int reveals=Match?.RevealSequence??-1;int collected=Match?.LastCollectedCount??0;
            Match=HalliGalliMatch.FromPublicState(state,deck);
            display.Initialize(Match);shownRevision=Match.Revision;ReadyAt=0;
            if(collected!=state.collected)LastClaimTime=Time.time;
            if(tableView&&session.Local.IsSeated&&(reveals!=state.reveals||collected!=state.collected))tableView.Focus();
        }
        int shownRevision=-1,botRevision=-1,matchNumber;float botSince;
        public int Index(CourseParticipant p)=>System.Array.IndexOf(session.participants,p);
        public void ResetMatch()
        {
            if(tableView)tableView.ClearFocus();LastClaimTime=-100;
            int matchSeed=shuffleOnReset?System.Environment.TickCount^(++matchNumber*997):seed;
            Match=new HalliGalliMatch(deck.Deal(session.participants.Length,matchSeed));display.Initialize(Match);ReadyAt=Time.time+.35f;shownRevision=Match.Revision;botRevision=-1;
        }
        public bool CanReveal(CourseParticipant p)=>Match!=null&&Time.time>=ReadyAt&&session.EveryoneSeated&&Match.CanReveal(Index(p));
        public bool Reveal(CourseParticipant p)
        {
            if(!CanReveal(p))return false;bool success=Match.Reveal(Index(p));if(success){ReadyAt=Time.time+.18f;Refresh();if(tableView)tableView.Focus();}return success;
        }
        public bool CanStand(CourseParticipant p)=>Match!=null&&Match.CanStand(Index(p));
        public void Stand(CourseParticipant p){Match.Stand(Index(p));if(p.localPlayer&&tableView)tableView.ClearFocus();Refresh();}
        public void Sit(CourseParticipant p){if(Match==null)return;bool wasPaused=Match.Paused;Match.Sit(Index(p));if(wasPaused&&!Match.Paused)ReadyAt=Time.time+.35f;Refresh();}
        public bool MustReturn(CourseParticipant p){if(Match==null)return false;var seat=Match.Seats[Index(p)];return seat.MustSit||seat.Eliminated||Match.Claimed||Match.Finished;}
        public bool Claim(CourseParticipant p){if(Match==null||!Match.Claim(Index(p)))return false;LastClaimTime=Time.time;Refresh();return true;}
        void Refresh(){if(display&&Match!=null){display.Refresh(Match);shownRevision=Match.Revision;}}
        void LateUpdate()
        {
            if(Match==null)return;if(shownRevision!=Match.Revision)Refresh();
            if(!botEnabled||Match.Finished)return;
            if(botRevision!=Match.Revision){botRevision=Match.Revision;botSince=Time.time;}
            foreach(var p in session.participants){
                if(p.localPlayer||!p.IsSeated)continue;var rival=p.GetComponent<CourseRival>();if(!rival||!rival.enabled||!rival.autoDepart)continue;
                if(Match.CanStand(Index(p))&&Match.HasExactFive()){
                    if(Time.time-botSince>=botStandDelay)p.Stand();
                    // React to the exposed cards before covering them with the bot's next card.
                }else if(Time.time-botSince>=botRevealDelay&&CanReveal(p)){Reveal(p);return;}
            }
        }
    }
}
