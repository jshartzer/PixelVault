const {test}=require('node:test'),assert=require('node:assert/strict'),{chromium}=require('playwright');
const fs=require('node:fs'),http=require('node:http'),path=require('node:path');
const web=path.resolve(__dirname,'../src/PixelVault.Companion/wwwroot');
const policy=fs.readFileSync(path.join(web,'../Program.cs'),'utf8').match(/ContentSecurityPolicy = "([^"]+)"/)[1];
const century='century.death-stranding-dc.';
const art={'all-roads':'all-roads','all-interviews':'all-interviews','all-mails':'all-mails','all-lll-orders':'all-lll-orders','cargo-60000kg':'cargo-60000kg'};
const tracker=(id,title,achievementId,completed,total)=>({id,title,achievementId,provider:'steam',gameId:'1850570',gameTitle:"Death Stranding Director's Cut",totals:{completed,total},updateVersion:'v1',updateProgress:[]});
function fixtures(){return [tracker('official-orders','Growth of a Legend','019',50,80),tracker('official-interviews','Read 100 interviews','039',100,100),
  tracker('notes','Century notes',null,0,0),tracker('roads','All Roads Lead Home',century+'all-roads',51,51),
  tracker('interviews','The Whole Story',century+'all-interviews',69,149),tracker('mail','No Mail Left Unread',century+'all-mails',154,240),
  tracker('legends','Nothing Left to Deliver',century+'all-lll-orders',138,540),tracker('weight','The Weight of a Nation',century+'cargo-60000kg',16503,60000)];}

