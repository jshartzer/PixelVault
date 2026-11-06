const {test}=require('node:test');const assert=require('node:assert/strict');const library=require('../src/PixelVault.Companion/wwwroot/library.js');
const game=(id,title,platform,date,extra={})=>({groupId:id,provider:platform.toLowerCase(),gameId:id,title,platform,trackerCount:0,stats:{lastScreenshotUtc:date},...extra});
test('linked Xbox editions retain old pins and use actual play dates without combining unlocks',()=>{
  const steam=game('native','Game 2','Steam',0,{titleGroupId:'old-steam',stats:{lastPlayedUtc:20,achievementUnlocked:2,achievementTotal:10}});
  const xbox=game('native','Game II','Xbox',0,{titleGroupId:'old-xbox',stats:{lastPlayedUtc:50,achievementUnlocked:8,achievementTotal:10}});
  const groups=library.groupGames([steam,xbox,game('other','Other','Steam',0,{stats:{lastPlayedUtc:40}})]);
  const linked=groups.find(g=>g.id==='native');
  assert.equal(linked.lastPlayedUtc,50);assert.equal(linked.editions[0].stats.achievementUnlocked,2);assert.equal(linked.editions[1].stats.achievementUnlocked,8);
  assert.equal(library.isPinned(linked,['old-xbox']),true);
  assert.equal(library.sortGames(groups,'title-za',['old-xbox'])[0].id,'native');
  assert.equal(library.sortGames(groups,'last-played')[0].id,'native');
});
test('completion stays within an edition and unknown or empty progress does not create an award',()=>{
  const base=game('a','Game','Steam',0,{completionTier:'base'}),dlc=game('a','Game','Xbox',0,{completionTier:'none'});
  assert.equal(library.completionTier(library.groupGames([base,dlc])[0]),'base');
  assert.equal(library.completionTier(dlc),'none');
  assert.equal(library.completionTier({...base,completionTier:'base-dlc'}),'base-dlc');
  assert.equal(library.completionDecoration('none'),'');
  assert.match(library.completionDecoration('base-dlc'),/Base and DLC achievements complete/);
  assert.equal(library.completionTier({editions:[base,{...base,completionTier:'base-dlc-pv'}]}),'base-dlc-pv');
  assert.match(library.completionDecoration('base-dlc-pv'),/completion-foil/);
  assert.match(library.completionDecoration('base'),/100%/);
});
test('only diamond has cover foil and all earned tiers retain the same frame and corner clasp structure',()=>{
  for(const tier of ['base','base-dlc','base-dlc-pv']){
    const markup=library.completionDecoration(tier);
    assert.match(markup,/completion-frame/);assert.match(markup,/completion-clasp/);
    assert.equal((markup.match(/completion-ornament /g)||[]).length,4);
    assert.equal(markup.includes('completion-foil'),tier==='base-dlc-pv');
    assert.doesNotMatch(markup,/completion-diamond-art|<button|<a /);
  }
  assert.equal(library.completionDecoration('unknown'),'');
});
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

