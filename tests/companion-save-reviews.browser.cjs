const {test}=require('node:test'),assert=require('node:assert/strict'),{chromium}=require('playwright');
const fs=require('node:fs'),http=require('node:http'),path=require('node:path');
const web=path.resolve(__dirname,'../src/PixelVault.Companion/wwwroot');
test('review fetches current decisions and polling clears superseded buttons',async()=>{
 const game={provider:'steam',gameId:'42',groupId:'review-game',title:'Test game',platform:'Steam',trackerCount:0};
 let pending=true,reads=0;
 const review=()=>({id:'old',status:pending?'review':'complete',message:'Save check',canReview:pending,hasOlder:pending,version:'v1',tasks:pending?[{title:'Chips',status:'review',conflicts:0,older:1,changes:[{name:'#03',previous:0,incoming:1,manual:null,older:true}]}]:[]});
 const server=http.createServer((req,res)=>{
  const url=new URL(req.url,'http://localhost');
  if(url.pathname.startsWith('/api/v1')){
   const ep=url.pathname.slice(7);if(ep==='/refresh/old')reads++;
   const result=ep==='/refresh/old'?review():({'/session':{authenticated:true,workspaceMode:'direct-local'},'/antiforgery':{token:'test'},'/games':[game],'/games/steam/42':game,'/trackers':[],'/preferences/game-sections':{sections:['trackers']},'/preferences/library':{sort:'title-az',pins:[]},'/refresh/connection':{workspaceId:'review-test',local:true,online:true,supported:['review-game'],reviewJobs:pending?[{id:'old',groupId:'review-game'}]:[]},'/workspace':{id:'review-test',mode:'direct-local'}})[ep]??{};
   res.setHeader('Content-Type','application/json');res.end(JSON.stringify(result));return;
  }
  const file=path.join(web,url.pathname==='/'?'index.html':decodeURIComponent(url.pathname));if(!file.startsWith(web)||!fs.existsSync(file)){res.writeHead(404);res.end();return;}
  res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':file.endsWith('.html')?'text/html':'application/octet-stream');res.end(fs.readFileSync(file));
 });
 await new Promise(r=>server.listen(0,'127.0.0.1',r));let browser;
 try{browser=await chromium.launch({channel:'msedge',headless:true});for(const width of [390,1280]){
  pending=true;const page=await browser.newPage({viewport:{width,height:844}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.goto(`http://127.0.0.1:${server.address().port}/#game/steam/42`);
  await page.locator('#refresh-status button').waitFor();await page.locator('#refresh-status button').click();await page.locator('#refresh-review[open]').waitFor();
  assert.equal(await page.locator('#refresh-review-changes li').count(),1);assert.match(await page.locator('#refresh-review-changes').innerText(),/saved 0/);
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
  if(width===390)await page.screenshot({path:path.resolve(__dirname,'../artifacts/stale-save-reviews/review-phone.png')});
  await page.locator('#refresh-review-close').click();pending=false;const count=reads;await page.evaluate(()=>openRefreshReview());
  assert.ok(reads>count);assert.equal(await page.locator('#refresh-review[open]').count(),0);assert.equal(await page.locator('#refresh-status button').count(),0);
  pending=true;await page.evaluate(async()=>{pcConnection=await api('/refresh/connection');rememberRefresh(await api('/refresh/old'),'review-game');});pending=false;
  await page.evaluate(()=>pollConnection());assert.equal(await page.locator('#refresh-status button').count(),0);assert.deepEqual(errors,[]);await page.close();
 }}finally{await browser?.close();await new Promise(r=>server.close(r));}
});
