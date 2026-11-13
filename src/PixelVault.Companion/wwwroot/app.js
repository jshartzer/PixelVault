'use strict';
let pollTimer, refreshStream, appliedPush, initialization=0, initializing=false, appliedVersions={}, viewVersions=new Map();
const $ = id => document.getElementById(id);
function closeSideMenu(){if($('side-menu').open)$('side-menu').close();}
$('menu-open').addEventListener('click',()=>{
  if(!session?.authenticated)return;
  $('menu-error').textContent='';$('side-menu').showModal();$('menu-open').setAttribute('aria-expanded','true');document.documentElement.classList.add('menu-open');
});
$('menu-close').addEventListener('click',closeSideMenu);
$('side-menu').addEventListener('close',()=>{$('menu-open').setAttribute('aria-expanded','false');document.documentElement.classList.remove('menu-open');});
$('side-menu').addEventListener('click',event=>{
  const box=$('side-menu').getBoundingClientRect();
  if(event.target===$('side-menu')&&(event.clientX<box.left||event.clientX>box.right||event.clientY<box.top||event.clientY>box.bottom))closeSideMenu();
});
let session, csrf, trackers = [], games = [], detail, profile, sectionOrder, sectionDraft, sectionSaving = false, limit = 60, completedLimit = 30, routeVersion = 0;
let trackerGroupOrder=PixelVaultLibrary.normalizeTrackerGroupOrder();
let libraryLayout={sort:'screenshot-newest',pins:[]},librarySaving=false;
let coverControlsVisible=true;
try{coverControlsVisible=localStorage.getItem('PixelVault.Companion.cover-controls')!=='hidden';}catch{/* Keep the default when browser storage is unavailable. */}
function applyCoverControlsVisibility(){
  $('games').classList.toggle('cover-controls-hidden',!coverControlsVisible);
  $('cover-controls-toggle').checked=coverControlsVisible;
  sizeGameCards();
}
function sizeGameCards(){
  const grid=$('games');if(grid.hidden)return;
  const width=parseFloat(getComputedStyle(grid).gridTemplateColumns);if(!Number.isFinite(width))return;
  const height=width*1.5+(coverControlsVisible?Math.max(14,width*.13)+44:width*.13);
  grid.style.setProperty('--game-card-height',height+'px');
}
if(typeof ResizeObserver!=='undefined')new ResizeObserver(sizeGameCards).observe($('games'));
applyCoverControlsVisibility();
$('cover-controls-toggle').addEventListener('change',()=>{
  coverControlsVisible=$('cover-controls-toggle').checked;applyCoverControlsVisibility();
  try{localStorage.setItem('PixelVault.Companion.cover-controls',coverControlsVisible?'shown':'hidden');}catch{/* The current view can still be changed without storage. */}
});
let pcConnection={paired:false,online:false,supported:[]},refreshJob,refreshingGroup;
const updateBatchDelay=3000;
let updateScope='',pendingUpdateSummary,visibleUpdateSummary,updateSummaryTimer,updateAcknowledgement=0;
let achievementUpdateCache=null,achievementUpdateVersion=null,seenAchievementVersions=null;
const libraryGroups=()=>PixelVaultLibrary.groupGames(games);
const completionShowcase=PixelVaultShowcase.create({getGames:()=>games});
const profileGroup=()=>libraryGroups().find(g=>g.id===profile?.groupId);
const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const pct = (done, total) => total ? Math.min(100, Math.max(0, done * 100 / total)) : 0;
const progressBar = (done, total) => `<div class="progress" role="meter" aria-label="Completion" aria-valuemin="0" aria-valuemax="${Math.max(total, 1)}" aria-valuenow="${done}"><div class="progress-value" data-percent="${pct(done, total)}"></div></div>`;
function paintBars() { document.querySelectorAll('[data-percent]').forEach(e => { e.style.width = `${Number(e.dataset.percent)}%`; }); }
async function api(path, body) {
  const requestPath=!body&&/^\/trackers\/[^/?]+$/.test(path)?path+'?compact=true':path;
  const response = await fetch('/api/v1' + requestPath, { credentials: 'same-origin', cache: 'no-store',
    headers: body ? {'Content-Type':'application/json','X-CSRF-TOKEN':csrf ?? ''} : {},
    method: body ? 'POST' : 'GET', body: body ? JSON.stringify(body) : undefined });
  if (!response.ok) {
    let message = response.status === 429 ? 'Too many attempts. Wait a minute and try again.' : 'The request could not be completed.';
    try { const error = await response.json(); message = error.error ?? message; } catch { /* A proxy may return non-JSON. */ }
    if (response.status === 401 && path !== '/login') {
      routeVersion++; session = {...session, authenticated:false}; showAuth(false); $('workspace').hidden = true;
      message = 'Your session expired. Sign in again.';
    }
    const failure = new Error(message); failure.status = response.status; throw failure;
  }
  const result=await response.json();
  if(result?.definition?.items&&result.states?.some(s=>s.itemId)){const items=new Map(result.definition.items.map(i=>[i.id,i]));result.states=result.states.map(s=>s.itemId?{...s,item:items.get(s.itemId)}:s);}
  return result;
}
function status(message, error = false) { $('status').hidden = !message; $('status').textContent = message; $('status').className = error ? 'error' : ''; }
const trackerCard=(t,platform='')=>PixelVaultTrackerIcons.card(t,{platform,readOnly:!!session?.readOnly});
const trackerImageUI=PixelVaultTrackerIcons.create({api,getTracker:id=>detail?.definition.id===id?detail.definition:trackers.find(t=>t.id===id),isReadOnly:()=>!!session?.readOnly,onSaved:(id,iconId)=>{
  const t=trackers.find(t=>t.id===id);if(t)t.iconId=iconId;
  if(detail?.definition.id===id)detail.definition.iconId=iconId;
}});
const centurionUI=PixelVaultCenturion.create({api,onChange:renderCenturionShelf});
function renderCenturionShelf(){
  if(!profile)return;
  const selected=PixelVaultLibrary.achievementEdition(profile,profileGroup()?.editions||[profile])||profile;
  centurionUI.render($('centurion-profile'),selected.provider,selected.gameId);
}
const achievementUI=PixelVaultAchievements.create({api,heading,progressBar,paintBars,status,getGames:()=>games,getTrackers:()=>trackers,getWorkspace:()=>updateScope,newRequestId:requestId,trackerCard});
const screenshotUI=PixelVaultScreenshots.create({api,heading,getGames:()=>games,navigate:(hash,replace)=>navigation.go(hash,replace),back:()=>navigation.back()});
const mapUI=PixelVaultMaps.create({api,heading,getGames:()=>games,navigate:hash=>navigation.go(hash)});
function showAuth(setup) {
  centurionUI.reset();
  stopRefreshStream();
  initialization++;initializing=false;clearInterval(pollTimer);appliedVersions={};viewVersions.clear();games=[];trackers=[];
  mapUI.clear();
  screenshotUI.clear();trackerImageUI.close();
  $('menu-open').hidden=true;
  updateScope='';pendingUpdateSummary=undefined;visibleUpdateSummary=undefined;clearTimeout(updateSummaryTimer);
  achievementUpdateCache=null;achievementUpdateVersion=null;seenAchievementVersions=null;
  $('navigate-back').hidden=true;
  document.querySelectorAll('dialog[open]').forEach(dialog=>dialog.close());
  $('auth').hidden = false; $('logout').hidden = true; $('token-field').hidden = !setup; $('token').required = setup;
  $('auth-title').textContent = setup ? 'Make yourself at home.' : 'Welcome back.';
  $('auth-intro').textContent = setup ? 'Create the owner account for your private Companion.' : 'Sign in to open your library.';
  $('submit').textContent = setup ? 'Create your account' : 'Sign in';
  $('password').autocomplete = setup ? 'new-password' : 'current-password';
  $('password').minLength = setup ? 12 : 1;
  $('auth-form').dataset.setup = String(setup);
}
async function initialize() {
  stopRefreshStream();
  const generation=++initialization;initializing=true;clearInterval(pollTimer);appliedVersions={};viewVersions.clear();
  achievementUpdateCache=null;achievementUpdateVersion=null;seenAchievementVersions=null;
  try {
    const bootstrap=await Promise.all([api('/session'),api('/antiforgery')]);if(generation!==initialization)return;
    session=bootstrap[0];csrf=bootstrap[1].token;status('');
    if(!session.authenticated){showAuth(session.setupRequired);return;}
    $('auth').hidden=true;$('auth-error').textContent='';$('logout').hidden=false;$('menu-open').hidden=false;
    $('workspace').hidden=false;$('sample-banner').hidden=!session.demoEnabled;
    const requiredReads=Promise.all([api('/games'),api('/preferences/game-sections'),api('/preferences/library'),api('/workspace'),api('/preferences/tracker-groups')]);
    const optional=Promise.allSettled([api('/trackers'),api('/refresh/connection')]);
    const required=await requiredReads;
    if(generation!==initialization)return;
    trackerGroupOrder=PixelVaultLibrary.normalizeTrackerGroupOrder(required[4]?.groups);
    const workspace=required[3];games=required[0];sectionOrder=required[1];libraryLayout=required[2];trackers=[];
    if(updateScope&&updateScope!==workspace.id){
      pendingUpdateSummary=undefined;visibleUpdateSummary=undefined;clearTimeout(updateSummaryTimer);
      if($('refresh-summary').open)$('refresh-summary').close();
    }
    updateScope=workspace.id;session.workspaceMode=workspace.mode;
    $('workspace-label').textContent=workspace.mode==='direct-local'?'Saved on this PC.':session.demoEnabled?'Sample workspace · Read-only preview. Your desktop library is separate.':'Separate Companion workspace';
    screenshotUI.setWorkspace(updateScope);mapUI.setWorkspace(updateScope);
    $('game-sort').value=libraryLayout.sort;
    const platform=$('game-platform').value;
    $('game-platform').innerHTML='<option value="">All platforms</option>'+[...new Set(games.map(g=>g.platform))].sort().map(p=>`<option value="${escapeHtml(p)}">${escapeHtml(p)}</option>`).join('');
    $('game-platform').value=platform;
    await route({refreshLibrary:false});
    const extras=await optional;if(generation!==initialization)return;
    if(extras[0].status==='fulfilled')trackers=extras[0].value;
    if(extras[1].status==='fulfilled')pcConnection=extras[1].value;
    renderConnectionStatus();
    if(!unsafeToRefresh()){if(profile)renderGameProfile();else if(!location.hash.slice(1))renderGameGrid({preservePosition:true});}
    await checkForTrackerUpdates();
    if(generation!==initialization)return;
    startRefreshStream();
    pollTimer=setInterval(()=>refreshStream?.connected?undefined:pollConnection(),workspace.mode==='direct-local'?5000:15000);
  } catch(error){if(generation===initialization)status(error.message||'Cannot reach Companion. Check your connection and reload.',true);}
  finally{if(generation===initialization)initializing=false;}
}
$('auth-form').addEventListener('submit', async event => {
  event.preventDefault(); $('submit').disabled = true; $('auth-error').textContent = '';
  try {
    csrf = (await api('/antiforgery')).token;
    await api($('auth-form').dataset.setup === 'true' ? '/setup' : '/login',
      {token:$('token').value, name:$('name').value, password:$('password').value});
    $('password').value = ''; $('token').value = '';
    await initialize();
  } catch (error) { $('auth-error').textContent = error.message; }
  finally { $('submit').disabled = false; }
});
$('logout').addEventListener('click', async () => {
  $('logout').disabled=true;$('menu-error').textContent='';
  try { await api('/logout', {}); session = {...session, authenticated:false}; routeVersion++; $('workspace').hidden = true; navigation.reset(); await initialize(); }
  catch (error) { $('menu-error').textContent=error.message;status(error.message, true); }
  finally{$('logout').disabled=false;}
});
function heading(title, subtitle, eyebrow = 'KEEP YOUR PLACE') {
  $('tracker-heading-image').hidden=true;$('page-heading').classList.remove('has-tracker-image');
  $('page-title').textContent = title; $('page-subtitle').textContent = subtitle; $('eyebrow').textContent = eyebrow;
}
function trackerHeading(){
  const d=detail.definition;
  heading(d.title, d.description, d.gameTitle.toUpperCase());
  const trackerImage=$('tracker-heading-image');trackerImage.hidden=false;trackerImage.disabled=!!detail.readOnly;trackerImage.dataset.selectTrackerImage=d.id;trackerImage.dataset.trackerIconOwner=d.id;
  const restore=PixelVaultTrackerIcons.retainImages(trackerImage);
  trackerImage.innerHTML=`<span class="tracker-image" data-tracker-image="${escapeHtml(d.id)}">${PixelVaultTrackerIcons.image(d)}</span>`;restore();$('page-heading').classList.add('has-tracker-image');
  $('select-tracker-image').hidden=!!detail.readOnly;$('select-tracker-image').dataset.selectTrackerImage=d.id;
}
async function route({refreshLibrary=true}={}) {
  const version = ++routeVersion;
  if (!session?.authenticated) return;
  let parts,incomingGame,incomingTracker;
  try { parts = location.hash.slice(1).split('/').map(decodeURIComponent); }
  catch { status('That tracker link is invalid.', true); return; }
  // Prepare data before hiding the current screen. Parent game cards are already in the library.
  try{
    if(parts[0]==='game')incomingGame=games.find(g=>g.provider===parts[1]&&g.gameId===parts[2])||await api(`/games/${encodeURIComponent(parts[1])}/${encodeURIComponent(parts[2])}`);
    if(parts[0]==='tracker'){incomingTracker=await api('/trackers/'+encodeURIComponent(parts[1]));if(session?.workspaceMode==='direct-local')void api('/refresh/opened/'+encodeURIComponent(parts[1]),{}).catch(()=>{});}
    if(version!==routeVersion)return;
  }catch(error){if(version===routeVersion)status(error.message,true);return;}
  closeSideMenu();trackerImageUI.close();
  completionShowcase.close();$('showcase-open').hidden=true;
  if($('tracker-content').open)$('tracker-content').close();
  $('navigate-back').hidden=!location.hash.slice(1);
  status(''); captureDrafts(); detail = undefined; profile = undefined;achievementUI.reset();screenshotUI.reset();mapUI.reset();
  if ($('sections-dialog').open) $('sections-dialog').close();
  for (const id of ['games','game-profile','detail','empty','library-tools']) $(id).hidden = true;
  $('page-heading').hidden = false;
  $('breadcrumbs').innerHTML = ''; $('page-count').textContent = '';
  $('breadcrumbs').hidden = !location.hash.slice(1);
  if (!parts[0]) {
    renderHome();
    if(refreshLibrary){
      try{
        const [nextGames,nextTrackers,nextGroups]=await Promise.all([api('/games'),api('/trackers'),api('/preferences/tracker-groups')]);
        if(version!==routeVersion)return;
        trackerGroupOrder=PixelVaultLibrary.normalizeTrackerGroupOrder(nextGroups?.groups);games=nextGames;trackers=nextTrackers;
      }catch(error){if(version!==routeVersion)return;status('Could not refresh your library. Your previous library is kept.',true);}
    }
    $('empty').hidden=!!games.length;$('games').hidden=!games.length;$('library-tools').hidden=!games.length;
    renderGameGrid({preservePosition:true});$('page-count').textContent=`${libraryGroups().length} games`;
  } else if (parts[0] === 'game') {
    profile=incomingGame;
    renderGameProfile();
  } else if (parts[0] === 'achievements' || parts[0] === 'achievement') {
    await achievementUI.open(parts);
  } else if (parts[0] === 'recent-photo') {
    renderHome();await screenshotUI.open(parts);
  } else if (parts[0] === 'screenshots' || parts[0] === 'photo' || parts[0] === 'timeline' || parts[0] === 'timeline-photo') {
    await screenshotUI.open(parts);
  } else if (parts[0] === 'maps' || parts[0] === 'map') {
    await mapUI.open(parts);
  } else if (parts[0] === 'tracker') {
    detail=incomingTracker;
    const d = detail.definition;
    trackerHeading();
    $('breadcrumbs').innerHTML = `<a href="#">All games</a><span>/</span><a href="#game/${encodeURIComponent(d.provider)}/${encodeURIComponent(d.gameId)}">${escapeHtml(d.gameTitle)}</a><span>/</span><span aria-current="page">Tracker</span>`;
    renderSummary();
    $('detail').hidden = false; $('search').value = ''; $('filter').value = 'all'; limit = 60; completedLimit = 30; renderItems();
  } else status('That page is unavailable. Use the PixelVault link to return home.', true);
  paintBars();
}
function renderHome() {
    $('showcase-open').hidden=false;
    heading('Your games.', 'Choose a game to pick up where you left off.', 'YOUR COLLECTION');
    const count=libraryGroups().length;$('page-count').textContent = `${count} ${count === 1 ? 'game' : 'games'}`;
    if (!games.length) { $('empty').hidden = false; return; }
    $('games').hidden = false; $('library-tools').hidden = false; renderGameGrid();
    void screenshotUI.showHome({refresh:true});
}
const renderedGameMarkup=new WeakMap();
let lastGameGridMarkup;
function updateGameGrid(markup,preservePosition){
  const grid=$('games');
  if(markup===lastGameGridMarkup)return;
  const current=[...grid.children],anchors=preservePosition&&window.scrollY>0?current.filter(card=>{
    const rect=card.getBoundingClientRect();return rect.bottom>0&&rect.top<window.innerHeight;
  }).map(card=>({id:card.dataset.gameCard,top:card.getBoundingClientRect().top})):[];
  const focused=grid.contains(document.activeElement)?document.activeElement:null;
  const focusedId=focused?.closest('[data-game-card]')?.dataset.gameCard;
  const existing=new Map(current.map(card=>[card.dataset.gameCard,card]));
  const template=document.createElement('template');template.innerHTML=markup;
  const retained=new Set(),nextCards=new Map();let cursor=grid.firstElementChild;
  for(const next of [...template.content.children]){
    const html=next.outerHTML,old=existing.get(next.dataset.gameCard);
    const card=old&&renderedGameMarkup.get(old)===html?old:next;
    if(card===next){
      const oldImage=old?.querySelector('.cover-frame img'),newImage=card.querySelector('.cover-frame img');
      if(oldImage&&newImage&&oldImage.getAttribute('src')===newImage.getAttribute('src'))newImage.replaceWith(oldImage);
      renderedGameMarkup.set(card,html);handleMissingArtwork(card);
    }
    if(card===cursor)cursor=cursor.nextElementSibling;else grid.insertBefore(card,cursor);
    retained.add(card);nextCards.set(card.dataset.gameCard,card);
  }
  for(const card of current)if(!retained.has(card))card.remove();
  if(focused&&!focused.isConnected)nextCards.get(focusedId)?.querySelector(focused.matches('.pin-game')?'.pin-game':'a')?.focus({preventScroll:true});
  for(const anchor of anchors){
    const card=nextCards.get(anchor.id);if(!card)continue;
    const delta=card.getBoundingClientRect().top-anchor.top;if(Math.abs(delta)>.5)window.scrollBy(0,delta);
    break;
  }
  lastGameGridMarkup=markup;
}
function renderGameGrid({preservePosition=false}={}) {
  sizeGameCards();
  const query = $('game-search').value.trim().toLocaleLowerCase(), platform = $('game-platform').value;
  const visible = PixelVaultLibrary.sortGames(libraryGroups().filter(g=>g.editions.some(e=>e.title.toLocaleLowerCase().includes(query)) && (!platform || g.platforms.includes(platform))),libraryLayout.sort,libraryLayout.pins);
  $('game-results').textContent = `${visible.length} ${visible.length === 1 ? 'game' : 'games'}`;
  const markup = visible.map((g,index)=>{
    const pinned=PixelVaultLibrary.isPinned(g,libraryLayout.pins),selected=platform?PixelVaultLibrary.platformChoices(g.editions).find(c=>c.edition.platform===platform)?.edition||g:g;
    const completion=PixelVaultLibrary.completionTier(platform?selected:g);
    const label=escapeHtml(g.title+' · '+g.platforms.join(' · '));
    return `<article data-game-card="${escapeHtml(g.id)}" aria-label="${escapeHtml(g.title)}" class="game-card cover-card completion-${completion} ${pinned?'is-pinned':''}"><a href="#game/${encodeURIComponent(selected.provider)}/${encodeURIComponent(selected.gameId)}" aria-label="${label}" title="${label}"><div class="completion-cover"><div class="cover-frame"><span class="cover-fallback" aria-hidden="true">${escapeHtml(g.title.split(/\s+/).map(w=>w[0]).slice(0,3).join(''))}</span>${g.coverUrl ? `<img src="${escapeHtml(g.coverUrl)}" alt="" loading="${index<6?'eager':'lazy'}" fetchpriority="${index<6?'high':'auto'}" decoding="async" width="480" height="720">` : ''}</div>${PixelVaultLibrary.completionDecoration(completion)}</div></a><div class="cover-actions">${PixelVaultLibrary.coverProgressBadges(platform?selected:g,trackers)}<button class="pin-game" data-game="${escapeHtml(g.id)}" aria-label="${pinned?'Unpin':'Pin'} ${escapeHtml(g.title)}" aria-pressed="${pinned}" ${librarySaving?'disabled':''} title="${pinned?'Unpin':'Pin to top'}"><span aria-hidden="true">${pinned?'★':'☆'}</span></button></div></article>`;
  }).join('') || '<p class="muted">No games match your search.</p>';
  updateGameGrid(markup,preservePosition);
}
function handleMissingArtwork(root) { root.querySelectorAll('img').forEach(img=>img.addEventListener('error',()=>{img.hidden = true;},{once:true})); }
function stat(label,value) { return `<div class="game-stat"><span>${escapeHtml(label)}</span><strong>${escapeHtml(value)}</strong></div>`; }
function formatPlaytime(minutes) { return minutes < 60 ? `${minutes}m` : `${new Intl.NumberFormat(undefined,{maximumFractionDigits:1}).format(minutes/60)}h`; }
function renderGameProfile() {
  if(!profile)return;
  const restore=PixelVaultTrackerIcons.retainImages($('game-profile'));
  renderGameProfileContents();restore();
}
function renderGameProfileContents() {
  if(!profile)return;
  const group=profileGroup(),editions=group?.editions || [profile],g=PixelVaultLibrary.profilePresentation(profile,editions),s=g.stats || {};
  $('page-heading').hidden = true; $('game-profile').hidden = false;
  $('profile-maps').href=`#maps/${encodeURIComponent(g.provider)}/${encodeURIComponent(g.gameId)}`;
  $('breadcrumbs').innerHTML = `<a href="#">All games</a><span>/</span><span aria-current="page">${escapeHtml(g.title)}</span>`;
  $('game-hero').innerHTML = `<div class="profile-hero">${g.bannerUrl || g.coverUrl ? `<img src="${escapeHtml(g.bannerUrl || g.coverUrl)}" alt="" decoding="async" fetchpriority="high">` : ''}<div class="hero-shade"></div><div class="profile-title"><span class="platform-pill">${escapeHtml(g.platform || g.provider)}</span><h1>${escapeHtml(g.title)}</h1></div></div>`;
  handleMissingArtwork($('game-hero'));
  const choices=PixelVaultLibrary.platformChoices(editions);
  $('profile-platform-label').hidden=choices.length<2;
  $('profile-platform').innerHTML=choices.map(c=>`<option value="${escapeHtml(c.edition.provider+':'+c.edition.gameId)}" ${c.members.some(e=>e.provider===g.provider&&e.gameId===g.gameId)?'selected':''}>${escapeHtml(c.label)}</option>`).join('');
  renderRefreshStatus();
  const stats = [];
  const achievement=PixelVaultLibrary.achievementEdition(g,editions),achievementStats=achievement?.stats || {};
  const achievementLabel=achievement&&achievement.platform!==g.platform?`Achievements · ${achievement.platform || achievement.provider}`:'Achievements';
  if(s.playtimeMinutes !== null && s.playtimeMinutes !== undefined) stats.push(stat('Playtime',formatPlaytime(s.playtimeMinutes)));
  stats.push(stat(achievementLabel,achievementStats.achievementTotal !== null && achievementStats.achievementTotal !== undefined ? `${achievementStats.achievementUnlocked ?? '—'} / ${achievementStats.achievementTotal}` : achievement?'Not synced':editions.some(e=>['steam','retroachievements','xbox'].includes(e.provider))?'Choose a platform':'Not available'));
  if(g.total)stats.push(stat('Tracker goals',`${g.completed} / ${g.total}`));
  else if(s.captures !== null && s.captures !== undefined)stats.push(stat('Captures',String(s.captures)));
  $('game-stats').innerHTML = stats.join('');$('game-stats').setAttribute('aria-label','Saved game statistics');$('game-stats').title = 'Playtime and achievements are from your saved library snapshot. Tracker goals reflect saved Companion progress.';
  renderSections();renderCenturionShelf();void centurionUI.refresh(updateScope);
}
const sectionInfo = {
  trackers:{title:'Trackers',description:'Your next steps, all in one place.',icon:'✓'},
  achievements:{title:'Achievements',description:'Browse your unlocks and achievement progress.',icon:'◇'},
  screenshots:{title:'Screenshots',description:'Revisit the moments you captured.',icon:'▧'}
};
function renderSections() {
  if(!profile)return;
  const restore=PixelVaultTrackerIcons.retainImages($('game-sections'));
  const scope = profile.provider + ':' + profile.gameId;
  const wasOpen = $('game-sections').dataset.game === scope && !!$('game-sections').querySelector('.tracker-section[open]');
  $('game-sections').dataset.game = scope;
  const editions=profileGroup()?.editions || [profile];
  const matching = trackers.filter(t=>editions.some(e=>t.provider===e.provider&&t.gameId===e.gameId));
  const trackerSections=PixelVaultLibrary.trackerSections(matching,trackerGroupOrder);
  const gameTrackerCard=t=>trackerCard(t,editions.length>1?editions.find(e=>e.provider===t.provider&&e.gameId===t.gameId)?.platform || t.provider:'');
  const trackerSection=group=>`<section class="tracker-group tracker-group-${group.id}" data-tracker-group="${group.id}" aria-label="${group.title}"><h4 class="tracker-group-heading">${group.title}<span class="section-count">${group.count}</span></h4>${group.active.map(gameTrackerCard).join('')}${group.completed.length?`<h5 class="completed-heading"><span aria-hidden="true">✓</span> Completed <span>${group.completed.length}</span></h5>${group.completed.map(gameTrackerCard).join('')}`:''}</section>`;
  $('game-sections').innerHTML = (sectionOrder?.sections || Object.keys(sectionInfo)).map(id=>{
    const section = sectionInfo[id]; if(!section)return '';
    if(id==='achievements'){
      const selected=PixelVaultLibrary.achievementEdition(profile,editions);
      if(!selected){
        const message=editions.some(e=>['steam','retroachievements','xbox'].includes(e.provider))?'Choose a platform above to view its achievements.':'No achievement platform is linked to this game. You can link one in PixelVault.';
        return `<section class="profile-section coming-section" data-section="achievements"><div class="section-icon" aria-hidden="true">${section.icon}</div><div><h3>Achievements</h3><p>${message}</p></div></section>`;
      }
      return `<a class="profile-section achievement-section-link" data-section="achievements" href="#achievements/${encodeURIComponent(selected.provider)}/${encodeURIComponent(selected.gameId)}"><div class="section-icon" aria-hidden="true">${section.icon}</div><div><h3>Achievements</h3><p>Progress, saved groups and individual guides.${selected.provider!==profile.provider?' · '+escapeHtml(selected.platform||selected.provider):''}</p></div><span class="section-chevron" aria-hidden="true">›</span></a>`;
    }
    if(id==='screenshots'){
      const photos=editions.find(e=>e.screenshots)?.screenshots;
      if(photos)return `<a class="profile-section screenshot-section-link" data-section="screenshots" href="#screenshots/${encodeURIComponent(photos.provider)}/${encodeURIComponent(photos.gameId)}"><div class="section-icon" aria-hidden="true">${section.icon}</div><div><h3>Screenshots <span class="section-count">${photos.count}</span></h3><p>Your photos, organized by capture date.</p></div><span class="section-chevron" aria-hidden="true">›</span></a>`;
    }
    if(id !== 'trackers')return `<section class="profile-section coming-section" data-section="${id}"><div class="section-icon" aria-hidden="true">${section.icon}</div><div><h3>${section.title}</h3><p>${section.description}</p></div><span class="coming-label">Coming soon</span></section>`;
    return `<details class="profile-section tracker-section" data-section="trackers" ${wasOpen?'open':''}><summary class="section-title"><span class="section-icon" aria-hidden="true">${section.icon}</span><span><h3>Trackers <span class="section-count">${matching.length}</span></h3><span class="section-description">${matching.length?'Keep your progress moving.':'No trackers available yet.'}</span></span><span class="section-chevron" aria-hidden="true">›</span></summary><div class="tracker-list">${trackerSections.map(trackerSection).join('')}${matching.length?'':'<p class="section-empty">No trackers are available for this game yet.</p>'}</div></details>`;
  }).join('');restore();paintBars();
}
$('game-search').addEventListener('input',renderGameGrid);
$('game-platform').addEventListener('change',renderGameGrid);
$('profile-platform').addEventListener('change',()=>{
  const edition=profileGroup()?.editions.find(e=>e.provider+':'+e.gameId===$('profile-platform').value);
  if(edition)navigation.go(`game/${encodeURIComponent(edition.provider)}/${encodeURIComponent(edition.gameId)}`);
});
async function saveLibraryPreference(path,body,message){
  if(librarySaving)return;librarySaving=true;$('game-sort').disabled=true;renderGameGrid();
  try{libraryLayout=await api('/preferences/library/'+path,body);status(message);}
  catch(error){status(error.message,true);}
  finally{librarySaving=false;$('game-sort').disabled=false;$('game-sort').value=libraryLayout.sort;renderGameGrid();if(body.gameId)$('games').querySelector(`[data-game="${body.gameId}"]`)?.focus();}
}
$('game-sort').addEventListener('change',()=>saveLibraryPreference('sort',{sort:$('game-sort').value},'Sort saved.'));
$('games').addEventListener('click',event=>{
  const pin=event.target.closest('button[data-game]');if(!pin)return;
  const pinned=pin.getAttribute('aria-pressed')==='true';
  saveLibraryPreference('pins',{gameId:pin.dataset.game,pinned:!pinned},pinned?'Game unpinned.':'Game pinned to the top.');
});
function renderRefreshStatus(){
  if(!profile)return;const supported=pcConnection.supported.includes(profile.groupId),busy=refreshingGroup===profile.groupId;
  $('refresh-game').disabled=!supported || busy;
  $('refresh-game').textContent=busy?'Checking PC…':'Refresh from PC';
  $('refresh-game').title=supported?(pcConnection.local?'Read the configured local save files.':'Read the configured save files on your paired PC.'):pcConnection.paired||pcConnection.local?'No supported PC save source is configured for this game.':'The PC connection has not been paired.';
  const active=refreshJob?.groupId===profile.groupId;
  $('refresh-status').hidden=!supported && !active;
  $('refresh-status').textContent=active?refreshJob.message:pcConnection.online?'PC online · Ready to check its save files.':'PC offline · Turn it on to check its save files.';
  if(active&&refreshJob.canReview){const review=document.createElement('button');review.type='button';review.className='quiet';review.textContent='Review save changes';review.addEventListener('click',openRefreshReview);$('refresh-status').append(' ',review);}
  const pending=pcConnection.reviewJobs?.find(j=>j.groupId===profile.groupId);
  if(pending && (!active || !refreshJob.canReview || refreshJob.id!==pending.id)){
    $('refresh-status').hidden=false;$('refresh-status').textContent='Automatic updates need review. Your progress is kept. ';
    const review=document.createElement('button');review.type='button';review.className='quiet';review.textContent='Review save changes';review.addEventListener('click',async()=>{try{rememberRefresh(await api('/refresh/'+encodeURIComponent(pending.id)),pending.groupId);openRefreshReview();}catch(error){status(error.message,true);}});$('refresh-status').append(review);
  }
}
function timeSince(ticks){
  if(!ticks)return 'not yet';const ms=Math.max(0,Date.now()-(Number(ticks)-621355968000000000)/10000),minutes=Math.floor(ms/60000);
  return minutes<1?'just now':minutes<60?`${minutes}m ago`:minutes<1440?`${Math.floor(minutes/60)}h ago`:`${Math.floor(minutes/1440)}d ago`;
}
function renderConnectionStatus(){
  $('pc-status-bar').hidden=false;$('automatic-toggle').hidden=!(pcConnection.paired||pcConnection.local&&pcConnection.online);
  $('pc-status-dot').classList.toggle('online',!!pcConnection.online);
  $('pc-status-title').textContent=pcConnection.local?`PC ${pcConnection.online?'connected':'readers unavailable'} · Automatic updates ${pcConnection.automaticEnabled?'on':'paused'}`:pcConnection.paired?`PC ${pcConnection.online?'online':'offline'} · Automatic updates ${pcConnection.automaticEnabled?'on':'paused'}`:'PC not connected';
  const details=[];
  if(pcConnection.webTrackerEditsEnabled){if(pcConnection.trackerEditConflicts)details.push(pcConnection.trackerEditConflicts+' web edits need review on PC');else if(pcConnection.lastTrackerEditSyncUtc)details.push('Web edits synced to PC '+timeSince(pcConnection.lastTrackerEditSyncUtc));if(pcConnection.trackerEditsError)details.push(pcConnection.trackerEditsError);}
  if(pcConnection.lastSaveCheckUtc)details.push('Saves checked '+timeSince(pcConnection.lastSaveCheckUtc));
  if(pcConnection.lastLibraryCheckUtc)details.push('Library synced '+timeSince(pcConnection.lastLibraryCheckUtc));
  if(!details.length)details.push(pcConnection.local?'Using saved PC data.':pcConnection.paired?(pcConnection.online?'Waiting for the first automatic check.':'Showing saved NAS data.'):'Connect your PC to enable automatic updates.');
    if(pcConnection.sourceError)details.push('A save source is unavailable');if(pcConnection.libraryError)details.push('Library update pending');if(pcConnection.publicationError)details.push('Tracker sync pending');if(pcConnection.trackerTitleConflicts)details.push('Tracker name conflict: NAS names kept');if(pcConnection.achievementError)details.push('Achievement sync pending');
  if(pcConnection.reviewJobs?.length)details.push('Save changes need review');
  $('pc-status-detail').textContent=details.join(' · ');
  $('pc-status-bar').title=`Save progress updated ${timeSince(pcConnection.lastSaveUpdateUtc)}. Library changed ${timeSince(pcConnection.lastLibraryUpdateUtc)}.`;
  $('automatic-toggle').textContent=pcConnection.automaticEnabled?'Pause':'Resume';$('automatic-toggle').setAttribute('aria-label',pcConnection.automaticEnabled?'Pause automatic updates':'Resume automatic updates');
}
$('automatic-toggle').addEventListener('click',async()=>{
  $('automatic-toggle').disabled=true;
  try{await api('/refresh/automatic',{enabled:!pcConnection.automaticEnabled});pcConnection=await api('/refresh/connection');renderConnectionStatus();renderRefreshStatus();}
  catch(error){status(error.message,true);}finally{$('automatic-toggle').disabled=false;}
});
function rememberRefresh(job,groupId){refreshJob={...job,groupId};renderRefreshStatus();}
$('refresh-game').addEventListener('click',async()=>{
  if(!profile || refreshingGroup)return;const groupId=profile.groupId;refreshingGroup=groupId;renderRefreshStatus();
  const before=PixelVaultUpdates.snapshot(trackers);
  try{
    let job=await api('/refresh',{groupId,requestId:requestId()});rememberRefresh(job,groupId);
    while(job.status==='reading' && profile?.groupId===groupId && session?.authenticated){
      await new Promise(resolve=>setTimeout(resolve,1500));
      job=await api('/refresh/'+encodeURIComponent(job.id));rememberRefresh(job,groupId);
    }
    if(job.status!=='reading'){
      await refreshLists();
      if(profile?.groupId===groupId){profile=games.find(g=>g.provider===profile.provider&&g.gameId===profile.gameId)||profile;renderGameProfile();}
      await showTrackerUpdateSummary(PixelVaultUpdates.summary(before,trackers,job),{previous:before});
    }
  }catch(error){rememberRefresh({message:error.message,canReview:false},groupId);await showTrackerUpdateSummary({title:'Refresh unavailable',message:error.message,updated:[],review:[],failed:[]});}
  finally{refreshingGroup=undefined;renderRefreshStatus();}
});
async function openRefreshReview(){
  if(!refreshJob?.canReview)return;
  const {id,groupId}=refreshJob;
  try{
    const current=await api('/refresh/'+encodeURIComponent(id));
    if(refreshJob?.id!==id)return;
    if(!current.canReview)pcConnection.reviewJobs=pcConnection.reviewJobs?.filter(j=>j.id!==id);
    rememberRefresh(current,groupId);renderConnectionStatus();
  }
  catch(error){status(error.message,true);return;}
  if(!refreshJob.canReview){if($('refresh-review').open)$('refresh-review').close();return;}
  $('refresh-older').checked=false;$('refresh-review-status').textContent=refreshJob.message;
  $('refresh-older-label').hidden=!refreshJob.hasOlder;
  $('refresh-review-changes').innerHTML=refreshJob.tasks.filter(t=>t.status==='review').map(t=>`<section><h3>${escapeHtml(t.title)}</h3><p class="hint">${t.conflicts} manual conflicts · ${t.older} older observations</p><ul>${t.changes.map(c=>`<li><strong>${escapeHtml(c.name)}</strong>: saved ${c.previous??'unknown'} → incoming ${c.incoming??'unknown'}${c.manual!==null?' · your manual value '+c.manual:''}${c.older?' · older save':''}</li>`).join('')}</ul>${t.changes.length===80?'<p class="hint">Showing the first 80 differences.</p>':''}</section>`).join('');
  if(!$('refresh-review').open)$('refresh-review').showModal();
}
$('refresh-review-close').addEventListener('click',()=>$('refresh-review').close());
$('refresh-review-apply').addEventListener('click',async()=>{
  if(!refreshJob)return;const groupId=refreshJob.groupId;$('refresh-review-apply').disabled=true;
  const before=PixelVaultUpdates.snapshot(trackers);
  try{
    const job=await api('/refresh/'+encodeURIComponent(refreshJob.id)+'/review',{version:refreshJob.version,allowOlder:$('refresh-older').checked});rememberRefresh(job,groupId);
    if(job.canReview)openRefreshReview();else $('refresh-review').close();
    await refreshLists();if(profile?.groupId===groupId){profile=games.find(g=>g.provider===profile.provider&&g.gameId===profile.gameId)||profile;renderGameProfile();}
    if(!job.canReview)await showTrackerUpdateSummary(PixelVaultUpdates.summary(before,trackers,job),{previous:before});
  }catch(error){$('refresh-review-status').textContent=error.message;}
  finally{$('refresh-review-apply').disabled=false;}
});
function updateStorageKey(){return 'PixelVault.Companion.seen-trackers.'+updateScope;}
function readSeenTrackerVersions(){
  try{const stored=JSON.parse(localStorage.getItem(updateStorageKey()));return stored && typeof stored==='object' && !Array.isArray(stored)?stored:null;}catch{return null;}
}
function writeSeenTrackerVersions(versions){
  if(!updateScope)return;try{localStorage.setItem(updateStorageKey(),JSON.stringify(versions));}catch{/* Refresh summaries still work when browser storage is unavailable. */}
}
function acknowledgeTracker(id){
  const tracker=trackers.find(t=>t.id===id);if(!tracker?.updateVersion)return;
  const seen=readSeenTrackerVersions() || PixelVaultUpdates.snapshot(trackers);seen[id]=PixelVaultUpdates.snapshot([tracker])[id];writeSeenTrackerVersions(seen);
}
function achievementStorageKey(){return 'PixelVault.Companion.seen-achievements.'+updateScope;}
function currentAchievementVersion(){return pcConnection.versions?(pcConnection.versions.achievements??JSON.stringify([pcConnection.versions.library,pcConnection.versions.trackers])):null;}
function readSeenAchievementVersions(){
  if(seenAchievementVersions)return seenAchievementVersions;
  try{const saved=JSON.parse(localStorage.getItem(achievementStorageKey()));return saved&&typeof saved==='object'&&!Array.isArray(saved)?saved:null;}catch{return null;}
}
function writeSeenAchievementVersions(versions){
  if(!updateScope||!versions)return;
  seenAchievementVersions=versions;
  try{localStorage.setItem(achievementStorageKey(),JSON.stringify(versions));}catch{/* Keep the current session baseline when browser storage is unavailable. */}
}
async function readAchievementUpdates(force,seen){
  await centurionUI.refresh(updateScope);
  const scope=updateScope,generation=initialization;
  const version=currentAchievementVersion();
  try{
    const current=!force&&version!==null&&version===achievementUpdateVersion&&achievementUpdateCache?achievementUpdateCache:await api('/achievement-updates');
    if(scope!==updateScope||generation!==initialization)return {failed:true};
    const changes=PixelVaultUpdates.achievementChanges(seen,current).filter(change=>!change.id.startsWith('century.'));
    const gameKeys=[...new Set(changes.map(row=>JSON.stringify([row.provider,row.gameId])))];
    const metadata=new Map(await Promise.all(gameKeys.map(async key=>{
      const [provider,gameId]=JSON.parse(key);
      const game=await api('/achievements/'+encodeURIComponent(provider)+'/'+encodeURIComponent(gameId));
      return [key,new Map([...(game.rows||[]).map(row=>row.achievement),...(game.century?.definitions||[])].map(row=>[row.id,row]))];
    })));
    const unlocked=changes.map(change=>{
      const row=metadata.get(JSON.stringify([change.provider,change.gameId]))?.get(change.id);
      if(!row?.progressKnown||!row.unlocked)throw new Error('Achievement progress changed while checking updates.');
      const game=games.find(game=>game.provider===change.provider&&game.gameId===change.gameId);
      return {...change,title:row.title,gameTitle:game?.title||row.gameTitle||'Game '+change.gameId,
        platform:game?.platform||({steam:'Steam',xbox:'Xbox',retroachievements:'RetroAchievements'}[change.provider]||change.provider)};
    });
    if(scope!==updateScope||generation!==initialization)return {failed:true};
    achievementUpdateCache=current;achievementUpdateVersion=version;
    return {unlocked,versions:PixelVaultUpdates.achievementSnapshot(current,seen),failed:false};
  }catch{if(scope===updateScope&&generation===initialization)achievementUpdateVersion=null;return {unlocked:[],versions:null,failed:true};}
}
async function checkForTrackerUpdates(previous=null){
  await showTrackerUpdateSummary(null,{automatic:true,previous});
}
async function showTrackerUpdateSummary(summary,{automatic=false,previous=null}={}){
  if(!session?.authenticated || !updateScope)return;
  const scope=updateScope,generation=initialization,acknowledgement=updateAcknowledgement;
  const batch=visibleUpdateSummary||pendingUpdateSummary;
  const trackerBase=batch?batch.trackerBase:automatic?readSeenTrackerVersions()||previous:previous||readSeenTrackerVersions();
  const achievementBase=batch?batch.achievementBase:readSeenAchievementVersions();
  const achievements=await readAchievementUpdates(!automatic,achievementBase);
  if(!session?.authenticated||scope!==updateScope||generation!==initialization)return;
  // The user can dismiss the popup while its next snapshot is being read.
  if(acknowledgement!==updateAcknowledgement)return checkForTrackerUpdates(previous);
  const versions=PixelVaultUpdates.snapshot(trackers);
  const currentBatch=visibleUpdateSummary||pendingUpdateSummary;
  const changes=PixelVaultUpdates.summary(trackerBase,trackers);
  if(automatic){
    summary=currentBatch?{...currentBatch.summary,updated:changes.updated}:changes;
  }else if(currentBatch){
    summary={...summary,updated:changes.updated};
  }
  const unlocked=achievements.failed?currentBatch?.summary.achievements||[]:achievements.unlocked;
  const achievementVersions=achievements.versions||currentBatch?.achievementVersions;
  const progress=PixelVaultUpdates.progressSummary(summary,unlocked,achievements.failed);
  if(automatic&&!currentBatch&&!progress.updated.length&&!progress.achievements.length){
    writeSeenTrackerVersions(versions);writeSeenAchievementVersions(achievementVersions);
    return;
  }
  const next={summary:progress,scope,versions,achievementVersions,trackerBase,achievementBase,automatic,
    showAt:automatic?(currentBatch?.showAt??Date.now()+updateBatchDelay):Date.now()};
  if(visibleUpdateSummary&&$('refresh-summary').open){
    const changed=JSON.stringify(visibleUpdateSummary.summary)!==JSON.stringify(progress);
    visibleUpdateSummary=next;
    if(changed)renderUpdateSummary(progress);
  }else{
    pendingUpdateSummary=next;flushTrackerUpdateSummary();
  }
}
function flushTrackerUpdateSummary(){
  clearTimeout(updateSummaryTimer);
  if(!session?.authenticated||!pendingUpdateSummary||document.visibilityState==='hidden'||document.querySelector('dialog[open]'))return;
  const remaining=pendingUpdateSummary.showAt-Date.now();
  if(remaining>0||pendingUpdateSummary.automatic&&unsafeToRefresh()){
    updateSummaryTimer=setTimeout(flushTrackerUpdateSummary,remaining>0?remaining:500);return;
  }
  visibleUpdateSummary=pendingUpdateSummary;pendingUpdateSummary=undefined;
  renderUpdateSummary(visibleUpdateSummary.summary);
  $('refresh-summary').showModal();
}
function renderUpdateSummary(summary){
  $('refresh-summary-list').style.setProperty('--tracker-update-count',summary.updated.length);
  $('refresh-summary-title').textContent=summary.title;$('refresh-summary-message').textContent=summary.message;
  const trackerGroups=PixelVaultUpdates.groupByGame(summary.updated,games).map(group=>{
    const rows=group.trackers.map(renderTrackerUpdate).join('');
    return `<section class="update-game"><h4>${escapeHtml(group.title)}</h4><ul>${rows}</ul></section>`;
  }).join('');
  $('refresh-summary-list').innerHTML=(trackerGroups?`<section class="update-section" aria-label="Tracker updates"><div class="update-section-heading"><h3>Trackers</h3><span>${summary.updated.length} updated</span></div>${trackerGroups}</section>`:'')
    +(summary.achievements.length?`<section class="update-section update-achievements" aria-label="Newly unlocked achievements"><div class="update-section-heading"><h3>Achievements</h3><span class="update-unlocked-count">${summary.achievements.length} unlocked</span></div><ul>${summary.achievements.map(renderAchievementUpdate).join('')}</ul></section>`:'')
    +(summary.review.length?`<p class="hint">Needs review: ${summary.review.map(escapeHtml).join(', ')}. Your current progress is kept.</p>`:'')
    +(summary.failed.length?`<p class="hint">Could not check: ${summary.failed.map(escapeHtml).join(', ')}. Saved progress is kept.</p>`:'')
    +(summary.achievementCheckFailed?'<p class="hint">Achievement updates could not be checked. They will be checked again automatically.</p>':'');
  handleMissingArtwork($('refresh-summary-list'));
}
function renderUpdateTotal(totals,label){
  if(!Number.isInteger(totals?.completed)||!Number.isInteger(totals?.total)||totals.total<=0)return '';
  return `<span class="update-total" aria-label="${totals.completed} of ${totals.total} ${label}">${totals.completed}/${totals.total}</span>`;
}
function renderAchievementUpdate(achievement){
  const icon='/api/v1/achievements/'+encodeURIComponent(achievement.provider)+'/'+encodeURIComponent(achievement.gameId)+'/'+encodeURIComponent(achievement.id)+'/unlocked-icon';
  return `<li><span class="update-achievement-art" aria-hidden="true"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M8 21h8m-4-4v4M7 4h10v6a5 5 0 0 1-10 0V4ZM7 6H4v2a4 4 0 0 0 4 4m9-6h3v2a4 4 0 0 1-4 4"/></svg><img src="${escapeHtml(icon)}" alt="" loading="lazy" decoding="async"></span><div><strong>${escapeHtml(achievement.title)}</strong><small>${escapeHtml(achievement.gameTitle)} · ${escapeHtml(achievement.platform)}</small></div></li>`;
}
function renderTrackerUpdate(tracker){
  const title=escapeHtml(tracker.title);

  const total=renderUpdateTotal(tracker.totals,'complete');
  if(tracker.completed){
    return `<li class="update-completed"><span class="update-trophy" aria-hidden="true"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M8 21h8m-4-4v4M7 4h10v6a5 5 0 0 1-10 0V4ZM7 6H4v2a4 4 0 0 0 4 4m9-6h3v2a4 4 0 0 1-4 4"/></svg><span class="update-sparkle">✦</span></span><div class="update-tracker-name"><strong>${title}</strong> ${total}<span class="sr-only">Tracker completed</span></div></li>`;
  }
  const increase=Number.isInteger(tracker.increased)?`<span class="update-increase" aria-label="Progress increased by ${tracker.increased}">+${tracker.increased}</span>`:'';
  return `<li><span class="update-check" aria-hidden="true">✓</span><div class="update-tracker-name"><strong>${title}</strong> ${total}</div>${increase}</li>`;
}
function acknowledgeUpdateSummary(){
  if(visibleUpdateSummary?.scope===updateScope && session?.authenticated){
    writeSeenTrackerVersions(visibleUpdateSummary.versions);writeSeenAchievementVersions(visibleUpdateSummary.achievementVersions);
    updateAcknowledgement++;
  }
  visibleUpdateSummary=undefined;
}
$('refresh-summary').addEventListener('click',event=>{event.preventDefault();event.stopPropagation();acknowledgeUpdateSummary();$('refresh-summary').close();});
$('refresh-summary').addEventListener('cancel',acknowledgeUpdateSummary);
$('refresh-summary').addEventListener('close',()=>{if(!$('refresh-summary').open)visibleUpdateSummary=undefined;});
document.addEventListener('close',()=>queueMicrotask(flushTrackerUpdateSummary),true);
function renderSectionOrder() {
  if(!sectionDraft)return;
  $('section-order').innerHTML = sectionDraft.sections.map((id,index)=>`<li><span class="order-number">${index+1}</span><strong>${sectionInfo[id].title}</strong><div><button type="button" data-move="-1" data-section="${id}" aria-label="Move ${sectionInfo[id].title} up" ${index===0?'disabled':''}>↑</button><button type="button" data-move="1" data-section="${id}" aria-label="Move ${sectionInfo[id].title} down" ${index===sectionDraft.sections.length-1?'disabled':''}>↓</button></div></li>`).join('');
}
async function loadSectionOrder() {
  const saved = await api('/preferences/game-sections');
  sectionOrder = saved; sectionDraft = {sections:[...saved.sections],version:saved.version}; renderSectionOrder();
}
$('arrange-sections').addEventListener('click',async()=>{
  if(sectionSaving)return;
  sectionDraft = undefined;
  $('sections-dialog').showModal();$('section-order').innerHTML = '';$('section-order-status').textContent = 'Loading your layout…';$('sections-save').disabled = true;$('sections-reload').hidden = true;
  try { await loadSectionOrder();$('section-order-status').textContent = ''; }
  catch(error) { $('section-order-status').textContent = error.message; }
  finally { $('sections-save').disabled = !sectionDraft; }
});
$('section-order').addEventListener('click',event=>{
  const button = event.target.closest('button[data-move]'); if(!button || sectionSaving || !sectionDraft)return;
  const from = sectionDraft.sections.indexOf(button.dataset.section), to = from + Number(button.dataset.move);
  if(from < 0 || to < 0 || to >= sectionDraft.sections.length)return;
  [sectionDraft.sections[from],sectionDraft.sections[to]] = [sectionDraft.sections[to],sectionDraft.sections[from]];renderSectionOrder();
  const moved = [...$('section-order').querySelectorAll('button[data-section]')].find(b=>b.dataset.section === button.dataset.section && b.dataset.move === button.dataset.move && !b.disabled);
  (moved || $('section-order').querySelector(`[data-section="${button.dataset.section}"]:not(:disabled)`))?.focus();
  $('section-order-status').textContent = `${sectionInfo[button.dataset.section].title} is now ${to+1} of ${sectionDraft.sections.length}.`;
});
$('sections-cancel').addEventListener('click',()=>{if(!sectionSaving)$('sections-dialog').close();});
$('sections-dialog').addEventListener('cancel',event=>{if(sectionSaving)event.preventDefault();});
$('sections-reload').addEventListener('click',async()=>{
  try { await loadSectionOrder();$('sections-reload').hidden = true;$('section-order-status').textContent = 'Saved layout loaded.';renderSections(); }
  catch(error) { $('section-order-status').textContent = error.message; }
});
$('sections-save').addEventListener('click',async()=>{
  if(sectionSaving || !sectionDraft)return;sectionSaving = true;
  $('sections-dialog').querySelectorAll('button').forEach(b=>{b.disabled = true;});
  try { sectionOrder = await api('/preferences/game-sections',{sections:sectionDraft.sections,expectedVersion:sectionDraft.version});$('sections-dialog').close();renderSections();status('Section order saved for all your games.'); }
  catch(error) { $('section-order-status').textContent = error.message;$('sections-reload').hidden = error.status !== 409; }
  finally { sectionSaving = false;$('sections-dialog').querySelectorAll('button').forEach(b=>{b.disabled = false;});renderSectionOrder(); }
});
function matches(state, query, filter) {
  const text = [state.item.name,state.item.detail,state.note,state.item.category].join(' ').toLocaleLowerCase();
  return text.includes(query) && (filter === 'all' || filter === 'completed' && state.complete || filter === 'remaining' && !state.complete
    || filter === 'unknown' && !state.isKnown || filter === 'ready' && !state.complete && PixelVaultLibrary.trackerStatus([state],detail.sourceLinked)==='Available' || filter === 'locked' && !state.complete && PixelVaultLibrary.trackerStatus([state],detail.sourceLinked)==='Locked');
}
function contentButtons(row){
  return row.filter(s=>typeof s.item.content==='string'&&s.item.content.trim()).map(({item})=>{
    const missable=item.missable?'<span class="achievement-row-meta"><span class="missable">Missable</span></span>':'';
    return `<span class="content-action">${missable}<button type="button" class="quiet content-open" data-content-item="${escapeHtml(item.id)}" aria-haspopup="dialog" aria-label="Read content: ${escapeHtml(item.name)}${item.milestone?' · '+escapeHtml(item.milestone):''}">Content${row.length>1&&item.milestone?' · '+escapeHtml(item.milestone):''}</button></span>`;
  }).join('');
}
function renderItemBody(row) {
  const state=row[0],grouped=row.length>1;
  const notes = row.filter(s => s.note).map(s => `<p><strong>${grouped ? escapeHtml(s.item.milestone) + ' · ' : ''}Note</strong><br>${escapeHtml(s.note)}</p>`).join('');
  const gameDetails=state.item.detail?.trim()? `<p>${mailIconText(state.item.detail)}</p>` : '';
  return `${grouped?`<div class="columns">${row.map(s=>`<span class="column-chip ${s.complete?'complete':''}">${escapeHtml(s.item.milestone)}${PixelVaultLibrary.trackerStatus([s],detail.sourceLinked)?' · '+escapeHtml(PixelVaultLibrary.trackerStatus([s],detail.sourceLinked)):''}</span>`).join('')}</div>`:''}<div class="item-body">${gameDetails}${notes}${contentButtons(row)}${editorControls(row)}</div>`;
}
$('detail').addEventListener('toggle',event=>{
  const item=event.target;if(!detail||!item.matches?.('details.item')||!item.open)return;
  const content=item.querySelector(':scope > .item-content');if(!content||content.dataset.loaded)return;
  const row=detail.states.filter(s=>(s.item.groupId||s.item.id)===item.dataset.record&&!s.item.archived);
  content.innerHTML=renderItemBody(row);content.dataset.loaded='true';restoreDrafts();
},true);
function renderItems(skipCapture = false) {
  if (!detail) return;
  if (!skipCapture) captureDrafts();
  const openRows = new Set([...document.querySelectorAll('#detail details[open]')].map(e => e.dataset.record));
  const query = $('search').value.trim().toLocaleLowerCase(), filter = $('filter').value;
  const active = detail.states.filter(s => !s.item.archived).sort((a,b) => a.item.order - b.item.order);
  const rows = new Map();
  for (const state of active) {
    const key = state.item.groupId || state.item.id;
    if (!rows.has(key)) rows.set(key, []);
    rows.get(key).push(state);
  }
  const matching = [...rows.values()].filter(row => row.some(s => matches(s, query, filter)));
  const groups=PixelVaultLibrary.partition(matching,PixelVaultLibrary.recordComplete);
  $('results').textContent = `${Math.min(limit,groups.active.length)+Math.min(completedLimit,groups.completed.length)} of ${matching.length} matching records · Filters keep full tracker totals`;
  $('more').hidden = limit >= groups.active.length;
  $('more').textContent='Show more in progress';
  $('more-completed').hidden=completedLimit>=groups.completed.length;
  $('completed-items-section').hidden=!groups.completed.length;
  $('completed-items-count').textContent=String(groups.completed.length);
  const renderRows=subset=>{
  const categories = new Map();
  for (const row of subset) {
    const key = row[0].item.category || 'Other';
    if (!categories.has(key)) categories.set(key, []);
    categories.get(key).push(row);
  }
  return [...categories.entries()].map(([category, records]) => {
    const full = [...rows.values()].filter(row => (row[0].item.category || 'Other') === category);
    const included = full.filter(row=>!row.every(s=>s.item.excludedFromTotal));
    const completeRows = included.filter(row => row.some(s => s.complete)).length;
    const goal = detail.definition.categoryTargets[category];
    return `<section aria-label="${escapeHtml(category)}"><h3 class="category-heading">${escapeHtml(category)}<span>${completeRows} / ${included.length} records${goal ? ` · Goal ${Math.min(goal,completeRows)} / ${goal}` : ''}</span></h3>${records.map(row => {
      const state = row[0], grouped = row.length > 1, completed = PixelVaultLibrary.recordComplete(row);
      const label = PixelVaultLibrary.trackerStatus(row,detail.sourceLinked);
      const flags=`${state.item.missable?'<span class="tracker-item-flag missable">Missable</span>':''}${state.item.excludedFromTotal?'<span class="tracker-item-flag excluded">Optional</span>':''}`;
      return `<details class="item ${completed?'is-complete':''}" data-record="${escapeHtml(state.item.groupId || state.item.id)}"><summary>${headerChecks(row)}<span class="item-name">${mailIconText(state.item.name)}${flags}${row.some(s=>s.note)?'<small>Has a note</small>':''}</span>${label?`<span class="state-label">${completed?'<span class="completion-label">✓ Completed</span>':escapeHtml(label)}</span>`:''}</summary><div class="item-content"${openRows.has(state.item.groupId||state.item.id)?' data-loaded="true"':''}>${openRows.has(state.item.groupId||state.item.id)?renderItemBody(row):''}</div></details>`;
    }).join('')}</section>`;
  }).join('');
  };
  $('items').innerHTML=matching.length?renderRows(groups.active.slice(0,limit)):'<p class="empty">No items match these filters.</p>';
  $('completed-items').innerHTML=renderRows(groups.completed.slice(0,completedLimit));
  restoreDrafts(); document.querySelectorAll('#detail details[data-record]').forEach(e => { e.open = openRows.has(e.dataset.record); });
}
$('search').addEventListener('input', () => { limit = 60; completedLimit=30; renderItems(); });
$('filter').addEventListener('change', () => { limit = 60; completedLimit=30; renderItems(); });
$('more').addEventListener('click', () => { limit += 60; renderItems(); });
$('more-completed').addEventListener('click',()=>{completedLimit+=30;renderItems();});
window.addEventListener('online', () => initialize());
initialize();
// Editor state is scoped to this browser session. Drafts are never written to localStorage.
const drafts = new Map(), pendingSaves = new Map();
const ruleDrafts = new Map();
let saving = false, conflict;
const fieldKey = (tracker,item,field) => JSON.stringify([tracker,item,field]);
function requestId() {
  if (crypto.randomUUID) return crypto.randomUUID();
  const bytes = crypto.getRandomValues(new Uint8Array(16)); bytes[6] = bytes[6] & 15 | 64; bytes[8] = bytes[8] & 63 | 128;
  const hex = [...bytes].map(b=>b.toString(16).padStart(2,'0')).join('');
  return `${hex.slice(0,8)}-${hex.slice(8,12)}-${hex.slice(12,16)}-${hex.slice(16,20)}-${hex.slice(20)}`;
}
function captureDrafts() {
  captureRuleDrafts();
  document.querySelectorAll('#detail form[data-field]').forEach(form => {
    const input = form.querySelector('textarea,input'); if (!input) return;
    const key = fieldKey(form.dataset.tracker,form.dataset.item,form.dataset.field);
    if (input.value !== input.defaultValue) {
      const old = drafts.get(key);
      drafts.set(key,{value:input.value,version:old?.version ?? form.dataset.version});
    } else drafts.delete(key);
  });
}
function restoreDrafts() {
  restoreRuleDrafts();
  document.querySelectorAll('#detail form[data-field]').forEach(form => {
    const draft = drafts.get(fieldKey(form.dataset.tracker,form.dataset.item,form.dataset.field));
    if (draft) { form.querySelector('textarea,input').value = draft.value; form.dataset.version = draft.version; }
  });
}
function renderSummary() {
  if (!detail) return;
  const d = detail.definition;
  $('tracker-maps').href=`#maps/${encodeURIComponent(d.provider)}/${encodeURIComponent(d.gameId)}/tracker/${encodeURIComponent(d.id)}`;
  $('page-count').textContent = detail.readOnly ? 'Read-only preview' : 'Saved to Companion';
  $('history-open').hidden = detail.readOnly;
  $('summary').innerHTML = `<div><strong>${detail.totals.completed} <span class="muted">/ ${detail.totals.total}</span></strong><p class="hint">${Object.keys(d.categoryTargets).length ? 'Qualifying records / goal' : d.milestones.length ? 'Completion boxes' : 'Items completed'}</p></div><div class="progress-wrap">${progressBar(detail.totals.completed,detail.totals.total)}<p class="hint">${detail.totals.unknown} without saved progress · Full tracker totals</p>${detail.replaceManualProgress?'<p class="hint">Save replaces manual checks · Notes stay saved</p>':''}</div>`;
  paintBars();
}
function itemRulesEditor(state) {
  const item=state.item;
  return `<details class="item-rules"><summary>Item settings</summary><form data-item-rules data-tracker="${escapeHtml(detail.definition.id)}" data-item="${escapeHtml(item.id)}" data-version="${escapeHtml(detail.rulesVersions[item.id])}"><label class="rule-option"><input type="checkbox" name="missable" ${item.missable?'checked':''}>Missable</label><label class="rule-option"><input type="checkbox" name="required" ${item.excludedFromTotal?'':'checked'}>Required for completion</label><button type="submit">Save item settings</button><button type="button" class="quiet" data-rules-reload="${escapeHtml(item.id)}">Reload item settings</button></form></details>`;
}
function captureRuleDrafts() {
  document.querySelectorAll('#detail form[data-item-rules]').forEach(form=>{
    const key=fieldKey(form.dataset.tracker,form.dataset.item,'rules'), m=form.elements.missable, r=form.elements.required;
    if(m.checked!==m.defaultChecked||r.checked!==r.defaultChecked)ruleDrafts.set(key,{missable:m.checked,required:r.checked,version:form.dataset.version});else ruleDrafts.delete(key);
  });
}
function restoreRuleDrafts() {
  document.querySelectorAll('#detail form[data-item-rules]').forEach(form=>{
    const draft=ruleDrafts.get(fieldKey(form.dataset.tracker,form.dataset.item,'rules'));
    if(draft){form.elements.missable.checked=draft.missable;form.elements.required.checked=draft.required;form.dataset.version=draft.version;form.closest('details').open=true;}
  });
}
async function saveItemRules(form) {
  if(saving)return;
  const tracker=form.dataset.tracker, key=fieldKey(tracker,form.dataset.item,'rules');
  const command={itemId:form.dataset.item,missable:form.elements.missable.checked,requiredForCompletion:form.elements.required.checked,expectedVersion:form.dataset.version};
  const inputs=[...form.querySelectorAll('input')];setBusy(true);inputs.forEach(i=>i.disabled=true);
  try{
    const response=await api(`/trackers/${encodeURIComponent(tracker)}/item-rules`,command);
    if(detail?.definition.id===tracker)acceptSnapshot(response.tracker,null,key);
    status('Item settings saved. Centurian progress updates after the next game check.');await refreshLists();
  }catch(error){status(error.status===409?'Item settings changed. Use Reload item settings to review the saved choices.':error.message,true);}
  finally{setBusy(false);inputs.forEach(i=>i.disabled=false);}
}
function editorControls(row) {
  if (detail.readOnly) return '';
  const tracker = escapeHtml(detail.definition.id), protectedItems = new Set(detail.protectedItems ?? []);
  return `<div class="editors">${row.map(state=>{
    const id=escapeHtml(state.item.id), label=escapeHtml(state.item.milestone || state.item.name), versions=detail.versions[state.item.id];
    const locked=protectedItems.has(state.item.id), toggleLocked=locked && state.complete;
    const progress=row.length===1&&state.item.target===1?singleCheckbox(state):state.item.target===1
      ? `<button type="button" data-action="toggle" data-item="${id}" data-value="${state.complete?0:1}" data-locked="${toggleLocked}" ${toggleLocked?'disabled':''}>${toggleLocked?'Confirmed by game':state.complete?'Mark incomplete':'Mark complete'}</button>`
      : `<form data-tracker="${tracker}" data-item="${id}" data-field="progress" data-version="${versions.progress}"><label>Count<input type="number" inputmode="numeric" min="0" max="1000000" step="1" name="current" value="${state.current ?? ''}" required></label><button type="submit">Save count</button><p class="editor-status" role="status"></p></form>`;
    const resetLabel=state.reportedCurrent === null ? 'Clear manual progress' : 'Use source progress';
    const sourceCounts=`Manual: ${state.manualCurrent} · Imported: ${state.reportedCurrent ?? 'Unknown'}`;
    return `<fieldset><legend>${label}</legend>${progress}${state.manualCurrent!==null?`<button type="button" class="quiet" data-action="source" data-item="${id}" title="${sourceCounts}">${resetLabel}</button>`:''}<form data-tracker="${tracker}" data-item="${id}" data-field="note" data-version="${versions.note}"><label>Note<textarea name="note" rows="3" maxlength="20000">${escapeHtml(state.note)}</textarea></label><button type="submit">Save note</button><p class="editor-status" role="status"></p></form>${itemRulesEditor(state)}</fieldset>`;
  }).join('')}</div>`;
}
function singleCheckbox(state){
  const locked=detail.readOnly || (state.complete && (detail.protectedItems||[]).includes(state.item.id));
  const label=state.item.name+(state.item.milestone?' · '+state.item.milestone:'');
  return `<label class="tracker-check-target" title="${detail.readOnly?'Read-only':locked?'Confirmed by game':escapeHtml(label)}"><input type="checkbox" class="tracker-checkbox" aria-label="${escapeHtml(label)}" data-action="toggle" data-item="${escapeHtml(state.item.id)}" data-locked="${locked}" ${state.complete?'checked':''} ${locked||saving?'disabled':''}></label>`;
}
function headerChecks(row){
  if(row.length===1&&row[0].item.target===1)return singleCheckbox(row[0]);
  const protectedItems=new Set(detail.protectedItems || []);
  return `<span class="header-checks">${row.map(state=>{
    const locked=detail.readOnly || (state.complete && protectedItems.has(state.item.id));
    const label=(state.complete?'Uncheck ':'Check ')+state.item.name+(state.item.milestone?' · '+state.item.milestone:'');
    return `<button type="button" role="checkbox" aria-checked="${state.complete}" aria-label="${escapeHtml(label)}" class="header-check ${state.complete?'complete':''}" data-action="toggle" data-item="${escapeHtml(state.item.id)}" data-value="${state.complete?0:state.item.target}" data-locked="${locked}" ${locked?'disabled':''} title="${locked?'Confirmed by game':escapeHtml(label)}"><span aria-hidden="true">${state.complete?'✓':''}</span>${row.length>1?`<small>${escapeHtml(state.item.milestone)}</small>`:''}</button>`;
  }).join('')}</span>`;
}
function acceptSnapshot(snapshot, clearKey, clearRulesKey) {
  if (detail?.definition.id !== snapshot.definition.id) return;
  captureDrafts(); if (clearKey) drafts.delete(clearKey);
  if(clearRulesKey)ruleDrafts.delete(clearRulesKey);
  detail=snapshot; trackerHeading(); renderSummary(); renderItems(true);
}
function setBusy(value) {
  saving=value;
  $('history-open').disabled=value; $('reload-tracker').disabled=value;
  document.querySelectorAll('#items button,#completed-items button,#detail .tracker-checkbox').forEach(button=>{button.disabled=value || button.dataset.locked==='true';});
}
async function refreshLists() { [games,trackers]=await Promise.all([api('/games'),api('/trackers')]); }
async function saveCommand(tracker,command,form) {
  if (saving) return;
  const field=command.field==='source'?'progress':command.field, key=fieldKey(tracker,command.itemId,field);
  const signature=JSON.stringify(command), old=pendingSaves.get(key);
  const pending=old?.signature===signature?old:{signature,id:requestId()}; pendingSaves.set(key,pending);
  form ??= [...document.querySelectorAll('#detail form[data-field]')].find(f=>f.dataset.tracker===tracker && f.dataset.item===command.itemId && f.dataset.field===field);
  const controls=form ? [...form.querySelectorAll('textarea,input')] : [];
  setBusy(true); controls.forEach(e=>{e.disabled=true;}); status('Saving…');
  try {
    const response=await api(`/trackers/${encodeURIComponent(tracker)}/items`,{...command,requestId:pending.id});
    pendingSaves.delete(key); acceptSnapshot(response.tracker,key); status(response.result.replayed?'Save already received.':'Saved.');
    await refreshLists().then(()=>acknowledgeTracker(tracker)).catch(()=>{});
  } catch(error) {
    status(error.message,true);
    if (error.status===409 && detail?.definition.id===tracker) {
      try {
        const latest=await api(`/trackers/${encodeURIComponent(tracker)}`), state=latest.states.find(s=>s.item.id===command.itemId);
        if (!state || state.item.archived) return;
        conflict={tracker,command,key,latest};
        $('conflict-message').textContent=error.message;
        $('saved-value').value=field==='note'?state.note:String(state.current ?? 'Not checked');
        $('draft-value').value=field==='note'?command.note:command.field==='source'?'Use source progress':String(command.current);
        $('keep-edit').disabled=field!=='note' && command.current!==null && command.current<state.item.target && latest.protectedItems.includes(command.itemId);
        $('conflict-dialog').showModal();
      } catch { /* Keep the draft and original error if a fresh read fails. */ }
    }
  } finally { setBusy(false); controls.forEach(e=>{e.disabled=false;}); }
}
$('detail').addEventListener('input',captureDrafts);
$('detail').addEventListener('click',event=>{
  if(event.target.closest('.tracker-check-target'))event.stopPropagation();
});
$('detail').addEventListener('change',event=>{
  const input=event.target;
  if(!input.matches('.tracker-checkbox')||!detail||detail.readOnly||saving||input.disabled)return;
  const state=detail.states.find(s=>s.item.id===input.dataset.item);
  if(!state||state.item.target!==1)return;
  const current=input.checked?1:0;
  // Keep the last confirmed state while saving, and on failures/conflicts.
  input.checked=state.complete;
  void saveCommand(detail.definition.id,{itemId:state.item.id,field:'progress',current,note:null,expectedVersion:detail.versions[state.item.id].progress});
});
$('detail').addEventListener('submit',event=>{
  event.preventDefault(); const form=event.target; captureDrafts();
  if(form.matches('[data-item-rules]')){void saveItemRules(form);return;}
  const command={itemId:form.dataset.item,field:form.dataset.field,current:null,note:null,expectedVersion:form.dataset.version};
  if (command.field==='note') command.note=form.elements.note.value;
  else { command.current=Number(form.elements.current.value); if(!Number.isInteger(command.current))return; }
  saveCommand(form.dataset.tracker,command,form);
});
$('detail').addEventListener('click',event=>{
  const reloadRules=event.target.closest('button[data-rules-reload]');
  if(reloadRules&&detail&&!detail.readOnly){
    event.preventDefault();event.stopPropagation();const tracker=detail.definition.id,key=fieldKey(tracker,reloadRules.dataset.rulesReload,'rules');
    void api('/trackers/'+encodeURIComponent(tracker)).then(snapshot=>{if(detail?.definition.id===tracker){acceptSnapshot(snapshot,null,key);status('Saved item settings reloaded.');}}).catch(error=>status(error.message,true));return;
  }
  const content=event.target.closest('button[data-content-item]');
  if(content&&detail){
    event.preventDefault();event.stopPropagation();
    const item=detail.states.find(s=>s.item.id===content.dataset.contentItem&&!s.item.archived)?.item;
    if(!item?.content?.trim())return;
    $('tracker-content-title').innerHTML=mailIconText(item.name);
    $('tracker-content-detail').innerHTML=mailIconText(item.detail || '');
    $('tracker-content-body').innerHTML=mailIconText(item.content);
    $('tracker-content').showModal();return;
  }
  const button=event.target.closest('button[data-action]'); if(!button || !detail || detail.readOnly)return;
  event.preventDefault();event.stopPropagation();
  const item=button.dataset.item, source=button.dataset.action==='source';
  saveCommand(detail.definition.id,{itemId:item,field:source?'source':'progress',current:source?null:Number(button.dataset.value),note:null,expectedVersion:detail.versions[item].progress});
});
$('tracker-content-close').addEventListener('click',()=>$('tracker-content').close());
$('tracker-content').addEventListener('close',()=>{$('tracker-content-body').textContent='';$('tracker-content-detail').textContent='';});
$('use-saved').addEventListener('click',()=>{
  if(!conflict)return; const saved=conflict; conflict=undefined; $('conflict-dialog').close();
  pendingSaves.delete(saved.key); acceptSnapshot(saved.latest,saved.key); status('Using the saved value.');
});
$('keep-edit').addEventListener('click',()=>{
  if(!conflict)return; const saved=conflict; conflict=undefined; $('conflict-dialog').close();
  const field=saved.command.field==='note'?'note':'progress';
  pendingSaves.delete(saved.key);
  saveCommand(saved.tracker,{...saved.command,expectedVersion:saved.latest.versions[saved.command.itemId][field]});
});
$('reload-tracker').addEventListener('click',async()=>{
  if(!detail)return; captureDrafts(); const tracker=detail.definition.id;
  try{acceptSnapshot(await api('/trackers/'+encodeURIComponent(tracker)));status('Saved values reloaded. Unsaved drafts are kept.');}
  catch(error){status(error.message,true);}
});
async function showHistory() {
  if(!detail || detail.readOnly)return;
  $('history-status').textContent='Loading history…';
  try {
    const history=await api(`/trackers/${encodeURIComponent(detail.definition.id)}/history`);
    $('history-rows').innerHTML=history.map(h=>`<article class="history-entry"><div><strong>${escapeHtml(h.label)}</strong><p class="hint">${escapeHtml(new Date(Math.floor(h.createdUtc/10000)-62135596800000).toLocaleString())}${h.undoneUtc?' · Undone':''}</p></div>${h.undoneUtc?'':`<button data-history="${h.id}">Undo</button>`}</article>`).join('');
    $('history-status').textContent=history.length?'Recent changes. Undo preserves unrelated newer edits.':'No changes recorded yet.';
  }catch(error){$('history-status').textContent=error.message;}
}
$('history-open').addEventListener('click',()=>{$('history-dialog').showModal();showHistory();});
$('history-close').addEventListener('click',()=>{$('history-dialog').close();});
$('history-rows').addEventListener('click',async event=>{
  const button=event.target.closest('button[data-history]');if(!button || !detail || saving)return;
  setBusy(true);document.querySelectorAll('#history-rows button').forEach(b=>{b.disabled=true;});const tracker=detail.definition.id,key=`undo:${tracker}:${button.dataset.history}`;
  if(!pendingSaves.has(key))pendingSaves.set(key,{id:requestId()});
  try {
    const response=await api(`/trackers/${encodeURIComponent(tracker)}/history/${button.dataset.history}/undo`,{requestId:pendingSaves.get(key).id});
    pendingSaves.delete(key);acceptSnapshot(response.tracker);await refreshLists().then(()=>acknowledgeTracker(tracker)).catch(()=>{});await showHistory();status('Change undone.');
  }catch(error){$('history-status').textContent=error.message;}
  finally{setBusy(false);document.querySelectorAll('#history-rows button').forEach(b=>{b.disabled=false;});}
});
window.addEventListener('beforeunload',event=>{captureDrafts();if(drafts.size||ruleDrafts.size){event.preventDefault();event.returnValue='';}});

