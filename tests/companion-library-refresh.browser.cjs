// Run with NODE_PATH pointing at the workspace Playwright package.
const {test}=require('node:test'),assert=require('node:assert/strict');
const {chromium}=require('playwright');
const fs=require('node:fs'),http=require('node:http'),path=require('node:path');
const web=path.resolve(__dirname,'../src/PixelVault.Companion/wwwroot');

test('library refreshes on entry and resume, while connection polling leaves browsing untouched',async()=>{
  let rows=[],offline=false,hold=false,pending,gameReads=0;
  const server=http.createServer(async(req,res)=>{
    const url=new URL(req.url,'http://localhost');
    if(url.pathname.startsWith('/api/v1')){
      res.setHeader('Content-Type','application/json');
      const endpoint=url.pathname.slice(7);
      if(endpoint==='/games'){
        gameReads++;if(hold)await new Promise(resolve=>pending=resolve);
        if(offline){res.statusCode=503;res.end('{}');return;}
      }
      const responses={'/session':{authenticated:true,demoEnabled:false},'/antiforgery':{token:'test'},'/games':rows,'/trackers':[],
        '/preferences/game-sections':{sections:['trackers','achievements','screenshots']},'/preferences/library':{sort:'title-az',pins:[]},
        '/refresh/connection':{paired:false,online:false,supported:[]},'/workspace':{id:'scroll-regression'},'/screenshots/recent':{photos:[]}};
      res.end(JSON.stringify(responses[endpoint]??(endpoint.startsWith('/games/')?rows.find(row=>endpoint===`/games/${row.provider}/${row.gameId}`):{})));return;
    }
    const file=path.join(web,url.pathname==='/'?'index.html':url.pathname);
    if(!fs.existsSync(file)){res.writeHead(404);res.end();return;}
    res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':file.endsWith('.svg')?'image/svg+xml':file.endsWith('.png')?'image/png':'text/html');
    res.end(fs.readFileSync(file));
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const browser=await chromium.launch({headless:true,channel:'chrome'});
  try{
    for(const width of [390,1280]){
      rows=Array.from({length:160},(_,index)=>({provider:'steam',gameId:String(index),groupId:'game-'+index,title:'Game '+String(index).padStart(3,'0'),platform:'Steam',trackerCount:0,stats:{achievementUnlocked:1,achievementTotal:10}}));
      rows[0].coverUrl='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a6ioAAAAASUVORK5CYII=';
      const page=await browser.newPage({viewport:{width,height:844}}),errors=[];
      page.on('pageerror',e=>errors.push(e.message));
      await page.addInitScript(()=>{
        const interval=window.setInterval;window.setInterval=(callback,delay,...args)=>delay===15000?(window.libraryPoll=callback,1):interval(callback,delay,...args);
      });
      await page.goto(`http://127.0.0.1:${server.address().port}/`);
      await page.waitForFunction(()=>document.querySelectorAll('.game-card').length===160);
      await page.waitForFunction(()=>document.getElementById('recent-photos').hidden);
      await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
      await page.evaluate(()=>{
        const card=document.querySelector('[data-game-card="game-60"]');
        window.scrollTo(0,card.getBoundingClientRect().top+scrollY-35);
        window.gridMutations=0;new MutationObserver(changes=>gridMutations+=changes.length).observe(document.getElementById('games'),{subtree:true,childList:true,attributes:true});
        window.originalCover=card;window.originalTop=card.getBoundingClientRect().top;
      });
      await page.waitForTimeout(550);
      await page.evaluate(()=>window.originalImage=document.querySelector('[data-game-card="game-0"] .cover-frame img'));
      const initialReads=gameReads;
      for(let poll=0;poll<3;poll++)await page.evaluate(()=>libraryPoll());
      let state=await page.evaluate(()=>({connected:originalCover.isConnected,top:originalCover.getBoundingClientRect().top,expected:originalTop,mutations:gridMutations}));
      assert.equal(state.connected,true,'Unchanged polling replaced the cover DOM');assert.equal(state.mutations,0,'Unchanged polling mutated the grid');assert.ok(Math.abs(state.top-state.expected)<1);
      assert.equal(gameReads,initialReads,'Connection polling fetched the library');

      rows[0]={...rows[0],title:'ZZZ moved game',stats:{achievementUnlocked:4,achievementTotal:10}};
      rows.unshift({provider:'steam',gameId:'new',groupId:'new',title:'A newly added game',platform:'Steam',trackerCount:0,stats:{}});
      await page.evaluate(()=>libraryPoll());
      assert.equal(gameReads,initialReads);assert.equal(await page.locator('[data-game-card="new"]').count(),0);
      await page.evaluate(()=>pollConnection({refreshLibrary:true}));
      state=await page.evaluate(()=>({connected:originalCover.isConnected,delta:originalCover.getBoundingClientRect().top-originalTop}));
      assert.equal(state.connected,true);assert.ok(Math.abs(state.delta)<1,'Adding/reordering games moved the visible cover');
      assert.equal(await page.locator('[data-game-card="game-0"] .cover-progress-achievements strong').textContent(),'40%');
      assert.equal(await page.evaluate(()=>originalImage.isConnected),true,'Progress refresh replaced a loaded cover image');

      await page.waitForTimeout(550);
      await page.evaluate(()=>{
        const visible=[...document.querySelectorAll('.game-card')].filter(c=>{const r=c.getBoundingClientRect();return r.bottom>0&&r.top<innerHeight});
        window.removedId=visible[0].dataset.gameCard;window.fallbackCover=visible[1];
        window.fallbackTop=fallbackCover.getBoundingClientRect().top;
      });
      const removedId=await page.evaluate(()=>removedId);rows=rows.filter(r=>r.groupId!==removedId);
      await page.evaluate(()=>pollConnection({refreshLibrary:true}));
      assert.ok(Math.abs(await page.evaluate(()=>fallbackCover.getBoundingClientRect().top-fallbackTop))<1,'Removing a visible cover lost the next anchor');

      await page.waitForTimeout(550);
      hold=true;await page.evaluate(()=>{void(window.pendingLibraryPoll=pollConnection({refreshLibrary:true}));});
      while(!pending)await new Promise(resolve=>setTimeout(resolve,5));
      const beforeScroll=await page.evaluate(()=>{
        window.scrollBy(0,97);window.dispatchEvent(new Event('scroll'));
        const anchor=[...document.querySelectorAll('.game-card')].find(c=>{const r=c.getBoundingClientRect();return r.bottom>0&&r.top<innerHeight});
        return {id:anchor.dataset.gameCard,top:anchor.getBoundingClientRect().top};
      });
      rows.unshift({provider:'steam',gameId:'deferred',groupId:'deferred',title:'A deferred game',platform:'Steam',trackerCount:0,stats:{}});
      hold=false;pending();pending=undefined;await page.evaluate(()=>pendingLibraryPoll);
      assert.equal(await page.locator('[data-game-card="deferred"]').count(),1);
      const afterScroll=await page.evaluate(id=>[...document.querySelectorAll('.game-card')].find(c=>c.dataset.gameCard===id).getBoundingClientRect().top,beforeScroll.id);
      assert.ok(Math.abs(afterScroll-beforeScroll.top)<1,'App resume lost the current cover after in-flight scrolling');

      await page.waitForTimeout(550);
      const focusedId=await page.evaluate(()=>{
        const cover=[...document.querySelectorAll('.game-card')].find(c=>{const r=c.getBoundingClientRect();return r.top>=0&&r.bottom<innerHeight});
        cover.querySelector('.pin-game').focus({preventScroll:true});return cover.dataset.gameCard;
      });
      const index=rows.findIndex(r=>r.groupId===focusedId);rows[index]={...rows[index],stats:{achievementUnlocked:8,achievementTotal:10}};
      await page.evaluate(()=>pollConnection({refreshLibrary:true}));
      assert.equal(await page.evaluate(()=>document.activeElement.closest('[data-game-card]')?.dataset.gameCard),focusedId);
      offline=true;await page.waitForTimeout(550);const savedY=await page.evaluate(()=>scrollY);await page.evaluate(()=>pollConnection({refreshLibrary:true}));offline=false;
      assert.equal(await page.evaluate(()=>scrollY),savedY);assert.deepEqual(errors,[]);
      await page.evaluate(()=>navigation.go('#game/steam/60'));
      rows.unshift({provider:'steam',gameId:'on-entry',groupId:'on-entry',title:'A game added while away',platform:'Steam',trackerCount:0,stats:{}});
      const beforeEntry=gameReads;
      await page.evaluate(()=>navigation.go('#'));
      assert.equal(gameReads,beforeEntry+1,'Returning to the library did not fetch fresh data');
      assert.equal(await page.locator('[data-game-card="on-entry"]').count(),1);
      await page.close();
    }
    assert.ok(gameReads>10);
  }finally{pending?.();await browser.close();await new Promise(resolve=>server.close(resolve));}
});
