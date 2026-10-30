using UnityEngine;

namespace HalliGalli.Movement
{
    public sealed partial class CourseNetwork
    {
        public enum EntryScreen { Home, Create, Join }
        EntryScreen entryScreen;
        public EntryScreen CurrentEntryScreen=>entryScreen;
        public bool OverlayVisible=>UserMapStudio.IsOpen?!UserMapStudio.IsTesting:menu||online&&!Playing;
        public void ShowMenu()
        {
            if(UserMapStudio.IsOpen)return;
            if(!online){ShowStartScreen();return;}
            menu=true;
            if(game){var controller=game.Local.GetComponent<SourcePlayerController>();controller.SetCursorCaptured(false);controller.suppressCursorCapture=true;}
        }
        public void ShowStartScreen(){OpenEntry(EntryScreen.Home);}
        public void ShowCreateRoom(){OpenEntry(EntryScreen.Create);}
        public void ShowJoinRoom(){OpenEntry(EntryScreen.Join);}
        void OpenEntry(EntryScreen screen)
        {
            if(online||Busy||leaving||!game||UserMapStudio.IsOpen)return;
            if(game.practiceRoom)game.practiceRoom.Exit();
            entryScreen=screen;menu=true;FreezeWorld();GUIUtility.keyboardControl=0;
        }
        public void StartTraining()
        {
            if(online||Busy||leaving||!game||UserMapStudio.IsOpen)return;
            StartCoroutine(LoadTraining());
        }
        System.Collections.IEnumerator LoadTraining()
        {
            Busy=true;menu=true;FreezeWorld();Status="연습실 불러오는 중…";
            yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(CombatPracticeRoom.ScenePath);
            yield return null;
            Busy=false;menu=false;Status="";game.practiceRoom.Enter();
        }
#if UNITY_EDITOR
        // Offline authoring/validation helper. Player-facing games are created from the room menu.
        public void StartPractice()
        {
            if(online||Busy||leaving||!game||UserMapStudio.IsOpen)return;
            menu=false;Status="";entryScreen=EntryScreen.Home;game.ResetPractice();game.cardGame.botEnabled=true;
            foreach(var p in game.roster.allParticipants){var bot=p.GetComponent<CourseRival>();if(bot)bot.enabled=true;p.Combat.simulateAutomatically=true;}
            var controller=game.Local.GetComponent<SourcePlayerController>();controller.controlsEnabled=true;controller.suppressCursorCapture=false;
            if(!Application.isBatchMode)controller.SetCursorCaptured(true);
        }
#endif
        public static bool CapturesPointer=>ControlSettings.BlocksInput||UserMapStudio.IsOpen&&!UserMapStudio.IsTesting||Instance&&Instance.OverlayVisible;
        void OnGUI()
        {
            if(UserMapStudio.IsOpen||ControlSettings.IsOpen||!game||!OverlayVisible)return;
            var matrix=GUI.matrix;var color=GUI.color;bool enabled=GUI.enabled;int depth=GUI.depth;GUI.depth=-30;BellUI.Begin();
            float w=BellUI.W,h=BellUI.H,x=(w-1040)*.5f,y=(h-640)*.5f;
            if(!online){DrawEntryScreen(x,y);GUI.enabled=enabled;GUI.matrix=matrix;GUI.color=color;GUI.depth=depth;return;}
            BellUI.Round(new Rect(0,0,w,h),new Color(.045f,.05f,.04f,.97f),0);
            BellUI.Icon("bell",new Rect(x,y+8,54,54),BellUI.Gold);
            BellUI.Text(new Rect(x+74,y,620,55),"BELL RUN",39,BellUI.Paper,true);
            BellUI.Text(new Rect(x+76,y+57,600,28),"함께할 준비됐어?",18,BellUI.Muted);
            BellUI.Line(new Vector2(x,y+105),new Vector2(x+1040,y+105),new Color(.4f,.38f,.29f,.3f),1);
            DrawRoom(x,y);
            if(!string.IsNullOrWhiteSpace(Status))BellUI.Text(new Rect(x+445,y+570,595,63),Status,15,BellUI.Gold);
            GUI.enabled=enabled;GUI.matrix=matrix;GUI.color=color;GUI.depth=depth;
        }
        void DrawEntryScreen(float x,float y)
        {
            float w=BellUI.W,h=BellUI.H;
            var backdrop=BellUI.MapImage(game.maps.current);
            if(backdrop){GUI.color=new Color(.5f,.53f,.47f);GUI.DrawTexture(new Rect(0,0,w,h),backdrop,ScaleMode.ScaleAndCrop);GUI.color=Color.white;}
            BellUI.Round(new Rect(0,0,w,h),new Color(.04f,.05f,.04f,.88f),0);
            // A quiet tonal wash keeps the title readable without a large enclosing panel.
            for(int i=0;i<32;i++)BellUI.Round(new Rect(w*i/32,0,w/32+1,h),new Color(.025f,.03f,.024f,.4f*(1-i/31f)),0);
            BellUI.Round(new Rect(x,y+99,88,88),new Color(.2f,.18f,.12f,.55f),44);
            BellUI.Icon("bell",new Rect(x+17,y+116,54,54),BellUI.Gold);
            BellUI.Text(new Rect(x-5,y+210,480,100),"BELL",88,BellUI.Paper,true);
            BellUI.Text(new Rect(x-5,y+298,480,100),"RUN",88,BellUI.Gold,true);
            BellUI.Text(new Rect(x+2,y+425,460,31),"할리갈리 데스런",21,BellUI.Muted);
            if(!Busy&&!leaving&&BellUI.Button(new Rect(x+2,y+485,180,40),"조작키 설정",size:16))ControlSettings.Open();
            float right=x+584;
            GUI.enabled=!Busy&&!leaving;
            if(entryScreen==EntryScreen.Home){
                BellUI.Text(new Rect(right,y+160,456,30),"어떻게 시작할까?",18,BellUI.Muted);
                if(EntryChoice(new Rect(right,y+216,456,92),"person","방 만들기","친구 또는 AI와 · 2–6인",true))ShowCreateRoom();
                if(EntryChoice(new Rect(right,y+342,456,92),"arrow","초대코드로 참가","친구의 방으로"))ShowJoinRoom();
                if(BellUI.Button(new Rect(right,y+457,221,48),"연습실",size:18))StartTraining();
                if(BellUI.Button(new Rect(right+235,y+457,221,48),"맵 만들기",size:18))UserMapStudio.Open();
                if(BellUI.Button(new Rect(right+342,y+574,114,40),"종료",size:15))QuitFromHome();
            }else{
                bool create=entryScreen==EntryScreen.Create;
                if(BellUI.Button(new Rect(right,y+62,90,34),"뒤로",size:14))ShowStartScreen();
                BellUI.Text(new Rect(right,y+128,456,45),create?"방 만들기":"초대코드로 참가",30,BellUI.Paper,true);
                BellUI.Text(new Rect(right,y+198,456,26),"닉네임",14,BellUI.Muted);
                nickname=BellUI.Field(new Rect(right,y+233,456,52),nickname,16,"Nickname");
                BellUI.Text(new Rect(right,y+310,456,26),create?"최대 6명 · 들어온 인원으로 시작":"초대 코드",14,BellUI.Muted);
                if(!create)joinCode=BellUI.Field(new Rect(right,y+347,456,52),joinCode,16,"JoinCode");
                GUI.enabled=!Busy&&!leaving&&(create||joinCode.Trim().Length>=4);
                if(BellUI.Button(new Rect(right,y+(create?366:437),456,54),Busy?"연결 중…":create?"방 만들기":"참가하기",true))_=Connect(create,joinCode);
            }
            GUI.enabled=!Busy&&!leaving;
            if(!string.IsNullOrWhiteSpace(Status))BellUI.Text(new Rect(right,y+509,456,61),Status,14,BellUI.Gold);
            GUI.enabled=true;
        }
        bool EntryChoice(Rect rect,string icon,string title,string hint,bool primary=false)
        {
            bool clicked=BellUI.Button(rect,"",primary:primary);Color text=primary?BellUI.Ink:BellUI.Paper;
            BellUI.Icon(icon,new Rect(rect.x+25,rect.y+28,35,35),primary?BellUI.Ink:BellUI.Gold);
            BellUI.Text(new Rect(rect.x+82,rect.y+15,325,35),title,24,text,true);
            BellUI.Text(new Rect(rect.x+83,rect.y+52,325,25),hint,14,primary?new Color(.25f,.21f,.14f):BellUI.Muted);
            BellUI.Icon("arrow",new Rect(rect.xMax-43,rect.y+36,20,20),text);
            return clicked;
        }
        void QuitFromHome()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
#else
            Application.Quit();
#endif
        }
        void DrawRoom(float x,float y)
        {
            BellUI.Text(new Rect(x,y+128,120,25),"초대 코드",13,BellUI.Muted);
            BellUI.Text(new Rect(x,y+155,320,44),string.IsNullOrEmpty(InviteCode)?"발급 중…":InviteCode,31,BellUI.Gold,true);
            GUI.enabled=!string.IsNullOrEmpty(InviteCode);
            if(BellUI.Button(new Rect(x+328,y+157,102,38),"복사",size:15)){GUIUtility.systemCopyBuffer=InviteCode;Status="초대 코드를 복사했어요.";}
            GUI.enabled=true;
            BellUI.Text(new Rect(x,y+223,240,30),"참가자  "+members.Count+" / "+capacity,16,BellUI.Muted);
            if(IsHost&&!Playing&&!loading){
                GUI.enabled=CanAddBot;
                if(BellUI.Button(new Rect(x+302,y+219,130,34),"+ AI 추가",size:14))AddBot();
                GUI.enabled=true;
            }
            for(int i=0;i<members.Count;i++){
                var m=members[i];float row=y+266+i*45;Color c=m.id==0?BellUI.Gold:m.ready?BellUI.Cool:BellUI.Muted;
                BellUI.Round(new Rect(x,row,31,31),new Color(.18f,.19f,.17f),16);BellUI.Text(new Rect(x,row,31,31),(i+1).ToString(),13,c,true,TextAnchor.MiddleCenter);
                string name=SeatName(i);int size=Mathf.Min(16,Mathf.FloorToInt(16*267/Mathf.Max(1,BellUI.Style(16).CalcSize(new GUIContent(name)).x)));
                BellUI.Text(new Rect(x+47,row,267,31),name,Mathf.Max(12,size));
                if(m.bot){
                    if(IsHost&&!Playing&&!loading){
                        GUI.enabled=CanEditBots;
                        if(BellUI.Button(new Rect(x+348,row,84,31),"빼기",size:13)){RemoveBot(m.id);GUI.enabled=true;break;}
                        GUI.enabled=true;
                    }else BellUI.Text(new Rect(x+324,row,108,31),"AI",13,BellUI.Cool,false,TextAnchor.MiddleRight);
                }else BellUI.Text(new Rect(x+324,row,108,31),MapPeerLabel(m),13,c,false,TextAnchor.MiddleRight);
            }
            for(int i=members.Count;i<capacity;i++){
                float row=y+266+i*45;BellUI.Round(new Rect(x,row,31,31),new Color(.16f,.17f,.15f),16);
                BellUI.Text(new Rect(x,row,31,31),(i+1).ToString(),13,BellUI.Muted,false,TextAnchor.MiddleCenter);
                BellUI.Text(new Rect(x+47,row,267,31),"빈자리",15,new Color(.44f,.46f,.42f));
            }
            float right=x+600;DrawMapSelection(right,y);
            if(Playing){
                if(BellUI.Button(new Rect(right,y+444,440,49),"계속하기",true)){menu=false;EnablePlay();}
                if(IsHost&&BellUI.Button(new Rect(right,y+506,440,40),"대기방으로"))ReturnToRoom();
            }else if(IsHost){
                GUI.enabled=CanStart;
                if(BellUI.Button(new Rect(right,y+444,440,52),loading?"맵 불러오는 중…":members.Count+"명으로 시작",true))StartMatch();
                GUI.enabled=true;
                if(!CanStart)BellUI.Text(new Rect(right,y+505,440,30),members.Count<2?"친구를 초대하거나 AI를 추가해요":!AllUserMapsReady?"모두 맵을 받은 뒤 시작할 수 있어요":"모두 준비하면 시작",14,BellUI.Muted);
            }else{
                GUI.enabled=!loading&&!Busy&&UserMapReady;
                bool ready=LocalSeat>=0&&members[LocalSeat].ready;
                if(BellUI.Button(new Rect(right,y+444,440,52),loading?"맵 불러오는 중…":!UserMapReady?"맵을 받는 중…":ready?"준비 취소":"준비 완료",!ready))LocalAction(NetworkAction.Ready);
                GUI.enabled=true;
            }
            GUI.enabled=!Busy&&!leaving;
            if(BellUI.Button(new Rect(x,y+581,202,44),IsHost?"방 닫기":"나가기"))_=Leave();
            if(BellUI.Button(new Rect(x+220,y+581,202,44),"조작키 설정"))ControlSettings.Open();
            GUI.enabled=true;
        }
    }
}
