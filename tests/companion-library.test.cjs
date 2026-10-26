const {test}=require('node:test');const assert=require('node:assert/strict');const library=require('../src/PixelVault.Companion/wwwroot/library.js');
const game=(id,title,platform,date,extra={})=>({groupId:id,provider:platform.toLowerCase(),gameId:id,title,platform,trackerCount:0,stats:{lastScreenshotUtc:date},...extra});
test('one card combines console editions while keeping edition stats intact',()=>{
  const groups=library.groupGames([game('same','The game','Xbox',100),game('same','The game','Steam',200,{trackerCount:2,stats:{lastScreenshotUtc:200,achievementTotal:20,achievementUnlocked:7}}),game('sequel','The game 2','Xbox',300)]);
  assert.equal(groups.length,2);assert.equal(groups[0].editions.length,2);assert.deepEqual(groups[0].platforms,['Steam','Xbox']);assert.equal(groups[0].provider,'steam');assert.equal(groups[0].lastScreenshotUtc,200);assert.equal(groups[0].stats.achievementTotal,20);
});
test('newest screenshot is chronological across editions, missing dates last, title breaks ties',()=>{
  const groups=library.groupGames([game('a','Alpha','Steam',100),game('z','Zulu','Xbox',200),game('b','Beta','Xbox',null)]);
  assert.deepEqual(library.sortGames(groups,'screenshot-newest').map(g=>g.id),['z','a','b']);assert.deepEqual(library.sortGames(groups,'screenshot-oldest').map(g=>g.id),['a','z','b']);
});
test('pins stay above each sort without duplicate cards, and filters retain consolidated identity',()=>{
  const groups=library.groupGames([game('a','Alpha','Steam',100),game('z','Zulu','Xbox',200),game('b','Beta','Xbox',null)]);
  for(const sort of ['screenshot-newest','screenshot-oldest','title-az','title-za','last-played','playtime'])assert.equal(library.sortGames(groups,sort,['b'])[0].id,'b');
  assert.deepEqual(library.sortGames(groups,'title-za').map(g=>g.id),['z','b','a']);
});
test('completed trackers move after active trackers, preserving order and excluding empty totals',()=>{
  const rows=[{id:'done',totals:{completed:8,total:8}},{id:'empty',totals:{completed:0,total:0}},{id:'active',totals:{completed:3,total:8}},{id:'over',totals:{completed:9,total:8}}];
  const result=library.partition(rows,library.trackerComplete);
  assert.deepEqual(result.active.map(t=>t.id),['empty','active']);assert.deepEqual(result.completed.map(t=>t.id),['done','over']);assert.equal(rows[0].id,'done');
});
test('partial multi-column records remain active and undoing a check returns a record to active',()=>{
  const complete=[{complete:true},{complete:true}],partial=[{complete:true},{complete:false}],unknown=[{complete:false}];
  assert.deepEqual(library.partition([complete,partial,unknown],library.recordComplete),{active:[partial,unknown],completed:[complete]});
  complete[1].complete=false;assert.equal(library.partition([complete],library.recordComplete).completed.length,0);assert.equal(library.recordComplete([]),false);
});
test('tracker labels require a linked observed source except manual completion',()=>{
  const state={complete:false,availability:1,observedUtcTicks:123};
  assert.equal(library.trackerStatus([state],false),'');
  assert.equal(library.trackerStatus([{...state,observedUtcTicks:0}],true),'');
  assert.equal(library.trackerStatus([{...state,availability:0}],true),'');
  assert.equal(library.trackerStatus([state],true),'Available');
  assert.equal(library.trackerStatus([{...state,availability:2}],true),'Locked');
  assert.equal(library.trackerStatus([{complete:true,observedUtcTicks:0}],false),'Completed');
  assert.equal(library.trackerStatus([{complete:true},state],false),'');
});
