import fs from 'node:fs';
import path from 'node:path';
import {spawn} from 'node:child_process';
import {createHash} from 'node:crypto';
import {pathToFileURL} from 'node:url';

const rate=48000,channels=2,targetLufs=-18,crossfadeSeconds=2;
const tracks={
 'first-steps':'BGM_FirstSteps.mp3',summit:'BGM_Summit.mp3',surf:'BGM_SurfCanal.mp3',
 factory:'BGM_Factory.mp3',pyramid:'BGM_KingsCorridor.mp3','item-race':'BGM_ItemRace.mp3',clockwork:'BGM_ClockworkFortress.mp3'
};
const hash=file=>createHash('sha256').update(fs.readFileSync(file)).digest('hex');
async function execute(exe,args){
 return await new Promise((resolve,reject)=>{
  const p=spawn(exe,['-hide_banner','-nostdin','-y',...args],{stdio:['ignore','pipe','pipe']});
  const output=[],errors=[];p.stdout.on('data',x=>output.push(x));p.stderr.on('data',x=>errors.push(x));p.on('error',reject);
  p.on('close',code=>{const log=Buffer.concat(errors).toString();if(code!==0)reject(new Error(log));else resolve({pcm:Buffer.concat(output),log});});
 });
}
function levels(log){
 const summary=log.slice(log.lastIndexOf('Summary:'));
 const integrated=Number(summary.match(/I:\s+(-?[\d.]+) LUFS/)?.[1]);
 const truePeak=Number(summary.match(/Peak:\s+(-?[\d.]+) dBFS/)?.[1]);
 if(!Number.isFinite(integrated)||!Number.isFinite(truePeak))throw new Error('Missing EBU R128 measurements: '+summary);
 return {integratedLufs:integrated,truePeakDbfs:truePeak};
}
function rms(data,start,end){let sum=0;for(let i=start;i<end;i++)sum+=data[i]*data[i];return Math.sqrt(sum/Math.max(1,end-start));}
function floats(buffer){return new Float32Array(buffer.buffer,buffer.byteOffset,buffer.byteLength/4);}

