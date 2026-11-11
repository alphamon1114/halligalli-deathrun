import fs from 'node:fs';
import path from 'node:path';
import {spawn,execFile} from 'node:child_process';
import {promisify} from 'node:util';

const project=path.resolve(process.argv[2]??'.');
const folder=path.join(project,'Tools/Network/Runs','shirt-colors-'+Date.now());
fs.mkdirSync(folder,{recursive:true});
const preferenceKey='HalliGalli.PlayerShirtColor',port='17904';
const nativeUI=!process.argv.includes('--api-only');
const checks=[],captures=[],commands=[],snapshots=[],nativeInput=[],tints=[],processes={},cached={},sequences={};
const sleep=ms=>new Promise(resolve=>setTimeout(resolve,ms)),execAsync=promisify(execFile);
let processError,preferenceBackup,restored=false;
process.on('SIGINT',()=>{processError=new Error('Interrupted; restoring the original shirt preference');});
function check(name,passed,detail){checks.push({name,passed,detail});if(!passed)throw new Error(name);}
function state(role){try{cached[role]=JSON.parse(fs.readFileSync(path.join(folder,'state-'+role+'.json'),'utf8'));}catch{}return cached[role];}
const host=()=>state('host'),guest=()=>state('client1'),peers=()=>[host(),guest()];
async function wait(name,predicate,timeout=20000){
 const end=Date.now()+timeout;
 while(Date.now()<end){if(processError)throw processError;const value=predicate();if(value)return value;await sleep(30);}
 throw new Error('Scenario state timeout: '+name);
}
async function powershell(script){
 const {stdout}=await execAsync('powershell.exe',['-NoProfile','-NonInteractive','-EncodedCommand',Buffer.from(script,'utf16le').toString('base64')],{windowsHide:true,timeout:20000});
 return stdout.trim()?JSON.parse(stdout.trim()):null;
}
const quote=value=>"'"+value.replaceAll("'","''")+"'";
async function backupPreference(){
 const settings=fs.readFileSync(path.join(project,'ProjectSettings/ProjectSettings.asset'),'utf8');
 const company=settings.match(/^  companyName: (.+)$/m)?.[1],product=settings.match(/^  productName: (.+)$/m)?.[1];
 if(!company||!product)throw new Error('Cannot locate the Windows player preference registry');
 const subkey='Software\\'+company+'\\'+product;
 const snapshot=await powershell(`
$ErrorActionPreference='Stop'
$path=${quote(subkey)};$name=${quote(preferenceKey)}
$key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($path)
$entries=@()
try {if($key){foreach($entry in $key.GetValueNames()){if($entry -match ('^'+[Regex]::Escape($name)+'(?:_h[0-9]+)?$')){$entries+=@{name=$entry;kind=$key.GetValueKind($entry).ToString();value=$key.GetValue($entry)}}}}} finally {if($key){$key.Dispose()}}
@{path=$path;preferenceKey=$name;entries=$entries}|ConvertTo-Json -Depth 6 -Compress
`);
 fs.writeFileSync(path.join(folder,'original-shirt-preference.json'),JSON.stringify(snapshot,null,2));
 return snapshot;
}
async function restorePreference(){
 if(!preferenceBackup)return;
 const file=path.join(folder,'original-shirt-preference.json');
 const result=await powershell(`
$ErrorActionPreference='Stop'
$s=Get-Content -Raw -LiteralPath ${quote(file)}|ConvertFrom-Json
$key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($s.path)
try {
 foreach($entry in $key.GetValueNames()){if($entry -match ('^'+[Regex]::Escape($s.preferenceKey)+'(?:_h[0-9]+)?$')){$key.DeleteValue($entry,$false)}}
 foreach($entry in $s.entries){
  $kind=[Enum]::Parse([Microsoft.Win32.RegistryValueKind],$entry.kind);$value=$entry.value
  if($kind -eq [Microsoft.Win32.RegistryValueKind]::DWord){$value=[int]$value}
  elseif($kind -eq [Microsoft.Win32.RegistryValueKind]::QWord){$value=[long]$value}
  elseif($kind -eq [Microsoft.Win32.RegistryValueKind]::Binary){$value=[byte[]]$value}
  elseif($kind -eq [Microsoft.Win32.RegistryValueKind]::MultiString){$value=[string[]]$value}
  $key.SetValue($entry.name,$value,$kind)
 }
 $key.Flush()
 $names=@($key.GetValueNames()|Where-Object {$_ -match ('^'+[Regex]::Escape($s.preferenceKey)+'(?:_h[0-9]+)?$')})
 $ok=$names.Count -eq @($s.entries).Count
 foreach($entry in $s.entries){$ok=$ok -and $key.GetValueKind($entry.name).ToString() -eq $entry.kind -and (ConvertTo-Json -InputObject ($key.GetValue($entry.name)) -Compress) -eq (ConvertTo-Json -InputObject $entry.value -Compress)}
 @{restored=$ok;names=$names}|ConvertTo-Json -Compress
} finally {$key.Dispose()}
`);
 restored=result.restored;
 check('Original shirt preference restored exactly; all unrelated PlayerPrefs untouched',restored,result);
}
function launch(role){
 delete cached[role];sequences[role]=0;
 for(const prefix of ['state-','command-'])fs.rmSync(path.join(folder,prefix+role+'.json'),{force:true});
 const child=spawn(path.join(project,'Build/Windows/HalliGalliMovement.exe'),['-screen-fullscreen','0','-screen-width','1280','-screen-height','720','--net-probe',role,'--probe-manual','true','--probe-dir',folder,'-logFile',path.join(folder,role+'-'+Date.now()+'.log')],{stdio:'ignore'});
 const entry={child,stopping:false};processes[role]=entry;
 child.on('error',error=>{processError=error;});
 child.on('exit',(code,signal)=>{if(!entry.stopping)processError=new Error(role+' player exited: '+code+' '+signal);});
}
async function stop(role){
 const entry=processes[role];if(!entry)return;
 entry.stopping=true;
 if(entry.child.exitCode===null&&entry.child.signalCode===null){
  const exited=new Promise(resolve=>entry.child.once('exit',resolve));entry.child.kill();
  await Promise.race([exited,sleep(15000).then(()=>{throw new Error('Player did not exit before preference restoration: '+role);})]);
 }
 delete processes[role];
}
async function command(role,action,extra={}){
 const sequence=sequences[role]=(sequences[role]??0)+1,file=path.join(folder,'command-'+role+'.json');
 const entry={utc:new Date().toISOString(),role,sequence,action,...extra};commands.push(entry);
 fs.writeFileSync(file+'.tmp',JSON.stringify({sequence,action,...extra}));
 for(let n=0;;n++){try{fs.renameSync(file+'.tmp',file);break;}catch(error){if(n===99)throw error;await sleep(10);}}
 const ack=await wait(role+' '+action+' '+sequence,()=>{const value=state(role);return value?.commandSequence===sequence&&value;});
 if(ack.error)throw new Error(ack.error);return ack;
}
function roomColors(s){return s.members.map(m=>({id:m.id,name:m.name,bot:m.bot,shirtColor:m.shirtColor}));}
function sameRoom(){return host()?.members.length>0&&JSON.stringify(roomColors(host()))===JSON.stringify(roomColors(guest()??{members:[]}));}
function unique(s){return s.members.every(m=>Number.isInteger(m.shirtColor)&&m.shirtColor>=0&&m.shirtColor<8)&&new Set(s.members.map(m=>m.shirtColor)).size===s.members.length;}
function shirtActors(s){return s.actors.length===s.members.length&&s.actors.every((a,i)=>a.shirtColor===s.members[i].shirtColor&&a.shirtRenderers>0&&a.shirtTint.a>.99&&JSON.stringify(a.shirtTint)===JSON.stringify(tints[a.shirtColor]));}
function remember(label){const value={utc:new Date().toISOString(),label,host:host(),guest:guest()};snapshots.push(value);return value;}
async function synchronized(label){await wait(label,()=>sameRoom()&&peers().every(unique));check(label,true,remember(label));}
async function choose(role,index){
 const ack=await command(role,'shirt-color',{value:String(index)});check(role+' accepts explicit color '+index,ack.shirtSelectionAccepted);
 await wait(role+' color acknowledged and saved',()=>state(role).selectedShirtColor===index&&state(role).savedShirtColor===index&&state(role).selectableShirtColors[index]);
 if(state(role).active)await wait('Color replicated',sameRoom);
}
async function capture(role,name){
 if(nativeUI&&!state(role).playing)await click(role,5,5,false);
 await command(role,'capture',{value:name});const file=path.join(folder,name+'.png');
 await wait('Screenshot '+name,()=>fs.existsSync(file)&&fs.statSync(file).size>1024);
 captures.push({utc:new Date().toISOString(),role,file,selectedShirtColor:state(role).selectedShirtColor,actors:state(role).actors.map(a=>({color:a.shirtColor,renderers:a.shirtRenderers,tint:a.shirtTint}))});
}
async function click(role,x,y,press=true){
 const result=await powershell(`
$ErrorActionPreference='Stop'
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class ShirtWindow {
 [StructLayout(LayoutKind.Sequential)] public struct Point { public int x,y; }
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int left,top,right,bottom; }
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr w,int c);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr w);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr w,IntPtr p);
 [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a,uint b,bool attach);
 [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr w,out Rect r);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr w,IntPtr after,int x,int y,int width,int height,uint flags);
 [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr w,ref Point p);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint data,UIntPtr extra);
}
'@
[void][ShirtWindow]::SetProcessDPIAware()
$p=Get-Process -Id ${processes[role].child.pid};$w=$p.MainWindowHandle
if($w -eq [IntPtr]::Zero){throw 'Player window is unavailable'}
[void][ShirtWindow]::ShowWindow($w,9)
[void][ShirtWindow]::SetWindowPos($w,[IntPtr](-1),0,0,0,0,0x43)
if([ShirtWindow]::GetForegroundWindow() -ne $w){$shell=New-Object -ComObject WScript.Shell;$shell.SendKeys('%');[void]$shell.AppActivate($p.Id)}
$current=[ShirtWindow]::GetCurrentThreadId();$foreground=[ShirtWindow]::GetWindowThreadProcessId([ShirtWindow]::GetForegroundWindow(),[IntPtr]::Zero)
$attached=$foreground -ne 0 -and $foreground -ne $current -and [ShirtWindow]::AttachThreadInput($current,$foreground,$true)
try{[void][ShirtWindow]::SetForegroundWindow($w)}finally{if($attached){[void][ShirtWindow]::AttachThreadInput($current,$foreground,$false)}}
Start-Sleep -Milliseconds 600
if([ShirtWindow]::GetForegroundWindow() -ne $w){throw 'Player did not gain foreground'}
$r=New-Object ShirtWindow+Rect
if(-not [ShirtWindow]::GetClientRect($w,[ref]$r)){throw 'GetClientRect failed'}
$scale=[Math]::Min(($r.right-$r.left)/1280.0,($r.bottom-$r.top)/720.0)
$point=New-Object ShirtWindow+Point;$point.x=[int](${x}*$scale);$point.y=[int](${y}*$scale)
if(-not [ShirtWindow]::ClientToScreen($w,[ref]$point)){throw 'ClientToScreen failed'}
[void][ShirtWindow]::SetCursorPos($point.x,$point.y);Start-Sleep -Milliseconds 250
$focused=[ShirtWindow]::GetForegroundWindow() -eq $w
if(-not $focused){throw 'Player lost foreground before input dispatch'}
if (${press?'$true':'$false'}) {
[ShirtWindow]::mouse_event(2,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 250
[ShirtWindow]::mouse_event(4,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 300
}
[void][ShirtWindow]::SetWindowPos($w,[IntPtr](-2),0,0,0,0,0x43)
@{pid=$p.Id;foregroundAtDispatch=$focused;foreground=([ShirtWindow]::GetForegroundWindow() -eq $w);width=($r.right-$r.left);height=($r.bottom-$r.top)}|ConvertTo-Json -Compress
`);
 nativeInput.push({utc:new Date().toISOString(),role,x,y,press,...result});check(role+' actual window receives '+(press?'native click':'foreground focus'),result.foregroundAtDispatch,result);
}
async function begin(label){
 if(!host().canStart){await command('client1','ready');await wait(label+' ready',()=>host().canStart);}
 const before=JSON.stringify(roomColors(host())),saved=host().savedShirtColor;
 const free=[0,1,2,3,4,5,6,7].find(index=>!host().members.some(m=>m.shirtColor===index));
 const ack=await command('host','start-shirt-color',{value:String(free)});
 check(label+' rejects selection during actual LoadMap operation',ack.shirtSelectionLoading&&!ack.shirtSelectionAccepted&&ack.savedShirtColor===saved);
 await wait(label+' both peers playing',()=>peers().every(s=>s?.playing&&shirtActors(s)),60000);
 check(label+' identity colors survive scene bind and local-seat reorder',sameRoom()&&JSON.stringify(roomColors(host()))===before,remember(label));
}
async function backToRoom(){await command('host','room');await wait('Both peers back in lobby',()=>peers().every(s=>!s.playing&&!s.loading));}

