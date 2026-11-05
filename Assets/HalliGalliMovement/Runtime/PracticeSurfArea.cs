using UnityEngine;

namespace HalliGalli.Movement
{
    // Offline lesson routing only. All acceleration and collisions remain in SourceMotor.
    [DefaultExecutionOrder(90), DisallowMultipleComponent]
    public sealed class PracticeSurfArea : MonoBehaviour
    {
        CombatPracticeRoom room;
        CourseParticipant player;
        SourcePlayerController controller;
        CharacterController capsule;
        SourceMovementProfile combatProfile, surfProfile;
        PracticeSurfGeometry geometry;
        Vector3 combatStart;
        Quaternion combatFacing;
        bool touchedBank, crossedGap, caughtBank;
        float airborneSeconds;
        int respawnSequence;

        public bool Available => surfProfile && geometry && geometry.Stages != null;
        public int SelectedStage { get; private set; } = -1;
        public bool Completed { get; private set; }
        public static string StageName(int index)
        {
            switch(index) {
                case 0: return "경사면 타기";
                case 1: return "낮은 서프로 갈아타기";
                case 2: return "높은 서프에 착지하기";
                default: return "공격 연습";
            }
        }

        internal void Enter(CombatPracticeRoom owner)
        {
            if(!room) {
                room=owner;player=room.session.Local;player.Initialize();
                controller=player.GetComponent<SourcePlayerController>();
                capsule=player.GetComponent<CharacterController>();
                combatProfile=player.Motor.profile;
                var catalog=room.session.maps?room.session.maps.catalog:null;
                if(catalog&&catalog.maps!=null)foreach(var map in catalog.maps)
                    if(map&&map.id=="surf"&&map.movementProfile&&map.movementProfile.surfEnabled) {
                        surfProfile=map.movementProfile;break;
                    }
                if(surfProfile) {
                    var root=new GameObject("Practice surf lessons");
                    geometry=root.AddComponent<PracticeSurfGeometry>();
                    var floor=GameObject.Find("Practice floor");
                    var surface=floor?floor.GetComponentInChildren<Renderer>(true):null;
                    geometry.Build(surface?surface.sharedMaterial:null);
                } else Debug.LogError("Practice surf needs the official surf movement profile in the map catalog.",this);
            }
            combatStart=player.standAnchor.position;combatFacing=player.standAnchor.rotation;
            SelectedStage=-1;ResetAttempt();ApplyProfile(combatProfile);
        }

        public void SelectStage(int index)
        {
            if(!room||!room.Active||CourseNetwork.Active||index < -1||index > 2||index>=0&&!Available)return;
            SelectedStage=index;
            ApplyProfile(index<0?combatProfile:surfProfile);
            Vector3 start=index<0?combatStart:geometry.Stages[index].start;
            Quaternion facing=index<0?combatFacing:Quaternion.identity;
            player.standAnchor.SetPositionAndRotation(start,facing);
            player.retryAnchor.SetPositionAndRotation(start,facing);
            player.SetCheckpoint(-1,0);
            player.Motor.ClearStun();
            if(player.Lasso)player.Lasso.CancelConnection();
            player.Respawn();ResetAttempt();
        }

        internal void Leave()
        {
            if(room&&room.Active&&player)SelectStage(-1);
        }

        void ApplyProfile(SourceMovementProfile profile)
        {
            // SourceMotor initializes its capsule once; update the same settings on a local profile switch.
            bool mobile=capsule.enabled;capsule.enabled=false;
            player.Motor.profile=profile;
            capsule.radius=profile.radius;capsule.height=profile.standingHeight;
            capsule.center=Vector3.up*(profile.standingHeight*.5f);
            capsule.skinWidth=profile.skinWidth;capsule.stepOffset=profile.stepHeight;
            capsule.slopeLimit=profile.surfEnabled?90:profile.slopeLimit;
            capsule.enabled=mobile;
            if(controller&&controller.playerCamera)controller.playerCamera.fieldOfView=profile.fieldOfView;
        }

        void ResetAttempt()
        {
            touchedBank=crossedGap=caughtBank=Completed=false;airborneSeconds=0;
            respawnSequence=player.RespawnSequence;
        }

        void FixedUpdate()
        {
            if(!room||!room.Active||SelectedStage<0||!Available||CourseNetwork.Active||CourseNetwork.CapturesPointer)return;
            var stage=geometry.Stages[SelectedStage];
            // R and missed landings return only this player to this lesson, without restarting the room.
            if(player.IsRespawning) { player.Respawn();ResetAttempt();return; }
            if(player.RespawnSequence!=respawnSequence)ResetAttempt();
            Vector3 feet=player.transform.position;
            if(feet.y<stage.fallY||!stage.limits.Contains(feet)) {
                player.BeginFall();player.Respawn();ResetAttempt();return;
            }
            var motor=player.Motor;
            if(motor.Surfing) {
                if(crossedGap&&stage.catchZone.Contains(feet))caughtBank=true;
                touchedBank=true;airborneSeconds=0;
            } else if(touchedBank&&!motor.Grounded) {
                airborneSeconds+=Time.fixedDeltaTime;
                if(airborneSeconds>=.1f)crossedGap=true;
            } else airborneSeconds=0;
            if(!Completed&&touchedBank&&(SelectedStage==0||caughtBank)&&motor.Grounded&&stage.finish.Contains(feet))Completed=true;
        }

        void OnDestroy()
        {
            if(player&&capsule&&combatProfile)ApplyProfile(combatProfile);
            if(geometry)Destroy(geometry.gameObject);
        }
    }
}