export async function prepareMapMusic({root,ffmpeg,source}){
 const output=path.join(root,'Assets/HalliGalliMovement/Resources/MapMusic');
 const temporary=path.join(root,'Build/AudioVerification');
 fs.mkdirSync(output,{recursive:true});fs.mkdirSync(temporary,{recursive:true});
 const audit=JSON.parse(fs.readFileSync(path.join(root,'TestResults/map-bgm-audit.json'),'utf8'));
 const results=[];
 for(const [id,name] of Object.entries(tracks)){
  const original=path.join(source,name),originalHash=hash(original);
  if(audit.tracks.find(t=>t.file===name)?.sha256!==originalHash)throw new Error('Selected source changed: '+name);
  const decoded=await execute(ffmpeg,['-v','error','-i',original,'-ar',String(rate),'-ac','2','-f','f32le','pipe:1']);
  const input=floats(decoded.pcm),block=rate*channels/10,threshold=10**(-35/20);
  let first=0,last=input.length;
  // Remove low-level intro/outro edges; preserve every interior musical passage.
  while(first+block<last&&rms(input,first,first+block)<threshold)first+=block;
  while(last-block>first&&rms(input,last-block,last)<threshold)last-=block;
  // A short opening sting can precede a quiet intro. Start at sustained material
  // so rotating by the crossfade does not expose that gap at the loop boundary.
  const sustained=rate*channels*3,minimum=10**(-40/20);
  while(first+sustained<last){
   let continuous=true;
   for(let j=first;j<first+sustained;j+=block)if(rms(input,j,j+block)<minimum){continuous=false;break;}
   if(continuous)break;
   first+=block;
  }
  const overlap=rate*channels*crossfadeSeconds,n=last-first;
  if(n<overlap*4)throw new Error('Not enough material for a loop: '+name);
  const loop=new Float32Array(n-overlap),body=n-2*overlap;
  loop.set(input.subarray(first+overlap,last-overlap));
  // Body -> original tail blended into original head -> body. The file boundary
  // lands on consecutive head samples, not on an unrelated end/start splice.
  for(let frame=0;frame<overlap/channels;frame++){
   const t=frame/(overlap/channels-1),a=Math.cos(t*Math.PI/2),b=Math.sin(t*Math.PI/2);
   for(let channel=0;channel<channels;channel++){
    const j=frame*channels+channel;
    loop[body+j]=input[last-overlap+j]*a+input[first+j]*b;
   }
  }
  const raw=path.join(temporary,id+'.f32'),destination=path.join(output,id+'.wav');
  fs.writeFileSync(raw,Buffer.from(loop.buffer));
  try{
   const inputArgs=['-f','f32le','-ar',String(rate),'-ac','2','-i',raw];
   const before=levels((await execute(ffmpeg,[...inputArgs,'-af','ebur128=peak=true','-f','null','-'])).log);
   const gainDb=targetLufs-before.integratedLufs;
   if(before.truePeakDbfs+gainDb>-1)throw new Error('Normalization needs a limiter; do not silently clip '+name);
   await execute(ffmpeg,[...inputArgs,'-af','volume='+gainDb.toFixed(6)+'dB','-c:a','pcm_s16le',destination]);
   const measured=await execute(ffmpeg,['-i',destination,'-af','ebur128=peak=true','-f','f32le','pipe:1']);
   const after=levels(measured.log),pcm=floats(measured.pcm);
   const endpointStep=Math.max(Math.abs(pcm[0]-pcm[pcm.length-2]),Math.abs(pcm[1]-pcm[pcm.length-1]));
   let localMaxStep=0,edgeMinRms=Infinity;
   for(const [begin,end] of [[0,rate*channels],[pcm.length-rate*channels,pcm.length]]){
    for(let j=begin+channels;j<end;j++)localMaxStep=Math.max(localMaxStep,Math.abs(pcm[j]-pcm[j-channels]));
    for(let j=begin;j+block<=end;j+=block)edgeMinRms=Math.min(edgeMinRms,rms(pcm,j,j+block));
   }
   if(Math.abs(after.integratedLufs-targetLufs)>.15||after.truePeakDbfs>-1||edgeMinRms<10**(-50/20)||endpointStep>Math.max(.001,localMaxStep))throw new Error('Rendered loop failed loudness/seam checks: '+name);
   if(hash(original)!==originalHash)throw new Error('Original changed: '+name);
   const result={id,original:name,originalSha256:originalHash,outputSha256:hash(destination),sourceSeconds:input.length/rate/channels,trimStartSeconds:first/rate/channels,trimEndSeconds:(input.length-last)/rate/channels,crossfadeSeconds,durationSeconds:pcm.length/rate/channels,gainDb,...after,endpointStep,localMaxStep,edgeMinimum100msDbfs:20*Math.log10(edgeMinRms),passed:true};
   results.push(result);console.log(JSON.stringify(result));
  }finally{fs.rmSync(raw,{force:true});}
 }
 const report={utc:new Date().toISOString(),targetLufs,sampleRate:rate,channels,format:'PCM16 WAV; preserve loop sample count in Unity importer',crossfade:'2 seconds equal-power tail/head overlap; file rotated to a contiguous sample boundary',originalsPreserved:true,subjectiveListeningPerformed:false,passed:results.length,failed:0,tracks:results};
 fs.writeFileSync(path.join(root,'TestResults/map-music-preparation.json'),JSON.stringify(report,null,2)+'\n');
 return report;
}

if(process.argv[1]&&import.meta.url===pathToFileURL(path.resolve(process.argv[1])).href){
 const [root,ffmpeg,source]=process.argv.slice(2);
 if(!root||!ffmpeg||!source)throw new Error('Usage: node Tools/Audio/prepare-map-music.mjs <project> <ffmpeg.exe> <selected-mp3-directory>');
 await prepareMapMusic({root:path.resolve(root),ffmpeg:path.resolve(ffmpeg),source:path.resolve(source)});
}
