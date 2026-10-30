using System.Linq;
using HalliGalli.Movement;
using UnityEngine;

namespace HalliGalli.EditorTools
{
    public static class PyramidCourseObstacles
    {
        public static void Install(PyramidCourse course,Material stone)
        {
            var root=new GameObject("Pyramid course obstacles").transform;
            var surf=course.GetComponent<SurfCourse>();
            // Place openings along the actual ride lane, never across its collision surface.
            Aperture(root,course.chambers[0],surf.sections[0],.72f,stone);
            Aperture(root,course.chambers[2],surf.sections[course.chambers[2].firstSection+2],.65f,stone);
            Aperture(root,course.chambers[3],surf.sections[course.chambers[3].firstSection+4],.55f,stone);
            Aperture(root,course.chambers[4],surf.sections[course.chambers[4].firstSection+2],.6f,stone);
            var spiral=course.chambers[1];
            foreach(int offset in new[]{1,3}){
                var arc=surf.sections[spiral.firstSection+offset];
                var center=(arc.points[0]+arc.points.Last())*.5f;
                float top=arc.points[0].y+16,baseY=spiral.minimum.y;
                var shaft=PyramidArtBuilder.Box(root,"Spiral central shaft",new Vector3(center.x,(baseY+top)*.5f,center.z),new Vector3(10,top-baseY,10),stone,true);
                shaft.GetComponent<Renderer>().enabled=false;
                var column=PyramidVarcoImporter.Place(root,"LotusColumn",new Vector3(center.x,baseY,center.z),top-baseY);
                var size=column.GetComponentInChildren<Renderer>().bounds.size;var scale=column.transform.localScale;
                column.transform.localScale=new Vector3(scale.x*14/size.x,scale.y,scale.z*14/size.z);
                PyramidVarcoImporter.Place(root,"Anubis",new Vector3(center.x,top,center.z),8,offset==1?180:0);
            }
        }
        static void Aperture(Transform root,PyramidCourse.Chamber room,SurfCourse.Section section,float fraction,Material stone)
        {
            // These selected sections are straight before their kicker, so the opening is perpendicular to travel.
            Vector3 center=Vector3.Lerp(section.points[0],section.points[1],fraction);
            var rotation=Quaternion.Euler(0,section.headings[0],0);
            if(!section.twoSided)center+=rotation*Vector3.right*(section.side*3.2f);
            var frame=new GameObject("Pyramid aperture").transform;frame.SetParent(root);
            float halfOpening=section.twoSided?7.4f:3.8f;
            float low=center.y-4.8f,high=center.y+7.5f,bottom=room.minimum.y,top=high+4;
            foreach(int side in new[]{-1,1}){
                var at=center+rotation*Vector3.right*(side*(halfOpening+3));at.y=(bottom+top)*.5f;
                var pillar=PyramidArtBuilder.Box(frame,"Aperture stone pier",at,new Vector3(6,top-bottom,2),stone,true);pillar.transform.rotation=rotation;
            }
            foreach(bool upper in new[]{false,true}){
                float a=upper?high:bottom,b=upper?top:low;
                var at=center;at.y=(a+b)*.5f;
                var slab=PyramidArtBuilder.Box(frame,upper?"Aperture lintel":"Aperture sill",at,new Vector3(halfOpening*2,b-a,2),stone,true);slab.transform.rotation=rotation;
            }
            var gate=PyramidVarcoImporter.Place(frame,"StoneGate",new Vector3(center.x,low-.2f,center.z)-rotation*Vector3.forward*.9f,17.5f,section.headings[0]+180);
            if(section.twoSided){var scale=gate.transform.localScale;scale.x*=1.95f;gate.transform.localScale=scale;}
        }
    }
}
