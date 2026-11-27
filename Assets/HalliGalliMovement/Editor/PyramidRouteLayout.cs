using System.Collections.Generic;
using HalliGalli.Movement;
using UnityEngine;

namespace HalliGalli.EditorTools
{
    // Separate catch banks require speed and air steering; no collider spans a flight.
    public static class PyramidRouteLayout
    {
        public static readonly string[] Names={"유적 진입","외벽 회랑","무너진 중앙 홀","수문 통로","왕의 제단"};
        public static readonly float[] Headings={0,20,170,260,350};
        public static SurfCourse.Section[] Room(int index,Vector3 start)
        {
            var p=new Pen(start,Headings[index]);
            switch(index){
                case 0:
                    p.Down(36,20,5.4f,20);
                    p.Up(18,4.5f,5.4f);
                    p.Transfer(20,3);
                    p.Down(24,4,5.8f);
                    p.Up(12,2.2f,5.8f);
                    break;
                case 1:
                    // Fold back around the courtyard; the exit faces the entrance, not farther east.
                    p.Down(166,21,5.6f,150);
                    p.Up(12,1.1f,5.6f);
                    p.Transfer(36,-10);
                    p.Down(28,3,4.2f);
                    p.Up(10,2,4.2f);
                    break;
                case 2:
                    // Aim off the launch tangent, then turn again into the lower gallery.
                    p.Down(62,20,5.4f,55);
                    p.Up(18,4.8f,5.4f);
                    p.Transfer(18,4,6,20);
                    p.Down(24,4,4.2f);
                    p.Transfer(17,-12,4.5f,15);
                    p.Down(28,3,4.6f);
                    p.Up(12,2.2f,4.6f);
                    break;
                case 3:
                    // A full quarter-turn returns along the opposite side of the ruins.
                    p.Down(96,16,5.8f,90);
                    p.Up(12,1,5.8f);
                    p.Transfer(32,-14);
                    p.Down(26,7,4.6f);
                    p.Up(18,5.5f,4.6f);
                    p.Transfer(25,6);
                    p.Down(22,3,4.2f);
                    p.Up(12,2.2f,4.2f);
                    break;
                default:
                    p.Down(34,24,5.2f,10);
                    p.Up(18,5.5f,5.2f);
                    p.Transfer(22,5.5f,4,10);
                    p.Down(26,5,3.8f);
                    p.Up(14,2.5f,3.8f);
                    break;
            }
            p.sections[0].entry=index==0?new Vector3(0,.375f,6.1f):start-p.InitialForward*2.4f+Vector3.up;
            return p.sections.ToArray();
        }
        sealed class Pen
        {
            public readonly List<SurfCourse.Section> sections=new List<SurfCourse.Section>();
            public readonly Vector3 InitialForward;
            float heading;
            Vector3 point;

            public Pen(Vector3 start,float heading)
            {
                point=start;this.heading=heading;
                InitialForward=Quaternion.Euler(0,heading,0)*Vector3.forward;
            }
            public void Down(float length,float drop,float width,float turn=0)=>Ramp(length,-drop,width,turn);
            public void Up(float length,float rise,float width,float turn=0)=>Ramp(length,rise,width,turn);
            void Ramp(float length,float change,float width,float turn)
            {
                int steps=Mathf.CeilToInt(length/.5f);
                var points=new Vector3[steps+1];var headings=new float[steps+1];
                Vector3 center=point;
                for(int i=0;i<=steps;i++){
                    float distance=length*i/steps,t=i/(float)steps;
                    // Ease only the ends: a broad U-turn must not tighten halfway around.
                    const float turnEase=.1f;
                    float eased=t<turnEase?t*t/(2*turnEase*(1-turnEase)):
                        t>1-turnEase?1-(1-t)*(1-t)/(2*turnEase*(1-turnEase)):(t-turnEase*.5f)/(1-turnEase);
                    headings[i]=heading+turn*eased;
                    if(i>0){
                        float yaw=(headings[i-1]+headings[i])*.5f;
                        center+=Quaternion.Euler(0,yaw,0)*Vector3.forward*(length/steps);
                    }
                    float height;
                    if(change<0){
                        float transition=length*.4f,straight=length-transition;
                        float ease=Mathf.Clamp01((distance-straight)/transition);
                        float integral=ease-ease*ease*ease+.5f*ease*ease*ease*ease;
                        height=change*(Mathf.Min(distance,straight)+transition*integral)/(straight+transition*.5f);
                    }else{
                        // A rising-only quartic launch: no recessed centre or abrupt uphill step.
                        height=change*t*t*t*t;
                    }
                    points[i]=center+Vector3.up*height;
                }
                sections.Add(new SurfCourse.Section{points=points,headings=headings,side=-1,twoSided=true,halfWidth=width});
                point=points[steps];heading=headings[steps];
            }
            public void Transfer(float gap,float height,float lateral=0,float turn=0)
            {
                point+=Quaternion.Euler(0,heading,0)*new Vector3(lateral,0,gap)+Vector3.up*height;
                heading+=turn;
            }
        }
    }
}
