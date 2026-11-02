import fs from 'node:fs';
import path from 'node:path';
import {spawn} from 'node:child_process';

const project=path.resolve(process.argv[2]??'.');
const folder=path.join(project,'Tools/Network/Runs','push-'+Date.now());
fs.mkdirSync(folder,{recursive:true});
const checks=[],processes=[],sequences={},cached={},travel=[];
const sleep=ms=>new Promise(resolve=>setTimeout(resolve,ms));
function state(role){try{cached[role]=JSON.parse(fs.readFileSync(path.join(folder,'state-'+role+'.json'),'utf8'));}catch{}return cached[role];}
const host=()=>state('host'),guest=()=>state('client1');
const roleFor=seat=>seat===0?'host':'client1';
function check(name,passed,detail=''){checks.push({name,passed,detail});if(!passed)throw new Error(name);}
async function wait(predicate,timeout=15000){const end=Date.now()+timeout;while(Date.now()<end){if(predicate())return;await sleep(15);}throw new Error('Scenario state timeout');}
async function command(role,action,extra={}){
 const file=path.join(folder,'command-'+role+'.json');const sequence=sequences[role]=(sequences[role]??0)+1;
 fs.writeFileSync(file+'.tmp',JSON.stringify({sequence,action,...extra}));
 for(let i=0;;i++){try{fs.renameSync(file+'.tmp',file);break;}catch(error){if(i===99)throw error;await sleep(10);}}
 await wait(()=>state(role)?.commandSequence===sequence);
 if(state(role).error)throw new Error(state(role).error);
}
function launch(role){
 const args=role==='host'?['-screen-fullscreen','0','-screen-width','1440','-screen-height','900']:['-batchmode','-nographics'];
 const child=spawn(path.join(project,'Build/Windows/HalliGalliMovement.exe'),[...args,'--net-probe',role,'--probe-dir',folder,'--net-port','17894','-logFile',path.join(folder,role+'.log')],{stdio:'ignore'});
 processes.push(child);child.on('error',error=>console.error(error));
}
async function pair(attacker,gap=1.1,yaw=0){
 await command('host','move',{yaw:0});await command('client1','move',{yaw:0});
 await wait(()=>[host(),guest()].every(s=>s.actors.every(a=>a.attack==='None'&&a.pushProtection===0&&a.kickCooldown===0)));
 for(const seat of [0,1])await command('host','teleport',{seat,position:{x:0,y:.025,z:seat===attacker?160:160+gap}});
 await command(roleFor(attacker),'pointer',{yaw,pitch:0});
 await wait(()=>[host(),guest()].every(s=>s.actors.every((a,i)=>a.grounded&&Math.abs(a.position.z-(i===attacker?160:160+gap))<.04&&Math.hypot(a.velocity.x,a.velocity.z)<.03)));
}
async function push(attacker,kick){await command(roleFor(attacker),'pointer',{yaw:0,pitch:0,primary:!kick,kick});}
async function displacement(attacker,kick){
 await pair(attacker);const target=1-attacker,role=roleFor(attacker),before=structuredClone(host().actors),remoteBefore=structuredClone(guest().actors);
 await push(attacker,kick);
 let peak=0,locked=false;const until=Date.now()+1450;
 while(Date.now()<until){
  for(const s of [host(),guest()]){const a=s.actors[target];peak=Math.max(peak,a.position.y-before[target].position.y);locked||=a.stun>0||a.stagger>0||a.raceLaunch>0;}
  await sleep(15);
 }
 await wait(()=>host().actors[attacker].attack==='None'&&guest().actors[attacker].attack==='None'&&Math.abs(host().actors[target].position.z-guest().actors[target].position.z)<.04);
 const h=host().actors,g=guest().actors,distance=h[target].position.z-before[target].position.z,remoteDistance=g[target].position.z-remoteBefore[target].position.z;
 const name=role+' '+(kick?'kick':'quick push');
 check(name+' counts exactly one authoritative hit on both peers',h[target].hits===before[target].hits+1&&g[target].hits===remoteBefore[target].hits+1&&h[attacker].attackHits===before[attacker].attackHits+1&&g[attacker].attackHits===remoteBefore[attacker].attackHits+1);
 check(name+' produces real horizontal displacement without launch or control lock',distance>(kick?.3:.05)&&peak<.08&&!locked,JSON.stringify({distance,peak}));
 check(name+' has equivalent host and client travel without double impulse',Math.abs(distance-remoteDistance)<.06,JSON.stringify({distance,remoteDistance}));
 travel.push({attacker,kick,distance,remoteDistance,peak});return distance;
}
try{
 launch('host');await wait(()=>host()?.active&&!host().busy,60000);launch('client1');await wait(()=>host().members.length===2&&guest()?.members.length===2&&!guest().busy);
 await command('host','map',{value:'item-race'});await command('client1','ready');await wait(()=>host().canStart);
 await command('host','start');await wait(()=>host().playing&&guest().playing&&host().map==='item-race'&&guest().map==='item-race',60000);
 const seated=host().actors.map(a=>a.attacks);
 await push(0,false);await push(1,true);await sleep(200);
 check('Seated host and guest cannot start a push or kick',host().actors.every((a,i)=>a.seated&&a.attacks===seated[i]));
 await command('host','five');await command('host','action',{value:'Interact'});await command('client1','action',{value:'Interact'});
 await wait(()=>[host(),guest()].every(s=>s.actors.every(a=>!a.seated)));
 for(const attacker of [0,1]){
  const quick=await displacement(attacker,false),kick=await displacement(attacker,true);
  check(roleFor(attacker)+' kick travels meaningfully farther than quick push',kick>quick*2,JSON.stringify({quick,kick}));
 }
 for(const attacker of [0,1]){
  await pair(attacker);const target=1-attacker,before=structuredClone(host().actors),remoteBefore=structuredClone(guest().actors);
  await push(attacker,false);await wait(()=>host().actors[target].hits===before[target].hits+1);
  // One deliberate recovery input queues the second quick push. Its contact falls
  // .55 seconds after the first, inside the receiver's .65-second protection.
  await wait(()=>{const a=host().actors[attacker];return a.attack!=='None'&&a.attackElapsed>=.4&&a.attackElapsed<.54;},1500);
  await command(roleFor(attacker),'action',{value:'Push'});
  await wait(()=>host().actors[attacker].attacks===before[attacker].attacks+2&&host().actors[attacker].attack==='None');await sleep(150);
  const h=host().actors,g=guest().actors;
  check(roleFor(attacker)+' protected second contact does not count or replicate a hit',h[target].hits===before[target].hits+1&&g[target].hits===remoteBefore[target].hits+1&&h[attacker].attackHits===before[attacker].attackHits+1&&g[attacker].attackHits===remoteBefore[attacker].attackHits+1);
  const reference=travel.find(t=>t.attacker===attacker&&!t.kick).distance,distance=h[target].position.z-before[target].position.z;
  check(roleFor(attacker)+' protected second contact adds no physical displacement',Math.abs(distance-reference)<.06,JSON.stringify({reference,distance}));
  await push(attacker,false);await wait(()=>host().actors[target].hits===before[target].hits+2&&guest().actors[target].hits===remoteBefore[target].hits+2);
  check(roleFor(attacker)+' can push again after original protection expires',host().actors[attacker].attackHits===before[attacker].attackHits+2);
 }
 for(const gap of [3,-1.1]){
  await pair(0,gap);const hits=host().actors[1].hits,credit=host().actors[0].attackHits;
  await push(0,true);await sleep(1300);
  check(gap>0?'Kick cannot hit beyond range':'Kick cannot hit behind attacker',host().actors[1].hits===hits&&host().actors[0].attackHits===credit);
 }
 await pair(0);let hits=host().actors[1].hits,attacks=host().actors[1].attacks;
 await push(0,true);await wait(()=>host().actors[1].hits===hits+1&&guest().actors[1].hits===hits+1);
 await command('client1','pointer',{yaw:180,pitch:12,primary:true});
 await wait(()=>host().actors[1].attacks===attacks+1);
 check('Pushed predicting guest can counterattack during protection',host().actors[1].pushProtection>0);
 const from=host().actors[1].position;
 await command('client1','move',{x:1,jump:true,yaw:180,pitch:12});
 await wait(()=>host().actors[1].position.y>from.y+.25&&guest().actors[1].position.y>from.y+.25);
 check('Pushed guest immediately jumps and steers instead of being immobilized',Math.abs(host().actors[1].velocity.x)>.1&&host().actors[1].stun===0&&host().actors[1].stagger===0&&host().actors[1].raceLaunch===0);
 check('Pushed guest retains mouse aim without camera roll',Math.abs(guest().actors[1].viewYaw-180)<.1&&Math.abs(guest().actors[1].viewRoll)<.1);
 await command('client1','move',{crouch:true,yaw:180,pitch:12});await wait(()=>host().actors[1].crouched&&guest().actors[1].crouched);
 check('Pushed guest retains crouch input',host().actors[1].crouched);
 await command('client1','move',{yaw:180});
 await pair(0);const life=host().actors[1].respawns;
 await command('client1','action',{value:'Respawn'});await wait(()=>host().actors[1].respawning);
 attacks=host().actors[1].attacks;await push(1,true);await sleep(150);
 check('Respawning guest cannot start a kick',host().actors[1].attacks===attacks);
 await wait(()=>host().actors[1].respawns>life&&!host().actors[1].respawning);
 await command('host','capture',{value:'push-complete'});await wait(()=>fs.existsSync(path.join(folder,'push-complete.png')));
}catch(error){if(checks.at(-1)?.passed!==false)checks.push({name:String(error),passed:false});fs.writeFileSync(path.join(folder,'failure.json'),JSON.stringify({error:error.stack,host:host(),guest:guest()},null,2));process.exitCode=1;}
finally{
 for(const child of processes)child.kill();
 fs.mkdirSync(path.join(project,'TestResults'),{recursive:true});
 fs.writeFileSync(path.join(project,'TestResults/push-network.json'),JSON.stringify({utc:new Date().toISOString(),folder,passed:checks.filter(c=>c.passed).length,failed:checks.filter(c=>!c.passed).length,travel,checks},null,2));
}
