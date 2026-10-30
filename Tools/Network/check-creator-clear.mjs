import fs from 'node:fs';
import path from 'node:path';
import {spawn} from 'node:child_process';
import {randomUUID} from 'node:crypto';

const project=path.resolve(process.argv[2]??'.');
const folder=path.join(project,'Tools/Network/Runs','creator-clear-'+Date.now());
fs.mkdirSync(folder,{recursive:true});
const checks=[],ownedMaps=[];let sequence=0,lastState=null,game;
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
const id=()=>randomUUID().replaceAll('-','');
function launch(){
  game=spawn(path.join(project,'Build/Windows/HalliGalliMovement.exe'),['-screen-fullscreen','0','-screen-width','1440','-screen-height','900','--net-probe','manual','--probe-dir',folder,'-logFile',path.join(folder,'game-'+Date.now()+'.log')],{stdio:'ignore'});
}
function state(){try{lastState=JSON.parse(fs.readFileSync(path.join(folder,'state-manual.json'),'utf8'));}catch{}return lastState;}
function command(action,extra={}){
  const file=path.join(folder,'command-manual.json');fs.writeFileSync(file+'.tmp',JSON.stringify({sequence:++sequence,action,...extra}));
  for(let i=0;;i++){try{fs.renameSync(file+'.tmp',file);return;}catch(e){if(i===99)throw e;Atomics.wait(new Int32Array(new SharedArrayBuffer(4)),0,0,10);}}
}
function check(name,passed){checks.push({name,passed});console.log((passed?'PASS ':'FAIL ')+name);if(!passed)throw Error(name);}
async function until(name,fn,timeout=30000){const end=Date.now()+timeout;while(Date.now()<end){if(fn()){check(name,true);return;}await sleep(40);}throw Error(name+' '+JSON.stringify(state()));}
async function settle(){await sleep(300);if(state()?.error)throw Error(state().error);}
const mapDirectory=path.join(process.env.USERPROFILE,'AppData/LocalLow/HalliGalli Prototype/HalliGalli Deathrun - Bell Run/UserMaps');
const localFile=mapId=>path.join(mapDirectory,mapId+'.bellmap');
function part(kind,position,scale){return{id:id(),kind,position,rotation:{x:0,y:0,z:0},scale,color:kind===3?2:0,travel:{x:0,y:0,z:0},period:4,speed:2};}
const fixture={version:1,id:id(),name:'클리어 검증 코스',description:'실제 이동 물리로 걸어서 종을 울리는 검증 코스',fallHeight:-5,objects:[
  part(0,{x:0,y:-.25,z:3},{x:10,y:.5,z:6}),
  part(3,{x:0,y:0,z:2},{x:4,y:.2,z:1}),
  part(8,{x:0,y:0,z:4.5},{x:1,y:1,z:1}),
  part(9,{x:0,y:.025,z:3.65},{x:.35,y:.35,z:.35})]};
const input=path.join(folder,'uncleared.bellmap');fs.writeFileSync(input,JSON.stringify(fixture));
const small=path.join(folder,'cleared-small.bellmap'),large=path.join(folder,'cleared-large.bellmap');
async function importDraft(file){const previous=state().studioDraftId;command('studio-import',{value:file});await settle();if(state().studioConfirmation){command('studio-confirm',{value:'discard'});}
  await until('Imported map opens as a fresh uncertified draft',()=>!state().studioBusy&&state().studioDraftId!==previous&&!state().studioCleared);ownedMaps.push(state().studioDraftId);}
