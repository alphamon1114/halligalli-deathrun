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
            if(!online){DrawEntryScreen(x,y);GUI.enabled=enabled;GUI.matrix=matrix;GUI.color=color;GUI.depth=depth;return;}
            BellUI.Round(new Rect(0,0,w,h),new Color(.045f,.05f,.04f,.97f),0);
            BellUI.Icon("bell",new Rect(x,y+8,54,54),BellUI.Gold);
            BellUI.Text(new Rect(x+74,y,620,55),BellUI.GameTitle,39,BellUI.Paper,true);
            BellUI.Text(new Rect(x+76,y+57,600,28),"함께할 준비됐어?",18,BellUI.Muted);
            MusicVolumeControl.Draw(new Rect(x+600,y+12,440,59));
            BellUI.Line(new Vector2(x,y+105),new Vector2(x+1040,y+105),new Color(.4f,.38f,.29f,.3f),1);
            DrawRoom(x,y);
            if(!string.IsNullOrWhiteSpace(Status))BellUI.Text(new Rect(x+445,y+570,595,63),Status,15,BellUI.Gold);
            GUI.enabled=enabled;GUI.matrix=matrix;GUI.color=color;GUI.depth=depth;
        }
        void DrawEntryScreen(float x,float y)
        {
            float w=BellUI.W,h=BellUI.H;
            if(GameEdition.IsOfflineDemo&&entryScreen==EntryScreen.OfflineMaps){DrawOfflineDemoMaps();return;}
            var backdrop=BellUI.MapImage(game.maps.current);
            if(backdrop){GUI.color=new Color(.5f,.53f,.47f);GUI.DrawTexture(new Rect(0,0,w,h),backdrop,ScaleMode.ScaleAndCrop);GUI.color=Color.white;}
            BellUI.Round(new Rect(0,0,w,h),new Color(.04f,.05f,.04f,.88f),0);
            // A quiet tonal wash keeps the title readable without a large enclosing panel.
            for(int i=0;i<32;i++)BellUI.Round(new Rect(w*i/32,0,w/32+1,h),new Color(.025f,.03f,.024f,.4f*(1-i/31f)),0);
            BellUI.Round(new Rect(x,y+99,88,88),new Color(.2f,.18f,.12f,.55f),44);
            BellUI.Icon("bell",new Rect(x+17,y+116,54,54),BellUI.Gold);
            if(GameEdition.IsOfflineDemo)BellUI.Text(new Rect(x+2,y+199,478,28),"OFFLINE EXHIBITION DEMO",18,BellUI.Gold,true);
            BellUI.Text(new Rect(x-5,y+244,480,116),BellUI.GameTitle,80,BellUI.Paper,true);
            BellUI.Text(new Rect(x+2,y+350,460,28),"HALLIGALLI RUN",21,BellUI.Gold,true);
            BellUI.Round(new Rect(x+2,y+394,128,5),BellUI.Gold,2);
            BellUI.Text(new Rect(x+2,y+420,460,31),GameEdition.IsOfflineDemo?"인터넷 없이 · 나 1명 + AI 최대 5명":"카드를 읽고, 종까지 달려!",21,BellUI.Muted);
            GUI.enabled=CanEditAppearance;
            if(BellUI.Button(new Rect(x+2,y+477,478,50),"내 캐릭터 꾸미기",size:19))ShowCustomization();
            GUI.enabled=!Busy&&!leaving;
            if(!Busy&&!leaving&&BellUI.Button(new Rect(x+2,y+560,180,40),"조작키 설정",size:16))ControlSettings.Open();
            MusicVolumeControl.Draw(new Rect(x+202,y+548,278,59));
            float right=x+584;
            GUI.enabled=!Busy&&!leaving;
            if(GameEdition.IsOfflineDemo){DrawOfflineDemoHome(right,y);GUI.enabled=true;return;}
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
        void DrawOfflineDemoHome(float x,float y)
        {
            BellUI.Text(new Rect(x,y+94,456,38),"오프라인 전시 데모",27,BellUI.Paper,true);
            BellUI.Text(new Rect(x,y+136,456,28),"온라인 접속 없이 실제 AI와 겨뤄요.",15,BellUI.Muted);
            BellUI.Text(new Rect(x,y+181,456,26),"참가 인원 · 나 1명 + AI "+(offlineDemoPlayers-1)+"명",16,BellUI.Muted);
            DrawOfflineDemoParticipants(x,y+215,456);
            var map=OfflineDemoMap;
            BellUI.Text(new Rect(x,y+272,456,35),"선택 맵 · "+(map?map.displayName:"선택 가능한 맵 없음"),19,BellUI.Gold,true);
            GUI.enabled=!Busy&&!leaving&&map&&game.maps.CanSelect(map);
            if(EntryChoice(new Rect(x,y+317,456,86),"person",Busy?"불러오는 중…":"AI 대결 시작","같은 과일이 정확히 5개면 종까지 달리기",true))StartOfflineDemoMatch();
            GUI.enabled=!Busy&&!leaving;
            if(BellUI.Button(new Rect(x,y+416,456,43),"공식 맵 선택",size:18))ShowOfflineDemoMaps();
            if(BellUI.Button(new Rect(x,y+473,221,43),"전투 / 서프 연습",size:17))StartTraining();
            if(BellUI.Button(new Rect(x+235,y+473,221,43),"맵 만들기",size:17))UserMapStudio.Open();
            if(!string.IsNullOrWhiteSpace(Status))BellUI.Text(new Rect(x,y+521,456,42),Status,14,BellUI.Gold);
            if(BellUI.Button(new Rect(x+342,y+574,114,40),"종료",size:15))QuitFromHome();
        }
        void DrawOfflineDemoParticipants(float x,float y,float width)
        {
            float step=(width+10)/5;
            for(int count=2;count<=6;count++)
                if(BellUI.Button(new Rect(x+(count-2)*step,y,step-10,40),count+"명",selected:offlineDemoPlayers==count,size:17))SelectOfflineDemoParticipants(count);
        }
        void DrawOfflineDemoMaps()
        {
            float w=BellUI.W,h=BellUI.H,x=(w-1100)*.5f,y=(h-640)*.5f;
            BellUI.Round(new Rect(0,0,w,h),new Color(.035f,.04f,.03f,.97f),0);
            BellUI.Text(new Rect(x,y,630,48),"오프라인 데모 · 공식 맵 선택",30,BellUI.Paper,true);
            GUI.enabled=!Busy&&!leaving;
            if(BellUI.Button(new Rect(x+990,y+4,110,38),"홈으로",size:16))ShowStartScreen();
            BellUI.Text(new Rect(x,y+53,370,28),"참가 인원 · 나 1명 + AI "+(offlineDemoPlayers-1)+"명",15,BellUI.Muted);
            DrawOfflineDemoParticipants(x+390,y+49,410);
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
