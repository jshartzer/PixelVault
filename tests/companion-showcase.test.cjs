const {test}=require('node:test');const assert=require('node:assert/strict');
const showcase=require('../src/PixelVault.Companion/wwwroot/showcase.js');
const game=(id,title,tier='none',earned=0,total=10)=>({provider:'steam',gameId:id,groupId:id,title,platform:'Steam',trackerCount:0,stats:{achievementUnlocked:999,achievementTotal:999},completionTier:tier,completionEarned:earned,completionTotal:total,coverUrl:'/cover/'+id});
test('collection uses real games in gold-first cases with a single outer frame',()=>{
  const games=[game('1','Incomplete'),game('2','Gold','base',8,10),game('3','Emerald','base-dlc',20,20)],before=JSON.stringify(games);
  const rows=showcase.entries(games);
  assert.deepEqual(rows.map(r=>r.title),['Emerald','Gold']);assert.equal(JSON.stringify(games),before);
  const markup=showcase.markup(rows);
  assert.equal((markup.match(/class="showcase-case completion-/g)||[]).length,3);
  assert.equal((markup.match(/class="case-plaque"/g)||[]).length,3);
  assert.equal((markup.match(/class="case-corner /g)||[]).length,12);
  assert.ok(markup.indexOf('data-tier="base"')<markup.indexOf('data-tier="base-dlc"'));
  assert.match(markup,/8 \/ 10 achievements/);assert.match(markup,/20 \/ 20 achievements/);
  assert.match(markup,/#achievements\/steam\/2/);assert.doesNotMatch(markup,/999|Incomplete/);
  assert.equal((markup.match(/class="case-art"/g)||[]).length,2);
  assert.doesNotMatch(markup,/completion-clasp|completion-frame|case-post|case-rail/);
  assert.equal((markup.match(/class="case-border"/g)||[]).length,3);
  assert.equal((markup.match(/class="case-caption"/g)||[]).length,2);
});
test('award, count and navigation come from the same edition without mixing platform progress',()=>{
  const games=[game('1','Merged'),{...game('2','Merged','base',4,8),groupId:'1',provider:'retroachievements'}];
  const row=showcase.entries(games)[0];assert.equal(row.gameId,'2');assert.equal(row.provider,'retroachievements');assert.equal(row.completionEarned,4);
  games.push({...game('3','Merged','base-dlc',20,20),groupId:'1'});
  assert.equal(showcase.entries(games)[0].gameId,'3');
  games.push({...game('5','Merged','base-dlc-pv',24,24),groupId:'1',provider:'retroachievements'});
  const diamond=showcase.entries(games)[0];
  assert.equal(diamond.gameId,'5');assert.equal(diamond.provider,'retroachievements');assert.equal(diamond.completionEarned,24);
  assert.match(showcase.markup([diamond]),/BASE GAME \+ DLC \+ PV/);
  assert.deepEqual(showcase.entries([{...game('4','No verified counts','base'),completionTotal:null}]),[]);
});
test('empty libraries stay empty and titles and attributes remain escaped',()=>{
  assert.deepEqual(showcase.sizes,{small:120,medium:160,large:200});assert.deepEqual(showcase.entries([]),[]);
  const unsafe=game('1','<script>alert(1)</script>','base',1,1);unsafe.coverUrl='" onerror="alert(1)';
  const markup=showcase.markup(showcase.entries([unsafe]));assert.doesNotMatch(markup,/<script>|src="" onerror=/);assert.match(markup,/&lt;script&gt;/);
});

test('wrapping adds floors and increases height without repeating outer corners or plaques',()=>{
  const rows=showcase.entries(Array.from({length:14},(_,i)=>game(String(i),'Gold '+i,'base',10,10)));
  const wide=showcase.layout(rows,1400,200),narrow=showcase.layout(rows,680,200);
  assert.equal(wide[0].columns,6);assert.equal(wide[0].floors.length,3);
  assert.equal(narrow[0].columns,2);assert.equal(narrow[0].floors.length,7);
  assert.deepEqual(wide[0].floors.flat().map(g=>g.id),narrow[0].floors.flat().map(g=>g.id));
  for(const width of [1400,680,260]){
    const html=showcase.markup(rows,{width,coverSize:200,inset:18});
    assert.equal((html.match(/class="case-corner /g)||[]).length,12);
    assert.equal((html.match(/class="case-plaque"/g)||[]).length,3);
    assert.equal((html.match(/class="showcase-card"/g)||[]).length,14);
  }
});

test('sculpted cases reserve room for the crown and keep artwork centered on every floor',()=>{
  const artwork=require('../src/PixelVault.Companion/wwwroot/case-artwork.js');
  for(const width of [258,328,954,1400])for(const size of Object.values(showcase.sizes)){
    const m=artwork.metrics(width,size,14,width<600?18:44);
    assert.equal(m.topPadding,20);assert.equal(m.pitch,size*1.5+57);
    assert.ok(m.height>=20+62+m.rows*m.pitch+21);
    assert.ok(m.columns>=1);
    const html=showcase.markup([],{width,coverSize:size});
    assert.equal((html.match(/class="case-front"/g)||[]).length,3);
    assert.doesNotMatch(html,/case-lights/);
  }
});

test('shelf columns do not depend on HTML style attributes blocked by production CSP',()=>{
  const rows=showcase.entries(Array.from({length:7},(_,i)=>game(String(i),'Gold '+i,'base',10,10)));
  const html=showcase.markup(rows,{width:328,coverSize:120,inset:18});
  assert.doesNotMatch(html,/\sstyle=/i);
  assert.equal((html.match(/data-cover-count="2"/g)||[]).length,3);
  assert.equal((html.match(/data-cover-count="1"/g)||[]).length,3); // Partial last shelf and empty emerald case.
  assert.match(html,/data-tier="base" data-columns="2"/);
});
