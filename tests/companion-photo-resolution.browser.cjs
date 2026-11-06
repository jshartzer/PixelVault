const {test}=require('node:test'),assert=require('node:assert/strict');
const fs=require('node:fs'),http=require('node:http'),path=require('node:path');
const {chromium}=require('playwright');
const web=path.resolve(__dirname,'../src/PixelVault.Companion/wwwroot');

test('timeline selects sharp images, unloads offscreen tiles, refreshes and falls back safely',async()=>{
  const shell=fs.readFileSync(path.join(web,'index.html'),'utf8').replace(/<script\b[^>]*>[\s\S]*?<\/script>/g,'');
  let failPreview=false;
  const server=http.createServer((req,res)=>{
    const url=new URL(req.url,'http://localhost');
    if(url.pathname==='/'){res.setHeader('Content-Type','text/html');res.end(shell);return;}
    if(url.pathname.startsWith('/api/v1/screenshots/')){
      if(failPreview&&url.pathname.endsWith('/preview')){res.writeHead(404);res.end();return;}
      res.setHeader('Content-Type','image/svg+xml');res.end('<svg xmlns="http://www.w3.org/2000/svg" width="1920" height="1080"><rect width="1920" height="1080" fill="teal"/></svg>');return;
    }
    const file=path.join(web,path.basename(url.pathname));
    if(fs.existsSync(file)){res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':'image/png');res.end(fs.readFileSync(file));return;}
    res.writeHead(404);res.end();
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));let browser;
  try{
    browser=await chromium.launch({headless:true,channel:'chrome'});
    for(const dpr of [1,3]){
      const page=await browser.newPage({viewport:{width:390,height:844},deviceScaleFactor:dpr});
      await page.goto(`http://127.0.0.1:${server.address().port}/`);
      await page.addScriptTag({path:path.join(web,'screenshots.js')});
      await page.evaluate(async()=>{
        document.getElementById('workspace').hidden=false;document.getElementById('auth').hidden=true;
        window.revision='v1';window.rows=Array.from({length:60},(_,i)=>({id:String(i),console:'Steam',captureDay:'2026-11-03',group:'Photos',width:1920,height:1080,thumbnail:'thumb-v1',preview:'preview-v1'}));
        window.photoUI=PixelVaultScreenshots.create({heading(){},getGames:()=>[],navigate(){},back(){},api:async()=>({version:revision,total:rows.length,filteredTotal:rows.length,consoles:[{name:'Steam',count:rows.length}],nextOffset:null,photos:rows.map(photo=>({provider:'steam',gameId:'42',title:'Game',photo}))})});
        await photoUI.open(['timeline']);
      });
      const first=page.locator('[data-photo-id="0"] img');
      await page.waitForFunction(()=>document.querySelector('[data-photo-id="0"] img')?.naturalWidth>0);
      assert.match(await first.getAttribute('src'),dpr===1?/\/thumbnail\?/:/\/preview\?/);
      assert.equal(await page.locator('[data-photo-id="59"] img').getAttribute('src'),null);
      await page.setViewportSize({width:1280,height:844});
      await page.waitForFunction(()=>document.querySelector('[data-photo-id="0"] img').getAttribute('src')?.includes('/preview?'));
      await page.evaluate(()=>{window.savedTile=document.querySelector('[data-photo-id="0"]');});
      await page.evaluate(async()=>{rows=rows.map(p=>({...p,preview:'preview-v2',thumbnail:'thumb-v2'}));revision='v2';await photoUI.refresh();});
      await page.waitForFunction(()=>document.querySelector('[data-photo-id="0"] img').getAttribute('src')?.endsWith('asset=preview-v2'));
      assert.equal(await page.evaluate(()=>savedTile===document.querySelector('[data-photo-id="0"]')),true);
      await page.locator('[data-photo-id="59"]').scrollIntoViewIfNeeded();
      await page.waitForFunction(()=>!document.querySelector('[data-photo-id="0"] img').hasAttribute('src'));
      await page.evaluate(()=>scrollTo(0,0));
      await page.waitForFunction(()=>document.querySelector('[data-photo-id="0"] img').getAttribute('src')?.endsWith('asset=preview-v2'));
      failPreview=true;
      await page.evaluate(async()=>{rows=rows.map(p=>({...p,preview:'preview-failed',thumbnail:'thumb-v3'}));revision='v3';await photoUI.refresh();});
      await page.waitForFunction(()=>{const img=document.querySelector('[data-photo-id="0"] img');return img.getAttribute('src')?.endsWith('asset=thumb-v3')&&img.naturalWidth>0;});
      assert.equal(await first.isVisible(),true);
      failPreview=false;
      await page.evaluate(async()=>{rows=rows.map(p=>({...p,preview:'preview-v4'}));revision='v4';await photoUI.refresh();});
      await page.waitForFunction(()=>document.querySelector('[data-photo-id="0"] img').getAttribute('src')?.endsWith('asset=preview-v4'));
      await page.close();
    }
  }finally{await browser?.close();await new Promise(resolve=>server.close(resolve));}
});
