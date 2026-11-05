#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalliGalli.Movement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class BarricadeValidationRunner:MonoBehaviour
{
    const float Step=.01f;
    const string ReportPath="TestResults/barricade-validation.json";
    [Serializable]sealed class Check{public string name,detail;public bool passed;}
    [Serializable]sealed class Report
    {
        public string utc,completedUtc,status,unityVersion;
        public int passed,failed;
        public List<Check> checks;
        public List<string> captures,limitations;
    }
    readonly List<Check> checks=new List<Check>();
    readonly List<string> captures=new List<string>(),limitations=new List<string>();
    readonly Stack<IEnumerator> routines=new Stack<IEnumerator>();
    CourseSession session;RaceCourse race;CourseParticipant owner,attacker;
    RaceCourse.ProjectileSnapshot wall;
    Collider wallCollider;
    Camera captureCamera;
    GameObject occluder;
    string startedUtc;
    float deadline;
    bool finished;

    // Run with graphics for front/back image assertions. No scene or project settings are saved.
    public static void Run()
    {
        Directory.CreateDirectory("TestResults");
        File.WriteAllText(ReportPath,JsonUtility.ToJson(new Report{utc=DateTime.UtcNow.ToString("O"),status="starting",unityVersion=Application.unityVersion},true));
        EditorSceneManager.OpenScene("Assets/HalliGalliMovement/Demo/RaceCourse.unity");
        new GameObject("Barricade runtime validation").AddComponent<BarricadeValidationRunner>();
        EditorApplication.isPlaying=true;
    }
    void Update(){if(!finished&&deadline>0&&Time.realtimeSinceStartup>deadline)Finish(new TimeoutException("Barricade validation exceeded 150 seconds."));}
    IEnumerator Start()
    {
        startedUtc=DateTime.UtcNow.ToString("O");deadline=Time.realtimeSinceStartup+150;Save("running");routines.Push(Validate());
        // Flatten nested coroutines so setup/wait failures always write a report and exit nonzero.
        while(routines.Count>0&&!finished){
            object next=null;bool more;
            try{more=routines.Peek().MoveNext();if(more)next=routines.Peek().Current;}
            catch(Exception e){Finish(e);yield break;}
            if(!more){routines.Pop();continue;}
            if(next is IEnumerator nested){routines.Push(nested);continue;}
            yield return next;
        }
        if(!finished)Finish(null);
    }
    IEnumerator Validate()
    {
        yield return null;yield return null;
        CourseNetwork.Instance.StartPractice();yield return null;
        session=FindAnyObjectByType<CourseSession>();race=FindAnyObjectByType<RaceCourse>();
        Require(session&&race,"Official race scene did not load its live item runtime.");
        session.cardGame.botEnabled=false;session.cardGame.ReadInput=false;session.cardGame.enabled=false;
        session.roster.StartNewGame(2);owner=session.participants[0];attacker=session.participants[1];
        var hud=FindAnyObjectByType<CourseHUD>();if(hud)hud.ReadInput=false;
        foreach(var p in session.participants){
            var bot=p.GetComponent<CourseRival>();if(bot)bot.enabled=false;
            var control=p.GetComponent<SourcePlayerController>();if(control){control.enabled=false;control.SetCursorCaptured(false);}
            p.Combat.readInput=false;p.Combat.simulateAutomatically=false;
        }
        yield return FreshWall();
        CheckThat("A real box pickup and item use create a solid barricade",wall.remaining>7.5f&&wallCollider&&wallCollider.enabled&&race.HeldItem(owner)==RaceItem.None,"id="+wall.id+" lifetime="+wall.remaining);
        Place(attacker,wall.position+new Vector3(0,.025f,-1.1f));Aim(attacker,Vector3.forward);
        int hits=attacker.Combat.HitCount,attacks=attacker.Combat.AttackSequence;
        attacker.Combat.ProcessPointerInput(true,false,false);Advance(attacker,.65f);
        CheckThat("Left click starts a real push but never damages the barricade",attacker.Combat.AttackSequence==attacks+1&&Present(0)&&attacker.Combat.HitCount==hits,"hits="+attacker.Combat.HitCount);
        attacker.Combat.ProcessPointerInput(false,false,true);
        Advance(attacker,.30f);CheckThat("Kick windup cannot damage the wall before contact",Present(0),"elapsed="+attacker.Combat.Elapsed);
        Advance(attacker,.05f);CheckThat("First kick contact deals exactly one damage and confirms a hit",Present(1)&&attacker.Combat.HitCount==hits+1&&attacker.Combat.HitMarkerRemaining>0,"elapsed="+attacker.Combat.Elapsed);
        int accepted=attacker.Combat.AttackSequence;
        for(int i=0;i<15;i++){attacker.Combat.ProcessPointerInput(false,false,true);Advance(attacker,Step);}
        CheckThat("Repeated contact ticks and held/repeated input cannot duplicate the first kick",Present(1)&&attacker.Combat.AttackSequence==accepted&&attacker.Combat.HitCount==hits+1&&!attacker.Combat.HasBufferedAttack,"attackSequence="+accepted);
        Advance(attacker,.65f);attacker.Combat.ProcessPointerInput(false,false,true);
        CheckThat("A completed animation still rejects input during the two-second cooldown",!attacker.Combat.IsAttacking&&attacker.Combat.KickCooldownRemaining>0&&attacker.Combat.AttackSequence==accepted&&Present(1),"cooldown="+attacker.Combat.KickCooldownRemaining);
        Walk(attacker,Vector3.forward,50);
        CheckThat("Damaged barricade stops actual CharacterController movement",Present(1)&&wallCollider.enabled&&attacker.transform.position.z<wall.position.z-.3f&&attacker.transform.position.z>wall.position.z-1,"position="+attacker.transform.position);
        Advance(attacker,attacker.Combat.KickCooldownRemaining+.01f);attacker.Combat.ProcessPointerInput(false,false,true);Advance(attacker,.45f);
        CheckThat("Second legal kick breaks the barricade immediately",!Exists()&&!wallCollider.enabled&&attacker.Combat.HitCount==hits+2,"hit delta="+(attacker.Combat.HitCount-hits));
        Walk(attacker,Vector3.forward,70);
        CheckThat("Broken barricade lets the same controller cross its former footprint",attacker.transform.position.z>wall.position.z+1,"position="+attacker.transform.position);

        Collider reused=wallCollider;int previousId=wall.id;
        yield return FreshWall(false);
        CheckThat("A new deployment in the released pool slot has no retained damage",wallCollider==reused&&wall.id!=previousId&&Present(0),"oldId="+previousId+" newId="+wall.id);
        Place(attacker,wall.position+new Vector3(0,.025f,-1.1f));Aim(attacker,Vector3.forward);Kick(attacker);
        Place(owner,wall.position+new Vector3(0,.025f,1.1f));Aim(owner,Vector3.back);Kick(owner);
        CheckThat("Two distinct actors combine one kick each into one break",!Exists()&&!wallCollider.enabled,"attacker="+attacker.Combat.AttackSequence+" owner="+owner.Combat.AttackSequence);

        yield return FreshWall();
        Place(attacker,wall.position+new Vector3(0,.025f,-3));Aim(attacker,Vector3.forward);hits=attacker.Combat.HitCount;Kick(attacker);
        CheckThat("An out-of-range real kick leaves the wall intact",Present(0)&&attacker.Combat.HitCount==hits,"distance=3m");
        Advance(attacker,2.1f);Place(attacker,wall.position+new Vector3(0,.025f,-1.1f));Aim(attacker,Vector3.back);Kick(attacker);
        CheckThat("A close kick aimed away leaves the wall intact",Present(0)&&attacker.Combat.HitCount==hits,"backward miss");
        Advance(attacker,2.1f);Aim(attacker,Vector3.forward);
        occluder=GameObject.CreatePrimitive(PrimitiveType.Cube);occluder.name="Barricade test solid occluder";
        occluder.transform.position=wall.position+new Vector3(0,1.2f,-.58f);occluder.transform.localScale=new Vector3(3,2.4f,.15f);Physics.SyncTransforms();Kick(attacker);
        CheckThat("A real nearer solid wall occludes kick damage",Present(0)&&attacker.Combat.HitCount==hits,"occluder="+occluder.GetComponent<Collider>().bounds);
        occluder.SetActive(false);Destroy(occluder);Physics.SyncTransforms();Advance(attacker,2.1f);Kick(attacker);
        CheckThat("Removing only the occluder makes the same attack hit",Present(1)&&attacker.Combat.HitCount==hits+1,"positive control");

        yield return FreshWall();
        if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)limitations.Add("No graphics device: front/back intact and damaged captures and visible-pixel assertions were not exercised. Rerun without -nographics.");
        else{
            Place(attacker,new Vector3(5,.025f,168));Place(owner,new Vector3(5,.025f,178));
            yield return null;yield return null;
            var front=Capture("Barricade_Front_Intact",1);var back=Capture("Barricade_Back_Intact",-1);
            Place(attacker,wall.position+new Vector3(0,.025f,-1.1f));Aim(attacker,Vector3.forward);Kick(attacker);
            Place(attacker,new Vector3(5,.025f,168));
            // Let the genuine hit wobble settle; do not toggle crack meshes or fabricate pixels.
            yield return new WaitForSeconds(.5f);yield return null;
            var damagedFront=Capture("Barricade_Front_Damaged",1);var damagedBack=Capture("Barricade_Back_Damaged",-1);
            int frontPixels=Brightened(front,damagedFront),backPixels=Brightened(back,damagedBack);
            CheckThat("First kick exposes visible pale fractures on the front face",Present(1)&&frontPixels>80,"brightened centre pixels="+frontPixels);
            CheckThat("First kick exposes visible pale fractures on the back face",Present(1)&&backPixels>80,"brightened centre pixels="+backPixels);
        }
        if(!Present(1)){Place(attacker,wall.position+new Vector3(0,.025f,-1.1f));Aim(attacker,Vector3.forward);Kick(attacker);}
        reused=wallCollider;previousId=wall.id;
        foreach(var p in session.participants)p.Sit();
        CheckThat("Returning everyone to the table clears a damaged wall and its collision",!Exists()&&!reused.enabled,"round="+race.Round);
        yield return FreshWall();
        CheckThat("New round and reused pool both begin undamaged",Present(0)&&wall.id!=previousId&&wallCollider==reused,"id="+wall.id);

        double placedAt=CourseNetwork.WorldTime-(8-wall.remaining);
        yield return Until(()=>CourseNetwork.WorldTime>=placedAt+2,"delayed expiry kick");
        Place(attacker,wall.position+new Vector3(0,.025f,-1.1f));Aim(attacker,Vector3.forward);Kick(attacker);
        yield return Until(()=>CourseNetwork.WorldTime>=placedAt+7.6,"original eight-second lifetime",10);
        CheckThat("A damaged barricade stays solid until its original eight-second expiry",Present(1)&&wallCollider.enabled,"age="+(CourseNetwork.WorldTime-placedAt));
        yield return Until(()=>!Exists(),"expiry cleanup",2);
        CheckThat("Damage never renews the original eight-second expiry",!wallCollider.enabled&&CourseNetwork.WorldTime-placedAt<8.25,"age="+(CourseNetwork.WorldTime-placedAt));
        Walk(attacker,Vector3.forward,70);CheckThat("Expired collision permits actual passage",attacker.transform.position.z>wall.position.z+1,"position="+attacker.transform.position);

        yield return FreshWall();Place(attacker,wall.position+new Vector3(0,.025f,-1.1f));Aim(attacker,Vector3.forward);Kick(attacker);
        int life=owner.RespawnSequence;double retryAt=CourseNetwork.WorldTime;Require(owner.RequestRespawn(),"Owner retry input was rejected.");
        yield return Until(()=>!Exists(),"owner-life cleanup",2);
        CheckThat("Owner-life cleanup removes a damaged wall before its timer expires",!wallCollider.enabled&&CourseNetwork.WorldTime-retryAt<2,"owner respawning="+owner.IsRespawning);
        yield return Until(()=>!owner.IsRespawning&&owner.RespawnSequence>life,"owner respawn",3);
        Walk(attacker,Vector3.forward,70);CheckThat("Owner respawn leaves no invisible collision",attacker.transform.position.z>wall.position.z+1,"position="+attacker.transform.position);
    }
    IEnumerator FreshWall(bool newRound=true)
    {
        if(newRound){foreach(var p in session.participants)p.Sit();foreach(var p in session.participants)p.Stand();}
        Place(owner,new Vector3(0,.025f,178));Place(attacker,new Vector3(0,.025f,170));
        foreach(var p in session.participants)Advance(p,2.1f);
        for(int attempt=0;attempt<12&&race.HeldItem(owner)!=RaceItem.Barricade;attempt++){
            if(race.HeldItem(owner)!=RaceItem.None){Require(owner.RequestRespawn(),"Retry did not clear a non-barricade pickup.");yield return Until(()=>!owner.IsRespawning,"pickup retry",3);}
            yield return Until(()=>race.CaptureSnapshot().boxes.Any(b=>b.cooldown<=0),"live box availability",10);
            var box=race.CaptureSnapshot().boxes.First(b=>b.cooldown<=0);
            // Seed the existing random draw, never assign inventory or call damage internals.
            int seed=125+attempt;for(;seed<10000;seed++){UnityEngine.Random.InitState(seed);if(UnityEngine.Random.value>=2f/3)break;}
            UnityEngine.Random.InitState(seed);Place(owner,new Vector3(box.position.x,.025f,box.position.z));
            yield return Until(()=>race.HeldItem(owner)!=RaceItem.None,"live box pickup",2);
        }
        Require(race.HeldItem(owner)==RaceItem.Barricade,"No barricade acquired from live boxes.");
        Place(owner,new Vector3(0,.025f,178));Place(attacker,new Vector3(0,.025f,170));
        race.UseItem(owner,Vector3.forward);
        var walls=race.CaptureSnapshot().projectiles.Where(p=>p.item==RaceItem.Barricade).ToArray();Require(walls.Length==1,"Live item use did not deploy exactly one wall.");wall=walls[0];
        int mask=~((1<<CharacterMotionDriver.BodyLayer)|(1<<CharacterMotionDriver.HandsLayer));
        Require(Physics.Raycast(wall.position+new Vector3(0,1,-2),Vector3.forward,out var hit,3,mask,QueryTriggerInteraction.Ignore),"No real barricade collision surface.");
        wallCollider=hit.collider;Require(Mathf.Abs(hit.point.z-(wall.position.z-.3f))<.05f,"Ray hit another surface rather than the barricade.");
    }
    bool Exists()=>race.CaptureSnapshot().projectiles.Any(p=>p.id==wall.id);
    bool Present(int hits)=>race.CaptureSnapshot().projectiles.Any(p=>p.id==wall.id&&p.barricadeHits==hits);
    static void Place(CourseParticipant p,Vector3 position){p.Motor.Teleport(position);p.Motor.Simulate(default,Step);Physics.SyncTransforms();}
    static void Aim(CourseParticipant p,Vector3 direction){p.Combat.networkAim=direction;}
    static void Advance(CourseParticipant p,float seconds){int ticks=Mathf.CeilToInt(seconds/Step);for(int i=0;i<ticks;i++)p.Combat.SimulateAttack(Step);}
    static void Kick(CourseParticipant p){int sequence=p.Combat.AttackSequence;p.Combat.ProcessPointerInput(false,false,true);Require(p.Combat.AttackSequence==sequence+1,"Real kick input rejected.");Advance(p,.45f);}
    static void Walk(CourseParticipant p,Vector3 direction,int ticks){float yaw=Quaternion.LookRotation(direction).eulerAngles.y;for(int i=0;i<ticks;i++)p.Motor.Simulate(new MovementCommand{move=Vector2.up,yaw=yaw},Step);Physics.SyncTransforms();}
    static IEnumerator Until(Func<bool> predicate,string label,float seconds=5){float end=Time.realtimeSinceStartup+seconds;while(!predicate()){Require(Time.realtimeSinceStartup<end,"Timed out: "+label);yield return null;}}
    Color32[] Capture(string name,int side)
    {
        if(!captureCamera){captureCamera=new GameObject("Barricade capture camera").AddComponent<Camera>();captureCamera.enabled=false;captureCamera.orthographic=true;captureCamera.orthographicSize=1.4f;captureCamera.nearClipPlane=.1f;captureCamera.farClipPlane=12;captureCamera.cullingMask=~((1<<CharacterMotionDriver.BodyLayer)|(1<<CharacterMotionDriver.HandsLayer));}
        Vector3 centre=wall.position+Vector3.up*1.1f;captureCamera.transform.SetPositionAndRotation(centre+wall.direction*(side*6),Quaternion.LookRotation(-wall.direction*side));
        var target=RenderTexture.GetTemporary(960,540,24);var previous=RenderTexture.active;var image=new Texture2D(960,540,TextureFormat.RGB24,false);
        try{captureCamera.targetTexture=target;captureCamera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();Directory.CreateDirectory("Previews");string file="Previews/"+name+".png";File.WriteAllBytes(file,image.EncodeToPNG());captures.Add(file);return image.GetPixels32();}
        finally{captureCamera.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);Destroy(image);}
    }
    static int Brightened(Color32[] before,Color32[] after){int count=0;for(int y=81;y<459;y++)for(int x=288;x<672;x++){int i=y*960+x;var a=before[i];var b=after[i];if(b.g>a.g+35&&b.b>a.b+35&&b.r+b.g+b.b>a.r+a.g+a.b+60)count++;}return count;}
    void CheckThat(string name,bool passed,string detail){checks.Add(new Check{name=name,passed=passed,detail=detail});Save("running");Require(passed,name+": "+detail);}
    void Save(string status){Directory.CreateDirectory("TestResults");File.WriteAllText(ReportPath,JsonUtility.ToJson(new Report{utc=startedUtc,completedUtc=finished?DateTime.UtcNow.ToString("O"):null,status=status,unityVersion=Application.unityVersion,passed=checks.Count(c=>c.passed),failed=checks.Count(c=>!c.passed),checks=checks,captures=captures,limitations=limitations},true));}
    void Finish(Exception error)
    {
        if(finished)return;finished=true;
        if(error!=null){if(checks.Count==0||checks[checks.Count-1].passed)checks.Add(new Check{name="Runner completes",passed=false,detail=error.ToString()});Debug.LogError(error);}
        if(occluder)occluder.SetActive(false);if(captureCamera)Destroy(captureCamera.gameObject);
        bool failed=checks.Any(c=>!c.passed);Save(failed?"failed":limitations.Count>0?"limited":"passed");
        Debug.Log("BARRICADE_VALIDATION_RESULT "+checks.Count(c=>c.passed)+"/"+checks.Count+" limitations="+limitations.Count);
        EditorApplication.Exit(failed?1:0);
    }
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
#endif
