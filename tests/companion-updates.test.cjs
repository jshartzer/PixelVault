const {test}=require('node:test');const assert=require('node:assert/strict');const updates=require('../src/PixelVault.Companion/wwwroot/updates.js');
const tracker=(id,version)=>({id,title:'Tracker '+id,gameTitle:'Test game',updateVersion:version});
test('first visit establishes a baseline without claiming every tracker was just updated',()=>{
  assert.deepEqual(updates.changed(null,[tracker('a','v1')]),[]);assert.deepEqual(updates.snapshot([tracker('a','v1')]),{a:{version:'v1',progress:null,complete:null}});
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
test('reading two interviews shows the same gain for the catalogue and standalone achievement counter',()=>{
  const before=[{...tracker('catalogue','v1'),updateProgress:[{id:'one',current:0},{id:'two',current:0},{id:'three',current:1}]},
    {...tracker('counter','v1'),updateProgress:[{id:'interviews-read',current:69}]}];
  const after=[{...before[0],updateVersion:'v2',updateProgress:[{id:'one',current:1},{id:'two',current:1},{id:'three',current:1}]},
    {...before[1],updateVersion:'v2',updateProgress:[{id:'interviews-read',current:71}]}];
  assert.deepEqual(updates.summary(updates.snapshot(before),after).updated.map(t=>t.increased),[2,2]);
});
test('standalone counters report gains beyond the achievement goal, zero for no gain, and no invented unknown baseline',()=>{
  const before={...tracker('a','v1'),updateProgress:[{id:'count',current:99}]};
  const gain=current=>updates.summary(updates.snapshot([before]),[{...before,updateVersion:'v2',updateProgress:[{id:'count',current}]}]).updated[0].increased;
  assert.equal(gain(101),2);assert.equal(gain(99),0);assert.equal(gain(98),0);assert.equal(gain(null),null);
  const unknown={...before,updateProgress:[{id:'count',current:null}]};
  assert.equal(updates.summary(updates.snapshot([unknown]),[{...before,updateVersion:'v2'}]).updated[0].increased,null);
  const grouped={...before,updateProgress:[{id:'count',groupId:'record',current:1}]};
  assert.equal(updates.summary(updates.snapshot([grouped]),[{...grouped,updateVersion:'v2',updateProgress:[{id:'count',groupId:'record',current:3}]}]).updated[0].increased,1);
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

test('achievement-linked trackers at or beyond the unlock threshold are omitted while catalogue additions remain visible',()=>{
  const catalogue={...tracker('catalogue','v2'),achievementId:null,totals:{completed:140,total:240},updateProgress:[{id:'mail',current:1}]};
  const mailGoal={...tracker('goal','v2'),achievementId:'038',totals:{completed:100,total:100},updateProgress:[{id:'read',current:140}]};
  const seen={catalogue:{version:'v1',progress:[{id:'mail',current:0}]},goal:{version:'v1',progress:[{id:'read',current:139}]}};
  const summary=updates.summary(seen,[catalogue,mailGoal]);
  assert.deepEqual(summary.updated.map(({title,increased,completed})=>({title,increased,completed})),[{title:'Tracker catalogue',increased:1,completed:false}]);
  assert.equal(summary.message,'1 tracker changed.');
  assert.deepEqual(updates.changed(seen,[{...mailGoal,totals:{completed:140,total:100}}]),[]);
  // Suppressed trackers still receive an acknowledgement baseline, without changing their data.
  assert.equal(updates.snapshot([catalogue,mailGoal]).goal.progress[0].current,140);
});

test('a newly completed linked goal shows a completion message without a change count',()=>{
  const goal={...tracker('goal','v2'),achievementId:'038',totals:{completed:99,total:100}};
  const completed={...goal,updateVersion:'v3',totals:{completed:100,total:100}};
  const summary=updates.summary(updates.snapshot([goal]),[completed]);
  assert.equal(summary.title,'Tracker completed');
  assert.equal(summary.message,'');
  assert.equal(summary.updated[0].completed,true);
  assert.equal(summary.updated[0].increased,null);
  assert.deepEqual(updates.changed(updates.snapshot([completed]),[{...completed,updateVersion:'v4'}]),[]);
  const reopened=updates.summary(updates.snapshot([completed]),[{...goal,updateVersion:'v4'}]);
  assert.equal(reopened.updated[0].completed,false);
});

test('mixed results group editions by library game and retain regular gains',()=>{
  const first={...tracker('a','v1'),provider:'steam',gameId:'1',totals:{completed:1,total:2}};
  const second={...tracker('b','v1'),provider:'xbox',gameId:'2',updateProgress:[{id:'count',current:3}]};
  const third={...tracker('c','v1'),provider:'steam',gameId:'3'};
  const summary=updates.summary(updates.snapshot([second,first,third]),[
    {...second,updateVersion:'v2',updateProgress:[{id:'count',current:5}]},
    {...first,totals:{completed:2,total:2}},
    {...third,updateVersion:'v2'}
  ]);
  const games=[{provider:'steam',gameId:'1',groupId:'shared',title:'Same game'},{provider:'xbox',gameId:'2',groupId:'shared',title:'Same game'}];
  const groups=updates.groupByGame(summary.updated,games);
  assert.equal(summary.title,'Trackers updated');
  assert.equal(summary.message,'');
  assert.deepEqual(groups.map(g=>g.title),['Same game','Test game']);
  assert.deepEqual(groups[0].trackers.map(t=>[t.title,t.completed,t.increased]),[['Tracker a',true,null],['Tracker b',false,2]]);
});

test('completed unlinked catalogues still notify and missing or invalid goal totals never imply achievement completion',()=>{
  const complete={...tracker('a','v2'),totals:{completed:2,total:2}};
  for(const achievementId of [undefined,null,'','   '])assert.equal(updates.changed({a:'v1'},[{...complete,achievementId}]).length,1);
  for(const totals of [undefined,{completed:0,total:0},{completed:null,total:100},{completed:100,total:null},{completed:100,total:'100'},{completed:NaN,total:100}]){
    assert.equal(updates.changed({a:'v1'},[{...complete,achievementId:'038',totals}]).length,1);
  }
});

test('silenced completed goals do not suppress refresh errors, review or offline status',()=>{
  const goal={...tracker('a','v2'),achievementId:'038',totals:{completed:100,total:100}};
  const summary=updates.summary({a:'v1'},[goal],{status:'review',tasks:[{title:'Review save',status:'review'},{title:'Read failed',status:'failed'}]});
  assert.equal(summary.title,'Refresh finished');assert.deepEqual(summary.updated,[]);
  assert.deepEqual(summary.review,['Review save']);assert.deepEqual(summary.failed,['Read failed']);
  assert.equal(updates.summary({a:'v1'},[goal],{status:'offline'}).title,'PC unavailable');
});
