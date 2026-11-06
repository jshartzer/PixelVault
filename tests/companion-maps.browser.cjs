const {test}=require('node:test'),assert=require('node:assert/strict'),{chromium}=require('playwright');
const fs=require('node:fs'),path=require('node:path'),http=require('node:http');
const web=path.resolve(__dirname,'../src/PixelVault.Companion/wwwroot');
const policy=fs.readFileSync(path.join(web,'../Program.cs'),'utf8').match(/ContentSecurityPolicy = "([^"]+)"/)[1];
test('map viewer works under production CSP on phone and desktop, with gestures, recovery and navigation',async()=>{
 let browser;
 const game={provider:'steam',gameId:'42',groupId:'game-42',title:'Test journey',platform:'Steam',trackerCount:0,stats:{}};
 const maps=[{id:'east',title:'Eastern region',region:'East',pixelWidth:1025,pixelHeight:513},{id:'central',title:'Central region',region:'Central',pixelWidth:8192,pixelHeight:8192}];
 let tilesFail=false,listingFail=false,enabled=true,empty=false,hold=false,pending=[],tileRequests=0;
 const server=http.createServer(async(req,res)=>{
  const url=new URL(req.url,'http://localhost');res.setHeader('Content-Security-Policy',policy);res.setHeader('Cache-Control','no-store');
  if(url.pathname.startsWith('/api/v1')){
   const endpoint=url.pathname.slice(7);res.setHeader('Content-Type','application/json');
   if(endpoint.includes('/tiles/')){tileRequests++;if(tilesFail){res.writeHead(503);res.end('{}');return;}res.setHeader('Content-Type','image/svg+xml');res.end(`<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512"><rect width="512" height="512" fill="#264b54"/><path d="M0 0H512V512H0Z" fill="none" stroke="#6d9b94"/><text x="20" y="40" fill="white">${endpoint.split('/tiles/')[1]}</text></svg>`);return;}
   if(endpoint.endsWith('/manifest')){
    if(hold&&endpoint.includes('/east/'))await new Promise(resolve=>pending.push(resolve));
    const m=endpoint.includes('/east/')?maps[0]:maps[1];const prefix='/api/v1/maps/steam/42/'+m.id;
    res.end(JSON.stringify({version:'test',width:m.pixelWidth,height:m.pixelHeight,maxZoom:m.id==='east'?2:4,tileSize:512,tileUrl:prefix+'/tiles/{z}/{x}/{y}?v=test',overviewUrl:prefix+'/tiles/0/0/0?v=test'}));return;
   }
   if(endpoint==='/maps/steam/42'){if(listingFail){res.writeHead(503);res.end('{"error":"Map library unavailable"}');return;}res.end(JSON.stringify({enabled,maps:empty?[]:maps}));return;}
   const pin={id:'center',label:'Center',x:.5,y:.5};
   if(/^\/maps\/steam\/42\/[^/]+\/progress$/.test(endpoint)){res.end(JSON.stringify({workspaceId:'map-test',mapRevision:1,version:'empty',pins:[{id:pin.id,status:'unlinked'}]}));return;}
   if(/^\/maps\/steam\/42\/[^/]+$/.test(endpoint)){res.end(JSON.stringify({map:{...maps.find(m=>endpoint.endsWith('/'+m.id)),revision:1},pins:[pin]}));return;}
   const values={'/session':{authenticated:true,demoEnabled:false},'/antiforgery':{token:'test'},'/workspace':{id:'map-test'},'/games':[game],'/games/steam/42':game,'/trackers':[], '/preferences/game-sections':{sections:['trackers','achievements','screenshots']},'/preferences/library':{sort:'title-az',pins:[]},'/refresh/connection':{paired:false,online:false,supported:[]},'/screenshots/recent':{photos:[]}};
   res.end(JSON.stringify(values[endpoint]||{}));return;
  }
  const file=path.join(web,url.pathname==='/'?'index.html':url.pathname);if(!fs.existsSync(file)){res.writeHead(404);res.end();return;}
  res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':file.endsWith('.png')?'image/png':'text/html');res.end(fs.readFileSync(file));
 });
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
 try{
  browser=await chromium.launch({channel:'chrome',headless:true});
  for(const width of [390,1280]){
   const context=await browser.newContext({viewport:{width,height:900},hasTouch:true,isMobile:width===390});const page=await context.newPage(),errors=[];
   page.setDefaultTimeout(7000);page.on('pageerror',e=>errors.push(e.message));await page.addInitScript(()=>{window.cspErrors=[];document.addEventListener('securitypolicyviolation',e=>cspErrors.push(e.violatedDirective));Object.defineProperty(navigator,'standalone',{get:()=>true});});
   await page.goto(`http://127.0.0.1:${server.address().port}/#game/steam/42`);await page.locator('#profile-maps').click();await page.waitForSelector('.map-card');
   assert.equal(await page.locator('.map-card').count(),2);await page.locator('.map-card').first().click();await page.waitForSelector('.leaflet-tile-loaded');
   assert.equal(await page.locator('#map-region').inputValue(),'east');
   assert.ok(await page.locator('.leaflet-tile').count()<10,'Viewer loads the viewport, not every pyramid level');
   assert.equal(await page.locator('#map-canvas').evaluate(el=>getComputedStyle(el).overflow==='hidden'),true);
   // A map drag must never become global back navigation.
   await page.locator('#map-canvas').evaluate(el=>{
    const start=new Touch({identifier:1,target:el,clientX:80,clientY:400});el.dispatchEvent(new TouchEvent('touchstart',{bubbles:true,cancelable:true,touches:[start],changedTouches:[start]}));
    const end=new Touch({identifier:1,target:el,clientX:240,clientY:405});el.dispatchEvent(new TouchEvent('touchmove',{bubbles:true,cancelable:true,touches:[end],changedTouches:[end]}));el.dispatchEvent(new TouchEvent('touchend',{bubbles:true,cancelable:true,touches:[],changedTouches:[end]}));
   });await page.waitForTimeout(350);assert.match(page.url(),/#map\/steam\/42\/east$/);
   fs.mkdirSync(path.resolve(__dirname,'../tmp/map-viewer-step3'),{recursive:true});await page.screenshot({path:path.resolve(__dirname,`../tmp/map-viewer-step3/viewer-${width}.png`),fullPage:true});
   await page.locator('#map-canvas').scrollIntoViewIfNeeded();
   const beforePinch=await page.locator('.leaflet-tile-loaded').evaluateAll(tiles=>tiles.map(t=>t.src+':'+Math.round(t.getBoundingClientRect().width)).sort());
   const box=await page.locator('#map-canvas').boundingBox(),cdp=await context.newCDPSession(page);
   await cdp.send('Input.synthesizePinchGesture',{x:Math.round(box.x+box.width/2),y:Math.round(Math.min(box.y+box.height/2,850)),scaleFactor:1.5,relativeSpeed:800,gestureSourceType:'touch'});await page.waitForTimeout(350);await cdp.detach();
   assert.notDeepEqual(await page.locator('.leaflet-tile-loaded').evaluateAll(tiles=>tiles.map(t=>t.src+':'+Math.round(t.getBoundingClientRect().width)).sort()),beforePinch,'Touch pinch changes map zoom');
   assert.match(page.url(),/#map\/steam\/42\/east$/,'Pinching must remain in the map');
   const zoom=page.locator('.leaflet-control-zoom-in');await zoom.click();await page.waitForTimeout(350);
   const tilePosition=()=>page.locator('.leaflet-tile-loaded').evaluateAll(tiles=>tiles.map(t=>{const r=t.getBoundingClientRect(),c=document.getElementById('map-canvas').getBoundingClientRect();return {src:t.src,x:Math.round(r.x-c.x),y:Math.round(r.y-c.y),width:Math.round(r.width)};}).sort((a,b)=>a.src.localeCompare(b.src)));const before=await tilePosition();await page.locator('#map-region').selectOption('central');await page.waitForSelector('.leaflet-tile-loaded');await page.locator('#map-region').selectOption('east');await page.waitForSelector('.leaflet-tile-loaded');
   assert.deepEqual((await tilePosition())[0],before[0],'Returning to a region restores its viewport');
   await page.locator('[data-map-expand]').click();assert.equal(await page.locator('html').evaluate(el=>el.classList.contains('map-expanded')),true);
   await page.locator('#map-canvas').focus();await page.keyboard.press('Escape');await page.waitForFunction(()=>!document.documentElement.classList.contains('map-expanded'));assert.equal(await page.locator('html').evaluate(el=>el.classList.contains('map-expanded')),false);
   await page.locator('#map-canvas').focus();await page.keyboard.press('r');await page.locator('[data-map-fit]').click();
   await page.setViewportSize({width:width===390?700:1000,height:700});await page.waitForTimeout(100);assert.equal(await page.locator('body').evaluate(el=>el.scrollWidth<=window.innerWidth),true);
   tilesFail=true;await page.locator('#map-region').selectOption('central');await page.waitForSelector('[data-map-retry]:not([hidden])');assert.match(await page.locator('.map-message').innerText(),/tiles/);
   tilesFail=false;await page.locator('[data-map-retry]').click();await page.waitForSelector('.leaflet-tile-loaded');
   hold=true;await page.evaluate(()=>{void navigation.go('#map/steam/42/east');});await page.waitForTimeout(50);await page.evaluate(()=>navigation.go('#maps/steam/42'));hold=false;for(const done of pending.splice(0))done();await page.waitForSelector('.map-card');await page.waitForTimeout(150);assert.equal(await page.locator('#map-canvas').count(),0,'Stale manifest cannot mount a viewer');
   listingFail=true;await page.evaluate(()=>navigation.go('#map/steam/42/east'));await page.waitForSelector('[data-map-retry]:not([hidden])');listingFail=false;await page.locator('[data-map-retry]').click();await page.waitForSelector('.leaflet-tile-loaded');
   await page.evaluate(()=>navigation.reset('#map/steam/42/east'));await page.reload();await page.waitForSelector('.leaflet-tile-loaded');await page.locator('#navigate-back').click();await page.waitForURL('**/#maps/steam/42');await page.locator('#navigate-back').click();await page.waitForURL('**/#game/steam/42');
   empty=true;await page.locator('#profile-maps').click();await page.waitForFunction(()=>document.querySelector('.map-message')?.textContent.includes('No saved'));empty=false;
   await page.evaluate(()=>navigation.go('#game/steam/42'));enabled=false;await page.locator('#profile-maps').click();await page.waitForFunction(()=>document.querySelector('.map-message')?.textContent.includes('unavailable in this workspace'));enabled=true;
   assert.deepEqual(errors,[]);assert.deepEqual(await page.evaluate(()=>cspErrors),[]);
   await context.close();
  }
  assert.ok(tileRequests>0);
 }finally{for(const done of pending)done();await browser?.close();await new Promise(resolve=>server.close(resolve));}
});
