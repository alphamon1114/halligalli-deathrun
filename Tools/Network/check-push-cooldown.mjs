import fs from 'node:fs';
import path from 'node:path';
import {spawn,execFile} from 'node:child_process';
import {promisify} from 'node:util';

const project=path.resolve(process.argv[2]??'.'),width=Number(process.argv[3]??1280),height=width===1280?720:900;
const folder=path.join(project,'Tools/Network/Runs','push-cooldown-'+Date.now());
fs.mkdirSync(folder,{recursive:true});
const checks=[],processes={},sequences={},cached={},commands=[],samples=[],captures=[],nativeFocus=[];
const sleep=ms=>new Promise(resolve=>setTimeout(resolve,ms)),execFileAsync=promisify(execFile);
let processError,stopping=false;
function state(role){try{cached[role]=JSON.parse(fs.readFileSync(path.join(folder,'state-'+role+'.json'),'utf8'));}catch{}return cached[role];}
const host=()=>state('host'),guest=()=>state('client1'),roleFor=seat=>seat===0?'host':'client1';
function check(name,passed,detail=''){checks.push({name,passed,detail});if(!passed)throw new Error(name);}
async function wait(name,predicate,timeout=15000){
 const end=Date.now()+timeout;
 while(Date.now()<end){if(processError)throw processError;const value=predicate();if(value)return value;await sleep(20);}
 throw new Error('Scenario state timeout: '+name);
}
async function command(role,action,extra={}){
 const file=path.join(folder,'command-'+role+'.json'),sequence=sequences[role]=(sequences[role]??0)+1;
 const entry={role,sequence,action,...extra,sentUtc:new Date().toISOString()};commands.push(entry);
 fs.writeFileSync(file+'.tmp',JSON.stringify({sequence,action,...extra}));
 for(let i=0;;i++){try{fs.renameSync(file+'.tmp',file);break;}catch(error){if(i===99)throw error;await sleep(10);}}
 const ack=await wait(role+' command '+sequence+' '+action,()=>{const s=state(role);return s?.commandSequence===sequence&&s;});
 entry.acknowledgedUtc=new Date().toISOString();entry.worldTime=ack.worldTime;
 if(ack.error)throw new Error(ack.error);
 return ack;
}
function launch(role){
 const child=spawn(path.join(project,'Build/Windows/HalliGalliMovement.exe'),['-screen-fullscreen','0','-screen-width',String(width),'-screen-height',String(height),'--net-probe',role,'--probe-dir',folder,'--net-port','17895','-logFile',path.join(folder,role+'.log')],{stdio:'ignore'});
 processes[role]=child;
 child.on('error',error=>{processError=error;});
 child.on('exit',(code,signal)=>{if(!stopping)processError=new Error(role+' player exited: '+code+' '+signal);});
}
function sample(seat,label){
 const view=s=>({worldTime:s.worldTime,commandSequence:s.commandSequence,...s.actors[seat]});
 const result={utc:new Date().toISOString(),seat,label,host:view(host()),guest:view(guest())};
 const previous=samples.at(-1);
 if(!previous||previous.seat!==seat||previous.label!==label||previous.host.worldTime!==result.host.worldTime||previous.guest.worldTime!==result.guest.worldTime)samples.push(result);
 return result;
}
const peers=s=>[s.host,s.guest],deadline=a=>a.worldTime+a.kickCooldown;
const sameDeadline=(s,expected,tolerance=.24)=>peers(s).every(a=>a.kickCooldown===0||Math.abs(deadline(a)-expected)<=tolerance);
async function fresh(seat,label){
 const before=sample(seat,label);
 // CommandSequence acknowledges dispatch, not remote execution. Also observe
 // several 50ms authority snapshots before judging a rejected network action.
 return wait(label,()=>{const s=sample(seat,label);return s.host.worldTime>=before.host.worldTime+.12&&s.guest.worldTime>=before.guest.worldTime+.12&&s;});
}
async function ready(seat){
 await command('host','move',{yaw:0});await command('client1','move',{yaw:0});
 await wait('Both players idle and off cooldown',()=>[host(),guest()].every(s=>s.actors.every(a=>a.attack==='None'&&a.kickCooldown===0&&!a.respawning)));
 // Separate actors so contact protection, item boxes and knockback cannot mask
 // cooldown behavior. These are real motors on the existing flat race course.
 for(const actor of [0,1])await command('host','teleport',{seat:actor,position:{x:actor===0?-3:3,y:.025,z:160}});
 await command(roleFor(seat),'pointer',{yaw:0,pitch:0});
 await wait('Both actors settled on the race floor',()=>[host(),guest()].every(s=>s.actors.every((a,i)=>a.grounded&&Math.abs(a.position.x-(i===0?-3:3))<.05&&Math.abs(a.position.z-160)<.05&&Math.hypot(a.velocity.x,a.velocity.z)<.03)));
}
async function resumeForCapture(role){
 // The probe intentionally disables live combat/controller input, but the real
 // pause-menu Resume button still locks the cursor and reveals DrawTools.
 // Never add a probe-only cursor/HUD bypass. This helper needs an interactive
 // Windows desktop and targets only the player process created by this script.
 const script=`
$ErrorActionPreference='Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class PushCaptureWindow {
 [StructLayout(LayoutKind.Sequential)] public struct Point { public int x,y; }
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int left,top,right,bottom; }
 [StructLayout(LayoutKind.Sequential)] public struct CursorInfo { public int size,flags; public IntPtr cursor; public Point position; }
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window,int command);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window,IntPtr process);
 [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint first,uint second,bool attach);
 [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr window,out Rect rect);
 [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr window,ref Point point);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
 [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CursorInfo info);
}
'@
[void][PushCaptureWindow]::SetProcessDPIAware()
$player=Get-Process -Id ${processes[role].pid}
$window=$player.MainWindowHandle
if($window -eq [IntPtr]::Zero){throw 'Player has no graphical window'}
[void][PushCaptureWindow]::ShowWindow($window,9)
$shell=New-Object -ComObject WScript.Shell
$shell.SendKeys('%')
[void]$shell.AppActivate($player.Id)
$current=[PushCaptureWindow]::GetCurrentThreadId()
$foreground=[PushCaptureWindow]::GetWindowThreadProcessId([PushCaptureWindow]::GetForegroundWindow(),[IntPtr]::Zero)
$attached=$foreground -ne 0 -and $foreground -ne $current -and [PushCaptureWindow]::AttachThreadInput($current,$foreground,$true)
try {[void][PushCaptureWindow]::SetForegroundWindow($window)} finally {if($attached){[void][PushCaptureWindow]::AttachThreadInput($current,$foreground,$false)}}
Start-Sleep -Milliseconds 250
if([PushCaptureWindow]::GetForegroundWindow() -ne $window){throw 'Could not foreground the player for HUD capture'}
$rect=New-Object PushCaptureWindow+Rect
if(-not [PushCaptureWindow]::GetClientRect($window,[ref]$rect)){throw 'GetClientRect failed'}
$w=$rect.right-$rect.left;$h=$rect.bottom-$rect.top
$scale=[Math]::Min($w/1280.0,$h/720.0)
# RoomPanel.x + 24 + (318-48)/2; Resume.y = 88+22+51+43/2.
$point=New-Object PushCaptureWindow+Point
$point.x=[int]($w-183*$scale);$point.y=[int](182.5*$scale)
if(-not [PushCaptureWindow]::ClientToScreen($window,[ref]$point)){throw 'ClientToScreen failed'}
[void][PushCaptureWindow]::SetCursorPos($point.x,$point.y)
Start-Sleep -Milliseconds 100
[PushCaptureWindow]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
Start-Sleep -Milliseconds 80
[PushCaptureWindow]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
Start-Sleep -Milliseconds 250
$cursor=New-Object PushCaptureWindow+CursorInfo
$cursor.size=[Runtime.InteropServices.Marshal]::SizeOf($cursor)
if(-not [PushCaptureWindow]::GetCursorInfo([ref]$cursor)){throw 'GetCursorInfo failed'}
if([PushCaptureWindow]::GetForegroundWindow() -ne $window -or ($cursor.flags -band 1) -ne 0){throw 'Resume did not capture the cursor; HUD screenshot would show the pause menu'}
@{pid=$player.Id;width=$w;height=$h;foreground=$true;cursorHidden=$true}|ConvertTo-Json -Compress
`;
 const {stdout}=await execFileAsync('powershell.exe',['-NoProfile','-NonInteractive','-EncodedCommand',Buffer.from(script,'utf16le').toString('base64')],{windowsHide:true,timeout:15000});
 const result={role,...JSON.parse(stdout.trim())};nativeFocus.push(result);
 check(role+' foreground Resume captures the cursor for real HUD screenshots',result.foreground&&result.cursorHidden,result);
}
async function capture(seat,phase){
 const role=roleFor(seat),name='push-hud-'+role+'-'+phase,before=sample(seat,'capture '+phase);
 const ack=await command(role,'capture',{value:name}),file=path.join(folder,name+'.png');
 await wait('PNG '+name,()=>fs.existsSync(file)&&fs.statSync(file).size>1024);
 const local=ack.actors[seat],entry={role,phase,file,worldTime:ack.worldTime,kickCooldown:local.kickCooldown,attack:local.attack,before};captures.push(entry);
 check(role+' '+phase+' HUD screenshot captured in the requested cooldown state',phase==='ready'?local.kickCooldown===0:local.kickCooldown>.5,entry);
}
async function beginKick(seat,label){
 const before=sample(seat,label+' before'),sequence=before.host.attacks+1;
 check(label+' starts from ready on both peers',peers(before).every(a=>a.attack==='None'&&a.kickCooldown===0),before);
 await command(roleFor(seat),'pointer',{yaw:0,pitch:0,kick:true});
 const start=await wait(label+' authoritative start replicated',()=>{const s=sample(seat,label+' start');return peers(s).every(a=>a.attacks===sequence)&&s;});
 check(label+' accepted F immediately starts approximately two seconds',peers(start).every(a=>a.attack==='Kick'&&a.kickCooldown>1.5&&a.kickCooldown<=2.02)&&Math.abs(start.host.attackElapsed+start.host.kickCooldown-2)<.12,start);
 check(label+' host and guest agree on the cooldown deadline',Math.abs(deadline(start.host)-deadline(start.guest))<.24,start);
 return {seat,label,sequence,start,deadline:deadline(start.host)};
}
async function recovered(cycle){
 const s=await wait(cycle.label+' recovery',()=>{const s=sample(cycle.seat,cycle.label+' recovery');return peers(s).every(a=>a.attack==='None')&&s;});
 check(cycle.label+' recovery leaves a substantial cooldown',peers(s).every(a=>a.attacks===cycle.sequence&&a.kickCooldown>.45),s);
 check(cycle.label+' host and guest countdown advance without resetting',cycle.start.host.kickCooldown-s.host.kickCooldown>.7&&cycle.start.guest.kickCooldown-s.guest.kickCooldown>.7&&sameDeadline(s,cycle.deadline),s);
 return s;
}
async function rejectKick(cycle,label){
 const before=sample(cycle.seat,label+' before');
 check(label+' is issued while cooldown is active',peers(before).every(a=>a.kickCooldown>.2),before);
 // Route the actual network action even on the predicting guest, so a local
 // cooldown prefilter alone cannot make the authority assertions pass.
 await command(roleFor(cycle.seat),'action',{value:'Kick'});
 const after=await fresh(cycle.seat,label+' after');
 check(label+' cannot start another attack on either peer',peers(after).every(a=>a.attacks===cycle.sequence),after);
 check(label+' cannot extend or restart the original cooldown',sameDeadline(after,cycle.deadline)&&after.host.kickCooldown<before.host.kickCooldown,after);
}
async function expires(cycle){
 const trace=[];
 const end=await wait(cycle.label+' cooldown expiry',()=>{const s=sample(cycle.seat,cycle.label+' expiry');trace.push(s);return peers(s).every(a=>a.kickCooldown===0)&&s;},5000);
 check(cycle.label+' expires at the original deadline without a queued retry',trace.every(s=>sameDeadline(s,cycle.deadline)&&peers(s).every(a=>a.attacks===cycle.sequence))&&Math.abs(end.host.worldTime-cycle.deadline)<.3&&Math.abs(end.guest.worldTime-cycle.deadline)<.35,end);
 return end;
}

