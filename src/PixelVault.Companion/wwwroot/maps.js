(function(root){
  'use strict';
  let leafletLoading;
  function loadLeaflet(){
    if(leafletLoading)return leafletLoading;
    if(root.L&&document.querySelector('link[href*="leaflet.css"]'))return Promise.resolve();
    leafletLoading=new Promise((resolve,reject)=>{
      const scriptUrl=document.querySelector('meta[name="pixelvault-map-script"]')?.content;
      const styleUrl=document.querySelector('meta[name="pixelvault-map-style"]')?.content;
      if(!scriptUrl||!styleUrl){reject(new Error('Map assets are unavailable.'));return;}
      const style=document.createElement('link');style.rel='stylesheet';style.href=styleUrl;
      const script=document.createElement('script');script.src=scriptUrl;
      let loaded=0;const done=()=>{if(++loaded===2)resolve();};
      const fail=()=>{style.remove();script.remove();reject(new Error('Map assets could not load. Retry Maps.'));};
      style.onload=done;script.onload=done;style.onerror=script.onerror=fail;document.head.append(style,script);
    }).catch(error=>{leafletLoading=undefined;throw error;});return leafletLoading;
  }
  const encode=encodeURIComponent;
  const escape=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const mapHash=(provider,game,id)=>`#${id?'map':'maps'}/${encode(provider)}/${encode(game)}${id?'/'+encode(id):''}`;
  function geometry({width,height,maxZoom}){
    const scale=2**maxZoom;
    return {bounds:[[-height/scale,0],[0,width/scale]],point:(x,y)=>[-y*height/scale,x*width/scale]};
  }
  function validManifest(m){return m&&Number.isInteger(m.width)&&m.width>0&&Number.isInteger(m.height)&&m.height>0&&m.width*m.height<=100000000&&m.tileSize===512&&Number.isInteger(m.maxZoom)&&m.maxZoom>=0&&m.maxZoom<=15&&typeof m.tileUrl==='string'&&m.tileUrl.startsWith('/api/v1/maps/')&&!m.tileUrl.includes('..');}
  const pinStates={completed:'Completed',remaining:'Remaining',unknown:'Progress unknown',unavailable:'Progress unavailable',unlinked:'No linked item'};
  function pinState(pin,progress){return Object.hasOwn(pinStates,progress?.status)?progress.status:pin.trackerId&&pin.itemId?'unavailable':'unlinked';}
  function visiblePins(pins,progress,query='',filter='all'){
    const needle=query.trim().toLocaleLowerCase();
    return pins.filter(pin=>(!needle||`${pin.label} ${pin.detail||''}`.toLocaleLowerCase().includes(needle))&&
      (filter==='all'||filter==='completed'&&pinState(pin,progress.get(pin.id))==='completed'||filter==='remaining'&&pinState(pin,progress.get(pin.id))!=='completed'));
  }
  const shortLabel=(pin,index)=>/^#?\d{1,4}$/.test(pin.label)?pin.label.replace(/^#/,''):String(index+1);
  function create({api,heading,getGames,navigate}){
    const page=document.getElementById('maps-page'),views=new Map();
    let generation=0,scope,workspace='',map,observer,viewKey,failed=false,expanded=false,browserFullscreen=false,fullscreenListener,mapExpanded=false,mapBrowserFullscreen=false,mapFullscreenListener,mapFullscreenButton,mapFullscreenCanvas,mapFullscreenPortal,mapFullscreenPlaceholder;
    let manifest,geo,pinData,progressData=new Map(),progressVersion='',selectedId,query='',filter='all',rowLimit=100,refreshing;
    const markers=new Map();
    const el=selector=>page.querySelector(selector);
    function remember(){if(map&&viewKey){const center=map.getCenter();views.set(viewKey,{center:[center.lat,center.lng],zoom:map.getZoom(),selectedId,query,filter});if(views.size>40)views.delete(views.keys().next().value);}}
    function restoreMapFullscreen(){if(mapFullscreenCanvas&&mapFullscreenPlaceholder?.parentNode)mapFullscreenPlaceholder.parentNode.replaceChild(mapFullscreenCanvas,mapFullscreenPlaceholder);mapFullscreenPortal?.remove();mapFullscreenCanvas=mapFullscreenPortal=mapFullscreenPlaceholder=undefined;document.body.classList.remove('map-only-expanded');}
    function reset(){generation++;if(fullscreenListener)document.removeEventListener('fullscreenchange',fullscreenListener);if(mapFullscreenListener)document.removeEventListener('fullscreenchange',mapFullscreenListener);fullscreenListener=mapFullscreenListener=undefined;if(document.fullscreenElement===page||document.fullscreenElement===mapFullscreenCanvas)document.exitFullscreen?.().catch(()=>{});restoreMapFullscreen();browserFullscreen=mapBrowserFullscreen=false;remember();map?.off("moveend zoomend",remember);markers.clear();pinData=undefined;progressData=new Map();progressVersion='';selectedId=undefined;query='';filter='all';rowLimit=100;refreshing=undefined;manifest=undefined;geo=undefined;observer?.disconnect();observer=undefined;map?.remove();map=undefined;mapFullscreenButton=undefined;viewKey=undefined;scope=undefined;page.hidden=true;page.replaceChildren();expanded=mapExpanded=false;document.documentElement.classList.remove('map-expanded','map-only-expanded');}
    function clear(){reset();views.clear();workspace='';}
    function fallback(){return scope?.id?mapHash(scope.provider,scope.game):scope?`#game/${encode(scope.provider)}/${encode(scope.game)}`:'#';}
    function message(text,retry=false){
      const status=page.querySelector('.map-message');if(!status)return;
      status.textContent=text;status.hidden=!text;page.querySelector('[data-map-retry]').hidden=!retry;
    }
    function progressMessage(text){const node=el('.map-progress-message');if(node){node.textContent=text;node.hidden=!text;}}
    function pinDetails(visible){
      const pin=pinData?.pins.find(p=>p.id===selectedId),card=el('.map-pin-details');if(!card)return;
      card.hidden=!pin;el('.map-selection-hint').hidden=!!pin;if(!pin)return;
      const progress=progressData.get(pin.id),state=pinState(pin,progress);
      el('#map-pin-title').textContent=pin.label;
      el('.map-pin-description').textContent=pin.detail||'No description for this pin.';
      const status=el('.map-pin-status');status.textContent=pinStates[state]+(progress?.current!=null&&progress?.target>0?` · ${progress.current} / ${progress.target}`:'');status.className='map-pin-status pin-'+state;
      el('.map-pin-filtered').hidden=visible.some(p=>p.id===pin.id);
      for(const [kind,prefix] of [['tracker','#tracker/'],['achievement','#achievement/']]){
        const link=el(`[data-pin-${kind}]`),url=progress?.[kind+'Url'];link.hidden=!(typeof url==='string'&&url.startsWith(prefix));
        if(!link.hidden)link.href=url;else link.removeAttribute('href');
      }
    }
    function selectPin(id,pan=false){
      selectedId=id;renderPins();remember();
      const pin=pinData?.pins.find(p=>p.id===id);if(pan&&pin)map.panTo(geo.point(pin.x,pin.y),{animate:false});
    }
    function renderPins(){
      if(!map||!pinData)return;
      const visible=visiblePins(pinData.pins,progressData,query,filter),shown=new Set(visible.map(p=>p.id)),ids=new Set(pinData.pins.map(p=>p.id));
      for(const [id,record] of markers)if(!ids.has(id)){record.marker.remove();record.row.remove();markers.delete(id);}
      if(selectedId&&!ids.has(selectedId))selectedId=undefined;
      pinData.pins.forEach((pin,index)=>{
        const state=pinState(pin,progressData.get(pin.id)),label=shortLabel(pin,index);
        let record=markers.get(pin.id);
        if(!record){
          const symbol=document.createElement('span');symbol.className='map-pin-number';symbol.textContent=label;
          const marker=root.L.marker(geo.point(pin.x,pin.y),{keyboard:true,autoPanOnFocus:false,icon:root.L.divIcon({html:symbol,className:'map-marker',iconSize:[44,44],iconAnchor:[22,22]})});
          marker.on('click',()=>selectPin(pin.id));
          marker.on('keydown',event=>{if(event.originalEvent.key==='Enter'||event.originalEvent.key===' '){root.L.DomEvent.stop(event.originalEvent);selectPin(pin.id);}});
          const row=document.createElement('li'),button=document.createElement('button');button.type='button';button.className='map-pin-row';button.dataset.pinId=pin.id;
          button.onclick=()=>selectPin(pin.id,true);row.append(button);
          record={marker,row,button,symbol};markers.set(pin.id,record);
        }
        const point=geo.point(pin.x,pin.y),position=record.marker.getLatLng();if(position.lat!==point[0]||position.lng!==point[1])record.marker.setLatLng(point);
        record.symbol.textContent=label;
        if(shown.has(pin.id)){if(!map.hasLayer(record.marker))record.marker.addTo(map);}else record.marker.remove();
        const icon=record.marker.getElement();
        if(icon){icon.className='leaflet-marker-icon leaflet-zoom-animated leaflet-interactive map-marker pin-'+state+(selectedId===pin.id?' selected':'');icon.dataset.pinId=pin.id;icon.setAttribute('title',pin.label+' · '+pinStates[state]);icon.setAttribute('aria-label',pin.label+' · '+pinStates[state]);icon.setAttribute('aria-pressed',selectedId===pin.id);}
        record.marker.setZIndexOffset(selectedId===pin.id?1000:0);
        const signature=JSON.stringify([pin.label,pin.detail,label,state]);
        if(record.signature!==signature){record.button.innerHTML=`<span class="map-row-number pin-${state}" aria-hidden="true">${escape(label)}${state==='completed'?' ✓':''}</span><span><strong>${escape(pin.label)}</strong><small>${escape(pinStates[state])}</small><span class="map-row-preview">${escape((pin.detail||'').split('\n')[0])}</span></span>`;record.signature=signature;}
        record.button.setAttribute('aria-pressed',selectedId===pin.id);record.button.classList.toggle('selected',selectedId===pin.id);
      });
      const list=el('#map-pin-list'),rows=visible.slice(0,rowLimit).map(pin=>markers.get(pin.id).row);
      if(list.children.length!==rows.length||rows.some((row,i)=>list.children[i]!==row))list.replaceChildren(...rows);
      el('.map-pins-empty').hidden=visible.length>0;
      el('[data-pins-more]').hidden=visible.length<=rowLimit;
      const completed=pinData.pins.filter(p=>pinState(p,progressData.get(p.id))==='completed').length;
      el('#map-pin-count').textContent=`${visible.length} of ${pinData.pins.length} pins · ${completed} completed`;
      pinDetails(visible);
    }
    async function refresh(initialProgress){
      if(!scope?.id||!map||!manifest||refreshing===generation)return;
      const current=generation;refreshing=current;const base=`/maps/${encode(scope.provider)}/${encode(scope.game)}/${encode(scope.id)}`;
      const button=el('[data-pins-refresh]');button.disabled=true;
      try{
        if(!pinData){
          const data=await api(base);if(current!==generation)return;
          if(!Array.isArray(data.pins)||!data.map||data.map.id!==scope.id)throw new Error('Pins could not load. Retry saved progress.');
          pinData=data;renderPins();
        }
        if(initialProgress?.loadError)throw initialProgress.loadError;
        const progress=initialProgress??await api(base+'/progress');if(current!==generation)return;
        if(progress.workspaceId!==workspace||!Array.isArray(progress.pins))throw new Error('The workspace changed. Reopen Maps to load its saved progress.');
        if(progress.mapRevision!==pinData.map.revision){
          const [data,nextManifest]=await Promise.all([api(base),api(base+'/manifest')]);if(current!==generation)return;
          if(nextManifest.version!==manifest.version){await open(['map',scope.provider,scope.game,scope.id]);return;}
          if(!Array.isArray(data.pins)||data.map?.id!==scope.id||data.map.revision!==progress.mapRevision)throw new Error('This map is changing. Retry saved progress.');
          pinData=data;progressVersion='';
        }
        progressMessage('');
        if(progressVersion!==progress.version){progressData=new Map(progress.pins.map(p=>[p.id,p]));progressVersion=progress.version;renderPins();}
      }catch(error){
        if(current!==generation)return;
        progressData=new Map();progressVersion='';renderPins();progressMessage(error.status===401?'Sign in again to read saved progress.':error.message||'Saved progress is unavailable. Retry when connected.');return false;
      }finally{if(current===generation){refreshing=undefined;button.disabled=false;}}
    }
    async function open(parts){
      reset();const current=generation;const provider=parts[1],game=parts[2],id=parts[0]==='map'?parts[3]:undefined;
      const linked=!id&&['tracker','achievement'].includes(parts[3])&&parts[4]?{kind:parts[3],id:parts[4]}:null;
      scope={provider,game,id};page.hidden=false;
      const title=getGames().find(g=>g.provider===provider&&g.gameId===game)?.title||'Game';
      heading('Maps',id?'Drag to explore. Pinch or use the zoom buttons.':'Choose a region to explore.',title);
      document.getElementById('breadcrumbs').innerHTML=`<a href="#">All games</a><span>/</span><a href="#game/${encode(provider)}/${encode(game)}">${escape(title)}</a><span>/</span><a href="${mapHash(provider,game)}">Maps</a>`;
      page.innerHTML='<p class="map-message" role="status">Loading maps…</p><button class="quiet" data-map-retry hidden>Retry</button><div class="map-body"></div>';
      page.querySelector('[data-map-retry]').onclick=()=>open(parts);
      try{
        const base=`/maps/${encode(provider)}/${encode(game)}`;
        const [listing,loadedManifest,initialPins,,initialProgress]=await Promise.all([api(base+(linked?`?${linked.kind}=${encode(linked.id)}`:'')),id?api(base+'/'+encode(id)+'/manifest'):null,id?api(base+'/'+encode(id)):null,id?loadLeaflet():null,id?api(base+'/'+encode(id)+'/progress').catch(loadError=>({loadError})):null]);if(current!==generation)return;
        if(!listing.enabled){message('Maps are unavailable in this workspace.');return;}
        const maps=listing.maps||[];
        if(linked){const all=document.createElement('a');all.className='quiet map-show-all';all.href=mapHash(provider,game);all.textContent='Show all game maps';page.insertBefore(all,el('.map-body'));}
        if(!maps.length){message(linked?'No maps are linked here yet.':'No saved maps for this game yet.');return;}
        const body=page.querySelector('.map-body');
        if(!id){
          message('');body.innerHTML=`<div class="map-list">${maps.map(m=>`<a class="map-card" href="${mapHash(provider,game,m.id)}"><span class="map-card-symbol" aria-hidden="true">⌖</span><div><h2>${escape(m.title)}</h2><p>${escape(m.region||'Saved map')} · ${m.pixelWidth.toLocaleString()} × ${m.pixelHeight.toLocaleString()}</p></div><span aria-hidden="true">›</span></a>`).join('')}</div>`;return;
        }
        const selected=maps.find(m=>m.id===id);if(!selected){message('This map is no longer available. Choose another region.');return;}
        body.innerHTML=`<div class="map-toolbar"><a class="quiet" href="${mapHash(provider,game)}">‹ All maps</a><label>Region <select id="map-region">${maps.map(m=>`<option value="${escape(m.id)}" ${m.id===id?'selected':''}>${escape(m.region||m.title)}</option>`).join('')}</select></label><button class="quiet" data-map-fit>Reset view</button><button class="quiet" data-map-expand aria-pressed="false">Expand viewer</button></div><div class="map-pin-tools"><label>Search pins<input id="map-pin-search" type="search" placeholder="Name or description…" autocomplete="off"></label><label>Show<select id="map-pin-filter"><option value="all">All pins</option><option value="remaining">Remaining</option><option value="completed">Completed</option></select></label><button class="quiet" data-pins-refresh>Reload saved progress</button></div><p class="map-progress-message" role="status">Loading pins…</p><div class="map-workspace"><div class="map-viewport"><div id="map-canvas" data-native-scroll role="region" aria-label="${escape(selected.title)}. Arrow keys pan; plus and minus zoom; R resets the view."></div><p class="map-help">Drag to pan · Pinch or + / − to zoom · R to reset</p></div><aside class="map-pin-panel" aria-label="Map pins" data-native-scroll><p id="map-pin-count" class="hint" aria-live="polite"></p><p class="map-selection-hint">Choose a pin on the map or in the list.</p><section class="map-pin-details" aria-labelledby="map-pin-title" hidden><h3 id="map-pin-title"></h3><p class="map-pin-status"></p><p class="map-pin-filtered" hidden>This pin is hidden by your current filters.</p><p class="map-pin-description"></p><div class="map-pin-links"><button class="quiet" data-pin-locate>Show on map</button><a class="quiet" data-pin-tracker hidden>Open tracker</a><a class="quiet" data-pin-achievement hidden>Achievement guide</a></div></section><ul id="map-pin-list"></ul><p class="map-pins-empty" hidden>No pins match your search or filter.</p><button class="quiet" data-pins-more hidden>Show more pins</button></aside></div>`;
        page.querySelector('#map-region').onchange=event=>navigate(mapHash(provider,game,event.target.value));
        if(current!==generation)return;
        if(!Array.isArray(initialPins?.pins)||initialPins.map?.id!==id)throw new Error('Map pins are unavailable.');pinData=initialPins;
        manifest=loadedManifest;
        if(!validManifest(manifest))throw new Error('This map cannot be displayed. Try refreshing it.');
        if(!root.L)throw new Error('The map viewer could not load. Reload Companion and try again.');
        const canvas=page.querySelector('#map-canvas');mapFullscreenCanvas=canvas;geo=geometry(manifest);const bounds=root.L.latLngBounds(geo.bounds);
        viewKey=workspace+'\n'+provider+'\n'+game+'\n'+id+'\n'+manifest.version;
        map=root.L.map(canvas,{crs:root.L.CRS.Simple,minZoom:-4,maxZoom:manifest.maxZoom+2,zoomSnap:.25,attributionControl:false,maxBounds:bounds.pad(.3),maxBoundsViscosity:.8});
        const fullscreenControl=root.L.control({position:'topright'});
        fullscreenControl.onAdd=()=>{const container=root.L.DomUtil.create('div','leaflet-bar map-fullscreen-control');mapFullscreenButton=document.createElement('button');mapFullscreenButton.type='button';mapFullscreenButton.className='map-fullscreen-toggle';mapFullscreenButton.textContent='Fullscreen map';mapFullscreenButton.setAttribute('aria-label','Open map fullscreen');mapFullscreenButton.setAttribute('aria-pressed','false');container.append(mapFullscreenButton);root.L.DomEvent.disableClickPropagation(container);return container;};
        fullscreenControl.addTo(map);
        const fit=()=>{map?.fitBounds(bounds,{padding:[12,12],animate:false});};
        const saved=views.get(viewKey);selectedId=saved?.selectedId;query=saved?.query||'';filter=saved?.filter||'all';
        el('#map-pin-search').value=query;el('#map-pin-filter').value=filter;
        if(saved)map.setView(saved.center,saved.zoom,{animate:false});else fit();
        page.querySelector('[data-map-fit]').onclick=fit;
        const expandButton=page.querySelector('[data-map-expand]');
        const setExpanded=value=>{
          expanded=value;document.documentElement.classList.toggle('map-expanded',expanded);
          expandButton.textContent=expanded?'Exit expanded viewer':'Expand viewer';expandButton.setAttribute('aria-pressed',String(expanded));
          requestAnimationFrame(()=>map?.invalidateSize({pan:false}));
        };
        const setMapExpanded=(value,browserMode=false)=>{
          if(value&&!browserMode&&!mapFullscreenPortal){mapFullscreenPlaceholder=document.createComment('map fullscreen placeholder');canvas.before(mapFullscreenPlaceholder);mapFullscreenPortal=document.createElement('div');mapFullscreenPortal.className='map-fullscreen-portal';mapFullscreenPortal.append(canvas);document.body.append(mapFullscreenPortal);document.body.classList.add('map-only-expanded');}
          if(!value)restoreMapFullscreen();
          mapExpanded=value;mapBrowserFullscreen=value&&browserMode;document.documentElement.classList.toggle('map-only-expanded',value);
          if(mapFullscreenButton){mapFullscreenButton.textContent=value?'Exit map fullscreen':'Fullscreen map';mapFullscreenButton.setAttribute('aria-label',value?'Exit map fullscreen':'Open map fullscreen');mapFullscreenButton.setAttribute('aria-pressed',String(value));}
          requestAnimationFrame(()=>map?.invalidateSize({pan:false}));
        };
        const onFullscreenChange=()=>{
          if(document.fullscreenElement===page||browserFullscreen&&page.contains(document.fullscreenElement)){setExpanded(true);return;}
          if(browserFullscreen){browserFullscreen=false;setExpanded(false);}
        };
        fullscreenListener=onFullscreenChange;
        document.addEventListener('fullscreenchange',fullscreenListener);
        const onMapFullscreenChange=()=>{
          if(document.fullscreenElement===canvas){setMapExpanded(true,true);return;}
          if(mapBrowserFullscreen){setMapExpanded(false);}
        };
        mapFullscreenListener=onMapFullscreenChange;document.addEventListener('fullscreenchange',mapFullscreenListener);
        mapFullscreenButton.onclick=async event=>{
          event.preventDefault();event.stopPropagation();
          if(mapExpanded){if(mapBrowserFullscreen&&document.fullscreenElement===canvas){await document.exitFullscreen?.();return;}setMapExpanded(false);return;}
          if(canvas.requestFullscreen){try{mapBrowserFullscreen=true;await canvas.requestFullscreen();return;}catch{mapBrowserFullscreen=false;}}
          setMapExpanded(true);
        };
        expandButton.onclick=async()=>{
          if(expanded){
            if(browserFullscreen&&document.fullscreenElement===page){await document.exitFullscreen?.();return;}
            setExpanded(false);return;
          }
          if(page.requestFullscreen){
            try{browserFullscreen=true;await page.requestFullscreen();return;}
            catch{browserFullscreen=false;}
          }
          setExpanded(true);
        };
        canvas.addEventListener('keydown',event=>{
          if(event.key.toLowerCase()==='r'){event.preventDefault();fit();}
          if(event.key==='Escape'&&mapExpanded&&!mapBrowserFullscreen)setMapExpanded(false);
          else if(event.key==='Escape'&&expanded)page.querySelector('[data-map-expand]').click();
        });
        failed=false;
        const layer=root.L.tileLayer(manifest.tileUrl,{tileSize:512,minNativeZoom:0,maxNativeZoom:manifest.maxZoom,minZoom:-4,maxZoom:manifest.maxZoom+2,noWrap:true,bounds,keepBuffer:1});
        layer.on('loading',()=>{if(current===generation&&!failed)message('Loading map…');});
        layer.on('tileerror',()=>{if(current===generation){failed=true;message('Some map tiles could not load. Check your connection and retry.',true);}});
        layer.on('load',()=>{if(current===generation&&!failed)message('');});layer.addTo(map);
        observer=new ResizeObserver(()=>map?.invalidateSize({pan:false}));observer.observe(canvas);
        map.on('moveend zoomend',remember);
        el('#map-pin-search').oninput=event=>{query=event.target.value;rowLimit=100;renderPins();remember();};
        el('#map-pin-filter').onchange=event=>{filter=event.target.value;rowLimit=100;renderPins();remember();};
        el('[data-pins-more]').onclick=()=>{rowLimit+=100;renderPins();};
        el('[data-pins-refresh]').onclick=()=>refresh();
        el('[data-pin-locate]').onclick=()=>{query='';filter='all';el('#map-pin-search').value='';el('#map-pin-filter').value='all';selectPin(selectedId,true);canvas.scrollIntoView({block:'nearest'});canvas.focus({preventScroll:true});};
        renderPins();await refresh(initialProgress);
      }catch(error){if(current===generation)message(error.message||'The map could not load. Check your connection and retry.',true);}
    }
    return {open,refresh,reset,clear,fallback,setWorkspace(id){if(workspace!==id){clear();workspace=id;}},get active(){return !!scope;}};
  }
  const api={mapHash,geometry,validManifest,pinState,visiblePins,shortLabel,create};if(typeof module!=='undefined'&&module.exports)module.exports=api;else root.PixelVaultMaps=api;
})(typeof globalThis!=='undefined'?globalThis:this);
