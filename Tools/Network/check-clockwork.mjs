import fs from 'node:fs';
import path from 'node:path';
import {spawn} from 'node:child_process';
const project=path.resolve(process.argv[2]??'.'),folder=path.join(project,'Tools/Network/Runs','clockwork-'+Date.now());
fs.mkdirSync(folder,{recursive:true});
const processes=[],checks=[],observations=[],cached={},sequences={};
const sleep=ms=>new Promise(resolve=>setTimeout(resolve,ms));
function state(role){try{cached[role]=JSON.parse(fs.readFileSync(path.join(folder,'state-'+role+'.json'),'utf8'));}catch{}return cached[role];}
const host=()=>state('host'),guest=()=>state('client1'),peers=()=>[host(),guest()];
const distance=(a,b)=>Math.hypot(a.x-b.x,a.y-b.y,a.z-b.z);
const offset=(point,forward,n)=>({x:point.x+forward.x*n,y:.025,z:point.z+forward.z*n});
const yaw=forward=>Math.atan2(forward.x,forward.z)*180/Math.PI;
function check(name,passed,detail){checks.push({name,passed,detail});if(!passed)throw Error(name);}
async function wait(name,predicate,timeout=20000){const deadline=Date.now()+timeout;while(Date.now()<deadline){if(predicate())return;await sleep(30);}throw Error('Timeout: '+name);}
async function command(role,action,extra={}){
 const sequence=sequences[role]=(sequences[role]??0)+1,file=path.join(folder,'command-'+role+'.json');
 fs.writeFileSync(file+'.tmp',JSON.stringify({sequence,action,...extra}));for(let n=0;;n++){try{fs.renameSync(file+'.tmp',file);break;}catch(error){if(n===99)throw error;await sleep(10);}}
 await wait(role+' '+action,()=>state(role)?.commandSequence===sequence);if(state(role).error)throw Error(state(role).error);
}
function launch(role){processes.push(spawn(path.join(project,'Build/Windows/HalliGalliMovement.exe'),[...(role==='host'?['-screen-fullscreen','0','-screen-width','1440','-screen-height','900']:['-batchmode','-nographics']),'--net-probe',role,'--net-port','17916','--probe-dir',folder,'-logFile',path.join(folder,role+'.log')],{stdio:'ignore'}));}
async function capture(name){await command('host','capture',{value:name});await wait('Capture '+name,()=>fs.existsSync(path.join(folder,name+'.png')));observations.push({name,host:structuredClone(host()),guest:structuredClone(guest())});}
async function stand(){await command('host','five');for(const role of ['host','client1'])await command(role,'action',{value:'Interact'});await wait('Racers stand',()=>peers().every(s=>s.actors.slice(0,2).every(a=>!a.seated)));}
async function teleport(seat,position){
 // Fixture repositioning travels above the course, not through unrelated live hazards.
 const start=host().actors[seat].position;
 await command('host','teleport',{seat,position:{x:start.x,y:10,z:start.z}});
 await command('host','teleport',{seat,position:{x:position.x,y:10,z:position.z}});
 await command('host','teleport',{seat,position});
}
async function startRoom(){await command('client1','ready');await wait('Room ready',()=>host().canStart);await command('host','start');await wait('Course loaded',()=>peers().every(s=>s.playing&&s.map==='clockwork'),60000);}
try{
 launch('host');await wait('Host ready',()=>host()?.active&&!host().busy,60000);launch('client1');await wait('Guest joins',()=>guest()?.active&&!guest().busy&&host().members.length===2,60000);
 await command('host','map',{value:'clockwork'});await startRoom();
 check('Mixed course loads with three safe checkpoints',peers().every(s=>s.checkpoints.length===3&&s.clockwork?.traps.length===6));await capture('clockwork-table-two');
 for(const role of ['host','client1'])await command(role,'action',{value:'Interact'});
 await wait('Wrong-card departures',()=>host().actors.every(a=>!a.seated));
 check('Both wrong-card departures require returning',JSON.parse(host().cardState).seats.every(s=>s.mustSit));
 const kinds=['Spears','Arrows'];
 for(const kind of kinds)for(const seat of [0,1]){
  const trap=host().clockwork.traps.find(t=>t.kind===kind),falls=host().actors[seat].falls;
  await teleport(seat,{...trap.position,y:.025});
  await wait(kind+' wrong-card hit '+seat,()=>peers().every(s=>s.actors[seat].falls===falls+1),8000);
  await wait(kind+' wrong-card respawn '+seat,()=>peers().every(s=>!s.actors[seat].respawning));
  check(kind+' wrong-card departure respawns seat '+seat,host().actors[seat].position.z<6&&distance(host().actors[seat].position,guest().actors[seat].position)<.3);
 }
 for(const seat of [0,1]){
  const press=host().clockwork.presses[0],falls=host().actors[seat].falls;
  await teleport(seat,{...press.position,y:.025});
  await wait('Wrong-card press hit '+seat,()=>peers().every(s=>s.actors[seat].falls===falls+1),8000);
  await wait('Wrong-card press respawn '+seat,()=>peers().every(s=>!s.actors[seat].respawning));
  check('Press wrong-card departure respawns seat '+seat,host().actors[seat].position.z<6&&distance(host().actors[seat].position,guest().actors[seat].position)<.3);
 }
 for(const role of ['host','client1'])await command(role,'action',{value:'Interact'});
 await wait('Wrong-card departures return to seats',()=>peers().every(s=>s.actors.every(a=>a.seated)));
 await stand();
 for(let index=0;index<6;index++){
  const seat=index%2,trap=host().clockwork.traps[index];
  await wait('Warning opens '+index,()=>{const t=host().clockwork.traps[index],safe=t.period-t.warningSeconds-t.attackSeconds;return t.cycle>safe+.05&&t.cycle<safe+.25;},15000);
  const falls=host().actors[seat].falls;await teleport(seat,{...trap.position,y:.025});await sleep(120);
  check('Trap '+index+' warning remains harmless',host().actors[seat].falls===falls);
  await wait('Oriented trap hit '+index,()=>peers().every(s=>s.actors[seat].falls===falls+1),7000);
  await wait('Trap respawn '+index,()=>peers().every(s=>!s.actors[seat].respawning));
  check('Oriented trap '+index+' hit and respawn synchronize',distance(host().actors[seat].position,guest().actors[seat].position)<.3);
 }
 for(let i=0;i<3;i++){
  await teleport(1,host().checkpoints[i].position);await wait('Checkpoint landing',()=>peers().every(s=>s.actors[1].checkpoint===i));
  const falls=host().actors[1].falls;await command('client1','action',{value:'Respawn'});await wait('Checkpoint return',()=>peers().every(s=>s.actors[1].falls===falls+1&&!s.actors[1].respawning));
  check('Corner checkpoint '+(i+1)+' restores guest safely',distance(host().actors[1].position,host().checkpoints[i].position)<.35);
 }
 // Repositioning sets up only this carry fixture; subsequent motion must come from the platform.
 const carrier=host().clockwork.ferries[0];
 await teleport(1,{x:carrier.position.x,y:carrier.position.y+carrier.standingOffset,z:carrier.position.z});
 await wait('Guest lands on carrier',()=>host().actors[1].grounded);
 const carryStart=structuredClone(host().actors[1]),baseCarrier=host().clockwork.ferries[0].position,carryOffsets=[];
 const relative={x:carryStart.position.x-baseCarrier.x,y:carryStart.position.y-baseCarrier.y,z:carryStart.position.z-baseCarrier.z};
 for(let n=0;n<12;n++){await sleep(180);const s=host(),p=s.clockwork.ferries[0].position;carryOffsets.push(distance(s.actors[1].position,{x:p.x+relative.x,y:p.y+relative.y,z:p.z+relative.z}));}
 check('Moving platform carries remote player without input',host().actors[1].falls===carryStart.falls&&distance(host().actors[1].position,carryStart.position)>1&&carryOffsets.every(d=>d<.7),{offsets:carryOffsets});
 check('Carried guest position replicates',distance(host().actors[1].position,guest().actors[1].position)<1.4);
 const arrowIndex=host().clockwork.traps.findIndex(t=>t.kind==='Arrows'),arrow=host().clockwork.traps[arrowIndex];
 await teleport(0,offset(arrow.position,arrow.forward,-3));await command('host','pointer',{yaw:yaw(arrow.forward),pitch:6});
 const safeFalls=host().actors[0].falls,phaseSamples=[];
 for(let i=0;i<28;i++){await sleep(220);const a=host(),b=guest();phaseSamples.push(a.clockwork.traps.map((t,j)=>{const d=Math.abs(t.cycle-b.clockwork.traps[j].cycle);return Math.min(d,t.period-d);}));}
 check('Host and guest trap clocks synchronize',phaseSamples.every(row=>row.every(d=>d<.3)),{maxPhaseError:Math.max(...phaseSamples.flat())});
 check('Trap waiting pad stays safe through whole cycle',host().actors[0].falls===safeFalls);
 await wait('Arrow flight visible',()=>{const t=host().clockwork.traps[arrowIndex],side={x:t.forward.z,z:-t.forward.x},x=(t.visual.x-t.position.x)*side.x+(t.visual.z-t.position.z)*side.z;return t.stage==='Attack'&&x>-5&&x<0;},30000);await capture('clockwork-arrows-active');
 const press=host().clockwork.presses[0];await teleport(0,offset(press.position,press.forward,-3));await command('host','pointer',{yaw:yaw(press.forward),pitch:8});await capture('clockwork-presses');
 await teleport(0,host().clockwork.ferries[0].waiting);await command('host','pointer',{yaw:0,pitch:10});await capture('clockwork-ferry-wait');
 await command('host','room');await wait('Room restored',()=>!host().playing&&!guest().playing);await command('host','add-bot');await startRoom();
 await command('host','five');await wait('Authoritative AI departs',()=>!host().actors[2].seated);
 const runStart=Date.now(),initialFalls=host().actors[2].falls,trace=[];let checkpointMask=0,rode=false,jumped=false;
 await wait('Physical mixed route completion',()=>{
  const a=host(),b=guest(),p=a.actors[2],f=a.clockwork.ferries[0],c=f.position;
  if(p.checkpoint>=0)checkpointMask|=1<<p.checkpoint;
  if(p.grounded&&Math.abs(p.position.x-c.x)<2&&Math.abs(p.position.z-c.z)<1.8&&p.position.z>54&&p.position.z<62)rode=true;
  if(!p.grounded&&p.position.y>.2)jumped=true;
  if(trace.at(-1)?.time!==a.worldTime)trace.push({time:a.worldTime,position:p.position,falls:p.falls,checkpoint:p.checkpoint});
  return a.bells===1&&b.bells===1&&a.actors.every(actor=>actor.seated)&&b.actors.every(actor=>actor.seated);
 },150000);
 observations.push({name:'Physical mixed-route authoritative AI run',trace});
 check('Mixed route completes physically without teleport or falls',host().actors[2].falls===initialFalls,{seconds:(Date.now()-runStart)/1000});
 check('Physical route crosses all three corner checkpoints',checkpointMask===7,{checkpointMask});
 check('Physical route jumps and rides mandatory carrier',jumped&&rode,{jumped,rode});
 check('Bell/card result replicates after mixed route',host().cardState===guest().cardState);
 await command('host','room');await wait('Room restored for six',()=>!host().playing&&!guest().playing);
 for(let i=0;i<3;i++)await command('host','add-bot');await startRoom();
 check('Six players start seated',peers().every(s=>s.actors.length===6&&s.actors.every(a=>a.seated)));
 await sleep(500);await command('host','action',{value:'Reveal'});await wait('Six-player first card',()=>host().revision>0&&guest().revision>0);await sleep(350);await capture('clockwork-table-six');
 check('No runtime probe errors',peers().every(s=>!s.error));
}catch(error){if(checks.at(-1)?.passed!==false)checks.push({name:String(error),passed:false});fs.writeFileSync(path.join(folder,'failure.json'),JSON.stringify({error:String(error),host:host(),guest:guest()},null,2));process.exitCode=1;}
finally{for(const process of processes)process.kill();fs.writeFileSync(path.join(project,'TestResults/clockwork-network.json'),JSON.stringify({utc:new Date().toISOString(),protocol:host()?.protocol,folder,passed:checks.filter(c=>c.passed).length,failed:checks.filter(c=>!c.passed).length,checks,observations},null,2));}