const navigation=PixelVaultNavigation.create(window,{
  onNavigate:route,
  allowed:()=>!!session?.authenticated && !saving && !librarySaving && !sectionSaving && !achievementUI.busy && !document.querySelector('dialog[open]'),
  fallback:()=>{
    if(achievementUI.active)return achievementUI.fallback();
    if(screenshotUI.active)return screenshotUI.fallback();
    if(mapUI.active)return mapUI.fallback();
    const parts=location.hash.slice(1).split('/');
    if(parts[0]==='tracker'){
      let id;try{id=decodeURIComponent(parts[1] || '');}catch{return '#';}
      const tracker=trackers.find(t=>t.id===id);
      if(tracker)return `#game/${encodeURIComponent(tracker.provider)}/${encodeURIComponent(tracker.gameId)}`;
    }
    return parts[0]?'#':null;
  },
  hint:ready=>document.body.classList.toggle('swipe-back-ready',ready)
});
$('navigate-back').addEventListener('click',()=>navigation.back());

let pollingConnection=false;
function stopRefreshStream(){refreshStream?.close();refreshStream=undefined;appliedPush=undefined;}
function startRefreshStream(){
  if(refreshStream||!pcConnection.pushEnabled||!session?.authenticated||document.visibilityState==='hidden')return;
  refreshStream=PixelVaultRefreshStream.create({workspace:updateScope,onUpdate:async snapshot=>{
    const result=await pollConnection({connection:snapshot.connection,push:snapshot});
    if(result!==false)appliedPush=snapshot;
    return result;
  }});
}
function unsafeToRefresh(){return saving || librarySaving || sectionSaving || refreshingGroup || achievementUI.busy || pendingSaves.size || (detail && (drafts.size||ruleDrafts.size)) || document.querySelector('dialog[open]:not(#refresh-summary)') || document.activeElement?.matches('input,textarea,select,[contenteditable="true"]');}
async function pollConnection({refreshLibrary=false,connection,push}={}){
  if(initializing || pollingConnection || !session?.authenticated || document.visibilityState==='hidden')return false;pollingConnection=true;
  try{
    pcConnection=connection||await api('/refresh/connection');startRefreshStream();
    if(pcConnection.local&&refreshJob?.canReview&&!pcConnection.reviewJobs?.some(j=>j.id===refreshJob.id))refreshJob=undefined;
    renderConnectionStatus();renderRefreshStatus();
    if(pcConnection.workspaceId && pcConnection.workspaceId!==updateScope){await initialize();return;}
    const versions=pcConnection.versions,currentRoute=location.hash,viewKey=JSON.stringify(versions);
    const changed=key=>!versions||appliedVersions[key]!==versions[key];
    const getAchievements=refreshLibrary||!achievementUpdateCache||currentAchievementVersion()===null||currentAchievementVersion()!==achievementUpdateVersion;
    if(!mapUI.active){captureDrafts();if(unsafeToRefresh())return false;}
    if(mapUI.active||achievementUI.active||screenshotUI.active){
      if(refreshLibrary||!versions||viewVersions.get(currentRoute)!==viewKey){
        const refreshed=mapUI.active?await mapUI.refresh():achievementUI.active?await achievementUI.refresh():await screenshotUI.refresh();
        if(refreshed!==false&&currentRoute===location.hash)viewVersions.set(currentRoute,viewKey);
      }
      if((getAchievements||versions&&changed('trackers'))&&!unsafeToRefresh()){
        const previous=PixelVaultUpdates.snapshot(trackers);
        await refreshLists();
        if(currentRoute!==location.hash||unsafeToRefresh())return false;
        if(versions)appliedVersions.trackers=versions.trackers;
        await checkForTrackerUpdates(previous);
      }
      return !unsafeToRefresh()&&(!getAchievements||achievementUpdateVersion===currentAchievementVersion());
    }
    if(!location.hash.slice(1)&&!refreshLibrary&&!versions)return;
    const previous=PixelVaultUpdates.snapshot(trackers);
    const getGames=refreshLibrary||changed('library')||changed('trackers')||changed('photos');
    const getTrackers=refreshLibrary||changed('trackers');
    const getDetail=detail&&(refreshLibrary||!versions||(push&&appliedPush
      ?push.trackers[detail.definition.id]!==appliedPush.trackers[detail.definition.id]||push.versions.sources!==appliedPush.versions.sources
      :viewVersions.get(currentRoute)!==viewKey));
    if(!getGames&&!getTrackers&&!getDetail&&!getAchievements)return;
    const [nextGames,nextTrackers,nextDetail,nextGroupOrder]=await Promise.all([getGames?api('/games'):games,getTrackers?api('/trackers'):trackers,getDetail?api('/trackers/'+encodeURIComponent(detail.definition.id)):null,getTrackers?api('/preferences/tracker-groups'):{groups:trackerGroupOrder}]);
    captureDrafts();if(unsafeToRefresh()||currentRoute!==location.hash)return false;
    const nextOrder=PixelVaultLibrary.normalizeTrackerGroupOrder(nextGroupOrder?.groups);
    const listsChanged=JSON.stringify(trackerGroupOrder)!==JSON.stringify(nextOrder)||JSON.stringify(games)!==JSON.stringify(nextGames)||JSON.stringify(trackers)!==JSON.stringify(nextTrackers);
    const photosChanged=refreshLibrary||changed('photos');
    trackerGroupOrder=nextOrder;games=nextGames;trackers=nextTrackers;if(versions)appliedVersions={...versions};
    if(listsChanged)completionShowcase.refresh();
    if(profile){profile=games.find(g=>g.provider===profile.provider&&g.gameId===profile.gameId)||profile;if(listsChanged)renderGameProfile();}
    else if(detail){if(nextDetail&&detail.definition.id===nextDetail.definition.id){if(JSON.stringify(nextDetail)!==JSON.stringify(detail))acceptSnapshot(nextDetail);viewVersions.set(currentRoute,viewKey);}}
    else{if(listsChanged){renderGameGrid({preservePosition:true});$('page-count').textContent=`${libraryGroups().length} games`;}if(photosChanged&&!location.hash.slice(1))void screenshotUI.showHome({refresh:true,preservePosition:true});}
    await checkForTrackerUpdates(previous);
    return achievementUpdateVersion===currentAchievementVersion();
  }catch{return false;/* Retain and retry the pending notice after a temporary interruption. */}
  finally{pollingConnection=false;}
}

document.addEventListener('visibilitychange',()=>{
  if(document.visibilityState==='visible'){startRefreshStream();flushTrackerUpdateSummary();void pollConnection({refreshLibrary:true});}
  else stopRefreshStream();
});

// Escape all text first; only the game's allowlisted icon tokens become images.
function mailIconText(value) {
  return escapeHtml(value ?? '').replace(/&lt;icon\s+name=\s*(faceicon_(?:bad|cry|death|good|love|please|smile|sweat|think|whale)|gaiji_tutorial_prio_(?:etc|quality|quantity|speed|bridgelink))\s*&gt;/g,
    (_,name)=>`<img class="mail-inline-icon" src="/mail-icons/${name}.png" alt="[${name.replace('faceicon_','').replace('gaiji_tutorial_prio_','')}]">`);
}


