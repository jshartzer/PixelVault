const {test}=require('node:test'),assert=require('node:assert/strict');
const {chromium}=require('playwright');
const fs=require('node:fs'),http=require('node:http'),path=require('node:path');
const web=path.resolve(__dirname,'../src/PixelVault.Companion/wwwroot');
test('the update dialog shows +2 for both interview trackers and acknowledges the new baseline',async()=>{
  const common={provider:'steam',gameId:'1850570',gameTitle:"Death Stranding Director's Cut",totals:{completed:69,total:100},updateVersion:'v1'};
  let trackers=[{...common,id:'catalogue',title:'Interview catalogue',updateProgress:[{id:'one',current:0},{id:'two',current:0},{id:'three',current:1}]},
    {...common,id:'counter',title:'Interviews — The Past Guides the Present',updateProgress:[{id:'interviews-read',current:69}]}];
  const server=http.createServer((req,res)=>{
    const url=new URL(req.url,'http://localhost');
    if(url.pathname.startsWith('/api/v1')){
      const endpoint=url.pathname.slice(7);res.setHeader('Content-Type','application/json');
      const responses={'/session':{authenticated:true,demoEnabled:false},'/antiforgery':{token:'test'},'/games':[],
        '/trackers':trackers,'/preferences/game-sections':{sections:['trackers','achievements','screenshots']},
        '/preferences/library':{sort:'title-az',pins:[]},'/refresh/connection':{paired:false,online:false,supported:[]},
        '/workspace':{id:'interview-updates'},'/screenshots/recent':{photos:[]}};
      res.end(JSON.stringify(responses[endpoint]??{}));return;
    }
    const file=path.join(web,url.pathname==='/'?'index.html':url.pathname);
    if(!fs.existsSync(file)){res.writeHead(404);res.end();return;}
    res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':file.endsWith('.svg')?'image/svg+xml':'text/html');res.end(fs.readFileSync(file));
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const browser=await chromium.launch({headless:true,channel:'chrome'});
  try{
    const page=await browser.newPage({viewport:{width:390,height:844}}),errors=[];
    page.on('pageerror',error=>errors.push(error.message));
    await page.goto(`http://127.0.0.1:${server.address().port}/`);
    await page.waitForFunction(()=>localStorage.getItem('PixelVault.Companion.seen-trackers.interview-updates'));
    trackers=[{...trackers[0],updateVersion:'v2',updateProgress:[{id:'one',current:1},{id:'two',current:1},{id:'three',current:1}]},
      {...trackers[1],updateVersion:'v2',updateProgress:[{id:'interviews-read',current:71}]}];
    await page.reload();await page.locator('#refresh-summary[open]').waitFor();
    assert.deepEqual(await page.locator('#refresh-summary .update-increase').allTextContents(),['+2','+2']);
    assert.equal(await page.getByLabel('Progress increased by 2',{exact:true}).count(),2);
    assert.equal(await page.locator('#refresh-summary-message').innerText(),'2 trackers changed.');
    assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false);
    await page.screenshot({path:path.resolve(__dirname,'../tmp/companion-interview-update-counts-0.36.6.png')});
    await page.locator('#refresh-summary-title').click();
    await page.reload();await page.waitForFunction(()=>JSON.parse(localStorage.getItem('PixelVault.Companion.seen-trackers.interview-updates')).counter.progress[0].current===71);
    assert.equal(await page.locator('#refresh-summary').isVisible(),false);assert.deepEqual(errors,[]);
  }finally{await browser.close();await new Promise(resolve=>server.close(resolve));}
});

test('completion rows stand out within game groups and only notify on the transition',async()=>{
  const scope='mail-goal-updates',common={provider:'steam',gameId:'1850570',gameTitle:"Death Stranding Director's Cut",updateVersion:'v1'};
  let trackers=[{...common,id:'catalogue',title:'Email catalogue',achievementId:null,totals:{completed:139,total:240},updateProgress:[{id:'mail',current:0}]},
    {...common,id:'goal',title:'The Post Guides the Present',achievementId:'038',totals:{completed:99,total:100},updateProgress:[{id:'read',current:99}]},
    {...common,id:'side-ops',title:'Side Ops',gameId:'287700',gameTitle:'Metal Gear Solid V: The Phantom Pain',totals:{completed:1,total:10},updateProgress:[{id:'count',current:1}]}];
  const server=http.createServer((req,res)=>{
    const url=new URL(req.url,'http://localhost');
    if(url.pathname.startsWith('/api/v1')){
      res.setHeader('Content-Type','application/json');
      const responses={'/session':{authenticated:true,demoEnabled:false},'/antiforgery':{token:'test'},'/games':[],
        '/trackers':trackers,'/preferences/game-sections':{sections:['trackers','achievements','screenshots']},
        '/preferences/library':{sort:'title-az',pins:[]},'/refresh/connection':{paired:false,online:false,supported:[]},
        '/workspace':{id:scope},'/screenshots/recent':{photos:[]}};
      res.end(JSON.stringify(responses[url.pathname.slice(7)]??{}));return;
    }
    const file=path.join(web,url.pathname==='/'?'index.html':url.pathname);
    if(!fs.existsSync(file)){res.writeHead(404);res.end();return;}
    res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':file.endsWith('.svg')?'image/svg+xml':'text/html');res.end(fs.readFileSync(file));
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const browser=await chromium.launch({headless:true,channel:'chrome'});
  try{
    const page=await browser.newPage({viewport:{width:390,height:844}}),errors=[];
    page.on('pageerror',error=>errors.push(error.message));
    await page.goto(`http://127.0.0.1:${server.address().port}/`);
    const baseline=()=>page.waitForFunction(()=>localStorage.getItem('PixelVault.Companion.seen-trackers.mail-goal-updates'));
    await baseline();
    trackers=[{...trackers[0],updateVersion:'v2',totals:{completed:140,total:240},updateProgress:[{id:'mail',current:1}]},
      {...trackers[1],updateVersion:'v2',totals:{completed:100,total:100},updateProgress:[{id:'read',current:100}]},
      {...trackers[2],updateVersion:'v2',totals:{completed:5,total:10},updateProgress:[{id:'count',current:5}]}];
    await page.reload();await page.locator('#refresh-summary[open]').waitFor();
    assert.deepEqual(await page.locator('.update-game h3').allTextContents(),[common.gameTitle,trackers[2].gameTitle]);
    assert.deepEqual(await page.locator('#refresh-summary-list strong').allTextContents(),['The Post Guides the Present','Email catalogue','Side Ops']);
    assert.equal(await page.locator('#refresh-summary-message').isVisible(),false);
    assert.deepEqual(await page.locator('#refresh-summary .update-increase').allTextContents(),['+1','+4']);
    assert.equal(await page.locator('.update-completed .update-completion-label').innerText(),'Tracker completed');
    assert.equal(await page.locator('.update-completed .update-increase').count(),0);
    for(const width of [320,390,1280]){
      await page.setViewportSize({width,height:844});
      assert.equal(await page.locator('#refresh-summary').evaluate(dialog=>dialog.scrollWidth>dialog.clientWidth),false);
    }
    await page.emulateMedia({reducedMotion:'reduce'});
    assert.equal(await page.locator('.update-sparkle').evaluate(node=>getComputedStyle(node).animationName),'none');
    await page.keyboard.press('Escape');
    await page.waitForFunction(()=>JSON.parse(localStorage.getItem('PixelVault.Companion.seen-trackers.mail-goal-updates')).goal.complete===true);
    trackers=[trackers[0],{...trackers[1],updateVersion:'v3',updateProgress:[{id:'read',current:101}]},trackers[2]];
    await page.reload();
    await page.waitForFunction(()=>JSON.parse(localStorage.getItem('PixelVault.Companion.seen-trackers.mail-goal-updates')).goal.progress[0].current===101);
    assert.equal(await page.locator('#refresh-summary').isVisible(),false);
    trackers=[trackers[0],trackers[1],{...trackers[2],updateVersion:'v3',totals:{completed:10,total:10},updateProgress:[{id:'count',current:10}]}];
    await page.reload();await page.locator('#refresh-summary[open]').waitFor();
    assert.equal(await page.locator('#refresh-summary-title').innerText(),'Tracker completed');
    assert.equal(await page.locator('#refresh-summary .update-increase').count(),0);
    assert.deepEqual(errors,[]);
  }finally{await browser.close();await new Promise(resolve=>server.close(resolve));}
});
