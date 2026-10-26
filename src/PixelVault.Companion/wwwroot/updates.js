(function(root){
  'use strict';
  function snapshot(trackers){return Object.fromEntries(trackers.filter(t=>typeof t.updateVersion==='string').map(t=>[t.id,{version:t.updateVersion,progress:t.updateProgress ?? null}]));}
  function version(saved){return typeof saved==='string'?saved:saved?.version;}
  function changed(previous,trackers){return previous?trackers.filter(t=>typeof t.updateVersion==='string' && version(previous[t.id])!==t.updateVersion):[];}
  function increased(saved,tracker){
    if(!Array.isArray(saved?.progress)||!Array.isArray(tracker.updateProgress))return null;
    const before=new Map(saved.progress.map(item=>[item.id,item.current])),groups=new Set();
    for(const item of tracker.updateProgress){
      if(before.has(item.id)&&Number.isFinite(item.current)&&item.current>(before.get(item.id)??0))groups.add(item.groupId || item.id);
    }
    return groups.size;
  }
  function summary(previous,trackers,job){
    const updated=changed(previous,trackers).map(t=>({title:t.title,gameTitle:t.gameTitle,increased:increased(previous[t.id],t)}));
    const review=(job?.tasks || []).filter(t=>t.status==='review').map(t=>t.title);
    const failed=(job?.tasks || []).filter(t=>t.status==='failed').map(t=>t.title);
    const offline=job?.status==='offline';
    return {title:updated.length?'Trackers updated':offline?'PC unavailable':review.length || failed.length?'Refresh finished':'Up to date',updated,review,failed,
      message:updated.length?`${updated.length} ${updated.length===1?'tracker changed':'trackers changed'}.`:offline?'The PC did not finish this check. Saved progress is kept.':review.length || failed.length?'No tracker changes were applied.':'No tracker changes since the last check.'};
  }
  const api={snapshot,changed,summary};if(typeof module!=='undefined'&&module.exports)module.exports=api;else root.PixelVaultUpdates=api;
})(typeof globalThis!=='undefined'?globalThis:this);
