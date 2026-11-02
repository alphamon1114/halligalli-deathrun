using UnityEngine;

namespace HalliGalli.Movement
{
    [RequireComponent(typeof(SourceMotor),typeof(MeleeReceiver),typeof(MeleeCombat))]
    public sealed class CourseParticipant : MonoBehaviour
    {
        public CourseSession session;
        public Transform seatAnchor,standAnchor,retryAnchor;
        public bool localPlayer;
        public bool IsSeated{get;private set;}
        public bool IsRespawning{get;private set;}
        public int Falls{get;private set;}
        public int RespawnSequence{get;private set;}
        public int CheckpointIndex{get;private set;}=-1;
        public float CheckpointSide{get;private set;}
        public bool HasCheckpoint=>!IsSeated&&session.checkpoints&&session.checkpoints.Saved(this)!=null&&!session.MustReturn(this);
        internal void SetCheckpoint(int index,float side){CheckpointIndex=index;CheckpointSide=side;}
        public SourceMotor Motor{get;private set;}
        public MeleeCombat Combat{get;private set;}
        public MeleeReceiver Receiver{get;private set;}
        public bool Racing=>session&&session.RunActive&&!IsSeated&&!IsRespawning&&!session.MustReturn(this);
        public LassoTool Lasso{get;private set;}
        SourcePlayerController controller;CharacterMotionDriver driver;CourseBodyAnimator bodyAnimator;
        float respawnAt;
        void Awake(){Initialize();}
        public void Initialize(){if(Motor)return;Motor=GetComponent<SourceMotor>();Combat=GetComponent<MeleeCombat>();Receiver=GetComponent<MeleeReceiver>();controller=GetComponent<SourcePlayerController>();driver=GetComponent<CharacterMotionDriver>();bodyAnimator=GetComponent<CourseBodyAnimator>();Lasso=GetComponent<LassoTool>();if(!Lasso)Lasso=gameObject.AddComponent<LassoTool>();}
        void Update(){if(CourseNetwork.Active&&!CourseNetwork.Authority)return;if(IsRespawning)Motor.AdvanceStun(Time.deltaTime);if(!IsSeated&&!IsRespawning&&transform.position.y<session.fallHeight)BeginFall();if(IsRespawning&&Time.time>=respawnAt)Respawn();}
        public bool ApplyNetworkStatus(bool seated,bool respawning,int falls,int respawns,float facing=float.NaN)
        {
            bool changed=IsSeated!=seated||IsRespawning!=respawning||RespawnSequence!=respawns;
            IsSeated=seated;IsRespawning=respawning;Falls=falls;RespawnSequence=respawns;
            if(seated){SetCheckpoint(-1,0);Motor.ClearStun();}
            if(changed){
                SetMobility(!seated&&!respawning);
                if(controller)controller.ResetView(seated?seatAnchor.eulerAngles.y:float.IsNaN(facing)?standAnchor.eulerAngles.y:facing,seated?22:0);
                if(localPlayer&&session.cardGame.tableView)session.cardGame.tableView.ClearFocus();
            }
            return changed;
        }
        void SetMobility(bool mobile)
        {
            if(!mobile&&RaceCourse.Active&&RaceCourse.Active.session==session)RaceCourse.Active.ClearParticipant(this);
            if(!mobile&&Lasso){Lasso.CancelConnection();if(session&&session.participants!=null)foreach(var p in session.participants)if(p&&p.Lasso)p.Lasso.ReleaseIfTarget(this);}
            if(controller){controller.movementEnabled=controller.actionsEnabled=mobile;controller.cameraHeightOffset=IsSeated?-.42f:0;}
            if(driver)driver.seated=IsSeated;if(bodyAnimator)bodyAnimator.seated=IsSeated;
            Receiver.canReceiveHits=mobile;Combat.actionsEnabled=mobile;Combat.ResetCombat();Receiver.ResetReaction();
            GetComponent<CharacterController>().enabled=mobile;
        }
        public void Sit()
        {
            Initialize();bool wasSeated=IsSeated;IsSeated=true;IsRespawning=false;SetCheckpoint(-1,0);
            Motor.ClearStun();
            Motor.Teleport(seatAnchor.position);transform.rotation=seatAnchor.rotation;
            if(controller)controller.ResetView(seatAnchor.eulerAngles.y,22);
            SetMobility(false);if(!wasSeated)session.NotifySeated(this);
        }
        public bool Stand()
        {
            if(!IsSeated||!session.CanStand(this))return false;IsSeated=false;SetCheckpoint(-1,0);SetMobility(true);
            Motor.Teleport(standAnchor.position);transform.rotation=standAnchor.rotation;
            if(controller)controller.ResetView(standAnchor.eulerAngles.y);
            session.NotifyStand(this);return true;
        }
        public bool CanSit => !IsSeated&&!IsRespawning&&!Motor.Stunned&&Vector3.Distance(transform.position,seatAnchor.position)<2.1f;
        public bool RequestRespawn()
        {
            if(IsSeated||IsRespawning||!session.RunActive||session.HasCardRules&&session.cardGame.Match.Finished)return false;
            // Share the fall delay and cleanup; never refill cards or reset ability cooldowns.
            BeginFall();return true;
        }
        public void BeginFall(){if(IsRespawning||IsSeated)return;Falls++;IsRespawning=true;respawnAt=Time.time+.7f;SetMobility(false);}
        public void Respawn()
        {
            IsSeated=IsRespawning=false;SetMobility(true);var anchor=HasCheckpoint?session.checkpoints.Saved(this).spawn:session.MustReturn(this)?standAnchor:retryAnchor;Motor.Teleport(HasCheckpoint?session.checkpoints.SpawnPosition(this):anchor.position);
            transform.rotation=anchor.rotation;if(controller)controller.ResetView(anchor.eulerAngles.y);RespawnSequence++;
            if(session.checkpoints)session.checkpoints.RestoreSurfProgress(this);
        }
        public void ResetPractice(){Falls=0;RespawnSequence++;Sit();}
    }
}
