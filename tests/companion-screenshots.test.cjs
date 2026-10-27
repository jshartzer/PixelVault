const {test}=require('node:test');const assert=require('node:assert/strict');const s=require('../src/PixelVault.Companion/wwwroot/screenshots.js');
test('recent strip stays capped, carries each photo scope and encodes bookmark paths',()=>{
  const photos=Array.from({length:40},(_,i)=>({provider:i%2?'steam':'other',gameId:'game / '+i,title:'Game '+i,photo:{id:'photo#'+i,console:'Xbox',original:{asset:'a.png'}}}));
  const rows=s.recentRows({photos});assert.equal(rows.length,30);assert.equal(rows[0].title,'Game 0');assert.equal(rows[1].provider,'steam');assert.equal(rows[0].original.asset,'a.png');
  assert.equal(s.recentHash(rows[0]),'recent-photo/other/game%20%2F%200/photo%230');assert.deepEqual(photos[0].photo,{id:'photo#0',console:'Xbox',original:{asset:'a.png'}});
  assert.deepEqual(s.recentRows({photos:[]}),[]);
});
test('capture-day grouping keeps dates supplied by the desktop without local timezone shifts',()=>{
  const rows=[{id:'a',captureDay:'2026-10-26'},{id:'b',captureDay:'2026-10-26'},{id:'c',captureDay:'2026-10-25'},{id:'d',captureDay:null}];
  assert.deepEqual(s.groupDays(rows).map(g=>[g.day,g.photos.map(p=>p.id)]),[['2026-10-26',['a','b']],['2026-10-25',['c']],['unknown',['d']]]);
  assert.match(s.dayLabel('2026-10-26'),/26/);assert.equal(s.dayLabel(null),'Date unavailable');assert.equal(s.dayLabel('not-a-date'),'Date unavailable');
});
test('timeline paging retains separate games with colliding photo IDs and identical group names',()=>{
  const feed={version:'v',nextOffset:null,photos:[{provider:'steam',gameId:'1',title:'One',photo:{id:'same',captureDay:'2026-10-26',group:'Favorites'}},{provider:'library',gameId:'2',title:'Two',photo:{id:'same',captureDay:'2026-10-26',group:'Favorites'}}]};
  const rows=s.timelineRows(feed),merged=s.mergePage({rows:[]},{...feed,photos:rows},false);assert.equal(merged.rows.length,2);assert.notEqual(s.photoKey(rows[0]),s.photoKey(rows[1]));
  const day=s.photoSections(rows,true)[0];assert.equal(day.groups.length,2);assert.deepEqual(day.groups.map(g=>g.title),['One','Two']);assert.notEqual(day.groups[0].key,day.groups[1].key);
  assert.equal(s.photoSections(rows,false)[0].groups.length,1);assert.equal(s.timelineHash(rows[1]),'timeline-photo/library/2/same');
  const next=s.mergePage(merged,{version:'v',photos:[rows[0]],nextOffset:null},true);assert.equal(next.rows.length,2);
});
test('paged gallery merges without duplicates and refuses to mix library versions',()=>{
  const first=s.mergePage({filter:'Xbox',rows:[]},{version:'a',photos:[{id:'1'},{id:'2'}],nextOffset:2},false);
  const next=s.mergePage(first,{version:'a',photos:[{id:'2'},{id:'3'}],nextOffset:null},true);
  assert.deepEqual(next.rows.map(p=>p.id),['1','2','3']);assert.equal(next.filter,'Xbox');assert.equal(next.nextOffset,null);assert.equal(first.rows.length,2);
  assert.throws(()=>s.mergePage(first,{version:'b',photos:[]},true),/Refresh photos/);
  assert.deepEqual(s.mergePage(first,{version:'b',photos:[{id:'x'}]},false).rows,[{id:'x'}]);
});
test('photo groups stay inside their capture day, retaining order and ungrouped photos',()=>{
  const rows=[{id:'a',captureDay:'2026-10-26',group:'Trip',console:'Xbox'},{id:'b',captureDay:'2026-10-26',group:null},{id:'c',captureDay:'2026-10-26',group:'Trip',console:'Steam'},{id:'d',captureDay:'2026-10-25',group:'Trip'},{id:'e',captureDay:null,group:' '},{id:'f',captureDay:null,group:'Ungrouped'}];
  assert.deepEqual(s.photoSections(rows).map(d=>d.groups.flatMap(g=>g.photos.map(p=>p.id))),[['a','b','c'],['d'],['e','f']]);
  const days=s.photoSections(rows,true);
  assert.deepEqual(days.map(d=>d.groups.map(g=>[g.name,g.photos.map(p=>p.id)])),[[['Trip',['a','c']],[null,['b']]],[['Trip',['d']]],[[null,['e']],['Ungrouped',['f']]]]);
  const keys=days.flatMap(d=>d.groups.map(g=>g.key));assert.equal(new Set(keys).size,keys.length);
  assert.deepEqual(rows.map(p=>p.id),['a','b','c','d','e','f']);
});
test('a continued page extends the same group and safely treats authored names as data',()=>{
  const first=s.mergePage({rows:[]},{version:'v',photos:[{id:'a',captureDay:'2024-01-01',group:'<b>雪 " /</b>'}],nextOffset:1},false);
  const next=s.mergePage(first,{version:'v',photos:[{id:'b',captureDay:'2024-01-01',group:'<b>雪 " /</b>'},{id:'c',captureDay:'2024-01-01'}],nextOffset:null},true);
  const a=s.photoSections(first.rows,true)[0],b=s.photoSections(next.rows,true)[0];
  assert.equal(a.groups[0].key,b.groups[0].key);assert.equal(b.groups.length,2);assert.deepEqual(b.groups[0].photos.map(p=>p.id),['a','b']);
  assert.equal(b.groups[0].name,'<b>雪 " /</b>');assert.deepEqual(JSON.parse(b.groups[0].key),['2024-01-01','<b>雪 " /</b>']);
  assert.deepEqual(s.photoSections([],true),[]);
});
test('only deliberate downward swipes dismiss photos and horizontal swipes change photos',()=>{
  assert.equal(s.photoGesture({dx:8,dy:150,duration:220}),'dismiss');assert.equal(s.photoGesture({dx:-120,dy:5,duration:220}),'next');assert.equal(s.photoGesture({dx:120,dy:5,duration:220}),'previous');
  for(const gesture of [{dx:4,dy:5,duration:100},{dx:90,dy:100,duration:220},{dx:4,dy:-150,duration:220},{dx:10,dy:150,duration:220,zoom:2},{dx:10,dy:150,duration:220,multi:true},{dx:10,dy:150,duration:1500},{dx:10,dy:150,duration:10},{dx:-120,dy:5,duration:220,startX:5},{dx:10,dy:150,duration:220,maxX:100},{dx:150,dy:2,duration:220,maxY:100}])assert.equal(s.photoGesture(gesture),null,JSON.stringify(gesture));
});
test('square packing fills every band for awkward counts, densities and responsive widths',()=>{
  for(const density of ['compact','comfortable'])for(const width of [240,280,335,375,500,720,1080,1440])for(let count=1;count<=500;count++){
    const layout=s.squareLayout(count,width,density),context=`${density}/${width}/${count}`;
    assert.deepEqual(layout.tiles.map(t=>t.index),Array.from({length:count},(_,i)=>i),context);
    for(const [index,block] of layout.blocks.entries()){
      const coverage=Array.from({length:block.rows},()=>Array(block.columns).fill(0)),tiles=layout.tiles.filter(t=>t.block===index);
      for(const t of tiles){assert.ok(t.size>0&&t.x>=0&&t.y>=0&&t.x+t.size<=width+1e-7&&t.y+t.size<=layout.height+1e-7,context);for(let y=t.row;y<t.row+t.span;y++)for(let x=t.col;x<t.col+t.span;x++)coverage[y][x]++;}
      assert.ok(coverage.flat().every(c=>c===1),context);assert.ok(Math.abs(Math.max(...tiles.map(t=>t.x+t.size))-width)<1e-7,context);
      assert.deepEqual([...tiles].sort((a,b)=>a.y-b.y||a.x-b.x).map(t=>t.index),tiles.map(t=>t.index),context);
    }
  }
});
test('layout is deterministic, gives Compact smaller base tiles and balances the final rows',()=>{
  const a=s.squareLayout(60,335,'comfortable'),b=s.squareLayout(60,335,'compact');assert.deepEqual(a,s.squareLayout(60,335,'comfortable'));assert.ok(Math.min(...b.tiles.map(t=>t.size))<Math.min(...a.tiles.map(t=>t.size)));assert.ok(a.tiles.some(t=>t.span===2));
  const tail=s.squareLayout(10,335,'comfortable').blocks.filter(b=>b.type==='tail');assert.deepEqual(tail.map(b=>b.columns),[2,2]);
  assert.equal(s.squareLayout(1,335).tiles[0].size,335);assert.equal(s.squareLayout(0,335).height,0);assert.equal(s.squareLayout(10,0).tiles.length,0);
});
