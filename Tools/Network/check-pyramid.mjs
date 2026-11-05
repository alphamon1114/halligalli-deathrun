import fs from 'node:fs';
import path from 'node:path';
const folder=path.resolve(process.argv[2]),checks=[],sequences={};
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
function state(role){try{return JSON.parse(fs.readFileSync(path.join(folder,`state-${role}.json`),'utf8'));}catch{return null;}}
const host=()=>state('host'),client=()=>state('client1');
function command(role,action,extra={}){
  if(!sequences[role]){try{sequences[role]=JSON.parse(fs.readFileSync(path.join(folder,`command-${role}.json`),'utf8')).sequence;}catch{sequences[role]=0;}}
  const file=path.join(folder,`command-${role}.json`);fs.writeFileSync(file+'.tmp',JSON.stringify({sequence:++sequences[role],action,...extra}));
  for(let retry=0;;retry++){try{fs.renameSync(file+'.tmp',file);break;}catch(e){if(retry===99)throw e;Atomics.wait(new Int32Array(new SharedArrayBuffer(4)),0,0,10);}}
}
function check(name,passed,detail=''){checks.push({name,passed,detail});console.log((passed?'PASS ':'FAIL ')+name);if(!passed)throw new Error(name);}
async function wait(name,predicate,timeout=15000){
  const end=Date.now()+timeout;
  while(Date.now()<end){let passed=false;try{passed=predicate();}catch{}if(passed){check(name,true);return;}await sleep(30);}
  check(name,false);
}
const cards=()=>JSON.parse(host().cardState);
try{
  await wait('Two peers joined',()=>host()?.members.length===2&&client()?.members.length===2&&!client().status.includes('접속 중'));
  await sleep(400);command('host','map',{value:'pyramid'});await sleep(150);command('client1','ready');
  await wait('Pyramid room ready',()=>host().canStart);
  command('host','start');
  await wait('Pyramid loaded on both peers',()=>host().playing&&client().playing&&host().map==='pyramid'&&client().map==='pyramid'&&client().actors.length===2,40000);
  await sleep(400);command('host','five');await wait('Legal five synchronized',()=>cards().reveals>0&&host().cardState===client().cardState);
  command('host','action',{value:'Interact'});await sleep(150);command('client1','action',{value:'Interact'});
  await wait('Both racers standing',()=>cards().five&&host().actors.every(p=>!p.seated)&&client().actors.every(p=>!p.seated));
  const cardState=host().cardState;let falls=host().actors[1].falls;
  command('host','teleport',{seat:1,position:host().bellLanding});await sleep(300);command('client1','action',{value:'Interact'});await sleep(300);
  check('Unfinished racer cannot ring the royal bell',host().bells===0&&client().bells===0);
  for(let i=0;i<4;i++){
    const cp=host().checkpoints[i];
    command('host','teleport',{seat:1,position:{...cp.position,y:cp.position.y+3}});
    await wait(`Airborne checkpoint ${i+1} saves on both peers`,()=>host().actors[1].checkpoint===i&&client().actors[1].checkpoint===i&&!host().actors[1].grounded&&!client().actors[1].grounded);
    command('client1','action',{value:'Respawn'});falls++;
    await wait(`R restores checkpoint ${i+1} and earned gates`,()=>!client().actors[1].respawning&&client().actors[1].falls===falls&&host().actors[1].falls===falls&&client().actors[1].gates===cp.gates&&client().actors[1].checkpoint===i);
  }
  const layout=JSON.parse(fs.readFileSync(path.resolve(process.argv[3],'TestResults/pyramid-layout.json'),'utf8'));
  const room=layout.rooms[3];
  const yaw=room.heading*Math.PI/180,px=room.localMinimum.x+6,pz=(room.localMinimum.z+room.localMaximum.z)/2;
  command('host','teleport',{seat:1,position:{x:room.origin.x+Math.cos(yaw)*px+Math.sin(yaw)*pz,y:room.origin.y+room.localMinimum.y+2,z:room.origin.z-Math.sin(yaw)*px+Math.cos(yaw)*pz}});falls++;
  await wait('Cursed pit contact recovers guest on both peers',()=>host().actors[1].falls===falls&&client().actors[1].falls===falls,3500);
  await wait('Pit restores fourth checkpoint without changing cards',()=>!client().actors[1].respawning&&client().actors[1].checkpoint===3&&client().actors[1].gates===host().checkpoints[3].gates&&host().cardState===cardState);
  command('host','teleport',{seat:1,position:host().checkpoints[0].position});await sleep(250);
  check('Backtracking cannot replace the fourth checkpoint',host().actors[1].checkpoint===3&&client().actors[1].checkpoint===3);
  check('Final bank contacts are not fabricated',host().actors[1].gates===host().checkpoints[3].gates&&client().actors[1].gates===host().actors[1].gates&&host().actors[1].gates<host().surfSections);
  command('host','teleport',{seat:1,position:host().bellLanding});
  await sleep(250);command('client1','action',{value:'Interact'});
  await wait('Completed pyramid awards bell and reseats everyone',()=>host().bells===1&&client().bells===1&&host().actors.every(p=>p.seated)&&client().actors.every(p=>p.seated));
  await wait('Awarded cards remain synchronized',()=>host().cardState===client().cardState);
  command('host','room');await wait('Both peers return to room',()=>!host().playing&&!client().playing&&host().members.length===2);
  command('host','map',{value:'surf'});await sleep(150);command('client1','ready');
  await wait('Room ready for a second match',()=>host().canStart);
  // The probe's map field describes the loaded scene; room selection loads only on StartMatch.
  command('host','start');await wait('Original surf still loads',()=>host().playing&&client().playing&&host().map==='surf'&&client().map==='surf',40000);
  check('Original surf retains single-sided ramps',host().twoSidedSurfSections===0&&client().twoSidedSurfSections===0);
  command('client1','leave');await wait('Guest leaves cleanly',()=>!client().active&&host().members.length===1);
  command('host','leave');await wait('Host closes room',()=>!host().active);
}catch(error){console.error(error);checks.push({name:String(error),passed:false});fs.writeFileSync(path.join(folder,'pyramid-failure.json'),JSON.stringify({host:host(),client:client()},null,2));process.exitCode=1;}
finally{fs.writeFileSync(path.join(folder,'pyramid-results.json'),JSON.stringify({utc:new Date().toISOString(),passed:checks.filter(c=>c.passed).length,failed:checks.filter(c=>!c.passed).length,checks},null,2));}
