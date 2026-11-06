// Run with NODE_PATH pointing at the workspace Playwright package.
const {test}=require('node:test'),assert=require('node:assert/strict');
const {chromium}=require('playwright'),http=require('node:http'),fs=require('node:fs'),path=require('node:path');
const web=path.resolve(__dirname,'../src/PixelVault.Companion/wwwroot');
test('Home Screen back gestures match the button across reloads, guides, photos and slow requests',async()=>{
  const game={provider:'steam',gameId:'42',groupId:'game-42',title:'Test game',platform:'Steam',trackerCount:1,completed:0,total:1,stats:{achievementUnlocked:0,achievementTotal:1},screenshots:{provider:'steam',gameId:'42',count:2}};
  const definition={id:'test',provider:'steam',gameId:'42',title:'Test tracker',gameTitle:'Test game',categoryTargets:{},milestones:[],items:[]};
  const tracker={...definition,totals:{completed:0,total:1,unknown:0},updateVersion:'1',updateProgress:[]};
  const achievement={id:'one',title:'One achievement',description:'Saved description',guide:'A saved guide.',groups:{category:'Story',dlc:''},tags:'',progressKnown:true,unlocked:false,rarity:25};
  const photos=['p1','p2'].map(id=>({id,console:'Steam',captureDay:'2026-10-29',width:960,height:540}));
  let holdAchievements=false,holdLibrary=false,pending=[];
  const release=()=>{holdAchievements=holdLibrary=false;for(const done of pending)done();pending=[];};
  const server=http.createServer(async(req,res)=>{
    const url=new URL(req.url,'http://localhost');
    if(url.pathname.startsWith('/api/v1')){
      const endpoint=url.pathname.slice(7);
      if(endpoint.endsWith('/thumbnail')||endpoint.endsWith('/preview')){res.setHeader('Content-Type','image/svg+xml');res.end('<svg xmlns="http://www.w3.org/2000/svg" width="960" height="540"><rect width="960" height="540" fill="#789"/></svg>');return;}
      if(holdAchievements&&endpoint==='/achievements/steam/42'||holdLibrary&&endpoint==='/games')await new Promise(resolve=>pending.push(resolve));
      const responses={'/session':{authenticated:true,demoEnabled:false},'/antiforgery':{token:'test'},'/workspace':{id:'navigation-test'},'/games':[game],'/games/steam/42':game,'/trackers':[tracker],
        '/preferences/game-sections':{sections:['trackers','achievements','screenshots']},'/preferences/library':{sort:'title-az',pins:[]},'/refresh/connection':{paired:false,online:false,supported:[]},
        '/screenshots/recent':{photos:[]},'/screenshots/steam/42':{version:'1',title:'Test game',total:2,filteredTotal:2,eligible:2,consoles:[{name:'Steam',count:2}],nextOffset:null,photos},
        '/screenshots/timeline':{version:'1',total:2,filteredTotal:2,consoles:[{name:'Steam',count:2}],nextOffset:null,photos:photos.map(photo=>({provider:'steam',gameId:'42',title:'Test game',photo}))},
        '/trackers/test':{definition,states:[],totals:tracker.totals,versions:{},protectedItems:[],readOnly:true},
        '/achievements/steam/42':{rows:[{achievement}],categoryOrder:['Story'],progressUpdatedUtc:0}};
      res.setHeader('Content-Type','application/json');res.end(JSON.stringify(responses[endpoint]??{}));return;
    }
    let file=path.join(web,url.pathname==='/'?'index.html':url.pathname);
    if(process.env.PV_NAVIGATION_MODULE&&url.pathname==='/navigation.js')file=process.env.PV_NAVIGATION_MODULE;
    if(!fs.existsSync(file)){res.writeHead(404);res.end();return;}
    res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':file.endsWith('.svg')?'image/svg+xml':file.endsWith('.png')?'image/png':'text/html');res.end(fs.readFileSync(file));
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));let browser;
  try{
    browser=await chromium.launch({headless:true,channel:'chrome'});
    const page=await browser.newPage({viewport:{width:390,height:844},hasTouch:true,isMobile:true}),errors=[];
    page.setDefaultTimeout(6000);
    page.on('pageerror',error=>errors.push(error.message));
    await page.addInitScript(()=>{Object.defineProperty(navigator,'standalone',{get:()=>true});const interval=setInterval;window.setInterval=(f,delay,...rest)=>delay===15000?1:interval(f,delay,...rest);});
    await page.goto(`http://127.0.0.1:${server.address().port}/`);await page.waitForSelector('.game-card');
    const go=async hash=>{await page.evaluate(hash=>navigation.go(hash),hash);};
    const swipe=async(selector,down=false)=>{
      const target=page.locator(selector).first();await target.scrollIntoViewIfNeeded();
      await target.evaluate((target,down)=>{
        window.gestureTarget=target;window.gestureStart={x:40,y:200,down};
        const point=new Touch({identifier:1,target,clientX:40,clientY:200});target.dispatchEvent(new TouchEvent('touchstart',{bubbles:true,cancelable:true,touches:[point],changedTouches:[point]}));
      },down);
      await page.waitForTimeout(70);
      return page.evaluate(()=>{
        const target=gestureTarget,point=new Touch({identifier:1,target,clientX:gestureStart.down?45:200,clientY:gestureStart.down?340:205});
        target.dispatchEvent(new TouchEvent('touchmove',{bubbles:true,cancelable:true,touches:[point],changedTouches:[point]}));
        const end=new TouchEvent('touchend',{bubbles:true,cancelable:true,touches:[],changedTouches:[point]});target.dispatchEvent(end);return end.defaultPrevented;
      });
    };
    await go('#game/steam/42');await go('#tracker/test');await page.reload();await page.waitForSelector('#detail:not([hidden])');
    assert.equal(await page.locator('#tracker-maps').getAttribute('href'),'#maps/steam/42/tracker/test');
    assert.equal(await swipe('#summary'),true);await page.waitForURL('**/#game/steam/42');await page.waitForSelector('#game-profile:not([hidden])');
    assert.equal(await page.locator('#games').isVisible(),false);
    await go('#achievements/steam/42');await go('#achievement/steam/42/one');
    assert.equal(await page.locator('.achievement-map-link a').getAttribute('href'),'#maps/steam/42/achievement/one');
    holdAchievements=true;assert.equal(await swipe('#page-title'),true);await page.waitForURL('**/#achievements/steam/42');
    assert.equal(await page.locator('.achievement-overview').isVisible(),true,'Returning to a guide list went blank while refreshing');
    await swipe('.achievement-row');assert.equal(new URL(page.url()).hash,'#achievements/steam/42','A second gesture skipped the loading menu');
    release();await page.waitForFunction(()=>navigation.canBack());
    await page.locator('#navigate-back').click();await page.waitForURL('**/#game/steam/42');
    await go('#screenshots/steam/42');await page.waitForSelector('.screenshot-tile');await go('#photo/steam/42/p1');await page.waitForSelector('#photo-viewer[open]');
    assert.equal(await swipe('#photo-stage',true),true);await page.waitForURL('**/#screenshots/steam/42');await page.waitForFunction(()=>navigation.canBack());
    assert.equal(await page.locator('html').evaluate(el=>el.classList.contains('photo-open')),false);assert.equal(await page.locator('dialog[open]').count(),0);
    assert.equal(await swipe('.screenshot-tile'),true);await page.waitForURL('**/#game/steam/42');await page.waitForFunction(()=>navigation.canBack());
    holdLibrary=true;await swipe('#game-stats');await page.waitForURL(/\/$|\/#$/);assert.equal(await page.locator('.game-card').isVisible(),true,'Returning home hid the library until a network response');release();
    await page.waitForFunction(()=>!document.getElementById('games').hidden);await go('#timeline');await go('#timeline-photo/steam/42/p1');
    await page.reload();await page.waitForSelector('#photo-viewer[open]');await swipe('#photo-stage',true);await page.waitForURL('**/#timeline');
    assert.equal(await page.locator('#screenshots-page').isVisible(),true);assert.equal(await page.locator('dialog[open]').count(),0);
    await page.waitForFunction(()=>navigation.canBack());await page.locator('#navigate-back').click();await page.waitForURL(/\/$|\/#$/);
    assert.deepEqual(errors,[]);
  }finally{release();await browser?.close();await new Promise(resolve=>server.close(resolve));}
});
