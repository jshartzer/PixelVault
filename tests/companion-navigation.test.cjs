const {test}=require('node:test');const assert=require('node:assert/strict');
const {create,isBackSwipe}=require('../src/PixelVault.Companion/wwwroot/navigation.js');
const swipe={dx:140,dy:8,maxY:12,duration:240,width:390,startX:60};
test('only deliberate rightward swipes qualify; taps, scrolling, diagonals and native edge gestures do not',()=>{
  assert.equal(isBackSwipe(swipe),true);
  for(const patch of [{dx:35},{dx:-140},{dy:80},{maxY:90},{duration:1600},{duration:10},{startX:8}])assert.equal(isBackSwipe({...swipe,...patch}),false,JSON.stringify(patch));
  assert.equal(isBackSwipe({...swipe,startX:8,edge:0}),true,'Standalone/Home Screen mode can handle the edge itself.');
});
function browser(initial='https://companion.local/#'){
  const events={},docEvents={},entries=[{url:initial,state:null}];let cursor=0;let allowed=true;let fallback=null;let renders=0;
  const host={innerWidth:390,scrollX:0,scrollY:0,location:{href:initial,hash:new URL(initial).hash},
    addEventListener:(name,callback)=>{events[name]=callback;},scrollTo:(x,y)=>{host.scrollX=x;host.scrollY=y;},
    document:{addEventListener:(name,callback)=>{docEvents[name]=callback;},getSelection:()=>({isCollapsed:true})}};
  function update(url,state){const resolved=new URL(url || host.location.href,host.location.href);host.location.href=resolved.href;host.location.hash=resolved.hash;host.history.state=state;}
  host.history={state:null,replaceState(state,_,url){entries[cursor]={url:new URL(url || host.location.href,host.location.href).href,state};update(url,state);},
    pushState(state,_,url){entries.splice(cursor+1);entries.push({url:new URL(url,host.location.href).href,state});cursor++;update(url,state);},
    back(){if(cursor){cursor--;update(entries[cursor].url,entries[cursor].state);events.popstate();}},
    forward(){if(cursor+1<entries.length){cursor++;update(entries[cursor].url,entries[cursor].state);events.popstate();}}};
  const nav=create(host,{allowed:()=>allowed,fallback:()=>fallback,onNavigate:()=>{renders++;}});
  const touch=(name,x,y,time,{interactive=false,count=1,nativeScroll=false}={})=>{
    const event={target:{closest:selector=>interactive||nativeScroll&&selector.includes('[data-native-scroll]')?{}:null},touches:name==='touchend'?[]:Array.from({length:count},()=>({clientX:x,clientY:y})),changedTouches:[{clientX:x,clientY:y}],timeStamp:time,cancelable:true,preventDefault(){this.prevented=true;}};
    docEvents[name](event);return event;
  };
  return {nav,host,touch,events,allow:value=>{allowed=value;},fallback:value=>{fallback=value;},renders:()=>renders};
}
const settle=()=>new Promise(resolve=>setImmediate(resolve));
test('swipe returns to the actual previous screen and restores its scroll, without a duplicate hash render',async()=>{
  const b=browser();b.host.scrollY=420;await b.nav.go('#game/steam/42');b.host.scrollY=185;await b.nav.go('#tracker/test');
  b.touch('touchstart',60,210,0);b.touch('touchmove',210,218,140);const end=b.touch('touchend',210,218,220);await settle();
  assert.equal(end.prevented,true);assert.equal(b.host.location.hash,'#game/steam/42');assert.equal(b.host.scrollY,185);
  const count=b.renders();b.events.hashchange();await settle();assert.equal(b.renders(),count);
  assert.equal(b.nav.back(),true);await settle();assert.equal(b.host.location.hash,'');assert.equal(b.host.scrollY,420);
  assert.equal(b.nav.back(),false);
});
test('controls, multiple fingers, vertical scrolling, cancelled touches and modal/save guards never navigate',async()=>{
  const b=browser();await b.nav.go('#game/steam/42');
  b.touch('touchstart',60,210,0,{interactive:true});b.touch('touchend',240,210,200);
  b.touch('touchstart',60,210,300,{count:2});b.touch('touchend',240,210,500);
  b.touch('touchstart',60,210,600);b.touch('touchmove',65,290,700);b.touch('touchend',240,210,800);
  b.touch('touchstart',60,210,900);
  b.touch('touchcancel',60,210,950);b.touch('touchend',240,210,1100);
  b.allow(false);b.touch('touchstart',60,210,1200);b.touch('touchend',240,210,1400);
  await settle();assert.equal(b.host.location.hash,'#game/steam/42');
});
test('a bookmarked tracker falls back to its game instead of leaving the app, and normal browser forward works',async()=>{
  const b=browser('https://companion.local/#tracker/test');b.fallback('#game/steam/42');assert.equal(b.nav.back(),true);await settle();assert.equal(b.host.location.hash,'#game/steam/42');
  await b.nav.go('#tracker/other');b.nav.back();await settle();b.host.history.forward();await settle();assert.equal(b.host.location.hash,'#tracker/other');
});
test('horizontal scrolling in a photo strip does not trigger page back, and a recent-photo bookmark returns home',async()=>{
  const b=browser();await b.nav.go('#game/steam/42');b.touch('touchstart',60,210,0,{nativeScroll:true});b.touch('touchmove',240,210,150,{nativeScroll:true});b.touch('touchend',240,210,220,{nativeScroll:true});await settle();assert.equal(b.host.location.hash,'#game/steam/42');
  const direct=browser('https://companion.local/#recent-photo/steam/42/p');direct.fallback('#');assert.equal(direct.nav.back(),true);await settle();assert.equal(direct.host.location.hash,'');
});
