const {test}=require('node:test'),assert=require('node:assert/strict');
const {chromium}=require('playwright');
const fs=require('node:fs'),http=require('node:http'),path=require('node:path');
const web=path.resolve(__dirname,'../src/PixelVault.Companion/wwwroot');
const policy=fs.readFileSync(path.join(web,'../Program.cs'),'utf8').match(/ContentSecurityPolicy = "([^"]+)"/)[1];
test('cover footer toggle persists locally, keeps covers intact and preserves pin navigation',async()=>{
  const rows=['none','base-dlc','base','base-dlc-pv'].map((tier,index)=>({provider:'steam',gameId:String(index),groupId:'g'+index,title:'Game '+index,platform:'Steam',trackerCount:0,completionTier:tier,stats:{achievementUnlocked:2,achievementTotal:10}}));
  let layout={sort:'title-az',pins:[]};const writes=[];
  const server=http.createServer(async(req,res)=>{
    const url=new URL(req.url,'http://localhost');res.setHeader('Content-Security-Policy',policy);
    if(url.pathname.startsWith('/api/v1')){
      const endpoint=url.pathname.slice(7);res.setHeader('Content-Type','application/json');
      if(req.method==='POST'){
        let json='';for await(const chunk of req)json+=chunk;const body=JSON.parse(json);writes.push({endpoint,body});
        if(endpoint==='/preferences/library/pins')layout={...layout,pins:body.pinned?[...layout.pins,body.gameId]:layout.pins.filter(id=>id!==body.gameId)};
        res.end(JSON.stringify(layout));return;
      }
      const responses={'/session':{authenticated:true,demoEnabled:false},'/antiforgery':{token:'test'},'/games':rows,'/trackers':[],
        '/preferences/game-sections':{sections:['trackers','achievements','screenshots']},'/preferences/library':layout,
        '/refresh/connection':{paired:false,online:false,supported:[]},'/workspace':{id:'cover-controls'},'/screenshots/recent':{photos:[]}};
      res.end(JSON.stringify(responses[endpoint]??(endpoint.startsWith('/games/')?rows.find(row=>endpoint===`/games/${row.provider}/${row.gameId}`):{})));return;
    }
    const file=path.join(web,url.pathname==='/'?'index.html':url.pathname);
    if(!fs.existsSync(file)){res.writeHead(404);res.end();return;}
    res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':file.endsWith('.svg')?'image/svg+xml':file.endsWith('.png')?'image/png':'text/html');res.end(fs.readFileSync(file));
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const browser=await chromium.launch({headless:true,channel:'chrome'});
  try{
    const page=await browser.newPage({viewport:{width:390,height:844}}),errors=[];page.on('pageerror',error=>errors.push(error.message));
    const url=`http://127.0.0.1:${server.address().port}/`;await page.goto(url);
    await page.waitForFunction(()=>document.querySelectorAll('.cover-actions').length===4);
    const toggle=page.getByRole('switch',{name:'Show cover controls'});
    assert.equal(await toggle.isChecked(),true);
    assert.equal(await toggle.locator('..').innerText(),'');
    assert.equal(await page.locator('.cover-card h2,.cover-card p').count(),0);
    assert.equal(await page.getByRole('link',{name:'Game 0 · Steam',exact:true}).count(),1);
    for(const width of [320,390,768,1280]){
      await page.setViewportSize({width,height:844});
      const result=await page.evaluate(()=>({overflow:document.documentElement.scrollWidth>innerWidth,aligned:[...document.querySelectorAll('.cover-card')].every(card=>{
        const cover=card.querySelector('.cover-frame').getBoundingClientRect(),row=card.querySelector('.cover-actions').getBoundingClientRect(),a=card.querySelector('.cover-progress-achievements').getBoundingClientRect(),t=card.querySelector('.cover-progress-trackers').getBoundingClientRect(),pin=card.querySelector('.pin-game'),p=pin.getBoundingClientRect(),style=getComputedStyle(pin);
        return row.top>cover.bottom&&Math.abs(a.left-cover.left)<1&&a.right<t.left&&t.right<=p.left&&Math.abs(p.right-cover.right)<1&&style.borderTopWidth==='0px'&&style.backgroundColor==='rgba(0, 0, 0, 0)';
      })}));assert.deepEqual(result,{overflow:false,aligned:true},'Viewport '+width+': '+await page.evaluate(()=>JSON.stringify(['.cover-frame','.cover-progress-achievements','.cover-progress-trackers','.pin-game'].map(selector=>{const el=document.querySelector(selector),r=el.getBoundingClientRect();return {selector,left:r.left,right:r.right,top:r.top,bottom:r.bottom,border:getComputedStyle(el).borderTopWidth,background:getComputedStyle(el).backgroundColor};}))));
    }
    if(process.env.PV_COVER_BADGE_SCREENSHOTS){
      fs.mkdirSync(process.env.PV_COVER_BADGE_SCREENSHOTS,{recursive:true});
      for(const width of [320,390,1280]){
        await page.setViewportSize({width,height:844});
        await page.locator('#games').screenshot({path:path.join(process.env.PV_COVER_BADGE_SCREENSHOTS,`cover-badges-${width}.png`)});
      }
    }
    await page.evaluate(()=>window.originalCard=document.querySelector('.cover-card'));
    await toggle.uncheck();assert.equal(await page.locator('.cover-actions').first().isVisible(),false);
    assert.equal(await page.evaluate(()=>originalCard===document.querySelector('.cover-card')),true);
    assert.equal(writes.length,0);
    await page.reload();await page.waitForFunction(()=>document.querySelectorAll('.cover-card').length===4);
    assert.equal(await toggle.isChecked(),false);assert.equal(await page.locator('.cover-actions').first().isVisible(),false);
    await page.evaluate(()=>navigation.go('#game/steam/0'));await page.evaluate(()=>navigation.go('#'));
    assert.equal(await toggle.isChecked(),false);assert.equal(await page.locator('.cover-actions').first().isVisible(),false);
    await toggle.check();assert.equal(await page.locator('.cover-actions').first().isVisible(),true);
    const star=page.locator('[data-game-card="g3"] .pin-game');await star.click();
    await page.waitForFunction(()=>document.querySelector('[data-game-card="g3"] .pin-game').getAttribute('aria-pressed')==='true'&&!document.querySelector('[data-game-card="g3"] .pin-game').disabled);
    assert.equal(await page.locator('.cover-card').first().getAttribute('data-game-card'),'g3');
    assert.equal(new URL(page.url()).hash,'');
    await star.click();await page.waitForFunction(()=>document.querySelector('[data-game-card="g3"] .pin-game').getAttribute('aria-pressed')==='false');
    assert.equal(writes.length,2);assert.deepEqual(layout.pins,[]);assert.deepEqual(errors,[]);
    const blocked=await browser.newPage({viewport:{width:390,height:844}});
    await blocked.addInitScript(()=>{Storage.prototype.getItem=Storage.prototype.setItem=()=>{throw new Error('Storage blocked');};});
    await blocked.goto(url);await blocked.waitForFunction(()=>document.querySelectorAll('.cover-card').length===4);
    const blockedToggle=blocked.getByRole('switch',{name:'Show cover controls'});assert.equal(await blockedToggle.isChecked(),true);
    await blockedToggle.uncheck();assert.equal(await blocked.locator('.cover-actions').first().isVisible(),false);
    await blocked.close();await page.close();
  }finally{await browser.close();await new Promise(resolve=>server.close(resolve));}
});
