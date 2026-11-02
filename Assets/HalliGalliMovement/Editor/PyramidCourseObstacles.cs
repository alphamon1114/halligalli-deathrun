using HalliGalli.Movement;
using UnityEngine;
using UnityEngine.Rendering;

namespace HalliGalli.EditorTools
{
    public static class PyramidCourseObstacles
    {
        public static void Install(PyramidCourse course,Material stone)
        {
            var root=new GameObject("Pyramid course cues").transform;
            var surf=course.GetComponent<SurfCourse>();
            var launch=PyramidArtBuilder.Material("Jump launch gold",new Color(1,.64f,.18f),true);
            var landing=PyramidArtBuilder.Material("Jump landing turquoise",new Color(.17f,.75f,.72f),true);
            for(int roomIndex=0;roomIndex<course.chambers.Length;roomIndex++){
                var room=course.chambers[roomIndex];
                // Mark at most two transfers: the widest gap and an opposite-height
                // transfer when present. Runes never outline a floor or span a gap.
                int widest=room.firstSection;float largestGap=float.NegativeInfinity;
                for(int i=room.firstSection;i<room.lastSection;i++){
                    var section=surf.sections[i];var next=surf.sections[i+1];int end=section.points.Length-1;
                    var forward=Quaternion.Euler(0,section.headings[end],0)*Vector3.forward;
                    float gap=Vector3.Dot(next.points[0]-section.points[end],forward);
                    if(gap>largestGap){largestGap=gap;widest=i;}
                }
                int other=room.firstSection,direction=TransferDirection(surf,widest);float oppositeHeight=0;
                for(int i=room.firstSection;i<room.lastSection;i++){
                    int candidate=TransferDirection(surf,i);
                    if(candidate==0||candidate==direction)continue;
                    var section=surf.sections[i];
                    float height=Mathf.Abs(surf.sections[i+1].points[0].y-section.points[section.points.Length-1].y);
                    if(height>oppositeHeight){oppositeHeight=height;other=i;}
                }
                GapCues(root,surf,widest,stone,launch,landing);
                if(other!=widest)GapCues(root,surf,other,stone,launch,landing);
                if(roomIndex+1<course.chambers.Length)TurnArrow(root,room,course.chambers[roomIndex+1],roomIndex,landing);
            }
        }
        static int TransferDirection(SurfCourse surf,int index)
        {
            var section=surf.sections[index];
            float rise=surf.sections[index+1].points[0].y-section.points[section.points.Length-1].y;
            return rise>.05f?1:rise<-.05f?-1:0;
        }
        static void GapCues(Transform root,SurfCourse surf,int index,Material stone,Material launch,Material landing)
        {
            var from=surf.sections[index];var to=surf.sections[index+1];int direction=TransferDirection(surf,index);
            CuePair(root,"Launch "+(index+1),from,from.points.Length-1,surf.laneX,stone,launch,direction);
            CuePair(root,"Landing "+(index+2),to,0,surf.laneX,stone,landing,direction);
        }
        static void CuePair(Transform root,string name,SurfCourse.Section section,int point,float laneX,Material stone,Material accent,int direction)
        {
            var rotation=Quaternion.Euler(0,section.headings[point],0);
            foreach(int side in new[]{-1,1}){
                var cue=new GameObject(name+(side<0?" left rune":" right rune")).transform;cue.SetParent(root);
                // A small floating chevron stays at riding-lane eye level, beyond the
                // outer bank. There is no pillar, hidden proxy, or decorative collider.
                var center=section.points[point]+rotation*Vector3.right*(side*(laneX+section.halfWidth+.8f))+Vector3.up*.9f;
                if(direction==0){
                    foreach(int bar in new[]{-1,1}){
                        var level=PyramidArtBuilder.Box(cue,"Level transfer rune",center+Vector3.up*bar*.13f,new Vector3(.46f,.10f,.12f),accent);
                        level.transform.rotation=rotation;level.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
                    }
                    continue;
                }
                var stem=PyramidArtBuilder.Box(cue,"Rune stem",center-Vector3.up*direction*.5f,new Vector3(.13f,.5f,.13f),stone);
                stem.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
                foreach(int arm in new[]{-1,1}){
                    var offset=new Vector3(arm*.18f,-direction*.18f,0);
                    var chevron=PyramidArtBuilder.Box(cue,direction>0?"Ascending transfer chevron":"Descending transfer chevron",center+rotation*offset,new Vector3(.12f,.62f,.12f),accent);
                    chevron.transform.rotation=rotation*Quaternion.Euler(0,0,arm*direction*45);
                    chevron.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
                }
            }
        }
        static void TurnArrow(Transform root,PyramidCourse.Chamber room,PyramidCourse.Chamber next,int index,Material accent)
        {
            var incoming=room.Rotation*Vector3.forward;
            var outgoing=next.Rotation*Vector3.forward;
            var right=Vector3.Cross(Vector3.up,outgoing);
            var cue=new GameObject("Checkpoint "+(index+1)+" turn arrow").transform;cue.SetParent(root);
            // A small inlay occupies the outside corner, away from the approach and
            // exit lanes. Its only geometry is the bent arrow; there is no floor rim.
            var bend=room.landing+incoming*3.5f-outgoing*3.5f+Vector3.up*.04f;
            var tip=bend+outgoing*1.35f;
            ArrowStroke(cue,bend-incoming*1.35f,bend,accent);
            ArrowStroke(cue,bend,tip,accent);
            foreach(int side in new[]{-1,1})ArrowStroke(cue,tip-outgoing*.55f+right*side*.4f,tip,accent);
        }
        static void ArrowStroke(Transform root,Vector3 from,Vector3 to,Material accent)
        {
            var delta=to-from;
            var stroke=PyramidArtBuilder.Box(root,"Checkpoint direction stroke",(from+to)*.5f,new Vector3(.14f,.035f,delta.magnitude),accent);
            stroke.transform.rotation=Quaternion.LookRotation(delta,Vector3.up);
            stroke.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
        }
    }
}
