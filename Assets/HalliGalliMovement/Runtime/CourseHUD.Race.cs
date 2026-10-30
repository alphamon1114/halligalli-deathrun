using UnityEngine;

namespace HalliGalli.Movement
{
    public sealed partial class CourseHUD
    {
        void DrawRaceItems()
        {
            var race=RaceCourse.Active;var actor=session.Local;
            if(!race||race.session!=session||!actor.Racing)return;
            var controller=actor.GetComponent<SourcePlayerController>();
            var item=race.HeldItem(actor);
            var target=item==RaceItem.Missile?race.LockTarget(actor,controller.AimRotation*Vector3.forward):null;
            float shield=race.ShieldRemaining(actor),effect=race.EffectRemaining(actor);
            float y=BellUI.H-142;
            BellUI.Round(new Rect(22,y,310,118),new Color(.045f,.055f,.065f,.9f),18);
            BellUI.Text(new Rect(38,y+10,278,24),race.Place(actor)+"위  ·  "+Mathf.Max(0,race.finishZ-actor.transform.position.z).ToString("F0")+"m 남음",14,BellUI.Paper,true);
            string title=item==RaceItem.Missile?"유도 미사일":item==RaceItem.Banana?"바나나":item==RaceItem.Shield?"방어막":"아이템 없음";
            BellUI.Text(new Rect(38,y+37,220,28),title,19,item==RaceItem.None?BellUI.Paper:BellUI.Gold,true);
            if(item!=RaceItem.None)BellUI.Key(new Rect(268,y+40,48,25),ControlBindings.Hint(GameAction.UseItem));
            string detail=shield>0?"방어 중 · "+shield.ToString("F1")+"초":effect>0?"피격 · "+effect.ToString("F1")+"초":item==RaceItem.Missile?(target?"락온 완료 · "+SeatName(System.Array.IndexOf(session.participants,target)):"앞의 상대를 조준하면 락온"):item==RaceItem.Banana?"내 뒤에 설치 · 밟으면 미끄러짐":item==RaceItem.Shield?"잠시 미사일·바나나 공격 방어":"박스를 통과하면 아이템 획득";
            BellUI.Text(new Rect(38,y+76,278,26),detail,13,shield>0?BellUI.Cool:BellUI.Paper);
            if(!target||!controller.playerCamera)return;
            Vector3 point=controller.playerCamera.WorldToScreenPoint(target.transform.position+Vector3.up);
            if(point.z<=0)return;
            float x=point.x/BellUI.Scale,sy=(Screen.height-point.y)/BellUI.Scale;
            if(x<35||x>BellUI.W-35||sy<80||sy>BellUI.H-170)return;
            BellUI.Round(new Rect(x-26,sy-31,52,62),BellUI.Gold,10,2);
            BellUI.Text(new Rect(x-70,sy-57,140,24),"LOCK ON",13,BellUI.Gold,true,TextAnchor.MiddleCenter,true);
        }
    }
}
