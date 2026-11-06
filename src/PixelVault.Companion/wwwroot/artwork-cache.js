(function(root){
  'use strict';
  let database;
  const pending=new Set();
  function open(){
    return database??=new Promise(resolve=>{
      try{const request=indexedDB.open('PixelVault.Companion.artwork',1);
        request.onupgradeneeded=()=>{const store=request.result.createObjectStore('images',{keyPath:'key'});store.createIndex('updated','updated');};
        request.onsuccess=()=>resolve(request.result);request.onerror=()=>resolve(null);request.onblocked=()=>resolve(null);
      }catch{resolve(null);}
    });
  }
  async function read(scope,urls){
    const result=new Map();result.stale=new Set();const db=await open();if(!db)return result;
    try{await new Promise(resolve=>{
      const tx=db.transaction('images','readonly'),store=tx.objectStore('images');
      for(const url of urls){const request=store.get(scope+'\n'+url);request.onsuccess=()=>{const row=request.result;if(row){result.set(url,row.data);if(Date.now()-row.updated>3600000)result.stale.add(url);}};}
      tx.oncomplete=resolve;tx.onerror=resolve;tx.onabort=resolve;
    });}catch{}return result;
  }
  function save(scope,url,img){
    const work=(async()=>{
      try{
        if(!img.naturalWidth||img.naturalWidth*img.naturalHeight>4194304)return;
        const canvas=document.createElement('canvas');canvas.width=img.naturalWidth;canvas.height=img.naturalHeight;canvas.getContext('2d').drawImage(img,0,0);
        const blob=await new Promise(resolve=>canvas.toBlob(resolve,'image/png'));if(!blob||blob.size>98304)return;
        const data=await new Promise((resolve,reject)=>{const reader=new FileReader();reader.onload=()=>resolve(reader.result);reader.onerror=reject;reader.readAsDataURL(blob);});if(data.length>131072)return;
        const db=await open();if(!db)return;
        await new Promise(resolve=>{
          const tx=db.transaction('images','readwrite'),store=tx.objectStore('images');store.put({key:scope+'\n'+url,data,updated:Date.now()});
          const count=store.count();count.onsuccess=()=>{let excess=count.result-512;if(excess<=0)return;const cursor=store.index('updated').openCursor();cursor.onsuccess=()=>{const row=cursor.result;if(row&&excess-->0){row.delete();row.continue();}};};
          tx.oncomplete=resolve;tx.onerror=resolve;tx.onabort=resolve;
        });
        return data;
      }catch{/* Storage restrictions must never prevent normal artwork loading. */}
    })();pending.add(work);work.finally(()=>pending.delete(work));return work;
  }
  root.PixelVaultArtworkCache={read,save,settled:()=>Promise.all([...pending])};
})(globalThis);
