// Opt-in release rehearsal: use a private copied workspace with local workers disabled.
const {test}=require('node:test'),assert=require('node:assert/strict'),{chromium}=require('playwright');
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const root=process.env.PV_MAP_REHEARSAL_ROOT;
test('published maps work with copied regional images, saved progress and adjacent workflows',{skip:!root,timeout:120000},async()=>{
 const configuration=JSON.parse(fs.readFileSync(path.join(root,'rehearsal.json'),'utf8'));
 const {DataRoot,ApplicationDataRoot,LocalWorkersEnabled}=configuration.Companion;
 assert.equal(LocalWorkersEnabled,false);assert.ok(path.resolve(ApplicationDataRoot).startsWith(path.resolve(root)+path.sep));
 const base=configuration.Urls;assert.match(base,/^http:\/\/127\.0\.0\.1:\d+$/);
 const browser=await chromium.launch({channel:'chrome',headless:true});
 const context=await browser.newContext({viewport:{width:390,height:844},isMobile:true,hasTouch:true});
 const page=await context.newPage(),errors=[],violations=[],measurements=[];
 page.setDefaultTimeout(20000);page.on('pageerror',error=>errors.push(error.message));
 await page.addInitScript(()=>{window.mapReleaseCsp=[];document.addEventListener('securitypolicyviolation',e=>mapReleaseCsp.push(e.violatedDirective));});
 try{
  const health=await (await context.request.get(base+'/health')).json();assert.equal(health.version,'0.41.0');
  assert.equal((await context.request.get(base+'/api/v1/maps/steam/1850570')).status(),401);
  await page.goto(base+'/#maps/steam/1850570');
  const credentialsFile=path.join(root,'rehearsal-owner.private.json');
  const session=await (await context.request.get(base+'/api/v1/session')).json();
  if(session.setupRequired){
   const credentials={name:'Map release rehearsal',password:crypto.randomBytes(24).toString('base64url')};fs.writeFileSync(credentialsFile,JSON.stringify(credentials));
   await page.locator('#token').fill(fs.readFileSync(path.join(DataRoot,'setup-token.txt'),'utf8').trim());
   await page.locator('#name').fill(credentials.name);await page.locator('#password').fill(credentials.password);await page.locator('#submit').click();
  }else{
   const credentials=JSON.parse(fs.readFileSync(credentialsFile,'utf8'));
   await page.locator('#name').fill(credentials.name);await page.locator('#password').fill(credentials.password);await page.locator('#submit').click();
  }
  await page.waitForSelector('.map-card');assert.equal(await page.locator('.map-card').count(),3);
  const listing=await page.evaluate(()=>api('/maps/steam/1850570'));
  const visit=async(map,phase)=>{
   let tileBytes=0,tileRequests=0;
   const capture=response=>{if(response.url().includes('/tiles/')){tileRequests++;tileBytes+=Number(response.headers()['content-length']||0);}};page.on('response',capture);
   const start=performance.now();await page.evaluate(id=>navigation.go('#map/steam/1850570/'+id),map.id);
   await page.waitForFunction(()=>document.querySelector('.leaflet-tile-loaded')&&document.querySelector('.map-progress-message')?.hidden&&document.querySelector('.map-pin-row'));
   const visibleMs=performance.now()-start;
   const detail=await page.evaluate(id=>api('/maps/steam/1850570/'+id),map.id),progress=await page.evaluate(id=>api('/maps/steam/1850570/'+id+'/progress'),map.id);
   assert.equal(await page.locator('.map-marker').count(),map.pinCount);assert.equal(detail.pins.length,map.pinCount);
   assert.equal(await page.locator('.map-marker.pin-completed').count(),progress.pins.filter(p=>p.status==='completed').length);
   assert.ok(tileRequests<16,'Initial phone viewport requests a bounded number of tiles');
   page.off('response',capture);measurements.push({region:map.region,phase,visibleMs:Math.round(visibleMs),tileRequests,tileBytes,pins:map.pinCount});
   return {detail,progress};
  };
  let total=0;
  for(const map of listing.maps){await visit(map,'cold');total+=map.pinCount;}
  assert.equal(total,56);
  for(const map of listing.maps)await visit(map,'warm');
  const map=listing.maps.find(m=>m.pinCount===44);const {detail,progress}=await visit(map,'warm-detail');
  await page.locator('.map-pin-row').first().click();await page.locator('[data-map-expand]').click();
  await page.screenshot({path:path.join(root,'phone-map.png'),fullPage:true});
  await page.locator('#map-canvas').focus();await page.keyboard.press('Escape');
  const selected=detail.pins[0];await page.locator('#map-pin-search').fill(selected.label);assert.ok(await page.locator('.map-pin-row').count()>=1);
  await page.locator('#map-pin-filter').selectOption('remaining');
  assert.equal(await page.locator('.map-marker.pin-completed').count(),0);
  await page.locator('[data-pin-locate]').click();await page.waitForTimeout(350);
  await page.evaluate(()=>{window.mapTilesBefore=[...document.querySelectorAll('.leaflet-tile')];window.mapPositionBefore=document.querySelector('.leaflet-map-pane').style.transform;});
  await page.evaluate(()=>pollConnection());
  assert.equal(await page.evaluate(()=>mapTilesBefore.every(t=>t.isConnected)&&mapPositionBefore===document.querySelector('.leaflet-map-pane').style.transform),true);
  await page.locator('[data-pin-tracker]').click();await page.waitForSelector('#detail:not([hidden])');
  await page.locator('#tracker-maps').click();await page.waitForSelector('.map-card');assert.equal(await page.locator('.map-card').count(),3);
  await page.evaluate(id=>navigation.go('#map/steam/1850570/'+id),map.id);await page.waitForSelector('.map-pin-row');
  await page.locator('[data-pin-achievement]').click();await page.waitForSelector('.achievement-guide');
  await page.locator('.achievement-map-link a').click();await page.waitForSelector('.map-card');assert.equal(await page.locator('.map-card').count(),3);
  await page.evaluate(()=>navigation.go('#game/steam/1850570'));await page.waitForSelector('#game-profile:not([hidden])');
  await page.evaluate(()=>navigation.go('#screenshots/steam/1850570'));await page.waitForSelector('.screenshot-tile');
  await page.locator('.screenshot-tile').first().click();await page.waitForSelector('#photo-viewer[open]');await page.waitForFunction(()=>[...document.querySelectorAll('#photo-stage img')].some(i=>i.complete&&i.naturalWidth>0));
  await page.evaluate(id=>navigation.go('#map/steam/1850570/'+id),map.id);await page.waitForSelector('.map-marker');
  await page.setViewportSize({width:1280,height:900});await page.locator('[data-map-fit]').click();await page.locator('[data-map-expand]').click();await page.screenshot({path:path.join(root,'desktop-map.png'),fullPage:true});
  assert.equal(await page.locator('body').evaluate(el=>el.scrollWidth<=innerWidth),true);
  violations.push(...await page.evaluate(()=>mapReleaseCsp));assert.deepEqual(errors,[]);assert.deepEqual(violations,[]);
  fs.writeFileSync(path.join(root,'browser-result.json'),JSON.stringify({passed:true,version:health.version,regions:3,pins:total,measurements,errors,violations,originalsChanged:false,localWorkersEnabled:false,actualPhoneAcceptance:false},null,2));
  console.log(JSON.stringify({passed:true,measurements}));
 }finally{await browser.close();}
});
