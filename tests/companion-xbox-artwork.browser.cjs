const {test}=require('node:test'),assert=require('node:assert/strict'),{chromium}=require('playwright');
const path=require('node:path'),fs=require('node:fs');
const web=path.resolve(__dirname,'../src/PixelVault.Companion/wwwroot');
const policy=fs.readFileSync(path.resolve(__dirname,'../src/PixelVault.Companion/Program.cs'),'utf8').match(/ContentSecurityPolicy = "([^"]+)"/)[1];

test('Xbox artwork crops match native in cards and details under the production CSP',async()=>{
 const browser=await chromium.launch({channel:'chrome',headless:true});
 try{for(const width of [390,1280]){
  const context=await browser.newContext({viewport:{width,height:900}}),page=await context.newPage();
  await context.route('http://xbox.test/**',async route=>{
   const url=new URL(route.request().url()),name=path.basename(url.pathname);
   if(url.pathname.startsWith('/api/')){
    const portrait=url.pathname.includes('/portrait/'),square=url.pathname.includes('/square/');
    await route.fulfill({contentType:'image/svg+xml',body:`<svg xmlns="http://www.w3.org/2000/svg" width="${portrait?480:square?512:1920}" height="${portrait?640:square?512:1080}"><rect width="100%" height="100%" fill="#243037"/><rect x="704" y="284" width="512" height="512" fill="#b4d7a7"/></svg>`});
   }else if(/\.(js|css)$/.test(name))await route.fulfill({contentType:name.endsWith('.js')?'text/javascript':'text/css',body:fs.readFileSync(path.join(web,name))});
   else await route.fulfill({contentType:'text/html',headers:{'Content-Security-Policy':policy},body:'<link rel="stylesheet" href="/app.css"><link rel="stylesheet" href="/achievements.css">'+['page-heading','breadcrumbs','achievements-page','page-count','achievement-group-form','achievement-use-desktop','achievement-group-cancel','achievement-group-dialog','achievement-category','achievement-dlc'].map(id=>`<div id="${id}"></div>`).join('')});
  });
  await page.goto('http://xbox.test/');await page.addScriptTag({url:'http://xbox.test/achievements.js'});
  await page.evaluate(()=>{
   window.ui=PixelVaultAchievements.create({api:async()=>({rows:[{achievement:{id:'one',title:'Badge',description:'Test',icon:'https://images-eds-ssl.xboxlive.com/image?url=badge',progressKnown:true,unlocked:true,order:0,groups:{category:'Story',dlc:'',sortOrder:0},tags:''}}],categories:[],categoryOrder:[],dlcs:[]}),heading(){},progressBar:()=>'',paintBars(){},status(){},getGames:()=>[],getTrackers:()=>[],newRequestId:()=> 'test',trackerCard:()=>''});
  });
  for(const [provider,game,zoom,fit] of [['xbox','2026288209',true,'cover'],['xbox','1973901376',false,'cover'],['xbox','square',false,'contain'],['xbox','portrait',false,'contain'],['steam','2026288209',false,'cover']]){
   for(const detail of [false,true]){
    await page.evaluate(async({provider,game,detail})=>{const parts=detail?['achievement',provider,game,'one']:['achievements',provider,game];location.hash='#'+parts.join('/');await ui.open(parts);},{provider,game,detail});
    const img=page.locator('.achievement-icon img');await img.waitFor();await page.waitForFunction(()=>{const i=document.querySelector('.achievement-icon img');return i.complete&&i.naturalWidth>0;});
    const style=await img.evaluate(i=>({transform:getComputedStyle(i).transform,fit:getComputedStyle(i).objectFit,clip:getComputedStyle(i.parentElement).overflow,hidden:i.hidden}));
    assert.equal(style.transform,zoom?'matrix(2.10938, 0, 0, 2.10938, 0, 0)':'none');assert.equal(style.fit,fit);assert.equal(style.clip,'hidden');assert.equal(style.hidden,false);
   }
  }
  await context.close();
 }}finally{await browser.close();}
});
