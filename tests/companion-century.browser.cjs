const {test}=require('node:test'),assert=require('node:assert/strict');
const {chromium}=require('playwright'),http=require('node:http'),fs=require('node:fs'),path=require('node:path');
const web=path.resolve(__dirname,'../src/PixelVault.Companion/wwwroot');
test('Century saved road progress renders in cards, filters and details at phone and desktop widths',async()=>{
  const exported=process.env.PV_CENTURY_SAVED_VIEW;
  const road=exported?JSON.parse(fs.readFileSync(exported,'utf8')):{id:'century.death-stranding-dc.all-roads',provider:'steam',gameId:'1850570',title:'All Roads Lead Home',description:'Road network',category:'Infrastructure',requirement:'All 51 road sections',points:100,progressKnown:true,unlocked:false,counterCurrent:36,counterTarget:51,connectionStatus:'36 / 51 roads · verified from save'};
  const game={provider:'steam',gameId:'1850570',groupId:'ds',title:"Death Stranding Director's Cut",platform:'Steam',trackerCount:0,stats:{},screenshots:{count:0}};
  const century={planned:5,status:'Awaiting save connection',definitions:[road,{...road,id:'century.death-stranding-dc.all-interviews',title:'The Whole Story',description:'Read interviews',progressKnown:false,unlocked:false,counterCurrent:null,counterTarget:null,connectionStatus:'Awaiting save connection'}]};
  const server=http.createServer((req,res)=>{
    const url=new URL(req.url,'http://localhost');
    if(url.pathname.includes('/icon')){res.setHeader('Content-Type','image/png');res.end(fs.readFileSync(path.resolve(web,'../../PixelVault.Tracking/CenturyArtwork/all-roads-'+(url.searchParams.get('state')==='unlocked'?'unlocked':'locked')+'.png')));return;}
    if(url.pathname.startsWith('/api/v1')){
      const endpoint=url.pathname.slice(7),responses={'/session':{authenticated:true,demoEnabled:false},'/workspace':{id:'century-browser'},'/antiforgery':{token:'test'},'/games':[game],'/games/steam/1850570':game,'/trackers':[],
      '/preferences/game-sections':{sections:['trackers','achievements','screenshots']},'/preferences/library':{sort:'title-az',pins:[]},'/refresh/connection':{paired:false,online:false,supported:[]},'/screenshots/recent':{photos:[]},
      '/achievements/steam/1850570':{rows:[],categories:[],categoryOrder:[],dlcs:[],progressUpdatedUtc:0,century}};
      res.setHeader('Content-Type','application/json');res.end(JSON.stringify(responses[endpoint]??{}));return;
    }
    const file=path.join(web,url.pathname==='/'?'index.html':url.pathname);if(!fs.existsSync(file)){res.writeHead(404);res.end();return;}
    res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':file.endsWith('.svg')?'image/svg+xml':'text/html');res.end(fs.readFileSync(file));
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));const browser=await chromium.launch({headless:true,channel:'chrome'});
  try{
    for(const width of [390,1280]){
      const page=await browser.newPage({viewport:{width,height:900}}),errors=[];page.on('pageerror',e=>errors.push(e.message));page.setDefaultTimeout(6000);
      await page.goto(`http://127.0.0.1:${server.address().port}/#achievements/steam/1850570`);
      const card=page.locator('.century-achievement').filter({hasText:road.title});await card.waitFor();
      assert.match(await card.innerText(),new RegExp(`${road.counterCurrent} / ${road.counterTarget}`));
      await page.locator('#achievement-filter').selectOption('remaining');assert.equal(await page.locator('.century-achievement').count(),1);
      await card.click();await page.locator('.achievement-counter').waitFor();assert.match(await page.locator('.achievement-counter').innerText(),new RegExp(`${road.counterCurrent} / ${road.counterTarget}`));
      assert.match(await page.locator('.achievement-assignment').innerText(),/100 PV pts/);
      assert.doesNotMatch(await page.locator('#achievements-page').innerText(),/verified|save data|connection|Progress checked|Saved guide/i);
      assert.equal(await page.locator('#achievement-guide-title').innerText(),'Requirements');
      assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false);
      if(process.env.PV_CENTURY_SCREENSHOTS){fs.mkdirSync(process.env.PV_CENTURY_SCREENSHOTS,{recursive:true});await page.screenshot({path:path.join(process.env.PV_CENTURY_SCREENSHOTS,`companion-road-${width}.png`),fullPage:true});}
      assert.deepEqual(errors,[]);await page.close();
    }
  }finally{await browser.close();await new Promise(resolve=>server.close(resolve));}
});
