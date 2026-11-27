using UnityEngine;

namespace HalliGalli.Movement
{
    public sealed partial class CourseNetwork
    {
        public enum EntryScreen { Home, Create, Join, OfflineMaps }
        EntryScreen entryScreen;
        public EntryScreen CurrentEntryScreen=>entryScreen;
        int offlineDemoPlayers=2,offlineDemoMapPage;
        CourseMapDefinition offlineDemoMap;
        public int OfflineDemoPlayers=>offlineDemoPlayers;
        public CourseMapDefinition OfflineDemoMap
        {
            get{
                if(!GameEdition.IsOfflineDemo||!game||!game.maps||!game.maps.catalog)return null;
                if(IsOfflineDemoMap(offlineDemoMap)&&game.maps.catalog.Contains(offlineDemoMap))return offlineDemoMap;
                if(IsOfflineDemoMap(game.maps.current))offlineDemoMap=game.maps.current;
                else foreach(var map in game.maps.catalog.maps)if(IsOfflineDemoMap(map)&&game.maps.CanSelect(map)){offlineDemoMap=map;break;}
                return offlineDemoMap;
            }
        }
        static bool IsOfflineDemoMap(CourseMapDefinition map)=>map&&map.playable&&map.scenePath!=CombatPracticeRoom.ScenePath&&map.scenePath!=UserMapRuntime.ScenePath;
        bool CanEditOfflineDemo=>GameEdition.IsOfflineDemo&&!online&&!Busy&&!leaving&&game&&game.maps&&!UserMapStudio.IsOpen&&!IsCustomizationOpen;
        public bool OfflineDemoMatchActive=>GameEdition.IsOfflineDemo&&!online&&!menu&&!Busy&&game&&!game.practiceRoom&&!UserMapStudio.IsOpen&&game.HasCardRules;
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
            if(IsCustomizationOpen){CloseCustomization();return;}
            if(online||Busy||leaving||!game||UserMapStudio.IsOpen)return;
            if(GameEdition.IsOfflineDemo&&(screen==EntryScreen.Create||screen==EntryScreen.Join)||!GameEdition.IsOfflineDemo&&screen==EntryScreen.OfflineMaps)screen=EntryScreen.Home;
            if(game.practiceRoom)game.practiceRoom.Exit();
            // End the previous local round, including pending falls and surf practice, before showing home.
            if(GameEdition.IsOfflineDemo){game.ResetPractice();Status="";}
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
        public void ShowOfflineDemoMaps()
        {
            if(!CanEditOfflineDemo)return;
            OpenEntry(EntryScreen.OfflineMaps);
        }
        public bool SelectOfflineDemoParticipants(int players)
        {
            if(!CanEditOfflineDemo||players<2||players>6)return false;
            offlineDemoPlayers=players;return true;
        }
        public bool SelectOfflineDemoMap(CourseMapDefinition map)
        {
            if(!CanEditOfflineDemo||!IsOfflineDemoMap(map)||!game.maps.CanSelect(map))return false;
            offlineDemoMap=map;Status="";return true;
        }
        public bool StartOfflineDemoMatch()=>StartOfflineDemoMatch(OfflineDemoMap,offlineDemoPlayers);
        public bool StartOfflineDemoMatch(CourseMapDefinition map,int players)
        {
            if(!CanEditOfflineDemo||players<2||players>6||!IsOfflineDemoMap(map)||!game.maps.CanSelect(map))return false;
            if(game.practiceRoom)game.practiceRoom.Exit();
            offlineDemoMap=map;offlineDemoPlayers=players;
            Busy=true;menu=true;entryScreen=EntryScreen.Home;Status="AI 대결을 준비하고 있어요…";FreezeWorld();
            bool changeScene=game.gameObject.scene.path!=map.scenePath;
            if(!changeScene)game.cardGame.enabled=true;
            if(!game.maps.TryStart(map,players)){Busy=false;Status="맵을 불러오지 못했어요. 다른 맵을 선택해 주세요.";return false;}
            if(changeScene)StartCoroutine(FinishOfflineDemoMap(map));
            else EnableOfflineDemoPlay();
            return true;
        }
        System.Collections.IEnumerator FinishOfflineDemoMap(CourseMapDefinition map)
        {
            while(!game||game.gameObject.scene.path!=map.scenePath)yield return null;
            // Scene Start and the existing home freeze coroutine must finish before restoring local play.
            yield return null;
            yield return null;
            EnableOfflineDemoPlay();
        }
        void EnableOfflineDemoPlay()
        {
            if(!GameEdition.IsOfflineDemo)return;
            game.cardGame.botEnabled=true;
            foreach(var p in game.roster.allParticipants){
                p.Initialize();var bot=p.GetComponent<CourseRival>();
                if(bot)bot.enabled=!p.localPlayer&&p.gameObject.activeSelf;
                p.Combat.simulateAutomatically=p.gameObject.activeSelf;p.Combat.readInput=p.localPlayer;p.Combat.networkAim=null;
            }
            Busy=false;menu=false;Status="";entryScreen=EntryScreen.Home;
            var controller=game.Local.GetComponent<SourcePlayerController>();controller.controlsEnabled=true;controller.suppressCursorCapture=false;
            if(!Application.isBatchMode)controller.SetCursorCaptured(true);
        }
        public bool ReplayOfflineDemoMatch()
        {
            if(!CanEditOfflineDemo||game.practiceRoom||!game.roster)return false;
            return StartOfflineDemoMatch(game.maps.current,game.roster.PlayerCount);
        }
#if UNITY_EDITOR
        // Offline authoring/validation helper. Player-facing games are created from the room menu.
        public void StartPractice()
        {
            if(online||Busy||leaving||!game||UserMapStudio.IsOpen)return;
            menu=false;Status="";entryScreen=EntryScreen.Home;game.ResetPractice();game.cardGame.botEnabled=true;
            foreach(var p in game.roster.allParticipants){p.Initialize();var bot=p.GetComponent<CourseRival>();if(bot)bot.enabled=true;p.Combat.simulateAutomatically=true;}
            var controller=game.Local.GetComponent<SourcePlayerController>();controller.controlsEnabled=true;controller.suppressCursorCapture=false;
            if(!Application.isBatchMode)controller.SetCursorCaptured(true);
        }
#endif
        public static bool CapturesPointer=>ControlSettings.BlocksInput||UserMapStudio.IsOpen&&!UserMapStudio.IsTesting||Instance&&(Instance.OverlayVisible||Instance.IsCustomizationOpen||Time.frameCount<=Instance.customizationBlockedThrough);
        void OnGUI()
        {
            if(UserMapStudio.IsOpen||ControlSettings.IsOpen||!game||!OverlayVisible)return;
            var matrix=GUI.matrix;var color=GUI.color;bool enabled=GUI.enabled;int depth=GUI.depth;GUI.depth=-30;BellUI.Begin();
            if(Time.frameCount<=customizationBlockedThrough&&(Event.current.isMouse||Event.current.isKey||Event.current.type==EventType.ScrollWheel))Event.current.Use();
            float w=BellUI.W,h=BellUI.H,x=(w-1040)*.5f,y=(h-640)*.5f;
            if(IsCustomizationOpen){DrawCustomizationScreen(x,y);GUI.enabled=enabled;GUI.matrix=matrix;GUI.color=color;GUI.depth=depth;return;}
            if(!online){DrawEntryScreen();GUI.enabled=enabled;GUI.matrix=matrix;GUI.color=color;GUI.depth=depth;return;}
            BellUI.Round(new Rect(0,0,w,h),new Color(.045f,.05f,.04f,.97f),0);
            BellUI.Icon("bell",new Rect(x,y+8,54,54),BellUI.Gold);
            BellUI.Text(new Rect(x+74,y,620,55),BellUI.GameTitle,39,BellUI.Paper,true);
            BellUI.Text(new Rect(x+76,y+57,600,28),"함께할 준비됐어?",18,BellUI.Muted);
            MusicVolumeControl.Draw(new Rect(x+600,y+12,440,59),false);
            BellUI.Line(new Vector2(x,y+105),new Vector2(x+1040,y+105),new Color(.4f,.38f,.29f,.3f),1);
            DrawRoom(x,y);
            if(!string.IsNullOrWhiteSpace(Status))BellUI.Text(new Rect(x+445,y+570,595,63),Status,15,BellUI.Gold);
            GUI.enabled=enabled;GUI.matrix=matrix;GUI.color=color;GUI.depth=depth;
        }
        enum EntryTone { Outline, Primary, Dark }
        static readonly Color EntryPaper=new Color(.961f,.949f,.914f),EntrySage=new Color(.863f,.898f,.847f);
        static readonly Color EntryInk=new Color(.125f,.173f,.153f),EntryMuted=new Color(.325f,.392f,.325f);
        static readonly Color EntryGold=new Color(.953f,.812f,.294f),EntryBorder=new Color(.63f,.69f,.61f);
        static readonly string[] EntryPlayerLabels={"2명","3명","4명","5명","6명"};
        Texture2D entryArtwork;
        GUIStyle entryButtonStyle,entryFieldStyle;

