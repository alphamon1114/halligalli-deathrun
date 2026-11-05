using HalliGalli.Movement;
using UnityEngine;
using UnityEngine.Rendering;

namespace HalliGalli.EditorTools
{
    public static class PyramidCourseObstacles
    {
        public static void Install(PyramidCourse course)
        {
            var root=new GameObject("Pyramid course cues").transform;
            var surf=course.GetComponent<SurfCourse>();
            var accent=PyramidArtBuilder.Material("Jump landing turquoise",new Color(.17f,.75f,.72f),true);
            var basalt=PyramidArtBuilder.Material("Basalt",new Color(.12f,.14f,.15f));
            for(int roomIndex=0;roomIndex<course.chambers.Length;roomIndex++){
                var room=course.chambers[roomIndex];
                for(int i=room.firstSection;i<room.lastSection;i++){
                    var from=surf.sections[i];var to=surf.sections[i+1];
                    if(!PyramidArtBuilder.IsAirTransfer(from,to))continue;
                    // Ground the two quiet side beacons outside the full riding width.
                    // Their heads follow the catch level, not the launch or chamber ceiling.
                    var rotation=Quaternion.Euler(0,to.headings[0],0);
                    float floor=room.origin.y+room.localMinimum.y+.25f;
                    float top=to.points[0].y+1.8f;
                    for(int side=-1;side<=1;side+=2){
                        Vector3 center=to.points[0]+rotation*(Vector3.right*(side*(surf.laneX+to.halfWidth+2))+Vector3.forward*2.5f);
                        var support=PyramidArtBuilder.Box(root,"Catch beacon pier "+(i+1),new Vector3(center.x,(floor+top)*.5f,center.z),
                            new Vector3(1.1f,top-floor,1.8f),basalt);
                        support.transform.rotation=rotation;
                        var marker=PyramidArtBuilder.Box(root,"Catch beacon "+(i+1),center+Vector3.up*.5f-rotation*Vector3.forward*.94f,
                            new Vector3(.5f,2.4f,.14f),accent);
                        marker.transform.rotation=rotation;marker.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
                    }
                }
                if(roomIndex+1<course.chambers.Length)ExitArrow(root,room,course.chambers[roomIndex+1],roomIndex,accent);
            }
        }
        static void ExitArrow(Transform root,PyramidCourse.Chamber room,PyramidCourse.Chamber next,int index,Material accent)
        {
            var forward=next.Rotation*Vector3.forward;var right=next.Rotation*Vector3.right;
            var cue=new GameObject("Checkpoint "+(index+1)+" exit arrow").transform;cue.SetParent(root);
            var tip=room.landing+right*7+forward*2+Vector3.up*.04f;
            ArrowStroke(cue,tip-forward*2,tip,accent);
            foreach(int side in new[]{-1,1})ArrowStroke(cue,tip-forward*.7f+right*side*.5f,tip,accent);
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
