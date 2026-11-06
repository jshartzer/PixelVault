const {test}=require('node:test'),assert=require('node:assert/strict');
const {chromium}=require('playwright');
const fs=require('node:fs'),http=require('node:http'),path=require('node:path');
const web=path.resolve(__dirname,'../src/PixelVault.Companion/wwwroot');

test('Heritage frames preserve navigation, reserve foil for diamond, and respect motion/contrast settings',async()=>{
  const fixture=`<!doctype html><html><head><link rel="stylesheet" href="/profile.css"><link rel="stylesheet" href="/completion.css"><link rel="stylesheet" href="/fixture.css"><script src="/library.js" defer></script><script src="/fixture.js" defer></script></head><body><main></main></body></html>`;
  const script=`document.querySelector('main').innerHTML=['none','base','base-dlc','base-dlc-pv'].map(tier=>'<article class="completion-'+tier+'"><a href="#'+tier+'" aria-label="'+tier+'"><div class="completion-cover"><img src="/cover.svg" alt="Mountain cover">'+PixelVaultLibrary.completionDecoration(tier)+'</div></a><h2>'+tier+'</h2></article>').join('');`;
  const css='body{margin:32px;background:#151b20;color:#e1e8ef;font:14px Segoe UI}main{display:grid;grid-template-columns:repeat(auto-fit,minmax(145px,1fr));gap:24px;max-width:960px}article{min-width:0}a{display:block}img{display:block;width:100%;aspect-ratio:2/3;border-radius:12px}h2{font-size:14px;font-weight:500}';
  const cover='<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 220 330"><defs><linearGradient id="s" x2="0" y2="1"><stop stop-color="#31516a"/><stop offset="1" stop-color="#102b33"/></linearGradient></defs><path fill="url(#s)" d="M0 0H220V330H0Z"/><circle cx="105" cy="100" r="38" fill="#c3ceaf"/><path fill="#486879" d="M0 255L110 110L220 245V330H0Z"/><path fill="#183940" d="M0 200L75 267L150 230L220 270V330H0Z"/></svg>';
  const server=http.createServer((req,res)=>{
    const name=new URL(req.url,'http://localhost').pathname;
    res.setHeader('Content-Security-Policy',"default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; object-src 'none'");
    const fixtures={'/':['text/html',fixture],'/fixture.js':['text/javascript',script],'/fixture.css':['text/css',css],'/cover.svg':['image/svg+xml',cover]};
    if(fixtures[name]){res.setHeader('Content-Type',fixtures[name][0]);res.end(fixtures[name][1]);return;}
    const file=path.join(web,name);
    if(!file.startsWith(web+path.sep)||!fs.existsSync(file)){res.writeHead(404);res.end();return;}
    res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':'image/svg+xml');res.end(fs.readFileSync(file));
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const browser=await chromium.launch({headless:true,channel:'chrome'});
  try{
    const page=await browser.newPage({viewport:{width:1050,height:550}}),errors=[];
    page.on('pageerror',error=>errors.push(error.message));
    await page.goto(`http://127.0.0.1:${server.address().port}`);
    await page.waitForFunction(()=>document.querySelectorAll('article').length===4);
    assert.equal(await page.locator('.completion-frame').count(),3);
    assert.equal(await page.locator('.completion-foil').count(),1);
    const diamond=page.locator('.completion-base-dlc-pv .completion-cover');
    for(const tier of ['base','base-dlc']){
      const card=page.locator('.completion-'+tier+' .completion-cover');
      const before=await card.screenshot();await card.hover({position:{x:20,y:20}});
      assert.deepEqual(await card.screenshot(),before,'Ordinary metal has no animated or prismatic overlay');
      assert.equal(await card.locator('.completion-foil').count(),0);
    }
    await page.mouse.move(0,0);
    const flat=await diamond.screenshot();await diamond.hover({position:{x:20,y:20}});
    assert.notDeepEqual(await diamond.screenshot(),flat,'Diamond foil must respond to light');
    await page.mouse.move(0,0);assert.deepEqual(await diamond.screenshot(),flat,'Leaving restores the static finish');
    const evidence=path.resolve(__dirname,'../artifacts/heritage-completion');fs.mkdirSync(evidence,{recursive:true});
    await page.screenshot({path:path.join(evidence,'companion-frames.png'),fullPage:true});
    await page.emulateMedia({reducedMotion:'reduce'});await diamond.hover({position:{x:20,y:20}});
    assert.equal(await diamond.evaluate(e=>e.style.getPropertyValue('--completion-x')),'');
    assert.deepEqual(await diamond.screenshot(),flat,'Reduced motion keeps the same static foil');
    await page.emulateMedia({forcedColors:'active'});
    assert.equal(await diamond.locator('.completion-foil').evaluate(e=>getComputedStyle(e).display),'none');
    assert.equal(await diamond.locator('.completion-clasp').evaluate(e=>getComputedStyle(e).backgroundImage),'none');
    await page.emulateMedia({forcedColors:'none',reducedMotion:'no-preference'});
    for(const width of [320,390,768,1050]){
      await page.setViewportSize({width,height:850});
      assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false);
      assert.equal(await page.locator('.completion-clasp').evaluateAll(els=>els.every(el=>{
        const badge=el.getBoundingClientRect(),card=el.parentElement.getBoundingClientRect();
        return Math.abs(badge.right-card.right)<=2.1 && Math.abs(badge.bottom-card.bottom)<=2.1 && badge.width<card.width*.28;
      })),true,'Emblems must stay tucked into the corner at '+width);
      if(width===390)await page.screenshot({path:path.join(evidence,'companion-phone.png'),fullPage:true});
    }
    await page.getByRole('link',{name:'base',exact:true}).click();assert.equal(new URL(page.url()).hash,'#base');
    await page.evaluate(()=>{const host=document.querySelector('.completion-base-dlc-pv');host.className='completion-base';host.querySelector('.completion-cover').innerHTML='<img src="/cover.svg" alt="Cover">'+PixelVaultLibrary.completionDecoration('base');});
    assert.equal(await page.locator('.completion-foil').count(),0);
    assert.deepEqual(errors,[]);
  }finally{await browser.close();await new Promise(resolve=>server.close(resolve));}
});
