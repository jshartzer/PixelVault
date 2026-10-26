(function(root){
  'use strict';
  function isBackSwipe({dx,dy,maxY,duration,width,startX,edge=24}){
    return startX>=edge && dx>=Math.max(80,Math.min(100,width*.24)) && Math.abs(dy)<=36 && maxY<=36 && dx>Math.abs(dy)*2.5 && duration>=40 && duration<=700;
  }
  function create(host,{onNavigate,allowed=()=>true,fallback=()=>null,hint=()=>{}}){
    const doc=host.document,history=host.history,positions=new Map();
    const edge=host.navigator?.standalone || host.matchMedia?.('(display-mode: standalone)').matches?0:24;
    let session=Date.now().toString(36)+Math.random().toString(36).slice(2),index=0,lastUrl=host.location.href,touch;
    const hash=value=>value && value!=='#'?'#'+value.replace(/^#/,''):'#';
    const mark=()=>({...history.state,pixelVaultNavigation:{session,index}});
    const remember=()=>positions.set(index,{x:host.scrollX || 0,y:host.scrollY || 0});
    history.replaceState(mark(),'');history.scrollRestoration='manual';
    async function render(position={x:0,y:0}){
      const url=host.location.href;lastUrl=url;
      await onNavigate();if(host.location.href===url)host.scrollTo(position.x,position.y);
    }
    function go(value,replace=false){
      const target=hash(value);if(target===hash(host.location.hash))return;
      remember();if(!replace)index++;
      history[replace?'replaceState':'pushState'](mark(),'',target);return render();
    }
    function canBack(){return allowed() && (index>0 || !!fallback());}
    function back(){
      if(!canBack())return false;
      remember();if(index>0)history.back();else go(fallback(),true);return true;
    }
    function reset(value='#'){
      session=Date.now().toString(36)+Math.random().toString(36).slice(2);index=0;positions.clear();
      history.replaceState(mark(),'',hash(value));lastUrl=host.location.href;return render();
    }
    host.addEventListener('popstate',()=>{
      const state=history.state?.pixelVaultNavigation;
      index=state?.session===session?state.index:0;
      return render(positions.get(index));
    });
    host.addEventListener('hashchange',()=>{
      if(lastUrl===host.location.href)return;
      remember();index++;history.replaceState(mark(),'');return render();
    });
    doc.addEventListener('click',event=>{
      const link=event.target.closest?.('a[href^="#"]');
      if(!link || event.defaultPrevented || event.button!==0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || link.hasAttribute('download') || link.target && link.target!=='_self')return;
      event.preventDefault();go(link.getAttribute('href'));
    });
    const blocked=target=>!!target.closest?.('input,textarea,select,button,a,[contenteditable="true"],[role="slider"],[role="checkbox"],[data-native-scroll],video,audio');
    const clear=()=>{touch=undefined;hint(false);};
    doc.addEventListener('touchstart',event=>{
      clear();if(event.touches.length!==1 || blocked(event.target) || !canBack() || doc.getSelection?.()?.isCollapsed===false)return;
      const point=event.touches[0];if(point.clientX<edge)return; // Leave Safari's native edge gesture alone, except in standalone mode.
      touch={x:point.clientX,y:point.clientY,time:event.timeStamp,maxY:0,url:host.location.href};
    },{passive:true});
    doc.addEventListener('touchmove',event=>{
      if(!touch)return;if(event.touches.length!==1){clear();return;}
      const point=event.touches[0],dx=point.clientX-touch.x,dy=point.clientY-touch.y;
      touch.maxY=Math.max(touch.maxY,Math.abs(dy));
      if(touch.maxY>36 || dx < -20){clear();return;}
      if(dx>20 && dx>Math.abs(dy)*2.5 && event.cancelable)event.preventDefault();
      hint(isBackSwipe({dx,dy,maxY:touch.maxY,duration:event.timeStamp-touch.time,width:host.innerWidth,startX:touch.x,edge}));
    },{passive:false});
    doc.addEventListener('touchend',event=>{
      const start=touch;clear();if(!start || event.touches.length!==0 || event.changedTouches.length!==1 || start.url!==host.location.href || !canBack())return;
      const point=event.changedTouches[0];
      if(isBackSwipe({dx:point.clientX-start.x,dy:point.clientY-start.y,maxY:start.maxY,duration:event.timeStamp-start.time,width:host.innerWidth,startX:start.x,edge})){
        if(event.cancelable)event.preventDefault();back();
      }
    },{passive:false});
    doc.addEventListener('touchcancel',clear,{passive:true});
    return {go,back,reset,canBack};
  }
  const api={create,isBackSwipe};if(typeof module!=='undefined'&&module.exports)module.exports=api;else root.PixelVaultNavigation=api;
})(typeof globalThis!=='undefined'?globalThis:this);
