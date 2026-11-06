const {test}=require('node:test'),assert=require('node:assert/strict');
const maps=require('../src/PixelVault.Companion/wwwroot/maps.js');
test('map image coordinates retain top-left origin and aspect ratio at native zoom',()=>{
 const g=maps.geometry({width:1025,height:513,maxZoom:2});
 assert.deepEqual(g.bounds,[[-128.25,0],[0,256.25]]);
 assert.deepEqual(g.point(0,0),[-0,0]);assert.deepEqual(g.point(1,1),[-128.25,256.25]);assert.deepEqual(g.point(.5,.5),[-64.125,128.125]);
 assert.equal(maps.mapHash('library','a b','floor/1'),'#map/library/a%20b/floor%2F1');
 assert.equal(maps.validManifest({width:1025,height:513,maxZoom:2,tileSize:512,tileUrl:'/api/v1/maps/steam/42/id/tiles/{z}/{x}/{y}?v=x'}),true);
 for(const tileUrl of ['https://external.test/{z}','/api/v1/maps/../secret'])assert.equal(maps.validManifest({width:1,height:1,maxZoom:0,tileSize:512,tileUrl}),false);
});
test('pin filters never hide unknown progress as completed and search descriptions without changing numbering',()=>{
 const pins=['completed','remaining','unknown','unavailable','unlinked'].map((id,i)=>({id,label:'#'+String(i+1).padStart(2,'0'),detail:i===2?'By the northern terminal':'A landmark',trackerId:id==='unlinked'?null:'tracker',itemId:id}));
 const progress=new Map(pins.map(p=>[p.id,{status:p.id}]));
 assert.deepEqual(maps.visiblePins(pins,progress,'','completed').map(p=>p.id),['completed']);
 assert.deepEqual(maps.visiblePins(pins,progress,'','remaining').map(p=>p.id),['remaining','unknown','unavailable','unlinked']);
 assert.deepEqual(maps.visiblePins(pins,progress,' NORTHERN ','remaining').map(p=>p.id),['unknown']);
 assert.equal(maps.visiblePins(pins,progress,'northern','completed').length,0);
 assert.equal(maps.shortLabel(pins[2],0),'03');assert.equal(maps.shortLabel({label:'A place'},3),'4');
 assert.equal(maps.pinState(pins[0],{status:'toString'}),'unavailable');
 assert.equal(maps.visiblePins(pins,new Map(),'','remaining').length,5);
 assert.equal(maps.visiblePins(pins,new Map(),'','completed').length,0);
});
