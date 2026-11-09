const {test}=require('node:test'),assert=require('node:assert/strict');
const {chromium}=require('playwright');
const fs=require('node:fs'),http=require('node:http'),path=require('node:path');
const web=path.resolve(__dirname,'../src/PixelVault.Companion/wwwroot');

test('detailed achievement notifications combine changes, persist dismissal, retry failures and fit phones',async()=>{
  let revision='v1',earned=['OLD'],locked=['ONE','TWO','THREE'],failUpdates=false;
  const calls=[],game={provider:'steam',gameId:'1850570',groupId:'death-stranding',title:"Death Stranding Director’s Cut",platform:'Steam',stats:{},trackerCount:2};
  let trackers=[{...game,id:'structures',title:'Structures',gameTitle:game.title,totals:{completed:2,total:10},updateVersion:'v1',updateProgress:[{id:'count',current:2}]},
    {...game,id:'emails',title:'Emails',gameTitle:game.title,totals:{completed:3,total:240},updateVersion:'v1',updateProgress:[{id:'count',current:3}]}];
  const titles={ONE:'Master Builder',TWO:'Trail-Blazer',THREE:'A very long achievement name with <markup> & characters that must stay readable on a small phone'};
  const server=http.createServer((req,res)=>{
    const url=new URL(req.url,'http://localhost');
    if(url.pathname.startsWith('/api/v1')){
      const endpoint=url.pathname.slice(7);calls.push(endpoint);res.setHeader('Content-Type','application/json');
      if(endpoint.endsWith('/unlocked-icon')){res.writeHead(404);res.end('{}');return;}
      if(endpoint==='/achievement-updates'&&failUpdates){res.writeHead(503);res.end('{}');return;}
      const responses={'/session':{authenticated:true},'/antiforgery':{token:'test'},'/games':[game],'/trackers':trackers,
        '/preferences/game-sections':{sections:['trackers','achievements','screenshots']},'/preferences/library':{sort:'title-az',pins:[]},
        '/refresh/connection':{local:true,online:true,supported:[],workspaceId:'achievement-updates',versions:{library:revision,trackers:revision,photos:'photos'}},
        '/workspace':{id:'achievement-updates',mode:'direct-local'},'/screenshots/recent':{photos:[]},
        '/achievement-updates':[{provider:game.provider,gameId:game.gameId,scope:'account-a',locked,unlocked:earned}],
        '/achievements/steam/1850570':{rows:earned.map(id=>({achievement:{id,title:titles[id]||id,progressKnown:true,unlocked:true}}))}};
      res.end(JSON.stringify(responses[endpoint]??{}));return;
    }
    const file=path.join(web,url.pathname==='/'?'index.html':url.pathname);
    if(!fs.existsSync(file)){res.writeHead(404);res.end();return;}
    res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':file.endsWith('.png')?'image/png':'text/html');res.end(fs.readFileSync(file));
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const browser=await chromium.launch({headless:true,channel:'chrome'});
  try{
    const page=await browser.newPage({viewport:{width:390,height:844}}),errors=[];
    page.on('pageerror',error=>errors.push(error.message));
    await page.addInitScript(()=>{const original=setInterval;window.setInterval=(fn,delay,...args)=>[5000,15000].includes(delay)?1:original(fn,delay,...args);});
    await page.goto(`http://127.0.0.1:${server.address().port}/`);
    await page.waitForFunction(()=>!initializing&&localStorage.getItem('PixelVault.Companion.seen-achievements.achievement-updates'));
    assert.equal(await page.locator('#refresh-summary').isVisible(),false,'Existing unlocks must not alert on first visit');
    assert.equal(calls.filter(p=>p==='/achievements/steam/1850570').length,0,'No full achievement catalogues on startup');
    revision='v2';earned=['OLD','ONE','TWO'];locked=['THREE'];
    trackers=trackers.map((tracker,i)=>({...tracker,updateVersion:'v2',updateProgress:[{id:'count',current:i===0?4:6}]}));
    await page.evaluate(()=>pollConnection({refreshLibrary:true}));await page.locator('#refresh-summary[open]').waitFor();
    assert.equal(await page.locator('#refresh-summary-title').innerText(),'Progress updated');
    assert.equal(await page.locator('#refresh-summary-message').innerText(),'2 trackers changed · 2 achievements unlocked');
    assert.deepEqual(await page.locator('.update-section-heading h3').allTextContents(),['Trackers','Achievements']);
    assert.deepEqual(await page.locator('.update-achievements strong').allTextContents(),['Master Builder','Trail-Blazer']);
    assert.deepEqual(await page.locator('#refresh-summary .update-increase').allTextContents(),['+2','+3']);
    assert.deepEqual(await page.evaluate(()=>JSON.parse(localStorage.getItem('PixelVault.Companion.seen-achievements.achievement-updates'))['account-a'].unlocked),['OLD']);
    const proof=process.env.PIXELVAULT_TEST_ARTIFACTS;
    if(proof){fs.mkdirSync(proof,{recursive:true});await page.screenshot({path:path.join(proof,'detailed-phone.png')});}
    for(const width of [320,390,1280]){
      await page.setViewportSize({width,height:844});
      assert.equal(await page.locator('#refresh-summary').evaluate(dialog=>dialog.scrollWidth>dialog.clientWidth),false);
      assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false);
      if(proof&&width===1280)await page.screenshot({path:path.join(proof,'detailed-desktop.png')});
    }
    await page.locator('#refresh-summary-close').click();
    await page.waitForFunction(()=>JSON.parse(localStorage.getItem('PixelVault.Companion.seen-achievements.achievement-updates'))['account-a'].unlocked.includes('TWO'));
    const checks=calls.filter(p=>p==='/achievement-updates').length;
    await page.evaluate(()=>pollConnection());
    assert.equal(calls.filter(p=>p==='/achievement-updates').length,checks,'Idle polling should reuse the saved snapshot');
    await page.reload();await page.waitForFunction(()=>!initializing);
    assert.equal(await page.locator('#refresh-summary').isVisible(),false,'Dismissed unlocks must not replay');
    revision='v3';earned.push('THREE');locked=[];failUpdates=true;
    await page.evaluate(()=>pollConnection({refreshLibrary:true}));
    assert.equal(await page.locator('#refresh-summary').isVisible(),false);
    failUpdates=false;await page.evaluate(()=>pollConnection());
    await page.locator('#refresh-summary[open]').waitFor();
    assert.equal(await page.locator('#refresh-summary-message').innerText(),'1 achievement unlocked');
    assert.equal(await page.locator('.update-achievements strong').innerText(),titles.THREE);
    assert.equal(await page.locator('.update-section[aria-label="Tracker updates"]').count(),0);
    await page.setViewportSize({width:320,height:568});
    assert.equal(await page.locator('#refresh-summary').evaluate(dialog=>dialog.scrollWidth>dialog.clientWidth),false);
    await page.keyboard.press('Escape');
    await page.waitForFunction(()=>JSON.parse(localStorage.getItem('PixelVault.Companion.seen-achievements.achievement-updates'))['account-a'].unlocked.includes('THREE'));
    assert.deepEqual(errors,[]);
  }finally{await browser.close();await new Promise(resolve=>server.close(resolve));}
});
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
        '/workspace':{id:'interview-updates'},'/screenshots/recent':{photos:[]},'/achievement-updates':[]};
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
    assert.equal(await page.locator('#refresh-summary-message').innerText(),'2 trackers changed');
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
        '/workspace':{id:scope},'/screenshots/recent':{photos:[]},'/achievement-updates':[]};
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
    assert.deepEqual(await page.locator('#refresh-summary-list small').allTextContents(),[common.gameTitle,common.gameTitle,trackers[2].gameTitle]);
    assert.deepEqual(await page.locator('#refresh-summary-list strong').allTextContents(),['The Post Guides the Present','Email catalogue','Side Ops']);
    assert.equal(await page.locator('#refresh-summary-message').innerText(),'3 trackers changed');
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
    assert.equal(await page.locator('#refresh-summary-title').innerText(),'Progress updated');
    assert.equal(await page.locator('#refresh-summary .update-increase').count(),0);
    assert.deepEqual(errors,[]);
  }finally{await browser.close();await new Promise(resolve=>server.close(resolve));}
});