test('Century tracker sections follow achievement links and retain completed groups independently',()=>{
  const rows=[{id:'century-done',title:'Zed',achievementId:'century.death-stranding-dc.all-roads',totals:{completed:51,total:51}},
    {id:'official',title:'Century in the title',achievementId:'039',totals:{completed:100,total:100}},
    {id:'unlinked',title:'Personal notes',totals:{completed:0,total:0}},
    {id:'century-active',title:'Alpha',achievementId:'century.death-stranding-dc.all-interviews',totals:{completed:69,total:149}}];
  const before=JSON.stringify(rows),groups=library.trackerSections(rows);
  assert.deepEqual(groups.map(g=>g.title),['Achievement-linked trackers','Manual trackers','Centurian Achievements']);
  assert.deepEqual(groups.map(g=>g.count),[1,1,2]);
  assert.deepEqual(groups[1].active.map(t=>t.id),['unlinked']);assert.deepEqual(groups[0].completed.map(t=>t.id),['official']);
  assert.deepEqual(groups[2].active.map(t=>t.id),['century-active']);assert.deepEqual(groups[2].completed.map(t=>t.id),['century-done']);
  assert.equal(JSON.stringify(rows),before);
  rows[0]={...rows[0],achievementId:'049'};
  assert.equal(library.trackerSections(rows)[2].count,1,'Relinking moves the card without transferring progress');
  assert.deepEqual(library.trackerSections(rows,['century','manual','achievement']).map(g=>g.id),['century','manual','achievement']);
  assert.deepEqual(library.trackerSections([]),[]);
  assert.deepEqual(library.trackerSections([rows[3]]).map(g=>g.id),['century']);
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

test('cover tracker percentage combines individual item totals rather than counting completed trackers',()=>{
  const edition=game('42','Game','Steam',0,{stats:{achievementUnlocked:48,achievementTotal:63}});
  const trackers=[{provider:'steam',gameId:'42',totals:{completed:100,total:100}},{provider:'steam',gameId:'42',totals:{completed:1,total:2}},
    {provider:'steam',gameId:'42',totals:{completed:9,total:8}},{provider:'steam',gameId:'42',totals:{completed:0,total:0}},
    {provider:'steam',gameId:'other',totals:{completed:2,total:2}}];
  assert.deepEqual(library.coverProgress(edition,trackers),{achievements:{completed:48,total:63,percent:76},trackers:{completed:109,total:110,percent:99}});
  assert.match(library.coverProgressBadges(edition,trackers),/Tracker items: 109 of 110 completed \(99%\) · 1 remaining/);
});

test('cover progress combines editions and selected-platform progress stays within that edition',()=>{
  const steam=game('42','Game','Steam',0,{stats:{achievementUnlocked:10,achievementTotal:20}});
  const xbox=game('42','Game','Xbox',0,{stats:{achievementUnlocked:20,achievementTotal:80}});
  const trackers=[{provider:'steam',gameId:'42',totals:{completed:1,total:1}},{provider:'xbox',gameId:'42',totals:{completed:0,total:1}}];
  const combined=library.coverProgress(library.groupGames([steam,xbox])[0],trackers);
  assert.deepEqual(combined.achievements,{completed:30,total:100,percent:30});assert.equal(combined.trackers.percent,50);
  assert.equal(library.coverProgress(steam,trackers).achievements.percent,50);assert.equal(library.coverProgress(steam,trackers).trackers.percent,100);
  assert.equal(library.coverProgress(xbox,trackers).trackers.percent,0);
});

test('a cumulative weight goal contributes one item while retaining its kilogram counter',()=>{
  const edition=game('42','Game','Steam',0);
  const weight={provider:'steam',gameId:'42',totals:{completed:16503,total:50000,aggregateCompleted:0,aggregateTotal:1}};
  const checklist={provider:'steam',gameId:'42',totals:{completed:2,total:2}};
  assert.deepEqual(library.coverProgress(edition,[weight,checklist]).trackers,{completed:2,total:3,percent:67});
  assert.equal(library.trackerComplete(weight),false);
  weight.totals.completed=50000;weight.totals.aggregateCompleted=1;
  assert.deepEqual(library.coverProgress(edition,[weight,checklist]).trackers,{completed:3,total:3,percent:100});
  assert.equal(library.trackerComplete(weight),true);
});

test('cover progress preserves unknown and zero states and never rounds unfinished achievements to 100%',()=>{
  const edition=game('42','Game','Steam',0);
  assert.equal(library.coverProgress(edition).achievements.percent,null);assert.equal(library.coverProgress(edition).trackers.percent,null);
  for(const stats of [{achievementUnlocked:null,achievementTotal:50},{achievementUnlocked:0,achievementTotal:0}])assert.equal(library.coverProgress({...edition,stats}).achievements.percent,null);
  assert.equal(library.coverProgress({...edition,stats:{achievementUnlocked:0,achievementTotal:50}}).achievements.percent,0);
  assert.equal(library.coverProgress({...edition,stats:{achievementUnlocked:999,achievementTotal:1000}}).achievements.percent,99);
  assert.equal(library.coverProgress({...edition,stats:{achievementUnlocked:50,achievementTotal:50}}).achievements.percent,100);
  assert.equal(library.coverProgress({...edition,completionEarned:3,completionTotal:4}).achievements.percent,75);
});

test('cover badges describe progress accessibly and use the approved local symbols without inline styles',()=>{
  const unknown=library.coverProgressBadges(game('42','Game','Steam',0));
  assert.match(unknown,/Achievements: no saved progress/);assert.match(unknown,/Tracker items: no saved progress/);assert.match(unknown,/>—<\/strong>/);
  const markup=library.coverProgressBadges(game('42','Game','Steam',0,{stats:{achievementUnlocked:12,achievementTotal:25}}));
  assert.match(markup,/Achievements: 12 of 25 obtained \(48%\)/);assert.match(markup,/48<small>%<\/small>/);
  assert.match(markup,/\/cover-progress-achievements.svg/);assert.match(markup,/\/cover-progress-trackers.svg/);assert.doesNotMatch(markup,/style=|tabindex=|cover-tracker-count/);
});


test('split originals and remakes use server edition identities and do not duplicate a historic pin',()=>{
  const remake={...game('1693980','Dead Space','Steam',0),groupId:'retained',titleGroupId:'retained'};
  const modern={...game('2008403666','Dead Space','Xbox',0),provider:'xbox',groupId:'retained',titleGroupId:'retained'};
  const original={...game('1161889879','Dead Space','Xbox 360',0),provider:'xbox',groupId:'original',titleGroupId:'original'};
  const groups=library.groupGames([remake,modern,original]);
  assert.equal(groups.length,2);
  assert.deepEqual(groups.find(g=>g.id==='retained').platforms,['Steam','Xbox']);
  assert.equal(groups.filter(g=>library.isPinned(g,['retained'])).length,1);
  assert.equal(library.isPinned(groups.find(g=>g.id==='original'),['retained']),false);
});


test('capture profiles resolve the same achievement edition as their detail link without borrowing progress across providers',()=>{
  const capture={provider:'library',gameId:'G1',platform:'Xbox 360',stats:{captures:2}};
  const xbox={provider:'xbox',gameId:'1480657456',platform:'Xbox 360',stats:{achievementUnlocked:4,achievementTotal:30}};
  const steam={provider:'steam',gameId:'238460',platform:'Steam',stats:{achievementUnlocked:null,achievementTotal:null}};
  assert.equal(library.achievementEdition(capture,[capture,steam,xbox]),xbox);
  assert.equal(library.achievementEdition(steam,[capture,steam,xbox]),steam);
  assert.equal(library.achievementEdition(capture,[capture]),null);
  assert.equal(library.achievementEdition(capture,[capture,xbox,{...xbox,gameId:'other'}]),null);
  assert.equal(library.achievementEdition({...capture,platform:'PC'},[capture,steam,xbox]),null);
  assert.equal(library.achievementEdition(capture,[capture,{...xbox,stats:{achievementUnlocked:null,achievementTotal:30}}]).stats.achievementUnlocked,null);
});


test('reunited console editions retain pins saved while their cards were split',()=>{
 const steam={...game('1850570',"Death Stranding Director's Cut",'Steam',1),groupId:'joined',editionGroupId:'old-steam'};
 const capture={...game('G20',"Death Stranding Director's Cut",'PS5',0),provider:'library',groupId:'joined',editionGroupId:'old-ps5'};
 const group=library.groupGames([capture,steam])[0];assert.equal(group.provider,'steam');
 assert.equal(library.isPinned(group,['old-steam']),true);assert.equal(library.isPinned(group,['old-ps5']),true);
});


test('platform selector combines capture and provider records and preserves console-specific presentation',()=>{
 const capture={provider:'library',gameId:'G1',platform:'Xbox',coverUrl:'/cover',bannerUrl:'/banner',stats:{captures:8}};
 const xbox={provider:'xbox',gameId:'1',platform:'Xbox',stats:{achievementUnlocked:4,achievementTotal:20}};
 const steam={provider:'steam',gameId:'2',platform:'Steam',stats:{achievementUnlocked:1,achievementTotal:20,captures:3}};
 const choices=library.platformChoices([capture,steam,xbox]);assert.equal(choices.length,2);
 assert.equal(choices.find(c=>c.label==='Xbox').edition,xbox);
 assert.deepEqual(choices.find(c=>c.label==='Xbox').members,[capture,xbox]);
 const view=library.profilePresentation(xbox,[capture,steam,xbox]);assert.equal(view.bannerUrl,'/banner');assert.equal(view.stats.captures,8);assert.equal(view.stats.achievementUnlocked,4);
 const pc=library.profilePresentation(steam,[capture,steam,xbox]);assert.equal(pc.stats.captures,3);assert.equal(pc.stats.achievementUnlocked,1);
 assert.equal(library.platformChoices([capture,xbox]).length,1);
 assert.equal(library.platformChoices([xbox,{...steam,platform:'Xbox'}]).length,2);
});
