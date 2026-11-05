#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalliGalli.Movement;
using UnityEditor;
using UnityEngine;

public sealed class PyramidValidationRunner:MonoBehaviour
{
    public bool probeOnly;
    [Serializable]class Check{public string name,detail;public bool passed;}
    [Serializable]class Report{public string utc;public int passed,failed;public List<Check> checks;public List<Flight> flights;}
    [Serializable]sealed class Flight
    {
        public int seat,room,source,destination,airborneTicks,gapTicks;
        public float authoredGap,authoredDeltaY,airSeconds,deltaY,surfaceDeltaY,minimumY,maximumY;
        public Vector3 takeoff,takeoffVelocity,catchPosition,catchVelocity,sourceTouch,catchTouch;
        public bool sourceContact,destinationContact,interrupted,complete;
        [NonSerialized]public bool airborne;
        [NonSerialized]public float lastSourceTime;
        public string Detail()=>"room="+room+" "+source+"->"+destination+" gap="+authoredGap.ToString("F2")+" authoredDY="+authoredDeltaY.ToString("F2")
            +" source="+sourceContact+" catch="+destinationContact+" free="+airSeconds.ToString("F3")+"s ticks="+airborneTicks+" gapTicks="+gapTicks
            +" dY="+deltaY.ToString("F2")+" surfaceDY="+surfaceDeltaY.ToString("F2")+" min/maxY="+minimumY.ToString("F2")+"/"+maximumY.ToString("F2")+" interrupted="+interrupted
            +" takeoff="+takeoff+" v="+takeoffVelocity+" catch="+catchPosition+" v="+catchVelocity+" touch="+sourceTouch+" -> "+catchTouch;
    }
    readonly List<Check> checks=new List<Check>();
    readonly List<Flight> flights=new List<Flight>();
    IEnumerator Start()
    {
        var run=Validate();while(true){bool more;object next=null;try{more=run.MoveNext();if(more)next=run.Current;}catch(Exception e){Test("Runner completes",false,e.ToString());break;}if(!more)break;yield return next;}
        Time.timeScale=1;Save();Debug.Log("PYRAMID_VALIDATION_RESULT "+checks.Count(c=>c.passed)+"/"+checks.Count);
        if(Application.isBatchMode){EditorApplication.Exit(checks.Any(c=>!c.passed)?1:0);yield break;}
        EditorApplication.isPlaying=false;
    }
    IEnumerator Validate()
    {
        yield return null;yield return null;
        CourseNetwork.Instance.StartPractice();yield return null;
        var session=FindAnyObjectByType<CourseSession>();var roster=session.roster;var surf=roster.surf;var pyramid=surf.GetComponent<PyramidCourse>();
        session.cardGame.botEnabled=false;session.cardGame.shuffleOnReset=false;session.cardGame.seed=20261020;
        FindAnyObjectByType<CourseHUD>().ReadInput=false;
        var control=session.Local.GetComponent<SourcePlayerController>();control.controlsEnabled=false;control.SetCursorCaptured(false);
        foreach(var p in roster.allParticipants){p.Initialize();p.Combat.readInput=false;var b=p.GetComponent<CourseRival>();if(b){b.enabled=false;b.attacksEnabled=false;}}
        Time.timeScale=3;
        bool complete=pyramid.chambers.Length==5&&pyramid.chambers[0].firstSection==0&&pyramid.chambers.Last().lastSection==surf.sections.Length-1;
        for(int i=1;i<pyramid.chambers.Length;i++)complete&=pyramid.chambers[i].firstSection==pyramid.chambers[i-1].lastSection+1;
        Test("Five ordered spaces cover the complete route",complete,"");
        Test("Banks retain broad rideable lane clearance",surf.sections.All(s=>s.Length>0&&s.halfWidth>=surf.laneX),"sections="+surf.sections.Length+" metres="+surf.sections.Sum(s=>s.Length));
        bool faces=true,aligned=true,gentle=true,monotonic=true;float widestSweep=0;
        var gaps=new List<float>();var elevationChanges=new List<float>();
        var banks=surf.GetComponentsInChildren<MeshCollider>().Where(c=>c.name.StartsWith("Pyramid bank ")).ToArray();
        var sectionBanks=SectionBanks(surf);
        var transfers=RoomFlights(surf,pyramid,-1);
        Test("Every space has independent takeoff and catch colliders",pyramid.chambers.Select((room,index)=>transfers.Any(f=>f.room==index)).All(found=>found)
            &&transfers.All(f=>sectionBanks[f.source]&&sectionBanks[f.destination]&&sectionBanks[f.source]!=sectionBanks[f.destination]
                &&f.authoredGap>2*roster.allParticipants[0].Motor.profile.radius),string.Join("; ",transfers.Select(f=>f.room+":"+f.source+"->"+f.destination)));
        for(int i=0;i<surf.sections.Length;i++){
            var s=surf.sections[i];
            foreach(float fraction in new[]{.1f,.5f,.9f}){
                int j=Mathf.Min((int)((s.points.Length-1)*fraction),s.points.Length-2);
                var center=Vector3.Lerp(s.points[j],s.points[j+1],.5f);var right=Quaternion.Euler(0,Mathf.LerpAngle(s.headings[j],s.headings[j+1],.5f),0)*Vector3.right;
                foreach(int side in s.twoSided?new[]{-1,1}:new[]{s.side}){
                    var origin=center+right*(side*surf.laneX)+Vector3.up*10;
                    faces&=banks.Any(c=>c.Raycast(new Ray(origin,Vector3.down),out var hit,20)&&Mathf.Abs(hit.point.y-center.y)<.2f&&hit.normal.y>.35f&&hit.normal.y<.7f);
                }
            }
            bool up=false,down=false;
            for(int j=1;j<s.points.Length;j++){
                float dy=s.points[j].y-s.points[j-1].y;up|=dy>.0001f;down|=dy<-.0001f;
                float metres=Vector3.ProjectOnPlane(s.points[j]-s.points[j-1],Vector3.up).magnitude;
                gentle&=Mathf.Abs(Mathf.DeltaAngle(s.headings[j-1],s.headings[j]))/metres<=1.05f;
            }
            monotonic&=!(up&&down);widestSweep=Mathf.Max(widestSweep,Mathf.Abs(Mathf.DeltaAngle(s.headings[0],s.headings.Last())));
            if(i==0)continue;
            var previous=surf.sections[i-1];var rotation=Quaternion.Euler(0,previous.headings.Last(),0);
            if(previous.landing==Vector3.zero){
                var transfer=s.points[0]-previous.points.Last();
                aligned&=Mathf.Abs(Vector3.Dot(transfer,rotation*Vector3.right))<.02f&&Mathf.Abs(Mathf.DeltaAngle(previous.headings.Last(),s.headings[0]))<.01f;
                float gap=Vector3.Dot(transfer,rotation*Vector3.forward);
                if(gap>1){gaps.Add(gap);elevationChanges.Add(transfer.y);}
            }else gentle&=Mathf.Abs(Mathf.DeltaAngle(previous.headings.Last(),s.headings[0]))<=15;
        }
        Test("Riding faces retain real surf collision throughout curves",faces,"");
        Test("Transfers preserve tangent and lane without forced side swaps",aligned,"");
        Test("Broad horizontal sweep without sharp checkpoint turns",gentle&&widestSweep>=30,"largest sweep="+widestSweep);
        Test("Ascending runs never recess into a middle trough",monotonic,"");
        Test("Airborne transfers alternate between higher and lower catches",elevationChanges.Count>=pyramid.chambers.Length
            &&elevationChanges.All(y=>Mathf.Abs(y)>0)&&elevationChanges.Skip(1).Select((y,i)=>Mathf.Sign(y)!=Mathf.Sign(elevationChanges[i])).All(alternates=>alternates),
            "gaps="+string.Join(", ",gaps)+" height changes="+string.Join(", ",elevationChanges));
        Test("Four ordered AIR-PASSAGE checkpoints track chamber exits",session.checkpoints.orderedPassage&&session.checkpoints.points.Length==4&&session.checkpoints.points.Select(p=>p.surfGates).SequenceEqual(pyramid.chambers.Take(4).Select(c=>c.lastSection+1)),"");
        var profile=AssetDatabase.LoadAssetAtPath<SourceMovementProfile>("Assets/HalliGalliMovement/Settings/SurfMovement.asset");
        Test("Pyramid retains its authored surf profile reference",profile&&session.maps.current.movementProfile==profile&&roster.allParticipants.All(p=>p.Motor.profile==profile),"");
        var decoration=GameObject.Find("Pyramid decoration");var cues=GameObject.Find("Pyramid course cues");var architecture=GameObject.Find("Pyramid architecture");
        Test("Decoration cannot collide with racers",decoration&&cues&&architecture&&decoration.GetComponentsInChildren<Collider>(true).Length==0&&cues.GetComponentsInChildren<Collider>(true).Length==0&&architecture.GetComponentsInChildren<Collider>(true).All(c=>c.GetComponent<SurfDivider>()),"");
        foreach(int count in new[]{2,3,6}){
            roster.StartNewGame(count);yield return null;
            foreach(var p in roster.allParticipants){var b=p.GetComponent<CourseRival>();if(b)b.enabled=false;}
            Test(count+" seats retain equal first jump distance",session.participants.All(p=>Mathf.Abs(p.standAnchor.position.z+.7f)<.01f)&&roster.firstPlatforms.Take(count).All(p=>Mathf.Abs(p.position.z-2.3f)<.01f),"");
        }
        var starts=roster.allParticipants.Select(p=>p.standAnchor.position).ToArray();
        var runner=roster.allParticipants[1];var bot=runner.GetComponent<CourseRival>();
        foreach(int seat in new[]{0,5}){
            var traversal=Traverse(session,runner,bot,seat,starts[seat],false);
            while(traversal.MoveNext())yield return traversal.Current;
        }
        if(!probeOnly){
            // Earn all checkpoints through real movement before exercising R. No injected gate credit.
            var traversal=Traverse(session,runner,bot,2,starts[2],true);
            while(traversal.MoveNext())yield return traversal.Current;
            for(int i=0;i<pyramid.chambers.Length;i++){
                session.ResetPractice();LegalDeparture(session);yield return new WaitForFixedUpdate();runner.Stand();var room=pyramid.chambers[i];
                var localPit=new Vector3(room.localMinimum.x+6,room.localMinimum.y+3,(room.localMinimum.z+room.localMaximum.z)*.5f);
                runner.Motor.Teleport(room.ToWorld(localPit));
                float end=Time.time+1.5f;while(!runner.IsRespawning&&Time.time<end){runner.Motor.Simulate(default,Time.fixedDeltaTime);yield return new WaitForFixedUpdate();}
                Test("Chamber "+i+" pit recovers rather than reaching another route",runner.IsRespawning&&runner.Falls==1,"y="+runner.transform.position.y);
            }
        }
        session.ResetPractice();LegalDeparture(session);yield return new WaitForFixedUpdate();runner.Stand();runner.Motor.Teleport(surf.finish.position);runner.Motor.Simulate(default,.02f);
        Test("Skipping directly to the royal bell cannot win",!session.CanRing(runner)&&runner.CheckpointIndex==-1,"");
        runner.Motor.Teleport(session.checkpoints.points.Last().spawn.position+Vector3.up*2);runner.Motor.Simulate(default,.02f);yield return new WaitForFixedUpdate();
        Test("Skipping earlier AIR-PASSAGE checkpoints cannot grant progress",runner.CheckpointIndex==-1,"");
        Test("Sandstone shader supported",Shader.Find("HalliGalli/Sandstone").isSupported,"");
        runner.standAnchor.position=starts[1];runner.retryAnchor.position=starts[1];session.ResetPractice();
    }
    IEnumerator Traverse(CourseSession session,CourseParticipant runner,CourseRival bot,int seat,Vector3 start,bool exerciseRespawns)
    {
        var surf=session.roster.surf;var pyramid=surf.GetComponent<PyramidCourse>();
        session.ResetPractice();LegalDeparture(session);yield return new WaitForFixedUpdate();
        bot.surfSidePreference=seat%2==0?-1:1;bot.route=session.roster.RouteForSeat(seat);
        runner.standAnchor.position=start;runner.retryAnchor.position=start;
        Test("Start "+seat+" departs under real card rules",session.HasCardRules&&runner.Stand(),"");
        var recorder=runner.GetComponent<PyramidBankContacts>();if(!recorder)recorder=runner.gameObject.AddComponent<PyramidBankContacts>();
        recorder.Clear();
        var sectionBanks=SectionBanks(surf);var touches=new bool[surf.sections.Length];var touchPoints=new Vector3[surf.sections.Length];
        var journey=RoomFlights(surf,pyramid,seat);flights.AddRange(journey);
        bot.enabled=true;float begin=Time.time,deadline=begin+240,next=0;
        var trace=new List<string>();var contacted=new bool[pyramid.chambers.Length];var passages=new bool[session.checkpoints.points.Length];
        int furthest=-1,expectedFalls=0;bool launched=false;
        while(session.BellCount==0&&Time.time<deadline&&runner.Falls<=expectedFalls){
            ResolveContacts(surf,runner.Motor,recorder,sectionBanks,touches,touchPoints);
            launched|=!runner.Motor.Grounded&&!runner.Motor.Surfing&&runner.Motor.Velocity.y>1;
            for(int i=0;i<contacted.Length;i++){
                var room=pyramid.chambers[i];
                for(int section=room.firstSection;section<=room.lastSection;section++)contacted[i]|=touches[section];
            }
            foreach(var flight in journey)ObserveFlight(flight,surf,runner,recorder,touches,touchPoints,trace);
            if(runner.CheckpointIndex>furthest){
                furthest=runner.CheckpointIndex;passages[furthest]=true;
                trace.Add("checkpoint="+furthest+" airborne="+!runner.Motor.Grounded+" p="+runner.transform.position);
                if(exerciseRespawns){
                    bot.enabled=false;var saved=session.checkpoints.Saved(runner);expectedFalls++;
                    Test("Earned checkpoint "+furthest+" accepts R",runner.RequestRespawn(),"");
                    yield return new WaitForSeconds(.85f);
                    Test("Earned checkpoint "+furthest+" restores position and route",!runner.IsRespawning&&runner.CheckpointIndex==furthest&&Vector3.Distance(runner.transform.position,saved.spawn.position)<.15f&&surf.GatesPassed(runner)==saved.surfGates,"");
                    bot.enabled=true;
                }
            }
            if(Time.time>=next){next=Time.time+.5f;trace.Add((Time.time-begin).ToString("F2")+" s="+bot.SurfSection+"/"+bot.SurfPhase+" cp="+runner.CheckpointIndex
                +" p="+runner.transform.position+" v="+runner.Motor.Velocity+" surfing="+runner.Motor.Surfing+" grounded="+runner.Motor.Grounded
                +" contacts="+string.Join(",",Enumerable.Range(0,touches.Length).Where(i=>touches[i])));}
            yield return new WaitForFixedUpdate();
        }
        bot.enabled=false;
        foreach(var flight in journey)Test("Start "+seat+" flies across room "+flight.room+" gap and physically surfs its catch",flight.complete,flight.Detail());
        Test("Start "+seat+" traverses all spaces and ordered passages without unintended falls",session.BellCount==1&&session.cardGame.Match.LastCollector==session.cardGame.Index(runner)
            &&runner.Falls==expectedFalls&&contacted.All(c=>c)&&passages.All(p=>p)&&journey.All(f=>f.complete),
            "seconds="+(Time.time-begin)+" falls="+runner.Falls+" requested="+expectedFalls+"\n"+string.Join("\n",trace));
        Test("Start "+seat+" launches using unmodified movement physics",launched,"");
        Test("Start "+seat+" bell collects cards and reseats the table",session.BellCount==1&&session.cardGame.Match.LastCollectedCount>0&&session.participants.All(p=>p.IsSeated),"");
    }
    static List<Flight> RoomFlights(SurfCourse surf,PyramidCourse pyramid,int seat)
    {
        var result=new List<Flight>();
        for(int room=0;room<pyramid.chambers.Length;room++){
            var chamber=pyramid.chambers[room];
            for(int source=chamber.firstSection;source<chamber.lastSection;source++){
                var from=surf.sections[source];var to=surf.sections[source+1];
                if(from.landing!=Vector3.zero||from.points.Last()==to.points[0])continue;
                var delta=to.points[0]-from.points.Last();
                result.Add(new Flight{seat=seat,room=room,source=source,destination=source+1,
                    authoredGap=Vector3.ProjectOnPlane(delta,Vector3.up).magnitude,authoredDeltaY=delta.y});
            }
        }
        return result;
    }
    static MeshCollider[] SectionBanks(SurfCourse surf)
    {
        var result=new MeshCollider[surf.sections.Length];
        var banks=surf.GetComponentsInChildren<MeshCollider>().Where(c=>c.name.StartsWith("Pyramid bank ")).ToArray();
        int first=0;
        for(int i=0;i<result.Length;i++){
            if(i>0){
                var previous=surf.sections[i-1];var current=surf.sections[i];
                if(previous.landing!=Vector3.zero||previous.points.Last()!=current.points[0]||!Mathf.Approximately(previous.halfWidth,current.halfWidth))first=i;
            }
            string name="Pyramid bank "+first;result[i]=banks.FirstOrDefault(c=>c.name==name);
        }
        return result;
    }
    static void ResolveContacts(SurfCourse surf,SourceMotor motor,PyramidBankContacts recorder,MeshCollider[] banks,bool[] touches,Vector3[] points)
    {
        Array.Clear(touches,0,touches.Length);
        if(!motor.Surfing||!recorder.HasContacts)return;
        float walkable=Mathf.Cos(motor.profile.slopeLimit*Mathf.Deg2Rad);
        foreach(var hit in recorder.contacts){
            if(hit.normal.y<=.05f||hit.normal.y>=walkable)continue;
            for(int i=0;i<touches.Length;i++){
                if(hit.collider!=banks[i])continue;
                var section=surf.sections[i];var frame=section.Sample(hit.point);
                if(frame.distance<-.05f||frame.distance>section.Length+.05f)continue;
                touches[i]=true;points[i]=hit.point;
            }
        }
    }
    static void ObserveFlight(Flight flight,SurfCourse surf,CourseParticipant runner,PyramidBankContacts recorder,bool[] touches,Vector3[] touchPoints,List<string> trace)
    {
        if(flight.complete)return;
        var motor=runner.Motor;var position=runner.transform.position;
        if(touches[flight.source]){
            // A brief loss of contact within the launch is not the final airborne transfer.
            flight.sourceContact=true;flight.destinationContact=false;flight.airborne=false;flight.interrupted=false;
            flight.airborneTicks=flight.gapTicks=0;flight.airSeconds=0;flight.lastSourceTime=Time.fixedTime;
            flight.takeoff=position;flight.takeoffVelocity=motor.Velocity;flight.sourceTouch=touchPoints[flight.source];
            flight.minimumY=flight.maximumY=position.y;
            return;
        }
        if(!flight.sourceContact)return;
        if(touches[flight.destination]){
            if(!flight.destinationContact){
                flight.destinationContact=true;flight.catchPosition=position;flight.catchVelocity=motor.Velocity;flight.catchTouch=touchPoints[flight.destination];
                if(flight.airborne&&!flight.interrupted)flight.airSeconds=Time.fixedTime-flight.lastSourceTime;
                flight.deltaY=position.y-flight.takeoff.y;
                flight.surfaceDeltaY=flight.catchTouch.y-flight.sourceTouch.y;
                flight.minimumY=Mathf.Min(flight.minimumY,position.y);flight.maximumY=Mathf.Max(flight.maximumY,position.y);
                // Continuous, collision-free flight must span open space, then reacquire this bank.
                // The duration is a behavioral requirement, not a copy of the authored gap length.
                flight.complete=flight.airborne&&!flight.interrupted&&flight.airSeconds>=.6f&&flight.gapTicks>0
                    &&flight.surfaceDeltaY*flight.authoredDeltaY>0;
                trace.Add("CATCH "+flight.Detail()+" accepted="+flight.complete);
            }
            return;
        }
        if(flight.interrupted||flight.destinationContact)return;
        bool free=!motor.Grounded&&!motor.Surfing&&!recorder.HasContacts;
        if(free){
            if(!flight.airborne){
                flight.airborne=true;
                trace.Add("TAKEOFF room="+flight.room+" "+flight.source+"->"+flight.destination+" p="+flight.takeoff+" v="+flight.takeoffVelocity);
            }
            flight.airborneTicks++;flight.airSeconds=Time.fixedTime-flight.lastSourceTime;
            flight.minimumY=Mathf.Min(flight.minimumY,position.y);flight.maximumY=Mathf.Max(flight.maximumY,position.y);
            var from=surf.sections[flight.source];var forward=Quaternion.Euler(0,from.headings.Last(),0)*Vector3.forward;
            float along=Vector3.Dot(position-from.points.Last(),forward),radius=motor.profile.radius;
            if(along>radius&&along<flight.authoredGap-radius)flight.gapTicks++;
        }else if(flight.airborne&&!flight.interrupted){
            flight.interrupted=true;
            trace.Add("INTERRUPTED room="+flight.room+" p="+position+" surfing="+motor.Surfing+" grounded="+motor.Grounded);
        }
    }
    static void LegalDeparture(CourseSession session)
    {
        for(int i=0;!session.cardGame.Match.HasExactFive()&&i<1000;i++)session.cardGame.Match.Reveal(session.cardGame.Match.Turn);
        if(!session.cardGame.Match.HasExactFive())throw new InvalidOperationException("Fixed deck did not produce a legal departure.");
    }
    void Test(string name,bool passed,string detail){checks.Add(new Check{name=name,passed=passed,detail=detail});if(!passed)Debug.LogError(name+" "+detail);Save();}
    void Save(){Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/"+(probeOnly?"pyramid-probe":"pyramid-validation")+".json",JsonUtility.ToJson(new Report{utc=DateTime.UtcNow.ToString("O"),passed=checks.Count(c=>c.passed),failed=checks.Count(c=>!c.passed),checks=checks,flights=flights},true));}
}

// Test-only collision observer: navigation can advance to a catch while still above empty air.
public sealed class PyramidBankContacts:MonoBehaviour
{
    public struct Contact{public Collider collider;public Vector3 point,normal;}
    public readonly List<Contact> contacts=new List<Contact>();
    float tick=float.NegativeInfinity;
    public bool HasContacts=>tick==Time.fixedTime&&contacts.Count>0;
    public void Clear(){contacts.Clear();tick=float.NegativeInfinity;}
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if(tick!=Time.fixedTime){contacts.Clear();tick=Time.fixedTime;}
        contacts.Add(new Contact{collider=hit.collider,point=hit.point,normal=hit.normal});
    }
}
#endif
