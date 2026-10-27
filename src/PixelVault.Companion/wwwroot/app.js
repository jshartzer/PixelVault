'use strict';
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
let libraryLayout={sort:'screenshot-newest',pins:[]},librarySaving=false;
let pcConnection={paired:false,online:false,supported:[]},refreshJob,refreshingGroup;
let updateScope='',pendingUpdateSummary,visibleUpdateSummary,summaryDismissed=false;
const libraryGroups=()=>PixelVaultLibrary.groupGames(games);
const profileGroup=()=>libraryGroups().find(g=>g.id===profile?.groupId);
const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const pct = (done, total) => total ? Math.min(100, Math.max(0, done * 100 / total)) : 0;
const progressBar = (done, total) => `<div class="progress" role="meter" aria-label="Completion" aria-valuemin="0" aria-valuemax="${Math.max(total, 1)}" aria-valuenow="${done}"><div class="progress-value" data-percent="${pct(done, total)}"></div></div>`;
function paintBars() { document.querySelectorAll('[data-percent]').forEach(e => { e.style.width = `${Number(e.dataset.percent)}%`; }); }
async function api(path, body) {
  const response = await fetch('/api/v1' + path, { credentials: 'same-origin', cache: 'no-store',
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
  return response.json();
}
function status(message, error = false) { $('status').hidden = !message; $('status').textContent = message; $('status').className = error ? 'error' : ''; }
const achievementUI=PixelVaultAchievements.create({api,heading,progressBar,paintBars,status,getGames:()=>games,getTrackers:()=>trackers,newRequestId:requestId});
const screenshotUI=PixelVaultScreenshots.create({api,heading,getGames:()=>games,navigate:(hash,replace)=>navigation.go(hash,replace),back:()=>navigation.back()});
function showAuth(setup) {
  screenshotUI.clear();
  $('menu-open').hidden=true;
  updateScope='';pendingUpdateSummary=undefined;visibleUpdateSummary=undefined;
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
  try {
    session = await api('/session');
    csrf = (await api('/antiforgery')).token;
    status('');
    if (!session.authenticated) { showAuth(session.setupRequired); return; }
    $('auth').hidden = true; $('auth-error').textContent = ''; $('logout').hidden = false; $('menu-open').hidden=false;
    $('workspace').hidden = false; $('sample-banner').hidden = !session.demoEnabled;
    $('workspace-label').textContent = session.demoEnabled ? 'Sample workspace · Read-only preview. Your desktop library is separate.' : 'Separate Companion workspace · Edits save here. Desktop synchronization is not connected yet.';
    let workspace;
    [games, trackers, sectionOrder,libraryLayout,pcConnection,workspace] = await Promise.all([api('/games'), api('/trackers'), api('/preferences/game-sections'),api('/preferences/library'),api('/refresh/connection'),api('/workspace')]);
    updateScope=workspace.id;
    screenshotUI.setWorkspace(updateScope);
    renderConnectionStatus();
    $('game-sort').value=libraryLayout.sort;
    const platform = $('game-platform').value;
    $('game-platform').innerHTML = '<option value="">All platforms</option>' + [...new Set(games.map(g=>g.platform))].sort().map(p=>`<option value="${escapeHtml(p)}">${escapeHtml(p)}</option>`).join('');
    $('game-platform').value = platform;
    await route();
    checkForTrackerUpdates();
  } catch (error) { status(error.message || 'Cannot reach Companion. Check your connection and reload.', true); }
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
  $('page-title').textContent = title; $('page-subtitle').textContent = subtitle; $('eyebrow').textContent = eyebrow;
}
async function route() {
  const version = ++routeVersion;
  if (!session?.authenticated) return;
  closeSideMenu();
  if($('tracker-content').open)$('tracker-content').close();
  $('navigate-back').hidden=!location.hash.slice(1);
  status(''); captureDrafts(); detail = undefined; profile = undefined;achievementUI.reset();screenshotUI.reset();
  if ($('sections-dialog').open) $('sections-dialog').close();
  for (const id of ['games','game-profile','detail','empty','library-tools']) $(id).hidden = true;
  $('page-heading').hidden = false;
  $('breadcrumbs').innerHTML = ''; $('page-count').textContent = '';
  $('breadcrumbs').hidden = !location.hash.slice(1);
  let parts;
  try { parts = location.hash.slice(1).split('/').map(decodeURIComponent); }
  catch { status('That tracker link is invalid.', true); return; }
  if (!parts[0]) {
    renderHome();
  } else if (parts[0] === 'game') {
    try { const game = await api(`/games/${encodeURIComponent(parts[1])}/${encodeURIComponent(parts[2])}`); if(version !== routeVersion)return; profile = game; }
    catch(error) { if(version === routeVersion) status(error.message,true); return; }
    renderGameProfile();
  } else if (parts[0] === 'achievements' || parts[0] === 'achievement') {
    await achievementUI.open(parts);
  } else if (parts[0] === 'recent-photo') {
    renderHome();await screenshotUI.open(parts);
  } else if (parts[0] === 'screenshots' || parts[0] === 'photo' || parts[0] === 'timeline' || parts[0] === 'timeline-photo') {
    await screenshotUI.open(parts);
  } else if (parts[0] === 'tracker') {
    try { const incoming = await api('/trackers/' + encodeURIComponent(parts[1])); if (version !== routeVersion) return; detail = incoming; }
    catch (error) { if (version === routeVersion) status(error.message, true); return; }
    const d = detail.definition;
    heading(d.title, d.description, d.gameTitle.toUpperCase());
    $('breadcrumbs').innerHTML = `<a href="#">All games</a><span>/</span><a href="#game/${encodeURIComponent(d.provider)}/${encodeURIComponent(d.gameId)}">${escapeHtml(d.gameTitle)}</a><span>/</span><span aria-current="page">Tracker</span>`;
    renderSummary();
    $('detail').hidden = false; $('search').value = ''; $('filter').value = 'all'; limit = 60; completedLimit = 30; renderItems();
  } else status('That page is unavailable. Use the PixelVault link to return home.', true);
  paintBars();
}
function renderHome() {
    heading('Your games.', 'Choose a game to pick up where you left off.', 'YOUR COLLECTION');
    const count=libraryGroups().length;$('page-count').textContent = `${count} ${count === 1 ? 'game' : 'games'}`;
    if (!games.length) { $('empty').hidden = false; return; }
    $('games').hidden = false; $('library-tools').hidden = false; renderGameGrid();
    void screenshotUI.showHome();
}
function renderGameGrid() {
  const query = $('game-search').value.trim().toLocaleLowerCase(), platform = $('game-platform').value;
  const visible = PixelVaultLibrary.sortGames(libraryGroups().filter(g=>g.editions.some(e=>e.title.toLocaleLowerCase().includes(query)) && (!platform || g.platforms.includes(platform))),libraryLayout.sort,libraryLayout.pins);
  $('game-results').textContent = `${visible.length} ${visible.length === 1 ? 'game' : 'games'}`;
  $('games').innerHTML = visible.map(g=>{
    const pinned=libraryLayout.pins.includes(g.id),selected=platform?g.editions.find(e=>e.platform===platform):g;
    return `<article class="game-card cover-card ${pinned?'is-pinned':''}"><button class="pin-game" data-game="${escapeHtml(g.id)}" aria-label="${pinned?'Unpin':'Pin'} ${escapeHtml(g.title)}" aria-pressed="${pinned}" ${librarySaving?'disabled':''} title="${pinned?'Unpin':'Pin to top'}"><span aria-hidden="true">${pinned?'★':'☆'}</span></button><a href="#game/${encodeURIComponent(selected.provider)}/${encodeURIComponent(selected.gameId)}"><div class="cover-frame"><span class="cover-fallback" aria-hidden="true">${escapeHtml(g.title.split(/\s+/).map(w=>w[0]).slice(0,3).join(''))}</span>${g.coverUrl ? `<img src="${escapeHtml(g.coverUrl)}" alt="" loading="lazy" decoding="async" width="480" height="720">` : ''}${g.trackerCount ? `<span class="cover-tracker-count">${g.trackerCount} ${g.trackerCount === 1 ? 'tracker' : 'trackers'}</span>` : ''}</div><h2>${escapeHtml(g.title)}</h2><p>${escapeHtml(g.platforms.join(' · '))}</p></a></article>`;
  }).join('') || '<p class="muted">No games match your search.</p>';
  handleMissingArtwork($('games'));
}
function handleMissingArtwork(root) { root.querySelectorAll('img').forEach(img=>img.addEventListener('error',()=>{img.hidden = true;},{once:true})); }
function stat(label,value) { return `<div class="game-stat"><span>${escapeHtml(label)}</span><strong>${escapeHtml(value)}</strong></div>`; }
function formatPlaytime(minutes) { return minutes < 60 ? `${minutes}m` : `${new Intl.NumberFormat(undefined,{maximumFractionDigits:1}).format(minutes/60)}h`; }
function renderGameProfile() {
  if(!profile)return; const g = profile, s = g.stats || {};
  $('page-heading').hidden = true; $('game-profile').hidden = false;
  $('breadcrumbs').innerHTML = `<a href="#">All games</a><span>/</span><span aria-current="page">${escapeHtml(g.title)}</span>`;
  $('game-hero').innerHTML = `<div class="profile-hero">${g.bannerUrl || g.coverUrl ? `<img src="${escapeHtml(g.bannerUrl || g.coverUrl)}" alt="" decoding="async" fetchpriority="high">` : ''}<div class="hero-shade"></div><div class="profile-title"><span class="platform-pill">${escapeHtml(g.platform || g.provider)}</span><h1>${escapeHtml(g.title)}</h1></div></div>`;
  handleMissingArtwork($('game-hero'));
  const group=profileGroup(),editions=group?.editions || [g];
  $('profile-platform-label').hidden=editions.length<2;
  $('profile-platform').innerHTML=editions.map(e=>`<option value="${escapeHtml(e.provider+':'+e.gameId)}" ${e.provider===g.provider&&e.gameId===g.gameId?'selected':''}>${escapeHtml(e.platform || e.provider)}</option>`).join('');
  renderRefreshStatus();
  const stats = [];
  if(s.playtimeMinutes !== null && s.playtimeMinutes !== undefined) stats.push(stat('Playtime',formatPlaytime(s.playtimeMinutes)));
  stats.push(stat('Achievements',s.achievementTotal !== null && s.achievementTotal !== undefined ? `${s.achievementUnlocked ?? '—'} / ${s.achievementTotal}` : 'Not synced'));
  if(g.total)stats.push(stat('Tracker goals',`${g.completed} / ${g.total}`));
  else if(s.captures !== null && s.captures !== undefined)stats.push(stat('Captures',String(s.captures)));
  $('game-stats').innerHTML = stats.join('');$('game-stats').setAttribute('aria-label','Saved game statistics');$('game-stats').title = 'Playtime and achievements are from your saved library snapshot. Tracker goals reflect saved Companion progress.';
  renderSections();
}
const sectionInfo = {
  trackers:{title:'Trackers',description:'Your next steps, all in one place.',icon:'✓'},
  achievements:{title:'Achievements',description:'Browse your unlocks and achievement progress.',icon:'◇'},
  screenshots:{title:'Screenshots',description:'Revisit the moments you captured.',icon:'▧'}
};
function renderSections() {
  if(!profile)return;
  const scope = profile.provider + ':' + profile.gameId;
  const wasOpen = $('game-sections').dataset.game === scope && !!$('game-sections').querySelector('.tracker-section[open]');
  $('game-sections').dataset.game = scope;
  const editions=profileGroup()?.editions || [profile];
  const matching = trackers.filter(t=>editions.some(e=>t.provider===e.provider&&t.gameId===e.gameId));
  const trackerGroups=PixelVaultLibrary.partition(matching,PixelVaultLibrary.trackerComplete);
  const trackerCard=t=>{
    const complete=PixelVaultLibrary.trackerComplete(t);
    return `<a class="tracker-card ${complete?'is-complete':''}" href="#tracker/${encodeURIComponent(t.id)}"><div><h4>${escapeHtml(t.title)}</h4>${editions.length>1?`<p class="hint">${escapeHtml(editions.find(e=>e.provider===t.provider&&e.gameId===t.gameId)?.platform || t.provider)}</p>`:''}</div><div class="tracker-progress"><strong>${t.totals.completed} <span class="muted">/ ${t.totals.total}</span></strong>${progressBar(t.totals.completed,t.totals.total)}</div><span class="${complete?'completion-check':'arrow'}" ${complete?'aria-label="Completed"':'aria-hidden="true"'}>${complete?'✓':'›'}</span></a>`;
  };
  $('game-sections').innerHTML = (sectionOrder?.sections || Object.keys(sectionInfo)).map(id=>{
    const section = sectionInfo[id]; if(!section)return '';
    if(id==='achievements'){
      const eligible=e=>e.provider==='steam'||e.provider==='retroachievements';const selected=eligible(profile)?profile:editions.find(eligible)||profile;
      return `<a class="profile-section achievement-section-link" data-section="achievements" href="#achievements/${encodeURIComponent(selected.provider)}/${encodeURIComponent(selected.gameId)}"><div class="section-icon" aria-hidden="true">${section.icon}</div><div><h3>Achievements</h3><p>Progress, saved groups and individual guides.${selected.provider!==profile.provider?' · '+escapeHtml(selected.platform||selected.provider):''}</p></div><span class="section-chevron" aria-hidden="true">›</span></a>`;
    }
    if(id==='screenshots'){
      const photos=editions.find(e=>e.screenshots)?.screenshots;
      if(photos)return `<a class="profile-section screenshot-section-link" data-section="screenshots" href="#screenshots/${encodeURIComponent(photos.provider)}/${encodeURIComponent(photos.gameId)}"><div class="section-icon" aria-hidden="true">${section.icon}</div><div><h3>Screenshots <span class="section-count">${photos.count}</span></h3><p>Your photos, organized by capture date.</p></div><span class="section-chevron" aria-hidden="true">›</span></a>`;
    }
    if(id !== 'trackers')return `<section class="profile-section coming-section" data-section="${id}"><div class="section-icon" aria-hidden="true">${section.icon}</div><div><h3>${section.title}</h3><p>${section.description}</p></div><span class="coming-label">Coming soon</span></section>`;
    return `<details class="profile-section tracker-section" data-section="trackers" ${wasOpen?'open':''}><summary class="section-title"><span class="section-icon" aria-hidden="true">${section.icon}</span><span><h3>Trackers <span class="section-count">${matching.length}</span></h3><span class="section-description">${matching.length?'Keep your progress moving.':'No trackers available yet.'}</span></span><span class="section-chevron" aria-hidden="true">›</span></summary><div class="tracker-list">${trackerGroups.active.map(trackerCard).join('')}${trackerGroups.completed.length?`<h4 class="completed-heading"><span aria-hidden="true">✓</span> Completed <span>${trackerGroups.completed.length}</span></h4>${trackerGroups.completed.map(trackerCard).join('')}`:''}${matching.length?'':'<p class="section-empty">No trackers are available for this game yet.</p>'}</div></details>`;
  }).join(''); paintBars();
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
  saveLibraryPreference('pins',{gameId:pin.dataset.game,pinned:!libraryLayout.pins.includes(pin.dataset.game)},libraryLayout.pins.includes(pin.dataset.game)?'Game unpinned.':'Game pinned to the top.');
});
function renderRefreshStatus(){
  if(!profile)return;const supported=pcConnection.supported.includes(profile.groupId),busy=refreshingGroup===profile.groupId;
  $('refresh-game').disabled=!supported || busy;
  $('refresh-game').textContent=busy?'Checking PC…':'Refresh from PC';
  $('refresh-game').title=supported?'Read the configured save files on your paired PC.':pcConnection.paired?'No supported PC save source is configured for this game.':'The PC connection has not been paired.';
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
  $('pc-status-bar').hidden=false;$('automatic-toggle').hidden=!pcConnection.paired;
  $('pc-status-dot').classList.toggle('online',!!pcConnection.online);
  $('pc-status-title').textContent=pcConnection.paired?`PC ${pcConnection.online?'online':'offline'} · Automatic updates ${pcConnection.automaticEnabled?'on':'paused'}`:'PC not connected';
  const details=[];
  if(pcConnection.lastSaveCheckUtc)details.push('Saves checked '+timeSince(pcConnection.lastSaveCheckUtc));
  if(pcConnection.lastLibraryCheckUtc)details.push('Library synced '+timeSince(pcConnection.lastLibraryCheckUtc));
  if(!details.length)details.push(pcConnection.paired?(pcConnection.online?'Waiting for the first automatic check.':'Showing saved NAS data.'):'Connect your PC to enable automatic updates.');
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
      showTrackerUpdateSummary(PixelVaultUpdates.summary(before,trackers,job));
    }
  }catch(error){rememberRefresh({message:error.message,canReview:false},groupId);showTrackerUpdateSummary({title:'Refresh unavailable',message:error.message,updated:[],review:[],failed:[]});}
  finally{refreshingGroup=undefined;renderRefreshStatus();}
});
function openRefreshReview(){
  if(!refreshJob?.canReview)return;
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
    if(!job.canReview)showTrackerUpdateSummary(PixelVaultUpdates.summary(before,trackers,job));
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
function checkForTrackerUpdates(){
  const seen=readSeenTrackerVersions();
  if(!seen){writeSeenTrackerVersions(PixelVaultUpdates.snapshot(trackers));return;}
  const summary=PixelVaultUpdates.summary(seen,trackers);
  if(summary.updated.length)showTrackerUpdateSummary(summary);
  else writeSeenTrackerVersions(PixelVaultUpdates.snapshot(trackers));
}
function showTrackerUpdateSummary(summary){
  if(!session?.authenticated || !updateScope)return;
  pendingUpdateSummary={summary,scope:updateScope,versions:PixelVaultUpdates.snapshot(trackers)};flushTrackerUpdateSummary();
}
function flushTrackerUpdateSummary(){
  if(!session?.authenticated || !pendingUpdateSummary || document.querySelector('dialog[open]'))return;
  visibleUpdateSummary=pendingUpdateSummary;pendingUpdateSummary=undefined;summaryDismissed=false;
  const summary=visibleUpdateSummary.summary;
  $('refresh-summary-title').textContent=summary.title;$('refresh-summary-message').textContent=summary.message;
  $('refresh-summary-list').innerHTML=(summary.updated.length?`<ul>${summary.updated.map(t=>`<li><span aria-hidden="true">✓</span><div><strong>${escapeHtml(t.title)}</strong><small>${escapeHtml(t.gameTitle)}</small></div>${Number.isInteger(t.increased)?`<span class="update-increase" aria-label="${t.increased} ${t.increased===1?'item':'items'} increased">+${t.increased}</span>`:''}</li>`).join('')}</ul>`:'')
    +(summary.review.length?`<p class="hint">Needs review: ${summary.review.map(escapeHtml).join(', ')}. Your current progress is kept.</p>`:'')
    +(summary.failed.length?`<p class="hint">Could not check: ${summary.failed.map(escapeHtml).join(', ')}. Saved progress is kept.</p>`:'');
  $('refresh-summary').showModal();
}
$('refresh-summary').addEventListener('click',event=>{event.preventDefault();event.stopPropagation();summaryDismissed=true;$('refresh-summary').close();});
$('refresh-summary').addEventListener('cancel',()=>{summaryDismissed=true;});
$('refresh-summary').addEventListener('close',()=>{
  if(summaryDismissed && visibleUpdateSummary?.scope===updateScope && session?.authenticated)writeSeenTrackerVersions(visibleUpdateSummary.versions);
  visibleUpdateSummary=undefined;
});
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
  return row.filter(s=>typeof s.item.content==='string'&&s.item.content.trim()).map(s=>`<button type="button" class="quiet content-open" data-content-item="${escapeHtml(s.item.id)}" aria-haspopup="dialog" aria-label="Read content: ${escapeHtml(s.item.name)}${s.item.milestone?' · '+escapeHtml(s.item.milestone):''}">Content${row.length>1&&s.item.milestone?' · '+escapeHtml(s.item.milestone):''}</button>`).join('');
}
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
    const completeRows = full.filter(row => row.some(s => s.complete)).length;
    const goal = detail.definition.categoryTargets[category];
    return `<section aria-label="${escapeHtml(category)}"><h3 class="category-heading">${escapeHtml(category)}<span>${completeRows} / ${full.length} records${goal ? ` · Goal ${Math.min(goal,completeRows)} / ${goal}` : ''}</span></h3>${records.map(row => {
      const state = row[0], grouped = row.length > 1, completed = PixelVaultLibrary.recordComplete(row);
      const label = PixelVaultLibrary.trackerStatus(row,detail.sourceLinked);
      const notes = row.filter(s => s.note).map(s => `<p><strong>${grouped ? escapeHtml(s.item.milestone) + ' · ' : ''}Note</strong><br>${escapeHtml(s.note)}</p>`).join('');
      return `<details class="item ${completed?'is-complete':''}" data-record="${escapeHtml(state.item.groupId || state.item.id)}"><summary>${headerChecks(row)}<span class="item-name">${escapeHtml(state.item.name)}${notes?'<small>Has a note</small>':''}</span>${label?`<span class="state-label">${completed?'<span class="completion-label">✓ Completed</span>':escapeHtml(label)}</span>`:''}</summary>${grouped?`<div class="columns">${row.map(s=>`<span class="column-chip ${s.complete?'complete':''}">${escapeHtml(s.item.milestone)}${PixelVaultLibrary.trackerStatus([s],detail.sourceLinked)?' · '+escapeHtml(PixelVaultLibrary.trackerStatus([s],detail.sourceLinked)):''}</span>`).join('')}</div>`:''}<div class="item-body"><p>${escapeHtml(state.item.detail || 'No additional details.')}</p>${notes}${contentButtons(row)}<p>${row.some(s=>s.isManual)?'Includes saved manual progress. ':''}${row.some(s=>s.reportedCurrent!==null)?'Includes a saved source observation. ':''}${detail.readOnly ? 'Read-only sample.' : 'Saved source observations stay separate from manual edits.'}</p>${editorControls(row)}</div></details>`;
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
let saving = false, conflict;
const fieldKey = (tracker,item,field) => JSON.stringify([tracker,item,field]);
function requestId() {
  if (crypto.randomUUID) return crypto.randomUUID();
  const bytes = crypto.getRandomValues(new Uint8Array(16)); bytes[6] = bytes[6] & 15 | 64; bytes[8] = bytes[8] & 63 | 128;
  const hex = [...bytes].map(b=>b.toString(16).padStart(2,'0')).join('');
  return `${hex.slice(0,8)}-${hex.slice(8,12)}-${hex.slice(12,16)}-${hex.slice(16,20)}-${hex.slice(20)}`;
}
function captureDrafts() {
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
  document.querySelectorAll('#detail form[data-field]').forEach(form => {
    const draft = drafts.get(fieldKey(form.dataset.tracker,form.dataset.item,form.dataset.field));
    if (draft) { form.querySelector('textarea,input').value = draft.value; form.dataset.version = draft.version; }
  });
}
function renderSummary() {
  if (!detail) return;
  const d = detail.definition;
  $('page-count').textContent = detail.readOnly ? 'Read-only preview' : 'Saved to Companion';
  $('history-open').hidden = detail.readOnly;
  $('summary').innerHTML = `<div><strong>${detail.totals.completed} <span class="muted">/ ${detail.totals.total}</span></strong><p class="hint">${Object.keys(d.categoryTargets).length ? 'Qualifying records / goal' : d.milestones.length ? 'Completion boxes' : 'Items completed'}</p></div><div class="progress-wrap">${progressBar(detail.totals.completed,detail.totals.total)}<p class="hint">${detail.totals.unknown} without saved progress · Full tracker totals</p></div>`;
  paintBars();
}
function editorControls(row) {
  if (detail.readOnly) return '';
  const tracker = escapeHtml(detail.definition.id), protectedItems = new Set(detail.protectedItems ?? []);
  return `<div class="editors">${row.map(state=>{
    const id=escapeHtml(state.item.id), label=escapeHtml(state.item.milestone || state.item.name), versions=detail.versions[state.item.id];
    const locked=protectedItems.has(state.item.id), toggleLocked=locked && state.complete;
    const progress=state.item.target===1
      ? `<button type="button" data-action="toggle" data-item="${id}" data-value="${state.complete?0:1}" data-locked="${toggleLocked}" ${toggleLocked?'disabled':''}>${toggleLocked?'Confirmed by game':state.complete?'Mark incomplete':'Mark complete'}</button>`
      : `<form data-tracker="${tracker}" data-item="${id}" data-field="progress" data-version="${versions.progress}"><label>Count<input type="number" inputmode="numeric" min="0" max="1000000" step="1" name="current" value="${state.current ?? ''}" required></label><button type="submit">Save count</button><p class="editor-status" role="status"></p></form>`;
    return `<fieldset><legend>${label}</legend><p class="hint">Saved source: ${state.reportedCurrent === null ? 'No saved data' : state.reportedCurrent + ' / ' + state.item.target}${state.isManual ? ' · Manual: ' + state.manualCurrent : ''}</p>${state.hasConflict ? '<p class="conflict-hint">Manual progress overrides a different source value.</p>' : ''}${progress}${state.manualCurrent!==null?`<button type="button" class="quiet" data-action="source" data-item="${id}">${state.reportedCurrent === null ? 'Clear manual progress' : 'Use source progress'}</button>`:''}${locked?'<p class="hint">Game-confirmed completion is protected.</p>':''}<form data-tracker="${tracker}" data-item="${id}" data-field="note" data-version="${versions.note}"><label>Note<textarea name="note" rows="3" maxlength="20000">${escapeHtml(state.note)}</textarea></label><button type="submit">Save note</button><p class="editor-status" role="status"></p></form></fieldset>`;
  }).join('')}</div>`;
}
function headerChecks(row){
  const protectedItems=new Set(detail.protectedItems || []);
  return `<span class="header-checks">${row.map(state=>{
    const locked=detail.readOnly || (state.complete && protectedItems.has(state.item.id));
    const label=(state.complete?'Uncheck ':'Check ')+state.item.name+(state.item.milestone?' · '+state.item.milestone:'');
    return `<button type="button" role="checkbox" aria-checked="${state.complete}" aria-label="${escapeHtml(label)}" class="header-check ${state.complete?'complete':''}" data-action="toggle" data-item="${escapeHtml(state.item.id)}" data-value="${state.complete?0:state.item.target}" data-locked="${locked}" ${locked?'disabled':''} title="${locked?'Confirmed by game':escapeHtml(label)}"><span aria-hidden="true">${state.complete?'✓':''}</span>${row.length>1?`<small>${escapeHtml(state.item.milestone)}</small>`:''}</button>`;
  }).join('')}</span>`;
}
function acceptSnapshot(snapshot, clearKey) {
  if (detail?.definition.id !== snapshot.definition.id) return;
  captureDrafts(); if (clearKey) drafts.delete(clearKey);
  detail=snapshot; heading(detail.definition.title,detail.definition.description,detail.definition.gameTitle.toUpperCase()); renderSummary(); renderItems(true);
}
function setBusy(value) {
  saving=value;
  $('history-open').disabled=value; $('reload-tracker').disabled=value;
  document.querySelectorAll('#items button,#completed-items button').forEach(button=>{button.disabled=value || button.dataset.locked==='true';});
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
$('detail').addEventListener('submit',event=>{
  event.preventDefault(); const form=event.target; captureDrafts();
  const command={itemId:form.dataset.item,field:form.dataset.field,current:null,note:null,expectedVersion:form.dataset.version};
  if (command.field==='note') command.note=form.elements.note.value;
  else { command.current=Number(form.elements.current.value); if(!Number.isInteger(command.current))return; }
  saveCommand(form.dataset.tracker,command,form);
});
$('detail').addEventListener('click',event=>{
  const content=event.target.closest('button[data-content-item]');
  if(content&&detail){
    event.preventDefault();event.stopPropagation();
    const item=detail.states.find(s=>s.item.id===content.dataset.contentItem&&!s.item.archived)?.item;
    if(!item?.content?.trim())return;
    $('tracker-content-title').textContent=item.name;
    $('tracker-content-detail').textContent=item.detail || '';
    $('tracker-content-body').textContent=item.content;
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
window.addEventListener('beforeunload',event=>{captureDrafts();if(drafts.size){event.preventDefault();event.returnValue='';}});

const navigation=PixelVaultNavigation.create(window,{
  onNavigate:route,
  allowed:()=>!!session?.authenticated && !saving && !librarySaving && !sectionSaving && !achievementUI.busy && !document.querySelector('dialog[open]'),
  fallback:()=>{
    if(achievementUI.active)return achievementUI.fallback();
    if(screenshotUI.active)return screenshotUI.fallback();
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
function unsafeToRefresh(){return saving || librarySaving || sectionSaving || refreshingGroup || achievementUI.busy || pendingSaves.size || (detail && drafts.size) || document.querySelector('dialog[open]') || document.activeElement?.matches('input,textarea,select,[contenteditable="true"]');}
async function pollConnection(){
  if(pollingConnection || !session?.authenticated || document.visibilityState==='hidden')return;pollingConnection=true;
  try{
    pcConnection=await api('/refresh/connection');renderConnectionStatus();
    if(pcConnection.workspaceId && pcConnection.workspaceId!==updateScope){await initialize();return;}
    captureDrafts();if(unsafeToRefresh())return;
    if(achievementUI.active){await achievementUI.refresh();return;}
    if(screenshotUI.active)return;
    const currentRoute=location.hash,previous=PixelVaultUpdates.snapshot(trackers);
    const [nextGames,nextTrackers]=await Promise.all([api('/games'),api('/trackers')]);
    captureDrafts();if(unsafeToRefresh() || currentRoute!==location.hash)return;
    games=nextGames;trackers=nextTrackers;
    if(profile){profile=games.find(g=>g.provider===profile.provider&&g.gameId===profile.gameId)||profile;renderGameProfile();}
    else if(detail){const id=detail.definition.id;const snapshot=await api('/trackers/'+encodeURIComponent(id));captureDrafts();if(!unsafeToRefresh()&&currentRoute===location.hash&&detail?.definition.id===id)acceptSnapshot(snapshot);}
    else{renderGameGrid();$('page-count').textContent=`${libraryGroups().length} games`;if(!location.hash.slice(1))void screenshotUI.showHome();}
    const summary=PixelVaultUpdates.summary(readSeenTrackerVersions() || previous,trackers);
    if(summary.updated.length)showTrackerUpdateSummary(summary);
  }catch{/* Retain the current screen during a temporary network interruption. */}
  finally{pollingConnection=false;}
}
setInterval(pollConnection,15000);
document.addEventListener('visibilitychange',()=>{if(document.visibilityState==='visible')pollConnection();});
