using UnityEngine;

namespace HalliGalli.Movement
{
    public sealed partial class RaceCourse
    {
        const float BarricadeHeight=2.2f, BarricadeThickness=.6f, BarricadeLifetime=8, BarricadeBehind=3;
        const float BarricadeProbeStep=.25f, BarricadeFloorTolerance=.025f, BarricadeClearance=.03f;
        const int BarricadeProbeSteps=512;
        readonly BoxCollider[] barricadeColliders=new BoxCollider[MaxEntities-BananaEnd];
        GameObject barricadeCollisionRoot;

        void InitializeBarricades()
        {
            // World-space, unit-scale collision does not depend on a rendered visual or the map root's scale.
            barricadeCollisionRoot=new GameObject("Race barricade collision pool");
            for(int i=0;i<barricadeColliders.Length;i++){
                var node=new GameObject("Barricade collision "+i);
                node.transform.SetParent(barricadeCollisionRoot.transform,false);
                var collider=node.AddComponent<BoxCollider>();collider.enabled=false;
                barricadeColliders[i]=collider;
            }
        }
        void DisposeBarricades()
        {
            if(!barricadeCollisionRoot)return;
            barricadeCollisionRoot.SetActive(false);Destroy(barricadeCollisionRoot);barricadeCollisionRoot=null;
        }
        void SyncBarricades()
        {
            if(!barricadeCollisionRoot)return;
            bool changed=false,live=raceRunning&&Live;double now=Clock;
            for(int i=0;i<barricadeColliders.Length;i++){
                ref var entity=ref entities[BananaEnd+i];var collider=barricadeColliders[i];
                var owner=Participant(entity.owner);
                bool active=entity.item==RaceItem.Barricade&&live&&entity.expires>now&&Eligible(owner)&&owner.RespawnSequence==entity.ownerLife;
                if(!active){
                    if(entity.item==RaceItem.Barricade)entity=default;
                    if(collider.enabled){collider.enabled=false;changed=true;}
                    continue;
                }
                Quaternion rotation=Quaternion.LookRotation(entity.direction,Vector3.up);
                Vector3 center=Vector3.up*(entity.size.y*.5f);
                if(collider.transform.position!=entity.position||collider.transform.rotation!=rotation){
                    collider.transform.SetPositionAndRotation(entity.position,rotation);changed=true;
                }
                if(collider.size!=entity.size||collider.center!=center){collider.size=entity.size;collider.center=center;changed=true;}
                if(!collider.enabled){collider.enabled=true;changed=true;}
            }
            // Network reads and item uses must affect controller movement and ray tests immediately.
            if(changed)Physics.SyncTransforms();
        }
        internal bool TryKickBarricade(CourseParticipant attacker,Collider target)
        {
            if(!CourseNetwork.Authority||!initialized||!raceRunning||!Live||!Eligible(attacker))return false;
            for(int i=0;i<barricadeColliders.Length;i++){
                if(barricadeColliders[i]!=target||!target.enabled)continue;
                ref var entity=ref entities[BananaEnd+i];
                var owner=Participant(entity.owner);
                if(entity.item!=RaceItem.Barricade||entity.expires<=Clock||!Eligible(owner)||owner.RespawnSequence!=entity.ownerLife)return false;
                entity.barricadeHits++;
                entity.barricadeHitAt=Clock;
                if(entity.barricadeHits>=2)entity=default;
                SyncBarricades();
                return true;
            }
            return false;
        }
        bool TryPlaceBarricade(CourseParticipant actor,Vector3 aim,out Vector3 position,out Vector3 forward,out Vector3 size)
        {
            position=size=default;forward=Vector3.ProjectOnPlane(aim,Vector3.up);
            if(forward.sqrMagnitude<.001f||!GroundPoint(actor,actor.transform.position,out var actorFloor))return false;
            forward.Normalize();Vector3 right=Vector3.Cross(Vector3.up,forward);
            Quaternion rotation=Quaternion.LookRotation(forward,Vector3.up);
            float walkable=Mathf.Cos(actor.Motor.profile.slopeLimit*Mathf.Deg2Rad);
            int mask=WorldMask&actor.Motor.profile.collisionMask.value;
            Vector3 origin=actor.transform.position;origin.y=actorFloor.y;
            Vector3 behind=origin-forward*BarricadeBehind;
            if(!BarricadeFloor(behind,actorFloor.y,walkable,mask,out var floor))return false;
            behind.y=floor.y;
            if(!BarricadePathClear(origin,forward,rotation,mask))return false;
            // Do not bridge a corner, a height change or an unsupported run behind the player.
            int pathSteps=Mathf.CeilToInt(BarricadeBehind/BarricadeProbeStep);
            for(int i=1;i<=pathSteps;i++)
                if(!BarricadeFloor(origin-forward*(BarricadeBehind*i/pathSteps),floor.y,walkable,mask,out _))return false;
            if(!BarricadeStrip(behind,forward,rotation,floor.y,walkable,mask))return false;
            if(!BarricadeExtent(behind,right,forward,rotation,floor.y,walkable,mask,out float rightExtent)
                ||!BarricadeExtent(behind,-right,forward,rotation,floor.y,walkable,mask,out float leftExtent))return false;
            leftExtent=Mathf.Max(0,leftExtent-BarricadeClearance);rightExtent=Mathf.Max(0,rightExtent-BarricadeClearance);
            float roadWidth=leftExtent+rightExtent,width=roadWidth/3;
            float passage=0;
            for(int i=0;i<session.participants.Length;i++){
                var p=Participant(i);if(!p||!p.Motor)continue;
                passage=Mathf.Max(passage,2*(p.Motor.profile.radius+p.Motor.profile.skinWidth)+BarricadeClearance*2);
            }
            if(width<passage)return false;
            // Stay directly behind the actor unless an edge requires a lateral shift to retain both bypasses.
            float center=Mathf.Clamp(0,-leftExtent+width*.5f+passage,rightExtent-width*.5f-passage);
            position=behind+right*center;
            size=new Vector3(width,BarricadeHeight,BarricadeThickness);
            int footprintSteps=Mathf.CeilToInt(width/BarricadeProbeStep);
            for(int i=0;i<=footprintSteps;i++){
                Vector3 point=position+right*(width*((float)i/footprintSteps-.5f));
                if(!BarricadeStrip(point,forward,rotation,floor.y,walkable,mask))return false;
            }
            Vector3 half=size*.5f;
            int overlaps=Physics.OverlapBoxNonAlloc(position+Vector3.up*(half.y+.005f),
                new Vector3(half.x+BarricadeClearance,half.y-.005f,half.z+BarricadeClearance),overlapHits,rotation,mask,QueryTriggerInteraction.Ignore);
            if(overlaps!=0)return false;
            // Participant body layers are excluded from world queries; test every current capsule explicitly.
            for(int i=0;i<session.participants.Length;i++){
                var p=Participant(i);if(!p||!p.isActiveAndEnabled||!p.Motor)continue;
                Vector3 delta=p.transform.position-position;
                float radius=p.Motor.profile.radius+BarricadeClearance;
                float dx=Mathf.Max(0,Mathf.Abs(Vector3.Dot(delta,right))-half.x);
                float dz=Mathf.Max(0,Mathf.Abs(Vector3.Dot(delta,forward))-half.z);
                float bottom=delta.y+p.Motor.profile.radius,top=delta.y+p.Motor.BodyHeight-p.Motor.profile.radius;
                float dy=Mathf.Max(0,Mathf.Max(bottom-size.y,-top));
                if(dx*dx+dy*dy+dz*dz<=radius*radius)return false;
            }
            return true;
        }
        bool BarricadePathClear(Vector3 origin,Vector3 forward,Quaternion rotation,int mask)
        {
            Vector3 center=origin+Vector3.up*(BarricadeHeight*.5f+.01f);
            Vector3 half=new Vector3(.1f,BarricadeHeight*.5f,.1f);
            int overlaps=Physics.OverlapBoxNonAlloc(center,half,overlapHits,rotation,mask,QueryTriggerInteraction.Ignore);
            if(overlaps==overlapHits.Length)return false;
            for(int i=0;i<overlaps;i++)if(!overlapHits[i].GetComponentInParent<CourseParticipant>())return false;
            int count=Physics.BoxCastNonAlloc(center,half,-forward,worldHits,rotation,BarricadeBehind,mask,QueryTriggerInteraction.Ignore);
            if(count==worldHits.Length)return false;
            for(int i=0;i<count;i++)if(!worldHits[i].collider.GetComponentInParent<CourseParticipant>())return false;
            return true;
        }
        bool BarricadeExtent(Vector3 origin,Vector3 sideways,Vector3 forward,Quaternion rotation,float floorY,float walkable,int mask,out float extent)
        {
            extent=0;
            for(int i=1;i<=BarricadeProbeSteps;i++){
                float distance=i*BarricadeProbeStep;
                if(BarricadeStrip(origin+sideways*distance,forward,rotation,floorY,walkable,mask)){extent=distance;continue;}
                float end=distance;
                for(int refinement=0;refinement<4;refinement++){
                    float middle=(extent+end)*.5f;
                    if(BarricadeStrip(origin+sideways*middle,forward,rotation,floorY,walkable,mask))extent=middle;else end=middle;
                }
                return true;
            }
            // An unseen edge is not a measured road width: retain the item instead of inventing a width.
            return false;
        }
        bool BarricadeStrip(Vector3 point,Vector3 forward,Quaternion rotation,float floorY,float walkable,int mask)
        {
            float depth=BarricadeThickness*.5f+BarricadeClearance;
            if(!BarricadeFloor(point,floorY,walkable,mask,out _)
                ||!BarricadeFloor(point-forward*depth,floorY,walkable,mask,out _)
                ||!BarricadeFloor(point+forward*depth,floorY,walkable,mask,out _))return false;
            int count=Physics.OverlapBoxNonAlloc(point+Vector3.up*(BarricadeHeight*.5f+.01f),
                new Vector3(BarricadeProbeStep*.5f,BarricadeHeight*.5f,depth),overlapHits,rotation,mask,QueryTriggerInteraction.Ignore);
            if(count==overlapHits.Length)return false;
            for(int i=0;i<count;i++)if(!overlapHits[i].GetComponentInParent<CourseParticipant>())return false;
            return true;
        }
        bool BarricadeFloor(Vector3 point,float floorY,float walkable,int mask,out Vector3 floor)
        {
            point.y=floorY+.15f;floor=default;
            int count=Physics.RaycastNonAlloc(point,Vector3.down,worldHits,.3f,mask,QueryTriggerInteraction.Ignore);
            if(count==worldHits.Length)return false;
            float nearest=float.PositiveInfinity;int best=-1;
            for(int i=0;i<count;i++){
                var hit=worldHits[i];if(hit.collider.GetComponentInParent<CourseParticipant>()||hit.distance>=nearest)continue;
                nearest=hit.distance;best=i;
            }
            if(best<0)return false;
            var ground=worldHits[best];floor=ground.point;
            return ground.normal.y>=walkable&&Mathf.Abs(floor.y-floorY)<=BarricadeFloorTolerance;
        }
    }
}
