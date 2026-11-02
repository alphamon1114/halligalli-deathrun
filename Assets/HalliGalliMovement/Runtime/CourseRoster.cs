using System;
using System.Linq;
using UnityEngine;

namespace HalliGalli.Movement
{
    [DefaultExecutionOrder(-200)]
    public sealed class CourseRoster:MonoBehaviour
    {
        public CourseSession session;
        [Range(2,6)] public int playerCount=6;
        public CourseParticipant[] allParticipants;
        public Transform[] chairs,firstPlatforms,routeRoots,drawAnchors,openAnchors,cardLabels,startLabels;
        public Transform mergeSurface;
        public Transform startLine;
        public CourseBranchRoutes branches;
        public SurfCourse surf;
        public const float SeatSpacing=1.5f,SeatZ=-1.85f,StartZ=-.7f,FirstZ=2.3f;
        public static readonly Vector3 CardCenter=new Vector3(0,.891f,-5.1f);
        public int PlayerCount=>playerCount;
        void Awake(){ApplyLayout(playerCount);}
        public void StartNewGame(int count){ApplyLayout(count);session.ResetPractice();}
        public Transform[] RouteForSeat(int seat)=>routeRoots[seat].Cast<Transform>().Take(branches?branches.RouteLength(seat,playerCount):int.MaxValue).ToArray();
        public void ApplyLayout(int count)
        {
            if(count<2||count>6)throw new ArgumentOutOfRangeException(nameof(count));
            playerCount=count;
            if(startLine){startLine.position=new Vector3(0,.013f,StartZ);startLine.localScale=new Vector3(count*SeatSpacing,.012f,.07f);}
            bool itemRace=session.maps&&session.maps.current&&session.maps.current.style==CourseMapStyle.Race;
            for(int i=0;i<6;i++){
                bool active=i<count;var p=allParticipants[i];
                p.gameObject.SetActive(active);chairs[i].gameObject.SetActive(active);firstPlatforms[i].gameObject.SetActive(active);routeRoots[i].gameObject.SetActive(active);cardLabels[i].gameObject.SetActive(active);startLabels[i].gameObject.SetActive(active);
                drawAnchors[i].gameObject.SetActive(active);openAnchors[i].gameObject.SetActive(active);
                if(!active)continue;
                float x=(i-(count-1)*.5f)*SeatSpacing;
                chairs[i].SetPositionAndRotation(new Vector3(x,0,SeatZ),Quaternion.Euler(0,180,0));
                p.seatAnchor.SetPositionAndRotation(new Vector3(x,.025f,SeatZ),Quaternion.Euler(0,180,0));
                p.standAnchor.SetPositionAndRotation(new Vector3(x,.025f,StartZ),Quaternion.identity);
                p.retryAnchor.SetPositionAndRotation(p.standAnchor.position,Quaternion.identity);
                firstPlatforms[i].position=new Vector3(x,.15f,FirstZ);
                startLabels[i].position=new Vector3(x,.03f,-.25f);
                var route=RouteForSeat(i);
                if(!session.userMap&&!itemRace){
                    if(route.Length>0)route[0].position=new Vector3(x,.175f,FirstZ);
                    if(route.Length>1)route[1].position=new Vector3(x,.375f,5.7f);
                    if(branches)branches.Apply(route,i,count);
                    if(surf)surf.Apply(route,i,count);
                }
                var bot=p.GetComponent<CourseRival>();if(bot)bot.route=route;
                float angle=(count==2?180:270)+i*360f/count;
                Vector3 direction=new Vector3(Mathf.Cos(angle*Mathf.Deg2Rad),0,Mathf.Sin(angle*Mathf.Deg2Rad));
                float radius=count==2?.66f:1.18f,drawRadius=count==2?1.6f:2.12f;
                openAnchors[i].SetPositionAndRotation(CardCenter+direction*radius,Quaternion.identity);
                drawAnchors[i].SetPositionAndRotation(CardCenter+direction*drawRadius,Quaternion.identity);
                cardLabels[i].position=CardCenter+direction*(count==2?radius+.5f:radius-.52f)+Vector3.up*.003f;
            }
            session.participants=allParticipants.Take(count).ToArray();
            session.cardGame.display.drawAnchors=drawAnchors.Take(count).ToArray();session.cardGame.display.openAnchors=openAnchors.Take(count).ToArray();
            float width=surf?10.5f:Mathf.Max(4.6f,(count-1)*SeatSpacing+1.5f);
            mergeSurface.localScale=new Vector3(width,.44f,2.2f);
            if(session.userMap)session.userMap.ApplyRoster(this);
            if(!CourseNetwork.Active)ApplyOfflineShirtColors();
        }
        public void ApplyOfflineShirtColors()
        {
            int local=PlayerShirtColors.SavedIndex,next=0;
            foreach(var participant in allParticipants){
                if(!participant)continue;
                if(participant.localPlayer){PlayerShirtAppearance.Apply(participant,local);continue;}
                if(next==local)next++;
                PlayerShirtAppearance.Apply(participant,next++);
            }
        }
    }
}
