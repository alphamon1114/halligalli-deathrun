using UnityEngine;

namespace HalliGalli.Movement
{
    [DefaultExecutionOrder(-1000)]
    public sealed class ControlSettings:MonoBehaviour
    {
        static ControlSettings instance;
        public static bool IsOpen{get;private set;}
        static int blockedThrough=-1;
        static bool waitRelease;
        public static bool BlocksInput=>IsOpen||Time.frameCount<=blockedThrough||waitRelease;
        int row=-1,slot=-1,armedFrame,candidate;
        string message="";
        bool focused=true;
        public bool Listening=>row>=0;
        public static void Open()
        {
            Ensure();IsOpen=true;instance.row=-1;instance.message="";GUIUtility.keyboardControl=0;
            foreach(var controller in FindObjectsByType<SourcePlayerController>())if(controller.enabled)controller.SetCursorCaptured(false);
        }
        public static void Close(){IsOpen=false;blockedThrough=Time.frameCount+2;waitRelease=true;if(instance){instance.row=-1;instance.candidate=0;}}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]static void Boot(){GameReleaseGate.RunWhenAllowed(BootAfterReleaseCheck);}
        static void BootAfterReleaseCheck(){IsOpen=false;blockedThrough=-1;waitRelease=false;Ensure();ControlBindings.Reload();}
        static void Ensure(){if(instance)return;instance=new GameObject("Control settings").AddComponent<ControlSettings>();DontDestroyOnLoad(instance.gameObject);}
        public void BeginCapture(GameAction action,int bindingSlot){row=(int)action;slot=bindingSlot;candidate=0;armedFrame=Time.frameCount+1;message="원하는 키·마우스 버튼·휠을 입력하세요.  ESC 취소";}
        void OnApplicationFocus(bool focus){focused=focus;if(!focus){row=-1;candidate=0;blockedThrough=Time.frameCount+2;waitRelease=true;}}
        void Update()
        {
            if(waitRelease&&!Input.anyKey&&Mathf.Approximately(Input.mouseScrollDelta.y,0))waitRelease=false;
            if(!IsOpen||!focused)return;
            if(Input.GetKeyDown(KeyCode.Escape)){
                if(row>=0){row=-1;candidate=0;message="변경을 취소했어요.";}else Close();return;
            }
            if(row<0||candidate!=0||Time.frameCount<=armedFrame)return;
            // Clicks on Cancel/Close are UI actions, not mouse-button bindings.
            if(Input.GetMouseButtonDown(0)&&BellUI.MousePoint.y>(BellUI.H-650)/2+585)return;
            AcceptInput(ControlBindings.CaptureCode());
        }
        public void AcceptInput(int code)
        {
            if(!IsOpen||row<0||candidate!=0||code==0)return;
            if(!ControlBindings.Valid(code)||code<0&&!ControlBindings.AllowsWheel((GameAction)row)){message="이 동작에는 누르고 있을 수 있는 키나 버튼을 지정해주세요.";return;}
            if(ControlBindings.Conflict((GameAction)row,slot,code,out var other,out _)){
                candidate=code;message=ControlBindings.Label(code)+" · 이미 ‘"+ControlBindings.Names[(int)other]+"’에 지정되어 있어요.";return;
            }
            Apply(code,false);
        }
        void Apply(int code,bool swap)
        {
            if(!ControlBindings.Assign((GameAction)row,slot,code,swap)){candidate=0;message="휠은 이동·웅크리기에 지정할 수 없어요.";return;}
            row=-1;candidate=0;message="저장했어요.";blockedThrough=Time.frameCount+2;
        }
        void OnGUI()
        {
            if(!IsOpen)return;
            var matrix=GUI.matrix;var color=GUI.color;bool enabled=GUI.enabled;int depth=GUI.depth;GUI.depth=-100;BellUI.Begin();
            float x=(BellUI.W-980)/2,y=(BellUI.H-650)/2;
            BellUI.Round(new Rect(0,0,BellUI.W,BellUI.H),new Color(.03f,.035f,.03f,.94f),0);
            BellUI.Round(new Rect(x,y,980,650),BellUI.Surface,30);
            BellUI.Text(new Rect(x+28,y+22,550,42),"조작키",30,BellUI.Paper,true);
            BellUI.Text(new Rect(x+28,y+66,920,28),"키를 눌러 변경 · ×로 해제 · 변경 즉시 저장",15,BellUI.Muted);
            for(int s=0;s<3;s++)BellUI.Text(new Rect(x+275+s*212,y+100,184,25),s==0?"기본":"추가 "+s,13,BellUI.Muted);
            GUI.enabled=row<0;
            for(int a=0;a<ControlBindings.Actions.Length;a++){
                float cy=y+130+a*32;
                BellUI.Text(new Rect(x+28,cy,240,30),ControlBindings.Names[a],15);
                for(int s=0;s<3;s++){
                    float cx=x+275+s*212;
                    if(BellUI.Button(new Rect(cx,cy,148,28),row==a&&slot==s?"입력 대기…":ControlBindings.Label(ControlBindings.Get((GameAction)a,s)),selected:row==a&&slot==s,size:13))BeginCapture((GameAction)a,s);
                    if(BellUI.Button(new Rect(cx+154,cy,29,28),"×",size:16)){ControlBindings.Assign((GameAction)a,s,0);message="지정을 해제했어요.";}
                }
            }
            GUI.enabled=true;
            BellUI.Text(new Rect(x+28,y+551,924,29),message.Length>0?message:"휠은 한 번 굴릴 때 점프 입력만 보냅니다. 자동 연속 점프는 켜지지 않아요.",14,candidate!=0?BellUI.Gold:BellUI.Muted);
            if(candidate!=0){
                if(BellUI.Button(new Rect(x+28,y+595,180,36),"서로 바꿔 지정",true,size:15))Apply(candidate,true);
                if(BellUI.Button(new Rect(x+220,y+595,110,36),"취소",size:15)){row=-1;candidate=0;message="변경을 취소했어요.";}
            }else if(row>=0){
                if(BellUI.Button(new Rect(x+28,y+595,150,36),"입력 취소",size:15)){row=-1;message="변경을 취소했어요.";}
            }else{
                if(BellUI.Button(new Rect(x+28,y+595,180,36),"휠 양방향 점프",size:15)){
                    bool conflict=false;
                    foreach(int code in new[]{ControlBindings.WheelDown,ControlBindings.WheelUp})
                        if(ControlBindings.Conflict(GameAction.Jump,code==ControlBindings.WheelDown?1:2,code,out var other,out _)&&other!=GameAction.Jump)conflict=true;
                    if(conflict)message="다른 동작에 지정한 휠이 있어요. 점프 칸에서 직접 바꿔주세요.";
                    else {ControlBindings.Assign(GameAction.Jump,1,ControlBindings.WheelDown,true);ControlBindings.Assign(GameAction.Jump,2,ControlBindings.WheelUp,true);message="점프에 휠 ↑·↓를 추가했어요.";}
                }
                if(BellUI.Button(new Rect(x+220,y+595,150,36),"기본값 복원",size:15)){ControlBindings.Defaults();message="기본 조작으로 복원했어요.";}
            }
            if(BellUI.Button(new Rect(x+790,y+591,160,42),"닫기",true,size:16))Close();
            GUI.enabled=enabled;GUI.matrix=matrix;GUI.color=color;GUI.depth=depth;
        }
    }
}
