const {test}=require('node:test'),assert=require('node:assert/strict'),{chromium}=require('playwright');
const fs=require('node:fs'),path=require('node:path'),http=require('node:http');
const web=path.resolve(__dirname,'../src/PixelVault.Companion/wwwroot');
const policy=fs.readFileSync(path.join(web,'../Program.cs'),'utf8').match(/ContentSecurityPolicy = "([^"]+)"/)[1];
test('map pins preserve tiles, selection and focus through scoped saved-progress changes on phone and desktop',async()=>{
 let browser;
 const game={provider:'steam',gameId:'42',groupId:'game-42',title:'Test journey',platform:'Steam',trackerCount:0,stats:{}};
 const region={id:'east',provider:'steam',gameId:'42',title:'Eastern region',region:'East',pixelWidth:1025,pixelHeight:513};
 const original=[
  {id:'one',label:'#01',detail:'By the northern terminal',x:0,y:0,trackerId:'tracker',itemId:'one'},
  {id:'two',label:'#02',detail:'On the ridge. Restore its data at a facility.',x:.5,y:.5,trackerId:'tracker',itemId:'two'},
  {id:'three',label:'#03',detail:'Across the southern river',x:1,y:1,trackerId:'tracker',itemId:'three'},
  {id:'four',label:'#04',detail:'No saved item',x:.8,y:.2,trackerId:'missing',itemId:'four'},
  {id:'place',label:'<img src=x onerror=alert(1)>',detail:'An unlinked landmark <script>bad()</script>',x:.2,y:.8}
 ];
 let pins,states,version,revision,imageVersion,workspace,progressFail,hold,pending=[],tileRequests=0,detailRequests=0,listQueries=[];
 function reset(){pins=structuredClone(original);states=['completed','remaining','unknown','unavailable','unlinked'];version=1;revision=1;imageVersion='image1';workspace='account-one';progressFail=false;hold=false;}
 const server=http.createServer(async(req,res)=>{
  const url=new URL(req.url,'http://localhost');res.setHeader('Content-Security-Policy',policy);res.setHeader('Cache-Control','no-store');
  const json=(data,code=200)=>{res.writeHead(code,{'Content-Type':'application/json'});res.end(JSON.stringify(data));};
  if(url.pathname.startsWith('/api/v1')){
   const endpoint=url.pathname.slice(7);
   if(endpoint.includes('/tiles/')){tileRequests++;res.writeHead(200,{'Content-Type':'image/svg+xml'});res.end('<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512"><rect width="512" height="512" fill="#264b54"/><path d="M0 0H512V512H0Z" fill="none" stroke="#719d94"/></svg>');return;}
   if(endpoint.endsWith('/manifest'))return json({version:imageVersion,width:1025,height:513,maxZoom:2,tileSize:512,tileUrl:`/api/v1/maps/steam/42/east/tiles/{z}/{x}/{y}?v=${imageVersion}`});
   if(endpoint.endsWith('/progress')){
    const payload={workspaceId:workspace,mapRevision:revision,version:workspace+':'+version,pins:pins.map((p,i)=>({id:p.id,status:states[i],current:i<2?(states[i]==='completed'?1:0):null,target:i<3?1:null,trackerUrl:i<3?'#tracker/tracker':null,achievementUrl:i<3?'#achievement/steam/42/ONE':null}))};
    if(hold)await new Promise(resolve=>pending.push(resolve));
    return progressFail?json({error:'Saved progress is unavailable'},503):json(payload);
   }
   if(endpoint==='/maps/steam/42/east'){detailRequests++;return json({map:{...region,revision},pins});}
   if(endpoint==='/maps/steam/42'){listQueries.push(url.search);return json({enabled:true,maps:[region]});}
   const values={'/session':{authenticated:true,demoEnabled:false},'/antiforgery':{token:'test'},'/workspace':{id:workspace},'/games':[game],'/games/steam/42':game,'/trackers':[], '/preferences/game-sections':{sections:['trackers','achievements','screenshots']},'/preferences/library':{sort:'title-az',pins:[]},'/refresh/connection':{workspaceId:workspace,paired:false,online:false,supported:[]},'/screenshots/recent':{photos:[]}};
   return json(values[endpoint]||{});
  }
  const file=path.join(web,url.pathname==='/'?'index.html':url.pathname);if(!fs.existsSync(file)){res.writeHead(404);res.end();return;}
  res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':file.endsWith('.png')?'image/png':'text/html');res.end(fs.readFileSync(file));
 });
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
 try{
  browser=await chromium.launch({channel:'chrome',headless:true});
  for(const width of [390,1280]){
   reset();const context=await browser.newContext({viewport:{width,height:900},hasTouch:true,isMobile:width===390});const page=await context.newPage(),errors=[];
   page.setDefaultTimeout(7000);page.on('pageerror',e=>errors.push(e.message));
   await page.addInitScript(()=>{window.cspErrors=[];document.addEventListener('securitypolicyviolation',e=>cspErrors.push(e.violatedDirective));});
   await page.goto(`http://127.0.0.1:${server.address().port}/#map/steam/42/east`);
   await page.waitForSelector('.map-marker.pin-completed');await page.waitForSelector('.leaflet-tile-loaded');
   const markers=page.locator('.map-marker'),rows=page.locator('.map-pin-row');
   assert.equal(await markers.count(),5);assert.equal(await rows.count(),5);
   assert.equal(await page.locator('#maps-page img:not(.leaflet-tile)').count(),0,'Pin text cannot inject HTML');
   assert.equal(await rows.last().locator('strong').innerText(),original[4].label);
   // Every normalized point must land on the corresponding native image pixel, including rectangular corners.
   const positions=await page.evaluate(()=>{
    const tile=document.querySelector('.leaflet-tile-loaded'),r=tile.getBoundingClientRect();
    const z=Number(new URL(tile.src).pathname.split('/').at(-3)),x=Number(new URL(tile.src).pathname.split('/').at(-2)),y=Number(new URL(tile.src).pathname.split('/').at(-1));
    return [...document.querySelectorAll('.map-marker')].map(el=>{const b=el.getBoundingClientRect();return {id:el.dataset.pinId,x:(b.x+b.width/2-(r.x-x*r.width))/r.width*512*2**(2-z),y:(b.y+b.height/2-(r.y-y*r.height))/r.width*512*2**(2-z)};});
   });
   for(const p of original){const actual=positions.find(v=>v.id===p.id);assert.ok(Math.abs(actual.x-p.x*1025)<5&&Math.abs(actual.y-p.y*513)<5,`Pin ${p.id} aligns with rectangular tiles`);}
   const two=page.locator('.map-pin-row[data-pin-id="two"]');await two.click();
   assert.equal(await page.locator('#map-pin-title').innerText(),'#02');
   assert.equal(await page.locator('[data-pin-tracker]').getAttribute('href'),'#tracker/tracker');
   assert.equal(await page.locator('[data-pin-achievement]').getAttribute('href'),'#achievement/steam/42/ONE');
   await page.locator('#map-pin-filter').selectOption('remaining');assert.equal(await markers.count(),4);
   await page.locator('#map-pin-search').fill('ridge');assert.equal(await markers.count(),1);
   assert.equal(await markers.innerText(),'02');
   await page.waitForTimeout(350);
   await page.evaluate(()=>{window.savedTiles=[...document.querySelectorAll('.leaflet-tile')];window.savedMarker=document.querySelector('.map-marker');window.savedRow=document.querySelector('.map-pin-row');window.savedPane=document.querySelector('.leaflet-map-pane').style.transform;});
   const requests=tileRequests,details=detailRequests;
   await page.evaluate(()=>pollConnection());
   assert.equal(detailRequests,details,'Unchanged progress does not re-read pin definitions');
   assert.equal(tileRequests,requests,'Polling does not refetch tiles');
   assert.equal(await page.evaluate(()=>savedMarker===document.querySelector('.map-marker')&&savedRow===document.querySelector('.map-pin-row')&&savedTiles.every(t=>t.isConnected)),true);
   assert.equal(await page.locator('#map-pin-search').evaluate(el=>el===document.activeElement),true);
   states[1]='completed';version++;await page.evaluate(()=>pollConnection());
   assert.equal(await markers.count(),0,'Newly completed pin leaves Remaining');
   assert.equal(await page.locator('#map-pin-title').innerText(),'#02','Selected detail stays open after filtering');
   assert.match(await page.locator('.map-pin-status').innerText(),/Completed/);assert.equal(await page.locator('.map-pin-filtered').isVisible(),true);
   assert.equal(await page.evaluate(()=>savedTiles.every(t=>t.isConnected)&&savedPane===document.querySelector('.leaflet-map-pane').style.transform),true);
   assert.equal(tileRequests,requests);assert.equal(await page.locator('#map-pin-search').inputValue(),'ridge');
   await page.locator('#map-pin-filter').selectOption('completed');assert.equal(await markers.count(),1);
   progressFail=true;await page.locator('[data-pins-refresh]').click();await page.waitForSelector('.map-progress-message:not([hidden])');
   assert.match(await page.locator('.map-pin-status').innerText(),/unavailable/);assert.equal(await markers.count(),0);assert.equal(await page.locator('[data-pin-tracker]').isVisible(),false);
   progressFail=false;await page.locator('[data-pins-refresh]').click();await page.waitForSelector('.map-marker.pin-completed');
   await page.locator('[data-pin-locate]').click();assert.equal(await markers.count(),5);assert.equal(await page.locator('#map-pin-filter').inputValue(),'all');
   // A metadata-only edit updates a pin in place and keeps the existing image pyramid and viewport.
   await page.waitForTimeout(350);await page.evaluate(()=>{window.savedTiles=[...document.querySelectorAll('.leaflet-tile')];window.savedPane=document.querySelector('.leaflet-map-pane').style.transform;window.savedMarker=document.querySelector('.map-marker[data-pin-id="two"]');});
   pins[1].x=.6;pins[1].detail='Updated ridge description';revision++;version++;await page.evaluate(()=>mapUI.refresh());
   assert.match(await page.locator('.map-pin-description').innerText(),/Updated ridge/);
   assert.equal(await page.evaluate(()=>savedMarker===document.querySelector('.map-marker[data-pin-id="two"]')&&savedTiles.every(t=>t.isConnected)&&savedPane===document.querySelector('.leaflet-map-pane').style.transform),true);
   // Keyboard activation and touch-sized targets, with a selected panel in full-screen mode.
   const unknown=page.locator('.map-marker[data-pin-id="three"]');await unknown.focus();await page.keyboard.press('Enter');assert.equal(await page.locator('#map-pin-title').innerText(),'#03');
   assert.ok((await unknown.boundingBox()).width>=44);await page.locator('[data-map-expand]').click();
   fs.mkdirSync(path.resolve(__dirname,'../tmp/map-pins-step4'),{recursive:true});await page.screenshot({path:path.resolve(__dirname,`../tmp/map-pins-step4/pins-${width}.png`),fullPage:true});
   assert.equal(await page.locator('body').evaluate(el=>el.scrollWidth<=innerWidth),true);
   await page.locator('#map-canvas').focus();await page.keyboard.press('Escape');
   await page.evaluate(()=>navigation.go('#maps/steam/42/tracker/tracker'));await page.waitForSelector('.map-card');assert.equal(listQueries.at(-1),'?tracker=tracker');
   await page.evaluate(()=>navigation.go('#maps/steam/42/achievement/ONE'));await page.waitForSelector('.map-card');assert.equal(listQueries.at(-1),'?achievement=ONE');
   await page.locator('.map-show-all').click();await page.waitForURL('**/#maps/steam/42');
   await page.locator('.map-card').click();await page.waitForSelector('.map-marker');assert.equal(await page.locator('#map-pin-title').innerText(),'#03','Region return restores selection');
   hold=true;await page.evaluate(()=>{void mapUI.refresh();});await page.waitForTimeout(50);assert.ok(pending.length);
   await page.evaluate(()=>navigation.go('#maps/steam/42'));hold=false;for(const done of pending.splice(0))done();await page.waitForTimeout(100);
   assert.equal(await page.locator('.map-marker').count(),0,'Delayed progress cannot repaint a route that was left');
   await page.locator('.map-card').click();await page.waitForSelector('.map-marker');
   workspace='account-two';states=states.map((s,i)=>i<3?'unknown':s);version++;await page.evaluate(()=>pollConnection());await page.waitForSelector('.map-marker.pin-unknown');
   assert.equal(await page.locator('.map-marker.pin-completed').count(),0);assert.equal(await page.locator('.map-pin-details').isVisible(),false,'Workspace change clears old selection');
   imageVersion='image2';revision++;version++;await page.evaluate(()=>mapUI.refresh());await page.waitForSelector('.leaflet-tile-loaded[src*="image2"]');
   assert.equal(await page.locator('.map-marker').count(),5);assert.deepEqual(errors,[]);assert.deepEqual(await page.evaluate(()=>cspErrors),[]);
   await context.close();
  }
 }finally{for(const done of pending)done();await browser?.close();await new Promise(resolve=>server.close(resolve));}
});
