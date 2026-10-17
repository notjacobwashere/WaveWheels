// Small loopback-only WebSocket test server; no packages or external services.
const http=require('node:http'),crypto=require('node:crypto'),{EventEmitter}=require('node:events');
class Peer extends EventEmitter{
 constructor(socket,head){super();this.socket=socket;this.readyState=1;this.buffer=Buffer.alloc(0);this.parts=[];socket.on('data',b=>this.feed(b));socket.on('error',()=>this.close());socket.on('close',()=>{this.readyState=3;this.emit('close');});if(head?.length)setImmediate(()=>this.feed(head));}
 frame(op,payload){const body=Buffer.isBuffer(payload)?payload:Buffer.from(payload);let header;if(body.length<126){header=Buffer.from([0x80|op,body.length]);}else if(body.length<65536){header=Buffer.alloc(4);header[0]=0x80|op;header[1]=126;header.writeUInt16BE(body.length,2);}else{header=Buffer.alloc(10);header[0]=0x80|op;header[1]=127;header.writeBigUInt64BE(BigInt(body.length),2);}if(this.readyState===1)this.socket.write(Buffer.concat([header,body]));}
 send(value){this.frame(1,value);}
 close(){if(this.readyState!==1)return;this.frame(8,Buffer.alloc(0));this.readyState=2;this.socket.end();}
 feed(bytes){this.buffer=Buffer.concat([this.buffer,bytes]);while(this.buffer.length>=2){const b=this.buffer,opcode=b[0]&15,fin=!!(b[0]&128),masked=!!(b[1]&128);let length=b[1]&127,offset=2;if(length===126){if(b.length<4)return;length=b.readUInt16BE(2);offset=4;}else if(length===127){if(b.length<10)return;length=Number(b.readBigUInt64BE(2));offset=10;}if(length>8*1024*1024)throw Error('Oversized test frame');const maskBytes=masked?4:0;if(b.length<offset+maskBytes+length)return;const mask=b.subarray(offset,offset+maskBytes);offset+=maskBytes;const payload=Buffer.from(b.subarray(offset,offset+length));if(masked)for(let i=0;i<payload.length;i++)payload[i]^=mask[i%4];this.buffer=b.subarray(offset+length);if(opcode===8){this.close();return;}if(opcode===9){this.frame(10,payload);continue;}if(opcode===10)continue;this.parts.push(payload);if(fin){this.emit('message',Buffer.concat(this.parts));this.parts=[];}}}
}
class WebSocketServer extends EventEmitter{
 constructor(options){super();this.server=http.createServer((q,r)=>{r.writeHead(404);r.end();});this.server.on('upgrade',(request,socket,head)=>{const key=request.headers['sec-websocket-key'];if(!key){socket.destroy();return;}const accept=crypto.createHash('sha1').update(key+'258EAFA5-E914-47DA-95CA-C5AB0DC85B11').digest('base64');socket.write('HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: '+accept+'\r\n\r\n');this.emit('connection',new Peer(socket,head));});this.server.listen(options.port,options.host,()=>this.emit('listening'));}
 address(){return this.server.address();}
}
module.exports={WebSocketServer};
