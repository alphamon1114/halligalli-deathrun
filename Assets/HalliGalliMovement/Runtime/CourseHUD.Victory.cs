using UnityEngine;

namespace HalliGalli.Movement
{
    public sealed partial class CourseHUD
    {
        void DrawVictoryBanner()
        {
            float intro=Mathf.SmoothStep(0,1,Mathf.Clamp01((Time.unscaledTime-victoryShownAt)/.4f));
            float x=BellUI.W*.5f,y=BellUI.H*.47f+(1-intro)*16;
            Color Fade(Color c){c.a*=intro;return c;}
            BellUI.Round(new Rect(0,0,BellUI.W,BellUI.H),Fade(new Color(.025f,.03f,.025f,.46f)),0);
            BellUI.Round(new Rect(x-320,y-118,640,244),Fade(new Color(0,0,0,.22f)),40);
            BellUI.Round(new Rect(x-320,y-126,640,244),Fade(BellUI.Surface),40);
            BellUI.Round(new Rect(x-320,y-126,640,244),Fade(new Color(BellUI.Gold.r,BellUI.Gold.g,BellUI.Gold.b,.3f)),40,1);
            // The bell seal and fine curved accents use the same gold as the in-game HUD.
            var seal=new Vector2(x,y-126);
            BellUI.Round(new Rect(x-37,y-163,74,74),Fade(BellUI.Ink),37);
            BellUI.Arc(seal,36,1,Fade(BellUI.Gold),2);
            BellUI.Icon("bell",new Rect(x-20,y-146,40,40),Fade(BellUI.Gold));
            foreach(int side in new[]{-1,1}){
                BellUI.Line(new Vector2(x+side*65,y-126),new Vector2(x+side*246,y-126),Fade(new Color(BellUI.Gold.r,BellUI.Gold.g,BellUI.Gold.b,.5f)),2);
                BellUI.Round(new Rect(x+side*257-3,y-129,6,6),Fade(BellUI.Gold),3);
            }
            BellUI.Text(new Rect(x-270,y-80,540,74),"승자",52,Fade(BellUI.Gold),true,TextAnchor.MiddleCenter);
            string name=WinnerNickname;int size=32;
            while(size>18&&BellUI.Style(size,true).CalcSize(new GUIContent(name)).x>550)size--;
            BellUI.Text(new Rect(x-280,y+3,560,52),name,size,Fade(BellUI.Paper),true,TextAnchor.MiddleCenter);
            if(intro<.99f)return;
            if(CanContinueAfterVictory){
                float width=CourseNetwork.Active?174:150;
                Prompt(x-width*.5f,y+69,"R",VictoryAction,width);
            }else BellUI.Text(new Rect(x-240,y+69,480,39),VictoryAction,15,BellUI.Muted,false,TextAnchor.MiddleCenter);
        }
    }
}
