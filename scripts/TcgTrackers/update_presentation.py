"""Approved metadata-only migration. Stable tracker/item/group IDs and progress are preserved."""
import argparse
import copy
from datetime import datetime,timezone
import hashlib
import json
from pathlib import Path
import sqlite3

from tcg_reader import atomic_json


def migrate(app,bridge,output):
    config=json.loads((bridge/'config.json').read_text('utf-8'));updated=copy.deepcopy(config)
    db=sqlite3.connect(app/'achievement-tracking/pixelvault-tracking.sqlite',timeout=10)
    stamp=datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%f')
    backup=app/'achievement-tracking/installation-backups'/('tcg-presentation-'+stamp+'.sqlite')
    backup.parent.mkdir(parents=True,exist_ok=True)
    with sqlite3.connect(backup) as target:db.backup(target)
    config_backup=bridge/('config-before-presentation-'+stamp+'.json');config_backup.write_bytes((bridge/'config.json').read_bytes())
    renamed=[];art=[]
    with db:
        db.execute('BEGIN IMMEDIATE')
        for tracker in updated['trackers']:
            d=tracker['definition'];row=db.execute('SELECT definition FROM tracker_definition WHERE id=?',(d['Id'],)).fetchone()
            if not row:continue  # Never recreate a deleted tracker.
            before=json.loads(row[0]);after=copy.deepcopy(before)
            if before['Provider']!='steam' or before['GameId']!='3070070':raise ValueError('Unexpected tracker scope')
            if tracker['metric']=='collection':
                mapping={'Collected before':'Collected','In binder':'In Binder','Collected':'Collected','In Binder':'In Binder'}
                if any(m not in mapping for m in before['Milestones']):raise ValueError('Milestones changed; review required')
                for definition in (d,after):
                    definition['Milestones']=[mapping[m] for m in definition['Milestones']]
                    for item in definition['Items']:item['Milestone']=mapping[item['Milestone']]
                renamed.append(d['Id'])
            if before.get('AchievementId'):
                d['IconId']=None;after['IconId']=None
                db.execute('INSERT INTO tracker_icon(tracker_id,icon_id) VALUES(?,NULL) ON CONFLICT(tracker_id) DO UPDATE SET icon_id=NULL',(d['Id'],))
                art.append(d['Id'])
            # A metadata migration must never change the record set or numeric targets.
            identity=lambda value:[(x['Id'],x.get('GroupId'),x['Target']) for x in value['Items']]
            if identity(before)!=identity(after):raise ValueError('Record identity changed')
            if before!=after:
                after_json=json.dumps(after,ensure_ascii=False,indent=2)
                db.execute('UPDATE tracker_definition SET definition=? WHERE id=?',(after_json,d['Id']))
                db.execute('INSERT INTO tracker_catalogue_history(tracker_id,before_definition,after_definition,created_utc,source,fingerprint) VALUES(?,?,?,?,?,?)',
                    (d['Id'],row[0],after_json,621355968000000000+int(datetime.now(timezone.utc).timestamp()*10000000),
                     'Requested card labels and automatic linked achievement artwork',hashlib.sha256(after_json.encode()).hexdigest().upper()))
    atomic_json(bridge/'config.json',updated)
    receipt={'backup':str(backup),'configBackup':str(config_backup),'renamedCollections':renamed,'automaticArtwork':art}
    atomic_json(output,receipt);print(json.dumps(receipt));db.close()


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('app',type=Path);parser.add_argument('bridge',type=Path);parser.add_argument('receipt',type=Path)
    args=parser.parse_args();migrate(args.app,args.bridge,args.receipt)
