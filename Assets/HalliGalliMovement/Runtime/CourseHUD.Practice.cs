using UnityEngine;

namespace HalliGalli.Movement
{
    public sealed partial class CourseHUD
    {
        bool PracticeSurfActive=>session&&session.practiceRoom&&session.practiceRoom.Surf&&session.practiceRoom.Surf.SelectedStage>=0;
        Rect PracticeInfoPanel=>new Rect(24,88,440,226);

        void DrawPracticeState()
        {
            if(!PracticeSurfActive){
                BellUI.Text(new Rect(24,54,540,28),"ESC · 서프 기초 3단계 / 전투 연습 선택",13,BellUI.Muted,shadow:true);
                Prompt(24,BellUI.H-63,"R","전투 시작점",178);
                return;
            }
            var surf=session.practiceRoom.Surf;int stage=surf.SelectedStage;
            var r=PracticeInfoPanel;float x=r.x+16,y=r.y+12,width=r.width-32;
            string forward=ControlBindings.Hint(GameAction.Forward),back=ControlBindings.Hint(GameAction.Back);
            string left=ControlBindings.Hint(GameAction.Left),right=ControlBindings.Hint(GameAction.Right);
            string jump=ControlBindings.Hint(GameAction.Jump);
            BellUI.Round(r,new Color(.065f,.07f,.06f,.84f),18);
            BellUI.Text(new Rect(x,y,width,26),(surf.Completed?"성공!  ":"")+(stage+1)+" · "+PracticeSurfArea.StageName(stage),17,surf.Completed?BellUI.Gold:BellUI.Paper,true);
            string goal=stage==0?"출발대에서 오른쪽 경사면에 진입해 초록 도착대까지 미끄러지세요.":stage==1?"첫 경사면 끝의 틈을 넘어 다음 경사면에 이어 타고 초록 도착대에 착지하세요.":"끝 곡면에서 도약해 높은 경사면에 이어 탄 뒤 초록 도착대에 착지하세요.";
            BellUI.Text(new Rect(x,y+32,width,38),goal,13,BellUI.Paper);
            BellUI.Text(new Rect(x,y+72,width,38),"면 쪽으로 옆걸음 · 이번 연습은 오른쪽\n오른쪽 "+right+" / 왼쪽 "+left,13,BellUI.Cool);
            BellUI.Text(new Rect(x,y+112,width,38),"면에 올라타면 "+forward+"는 놓고, 마우스를 진행 방향으로 천천히 움직이세요.",13,BellUI.Paper);
            BellUI.Text(new Rect(x,y+152,width,50),"출발대 이동 "+forward+" / "+back+" / "+left+" / "+right+"\n점프 "+jump,12,BellUI.Muted);
            BellUI.Text(new Rect(24,BellUI.H-97,600,28),"ESC · 단계 선택 / 전투 연습으로 돌아가기",13,BellUI.Muted,shadow:true);
            Prompt(24,BellUI.H-63,"R","단계 시작점",178);
        }

        float DrawPracticeNavigation(float x,float y,float width)
        {
            var surf=session.practiceRoom.Surf;
            BellUI.Text(new Rect(x,y,width,22),"전투 / 서프 기초 3단계",14,BellUI.Muted);y+=28;
            bool enabled=GUI.enabled;
            for(int stage=-1;stage<3;stage++){
                bool available=surf&&(stage<0||surf.Available);
                bool selected=surf?surf.SelectedStage==stage:stage<0;
                GUI.enabled=enabled&&ReadInput&&available;
                string label=stage<0?"전투 연습":(stage+1)+" · "+PracticeSurfArea.StageName(stage);
                if(stage>=0&&selected&&surf.Completed)label+=" · 성공";
                if(BellUI.Button(new Rect(x,y,width,34),label,selected:selected,size:14)&&ReadInput&&available){
                    surf.SelectStage(stage);Resume();
                }
                y+=40;
            }
            GUI.enabled=enabled;
            BellUI.Text(new Rect(x,y,width,30),surf&&surf.Available?"선택하면 해당 시작점에서 바로 재개해요.":"공식 서프 설정이 없어 서프 연습을 사용할 수 없어요.",12,surf&&surf.Available?BellUI.Muted:BellUI.Danger);
            return y+38;
        }
    }
}
