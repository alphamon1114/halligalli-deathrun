import fs from 'node:fs';
import path from 'node:path';
import {spawn} from 'node:child_process';

const project=path.resolve(process.argv[2]??'.');
const folder=path.join(project,'Tools/Network/Runs','race-'+Date.now());
fs.mkdirSync(folder,{recursive:true});
const checks=[],processes=[],sequences={},cached={};
const sleep=ms=>new Promise(resolve=>setTimeout(resolve,ms));
function state(role){try{cached[role]=JSON.parse(fs.readFileSync(path.join(folder,'state-'+role+'.json'),'utf8'));}catch{}return cached[role];}
const host=()=>state('host'),guest=()=>state('client1');
const rolls=[];
function check(name,passed){checks.push({name,passed});if(!passed)throw new Error(name);}
async function wait(predicate,timeout=15000){const end=Date.now()+timeout;while(Date.now()<end){if(predicate())return;await sleep(40);}throw new Error('Scenario state timeout');}
async function command(role,action,extra={}){
 const file=path.join(folder,'command-'+role+'.json');const sequence=sequences[role]=(sequences[role]??0)+1;
 fs.writeFileSync(file+'.tmp',JSON.stringify({sequence,action,...extra}));
 for(let i=0;;i++){try{fs.renameSync(file+'.tmp',file);break;}catch(e){if(i===99)throw e;await sleep(10);}}
 await wait(()=>state(role)?.commandSequence===sequence);
 if(state(role).error)throw new Error(state(role).error);
}
function launch(role){
 const args=role==='host'?['-screen-fullscreen','0','-screen-width','1440','-screen-height','900']:['-batchmode','-nographics'];
 const child=spawn(path.join(project,'Build/Windows/HalliGalliMovement.exe'),[...args,'--net-probe',role,'--probe-dir',folder,'--net-port','17893','-logFile',path.join(folder,role+'.log')],{stdio:'ignore'});
 processes.push(child);child.on('error',error=>console.error(error));
}
async function capture(name){await command('host','capture',{value:name});await wait(()=>fs.existsSync(path.join(folder,name+'.png')));}
async function teleport(seat,z,x=0){await command('host','teleport',{seat,position:{x,y:.025,z}});await wait(()=>Math.abs(host().actors[seat].position.z-z)<.3&&host().actors[seat].grounded);await sleep(100);}
async function use(role){await command(role,'pointer',{yaw:0,pitch:0});await command(role,'race-item');}
async function take(item,seat=0,leader=seat===1){
 const role=seat===0?'host':'client1';
 for(let tries=0;tries<32;tries++){
  await teleport(1-seat,leader?5:214);
  const box=host().race.boxes.find(b=>b.cooldown<=0&&b.position.z>30&&b.position.z<205);
  if(!box){await sleep(1000);continue;}
  await command('host','random-seed',{value:String(125+tries)});
  await teleport(seat,box.position.z,box.position.x);
  await wait(()=>host().race.actors[seat].item!==0&&guest().race.actors[seat].item===host().race.actors[seat].item);
  rolls.push({leader,item:host().race.actors[seat].item});
  if(host().race.actors[seat].item===item)return;
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
try{
 launch('host');await wait(()=>host()?.active&&!host().busy,60000);launch('client1');await wait(()=>host().members.length===2&&guest()?.members.length===2&&!guest().busy);
 await startRace();check('Final official race loads on both peers with identical box placement',JSON.stringify(host().race.boxes.map(b=>b.position))===JSON.stringify(guest().race.boxes.map(b=>b.position))&&host().bellLanding.z>=210);
 await command('host','race-item');check('Seated player cannot create an item attack',host().race.projectiles.length===0);
 await stand();await command('host','pointer',{yaw:0,pitch:7});await capture('race-start');
 await take(3,1);check('First-place racer receives a shield on both peers',host().race.actors[1].item===3&&guest().race.actors[1].item===3);
 await take(1);
 const spare=host().race.boxes.find(b=>b.cooldown<=0&&b.position.z>30);
 const acquired=host().race.actors[0].acquisitions;
 await teleport(0,spare.position.z,spare.position.x);
 check('Holding an item leaves other boxes available',host().race.actors[0].acquisitions===acquired&&host().race.boxes[spare.index].cooldown<=0);
 await positions();
 const held=host().race.actors[0].item;
 await command('host','pointer',{yaw:180,pitch:0});await command('host','race-item');
 check('Missile without a forward lock preserves inventory',host().race.actors[0].item===held);
 await command('host','pointer',{yaw:0,pitch:0});await capture('race-lock-on');
 await use('client1');await wait(()=>host().race.actors[1].shieldRemaining>0&&guest().race.actors[1].shieldRemaining>0);
 let blocked=host().race.blockedHits,flips=host().race.actors[1].flips;
 await use('host');await wait(()=>host().race.blockedHits>blocked&&guest().race.blockedHits>blocked);
 check('Remote shield blocks homing missile without somersault',host().race.actors[1].flips===flips);
 await wait(()=>host().race.actors[1].shieldRemaining<=0,12000);
 await take(1);await positions();await teleport(1,168);flips=host().race.actors[1].flips;
 await use('host');await wait(()=>host().race.actors[1].flips>flips&&guest().race.actors[1].flips>flips);
 check('Homing hit forces victim into airborne somersault',host().race.actors[1].effectRemaining>0&&host().actors[1].position.y>.05);
 check('Victim first-person camera never rolls with the somersault',Math.abs(guest().actors[1].viewRoll)<.1);
 await sleep(180);await capture('race-missile-hit');await wait(()=>host().race.actors[1].effectRemaining<=0&&host().actors[1].grounded);
 await take(1);await positions();const hitsBefore=host().race.missileHits,lifeBefore=host().actors[1].respawns;
 await use('host');await command('client1','action',{value:'Respawn'});
 await wait(()=>host().actors[1].respawns>lifeBefore&&host().race.projectiles.every(p=>p.item!==1));
 check('In-flight missile cannot hit a new target life',host().race.missileHits===hitsBefore);
 await take(2);await positions();let slips=host().race.actors[1].slips;
 await use('host');await wait(()=>host().race.projectiles.some(p=>p.item===2));
 let banana=host().race.projectiles.find(p=>p.item===2);await teleport(0,160,3);await command('host','pointer',{yaw:220,pitch:15});
 await command('host','teleport',{seat:1,position:{...banana.position,y:.025}});
 await wait(()=>host().race.actors[1].slips>slips&&guest().race.actors[1].slips>slips);
 check('Placed banana causes authoritative sliding on both peers',host().race.actors[1].effectRemaining>0&&Math.hypot(host().actors[1].velocity.x,host().actors[1].velocity.z)>1);
 await capture('race-banana-hit');await wait(()=>host().race.actors[1].effectRemaining<=0);
 await take(3,1);await take(2);await positions();await use('client1');await wait(()=>host().race.actors[1].shieldRemaining>0);
 blocked=host().race.blockedHits;slips=host().race.actors[1].slips;
 await use('host');await wait(()=>host().race.projectiles.some(p=>p.item===2));banana=host().race.projectiles.find(p=>p.item===2);
 await teleport(0,160,3);await command('host','teleport',{seat:1,position:{...banana.position,y:.025}});await wait(()=>host().race.blockedHits>blocked&&guest().race.blockedHits>blocked);
 check('Temporary shield also blocks banana sliding',host().race.actors[1].slips===slips);
 await take(4,1);await positions();const balls=host().race.ballHits;
 await use('client1');await wait(()=>host().race.projectiles.some(p=>p.item===4));await sleep(250);await capture('race-rolling-ball');
 await wait(()=>host().race.ballHits>balls&&guest().race.ballHits>balls);
 check('Backward rolling ball pushes the pursuing player backward',host().actors[0].velocity.z< -1);
 await wait(()=>host().race.actors[0].effectRemaining<=0);
 await take(5);await positions();await wait(()=>host().race.actors[1].shieldRemaining<=0);
 let lasers=host().race.laserHits;
 await use('host');await wait(()=>host().race.laserHits>lasers&&guest().race.laserHits>lasers);
 check('Straight laser hits and pushes the player ahead',host().actors[1].velocity.z>1);
 await capture('race-laser');
 await sleep(600);check('Laser beam display cannot repeatedly damage the victim',host().race.laserHits===lasers+1);
 await take(3,1);await take(5);await positions();await use('client1');await wait(()=>host().race.actors[1].shieldRemaining>0);
 blocked=host().race.blockedHits;lasers=host().race.laserHits;
 await use('host');await wait(()=>host().race.blockedHits>blocked&&guest().race.blockedHits>blocked);
 check('Shield blocks a straight laser',host().race.laserHits===lasers);
 await take(3,0,true);await take(4,1);await positions();await use('host');await wait(()=>host().race.actors[0].shieldRemaining>0);
 blocked=host().race.blockedHits;const ballHits=host().race.ballHits;
 await use('client1');await wait(()=>host().race.blockedHits>blocked&&guest().race.blockedHits>blocked);
 check('Shield blocks the backward rolling ball',host().race.ballHits===ballHits);
 await take(5);await positions();lasers=host().race.laserHits;
 await command('host','pointer',{yaw:45,pitch:0});await command('host','race-item');
 await wait(()=>host().race.projectiles.some(p=>p.item===5));
 const beam=host().race.projectiles.find(p=>p.item===5);
 check('Unhoming laser misses off-axis opponent and stops at track wall',host().race.laserHits===lasers&&beam.distance>0&&beam.distance<20);
 await command('host','pointer',{yaw:0,pitch:0});await capture('race-laser-wall');
 await take(2,1);await use('client1');
 check('Leader live pickups cover banana, shield and ball only',[2,3,4].every(i=>rolls.some(r=>r.leader&&r.item===i))&&rolls.filter(r=>r.leader).every(r=>[2,3,4].includes(r.item)));
 check('Trailing live pickups cover missile, laser and occasional banana only',[1,2,5].every(i=>rolls.some(r=>!r.leader&&r.item===i))&&rolls.filter(r=>!r.leader).every(r=>[1,2,5].includes(r.item)));
 await teleport(0,host().bellLanding.z,host().bellLanding.x);await command('host','action',{value:'Interact'});
 await wait(()=>host().bells===1&&guest().bells===1&&host().actors.every(a=>a.seated));
 await wait(()=>host().race.actors.every(a=>a.item===0&&a.shieldRemaining<=0&&a.effectRemaining<=0)&&host().race.projectiles.length===0&&guest().race.projectiles.length===0);
 check('Bell award preserves card rules and clears all round item effects',host().cardState===guest().cardState);
 await stand();const fallsBefore=host().actors[0].falls;
 await command('host','move',{z:1,yaw:0});await wait(()=>host().actors[0].position.z>=219,70000);await command('host','move',{z:0,yaw:0});
 check('Entire continuous course can be run with normal movement',host().actors[0].grounded&&host().actors[0].falls===fallsBefore&&host().actors[0].position.z<224);
 await command('host','pointer',{yaw:0,pitch:12});await capture('race-finish');await command('host','action',{value:'Interact'});await wait(()=>host().bells===2&&guest().bells===2);
 await command('host','room');await wait(()=>!host().playing&&!guest().playing);
 await command('host','add-bot');await startRace();await command('host','five');await command('host','action',{value:'Interact'});
 await wait(()=>host().race.actors[2].acquisitions>0&&host().actors[2].position.z>30,25000);
 const aiItem=host().race.actors[2].item;
 check('AI races along the same track and collects a leader item',host().actors[2].aiEnabled&&[2,3,4].includes(aiItem));
 const bot=host().actors[2];await teleport(0,bot.position.z-6,bot.position.x);
 await wait(()=>host().race.actors[2].uses>0&&guest().race.actors[2].uses>0);
 check('AI uses its defensive item against a nearby follower',aiItem===3?guest().race.actors[2].shieldRemaining>0:host().race.projectiles.some(p=>p.item===aiItem)||host().race.ballHits>0||host().race.bananaHits>0);
 await wait(()=>host().bells===1&&guest().bells===1&&host().actors.every(a=>a.seated),70000);
 check('AI can complete the track and ring the original bell',host().cardState===guest().cardState);
 await command('host','room');await wait(()=>!host().playing&&!guest().playing);
 await command('host','map',{value:'first-steps'});await command('client1','ready');await wait(()=>host().canStart);await command('host','start');
 await wait(()=>host().playing&&guest().playing&&host().map==='first-steps'&&guest().map==='first-steps',60000);
 // Unity JsonUtility serializes a null nested DTO as its empty default object.
 check('Switching to an existing map removes race state on both peers',[host(),guest()].every(s=>!s.race.running&&s.race.actors.length===0&&s.race.boxes.length===0&&s.race.projectiles.length===0));
 await command('host','controls');await capture('race-controls');
 check('No runtime probe errors',!host().error&&!guest().error);
}catch(error){if(checks.at(-1)?.passed!==false)checks.push({name:String(error),passed:false});fs.writeFileSync(path.join(folder,'failure.json'),JSON.stringify({error:error.stack,host:host(),guest:guest()},null,2));process.exitCode=1;}
finally{for(const p of processes)p.kill();fs.writeFileSync(path.join(project,'TestResults/race-network.json'),JSON.stringify({utc:new Date().toISOString(),folder,passed:checks.filter(c=>c.passed).length,failed:checks.filter(c=>!c.passed).length,checks},null,2));}
