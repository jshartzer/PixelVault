const {test}=require('node:test');const assert=require('node:assert/strict');const a=require('../src/PixelVault.Companion/wwwroot/achievements.js');
const row=(id,category,dlc,known=true,earned=false)=>({id,title:id,description:'',tags:'',order:0,progressKnown:known,unlocked:earned,groups:{category,dlc,sortOrder:0}});

test('Century definitions show requirements without connection or implementation details',()=>{
  const century={planned:5,status:'Awaiting save connection',definitions:[{id:'century.roads',provider:'steam',gameId:'1850570',points:100,title:'All Roads Lead Home',description:'Rebuild every road section.',category:'Infrastructure',requirement:'Mountain routes and online contributions count.'}]};
  const html=a.centurySection(century);
  assert.match(html,/<span>Centurian Achievements<\/span>/);assert.match(html,/All Roads Lead Home/);
  assert.doesNotMatch(html,/Awaiting save connection|planned achievements defined|verified from save/);
  assert.doesNotMatch(html,/earned-state|\b0 \/ 1\b/);
  assert.match(html,/href="#achievement\/steam\/1850570\/century.roads"/);
  assert.match(html,/achievement-row rarity-common century-achievement/);assert.match(html,/achievement-entries achievement-tiles/);
  assert.match(html,/100 PV pts/);assert.match(html,/\/api\/v1\/century\/steam\/1850570\/century.roads\/icon\?art=5&amp;state=locked/);
  assert.match(a.centurySection(century,'','all',false,'list'),/achievement-entries achievement-list/);
  assert.match(a.centurySection(century,'mountain','unknown'),/All Roads Lead Home/);
  for(const filter of ['earned','remaining'])assert.doesNotMatch(a.centurySection(century,'',filter),/All Roads Lead Home/);
  assert.match(a.centurySection(century,'missing'),/No Centurian Achievements match/);
  assert.match(a.centurySection(undefined),/No Centurian Achievements have been added/);
  assert.doesNotMatch(a.centurySection(century,'','all',true),/data-collapse="century" open/);
  assert.match(a.centurySection({...century,definitions:[{...century.definitions[0],title:'<script>unsafe</script>'}]}),/&lt;script&gt;/);
});
test('Century save progress controls filters, counts and unlocked artwork independently of other definitions',()=>{
  const road={id:'century.roads',provider:'steam',gameId:'1850570',points:100,title:'Roads',description:'Roads',category:'Infrastructure',requirement:'All 51',progressKnown:true,unlocked:false,counterCurrent:28,counterTarget:51,connectionStatus:'28 / 51 roads · verified from save'};
  const century={planned:5,status:'Awaiting save connection',definitions:[road,{...road,id:'century.interviews',title:'Interviews',description:'Interviews',progressKnown:false,connectionStatus:'Awaiting save connection'}]};
  assert.match(a.centurySection(century,'','remaining'),/28 \/ 51/);
  assert.doesNotMatch(a.centurySection(century,'','remaining'),/>Interviews</);
  assert.doesNotMatch(a.centurySection(century,'','unknown'),/>Roads</);
  assert.doesNotMatch(a.centurySection(century,'','earned'),/>Roads</);
  const html=a.centurySection({...century,definitions:[{...road,unlocked:true,connectionStatus:'Earned · verified from save'}]},'','earned');
  assert.match(html,/state=unlocked/);assert.doesNotMatch(html,/verified from save/);assert.match(html,/100 PV pts/);
});
test('locked artwork renders even without unlocked art and image identity changes on earning',()=>{
  const scope={provider:'steam',game:'42'};
  const locked=a.achievementCard({...row('one','Story',''),icon:'',lockedIcon:'locked-source'},scope);
  assert.match(locked,/<img /);assert.match(locked,/state=locked/);
  const earned=a.achievementCard({...row('one','Story','',true,true),icon:'source',lockedIcon:'locked-source'},scope);
  assert.match(earned,/state=earned/);
  const unknown=a.achievementCard({...row('one','Story','',false,true),icon:'source',lockedIcon:'locked-source'},scope);
  assert.match(unknown,/state=locked/);
});
test('achievement cards emphasize artwork without status or Guide badges while keeping accessible state and links',()=>{
  for(const unlocked of [false,true]){
    const html=a.achievementCard({...row('one','Story',''),title:'<A title>',icon:'source',guide:'Saved guide',unlocked,rarity:5},{provider:'steam',game:'42'});
    assert.doesNotMatch(html,/class="achievement-state|<span>Guide<\/span>/);
    assert.match(html,/width="96" height="96"/);assert.match(html,/href="#achievement\/steam\/42\/one"/);
    assert.match(html,new RegExp('aria-label="&lt;A title&gt; · '+(unlocked?'Earned':'Remaining')+'"'));
    assert.match(html,/5% earned/);
  }
});
test('remaining excludes unknown progress and filters never alter full group totals',()=>{const rows=[row('A','Story','',true,true),row('B','Story',''),row('C','Story','',false)];const grouped=a.organize(rows,'','remaining');assert.deepEqual(grouped[0].totals,{earned:1,total:3,unknown:1});assert.deepEqual(grouped[0].groups[0].rows.map(r=>r.id),['B']);});
test('DLC and category assignments stay independent and saved category order is respected',()=>{const rows=[row('A','Story',''),row('B','Actions',''),row('C','Story','Expansion')];const g=a.organize(rows,'','all','provider',['Actions','Story']);assert.equal(g[0].name,'Base Game');assert.deepEqual(g[0].groups.map(x=>x.name),['Actions','Story']);assert.equal(g[1].name,'Expansion');assert.equal(g[1].groups[0].name,'Story');});
test('guide prose is readable while raw HTML and script links stay inert',()=>{const html=a.guideHTML('# Steps\n**Read this**\n- First\n- Second\n<script>alert(1)</script>');assert.ok(html.includes('<h3>Steps</h3>'));assert.ok(html.includes('<strong>Read this</strong>'));assert.ok(html.includes('<ul><li>First</li><li>Second</li></ul>'));assert.ok(!html.includes('<script>'));assert.equal(a.safeUrl('javascript:alert(1)'),null);assert.equal(a.safeUrl('https://example.com/guide'),'https://example.com/guide');});
test('missing guides are explicit and rarity sorting leaves unknown rarity last',()=>{assert.match(a.guideHTML(''),/No guide has been added/);const rows=[{...row('A','Story',''),rarity:null},{...row('B','Story',''),rarity:5}];assert.deepEqual(a.organize(rows,'','all','rarity')[0].groups[0].rows.map(r=>r.id),['B','A']);});
test('rarity tiers match desktop thresholds including boundaries and unavailable data',()=>{
  for(const [percent,tier] of [[0,'uber-rare'],[.009,'uber-rare'],[.01,'gold'],[1,'gold'],[1.001,'silver'],[5,'silver'],[5.001,'bronze'],[10,'bronze'],[10.001,'common'],[100,'common'],[null,'common'],[NaN,'common'],[-1,'common'],[101,'common']])assert.equal(a.rarityTier(percent),tier,String(percent));
});
test('completion stays inside each DLC category with unfinished before obtained',()=>{
  const rows=[row('done-base','Story','',true,true),row('todo-base','Story',''),row('done-action','Actions','',true,true),row('todo-action','Actions',''),row('done-dlc','Story','Expansion',true,true),row('todo-dlc','Story','Expansion'),row('unknown','Story','Expansion',false,true)];
  const groups=a.completionSections(rows);const ids=group=>group.phases.flatMap(p=>p.rows.map(r=>r.id));
  assert.deepEqual(groups.map(s=>s.name),['Base Game','Expansion']);
  assert.deepEqual(groups[0].groups.map(g=>g.name),['Story','Actions']);
  assert.deepEqual(ids(groups[0].groups[0]),['todo-base','done-base']);
  assert.deepEqual(ids(groups[0].groups[1]),['todo-action','done-action']);
  assert.deepEqual(ids(groups[1].groups[0]),['todo-dlc','unknown','done-dlc']);
  assert.deepEqual(groups[0].groups[0].phases.map(p=>p.name),['Not obtained','Obtained']);
  assert.deepEqual(groups[0].groups[0].totals,{earned:1,total:2,unknown:0});
  const filtered=a.completionSections(rows,'','earned');
  assert.deepEqual(ids(filtered[0].groups[0]),['done-base']);
  assert.deepEqual(filtered[0].groups[0].totals,{earned:1,total:2,unknown:0});
  assert.deepEqual(a.completionSections(rows,'missing'),[]);
  assert.deepEqual(a.completionSections(rows,'','all','provider',['Actions','Story'])[0].groups.map(g=>g.name),['Actions','Story']);
});
