const {test}=require('node:test');const assert=require('node:assert/strict');const a=require('../src/PixelVault.Companion/wwwroot/achievements.js');
const row=(id,category,dlc,known=true,earned=false)=>({id,title:id,description:'',tags:'',order:0,progressKnown:known,unlocked:earned,groups:{category,dlc,sortOrder:0}});
test('remaining excludes unknown progress and filters never alter full group totals',()=>{const rows=[row('A','Story','',true,true),row('B','Story',''),row('C','Story','',false)];const grouped=a.organize(rows,'','remaining');assert.deepEqual(grouped[0].totals,{earned:1,total:3,unknown:1});assert.deepEqual(grouped[0].groups[0].rows.map(r=>r.id),['B']);});
test('DLC and category assignments stay independent and saved category order is respected',()=>{const rows=[row('A','Story',''),row('B','Actions',''),row('C','Story','Expansion')];const g=a.organize(rows,'','all','provider',['Actions','Story']);assert.equal(g[0].name,'Base Game');assert.deepEqual(g[0].groups.map(x=>x.name),['Actions','Story']);assert.equal(g[1].name,'Expansion');assert.equal(g[1].groups[0].name,'Story');});
test('guide prose is readable while raw HTML and script links stay inert',()=>{const html=a.guideHTML('# Steps\n**Read this**\n- First\n- Second\n<script>alert(1)</script>');assert.ok(html.includes('<h3>Steps</h3>'));assert.ok(html.includes('<strong>Read this</strong>'));assert.ok(html.includes('<ul><li>First</li><li>Second</li></ul>'));assert.ok(!html.includes('<script>'));assert.equal(a.safeUrl('javascript:alert(1)'),null);assert.equal(a.safeUrl('https://example.com/guide'),'https://example.com/guide');});
test('missing guides are explicit and rarity sorting leaves unknown rarity last',()=>{assert.match(a.guideHTML(''),/No guide has been saved/);const rows=[{...row('A','Story',''),rarity:null},{...row('B','Story',''),rarity:5}];assert.deepEqual(a.organize(rows,'','all','rarity')[0].groups[0].rows.map(r=>r.id),['B','A']);});
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