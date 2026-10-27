(function(root){
  'use strict';
  const esc=v=>String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  function dayLabel(day){return day&&/^\d{4}-\d{2}-\d{2}$/.test(day)?new Intl.DateTimeFormat(undefined,{dateStyle:'long',timeZone:'UTC'}).format(new Date(day+'T12:00:00Z')):'Date unavailable';}
  function groupDays(photos){const days=new Map();for(const p of photos){const day=p.captureDay||'unknown';if(!days.has(day))days.set(day,[]);days.get(day).push(p);}return [...days].map(([day,photos])=>({day,photos}));}
  const photoKey=p=>JSON.stringify([p.provider??'',p.gameId??'',p.id]);
  function photoSections(photos,grouped=false){
    return groupDays(photos).map(({day,photos})=>{
      const groups=new Map();for(const p of photos){const name=grouped&&p.group?.trim()?p.group:null,key=JSON.stringify(grouped&&p.provider?[day,p.provider,p.gameId,name]:[day,name]);if(!groups.has(key))groups.set(key,{name,key,title:grouped?p.title:undefined,photos:[]});groups.get(key).photos.push(p);}
      return {day,groups:[...groups.values()]};
    });
  }
  const timelineHash=p=>`timeline-photo/${encodeURIComponent(p.provider)}/${encodeURIComponent(p.gameId)}/${encodeURIComponent(p.id)}`;
  const timelineRows=feed=>feed.photos.map(p=>({...p.photo,provider:p.provider,gameId:p.gameId,title:p.title}));
  const recentHash=p=>`recent-photo/${encodeURIComponent(p.provider)}/${encodeURIComponent(p.gameId)}/${encodeURIComponent(p.id)}`;
  const recentRows=feed=>feed.photos.slice(0,30).map(p=>({...p.photo,provider:p.provider,gameId:p.gameId,title:p.title}));
  function photoGesture({dx,dy,maxX=Math.abs(dx),maxY=Math.abs(dy),duration,startX=100,width=390,zoom=1,multi=false}){
    if(multi||zoom>1.05||duration<40||duration>800)return null;
    if(dy>=90&&maxX<=45&&dy>Math.abs(dx)*2.5)return 'dismiss';
    if(startX>=24&&startX<=width-24&&Math.abs(dx)>=80&&maxY<=40&&Math.abs(dx)>Math.abs(dy)*2.5)return dx<0?'next':'previous';
    return null;
  }
  function mergePage(state,page,append){
    if(append&&state.meta&&state.meta.version!==page.version)throw new Error('The photo library changed. Refresh photos to continue.');
    const rows=append?[...state.rows]:[],seen=new Set(rows.map(photoKey));for(const p of page.photos)if(!seen.has(photoKey(p))){rows.push(p);seen.add(photoKey(p));}
    return {...state,rows,meta:page,nextOffset:page.nextOffset};
  }
  function squareLayout(count,width,density='comfortable',gap=6){
    if(!Number.isInteger(count)||count<1||!Number.isFinite(width)||width<=0)return {tiles:[],height:0,blocks:[]};
    gap=Math.max(0,Math.min(gap,width/12));
    const compact=density==='compact',minimum=compact&&width>=280?4:3;
    const target=Math.max(minimum,Math.min(compact?8:6,Math.floor((width+gap)/((compact?92:140)+gap))));
    let columns=target;while(columns>3&&2*columns-3>count)columns--;
    const tiles=[],blocks=[];let index=0,y=0,band=0;
    function block(cols,rows,cells,type){
      const unit=(width-gap*(cols-1))/cols,top=y,start=index;
      for(const {col,row,span} of cells)tiles.push({index:index++,x:col*(unit+gap),y:top+row*(unit+gap),size:span*unit+(span-1)*gap,col,row,span,block:blocks.length});
      const height=rows*unit+(rows-1)*gap;blocks.push({columns:cols,rows,top,height,start,count:cells.length,type});y+=height+gap;
    }
    while(count-index>=2*columns-3){
      const remaining=count-index;let accents=1;
      if(remaining-(2*columns-3)===1){if(columns>=4)accents=2;else break;}
      const starts=accents===2?[0,columns-2]:[band%2?columns-2:0],cells=[];
      for(let row=0;row<2;row++)for(let col=0;col<columns;col++){
        const big=starts.find(start=>col>=start&&col<start+2);
        if(big!==undefined){if(row===0&&col===big)cells.push({col,row,span:2});}
        else cells.push({col,row,span:1});
      }
      block(columns,2,cells,'mixed');band++;
    }
    const remaining=count-index;
    if(remaining){
      const rows=Math.ceil(remaining/target),base=Math.floor(remaining/rows),extra=remaining%rows;
      for(let row=0;row<rows;row++){const cols=base+(row<extra?1:0);block(cols,1,Array.from({length:cols},(_,col)=>({col,row:0,span:1})),'tail');}
    }
    return {tiles,height:y-gap,blocks};
  }
  const consoleIcon=name=>{const key=({'Steam':'steam','Xbox':'xbox','PS5':'ps5','PC':'pc','Switch':'switch','Emulation':'emulation'})[name];return key?`<img src="/platforms/${key}.png" alt="" width="24" height="24" aria-hidden="true">`:'<span aria-hidden="true">▧</span>';};
  function create({api,heading,getGames,navigate,back}){
    const $=id=>document.getElementById(id),cache=new Map();let workspace='',scope,state,layoutState,active=false,openedId,photo,epoch=0,ticket=0,busy=false,touch,suppressClick=0,recentView=false,recent,recentRequest,recentLoaded=0,recentGeneration=0,stripVersion,timelineView=false,moreObserver;
    const page=$('screenshots-page'),dialog=$('photo-viewer'),stage=$('photo-stage'),image=$('photo-image');
    const home=$('recent-photos'),strip=$('recent-strip');
    const gamePath=()=>`/screenshots/${encodeURIComponent(scope.provider)}/${encodeURIComponent(scope.game)}`;
    const path=()=>timelineView?'/screenshots/timeline':gamePath();
    const galleryHash=()=>timelineView?'timeline':`screenshots/${encodeURIComponent(scope.provider)}/${encodeURIComponent(scope.game)}`;
    const photoHash=p=>timelineView?timelineHash(p):`photo/${encodeURIComponent(scope.provider)}/${encodeURIComponent(scope.game)}/${encodeURIComponent(p.id)}`;
    const imageUrl=(p,kind)=>`/api/v1/screenshots/${encodeURIComponent(p.provider??scope.provider)}/${encodeURIComponent(p.gameId??scope.game)}/${encodeURIComponent(p.id)}/${kind}`;
    const title=()=>timelineView?(openedId?(photo?.title||state?.rows.find(p=>p.id===openedId&&p.provider===scope.provider&&p.gameId===scope.game)?.title||getGames().find(g=>g.provider===scope.provider&&g.gameId===scope.game)?.title||'Game screenshots'):'All games'):recentView?(state?.rows.find(p=>p.id===openedId&&p.provider===scope.provider&&p.gameId===scope.game)?.title||getGames().find(g=>g.provider===scope.provider&&g.gameId===scope.game)?.title||'Game screenshots'):(state?.meta?.title||getGames().find(g=>g.provider===scope.provider&&g.gameId===scope.game)?.title||'Game screenshots');
    const valid=version=>active&&epoch===version;
    const imageObserver=typeof IntersectionObserver==='undefined'?null:new IntersectionObserver(entries=>{
      if(!active||openedId)return;
      for(const entry of entries){if(!entry.target.isConnected)continue;const img=entry.target.querySelector('img[data-thumb-src]');if(!img)continue;
        if(entry.isIntersecting){if(!img.getAttribute('src')&&!img.dataset.failed)img.src=img.dataset.thumbSrc;}
        else img.removeAttribute('src');
      }
    },{rootMargin:'800px'});
    function watchImages(){if(imageObserver)page.querySelectorAll('.screenshot-tile:has(img[data-thumb-src])').forEach(tile=>imageObserver.observe(tile));}
    function recentFeed(){
      if(recent&&Date.now()-recentLoaded<60000)return Promise.resolve(recent);
      if(recentRequest)return recentRequest;
      const generation=recentGeneration;
      const request=api('/screenshots/recent?limit=30').then(feed=>{if(generation!==recentGeneration)throw new Error('Workspace changed.');recent={meta:feed,rows:recentRows(feed)};recentLoaded=Date.now();return recent;}).finally(()=>{if(recentRequest===request)recentRequest=undefined;});
      recentRequest=request;return request;
    }
    function stripArrows(){const max=strip.scrollWidth-strip.clientWidth;$('recent-left').disabled=strip.scrollLeft<=1;$('recent-right').disabled=strip.scrollLeft>=max-1;}
    function paintRecent(feed){
      if(!feed.rows.length){home.hidden=true;return;}
      home.hidden=false;$('recent-status').hidden=true;$('recent-retry').hidden=true;strip.hidden=false;
      if(stripVersion!==feed.meta.version){
        stripVersion=feed.meta.version;
        strip.innerHTML=`<a class="recent-library" href="#timeline" aria-label="View all photos"><svg class="recent-photo-icon" viewBox="0 0 24 24" width="28" height="28" fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true"><rect x="3" y="4" width="18" height="16" rx="3"/><circle cx="8" cy="9" r="1.5"/><path d="m3 16 5-5 4 4 3-3 6 6"/></svg><span>View all<br>photos</span></a>`+feed.rows.map(p=>`<a class="recent-photo" data-recent-id="${esc(p.id)}" href="#${recentHash(p)}" aria-label="Open ${esc(p.title)}, ${esc(p.console)} screenshot, ${esc(dayLabel(p.captureDay))}"><img src="${imageUrl(p,'thumbnail')}" alt="" width="480" height="480" loading="lazy" decoding="async"><span class="screenshot-thumb-error" hidden>Open photo</span></a>`).join('');
        strip.querySelectorAll('img').forEach(img=>img.addEventListener('error',()=>{img.hidden=true;img.nextElementSibling.hidden=false;},{once:true}));
      }
      root.requestAnimationFrame(stripArrows);
    }
    async function showHome(){
      const version=epoch;home.hidden=false;document.documentElement.classList.add('recent-home-open');
      if(recent)paintRecent(recent);
      try{const feed=await recentFeed();if(epoch===version&&!active)paintRecent(feed);}
      catch{if(epoch!==version||active)return;if(!recent){$('recent-status').hidden=false;$('recent-status').textContent='Photos could not be loaded.';$('recent-retry').hidden=false;}}
    }
    $('recent-left').addEventListener('click',()=>strip.scrollBy({left:-Math.max(120,strip.clientWidth*.8),behavior:root.matchMedia?.('(prefers-reduced-motion: reduce)').matches?'auto':'smooth'}));
    $('recent-right').addEventListener('click',()=>strip.scrollBy({left:Math.max(120,strip.clientWidth*.8),behavior:root.matchMedia?.('(prefers-reduced-motion: reduce)').matches?'auto':'smooth'}));
    strip.addEventListener('scroll',stripArrows,{passive:true});
    strip.addEventListener('keydown',event=>{if(event.target!==strip||!['ArrowLeft','ArrowRight'].includes(event.key))return;event.preventDefault();strip.scrollBy({left:event.key==='ArrowLeft'?-120:120,behavior:root.matchMedia?.('(prefers-reduced-motion: reduce)').matches?'auto':'smooth'});});
    root.addEventListener('resize',stripArrows);
    $('recent-retry').addEventListener('click',()=>{recentLoaded=0;$('recent-status').textContent='Loading photos…';$('recent-retry').hidden=true;void showHome();});
    let density='comfortable';try{if(localStorage.getItem('PixelVault.Companion.screenshot-density')==='compact')density='compact';}catch{}
    let grouped=false;try{grouped=localStorage.getItem('PixelVault.Companion.screenshot-grouped')==='true';}catch{}
    let layoutFrame=0,lastWidth=0;
    function rememberTile(){
      const tile=Array.from(page.querySelectorAll('[data-laid-out]')).find(el=>{const group=el.closest('.screenshot-group');if(group&&!group.open)return false;const r=el.getBoundingClientRect();return r.height>0&&r.bottom>0&&r.top<root.innerHeight;});
      return tile?{element:tile,top:tile.getBoundingClientRect().top}:null;
    }
    function layoutPhotos(preserve=true){
      if(!active||openedId||page.hidden)return;
      const anchor=preserve?rememberTile():null;
      page.querySelectorAll('.screenshot-grid').forEach(grid=>{
        const group=grid.closest('.screenshot-group');if(group&&!group.open)return;
        const width=grid.getBoundingClientRect().width;if(width<=0)return;
        const items=Array.from(grid.querySelectorAll('.screenshot-tile')),layout=squareLayout(items.length,width,density,6);
        grid.classList.add('screenshot-quilt');grid.style.height=layout.height+'px';
        for(const tile of layout.tiles){const el=items[tile.index];Object.assign(el.style,{left:tile.x+'px',top:tile.y+'px',width:tile.size+'px',height:tile.size+'px'});el.dataset.laidOut='true';el.classList.toggle('screenshot-large',tile.span===2);}
      });
      lastWidth=page.getBoundingClientRect().width;
      if(anchor&&anchor.element.isConnected){const delta=anchor.element.getBoundingClientRect().top-anchor.top;if(Math.abs(delta)>.5)root.scrollBy(0,delta);}
    }
    function requestLayout(){if(layoutFrame)root.cancelAnimationFrame(layoutFrame);layoutFrame=root.requestAnimationFrame(()=>{layoutFrame=0;layoutPhotos(true);});}
    if(typeof ResizeObserver!=='undefined')new ResizeObserver(()=>{const width=page.getBoundingClientRect().width;if(active&&!openedId&&width>0&&Math.abs(width-lastWidth)>.5)requestLayout();}).observe(page);
    else root.addEventListener('resize',requestLayout);
    function reset(){if(openedId&&state){state.lastPhoto=openedId;const last=state.rows.find(p=>p.id===openedId&&(!(recentView||timelineView)||p.provider===scope.provider&&p.gameId===scope.game));state.lastPhotoKey=last?photoKey(last):null;}if(busy&&state)state.interrupted=true;epoch++;ticket++;active=false;recentView=false;timelineView=false;moreObserver?.disconnect();imageObserver?.disconnect();page.querySelectorAll('img[data-thumb-src]').forEach(img=>img.removeAttribute('src'));busy=false;openedId=photo=touch=undefined;page.hidden=true;home.hidden=true;if(layoutFrame)root.cancelAnimationFrame(layoutFrame);layoutFrame=0;if(dialog.open)dialog.close();document.documentElement.classList.remove('photo-open','screenshot-gallery-open','recent-home-open');image.removeAttribute('src');image.onload=image.onerror=null;}
    function shell(){
      heading(timelineView?'Photo timeline':'Screenshots',timelineView?'All your games, in capture order.':title(),'YOUR MOMENTS');$('page-heading').classList.add('screenshot-heading');$('page-count').textContent=state.meta?`${state.meta.total} photos`:'';
      $('breadcrumbs').innerHTML=timelineView?'<a href="#">All games</a><span>/</span><span aria-current="page">Photo timeline</span>':`<a href="#">All games</a><span>/</span><a href="#game/${encodeURIComponent(scope.provider)}/${encodeURIComponent(scope.game)}">${esc(title())}</a><span>/</span><span aria-current="page">Screenshots</span>`;
      page.hidden=false;if(layoutState===state){counts();observeMore();watchImages();return;}imageObserver?.disconnect();layoutState=state;
      page.innerHTML='<div class="screenshot-toolbar"><div id="screenshot-consoles" data-native-scroll class="screenshot-consoles" role="group" aria-label="Filter screenshots by console"></div><div class="screenshot-actions"><label class="screenshot-density"><span>Tile size</span><select id="screenshot-density" aria-label="Photo tile size"><option value="comfortable">Comfortable</option><option value="compact">Compact</option></select></label><button id="screenshot-refresh" class="quiet" aria-label="Refresh photos"><span class="refresh-icon" aria-hidden="true">↻</span><span class="refresh-caption">Refresh photos</span></button></div></div><p id="screenshot-results" class="hint" aria-live="polite"></p><p id="screenshot-error" class="error" role="alert" hidden></p><div id="screenshot-days"></div><button id="screenshot-more" class="quiet load-more" hidden>Show more photos</button>';
      $('screenshot-density').value=density;$('screenshot-density').addEventListener('change',()=>{density=$('screenshot-density').value;try{localStorage.setItem('PixelVault.Companion.screenshot-density',density);}catch{}layoutPhotos(true);});
      const grouping=document.createElement('button');grouping.id='screenshot-grouping';grouping.className='quiet screenshot-grouping';grouping.textContent='Group photos';grouping.setAttribute('aria-pressed',String(grouped));$('screenshot-density').closest('label').before(grouping);
      grouping.addEventListener('click',()=>{grouped=!grouped;grouping.setAttribute('aria-pressed',String(grouped));try{localStorage.setItem('PixelVault.Companion.screenshot-grouped',String(grouped));}catch{}appendPhotos();});
      if(timelineView){
        $('screenshot-refresh').parentElement.classList.add('timeline-actions');
        const dates=document.createElement('label');dates.className='screenshot-density screenshot-date';dates.innerHTML='<span>Through date</span><input type="date" id="screenshot-date" aria-label="Photos on or before this date"><button type="button" class="quiet" id="screenshot-date-clear">All dates</button>';$('screenshot-refresh').before(dates);$('screenshot-date').value=state.through||'';
        $('screenshot-date').addEventListener('change',()=>{state.through=$('screenshot-date').value;load(true);});$('screenshot-date-clear').addEventListener('click',()=>{if(!state.through)return;state.through='';$('screenshot-date').value='';load(true);});
      }
      $('screenshot-refresh').addEventListener('click',()=>load(true));$('screenshot-more').addEventListener('click',()=>{state.autoError=false;load(false);});observeMore();
      if(state.meta){filters();appendPhotos();counts();}
    }
    function observeMore(){
      moreObserver?.disconnect();if(!timelineView||typeof IntersectionObserver==='undefined')return;
      moreObserver=new IntersectionObserver(entries=>{if(entries.some(e=>e.isIntersecting)&&active&&timelineView&&!openedId&&!busy&&!state.autoError&&state.meta&&state.nextOffset!==null)void load(false);},{rootMargin:'300px'});moreObserver.observe($('screenshot-more'));
    }
    function filters(){
      const consoles=timelineView?[...state.meta.consoles].sort((a,b)=>b.count-a.count||a.name.localeCompare(b.name)):state.meta.consoles;
      const box=$('screenshot-consoles'),names=[{name:'',count:state.meta.total},...consoles];
      const same=Array.from(box.querySelectorAll('button')).map(b=>b.dataset.console).join('|')===names.map(c=>c.name).join('|');
      if(!same){box.innerHTML=names.map(c=>`<button class="quiet console-filter" data-console="${esc(c.name)}" aria-pressed="false">${c.name?consoleIcon(c.name):'<span aria-hidden="true">▦</span>'}<span>${esc(c.name||'All')}</span><small>${c.count}</small></button>`).join('');box.querySelectorAll('button').forEach(button=>button.addEventListener('click',()=>{if(state.filter===button.dataset.console)return;state.filter=button.dataset.console;load(true);}));}
      box.querySelectorAll('button').forEach(button=>button.setAttribute('aria-pressed',String(button.dataset.console===state.filter)));
    }
    function appendPhotos(){
      const positions=new Map(state.rows.map((p,i)=>[photoKey(p),i+1]));
      const days=$('screenshot-days'),anchor=rememberTile(),tiles=new Map(Array.from(days.querySelectorAll('[data-photo-id]'),el=>[el.dataset.photoKey||el.dataset.photoId,el]));
      for(const {day,groups} of photoSections(state.rows,grouped)){
        let section=Array.from(days.children).find(el=>el.dataset.day===day);
        if(!section){section=document.createElement('section');section.className='screenshot-day';section.dataset.day=day;section.innerHTML=`<h2>${esc(dayLabel(day))}</h2>`;days.append(section);}
        if(section.dataset.grouped!==String(grouped)){section.querySelectorAll(':scope > :not(h2)').forEach(el=>el.remove());section.dataset.grouped=String(grouped);}
        for(const group of groups){
          let bucket=Array.from(section.children).find(el=>el.dataset.groupKey===group.key),grid;
          if(!bucket){
            bucket=document.createElement(grouped?'details':'div');bucket.dataset.groupKey=group.key;
            if(grouped){
              bucket.className='screenshot-group';bucket.open=!state.collapsed.has(group.key);bucket.innerHTML=`<summary><span>${group.title?esc(group.title)+' · ':''}${esc(group.name??'Ungrouped')}</span><small></small></summary>`;
              const collapsed=state.collapsed;bucket.addEventListener('toggle',()=>{if(!bucket.isConnected)return;if(bucket.open)collapsed.delete(group.key);else collapsed.add(group.key);requestLayout();});
            }
            grid=document.createElement('div');grid.className='screenshot-grid';bucket.append(grid);section.append(bucket);
          }else grid=bucket.querySelector('.screenshot-grid');
          if(grouped)bucket.querySelector('small').textContent=`${group.photos.length} loaded`;
          for(const p of group.photos){
            let tile=tiles.get(photoKey(p));
            if(!tile){tile=document.createElement('a');tile.className='screenshot-tile';tile.dataset.photoId=p.id;tile.dataset.photoKey=photoKey(p);tile.href='#'+photoHash(p);tile.setAttribute('aria-label',`Open ${p.title?p.title+', ':''}${p.console} screenshot, ${dayLabel(p.captureDay)}, photo ${positions.get(photoKey(p))}`);tile.innerHTML=`<img src="${imageUrl(p,'thumbnail')}" alt="" loading="lazy" decoding="async" width="480" height="480"><span class="screenshot-thumb-error" hidden>Open photo</span>`;}
            if(tile.parentElement!==grid)grid.append(tile);
          }
        }
      }
      days.querySelectorAll('img:not([data-watched])').forEach(img=>{img.dataset.watched='true';img.dataset.thumbSrc=img.getAttribute('src');img.addEventListener('error',()=>{if(!img.getAttribute('src'))return;img.dataset.failed='true';img.hidden=true;img.nextElementSibling.hidden=false;});});
      layoutPhotos(false);watchImages();
      if(anchor&&anchor.element.isConnected&&anchor.element.getBoundingClientRect().height>0)root.scrollBy(0,anchor.element.getBoundingClientRect().top-anchor.top);
    }
    function counts(){
      $('screenshot-results').textContent=state.meta?`${state.rows.length} of ${state.meta.filteredTotal} ${state.filter?state.filter+' ':''}screenshots${state.meta.total<state.meta.eligible?' · Recent selection':''}`:busy?'Loading photos…':'Photos unavailable. Try refreshing.';
      $('screenshot-more').hidden=state.nextOffset===null;$('screenshot-more').disabled=busy;$('screenshot-more').textContent=busy?'Loading…':'Show more photos';$('screenshot-refresh').disabled=busy;
      if(state.meta&&!state.rows.length&&!busy)$('screenshot-days').innerHTML=`<p class="muted">${timelineView?(state.meta.total?'No photos match these filters.':'No photos have been synced yet.'):'No screenshots for this console.'}</p>`;
    }
    async function load(fresh){
      const version=epoch,current=++ticket;busy=true;state.interrupted=false;state.autoError=false;$('screenshot-error').hidden=true;
      if(fresh){state.rows=[];state.nextOffset=0;$('screenshot-days').innerHTML='';}counts();if(state.meta)filters();
      try{
        const offset=fresh?0:state.nextOffset;if(offset===null)return;
        const query=`?offset=${offset}&limit=60${state.filter?'&console='+encodeURIComponent(state.filter):''}${timelineView&&state.through?'&through='+encodeURIComponent(state.through):''}${!fresh&&state.meta?'&version='+encodeURIComponent(state.meta.version):''}`;
        let result=await api(path()+query);if(!valid(version)||current!==ticket)return;if(timelineView)result={...result,eligible:result.total,photos:timelineRows(result)};
        Object.assign(state,mergePage(state,result,!fresh));filters();appendPhotos();$('page-count').textContent=`${state.meta.total} photos`;
      }catch(error){if(valid(version)&&current===ticket){state.autoError=true;$('screenshot-error').textContent=error.status===404?'Screenshots have not been synced for this game yet.':error.message;$('screenshot-error').hidden=false;}}
      finally{if(valid(version)&&current===ticket){busy=false;counts();if(!state.autoError)observeMore();}}
    }
    function showInfo(show){$('photo-info').hidden=!show;stage.setAttribute('aria-label',show?'Hide photo information':'Show photo information');}
    function neighbors(){const i=state.rows.findIndex(p=>p.id===openedId&&(!(recentView||timelineView)||p.provider===scope.provider&&p.gameId===scope.game));return {previous:i>0?state.rows[i-1]:null,next:i>=0&&i+1<state.rows.length?state.rows[i+1]:null};}
    function move(direction){const target=neighbors()[direction];if(target)navigate(recentView?recentHash(target):photoHash(target),true);}
    function closePhoto(){if(!dialog.open)return;dialog.close();document.documentElement.classList.remove('photo-open');back();}
    function viewer(row,version){
      if(!valid(version))return;photo=row;showInfo(false);$('photo-loading').hidden=false;$('photo-loading').textContent='Loading photo…';$('photo-retry').hidden=true;image.hidden=false;
      image.alt=`Screenshot from ${title()}`;image.onload=()=>{if(valid(version)&&openedId===row.id)$('photo-loading').hidden=true;};image.onerror=()=>{if(valid(version)&&openedId===row.id){image.hidden=true;$('photo-loading').textContent='This photo could not be loaded.';$('photo-retry').hidden=false;}};image.src=imageUrl(row,'preview');
      $('photo-info-content').innerHTML=`<h2>${esc(title())}</h2><p>${esc(row.console)} · ${esc(dayLabel(row.captureDay))}</p>${row.group?`<p>${esc(row.group)}</p>`:''}<p class="photo-dimensions">${row.width} × ${row.height}</p>`;
      const n=neighbors();$('photo-previous').disabled=!n.previous;$('photo-next').disabled=!n.next;
      $('photo-download')?.remove();$('photo-metadata')?.remove();
      if(row.original){const fragment=$('photo-download-template').content.cloneNode(true),link=fragment.querySelector('a');link.href=imageUrl(row,'original')+'?asset='+encodeURIComponent(row.original.asset);link.download=row.original.fileName;link.textContent=`↓ Download original (${(row.original.bytes/1024/1024).toFixed(1)} MB)`;$('photo-info').querySelector('.photo-navigation').prepend(fragment);}
      if(row.metadata){const link=document.createElement('a');link.id='photo-metadata';link.className='quiet';link.href=imageUrl(row,'metadata')+'?asset='+encodeURIComponent(row.metadata.asset);link.download=row.metadata.fileName;link.textContent='↓ Download metadata (.xmp)';$('photo-previous').before(link);}
      document.documentElement.classList.add('photo-open');if(!dialog.open)dialog.showModal();
    }
    dialog.addEventListener('cancel',event=>{event.preventDefault();closePhoto();});dialog.addEventListener('close',()=>{if(!dialog.open)document.documentElement.classList.remove('photo-open');});
    $('photo-previous').addEventListener('click',()=>move('previous'));$('photo-next').addEventListener('click',()=>move('next'));$('photo-retry').addEventListener('click',()=>{if(photo)viewer(photo,epoch);});
    stage.addEventListener('click',()=>{if(Date.now()>=suppressClick)showInfo($('photo-info').hidden);});
    stage.addEventListener('keydown',event=>{if(event.key==='Enter'||event.key===' '){event.preventDefault();showInfo($('photo-info').hidden);}});
    dialog.addEventListener('keydown',event=>{if(event.key==='ArrowLeft'||event.key==='ArrowRight'){event.preventDefault();move(event.key==='ArrowLeft'?'previous':'next');}});
    const zoom=()=>root.visualViewport?.scale||1;
    stage.addEventListener('touchstart',event=>{touch=undefined;if(event.touches.length!==1||zoom()>1.05)return;const p=event.touches[0];touch={x:p.clientX,y:p.clientY,maxX:0,maxY:0,time:event.timeStamp,epoch};},{passive:true});
    stage.addEventListener('touchmove',event=>{if(!touch)return;if(event.touches.length!==1||zoom()>1.05){touch=undefined;return;}const p=event.touches[0];touch.maxX=Math.max(touch.maxX,Math.abs(p.clientX-touch.x));touch.maxY=Math.max(touch.maxY,Math.abs(p.clientY-touch.y));if(event.cancelable)event.preventDefault();},{passive:false});
    stage.addEventListener('touchend',event=>{const start=touch;touch=undefined;if(!start||start.epoch!==epoch||event.touches.length||event.changedTouches.length!==1)return;const p=event.changedTouches[0],dx=p.clientX-start.x,dy=p.clientY-start.y;if(Math.max(Math.abs(dx),Math.abs(dy),start.maxX,start.maxY)>12)suppressClick=Date.now()+600;const gesture=photoGesture({dx,dy,maxX:Math.max(start.maxX,Math.abs(dx)),maxY:Math.max(start.maxY,Math.abs(dy)),duration:event.timeStamp-start.time,startX:start.x,width:root.innerWidth,zoom:zoom()});if(!gesture)return;if(event.cancelable)event.preventDefault();if(gesture==='dismiss')closePhoto();else move(gesture);},{passive:false});
    stage.addEventListener('touchcancel',()=>{touch=undefined;},{passive:true});
    return {
      get active(){return active;},get busy(){return busy;},
      showHome,
      reset(){reset();$('page-heading').classList.remove('screenshot-heading');},
      clear(){reset();recentGeneration++;recent=recentRequest=stripVersion=undefined;recentLoaded=0;strip.innerHTML='';strip.hidden=true;$('recent-status').textContent='Loading photos…';$('recent-status').hidden=false;$('recent-retry').hidden=true;cache.clear();layoutState=state=undefined;page.innerHTML='';},
      setWorkspace(id){if(id!==workspace){this.clear();workspace=id;}},
      fallback(){return active&&scope?recentView?'#':timelineView?(openedId?'timeline':'#'):openedId?galleryHash():`game/${encodeURIComponent(scope.provider)}/${encodeURIComponent(scope.game)}`:null;},
      async open(parts){
        const version=++epoch;active=true;scope={provider:parts[1],game:parts[2]};recentView=parts[0]==='recent-photo';timelineView=parts[0]==='timeline'||parts[0]==='timeline-photo';openedId=parts[0]==='photo'||parts[0]==='timeline-photo'||recentView?parts[3]:undefined;
        if(recentView){
          try{
            state=await recentFeed();if(!valid(version))return;paintRecent(state);
            let row=state.rows.find(p=>p.id===openedId&&p.provider===scope.provider&&p.gameId===scope.game);
            if(!row){row={...await api(path()+'/photos/'+encodeURIComponent(openedId)),provider:scope.provider,gameId:scope.game,title:title()};if(!valid(version))return;state={rows:[row],meta:state.meta};}
            viewer(row,version);
          }catch{if(valid(version)){$('recent-status').hidden=false;$('recent-status').textContent='This photo is unavailable. Choose another photo or return home.';home.hidden=false;}}
          return;
        }
        document.documentElement.classList.add('screenshot-gallery-open');const key=timelineView?'@timeline':scope.provider+':'+scope.game;
        if(!cache.has(key))cache.set(key,{filter:'',rows:[],meta:null,nextOffset:0,through:'',collapsed:new Set()});state=cache.get(key);shell();
        if(!state.meta)await load(true);else if(!openedId&&state.interrupted)await load(state.rows.length===0);if(!valid(version))return;
        if(openedId){try{const row=state.rows.find(p=>p.id===openedId&&(!timelineView||p.provider===scope.provider&&p.gameId===scope.game))||{...await api(gamePath()+'/photos/'+encodeURIComponent(openedId)),...(timelineView?{provider:scope.provider,gameId:scope.game,title:getGames().find(g=>g.provider===scope.provider&&g.gameId===scope.game)?.title}: {})};viewer(row,version);}catch(error){if(valid(version)){$('screenshot-error').textContent='This photo is unavailable. Return to the gallery or refresh photos.';$('screenshot-error').hidden=false;}}}
        else{layoutPhotos(false);if(state.lastPhoto)Array.from(page.querySelectorAll('[data-photo-id]')).find(el=>state.lastPhotoKey?el.dataset.photoKey===state.lastPhotoKey:el.dataset.photoId===state.lastPhoto)?.focus({preventScroll:true});}
      }
    };
  }
  const api={dayLabel,groupDays,photoSections,photoKey,timelineHash,timelineRows,recentRows,recentHash,photoGesture,mergePage,squareLayout,create};if(typeof module!=='undefined'&&module.exports)module.exports=api;else root.PixelVaultScreenshots=api;
})(typeof globalThis!=='undefined'?globalThis:this);
