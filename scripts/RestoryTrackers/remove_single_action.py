"""One-time removal; caller holds the affected PixelVault source leases."""
import json
import shutil
import sqlite3
from datetime import datetime, timezone
from pathlib import Path

ROOT=Path(r'C:\Codex\PixelVaultData\achievement-tracking')
BRIDGE=ROOT/'restory'
IDS=('restory.im-a-hacker-like-in-the-movies','restory.beautiful','restory.loot-box',
     'restory.official-partner','restory.akibas-terror')


def main():
    stamp=datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S')
    backup=ROOT/'installation-backups'/('remove-restory-single-action-'+stamp)
    backup.mkdir(parents=True)
    source=sqlite3.connect(ROOT/'pixelvault-tracking.sqlite',timeout=30)
    target=sqlite3.connect(backup/'before.sqlite');source.backup(target);target.close()
    config_path=BRIDGE/'config.json';shutil.copy2(config_path,backup/'config.json')
    config=json.loads(config_path.read_text('utf-8'))
    removed=[t for t in config['trackers'] if t['definition']['Id'] in IDS]
    assert len(removed)==5 and len(config['trackers'])==25
    for t in removed:
        d=t['definition']
        assert d['Provider']=='steam' and d['GameId']=='3812600'
        assert len(d['Items'])==1 and d['Items'][0]['Target']==1
    # Back up observations as well, without deleting game files or provider state.
    for tracker in IDS:shutil.copy2(BRIDGE/'progress'/(tracker+'.json'),backup/(tracker+'.json'))
    placeholders=','.join('?' for _ in IDS)
    source.execute('BEGIN IMMEDIATE')
    try:
        tables=[]
        for (table,) in source.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'").fetchall():
            fields={r[1] for r in source.execute('PRAGMA table_info("'+table+'")')}
            key='id' if table=='tracker_definition' else 'tracker_id' if 'tracker_id' in fields else None
            if key:tables.append((table,key))
        def retained():
            return {table:sorted(source.execute(f'SELECT * FROM "{table}" WHERE "{key}" NOT IN ({placeholders})',IDS).fetchall(),key=repr) for table,key in tables}
        before=retained()
        for tracker in IDS:
            d=json.loads(source.execute('SELECT definition FROM tracker_definition WHERE id=?',(tracker,)).fetchone()[0])
            assert d['GameId']=='3812600' and len(d['Items'])==1 and d['Items'][0]['Target']==1
        deleted={}
        for table,key in tables:
            deleted[table]=source.execute(f'DELETE FROM "{table}" WHERE "{key}" IN ({placeholders})',IDS).rowcount
        assert before==retained(), 'Unrelated rows changed'
        config['trackers']=[t for t in config['trackers'] if t['definition']['Id'] not in IDS]
        temp=config_path.with_suffix('.remove.tmp');temp.write_text(json.dumps(config,indent=2,ensure_ascii=False),encoding='utf-8');temp.replace(config_path)
        source.commit()
    except Exception:
        source.rollback()
        shutil.copy2(backup/'config.json',config_path)
        raise
    receipt={'backup':str(backup),'removed':[t['definition']['Title'] for t in removed],
             'remaining':len(config['trackers']),'deletedRows':deleted,'allOtherRowsPreserved':True}
    (backup/'receipt.json').write_text(json.dumps(receipt,indent=2),encoding='utf-8')
    print(json.dumps(receipt))


if __name__=='__main__':main()