try{
 if(process.platform!=='win32')throw new Error('This scenario requires actual Windows players and an interactive desktop');
 launch('host');await wait('Host room',()=>host()?.active&&!host().busy,60000);
 launch('client1');await wait('Guest joined',()=>host().members.length===2&&guest()?.members.length===2&&!guest().busy,60000);
 await command('host','map',{value:'item-race'});await command('client1','ready');await wait('Room ready',()=>host().canStart);
 await command('host','start');await wait('Race loaded on both peers',()=>host().playing&&guest().playing&&host().map==='item-race'&&guest().map==='item-race',60000);
 check('Both peers expose finite kick cooldown seconds',[host(),guest()].every(s=>s.actors.length===2&&s.actors.every(a=>Number.isFinite(a.kickCooldown)&&a.kickCooldown===0)));
 const seated=host().actors.map(a=>a.attacks);
 for(const role of ['host','client1'])await command(role,'action',{value:'Kick'});
 await fresh(0,'seated rejection');
 check('Rejected seated F consumes no cooldown or attack sequence',[host(),guest()].every(s=>s.actors.every((a,i)=>a.seated&&a.attacks===seated[i]&&a.kickCooldown===0)));
 await command('host','five');await command('host','action',{value:'Interact'});await command('client1','action',{value:'Interact'});
 await wait('Both players standing',()=>[host(),guest()].every(s=>s.actors.every(a=>!a.seated)));
 for(const seat of [0,1]){
  const role=roleFor(seat);await ready(seat);await resumeForCapture(role);await capture(seat,'ready');
  const repeated=await beginKick(seat,role+' repeated F');await capture(seat,'cooldown');await recovered(repeated);
  for(let attempt=1;attempt<=2;attempt++)await rejectKick(repeated,role+' recovered F rejection '+attempt);
  await expires(repeated);
  const quick=await beginKick(seat,role+' post-expiry F');await recovered(quick);
  const beforeQuick=sample(seat,role+' quick during cooldown before');
  await command(role,'pointer',{yaw:0,pitch:0,primary:true});
  const afterQuick=await wait(role+' quick push replicated during cooldown',()=>{const s=sample(seat,role+' quick during cooldown');return peers(s).every(a=>a.attacks===quick.sequence+1)&&s;});
  check(role+' LMB remains usable after recovery while F is cooling down',peers(afterQuick).every(a=>a.attack==='PushLeft'||a.attack==='PushRight')&&peers(beforeQuick).every(a=>a.kickCooldown>.4)&&peers(afterQuick).every(a=>a.kickCooldown>0),afterQuick);
  check(role+' quick push does not restart the F cooldown',sameDeadline(afterQuick,quick.deadline),afterQuick);
  await expires({...quick,sequence:quick.sequence+1});
  await wait(role+' quick recovery',()=>[host(),guest()].every(s=>s.actors[seat].attack==='None'));
  const respawn=await beginKick(seat,role+' respawn preservation'),life=host().actors[seat].respawns;
  await command(role,'action',{value:'Respawn'});
  const falling=await wait(role+' respawn begins',()=>{const s=sample(seat,role+' respawning');return peers(s).every(a=>a.respawning)&&s;});
  check(role+' canceling the kick for respawn preserves cooldown',peers(falling).every(a=>a.attack==='None'&&a.kickCooldown>0)&&sameDeadline(falling,respawn.deadline),falling);
  await rejectKick(respawn,role+' respawning F rejection');
  const returned=await wait(role+' respawn completed',()=>{const s=sample(seat,role+' returned');return peers(s).every(a=>a.respawns>life&&!a.respawning)&&s;});
  check(role+' respawn completion cannot clear the remaining cooldown',peers(returned).every(a=>a.kickCooldown>.45&&a.attacks===respawn.sequence)&&sameDeadline(returned,respawn.deadline),returned);
  await rejectKick(respawn,role+' returned F rejection');await expires(respawn);
  const afterRespawn=await beginKick(seat,role+' F after respawn cooldown expiry');await expires(afterRespawn);
 }
}catch(error){
 if(checks.at(-1)?.passed!==false)checks.push({name:String(error),passed:false});
 fs.writeFileSync(path.join(folder,'failure.json'),JSON.stringify({utc:new Date().toISOString(),error:error.stack,host:host(),guest:guest()},null,2));
 console.error(error);process.exitCode=1;
}finally{
 stopping=true;for(const child of Object.values(processes))child.kill();
 fs.mkdirSync(path.join(project,'TestResults'),{recursive:true});
 const report=path.join(project,'TestResults/push-cooldown.json');
 const result={utc:new Date().toISOString(),folder,width,height,passed:checks.filter(c=>c.passed).length,failed:checks.filter(c=>!c.passed).length,checks,captures,nativeFocus,commands,samples};
 fs.writeFileSync(report,JSON.stringify(result,null,2));
 console.log(JSON.stringify({report,passed:result.passed,failed:result.failed,folder}));
}
