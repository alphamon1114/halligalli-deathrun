import fs from 'node:fs';
import path from 'node:path';
import {spawn} from 'node:child_process';
import {createHash} from 'node:crypto';

const project=path.resolve(process.argv[2]??'.');
const relay=process.argv.includes('--relay');
const creatorReport=JSON.parse(fs.readFileSync(path.join(project,'TestResults/usermap-creator-clear.json'),'utf8'));
if(creatorReport.failed!==0)throw new Error('Run check-creator-clear.mjs successfully before the network scenario.');
const small=creatorReport.small,large=creatorReport.large;
const folder=path.join(project,'Tools/Network/Runs','usermaps-'+Date.now());
fs.mkdirSync(folder,{recursive:true});
const processes=[],checks=[],sequences={},snapshots={};
const sleep=ms=>new Promise(resolve=>setTimeout(resolve,ms));
let guest='client1';
function state(role){try{snapshots[role]=JSON.parse(fs.readFileSync(path.join(folder,`state-${role}.json`),'utf8'));}catch{}return snapshots[role]??null;}
const host=()=>state('host'),client=()=>state(guest);
function launch(role){
  const args=['-batchmode','-nographics','--net-probe',role,'--probe-dir',folder,'--net-port','17889','-logFile',path.join(folder,role+'.log')];
  if(relay)args.push('--relay-probe','true','--auth-profile','usermap'+role);
  const child=spawn(path.join(project,'Build/Windows/HalliGalliMovement.exe'),args,{stdio:'ignore'});
  processes.push(child);child.on('error',error=>console.error(error));return child;
}
function command(role,action,extra={}){
  const target=path.join(folder,`command-${role}.json`);
  const content=JSON.stringify({sequence:sequences[role]=(sequences[role]??0)+1,action,...extra});
  fs.writeFileSync(target+'.tmp',content);
  for(let i=0;;i++){try{fs.renameSync(target+'.tmp',target);return;}catch(error){if(i===99)throw error;Atomics.wait(new Int32Array(new SharedArrayBuffer(4)),0,0,10);}}
}
function check(name,passed){checks.push({name,passed});console.log((passed?'PASS ':'FAIL ')+name);if(!passed)throw new Error(name);}
async function until(name,predicate,timeout=30000){
  const end=Date.now()+timeout;
  while(Date.now()<end){if(predicate()){check(name,true);return;}await sleep(60);}
  throw new Error(name+' timed out: '+JSON.stringify({host:host(),client:client()}));
}
async function join(role){
  guest=role;launch(role);
  if(relay){await until('Guest process starts',()=>!!client());command(role,'join',{value:host().code});}
  await until('Two peers finish joining room',()=>host()?.members.length===2&&client()?.members.length===2&&!host().busy&&!client().busy);
}
async function ready(){command(guest,'ready');await until('Current map and both participants ready',()=>host()?.canStart);}
try {
  launch('host');
  await until('Host room starts',()=>host()?.active&&host().members.length===1&&(relay?!!host().code:true),60000);
  const unverified=JSON.parse(fs.readFileSync(path.join(creatorReport.folder,'uncleared.bellmap'),'utf8'));
  unverified.name='아직 공유하지 않은 수정본';
  const unverifiedFile=path.join(folder,'unverified.bellmap');fs.writeFileSync(unverifiedFile,JSON.stringify(unverified));
  command('host','user-map',{value:unverifiedFile});
  await until('Host processes an unverified map request',()=>host().commandSequence===sequences.host);
  check('Unverified map cannot replace the official room course',!host().selectedMap.startsWith('user:')&&!host().userMapHash);
  // Select before the guest exists: joining clients must receive the current map.
  command('host','user-map',{value:small});
  await until('Host snapshots its custom map',()=>host()?.selectedMap.startsWith('user:')&&!!host().userMapHash);
  await join('client1');
  await until('Late joining guest receives validated current map',()=>client()?.userMapReady&&host().userMapReady&&client().userMapHash===host().userMapHash);
  check('Map selection alone never auto-readies guest',!host().canStart);
  await ready();command('host','start');
  await until('Both peers build identical custom course and start',()=>host().playing&&client().playing&&host().loadedUserMapHash===host().userMapHash&&client().loadedUserMapHash===host().userMapHash,60000);
  check('Checkpoint layout agrees across peers',host().checkpoints.length>0&&JSON.stringify(host().checkpoints)===JSON.stringify(client().checkpoints));
  command('host','five');await until('Exposed card state replicates',()=>JSON.parse(host().cardState).reveals>0&&host().cardState===client().cardState);
  command('host','action',{value:'Interact'});await sleep(150);command(guest,'action',{value:'Interact'});
  await until('Both players legally leave their seats on custom map',()=>JSON.parse(host().cardState).five&&host().actors.every(a=>!a.seated)&&client().actors.every(a=>!a.seated));
  command('host','teleport',{seat:1,position:host().checkpoints[0].position});
  await until('Custom checkpoint records on host and guest',()=>host().actors[1].checkpoint===0&&client().actors[1].checkpoint===0);
  const falls=host().actors[1].falls;command(guest,'action',{value:'Respawn'});
  await until('Guest respawns at custom checkpoint',()=>client().actors[1].falls===falls+1&&!client().actors[1].respawning&&client().actors[1].checkpoint===0);
  command('host','teleport',{seat:1,position:host().bellLanding});await sleep(500);command(guest,'action',{value:'Interact'});
  await until('Custom bell awards cards and reseats participants',()=>host().bells===1&&client().bells===1&&host().actors.every(a=>a.seated)&&client().actors.every(a=>a.seated));
  command('host','room');await until('Custom match returns to room',()=>!host().playing&&!client().playing);
  const cachePath=path.join(process.env.USERPROFILE,'AppData/LocalLow/HalliGalli Prototype/HalliGalli Deathrun - Bell Run/DownloadedMaps',createHash('sha256').update(fs.readFileSync(large)).digest('hex')+'.bellmap');
  if(fs.existsSync(cachePath))fs.unlinkSync(cachePath);
  const previousRevision=host().userMapRevision;
  command('host','user-map',{value:large});
  await until('New map remains unready while its contents transfer',()=>host().userMapRevision>previousRevision&&client().userMapHash===host().userMapHash&&!client().userMapReady);
  check('Starting is blocked while new map transfers',!host().canStart);
  // Replace an in-flight map with an official map; stale chunks cannot reinstate it.
  command('host','map',{value:'first-steps'});
  await until('Official selection cancels pending map transfer',()=>host().selectedMap==='first-steps'&&client().selectedMap==='first-steps'&&!client().userMapHash);
  command('host','user-map',{value:large});
  await until('Large map reaches both peers after cancellation',()=>host().selectedMap.startsWith('user:')&&host().userMapReady&&client().userMapReady&&host().userMapHash===client().userMapHash,60000);
  await ready();command('host','start');
  await until('Large custom map loads with the exact acknowledged snapshot',()=>host().playing&&client().playing&&host().loadedUserMapHash===host().userMapHash&&client().loadedUserMapHash===host().userMapHash,60000);
  command('host','room');await until('Second custom match returns to room',()=>!host().playing&&!client().playing);
  command('host','map',{value:'first-steps'});await until('Official course selected after custom play',()=>client().selectedMap==='first-steps');
  await ready();command('host','start');
  await until('Official map still loads after custom maps',()=>host().playing&&client().playing&&host().map==='first-steps'&&client().map==='first-steps'&&!host().loadedUserMapHash&&!client().loadedUserMapHash,60000);
  check('No probe execution errors',!host().error&&!client().error);
} catch(error) {
  checks.push({name:String(error),passed:false});console.error(error);
  fs.writeFileSync(path.join(folder,'failure.json'),JSON.stringify({host:host(),client:client()},null,2));process.exitCode=1;
} finally {
  for(const role of ['host',guest])try{command(role,'leave');}catch{}
  await sleep(1800);for(const child of processes)child.kill();
  const report={utc:new Date().toISOString(),transport:relay?'Unity Relay':'loopback',folder,passed:checks.filter(c=>c.passed).length,failed:checks.filter(c=>!c.passed).length,checks};
  fs.mkdirSync(path.join(project,'TestResults'),{recursive:true});
  fs.writeFileSync(path.join(project,`TestResults/usermap-network-${relay?'relay':'loopback'}.json`),JSON.stringify(report,null,2));
  console.log('Evidence: '+folder);
}
