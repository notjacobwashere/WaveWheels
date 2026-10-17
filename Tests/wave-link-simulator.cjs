const fs = require('node:fs');
const { WebSocketServer } = require('./test-websocket.cjs');
const legacy = process.argv.includes('--legacy');
const passive = process.argv.includes('--passive');
const portArg = process.argv.find(a=>a.startsWith('--port='));
const wss = new WebSocketServer({host:'127.0.0.1',port:portArg?Number(portArg.split('=')[1]):0});
wss.on('listening',()=>console.log(JSON.stringify({port:wss.address().port,legacy,passive})));
const fresh = ()=>[
 {id:'music',name:'Music',level:.65,isMuted:false,mixes:[{mixId:'personal',level:.4,isMuted:false},{mixId:'stream',level:.8,isMuted:false}]},
 {id:'game',name:'Game',level:.9,isMuted:false,mixes:[{mixId:'personal',level:.7,isMuted:false},{mixId:'stream',level:.6,isMuted:false}]}
];
let connections=0;
const toLegacy = c=>({mixId:c.id,mixerName:c.name,isAvailable:true,localVolumeIn:c.mixes[0].level*100,streamVolumeIn:c.mixes[1].level*100,isLocalInMuted:c.mixes[0].isMuted,isStreamInMuted:c.mixes[1].isMuted,filters:[],localMixFilterBypass:false,streamMixFilterBypass:false});
wss.on('connection',ws=>{
 connections++;let channels=fresh(),sequenced=false;
 const send=o=>{if(ws.readyState===1)ws.send(JSON.stringify(o));};
 const notify=(method,params)=>send({jsonrpc:'2.0',method,params});
 ws.on('message',buffer=>{
  const request=JSON.parse(buffer);const {id,method,params}=request;let result=null;
  if(method==='getApplicationInfo')result={appID:'EWL',name:'Elgato Wave Link',isTestServer:true};
  else if(method==='getChannels'&&!legacy)result={channels};
  else if(method==='getMixes'&&!legacy)result={mixes:[{id:'personal',name:'Headphones'},{id:'stream',name:'Stream mix'}]};
  else if(method==='getAllChannelInfo'&&legacy)result=channels.map(toLegacy);
  else if(method==='setChannel'&&!legacy){
   const c=channels.find(c=>c.id===params.id);if(!c)throw Error('Unknown channel');
   for(const field of ['level','isMuted'])if(params[field]!==undefined)c[field]=params[field];
   for(const patch of params.mixes||[]){const mix=c.mixes.find(m=>m.mixId===patch.mixId);Object.assign(mix,patch);}
   notify('channelChanged',params);result=c;
  }else if(method==='setInputMixer'&&legacy){
   const c=channels.find(c=>c.id===params.mixId);const m=c.mixes[params.slider==='local'?0:1];
   if(params.slider==='local'){m.level=params.localVolumeIn/100;m.isMuted=params.isLocalInMuted;}else{m.level=params.streamVolumeIn/100;m.isMuted=params.isStreamInMuted;}
   result=toLegacy(c);notify('inputMixerChanged',result);
  }else {send({jsonrpc:'2.0',id,error:{code:-32601,message:'Method not found'}});return;}
  send({jsonrpc:'2.0',id,result});
  if(method.startsWith('set'))console.log(JSON.stringify({method,params}));
  const muted=channels[0]?.mixes[0]?.isMuted;
  if(muted&&!sequenced&&!passive){sequenced=true;
   setTimeout(()=>{const c=channels[0];c.mixes[0].level=.27;c.mixes[0].isMuted=false;notify(legacy?'inputMixerChanged':'channelChanged',legacy?toLegacy(c):{id:'music',mixes:[{mixId:'personal',level:.27,isMuted:false}]});},220);
   setTimeout(()=>{channels=channels.filter(c=>c.id!=='music');notify('channelsChanged',{channels:legacy?channels.map(toLegacy):channels});},550);
   setTimeout(()=>ws.close(),900);
  }
 });
});
