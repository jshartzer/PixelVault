const {test}=require('node:test');
const assert=require('node:assert/strict');
const {create}=require('../src/PixelVault.Companion/wwwroot/refresh-stream.js');
class Source{
  static latest;
  constructor(url){this.url=url;Source.latest=this;}
  addEventListener(name,fn){this.receive=fn;}
  send(sequence,epoch='run'){this.receive({data:JSON.stringify({schema:1,epoch,sequence,workspaceId:'w',versions:{trackers:String(sequence)},connection:{}})});}
  close(){this.closed=true;}
}
test('push coalesces blocked updates, retries failures, deduplicates and handles a restarted service',async()=>{
  let busy=true,fail=false;const applied=[];
  const client=create({workspace:'w',EventSourceType:Source,retryMs:100000,onUpdate:async value=>{
    if(busy)return false;if(fail){fail=false;throw Error('offline');}applied.push(value.id);
  }});
  try{
    const source=Source.latest;assert.match(source.url,/workspace=w/);source.onopen();assert.equal(client.connected,true);
    source.send(1);await client.flush();source.send(2);await new Promise(setImmediate);
    busy=false;fail=true;await client.flush();assert.deepEqual(applied,[]);await client.flush();assert.deepEqual(applied,['run:2']);
    source.send(2);await client.flush();assert.equal(applied.length,1);
    source.onerror();assert.equal(client.connected,false);source.onopen();source.send(1,'restart');await new Promise(setImmediate);
    assert.deepEqual(applied,['run:2','restart:1']);
    client.close();source.send(3);await client.flush();assert.equal(applied.length,2);assert.equal(source.closed,true);
  }finally{client.close();}
});