try{
 if(process.platform!=='win32')throw new Error('Requires actual Windows players and an interactive desktop');
 const creator=JSON.parse(fs.readFileSync(path.join(project,'TestResults/usermap-creator-clear.json'),'utf8'));
 if(creator.failed!==0||!fs.existsSync(creator.small))throw new Error('Run check-creator-clear.mjs first: this scenario reuses its actually cleared small map');
 preferenceBackup=await backupPreference();
 launch('host');await wait('Host player starts',()=>host()?.protocol>0,60000);await command('host','show-home');
 for(let index=0;index<8;index++){
  await choose('host',index);
  const actor=host().actors.find(a=>a.localPlayer);tints.push(actor.shirtTint);
  check('Offline '+index+' updates actual body renderer',actor.shirtColor===index&&actor.shirtRenderers>0&&actor.shirtTint.a>.99,actor);
 }
 check('All eight body tints are distinct',new Set(tints.map(t=>JSON.stringify(t))).size===8,tints);
 if(nativeUI){
  await click('host',361,542);await click('host',937,278);await click('host',1082,78);
 }else await choose('host',2);
 await wait('Home orange selected',()=>host().selectedShirtColor===2&&host().savedShirtColor===2);
 check((nativeUI?'Native home swatch':'Home selection')+' persists orange',host().actors.find(a=>a.localPlayer).shirtColor===2);await capture('host','shirt-home-orange');
 await command('host','training');await wait('Training has no selection controls',()=>!host().busy&&host().selectableShirtColors.every(v=>!v)&&host().actors.every(a=>!a.seated),60000);
 check('Training preserves local preference and unique actual avatars',host().actors.find(a=>a.localPlayer).shirtColor===2&&new Set(host().actors.map(a=>a.shirtColor)).size===host().actors.length&&host().actors.every(a=>a.shirtRenderers>0));
 const training=await command('host','shirt-color',{value:'4'});check('Active offline training rejects color changes',!training.shirtSelectionAccepted&&training.savedShirtColor===2);
 await capture('host','shirt-training');await command('host','show-home');await choose('host',0);
 // Clearance is local, not embedded in a shared .bellmap. Earn a fresh proof.
 const clearedMap=path.join(folder,'fresh-cleared.bellmap');
 await command('host','studio-open');await wait('Studio opens',()=>host().studioOpen&&!host().studioBusy);
 const oldDraft=host().studioDraftId;
 await command('host','studio-import',{value:creator.small});
 if(host().studioConfirmation)await command('host','studio-confirm',{value:'discard'});
 await wait('Fresh map imported',()=>!host().studioBusy&&host().studioDraftId!==oldDraft);
 await command('host','studio-test');await wait('Solo verification starts',()=>host().studioTesting&&!host().studioBusy&&host().actors.length===1&&!host().actors[0].seated);
 await command('host','move',{z:1});await wait('Walk to bell',()=>host().actors[0].position.z>3.25);
 await command('host','move',{z:0});await command('host','studio-interact');await wait('Actual solo clear succeeds',()=>host().bells===1&&host().studioCleared);
 await command('host','studio-edit');await wait('Return to studio',()=>!host().studioTesting&&!host().studioBusy);
 await command('host','studio-export',{value:clearedMap});await wait('Fresh cleared map exported',()=>fs.existsSync(clearedMap));
 await command('host','studio-close');if(host().studioConfirmation)await command('host','studio-confirm',{value:'discard'});
 await wait('Studio closes',()=>!host().studioOpen);
 launch('client1');await wait('Guest player starts',()=>guest()?.protocol>0,60000);await command('client1','show-home');await choose('client1',0);
 await command('host','host-loopback',{value:port});await wait('Host room',()=>host().members.length===1&&!host().busy);
 await command('client1','join-loopback',{value:port});await wait('Guest admitted',()=>sameRoom()&&host().members.length===2&&!guest().busy);
 check('Colliding join receives deterministic first-free without changing preference',host().selectedShirtColor===0&&guest().selectedShirtColor===1&&guest().savedShirtColor===0,remember('Join collision'));
 for(let n=0;n<4;n++)await command('host','add-bot');
 await synchronized('Host, guest and four AI have unique consistent colors');
 check('Full room reserves six colors and disables occupied swatches',peers().every(s=>s.members.length===6&&s.selectableShirtColors.every((can,index)=>can===(!s.members.some(m=>m.shirtColor===index)||s.selectedShirtColor===index))));
 const stableBots=roomColors(host()).filter(m=>m.bot),hostColor=host().selectedShirtColor;
 if(nativeUI){
  await click('client1',336,596);await click('client1',937,356);await click('client1',1082,78);
 }else await choose('client1',6);
 await wait('Guest selection acknowledged',()=>guest().selectedShirtColor===6&&guest().savedShirtColor===6&&sameRoom());
 check((nativeUI?'Native lobby swatch':'Lobby selection')+' changes only its owner',host().selectedShirtColor===hostColor&&JSON.stringify(roomColors(host()).filter(m=>m.bot))===JSON.stringify(stableBots));
 await command('client1','shirt-color',{value:'7',seat:0});await wait('Guest own color selected',()=>guest().selectedShirtColor===7&&guest().savedShirtColor===7&&sameRoom());
 check('Guest cannot target the host with another seat field',host().selectedShirtColor===hostColor);
 for(const role of ['host','client1']){
  const before=JSON.stringify(roomColors(host())),saved=state(role).savedShirtColor;
  for(const index of [-1,8,2147483647]){const ack=await command(role,'shirt-color',{value:String(index)});check(role+' rejects invalid '+index,!ack.shirtSelectionAccepted&&ack.savedShirtColor===saved&&JSON.stringify(roomColors(host()))===before);}
  const occupied=state(role).members.find(m=>m.shirtColor!==state(role).selectedShirtColor).shirtColor;
  const ack=await command(role,'shirt-color',{value:String(occupied)});check(role+' rejects an occupied color without changing preference or others',!ack.shirtSelectionAccepted&&ack.savedShirtColor===saved&&JSON.stringify(roomColors(host()))===before);
 }
 await Promise.all([command('host','shirt-color',{value:'6'}),command('client1','shirt-color',{value:'6'})]);
 await wait('Competing selection resolves',()=>sameRoom()&&peers().every(unique)&&host().members.filter(m=>m.shirtColor===6).length===1&&peers().every(s=>s.selectableShirtColors[s.selectedShirtColor]));
 // Both real players use the same Windows account/PlayerPrefs registry.
 // The winning explicit choice therefore also becomes visible to the other process.
 check('Simultaneous selection reserves one color atomically and persists the winner in the shared Windows account',peers().find(s=>s.selectedShirtColor===6).savedShirtColor===6,remember('Competing selection'));
 await capture('host','shirt-full-room-host');await capture('client1','shirt-full-room-guest');
 await command('host','map',{value:'item-race'});await wait('Race selected',()=>guest().selectedMap==='item-race');await begin('Official race');
 for(const role of ['host','client1']){
  const before=JSON.stringify(roomColors(state(role))),saved=state(role).savedShirtColor;
  const free=[0,1,2,3,4,5,6,7].find(index=>!state(role).members.some(m=>m.shirtColor===index));
  const ack=await command(role,'shirt-color',{value:String(free)});
  check(role+' in-play changes are rejected',!ack.shirtSelectionAccepted&&ack.savedShirtColor===saved&&JSON.stringify(roomColors(ack))===before&&ack.selectableShirtColors.every(v=>!v));
 }
 await command('host','five');await command('host','action',{value:'Interact'});await command('client1','action',{value:'Interact'});
 await wait('Both humans stand through actual game rules',()=>!host().actors[host().localSeat].seated&&!guest().actors[guest().localSeat].seated);
 if(nativeUI)await click('host',1097,182.5);
 await command('host','teleport',{seat:host().localSeat,position:{x:0,y:.025,z:160}});
 let display=0;
 for(let seat=0;seat<6;seat++)if(seat!==host().localSeat)await command('host','teleport',{seat,position:{x:(display++-2)*1.45,y:.025,z:164}});
 await command('host','pointer',{yaw:0,pitch:0});await capture('host','shirt-gameplay-six-avatars');
 check('Gameplay uses rendered prefab instances, not only member metadata',peers().every(shirtActors),remember('Visible actual avatars'));
 await backToRoom();await begin('Round restart');await backToRoom();
 const mapSelection=await command('host','user-map',{value:clearedMap});
 check('Cleared user map selection is accepted',mapSelection.selectedMap.startsWith('user:'),{status:mapSelection.status,playing:mapSelection.playing,loading:mapSelection.loading,busy:mapSelection.busy,selectedMap:mapSelection.selectedMap});
 await wait('Shared user map ready',()=>peers().every(s=>s.userMapReady&&s.selectedMap.startsWith('user:'))&&host().userMapHash===guest().userMapHash,60000);
 await begin('Shared user map');check('User map color binding agrees with exact loaded map',peers().every(s=>s.loadedUserMapHash===s.userMapHash)&&peers().every(shirtActors));await capture('client1','shirt-usermap');await backToRoom();
 await command('host','map',{value:'first-steps'});await wait('Official map restored',()=>guest().selectedMap==='first-steps');
 const preserved=roomColors(host()).filter(m=>m.id!==guest().members[guest().localSeat].id);
 const saved=guest().selectedShirtColor,alternate=[0,1,2,3,4,5,6,7].find(index=>!guest().members.some(m=>m.shirtColor===index));
 // Both windows share the Windows account. Make the guest's last explicit save
 // genuinely change its cached value before restarting that player.
 await choose('client1',alternate);await choose('client1',saved);
 await command('client1','leave');await wait('Guest leaves and seat frees',()=>host().members.length===5&&!guest().active);await stop('client1');
 launch('client1');await wait('Fresh guest process reads saved preference',()=>guest()?.protocol>0,60000);
 check('Explicit preference survives actual process restart',guest().savedShirtColor===saved&&guest().selectedShirtColor===saved);
 await command('client1','show-home');await command('client1','join-loopback',{value:port});await wait('Restarted guest rejoins final seat',()=>sameRoom()&&host().members.length===6&&guest().localSeat===5);
 check('Rejoin preserves human preference and every existing bot identity',guest().selectedShirtColor===saved&&JSON.stringify(roomColors(host()).slice(0,5))===JSON.stringify(preserved));
 const removed=host().members.findIndex(m=>m.bot),released=host().members[removed].shirtColor;
 await command('host','remove-bot',{seat:removed});await wait('AI removal replicates',()=>sameRoom()&&host().members.length===5);
 const used=new Set(host().members.map(m=>m.shirtColor)),expected=[0,1,2,3,4,5,6,7].find(c=>!used.has(c));
 await command('host','add-bot');await wait('AI addition replicates',()=>sameRoom()&&host().members.length===6);
 check('New AI takes a deterministic free color after removal',host().members.at(-1).bot&&host().members.at(-1).shirtColor===expected&&unique(host()),{released,expected});
 await begin('Rejoined and reordered seats');check('Local and remote instantiated bodies keep identities after reorder',peers().every(shirtActors)&&guest().actors[guest().localSeat].localPlayer);await capture('client1','shirt-rejoined-gameplay');
 check('No probe execution errors',peers().every(s=>!s.error));
}catch(error){
 if(checks.at(-1)?.passed!==false)checks.push({name:String(error),passed:false});
 fs.writeFileSync(path.join(folder,'failure.json'),JSON.stringify({utc:new Date().toISOString(),error:error.stack,host:host(),guest:guest()},null,2));console.error(error);process.exitCode=1;
}finally{
 try{for(const role of Object.keys(processes))await stop(role);await restorePreference();}
 catch(error){checks.push({name:'Preference cleanup: '+String(error),passed:false});console.error(error);process.exitCode=1;}
 const result={utc:new Date().toISOString(),folder,transport:'loopback',protocol:host()?.protocol,preferenceKey,preferenceRestored:restored,passed:checks.filter(c=>c.passed).length,failed:checks.filter(c=>!c.passed).length,checks,captures,nativeInput,commands,snapshots};
 fs.mkdirSync(path.join(project,'TestResults'),{recursive:true});const report=path.join(project,'TestResults/shirt-colors.json');fs.writeFileSync(report,JSON.stringify(result,null,2));console.log(JSON.stringify({report,passed:result.passed,failed:result.failed,folder,preferenceRestored:restored}));
}
