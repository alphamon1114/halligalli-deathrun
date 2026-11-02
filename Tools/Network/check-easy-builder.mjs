import fs from 'node:fs';
import path from 'node:path';
import {spawn} from 'node:child_process';
import {randomUUID} from 'node:crypto';
const project=path.resolve(process.argv[2]??'.'),width=Number(process.argv[3]??1440),height=width===1280?720:900;
const folder=path.join(project,'Tools/Network/Runs','easy-builder-'+width+'-'+Date.now());fs.mkdirSync(folder,{recursive:true});
const checks=[],ownedMaps=[];let sequence=0,cached,game;
const sleep=ms=>new Promise(r=>setTimeout(r,ms)),id=()=>randomUUID().replaceAll('-','');
function state(){try{cached=JSON.parse(fs.readFileSync(path.join(folder,'state-manual.json'),'utf8'));}catch{}return cached;}
function check(name,passed){checks.push({name,passed});if(!passed)throw Error(name);}
async function wait(fn,timeout=20000){const end=Date.now()+timeout;while(Date.now()<end){if(fn())return;await sleep(40);}throw Error('Runtime state timeout');}
async function cmd(action,extra={}){const file=path.join(folder,'command-manual.json'),n=++sequence;fs.writeFileSync(file+'.tmp',JSON.stringify({sequence:n,action,...extra}));for(let i=0;;i++){try{fs.renameSync(file+'.tmp',file);break;}catch(e){if(i===99)throw e;await sleep(10);}}await wait(()=>state()?.commandSequence===n);if(state().error)throw Error(state().error);}
async function draft(){await cmd('studio-read');return JSON.parse(fs.readFileSync(path.join(folder,'studio-draft.json'),'utf8'));}
async function preview(position,yaw=0,pitch=90,primary=true){await cmd('studio-build-preview',{position,yaw,pitch,primary});return JSON.parse(fs.readFileSync(path.join(folder,'build-preview.json'),'utf8'));}
async function capture(name){await cmd('capture',{value:name});await wait(()=>fs.existsSync(path.join(folder,name+'.png')));}
const near=(a,b)=>Math.abs(a-b)<.025,vecNear=(a,b)=>['x','y','z'].every(k=>near(a[k],b[k]));
function part(kind,position,scale){return{id:id(),kind,position,scale,rotation:{x:0,y:0,z:0},color:0,travel:{x:0,y:0,z:0},period:4,speed:2};}
const fixture={version:1,id:id(),name:'쉬운 블록 만들기 검증',description:'블록 면 붙이기와 자유 회전 검증',fallHeight:-10,objects:[part(0,{x:0,y:-.5,z:15},{x:30,y:1,z:30}),part(0,{x:0,y:1,z:8},{x:2,y:2,z:2}),part(8,{x:0,y:0,z:26},{x:1,y:1,z:1})]};
const input=path.join(folder,'input.bellmap');fs.writeFileSync(input,JSON.stringify(fixture));ownedMaps.push(fixture.id);
try{
 game=spawn(path.join(project,'Build/Windows/HalliGalliMovement.exe'),['-screen-fullscreen','0','-screen-width',String(width),'-screen-height',String(height),'--net-probe','manual','--probe-dir',folder,'-logFile',path.join(folder,'game.log')],{stdio:'ignore'});
 await wait(()=>!!state(),60000);await cmd('studio-open');await wait(()=>state().studioOpen&&!state().studioBusy);
 const oldId=state().studioDraftId;await cmd('studio-import',{value:input});if(state().studioConfirmation)await cmd('studio-confirm',{value:'discard'});await wait(()=>!state().studioBusy&&state().studioDraftId!==oldId);ownedMaps.push(state().studioDraftId);
 await cmd('studio-first-person',{value:'false'});
 await cmd('studio-select',{value:fixture.objects[1].id});await cmd('studio-focus',{value:'selected'});
 const beforeOrbit=structuredClone(state());await cmd('studio-orbit',{x:137,z:43});
 check('Free orbit changes both horizontal and vertical viewing angle without changing map data',!vecNear(state().studioCameraForward,beforeOrbit.studioCameraForward)&&Math.abs(state().studioCameraForward.y-beforeOrbit.studioCameraForward.y)>.05&&state().studioDraftHash===beforeOrbit.studioDraftHash);
 check('Orbit retains the focused part and zoom',vecNear(state().studioCameraFocus,beforeOrbit.studioCameraFocus)&&near(state().studioCameraSize,beforeOrbit.studioCameraSize));
 const orbit=structuredClone(state());await cmd('studio-advanced',{value:'true'});await cmd('studio-advanced',{value:'false'});
 check('Leaving free flight preserves arbitrary viewing angle',vecNear(state().studioCameraForward,orbit.studioCameraForward));
 await cmd('studio-pan',{x:80,z:30});check('Panning translates focus without changing orbit angle',!vecNear(state().studioCameraFocus,orbit.studioCameraFocus)&&vecNear(state().studioCameraForward,orbit.studioCameraForward));
 const size=state().studioCameraSize;await cmd('studio-zoom',{x:2});check('Wheel zoom preserves viewing angle and map data',state().studioCameraSize<size&&vecNear(state().studioCameraForward,orbit.studioCameraForward)&&state().studioDraftHash===beforeOrbit.studioDraftHash);
 await cmd('studio-focus',{value:'all'});await capture('easy-builder-overview');
 await cmd('studio-choose-part',{value:'Block'});await cmd('studio-build-size',{seat:1});
 let p=await preview({x:6,y:1,z:8},270,0);let d=await draft(),placed=d.objects.find(o=>o.id===p.placedId);
 check('Clicking a side attaches a block flush without embedding it',p.valid&&placed&&vecNear(placed.position,{x:2,y:1,z:8})&&vecNear(placed.scale,{x:2,y:2,z:2}));
 p=await preview({x:0,y:10,z:8});d=await draft();placed=d.objects.find(o=>o.id===p.placedId);
 check('Clicking the top stacks a block at the face instead of inside it',p.valid&&placed&&vecNear(placed.position,{x:0,y:3,z:8}));
 await cmd('studio-undo');await cmd('studio-undo');check('Undo restores both placed blocks exactly',state().studioDraftHash===beforeOrbit.studioDraftHash);
 await cmd('studio-select',{value:fixture.objects[1].id});await cmd('studio-build-size',{seat:2});placed=(await draft()).objects.find(o=>o.id===fixture.objects[1].id);
 check('Resizing a selected block keeps its bottom on the floor',vecNear(placed.scale,{x:4,y:4,z:4})&&near(placed.position.y,2));await cmd('studio-undo');
 await cmd('studio-choose-part',{value:'Ramp'});await cmd('studio-build-size',{seat:1});await cmd('studio-build-rotate');
 p=await preview({x:8,y:1,z:8},270,0);d=await draft();placed=d.objects.find(o=>o.id===p.placedId);
 check('Rotated rectangular ramp uses rotated footprint for side attachment',p.valid&&placed&&near(placed.rotation.y,90)&&vecNear(placed.scale,{x:2,y:2,z:4})&&near(placed.position.x,3));
 const beforeErase=state().studioDraftHash;await cmd('studio-erase',{value:'true'});await cmd('studio-erase-hit',{position:{x:3,y:12,z:8},pitch:90,yaw:0});
 check('Erase click removes only the targeted user part',!(await draft()).objects.some(o=>o.id===p.placedId)&&(await draft()).objects.length===fixture.objects.length);
 await cmd('studio-undo');check('Erasing is fully undoable',state().studioDraftHash===beforeErase);
 await cmd('studio-erase-hit',{position:{x:0,y:12,z:-4},pitch:90,yaw:0});check('Starting deck cannot be erased',state().studioDraftHash===beforeErase);
 await cmd('studio-erase',{value:'false'});await cmd('studio-choose-part',{value:'Block'});await cmd('studio-build-size',{seat:2});
 p=await preview({x:9,y:12,z:18});placed=(await draft()).objects.find(o=>o.id===p.placedId);
 check('Large preset remains grounded and matches preview dimensions',p.valid&&placed&&vecNear(placed.scale,{x:4,y:4,z:4})&&near(placed.position.y,2)&&vecNear(placed.position,p.position));
 await cmd('studio-choose-part',{value:'ItemBox'});p=await preview({x:-7,y:12,z:18});placed=(await draft()).objects.find(o=>o.id===p.placedId);
 check('Item box keeps fixed size and floor anchor under large preset',p.valid&&placed&&vecNear(placed.scale,{x:1,y:1,z:1})&&near(placed.position.y,0));
 const beforeSideBox=state().studioDraftHash;p=await preview({x:-8,y:1,z:8},90,0);
 check('Floor-anchored item boxes cannot be embedded in a vertical block face',!p.valid&&!p.placedId&&state().studioDraftHash===beforeSideBox);
 await cmd('studio-choose-part',{value:'Bell'});const beforeReject=state().studioDraftHash;p=await preview({x:10,y:12,z:24});
 check('A second finish bell is rejected without changing the map',!p.valid&&!p.placedId&&state().studioDraftHash===beforeReject);
 await cmd('studio-choose-part',{value:'Block'});p=await preview({x:500,y:12,z:18});
 check('Out-of-bounds construction is rejected rather than silently relocated',!p.valid&&!p.placedId&&state().studioDraftHash===beforeReject);
 p=await preview({x:0,y:12,z:-4});check('Building inside protected start is rejected',!p.valid&&!p.placedId&&state().studioDraftHash===beforeReject);
 await cmd('studio-select',{value:fixture.objects[1].id});await cmd('studio-focus',{value:'all'});await capture('easy-builder-built');
 await cmd('studio-parts-menu',{value:'open'});await capture('easy-builder-parts');await cmd('studio-parts-menu',{value:'close'});
 await cmd('studio-easy',{value:'false'});await capture('easy-builder-details');await cmd('studio-easy',{value:'true'});
 check('Switching simple and detailed tools preserves geometry',state().studioDraftHash===beforeReject);
 await cmd('studio-select',{value:fixture.objects[2].id});await cmd('studio-nudge',{seat:0,x:1});let bell=(await draft()).objects.find(o=>o.kind===8);
 check('Simple movement arrows can reposition the unique finish bell',near(bell.position.x,.5)&&near(bell.position.z,26));await cmd('studio-undo');
 check('Simple movement remains undoable without duplicating the bell',state().studioDraftHash===beforeReject&&(await draft()).objects.filter(o=>o.kind===8).length===1);
 const denied=path.join(folder,'uncleared.bellmap');await cmd('studio-export',{value:denied});check('Simple construction cannot bypass required solo clear',!fs.existsSync(denied));
 const camera=structuredClone(state());await cmd('studio-test');await wait(()=>state().studioTesting&&!state().studioBusy);await cmd('studio-edit');await wait(()=>!state().studioTesting&&!state().studioBusy);
 check('Play and return retain free orbit, zoom, focus and authored geometry',vecNear(state().studioCameraForward,camera.studioCameraForward)&&vecNear(state().studioCameraFocus,camera.studioCameraFocus)&&near(state().studioCameraSize,camera.studioCameraSize)&&state().studioDraftHash===camera.studioDraftHash);
}catch(e){if(checks.at(-1)?.passed!==false)checks.push({name:String(e),passed:false});fs.writeFileSync(path.join(folder,'failure.json'),JSON.stringify({error:e.message,state:state()},null,2));process.exitCode=1;}
finally{if(game)game.kill();fs.writeFileSync(path.join(project,'TestResults/easy-builder-'+width+'.json'),JSON.stringify({utc:new Date().toISOString(),folder,ownedMaps,passed:checks.filter(c=>c.passed).length,failed:checks.filter(c=>!c.passed).length,checks},null,2));}
