const {test}=require('node:test'),assert=require('node:assert/strict'),{chromium}=require('playwright');
const fs=require('node:fs'),http=require('node:http'),path=require('node:path');
const web=path.resolve(__dirname,'../src/PixelVault.Companion/wwwroot');
const policy=fs.readFileSync(path.join(web,'../Program.cs'),'utf8').match(/ContentSecurityPolicy = "([^"]+)"/)[1];
test('item rules edit separately from checks, survive polling and remain available on phone',async()=>{
 const game={provider:'steam',gameId:'42',groupId:'rules-game',title:'Another game',platform:'Steam',trackerCount:1};
 const definition={id:'custom.rules',provider:'steam',gameId:'42',gameTitle:'Another game',title:'Tasks',description:'',milestones:[],categoryTargets:{},items:[]};
 let missable=false,excluded=false,revision=0,progressWrites=0,ruleRequest;
 const view=()=>({definition,states:[{item:{id:'a',name:'Alternate message <icon name= faceicon_good>',detail:'Full size <icon name= faceicon_good>',category:'Messages',target:1,order:0,missable,excludedFromTotal:excluded},current:0,reportedCurrent:0,manualCurrent:null,note:'Keep my note',isKnown:true,complete:false},{item:{id:'b',name:'Required message',category:'Messages',target:1,order:1},current:0,reportedCurrent:0,manualCurrent:null,note:'',isKnown:true,complete:false}],totals:{completed:0,total:excluded?1:2,unknown:0},versions:{a:{progress:'p',note:'n'},b:{progress:'p',note:'n'}},rulesVersions:{a:String(revision),b:'0'},protectedItems:[],sourceLinked:true,readOnly:false});
 const server=http.createServer(async(req,res)=>{
  const url=new URL(req.url,'http://localhost');res.setHeader('Content-Security-Policy',policy);
  if(url.pathname.startsWith('/api/v1')){
   const ep=url.pathname.slice(7);let result;
   if(ep==='/trackers/custom.rules/item-rules'){
    let body='';for await(const chunk of req)body+=chunk;ruleRequest=JSON.parse(body);missable=ruleRequest.missable;excluded=!ruleRequest.requiredForCompletion;revision++;result={tracker:view()};
   }else if(ep==='/trackers/custom.rules/items'){progressWrites++;result={};}
   else result=({'/session':{authenticated:true,workspaceMode:'direct-local'},'/antiforgery':{token:'test'},'/games':[game],'/trackers':[{...definition,totals:view().totals}],'/trackers/custom.rules':view(),'/preferences/game-sections':{sections:['trackers']},'/preferences/library':{sort:'title-az',pins:[]},'/refresh/connection':{workspaceId:'rules-test',online:true,supported:[]},'/workspace':{id:'rules-test',mode:'direct-local'}})[ep]??{};
   res.setHeader('Content-Type','application/json');res.end(JSON.stringify(result));return;
  }
  const file=path.join(web,url.pathname==='/'?'index.html':decodeURIComponent(url.pathname));
  if(!file.startsWith(web)||!fs.existsSync(file)){res.writeHead(404);res.end();return;}
  res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':file.endsWith('.html')?'text/html':'application/octet-stream');res.end(fs.readFileSync(file));
 });
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
 let browser;
 try{
  browser=await chromium.launch({channel:'msedge',headless:true});
  const page=await browser.newPage({viewport:{width:390,height:844}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.goto(`http://127.0.0.1:${server.address().port}/#tracker/custom.rules`);
  const row=page.locator('[data-record="a"]');await row.waitFor();
  assert.equal(await row.locator('form').count(),0,'Closed rows should defer their editors');
  await row.locator(':scope > summary').click();
  await row.locator('textarea').fill('Draft survives collapsed row');
  await row.locator(':scope > summary').click();
  await page.evaluate(()=>renderItems());
  await row.locator(':scope > summary').click();
  assert.equal(await row.locator('textarea').inputValue(),'Draft survives collapsed row');
  await row.locator('textarea').fill('Keep my note');
  const titleIconSize=await row.locator(':scope > summary .mail-inline-icon').evaluate(img=>img.getBoundingClientRect().width/parseFloat(getComputedStyle(img.parentElement).fontSize));
  const detailIconSize=await row.locator('.item-body > p .mail-inline-icon').first().evaluate(img=>img.getBoundingClientRect().width/parseFloat(getComputedStyle(img.parentElement).fontSize));
  assert.ok(Math.abs(titleIconSize-0.65)<0.01);assert.ok(Math.abs(detailIconSize-1.3)<0.01);
  for(const width of [390,1280]){
   await page.setViewportSize({width,height:844});
   const bounds=await row.locator(':scope > summary .mail-inline-icon').evaluate(img=>{
    const icon=img.getBoundingClientRect(),range=document.createRange();range.selectNodeContents(img.parentElement.firstChild);
    const text=range.getBoundingClientRect();return {top:icon.top,bottom:icon.bottom,textTop:text.top,textBottom:text.bottom};
   });
   assert.ok(bounds.top>=bounds.textTop && bounds.bottom<=bounds.textBottom,'title icon stays within the text height');
   if(process.env.PV_MAIL_ICON_SCREENSHOTS){fs.mkdirSync(process.env.PV_MAIL_ICON_SCREENSHOTS,{recursive:true});await row.screenshot({path:path.join(process.env.PV_MAIL_ICON_SCREENSHOTS,`mail-icons-${width}.png`)});}
  }
  await page.setViewportSize({width:390,height:844});
  await row.locator('.item-rules>summary').click();const form=row.locator('[data-item-rules]');
  await form.getByLabel('Missable',{exact:true}).check();await form.getByLabel('Required for completion').uncheck();
  await page.locator('#page-title').click();await page.evaluate(()=>pollConnection());
  assert.equal(await form.getByLabel('Required for completion').isChecked(),false);
  await form.getByRole('button',{name:'Save item settings',exact:true}).click();
  await page.waitForFunction(()=>document.querySelector('[data-record="a"] .excluded')?.textContent==='Optional');
  assert.equal(ruleRequest.missable,true);assert.equal(ruleRequest.requiredForCompletion,false);assert.equal(progressWrites,0);
  assert.match(await row.innerText(),/Missable/);assert.match(await page.locator('#summary').innerText(),/0\s*\/\s*1/);
  await page.reload();await page.locator('[data-record="a"]>summary').click();await page.locator('[data-record="a"] .item-rules>summary').click();
  assert.equal(await page.locator('[data-record="a"] input[name="required"]').isChecked(),false);
  assert.equal(await page.locator('[data-record="a"] input[name="missable"]').isChecked(),true);
  assert.deepEqual(errors,[]);
 }finally{if(browser)await browser.close();await new Promise(resolve=>server.close(resolve));}
});
