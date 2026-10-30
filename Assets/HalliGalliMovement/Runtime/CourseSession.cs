using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace HalliGalli.Movement
{
    public sealed class CourseSession : MonoBehaviour
    {
        public CourseParticipant[] participants;
        public CourseCardGame cardGame;
        public CourseRoster roster;
        public CourseMapController maps;
        public CourseCheckpoints checkpoints;
        public CombatPracticeRoom practiceRoom;
        public FactoryCourse factory;
        public TutorialFerryCourse ferry;
        public UserMapRuntime userMap;
        public bool HasCardRules=>cardGame&&cardGame.enabled&&cardGame.Match!=null;
        public Transform bellTarget,bellButton;
        public AudioSource bellAudio;
        public float fallHeight=-3.2f;
        public UnityEvent onDeparture=new UnityEvent(),onEveryoneSeated=new UnityEvent(),onBellRung=new UnityEvent();
        public bool RunActive{get;private set;}
        public bool BellRung{get;private set;}
        public CourseParticipant BellRinger{get;private set;}
        public int BellCount{get;private set;}
        public bool EveryoneSeated => participants!=null&&participants.Length>0&&participants.All(p=>p&&p.IsSeated);
        public CourseParticipant Local => participants.First(p=>p.localPlayer);
        float bellAt=-100;Vector3 buttonRest;bool resetting;
        public void ApplyNetworkState(bool running,int bellCount)
        {
            RunActive=running;
            if(bellCount>BellCount){bellAt=Time.time;if(bellAudio&&bellAudio.clip)bellAudio.Play();onBellRung.Invoke();if(cardGame.tableView)cardGame.tableView.Focus();}
            BellCount=bellCount;
        }
        public void SetBell(Transform target,Transform button,AudioSource audio)
        {
            bellTarget=target;bellButton=button;bellAudio=audio;
            buttonRest=bellButton?bellButton.localPosition:Vector3.zero;bellAt=-100;
        }
        void Start(){buttonRest=bellButton?bellButton.localPosition:Vector3.zero;ResetPractice();}
        void Update(){if(bellButton)bellButton.localPosition=buttonRest-Vector3.up*.045f*Mathf.Exp(-(Time.time-bellAt)*12);}
        public bool CanStand(CourseParticipant participant)=>!HasCardRules||cardGame.CanStand(participant);
        public bool MustReturn(CourseParticipant participant)=>BellRung||(HasCardRules&&cardGame.MustReturn(participant));
        public void NotifyStand(CourseParticipant participant){if(HasCardRules)cardGame.Stand(participant);if(!RunActive){RunActive=true;foreach(var p in participants){p.Initialize();p.Lasso.BeginRace();}onDeparture.Invoke();}}
        public void NotifySeated(CourseParticipant participant){if(resetting)return;if(HasCardRules)cardGame.Sit(participant);if(EveryoneSeated){RunActive=BellRung=false;BellRinger=null;foreach(var p in participants){p.Lasso.EndRace();}onEveryoneSeated.Invoke();}}
        public bool CanRing(CourseParticipant participant)
        {
            if(practiceRoom)return false;
            if(BellRung||participant.IsSeated||participant.IsRespawning||participant.Motor.Stunned||!RunActive)return false;
            if(HasCardRules&&!cardGame.Match.CanClaim(cardGame.Index(participant)))return false;
            if(roster&&roster.surf&&!roster.surf.Completed(participant))return false;
            Vector3 target=bellTarget.position;
            return participant.Motor.Grounded && Vector3.Distance(participant.Motor.CurrentEyePosition,target)<1.7f;
        }
        public bool Ring(CourseParticipant participant)
        {
            if(!CanRing(participant))return false;
            if(HasCardRules&&!cardGame.Claim(participant))return false;
            BellRung=true;BellRinger=participant;BellCount++;bellAt=Time.time;
            if(bellAudio&&bellAudio.clip)bellAudio.Play();onBellRung.Invoke();
            if(HasCardRules){
                // A successful bell ends the race immediately; Sit also cancels pending fall respawns.
                foreach(var p in participants)p.Sit();
                if(cardGame.tableView)cardGame.tableView.Focus();
            }
            return true;
        }
        public void ResetPractice(){resetting=true;RunActive=BellRung=false;BellRinger=null;BellCount=0;foreach(var p in participants){p.ResetPractice();p.Lasso.EndRace();}resetting=false;if(cardGame&&cardGame.enabled)cardGame.ResetMatch();}
    }
}
