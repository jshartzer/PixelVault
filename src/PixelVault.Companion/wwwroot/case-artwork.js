// Sculpted completion cases. Static, retained artwork; no animation or game-state writes.
(function(root){
'use strict';
  const palettes={
    gold:{dark:'#3b230a',shade:'#80501e',mid:'#b68b41',light:'#f5d88e',shine:'#fff1ce',color:'#d6a946',wall:'#2d2416',glow:'255,193,75'},
    emerald:{dark:'#032b27',shade:'#086758',mid:'#179c81',light:'#68d8bd',shine:'#d4fff0',color:'#2cb396',wall:'#0a3027',glow:'38,230,179'}
  };
  const diamond=root.PixelVaultDiamondArtwork || (typeof require==='function'?require('./diamond-artwork.js'):null);
  palettes.diamond={dark:'#23364b',shade:'#456481',mid:'#6d8da9',light:'#c3e5fb',shine:'#f5fcff',color:'#8fd2ff',wall:'#102738',glow:'92,179,255'};
  let grain=null;
  if(typeof document!=='undefined'){grain=document.createElement('canvas');grain.width=160;grain.height=160;
  { const c=grain.getContext('2d'),d=c.createImageData(160,160);let seed=913;
    for(let i=0;i<d.data.length;i+=4){seed=(seed*1664525+1013904223)>>>0;const n=seed>>>24;d.data[i]=d.data[i+1]=d.data[i+2]=n;d.data[i+3]=31;}c.putImageData(d,0,0);
  }
  }
  function gradient(ctx,x1,y1,x2,y2,stops){const g=ctx.createLinearGradient(x1,y1,x2,y2);stops.forEach(([at,col])=>g.addColorStop(at,col));return g;}
  function polygon(ctx,points,fill,stroke,width=1){ctx.beginPath();points.forEach(([x,y],i)=>i?ctx.lineTo(x,y):ctx.moveTo(x,y));ctx.closePath();if(fill){ctx.fillStyle=fill;ctx.fill();}if(stroke){ctx.strokeStyle=stroke;ctx.lineWidth=width;ctx.stroke();}}
  function polyPath(points){const p=new Path2D();points.forEach(([x,y],i)=>i?p.lineTo(x,y):p.moveTo(x,y));p.closePath();return p;}
  function metallic(ctx,path,p,width=5,x1=0,y1=0,x2=60,y2=70){
    ctx.save();ctx.lineJoin='round';ctx.lineCap='round';ctx.strokeStyle='#000c';ctx.lineWidth=width+3;ctx.shadowColor='#000b';ctx.shadowBlur=4;ctx.shadowOffsetY=2;ctx.stroke(path);ctx.shadowBlur=0;ctx.shadowOffsetY=0;
    ctx.strokeStyle=gradient(ctx,x1,y1,x2,y2,[[0,p.dark],[.17,p.mid],[.28,p.shine],[.34,p.shade],[.52,p.dark],[.64,p.light],[.72,p.shine],[.82,p.mid],[1,p.dark]]);ctx.lineWidth=width;ctx.stroke(path);
    ctx.translate(-.35,-.6);ctx.strokeStyle=p.shine+'bb';ctx.lineWidth=.65;ctx.stroke(path);ctx.restore();
  }
  function roundRect(ctx,x,y,w,h,r){ctx.beginPath();ctx.roundRect(x,y,w,h,r);}
  function setup(canvas,w,h){const dpr=Math.min(devicePixelRatio||1,2,16380/h,16380/w,Math.sqrt(16000000/(w*h)));canvas.width=Math.round(w*dpr);canvas.height=Math.round(h*dpr);const c=canvas.getContext('2d');c.scale(dpr,dpr);return c;}
  function bevelRing(ctx,w,h,p){
    const out=[[14,43],[w-14,43],[w-14,h-21],[14,h-21]],inside=[[25,54],[w-25,54],[w-25,h-32],[25,h-32]];
    // Four mitered faces share their endpoints, forming one continuous casting.
    polygon(ctx,[out[0],out[1],inside[1],inside[0]],gradient(ctx,0,43,0,55,[[0,p.shine],[.14,p.mid],[.28,p.dark],[.47,p.light],[.55,p.shine],[.7,p.shade],[1,p.dark]]));
    polygon(ctx,[out[1],out[2],inside[2],inside[1]],gradient(ctx,w-25,0,w-14,0,[[0,p.dark],[.25,p.shine],[.36,p.mid],[.56,p.dark],[.84,p.light],[1,p.dark]]));
    polygon(ctx,[out[2],out[3],inside[3],inside[2]],gradient(ctx,0,h-32,0,h-21,[[0,p.dark],[.23,p.light],[.33,p.shine],[.5,p.shade],[.75,p.dark],[1,p.mid]]));
    polygon(ctx,[out[3],out[0],inside[0],inside[3]],gradient(ctx,14,0,25,0,[[0,p.dark],[.2,p.light],[.3,p.shine],[.48,p.shade],[.7,p.dark],[.9,p.light],[1,p.dark]]));
    ctx.lineWidth=.8;ctx.strokeStyle=p.light;ctx.stroke(polyPath(out));ctx.strokeStyle=p.shade;ctx.stroke(polyPath(inside));
    ctx.strokeStyle=p.color+'77';ctx.lineWidth=1;ctx.strokeRect(29,58,w-58,h-94);
    // Long, restrained specular highlights stay static; no shimmer animation.
    ctx.fillStyle=gradient(ctx,24,0,w-24,0,[[0,p.light+'00'],[.14,p.shine+'ee'],[.25,p.shine+'22'],[.58,p.shine+'44'],[.84,p.shine+'dd'],[1,p.shine+'00']]);ctx.fillRect(24,46,w-48,1.1);ctx.fillRect(26,h-25,w-52,.8);
  }
  function ledge(ctx,w,y,p,bottom=false){
    const edge=bottom?5:16;
    polygon(ctx,[[26,y-33],[w-26,y-33],[w-edge,y],[edge,y]],gradient(ctx,0,y-33,0,y,[[0,'#a2c8c70a'],[.35,p.light+'10'],[.65,'#c5d8d826'],[1,p.shine+'49']]),p.light+'55',.7);
    const g=gradient(ctx,0,y,0,y+11,[[0,p.shine],[.12,p.mid],[.31,p.dark],[.54,p.shade],[.76,p.mid],[1,p.dark]]);
    polygon(ctx,[[edge,y],[w-edge,y],[w-edge+1,y+9],[edge-1,y+9]],g,p.shade,.6);
    ctx.strokeStyle=gradient(ctx,edge,0,w-edge,0,[[0,p.mid],[.12,p.shine],[.28,p.mid],[.5,p.light],[.7,p.mid],[.89,p.shine],[1,p.mid]]);ctx.lineWidth=1.2;ctx.beginPath();ctx.moveTo(edge,y+.6);ctx.lineTo(w-edge,y+.6);ctx.stroke();
    ctx.fillStyle='#0007';ctx.fillRect(edge+8,y+9,w-2*edge-16,6);
  }
  function gem(ctx,x,y,r,side=false){
    ctx.save();ctx.translate(x,y);const p=palettes.gold;
    polygon(ctx,[[0,-r-4],[r*.78+4,0],[0,r+4],[-r*.78-4,0]],gradient(ctx,-r,-r,r,r,[[0,'#fff8cf'],[.25,'#8e622a'],[.5,'#e5ce88'],[.7,'#392a12'],[.9,'#e5d8ac'],[1,'#a78040']]),'#130e08',1);
    polygon(ctx,[[0,-r],[r*.78,0],[0,r],[-r*.78,0]],'#064c3c','#9bffe3',1);
    polygon(ctx,[[0,-r],[-r*.78,0],[-r*.18,-r*.08]],gradient(ctx,-r,-r,0,0,[[0,'#f1fff1'],[.3,'#76efd3'],[1,'#13765c']]));
    polygon(ctx,[[0,-r],[r*.78,0],[r*.16,-r*.1]],gradient(ctx,0,-r,r,0,[[0,'#d5ffe6'],[.3,'#08b58a'],[1,'#003c32']]));
    polygon(ctx,[[-r*.78,0],[0,r],[-r*.18,-r*.08]],gradient(ctx,-r,0,0,r,[[0,'#15d0a9'],[.3,'#006c56'],[1,'#002b26']]));
    polygon(ctx,[[r*.78,0],[0,r],[r*.16,-r*.1]],gradient(ctx,0,0,r,r,[[0,'#4af5c9'],[.4,'#00735a'],[1,'#012c29']]));
    polygon(ctx,[[0,-r*.72],[r*.42,0],[0,r*.66],[-r*.42,0]],gradient(ctx,-r*.4,-r*.7,r*.4,r*.5,[[0,'#b1ffe7'],[.24,'#069d78'],[.62,'#075644'],[1,'#0cf5b7']]),'#46e2bc',.65);
    ctx.strokeStyle='#d7ffed';ctx.lineWidth=1;ctx.beginPath();ctx.moveTo(0,-r);ctx.lineTo(-r*.78,0);ctx.lineTo(0,r);ctx.stroke();ctx.restore();
  }
  function leaf(ctx,x,y,angle,length,width,p=palettes.gold,green=false){
    ctx.save();ctx.translate(x,y);ctx.rotate(angle);
    const left=new Path2D(`M0 0 C${-width} ${-length*.28} ${-width*.9} ${-length*.62} 0 ${-length} C${-width*.1} ${-length*.58} ${-width*.12} ${-length*.23} 0 0Z`);
    const right=new Path2D(`M0 0 C${width} ${-length*.26} ${width*.9} ${-length*.7} 0 ${-length} C${width*.1} ${-length*.56} ${width*.1} ${-length*.22} 0 0Z`);
    ctx.shadowColor='#0009';ctx.shadowBlur=2;ctx.shadowOffsetY=1.5;
    ctx.fillStyle=gradient(ctx,-width,0,width,-length,[[0,p.dark],[.28,green?'#0a5b45':p.mid],[.54,p.light],[.68,p.shine],[.82,p.mid],[1,p.dark]]);ctx.fill(left);
    ctx.fillStyle=gradient(ctx,-width,-length,width,0,[[0,p.shine],[.16,p.light],[.4,green?'#08553f':p.dark],[.7,green?'#26876b':p.mid],[1,p.dark]]);ctx.fill(right);ctx.shadowBlur=0;
    ctx.strokeStyle=p.light;ctx.lineWidth=.75;ctx.stroke(left);ctx.stroke(right);ctx.strokeStyle=p.shine+'cc';ctx.lineWidth=.65;ctx.beginPath();ctx.moveTo(0,0);ctx.quadraticCurveTo(-width*.04,-length*.4,0,-length);ctx.stroke();ctx.restore();
  }
  function upperCorner(ctx,x,y,mirror,p,emerald){
    ctx.save();ctx.translate(x,y);ctx.scale(mirror,1);
    polygon(ctx,[[0,0],[51,0],[4,49]],gradient(ctx,0,0,40,40,[[0,p.shine],[.12,p.mid],[.35,p.dark],[.7,p.light],[1,p.shade]]),'#100e08',.8);
    polygon(ctx,[[6,5],[43,5],[7,39]],gradient(ctx,0,0,35,35,[[0,'#17352c'],[.6,'#05100e'],[1,p.dark]]),p.light,.8);
    const slash=new Path2D('M1 49 L24 17 Q31 8 44 3');metallic(ctx,slash,p,3.3,0,0,35,45);
    const fan=new Path2D('M11 28 Q9 12 23 10 Q31 9 35 6 M13 25 Q18 13 28 12');metallic(ctx,fan,p,1.7,6,5,30,30);
    if(emerald) gem(ctx,13,12,4);else{ctx.fillStyle=gradient(ctx,11,8,17,15,[[0,p.shine],[.45,p.light],[.6,p.shade],[1,p.dark]]);ctx.beginPath();ctx.ellipse(14,12,3,4,.6,0,Math.PI*2);ctx.fill();}
    ctx.restore();
  }
  function lowerCorner(ctx,x,y,mirror,p,emerald){
    ctx.save();ctx.translate(x,y);ctx.scale(mirror,1);
    // Cast scrolls and relief leaves live only at the four outside corners.
    const scrolls=[['M1 -90 C4 -70 28 -66 23 -46 C21 -37 12 -34 8 -41 C3 -50 13 -55 16 -48',6.2],['M4 -53 C3 -29 28 -37 31 -17 C34 4 10 5 9 -7 C7 -17 22 -20 25 -11',8.5],['M10 -28 C38 -38 41 -13 61 -4 L75 0',5.5],['M22 -4 C43 -5 43 -16 34 -19',3.5]];
    scrolls.forEach(([s,width])=>metallic(ctx,new Path2D(s),p,width,0,-85,55,5));
    leaf(ctx,21,-31,-.5,35,6,p,emerald);leaf(ctx,42,-8,1.14,27,6,p,emerald);leaf(ctx,56,-4,1.7,24,4.8,p,emerald);
    if(emerald){leaf(ctx,13,-49,-.35,46,8,p,true);leaf(ctx,12,-71,.1,34,6,p,true);gem(ctx,8,-13,19);}else{leaf(ctx,7,-52,-.48,28,5,p);leaf(ctx,32,-13,.9,22,5,p);}
    ctx.restore();
  }
  function laurel(ctx,x,y,mirror,scale=1){
    ctx.save();ctx.translate(x,y);ctx.scale(mirror*scale,scale);
    const p=palettes.gold;
    const stem=new Path2D('M0 0 Q-29 -18 -40 -50');metallic(ctx,stem,p,2.2,-60,-60,0,0);
    [[-6,-4,-.85,32,5],[-14,-11,-.7,36,6],[-23,-21,-.4,34,6],[-30,-32,-.12,29,5],[-8,-6,-1.85,35,6],[-19,-17,-1.5,33,6],[-29,-30,-1.1,28,5]].forEach(a=>leaf(ctx,...a,p,true));ctx.restore();
  }
  function plaque(ctx,w,p,emerald){
    const pw=Math.min(w-62,emerald?370:360),x=(w-pw)/2,y=8,h=53;
    const outer=[[x+17,y],[x+pw-17,y],[x+pw,y+h/2],[x+pw-17,y+h],[x+17,y+h],[x,y+h/2]];
    ctx.save();ctx.shadowColor='#000';ctx.shadowBlur=10;ctx.shadowOffsetY=3;
    polygon(ctx,outer,gradient(ctx,0,y,0,y+h,[[0,'#28362d'],[.12,'#101611'],[.56,emerald?'#052a21':'#11110d'],[1,'#070f0c']]),p.dark,7);ctx.shadowBlur=0;
    metallic(ctx,polyPath(outer),palettes.gold,3.4,x,y,x+20,y+h);
    const inner=[[x+20,y+5],[x+pw-20,y+5],[x+pw-7,y+h/2],[x+pw-20,y+h-5],[x+20,y+h-5],[x+7,y+h/2]];
    ctx.strokeStyle=emerald?'#65cbb0':'#d4b37a';ctx.lineWidth=.8;ctx.stroke(polyPath(inner));
    // Fine horizontal details stop short of the text rather than jutting out.
    ctx.strokeStyle=p.mid;ctx.lineWidth=.65;const mid=w/2;
    [[mid-128,mid-80],[mid+80,mid+128]].forEach(([a,b])=>{ctx.beginPath();ctx.moveTo(a,y+41);ctx.lineTo(b,y+41);ctx.stroke();});
    if(emerald){const s=w<500?.62:1;laurel(ctx,x+17,y+h-3,1,s);laurel(ctx,x+pw-17,y+h-3,-1,s);gem(ctx,w/2,y-1,11);}
    else {leaf(ctx,x+7,y+h-1,-1.15,36,7,p);leaf(ctx,x+pw-7,y+h-1,1.15,36,7,p);}
    ctx.restore();
  }
  function background(ctx,w,h,p,rows,pitch,coverW,columns,gap,inset=44){
    ctx.save();ctx.shadowColor='#000b';ctx.shadowBlur=18;ctx.shadowOffsetY=13;ctx.fillStyle='#050b0d';ctx.fillRect(15,41,w-30,h-65);ctx.restore();
    ctx.fillStyle=gradient(ctx,0,55,w,0,[[0,'#020605'],[.08,p.wall],[.5,'#0a100d'],[.92,p.wall],[1,'#020605']]);ctx.fillRect(23,54,w-46,h-86);
    if(grain){ctx.save();ctx.globalAlpha=.7;ctx.fillStyle=ctx.createPattern(grain,'repeat');ctx.fillRect(25,54,w-50,h-86);ctx.restore();}
    for(let row=0;row<rows;row++){
      const top=62+row*pitch,bottom=62+(row+1)*pitch;
      const shade=gradient(ctx,0,top,0,bottom,[[0,'#000d'],[.22,'#0000'],[.75,'#0003'],[1,'#000c']]);ctx.fillStyle=shade;ctx.fillRect(25,top,w-50,pitch);
      ctx.save();ctx.beginPath();ctx.rect(25,top,w-50,pitch);ctx.clip();
      for(let col=0;col<columns;col++){
        const cx=inset+col*(coverW+gap)+coverW/2;
        // Broad concealed lighting, with no visible oval fixture or bulb.
        const glow=ctx.createRadialGradient(cx,top-8,3,cx,top+23,coverW*.85);glow.addColorStop(0,`rgba(${p.glow},.65)`);glow.addColorStop(.22,`rgba(${p.glow},.26)`);glow.addColorStop(.68,`rgba(${p.glow},.055)`);glow.addColorStop(1,`rgba(${p.glow},0)`);ctx.fillStyle=glow;ctx.fillRect(cx-coverW,top,coverW*2,pitch*.7);
      }
      ctx.restore();
      // Glass inset catches the cover reflections without a bright painted base.
      ctx.fillStyle=gradient(ctx,0,bottom-42,0,bottom-13,[[0,p.light+'00'],[.55,p.mid+'20'],[1,p.light+'44']]);ctx.fillRect(26,bottom-42,w-52,29);
      if(row<rows-1)ledge(ctx,w,bottom-11,p,false);
    }
    // Subtle machining marks, below all metal and artwork.
    ctx.fillStyle='#ffffff03';for(let i=0;i<120;i++){const x=27+(i*179)%Math.max(1,w-54),y=61+(i*71)%Math.max(1,h-100);ctx.fillRect(x,y,1,1);}
    const vignette=gradient(ctx,25,0,w-25,0,[[0,'#0009'],[.05,'#0000'],[.95,'#0000'],[1,'#0009']]);ctx.fillStyle=vignette;ctx.fillRect(25,55,w-50,h-87);
  }
  function foreground(ctx,w,h,p,rows,pitch,emerald){
    bevelRing(ctx,w,h,p);
    // The cornice and glass floor have depth, but belong to the same casting.
    polygon(ctx,[[5,36],[w-5,36],[w-14,43],[14,43]],gradient(ctx,0,35,0,43,[[0,'#15231d'],[.3,p.mid],[.55,'#17241c'],[.85,p.dark],[1,p.light]]),p.mid,.6);
    ctx.fillStyle=gradient(ctx,0,43,0,47,[[0,p.shine],[.3,p.mid],[.6,p.dark],[1,p.shade]]);ctx.fillRect(6,43,w-12,4);
    // Intermediate floors repeat only the shelf; the side rails stay continuous.
    for(let r=1;r<rows;r++)ledge(ctx,w,62+r*pitch-11,p,false);
    ledge(ctx,w,h-22,p,true);
    const ornament=emerald?palettes.gold:p;
    upperCorner(ctx,24,54,1,ornament,emerald);upperCorner(ctx,w-24,54,-1,ornament,emerald);
    lowerCorner(ctx,25,h-34,1,ornament,emerald);lowerCorner(ctx,w-25,h-34,-1,ornament,emerald);
    if(emerald){gem(ctx,20,54+(h-88)*.5,8);gem(ctx,w-20,54+(h-88)*.5,8);}
    plaque(ctx,w,p,emerald);
  }

  const topPadding=20;
  function metrics(width,coverWidth,count,inset=44){
    const columns=Math.max(1,Math.floor((Math.max(0,width-inset*2)+18)/(coverWidth+18)));
    const rows=Math.max(1,Math.ceil(count/columns));
    const gap=columns>1?Math.min(32,Math.max(18,(width-inset*2-columns*coverWidth)/(columns-1))):18;
    const pitch=coverWidth*1.5+57;
    return {columns,rows,gap,pitch,inset,topPadding,height:topPadding+62+rows*pitch+21};
  }
  function draw(back,front,width,coverWidth,count,emerald,inset=44){
    const isDiamond=emerald==='base-dlc-pv';
    if(typeof emerald==='string'&&!isDiamond)emerald=emerald==='base-dlc';
    const m=metrics(width,coverWidth,count,inset),p=palettes[isDiamond?'diamond':emerald?'emerald':'gold'];
    if(isDiamond){m.height+=30;m.topPadding+=30;}
    if(isDiamond){
      const b=setup(back,width,m.height),f=setup(front,width,m.height);
      b.translate(0,50);background(b,width,m.height-50,p,m.rows,m.pitch,coverWidth,m.columns,m.gap,inset);
      diamond.foreground(f,width,m.height,m.rows,m.pitch);return m;
    }
    const b=setup(back,width,m.height),f=setup(front,width,m.height);
    b.translate(0,topPadding);f.translate(0,topPadding);
    // Keep the gem and its bezel inside the actual canvas, including on phones.
    background(b,width,m.height-topPadding,p,m.rows,m.pitch,coverWidth,m.columns,m.gap,inset);
    foreground(f,width,m.height-topPadding,p,m.rows,m.pitch,emerald);
    return m;
  }
  function ornament(ctx,tone,part){
    if(tone==='diamond'){diamond.ornament(ctx,part);return;}
    const emerald=tone==='emerald',p=palettes[tone];
    if(part==='upper')upperCorner(ctx,0,0,1,emerald?palettes.gold:p,emerald);
    if(part==='lower')lowerCorner(ctx,0,0,1,emerald?palettes.gold:p,emerald);
    if(part==='header')plaque(ctx,512,p,emerald);
    if(part==='side')gem(ctx,0,0,8);
  }
  const api={metrics,draw,ornament,topPadding};
  if(typeof module!=='undefined'&&module.exports)module.exports=api;else root.PixelVaultCaseArtwork=api;
})(typeof globalThis!=='undefined'?globalThis:this);
