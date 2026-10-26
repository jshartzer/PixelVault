(function(root){
  'use strict';
  const defaults=['Story','Actions','Difficulty','Collectibles','Multiplayer','Miscellaneous','Uncategorized'];
  const esc=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const category=g=>g.category||'Uncategorized',dlc=g=>g.dlc||'Base Game';
  const earned=row=>row.progressKnown&&row.unlocked;
  const rarityNames={common:'',bronze:'Bronze',silver:'Silver',gold:'Gold','uber-rare':'Uber rare'};
  const rarityPercent=value=>new Intl.NumberFormat(undefined,{maximumFractionDigits:value<.01?3:1}).format(value);
  function rarityTier(percent){
    if(!Number.isFinite(percent)||percent<0||percent>100)return 'common';
    return percent<.01?'uber-rare':percent<=1?'gold':percent<=5?'silver':percent<=10?'bronze':'common';
  }
  function totals(rows){return{earned:rows.filter(r=>r.progressKnown&&r.unlocked).length,total:rows.length,unknown:rows.filter(r=>!r.progressKnown).length};}
  function matches(row,query,filter){return (!query||[row.title,row.description,row.tags,category(row.groups),dlc(row.groups)].join(' ').toLocaleLowerCase().includes(query.toLocaleLowerCase()))&&(filter==='all'||filter==='remaining'&&row.progressKnown&&!row.unlocked||filter==='earned'&&row.progressKnown&&row.unlocked||filter==='unknown'&&!row.progressKnown);}
  function organize(rows,query='',filter='all',sort='provider',order=[]){
    const categories=[...order,...defaults].map(s=>s.toLocaleLowerCase());const sections=new Map();
    for(const row of rows){const expansion=dlc(row.groups);if(!sections.has(expansion))sections.set(expansion,new Map());const groups=sections.get(expansion);const name=category(row.groups);if(!groups.has(name))groups.set(name,[]);groups.get(name).push(row);}
    return [...sections].sort(([a],[b])=>a==='Base Game'?-1:b==='Base Game'?1:a.localeCompare(b)).map(([name,groups])=>({name,totals:totals([...groups.values()].flat()),groups:[...groups].sort(([a],[b])=>{const rank=s=>{if(s==='Uncategorized'&&!order.includes(s))return Number.MAX_SAFE_INTEGER;const i=categories.indexOf(s.toLocaleLowerCase());return i<0?categories.length:i;};return rank(a)-rank(b)||a.localeCompare(b);}).map(([name,all])=>({name,totals:totals(all),rows:all.filter(r=>matches(r,query,filter)).sort((a,b)=>sort==='title'?a.title.localeCompare(b.title):sort==='rarity'?(a.rarity??101)-(b.rarity??101)||a.order-b.order:sort==='unlocked'?b.unlockUtc-a.unlockUtc||a.order-b.order:a.groups.sortOrder-b.groups.sortOrder||a.order-b.order)}))}));
  }
  function guideHTML(text){
    if(!text?.trim())return '<p class="muted">No guide has been saved for this achievement yet. A guide added on the desktop will appear here after sync.</p>';
    const inline=s=>esc(s).replace(/\*\*([^*]+)\*\*/g,'<strong>$1</strong>').replace(/`([^`]+)`/g,'<code>$1</code>');
    let html='',list='';const close=()=>{if(list){html+=`</${list}>`;list='';}};
    for(const line of text.replace(/\r/g,'').split('\n')){
      const bullet=line.match(/^\s*[-*•]\s+(.+)/),numbered=line.match(/^\s*\d+[.)]\s+(.+)/),header=line.match(/^#{1,6}\s+(.+)/);
      if(bullet||numbered){const kind=bullet?'ul':'ol';if(list!==kind){close();html+=`<${kind}>`;list=kind;}html+=`<li>${inline((bullet||numbered)[1])}</li>`;}
      else{close();if(header)html+=`<h3>${inline(header[1])}</h3>`;else if(line.trim())html+=`<p>${inline(line)}</p>`;}
    }close();return html;
  }
  function safeUrl(value){try{const url=new URL(value);return ['http:','https:'].includes(url.protocol)&&!url.username&&!url.password?url.href:null;}catch{return null;}}
  function completionSections(rows,query='',filter='all',sort='provider',order=[]){
    const grouped=organize(rows,query,filter,sort,order);
    return grouped.map(section=>({...section,groups:section.groups.filter(group=>group.rows.length).map(group=>({...group,phases:[false,true].map(completed=>({completed,name:completed?'Obtained':'Not obtained',rows:group.rows.filter(row=>!!earned(row)===completed)})).filter(phase=>phase.rows.length)}))})).filter(section=>section.groups.length);
  }
  function create({api,heading,progressBar,paintBars,status,getGames,getTrackers,newRequestId}){
    const $=id=>document.getElementById(id);let scope,openedId,snapshot,active=false,busy=false,editing,requestId;
    let preferredLayout='cards';try{if(localStorage.getItem('PixelVault.Companion.achievement-view')==='list')preferredLayout='list';}catch{}
    const defaultsView=()=>({query:'',filter:'all',sort:'provider',limit:120,layout:preferredLayout});
    const savedViews=new Map(),closed=new Set();let view=defaultsView();
    function clearHeadingRarity(){$('page-heading').classList.remove('rarity-common','rarity-bronze','rarity-silver','rarity-gold','rarity-uber-rare','earned');}
    const path=()=>`/achievements/${encodeURIComponent(scope.provider)}/${encodeURIComponent(scope.game)}`;
    const gameName=()=>getGames().find(g=>g.provider===scope.provider&&g.gameId===scope.game)?.title||scope.game;
    const pageHash=()=>openedId?`#achievement/${encodeURIComponent(scope.provider)}/${encodeURIComponent(scope.game)}/${encodeURIComponent(openedId)}`:`#achievements/${encodeURIComponent(scope.provider)}/${encodeURIComponent(scope.game)}`;
    const detail=()=>snapshot?.rows.find(r=>r.achievement.id===openedId);
    const icon=row=>`<span class="achievement-icon" aria-hidden="true">◇${row.icon?`<img src="/api/v1${path()}/${encodeURIComponent(row.id)}/icon" alt="" loading="lazy" decoding="async" width="64" height="64">`:''}</span>`;
    const state=row=>!row.progressKnown?'Unknown':row.unlocked?'Earned':'Remaining';
    const date=ticks=>ticks?new Date((ticks-621355968000000000)/10000).toLocaleString(undefined,{dateStyle:'medium',timeStyle:'short'}):'Not yet available';
    const groupLabel=g=>`${dlc(g)} / ${category(g)}`;
    function images(){ $('achievements-page').querySelectorAll('img').forEach(img=>img.addEventListener('error',()=>img.hidden=true,{once:true})); }
    function breadcrumb(){ $('breadcrumbs').innerHTML=`<a href="#">All games</a><span>/</span><a href="#game/${encodeURIComponent(scope.provider)}/${encodeURIComponent(scope.game)}">${esc(gameName())}</a><span>/</span>${openedId?`<a href="#achievements/${encodeURIComponent(scope.provider)}/${encodeURIComponent(scope.game)}">Achievements</a><span>/</span><span aria-current="page">Guide</span>`:'<span aria-current="page">Achievements</span>'}`; }
    function render(){
      if(!active||!snapshot)return;$('page-heading').classList.add('achievement-heading');clearHeadingRarity();breadcrumb();$('achievements-page').hidden=false;
      if(openedId){renderDetail();return;}
      heading('Achievements',gameName(),scope.provider==='steam'?'STEAM':'RETROACHIEVEMENTS');const rows=snapshot.rows.map(r=>r.achievement),count=totals(rows);
      $('page-count').textContent=`${count.earned} / ${count.total}`;
      $('achievements-page').innerHTML=`<div class="achievement-overview"><div><strong>${count.earned}<span> / ${count.total}</span></strong><p>Earned${count.unknown?` · ${count.unknown} progress unavailable`:''}</p></div>${progressBar(count.earned,count.total)}</div><p class="hint achievement-updated">Progress checked ${esc(date(snapshot.progressUpdatedUtc))}</p><div class="achievement-tools"><label><span>Search achievements</span><input id="achievement-search" type="search" placeholder="Name, description or tag…" value="${esc(view.query)}" autocomplete="off"></label><label><span>Show</span><select id="achievement-filter">${[['all','All achievements'],['remaining','Remaining'],['earned','Earned'],['unknown','Progress unavailable']].map(([key,label])=>`<option value="${key}" ${key===view.filter?'selected':''}>${label}</option>`).join('')}</select></label><label><span>Sort</span><select id="achievement-sort">${[['provider','Saved order'],['title','Title: A–Z'],['rarity','Rarest first'],['unlocked','Recently earned']].map(([key,label])=>`<option value="${key}" ${key===view.sort?'selected':''}>${label}</option>`).join('')}</select></label><label><span>View</span><select id="achievement-view" aria-label="View"><option value="cards" ${view.layout==='cards'?'selected':''}>Cards</option><option value="list" ${view.layout==='list'?'selected':''}>List</option></select></label></div><p id="achievement-results" class="hint" aria-live="polite"></p><div id="achievement-groups"></div><button id="achievement-more" class="quiet" hidden>Show more achievements</button>${rows.length?'':'<div class="achievement-empty"><h2>Achievements have not synced yet.</h2><p>Open this game’s achievements in the desktop app to retrieve them. Your connected PC brings saved progress, groups and guides here automatically.</p></div>'}`;
      $('achievement-search').addEventListener('input',()=>{view.query=$('achievement-search').value;view.limit=120;renderRows();});
      for(const [id,key] of [['achievement-filter','filter'],['achievement-sort','sort'],['achievement-view','layout']])$(id).addEventListener('change',()=>{view[key]=$(id).value;if(key==='layout'){preferredLayout=view.layout;try{localStorage.setItem('PixelVault.Companion.achievement-view',view.layout);}catch{}}view.limit=120;renderRows();});
      $('achievement-more').addEventListener('click',()=>{view.limit+=120;renderRows();});renderRows();paintBars();
    }
    function renderRows(){
      const rows=snapshot.rows.map(r=>r.achievement),sections=completionSections(rows,view.query,view.filter,view.sort,snapshot.categoryOrder);let used=0;
      const matching=rows.filter(r=>matches(r,view.query,view.filter)).length;
      $('achievement-results').textContent=`${matching} of ${rows.length} achievements · Group totals always show the full set`;
      function card(row){
        const tier=rarityTier(row.rarity),rare=rarityNames[tier],valid=Number.isFinite(row.rarity)&&row.rarity>=0&&row.rarity<=100;
        return `<a class="achievement-row rarity-${tier} ${earned(row)?'earned':''}" title="${esc(row.title)}${rare?' · '+rare+' rarity':''}" href="#achievement/${encodeURIComponent(scope.provider)}/${encodeURIComponent(scope.game)}/${encodeURIComponent(row.id)}">${icon(row)}<div><div class="achievement-row-title"><h3>${esc(row.title)}</h3><span class="achievement-state ${earned(row)?'earned-state':''}">${earned(row)?'✓ ':''}${state(row)}</span></div><p>${esc(row.description)}</p><div class="achievement-row-meta">${row.missable?'<span class="missable">Missable</span>':''}${row.guide?'<span>Guide</span>':''}${valid?`<span class="rarity-percent">${rare?rare+' · ':''}${rarityPercent(row.rarity)}% earned</span>`:''}${row.counterTarget>0&&!row.unlocked&&row.counterCurrent!=null?`<span>${esc(row.counterCurrent)} / ${esc(row.counterTarget)}</span>`:''}</div></div><span class="achievement-arrow" aria-hidden="true">›</span></a>`;
      }
      $('achievement-groups').innerHTML=sections.map(section=>{
          const key=JSON.stringify([scope.provider,scope.game,section.name]);
          const categories=section.groups.map(group=>{
            const content=group.phases.map(phase=>{
              const visible=phase.rows.slice(0,Math.max(0,view.limit-used));used+=visible.length;if(!visible.length)return '';
              return `<section class="achievement-phase ${phase.completed?'achievement-completed':''}"><h4 class="achievement-phase-title">${phase.name} <span>· ${phase.rows.length}</span></h4><div class="achievement-entries ${view.layout==='list'?'achievement-list':'achievement-tiles'}">${visible.map(card).join('')}</div></section>`;
            }).join('');
            return content?`<section class="achievement-category"><h3 class="achievement-category-heading"><span>${esc(group.name)}</span><small>${group.totals.earned} / ${group.totals.total}${group.totals.unknown?' · '+group.totals.unknown+' unknown':''}</small></h3>${content}</section>`:'';
          }).join('');
          return categories?`<details class="achievement-expansion" data-collapse="${esc(key)}" ${closed.has(key)?'':'open'}><summary><span>${esc(section.name)}</span><span>${section.totals.earned} / ${section.totals.total}${section.totals.unknown?' · ? '+section.totals.unknown:''}</span></summary>${categories}</details>`:'';
      }).join('')||'<p class="muted">No achievements match these filters.</p>';
      $('achievement-groups').querySelectorAll('details').forEach(element=>element.addEventListener('toggle',()=>{if(element.open)closed.delete(element.dataset.collapse);else closed.add(element.dataset.collapse);}));
      $('achievement-more').hidden=used>=matching;images();
    }
    function renderDetail(){
      const item=detail();if(!item){heading('Achievement unavailable','Return to the achievement list to choose another.');$('achievements-page').innerHTML='';return;}
      const row=item.achievement;heading(row.title,row.description,gameName().toUpperCase());$('page-count').textContent=state(row);
      clearHeadingRarity();$('page-heading').classList.add('rarity-'+rarityTier(row.rarity));$('page-heading').classList.toggle('earned',!!earned(row));
      const linked=getTrackers().filter(t=>t.provider===scope.provider&&t.gameId===scope.game&&t.achievementId===row.id);const source=safeUrl(row.sourceUrl);
      $('achievements-page').innerHTML=`<div class="achievement-detail-top rarity-${rarityTier(row.rarity)} ${earned(row)?'earned':''}">${icon(row)}<div><span class="achievement-state ${row.progressKnown&&row.unlocked?'earned-state':''}">${row.progressKnown&&row.unlocked?'✓ ':''}${state(row)}</span>${row.progressKnown&&row.unlocked?`<p class="hint">${row.unlockUtc?'Earned '+esc(date(row.unlockUtc)):'Earned · Date unavailable'}</p>`:''}${row.rarity!=null?`<p class="hint">${rarityPercent(row.rarity)}% of players</p>`:''}<p class="hint">Progress checked ${esc(date(snapshot.progressUpdatedUtc))}</p></div></div><div class="achievement-assignment"><div><span class="eyebrow">GROUPS</span><strong>${esc(groupLabel(row.groups))}</strong><small>${item.groupSync==='conflict'?'Desktop changed too. Choose which assignment to keep.':item.groupSync==='pending'?'Saved on NAS · Waiting to sync with PC':'Synced with desktop'}</small>${item.groupSync==='conflict'?`<small>Desktop: ${esc(groupLabel(item.desktopGroups))}</small>`:''}</div><button id="achievement-edit-groups" class="quiet">${item.groupSync==='conflict'?'Resolve groups':'Change groups'}</button></div><div class="achievement-tags">${row.missable?'<span class="missable">Missable</span>':''}${row.tags.split(',').map(s=>s.trim()).filter(Boolean).map(s=>`<span>${esc(s)}</span>`).join('')}</div>${row.counterTarget>0&&row.counterCurrent!=null?`<div class="achievement-counter"><strong>${esc(row.counterCurrent)} / ${esc(row.counterTarget)}</strong><span class="hint">Saved achievement progress</span>${progressBar(row.counterCurrent,row.counterTarget)}</div>`:''}<section class="achievement-guide" aria-labelledby="achievement-guide-title"><div class="achievement-guide-heading"><h2 id="achievement-guide-title">Guide</h2><span>${row.guide?'Saved guide':'No guide yet'}</span></div><div class="guide-prose">${guideHTML(row.guide)}</div>${source?`<p class="achievement-guide-source">Source: <a href="${esc(source)}" target="_blank" rel="noopener noreferrer">${esc(row.sourceTitle||new URL(source).hostname)} ↗</a></p>`:''}</section>${linked.length?`<section class="achievement-linked"><h2>Track your progress</h2>${linked.map(t=>`<a class="tracker-card" href="#tracker/${encodeURIComponent(t.id)}"><div><h3>${esc(t.title)}</h3></div><strong>${t.totals.completed} / ${t.totals.total}</strong><span aria-hidden="true">›</span></a>`).join('')}</section>`:''}`;
      $('achievement-edit-groups').addEventListener('click',openGroups);images();paintBars();
    }
    function openGroups(){
      editing=detail();requestId=newRequestId();$('achievement-category').value=editing.achievement.groups.category;$('achievement-dlc').value=editing.achievement.groups.dlc;
      $('achievement-category-options').innerHTML=[...new Set(snapshot.categories)].map(n=>`<option value="${esc(n)}">`).join('');$('achievement-dlc-options').innerHTML=[...new Set(snapshot.dlcs)].map(n=>`<option value="${esc(n)}">`).join('');
      $('achievement-group-status').textContent=editing.groupSync==='conflict'?`Desktop: ${groupLabel(editing.desktopGroups)}. Your saved web assignment is shown below.`:'Changes sync with the desktop when your PC is connected. Guide text and achievement progress stay unchanged.';
      $('achievement-use-desktop').hidden=editing.groupSync!=='conflict';$('achievement-group-save').textContent=editing.groupSync==='conflict'?'Keep web groups':'Save groups';$('achievement-group-dialog').showModal();
    }
    async function saveGroups(useDesktop=false){
      if(busy)return;busy=true;$('achievement-group-save').disabled=true;$('achievement-use-desktop').disabled=true;
      try{
        const groups={category:$('achievement-category').value.trim(),dlc:$('achievement-dlc').value.trim(),sortOrder:editing.achievement.groups.sortOrder};
        const value=await api(`${path()}/${encodeURIComponent(openedId)}/groups`,{version:editing.version,requestId,groups,useDesktop});snapshot.rows=snapshot.rows.map(r=>r.achievement.id===openedId?value:r);$('achievement-group-dialog').close();renderDetail();
      }catch(error){
        if(error.status===409){try{const latest=await api(`${path()}/${encodeURIComponent(openedId)}`);editing=latest;requestId=newRequestId();$('achievement-group-status').textContent=`The saved assignment changed to ${groupLabel(latest.achievement.groups)}. Your draft is kept. Review it and choose Save my assignment to apply it.`;$('achievement-group-save').textContent='Save my assignment';}catch{$('achievement-group-status').textContent=error.message;}}
        else $('achievement-group-status').textContent=error.message;
      }finally{busy=false;$('achievement-group-save').disabled=false;$('achievement-use-desktop').disabled=false;}
    }
    $('achievement-group-form').addEventListener('submit',event=>{event.preventDefault();saveGroups();});$('achievement-use-desktop').addEventListener('click',()=>saveGroups(true));$('achievement-group-cancel').addEventListener('click',()=>{if(!busy)$('achievement-group-dialog').close();});$('achievement-group-dialog').addEventListener('cancel',event=>{if(busy)event.preventDefault();});
    for(const id of ['achievement-category','achievement-dlc'])$(id).addEventListener('input',()=>requestId=newRequestId());
    return {
      get active(){return active;},get busy(){return busy;},
      reset(){if(active&&scope)savedViews.set(scope.provider+scope.game,view);active=false;clearHeadingRarity();$('page-heading').classList.remove('achievement-heading');$('achievements-page').hidden=true;if($('achievement-group-dialog').open&&!busy)$('achievement-group-dialog').close();},
      async open(parts){active=true;scope={provider:parts[1],game:parts[2]};openedId=parts[0]==='achievement'?parts[3]:undefined;view={...(savedViews.get(scope.provider+scope.game)||defaultsView()),layout:preferredLayout};const expected=location.hash;try{const incoming=await api(path());if(active&&location.hash===expected){snapshot=incoming;render();}}catch(error){if(active&&location.hash===expected){heading('Achievements',gameName());$('achievements-page').hidden=false;$('achievements-page').innerHTML=`<div class="achievement-empty"><h2>Achievements aren’t available for this platform yet.</h2><p>${error.status===404?'Connect this platform’s account in the desktop app, then sync its achievements.':esc(error.message)}</p></div>`;}}},
      async refresh(){if(!active||busy||$('achievement-group-dialog').open)return;const expected=location.hash;const next=await api(path());if(active&&location.hash===expected&&!$('achievement-group-dialog').open&&!document.activeElement?.matches('input,select,textarea')){snapshot=next;render();}},
      fallback(){return active&&scope?openedId?`achievements/${scope.provider}/${scope.game}`:`game/${scope.provider}/${scope.game}`:null;}
    };
  }
  const api={totals,matches,organize,completionSections,rarityTier,guideHTML,safeUrl,create};if(typeof module!=='undefined'&&module.exports)module.exports=api;else root.PixelVaultAchievements=api;
})(typeof globalThis!=='undefined'?globalThis:this);
