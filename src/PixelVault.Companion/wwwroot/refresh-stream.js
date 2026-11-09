(function(root){
  'use strict';
  function create({workspace,onUpdate,EventSourceType=root.EventSource,retryMs=500}){
    let source,queued,running=false,closed=false,lastApplied='',connected=false;
    async function flush(){
      if(closed||running||!queued)return;
      running=true;const next=queued;
      try{
        if(await onUpdate(next)!==false){lastApplied=next.id;if(queued===next)queued=null;}
      }catch{/* Keep the newest snapshot pending after a failed read. */}
      finally{running=false;}
    }
    if(EventSourceType){
      source=new EventSourceType('/api/v1/refresh/events?workspace='+encodeURIComponent(workspace));
      source.onopen=()=>{connected=true;};
      source.onerror=()=>{connected=false;};
      source.addEventListener('refresh',event=>{
        try{
          const snapshot=JSON.parse(event.data);
          if(snapshot.schema!==1||!snapshot.epoch||!snapshot.versions||!snapshot.connection)return;
          snapshot.id=snapshot.epoch+':'+snapshot.sequence;
          if(snapshot.id===lastApplied)return;
          // Every notice is a complete version snapshot. Coalescing cannot lose an invalidation.
          queued=snapshot;void flush();
        }catch{/* An incomplete event is recovered by reconnect/fallback. */}
      });
    }
    const timer=setInterval(flush,retryMs);
    return {get connected(){return connected;},flush,close(){closed=true;connected=false;queued=null;source?.close();clearInterval(timer);}};
  }
  root.PixelVaultRefreshStream={create};
  if(typeof module!=='undefined')module.exports={create};
})(typeof globalThis!=='undefined'?globalThis:this);
