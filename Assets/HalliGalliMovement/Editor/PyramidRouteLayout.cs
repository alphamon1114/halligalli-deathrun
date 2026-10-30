using System.Collections.Generic;
using System.Linq;
using HalliGalli.Movement;
using UnityEngine;

namespace HalliGalli.EditorTools
{
    // Each room has its own sequence of momentum and transfer challenges.
    public static class PyramidRouteLayout
    {
        public static readonly int[] EntrySides={-1,-1,-1,-1,-1};
        public static readonly string[] Names={"태양의 낙하로","오벨리스크 나선","붕괴한 수문","공중 제단","왕의 도약"};
        public static SurfCourse.Section[] Room(int index,Vector3 start)
        {
            var p=new Pen(start,EntrySides[index]);
            switch(index){
                case 0:
                    p.Run(30,10);p.Turn(65,20,3);p.Run(24,5,2.6f);
                    p.Transfer(5,2);p.Turn(-65,24,4,2.8f,10,4);break;
                case 1:
                    p.Run(40,8);p.Turn(180,18,7);p.Run(22,5);
                    p.Transfer(4,2);p.Turn(-180,16,6);p.Run(52,6,3,12,4);break;
                case 2:
                    p.Run(30,9);p.Turn(-90,16,4);p.Run(24,4,2.5f);
                    p.Transfer(7,7);p.Turn(90,20,4);p.Run(34,3,2.8f,10,4);break;
                case 3:
                    p.Run(24,6);p.Turn(45,22,4);
                    p.Transfer(4,1.5f);p.Turn(-90,20,5);
                    p.Transfer(5,2);p.Turn(45,22,3);
                    p.Run(22,4,2.6f,8,3.5f);p.Transfer(7,0);p.Run(42,8,2.6f,12,3.5f);break;
                default:
                    p.Run(36,9);p.Turn(-135,22,7);p.Run(24,4,2.4f);
                    p.Transfer(6,2);p.Turn(135,20,7);p.Run(34,3,2.7f,14,5);break;
            }
            p.sections[0].entry=index==0?new Vector3(0,.375f,6.1f):start+Vector3.back*2.4f+Vector3.up;
            return p.sections.ToArray();
        }
        sealed class Pen
        {
            public readonly List<SurfCourse.Section> sections=new List<SurfCourse.Section>();
            Vector3 point;float heading;int side;
            Vector3 Forward=>Quaternion.Euler(0,heading,0)*Vector3.forward;
            
            public Pen(Vector3 start,int bankSide){point=start;side=bankSide;}
            public void Run(float length,float drop,float width=3,float kicker=0,float rise=0)
            {
                var points=new List<Vector3>{point,point+Forward*length+Vector3.down*drop};
                Vector3 low=points.Last();
                if(kicker>0)for(int i=1;i<=24;i++){float t=i/24f;points.Add(low+Forward*kicker*t+Vector3.up*rise*t*t);}
                Add(new SurfCourse.Section{points=points.ToArray(),headings=points.Select(_=>heading).ToArray(),side=side,halfWidth=width});
            }
            public void Turn(float turn,float radius,float drop,float width=3,float kicker=0,float rise=0)
            {
                var section=SurfCourseBuilder.Curve(point,heading,radius,point.y-drop,point.y-drop+rise,kicker,turn);
                section.side=side;section.halfWidth=width;Add(section);heading+=turn;
            }
            public void Transfer(float gap,float drop)
            {
                // Both faces continue across the gap. No forced lateral transfer or bank reversal.
                point+=Forward*gap+Vector3.down*drop;
            }
            void Add(SurfCourse.Section section){section.twoSided=true;sections.Add(section);point=section.points.Last();}
        }
    }
}
