const {test}=require('node:test'),assert=require('node:assert/strict');
const {chromium}=require('playwright');
const fs=require('node:fs'),http=require('node:http'),path=require('node:path');
const web=path.resolve(__dirname,'../src/PixelVault.Companion/wwwroot');

test('real push updates the browser without polling, defers edits, retries and catches up on reconnect',async()=>{
 let sequence=1,revision='v1',earned=false,fail=false;const clients=new Set(),calls=[];
 const game={provider:'steam',gameId:'42',groupId:'game',title:'Test game',platform:'Steam',stats:{},trackerCount:1};
 const trackers=()=>[{...game,id:'count',title:'Deliveries',gameTitle:game.title,totals:{completed:sequence,total:10},updateVersion:revision,updateProgress:[{id:'one',current:sequence}]}];
 const connection=()=>({pushEnabled:true,local:true,online:true,supported:[],workspaceId:'push-test',versions:{library:revision,trackers:revision,achievements:earned?'earned':'locked',photos:'photos',maps:'maps'}});
 const snapshot=()=>({schema:1,workspaceId:'push-test',epoch:'server',sequence,versions:connection().versions,trackers:{count:revision},achievements:{'steam:42':earned?'earned':'locked'},connection:connection()});
 function send(res){res.write('event: refresh\ndata: '+JSON.stringify(snapshot())+'\n\n');}
 const broadcast=()=>{for(const res of clients)send(res);};
 const server=http.createServer((req,res)=>{
  const url=new URL(req.url,'http://fixture'),endpoint=url.pathname.slice(7);
  if(url.pathname.startsWith('/api/v1')){
   calls.push(endpoint);
   if(endpoint==='/refresh/events'){res.writeHead(200,{'Content-Type':'text/event-stream','Cache-Control':'no-cache'});res.write('retry: 100\n\n');clients.add(res);send(res);req.on('close',()=>clients.delete(res));return;}
   res.setHeader('Content-Type','application/json');
   if(endpoint==='/games'&&fail){res.writeHead(503);res.end('{}');return;}
   if(endpoint.endsWith('/unlocked-icon')){res.writeHead(404);res.end('{}');return;}
   const responses={'/session':{authenticated:true},'/antiforgery':{token:'test'},'/games':[game],'/trackers':trackers(),
    '/preferences/game-sections':{sections:['trackers','achievements','screenshots']},'/preferences/library':{sort:'title-az',pins:[]},
    '/workspace':{id:'push-test',mode:'direct-local'},'/refresh/connection':connection(),'/screenshots/recent':{photos:[]},
    '/achievement-updates':[{provider:'steam',gameId:'42',scope:'account',locked:earned?[]:['ONE'],unlocked:earned?['ONE']:[]}],
    '/achievements/steam/42':{rows:[{achievement:{id:'ONE',title:'First delivery',progressKnown:true,unlocked:earned}}]}};
   res.end(JSON.stringify(responses[endpoint]??{}));return;
  }
  const file=path.join(web,url.pathname==='/'?'index.html':url.pathname);if(!fs.existsSync(file)){res.writeHead(404);res.end();return;}
  res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':'text/html');res.end(fs.readFileSync(file));
 });
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));let browser;
 try{
  browser=await chromium.launch({headless:true,channel:'chrome'});const page=await browser.newPage({viewport:{width:390,height:844}}),errors=[];
  page.on('pageerror',e=>errors.push(e.message));await page.goto(`http://127.0.0.1:${server.address().port}/`);
  await page.waitForFunction(()=>!initializing&&refreshStream?.connected&&appliedPush?.sequence===1);
  const idle=calls.length;await page.waitForTimeout(5500);assert.deepEqual(calls.slice(idle),[],'A healthy idle stream makes no polling requests');
  await page.locator('#game-search').fill('Test');const blocked=calls.length;
  revision='v2';sequence=2;earned=true;broadcast();await page.waitForTimeout(700);
  assert.deepEqual(calls.slice(blocked),[],'Focused edits defer data reads and rendering');
  fail=true;await page.locator('#page-title').click();await page.waitForTimeout(700);assert.equal(await page.locator('#refresh-summary').isVisible(),false);
  fail=false;await page.waitForFunction(()=>appliedPush?.sequence===2);
  assert.equal(await page.locator('#refresh-summary').isVisible(),false,'Automatic notices collect a short batch before opening');
  revision='v3';sequence=3;broadcast();
  await page.locator('#refresh-summary[open]').waitFor();assert.match(await page.locator('#refresh-summary-message').innerText(),/1 achievement unlocked/);
  assert.deepEqual(await page.locator('.update-total').allTextContents(),['3/10']);
  assert.equal(await page.locator('.update-unlocked-count').innerText(),'1 unlocked');
  assert.equal(await page.locator('.update-increase').innerText(),'+2');
  revision='v4';sequence=4;broadcast();
  await page.waitForFunction(()=>document.querySelector('.update-total')?.textContent==='4/10');
  assert.equal(await page.locator('.update-increase').innerText(),'+3');
  assert.equal(await page.locator('.update-achievements li').count(),1);
  await page.locator('#refresh-summary-close').click();
  await page.waitForTimeout(3300);
  assert.equal(await page.locator('#refresh-summary').isVisible(),false,'Dismissal acknowledges the whole updated batch');
  for(const res of clients)res.end();revision='v5';sequence=5;
  await page.waitForFunction(()=>appliedPush?.sequence===5);await page.locator('#refresh-summary[open]').waitFor();
  assert.equal(await page.locator('.update-achievements').count(),0,'Reconnect must not repeat the acknowledged achievement');
  await page.locator('#refresh-summary-close').click();
  assert.equal(calls.filter(p=>p==='/refresh/connection').length,1,'Push deliveries use the supplied connection snapshot');
  assert.deepEqual(errors,[]);
 }finally{for(const res of clients)res.end();await browser?.close();server.closeAllConnections();await new Promise(resolve=>server.close(resolve));}
});
