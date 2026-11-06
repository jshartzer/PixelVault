// Approved cut-crystal / platinum diamond tier. Drawing only; never writes game progress.
(function(root){'use strict';
const settings={blue:70};
function grad(c,x,y,x2,y2,stops){const g=c.createLinearGradient(x,y,x2,y2);stops.forEach(([p,v])=>g.addColorStop(p,v));return g;}
function poly(c,pts,fill,line=null,lw=1){c.beginPath();pts.forEach(([x,y],i)=>i?c.lineTo(x,y):c.moveTo(x,y));c.closePath();if(fill){c.fillStyle=fill;c.fill();}if(line){c.strokeStyle=line;c.lineWidth=lw;c.stroke();}}
function setup(canvas,w,h){const d=Math.min(devicePixelRatio||1,2,16380/h,16380/w,Math.sqrt(16000000/(w*h)));canvas.width=Math.round(w*d);canvas.height=Math.round(h*d);const c=canvas.getContext('2d');c.scale(d,d);return c;}
function silver(c,x,y,x2,y2){return grad(c,x,y,x2,y2,[[0,'#23364b'],[.16,'#d8e7f2'],[.29,'#fff'],[.4,'#6d8da9'],[.52,'#1c3148'],[.72,'#c3e5fb'],[.84,'#f5fcff'],[1,'#456481']]);}
function line(c,pts,brush,lw=1){c.beginPath();pts.forEach(([x,y],i)=>i?c.lineTo(x,y):c.moveTo(x,y));c.strokeStyle=brush;c.lineWidth=lw;c.stroke();}
function facet(c,pts,index=0){
 c.save();c.shadowColor='#020a13';c.shadowBlur=4;c.shadowOffsetY=2;
 const xs=pts.map(p=>p[0]),ys=pts.map(p=>p[1]),x=Math.min(...xs),y=Math.min(...ys),w=Math.max(...xs)-x,h=Math.max(...ys)-y;
 poly(c,pts,grad(c,x,y,x+w,y+h,[[0,'#f5fdffff'],[.2,'#b3d6facc'],[.43,'#32659399'],[.58,'#d5efffff'],[.7,'#fff'],[1,'#426d9fb0']]),'#ccecff',.7);c.shadowBlur=0;
 const center=[xs.reduce((a,b)=>a+b,0)/pts.length,ys.reduce((a,b)=>a+b,0)/pts.length];
 const colors=['#f5fcffb8','#4b86beaa','#d0edffbd','#0b3b6a99','#92cfff88','#ffffffb8'];
 for(let i=0;i<pts.length;i++)poly(c,[pts[i],pts[(i+1)%pts.length],center],colors[(i+index)%colors.length],'#e2f3ff44',.4);
 line(c,[pts[0],pts[1]],'#fff',1);c.restore();
}
function brilliant(c,x,y,r){
 c.save();c.translate(x,y);c.shadowColor='#65bcff';c.shadowBlur=10*(settings.blue/100);c.fillStyle='#93d7ff';c.beginPath();c.arc(0,0,r,0,Math.PI*2);c.fill();c.shadowBlur=0;
 const ring=(radius,n,rotation=0)=>Array.from({length:n},(_,i)=>[Math.cos(i*2*Math.PI/n+rotation)*radius,Math.sin(i*2*Math.PI/n+rotation)*radius]);
 const outer=ring(r,16,Math.PI/16),inner=ring(r*.54,8,Math.PI/8);
 poly(c,ring(r+2.5,16,Math.PI/16),silver(c,-r,-r,r,r),'#152839',1);
 for(let i=0;i<16;i++){const a=outer[i],b=outer[(i+1)%16],m=inner[Math.floor(i/2)%8];poly(c,[a,b,m],i%4===0?'#fff':i%4===1?'#73b5ed':i%4===2?'#d7edff':'#315984','#d8efffaa',.55);}
 for(let i=0;i<8;i++)poly(c,[inner[i],inner[(i+1)%8],[0,0]],['#f8fdff','#a4d5fb','#416ba5','#f0f9ff'][i%4],'#e8f6ff99',.5);
 poly(c,ring(r*.28,8,Math.PI/8),grad(c,-r*.3,-r*.3,r*.3,r*.3,[[0,'#fff'],[.46,'#88c7fb'],[1,'#e2f6ff']]),'#fff',.7);
 line(c,[[-r*.8,-r*.45],[-r*.25,-r*.75],[r*.2,-r*.55]],'#ffffffdd',1.1);c.restore();
}
function fan(c,x,y,flip=1,scale=1){
 c.save();c.translate(x,y);c.scale(flip*scale,scale);
 // Cut crystal blades and fluted platinum replace botanical scrollwork.
 poly(c,[[0,0],[55,0],[9,12],[0,55]],silver(c,0,0,30,40),'#dff3ff',.7);
 facet(c,[[4,5],[53,5],[29,15],[9,31]],0);
 facet(c,[[6,8],[29,18],[16,32],[6,52]],2);
 facet(c,[[11,12],[42,9],[25,22]],1);
 line(c,[[0,59],[4,16],[16,4],[59,0]],'#589cd7',1.2);
 c.restore();
}
function gemMount(c,x,y,rotation=0){
 c.save();c.translate(x,y);c.rotate(rotation);
 // Opposing stepped ribbons follow the outer rails, clear of the image aperture.
 const claws=[['M-3 11 L14 11 L26 8 L53 8 L63 16 L29 16 L16 22 L-3 22Z',-3,8,63,22],['M-16 1 L-16 -21 L-20 -31 L-20 -53 L-13 -64 L-13 -34 L-9 -23 L-9 1Z',-20,-64,-9,1]];
 for(const [d,x1,y1,x2,y2] of claws){const p=new Path2D(d);c.shadowColor='#000b';c.shadowBlur=3;c.shadowOffsetY=2;c.fillStyle=silver(c,x1,y1,x2,y2);c.fill(p);c.shadowBlur=0;c.strokeStyle='#e7f7ff';c.lineWidth=.65;c.stroke(p);}
 line(c,[[5,15],[17,15],[28,12],[52,12]],'#79baf2',.8);
 line(c,[[-12,-12],[-12,-22],[-17,-33],[-17,-50]],'#92c8f0',.7);
 brilliant(c,0,0,17);c.restore();
}
function rails(c,w,top,bottom,thin=false){
 const l=thin?9:14,r=w-l,inside=thin?15:25;
 poly(c,[[l,top],[r,top],[r-11,top+11],[l+11,top+11]],silver(c,0,top,0,top+11));
 poly(c,[[r,top],[r,bottom],[r-11,bottom-11],[r-11,top+11]],silver(c,r-11,0,r,0));
 poly(c,[[r,bottom],[l,bottom],[l+11,bottom-11],[r-11,bottom-11]],silver(c,0,bottom-11,0,bottom));
 poly(c,[[l,bottom],[l,top],[l+11,top+11],[l+11,bottom-11]],silver(c,l,0,l+11,0));
 c.save();c.strokeStyle='#8fd2ff';c.lineWidth=1.2;c.shadowColor='#3c9eff';c.shadowBlur=settings.blue/13;c.strokeRect(l+5,top+5,w-2*l-10,bottom-top-10);c.restore();
 // Alternating clear faces sit over the metal substrate; blue stays inside the cuts.
 for(let side=0;side<2;side++){
  const x=side?r-8:l+2;
  for(let y=top+48,i=0;y<bottom-45;y+=62,i++){
   const end=Math.min(y+62,bottom-36);poly(c,[[x,y],[x+6,y+17],[x+6,end],[x,end-14]],grad(c,x,y,x+6,end,[[0,'#ffffffbb'],[.3,'#92bde14d'],[.54,'#12253c55'],[.8,'#c9eaffbb'],[1,'#64b7ff80']]),'#d1ecff66',.5);
  }
 }
 c.strokeStyle='#d7efff88';c.lineWidth=.6;c.strokeRect(l,top,w-l*2,bottom-top);
 if(!thin){c.strokeStyle='#c9e9ffb0';c.lineWidth=.75;c.strokeRect(l+15,top+15,w-2*l-30,bottom-top-30);}
}
function plaque(c,w){
 const pw=Math.min(360,w-62),x=(w-pw)/2,y=40,h=53;
 const outer=[[x+17,y],[x+pw-17,y],[x+pw,y+h/2],[x+pw-17,y+h],[x+17,y+h],[x,y+h/2]];
 poly(c,outer,silver(c,x,y,x,y+h),'#edfbff',.8);
 poly(c,[[x+20,y+5],[x+pw-20,y+5],[x+pw-7,y+h/2],[x+pw-20,y+h-5],[x+20,y+h-5],[x+7,y+h/2]],grad(c,x,y,x,y+h,[[0,'#21374c'],[.5,'#071624'],[1,'#172b3d']]),'#7abce7',.8);
 // An independent hairline traces the inside of the dark plaque face.
 poly(c,[[x+24,y+9],[x+pw-24,y+9],[x+pw-13,y+h/2],[x+pw-24,y+h-9],[x+24,y+h-9],[x+13,y+h/2]],null,'#caeaff',.75);
 c.save();c.translate(x+1,y+4);c.scale(.7,.5);fan(c,0,0,-1,.7);c.restore();
 c.save();c.translate(x+pw-1,y+4);c.scale(.7,.5);fan(c,0,0,1,.7);c.restore();
 // A top-view brilliant cut is circular and multifaceted, never a rhombus.
 brilliant(c,w/2,21,15);
 for(const dx of [-20,20])poly(c,[[w/2+dx-2,28],[w/2+dx+2,28],[w/2+dx+1,40],[w/2+dx-1,40]],silver(c,0,28,0,40));
}
function shelf(c,w,y,last=false){
 const edge=20;
 poly(c,[[29,y-29],[w-29,y-29],[w-edge,y],[edge,y]],grad(c,0,y-29,0,y,[[0,'#d0eeff05'],[.5,'#5eb4f022'],[1,'#caedff55']]),'#b0ddff55',.5);
 poly(c,[[edge,y],[w-edge,y],[w-edge,y+8],[edge,y+8]],silver(c,0,y,0,y+10));
 line(c,[[edge,y],[w-edge,y]],'#c9f0ff',1);line(c,[[edge,y+5],[w-edge,y+5]],'#499ee7',1);
}

function cover(c){
rails(c,252,20,373,true);
 fan(c,10,21,1,.67);
 c.save();c.translate(242,372);c.scale(1,-1);fan(c,0,0,-1,.6);c.restore();
 gemMount(c,229,29,Math.PI);gemMount(c,22,363,0);
 facet(c,[[181,331],[232,331],[249,355],[232,379],[181,379],[165,355]],0);
 poly(c,[[185,338],[227,338],[239,355],[227,372],[185,372],[175,355]],grad(c,0,338,0,372,[[0,'#254c6a'],[.45,'#0a2238'],[1,'#193d5b']]),'#b8e5ff',.8);
}
function foreground(c,w,h,rows,pitch){
 for(let row=1;row<rows;row++)shelf(c,w,112+row*pitch-11);
 shelf(c,w,h-22,true);rails(c,w,76,h-22);
 if(rows>1){const y=112+Math.ceil((rows-1)/2)*pitch-7;brilliant(c,20,y,7);brilliant(c,w-20,y,7);}
 fan(c,23,86,1,.9);fan(c,w-23,86,-1,.9);
 c.save();c.translate(22,h-31);c.scale(1,-1);fan(c,0,0,1,.9);c.restore();
 c.save();c.translate(w-22,h-31);c.scale(1,-1);fan(c,0,0,-1,.9);c.restore();plaque(c,w);
}
function ornament(c,part){
 if(part==='upper')fan(c,0,0,1,.9);
 if(part==='lower'){c.save();c.scale(1,-1);fan(c,0,0,1,.9);c.restore();}
 if(part==='header')plaque(c,422);
 if(part==='side')brilliant(c,0,0,7);
 if(part==='cover')cover(c);
}
const api={foreground,ornament};
if(typeof module!=='undefined'&&module.exports)module.exports=api;else root.PixelVaultDiamondArtwork=api;
})(typeof globalThis!=='undefined'?globalThis:this);
