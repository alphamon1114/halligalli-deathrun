import fs from 'node:fs';
import path from 'node:path';
import {spawn} from 'node:child_process';
import {randomUUID} from 'node:crypto';
const project=path.resolve(process.argv[2]??'.'),width=Number(process.argv[3]??1440),height=width===1280?720:900;
const folder=path.join(project,'Tools/Network/Runs','first-person-'+width+'-'+Date.now());fs.mkdirSync(folder,{recursive:true});
const checks=[],ownedMaps=[];let sequence=0,cached,game;
const sleep=ms=>new Promise(r=>setTimeout(r,ms)),id=()=>randomUUID().replaceAll('-','');
function state(){try{cached=JSON.parse(fs.readFileSync(path.join(folder,'state-manual.json'),'utf8'));}catch{}return cached;}
function check(name,passed){checks.push({name,passed});if(!passed)throw Error(name);}
async function wait(fn,timeout=20000){const end=Date.now()+timeout;while(Date.now()<end){if(fn())return;await sleep(40);}throw Error('Runtime state timeout');}
async function cmd(action,extra={}){const file=path.join(folder,'command-manual.json'),n=++sequence;fs.writeFileSync(file+'.tmp',JSON.stringify({sequence:n,action,...extra}));for(let i=0;;i++){try{fs.renameSync(file+'.tmp',file);break;}catch(e){if(i===99)throw e;await sleep(10);}}await wait(()=>state()?.commandSequence===n);if(state().error)throw Error(state().error);}
async function draft(){await cmd('studio-read');return JSON.parse(fs.readFileSync(path.join(folder,'studio-draft.json'),'utf8'));}
async function preview(position,yaw=0,pitch=0,primary=false){await cmd('studio-build-preview',{position,yaw,pitch,primary});return JSON.parse(fs.readFileSync(path.join(folder,'build-preview.json'),'utf8'));}
async function capture(name){await cmd('capture',{value:name});await wait(()=>fs.existsSync(path.join(folder,name+'.png')));}
const near=(a,b)=>Math.abs(a-b)<.025,vecNear=(a,b)=>['x','y','z'].every(k=>near(a[k],b[k]));
function part(kind,position,scale){return{id:id(),kind,position,scale,rotation:{x:0,y:0,z:0},color:0,travel:{x:0,y:0,z:0},period:4,speed:2};}
const fixture={version:1,id:id(),name:'1인칭 공중 제작 검증',description:'명시적 공중 배치와 모드 전환 검증',fallHeight:-10,objects:[part(0,{x:0,y:-.5,z:15},{x:30,y:1,z:30}),part(0,{x:0,y:1,z:8},{x:2,y:2,z:2}),part(8,{x:0,y:0,z:26},{x:1,y:1,z:1})]};
const input=path.join(folder,'input.bellmap');fs.writeFileSync(input,JSON.stringify(fixture));ownedMaps.push(fixture.id);
try{
 game=spawn(path.join(project,'Build/Windows/HalliGalliMovement.exe'),['-screen-fullscreen','0','-screen-width',String(width),'-screen-height',String(height),'--net-probe','manual','--probe-dir',folder,'-logFile',path.join(folder,'game.log')],{stdio:'ignore'});
 await wait(()=>!!state(),60000);await cmd('studio-open');await wait(()=>state().studioOpen&&!state().studioBusy);
 const oldId=state().studioDraftId;await cmd('studio-import',{value:input});if(state().studioConfirmation)await cmd('studio-confirm',{value:'discard'});await wait(()=>!state().studioBusy&&state().studioDraftId!==oldId);ownedMaps.push(state().studioDraftId);
 const initial=structuredClone(state());await cmd('studio-first-person',{value:'false'});await cmd('studio-focus',{value:'all'});await cmd('studio-orbit',{x:80,z:35});await cmd('studio-first-person',{value:'true'});
 check('Overview navigation cannot displace the saved first-person pose or author map changes',!state().studioCameraOrthographic&&vecNear(state().studioCameraPosition,initial.studioCameraPosition)&&vecNear(state().studioCameraForward,initial.studioCameraForward)&&state().studioDraftHash===initial.studioDraftHash);
 await cmd('studio-choose-part',{value:'Block'});await cmd('studio-air',{value:'true'});await cmd('studio-air-distance',{x:2.3});
 let p=await preview({x:.13,y:7.13,z:12.13},0,0,true);let placed=(await draft()).objects.find(o=>o.id===p.placedId);
 check('Air placement snaps all axes without floor or face offsets and commits exact preview',p.valid&&placed&&vecNear(p.position,{x:0,y:7,z:22.5})&&vecNear(placed.position,p.position));
 p=await preview({x:.13,y:7.13,z:12.13});check('Existing block under crosshair does not implicitly switch air placement to face attachment',p.valid&&state().studioAir&&vecNear(p.position,{x:0,y:7,z:22.5}));
 check('Height guide measures preview bottom against actual floor',state().studioAirGround&&near(state().studioAirHeight,6)&&near(state().studioAirReference.y,0));
 await cmd('studio-air-distance',{x:-1000});p=await preview({x:5,y:7,z:12});check('Near distance stays usable and preview updates with distance',near(state().studioAirDistance,2)&&vecNear(p.position,{x:5,y:7,z:14}));
 await cmd('studio-air-distance',{x:1000});p=await preview({x:5,y:7,z:12});check('Far distance is bounded and floor miss is explicitly marked as reference plane',near(state().studioAirDistance,60)&&vecNear(p.position,{x:5,y:7,z:72})&&!state().studioAirGround&&near(state().studioAirReference.y,0));
 const beforeReject=state().studioDraftHash;p=await preview({x:499,y:10,z:20},90,0,true);check('Invalid air position is rejected without silent relocation or draft changes',!p.valid&&!p.placedId&&state().studioDraftHash===beforeReject);
 await cmd('studio-air-distance',{x:-52});await cmd('studio-choose-part',{value:'ItemBox'});p=await preview({x:5,y:5,z:12},0,0,true);placed=(await draft()).objects.find(o=>o.id===p.placedId);
 check('Air item box preserves its authored bottom anchor and fixed dimensions',p.valid&&placed&&vecNear(placed.position,{x:5,y:5,z:20})&&vecNear(placed.scale,{x:1,y:1,z:1}));
 await cmd('studio-undo');check('Air placement is undone without losing earlier geometry',state().studioDraftHash===beforeReject);
 await cmd('studio-choose-part',{value:'Block'});await cmd('studio-air',{value:'false'});p=await preview({x:5,y:12,z:15},0,90,true);placed=(await draft()).objects.find(o=>o.id===p.placedId);
 check('Explicit surface mode restores flush floor attachment',p.valid&&placed&&vecNear(placed.position,{x:5,y:1,z:15}));
 const camera=structuredClone(state());await cmd('studio-test');await wait(()=>state().studioTesting&&!state().studioBusy);check('Real clear attempt does not retain an editor walk pawn',!state().studioPawnActive);await cmd('studio-edit');await wait(()=>!state().studioTesting&&!state().studioBusy);
 check('F8 roundtrip retains first-person pose and edited geometry without certifying it',state().studioFirstPerson&&vecNear(state().studioCameraPosition,camera.studioCameraPosition)&&vecNear(state().studioCameraForward,camera.studioCameraForward)&&state().studioDraftHash===camera.studioDraftHash&&!state().studioCleared);
 const denied=path.join(folder,'uncleared-export.bellmap');await cmd('studio-export',{value:denied});check('First-person editing still requires actual solo completion before export',!fs.existsSync(denied));
 await cmd('studio-choose-part',{value:'Block'});await cmd('studio-air',{value:'true'});await preview({x:1,y:6,z:6});await capture('first-person-air');await cmd('studio-parts-menu',{value:'open'});await capture('first-person-parts');
}catch(e){if(checks.at(-1)?.passed!==false)checks.push({name:String(e),passed:false});fs.writeFileSync(path.join(folder,'failure.json'),JSON.stringify({error:e.message,state:state()},null,2));process.exitCode=1;}
finally{if(game)game.kill();fs.writeFileSync(path.join(project,'TestResults/first-person-'+width+'.json'),JSON.stringify({utc:new Date().toISOString(),folder,ownedMaps,passed:checks.filter(c=>c.passed).length,failed:checks.filter(c=>!c.passed).length,checks},null,2));}
