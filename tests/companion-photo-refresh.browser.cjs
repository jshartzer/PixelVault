// Run with NODE_PATH pointing at the workspace's Playwright packages.
const {test}=require('node:test'),assert=require('node:assert/strict');
const fs=require('node:fs'),http=require('node:http'),path=require('node:path');
const {chromium}=require('playwright');
const web=path.resolve(__dirname,'../src/PixelVault.Companion/wwwroot');
test('open photo collections replace deleted tiles, retain filters/paging, and safely retry',async()=>{
  const shell=fs.readFileSync(path.join(web,'index.html'),'utf8').replace(/<script\b[^>]*>[\s\S]*?<\/script>/g,'');
  const server=http.createServer((req,res)=>{
    const url=new URL(req.url,'http://localhost');
    if(url.pathname==='/'){res.setHeader('Content-Type','text/html');res.end(shell);return;}
    const file=path.join(web,path.basename(url.pathname));
    if(fs.existsSync(file)){res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':'image/png');res.end(fs.readFileSync(file));return;}
    res.writeHead(404);res.end();
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  let browser;
  try{
    browser=await chromium.launch({headless:true,channel:'chrome'});
    const page=await browser.newPage({viewport:{width:390,height:844}});
    await page.goto(`http://127.0.0.1:${server.address().port}/`);
    await page.addScriptTag({path:path.join(web,'screenshots.js')});
    await page.evaluate(()=>{
      document.getElementById('workspace').hidden=false;document.getElementById('auth').hidden=true;
      window.rows=Array.from({length:130},(_,i)=>({id:String(i),console:'Steam',captureDay:i===0?'2026-10-28':'2026-10-27',group:i===0?'Deleted group':'Kept group',thumbnail:'v1.jpg'}));
      window.revision='a';window.calls=[];window.fail=false;
      window.photoUI=PixelVaultScreenshots.create({heading(){},getGames:()=>[{provider:'steam',gameId:'42',title:'Game'}],navigate(){},back(){},api:async endpoint=>{
        calls.push(endpoint);if(fail)throw new Error('Offline');
        const url=new URL(endpoint,'http://fixture'),offset=Number(url.searchParams.get('offset')||0),limit=Number(url.searchParams.get('limit')||60);
        if(url.searchParams.has('version')&&url.searchParams.get('version')!==revision)throw new Error('Changed');
        const photos=rows.slice(offset,offset+limit);
        return {version:revision,title:'Game',total:rows.length,eligible:rows.length,filteredTotal:rows.length,consoles:[{name:'Steam',count:rows.length}],nextOffset:offset+limit<rows.length?offset+limit:null,photos:url.pathname.endsWith('/timeline')?photos.map(photo=>({provider:'steam',gameId:'42',title:'Game',photo})):photos};
      }});
    });
    for(const route of [['screenshots','steam','42'],['timeline']]){
      await page.evaluate(async route=>{photoUI.clear();await photoUI.open(route);},route);
      await page.locator('[data-console="Steam"]').click();
      await page.waitForFunction(()=>document.querySelectorAll('.screenshot-tile').length>=60);
      await page.evaluate(()=>document.getElementById('screenshot-more').click());
      await page.waitForFunction(()=>document.querySelectorAll('.screenshot-tile').length>=120);
      await page.locator('#screenshot-grouping').click();
      const result=await page.evaluate(async()=>{
        rows=rows.filter(p=>p.id!=='0'&&p.id!=='61').map(p=>({...p,thumbnail:'v2.jpg'}));revision+='b';await photoUI.refresh();
        const ids=[...document.querySelectorAll('.screenshot-tile')].map(el=>el.dataset.photoId);
        const before=ids.join(',');fail=true;revision+='c';await photoUI.refresh();fail=false;
        return {imageSource:document.querySelector('[data-photo-id="2"] img').dataset.thumbSrc,ids,retained:before===[...document.querySelectorAll('.screenshot-tile')].map(el=>el.dataset.photoId).join(','),selected:document.querySelector('[data-console="Steam"]').getAttribute('aria-pressed'),days:[...document.querySelectorAll('.screenshot-day')].map(el=>el.dataset.day),calls};
      });
      assert.ok(result.imageSource.endsWith('?asset=v2.jpg'));
      assert.ok(!result.ids.includes('0')&&!result.ids.includes('61'));
      assert.equal(result.ids.length,120);assert.equal(result.retained,true);assert.equal(result.selected,'true');assert.deepEqual(result.days,['2026-10-27']);
      assert.ok(result.calls.some(url=>url.includes('limit=1&console=Steam')));
      assert.deepEqual(result.ids,Array.from({length:122},(_,i)=>String(i)).filter(id=>id!=='0'&&id!=='61'));
      await page.evaluate(async()=>{rows=rows.filter(p=>p.id!=='1');revision+='d';photoUI.reset();await photoUI.open(['screenshots','steam','42']);});
      assert.equal(await page.locator('[data-photo-id="1"]').count(),0);
      await page.evaluate(()=>{rows=Array.from({length:130},(_,i)=>({id:String(i),console:'Steam',captureDay:i===0?'2026-10-28':'2026-10-27',group:i===0?'Deleted group':'Kept group',thumbnail:'v1.jpg'}));revision+='e';});
    }
  }finally{await browser?.close();await new Promise(resolve=>server.close(resolve));}
});
