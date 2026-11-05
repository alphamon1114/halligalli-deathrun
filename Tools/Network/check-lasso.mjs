import fs from 'node:fs';
import path from 'node:path';
import {spawn} from 'node:child_process';

// node Tools/Network/check-lasso.mjs <project> [player.exe] [--headless]
const project=path.resolve(process.argv[2]??'.');
const executable=path.resolve(process.argv[3]&&!process.argv[3].startsWith('--')?process.argv[3]:path.join(project,'Build/Windows/HalliGalliMovement.exe'));
const graphical=!process.argv.includes('--headless'),seed=20261105;
const folder=path.join(project,'Tools/Network/Runs','lasso-'+Date.now());
fs.mkdirSync(folder,{recursive:true});
const checks=[],processes=[],sequences={},cached={},travel=[];
const sleep=ms=>new Promise(resolve=>setTimeout(resolve,ms));
const roleFor=seat=>seat===0?'host':'client1';
function state(role){try{cached[role]=JSON.parse(fs.readFileSync(path.join(folder,'state-'+role+'.json'),'utf8'));}catch{}return cached[role];}
const peers=()=>[state('host'),state('client1')];
const host=()=>state('host');
function check(name,passed,detail=''){checks.push({name,passed,detail});console.log((passed?'PASS ':'FAIL ')+name);if(!passed)throw new Error(name);}
async function wait(predicate,timeout=15000){
 const end=Date.now()+timeout;
 while(Date.now()<end){
  for(const child of processes)if(child.exitCode!==null||child.spawnError)throw child.spawnError??new Error('Player exited with code '+child.exitCode);
  for(const report of peers())if(report?.error)throw new Error(report.error);
  if(predicate())return;
  await sleep(15);
 }
 throw new Error('Scenario state timeout');
}
async function command(role,action,extra={}){
 const file=path.join(folder,'command-'+role+'.json'),sequence=sequences[role]=(sequences[role]??0)+1;
 fs.writeFileSync(file+'.tmp',JSON.stringify({sequence,action,...extra}));
 for(let retry=0;;retry++){try{fs.renameSync(file+'.tmp',file);break;}catch(error){if(retry===99)throw error;await sleep(10);}}
 await wait(()=>state(role)?.commandSequence===sequence);
}
function launch(role){
 const args=graphical?['-screen-fullscreen','0','-screen-width','1440','-screen-height','900']:['-batchmode','-nographics'];
 const child=spawn(executable,[...args,'--net-probe',role,'--probe-dir',folder,'--net-port','17905','-logFile',path.join(folder,role+'.log')],{stdio:'ignore'});
 processes.push(child);child.on('error',error=>child.spawnError=error);
}
async function capture(role,name){
 if(!graphical)return;
 await command(role,'capture',{value:name});await wait(()=>fs.existsSync(path.join(folder,name+'.png')));
}
async function pair(caster,gap=8){
 for(const role of ['host','client1'])await command(role,'move',{yaw:0});
 await wait(()=>peers().every(s=>s.actors.every(a=>!a.respawning&&a.lassoCooldown===0&&a.lassoPhase==='Idle'&&a.attack==='None')));
 for(const seat of [0,1])await command('host','teleport',{seat,position:{x:0,y:.025,z:seat===caster?160:160+gap}});
 await command(roleFor(caster),'look',{yaw:0,pitch:0});
 await wait(()=>peers().every(s=>s.actors.every((a,i)=>!a.seated&&a.grounded&&Math.abs(a.position.z-(i===caster?160:160+gap))<.04&&Math.hypot(a.velocity.x,a.velocity.z)<.03)));
}
function baseline(){return peers().map(s=>structuredClone(s.actors));}
function delta(peer,before,caster,key){return peer.actors[caster][key]-before[caster][key];}
async function settle(){await wait(()=>peers().every(s=>s.actors.every(a=>Math.hypot(a.velocity.x,a.velocity.z)<.03))&&Math.abs(peers()[0].actors[0].position.z-peers()[1].actors[0].position.z)<.05&&Math.abs(peers()[0].actors[1].position.z-peers()[1].actors[1].position.z)<.05);}
async function success(caster,{turn=false,spam=false,countersteer=false,gap=8}={}){
 await pair(caster,gap);const target=1-caster,before=baseline(),role=roleFor(caster),victimRole=roleFor(target);
 await command(role,'lasso',{yaw:0,pitch:0});
 if(turn)await command(role,'look',{yaw:125,pitch:-70,x:120,z:80});
 if(spam){await command(role,'lasso',{yaw:90,pitch:-75});await command(role,'lasso',{yaw:180,pitch:0});}
 let peak=0,locked=false,inward=false;
 const until=Date.now()+1000;
 let steered=false;
 while(Date.now()<until){
  const states=peers();
  for(let i=0;i<states.length;i++){
   const actor=states[i].actors[target];peak=Math.max(peak,actor.position.y-before[i][target].position.y);
   locked||=actor.stun>0||actor.stagger>0||actor.raceLaunch>0;
   inward||=actor.velocity.z<-.1;
  }
  if(countersteer&&!steered&&states[0].actors[caster].lassoPulls>before[0][caster].lassoPulls){
   await command(victimRole,'move',{z:1,yaw:0});steered=true;
  }
  await sleep(15);
 }
 if(countersteer){
  check(role+' victim can countersteer after the automatic pull',steered&&peers().every(s=>s.actors[target].velocity.z>0&&!s.actors[target].stun&&!s.actors[target].stagger));
  await command(victimRole,'move',{yaw:0});
 }
 await settle();const states=peers();
 const distance=before[0][target].position.z-states[0].actors[target].position.z;
 const remoteDistance=before[1][target].position.z-states[1].actors[target].position.z;
 const name=role+' '+(countersteer?'countersteering':gap<2?'near target':turn?'camera turn':spam?'repeated Q':'single cast');
 check(name+' counts one cast, real hit and automatic pull on both peers',states.every((s,i)=>['lassoCasts','lassoHits','lassoPulls'].every(key=>delta(s,before[i],caster,key)===1)&&s.actors[caster].lassoPhase==='Idle'));
 check(name+' uses horizontal movement without lift or control lock',inward&&peak<.08&&!locked&&(countersteer||distance>.05),JSON.stringify({distance,peak,locked}));
 check(name+' agrees on physical travel without a client impulse replay',Math.abs(distance-remoteDistance)<.06,JSON.stringify({distance,remoteDistance}));
 check(name+' plays each existing rope sound once per peer',states.every((s,i)=>['ropeThrowSounds','ropeLatchSounds','ropePullSounds'].every(key=>delta(s,before[i],caster,key)===1)));
 if(gap>=8)check(name+' does not forcibly deliver victim to caster feet',Math.abs(states[0].actors[target].position.z-states[0].actors[caster].position.z)>1);
 travel.push({caster,turn,spam,countersteer,gap,distance,remoteDistance,peak});
 return distance;
}
async function miss(caster,gap,pitch,name){
 await pair(caster,gap);const before=baseline();await command(roleFor(caster),'lasso',{yaw:0,pitch});await sleep(800);
 check(name+' consumes one cast but never hits, pulls or plays success sounds',peers().every((s,i)=>delta(s,before[i],caster,'lassoCasts')===1&&delta(s,before[i],caster,'ropeThrowSounds')===1&&['lassoHits','lassoPulls','ropeLatchSounds','ropePullSounds'].every(key=>delta(s,before[i],caster,key)===0)&&s.actors[caster].lassoCooldown>0&&s.actors[caster].lassoPhase==='Idle'));
 check(name+' leaves victim position unchanged on both peers',peers().every((s,i)=>Math.abs(s.actors[1-caster].position.z-before[i][1-caster].position.z)<.04));
 await command(roleFor(caster),'lasso',{yaw:0,pitch});await sleep(150);
 check(name+' rejects cooldown retry on both peers',peers().every((s,i)=>delta(s,before[i],caster,'lassoCasts')===1&&delta(s,before[i],caster,'lassoPulls')===0));
}
try{
 launch('host');await wait(()=>host()?.active&&!host().busy,60000);launch('client1');await wait(()=>peers().every(s=>s?.members.length===2&&!s.busy),60000);
 await command('host','map',{value:'item-race'});await command('client1','ready');await wait(()=>host().canStart);
 await command('host','start');await wait(()=>peers().every(s=>s.playing&&s.map==='item-race'&&s.actors.length===2),60000);
 await command('host','random-seed',{value:String(seed)});
 const seated=baseline();for(const role of ['host','client1'])await command(role,'lasso',{yaw:0,pitch:0});await sleep(200);
 check('Seated host and guest cannot cast or spend cooldown',peers().every((s,i)=>s.actors.every((a,seat)=>a.seated&&a.lassoCasts===seated[i][seat].lassoCasts&&a.lassoCooldown===0)));
 await command('host','five');
 await wait(()=>JSON.parse(host().cardState).reveals>0&&host().cardState===state('client1').cardState);
 await command('host','action',{value:'Interact'});await command('client1','action',{value:'Interact'});
 await wait(()=>peers().every(s=>JSON.parse(s.cardState).five&&s.actors.every(a=>!a.seated&&!a.respawning)));
 for(const caster of [0,1]){
  const unopposed=await success(caster,{turn:true});
  const opposed=await success(caster,{countersteer:true});
  check(roleFor(caster)+' active countersteering reduces real inward travel',opposed<unopposed-.05,JSON.stringify({unopposed,opposed}));
  await success(caster,{spam:true});
 }
 await success(0,{gap:1.2});
 await miss(0,8,-80,'Upward host miss');
 await miss(1,14,0,'Out-of-range guest miss');
 await pair(0);let before=baseline();await command('host','lasso',{yaw:0,pitch:0});
 // Cancel while the projectile is travelling rather than trying to catch a .12s report window.
 await command('client1','action',{value:'Respawn'});
 await wait(()=>peers().every((s,i)=>s.actors[1].respawns>before[i][1].respawns&&!s.actors[1].respawning));
 check('Victim respawn prevents an in-flight rope from applying stale force',peers().every((s,i)=>delta(s,before[i],0,'lassoPulls')===0&&s.actors[0].lassoPhase==='Idle'&&s.actors[0].lassoCooldown>0));
 await pair(1);before=baseline();await command('client1','lasso',{yaw:0,pitch:0});await command('client1','action',{value:'Respawn'});
 await wait(()=>peers().every((s,i)=>s.actors[1].respawns>before[i][1].respawns&&!s.actors[1].respawning));
 check('Caster respawn cancels its rope without refunding cooldown',peers().every((s,i)=>delta(s,before[i],1,'lassoPulls')===0&&s.actors[1].lassoPhase==='Idle'&&s.actors[1].lassoCooldown>0));
 await pair(0);before=baseline();await command('host','lasso',{yaw:0,pitch:0});
 await wait(()=>peers().every((s,i)=>delta(s,before[i],0,'lassoPulls')===1));
 const start=structuredClone(host().actors[1].position);
 await command('client1','move',{x:1,jump:true,yaw:0});
 await wait(()=>peers().every(s=>s.actors[1].position.y>start.y+.2&&s.actors[1].position.x>start.x+.05));
 check('Pulled guest can immediately jump and steer on both peers',peers().every(s=>s.actors[1].stun===0&&s.actors[1].stagger===0&&s.actors[1].raceLaunch===0));
 await command('client1','move',{yaw:0});await wait(()=>peers().every(s=>s.actors[1].grounded));
 await capture('host','lasso-host-complete');await capture('client1','lasso-guest-complete');
 await command('host','room');await wait(()=>peers().every(s=>!s.playing));
 check('Returning to room clears live ropes on both peers',peers().every(s=>s.actors.every(a=>a.lassoPhase==='Idle')));
}catch(error){
 if(checks.at(-1)?.passed!==false)checks.push({name:String(error),passed:false});
 fs.writeFileSync(path.join(folder,'failure.json'),JSON.stringify({error:error.stack,host:host(),guest:state('client1')},null,2));process.exitCode=1;
}finally{
 for(const child of processes)child.kill();
 fs.mkdirSync(path.join(project,'TestResults'),{recursive:true});
 fs.writeFileSync(path.join(project,'TestResults/lasso-network.json'),JSON.stringify({utc:new Date().toISOString(),seed,folder,executable,graphical,passed:checks.filter(c=>c.passed).length,failed:checks.filter(c=>!c.passed).length,travel,checks},null,2));
}
