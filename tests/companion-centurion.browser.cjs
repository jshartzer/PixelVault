const {test}=require('node:test'),assert=require('node:assert/strict');
const {chromium}=require('playwright'),http=require('node:http'),fs=require('node:fs'),path=require('node:path');
const web=path.resolve(__dirname,'../src/PixelVault.Companion/wwwroot'), rewards=require(path.join(web,'centurion.js'));
const ids=['all-roads','all-interviews','all-mails','all-lll-orders','cargo-60000kg'];
const titles=['All Roads Lead Home','The Whole Story','Complete Correspondence','Nothing Left to Deliver','Heavy Haul'];
const collection=(mask=0,scope='alice')=>({provider:'steam',gameId:'1850570',gameTitle:"Death Stranding Director’s Cut",scope,badgeAvailable:true,badgeUnlocked:mask===31,items:ids.map((id,i)=>({id:'century.death-stranding-dc.'+id,title:titles[i],description:'Complete the verified game objective.',order:i,points:100,unlocked:!!(mask&(1<<i))}))});
test('reward history isolates accounts, survives unknown reads, and queues badge last',()=>{
 assert.equal(rewards.shelf(undefined,'steam','42'),'');
 assert.equal(rewards.shelf({...collection(),items:[]},'steam','42'),'');
 assert.match(rewards.shelf(collection(0),'steam','1850570'),/Centurion Achievements/);
 for(let mask=0;mask<32;mask++){
  const baseline=rewards.observe({},[collection(mask)]);assert.equal(baseline.notices.length,0);
  const earned=rewards.observe(baseline.next,[collection(31)]);
  if(mask!==31)assert.equal(earned.notices.at(-1).badge,true);
  assert.equal(rewards.observe(earned.next,[collection(31)]).notices.length,0);
  assert.equal(rewards.observe(rewards.observe(earned.next,[collection(0)]).next,[collection(31)]).notices.length,0);
  assert.equal(rewards.observe(earned.next,[collection(31,'bob')]).notices.length,0);
 }
});
test('live profile sockets, six reward effects, queues, pause, reload, account switch and reduced motion',async()=>{
 let snapshot=collection();const game={provider:'steam',gameId:'1850570',groupId:'ds',title:snapshot.gameTitle,platform:'Steam',trackerCount:0,stats:{},screenshots:{count:0}};
 const server=http.createServer((req,res)=>{
  const url=new URL(req.url,'http://localhost');
  if(url.pathname.endsWith('/badge')||url.pathname.endsWith('/icon')){
   let name='death-stranding-centurion.png';if(url.pathname.endsWith('/icon'))name=ids.find(id=>url.pathname.includes(id))+'-unlocked.png';
   res.setHeader('Content-Type','image/png');res.end(fs.readFileSync(path.resolve(web,'../../PixelVault.Tracking/CenturyArtwork',name)));return;
  }
  if(url.pathname.startsWith('/api/v1')){
   const responses={'/session':{authenticated:true},'/workspace':{id:'centurion-test'},'/antiforgery':{token:'test'},'/games':[game],'/games/steam/1850570':game,'/trackers':[],
    '/centurion':[snapshot],'/achievement-updates':[], '/preferences/game-sections':{sections:['trackers','achievements','screenshots']},'/preferences/library':{sort:'title-az',pins:[]},'/refresh/connection':{paired:false,online:false,supported:[]},'/screenshots/recent':{photos:[]},'/achievements/steam/1850570':{rows:[],categories:[],categoryOrder:[],dlcs:[],century:{definitions:[]}}};
   res.setHeader('Content-Type','application/json');res.end(JSON.stringify(responses[url.pathname.slice(7)]??{}));return;
  }
  const file=path.join(web,url.pathname==='/'?'index.html':url.pathname);if(!fs.existsSync(file)){res.writeHead(404).end();return;}
  res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':'text/html');res.end(fs.readFileSync(file));
 });
 await new Promise(r=>server.listen(0,'127.0.0.1',r));const browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage({viewport:{width:1280,height:850}}),errors=[];page.on('pageerror',e=>errors.push(e.message));page.setDefaultTimeout(8000);await page.clock.install();
  await page.goto(`http://127.0.0.1:${server.address().port}/#game/steam/1850570`);
  await page.locator('.centurion-slot').first().waitFor();assert.equal(await page.locator('.centurion-slot').count(),5);
  const card=page.locator('.reward-notification'),refresh=()=>page.evaluate(()=>centurionUI.refresh(updateScope));
  await refresh();assert.equal(await card.isVisible(),false);
  await page.locator('.centurion-slot').nth(3).click();assert.match(await page.locator('.centurion-detail').innerText(),/Nothing Left to Deliver/);
  assert.equal(await page.locator('.centurion-slot').nth(3).getAttribute('aria-pressed'),'true');
  await refresh();assert.equal(await page.locator('.centurion-slot').nth(3).getAttribute('aria-pressed'),'true');
  const selectedLink=await page.locator('.centurion-detail a').getAttribute('href');assert.match(selectedLink,/all-lll-orders/);
  await page.evaluate(()=>centurionUI.render(document.getElementById('centurion-profile'),'steam','42'));
  assert.equal(await page.locator('#centurion-profile').isVisible(),false);assert.equal(await page.locator('.centurion-collection').count(),0);
  await page.evaluate(()=>renderCenturionShelf());await page.locator('.centurion-slot').first().waitFor();
  for(let i=0;i<5;i++){
   snapshot=collection((1<<(i+1))-1);await refresh();await page.clock.runFor(900);
   assert.equal(await page.locator('.reward-title').textContent(),titles[i]);
   await page.locator('.reward-art').evaluate(img=>img.decode());
   assert.equal(await page.locator('.centurion-slot.is-earned').count(),i+1);
   if(i===0){await card.hover();await page.clock.runFor(7000);assert.equal(await card.isVisible(),true);await page.mouse.move(0,0);}
   await page.clock.runFor(5500);
  }
  assert.equal(await card.isVisible(),true);assert.equal(await page.locator('.reward-title').textContent(),'Centurion');
  await page.locator('.reward-art').evaluate(img=>img.decode());
  const evidence=process.env.PV_CENTURION_EVIDENCE;
  if(evidence)await page.screenshot({path:path.join(evidence,'web-desktop.png'),fullPage:true});
  for(const width of [390,320]){
   await page.setViewportSize({width,height:900});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false);
   const box=await card.boundingBox();assert.ok(box.x>=0&&box.x+box.width<=width);
   if(evidence)await page.screenshot({path:path.join(evidence,'web-'+width+'.png'),fullPage:true});
  }
  await page.clock.runFor(8000);assert.equal(await card.isVisible(),false);
  for(const width of [320,390,1280]){
   await page.setViewportSize({width,height:900});await page.locator('.centurion-slot').nth(4).click();
   assert.match(await page.locator('.centurion-detail').innerText(),/Heavy Haul/);
   assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false);
   const boxes=await page.locator('.centurion-slot').evaluateAll(els=>els.map(e=>{const r=e.getBoundingClientRect();return {top:r.top,width:r.width,height:r.height};}));
   assert.ok(boxes.every(b=>Math.abs(b.top-boxes[0].top)<1&&b.width>=40&&b.height>=44));
   if(evidence)await page.locator('.centurion-collection').screenshot({path:path.join(evidence,'rail-'+width+'.png')});
  }
  await page.reload();await page.locator('.centurion-slot').first().waitFor();await refresh();assert.equal(await card.isVisible(),false);
  snapshot=collection(0,'bob');await refresh();snapshot=collection(1,'bob');await refresh();assert.equal(await card.isVisible(),true);
  await page.emulateMedia({reducedMotion:'reduce'});assert.equal(await card.evaluate(el=>getComputedStyle(el).animationName),'none');
  snapshot=collection(31,'alice');await refresh();assert.equal(await card.isVisible(),false);
  snapshot=collection(3,'bob');await refresh();await page.getByRole('button',{name:'Dismiss reward notification'}).focus();await page.keyboard.press('Escape');await page.clock.runFor(400);assert.equal(await card.isVisible(),false);
  assert.deepEqual(errors,[]);
 }finally{await browser.close();await new Promise(r=>server.close(r));}
});
