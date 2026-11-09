(function(root){
 'use strict';
 const esc=v=>String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
 const badgeUrl=g=>`/api/v1/centurion/${encodeURIComponent(g.provider)}/${encodeURIComponent(g.gameId)}/badge?art=2`;
 const artUrl=(g,id)=>`/api/v1/century/${encodeURIComponent(g.provider)}/${encodeURIComponent(g.gameId)}/${encodeURIComponent(id)}/icon?art=5&state=unlocked`;
 function observe(previous,games){
   const next={...previous},notices=[];
   for(const game of games){
     const initialized=Array.isArray(previous?.[game.scope]),seen=new Set(initialized?previous[game.scope]:[]);
     for(const item of game.items.filter(i=>i.unlocked).sort((a,b)=>a.order-b.order)){
       if(!seen.has(item.id)&&initialized)notices.push({...game,...item,badge:false});seen.add(item.id);
     }
     if(game.badgeUnlocked){if(!seen.has('@badge')&&initialized)notices.push({...game,id:'@badge',title:'Centurion',description:'All five Centurion achievements unlocked.',badge:true});seen.add('@badge');}
     next[game.scope]=[...seen];
   }return {next,notices};
 }
 function shelf(game,provider,gameId,selectedId){
   const items=game?.items||[];if(!items.length)return '';
   const selected=items.find(i=>i.id===selectedId)||items[0],count=items.filter(i=>i.unlocked).length;
   const badge=game.badgeUnlocked?`<img src="${badgeUrl(game)}" alt="${esc(game.gameTitle)} Centurion badge">`:'<span class="centurion-hex-inner" aria-hidden="true">◇</span>';
   const cards=Array.from({length:5},(_,i)=>{
     const item=items[i],art=item?.unlocked?`<img src="${artUrl(game,item.id)}" alt="">`:`<span aria-hidden="true">${String(i+1).padStart(2,'0')}</span>`;
     return item?`<button type="button" class="centurion-slot ${item.unlocked?'is-earned':''}" data-centurion-id="${esc(item.id)}" aria-pressed="${item.id===selected.id}" aria-label="${esc(item.title+(item.unlocked?', earned':', not earned')+'. Show requirements.')}"><span class="centurion-socket">${art}</span></button>`:`<div class="centurion-slot" aria-label="Unannounced Centurion achievement"><span class="centurion-socket">${art}</span></div>`;
   }).join('');
   return `<section class="centurion-collection" aria-label="Centurion Achievements"><header class="centurion-heading"><div class="centurion-hex" role="img" aria-label="${game.badgeUnlocked?'Centurion badge earned':'Empty badge slot; collect all five achievements'}">${badge}</div><div><h3>Centurion<br>Achievements</h3><span class="centurion-count">${count} / 5</span></div></header><div class="centurion-rail"><div class="centurion-slots" aria-label="Five achievements">${cards}</div><div class="centurion-detail" aria-live="polite"><a href="#achievement/${encodeURIComponent(provider)}/${encodeURIComponent(gameId)}/${encodeURIComponent(selected.id)}" aria-label="Open achievement details: ${esc(selected.title)}">${esc(selected.title)} <span aria-hidden="true">↗</span></a><p>${esc(selected.description)}</p></div></div></section>`;
 }
 function create({api,onChange=()=>{}}){
   let scope='',generation=0,snapshots=[],seen={},pending=null,queue=[],timer,exitTimer,remaining=0,started=0,hover=false,focus=false;
   const selections=new Map();
   const host=document.createElement('div');host.id='centurion-notifications';host.className='centurion-notification-host';host.hidden=true;
   host.innerHTML='<div class="reward-stage"><article class="reward-notification"><button class="reward-dismiss" type="button" aria-label="Dismiss reward notification">×</button><div class="reward-sweep" aria-hidden="true"></div><div class="reward-layout"><div class="reward-art-wrap"><span class="reward-halo" aria-hidden="true"></span><span class="reward-burst" aria-hidden="true"></span><img class="reward-art" alt=""><span class="reward-points"></span></div><div class="reward-copy"><p class="reward-eyebrow"></p><h2 class="reward-title"></h2><p class="reward-description"></p><p class="reward-game"></p></div></div></article></div>';
   document.body.appendChild(host);
   const card=host.querySelector('article'),live=document.createElement('div');live.className='sr-only';live.setAttribute('role','status');live.setAttribute('aria-live','polite');document.body.appendChild(live);
   const el=s=>host.querySelector(s);
   for(let i=0;i<16;i++){const spark=document.createElement('i');spark.style.setProperty('--angle',`${i*22.5}deg`);spark.style.setProperty('--reach',`${-(82+i%3*16)}px`);el('.reward-burst').appendChild(spark);}
   function clear(){clearTimeout(timer);clearTimeout(exitTimer);timer=exitTimer=null;remaining=0;queue=[];host.hidden=true;live.textContent='';}
   function pause(){if(timer){clearTimeout(timer);timer=null;remaining=Math.max(1,remaining-(performance.now()-started));}}
   function resume(){if(!host.hidden&&!timer&&remaining>0&&!hover&&!focus&&!document.hidden){started=performance.now();timer=setTimeout(dismiss,remaining);}}
   function next(){
     if(!queue.length||document.hidden)return;
     const item=queue.shift();host.hidden=false;card.classList.remove('is-playing','is-leaving');card.classList.toggle('badge-reward',item.badge);
     el('.reward-art').src=item.badge?badgeUrl(item):artUrl(item,item.id);el('.reward-art').alt=item.title;
     el('.reward-eyebrow').textContent=item.badge?'CENTURION BADGE EARNED':'CENTURION ACHIEVEMENT EARNED';
     el('.reward-title').textContent=item.title;el('.reward-description').textContent=item.description;el('.reward-game').textContent=item.gameTitle;
     el('.reward-points').hidden=item.badge;el('.reward-points').textContent=`+${item.points||100} PV`;
     void card.offsetWidth;card.classList.add('is-playing');live.textContent=`${item.badge?'Badge':'Achievement'} earned: ${item.title}. ${item.description}`;
     remaining=item.badge?7000:5000;resume();
   }
   function dismiss(){clearTimeout(timer);timer=null;remaining=0;card.classList.remove('is-playing');card.classList.add('is-leaving');exitTimer=setTimeout(()=>{host.hidden=true;next();},270);}
   el('button').addEventListener('click',dismiss);
   host.addEventListener('pointerenter',()=>{hover=true;pause();});host.addEventListener('pointerleave',()=>{hover=false;resume();});
   host.addEventListener('focusin',()=>{focus=true;pause();});host.addEventListener('focusout',e=>{if(!host.contains(e.relatedTarget)){focus=false;resume();}});
   host.addEventListener('keydown',e=>{if(e.key==='Escape')dismiss();});
   document.addEventListener('visibilitychange',()=>{if(document.hidden)pause();else if(host.hidden)next();else resume();});
   async function refresh(workspace){
     if(!workspace)return;
     if(scope!==workspace){clear();scope=workspace;generation++;pending=null;snapshots=[];try{seen=JSON.parse(localStorage.getItem('PixelVault.centurion-rewards.'+scope))||{};}catch{seen={};}}
     if(pending)return pending;
     const token=generation;
     pending=(async()=>{try{
       const games=await api('/centurion');if(token!==generation)return;
       // Remove visible/queued notices when the active account changes.
       const allowed=new Set(games.map(g=>g.scope));if(snapshots.some(g=>!allowed.has(g.scope)))clear();
       const result=observe(seen,games);seen=result.next;snapshots=games;
       try{localStorage.setItem('PixelVault.centurion-rewards.'+scope,JSON.stringify(seen));}catch{/* Session dedup remains active. */}
       queue.push(...result.notices);if(host.hidden)next();onChange();
     }catch{/* Failed reads cannot erase saved unlocks or produce a reward. */}finally{if(token===generation)pending=null;}})();return pending;
   }
   return {refresh,reset(){selections.clear();generation++;scope='';pending=null;snapshots=[];seen={};clear();},render(target,provider,gameId){
     if(!target)return;const game=snapshots.find(g=>g.provider===provider&&g.gameId===gameId),key=game?.scope||provider+':'+gameId;
     const html=shelf(game,provider,gameId,selections.get(key));target.hidden=!html;
     if(target._centurionMarkup===key+'\n'+html)return;target._centurionMarkup=key+'\n'+html;
     const focused=target.contains(document.activeElement)?document.activeElement?.dataset.centurionId:null;
     target.innerHTML=html;
     target.querySelectorAll('[data-centurion-id]').forEach(button=>button.addEventListener('click',()=>{selections.set(key,button.dataset.centurionId);this.render(target,provider,gameId);}));
     if(focused)Array.from(target.querySelectorAll('[data-centurion-id]')).find(b=>b.dataset.centurionId===focused)?.focus();
   }};
 }
 const api={observe,shelf,create};if(typeof module!=='undefined'&&module.exports)module.exports=api;else root.PixelVaultCenturion=api;
})(typeof globalThis!=='undefined'?globalThis:this);
