// Generate the desktop vector reliefs from the same drawing routines used by Companion.
// Run with Node after changing wwwroot/case-artwork.js; no browser or network is required.
const fs=require('node:fs'),path=require('node:path');
class DrawPath {
  constructor(d=''){this.d=d;}
  moveTo(x,y){this.d+=`M${x},${y} `;} lineTo(x,y){this.d+=`L${x},${y} `;}
  closePath(){this.d+='Z ';}
}
global.Path2D=DrawPath;
const artwork=require('../src/PixelVault.Companion/wwwroot/case-artwork.js');
function matrix(a,b){return [a[0]*b[0]+a[2]*b[1],a[1]*b[0]+a[3]*b[1],a[0]*b[2]+a[2]*b[3],a[1]*b[2]+a[3]*b[3],a[0]*b[4]+a[2]*b[5]+a[4],a[1]*b[4]+a[3]*b[5]+a[5]];}
class Recorder {
  constructor(){this.ops=[];this.stack=[];this.state={matrix:[1,0,0,1,0,0],fillStyle:'#000',strokeStyle:'#000',lineWidth:1};this.path=new DrawPath();}
  save(){this.stack.push({...this.state,matrix:[...this.state.matrix]});} restore(){this.state=this.stack.pop();}
  translate(x,y){this.state.matrix=matrix(this.state.matrix,[1,0,0,1,x,y]);}
  scale(x,y){this.state.matrix=matrix(this.state.matrix,[x,0,0,y,0,0]);}
  rotate(a){this.state.matrix=matrix(this.state.matrix,[Math.cos(a),Math.sin(a),-Math.sin(a),Math.cos(a),0,0]);}
  createLinearGradient(x1,y1,x2,y2){return {x1,y1,x2,y2,stops:[],addColorStop(at,color){this.stops.push([at,color]);}};}
  beginPath(){this.path=new DrawPath();} moveTo(x,y){this.path.moveTo(x,y);} lineTo(x,y){this.path.lineTo(x,y);} closePath(){this.path.closePath();}
  quadraticCurveTo(a,b,c,d){this.path.d+=`Q${a},${b} ${c},${d} `;}
  arc(x,y,r){this.ellipse(x,y,r,r,0);}
  strokeRect(x,y,w,h){const p=new DrawPath(`M${x},${y} h${w} v${h} h${-w} Z`);this.stroke(p);}
  ellipse(x,y,rx,ry,rot){const dx=rx*Math.cos(rot),dy=rx*Math.sin(rot);this.path.d+=`M${x+dx},${y+dy} A${rx},${ry} ${rot*180/Math.PI} 1 0 ${x-dx},${y-dy} A${rx},${ry} ${rot*180/Math.PI} 1 0 ${x+dx},${y+dy} Z `;}
  fill(p=this.path){this.ops.push({d:p.d,matrix:this.state.matrix,fill:this.state.fillStyle});}
  stroke(p=this.path){this.ops.push({d:p.d,matrix:this.state.matrix,stroke:this.state.strokeStyle,sw:this.state.lineWidth});}
}
for(const property of ['fillStyle','strokeStyle','lineWidth'])Object.defineProperty(Recorder.prototype,property,{get(){return this.state[property];},set(v){this.state[property]=v;}});
const parts={};
for(const tone of ['gold','emerald','diamond'])for(const part of ['upper','lower','header',...(tone!=='gold'?['side']:[]),...(tone==='diamond'?['cover']:[])]){
  const c=new Recorder();artwork.ornament(c,tone,part);parts[`${tone}-${part}`]=c.ops;
}
const output=path.resolve(__dirname,'../src/PixelVault.Native/UI/Library/CompletionCaseReliefs.json');
fs.writeFileSync(output,JSON.stringify(parts));
// Export the same retained cover geometry for browsers, with no inline script or external resources.
const escape=s=>String(s).replaceAll('&','&amp;').replaceAll('"','&quot;').replaceAll('<','&lt;');
let defs='',serial=0;
function paint(value){if(typeof value==='string')return value;const id='d'+serial++;defs+=`<linearGradient id="${id}" gradientUnits="userSpaceOnUse" x1="${value.x1}" y1="${value.y1}" x2="${value.x2}" y2="${value.y2}">${value.stops.map(([at,color])=>`<stop offset="${at}" stop-color="${color}"/>`).join('')}</linearGradient>`;return `url(#${id})`;}
const paths=parts['diamond-cover'].map(op=>`<path d="${escape(op.d)}" transform="matrix(${op.matrix.join(' ')})" fill="${op.fill?paint(op.fill):'none'}"${op.stroke?` stroke="${paint(op.stroke)}" stroke-width="${op.sw}"`:''}/>`).join('');
fs.writeFileSync(path.resolve(__dirname,'../src/PixelVault.Companion/wwwroot/completion-frames/diamond-cover.svg'),`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 252 392" preserveAspectRatio="none"><defs>${defs}</defs>${paths}<text x="207" y="363" text-anchor="middle" fill="#e3f6ff" font-family="Georgia,serif" font-size="21" font-weight="600">100%</text></svg>`);
console.log(`Generated ${Object.keys(parts).length} shared vector reliefs (${fs.statSync(output).size} bytes).`);
