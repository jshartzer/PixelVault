const {test}=require('node:test'),assert=require('node:assert/strict'),{chromium}=require('playwright');
const path=require('node:path'),fs=require('node:fs');
const productionPolicy=fs.readFileSync(path.resolve(__dirname,'../src/PixelVault.Companion/Program.cs'),'utf8').match(/ContentSecurityPolicy = "([^"]+)"/)[1];
test('achievement polling retains artwork and still reflects changed progress',async()=>{
 const browser=await chromium.launch({channel:'chrome',headless:true});
 try{for(const [width,legacyPolicy] of [[390,false],[1280,false],[390,true]]){
 const context=await browser.newContext({viewport:{width,height:900}});let page=await context.newPage();let images=0,denyImages=false,reopened=false;
 await context.route('http://achievement.test/**',async route=>{if(route.request().url().includes('/api/')){images++;if(denyImages){await route.abort();return;}await new Promise(r=>setTimeout(r,150));await route.fulfill({contentType:'image/png',body:Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a6ioAAAAASUVORK5CYII=','base64')});}else if(route.request().url().endsWith('.js'))await route.fulfill({contentType:'text/javascript',body:fs.readFileSync(path.join(__dirname,'../src/PixelVault.Companion/wwwroot',new URL(route.request().url()).pathname))});else await route.fulfill({contentType:'text/html',headers:{'Content-Security-Policy':legacyPolicy&&reopened?productionPolicy.replace("img-src 'self' data:","img-src 'self'"):productionPolicy},body:['page-heading','breadcrumbs','achievements-page','page-count','achievement-group-form','achievement-use-desktop','achievement-group-cancel','achievement-group-dialog','achievement-category','achievement-dlc'].map(id=>`<div id="${id}"></div>`).join('')});});
 await page.goto('http://achievement.test/');
 await page.addScriptTag({url:'http://achievement.test/artwork-cache.js'});
 await page.addScriptTag({url:'http://achievement.test/achievements.js'});
 await page.evaluate(async()=>{
 location.hash='#achievements/steam/42';window.fixture={rows:['one','two'].map((id,order)=>({achievement:{id,title:id,description:'Test',icon:'https://example.com/'+id,progressKnown:true,unlocked:false,order,groups:{category:'Story',dlc:'',sortOrder:0},tags:''}})),categories:[],categoryOrder:[],dlcs:[]};
 window.ui=PixelVaultAchievements.create({api:async()=>structuredClone(fixture),heading(){},progressBar:()=>'',paintBars(){},status(){},getGames:()=>[],getTrackers:()=>[],newRequestId:()=> 'test',trackerCard:()=>''});await ui.open(['achievements','steam','42']);
 });
 await page.waitForFunction(()=>[...document.querySelectorAll('.achievement-icon img')].every(i=>i.complete&&i.naturalWidth>0));
 await page.evaluate(()=>{window.firstArt=document.querySelector('.achievement-icon img');window.firstCard=document.querySelector('.achievement-row');window.mutations=0;new MutationObserver(c=>mutations+=c.length).observe(document.getElementById('achievements-page'),{childList:true,subtree:true});});
 await page.evaluate(()=>PixelVaultArtworkCache.settled());const initial=images;
 await page.evaluate(async()=>{for(let i=0;i<3;i++)await ui.refresh();});
 assert.deepEqual(await page.evaluate(()=>({connected:firstArt.isConnected,card:firstCard.isConnected,mutations})),{connected:true,card:true,mutations:0});assert.equal(images,initial);
 await page.evaluate(async()=>{fixture.rows[1].achievement.unlocked=true;await ui.refresh();});
 assert.equal(await page.evaluate(()=>firstArt.isConnected&&firstArt.complete),true);
 assert.equal(await page.locator('.achievement-row.earned').count(),1);
 await page.evaluate(()=>PixelVaultArtworkCache.settled());
 await page.evaluate(()=>new Promise(resolve=>{const request=indexedDB.open('PixelVault.Companion.artwork',1);request.onsuccess=()=>{const tx=request.result.transaction('images','readwrite'),store=tx.objectStore('images'),cursor=store.openCursor();cursor.onsuccess=()=>{const c=cursor.result;if(c){c.update({...c.value,updated:Date.now()-7200000});c.continue();}};tx.oncomplete=resolve;};}));
 denyImages=!legacyPolicy;reopened=true;await page.close();page=await context.newPage();await page.goto('http://achievement.test/');
 await page.addScriptTag({url:'http://achievement.test/artwork-cache.js'});
 await page.addScriptTag({url:'http://achievement.test/achievements.js'});
 await page.evaluate(async()=>{
 location.hash='#achievements/steam/42';window.fixture={rows:['one','two'].map((id,order)=>({achievement:{id,title:id,description:'Test',icon:'https://example.com/'+id,progressKnown:true,unlocked:false,order,groups:{category:'Story',dlc:'',sortOrder:0},tags:''}})),categories:[],categoryOrder:[],dlcs:[]};
 window.ui=PixelVaultAchievements.create({api:async()=>structuredClone(fixture),heading(){},progressBar:()=>'',paintBars(){},status(){},getGames:()=>[],getTrackers:()=>[],newRequestId:()=> 'test',trackerCard:()=>''});await ui.open(['achievements','steam','42']);
 });

 await page.waitForFunction(()=>[...document.querySelectorAll('.achievement-icon img')].every(i=>i.complete&&i.naturalWidth>0));
 assert.equal(await page.locator('.achievement-icon img').evaluateAll(images=>images.every(i=>i.src.startsWith('data:'))),!legacyPolicy,'Reopened app should use persistent bytes or recover from a policy rejection');
 await page.waitForTimeout(300);assert.equal(await page.locator('.achievement-icon img').evaluateAll(images=>images.every(i=>i.complete&&i.naturalWidth>0&&!i.hidden)),true,'A failed background refresh must keep cached artwork visible');
 // Scope changes cannot reuse the previous workspace's cached artwork.
 assert.equal(await page.evaluate(async()=> (await PixelVaultArtworkCache.read('different-workspace',[document.querySelector('img').dataset.artworkUrl])).size),0);
 await context.close();
 }}finally{await browser.close();}
});
