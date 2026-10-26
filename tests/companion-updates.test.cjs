const {test}=require('node:test');const assert=require('node:assert/strict');const updates=require('../src/PixelVault.Companion/wwwroot/updates.js');
const tracker=(id,version)=>({id,title:'Tracker '+id,gameTitle:'Test game',updateVersion:version});
test('first visit establishes a baseline without claiming every tracker was just updated',()=>{
  assert.deepEqual(updates.changed(null,[tracker('a','v1')]),[]);assert.deepEqual(updates.snapshot([tracker('a','v1')]),{a:{version:'v1',progress:null}});
});
test('revision changes are detected even when completion totals remain equal; unchanged trackers are omitted',()=>{
  const data=[tracker('a','v2'),tracker('b','v1'),tracker('c','v1')];
  const summary=updates.summary({a:'v1',b:'v1'},data);assert.deepEqual(summary.updated.map(t=>t.title),['Tracker a','Tracker c']);assert.equal(summary.message,'2 trackers changed.');
});
test('manual refresh with no semantic changes reports no changes rather than listing every re-read save',()=>{
  const summary=updates.summary({a:'v1'},[tracker('a','v1')],{status:'complete',tasks:[{title:'Tracker a',changed:100,status:'complete'}]});
  assert.equal(summary.title,'Up to date');assert.deepEqual(summary.updated,[]);
});
test('review and failed tasks remain clearly separate from successful updates, and offline is never called up to date',()=>{
  const summary=updates.summary({a:'v1'},[tracker('a','v2')],{status:'review',tasks:[{title:'Needs review',status:'review'},{title:'Could not read',status:'failed'}]});
  assert.equal(summary.title,'Trackers updated');assert.deepEqual(summary.review,['Needs review']);assert.deepEqual(summary.failed,['Could not read']);
  assert.equal(updates.summary({},[],{status:'offline'}).title,'PC unavailable');
});
test('increase badges count distinct items gaining progress rather than net totals or counter units',()=>{
  const before={...tracker('a','v1'),updateProgress:[{id:'one',current:0},{id:'two',current:3},{id:'three',current:1},{id:'unknown',current:null},{id:'zero',current:null}]};
  const after={...tracker('a','v2'),updateProgress:[{id:'one',current:1},{id:'two',current:20},{id:'three',current:0},{id:'unknown',current:1},{id:'zero',current:0},{id:'new',current:5}]};
  assert.equal(updates.summary(updates.snapshot([before]),[after]).updated[0].increased,3);
});
test('multiple milestone columns in one record count once and note-only or downward changes show zero',()=>{
  const before={...tracker('a','v1'),updateProgress:[{id:'a',groupId:'record',current:0},{id:'b',groupId:'record',current:0},{id:'c',current:3}]};
  const after={...tracker('a','v2'),updateProgress:[{id:'a',groupId:'record',current:1},{id:'b',groupId:'record',current:1},{id:'c',current:2}]};
  assert.equal(updates.summary(updates.snapshot([before]),[after]).updated[0].increased,1);
  assert.equal(updates.summary(updates.snapshot([after]),[{...after,updateVersion:'v3'}]).updated[0].increased,0);
  assert.equal(updates.summary(updates.snapshot([after]),[{...before,updateVersion:'v4'}]).updated[0].increased,1); // c rises, even while the record drops.
});
test('legacy acknowledgements and newly discovered trackers do not invent a numeric increase',()=>{
  const current={...tracker('a','v2'),updateProgress:[{id:'one',current:1}]};
  assert.equal(updates.summary({a:'v1'},[current]).updated[0].increased,null);
  assert.equal(updates.summary({},[current]).updated[0].increased,null);
  assert.deepEqual(updates.changed({a:'v2'},[current]),[]);
  assert.deepEqual(updates.changed(updates.snapshot([current]),[current]),[]);
});
