const {test}=require('node:test');const assert=require('node:assert/strict');const icons=require('../src/PixelVault.Companion/wwwroot/tracker-icons.js');
const tracker={id:'custom.one',title:'Every region',provider:'steam',gameId:'42',achievementId:'A / B',totals:{completed:2,total:10}};
test('automatic achievement artwork uses the authenticated same-origin route and manual choices override it',()=>{
  assert.equal(icons.artwork(tracker),'/api/v1/achievements/steam/42/A%20%2F%20B/unlocked-icon?art=1');assert.match(icons.image(tracker),/<img src="\/api\/v1\/achievements\//);assert.match(icons.image(tracker),/<svg /);
  assert.doesNotMatch(icons.image({...tracker,iconId:'deliveries'}),/<img/);assert.doesNotMatch(icons.image({...tracker,achievementId:null}),/<img/);assert.equal(icons.symbol('unknown'),icons.symbol('checklist'));
});
test('cards retain exact tracker links, escaped names, completion totals and a separate accessible picker button',()=>{
  const html=icons.card({...tracker,id:'a / b',title:'<img onerror="bad">',totals:{completed:10,total:10}},{platform:'Steam & more'});
  assert.match(html,/href="#tracker\/a%20%2F%20b"/);assert.match(html,/&lt;img onerror=&quot;bad&quot;&gt;/);assert.match(html,/Steam &amp; more/);assert.match(html,/aria-label="Completed"/);assert.match(html,/data-percent="100"/);
  assert.match(html,/<\/a><button/);assert.match(html,/data-select-tracker-image="a \/ b"/);assert.doesNotMatch(icons.card(tracker,{readOnly:true}),/<button/);
});
test('all twelve native icon types are offered with complete vector paths',()=>{
  assert.deepEqual(icons.options.map(o=>o.id),['checklist','trophy','collectibles','exploration','deliveries','combat','crafting','upgrades','races','quests','building','relationships']);
  for(const option of icons.options)assert.match(icons.symbol(option.id),/<path d="M/);
});
