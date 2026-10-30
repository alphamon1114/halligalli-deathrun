import fs from 'node:fs';
import path from 'node:path';
import {spawn} from 'node:child_process';
const project=path.resolve(process.argv[2]??'.');
const source=JSON.parse(fs.readFileSync(path.join(project,'TestResults/usermap-creator-clear.json'),'utf8')).small;
const folder=path.join(project,'Tools/Network/Runs','downloads-'+Date.now());fs.mkdirSync(folder,{recursive:true});
const root=path.join(process.env.USERPROFILE,'AppData/LocalLow/HalliGalli Prototype/HalliGalli Deathrun - Bell Run');
const proofDirectory=path.join(root,'UserMapClearance');
const priorProofs=new Set(fs.existsSync(proofDirectory)?fs.readdirSync(proofDirectory):[]);
const checks=[],processes=[],snapshots={},sequences={};let cacheFile,hash;
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
function state(role){try{snapshots[role]=JSON.parse(fs.readFileSync(path.join(folder,'state-'+role+'.json'),'utf8'));}catch{}return snapshots[role];}
function launch(role){const child=spawn(path.join(project,'Build/Windows/HalliGalliMovement.exe'),['-screen-fullscreen','0','-screen-width','1440','-screen-height','900','--net-probe',role,'--probe-dir',folder,'--net-port','17890','-logFile',path.join(folder,role+'.log')],{stdio:'ignore'});processes.push(child);return child;}
function command(role,action,extra={}){const file=path.join(folder,'command-'+role+'.json');fs.writeFileSync(file+'.tmp',JSON.stringify({sequence:sequences[role]=(sequences[role]??0)+1,action,...extra}));for(let i=0;;i++){try{fs.renameSync(file+'.tmp',file);return;}catch(e){if(i===99)throw e;Atomics.wait(new Int32Array(new SharedArrayBuffer(4)),0,0,10);}}}
function check(name,passed){checks.push({name,passed});if(!passed)throw Error(name);}
async function until(name,fn,timeout=30000){const end=Date.now()+timeout;while(Date.now()<end){if(fn()){check(name,true);return;}await sleep(60);}throw Error(name+' '+JSON.stringify(snapshots));}
async function processed(role){await until('Command applied: '+role,()=>state(role)?.commandSequence===sequences[role]);}
try{
 launch('manual');await until('Receiver player starts',()=>!!state('manual'),60000);
 command('manual','studio-open');await until('Import editor opens',()=>state('manual').studioOpen&&!state('manual').studioBusy);
 command('manual','studio-import',{value:source});await processed('manual');await until('Import confirms discarding initial draft',()=>state('manual').studioConfirmation);
 command('manual','studio-confirm',{value:'discard'});await until('Imported editable copy remains uncertified',()=>!state('manual').studioBusy&&!state('manual').studioConfirmation&&!state('manual').studioCleared);
 command('manual','studio-close');await until('Close preserves explicit discard choice',()=>state('manual').studioConfirmation);command('manual','studio-confirm',{value:'discard'});await until('Returns home',()=>!state('manual').studioOpen);
 command('manual','host-loopback',{value:'17890'});await until('Recipient creates a room',()=>state('manual').active&&state('manual').host&&!state('manual').busy);
 command('manual','user-map',{value:source});await until('Imported original can host without recipient solo clear',()=>state('manual').selectedMap.startsWith('user:'));
 hash=state('manual').userMapHash;cacheFile=path.join(root,'DownloadedMaps',hash+'.bellmap');
 check('Shared original is persistently stored',fs.existsSync(cacheFile));
 fs.unlinkSync(cacheFile); // Separate-machine cache miss, scoped only to this scenario's map.
 launch('client1');await until('Joining guest downloads and verifies selected map',()=>state('client1')?.userMapReady&&state('client1').userMapHash===hash&&state('manual').members.length===2);
 const sourceId=JSON.parse(fs.readFileSync(source,'utf8')).id;
 const proofName=sourceId+'-'+hash+'.clearance';
 check('Recipient hosting does not fabricate solo-clear proof',priorProofs.has(proofName)||!fs.existsSync(path.join(proofDirectory,proofName)));
 check('First guest did not use a cache',!state('client1').userMapCacheHit);
 check('Downloaded original survives on disk',fs.existsSync(cacheFile));
 command('client1','ready');await until('Download completion permits ready and start',()=>state('manual').canStart);
 command('manual','start');await until('Both players actually load downloaded geometry',()=>state('manual').playing&&state('client1').playing&&state('manual').loadedUserMapHash===hash&&state('client1').loadedUserMapHash===hash,60000);
 command('manual','room');await until('Both return to room',()=>!state('manual').playing&&!state('client1').playing);
 command('manual','leave');await until('Original room closes',()=>!state('manual').active&&!state('client1').active);
 for(const child of processes)child.kill();await sleep(1500);
 launch('host');await until('Fresh process creates new room',()=>state('host')?.active&&!state('host').busy,60000);
 command('host','user-map',{value:cacheFile});await until('Previously downloaded map can be rehosted after restart',()=>state('host').userMapHash===hash);
 const modified=JSON.parse(fs.readFileSync(cacheFile,'utf8'));modified.objects[0].color=(modified.objects[0].color+1)%8;const changed=path.join(folder,'edited.bellmap');fs.writeFileSync(changed,JSON.stringify(modified));
 command('host','user-map',{value:changed});await processed('host');check('Edited download cannot inherit original hosting permission',state('host').userMapHash===hash);
 launch('client2');await until('Next guest reuses matching cache',()=>state('client2')?.userMapReady&&state('client2').userMapHash===hash&&state('client2').userMapCacheHit);
 command('client2','ready');await until('Cached-map ACK permits new host to start',()=>state('host').canStart);
 command('host','capture',{value:'downloaded-map-room'});await processed('host');await sleep(300);
 command('host','start');await until('Rehosted cached map actually plays',()=>state('host').playing&&state('client2').playing&&state('client2').loadedUserMapHash===hash,60000);
 check('No player probe errors',['manual','client1','host','client2'].every(role=>!state(role).error));
}catch(error){checks.push({name:String(error),passed:false});process.exitCode=1;}
finally{for(const child of processes)child.kill();const report={utc:new Date().toISOString(),folder,cacheFile,passed:checks.filter(c=>c.passed).length,failed:checks.filter(c=>!c.passed).length,checks};fs.writeFileSync(path.join(project,'TestResults/usermap-downloads.json'),JSON.stringify(report,null,2));}
