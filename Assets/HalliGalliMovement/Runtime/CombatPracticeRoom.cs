using System.Collections;
using UnityEngine;

namespace HalliGalli.Movement
{
    public sealed class CombatPracticeRoom:MonoBehaviour
    {
        public const string ScenePath="Assets/HalliGalliMovement/Demo/CombatPractice.unity";
        public CourseSession session;
        public bool Active{get;private set;}
        readonly float[] restoreAt=new float[6];
        public void Enter()
        {
            Active=true;session.cardGame.enabled=false;session.cardGame.botEnabled=false;
            session.cardGame.tableView.ClearFocus();session.ResetPractice();
            for(int i=0;i<session.participants.Length;i++){
                var p=session.participants[i];var bot=p.GetComponent<CourseRival>();if(bot)bot.enabled=false;
                Vector3 point=p.localPlayer?new Vector3(0,.025f,-6):new Vector3((i-3)*3,.025f,2+(i%2)*3);
                var facing=Quaternion.Euler(0,p.localPlayer?0:180,0);
                p.standAnchor.SetPositionAndRotation(point,facing);p.retryAnchor.SetPositionAndRotation(point,facing);
                p.Stand();p.Combat.readInput=p.localPlayer;p.Combat.simulateAutomatically=true;restoreAt[i]=0;
            }
            var control=session.Local.GetComponent<SourcePlayerController>();control.controlsEnabled=true;control.suppressCursorCapture=false;
            if(!Application.isBatchMode)control.SetCursorCaptured(true);
        }
        public void Exit(){Active=false;}
        void FixedUpdate()
        {
            if(!Active||CourseNetwork.CapturesPointer)return;
            for(int i=0;i<session.participants.Length;i++){
                var p=session.participants[i];if(p.localPlayer||p.IsRespawning)continue;
                p.Motor.Simulate(new MovementCommand{yaw=180},Time.fixedDeltaTime);
                bool displaced=Vector3.Distance(p.transform.position,p.retryAnchor.position)>.2f;
                if(displaced&&restoreAt[i]<=0)restoreAt[i]=Time.time+3;
                if(restoreAt[i]>0&&Time.time>=restoreAt[i]){
                    p.BeginFall();restoreAt[i]=0;
                }
            }
        }
    }
}
