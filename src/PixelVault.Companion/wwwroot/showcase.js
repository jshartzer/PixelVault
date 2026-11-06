(function(root){
  'use strict';
  const sizes={small:120,medium:160,large:200};
  const states={base:'Gold · Base complete','base-dlc':'Emerald · Base + DLC complete','base-dlc-pv':'Diamond · Base + DLC + PV complete'};
  const escape=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  function entries(games){
    const library=root.PixelVaultLibrary || (typeof require==='function'?require('./library.js'):null);
    return library.groupGames(games).flatMap(group=>{
      const tier=library.completionTier(group);
      if(!states[tier])return [];
      const edition=group.editions.find(e=>e.completionTier===tier&&e.completionTotal>0);
      return edition?[{...edition,id:group.id,title:group.title,coverUrl:edition.coverUrl||group.coverUrl,tier}]:[];
    }).sort((a,b)=>a.title.localeCompare(b.title));
  }
  const artwork=root.PixelVaultCaseArtwork || (typeof require==='function'?require('./case-artwork.js'):null);
  function layout(rows,width,coverSize=200,inset=44,gap=18){
    const columns=Math.max(1,Math.floor((Math.max(0,width-inset*2)+gap)/(coverSize+gap)));
    return Object.keys(states).map(tier=>{
      const games=rows.filter(game=>game.tier===tier),floors=[];
      for(let i=0;i<games.length;i+=columns)floors.push(games.slice(i,i+columns));
      if(!floors.length)floors.push([]);
      return {tier,columns,floors};
    });
  }
  function markup(rows,options={}){
    const coverSize=options.coverSize||200,inset=options.inset??44;
    const cases=layout(rows,options.width||1200,coverSize,inset);
    return cases.map(({tier,columns,floors})=>{
      const cards=games=>games.map(game=>{
        const href='#achievements/'+encodeURIComponent(game.provider)+'/'+encodeURIComponent(game.gameId);
        const caption=`<div class="case-caption"><h4>${escape(game.title)}</h4><p class="showcase-count">${game.completionEarned} / ${game.completionTotal} achievements</p><p class="showcase-state">${escape(game.platform||game.provider)}</p></div>`;
        const art=`<div class="case-art"><div class="cover-frame completion-foil-host"><span class="cover-fallback" aria-hidden="true">${escape(game.title.split(/\s+/).map(w=>w[0]).slice(0,3).join(''))}</span>${game.coverUrl?`<img src="${escape(game.coverUrl)}" alt="" loading="lazy" decoding="async" width="480" height="720">`:''}${tier==='base-dlc-pv'?'<span class="completion-foil" aria-hidden="true"></span>':''}</div>${caption}</div>`;
        const reflection=game.coverUrl?`<span class="case-reflection" aria-hidden="true"><img src="${escape(game.coverUrl)}" alt="" loading="lazy" decoding="async" width="480" height="720"></span>`:'';
        return `<article class="showcase-card"><a class="showcase-game" href="${escape(href)}" aria-label="${escape(game.title)}: ${game.completionEarned} of ${game.completionTotal} achievements. Open achievements.">${art}${reflection}</a></article>`;
      }).join('');
      const floorMarkup=floors.map(games=>`<div class="case-floor"><div class="case-covers" data-cover-count="${games.length||1}">${games.length?cards(games):'<p class="showcase-group-empty">No games in this case yet.</p>'}</div><div class="case-shelf" aria-hidden="true"></div></div>`).join('');
      return `<section class="showcase-case completion-${tier}" aria-label="${states[tier]}" data-tier="${tier}" data-columns="${columns}"><div class="case-shell"><canvas class="case-background" aria-hidden="true"></canvas><h3 class="case-plaque"><span>100% COMPLETE</span><small>${tier==='base'?'BASE GAME':tier==='base-dlc-pv'?'BASE GAME + DLC + PV':'BASE GAME + DLC'}</small></h3><div class="case-floors">${floorMarkup}</div><canvas class="case-front" aria-hidden="true"></canvas><span class="case-border" aria-hidden="true"></span><span class="case-corner case-nw" aria-hidden="true"></span><span class="case-corner case-ne" aria-hidden="true"></span><span class="case-corner case-sw" aria-hidden="true"></span><span class="case-corner case-se" aria-hidden="true"></span></div></section>`;
    }).join('');
  }

  function create({getGames}){
    const el=id=>document.getElementById(id),dialog=el('completion-showcase');
    let lastRender='';
    function render(){
      const rows=entries(getGames()),coverSize=sizes[el('showcase-size').value]||120;
      const width=el('showcase-grid').clientWidth||Math.min(1400,innerWidth-60);
      const inset=parseFloat(getComputedStyle(el('showcase-grid')).getPropertyValue('--case-inset'))||44;
      const columns=layout(rows,width,coverSize,inset)[0].columns;
      const stamp=JSON.stringify([coverSize,columns,rows]);
      function paint(){
        el('showcase-grid').querySelectorAll('.showcase-case').forEach(section=>{
          const count=section.querySelectorAll('.showcase-card').length;
          const key=JSON.stringify([width,coverSize,count,inset,devicePixelRatio]);
          if(section.dataset.paint===key)return;
          // CSP permits individual CSSOM properties, while HTML style attributes are blocked.
          section.querySelectorAll('.case-covers').forEach(floor=>floor.style.setProperty('--floor-count',floor.dataset.coverCount));
          const m=artwork.draw(section.querySelector('.case-background'),section.querySelector('.case-front'),width,coverSize,count,section.dataset.tier,inset);
          section.style.setProperty('--case-gap',m.gap+'px');section.style.setProperty('--case-pitch',m.pitch+'px');section.style.setProperty('--case-height',m.height+'px');section.dataset.paint=key;
        });
      }
      if(stamp===lastRender){paint();return;}
      lastRender=stamp;
      el('showcase-grid').style.setProperty('--showcase-size',coverSize+'px');
      el('showcase-grid').innerHTML=markup(rows,{width,coverSize,inset});
      paint();
      el('showcase-grid').querySelectorAll('img').forEach(image=>image.addEventListener('error',()=>image.hidden=true,{once:true}));
      el('showcase-grid').querySelectorAll('.showcase-game').forEach(link=>{
        let pointerType='';
        link.addEventListener('pointerdown',event=>{pointerType=event.pointerType;});
        link.addEventListener('click',event=>{if(event.detail>0&&(event.pointerType||pointerType)!=='touch')event.preventDefault();});
        link.addEventListener('dblclick',event=>{event.preventDefault();location.hash=link.getAttribute('href');});
      });
      el('showcase-empty').hidden=rows.length>0;
      el('showcase-count').textContent=`${rows.length} completed games`;
    }
    el('showcase-open').addEventListener('click',()=>{dialog.showModal();render();});
    el('showcase-close').addEventListener('click',()=>dialog.close());
    el('showcase-size').addEventListener('change',render);
    const resize=new ResizeObserver(()=>{if(dialog.open)render();});resize.observe(el('showcase-grid'));
    dialog.addEventListener('click',event=>{const bounds=dialog.getBoundingClientRect();if(event.target===dialog&&(event.clientX<bounds.left||event.clientX>bounds.right||event.clientY<bounds.top||event.clientY>bounds.bottom))dialog.close();});
    dialog.addEventListener('close',()=>{if(!el('showcase-open').hidden)el('showcase-open').focus({preventScroll:true});});
    return {close:()=>{if(dialog.open)dialog.close();},refresh:()=>{if(dialog.open)render();}};
  }
  const api={sizes,states,entries,layout,markup,create};
  if(typeof module!=='undefined'&&module.exports)module.exports=api;else root.PixelVaultShowcase=api;
})(typeof globalThis!=='undefined'?globalThis:this);
