const {test}=require('node:test'),assert=require('node:assert/strict');
const fs=require('node:fs'),http=require('node:http'),path=require('node:path');
const {chromium}=require('playwright');
const web=path.resolve(__dirname,'../src/PixelVault.Companion/wwwroot');
test('direct-local startup renders before optional reads and idle polls only request changes',async()=>{
 let release,held=true,revision='a',fail=false;
 const calls=[],requests=[];
 const rows=Array.from({length:715},(_,i)=>({provider:'steam',gameId:String(i),groupId:'g'+i,title:'Game '+i,platform:'Steam',trackerCount:0,stats:{achievementTotal:10,achievementUnlocked:2}}));
 const server=http.createServer(async(req,res)=>{
  const url=new URL(req.url,'http://fixture');requests.push(url.pathname);
  if(url.pathname.startsWith('/api/v1')){
   const endpoint=url.pathname.slice(7);calls.push(endpoint);res.setHeader('Content-Type','application/json');
   if(endpoint==='/trackers'&&held)await new Promise(resolve=>release=resolve);
   if(endpoint==='/games'&&fail){res.writeHead(503);res.end('{}');return;}
   const responses={'/session':{authenticated:true},'/antiforgery':{token:'test'},'/games':rows,'/trackers':[],
    '/preferences/game-sections':{sections:['trackers','achievements','screenshots']},'/preferences/library':{sort:'title-az',pins:[]},
    '/refresh/connection':{local:true,paired:false,online:true,supported:[],workspaceId:'performance',versions:{library:'library',trackers:revision,photos:'photos',maps:'maps'}},
    '/workspace':{id:'performance',mode:'direct-local'},'/screenshots/recent':{version:'photos',photos:[]},'/achievement-updates':[]};
   res.end(JSON.stringify(responses[endpoint]??{}));return;
  }
  const file=path.join(web,url.pathname==='/'?'index.html':url.pathname);
  if(!fs.existsSync(file)){res.writeHead(404);res.end();return;}
  res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':file.endsWith('.svg')?'image/svg+xml':file.endsWith('.png')?'image/png':'text/html');res.end(fs.readFileSync(file));
 });
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));let browser;
 try{
  browser=await chromium.launch({headless:true,channel:'chrome'});
  for(const width of [390,1280]){
   held=true;revision='a';calls.length=0;requests.length=0;
   const page=await browser.newPage({viewport:{width,height:844}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
   await page.addInitScript(()=>{const original=setInterval;window.setInterval=(fn,delay,...args)=>[5000,15000].includes(delay)?(window.performancePoll=fn,1):original(fn,delay,...args);});
   await page.goto(`http://127.0.0.1:${server.address().port}/`);
   await page.waitForFunction(()=>document.querySelectorAll('.game-card').length===715);
   assert.equal(held,true,'Library must render before tracker summaries finish');assert.ok(!requests.some(p=>p.includes('leaflet')));
   held=false;release();await page.waitForFunction(()=>!initializing&&typeof performancePoll==='function');
   await page.evaluate(()=>performancePoll());const start=calls.length;
   for(let i=0;i<12;i++)await page.evaluate(()=>performancePoll());
   assert.deepEqual(calls.slice(start),Array(12).fill('/refresh/connection'));
   revision='b';await page.evaluate(()=>performancePoll());assert.equal(calls.filter(p=>p==='/games').length,3);
   await page.locator('#game-search').fill('Game');revision='c';const deferred=calls.length;
   await page.evaluate(()=>performancePoll());assert.deepEqual(calls.slice(deferred),['/refresh/connection']);
   await page.locator('#page-title').click();await page.evaluate(()=>performancePoll());assert.equal(calls.filter(p=>p==='/games').length,4);
   revision='d';fail=true;await page.evaluate(()=>performancePoll());fail=false;const retry=calls.length;
   await page.evaluate(()=>performancePoll());assert.ok(calls.slice(retry).includes('/games'),'Failed refresh must remain pending');
   assert.equal(await page.locator('.game-card').count(),715);assert.deepEqual(errors,[]);await page.close();
  }
 }finally{held=false;release?.();await browser?.close();await new Promise(resolve=>server.close(resolve));}
});
