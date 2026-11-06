"""Text-only, backed-up update of the four existing TCG collection trackers."""
import argparse
import copy
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import sqlite3

from build_catalogue import monsters
from pull_rates import enrich, detail_text
from tcg_reader import atomic_json


def digest_tables(db):
    result={}
    for (name,) in db.execute("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name"):
        if name in ('tracker_definition','tracker_catalogue_history','sqlite_sequence'):continue
        quoted='"'+name.replace('"','""')+'"'
        rows=sorted(repr(tuple(row)) for row in db.execute('SELECT * FROM '+quoted))
        result[name]=hashlib.sha256('\n'.join(rows).encode()).hexdigest()
    return result


def migrate(app,bridge,raw_monsters,evidence):
    evidence.mkdir(parents=True,exist_ok=True)
    original=(bridge/'config.json').read_bytes();config=json.loads(original)
    if hashlib.sha256(Path(config['gameAssembly']).read_bytes()).hexdigest()!=config['assemblySha256']:
        raise ValueError('Installed game changed')
    for asset in config['assetFiles']:
        stat=Path(asset['path']).stat()
        if (stat.st_size,stat.st_mtime_ns)!=(asset['size'],asset['mtimeNs']):raise ValueError('Assets changed')
    catalog=monsters(raw_monsters.read_bytes())
    updated=copy.deepcopy(config);rates=enrich(updated,catalog,catalog['packSettings'])
    db=sqlite3.connect(app/'achievement-tracking/pixelvault-tracking.sqlite',timeout=30)
    with sqlite3.connect(evidence/'before.sqlite') as backup:db.backup(backup)
    (evidence/'config-before.json').write_bytes(original)
    changes=[]
    try:
        with db:
            db.execute('BEGIN IMMEDIATE')
            protected=digest_tables(db)
            for tracker in updated['trackers']:
                if tracker['metric']!='collection':continue
                tid=tracker['definition']['Id']
                row=db.execute('SELECT definition FROM tracker_definition WHERE id=?',(tid,)).fetchone()
                if row is None:continue  # Never recreate a deleted tracker.
                before=json.loads(row[0]);after=copy.deepcopy(before)
                if before['Provider']!='steam' or before['GameId']!='3070070':raise ValueError('Unexpected scope')
                count=0
                for item in after['Items']:
                    if item.get('GroupId') not in rates:continue  # Preserve user-added records.
                    detail=item.get('Detail','')
                    if detail.startswith('Pull rate — '):detail=detail.split('\n\n',1)[1] if '\n\n' in detail else ''
                    item['Detail']=detail_text(rates[item['GroupId']])+('\n\n'+detail if detail else '')
                    count+=1
                stripped=copy.deepcopy(after)
                for old,new in zip(before['Items'],stripped['Items']):new['Detail']=old.get('Detail')
                if stripped!=before:raise ValueError('Non-detail metadata changed')
                if before==after:continue
                serialized=json.dumps(after,ensure_ascii=False,indent=2)
                db.execute('UPDATE tracker_definition SET definition=? WHERE id=?',(serialized,tid))
                db.execute('INSERT INTO tracker_catalogue_history(tracker_id,before_definition,after_definition,created_utc,source,fingerprint) VALUES(?,?,?,?,?,?)',
                    (tid,row[0],serialized,621355968000000000+int(datetime.now(timezone.utc).timestamp()*10000000),
                     'Requested exact-variant pull percentages per pack',hashlib.sha256(serialized.encode()).hexdigest().upper()))
                changes.append({'tracker':tid,'items':count})
            if digest_tables(db)!=protected:raise ValueError('Non-catalogue state changed')
            atomic_json(bridge/'config.json',updated)
    except BaseException:
        atomic_json(bridge/'config.json',config)
        raise
    finally:db.close()
    atomic_json(evidence/'rates.json',{k:{pack:{'percentage':float(v*100),'fraction':str(v)} for pack,v in values.items()} for k,values in rates.items()})
    receipt={'changed':changes,'variants':len(rates),'protectedTables':protected,'progressUnchanged':True,
             'assemblySha256':config['assemblySha256'],'settings':catalog['packSettings'],
             'updatedUtc':datetime.now(timezone.utc).isoformat()}
    atomic_json(evidence/'verification.json',receipt)
    print(json.dumps({'changed':changes,'variants':len(rates),'progressUnchanged':True}))


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    for name in ('app','bridge','raw_monsters','evidence'):parser.add_argument(name,type=Path)
    a=parser.parse_args();migrate(a.app,a.bridge,a.raw_monsters,a.evidence)
