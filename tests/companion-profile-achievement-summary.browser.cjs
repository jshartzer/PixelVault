const {test}=require('node:test'),assert=require('node:assert/strict'),{chromium}=require('playwright');
const fs=require('node:fs'),http=require('node:http'),path=require('node:path');
const web=path.resolve(__dirname,'../src/PixelVault.Companion/wwwroot');
const policy=fs.readFileSync(path.join(web,'../Program.cs'),'utf8').match(/ContentSecurityPolicy = "([^"]+)"/)[1];
test('capture profile summary and achievement link share their Xbox source on mobile and desktop',async()=>{
 const base={title:'BattleBlock Theater',groupId:'battleblock',trackerCount:0,total:0,completed:0};
 const capture={...base,provider:'library',gameId:'G00154',platform:'Xbox 360',stats:{captures:2}};
 const xbox={...base,provider:'xbox',gameId:'1480657456',platform:'Xbox 360',stats:{achievementUnlocked:11,achievementTotal:30}};
 const steam={...base,provider:'steam',gameId:'238460',platform:'Steam',stats:{achievementUnlocked:null,achievementTotal:null}};
 const dsBase={title:"Death Stranding Director's Cut",groupId:'death-stranding',trackerCount:0,total:0,completed:0};
 const dsSteam={...dsBase,provider:'steam',gameId:'1850570',platform:'Steam',trackerCount:1,stats:{achievementUnlocked:37,achievementTotal:63}};
 const dsPs5={...dsBase,provider:'library',gameId:'G20',platform:'PS5',stats:{captures:13}};
 const dsXbox={...dsBase,provider:'library',gameId:'G21',platform:'Xbox',stats:{captures:4}};
 const orphan={...dsBase,title:'Unlinked game',groupId:'unlinked',provider:'library',gameId:'orphan',platform:'PS5',stats:{captures:1}};
 const rows=[capture,steam,xbox,dsPs5,dsXbox,dsSteam,orphan];
 const server=http.createServer((req,res)=>{
  const url=new URL(req.url,'http://localhost');res.setHeader('Content-Security-Policy',policy);
  if(url.pathname.startsWith('/api/v1')){
   const endpoint=url.pathname.slice(7);res.setHeader('Content-Type','application/json');
   const responses={'/session':{authenticated:true,demoEnabled:false},'/antiforgery':{token:'test'},'/games':rows,'/trackers':[],
    '/preferences/game-sections':{sections:['achievements','trackers','screenshots']},'/preferences/library':{sort:'title-az',pins:[]},
    '/refresh/connection':{paired:false,online:false,supported:[]},'/workspace':{id:'profile-summary'},'/screenshots/recent':{photos:[]}};
   res.end(JSON.stringify(responses[endpoint]??(endpoint.startsWith('/games/')?rows.find(r=>endpoint===`/games/${r.provider}/${r.gameId}`):{})));return;
  }
  const file=path.join(web,url.pathname==='/'?'index.html':url.pathname);
  if(!fs.existsSync(file)){res.writeHead(404);res.end();return;}
  res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':file.endsWith('.svg')?'image/svg+xml':file.endsWith('.png')?'image/png':'text/html');res.end(fs.readFileSync(file));
 });
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
 const browser=await chromium.launch({headless:true,channel:'chrome'});
 try{for(const width of [390,1280]){
  xbox.stats={achievementUnlocked:11,achievementTotal:30};
  const page=await browser.newPage({viewport:{width,height:844}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.addInitScript(()=>{const interval=window.setInterval;window.setInterval=(callback,delay,...args)=>delay===15000?(window.libraryPoll=callback,1):interval(callback,delay,...args);});
  const url=`http://127.0.0.1:${server.address().port}/#game/library/G00154`;await page.goto(url);
  await page.waitForFunction(()=>document.querySelector('#game-stats')?.textContent.includes('11 / 30'));
  assert.match(await page.locator('#game-stats').innerText(),/Captures\s+2/);
  assert.equal(await page.locator('#profile-platform option').count(),2);
  assert.equal(await page.locator('#profile-platform').inputValue(),'xbox:1480657456');
  assert.equal(await page.locator('.achievement-section-link').getAttribute('href'),'#achievements/xbox/1480657456');
  await page.locator('#profile-platform').selectOption('steam:238460');
  await page.waitForFunction(()=>document.querySelector('#game-stats')?.textContent.includes('Not synced'));
  assert.equal(await page.locator('.achievement-section-link').getAttribute('href'),'#achievements/steam/238460');
  await page.locator('#profile-platform').selectOption('xbox:1480657456');
  await page.waitForFunction(()=>document.querySelector('#game-stats')?.textContent.includes('11 / 30'));
  xbox.stats={achievementUnlocked:12,achievementTotal:30};await page.evaluate(()=>window.libraryPoll());
  await page.waitForFunction(()=>document.querySelector('#game-stats')?.textContent.includes('12 / 30'));
  xbox.stats={achievementUnlocked:null,achievementTotal:30};await page.evaluate(()=>window.libraryPoll());
  await page.waitForFunction(()=>document.querySelector('#game-stats')?.textContent.includes('— / 30'));
  assert.equal(await page.locator('.achievement-section-link').getAttribute('href'),'#achievements/xbox/1480657456');
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false);
  await page.goto(`http://127.0.0.1:${server.address().port}/`);
  await page.locator('[data-game-card="death-stranding"] a').first().click();
  await page.waitForFunction(()=>document.querySelector('#game-stats')?.textContent.includes('37 / 63'));
  assert.equal(new URL(page.url()).hash,'#game/steam/1850570');
  assert.equal(await page.locator('#profile-platform option').count(),3);
  assert.equal(await page.locator('#profile-platform-label').isVisible(),true);
  await page.locator('#profile-platform').selectOption('library:G20');
  await page.waitForFunction(()=>document.querySelector('#game-stats')?.textContent.includes('Captures'));
  assert.match(await page.locator('#game-stats').innerText(),/Achievements · Steam/);
  assert.equal(await page.locator('.achievement-section-link').getAttribute('href'),'#achievements/steam/1850570');
  await page.goto(`http://127.0.0.1:${server.address().port}/#game/library/orphan`);
  await page.waitForFunction(()=>document.querySelector('#game-stats')?.textContent.includes('Not available'));
  assert.equal(await page.locator('#profile-platform-label').isVisible(),false);
  const unavailable=page.locator('[data-section="achievements"]');
  assert.doesNotMatch(await unavailable.innerText(),/above|selected/);
  const layout=await unavailable.evaluate(el=>({display:getComputedStyle(el).display,padding:parseFloat(getComputedStyle(el).paddingLeft)}));
  assert.equal(layout.display,'flex');assert.ok(layout.padding>=16);
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false);
  assert.deepEqual(errors,[]);await page.close();
 }}finally{await browser.close();await new Promise(resolve=>server.close(resolve));}
});
