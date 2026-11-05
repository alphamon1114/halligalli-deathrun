using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace HalliGalli.Movement
{
    [DefaultExecutionOrder(-100)]
    public sealed partial class CourseHUD:MonoBehaviour
    {
        public CourseSession session;
        [System.NonSerialized] public bool ReadInput=true;
        Rect RoomPanel=>new Rect(BellUI.W-342,88,318,Mathf.Min(HelpOpen?584:CourseNetwork.Active?426:session&&session.practiceRoom?568:544,BellUI.H-112));
        public bool HelpOpen{get;private set;}
        public void OpenHelp(){ControlSettings.Open();}
        public bool MapMenuOpen{get;private set;}
        bool wasLassoCaught;float lassoCaughtAt=-100;
        int shownWinner=-1;float victoryShownAt=-100;
        public bool MatchFinished=>session&&session.HasCardRules&&session.cardGame.Match!=null&&session.cardGame.Match.Finished;
        public bool VictoryVisible=>MatchFinished&&(!CourseNetwork.Active||CourseNetwork.Instance.Playing)&&!(CourseNetwork.Instance&&CourseNetwork.Instance.OverlayVisible);
        public string WinnerNickname
        {
            get{
                if(!MatchFinished)return "";
                int seat=session.cardGame.Match.Winner;
                if(CourseNetwork.Active)return CourseNetwork.Instance.PlayerName(seat);
                return session.participants[seat].localPlayer?(CourseNetwork.Instance?CourseNetwork.Instance.LocalNickname:"플레이어"):SeatName(seat);
            }
        }
        public string VictoryAction=>!CourseNetwork.Active?"다시하기":CourseNetwork.Instance.VictoryReturnRemaining>0?Mathf.CeilToInt(CourseNetwork.Instance.VictoryReturnRemaining)+"초 후 대기방으로":"대기방으로 돌아가는 중";
        public bool CanContinueAfterVictory=>!CourseNetwork.Active;
        public bool LassoCaught
        {
            get{
                if(!session||!session.Local||session.Local.IsSeated||session.Local.IsRespawning)return false;
                foreach(var other in session.participants)
                    if(other&&other!=session.Local&&other.isActiveAndEnabled&&!other.IsSeated&&!other.IsRespawning&&other.Lasso&&other.Lasso.Phase==LassoPhase.Latched&&other.Lasso.Target==session.Local)return true;
                return false;
            }
        }
        void LateUpdate()
        {
            int winner=MatchFinished?session.cardGame.Match.Winner:-1;
            if(winner!=shownWinner){
                shownWinner=winner;victoryShownAt=Time.unscaledTime;
                if(winner>=0){HelpOpen=false;MapMenuOpen=false;}
            }
            UpdateVictoryAudio();
            bool caught=LassoCaught;
            if(caught&&!wasLassoCaught)lassoCaughtAt=Time.unscaledTime;
            wasLassoCaught=caught;
        }
        public void OpenMapMenu(){MapMenuOpen=true;session.Local.GetComponent<SourcePlayerController>().SetCursorCaptured(false);}
        public void CloseMapMenu(){MapMenuOpen=false;}
        void Update()
        {
            if(ControlSettings.BlocksInput||CourseNetwork.CapturesPointer)return;
            if(session.roster||session.practiceRoom){
#if ENABLE_INPUT_SYSTEM
                Vector2 mouse=Mouse.current!=null?Mouse.current.position.ReadValue():new Vector2(-1,-1);
#else
                Vector2 mouse=Input.mousePosition;
#endif
                var point=new Vector2(mouse.x,Screen.height-mouse.y)/BellUI.Scale;
                session.Local.GetComponent<SourcePlayerController>().suppressCursorCapture=MapMenuOpen||(Cursor.lockState!=CursorLockMode.Locked&&(RoomPanel.Contains(point)||PracticeSurfActive&&PracticeInfoPanel.Contains(point)));
            }
            if(!ReadInput)return;
            if(MapMenuOpen){
#if ENABLE_INPUT_SYSTEM
                if(Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame)CloseMapMenu();
#else
                if(Input.GetKeyDown(KeyCode.Escape))CloseMapMenu();
#endif
                return;
            }
            if(!session.Local.GetComponent<SourcePlayerController>().PointerActionsAllowed || Time.timeScale<=0)return;
            if(session.HasCardRules&&!session.cardGame.ReadInput)return;
            if(Input.GetKeyDown(KeyCode.Escape))return;
            bool interact=ControlBindings.Pressed(GameAction.Interact),reset=ControlBindings.Pressed(GameAction.Respawn);
            bool reveal=ControlBindings.Pressed(GameAction.Primary);
            ProcessActions(interact,reveal,reset);
            if(!interact&&!reset&&ControlBindings.Pressed(GameAction.UseItem))UseRaceItem();
        }
        public void ProcessActions(bool interact,bool reveal,bool reset)
        {
            if(CourseNetwork.Active&&MatchFinished)return;
            reveal=reveal&&session.Local.IsSeated;
            if(CourseNetwork.Active){CourseNetwork.Instance.LocalAction(interact?NetworkAction.Interact:reset?(session.HasCardRules&&session.cardGame.Match.Finished?NetworkAction.Restart:NetworkAction.Respawn):NetworkAction.Reveal,interact||reset||reveal);return;}
            if(MapMenuOpen||session.maps&&session.maps.Loading)return;
            if(reset){if(session.HasCardRules&&session.cardGame.Match.Finished)session.ResetPractice();else session.Local.RequestRespawn();return;}
            // Stand/sit always wins over a simultaneous reveal press.
            if(interact){Interact();return;}
            if(reveal&&session.HasCardRules)session.cardGame.Reveal(session.Local);
        }
        public void Interact()
        {
            if(session.practiceRoom)return;
            var p=session.Local;if(p.IsRespawning)return;
            if(p.IsSeated)p.Stand();else if(session.CanRing(p))session.Ring(p);else if(p.CanSit)p.Sit();
        }
        public void UseRaceItem()
        {
            if(!RaceCourse.Active||RaceCourse.Active.session!=session||MapMenuOpen||MatchFinished||
                ControlSettings.BlocksInput||CourseNetwork.CapturesPointer||session.maps&&session.maps.Loading)return;
            var actor=session.Local;
            var aim=actor.GetComponent<SourcePlayerController>().AimRotation*Vector3.forward;
            if(CourseNetwork.Active)CourseNetwork.Instance.LocalAction(NetworkAction.UseItem,aim:aim);
            else RaceCourse.Active.UseItem(actor,aim);
        }
        string SeatName(int seat)=>CourseNetwork.Active?CourseNetwork.Instance.SeatName(seat):session.participants[seat].localPlayer?"나 (1번)":"AI "+(seat+1);
        public string StatusText
        {
            get{
                var p=session.Local;
                if(!session.HasCardRules)return session.BellRung?"종을 울렸어! 모두 자기 자리로 돌아가 앉자.":p.IsSeated?ControlBindings.Hint(GameAction.Interact)+" 키로 일어나면 상대도 출발해.":"발판을 건너 종까지 달려가자.";
                var game=session.cardGame;var match=game.Match;var mine=match.Seats[game.Index(p)];
                if(match.Finished)return "승자 · "+WinnerNickname+" · "+(CanContinueAfterVictory?ControlBindings.Hint(GameAction.Respawn)+" · ":"")+VictoryAction;
                if(mine.MustSit)return "잘못 기립 · "+mine.LastPenaltyPaid+"장 지급 · 자기 자리에서 "+ControlBindings.Hint(GameAction.Interact)+"로 다시 앉기";
                if(match.Paused)return "카드 공개 중지 · 모두 자기 자리에 앉으면 재개";
                if(Time.time-game.LastClaimTime<1.5f)return SeatName(match.LastCollector)+" 공개 카드 "+match.LastCollectedCount+"장 획득 · "+(match.Turn==game.Index(p)?"내 차례":"상대 차례");
                if(mine.Eliminated)return "탈락 · 남은 참가자가 진행 중이야";
                if(match.Turn<0)return "뒤집을 카드가 없어 · 공개된 카드를 확인해";
                return match.Turn==game.Index(p)?"내 차례 · "+ControlBindings.Hint(GameAction.Primary)+"으로 한 장 공개":SeatName(match.Turn)+" 차례 · 카드를 보고 직접 판단해";
            }
        }
        public string ActionText
        {
            get{
                if(MatchFinished)return (CanContinueAfterVictory?"[ "+ControlBindings.Hint(GameAction.Respawn)+" ]  ":"")+VictoryAction;
                var p=session.Local;if(p.IsRespawning)return p.HasCheckpoint?"추락! 체크포인트에서 다시 출발…":"추락! 출발 지점으로 돌아가는 중…";
                if(p.Motor.Stunned)return "마비 · 이동·공격·종 누르기 불가";
                if(session.HasCardRules){var s=session.cardGame.Match.Seats[session.cardGame.Index(p)];
                    if(s.MustSit||(!p.IsSeated&&s.Eliminated))return p.CanSit?"[ "+ControlBindings.Hint(GameAction.Interact)+" ]  다시 앉기":"자기 자리로 돌아가서 [ "+ControlBindings.Hint(GameAction.Interact)+" ]";
                    if(s.Eliminated)return "";
                    if(p.IsSeated)return session.cardGame.CanReveal(p)?"[ "+ControlBindings.Hint(GameAction.Primary)+" ] 공개    [ "+ControlBindings.Hint(GameAction.Interact)+" ] 일어나기":"[ "+ControlBindings.Hint(GameAction.Interact)+" ]  일어나기";
                }
                return p.IsSeated?"[ "+ControlBindings.Hint(GameAction.Interact)+" ]  일어나기":session.CanRing(p)?"[ "+ControlBindings.Hint(GameAction.Interact)+" ]  종 울리기":p.CanSit?"[ "+ControlBindings.Hint(GameAction.Interact)+" ]  내 자리에 앉기":"";
            }
        }
    }
}
