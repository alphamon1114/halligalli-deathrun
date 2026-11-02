using System.Collections.Generic;
using HalliGalli.Movement;
using UnityEngine;

namespace HalliGalli.EditorTools
{
    // Separate downhill accelerators from uphill launches; turn only on the checkpoint decks.
    public static class PyramidRouteLayout
    {
        public static readonly string[] Names={"태양의 첫 도약","도약의 박자","떠오르는 제단","연속 도약의 회랑","왕의 도약"};
        public static readonly float[] Headings={0,90,0,90,0};
        public static SurfCourse.Section[] Room(int index,Vector3 start)
        {
            var p=new Pen(start,Headings[index]);
            switch(index){
                case 0:
                    p.Down(20,6,3.6f);p.Transfer(2.2f,-1.2f);
                    p.Up(16,3.2f,3.6f);p.Transfer(2.5f,.65f);
                    p.Down(20,5.6f,3.6f);p.Transfer(2.4f,-1.35f);
                    p.Up(16,3,3.6f);break;
                case 1:
                    p.Down(22,6.4f,3.5f);p.Transfer(2.5f,-1.35f);
                    p.Up(14,2.8f,3.5f);p.Transfer(2.6f,.65f);
                    p.Down(20,4.8f,3.5f);p.Transfer(3,-1.4f);
                    p.Down(16,3,3.5f);p.Transfer(2.4f,-1.25f);
                    p.Up(18,3.4f,3.5f);break;
                case 2:
                    p.Down(18,5.2f,3.4f);p.Transfer(2.8f,-1.3f);
                    p.Down(16,3,3.4f);p.Transfer(2.5f,-1.25f);
                    p.Up(22,4.2f,3.4f);p.Transfer(2.5f,.75f);
                    p.Down(16,4.2f,3.4f);p.Transfer(2.6f,-1.4f);
                    p.Up(18,3.4f,3.4f);break;
                case 3:
                    p.Down(20,6,3.2f);p.Transfer(2.6f,-1.4f);
                    p.Up(14,2.8f,3.2f);p.Transfer(2.5f,.65f);
                    p.Down(18,4.8f,3.2f);p.Transfer(3.2f,-1.6f);
                    p.Up(16,3.2f,3.2f);p.Transfer(2.6f,.75f);
                    p.Down(22,5.8f,3.2f);p.Transfer(3,-1.5f);
                    p.Up(18,3.6f,3.2f);break;
                default:
                    p.Down(22,6.6f);p.Transfer(3,-1.6f);
                    p.Up(16,3.2f);p.Transfer(2.5f,.75f);
                    p.Down(18,5);p.Transfer(2.8f,-1.4f);
                    p.Down(14,2.8f);p.Transfer(2.6f,-1.35f);
                    p.Up(20,4);break;
            }
            p.sections[0].entry=index==0?new Vector3(0,.375f,6.1f):start-p.forward*2.4f+Vector3.up;
            return p.sections.ToArray();
        }
        sealed class Pen
        {
            public readonly List<SurfCourse.Section> sections=new List<SurfCourse.Section>();
            public readonly Vector3 forward;
            readonly float heading;
            Vector3 point;

            public Pen(Vector3 start,float heading){point=start;this.heading=heading;forward=Quaternion.Euler(0,heading,0)*Vector3.forward;}
            public void Down(float length,float drop,float width=3)=>Ramp(length,-drop,width);
            public void Up(float length,float rise,float width=3)=>Ramp(length,rise,width);
            void Ramp(float length,float change,float width)
            {
                int steps=Mathf.CeilToInt(length/.5f);
                var points=new Vector3[steps+1];var headings=new float[steps+1];
                for(int i=0;i<=steps;i++){
                    float distance=length*i/steps,t=i/(float)steps,height;
                    if(change<0){
                        // The last 40% eases downhill speed into a level takeoff.
                        // No uphill tail: the next, separate ramp owns the launch.
                        float transition=length*.4f,straight=length-transition;
                        float ease=Mathf.Clamp01((distance-straight)/transition);
                        float integral=ease-ease*ease*ease+.5f*ease*ease*ease*ease;
                        height=change*(Mathf.Min(distance,straight)+transition*integral)/(straight+transition*.5f);
                    }else{
                        // Rise throughout, with enough exit tangent to clear raised catches.
                        // The gradual quartic bend never sinks into a middle trough.
                        height=change*(.1f*t+.9f*t*t*t*t);
                    }
                    points[i]=point+forward*distance+Vector3.up*height;
                    headings[i]=heading;
                }
                sections.Add(new SurfCourse.Section{points=points,headings=headings,side=-1,twoSided=true,halfWidth=width});
                point=points[points.Length-1];
            }
            public void Transfer(float gap,float height)
            {
                // Lower catches follow level accelerator lips; raised catches follow launches.
                point+=forward*gap+Vector3.up*height;
            }
        }
    }
}