async function begin(){command('studio-test');await until('Solo clear starts at central departure point',()=>state().studioTesting&&!state().studioBusy&&state().actors.length===1&&!state().actors[0].seated&&Math.abs(state().actors[0].position.z+.7)<.15);}
async function approachBell(){command('move',{z:1});await until('Real movement reaches bell approach',()=>state().actors[0].position.z>3.25);command('move',{z:0});await settle();}
async function finish(){command('studio-interact');await until('Ringing the bell certifies the exact map',()=>state().bells===1&&state().studioCleared);command('move',{z:0});command('capture',{value:'clear-success'});await settle();command('studio-edit');await until('Clear proof survives return to editing',()=>!state().studioTesting&&!state().studioBusy&&state().studioCleared);}
let savedId;
try{
  launch();await until('Player starts',()=>!!state(),60000);command('studio-open');await until('Editor opens',()=>state().studioOpen&&!state().studioBusy);
  command('studio-save');await settle();check('Uncleared default cannot create a saved map',!fs.existsSync(localFile(state().studioDraftId)));
  const denied=path.join(folder,'denied.bellmap');command('studio-export',{value:denied});await settle();check('Uncleared default cannot export',!fs.existsSync(denied));
  await importDraft(input);check('Import does not auto-save an uncleared file',!fs.existsSync(localFile(state().studioDraftId)));
  await begin();command('move',{z:1});await until('Player physically walks over checkpoint',()=>state().actors[0].position.z>2.3);command('move',{z:0});await settle();
  check('Checkpoint does not grant clear or save progress during verification',state().actors[0].checkpoint===-1&&!state().studioCleared);
  let falls=state().actors[0].falls;command('studio-respawn');await until('R restarts at departure instead of checkpoint',()=>state().actors[0].falls===falls+1&&!state().actors[0].respawning&&Math.abs(state().actors[0].position.z+.7)<.15);
  command('move',{z:1});await until('Player enters course again',()=>state().actors[0].position.z>1);command('move',{x:1,z:0});falls=state().actors[0].falls;
  await until('Walking off course causes an actual fall',()=>state().actors[0].falls>falls);command('move',{x:0,z:0});
  await until('Fall retries from the central start without certification',()=>!state().actors[0].respawning&&Math.abs(state().actors[0].position.z+.7)<.15&&!state().studioCleared);
  await approachBell();check('Reaching bell vicinity alone does not certify',!state().studioCleared&&state().bells===0);
  await finish();savedId=state().studioDraftId;command('studio-save');await until('Cleared map can be saved',()=>!state().studioDirty&&fs.existsSync(localFile(savedId)));
  command('studio-export',{value:small});await until('Cleared map exports as one portable file',()=>fs.existsSync(small));const saved=fs.readFileSync(localFile(savedId),'utf8');
  command('studio-place',{value:'Block',position:{x:25,y:1,z:20}});await until('Editing immediately invalidates clearance',()=>state().studioDirty&&!state().studioCleared);
  command('studio-save');await settle();check('Uncleared edit cannot overwrite last cleared saved map',fs.readFileSync(localFile(savedId),'utf8')===saved);
  command('studio-export',{value:denied});await settle();check('Uncleared edit cannot be exported',!fs.existsSync(denied));
  command('studio-undo');await settle();check('Undo does not manufacture a fresh clear attempt',!state().studioCleared);
  await importDraft(small);check('Portable file does not import its author local proof',!state().studioCleared&&!fs.existsSync(localFile(state().studioDraftId)));
  const bigger=structuredClone(fixture);bigger.id=id();bigger.name='큰 클리어 검증 코스';
  for(let i=0;i<400;i++)bigger.objects.push(part(0,{x:80+(i%20)*3,y:0,z:20+Math.floor(i/20)*3},{x:2,y:1,z:2}));
  const bigInput=path.join(folder,'uncleared-large.bellmap');fs.writeFileSync(bigInput,JSON.stringify(bigger));await importDraft(bigInput);await begin();await approachBell();await finish();
  command('studio-save');await until('Large cleared course saves',()=>!state().studioDirty&&fs.existsSync(localFile(state().studioDraftId)));
  command('studio-export',{value:large});await until('Large cleared course exports',()=>fs.existsSync(large));
  command('studio-close');await until('Editor exits cleanly after cleared save',()=>!state().studioOpen);
  game.kill();await new Promise(resolve=>game.once('exit',resolve));fs.rmSync(path.join(folder,'command-manual.json'));fs.rmSync(path.join(folder,'state-manual.json'));lastState=null;
  launch();await until('Fresh player process starts',()=>!!state(),60000);command('studio-open');await until('Fresh editor opens',()=>state().studioOpen&&!state().studioBusy);
  command('studio-reopen',{value:savedId});await settle();if(state().studioConfirmation)command('studio-confirm',{value:'discard'});
  await until('Unchanged local saved map retains clearance after restart',()=>state().studioDraftId===savedId&&state().studioCleared);
  command('capture',{value:'cleared-editor'});await settle();check('No probe execution errors',!state().error);
}catch(error){checks.push({name:String(error),passed:false});console.error(error);process.exitCode=1;}
finally{
  if(game)game.kill();
  const report={utc:new Date().toISOString(),folder,small,large,ownedMaps,passed:checks.filter(c=>c.passed).length,failed:checks.filter(c=>!c.passed).length,checks};
  fs.writeFileSync(path.join(project,'TestResults/usermap-creator-clear.json'),JSON.stringify(report,null,2));
}
