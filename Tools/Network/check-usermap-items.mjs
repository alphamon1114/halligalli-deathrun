import fs from 'node:fs';
import path from 'node:path';
import {spawn} from 'node:child_process';
import {randomUUID} from 'node:crypto';
const project=path.resolve(process.argv[2]??'.');
const folder=path.join(project,'Tools/Network/Runs','usermap-items-'+Date.now());fs.mkdirSync(folder,{recursive:true});
const storage=path.join(process.env.USERPROFILE,'AppData/LocalLow/HalliGalli Prototype/HalliGalli Deathrun - Bell Run');
const checks=[],processes=[],ownedMaps=[],cached={},sequences={};
const sleep=ms=>new Promise(r=>setTimeout(r,ms)),id=()=>randomUUID().replaceAll('-','');
function state(role='manual'){try{cached[role]=JSON.parse(fs.readFileSync(path.join(folder,'state-'+role+'.json'),'utf8'));}catch{}return cached[role];}
const host=()=>state(),guest=()=>state('client1');
function check(name,passed){checks.push({name,passed});if(!passed)throw Error(name);}
async function wait(fn,timeout=20000){const end=Date.now()+timeout;while(Date.now()<end){if(fn())return;await sleep(40);}throw Error('Runtime state timeout');}
async function command(role,action,extra={}){const file=path.join(folder,'command-'+role+'.json');const sequence=sequences[role]=(sequences[role]??0)+1;fs.writeFileSync(file+'.tmp',JSON.stringify({sequence,action,...extra}));for(let n=0;;n++){try{fs.renameSync(file+'.tmp',file);break;}catch(e){if(n===99)throw e;await sleep(10);}}await wait(()=>state(role)?.commandSequence===sequence);if(state(role).error)throw Error(state(role).error);}
const cmd=(action,extra)=>command('manual',action,extra);
function launch(role){const args=role==='manual'?['-screen-fullscreen','0','-screen-width','1440','-screen-height','900']:['-batchmode','-nographics'];const p=spawn(path.join(project,'Build/Windows/HalliGalliMovement.exe'),[...args,'--net-probe',role,'--probe-dir',folder,'--net-port','17894','-logFile',path.join(folder,role+'.log')],{stdio:'ignore'});processes.push(p);return p;}
async function capture(name){await cmd('capture',{value:name});await wait(()=>fs.existsSync(path.join(folder,name+'.png')));}
function part(kind,position,scale={x:1,y:1,z:1}){return{id:id(),kind,position,rotation:{x:0,y:0,z:0},scale,color:0,travel:{x:0,y:0,z:0},period:4,speed:2};}
const fixture={version:1,id:id(),name:'아이템 제작 검증',description:'아이템 박스 저장과 실제 플레이',fallHeight:-5,objects:[part(0,{x:0,y:-.25,z:15},{x:40,y:.5,z:30}),part(8,{x:0,y:0,z:8})]};
const input=path.join(folder,'input.bellmap'),shared=path.join(folder,'shared.bellmap'),maximumFile=path.join(folder,'maximum.bellmap');fs.writeFileSync(input,JSON.stringify(fixture));ownedMaps.push(fixture.id);
async function importMap(file){const before=host().studioDraftId;await cmd('studio-import',{value:file});if(host().studioConfirmation)await cmd('studio-confirm',{value:'discard'});await wait(()=>!host().studioBusy&&host().studioDraftId!==before);ownedMaps.push(host().studioDraftId);}
async function closeStudio(){await cmd('studio-close');if(host().studioConfirmation)await cmd('studio-confirm',{value:'discard'});await wait(()=>!host().studioOpen);}
async function start(){await command('client1','ready');await wait(()=>host().canStart);await cmd('start');await wait(()=>host().playing&&guest().playing,60000);}
async function stand(){await cmd('five');await cmd('action',{value:'Interact'});await command('client1','action',{value:'Interact'});await wait(()=>host().actors.slice(0,2).every(a=>!a.seated));}
async function teleport(seat,position){await cmd('teleport',{seat,position});await wait(()=>Math.abs(host().actors[seat].position.x-position.x)<.25&&Math.abs(host().actors[seat].position.z-position.z)<.25&&host().actors[seat].grounded);}
try{
 launch('manual');await wait(()=>!!host(),60000);await cmd('studio-open');await wait(()=>host().studioOpen&&!host().studioBusy);await importMap(input);
 await cmd('studio-place',{value:'ItemBox',position:{x:0,y:0,z:3}});const first=host().studioSelected;
 const box=part(10,{x:0,y:0,z:3});box.id=first;const validHash=host().studioDraftHash;
 for(const [name,change] of [['Resized pickup rejected',o=>o.scale.x=2],['Tilted pickup rejected',o=>o.rotation.x=45],['Animated pickup bounds outside map rejected',o=>o.position.z=.5]]){
  const invalid=structuredClone(box);change(invalid);await cmd('studio-update',{value:JSON.stringify(invalid)});check(name,host().studioDraftHash===validHash);
 }
 await cmd('studio-duplicate');check('Item box duplicates as a distinct editable part',host().studioObjects===4&&host().studioSelected!==first);
 await cmd('studio-delete');await cmd('studio-undo');check('Deleting an item box can be undone',host().studioObjects===4);await cmd('studio-redo');
 await cmd('studio-place',{value:'ItemBox',position:{x:10,y:0,z:20}});check('Boxes coexist with terrain and bell',host().studioObjects===4);
 await cmd('studio-select',{value:first});await cmd('studio-focus',{value:'selected'});await cmd('studio-view',{value:'Angled'});await capture('item-box-editor');
 await cmd('studio-select',{value:fixture.objects[0].id});await cmd('studio-click',{position:{x:0,y:1.05,z:3},x:10});check('Collider-free item box can be selected through its visible shape',host().studioSelected===first);
 await cmd('studio-parts-menu',{value:'open'});await capture('item-box-menu');await cmd('studio-choose-part',{value:'ItemBox'});check('Picking item-box component closes the compact menu',!host().studioPartsMenu);
 const denied=path.join(folder,'uncleared-denied.bellmap');await cmd('studio-export',{value:denied});check('Adding boxes does not bypass required solo clearance',!fs.existsSync(denied));
 await cmd('studio-test');await wait(()=>host().studioTesting&&!host().studioBusy&&host().actors.length===1&&!host().actors[0].seated);
 await cmd('move',{z:1});await wait(()=>host().race?.actors?.[0]?.item>0);await cmd('move',{z:0});check('Walking into creator box grants a live solo item',host().race.boxes.length===2&&[2,3,4].includes(host().race.actors[0].item));await capture('item-box-solo');
 await cmd('race-item');await wait(()=>host().race.actors[0].uses===1&&host().race.actors[0].item===0);check('Solo verification uses the real item action',true);
 await cmd('move',{z:1});await wait(()=>host().actors[0].position.z>7.1);await cmd('move',{z:0});await cmd('studio-interact');await wait(()=>host().studioCleared&&host().bells===1);
 await cmd('studio-edit');await wait(()=>!host().studioTesting&&!host().studioBusy);await cmd('studio-save');await wait(()=>!host().studioDirty);await cmd('studio-export',{value:shared});await wait(()=>fs.existsSync(shared));
 const saved=JSON.parse(fs.readFileSync(shared,'utf8'));check('Cleared portable file retains exact box placements',saved.objects.filter(o=>o.kind===10).length===2&&saved.objects.some(o=>o.kind===10&&o.position.x===10&&o.position.z===20));
 await cmd('studio-select',{value:first});await cmd('studio-delete');check('Editing a cleared box invalidates the clear proof',!host().studioCleared);await importMap(shared);check('Imported editable copy does not inherit solo clearance',!host().studioCleared);
 const maximum=structuredClone(fixture);maximum.id=id();maximum.name='최대 아이템 박스 검증';maximum.objects[0].position={x:0,y:-.25,z:50};maximum.objects[0].scale={x:100,y:.5,z:100};
 for(let i=0;i<510;i++)maximum.objects.push(part(10,{x:-24+(i%17)*3,y:0,z:3+Math.floor(i/17)*3}));
 fs.writeFileSync(maximumFile,JSON.stringify(maximum));ownedMaps.push(maximum.id);await importMap(maximumFile);check('Full 512-object map accepts 510 item boxes',host().studioObjects===512);await closeStudio();
 await cmd('host-loopback',{value:'17894'});await wait(()=>host().active&&host().host&&!host().busy);await cmd('user-map',{value:maximumFile});await wait(()=>host().selectedMap.startsWith('user:'));
 let hash=host().userMapHash;const maxCache=path.join(storage,'DownloadedMaps',hash+'.bellmap');if(fs.existsSync(maxCache))fs.unlinkSync(maxCache);
 launch('client1');await wait(()=>guest()?.userMapReady&&guest().userMapHash===hash&&host().members.length===2);check('Joining guest downloads the full box map without cache',!guest().userMapCacheHit&&fs.existsSync(maxCache));await start();
 await wait(()=>host().race?.boxes?.length===510&&guest().race?.boxes?.length===510);await stand();await teleport(0,{x:24,y:.025,z:90});await wait(()=>host().race.boxes[509].cooldown>0&&guest().race.boxes[509].cooldown>0);check('Last box in maximum map replicates pickup and cooldown',host().race.actors[0].item===guest().race.actors[0].item&&host().race.actors[0].item>0);
 await cmd('room');await wait(()=>!host().playing&&!guest().playing);await cmd('user-map',{value:shared});hash=host().userMapHash;await wait(()=>guest().userMapReady&&guest().userMapHash===hash);await start();await wait(()=>host().race?.boxes?.length===2&&guest().race?.boxes?.length===2);await stand();
 let attack=0;
 for(let n=0;n<16;n++){
  await teleport(1,{x:0,y:.025,z:12});await cmd('random-seed',{value:String(125+n)});await teleport(0,{x:10,y:.025,z:20});await wait(()=>host().race.actors[0].item>0&&guest().race.actors[0].item===host().race.actors[0].item);
  check('User-map leader follows bell distance, not increasing Z',host().race.leader===1);attack=host().race.actors[0].item;if(attack===1||attack===5)break;
  await cmd('action',{value:'Respawn'});await wait(()=>!host().actors[0].respawning&&host().race.actors[0].item===0);await wait(()=>host().race.boxes[1].cooldown===0);
 }
 check('Trailing player receives an offensive item on a shared map',attack===1||attack===5);
 await cmd('pointer',{yaw:Math.atan2(-10,-8)*180/Math.PI,pitch:0});await cmd('race-item');await wait(()=>host().race.missileHits+host().race.laserHits>0&&guest().race.missileHits+guest().race.laserHits>0);check('Shared user-map attack applies and replicates to the opponent',host().race.actors[1].flips+host().race.actors[1].pushes===1);await capture('item-box-multiplayer');
 await cmd('room');await wait(()=>!host().playing&&!guest().playing);await cmd('map',{value:'first-steps'});await start();await sleep(300);check('Changing to an item-free map removes all item state',!host().race?.boxes?.length&&!guest().race?.boxes?.length);
 await cmd('room');await wait(()=>!host().playing&&!guest().playing);await cmd('map',{value:'item-race'});for(let i=0;i<3;i++)await cmd('add-bot');await wait(()=>host().members.length===5);await start();await wait(()=>host().actors.length===5&&host().actors[0].seated);
 await cmd('pointer',{yaw:180,pitch:22});await sleep(400);await capture('race-five-player-seated');await cmd('action',{value:'Reveal'});await sleep(400);await capture('race-five-table-focus');check('Five-player official race starts with seated local player',host().actors.length===5&&host().actors[0].seated);
 check('No runtime probe errors',!host().error&&!guest().error);
}catch(error){checks.push({name:String(error),passed:false});fs.writeFileSync(path.join(folder,'failure.json'),JSON.stringify({error:error.stack,host:host(),guest:guest()},null,2));process.exitCode=1;}
finally{for(const p of processes)p.kill();fs.writeFileSync(path.join(project,'TestResults/usermap-items.json'),JSON.stringify({utc:new Date().toISOString(),folder,shared,maximumFile,ownedMaps,passed:checks.filter(c=>c.passed).length,failed:checks.filter(c=>!c.passed).length,checks},null,2));}
