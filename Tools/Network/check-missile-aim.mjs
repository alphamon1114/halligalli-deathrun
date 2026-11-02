import fs from 'node:fs';
import path from 'node:path';
import {spawn} from 'node:child_process';

const project=path.resolve(process.argv[2]??'.');
const folder=path.join(project,'Tools/Network/Runs','missile-aim-'+Date.now());
fs.mkdirSync(folder,{recursive:true});
const roles=['host','client1','client2'],processes=[],checks=[],observations=[],cached={},sequences={};
const sleep=ms=>new Promise(resolve=>setTimeout(resolve,ms));
function state(role){try{cached[role]=JSON.parse(fs.readFileSync(path.join(folder,'state-'+role+'.json'),'utf8'));}catch{}return cached[role];}
const host=()=>state('host'),peers=()=>roles.map(state);
function check(name,passed,detail){checks.push({name,passed,detail});if(!passed)throw Error(name);}
async function wait(name,predicate,timeout=20000){const end=Date.now()+timeout;while(Date.now()<end){if(predicate())return;await sleep(25);}throw Error('Timeout: '+name);}
async function command(role,action,extra={}){
 const sequence=sequences[role]=(sequences[role]??0)+1,file=path.join(folder,'command-'+role+'.json');
 fs.writeFileSync(file+'.tmp',JSON.stringify({sequence,action,...extra}));
 for(let n=0;;n++){try{fs.renameSync(file+'.tmp',file);break;}catch(error){if(n===99)throw error;await sleep(10);}}
 await wait(role+' '+action,()=>state(role)?.commandSequence===sequence);
 if(state(role).error)throw Error(state(role).error);
}
function launch(role){
 const graphics=role==='host'?['-screen-fullscreen','0','-screen-width','1440','-screen-height','900']:['-batchmode','-nographics'];
 processes.push(spawn(path.join(project,'Build/Windows/HalliGalliMovement.exe'),[...graphics,'--net-probe',role,'--net-port','17905','--probe-dir',folder,'-logFile',path.join(folder,role+'.log')],{stdio:'ignore'}));
}
async function teleport(seat,x,z){await command('host','teleport',{seat,position:{x,y:.025,z}});await wait('Actor placement',()=>peers().every(s=>s.actors[seat].grounded&&Math.abs(s.actors[seat].position.x-x)<.1&&Math.abs(s.actors[seat].position.z-z)<.1));}
async function takeMissile(seat){
 for(let attempt=0;attempt<32;attempt++){
  if(host().race.actors[seat].item!==0){await command(roles[seat],'action',{value:'Respawn'});await wait('Inventory cleared',()=>peers().every(s=>!s.actors[seat].respawning&&s.race.actors[seat].item===0));}
  await wait('Available real box',()=>host().race.boxes.some(b=>b.cooldown===0&&b.position.z>30&&b.position.z<205));
  const box=host().race.boxes.find(b=>b.cooldown===0&&b.position.z>30&&b.position.z<205),before=host().race.actors[seat].acquisitions;
  await command('host','random-seed',{value:String(125+attempt)});await teleport(seat,box.position.x,box.position.z);
  await wait('Pickup replicated',()=>peers().every(s=>s.race.actors[seat].acquisitions>before&&s.race.actors[seat].item===host().race.actors[seat].item));
  if(host().race.actors[seat].item===1)return;
 }
 throw Error('No missile acquired through live boxes');
}
async function aim(seat,yaw,target,label){
 await command(roles[seat],'pointer',{yaw,pitch:0});await wait(label,()=>state(roles[seat]).raceAimTarget===target);
 check(label,host().race.actors[seat].item===1,{yaw,target});
 observations.push({label,attacker:seat,host:structuredClone(host()),local:structuredClone(state(roles[seat]))});
 if(seat===0){await command('host','capture',{value:label});await wait('Capture '+label,()=>fs.existsSync(path.join(folder,label+'.png')));}
}
try{
 launch('host');await wait('Host ready',()=>host()?.active&&!host().busy,60000);
 launch('client1');await wait('Second player joins',()=>state('client1')?.members.length===2&&!state('client1').busy,60000);
 launch('client2');await wait('Third player joins',()=>peers().every(s=>s?.members.length===3&&!s.busy),60000);
 await command('host','map',{value:'item-race'});await command('client1','ready');await command('client2','ready');await wait('Ready',()=>host().canStart);
 await command('host','start');await wait('Playing',()=>peers().every(s=>s.playing&&s.map==='item-race'),60000);
 await command('host','five');for(const role of roles)await command(role,'action',{value:'Interact'});
 await wait('All actors racing',()=>peers().every(s=>s.actors.every(a=>!a.seated)));
 for(const seat of [0,1]){
  const others=[0,1,2].filter(i=>i!==seat),near=others[0],far=others[1],sign=seat===0?1:-1,forward=sign===1?0:180;
  const nearYaw=Math.atan2(3,6*sign)*180/Math.PI;
  await takeMissile(seat);await teleport(seat,0,160);await teleport(near,3,160+6*sign);await teleport(far,0,160+25*sign);
  await aim(seat,forward,far,roles[seat]+'-far-centered-beats-near-off-axis');
  await aim(seat,nearYaw,near,roles[seat]+'-aim-switches-to-near');
  await aim(seat,forward,far,roles[seat]+'-aim-switches-back-without-firing');
  await teleport(far,5,160+25*sign);await teleport(near,0,160+6*sign);
  await wait('Moving target updates held lock',()=>state(roles[seat]).raceAimTarget===near);
  check(roles[seat]+' moving opponent changes lock without another aim input',host().race.actors[seat].item===1);
  await teleport(far,0,160+50*sign);await teleport(near,3,160+6*sign);
  await aim(seat,forward,near,roles[seat]+'-out-of-range-center-is-excluded');
  await aim(seat,90,-1,roles[seat]+'-outside-cone-clears-lock');
  const uses=host().race.actors[seat].uses;await command(roles[seat],'race-item');await sleep(150);
  check(roles[seat]+' firing with no target retains missile',peers().every(s=>s.race.actors[seat].uses===uses&&s.race.actors[seat].item===1));
  await teleport(far,0,160+25*sign);await aim(seat,forward,far,roles[seat]+'-selected-shot-target');
  const before=host().race.actors.map(a=>a.flips);await command(roles[seat],'race-item');
  await wait('Missile target replicated',()=>peers().every(s=>s.race.projectiles.some(p=>p.item===1&&p.owner===seat&&p.target===far)));
  await command(roles[seat],'pointer',{yaw:nearYaw,pitch:0});
  check(roles[seat]+' launched missile keeps selected target after aim moves',peers().every(s=>s.race.projectiles.some(p=>p.item===1&&p.owner===seat&&p.target===far)));
  await wait('Chosen opponent hit',()=>peers().every(s=>s.race.actors[far].flips===before[far]+1));
  check(roles[seat]+' selected distant opponent is hit, closer opponent is not',peers().every(s=>s.race.actors[near].flips===before[near]));
  observations.push({label:roles[seat]+' selected hit',host:structuredClone(host()),local:structuredClone(state(roles[seat]))});
  await wait('Recovery',()=>peers().every(s=>s.actors[far].grounded&&s.race.actors[far].effectRemaining===0));
 }
 check('No runtime probe errors',peers().every(s=>!s.error));
}catch(error){if(checks.at(-1)?.passed!==false)checks.push({name:String(error),passed:false});fs.writeFileSync(path.join(folder,'failure.json'),JSON.stringify({error:String(error),states:peers()},null,2));process.exitCode=1;}
finally{for(const child of processes)child.kill();fs.writeFileSync(path.join(project,'TestResults/missile-aim.json'),JSON.stringify({utc:new Date().toISOString(),protocol:35,folder,passed:checks.filter(c=>c.passed).length,failed:checks.filter(c=>!c.passed).length,checks,observations},null,2));}
