(function(root){
  'use strict';
  const titleOrder=(a,b)=>a.title.localeCompare(b.title,undefined,{sensitivity:'base',numeric:true}) || a.id.localeCompare(b.id);
  const timestamp=value=>Number.isFinite(Number(value)) && Number(value)>0?Number(value):null;
  function completionTier(game){
    const editions=game.editions || [game];
    return editions.some(e=>e.completionTier==='base-dlc-pv')?'base-dlc-pv':editions.some(e=>e.completionTier==='base-dlc')?'base-dlc':editions.some(e=>e.completionTier==='base')?'base':'none';
  }
  function completionDecoration(tier){
    if(!['base','base-dlc','base-dlc-pv'].includes(tier))return '';
    return (tier==='base-dlc-pv'?'<span class="completion-foil" aria-hidden="true"></span>':'')+'<span class="completion-frame" aria-hidden="true"></span><span class="completion-ornament completion-nw" aria-hidden="true"></span><span class="completion-ornament completion-ne" aria-hidden="true"></span><span class="completion-ornament completion-sw" aria-hidden="true"></span><span class="completion-ornament completion-se" aria-hidden="true"></span><span class="completion-clasp" aria-hidden="true"><span>100%</span></span><span class="sr-only">'+(tier==='base-dlc-pv'?'Base, DLC and PV achievements complete':tier==='base-dlc'?'Base and DLC achievements complete':'Base achievements complete')+'</span>';
  }
  // Delegation also handles covers replaced by refresh and cards in the completion case.
  // No saved state, pointer capture, continuous rendering or navigation interception.
  if(root.document){
    const quiet=root.matchMedia('(prefers-reduced-motion: reduce), (forced-colors: active)');
    let active=null;
    const reset=()=>{if(active){active.style.removeProperty('--completion-x');active.style.removeProperty('--completion-y');active=null;}};
    root.document.addEventListener('pointermove',event=>{
      if(quiet.matches||event.pointerType==='touch'){reset();return;}
      const host=event.target.closest?.('.completion-cover,.completion-foil-host');
      if(!host?.querySelector(':scope > .completion-foil')){reset();return;}
      if(active!==host){reset();active=host;}
      const rect=host.getBoundingClientRect();
      host.style.setProperty('--completion-x',(15+70*Math.max(0,Math.min(1,(event.clientX-rect.left)/Math.max(1,rect.width))))+'%');
      host.style.setProperty('--completion-y',(15+70*Math.max(0,Math.min(1,(event.clientY-rect.top)/Math.max(1,rect.height))))+'%');
    },{passive:true});
    root.document.addEventListener('pointerout',event=>{if(active&&!active.contains(event.relatedTarget))reset();},{passive:true});
    root.document.addEventListener('visibilitychange',()=>{if(root.document.hidden)reset();});
    quiet.addEventListener('change',reset);
    root.addEventListener('blur',reset);
  }
  function preferred(editions){
    return [...editions].sort((a,b)=>Number(b.trackerCount>0)-Number(a.trackerCount>0)
      || Number(b.stats?.achievementTotal!=null)-Number(a.stats?.achievementTotal!=null)
      || Number(b.stats?.playtimeMinutes!=null)-Number(a.stats?.playtimeMinutes!=null)
      || (timestamp(b.stats?.lastScreenshotUtc)||0)-(timestamp(a.stats?.lastScreenshotUtc)||0)
      || (a.provider+':'+a.gameId).localeCompare(b.provider+':'+b.gameId))[0];
  }
  const achievementProvider=e=>['steam','retroachievements','xbox'].includes(e.provider);
  function achievementEdition(profile,editions=[profile]){
    // A selected provider owns its progress, even when it has not synced yet.
    if(achievementProvider(profile))return profile;
    const providers=editions.filter(achievementProvider),matching=providers.filter(e=>e.platform===profile.platform);
    const candidates=matching.length?matching:providers;
    return candidates.length===1?candidates[0]:null;
  }
  function platformChoices(editions){
    const platforms=new Map();
    for(const edition of editions){const label=edition.platform||edition.provider;if(!platforms.has(label))platforms.set(label,[]);platforms.get(label).push(edition);}
    return [...platforms].flatMap(([label,members])=>{
      const providers=members.filter(achievementProvider);
      if(providers.length<=1)return [{label,edition:providers[0]||preferred(members),members}];
      // Preserve distinct provider choices if a future source uses the same platform label.
      return members.map(edition=>({label:label+' · '+(achievementProvider(edition)?edition.provider:'Captures'),edition,members:[edition]}));
    });
  }
  function profilePresentation(profile,editions=[profile]){
    const matching=editions.filter(e=>e.platform===profile.platform),captures=matching.find(e=>e.provider==='library'&&e.stats?.captures!=null);
    const art=key=>profile[key]||matching.find(e=>e[key])?.[key]||editions.find(e=>e[key])?.[key];
    return {...profile,coverUrl:art('coverUrl'),bannerUrl:art('bannerUrl'),stats:{...profile.stats,
      captures:profile.stats?.captures??captures?.stats.captures,lastScreenshotUtc:profile.stats?.lastScreenshotUtc??captures?.stats.lastScreenshotUtc}};
  }
  function groupGames(games){
    const groups=new Map();
    for(const game of games){const key=game.groupId || game.provider+':'+game.gameId;if(!groups.has(key))groups.set(key,[]);groups.get(key).push(game);}
    return [...groups.entries()].map(([id,editions])=>{
      const lead=preferred(editions),dates=key=>editions.map(e=>timestamp(e.stats?.[key])).filter(v=>v!==null);
      return {...lead,id,editions,platforms:[...new Set(editions.map(e=>e.platform || e.provider))].sort(),
        pinAliases:[...new Set(editions.flatMap(e=>[e.titleGroupId,e.editionGroupId]).filter(Boolean))],
        coverUrl:lead.coverUrl || editions.find(e=>e.coverUrl)?.coverUrl,
        trackerCount:editions.reduce((n,e)=>n+e.trackerCount,0),
        lastScreenshotUtc:Math.max(0,...dates('lastScreenshotUtc')) || null,
        lastPlayedUtc:Math.max(0,...dates('lastPlayedUtc')) || null,
        playtimeMinutes:editions.some(e=>e.stats?.playtimeMinutes!=null)?editions.reduce((n,e)=>n+(e.stats?.playtimeMinutes || 0),0):null};
    });
  }
  function sortGames(groups,sort,pins=[]){
    const dateCompare=(a,b,key,asc=false)=>{
      const x=a[key],y=b[key];if(x==null || y==null)return Number(x==null)-Number(y==null);
      return asc?x-y:y-x;
    };
    return [...groups].sort((a,b)=>Number(isPinned(b,pins))-Number(isPinned(a,pins)) ||
      (sort==='title-az'?titleOrder(a,b):sort==='title-za'?-titleOrder(a,b):
       sort==='last-played'?dateCompare(a,b,'lastPlayedUtc'):sort==='playtime'?dateCompare(a,b,'playtimeMinutes'):
       dateCompare(a,b,'lastScreenshotUtc',sort==='screenshot-oldest')) || titleOrder(a,b));
  }
  const trackerComplete=t=>Number(t.totals?.total)>0 && Number(t.totals.completed)>=Number(t.totals.total);
  function coverProgress(game,trackers=[]){
    const editions=game.editions || [game];
    const valid=(done,total)=>Number.isFinite(done)&&done>=0&&Number.isFinite(total)&&total>0;
    const achievementRows=editions.map(e=>valid(e.stats?.achievementUnlocked,e.stats?.achievementTotal)
      ?[e.stats.achievementUnlocked,e.stats.achievementTotal]
      :valid(e.completionEarned,e.completionTotal)?[e.completionEarned,e.completionTotal]:null).filter(Boolean);
    const summary=(completed,total)=>({completed,total,percent:total>0?(completed>=total?100:Math.min(99,Math.round(completed*100/total))):null});
    const achievements=summary(achievementRows.reduce((n,[done,total])=>n+Math.min(done,total),0),achievementRows.reduce((n,[,total])=>n+total,0));
    const matching=trackers.filter(t=>editions.some(e=>e.provider===t.provider&&e.gameId===t.gameId));
    const itemRows=matching.filter(t=>valid(t.totals?.completed,t.totals?.total));
    return {achievements,trackers:summary(itemRows.reduce((n,t)=>n+Math.min(t.totals.aggregateCompleted??t.totals.completed,t.totals.aggregateTotal??t.totals.total),0),itemRows.reduce((n,t)=>n+(t.totals.aggregateTotal??t.totals.total),0))};
  }
  function coverProgressBadges(game,trackers=[]){
    const progress=coverProgress(game,trackers);
    return ['achievements','trackers'].map(kind=>{
      const {completed,total,percent}=progress[kind],name=kind==='achievements'?'Achievements':'Tracker items';
      const label=percent===null?`${name}: no saved progress`:`${name}: ${completed} of ${total} ${kind==='achievements'?'obtained':'completed'} (${percent}%)${kind==='trackers'?` · ${total-completed} remaining`:''}`;
      return `<span class="cover-progress cover-progress-${kind}" role="img" aria-label="${label}" title="${label}"><img class="cover-progress-icon" src="/cover-progress-${kind}.svg?v=2" alt="" width="14" height="14" aria-hidden="true"><strong aria-hidden="true">${percent===null?'—':percent+'<small>%</small>'}</strong></span>`;
    }).join('');
  }
  const recordComplete=row=>row.length>0 && row.every(state=>state.complete);
  function trackerStatus(row,sourceLinked){
    if(recordComplete(row))return 'Completed';
    const reported=row.filter(s=>!s.complete&&sourceLinked&&s.observedUtcTicks>0);
    if(reported.some(s=>s.availability===2))return 'Locked';
    if(reported.some(s=>s.availability===1))return 'Available';
    return '';
  }
  function partition(values,complete){return {active:values.filter(value=>!complete(value)),completed:values.filter(complete)};}
  const trackerGroupOrder=['achievement','manual','century'];
  const trackerGroupTitles={achievement:'Achievement-linked trackers',manual:'Manual trackers',century:'Centurian Achievements'};
  function normalizeTrackerGroupOrder(order){return Array.isArray(order)&&order.length===3&&new Set(order).size===3&&order.every(id=>trackerGroupOrder.includes(id))?[...order]:[...trackerGroupOrder];}
  const trackerGroup=t=>!t.achievementId?.trim()?'manual':t.achievementId.startsWith('century.')?'century':'achievement';
  function trackerSections(values,order){
    const ordered=[...values].sort(titleOrder);
    return normalizeTrackerGroupOrder(order).map(id=>({id,title:trackerGroupTitles[id],values:ordered.filter(t=>trackerGroup(t)===id)}))
      .filter(group=>group.values.length).map(({values,...group})=>({...group,count:values.length,...partition(values,trackerComplete)}));
  }
  function isPinned(game,pins=[]){return pins.includes(game.id)||(game.pinAliases||[]).some(id=>pins.includes(id));}
  const api={groupGames,sortGames,isPinned,preferred,achievementEdition,platformChoices,profilePresentation,trackerComplete,recordComplete,partition,trackerSections,trackerGroupTitles,normalizeTrackerGroupOrder,trackerStatus,completionTier,completionDecoration,coverProgress,coverProgressBadges};if(typeof module!=='undefined'&&module.exports)module.exports=api;else root.PixelVaultLibrary=api;
})(typeof globalThis!=='undefined'?globalThis:this);
