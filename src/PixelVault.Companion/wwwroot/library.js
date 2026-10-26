(function(root){
  'use strict';
  const titleOrder=(a,b)=>a.title.localeCompare(b.title,undefined,{sensitivity:'base',numeric:true}) || a.id.localeCompare(b.id);
  const timestamp=value=>Number.isFinite(Number(value)) && Number(value)>0?Number(value):null;
  function preferred(editions){
    return [...editions].sort((a,b)=>Number(b.trackerCount>0)-Number(a.trackerCount>0)
      || Number(b.stats?.achievementTotal!=null)-Number(a.stats?.achievementTotal!=null)
      || Number(b.stats?.playtimeMinutes!=null)-Number(a.stats?.playtimeMinutes!=null)
      || (timestamp(b.stats?.lastScreenshotUtc)||0)-(timestamp(a.stats?.lastScreenshotUtc)||0)
      || (a.provider+':'+a.gameId).localeCompare(b.provider+':'+b.gameId))[0];
  }
  function groupGames(games){
    const groups=new Map();
    for(const game of games){const key=game.groupId || game.provider+':'+game.gameId;if(!groups.has(key))groups.set(key,[]);groups.get(key).push(game);}
    return [...groups.entries()].map(([id,editions])=>{
      const lead=preferred(editions),dates=key=>editions.map(e=>timestamp(e.stats?.[key])).filter(v=>v!==null);
      return {...lead,id,editions,platforms:[...new Set(editions.map(e=>e.platform || e.provider))].sort(),
        coverUrl:lead.coverUrl || editions.find(e=>e.coverUrl)?.coverUrl,
        trackerCount:editions.reduce((n,e)=>n+e.trackerCount,0),
        lastScreenshotUtc:Math.max(0,...dates('lastScreenshotUtc')) || null,
        lastPlayedUtc:Math.max(0,...dates('lastPlayedUtc')) || null,
        playtimeMinutes:editions.some(e=>e.stats?.playtimeMinutes!=null)?editions.reduce((n,e)=>n+(e.stats?.playtimeMinutes || 0),0):null};
    });
  }
  function sortGames(groups,sort,pins=[]){
    const pinned=new Set(pins),dateCompare=(a,b,key,asc=false)=>{
      const x=a[key],y=b[key];if(x==null || y==null)return Number(x==null)-Number(y==null);
      return asc?x-y:y-x;
    };
    return [...groups].sort((a,b)=>Number(pinned.has(b.id))-Number(pinned.has(a.id)) ||
      (sort==='title-az'?titleOrder(a,b):sort==='title-za'?-titleOrder(a,b):
       sort==='last-played'?dateCompare(a,b,'lastPlayedUtc'):sort==='playtime'?dateCompare(a,b,'playtimeMinutes'):
       dateCompare(a,b,'lastScreenshotUtc',sort==='screenshot-oldest')) || titleOrder(a,b));
  }
  const trackerComplete=t=>Number(t.totals?.total)>0 && Number(t.totals.completed)>=Number(t.totals.total);
  const recordComplete=row=>row.length>0 && row.every(state=>state.complete);
  function trackerStatus(row,sourceLinked){
    if(recordComplete(row))return 'Completed';
    const reported=row.filter(s=>!s.complete&&sourceLinked&&s.observedUtcTicks>0);
    if(reported.some(s=>s.availability===2))return 'Locked';
    if(reported.some(s=>s.availability===1))return 'Available';
    return '';
  }
  function partition(values,complete){return {active:values.filter(value=>!complete(value)),completed:values.filter(complete)};}
  const api={groupGames,sortGames,preferred,trackerComplete,recordComplete,partition,trackerStatus};if(typeof module!=='undefined'&&module.exports)module.exports=api;else root.PixelVaultLibrary=api;
})(typeof globalThis!=='undefined'?globalThis:this);
