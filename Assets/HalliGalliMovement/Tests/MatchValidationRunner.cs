#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalliGalli.Cards;
using HalliGalli.Movement;
using UnityEditor;
using UnityEngine;

public sealed class MatchValidationRunner:MonoBehaviour
{
    [Serializable]class Check{public string name;public bool passed;public string detail;}
    [Serializable]class Report{public int passed,failed;public List<Check> checks;}
    readonly List<Check> results=new List<Check>();
    ClassicFruitDeck deck;CourseSession session;CourseCardGame game;CourseHUD hud;CourseParticipant player,rival;CourseRival bot;SourcePlayerController controller;
    IEnumerator Start()
    {
        var steps=Validate();
        while(true){bool more=false;object current=null;try{more=steps.MoveNext();if(more)current=steps.Current;}catch(Exception e){Run("Validation completed without exception",()=>throw e);break;}if(!more)break;yield return current;}
        Time.timeScale=1;
        int passed=results.Count(r=>r.passed);Directory.CreateDirectory("TestResults");
        File.WriteAllText("TestResults/match-validation.json",JsonUtility.ToJson(new Report{passed=passed,failed=results.Count-passed,checks=results},true));
        Debug.Log("MATCH_VALIDATION_RESULT "+passed+"/"+results.Count);EditorApplication.isPlaying=false;
    }
    IEnumerator Validate()
    {
        yield return null;yield return null;Application.runInBackground=true;
        session=FindFirstObjectByType<CourseSession>();if(session.roster)session.roster.StartNewGame(2);game=session.cardGame;deck=game.deck;hud=FindFirstObjectByType<CourseHUD>();player=session.Local;rival=session.participants.First(p=>p!=player);bot=rival.GetComponent<CourseRival>();controller=player.GetComponent<SourcePlayerController>();
        controller.controlsEnabled=false;controller.captureCursorOnStart=false;controller.SetCursorCaptured(false);hud.ReadInput=false;game.ReadInput=false;game.botEnabled=false;bot.enabled=false;bot.attacksEnabled=false;
        game.shuffleOnReset=false;game.seed=Enumerable.Range(0,100).First(n=>deck.Deal(2,n)[0][0].Definition.fruitCount!=5);Time.timeScale=3;
        ModelChecks();
        yield return StackChecks();
        session.ResetPractice();yield return new WaitForSeconds(.4f);
        Run("Scene starts seated with 28 hidden cards each and 56 views",()=>Require(session.EveryoneSeated&&game.Match.Seats.All(s=>s.Draw.Count==28&&s.Open.Count==0)&&game.display.ViewCount==56&&DisplayMatches(),"initial scene"));
        Run("Only local turn accepts left-click reveal",()=>{Require(!game.Reveal(rival),"wrong turn");hud.ProcessActions(false,true,false);Require(game.Match.RevealSequence==1&&game.Match.Turn==1&&game.Match.Seats[0].Open.Count==1,"left click");hud.ProcessActions(false,true,false);Require(game.Match.RevealSequence==1,"double reveal");});
        yield return new WaitForSeconds(.3f);Run("Exposed card model and table view agree",()=>Require(DisplayMatches(),"display differs"));
        var camera=controller.playerCamera;var driver=player.GetComponent<CharacterMotionDriver>();
        Run("Revealing moves only the camera above the desk and hides obstructing hands",()=>Require(game.tableView.Focused&&controller.lookSuppressed&&Vector3.Dot(camera.transform.forward,Vector3.down)>.93f&&!driver.handCamera.enabled&&Vector3.Distance(player.transform.position,player.seatAnchor.position)<.01f&&Vector3.Distance(driver.bodyModel.position,player.transform.position)<.1f,"table camera or body drift"));
        foreach(float aspect in new[]{4f/3,16f/9,21f/9}){camera.aspect=aspect;yield return null;Run("All card corners visible at aspect "+aspect.ToString("F2"),()=>Require(CardCornersVisible(camera),"card clipping"));}camera.ResetAspect();
        player.Stand();
        Run("E immediately restores first-person view, hands and movement without a camera wait",()=>Require(!game.tableView.Focused&&!controller.lookSuppressed&&driver.handCamera.enabled&&Mathf.Abs(camera.fieldOfView-player.Motor.profile.fieldOfView)<.01f&&controller.movementEnabled&&Vector3.Distance(camera.transform.position,player.Motor.CurrentEyePosition)<.01f,"camera did not release"));
        session.ResetPractice();yield return new WaitForSeconds(.4f);
        hud.ProcessActions(true,true,false);
        Run("E and left-click together freezes before a reveal",()=>Require(game.Match.Paused&&game.Match.RevealSequence==0&&game.Match.Seats[0].MustSit&&!player.IsSeated,"input ordering"));
        Run("Wrong stand transfers one card to rival and enables return movement",()=>Require(game.Match.Seats[0].Draw.Count==27&&game.Match.Seats[1].Draw.Count==29&&controller.movementEnabled&&hud.StatusText.Contains("1장 지급")&&DisplayMatches(),"penalty"));
        hud.ProcessActions(false,true,false);int rev=game.Match.Revision;player.Stand();
        Run("Frozen reveal and repeated stand cannot advance or charge again",()=>Require(game.Match.RevealSequence==0&&game.Match.Revision==rev&&!game.Reveal(rival),"repeat mutation"));
        game.botEnabled=true;bot.enabled=true;yield return new WaitForSeconds(1.6f);
        Run("Wrong stand waits for explicit sit; bot does not reveal or follow",()=>Require(!player.IsSeated&&rival.IsSeated&&game.Match.RevealSequence==0&&game.Match.Paused,"automatic resume"));game.botEnabled=false;bot.enabled=false;
        player.Motor.Teleport(new Vector3(0,.28f,21.95f));yield return new WaitForSeconds(.15f);
        Run("A penalized player cannot ring even when physically beside bell",()=>Require(!session.CanRing(player)&&!session.Ring(player),"illegal claim"));
        player.Motor.Teleport(new Vector3(0,-4,7));yield return null;yield return null;yield return new WaitForSeconds(.8f);
        Run("Falling preserves sit debt and respawns beside own chair",()=>Require(!player.IsSeated&&!player.IsRespawning&&game.Match.Seats[0].MustSit&&game.Match.Paused&&Vector3.Distance(player.transform.position,player.standAnchor.position)<.15f,"fall bypass"));
        hud.ProcessActions(true,false,false);yield return new WaitForSeconds(.4f);
        Run("E at own seat resumes the pending turn without dealing again",()=>Require(session.EveryoneSeated&&!game.Match.Paused&&game.CanReveal(player)&&game.Match.Seats[0].Draw.Count==27,"sit resume"));
        hud.ProcessActions(false,true,false);game.tableView.ClearFocus();game.botEnabled=true;bot.enabled=true;yield return new WaitForSeconds(1.55f);
        Run("Bot reveals exactly one card on its own turn and also focuses the desk",()=>Require(game.Match.RevealSequence==2&&game.Match.Turn==0&&game.tableView.Focused&&Vector3.Dot(camera.transform.forward,Vector3.down)>.93f,"bot sequence="+game.Match.RevealSequence));game.botEnabled=false;bot.enabled=false;

        PrepareFive();yield return null;
        Run("A valid total alone does not freeze or display a five hint",()=>Require(game.Match.HasExactFive()&&!game.Match.Paused&&!hud.StatusText.Contains("5")&&!hud.StatusText.Contains("일어나")&&!hud.ActionText.Contains("종"),"unsolicited signal"));
        int before=game.Match.RevealSequence,openCount=game.Match.Seats.Sum(s=>s.Open.Count);int[] draw=game.Match.Seats.Select(s=>s.Draw.Count).ToArray();
        player.Stand();rival.Stand();
        Run("Valid stand freezes both turns without penalty and allows other racer",()=>Require(game.Match.Paused&&!game.Match.Seats.Any(s=>s.MustSit)&&!game.CanReveal(player)&&!game.CanReveal(rival)&&game.Match.Seats.Select(s=>s.Draw.Count).SequenceEqual(draw)&&game.Match.CanClaim(0)&&game.Match.CanClaim(1),"valid race"));
        player.Sit();
        Run("One player seated is insufficient to resume",()=>Require(game.Match.Paused&&session.RunActive&&!game.CanReveal(player),"early resume"));rival.Sit();yield return new WaitForSeconds(.4f);
        Run("Last seat resumes same reveal turn and same exposed cards",()=>Require(!game.Match.Paused&&!session.RunActive&&game.Match.RevealSequence==before&&game.Match.HasExactFive(),"lost snapshot"));

        player.Stand();rival.Stand();bot.enabled=true;float end=Time.time+35;
        while(session.BellCount==0&&Time.time<end)yield return new WaitForFixedUpdate();bot.enabled=false;
        Run("Actual rival reaches bell, collects cards and instantly seats everybody",()=>Require(session.BellCount==1&&session.EveryoneSeated&&game.Match.LastCollector==1&&game.Match.LastCollectedCount==openCount&&game.Match.Seats.All(s=>s.Open.Count==0)&&!game.Match.Finished&&game.Match.Seats[1].Draw.Count==draw[1]+openCount&&Vector3.Distance(rival.transform.position,rival.seatAnchor.position)<.01f&&Vector3.Distance(player.transform.position,player.seatAnchor.position)<.01f,"bell/card integration, pos="+rival.transform.position));
        Run("Immediate reseating prevents duplicate collection and retains all card views",()=>Require(!session.Ring(rival)&&!session.RunActive&&!game.Match.Paused&&DisplayMatches(),"double collection"));
        yield return new WaitForSeconds(.4f);
        Run("Bell winner can reveal next without running back or another E press",()=>Require(game.Match.Turn==1&&game.Reveal(rival)&&!game.Match.Finished&&game.tableView.Focused,"next hand"));
        PrepareFive();yield return null;player.Stand();rival.Stand();rival.BeginFall();player.Motor.Teleport(new Vector3(0,.28f,21.95f));yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();
        Run("A local bell also cancels the other player's pending fall and seats both",()=>Require(rival.IsRespawning&&session.Ring(player)&&session.EveryoneSeated&&!rival.IsRespawning&&!controller.movementEnabled&&!player.Combat.actionsEnabled&&!rival.Receiver.canReceiveHits&&game.Match.Turn==0,"respawn return"));
        yield return new WaitForSeconds(.9f);
        Run("Cancelled fall cannot pull a seated player back onto the course",()=>Require(session.EveryoneSeated&&Vector3.Distance(rival.transform.position,rival.seatAnchor.position)<.01f&&game.CanReveal(player),"stale respawn timer"));
        Run("Seating rejects pushes and kicks and clears receiver protection",()=>Require(!player.Combat.TryPush()&&!player.Combat.TryKick()&&player.Receiver.PushProtectionRemaining==0&&!player.Receiver.Receive(MeleeAttack.Kick,Vector3.forward,player.Motor.CurrentEyePosition),"seated attack accepted or protection retained"));
        session.ResetPractice();yield return new WaitForSeconds(.4f);
        Run("New game resets physical seats, card turn, penalties and all 56 cards",()=>Require(session.EveryoneSeated&&!session.RunActive&&!session.BellRung&&!game.Match.Paused&&game.Match.Turn==0&&game.Match.Seats.All(s=>s.Draw.Count==28&&!s.MustSit)&&DisplayMatches(),"reset"));
        game.Reveal(player);yield return new WaitForSeconds(.3f);game.Reveal(rival);yield return new WaitForSeconds(.3f);
        Directory.CreateDirectory("Previews");CaptureTable();ScreenCapture.CaptureScreenshot("Previews/Match_UI.png");yield return new WaitForSeconds(.3f);
    }
    void PrepareFive()
    {
        session.ResetPractice();int guard=0;
        while(!game.Match.HasExactFive()&&game.Match.Turn>=0&&guard++<56)game.Match.Reveal(game.Match.Turn);
        if(!game.Match.HasExactFive())throw new Exception("Fixture seed did not expose a five");
    }
    bool DisplayMatches()
    {
        for(int i=0;i<game.Match.Seats.Count;i++)foreach(bool down in new[]{true,false}){
            var pile=down?game.Match.Seats[i].Draw:game.Match.Seats[i].Open;var anchor=down?game.display.drawAnchors[i]:game.display.openAnchors[i];
            float thickness=CardTableDisplay.CardThickness(down);
            for(int n=0;n<pile.Count;n++){var v=game.display.ViewFor(pile[n].Serial);if(v.definition!=pile[n].Definition||v.FaceDown!=down||Vector3.Distance(v.transform.position,anchor.position+anchor.up*((n+.5f)*thickness))>.0001f||Mathf.Abs(v.cardRenderer.bounds.size.y-thickness)>.0001f)return false;}
        }return game.Match.AllCards().Count()==56&&game.display.ViewCount==56;
    }
    bool CardCornersVisible(Camera camera)
    {
        foreach(var view in game.display.visualRoot.GetComponentsInChildren<FruitCardView>()){
            var bounds=view.cardRenderer.bounds;
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2){
                var point=camera.WorldToViewportPoint(bounds.center+Vector3.Scale(bounds.extents,new Vector3(x,y,z)));
                if(point.z<=0||point.x<.05f||point.x>.95f||point.y<.1f||point.y>.9f)return false;
            }
        }return true;
    }
    IEnumerator StackChecks()
    {
        game.botEnabled=false;
        var camera=controller.playerCamera;
        int[][] distributions={new[]{4,52},new[]{1,15,40},new[]{1,5,14,36},new[]{1,4,8,15,28},new[]{2,4,7,11,14,18},new[]{0,0,0,0,0,56}};
        foreach(var counts in distributions){
            session.roster.StartNewGame(counts.Length);
            foreach(var p in session.participants){var rivalAI=p.GetComponent<CourseRival>();if(rivalAI)rivalAI.enabled=false;}
            var cards=game.Match.AllCards().ToArray();int next=0;
            var hands=counts.Select(count=>{var hand=(IReadOnlyList<FruitCardInstance>)cards.Skip(next).Take(count).ToArray();next+=count;return hand;}).ToArray();
            var fixture=new HalliGalliMatch(hands);game.display.Initialize(fixture);
            Run(counts.Length+" seats: hidden stack heights match ownership "+string.Join("/",counts),()=>{
                for(int i=0;i<counts.Length;i++){
                    var draw=fixture.Seats[i].Draw;
                    if(draw.Count==0)continue;
                    var bottom=game.display.ViewFor(draw[0].Serial).cardRenderer.bounds;
                    var top=game.display.ViewFor(draw[draw.Count-1].Serial).cardRenderer.bounds;
                    Require(Mathf.Abs(bottom.min.y-game.display.drawAnchors[i].position.y)<.0001f,"floating bottom");
                    Require(Mathf.Abs(top.max.y-bottom.min.y-counts[i]*CardTableDisplay.CardThickness(true))<.0001f,"pile height");
                    Require(draw.All(card=>game.display.ViewFor(card.Serial).FaceDown),"hidden face exposed");
                }
                Require(game.display.visualRoot.GetComponentsInChildren<FruitCardView>().Length==56,"stale cards after reset");
            });
            if(counts.All(n=>n>0))for(int i=0;i<counts.Length;i++)fixture.Reveal(i);
            game.display.Refresh(fixture);game.tableView.Focus();yield return new WaitForSeconds(.3f);
            foreach(float aspect in new[]{4f/3,16f/9,21f/9}){
                camera.aspect=aspect;yield return null;
                Run(counts.Length+" seats, tallest pile "+counts.Max()+": cards fit tilted view at "+aspect.ToString("F2"),()=>Require(CardCornersVisible(camera),"card or stack clipped"));
            }
            camera.ResetAspect();yield return null;
            if(counts.Length==2||counts.Length==6&&counts[0]>0){
                Directory.CreateDirectory("Previews");CaptureTable("Previews/Card_Stacks_"+counts.Length+"Players.png");
                ScreenCapture.CaptureScreenshot("Previews/Card_Stacks_"+counts.Length+"Players_UI.png");yield return new WaitForSeconds(.1f);
            }
        }
        game.enabled=true;session.roster.StartNewGame(2);camera.ResetAspect();
    }
    void CaptureTable(string path="Previews/Match_Table.png")
    {
        // Offscreen camera capture is independent of whether the Game tab has focus.
        var camera=player.GetComponent<CharacterMotionDriver>().worldCamera;var target=RenderTexture.GetTemporary(1600,900,24);var previous=RenderTexture.active;var old=camera.targetTexture;
        camera.targetTexture=target;camera.Render();var overlay=player.GetComponent<CharacterMotionDriver>().handCamera;var oldOverlay=overlay.targetTexture;if(overlay.enabled){overlay.targetTexture=target;overlay.Render();overlay.targetTexture=oldOverlay;}RenderTexture.active=target;var image=new Texture2D(1600,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());camera.targetTexture=old;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);Destroy(image);
    }
    HalliGalliMatch Fixture(params int[][] hands)
    {
        int serial=0;return new HalliGalliMatch(hands.Select(h=>(IReadOnlyList<FruitCardInstance>)h.Select(code=>new FruitCardInstance(serial++,deck.Find((FruitKind)(code/10),code%10))).ToArray()).ToArray());
    }
    void ModelChecks()
    {
        Run("Network snapshots reveal no hidden card faces or shuffle identities",()=>{
            var original=new HalliGalliMatch(deck.Deal(6,74));original.Reveal(0);
            var snapshot=original.ExportPublicState(deck);string json=JsonUtility.ToJson(snapshot);
            Require(snapshot.seats.Sum(s=>s.open.Length)==1&&!json.Contains("serial")&&!json.Contains("seed")&&!json.Contains("definition"),"private deck leaked");
            var replica=HalliGalliMatch.FromPublicState(snapshot,deck);
            Require(replica.Seats.SelectMany(s=>s.Draw).All(c=>c.Definition==deck.definitions[0]),"private cards reconstructed");
        });
        Run("Public network snapshots preserve 2 to 6 player rules through complete games",()=>{
            for(int count=2;count<=6;count++){
                var model=new HalliGalliMatch(deck.Deal(count,71));
                for(int step=0;step<450&&!model.Finished;step++){
                    if(model.Paused){
                        var seat=Enumerable.Range(0,count).Where(model.CanClaim).DefaultIfEmpty(-1).First();
                        if(seat>=0)model.Claim(seat);
                        for(int i=0;i<count;i++)model.Sit(i);
                    }else if(model.HasExactFive()||step%17==0||model.Turn<0){
                        var seat=Enumerable.Range(0,count).Where(model.CanStand).DefaultIfEmpty(-1).First();if(seat>=0)model.Stand(seat);
                    }else model.Reveal(model.Turn);
                    var publicState=model.ExportPublicState(deck);
                    var replica=HalliGalliMatch.FromPublicState(publicState,deck);
                    Require(JsonUtility.ToJson(replica.ExportPublicState(deck))==JsonUtility.ToJson(publicState),"public state changed on round trip");
                    Require(replica.HasExactFive()==model.HasExactFive()&&replica.AllCards().Count()==56,"public card totals");
                    for(int i=0;i<count;i++)Require(replica.CanReveal(i)==model.CanReveal(i)&&replica.CanClaim(i)==model.CanClaim(i),"rule flags diverged");
                }
            }
        });
        Run("Network snapshot rejects missing cards and invalid public faces",()=>{
            var state=new HalliGalliMatch(deck.Deal(2,1)).ExportPublicState(deck);state.seats[0].drawCount--;
            bool rejected=false;try{HalliGalliMatch.FromPublicState(state,deck);}catch(ArgumentException){rejected=true;}Require(rejected,"missing card accepted");
            state.seats[0].open=new[]{999};rejected=false;try{HalliGalliMatch.FromPublicState(state,deck);}catch(ArgumentException){rejected=true;}Require(rejected,"invalid face accepted");
        });
        Run("Same fruit exactly five counts; six and mixed fruits do not",()=>{
            var m=Fixture(new[]{3,1},new[]{2,1});m.Reveal(0);m.Reveal(1);Require(m.HasExactFive(),"3+2");
            m=Fixture(new[]{3,1},new[]{3,1});m.Reveal(0);m.Reveal(1);Require(!m.HasExactFive(),"six");
            m=Fixture(new[]{3,1},new[]{12,1});m.Reveal(0);m.Reveal(1);Require(!m.HasExactFive(),"mixed fruit");
            m=Fixture(new[]{5,1},new[]{11,1});m.Reveal(0);Require(m.HasExactFive(),"single five");
        });
        Run("Only the top card of each open pile contributes",()=>{var m=Fixture(new[]{3,11,1},new[]{2,1});m.Reveal(0);m.Reveal(1);m.Reveal(0);Require(!m.HasExactFive()&&m.Seats[0].Open.Count==2,"covered card counted");});
        Run("Three players can contribute to the same total",()=>{var m=Fixture(new[]{3,2},new[]{1,2},new[]{1,2});for(int i=0;i<3;i++)m.Reveal(i);Require(m.HasExactFive(),"3+1+1");});
        Run("No automatic pause when a five appears",()=>{var m=Fixture(new[]{5,2},new[]{11,2});m.Reveal(0);Require(!m.Paused&&m.CanReveal(1),"auto stop");});
        Run("Wrong out-of-turn stand pays each of three opponents once",()=>{
            var m=Fixture(new[]{1,2,3,4},new[]{11,12,13,14},new[]{21,22,23,24},new[]{31,32,33,34});Require(m.Stand(2)==StandResult.Penalized,"out of turn");Require(m.Seats[2].Draw.Count==1&&m.Seats[2].LastPenaltyPaid==3&&m.Seats.Where((s,i)=>i!=2).All(s=>s.Draw.Count==5),"payment");int revision=m.Revision;Require(m.Stand(2)==StandResult.Blocked&&m.Revision==revision,"repeat");Require(m.AllCards().Count()==16&&Enumerable.Range(0,4).All(i=>!m.CanReveal(i)),"freeze");
        });
        Run("All standing players must sit, including penalized players",()=>{var m=Fixture(new[]{1,2,3},new[]{11,12,13},new[]{21,22,23});m.Stand(0);m.Stand(1);m.Sit(0);Require(m.Paused&&!m.CanReveal(0),"one sit");m.Sit(1);Require(!m.Paused&&m.Turn==0&&m.CanReveal(0),"all sit");});
        Run("Valid claim gathers every open pile, once, below existing draw cards",()=>{
            var m=Fixture(new[]{3,11,1},new[]{2,12,1});m.Reveal(0);m.Reveal(1);var tail=m.Seats[0].Draw.ToArray();m.Stand(0);m.Stand(1);Require(m.Claim(0)&&!m.Claim(1),"claim once");Require(m.LastCollectedCount==2&&m.Seats[0].Draw.Take(2).SequenceEqual(tail)&&m.Seats[0].Draw.Count==4&&m.Seats.All(s=>s.Open.Count==0)&&!m.Finished,"gather");m.Sit(0);Require(m.Paused,"waiting for other");m.Sit(1);Require(m.Turn==0&&m.CanReveal(0),"collector starts");
        });
        Run("Seated and penalized participants cannot claim",()=>{var m=Fixture(new[]{5,1},new[]{11,1});m.Reveal(0);m.Stand(0);Require(!m.CanClaim(1),"seated claim");m=Fixture(new[]{1,2},new[]{11,12});m.Stand(0);Require(!m.Claim(0),"wrong claim");});
        Run("Duel ends immediately on the final reveal, even if it is a five",()=>{
            var m=Fixture(new[]{5},new[]{2,11});Require(m.Reveal(0)&&m.Finished&&m.Winner==1&&!m.CanStand(0)&&!m.CanReveal(1)&&m.AllCards().Count()==3,"duel exhaustion");
        });
        Run("Duel penalty ends the match when the draw pile empties despite an open card",()=>{
            var m=Fixture(new[]{1,2},new[]{11,12,13});m.Reveal(0);m.Stand(0);Require(m.Finished&&m.Winner==1&&m.Seats[0].LastPenaltyPaid==1,"penalty exhaustion");
        });
        Run("Three-player empty draw pile retains its chance to claim",()=>{
            var m=Fixture(new[]{3},new[]{2,11},new[]{21,22});m.Reveal(0);m.Reveal(1);Require(!m.Finished&&!m.Seats[0].Eliminated&&m.CanStand(0),"early elimination");m.Stand(0);Require(m.Claim(0)&&!m.Finished&&m.Seats[0].Draw.Count==2,"comeback");
        });
        Run("Insufficient penalty cards are paid clockwise without duplication",()=>{
            var m=Fixture(new[]{1},new[]{11,12},new[]{21,22},new[]{31,32});m.Stand(0);Require(m.Seats[0].Eliminated&&m.Seats[0].MustSit&&m.Seats[0].LastPenaltyPaid==1&&m.Seats[1].Draw.Count==3&&m.Seats[2].Draw.Count==2&&m.AllCards().Count()==7,"insufficient payment");m.Sit(0);Require(!m.Paused&&m.Turn==1,"skip eliminated turn");
        });
        Run("Paying the final owned card eliminates a player with no exposed pile",()=>{var m=Fixture(new[]{1},new[]{11,12});m.Stand(0);Require(m.Seats[0].Eliminated&&m.Finished&&m.Winner==1&&m.Seats[1].Draw.Count==3,"zero-card participant survived");});
        Run("Eliminated players' exposed cards still contribute until collected",()=>{
            var m=Fixture(new[]{3},new[]{11,2},new[]{21,11});m.Reveal(0);m.Reveal(1);m.Reveal(2);m.Stand(0);m.Sit(0);Require(m.Seats[0].Eliminated,"elimination");m.Reveal(1);Require(m.HasExactFive(),"discard removed early");m.Stand(1);m.Claim(1);Require(m.LastCollectedCount==4&&m.Seats[0].Open.Count==0,"collection omitted abandoned pile");
        });
        Run("2 to 6 seats conserve all 56 identities across turns, penalties and claims",()=>{
            for(int count=2;count<=6;count++)for(int seed=0;seed<12;seed++){
                var m=new HalliGalliMatch(deck.Deal(count,seed));var random=new System.Random(seed);
                for(int step=0;step<500&&!m.Finished;step++){
                    if(m.Paused){if(!m.Claimed&&m.HasExactFive()){var candidates=Enumerable.Range(0,count).Where(i=>m.CanClaim(i)).ToArray();if(candidates.Length>0)m.Claim(candidates[0]);}for(int i=0;i<count;i++)m.Sit(i);}
                    else if(m.HasExactFive()||m.Turn<0||random.Next(12)==0){var candidates=Enumerable.Range(0,count).Where(m.CanStand).ToArray();if(candidates.Length>0)m.Stand(candidates[random.Next(candidates.Length)]);}
                    else m.Reveal(m.Turn);
                    var ids=m.AllCards().Select(c=>c.Serial).ToArray();Require(ids.Length==56&&ids.Distinct().Count()==56,"conservation seats="+count+" seed="+seed+" step="+step);
                    Require(!m.Paused||Enumerable.Range(0,count).All(i=>!m.CanReveal(i)),"paused reveal");
                }
            }
        });
    }
    void Run(string name,Action action){try{action();results.Add(new Check{name=name,passed=true,detail="passed"});Debug.Log("PASS "+name);}catch(Exception e){results.Add(new Check{name=name,passed=false,detail=e.Message});Debug.LogError("FAIL "+name+": "+e.Message);}}
    static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
}
#endif
