(function(root,factory){const icons=factory();if(typeof module==='object'&&module.exports)module.exports=icons;else root.PixelVaultTrackerIcons=icons;})(globalThis,function(){
  'use strict';
  const options=[
    ['checklist','Checklist','M4,5 L6,7 9,3 M12,5 H21 M4,12 L6,14 9,10 M12,12 H21 M4,19 L6,21 9,17 M12,19 H21'],
    ['trophy','Achievements','M7,3 H17 V10 Q17,15 12,15 Q7,15 7,10 Z M7,5 H3 V8 Q3,11 7,11 M17,5 H21 V8 Q21,11 17,11 M12,15 V21 M7,21 H17'],
    ['collectibles','Collectibles','M7,4 H17 L22,10 12,22 2,10 Z M2,10 H22 M7,4 L9,10 12,22 15,10 17,4'],
    ['exploration','Exploration','M12,2 A10,10 0 1 1 11.99,2 M16,8 L14,14 8,16 10,10 Z'],
    ['deliveries','Deliveries','M3,7 L12,2 21,7 V18 L12,23 3,18 Z M3,7 L12,12 21,7 M12,12 V23 M7.5,4.5 L16.5,9.5'],
    ['combat','Combat','M4,2 L19,17 17,19 2,4 Z M15,20 L20,15 M18,18 L22,22 M20,2 L14,8 M22,4 L16,10 M4,15 L9,20 M6,18 L2,22'],
    ['crafting','Crafting','M4,3 H13 L19,9 15,13 9,7 H4 Z M13,11 L3,21 5,23 15,13'],
    ['upgrades','Upgrades','M12,2 L22,12 H16 V22 H8 V12 H2 Z'],
    ['races','Races','M4,23 V3 Q8,0 13,3 Q17,6 22,3 V15 Q17,18 13,15 Q8,12 4,15 M10,2 V14 M16,4 V16 M4,9 Q8,6 13,9 Q17,12 22,9'],
    ['quests','Quests','M6,3 H19 Q23,3 23,7 H18 M19,3 V19 Q19,23 15,23 H4 Q1,23 1,19 H14 Q14,23 17,22 M6,3 V19 M9,8 H15 M9,12 H15'],
    ['building','Building','M2,11 L12,2 22,11 M5,9 V22 H19 V9 M10,22 V15 H14 V22'],
    ['relationships','Relationships','M12,21 L3,12 C-3,3 7,-2 12,6 C17,-2 27,3 21,12 Z']
  ].map(([id,label,path])=>({id,label,path}));
  const esc=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  function artwork(t){return t?.achievementId&&t.provider&&t.gameId?`/api/v1/achievements/${encodeURIComponent(t.provider)}/${encodeURIComponent(t.gameId)}/${encodeURIComponent(t.achievementId)}/unlocked-icon?art=1`:null;}
  function symbol(id){const option=options.find(o=>o.id===id)||options[0];return `<svg viewBox="-2 -2 28 28" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="${option.path}"></path></svg>`;}
  function image(t){const url=!t.iconId&&artwork(t);return `${symbol(t.iconId)}${url?`<img src="${esc(url)}" alt="" loading="lazy" decoding="async">`:''}`;}
  function retainImages(container){
    const saved=new Map();
    container.querySelectorAll('img').forEach(img=>{if(!img.hidden){const key=img.getAttribute('src');if(!saved.has(key))saved.set(key,[]);saved.get(key).push(img);}});
    return ()=>container.querySelectorAll('img').forEach(img=>{const previous=saved.get(img.getAttribute('src'))?.shift();if(previous&&previous!==img)img.replaceWith(previous);});
  }
  function card(t,{platform='',readOnly=false}={}){
    const done=Math.max(0,Number(t.totals?.completed)||0),total=Math.max(0,Number(t.totals?.total)||0),percent=total?Math.min(100,done*100/total):0,complete=total>0&&done>=total;
    return `<article class="tracker-card-shell ${complete?'is-complete':''}" data-tracker-icon-owner="${esc(t.id)}"><a class="tracker-card tracker-image-card ${complete?'is-complete':''}" href="#tracker/${encodeURIComponent(t.id)}"><span class="tracker-image" data-tracker-image="${esc(t.id)}">${image(t)}</span><div class="tracker-card-body"><div class="tracker-name"><h4>${esc(t.title)}</h4>${platform?`<p class="hint">${esc(platform)}</p>`:''}</div><div class="tracker-progress"><strong>${done} <span class="muted">/ ${total}</span>${complete?'<span class="tracker-complete-mark" aria-label="Completed"> ✓</span>':''}</strong><div class="progress" role="meter" aria-label="Completion" aria-valuemin="0" aria-valuemax="${Math.max(1,total)}" aria-valuenow="${done}"><div class="progress-value" data-percent="${percent}"></div></div></div></div></a>${readOnly?'':`<button type="button" class="quiet tracker-image-menu" data-select-tracker-image="${esc(t.id)}" aria-label="Select image for ${esc(t.title)}" title="Select Image..." aria-haspopup="dialog">⋯</button>`}</article>`;
  }
  function create({api,getTracker,onSaved,isReadOnly}){
    const dialog=document.getElementById('tracker-image-dialog'),context=document.getElementById('tracker-image-context'),status=document.getElementById('tracker-image-status');let selectedId,opener,saving=false;
    function closeMenu(){context.hidden=true;}
    function refresh(){document.querySelectorAll('[data-tracker-image]').forEach(host=>{const t=getTracker(host.dataset.trackerImage);if(t){const restore=retainImages(host);host.innerHTML=image(t);restore();}});}
    function open(id,origin){
      closeMenu();const t=getTracker(id);if(!t||isReadOnly()||saving)return;selectedId=id;opener=origin;status.textContent='';
      document.getElementById('tracker-image-options').innerHTML=options.map(o=>`<button type="button" class="tracker-icon-choice quiet" data-icon-choice="${o.id}" aria-label="Select ${o.label} icon" aria-pressed="${t.iconId===o.id}">${symbol(o.id)}<span>${o.label}</span></button>`).join('');
      const automatic=document.getElementById('tracker-image-automatic');automatic.textContent=artwork(t)?'Use achievement image':'Automatic (checklist)';automatic.setAttribute('aria-pressed',String(!t.iconId));
      dialog.showModal();
    }
    async function save(iconId){
      if(saving||!selectedId)return;saving=true;const id=selectedId;status.textContent='Saving image…';dialog.querySelectorAll('button').forEach(b=>b.disabled=true);
      try{const saved=await api('/trackers/'+encodeURIComponent(id)+'/icon',{iconId});onSaved(id,saved.iconId);refresh();dialog.close();}
      catch(error){status.textContent=error.message||'Could not save the image. Try again.';}
      finally{saving=false;dialog.querySelectorAll('button').forEach(b=>b.disabled=false);}
    }
    document.addEventListener('error',event=>{if(event.target.matches?.('.tracker-image img'))event.target.hidden=true;},true);
    document.addEventListener('click',event=>{const button=event.target.closest('[data-select-tracker-image]');if(button){event.preventDefault();open(button.dataset.selectTrackerImage,button);}});
    document.addEventListener('contextmenu',event=>{
      const owner=event.target.closest('[data-tracker-icon-owner]');if(!owner||isReadOnly())return;event.preventDefault();selectedId=owner.dataset.trackerIconOwner;opener=owner.querySelector('button,a')||owner;
      context.hidden=false;context.style.left=Math.max(8,Math.min(event.clientX||owner.getBoundingClientRect().left,innerWidth-context.offsetWidth-8))+'px';context.style.top=Math.max(8,Math.min(event.clientY||owner.getBoundingClientRect().top,innerHeight-context.offsetHeight-8))+'px';context.querySelector('button').focus();
    });
    context.querySelector('button').addEventListener('click',()=>open(selectedId,opener));
    document.addEventListener('pointerdown',event=>{if(!context.contains(event.target))closeMenu();});
    document.addEventListener('keydown',event=>{if(event.key==='Escape'&&!context.hidden){closeMenu();opener?.focus();}});
    dialog.addEventListener('click',event=>{const button=event.target.closest('[data-icon-choice]');if(button)void save(button.dataset.iconChoice);});
    document.getElementById('tracker-image-automatic').addEventListener('click',()=>void save(null));
    document.getElementById('tracker-image-cancel').addEventListener('click',()=>dialog.close());
    dialog.addEventListener('cancel',event=>{if(saving)event.preventDefault();});
    dialog.addEventListener('close',()=>{if(opener?.isConnected)opener.focus();});
    return {refresh,close(){closeMenu();if(dialog.open&&!saving)dialog.close();}};
  }
  return {options,artwork,symbol,image,card,create,retainImages};
});
