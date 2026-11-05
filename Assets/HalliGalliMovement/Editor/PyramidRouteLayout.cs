using System.Collections.Generic;
using HalliGalli.Movement;
using UnityEngine;

namespace HalliGalli.EditorTools
{
    // Independent banks share a takeoff tangent, never a collider across the flight.
    public static class PyramidRouteLayout
    {
        public static readonly string[] Names={"유적 진입","외벽 회랑","무너진 중앙 홀","수문 통로","왕의 제단"};
        public static readonly float[] Headings={0,12,54,28,40};
        public static SurfCourse.Section[] Room(int index,Vector3 start)
        {
            var p=new Pen(start,Headings[index]);
            switch(index){
                case 0:
                    p.Down(36,20,5.4f,12);
                    p.Up(18,4.5f,5.4f);
                    p.Transfer(20,3);
                    p.Down(24,4,5.8f);
                    p.Up(12,2.2f,5.8f);
                    break;
                case 1:
                    // The outside sweep launches across a broken wall to a lower terrace.
                    p.Down(60,17,5.6f,42);
                    p.Up(12,1.1f,5.6f);
                    p.Transfer(24,-12);
                    p.Down(24,3,5.8f);
                    p.Up(12,2.2f,5.8f);
                    break;
                case 2:
                    // A modest lip reaches the suspended bank, rather than climbing a U.
                    p.Down(32,20,5.4f,-14);
                    p.Up(18,4.8f,5.4f);
                    p.Transfer(20,4);
                    p.Down(26,5,6,-12);
                    p.Up(12,2.2f,6);
                    break;
                case 3:
                    // Open watergate: a long falling flight, then a broad lower catch.
                    p.Down(28,12,5.4f);
                    p.Up(12,1,5.4f);
                    p.Transfer(26,-14);
                    p.Down(24,3,5.8f,12);
                    p.Up(12,2.2f,5.8f);
                    break;
                default:
                    p.Down(34,20,5.6f);
                    p.Up(18,4.8f,5.6f);
                    p.Transfer(20,4);
                    p.Down(26,5,6);
                    p.Up(14,2.5f,6);
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
                    // Smooth heading rate at both ends; even the outside sweep has radius >60m.
                    float eased=t*t*(3-2*t);
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
            public void Transfer(float gap,float height)
            {
                point+=Quaternion.Euler(0,heading,0)*Vector3.forward*gap+Vector3.up*height;
            }
        }
    }
}