test('Century trackers form their own section with independent completed groups and stable polls',async()=>{
  let order=['achievement','manual','century'];let trackers=fixtures();const game={provider:'steam',gameId:'1850570',groupId:'ds',title:"Death Stranding Director's Cut",platform:'Steam',trackerCount:8,stats:{}};
  const server=http.createServer(async(req,res)=>{
    const url=new URL(req.url,'http://localhost');res.setHeader('Content-Security-Policy',policy);
    if(url.pathname.includes('/unlocked-icon')){const id=decodeURIComponent(url.pathname.split('/').at(-2)),name=art[id.replace(century,'')]||'all-roads';res.setHeader('Content-Type','image/png');res.end(fs.readFileSync(path.resolve(web,'../../PixelVault.Tracking/CenturyArtwork/'+name+'-unlocked.png')));return;}
    if(url.pathname==='/api/v1/preferences/tracker-groups'){
      if(req.method==='POST'){let body='';for await(const chunk of req)body+=chunk;const move=JSON.parse(body),i=order.indexOf(move.group),j=i+move.direction;[order[i],order[j]]=[order[j],order[i]];}
      res.setHeader('Content-Type','application/json');res.end(JSON.stringify({groups:order}));return;
    }
    if(url.pathname.startsWith('/api/v1')){
      const responses={'/session':{authenticated:true,demoEnabled:false},'/antiforgery':{token:'test'},'/games':[game],'/trackers':trackers,
        '/preferences/game-sections':{sections:['trackers','achievements','screenshots']},'/preferences/library':{sort:'title-az',pins:[]},
        '/refresh/connection':{workspaceId:'century-trackers',online:true,supported:[],automaticEnabled:true},'/workspace':{id:'century-trackers',mode:'direct-local'},'/screenshots/recent':{photos:[]}};
      res.setHeader('Content-Type','application/json');res.end(JSON.stringify(responses[url.pathname.slice(7)]??{}));return;
    }
    const file=path.join(web,url.pathname==='/'?'index.html':url.pathname);if(!fs.existsSync(file)){res.writeHead(404);res.end();return;}
    res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':file.endsWith('.svg')?'image/svg+xml':file.endsWith('.png')?'image/png':'text/html');res.end(fs.readFileSync(file));
  });await new Promise(r=>server.listen(0,'127.0.0.1',r));const browser=await chromium.launch({channel:'chrome',headless:true});
  try{for(const width of [390,1280]){
    order=['achievement','manual','century'];trackers=fixtures();const page=await browser.newPage({viewport:{width,height:900}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
    await page.addInitScript(()=>{const original=setInterval;window.setInterval=(callback,delay,...args)=>[2000,15000].includes(delay)?1:original(callback,delay,...args);});
    await page.goto(`http://127.0.0.1:${server.address().port}/#game/steam/1850570`);await page.locator('.tracker-section summary').click();
    const standard=page.locator('[data-tracker-group="achievement"]'),centuryGroup=page.locator('[data-tracker-group="century"]');
    assert.match(await standard.locator('.tracker-group-heading').innerText(),/Achievement-linked trackers\s*2/);
    assert.match(await centuryGroup.locator('.tracker-group-heading').innerText(),/Centurian Achievements\s*5/);
    assert.deepEqual(await standard.locator('.tracker-card-shell').evaluateAll(nodes=>nodes.map(n=>n.dataset.trackerIconOwner)),['official-orders','official-interviews']);
    assert.equal(await centuryGroup.locator('.tracker-card-shell').count(),5);
    assert.deepEqual(await page.locator('[data-tracker-group]').evaluateAll(nodes=>nodes.map(n=>n.dataset.trackerGroup)),['achievement','manual','century']);
    assert.equal(await page.getByRole('button',{name:'Order groups…'}).count(),0);
    order=['manual','achievement','century'];await page.evaluate(()=>pollConnection({refreshLibrary:true}));
    assert.deepEqual(await page.locator('[data-tracker-group]').evaluateAll(nodes=>nodes.map(n=>n.dataset.trackerGroup)),order);

    assert.equal(await centuryGroup.locator('.tracker-card-shell').last().getAttribute('data-tracker-icon-owner'),'roads');
    assert.match(await standard.locator('.completed-heading').innerText(),/Completed\s*1/);assert.match(await centuryGroup.locator('.completed-heading').innerText(),/Completed\s*1/);
    assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false);
    const image=centuryGroup.locator('[data-tracker-icon-owner="weight"] img');await image.scrollIntoViewIfNeeded();await image.evaluate(img=>img.decode());
    await image.evaluate(img=>window.savedCenturyImage=img);await page.evaluate(()=>pollConnection({refreshLibrary:true}));
    assert.equal(await page.evaluate(()=>savedCenturyImage.isConnected),true);assert.equal(await page.locator('.tracker-section').getAttribute('open'),'');
    trackers=trackers.map(t=>t.id==='weight'?{...t,totals:{completed:16504,total:60000}}:t);await page.evaluate(()=>pollConnection({refreshLibrary:true}));
    assert.equal(await image.evaluate(img=>img===window.savedCenturyImage),true);
    if(process.env.PV_CENTURY_TRACKER_SCREENSHOTS){fs.mkdirSync(process.env.PV_CENTURY_TRACKER_SCREENSHOTS,{recursive:true});await page.locator('.tracker-section').screenshot({path:path.join(process.env.PV_CENTURY_TRACKER_SCREENSHOTS,`companion-tracker-sections-${width}.png`)});}
    trackers=trackers.map(t=>t.id==='roads'?{...t,achievementId:'039'}:t);await page.evaluate(()=>pollConnection({refreshLibrary:true}));
    assert.equal(await standard.locator('[data-tracker-icon-owner="roads"]').count(),1);assert.equal(await centuryGroup.locator('.tracker-card-shell').count(),4);assert.equal(await centuryGroup.locator('.completed-heading').count(),0);
    assert.match(await standard.locator('.completed-heading').innerText(),/Completed\s*2/);
    assert.match(await standard.locator('[data-tracker-icon-owner="roads"] .tracker-progress').innerText(),/51\s*\/\s*51/);
    trackers=trackers.map(t=>({...t,achievementId:null}));await page.evaluate(()=>pollConnection({refreshLibrary:true}));
    assert.equal(await page.locator('[data-tracker-group="century"]').count(),0);assert.equal(await page.locator('.tracker-group-heading').count(),1);assert.equal(await page.locator('.tracker-card-shell').count(),8);
    assert.deepEqual(errors,[]);await page.close();
  }}finally{await browser.close();await new Promise(r=>server.close(r));}
});
