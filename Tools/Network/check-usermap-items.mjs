import fs from 'node:fs';
import path from 'node:path';
import {spawn} from 'node:child_process';
import {randomUUID} from 'node:crypto';
// Focused reuse of this script's previously exported two-box rotated-floor fixture:
// node Tools/Network/check-usermap-items.mjs <project> --barricade-only --attach <probe-folder> --shared-map <shared.bellmap> [--headless] [--host-role manual]
const project=path.resolve(process.argv[2]??'.');
const option=name=>{const i=process.argv.indexOf(name);if(i<0)return null;if(!process.argv[i+1]||process.argv[i+1].startsWith('--'))throw Error(name+' requires a value');return process.argv[i+1];};
const attach=option('--attach'),attached=!!attach,barricadeOnly=process.argv.includes('--barricade-only'),graphical=!process.argv.includes('--headless');
const hostRole=option('--host-role')??(attached?'host':'manual'),sharedInput=option('--shared-map');
if(barricadeOnly&&!sharedInput)throw Error('--barricade-only requires --shared-map pointing to a previously exported shared.bellmap');
const folder=attached?path.resolve(attach):path.join(project,'Tools/Network/Runs','usermap-items-'+Date.now());fs.mkdirSync(folder,{recursive:true});
const storage=path.join(process.env.USERPROFILE,'AppData/LocalLow/HalliGalli Prototype/HalliGalli Deathrun - Bell Run');
const checks=[],processes=[],ownedMaps=[],observations=[],pickups=[],cached={},sequences={};
const startedUtc=new Date().toISOString();
let barricadeSeed;
const sleep=ms=>new Promise(r=>setTimeout(r,ms)),id=()=>randomUUID().replaceAll('-','');
function state(role=hostRole){try{cached[role]=JSON.parse(fs.readFileSync(path.join(folder,'state-'+role+'.json'),'utf8'));}catch{}return cached[role];}
const host=()=>state(),guest=()=>state('client1');
function check(name,passed,detail){checks.push({name,passed,...(detail?{detail}:{})});console.log((passed?'PASS ':'FAIL ')+name);if(!passed)throw Error(name);}
async function wait(fn,timeout=20000){const end=Date.now()+timeout;while(Date.now()<end){for(const p of processes)if(p.spawnError||p.exitCode!==null)throw p.spawnError??Error('Player exited '+p.exitCode);for(const s of [host(),guest()])if(s?.error)throw Error(s.error);if(fn())return;await sleep(25);}throw Error('Runtime state timeout');}
async function command(role,action,extra={}){const file=path.join(folder,'command-'+role+'.json');if(sequences[role]===undefined){let old=0;try{old=JSON.parse(fs.readFileSync(file,'utf8')).sequence??0;}catch{}sequences[role]=Math.max(old,state(role)?.commandSequence??0);}const sequence=++sequences[role];fs.writeFileSync(file+'.tmp',JSON.stringify({sequence,action,...extra}));for(let n=0;;n++){try{fs.renameSync(file+'.tmp',file);break;}catch(e){if(n===99)throw e;await sleep(10);}}await wait(()=>state(role)?.commandSequence===sequence);}
const cmd=(action,extra)=>command(hostRole,action,extra);
function launch(role){if(attached)return;const args=graphical&&role===hostRole?['-screen-fullscreen','0','-screen-width','1440','-screen-height','900']:['-batchmode','-nographics'];const p=spawn(path.join(project,'Build/Windows/HalliGalliMovement.exe'),[...args,'-disable-audio','--net-probe',role,'--probe-dir',folder,'--net-port','17894','-logFile',path.join(folder,role+'.log')],{stdio:'ignore'});processes.push(p);p.on('error',error=>p.spawnError=error);return p;}
async function capture(name){if(!graphical)return;const file=path.join(folder,name+'.png');if(fs.existsSync(file))fs.unlinkSync(file);await cmd('capture',{value:name});await wait(()=>fs.existsSync(file));}
function part(kind,position,scale={x:1,y:1,z:1}){return{id:id(),kind,position,rotation:{x:0,y:0,z:0},scale,color:0,travel:{x:0,y:0,z:0},period:4,speed:2};}
const floor=part(0,{x:0,y:-.25,z:15},{x:30,y:.5,z:40});floor.rotation.y=90;
const fixture={version:1,id:id(),name:'아이템 제작 검증',description:'회전 바닥의 아이템 박스와 공중 설치 검증',fallHeight:-5,objects:[floor,part(8,{x:0,y:0,z:8})]};
const input=path.join(folder,'input.bellmap'),shared=sharedInput?path.resolve(sharedInput):path.join(folder,'shared.bellmap'),maximumFile=path.join(folder,'maximum.bellmap');if(!barricadeOnly){fs.writeFileSync(input,JSON.stringify(fixture));ownedMaps.push(fixture.id);}
const reportFile=path.join(project,'TestResults',barricadeOnly?'barricade-usermap-network.json':'usermap-items.json');
function report(status){fs.mkdirSync(path.join(project,'TestResults'),{recursive:true});fs.writeFileSync(reportFile,JSON.stringify({utc:startedUtc,completedUtc:status==='running'?null:new Date().toISOString(),status,protocol:host()?.protocol,folder,attached,graphical,shared,maximumFile,ownedMaps,passed:checks.filter(c=>c.passed).length,failed:checks.filter(c=>!c.passed).length,checks,pickups,observations,limitations:graphical?[]:['Headless mode does not capture crack visibility; use BarricadeValidationRunner with graphics.']},null,2));}
function failure(error){if(checks.at(-1)?.passed!==false)checks.push({name:String(error),passed:false});fs.writeFileSync(path.join(folder,'usermap-items-failure.json'),JSON.stringify({utc:new Date().toISOString(),error:error.stack,host:host(),guest:guest()},null,2));process.exitCode=1;}
report('running');const watchdog=setTimeout(()=>{failure(Error('Usermap item scenario exceeded '+(barricadeOnly?'four':'fifteen')+' minutes'));for(const p of processes)p.kill();report('failed');process.exit(1);},(barricadeOnly?4:15)*60000);
async function importMap(file){const before=host().studioDraftId;await cmd('studio-import',{value:file});if(host().studioConfirmation)await cmd('studio-confirm',{value:'discard'});await wait(()=>!host().studioBusy&&host().studioDraftId!==before);ownedMaps.push(host().studioDraftId);}
async function closeStudio(){await cmd('studio-close');if(host().studioConfirmation)await cmd('studio-confirm',{value:'discard'});await wait(()=>!host().studioOpen);}
async function start(){await command('client1','ready');await wait(()=>host().canStart);await cmd('start');await wait(()=>host().playing&&guest().playing,60000);}
async function stand(){await cmd('five');await cmd('action',{value:'Interact'});await command('client1','action',{value:'Interact'});await wait(()=>host().actors.slice(0,2).every(a=>!a.seated));}
async function teleport(seat,position){await cmd('teleport',{seat,position});await wait(()=>[host(),guest()].filter(Boolean).every(s=>Math.abs(s.actors[seat].position.x-position.x)<.25&&Math.abs(s.actors[seat].position.z-position.z)<.25&&s.actors[seat].grounded));}
function observe(name){const value={name,host:structuredClone(host()),guest:structuredClone(guest())};observations.push(value);return value;}
const roleFor=seat=>seat===0?hostRole:'client1';
const distance=(a,b)=>Math.hypot(a.x-b.x,a.y-b.y,a.z-b.z);
async function take(item,seat,leader){
 const role=roleFor(seat),other=1-seat;
 await cmd('move',{z:0});await command('client1','move',{z:0});
 // Rank sets only the fixture positions; every live box uses the same three-item draw.
 for(let attempt=0;attempt<32;attempt++){
  if(host().race.actors[seat].item!==0){
   await command(role,'action',{value:'Respawn'});
   await wait(()=>[host(),guest()].every(s=>!s.actors[seat].respawning&&s.race.actors[seat].item===0));
  }
  await teleport(other,leader?{x:-15,y:.025,z:28}:{x:0,y:.025,z:9});
  await teleport(seat,{x:-15,y:.025,z:12});
  await wait(()=>host().race.boxes.some(b=>b.cooldown<=0));
  const box=host().race.boxes.find(b=>b.cooldown<=0),before=host().race.actors[seat].acquisitions;
  const seed=item===3&&barricadeSeed!==undefined?barricadeSeed:125+attempt;
  await cmd('random-seed',{value:String(seed)});await teleport(seat,{...box.position,y:.025});
  await wait(()=>host().race.actors[seat].acquisitions>before&&host().race.actors[seat].item>0&&guest().race.actors[seat].item===host().race.actors[seat].item);
  await wait(()=>[host(),guest()].every(s=>s.race.leader===(leader?seat:other)));
  const received=host().race.actors[seat].item;
  pickups.push({seat,leader,item:received,box:box.index,seed});
  if(![1,2,3].includes(received))throw Error('Unexpected shared-map item '+received);
  if(received===item){if(item===3)barricadeSeed=seed;return;}
 }
 throw Error('No item '+item+' acquired from live unrestricted shared-map boxes');
}
async function jumpAndUse(seat,yaw){
 const role=roleFor(seat);
 await command(role,'pointer',{yaw,pitch:0});
 await command(role,'move',{z:0,yaw,jump:false});
 await command(role,'move',{z:0,yaw,jump:true});
 await wait(()=>[host(),guest()].every(s=>!s.actors[seat].grounded&&s.actors[seat].position.y>.45&&s.actors[seat].velocity.y>0),3000);
 const airborne=observe('Airborne use request, seat '+seat),before=airborne.host.race.actors[seat];
 await command(role,'race-item');await command(role,'move',{z:0,yaw,jump:false});
 return{airborne,before};
}
async function rejectAirDrop(item,seat,yaw,label){
 const before=host().race.actors[seat].uses;
 const request=await jumpAndUse(seat,yaw);await sleep(200);
 check(label,[host(),guest()].every(s=>s.race.actors[seat].item===item&&s.race.actors[seat].uses===before&&!s.race.projectiles.some(p=>p.owner===seat&&p.item===item)),request.airborne.host.actors[seat].position);
 observe(label);await wait(()=>[host(),guest()].every(s=>s.actors[seat].grounded));
}
async function placeAirDrop(item,seat,yaw){
 const request=await jumpAndUse(seat,yaw),before=request.before;
 await wait(()=>[host(),guest()].every(s=>s.race.actors[seat].uses===before.uses+1&&s.race.actors[seat].item===0&&s.race.projectiles.some(p=>p.owner===seat&&p.item===item)));
 const entity=host().race.projectiles.find(p=>p.owner===seat&&p.item===item),remote=guest().race.projectiles.find(p=>p.id===entity.id);
 const position=request.airborne.host.actors[seat].position,radians=yaw*Math.PI/180,offset=item===2?1.15:3;
 const expected={x:position.x-Math.sin(radians)*offset,y:item===2?.045:0,z:position.z-Math.cos(radians)*offset};
 check((item===2?'Banana':'Barricade')+' drops immediately to the rotated floor during a real jump',
  !host().actors[seat].grounded&&distance(entity.position,expected)<.12,{airborne:position,expected,entity});
 check('Both peers agree on airborne '+(item===2?'banana':'barricade')+' placement',
  !!remote&&distance(entity.position,remote.position)<.01&&distance(entity.direction,remote.direction)<.01&&distance(entity.size,remote.size)<.01);
 observe('Placed '+(item===2?'banana':'barricade')+' while airborne');
 return entity;
}
async function sharedBarricadeCombat(){
 await take(3,1,false);
 await wait(()=>[host(),guest()].every(s=>s.race.projectiles.every(p=>p.item!==3)&&s.actors.slice(0,2).every(a=>a.kickCooldown<=0&&a.attack==='None')));
 await teleport(0,{x:8,y:.025,z:12});await teleport(1,{x:15,y:.025,z:12});
 const wall=await placeAirDrop(3,1,90),on=s=>s.race.projectiles.find(p=>p.id===wall.id);
 check('Fresh rotated shared-map barricade has no retained damage',[host(),guest()].every(s=>on(s)?.barricadeHits===0));
 await wait(()=>[host(),guest()].every(s=>s.actors[1].grounded));
 await teleport(0,{x:wall.position.x-1.1,y:.025,z:wall.position.z});await cmd('pointer',{yaw:90,pitch:0});
 await teleport(1,{x:wall.position.x+1.1,y:.025,z:wall.position.z});await command('client1','pointer',{yaw:270,pitch:0});
 const pushes=host().actors[0].attacks;await cmd('action',{value:'Push'});
 await wait(()=>[host(),guest()].every(s=>s.actors[0].attacks===pushes+1&&s.actors[0].attack==='None'));
 check('Shared-map left-click attack cannot damage a rotated barricade',[host(),guest()].every(s=>on(s)?.barricadeHits===0));
 await cmd('action',{value:'Kick'});await wait(()=>[host(),guest()].every(s=>on(s)?.barricadeHits===1),2500);
 const kicks=host().actors[0].attacks;await cmd('action',{value:'Kick'});await sleep(300);
 check('Shared-map first kick and cooldown retry count once on both peers',[host(),guest()].every(s=>on(s)?.barricadeHits===1&&s.actors[0].attacks===kicks));
 await cmd('move',{z:1,yaw:90});await sleep(350);await cmd('move',{yaw:90});
 check('Damaged rotated shared-map barricade still blocks real movement',[host(),guest()].every(s=>on(s)?.barricadeHits===1&&s.actors[0].position.x<wall.position.x-.3&&s.actors[0].position.x>wall.position.x-1));
 observe('Shared-map damaged wall collision');await capture('item-barricade-cracked-shared');
 await command('client1','action',{value:'Kick'});await wait(()=>[host(),guest()].every(s=>!on(s)),2500);
 check('Host and guest kicks combine through the shared-map authoritative runtime',[host(),guest()].every(s=>!on(s)&&s.actors[1].kickCooldown>0));
 await teleport(1,{x:15,y:.025,z:20});await cmd('move',{z:1,yaw:90});await sleep(700);await cmd('move',{yaw:90});
 check('Broken rotated shared-map barricade permits real controller passage on both peers',[host(),guest()].every(s=>s.actors[0].position.x>wall.position.x+1));
 observe('Shared-map broken wall passage');
}
async function focusedSharedMap(){
 launch(hostRole);await wait(()=>host()?.protocol>0,60000);
 if(!host().active){await cmd('host-loopback',{value:'17894'});await wait(()=>host().active&&host().host&&!host().busy);}
 launch('client1');await wait(()=>guest()?.protocol>0&&host().members.length===2&&!host().busy&&!guest().busy,60000);
 const times=[host(),guest()].map(s=>s.worldTime);await wait(()=>[host(),guest()].every((s,i)=>s.worldTime>times[i]+.1),5000);
 check('Both shared-map peers publish fresh matching protocol state',host().protocol===guest().protocol);
 if(host().playing){await cmd('room');await wait(()=>!host().playing&&!guest().playing);}
 await cmd('user-map',{value:shared});await wait(()=>guest().userMapReady&&guest().userMapHash===host().userMapHash);
 await start();await wait(()=>host().race?.boxes?.length===2&&guest().race?.boxes?.length===2);await stand();
 await sharedBarricadeCombat();await cmd('room');await wait(()=>!host().playing&&!guest().playing);
}
try{
 if(barricadeOnly)await focusedSharedMap();
 else{
 launch(hostRole);await wait(()=>host()?.protocol>0,60000);await cmd('studio-open');await wait(()=>host().studioOpen&&!host().studioBusy);await importMap(input);
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
 await cmd('studio-select',{value:first}); // Leave placement mode before the solo-run controls are exercised.
 const denied=path.join(folder,'uncleared-denied.bellmap');await cmd('studio-export',{value:denied});check('Adding boxes does not bypass required solo clearance',!fs.existsSync(denied));
 await cmd('studio-test');await wait(()=>host().studioTesting&&!host().studioBusy&&host().actors.length===1&&!host().actors[0].seated);
 await cmd('move',{z:1});await wait(()=>host().race?.actors?.[0]?.item>0);await cmd('move',{z:0});check('Walking into creator box grants any of the three live solo items',host().race.boxes.length===2&&[1,2,3].includes(host().race.actors[0].item));await capture('item-box-solo');
 const soloItem=host().race.actors[0].item;await cmd('move',{z:1});await wait(()=>host().actors[0].position.z>6);await cmd('move',{z:0});
 const soloUses=host().race.actors[0].uses;await cmd('race-item');
 if(soloItem===1){
  check('Solo missile without an opponent preserves inventory',host().race.actors[0].item===1&&host().race.actors[0].uses===soloUses&&host().race.projectiles.length===0);
 }else{
  await wait(()=>host().race.actors[0].uses===soloUses+1&&host().race.actors[0].item===0);
  check('Solo verification creates the selected real hazard',host().race.projectiles.some(p=>p.item===soloItem));
 }
 await cmd('move',{z:1});await wait(()=>host().actors[0].position.z>7.1);await cmd('move',{z:0});await cmd('studio-interact');await wait(()=>host().studioCleared&&host().bells===1);
 await cmd('studio-edit');await wait(()=>!host().studioTesting&&!host().studioBusy);await cmd('studio-save');await wait(()=>!host().studioDirty);await cmd('studio-export',{value:shared});await wait(()=>fs.existsSync(shared));
 const saved=JSON.parse(fs.readFileSync(shared,'utf8'));check('Cleared portable file retains exact box placements',saved.objects.filter(o=>o.kind===10).length===2&&saved.objects.some(o=>o.kind===10&&o.position.x===10&&o.position.z===20));
 check('Cleared shared map retains its actual rotated floor',saved.objects.find(o=>o.id===floor.id)?.rotation.y===90);
 await cmd('studio-select',{value:first});await cmd('studio-delete');check('Editing a cleared box invalidates the clear proof',!host().studioCleared);await importMap(shared);check('Imported editable copy does not inherit solo clearance',!host().studioCleared);
 const maximum=structuredClone(fixture);maximum.id=id();maximum.name='최대 아이템 박스 검증';maximum.objects[0].position={x:0,y:-.25,z:50};maximum.objects[0].scale={x:100,y:.5,z:100};maximum.objects[0].rotation.y=0;
 for(let i=0;i<510;i++)maximum.objects.push(part(10,{x:-24+(i%17)*3,y:0,z:3+Math.floor(i/17)*3}));
 fs.writeFileSync(maximumFile,JSON.stringify(maximum));ownedMaps.push(maximum.id);await importMap(maximumFile);check('Full 512-object map accepts 510 item boxes',host().studioObjects===512);await closeStudio();
 await cmd('host-loopback',{value:'17894'});await wait(()=>host().active&&host().host&&!host().busy);await cmd('user-map',{value:maximumFile});await wait(()=>host().selectedMap.startsWith('user:'));
 let hash=host().userMapHash;const maxCache=path.join(storage,'DownloadedMaps',hash+'.bellmap');if(fs.existsSync(maxCache))fs.unlinkSync(maxCache);
 launch('client1');await wait(()=>guest()?.protocol>0&&guest().userMapReady&&guest().userMapHash===hash&&host().members.length===2);check('Joining guest downloads the full box map without cache',!guest().userMapCacheHit&&fs.existsSync(maxCache));await start();
 await wait(()=>host().race?.boxes?.length===510&&guest().race?.boxes?.length===510);await stand();await teleport(0,{x:24,y:.025,z:90});await wait(()=>host().race.boxes[509].cooldown>0&&guest().race.boxes[509].cooldown>0);check('Last box in maximum map replicates pickup and cooldown',host().race.actors[0].item===guest().race.actors[0].item&&host().race.actors[0].item>0);
 await cmd('room');await wait(()=>!host().playing&&!guest().playing);await cmd('user-map',{value:shared});hash=host().userMapHash;await wait(()=>guest().userMapReady&&guest().userMapHash===hash);await start();await wait(()=>host().race?.boxes?.length===2&&guest().race?.boxes?.length===2);await stand();
 await take(1,0,true);
 check('Shared-map leader can acquire a missile from a real box',host().race.leader===0&&host().race.actors[0].item===1);
 await teleport(0,{x:0,y:.025,z:12});await teleport(1,{x:0,y:.025,z:22});
 await wait(()=>host().race.leader===0&&guest().race.leader===0);
 check('User-map leader follows bell distance, not increasing Z',host().actors[0].position.z<host().actors[1].position.z);
 await cmd('pointer',{yaw:180,pitch:0});await cmd('race-item');
 check('Leader aiming toward the bell without a target retains the missile',host().race.actors[0].item===1);
 const flips=host().race.actors[1].flips,hits=host().race.missileHits;
 await cmd('pointer',{yaw:0,pitch:0});observe('Leader turns away from bell toward trailing opponent');await cmd('race-item');
 await wait(()=>host().race.missileHits>hits&&guest().race.missileHits>hits);
 check('Leader reverse-direction shot hits and replicates to the trailing opponent',[host(),guest()].every(s=>s.race.actors[1].flips===flips+1));
 check('Shared-map attack gives no horizontal speed to the victim',[host(),guest()].every(s=>Math.hypot(s.actors[1].velocity.x,s.actors[1].velocity.z)<.25));
 observe('Leader reverse missile hit');await capture('item-box-multiplayer');
 await wait(()=>[host(),guest()].every(s=>s.race.actors[1].effectRemaining<=0&&s.actors[1].grounded));
 await take(2,0,false);
 await teleport(1,{x:0,y:.025,z:12});await teleport(0,{x:19.5,y:.025,z:12});
 await rejectAirDrop(2,0,270,'Airborne banana without floor below its rear offset stays in inventory');
 await teleport(0,{x:-5,y:.025,z:12});
 const banana=await placeAirDrop(2,0,270),slips=host().race.actors[1].slips;
 await capture('item-banana-airborne-shared');await teleport(0,{x:-5,y:.025,z:18});await command('client1','move',{z:1,yaw:270});
 await wait(()=>host().race.actors[1].slips>slips&&guest().race.actors[1].slips>slips,4000);
 await command('client1','move',{z:0,yaw:270});
 check('Air-dropped shared banana causes real floor-contact sliding on both peers',[host(),guest()].every(s=>s.race.actors[1].effectRemaining>0&&Math.hypot(s.actors[1].velocity.x,s.actors[1].velocity.z)>1),banana.position);
 observe('Air-dropped banana collision');await wait(()=>[host(),guest()].every(s=>s.race.actors[1].effectRemaining<=0&&s.actors[1].grounded));
 await take(3,1,false);
 check('Trailing shared-map player can acquire a barricade from a real box',host().race.leader===0&&host().race.actors[1].item===3);
 await teleport(0,{x:8,y:.025,z:12});await teleport(1,{x:19.5,y:.025,z:12});
 await rejectAirDrop(3,1,270,'Airborne barricade without floor below its rear offset stays in inventory');
 // Keep the transverse measurement outside the preserved table deck, which extends below Z=0.
 await teleport(1,{x:15,y:.025,z:12});await teleport(0,{x:12,y:.025,z:12});
 await rejectAirDrop(3,1,90,'Airborne barricade with an occupied footprint stays in inventory');
 await teleport(0,{x:8,y:.025,z:12});const wall=await placeAirDrop(3,1,90);
 check('User-map barricade preserves local road width and rotated placement',wall.size.x>9.5&&wall.size.x<10.5&&wall.direction.x>.99&&Math.abs(wall.size.y-2.2)<.01&&Math.abs(wall.size.z-.6)<.01);
 await cmd('move',{z:1,yaw:90});await sleep(1300);await cmd('move',{z:0,yaw:90});
 check('Rotated shared barricade blocks actual host movement on both peers',[host(),guest()].every(s=>s.actors[0].position.x<wall.position.x-.25&&s.actors[0].position.x>wall.position.x-1));
 await teleport(0,{x:8,y:.025,z:4});await command('client1','move',{z:1,yaw:270});await sleep(1100);await command('client1','move',{z:0,yaw:270});
 check('Rotated shared barricade blocks actual guest prediction on both peers',[host(),guest()].every(s=>s.actors[1].position.x>wall.position.x+.25&&s.actors[1].position.x<wall.position.x+1));
 observe('Air-dropped barricade collision on both sides');await capture('item-barricade-shared');
 await sharedBarricadeCombat();
 await cmd('room');await wait(()=>!host().playing&&!guest().playing);await cmd('map',{value:'first-steps'});await start();await sleep(300);check('Changing to an item-free map removes all item state',!host().race?.boxes?.length&&!guest().race?.boxes?.length);
 await cmd('room');await wait(()=>!host().playing&&!guest().playing);await cmd('map',{value:'item-race'});for(let i=0;i<3;i++)await cmd('add-bot');await wait(()=>host().members.length===5);await start();await wait(()=>host().actors.length===5&&host().actors[0].seated);
 await cmd('pointer',{yaw:180,pitch:22});await sleep(400);await capture('race-five-player-seated');await cmd('action',{value:'Reveal'});await sleep(400);await capture('race-five-table-focus');check('Five-player official race starts with seated local player',host().actors.length===5&&host().actors[0].seated);
 }
 check('No runtime probe errors',!host().error&&!guest().error);
}catch(error){failure(error);}
finally{clearTimeout(watchdog);for(const p of processes)p.kill();report(process.exitCode?'failed':graphical?'passed':'limited');}
