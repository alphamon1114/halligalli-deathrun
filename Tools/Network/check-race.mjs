import fs from 'node:fs';
import path from 'node:path';
import {spawn} from 'node:child_process';

// node Tools/Network/check-race.mjs <project> [--barricade-only] [--attach <probe-folder>] [--headless]
// Attached peers must use roles host/client1. They are never terminated by this runner.
const project=path.resolve(process.argv[2]??'.');
const barricadeOnly=process.argv.includes('--barricade-only'),graphical=!process.argv.includes('--headless');
const attachIndex=process.argv.indexOf('--attach'),attached=attachIndex>=0;
if(attached&&!process.argv[attachIndex+1])throw new Error('--attach requires an existing probe folder');
const folder=attached?path.resolve(process.argv[attachIndex+1]):path.join(project,'Tools/Network/Runs',(barricadeOnly?'barricade-':'race-')+Date.now());
fs.mkdirSync(folder,{recursive:true});fs.mkdirSync(path.join(project,'TestResults'),{recursive:true});
const reportFile=path.join(project,'TestResults',barricadeOnly?'barricade-network.json':'race-network.json');
const startedUtc=new Date().toISOString(),checks=[],processes=[],sequences={},cached={},observations=[];
const limitations=graphical?[]:['Headless mode omits screenshot captures; run BarricadeValidationRunner with graphics for front/back visible-pixel assertions.'];
let barricadeSeed;
const sleep=ms=>new Promise(resolve=>setTimeout(resolve,ms));
function state(role){try{cached[role]=JSON.parse(fs.readFileSync(path.join(folder,'state-'+role+'.json'),'utf8'));}catch{}return cached[role];}
const host=()=>state('host'),guest=()=>state('client1'),peers=()=>[host(),guest()];
const rolls=[];
function report(status){fs.writeFileSync(reportFile,JSON.stringify({utc:startedUtc,completedUtc:status==='running'?null:new Date().toISOString(),status,folder,attached,graphical,protocol:host()?.protocol,passed:checks.filter(c=>c.passed).length,failed:checks.filter(c=>!c.passed).length,checks,rolls,observations,limitations},null,2));}
function observe(name){observations.push({name,host:structuredClone(host()),guest:structuredClone(guest())});}
function check(name,passed,detail=''){checks.push({name,passed,detail});console.log((passed?'PASS ':'FAIL ')+name);if(!passed)throw new Error(name);}
function failure(error){if(checks.at(-1)?.passed!==false)checks.push({name:String(error),passed:false});fs.writeFileSync(path.join(folder,barricadeOnly?'barricade-failure.json':'failure.json'),JSON.stringify({utc:new Date().toISOString(),error:error.stack,host:host(),guest:guest()},null,2));process.exitCode=1;}
report('running');
const watchdog=setTimeout(()=>{failure(new Error('Scenario exceeded '+(barricadeOnly?'six':'fifteen')+' minutes'));for(const p of processes)p.kill();report('failed');process.exit(1);},(barricadeOnly?6:15)*60000);
async function wait(predicate,timeout=15000){const end=Date.now()+timeout;while(Date.now()<end){for(const p of processes)if(p.spawnError||p.exitCode!==null)throw p.spawnError??new Error('Player exited: '+p.exitCode);for(const s of peers())if(s?.error)throw new Error(s.error);if(predicate())return;await sleep(25);}throw new Error('Scenario state timeout');}
async function command(role,action,extra={}){
 const file=path.join(folder,'command-'+role+'.json');
 if(sequences[role]===undefined){let previous=0;try{previous=JSON.parse(fs.readFileSync(file,'utf8')).sequence??0;}catch{}sequences[role]=Math.max(state(role)?.commandSequence??0,previous);}
 const sequence=++sequences[role];
 fs.writeFileSync(file+'.tmp',JSON.stringify({sequence,action,...extra}));
 for(let i=0;;i++){try{fs.renameSync(file+'.tmp',file);break;}catch(e){if(i===99)throw e;await sleep(10);}}
 await wait(()=>state(role)?.commandSequence===sequence);
 if(state(role).error)throw new Error(state(role).error);
}
function launch(role){
 if(attached)return;
 const args=graphical&&role==='host'?['-screen-fullscreen','0','-screen-width','1440','-screen-height','900']:['-batchmode','-nographics'];
 const child=spawn(path.join(project,'Build/Windows/HalliGalliMovement.exe'),[...args,'-disable-audio','--net-probe',role,'--probe-dir',folder,'--net-port','17893','-logFile',path.join(folder,role+'.log')],{stdio:'ignore'});
 processes.push(child);child.on('error',error=>child.spawnError=error);
}
async function capture(name){if(!graphical)return;const file=path.join(folder,name+'.png');if(fs.existsSync(file))fs.unlinkSync(file);await command('host','capture',{value:name});await wait(()=>fs.existsSync(file));}
async function teleport(seat,z,x=0){await command('host','teleport',{seat,position:{x,y:.025,z}});await wait(()=>Math.abs(host().actors[seat].position.z-z)<.3&&host().actors[seat].grounded);await sleep(100);}
async function use(role){await command(role,'pointer',{yaw:0,pitch:0});await command(role,'race-item');}
async function take(item,seat=0,leader=seat===1){
 const role=seat===0?'host':'client1';
 if(host().race.actors[seat].item!==0){
  const life=host().actors[seat].respawns;
  await command(role,'action',{value:'Respawn'});
  await wait(()=>host().actors[seat].respawns>life&&!host().actors[seat].respawning&&host().race.actors[seat].item===0&&guest().race.actors[seat].item===0);
 }
 for(let tries=0;tries<32;tries++){
  await teleport(1-seat,leader?5:214);
  const box=host().race.boxes.find(b=>b.cooldown<=0&&b.position.z>30&&b.position.z<205);
  if(!box){await sleep(1000);continue;}
  const seed=item===3&&barricadeSeed!==undefined?barricadeSeed:125+tries;
  await command('host','random-seed',{value:String(seed)});
  const acquisitions=host().race.actors[seat].acquisitions;
  await teleport(seat,box.position.z,box.position.x);
  await wait(()=>host().race.actors[seat].acquisitions>acquisitions&&host().race.actors[seat].item!==0&&guest().race.actors[seat].item===host().race.actors[seat].item);
  if((host().race.leader===seat)!==leader)throw new Error('Pickup setup did not establish the requested rank');
  rolls.push({leader,item:host().race.actors[seat].item,seed});
  if(host().race.actors[seat].item===item){if(item===3)barricadeSeed=seed;return;}
  const before=host().actors[seat].respawns;
  await command(role,'action',{value:'Respawn'});
  await wait(()=>host().actors[seat].respawns>before&&!host().actors[seat].respawning&&host().race.actors[seat].item===0);
 }
 throw new Error('Did not acquire requested item through live boxes: '+item);
}
async function positions(){await teleport(0,160);await teleport(1,178);await command('host','pointer',{yaw:0,pitch:0});}
async function startRace(){
 await command('host','map',{value:'item-race'});await command('client1','ready');await wait(()=>host().canStart);
 await command('host','start');await wait(()=>host().playing&&guest().playing&&host().map==='item-race'&&guest().map==='item-race'&&host().race&&guest().race,60000);
}
async function stand(){await command('host','five');await command('host','action',{value:'Interact'});await command('client1','action',{value:'Interact'});await wait(()=>host().actors.slice(0,2).every(a=>!a.seated)&&guest().actors.slice(0,2).every(a=>!a.seated));}
async function airborneUse(item,seat,yaw=0,rejected=false){
 const role=seat===0?'host':'client1',label=(item===2?'banana':'barricade')+'-'+role+(rejected?'-unsupported':'');
 await command(role,'move',{yaw});
 await command(role,'pointer',{yaw,pitch:0});
 await wait(()=>[host(),guest()].every(s=>s.actors[seat].grounded&&Math.hypot(s.actors[seat].velocity.x,s.actors[seat].velocity.z)<.1));
 const before=host().race.actors[seat],life=host().actors[seat].respawns;
 const previousIds=new Set(host().race.projectiles.map(p=>p.id));
 await command(role,'move',{yaw,jump:true});
 await wait(()=>[host(),guest()].every(s=>!s.actors[seat].grounded&&s.actors[seat].position.y>.35)&&host().actors[seat].velocity.y>0,2500);
 // Snapshot before sending use: a real jump, not a floating teleport or a post-landing position.
 const preUse={host:host(),guest:guest()};
 fs.writeFileSync(path.join(folder,'airborne-'+label+'-preuse.json'),JSON.stringify(preUse,null,2));
 await command(role,'race-item');
 if(rejected)await wait(()=>host().worldTime>preUse.host.worldTime+.3&&guest().worldTime>preUse.guest.worldTime+.3);
 else await wait(()=>[host(),guest()].every(s=>s.race.actors[seat].uses===before.uses+1&&s.race.projectiles.some(p=>p.owner===seat&&p.item===item&&!previousIds.has(p.id))),2500);
 const afterUse={host:host(),guest:guest()};
 const evidence={item,seat,yaw,rejected,preUse,afterUse};
 fs.writeFileSync(path.join(folder,'airborne-'+label+'.json'),JSON.stringify(evidence,null,2));
 await command(role,'move',{yaw,jump:false});
 if(rejected){
  check('Unsupported airborne '+label+' retains inventory on both peers',[afterUse.host,afterUse.guest].every(s=>s.actors[seat].respawns===life&&s.race.actors[seat].item===item&&s.race.actors[seat].itemSequence===before.itemSequence&&s.race.actors[seat].uses===before.uses&&!s.race.projectiles.some(p=>!previousIds.has(p.id))),JSON.stringify({position:preUse.host.actors[seat].position,yaw}));
  await wait(()=>host().actors[seat].grounded&&guest().actors[seat].grounded);
  return null;
 }
 const entity=afterUse.host.race.projectiles.find(p=>p.owner===seat&&p.item===item&&!previousIds.has(p.id));
 const remote=afterUse.guest.race.projectiles.find(p=>p.id===entity.id);
 const offset=item===2?1.15:3,angle=yaw*Math.PI/180,start=preUse.host.actors[seat].position;
 check('Actual jump deploys '+label+' on the floor on both peers',!afterUse.host.actors[seat].grounded&&afterUse.host.race.actors[seat].item===0&&afterUse.guest.race.actors[seat].item===0&&[entity,remote].every(p=>Math.abs(p.position.y-(item===2?.045:0))<.06&&Math.abs(p.position.x-(start.x-Math.sin(angle)*offset))<.15&&Math.abs(p.position.z-(start.z-Math.cos(angle)*offset))<.15),JSON.stringify({preUse:start,host:entity.position,guest:remote.position}));
 return entity;
}
async function deployed(){
 await take(3,0,true);
 for(const role of ['host','client1'])await command(role,'move',{yaw:0});
 await teleport(0,178);await teleport(1,173.9);
 await wait(()=>peers().every(s=>s.actors.slice(0,2).every(a=>a.kickCooldown<=0&&a.attack==='None')));
 await use('host');await wait(()=>peers().every(s=>s.race.projectiles.some(p=>p.item===3&&p.owner===0)));
 const wall=host().race.projectiles.find(p=>p.item===3&&p.owner===0);
 check('Fresh live pickup deployment has zero retained damage on both peers',peers().every(s=>s.race.projectiles.find(p=>p.id===wall.id)?.barricadeHits===0),'entity='+wall.id);
 observe('Fresh barricade '+wall.id);return wall;
}
const wallOn=(s,wall)=>s.race.projectiles.find(p=>p.id===wall.id);
async function kick(role,wall,hits){
 const seat=role==='host'?0:1,before=host().actors[seat].attacks;
 await command(role,'action',{value:'Kick'});
 await wait(()=>peers().every(s=>s.actors[seat].attacks===before+1&&(hits===2?!wallOn(s,wall):wallOn(s,wall)?.barricadeHits===hits)),2500);
 observe(role+' kick '+hits+' on '+wall.id);
}
async function passage(wall,label){
 await teleport(0,178,5);await command('client1','move',{z:1,yaw:0});await sleep(700);await command('client1','move',{yaw:0});
 check(label,peers().every(s=>s.actors[1].position.z>wall.position.z+1),'host='+host().actors[1].position.z+' guest='+guest().actors[1].position.z);
}
async function breakable(cooperative){
 const wall=await deployed();await teleport(0,wall.position.z+1.1);
 await command('host','pointer',{yaw:180,pitch:0});await command('client1','pointer',{yaw:0,pitch:0});
 const pushSequence=guest().actors[1].attacks;
 await command('client1','action',{value:'Push'});
 await wait(()=>peers().every(s=>s.actors[1].attacks>pushSequence&&s.actors[1].attack==='None'));
 check('Left-click push cannot damage a barricade',peers().every(s=>wallOn(s,wall)?.barricadeHits===0));
 await kick('client1',wall,1);
 const sequence=host().actors[1].attacks;
 await command('client1','action',{value:'Kick'});await command('client1','action',{value:'Kick'});await sleep(350);
 check('One guest kick contact and cooldown retries count only once',peers().every(s=>wallOn(s,wall)?.barricadeHits===1&&s.actors[1].attacks===sequence));
 await command('client1','move',{z:1,yaw:0});await sleep(350);await command('client1','move',{yaw:0});
 check('Damaged barricade still blocks the guest CharacterController on both peers',peers().every(s=>wallOn(s,wall)?.barricadeHits===1&&s.actors[1].position.z<wall.position.z-.3&&s.actors[1].position.z>wall.position.z-1));
 if(graphical&&!cooperative){
  await teleport(0,wall.position.z+5);await command('host','pointer',{yaw:180,pitch:6});await capture('barricade-front-cracked');
  await teleport(0,wall.position.z-5);await command('host','pointer',{yaw:0,pitch:6});await capture('barricade-back-cracked');
  await teleport(0,178,5);
 }
 if(!cooperative)await wait(()=>peers().every(s=>s.actors[1].kickCooldown<=0));
 await kick(cooperative?'host':'client1',wall,2);
 check(cooperative?'Host and guest share two-hit damage without a client-side double hit':'Second guest kick after cooldown destroys the same wall',peers().every(s=>!wallOn(s,wall)));
 await passage(wall,'Broken barricade frees real guest travel on both peers');
}
async function focusedBarricades(){
 for(const cooperative of [false,true])await breakable(cooperative);
 let wall=await deployed();
 await teleport(1,wall.position.z-3);await command('client1','pointer',{yaw:0,pitch:0});
 for(const miss of ['out-of-range','aimed-away']){
  if(miss==='aimed-away'){await wait(()=>peers().every(s=>s.actors[1].kickCooldown<=0));await teleport(1,wall.position.z-1.1);await command('client1','pointer',{yaw:180,pitch:0});}
  const before=host().actors[1].attacks;
  await command('client1','action',{value:'Kick'});await wait(()=>peers().every(s=>s.actors[1].attacks===before+1&&s.actors[1].attack==='None'));
  check('Actual '+miss+' guest kick leaves authoritative wall intact',peers().every(s=>wallOn(s,wall)?.barricadeHits===0));observe(miss);
 }
 await wait(()=>peers().every(s=>s.actors[1].kickCooldown<=0));await command('client1','pointer',{yaw:0,pitch:0});await kick('client1',wall,1);
 await teleport(0,host().bellLanding.z,host().bellLanding.x);await command('host','action',{value:'Interact'});
 await wait(()=>peers().every(s=>s.actors.every(a=>a.seated)&&s.race.projectiles.length===0));
 check('Real bell/new-round transition clears a damaged barricade on both peers',peers().every(s=>!wallOn(s,wall)));await stand();
 wall=await deployed();const expires=host().worldTime+wall.remaining;
 await command('client1','pointer',{yaw:0,pitch:0});
 await wait(()=>host().worldTime>=expires-6);await kick('client1',wall,1);
 await wait(()=>host().worldTime>=expires-.5,10000);
 check('Damage preserves the solid wall until the original eight-second deadline',peers().every(s=>wallOn(s,wall)?.barricadeHits===1),'deadline='+expires+' worldTime='+host().worldTime);
 await wait(()=>peers().every(s=>!wallOn(s,wall)),2000);
 check('Damaging the wall does not extend its original expiry',host().worldTime<expires+.6,'deadline='+expires+' worldTime='+host().worldTime);
 await passage(wall,'Expired barricade frees real guest travel on both peers');
 wall=await deployed();await command('client1','pointer',{yaw:0,pitch:0});await kick('client1',wall,1);
 const life=host().actors[0].respawns;await command('host','action',{value:'Respawn'});
 await wait(()=>peers().every(s=>s.actors[0].respawns>life&&!s.actors[0].respawning&&!wallOn(s,wall)),3000);
 check('Owner respawn clears damaged barricades on both peers before expiry',peers().every(s=>!wallOn(s,wall)));await passage(wall,'Owner-life cleanup removes guest collision');
 await command('host','room');await wait(()=>peers().every(s=>!s.playing));
}
try{
 launch('host');await wait(()=>host()?.active&&!host().busy,60000);launch('client1');await wait(()=>host().members.length===2&&guest()?.members.length===2&&!guest().busy,60000);
 const oldTimes=peers().map(s=>s.worldTime);await wait(()=>peers().every((s,i)=>s.worldTime>oldTimes[i]+.1),5000);
 check('Attached or launched peers publish fresh matching protocol state',host().protocol===guest().protocol);
 if(host().playing){await command('host','room');await wait(()=>peers().every(s=>!s.playing));}
 await startRace();check('Final official race loads on both peers with identical box placement',JSON.stringify(host().race.boxes.map(b=>b.position))===JSON.stringify(guest().race.boxes.map(b=>b.position))&&host().bellLanding.z>=210);
 await command('host','race-item');check('Seated player cannot create an item attack',host().race.projectiles.length===0);
 await stand();await command('host','pointer',{yaw:0,pitch:7});await capture(barricadeOnly?'barricade-start':'race-start');
 if(barricadeOnly)await focusedBarricades();
 else{
 await take(1);
 const spare=host().race.boxes.find(b=>b.cooldown<=0&&b.position.z>30);
 const acquired=host().race.actors[0].acquisitions;
 await teleport(0,spare.position.z,spare.position.x);
 check('Holding an item leaves other boxes available',host().race.actors[0].acquisitions===acquired&&host().race.boxes[spare.index].cooldown<=0);
 await positions();
 const held=host().race.actors[0].item;
 await command('host','pointer',{yaw:180,pitch:0});await command('host','race-item');
 check('Missile without an aimed lock preserves inventory',host().race.actors[0].item===held);
 await command('host','pointer',{yaw:0,pitch:0});await capture('race-lock-on');
 await teleport(1,168);let flips=host().race.actors[1].flips;
 await command('client1','move',{z:1,yaw:0});await wait(()=>host().actors[1].velocity.z>4);
 await use('host');await wait(()=>host().race.actors[1].flips>flips&&guest().race.actors[1].flips>flips);
 check('Homing hit forces victim into airborne somersault',host().race.actors[1].effectRemaining>0&&host().actors[1].position.y>.05);
 check('Missile removes a running victims horizontal momentum on both peers',[host(),guest()].every(s=>Math.hypot(s.actors[1].velocity.x,s.actors[1].velocity.z)<.25));
 const missileStop=host().actors[1].position.z;let apex=host().actors[1].position.y,stopped=true;
 await sleep(400);await capture('race-missile-hit');
 while(host().race.actors[1].effectRemaining>.15){
  apex=Math.max(apex,host().actors[1].position.y);
  stopped&&=[host(),guest()].every(s=>Math.hypot(s.actors[1].velocity.x,s.actors[1].velocity.z)<.25)&&Math.abs(host().actors[1].position.z-missileStop)<.15;
  await sleep(30);
 }
 check('Missile launches over 2.8m while held forward cannot move the victim',apex>2.8&&stopped,'peak metres='+apex);
 check('Victim first-person camera never rolls with the somersault',Math.abs(guest().actors[1].viewRoll)<.1);
 await wait(()=>host().race.actors[1].effectRemaining<=0&&host().actors[1].grounded);
 await wait(()=>host().actors[1].velocity.z>4);check('Missile victim regains ordinary movement after recovery',host().actors[1].grounded);
 await command('client1','move',{z:0,yaw:0});
 await take(1,0,true);await teleport(0,178);await teleport(1,166);
 await command('host','pointer',{yaw:180,pitch:0});flips=host().race.actors[1].flips;
 check('Leader can acquire a missile through a live item box',host().race.leader===0&&host().race.actors[0].item===1);
 await command('host','race-item');await wait(()=>host().race.actors[1].flips>flips&&guest().race.actors[1].flips>flips);
 check('Leader turning backwards hits the trailing player with a missile',host().race.actors[0].item===0&&host().race.actors[0].lockTarget===1&&host().race.actors[1].effectRemaining>0);
 await capture('race-leader-rear-missile');
 await wait(()=>host().race.actors[1].effectRemaining<=0&&host().actors[1].grounded);
 await take(1);await positions();const hitsBefore=host().race.missileHits,lifeBefore=host().actors[1].respawns;
 await use('host');await command('client1','action',{value:'Respawn'});
 await wait(()=>host().actors[1].respawns>lifeBefore&&host().race.projectiles.every(p=>p.item!==1));
 check('In-flight missile cannot hit a new target life',host().race.missileHits===hitsBefore);
 await take(2);await positions();let slips=host().race.actors[1].slips;
 let banana=await airborneUse(2,0);
 await command('host','pointer',{yaw:180,pitch:35});await capture('race-airborne-banana-host');
 await teleport(0,160,3);await command('host','pointer',{yaw:220,pitch:15});
 await command('host','teleport',{seat:1,position:{...banana.position,y:.025}});
 await wait(()=>host().race.actors[1].slips>slips&&guest().race.actors[1].slips>slips);
 check('Placed banana causes authoritative sliding on both peers',host().race.actors[1].effectRemaining>0&&Math.hypot(host().actors[1].velocity.x,host().actors[1].velocity.z)>1);
 await capture('race-banana-hit');await wait(()=>host().race.actors[1].effectRemaining<=0);
 await take(3,1,false);await positions();await teleport(0,170);
 let barricade=await airborneUse(3,1),remoteBarricade=guest().race.projectiles.find(p=>p.id===barricade.id);
 await capture('race-airborne-barricade-client');
 check('Barricade occupies about one third of the track with matching remote dimensions',barricade.size.x>4.3&&barricade.size.x<5&&JSON.stringify(barricade.size)===JSON.stringify(remoteBarricade.size),'size='+JSON.stringify(barricade.size));
 await capture('race-barricade');
 await command('host','move',{z:1,yaw:0});await sleep(1300);await command('host','move',{z:0,yaw:0});
 check('Barricade physically stops the host rather than giving an impact boost',host().actors[0].position.z<barricade.position.z-.3&&host().actors[0].position.z>173);
 await command('host','move',{x:1,z:0,yaw:0});await sleep(900);await command('host','move',{x:0,z:1,yaw:0});await sleep(1000);await command('host','move',{z:0,yaw:0});
 check('The remaining road is wide enough to run around the barricade',host().actors[0].position.z>barricade.position.z+1&&host().actors[0].grounded);
 await wait(()=>host().race.projectiles.every(p=>p.item!==3)&&guest().race.projectiles.every(p=>p.item!==3),12000);
 await teleport(0,170);await command('host','move',{z:1,yaw:0});await sleep(1500);await command('host','move',{z:0,yaw:0});
 check('Expired barricade removes its collision as well as its visual',host().actors[0].position.z>176);
 await take(3,0,true);await teleport(0,178,6);
 await airborneUse(3,0,270,true);
 await teleport(0,178);await teleport(1,170);barricade=await airborneUse(3,0);
 await command('host','pointer',{yaw:180,pitch:15});await capture('race-airborne-barricade-host');
 await command('client1','move',{z:1,yaw:0});await sleep(1300);await command('client1','move',{z:0,yaw:0});
 check('Predicting guest and host agree that the barricade blocks movement',[host(),guest()].every(s=>s.actors[1].position.z<barricade.position.z-.25&&s.actors[1].position.z>173));
 const ownerLife=host().actors[0].respawns;await command('host','action',{value:'Respawn'});
 await wait(()=>host().actors[0].respawns>ownerLife&&guest().race.projectiles.every(p=>p.item!==3));
 await command('client1','move',{z:1,yaw:0});await sleep(900);await command('client1','move',{z:0,yaw:0});
 check('Owner respawn removes deployed barricade collision on the guest',host().actors[1].position.z>177&&guest().actors[1].position.z>177);
 for(const cooperative of [false,true])await breakable(cooperative);
 await take(2,1);await teleport(0,160);await teleport(1,178,6.8);
 await airborneUse(2,1,270,true);
 await teleport(1,178);banana=await airborneUse(2,1);
 await teleport(0,172,3);await command('host','pointer',{yaw:325,pitch:20});await capture('race-airborne-banana-client');
 check('Leader live pickups include all three items',[1,2,3].every(i=>rolls.some(r=>r.leader&&r.item===i))&&rolls.filter(r=>r.leader).every(r=>[1,2,3].includes(r.item)));
 check('Trailing live pickups include all three items',[1,2,3].every(i=>rolls.some(r=>!r.leader&&r.item===i))&&rolls.filter(r=>!r.leader).every(r=>[1,2,3].includes(r.item)));
 await teleport(0,host().bellLanding.z,host().bellLanding.x);await command('host','action',{value:'Interact'});
 await wait(()=>host().bells===1&&guest().bells===1&&host().actors.every(a=>a.seated));
 await wait(()=>host().race.actors.every(a=>a.item===0&&a.effectRemaining<=0)&&host().race.projectiles.length===0&&guest().race.projectiles.length===0);
 check('Bell award preserves card rules and clears all round item effects',host().cardState===guest().cardState);
 await stand();const fallsBefore=host().actors[0].falls;
 await command('host','move',{z:1,yaw:0});await wait(()=>host().actors[0].position.z>=219,70000);await command('host','move',{z:0,yaw:0});
 check('Entire continuous course can be run with normal movement',host().actors[0].grounded&&host().actors[0].falls===fallsBefore&&host().actors[0].position.z<224);
 await command('host','pointer',{yaw:0,pitch:12});await capture('race-finish');await command('host','action',{value:'Interact'});await wait(()=>host().bells===2&&guest().bells===2);
 await command('host','room');await wait(()=>!host().playing&&!guest().playing);
 await command('host','add-bot');await startRace();await command('host','five');await command('host','action',{value:'Interact'});
 await wait(()=>host().race.actors[2].acquisitions>0&&host().actors[2].position.z>30,25000);
 const aiItem=host().race.actors[2].item;
 check('AI races along the same track and collects an unrestricted item',host().actors[2].aiEnabled&&[1,2,3].includes(aiItem));
 const bot=host().actors[2],aiUses=host().race.actors[2].uses,aiHits=host().race.missileHits+host().race.bananaHits;
 await teleport(0,bot.position.z+(aiItem===1?12:-6),bot.position.x);
 await wait(()=>host().race.actors[2].uses>aiUses&&guest().race.actors[2].uses>aiUses);
 check('AI uses its acquired missile at a forward target or defense at a follower',host().race.projectiles.some(p=>p.item===aiItem&&p.owner===2)||host().race.missileHits+host().race.bananaHits>aiHits);
 await wait(()=>host().bells===1&&guest().bells===1&&host().actors.every(a=>a.seated),70000);
 check('AI can complete the track and ring the original bell',host().cardState===guest().cardState);
 await command('host','room');await wait(()=>!host().playing&&!guest().playing);
 await command('host','map',{value:'first-steps'});await command('client1','ready');await wait(()=>host().canStart);await command('host','start');
 await wait(()=>host().playing&&guest().playing&&host().map==='first-steps'&&guest().map==='first-steps',60000);
 // Unity JsonUtility serializes a null nested DTO as its empty default object.
 check('Switching to an existing map removes race state on both peers',[host(),guest()].every(s=>!s.race.running&&s.race.actors.length===0&&s.race.boxes.length===0&&s.race.projectiles.length===0));
 await command('host','controls');await capture('race-controls');
 }
 check('No runtime probe errors',!host().error&&!guest().error);
}catch(error){failure(error);}
finally{clearTimeout(watchdog);for(const p of processes)p.kill();report(process.exitCode?'failed':limitations.length?'limited':'passed');}
