const {test}=require('node:test'),assert=require('node:assert/strict'),{chromium}=require('playwright');
const fs=require('node:fs'),http=require('node:http'),path=require('node:path');
const web=path.resolve(__dirname,'../src/PixelVault.Companion/wwwroot');

test('single binary fields use native checkboxes with safe saving, keyboard access and protected state',async()=>{
 let current=0,fail=false,hold=false,release,locked=false,readOnly=false;const writes=[];
 const view=id=>{
  const multi=id==='multi',count=id==='count';
  const items=multi?['First','Second'].map((name,i)=>({id:'m'+i,name:'Card',milestone:name,groupId:'m',category:'Cards',target:1,order:i})):
   [{id:'one',name:'Originally obtained',detail:'Pull rate: 1%.',category:'Cards',target:count?100:1,order:0,...(id==='collection'?{groupId:'card',milestone:'Collected'}:{})}];
  return {definition:{id,provider:'steam',gameId:'42',gameTitle:'Test game',title:id,milestones:multi?['First','Second']:id==='collection'?['Collected']:[],categoryTargets:{},items},
   states:items.map(item=>({item,current,reportedCurrent:current,manualCurrent:null,note:'Keep note',isKnown:true,complete:current>=item.target})),
   versions:Object.fromEntries(items.map(i=>[i.id,{progress:'p'+current,note:'n'}])),rulesVersions:Object.fromEntries(items.map(i=>[i.id,'r'])),
   totals:{completed:current,total:count?100:items.length,unknown:0},protectedItems:locked?['one']:[],sourceLinked:true,readOnly};
 };
 const server=http.createServer(async(req,res)=>{
  const p=new URL(req.url,'http://fixture').pathname;res.setHeader('Content-Type','application/json');
  if(p.startsWith('/api/v1')){
   const ep=p.slice(7);
   if(req.method==='POST'&&ep.endsWith('/items')){
    let body='';for await(const chunk of req)body+=chunk;const command=JSON.parse(body);writes.push(command);
    if(hold)await new Promise(r=>release=r);
    if(fail){res.writeHead(503);res.end(JSON.stringify({error:'Not saved'}));return;}
    current=command.current;res.end(JSON.stringify({result:{replayed:false},tracker:view(ep.split('/')[2])}));return;
   }
   const data={'/session':{authenticated:true},'/antiforgery':{token:'test'},'/games':[],'/trackers':[],
    '/preferences/game-sections':{sections:[]},'/preferences/library':{sort:'title-az',pins:[]},'/preferences/tracker-groups':{groups:['achievement','manual','century']},
    '/workspace':{id:'single-checkbox'},'/refresh/connection':{supported:[]},'/screenshots/recent':{photos:[]}};
   res.end(JSON.stringify(data[ep]??(ep.startsWith('/trackers/')?view(ep.split('/')[2]):{})));return;
  }
  const file=path.join(web,p==='/'?'index.html':p);if(!fs.existsSync(file)){res.writeHead(404);res.end();return;}
  res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':file.endsWith('.svg')?'image/svg+xml':'text/html');res.end(fs.readFileSync(file));
 });
 await new Promise(r=>server.listen(0,'127.0.0.1',r));const browser=await chromium.launch({channel:'chrome',headless:true});
 try{for(const width of [390,1280]){
  current=0;locked=false;readOnly=false;fail=false;hold=false;
  const page=await browser.newPage({viewport:{width,height:844}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.addInitScript(()=>window.setInterval=()=>1);
  await page.goto(`http://127.0.0.1:${server.address().port}/#tracker/collection`);
  const box=page.locator('details.item > summary input.tracker-checkbox');
  assert.equal(await box.getAttribute('type'),'checkbox');assert.equal(await box.isChecked(),false);
  assert.equal(await page.locator('details.item > summary .tracker-check-target').innerText(),'');
  hold=true;await box.click();await page.waitForFunction(()=>document.querySelector('.tracker-checkbox').disabled);
  assert.equal(await box.isChecked(),false,'Pending request must retain the confirmed state');
  assert.equal(await page.locator('details.item').getAttribute('open'),null,'Checking must not expand the row');
  hold=false;release();await page.waitForFunction(()=>document.querySelector('.tracker-checkbox').checked&&!document.querySelector('.tracker-checkbox').disabled);
  assert.equal(writes.at(-1).current,1);assert.equal(writes.at(-1).expectedVersion,'p0');
  await box.focus();await page.keyboard.press('Space');await page.waitForFunction(()=>!document.querySelector('.tracker-checkbox').checked&&!document.querySelector('.tracker-checkbox').disabled);
  assert.equal(writes.at(-1).current,0);
  fail=true;await box.click();await page.getByText('Not saved',{exact:true}).waitFor();assert.equal(await box.isChecked(),false);fail=false;
  await page.locator('details.item > summary').click();await page.locator('.item-body .tracker-checkbox').waitFor();
  assert.equal(await page.getByRole('button',{name:/Mark (complete|incomplete)/}).count(),0);
  assert.match(await page.locator('.item-body').innerText(),/Pull rate: 1%/);
  assert.equal(await page.locator('textarea[name="note"]').inputValue(),'Keep note');
  if(process.env.PV_SINGLE_CHECKBOX_SCREENSHOTS){fs.mkdirSync(process.env.PV_SINGLE_CHECKBOX_SCREENSHOTS,{recursive:true});await page.locator('details.item').screenshot({path:path.join(process.env.PV_SINGLE_CHECKBOX_SCREENSHOTS,`checkbox-${width}.png`)});}
  current=1;locked=true;await page.evaluate(()=>route());assert.equal(await box.isDisabled(),true);locked=false;
  readOnly=true;await page.evaluate(()=>route());assert.equal(await box.isDisabled(),true);readOnly=false;current=0;
  await page.evaluate(()=>navigation.go('#tracker/plain'));assert.equal(await box.count(),1);
  await page.evaluate(()=>navigation.go('#tracker/multi'));assert.equal(await box.count(),0);assert.equal(await page.locator('summary button[role="checkbox"]').count(),2);
  assert.match(await page.locator('summary .header-checks').innerText(),/First/);
  await page.evaluate(()=>navigation.go('#tracker/count'));await page.locator('details.item > summary').click();await page.locator('input[name="current"]').waitFor();
  assert.equal(await box.count(),0);assert.equal(await page.getByRole('button',{name:'Save count',exact:true}).count(),1);
  assert.deepEqual(errors,[]);await page.close();
 }}finally{release?.();await browser.close();await new Promise(r=>server.close(r));}
});
