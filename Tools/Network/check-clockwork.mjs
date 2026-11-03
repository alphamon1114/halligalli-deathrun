import fs from 'node:fs';
import path from 'node:path';
import {spawn} from 'node:child_process';
const project=path.resolve(process.argv[2]??'.'),folder=path.join(project,'Tools/Network/Runs','clockwork-'+Date.now());
fs.mkdirSync(folder,{recursive:true});
const processes=[],checks=[],observations=[],cached={},sequences={};
const sleep=ms=>new Promise(resolve=>setTimeout(resolve,ms));
function state(role){try{cached[role]=JSON.parse(fs.readFileSync(path.join(folder,'state-'+role+'.json'),'utf8'));}catch{}return cached[role];}
const host=()=>state('host'),guest=()=>state('client1'),peers=()=>[host(),guest()];
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
 // Lift above the entire course first: fixture repositioning must not sweep through unrelated live traps.
 const start=host().actors[seat].position;
 await command('host','teleport',{seat,position:{x:start.x,y:10,z:start.z}});
 await command('host','teleport',{seat,position:{x:position.x,y:10,z:position.z}});
 await command('host','teleport',{seat,position});
}
async function walkTo(x,z){
 const deadline=Date.now()+30000,falls=host().actors[1].falls;
 while(Date.now()<deadline){const actor=host().actors[1],dx=x-actor.position.x,dz=z-actor.position.z;if(actor.falls!==falls)throw Error('Physical traversal hit a trap');if(Math.hypot(dx,dz)<.4){await command('client1','move',{x:0,z:0});return;}await command('client1','move',{x:0,z:1,yaw:Math.atan2(dx,dz)*180/Math.PI});await sleep(35);}
 throw Error('Walking timeout');
}
try{
 launch('host');await wait('Host ready',()=>host()?.active&&!host().busy,60000);launch('client1');await wait('Guest joins',()=>guest()?.active&&!guest().busy&&host().members.length===2,60000);
 await command('host','map',{value:'clockwork'});await command('client1','ready');await wait('Ready',()=>host().canStart);await command('host','start');
 await wait('New course loaded',()=>peers().every(s=>s.playing&&s.map==='clockwork'&&s.clockwork?.traps?.length===6),60000);
 check('New course and two checkpoints load on both peers',peers().every(s=>s.checkpoints.length===2));await capture('clockwork-table-two');
 await command('host','five');await command('client1','action',{value:'Interact'});await wait('Guest stands',()=>!host().actors[1].seated);
 const routeStart=structuredClone(host().actors[1]),startTime=Date.now();await walkTo(0,5);
 for(let i=0;i<6;i++){
  const trap=host().clockwork.traps[i];await walkTo(0,trap.position.z-3);
  await wait('Safe opening '+i,()=>{const t=host().clockwork.traps[i];return t.cycle<.45;},15000);
  await walkTo(0,trap.position.z+3);
  check('Physical safe traversal '+(i+1),host().actors[1].falls===routeStart.falls);
 }
 await walkTo(0,host().bellLanding.z);
 await wait('Both checkpoints saved',()=>host().actors[1].checkpoint===1&&guest().actors[1].checkpoint===1);
 check('Physical full course completed without teleport or falls',host().actors[1].falls===routeStart.falls,{seconds:(Date.now()-startTime)/1000});
 await command('client1','action',{value:'Interact'});await wait('Bell awards and reseats peers',()=>peers().every(s=>s.bells===1&&s.actors.every(a=>a.seated)));
 check('Existing bell/card rule works',host().cardState===guest().cardState);
 await stand();
 for(const [kind,index]of [['Spears',0],['Arrows',2]])for(const seat of [0,1]){
  const trap=host().clockwork.traps[index],point=trap.hitPoints[0];
  const position={x:kind==='Spears'?point.x:0,y:.025,z:point.z};
  await wait(kind+' warning opens',()=>{const t=host().clockwork.traps[index],safe=t.period-t.warningSeconds-t.attackSeconds;return t.cycle>safe+.05&&t.cycle<safe+.25;},15000);
  const falls=host().actors[seat].falls;await teleport(seat,position);await sleep(120);
  check(kind+' warning does not hit seat '+seat,host().actors[seat].falls===falls);
  await wait(kind+' authoritative hit '+seat,()=>peers().every(s=>s.actors[seat].falls===falls+1),7000);
  await wait('Respawn '+seat,()=>peers().every(s=>!s.actors[seat].respawning));
  check(kind+' hit and respawn synchronize seat '+seat,Math.hypot(host().actors[seat].position.x-guest().actors[seat].position.x,host().actors[seat].position.z-guest().actors[seat].position.z)<.3);
 }
 for(let i=0;i<2;i++){
  await teleport(1,host().checkpoints[i].position);await wait('Checkpoint landing',()=>peers().every(s=>s.actors[1].checkpoint===i));
  const falls=host().actors[1].falls;await command('client1','action',{value:'Respawn'});await wait('Checkpoint return',()=>peers().every(s=>s.actors[1].falls===falls+1&&!s.actors[1].respawning));
  check('Checkpoint '+(i+1)+' restores guest at safe landing',Math.abs(host().actors[1].position.z-host().checkpoints[i].position.z)<.35);
 }
 const arrow=host().clockwork.traps[2];await teleport(0,{x:0,y:.025,z:arrow.position.z-3});await command('host','pointer',{yaw:0,pitch:6});
 const safeFalls=host().actors[0].falls,phaseSamples=[];for(let i=0;i<28;i++){
  await sleep(220);const a=host(),b=guest();
  phaseSamples.push(a.clockwork.traps.map((t,j)=>{const d=Math.abs(t.cycle-b.clockwork.traps[j].cycle);return Math.min(d,t.period-d);}));
 }
 check('Host and guest pattern clocks stay synchronized',phaseSamples.every(row=>row.every(d=>d<.3)),{maxPhaseError:Math.max(...phaseSamples.flat())});
 check('Adjacent waiting area stays safe through complete pattern',host().actors[0].falls===safeFalls);
 await wait('Arrow attack visible',()=>{const t=host().clockwork.traps[2];return t.cycle>t.period-t.attackSeconds+.35&&t.cycle<t.period-.25;});await capture('clockwork-arrows-active');
 await teleport(0,{x:0,y:.025,z:16.5});await command('host','pointer',{yaw:0,pitch:12});
 await wait('Spear attack visible',()=>{const t=host().clockwork.traps[0];return t.cycle>t.period-t.attackSeconds+.3&&t.cycle<t.period-.3;});await capture('clockwork-spears-active');
 await command('host','room');await wait('Room restored',()=>!host().playing&&!guest().playing);
 for(let i=0;i<4;i++)await command('host','add-bot');await command('client1','ready');await wait('Six ready',()=>host().canStart&&host().members.length===6);await command('host','start');await wait('Six-player course loaded',()=>peers().every(s=>s.playing&&s.actors.length===6),60000);
 check('Six players start seated with original table rules',peers().every(s=>s.actors.every(a=>a.seated)));
 await command('host','action',{value:'Reveal'});await wait('Six-player first card',()=>host().revision>0&&guest().revision>0);await sleep(350);await capture('clockwork-table-six');
 check('No runtime probe errors',peers().every(s=>!s.error));
}catch(error){if(checks.at(-1)?.passed!==false)checks.push({name:String(error),passed:false});fs.writeFileSync(path.join(folder,'failure.json'),JSON.stringify({error:String(error),host:host(),guest:guest()},null,2));process.exitCode=1;}
finally{for(const process of processes)process.kill();fs.writeFileSync(path.join(project,'TestResults/clockwork-network.json'),JSON.stringify({utc:new Date().toISOString(),protocol:host()?.protocol,folder,passed:checks.filter(c=>c.passed).length,failed:checks.filter(c=>!c.passed).length,checks,observations},null,2));}
