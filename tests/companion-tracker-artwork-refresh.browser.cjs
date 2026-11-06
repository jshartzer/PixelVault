const {test}=require('node:test'),assert=require('node:assert/strict'),{chromium}=require('playwright');
const fs=require('node:fs'),http=require('node:http'),path=require('node:path');
const web=path.resolve(__dirname,'../src/PixelVault.Companion/wwwroot');
const policy=fs.readFileSync(path.join(web,'../Program.cs'),'utf8').match(/ContentSecurityPolicy = "([^"]+)"/)[1];
test('tracker artwork survives scrolling and unchanged/changed profile polls',async()=>{
 let trackers=[],iconRequests=0;
 const game={provider:'steam',gameId:'42',groupId:'game42',title:'Test game',platform:'Steam',trackerCount:35,stats:{achievementUnlocked:1,achievementTotal:40}};
 const server=http.createServer(async(req,res)=>{
  const url=new URL(req.url,'http://localhost');res.setHeader('Content-Security-Policy',policy);
  if(url.pathname.includes('/unlocked-icon')){iconRequests++;await new Promise(r=>setTimeout(r,100));res.setHeader('Content-Type','image/png');res.setHeader('Cache-Control','no-store');res.end(Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a6ioAAAAASUVORK5CYII=','base64'));return;}
  if(url.pathname.startsWith('/api/v1')){const endpoint=url.pathname.slice(7);const responses={'/session':{authenticated:true,demoEnabled:false},'/antiforgery':{token:'test'},'/games':[game],'/trackers':trackers,'/preferences/game-sections':{sections:['trackers','achievements','screenshots']},'/preferences/library':{sort:'title-az',pins:[]},'/refresh/connection':{workspaceId:'tracker-art',online:true,supported:[],automaticEnabled:true},'/workspace':{id:'tracker-art',mode:'direct-local'},'/screenshots/recent':{photos:[]}};res.setHeader('Content-Type','application/json');res.end(JSON.stringify(responses[endpoint]??{}));return;}
  const file=path.join(web,url.pathname==='/'?'index.html':url.pathname);if(!fs.existsSync(file)){res.writeHead(404);res.end();return;}res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':file.endsWith('.svg')?'image/svg+xml':file.endsWith('.png')?'image/png':'text/html');res.end(fs.readFileSync(file));
 });await new Promise(r=>server.listen(0,'127.0.0.1',r));const browser=await chromium.launch({channel:'chrome',headless:true});
 try{for(const width of [390,1280]){
  trackers=Array.from({length:35},(_,i)=>({id:'tracker-'+i,provider:'steam',gameId:'42',gameTitle:'Test game',title:'Tracker '+i,achievementId:'art-'+i,totals:{completed:1,total:10},updateVersion:'v1',updateProgress:[]}));
  const page=await browser.newPage({viewport:{width,height:844}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.addInitScript(()=>{const original=setInterval;window.setInterval=(callback,delay,...args)=>[2000,15000].includes(delay)?1:original(callback,delay,...args);});
  await page.goto(`http://127.0.0.1:${server.address().port}/#game/steam/42`);
  await page.waitForSelector('.tracker-section');await page.locator('.tracker-section summary').click();
  const image=page.locator('[data-tracker-icon-owner="tracker-15"] img');await image.scrollIntoViewIfNeeded();await image.evaluate(img=>img.decode());await page.waitForLoadState("networkidle");
  await page.evaluate(()=>{window.savedImage=document.querySelector('[data-tracker-icon-owner="tracker-15"] img');window.savedCard=savedImage.closest('.tracker-card-shell');window.savedTop=savedCard.getBoundingClientRect().top;window.mutations=0;new MutationObserver(c=>mutations+=c.length).observe(document.getElementById('game-sections'),{subtree:true,childList:true});});
  const before=iconRequests;
  for(let i=0;i<3;i++)await page.evaluate(()=>pollConnection({refreshLibrary:true}));
  assert.deepEqual(await page.evaluate(()=>({image:savedImage.isConnected,card:savedCard.isConnected,mutations})),{image:true,card:true,mutations:0});assert.equal(iconRequests,before);
  trackers=trackers.map(t=>({...t,totals:{...t.totals,completed:2},updateVersion:'v2'}));
  await page.evaluate(()=>pollConnection({refreshLibrary:true}));
  assert.equal(await page.evaluate(()=>savedImage.isConnected&&savedImage.complete&&savedImage.naturalWidth>0&&!savedImage.hidden),true);
  assert.match(await page.locator('[data-tracker-icon-owner="tracker-15"] .tracker-progress strong').innerText(),/^2/);
  await page.evaluate(()=>window.scrollBy(0,600));await page.evaluate(()=>window.scrollBy(0,-600));assert.equal(await page.evaluate(()=>savedImage.isConnected),true);
  if(await page.locator('#refresh-summary').isVisible())await page.locator('#refresh-summary').click();
  trackers[15]={...trackers[15],iconId:'trophy'};await page.evaluate(()=>pollConnection({refreshLibrary:true}));assert.equal(await image.count(),0,'Manual icon must replace automatic artwork');
  assert.deepEqual(errors,[]);await page.close();
 }}finally{await browser.close();await new Promise(r=>server.close(r));}
});