        void DrawEntryScreen()
        {
            if(GameEdition.IsOfflineDemo&&entryScreen==EntryScreen.OfflineMaps){DrawOfflineDemoMaps();return;}
            float x=(BellUI.W-1280)*.5f,y=(BellUI.H-720)*.5f;
            BellUI.Round(new Rect(0,0,BellUI.W,BellUI.H),EntryPaper,0);
            BellUI.Round(new Rect(0,0,BellUI.W,6),EntryGold,0);
            BellUI.Text(new Rect(x+46,y+24,600,24),GameEdition.IsOfflineDemo?"HALLIGALLI RUN / OFFLINE DEMO":"HALLIGALLI RUN",15,EntryInk,true);
            BellUI.Round(new Rect(x+46,y+66,1188,1),EntryBorder,0);
            BellUI.Text(new Rect(x+42,y+87,480,104),BellUI.GameTitle,76,EntryInk,true);
            if(!entryArtwork)entryArtwork=Resources.Load<Texture2D>("UI/HomeStillLife");
            var artworkRect=new Rect(x+563,y+91,671,485);
            BellUI.Round(artworkRect,EntrySage,0);
            if(entryArtwork)GUI.DrawTexture(artworkRect,entryArtwork,ScaleMode.ScaleToFit);
            CutEntryCorner(artworkRect,40);
            BellUI.Round(new Rect(x+46,y+619,1188,1),EntryBorder,0);
            GUI.enabled=!Busy&&!leaving;
            if(EntryButton(new Rect(x+46,y+640,150,44),"조작키 설정",size:16,arrow:false))ControlSettings.Open();
            MusicVolumeControl.Draw(new Rect(x+228,y+625,300,59),true);
            if(entryScreen==EntryScreen.Home&&EntryButton(new Rect(x+1120,y+640,114,44),"종료",size:16,arrow:false))QuitFromHome();
            if(GameEdition.IsOfflineDemo){DrawOfflineDemoHome(x,y);GUI.enabled=true;return;}
            if(entryScreen==EntryScreen.Home){
                if(EntryButton(new Rect(x+46,y+222,466,86),"방 만들기",EntryTone.Primary,27,hint:"친구 또는 AI와 · 2–6인"))ShowCreateRoom();
                if(EntryButton(new Rect(x+46,y+318,466,58),"초대코드로 참가",EntryTone.Dark,21))ShowJoinRoom();
                if(EntryButton(new Rect(x+46,y+386,228,54),"연습실"))StartTraining();
                if(EntryButton(new Rect(x+284,y+386,228,54),"맵 만들기"))UserMapStudio.Open();
                GUI.enabled=CanEditAppearance;
                if(EntryButton(new Rect(x+46,y+450,466,54),"내 캐릭터 꾸미기"))ShowCustomization();
                if(!string.IsNullOrWhiteSpace(Status))BellUI.Text(new Rect(x+46,y+520,466,76),Status,14,EntryMuted);
            }else{
                bool create=entryScreen==EntryScreen.Create;
                if(EntryButton(new Rect(x+46,y+222,90,38),"뒤로",size:16,arrow:false))ShowStartScreen();
                BellUI.Text(new Rect(x+46,y+270,466,42),create?"방 만들기":"초대코드로 참가",28,EntryInk,true);
                BellUI.Text(new Rect(x+46,y+316,466,22),"닉네임",15,EntryMuted);
                nickname=EntryField(new Rect(x+46,y+342,466,48),nickname,16,"Nickname");
                if(!create){
                    BellUI.Text(new Rect(x+46,y+400,466,22),"초대 코드",15,EntryMuted);
                    joinCode=EntryField(new Rect(x+46,y+426,466,48),joinCode,16,"JoinCode");
                }
                GUI.enabled=!Busy&&!leaving&&(create||joinCode.Trim().Length>=4);
                if(EntryButton(new Rect(x+46,y+(create?412:490),466,54),Busy?"연결 중…":create?"방 만들기":"참가하기",EntryTone.Primary,21))_=Connect(create,joinCode);
                GUI.enabled=CanEditAppearance;
                if(EntryButton(new Rect(x+968,y+640,266,44),"내 캐릭터 꾸미기",size:17))ShowCustomization();
                if(!string.IsNullOrWhiteSpace(Status))BellUI.Text(new Rect(x+46,y+554,466,61),Status,14,new Color(.48f,.22f,.12f));
            }
            GUI.enabled=true;
        }
        void DrawOfflineDemoHome(float x,float y)
        {
            BellUI.Text(new Rect(x+46,y+214,466,26),"참가 인원",16,EntryInk,true);
            DrawOfflineDemoParticipants(x+46,y+254,466,true);
            var map=OfflineDemoMap;
            BellUI.Text(new Rect(x+46,y+308,466,32),map?map.displayName:"선택 가능한 맵 없음",20,EntryInk,true);
            GUI.enabled=!Busy&&!leaving&&map&&game.maps.CanSelect(map);
            if(EntryButton(new Rect(x+46,y+350,466,64),Busy?"불러오는 중…":"AI 대결 시작",EntryTone.Primary,24))StartOfflineDemoMatch();
            GUI.enabled=!Busy&&!leaving;
            if(EntryButton(new Rect(x+46,y+424,466,44),"공식 맵 선택"))ShowOfflineDemoMaps();
            if(EntryButton(new Rect(x+46,y+478,228,44),"연습실",size:17))StartTraining();
            if(EntryButton(new Rect(x+284,y+478,228,44),"맵 만들기",size:17))UserMapStudio.Open();
            GUI.enabled=CanEditAppearance;
            if(EntryButton(new Rect(x+46,y+532,466,44),"내 캐릭터 꾸미기"))ShowCustomization();
            if(!string.IsNullOrWhiteSpace(Status))BellUI.Text(new Rect(x+46,y+582,466,34),Status,14,EntryMuted);
        }
        void DrawOfflineDemoParticipants(float x,float y,float width,bool light)
        {
            float step=(width+10)/5;
            for(int count=2;count<=6;count++){
                var rect=new Rect(x+(count-2)*step,y,step-10,40);
                bool selected=offlineDemoPlayers==count;
                bool clicked=light?EntryButton(rect,EntryPlayerLabels[count-2],selected?EntryTone.Primary:EntryTone.Outline,17,false):
                    BellUI.Button(rect,EntryPlayerLabels[count-2],selected:selected,size:17);
                if(clicked)SelectOfflineDemoParticipants(count);
            }
        }
        void DrawOfflineDemoMaps()
        {
            float w=BellUI.W,h=BellUI.H,x=(w-1100)*.5f,y=(h-640)*.5f;
            BellUI.Round(new Rect(0,0,w,h),new Color(.035f,.04f,.03f,.97f),0);
            BellUI.Text(new Rect(x,y,630,48),"오프라인 데모 · 공식 맵 선택",30,BellUI.Paper,true);
            GUI.enabled=!Busy&&!leaving;
            if(BellUI.Button(new Rect(x+990,y+4,110,38),"홈으로",size:16))ShowStartScreen();
            BellUI.Text(new Rect(x,y+53,370,28),"참가 인원 · 나 1명 + AI "+(offlineDemoPlayers-1)+"명",15,BellUI.Muted);
            DrawOfflineDemoParticipants(x+390,y+49,410,false);
            var selected=OfflineDemoMap;var maps=game.maps.catalog.maps;int count=0,index=0;
            foreach(var map in maps)if(IsOfflineDemoMap(map))count++;
            int pages=Mathf.Max(1,(count+5)/6);offlineDemoMapPage=Mathf.Clamp(offlineDemoMapPage,0,pages-1);
            GUI.enabled=!Busy&&offlineDemoMapPage>0;
            if(BellUI.Button(new Rect(x+820,y+52,72,34),"이전",size:14))offlineDemoMapPage--;
            GUI.enabled=!Busy&&offlineDemoMapPage+1<pages;
            if(BellUI.Button(new Rect(x+1028,y+52,72,34),"다음",size:14))offlineDemoMapPage++;
            BellUI.Text(new Rect(x+902,y+52,116,34),(offlineDemoMapPage+1)+" / "+pages,14,BellUI.Muted,false,TextAnchor.MiddleCenter);
            int first=offlineDemoMapPage*6;
            foreach(var map in maps){
                if(!IsOfflineDemoMap(map))continue;
                int slot=index++;if(slot<first||slot>=first+6)continue;
                float cx=x+((slot-first)%3)*368,cy=y+105+((slot-first)/3)*228;
                var tile=new Rect(cx,cy,352,216);BellUI.Round(tile,BellUI.Surface,24);
                BellUI.MapPicture(new Rect(cx+10,cy+10,332,86),map,16);
                BellUI.Text(new Rect(cx+20,cy+100,312,35),map.displayName,22,BellUI.Paper,true);
                BellUI.Text(new Rect(cx+20,cy+135,312,25),map.StyleLabel+"  /  "+map.DifficultyLabel,13,BellUI.Muted);
                GUI.enabled=!Busy&&!leaving&&game.maps.CanSelect(map);
                if(BellUI.Button(new Rect(cx+20,cy+166,312,38),map==selected?"선택됨":"이 맵 선택",selected:map==selected,size:16))SelectOfflineDemoMap(map);
            }
            GUI.enabled=!Busy&&!leaving&&selected&&game.maps.CanSelect(selected);
            if(BellUI.Button(new Rect(x+670,y+579,430,48),Busy?"불러오는 중…":offlineDemoPlayers+"명으로 AI 대결 시작",true))StartOfflineDemoMatch();
            GUI.enabled=true;
            BellUI.Text(new Rect(x,y+579,650,48),!string.IsNullOrWhiteSpace(Status)?Status:selected?"선택 맵 · "+selected.displayName:"선택 가능한 공식 맵이 없어요.",17,BellUI.Gold);
        }
        bool EntryButton(Rect rect,string label,EntryTone tone=EntryTone.Outline,int size=18,bool arrow=true,string hint=null)
        {
            if(entryButtonStyle==null)entryButtonStyle=new GUIStyle(GUIStyle.none);
            bool hover=GUI.enabled&&rect.Contains(Event.current.mousePosition);
            bool primary=tone==EntryTone.Primary,dark=tone==EntryTone.Dark;
            Color fill=!GUI.enabled?new Color(.88f,.89f,.85f):primary?(hover?new Color(1,.87f,.37f):EntryGold):
                dark?(hover?new Color(.20f,.28f,.23f):EntryInk):hover?EntrySage:EntryPaper;
            BellUI.Round(rect,fill,0);
            if(tone==EntryTone.Outline)BellUI.Round(rect,EntryBorder,0,1);
            if(primary)CutEntryCorner(rect,rect.height>54?20:12);
            GUI.SetNextControlName(label);
            bool clicked=GUI.Button(rect,GUIContent.none,entryButtonStyle);
            Color text=!GUI.enabled?EntryMuted:dark?EntryPaper:EntryInk;
            if(GUI.GetNameOfFocusedControl()==label)BellUI.Round(new Rect(rect.x+3,rect.y+3,rect.width-6,rect.height-6),text,0,2);
            var titleRect=new Rect(rect.x+20,rect.y,rect.width-(arrow?66:40),rect.height);
            if(hint!=null){titleRect.y+=10;titleRect.height=36;BellUI.Text(new Rect(rect.x+20,rect.y+48,rect.width-68,24),hint,15,text);}
            BellUI.Text(titleRect,label,size,text,true);
            if(arrow)BellUI.Icon("arrow",new Rect(rect.xMax-40,rect.center.y-11,22,22),text);
            return clicked;
        }
        string EntryField(Rect rect,string value,int maxLength,string controlName)
        {
            if(entryFieldStyle==null){
                entryFieldStyle=new GUIStyle(BellUI.Style(23)){padding=new RectOffset(16,16,8,8)};
                entryFieldStyle.normal.background=null;entryFieldStyle.focused.background=null;
                entryFieldStyle.normal.textColor=EntryInk;entryFieldStyle.focused.textColor=EntryInk;
            }
            BellUI.Round(rect,Color.white,0);
            BellUI.Round(rect,GUI.GetNameOfFocusedControl()==controlName?EntryInk:EntryBorder,0,1);
            GUI.SetNextControlName(controlName);return GUI.TextField(rect,value,maxLength,entryFieldStyle);
        }
        static void CutEntryCorner(Rect rect,float cut)
        {
            BellUI.Icon("cut-corner",new Rect(rect.xMax-cut,rect.yMax-cut,cut,cut),EntryPaper);
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
                Color shirt=PlayerShirtColors.ColorAt(m.shirtColor);
                BellUI.Round(new Rect(x,row,31,31),shirt,16);BellUI.Text(new Rect(x,row,31,31),(i+1).ToString(),13,shirt.grayscale>.55f?BellUI.Ink:BellUI.Paper,true,TextAnchor.MiddleCenter);
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
            GUI.enabled=CanEditAppearance;
            if(BellUI.Button(new Rect(x,y+539,432,34),"내 캐릭터 꾸미기",size:15))ShowCustomization();
            GUI.enabled=!Busy&&!leaving;
            if(BellUI.Button(new Rect(x,y+581,202,44),IsHost?"방 닫기":"나가기"))_=Leave();
            if(BellUI.Button(new Rect(x+220,y+581,202,44),"조작키 설정"))ControlSettings.Open();
            GUI.enabled=true;
        }
    }
}
