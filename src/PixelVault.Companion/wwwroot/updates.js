(function(root){
  'use strict';
  function snapshot(trackers){return Object.fromEntries(trackers.filter(t=>typeof t.updateVersion==='string').map(t=>[t.id,{version:t.updateVersion,progress:t.updateProgress ?? null,complete:completion(t)}]));}
  function version(saved){return typeof saved==='string'?saved:saved?.version;}
  function completion(tracker){
    const totals=tracker.totals;
    return Number.isFinite(totals?.completed) && Number.isFinite(totals?.total) && totals.total>0
      ? totals.completed>=totals.total : null;
  }
  function newlyCompleted(saved,tracker){return saved?.complete===false && completion(tracker)===true;}
  function changed(previous,trackers){
    return previous?trackers.filter(t=>{
      if(typeof t.updateVersion!=='string')return false;
      const saved=previous[t.id];
      if(newlyCompleted(saved,t))return true;
      const linkedGoalMet=completion(t)===true && typeof t.achievementId==='string' && t.achievementId.trim().length>0;
      return !linkedGoalMet && version(saved)!==t.updateVersion;
    }):[];
  }
  function increased(saved,tracker){
    if(!Array.isArray(saved?.progress)||!Array.isArray(tracker.updateProgress))return null;
    // A standalone counter represents a quantity, so report its gain rather than one changed row.
    // Grouped milestone records still count once in the checklist path below.
    if(saved.progress.length===1 && tracker.updateProgress.length===1){
      const previous=saved.progress[0],current=tracker.updateProgress[0];
      if(previous.id===current.id && !previous.groupId && !current.groupId){
        if(!Number.isFinite(previous.current)||!Number.isFinite(current.current))return null;
        return Math.max(0,current.current-previous.current);
      }
    }
    const before=new Map(saved.progress.map(item=>[item.id,item.current])),groups=new Set();
    for(const item of tracker.updateProgress){
      if(before.has(item.id)&&Number.isFinite(item.current)&&item.current>(before.get(item.id)??0))groups.add(item.groupId || item.id);
    }
    return groups.size;
  }
  function summary(previous,trackers,job){
    const updated=changed(previous,trackers).map(t=>{
      const completed=newlyCompleted(previous[t.id],t);
      return {title:t.title,gameTitle:t.gameTitle,provider:t.provider,gameId:t.gameId,completed,increased:completed?null:increased(previous[t.id],t)};
    });
    const hasCompletion=updated.some(t=>t.completed);
    const title=hasCompletion && updated.every(t=>t.completed)?(updated.length===1?'Tracker completed':'Trackers completed'):'Trackers updated';
    const review=(job?.tasks || []).filter(t=>t.status==='review').map(t=>t.title);
    const failed=(job?.tasks || []).filter(t=>t.status==='failed').map(t=>t.title);
    const offline=job?.status==='offline';
    return {title:updated.length?title:offline?'PC unavailable':review.length || failed.length?'Refresh finished':'Up to date',updated,review,failed,
      message:hasCompletion?'':updated.length?`${updated.length} ${updated.length===1?'tracker changed':'trackers changed'}.`:offline?'The PC did not finish this check. Saved progress is kept.':review.length || failed.length?'No tracker changes were applied.':'No tracker changes since the last check.'};
  }
  function groupByGame(updated,games){
    const editions=new Map(games.map(g=>[`${g.provider}:${g.gameId}`,g]));
    const groups=new Map();
    for(const tracker of updated){
      const edition=`${tracker.provider}:${tracker.gameId}`;
      const game=editions.get(edition);
      const key=game?.groupId?`group:${game.groupId}`:tracker.provider && tracker.gameId?edition:`title:${tracker.gameTitle}`;
      if(!groups.has(key))groups.set(key,{title:game?.title || tracker.gameTitle,trackers:[]});
      groups.get(key).trackers.push(tracker);
    }
    return [...groups.values()].map(group=>({...group,trackers:group.trackers.sort((a,b)=>Number(b.completed)-Number(a.completed))}));
  }
  const api={snapshot,changed,summary,groupByGame};if(typeof module!=='undefined'&&module.exports)module.exports=api;else root.PixelVaultUpdates=api;
})(typeof globalThis!=='undefined'?globalThis:this);
