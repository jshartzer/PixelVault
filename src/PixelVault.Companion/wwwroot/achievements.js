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
  const state=row=>!row.progressKnown?'Unknown':row.unlocked?'Earned':'Remaining';
  function imageUrl(row,scope){
    return row.centuryId?`/api/v1/century/${encodeURIComponent(scope.provider)}/${encodeURIComponent(scope.game)}/${encodeURIComponent(row.centuryId)}/icon?art=5&state=${earned(row)?'unlocked':'locked'}`:`/api/v1/achievements/${encodeURIComponent(scope.provider)}/${encodeURIComponent(scope.game)}/${encodeURIComponent(row.id)}/icon?art=3&state=${earned(row)?'earned':'locked'}&source=${encodeURIComponent((earned(row)?row.icon:row.lockedIcon||row.icon)||'')}`;
  }
  function achievementIcon(row,scope){
    const path=imageUrl(row,scope),source=scope.artwork?.get(path)||path;
    return `<span class="achievement-icon" aria-hidden="true">◇${(row.centuryId||(earned(row)?row.icon:row.lockedIcon||row.icon))?`<img ${scope.artworkPending?'data-artwork-pending="true"':`src="${esc(source)}"`} data-artwork-url="${esc(path)}"${!row.centuryId&&scope.provider==='xbox'?` data-xbox-game="${esc(scope.game)}"`:''} alt="" loading="lazy" decoding="async" width="96" height="96">`:''}</span>`;
  }
  function achievementCard(row,scope){
    const tier=rarityTier(row.rarity),rare=rarityNames[tier],valid=Number.isFinite(row.rarity)&&row.rarity>=0&&row.rarity<=100;
    if(row.centuryId)return `<a href="#achievement/${encodeURIComponent(scope.provider)}/${encodeURIComponent(scope.game)}/${encodeURIComponent(row.centuryId)}" class="achievement-row rarity-common century-achievement" title="${esc(row.title+' · '+row.points+' PV pts\n'+row.requirement)}" aria-label="${esc(row.title+' · '+row.points+' PV points · '+state(row))}">${achievementIcon(row,scope)}<div><div class="achievement-row-title"><h3>${esc(row.title)}</h3></div><p>${esc(row.description)}</p><div class="achievement-row-meta"><span class="achievement-points">${esc(row.points)} PV pts</span>${row.counterTarget>0&&row.counterCurrent!=null&&!row.unlocked?`<span>${esc(row.counterCurrent)} / ${esc(row.counterTarget)}</span>`:''}</div></div></a>`;
    return `<a class="achievement-row rarity-${tier} ${earned(row)?'earned':''}" title="${esc(row.title)}${rare?' · '+rare+' rarity':''}" aria-label="${esc(row.title)} · ${state(row)}" href="#achievement/${encodeURIComponent(scope.provider)}/${encodeURIComponent(scope.game)}/${encodeURIComponent(row.id)}">${achievementIcon(row,scope)}<div><div class="achievement-row-title"><h3>${esc(row.title)}</h3></div><p>${esc(row.description)}</p><div class="achievement-row-meta">${row.missable?'<span class="missable">Missable</span>':''}${valid?`<span class="rarity-percent">${rare?rare+' · ':''}${rarityPercent(row.rarity)}% earned</span>`:''}${row.counterTarget>0&&!row.unlocked&&row.counterCurrent!=null?`<span>${esc(row.counterCurrent)} / ${esc(row.counterTarget)}</span>`:''}</div></div><span class="achievement-arrow" aria-hidden="true">›</span></a>`;
  }
  function totals(rows){return{earned:rows.filter(r=>r.progressKnown&&r.unlocked).length,total:rows.length,unknown:rows.filter(r=>!r.progressKnown).length};}
  function matches(row,query,filter){return (!query||[row.title,row.description,row.tags,category(row.groups),dlc(row.groups)].join(' ').toLocaleLowerCase().includes(query.toLocaleLowerCase()))&&(filter==='all'||filter==='remaining'&&row.progressKnown&&!row.unlocked||filter==='earned'&&row.progressKnown&&row.unlocked||filter==='unknown'&&!row.progressKnown);}
  function organize(rows,query='',filter='all',sort='provider',order=[]){
    const categories=[...order,...defaults].map(s=>s.toLocaleLowerCase());const sections=new Map();
    for(const row of rows){const expansion=dlc(row.groups);if(!sections.has(expansion))sections.set(expansion,new Map());const groups=sections.get(expansion);const name=category(row.groups);if(!groups.has(name))groups.set(name,[]);groups.get(name).push(row);}
    return [...sections].sort(([a],[b])=>a==='Base Game'?-1:b==='Base Game'?1:a.localeCompare(b)).map(([name,groups])=>({name,totals:totals([...groups.values()].flat()),groups:[...groups].sort(([a],[b])=>{const rank=s=>{if(s==='Uncategorized'&&!order.includes(s))return Number.MAX_SAFE_INTEGER;const i=categories.indexOf(s.toLocaleLowerCase());return i<0?categories.length:i;};return rank(a)-rank(b)||a.localeCompare(b);}).map(([name,all])=>({name,totals:totals(all),rows:all.filter(r=>matches(r,query,filter)).sort((a,b)=>sort==='title'?a.title.localeCompare(b.title):sort==='rarity'?(a.rarity??101)-(b.rarity??101)||a.order-b.order:sort==='unlocked'?b.unlockUtc-a.unlockUtc||a.order-b.order:a.order-b.order)}))}));
  }
  function guideHTML(text){
    if(!text?.trim())return '<p class="muted">No guide has been added for this achievement yet.</p>';
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
    return grouped.map(section=>({...section,groups:section.groups.filter(group=>group.rows.length).map(group=>{
      const phases=[];
      const remaining=group.rows.filter(row=>!earned(row));
      const obtained=group.rows.filter(earned);
      if(remaining.length)phases.push({completed:false,name:'Not obtained',rows:remaining});
      if(obtained.length)phases.push({completed:true,name:'Obtained',rows:obtained});
      return {...group,phases};
    })})).filter(section=>section.groups.length);
  }
  function centurySection(century,query='',filter='all',collapsed=false,layout='cards'){
    const definitions=century?.definitions||[],q=query.trim().toLocaleLowerCase();
    const visible=definitions.filter(d=>matches({...d,description:d.description+" "+d.requirement,groups:{category:d.category}},q,filter));
    const cards=visible.map(d=>achievementCard({...d,centuryId:d.id,connectionStatus:d.connectionStatus||century.status},{provider:d.provider,game:d.gameId})).join('');
    return `<details class="achievement-expansion achievement-century" data-collapse="century" ${collapsed?'':'open'}><summary><span>Centurian Achievements</span><span>${definitions.length} ${definitions.length===1?'achievement':'achievements'}</span></summary>${definitions.length?`${cards?`<div class="achievement-entries ${layout==='list'?'achievement-list':'achievement-tiles'}">${cards}</div>`:'<p class="muted century-plan">No Centurian Achievements match these filters.</p>'}`:'<p class="muted century-plan">No Centurian Achievements have been added for this game yet.</p>'}</details>`;
  }
  function create({api,heading,progressBar,paintBars,status,getGames,getTrackers,getWorkspace=()=>'',newRequestId,trackerCard}){
    const $=id=>document.getElementById(id);let scope,openedId,snapshot,snapshotKey,generation=0,active=false,busy=false,editing,requestId;
    let preferredLayout='cards';try{if(localStorage.getItem('PixelVault.Companion.achievement-view')==='list')preferredLayout='list';}catch{}
    const defaultsView=()=>({query:'',filter:'all',sort:'provider',limit:120,layout:preferredLayout});
    const savedViews=new Map(),closed=new Set();let view=defaultsView();
    function clearHeadingRarity(){$('page-heading').classList.remove('rarity-common','rarity-bronze','rarity-silver','rarity-gold','rarity-uber-rare','earned');}
    const path=()=>`/achievements/${encodeURIComponent(scope.provider)}/${encodeURIComponent(scope.game)}`;
    const gameName=()=>getGames().find(g=>g.provider===scope.provider&&g.gameId===scope.game)?.title||scope.game;
    const pageHash=()=>openedId?`#achievement/${encodeURIComponent(scope.provider)}/${encodeURIComponent(scope.game)}/${encodeURIComponent(openedId)}`:`#achievements/${encodeURIComponent(scope.provider)}/${encodeURIComponent(scope.game)}`;
    const detail=()=>snapshot?.rows.find(r=>r.achievement.id===openedId)||(()=>{
      const d=snapshot?.century?.definitions.find(d=>d.id===openedId);return d?{century:true,achievement:{...d,centuryId:d.id,connectionStatus:d.connectionStatus||snapshot.century.status,guide:d.requirement,tags:d.category,groups:{category:d.category,dlc:''}}}:null;
    })();
    const icon=row=>achievementIcon(row,scope);
    const date=ticks=>ticks?new Date((ticks-621355968000000000)/10000).toLocaleString(undefined,{dateStyle:'medium',timeStyle:'short'}):'Not yet available';
    const groupLabel=g=>`${dlc(g)} / ${category(g)}`;
    const retainedImages=new Map();
    async function prepareArtwork(incoming){
      if(!root.PixelVaultArtworkCache)return;
      const rows=[...incoming.rows.map(r=>r.achievement),...(incoming.century?.definitions||[]).map(d=>({...d,centuryId:d.id}))];
      const target=scope;target.artwork=await Promise.race([root.PixelVaultArtworkCache.read(getWorkspace(),rows.map(row=>imageUrl(row,target))),new Promise(resolve=>setTimeout(()=>resolve(new Map()),500))]);
    }
    function rememberImages(){
      $('achievements-page').querySelectorAll('img').forEach(img=>{if(!img.hidden){const key=img.getAttribute('src');if(!retainedImages.has(key))retainedImages.set(key,[]);const saved=retainedImages.get(key);if(!saved.includes(img))saved.push(img);}});
    }
    function images(){
      $('achievements-page').querySelectorAll('img').forEach(img=>{
        const previous=retainedImages.get(img.getAttribute('src'))?.shift();
        if(previous){img.replaceWith(previous);return;}
        if(img.src.startsWith('data:')&&scope.artwork?.stale?.has(img.dataset.artworkUrl)){
          const namespace=getWorkspace(),url=img.dataset.artworkUrl,fresh=new Image();
          fresh.onload=async()=>{const data=await root.PixelVaultArtworkCache.save(namespace,url,fresh);if(data&&img.isConnected&&img.src.startsWith('data:'))img.src=data;};fresh.src=url;
        }
        const namespace=getWorkspace(),url=img.dataset.artworkUrl;
        const save=()=>{
          img.hidden=false;
          // Match the native badge crop; square and portrait artwork stays intact.
          img.classList.toggle('xbox-square-art',!!img.dataset.xboxGame&&img.naturalWidth<=img.naturalHeight);
          img.classList.toggle('xbox-kabuto-art',img.dataset.xboxGame==='2026288209'&&img.naturalHeight>0&&Math.abs(img.naturalWidth/img.naturalHeight-16/9)<.02);
          if(url&&!img.src.startsWith('data:'))root.PixelVaultArtworkCache?.save(namespace,url,img);
        };
        img.addEventListener('load',save);if(img.complete&&img.naturalWidth)save();
        let retried=false;
        const failed=()=>{
          if(!retried&&img.src.startsWith('data:')&&url){retried=true;img.hidden=false;img.src=url;return;}
          img.hidden=true;
        };
        img.addEventListener('error',failed);
        if(img.getAttribute('src')&&img.complete&&!img.naturalWidth)queueMicrotask(failed);
      });
      retainedImages.clear();
    }
    function breadcrumb(){ $('breadcrumbs').innerHTML=`<a href="#">All games</a><span>/</span><a href="#game/${encodeURIComponent(scope.provider)}/${encodeURIComponent(scope.game)}">${esc(gameName())}</a><span>/</span>${openedId?`<a href="#achievements/${encodeURIComponent(scope.provider)}/${encodeURIComponent(scope.game)}">Achievements</a><span>/</span><span aria-current="page">Guide</span>`:'<span aria-current="page">Achievements</span>'}`; }
    function render(){
      if(!active||!snapshot)return;rememberImages();$('page-heading').classList.add('achievement-heading');clearHeadingRarity();breadcrumb();$('achievements-page').hidden=false;
      if(openedId){renderDetail();return;}
      heading('Achievements',gameName(),scope.provider==='xbox'?'XBOX':scope.provider==='steam'?'STEAM':'RETROACHIEVEMENTS');const rows=snapshot.rows.map(r=>r.achievement),count=totals(rows);
      $('page-count').textContent=`${count.earned} / ${count.total}`;
      $('achievements-page').innerHTML=`<div class="achievement-overview"><div><strong>${count.earned}<span> / ${count.total}</span></strong><p>Earned${count.unknown?` · ${count.unknown} progress unavailable`:''}</p></div>${progressBar(count.earned,count.total)}</div><p class="hint achievement-updated">Progress checked ${esc(date(snapshot.progressUpdatedUtc))}</p><div class="achievement-tools"><label><span>Search achievements</span><input id="achievement-search" type="search" placeholder="Name, description or tag…" value="${esc(view.query)}" autocomplete="off"></label><label><span>Show</span><select id="achievement-filter">${[['all','All achievements'],['remaining','Remaining'],['earned','Earned'],['unknown','Progress unavailable']].map(([key,label])=>`<option value="${key}" ${key===view.filter?'selected':''}>${label}</option>`).join('')}</select></label><label><span>Sort</span><select id="achievement-sort">${[['provider','Original order'],['title','Title: A–Z'],['rarity','Rarest first'],['unlocked','Recently earned']].map(([key,label])=>`<option value="${key}" ${key===view.sort?'selected':''}>${label}</option>`).join('')}</select></label><label><span>View</span><select id="achievement-view" aria-label="View"><option value="cards" ${view.layout==='cards'?'selected':''}>Cards</option><option value="list" ${view.layout==='list'?'selected':''}>List</option></select></label></div><p id="achievement-results" class="hint" aria-live="polite"></p><div id="achievement-groups"></div><button id="achievement-more" class="quiet" hidden>Show more achievements</button>${rows.length?'':'<div class="achievement-empty"><h2>Achievements have not synced yet.</h2><p>Open this game’s achievements in the desktop app to retrieve them. Your connected PC brings saved progress, groups and guides here automatically.</p></div>'}`;
      $('achievement-search').addEventListener('input',()=>{view.query=$('achievement-search').value;view.limit=120;renderRows();});
      for(const [id,key] of [['achievement-filter','filter'],['achievement-sort','sort'],['achievement-view','layout']])$(id).addEventListener('change',()=>{view[key]=$(id).value;if(key==='layout'){preferredLayout=view.layout;try{localStorage.setItem('PixelVault.Companion.achievement-view',view.layout);}catch{}}view.limit=120;renderRows();});
      $('achievement-more').addEventListener('click',()=>{view.limit+=120;renderRows();});renderRows();paintBars();
    }
    function renderRows(){
      rememberImages();
      const rows=snapshot.rows.map(r=>r.achievement),sections=completionSections(rows,view.query,view.filter,view.sort,snapshot.categoryOrder);let used=0;
      const matching=rows.filter(r=>matches(r,view.query,view.filter)).length;
      $('achievement-results').textContent=`${matching} of ${rows.length} achievements · Group totals always show the full set`;
      const card=row=>achievementCard(row,scope);
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
      $('achievement-groups').insertAdjacentHTML('beforeend',centurySection(snapshot.century,view.query,view.filter,closed.has('century'),view.layout));
      $('achievement-groups').querySelectorAll('details').forEach(element=>element.addEventListener('toggle',()=>{if(element.open)closed.delete(element.dataset.collapse);else closed.add(element.dataset.collapse);}));
      $('achievement-more').hidden=used>=matching;images();
    }
    function renderDetail(){
      const item=detail();if(!item){heading('Achievement unavailable','Return to the achievement list to choose another.');$('achievements-page').innerHTML='';return;}
      const row=item.achievement;heading(row.title,row.description,gameName().toUpperCase());$('page-count').textContent=state(row);
      clearHeadingRarity();$('page-heading').classList.add('rarity-'+rarityTier(row.rarity));$('page-heading').classList.toggle('earned',!!earned(row));
      const linked=getTrackers().filter(t=>t.provider===scope.provider&&t.gameId===scope.game&&t.achievementId===row.id);const source=safeUrl(row.sourceUrl);
      $('achievements-page').innerHTML=`<div class="achievement-detail-top rarity-${rarityTier(row.rarity)} ${earned(row)?'earned':''}">${icon(row)}<div><span class="achievement-state ${row.progressKnown&&row.unlocked?'earned-state':''}">${row.progressKnown&&row.unlocked?'✓ ':''}${state(row)}</span>${row.progressKnown&&row.unlocked?`<p class="hint">${row.unlockUtc?'Earned '+esc(date(row.unlockUtc)):'Earned · Date unavailable'}</p>`:''}${row.rarity!=null?`<p class="hint">${rarityPercent(row.rarity)}% of players</p>`:''}</div></div><div class="achievement-assignment"><div><span class="eyebrow">GROUPS</span><strong>${esc(groupLabel(row.groups))}</strong>${item.groupSync==='conflict'?'<small>This assignment changed. Choose which version to keep.</small>':item.groupSync==='pending'?'<small>Changes pending</small>':''}${item.groupSync==='conflict'?`<small>Other assignment: ${esc(groupLabel(item.desktopGroups))}</small>`:''}</div><button id="achievement-edit-groups" class="quiet">${item.groupSync==='conflict'?'Resolve groups':'Change groups'}</button></div><div class="achievement-tags">${row.missable?'<span class="missable">Missable</span>':''}${row.tags.split(',').map(s=>s.trim()).filter(Boolean).map(s=>`<span>${esc(s)}</span>`).join('')}</div>${row.counterTarget>0&&row.counterCurrent!=null?`<div class="achievement-counter"><strong>${esc(row.counterCurrent)} / ${esc(row.counterTarget)}</strong><span class="hint">Progress</span>${progressBar(row.counterCurrent,row.counterTarget)}</div>`:''}<p class="achievement-map-link"><a class="quiet" href="#maps/${encodeURIComponent(scope.provider)}/${encodeURIComponent(scope.game)}/achievement/${encodeURIComponent(row.centuryId||row.id)}">View linked maps</a></p><section class="achievement-guide" aria-labelledby="achievement-guide-title"><div class="achievement-guide-heading"><h2 id="achievement-guide-title">${row.centuryId?'Requirements':'Guide'}</h2>${row.centuryId?'':`<span>${row.guide?'':'No guide yet'}</span>`}</div><div class="guide-prose">${guideHTML(row.guide)}</div>${source?`<p class="achievement-guide-source">Source: <a href="${esc(source)}" target="_blank" rel="noopener noreferrer">${esc(row.sourceTitle||new URL(source).hostname)} ↗</a></p>`:''}</section>${linked.length?`<section class="achievement-linked"><h2>Track your progress</h2>${linked.map(t=>trackerCard(t)).join('')}</section>`:''}`;
      if(row.centuryId){
        const assignment=$('achievements-page').querySelector('.achievement-assignment');
        assignment.innerHTML=`<div><span class="eyebrow">CENTURY ACHIEVEMENTS</span><strong>${esc(row.points)} PV pts</strong></div>`;
      }else $('achievement-edit-groups').addEventListener('click',openGroups);
      images();paintBars();
    }
    function showCustomGroup(select){
      const input=$(select.id+'-custom');
      input.disabled=select.selectedOptions[0]?.dataset.custom!=='true';
      input.parentElement.hidden=input.disabled;
      return input;
    }
    function fillGroupSelect(id,names,current,emptyLabel,customLabel){
      const select=$(id);
      const options=[...new Set([...names,current])].filter(name=>name&&name.toLowerCase()!==emptyLabel.toLowerCase());
      const custom=new Option(customLabel,'');
      custom.dataset.custom='true';
      select.replaceChildren(new Option(emptyLabel,''),...options.map(name=>new Option(name,name)),custom);
      select.value=options.includes(current)?current:'';
      showCustomGroup(select).value='';
    }
    function groupValue(id){
      const custom=$(id+'-custom');
      return custom.disabled?$(id).value:custom.value.trim();
    }
    function openGroups(){
      editing=detail();
      requestId=newRequestId();
      const {category,dlc}=editing.achievement.groups;
      fillGroupSelect('achievement-category',snapshot.categories,category,'Uncategorized','Add a new category…');
      fillGroupSelect('achievement-dlc',snapshot.dlcs,dlc,'Base Game','Add a new DLC / expansion…');
      $('achievement-group-status').textContent=editing.groupSync==='conflict'?`Desktop: ${groupLabel(editing.desktopGroups)}. Your saved web assignment is shown below.`:'Changes sync with the desktop when your PC is connected. Guide text and achievement progress stay unchanged.';
      $('achievement-use-desktop').hidden=editing.groupSync!=='conflict';$('achievement-group-save').textContent=editing.groupSync==='conflict'?'Keep web groups':'Save groups';$('achievement-group-dialog').showModal();
    }
    async function saveGroups(useDesktop=false){
      if(busy)return;
      const groups=useDesktop?editing.desktopGroups:{
        category:groupValue('achievement-category'),dlc:groupValue('achievement-dlc'),sortOrder:editing.achievement.groups.sortOrder
      };
      if(!useDesktop){
        const blankCustom=['achievement-category','achievement-dlc'].map(id=>$(id+'-custom')).find(input=>!input.disabled&&!input.value.trim());
        if(blankCustom){
          $('achievement-group-status').textContent='Enter a name for the new group.';
          blankCustom.focus();
          return;
        }
      }
      busy=true;$('achievement-group-save').disabled=true;$('achievement-use-desktop').disabled=true;
      try{
        const value=await api(`${path()}/${encodeURIComponent(openedId)}/groups`,{version:editing.version,requestId,groups,useDesktop});
        snapshot.rows=snapshot.rows.map(r=>r.achievement.id===openedId?value:r);
        const saved=value.achievement.groups;
        if(saved.category&&!snapshot.categories.includes(saved.category))snapshot.categories.push(saved.category);
        if(saved.dlc&&!snapshot.dlcs.includes(saved.dlc))snapshot.dlcs.push(saved.dlc);
        $('achievement-group-dialog').close();
        renderDetail();
      }catch(error){
        if(error.status===409){try{const latest=await api(`${path()}/${encodeURIComponent(openedId)}`);editing=latest;requestId=newRequestId();$('achievement-group-status').textContent=`The saved assignment changed to ${groupLabel(latest.achievement.groups)}. Your draft is kept. Review it and choose Save my assignment to apply it.`;$('achievement-group-save').textContent='Save my assignment';}catch{$('achievement-group-status').textContent=error.message;}}
        else $('achievement-group-status').textContent=error.message;
      }finally{busy=false;$('achievement-group-save').disabled=false;$('achievement-use-desktop').disabled=false;}
    }
    $('achievement-group-form').addEventListener('submit',event=>{event.preventDefault();saveGroups();});$('achievement-use-desktop').addEventListener('click',()=>saveGroups(true));$('achievement-group-cancel').addEventListener('click',()=>{if(!busy)$('achievement-group-dialog').close();});$('achievement-group-dialog').addEventListener('cancel',event=>{if(busy)event.preventDefault();});
    $('achievement-group-form').addEventListener('input',()=>requestId=newRequestId());
    for(const id of ['achievement-category','achievement-dlc']){
      $(id).addEventListener('change',event=>{
        const custom=showCustomGroup(event.currentTarget);
        if(!custom.disabled)custom.focus();
      });
    }
    return {
      get active(){return active;},get busy(){return busy;},
      reset(){generation++;if(active&&scope)savedViews.set(scope.provider+scope.game,view);active=false;clearHeadingRarity();$('page-heading').classList.remove('achievement-heading');$('achievements-page').hidden=true;if($('achievement-group-dialog').open&&!busy)$('achievement-group-dialog').close();},
      async open(parts){
        active=true;scope={provider:parts[1],game:parts[2],artworkPending:true};openedId=parts[0]==='achievement'?parts[3]:undefined;
        view={...(savedViews.get(scope.provider+scope.game)||defaultsView()),layout:preferredLayout};
        const expected=location.hash,current=++generation,key=JSON.stringify([getWorkspace(),scope.provider,scope.game]),cached=snapshot&&snapshotKey===key;
        if(cached){scope.artworkPending=false;render();}else{heading('Achievements',gameName());$('achievements-page').hidden=false;$('achievements-page').innerHTML='<p class="hint" role="status">Loading achievements…</p>';}
        try{const incoming=await api(path());if(active&&generation===current&&location.hash===expected){snapshot=incoming;snapshotKey=key;render();await prepareArtwork(incoming);if(!active||generation!==current||location.hash!==expected)return;scope.artworkPending=false;$('achievements-page').querySelectorAll('img[data-artwork-pending]').forEach(img=>{delete img.dataset.artworkPending;img.hidden=false;img.src=scope.artwork?.get(img.dataset.artworkUrl)||img.dataset.artworkUrl;});}}
        catch(error){if(active&&generation===current&&location.hash===expected){
          if(cached){status('Could not refresh achievements. Your saved view is kept.',true);return;}
          heading('Achievements',gameName());$('achievements-page').hidden=false;$('achievements-page').innerHTML=`<div class="achievement-empty"><h2>Achievements aren’t available for this platform yet.</h2><p>${error.status===404?'Connect this platform’s account in the desktop app, then sync its achievements.':esc(error.message)}</p></div>`;
        }}
      },
      async refresh(){if(!active||busy||$('achievement-group-dialog').open)return;const expected=location.hash,current=generation;const next=await api(path());if(active&&generation===current&&location.hash===expected&&!$('achievement-group-dialog').open&&!document.activeElement?.matches('input,select,textarea')&&JSON.stringify(next)!==JSON.stringify(snapshot)){snapshot=next;render();}},
      fallback(){return active&&scope?openedId?`achievements/${scope.provider}/${scope.game}`:`game/${scope.provider}/${scope.game}`:null;}
    };
  }
  const api={achievementCard,totals,matches,organize,completionSections,centurySection,rarityTier,guideHTML,safeUrl,create};if(typeof module!=='undefined'&&module.exports)module.exports=api;else root.PixelVaultAchievements=api;
})(typeof globalThis!=='undefined'?globalThis:this);
