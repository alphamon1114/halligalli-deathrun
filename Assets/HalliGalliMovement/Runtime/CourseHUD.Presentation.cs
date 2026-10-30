using UnityEngine;

namespace HalliGalli.Movement
{
    public sealed partial class CourseHUD
    {
        void OnGUI()
        {
            if(ControlSettings.IsOpen||!session||!session.Local||CourseNetwork.Instance&&CourseNetwork.Instance.OverlayVisible)return;
            var matrix=GUI.matrix;var color=GUI.color;int depth=GUI.depth;GUI.depth=0;BellUI.Begin();
            if(VictoryVisible){
                DrawVictoryBanner();
                BellUI.Key(new Rect(BellUI.W-58,25,34,25),"ESC");
                if(Cursor.lockState!=CursorLockMode.Locked)DrawPauseMenu();
                GUI.matrix=matrix;GUI.color=color;GUI.depth=depth;return;
            }
            var p=session.Local;float w=BellUI.W,h=BellUI.H;
            bool table=session.HasCardRules&&session.cardGame.tableView&&session.cardGame.tableView.Focused;
            BellUI.Icon("bell",new Rect(24,24,22,22),BellUI.Gold);
            BellUI.Text(new Rect(55,21,300,28),session.practiceRoom?"연습실":session.maps&&session.maps.current?session.maps.current.displayName:"BELL RUN",17,BellUI.Paper,true,shadow:true);
            if(p.HasCheckpoint){BellUI.Icon("check",new Rect(24,56,17,17),BellUI.Cool);BellUI.Text(new Rect(47,51,130,26),"체크포인트",12,BellUI.Muted,shadow:true);}
            BellUI.Key(new Rect(w-58,25,34,25),"ESC");
            if(!MapMenuOpen){
                DrawMatchState();
                if(!p.IsSeated&&!p.IsRespawning){
                    if(Cursor.lockState==CursorLockMode.Locked)DrawTools();
                    DrawRaceItems();
                    if(session.roster&&session.roster.surf){
                        BellUI.Text(new Rect(26,h-73,85,35),p.Motor.HorizontalSpeed.ToString("F1"),25,BellUI.Paper,true,shadow:true);
                        BellUI.Text(new Rect(102,h-66,40,25),"m/s",12,BellUI.Muted,shadow:true);
                        for(int i=0;i<session.roster.surf.sections.Length;i++)BellUI.Round(new Rect(28+i*17,h-29,6,6),session.roster.surf.GatesPassed(p)>i?BellUI.Gold:new Color(.65f,.67f,.61f,.4f),3);
                    }
                }
                if(!table&&!p.IsSeated&&!p.IsRespawning)DrawCrosshair();
                if(LassoCaught&&!table)DrawLassoWarning();
                if(Cursor.lockState!=CursorLockMode.Locked)DrawPauseMenu();
            }else DrawMapMenu();
            GUI.matrix=matrix;GUI.color=color;GUI.depth=depth;
        }
        void DrawMatchState()
        {
            var p=session.Local;float w=BellUI.W,h=BellUI.H;
            if(p.IsRespawning){Badge(new Rect(w*.5f-82,h*.5f+60,164,38),p.HasCheckpoint?"체크포인트 복귀":"다시 출발",BellUI.Paper,"arrow");return;}
            if(session.practiceRoom){float first=Prompt(24,h-63,"F","발차기",120);Prompt(36+first,h-63,"R","제자리로",142);return;}
            if(session.HasCardRules){
                var game=session.cardGame;var match=game.Match;var mine=match.Seats[game.Index(p)];
                if(mine.MustSit){
                    Badge(new Rect(w*.5f-123,26,246,40),"잘못 기립  ·  "+mine.LastPenaltyPaid+"장 지급",BellUI.Danger,"card");
                    if(p.CanSit)Prompt(w*.5f-76,h-67,"E","다시 앉기",152);
                    else BellUI.Text(new Rect(w*.5f-140,h-62,280,32),"자리로 돌아가기",15,BellUI.Paper,false,TextAnchor.MiddleCenter,true);
                    return;
                }
                if(mine.Eliminated){Badge(new Rect(w*.5f-62,26,124,36),"관전 중",BellUI.Muted);return;}
                if(Time.time-game.LastClaimTime<1.5f){CentreBadge(SeatName(match.LastCollector)+"  +"+match.LastCollectedCount+"장",BellUI.Gold,"card");}
                else if(p.IsSeated){
                    string status=match.Paused?"공개 중지":match.Turn==game.Index(p)?"내 차례":match.Turn>=0?SeatName(match.Turn)+" 차례":"공개 대기";
                    CentreBadge(status,match.Paused?BellUI.Muted:match.Turn==game.Index(p)?BellUI.Gold:BellUI.Paper,match.Paused?"pause":"card");
                }
                if(p.IsSeated){
                    float x=24;
                    if(game.CanReveal(p))x+=Prompt(x,h-63,"LMB","공개",116)+12;
                    Prompt(x,h-63,"E","일어나기",140);return;
                }
            }else if(p.IsSeated){Prompt(24,h-63,"E","일어나기",140);return;}
            if(p.Motor.Stunned){
                Badge(new Rect(w*.5f-59,h*.5f+55,118,34),"마비",BellUI.Cool);
                BellUI.Arc(new Vector2(w*.5f,h*.5f),30,p.Motor.StunRemaining/2,BellUI.Cool,2);
            }else if(session.CanRing(p))Prompt(w*.5f-74,h-67,"E","종 울리기",148);
            else if(p.CanSit)Prompt(w*.5f-64,h-67,"E","앉기",128);
        }
        static void Badge(Rect rect,string text,Color color,string icon=null)
        {
            BellUI.Round(rect,new Color(.065f,.07f,.06f,.8f),rect.height*.5f);
            if(icon!=null){BellUI.Icon(icon,new Rect(rect.x+13,rect.y+9,rect.height-18,rect.height-18),color);rect.x+=26;rect.width-=26;}
            BellUI.Text(rect,text,15,color,false,TextAnchor.MiddleCenter);
        }
        static void CentreBadge(string text,Color color,string icon)
        {
            float width=Mathf.Clamp(BellUI.Style(15).CalcSize(new GUIContent(text)).x+58,126,430);
            Badge(new Rect((BellUI.W-width)*.5f,26,width,36),text,color,icon);
        }
        static float Prompt(float x,float y,string key,string text,float width)
        {
            key=BoundKey(key);
            float keyWidth=Mathf.Max(27,BellUI.Style(12).CalcSize(new GUIContent(key)).x+12);
            width=Mathf.Max(width,keyWidth+BellUI.Style(15).CalcSize(new GUIContent(text)).x+35);
            BellUI.Round(new Rect(x,y,width,39),new Color(.065f,.07f,.06f,.78f),20);
            BellUI.Key(new Rect(x+7,y+7,keyWidth,25),key);
            BellUI.Text(new Rect(x+keyWidth+16,y,width-keyWidth-20,39),text,15,BellUI.Paper);
            return width;
        }
        static string BoundKey(string key){switch(key){case "E":return ControlBindings.Hint(GameAction.Interact);case "F":return ControlBindings.Hint(GameAction.Kick);case "Q":return ControlBindings.Hint(GameAction.Lasso);case "R":return ControlBindings.Hint(GameAction.Respawn);case "LMB":return ControlBindings.Hint(GameAction.Primary);default:return key;}}
        void DrawTools()
        {
            var p=session.Local;float y=BellUI.H-70,x=BellUI.W-56;
            bool attached=p.Lasso.Phase==LassoPhase.Latched;float cooldown=p.Lasso.CooldownRemaining;
            Skill(x,y,"rope",attached?BellUI.Gold:cooldown>0?BellUI.Muted:BellUI.Paper,"Q",cooldown>0&&!attached?cooldown.ToString("F1"):null,attached?1:1-cooldown/5);
            if(attached)BellUI.Text(new Rect(x-35,y-57,70,22),"당기기",12,BellUI.Gold,true,TextAnchor.MiddleCenter,true);
        }
        static void Skill(float x,float y,string icon,Color color,string key,string value,float fraction)
        {
            BellUI.Round(new Rect(x-25,y-25,50,50),new Color(.06f,.065f,.055f,.7f),25);
            BellUI.Round(new Rect(x-27,y-27,54,54),new Color(.6f,.62f,.56f,.23f),27,2);
            if(fraction>=.999f)BellUI.Round(new Rect(x-27,y-27,54,54),new Color(color.r,color.g,color.b,.45f),27,2);
            else if(fraction>.001f)BellUI.Arc(new Vector2(x,y),27,Mathf.Clamp01(fraction),BellUI.Gold,2);
            if(value==null)BellUI.Icon(icon,new Rect(x-15,y-15,30,30),color);
            else BellUI.Text(new Rect(x-25,y-25,50,50),value,18,color,true,TextAnchor.MiddleCenter);
            key=BoundKey(key);float width=Mathf.Max(36,BellUI.Style(12).CalcSize(new GUIContent(key)).x+14);
            BellUI.Key(new Rect(x-width/2,y+33,width,22),key);
        }
        void DrawCrosshair()
        {
            var p=session.Local;float x=BellUI.W*.5f,y=BellUI.H*.5f;
            Color c=p.Combat.HitMarkerRemaining>0||p.Lasso.Phase==LassoPhase.Latched?BellUI.Gold:BellUI.Paper;
            BellUI.Round(new Rect(x-3,y-3,6,6),new Color(0,0,0,.65f),3);
            BellUI.Round(new Rect(x-1.5f,y-1.5f,3,3),c,2);
            if(p.Lasso.Phase==LassoPhase.Latched)BellUI.Arc(new Vector2(x,y),9,1,BellUI.Gold,2);
        }
        void DrawLassoWarning()
        {
            float age=Mathf.Max(0,Time.unscaledTime-lassoCaughtAt),scale=1+.35f*(1-Mathf.SmoothStep(0,1,age/.18f));
            Color c=Color.Lerp(BellUI.Paper,BellUI.Danger,Mathf.Clamp01(age/.14f));Vector2 centre=new Vector2(BellUI.W*.5f,BellUI.H*.5f);
            for(int i=0;i<4;i++){
                float a=(45+i*90)*Mathf.Deg2Rad;Vector2 d=new Vector2(Mathf.Cos(a),Mathf.Sin(a));
                BellUI.Line(centre+d*9*scale,centre+d*28*scale,new Color(.06f,.01f,.01f,.9f),7*scale);
                BellUI.Line(centre+d*10*scale,centre+d*27*scale,c,4*scale);
            }
            BellUI.Text(new Rect(centre.x-85,centre.y+37,170,28),"밧줄에 걸림",14,c,true,TextAnchor.MiddleCenter,true);
        }
        void Resume()
        {
            HelpOpen=false;MapMenuOpen=false;var controller=session.Local.GetComponent<SourcePlayerController>();controller.suppressCursorCapture=false;controller.SetCursorCaptured(true);
        }
        void DrawPauseMenu()
        {
            var r=RoomPanel;BellUI.Round(r,BellUI.Surface,26);float x=r.x+24,y=r.y+22,width=r.width-48;
            BellUI.Text(new Rect(x,y,width,32),"BELL RUN",23,BellUI.Paper,true);y+=51;
            if(BellUI.Button(new Rect(x,y,width,43),"계속하기",true)&&ReadInput)Resume();y+=59;
            if(BellUI.Button(new Rect(x,y,128,34),"게임",selected:!HelpOpen,size:14)&&ReadInput)HelpOpen=false;
            if(BellUI.Button(new Rect(x+140,y,128,34),"조작키 설정",selected:HelpOpen,size:14)&&ReadInput)OpenHelp();y+=54;
            if(HelpOpen){
                string[] keys={"WASD","SPACE","CTRL","LMB","F","Q","R","E"};
                string[] actions={"이동","점프","웅크리기","주먹 / 카드 공개","발차기","밧줄 · 다시 눌러 당기기","체크포인트 복귀","일어나기 · 앉기 · 종"};
                for(int i=0;i<keys.Length;i++){BellUI.Key(new Rect(x,y+i*37,54,25),keys[i]);BellUI.Text(new Rect(x+66,y-2+i*37,205,29),actions[i],14);}
            }else{
                if(!CourseNetwork.Active&&session.roster&&!session.practiceRoom){
                    BellUI.Text(new Rect(x,y,width,25),"연습 인원",14,BellUI.Muted);y+=32;
                    for(int count=2;count<=6;count++)if(BellUI.Button(new Rect(x+(count-2)*55,y,48,38),count.ToString(),selected:session.roster.PlayerCount==count)&&ReadInput)session.roster.StartNewGame(count);
                    y+=59;
                    if(BellUI.Button(new Rect(x,y,width,43),"맵 선택")&&ReadInput)OpenMapMenu();y+=55;
                }
                if(CourseNetwork.Instance&&BellUI.Button(new Rect(x,y,width,43),CourseNetwork.Active?"대기방":"시작 화면")&&ReadInput)CourseNetwork.Instance.ShowMenu();y+=55;
                if(!CourseNetwork.Active&&!session.practiceRoom&&BellUI.Button(new Rect(x,y,width,43),"새 게임")&&ReadInput){session.ResetPractice();Resume();}
            }
        }
        void DrawMapMenu()
        {
            float w=BellUI.W,h=BellUI.H,x=(w-1100)*.5f,y=(h-640)*.5f;
            BellUI.Round(new Rect(0,0,w,h),new Color(.035f,.04f,.03f,.92f),0);
            BellUI.Text(new Rect(x,y,700,48),"어디서 겨룰까?",32,BellUI.Paper,true);
            BellUI.Text(new Rect(x,y+51,500,28),session.roster.PlayerCount+"인 · 새로운 대결",15,BellUI.Muted);
            if(BellUI.Button(new Rect(x+990,y+4,110,38),"닫기")&&ReadInput)CloseMapMenu();
            var maps=session.maps.catalog.maps;int index=0;
            foreach(var map in maps){
                if(!map||!map.playable)continue;float cx=x+(index%3)*368,cy=y+98+(index/3)*244;index++;
                var tile=new Rect(cx,cy,352,232);BellUI.Round(tile,BellUI.Surface,24);
                BellUI.MapPicture(new Rect(cx+10,cy+10,332,94),map,16);
                string icon=map.style==CourseMapStyle.Climb?"mountain":map.style==CourseMapStyle.Surf?"surf":"jump";
                BellUI.Icon(icon,new Rect(cx+20,cy+118,22,22),BellUI.Gold);
                BellUI.Text(new Rect(cx+55,cy+109,277,38),map.displayName,22,BellUI.Paper,true);
                BellUI.Text(new Rect(cx+20,cy+144,312,25),map.StyleLabel+"  /  "+map.DifficultyLabel,13,BellUI.Muted);
                GUI.enabled=session.maps.CanSelect(map);
                if(BellUI.Button(new Rect(cx+20,cy+178,312,38),map==session.maps.current?"다시 시작":"선택",primary:map==session.maps.current)&&ReadInput&&session.maps.TryStart(map,session.roster.PlayerCount))CloseMapMenu();
                GUI.enabled=true;
            }
            BellUI.Text(new Rect(x,y+595,1100,30),"카드와 인원은 새로 시작해요.",14,BellUI.Muted);
        }
    }
}
