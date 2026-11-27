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
            var space=new PyramidArtBuilder.RouteSpace(course,surf);
            var decoration=GameObject.Find("Pyramid decoration");
            if(decoration)space.ReserveDecoration(decoration.transform);
            for(int roomIndex=0;roomIndex<course.chambers.Length;roomIndex++){
                var room=course.chambers[roomIndex];
                for(int i=room.firstSection;i<room.lastSection;i++){
                    var from=surf.sections[i];var to=surf.sections[i+1];
                    if(!PyramidArtBuilder.IsAirTransfer(from,to))continue;
                    // Short catch-level plinths never extend down through a lower return.
                    // Test both sides against every bank and flight, including other rooms.
                    var frame=PyramidArtBuilder.FrameAt(to,0);frame.center+=frame.forward*2.5f;
                    float foot=-to.halfWidth*Mathf.Tan(55*Mathf.Deg2Rad)-3,top=1.8f;
                    for(int side=-1;side<=1;side+=2){
                        var beacon=new GameObject("Catch beacon group "+(i+1)+" "+side).transform;beacon.SetParent(root);
                        PyramidArtBuilder.Box(beacon,"Catch beacon pier "+(i+1),Vector3.up*((foot+top)*.5f),
                            new Vector3(1.1f,top-foot,1.8f),basalt);
                        var marker=PyramidArtBuilder.Box(beacon,"Catch beacon "+(i+1),new Vector3(0,.5f,-.94f),
                            new Vector3(.5f,2.4f,.14f),accent);
                        marker.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
                        space.Place(beacon,frame,frame.right*side,surf.laneX+to.halfWidth+3.5f,0);
                    }
                }
                if(roomIndex+1<course.chambers.Length)ExitArrow(root,room,surf.sections[course.chambers[roomIndex+1].firstSection],roomIndex,accent);
            }
        }
        static void ExitArrow(Transform root,PyramidCourse.Chamber room,SurfCourse.Section next,int index,Material accent)
        {
            var frame=PyramidArtBuilder.FrameAt(next,0);var forward=frame.forward;var right=frame.right;
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
