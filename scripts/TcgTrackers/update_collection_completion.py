"""Backed-up metadata migration; removed binder state/history remains recoverable."""
import argparse
import copy
from datetime import datetime,timezone
import hashlib
import json
from pathlib import Path
import sqlite3

from collection_completion import obtained_only
from tcg_reader import atomic_json
from update_pull_rates import digest_tables


def migrate(app,bridge,evidence):
    evidence.mkdir(parents=True,exist_ok=True)
    original=(bridge/'config.json').read_bytes();config=json.loads(original);updated=copy.deepcopy(config)
    for tracker in updated['trackers']:
        if tracker['metric']=='collection':tracker['definition']=obtained_only(tracker['definition'])
    db=sqlite3.connect(app/'achievement-tracking/pixelvault-tracking.sqlite',timeout=30)
    with sqlite3.connect(evidence/'before.sqlite') as backup:db.backup(backup)
    (evidence/'config-before.json').write_bytes(original)
    changed=[]
    try:
        with db:
            db.execute('BEGIN IMMEDIATE');protected=digest_tables(db)
            for tracker in updated['trackers']:
                if tracker['metric']!='collection':continue
                tid=tracker['definition']['Id']
                row=db.execute('SELECT definition FROM tracker_definition WHERE id=?',(tid,)).fetchone()
                if row is None:continue
                before=json.loads(row[0])
                if before['Provider']!='steam' or before['GameId']!='3070070':raise ValueError('Unexpected game scope')
                after=obtained_only(before)
                if before==after:continue
                text=json.dumps(after,ensure_ascii=False,indent=2)
                db.execute('UPDATE tracker_definition SET definition=? WHERE id=?',(text,tid))
                db.execute('INSERT INTO tracker_catalogue_history(tracker_id,before_definition,after_definition,created_utc,source,fingerprint) VALUES(?,?,?,?,?,?)',
                    (tid,row[0],text,621355968000000000+int(datetime.now(timezone.utc).timestamp()*10000000),
                     'Requested collection completion from originally obtained indicator only',hashlib.sha256(text.encode()).hexdigest().upper()))
                changed.append({'tracker':tid,'before':len(before['Items']),'after':len(after['Items'])})
            if digest_tables(db)!=protected:raise ValueError('Progress, notes, bindings or history changed')
            atomic_json(bridge/'config.json',updated)
    except BaseException:
        atomic_json(bridge/'config.json',config)
        raise
    finally:db.close()
    receipt={'changed':changed,'allStoredProgressAndNotesPreserved':True,'protectedTables':protected,
             'updatedUtc':datetime.now(timezone.utc).isoformat()}
    atomic_json(evidence/'verification.json',receipt)
    print(json.dumps({'changed':changed,'allStoredProgressAndNotesPreserved':True}))


if __name__=='__main__':
    p=argparse.ArgumentParser()
    for name in ('app','bridge','evidence'):p.add_argument(name,type=Path)
    a=p.parse_args();migrate(a.app,a.bridge,a.evidence)
