import json
import sqlite3
import tempfile
import unittest
from contextlib import closing
from pathlib import Path
from stage import binding_aliases, manual_signature, reconcile_tracking, require_staged


class StagingTests(unittest.TestCase):
    def test_live_and_nested_inputs_cannot_be_stage_destinations(self):
        base=Path('C:/Codex/Companion Backups/example')
        require_staged(base,base/'phase2-test')
        for target in [Path('C:/Codex/PixelVaultData'),base/'pc/phase2-test',base/'application']:
            with self.assertRaises(ValueError):require_staged(base,target)

    def test_mirror_signatures_preserve_account_note_and_manual_value(self):
        def row(account,note,stamp):
            return {'tracker_id':'tracker','account':account,'changes':json.dumps([{'ItemId':'item','Fields':6,'Before':{'Manual':0,'Note':'before','ManualUtc':stamp},'After':{'Manual':1,'Note':note,'ManualUtc':stamp}}])}
        self.assertEqual(manual_signature(row('owner','note',1)),manual_signature(row('owner','note',2)))
        self.assertNotEqual(manual_signature(row('owner','note',1)),manual_signature(row('other','note',1)))
        self.assertNotEqual(manual_signature(row('owner','note',1)),manual_signature(row('owner','cleared',1)))

    def test_source_id_alias_is_not_permission_to_change_a_path(self):
        left,right=sqlite3.connect(':memory:'),sqlite3.connect(':memory:')
        for c in [left,right]:
            c.row_factory=sqlite3.Row;c.execute('CREATE TABLE tracker_source(tracker_id TEXT,account TEXT,binding TEXT,status TEXT)')
        binding={'Id':'pc','Location':'approved-save','AdapterId':'reader','Account':'owner'}
        left.execute('INSERT INTO tracker_source VALUES(?,?,?,?)',('tracker','owner',json.dumps(binding),'{}'))
        right.execute('INSERT INTO tracker_source VALUES(?,?,?,?)',('tracker','owner',json.dumps({**binding,'Id':'nas'}),'{}'))
        self.assertEqual(binding_aliases(left,right)[('tracker','owner')][:2],('nas','pc'))
        right.execute('UPDATE tracker_source SET binding=?',(json.dumps({**binding,'Id':'nas','Location':'different-save'}),))
        with self.assertRaises(ValueError):binding_aliases(left,right)
        self.assertEqual(binding_aliases(left,right,set()),{})
        left.close();right.close()

    def fixture(self,path):
        c=sqlite3.connect(path)
        c.executescript('''
            CREATE TABLE tracker_source(tracker_id TEXT,account TEXT,binding TEXT,status TEXT);
            CREATE TABLE tracker_definition(id TEXT,definition TEXT);
            CREATE TABLE tracker_link(tracker_id TEXT,achievement_id TEXT);
            CREATE TABLE tracker_title(tracker_id TEXT,title TEXT);
            CREATE TABLE tracker_icon(tracker_id TEXT,icon_id TEXT);
            CREATE TABLE tracker_item_state(tracker_id TEXT,account TEXT,item_id TEXT,reported_current INTEGER,manual_current INTEGER,availability INTEGER,note TEXT,source TEXT,evidence TEXT,observed_utc INTEGER,manual_utc INTEGER);
            CREATE TABLE tracker_history(id INTEGER PRIMARY KEY,tracker_id TEXT,account TEXT,kind TEXT,label TEXT,created_utc INTEGER,undone_utc INTEGER,adapter_id TEXT,adapter_version TEXT,fingerprint TEXT,observed_utc INTEGER,changes TEXT);
            CREATE TABLE tracker_client_request(tracker_id TEXT,account TEXT,request_id TEXT,fingerprint TEXT,history_id INTEGER);
            CREATE TABLE century_progress(provider TEXT,game TEXT,id TEXT,account TEXT,value TEXT);
        ''')
        c.close()

    def test_unproven_observation_rolls_back_archive_schema_and_values(self):
        with tempfile.TemporaryDirectory() as directory:
            pc,nas=Path(directory)/'pc.sqlite',Path(directory)/'nas.sqlite'
            for path,value in [(pc,0),(nas,1)]:
                self.fixture(path)
                with closing(sqlite3.connect(path)) as c:
                    c.execute("INSERT INTO tracker_item_state VALUES('tracker','owner','item',?,NULL,1,'','source','evidence',1,0)",(value,))
                    c.commit()
            with self.assertRaises(ValueError):
                reconcile_tracking(pc,nas,'report','workspace',{'owner'},{('tracker','owner')})
            with closing(sqlite3.connect(pc)) as c:
                self.assertEqual(c.execute('SELECT reported_current FROM tracker_item_state').fetchone()[0],0)
                self.assertEqual(c.execute("SELECT count(*) FROM sqlite_master WHERE name LIKE 'tracker_migration_%'").fetchone()[0],0)

    def test_replay_writes_nothing_and_unrelated_accounts_are_preserved(self):
        with tempfile.TemporaryDirectory() as directory:
            pc,nas=Path(directory)/'pc.sqlite',Path(directory)/'nas.sqlite'
            for path,value in [(pc,0),(nas,1)]:
                self.fixture(path)
                with closing(sqlite3.connect(path)) as c:
                    c.execute("INSERT INTO tracker_item_state VALUES('tracker','other-account','item',?,NULL,1,'private note','source','evidence',1,0)",(value,))
                    c.commit()
            first=reconcile_tracking(pc,nas,'report','workspace',{'owner'},set())
            second=reconcile_tracking(pc,nas,'report','workspace',{'owner'},set())
            self.assertFalse(first['replayed']);self.assertTrue(second['replayed']);self.assertEqual(second['newWrites'],0)
            with closing(sqlite3.connect(pc)) as c:
                self.assertEqual(c.execute('SELECT reported_current,note FROM tracker_item_state').fetchone(),(0,'private note'))

    def test_older_availability_cannot_replace_a_newer_observation(self):
        with tempfile.TemporaryDirectory() as directory:
            pc,nas=Path(directory)/'pc.sqlite',Path(directory)/'nas.sqlite'
            binding=json.dumps({'Id':'binding','AdapterId':'reader','AdapterVersion':'1','Location':'approved','Account':'owner'})
            for path,availability,observed in [(pc,1,5),(nas,0,4)]:
                self.fixture(path)
                with closing(sqlite3.connect(path)) as c:
                    c.execute("INSERT INTO tracker_source VALUES('tracker','owner',?,'{}')",(binding,))
                    c.execute("INSERT INTO tracker_item_state VALUES('tracker','owner','item',0,NULL,?,'','source','evidence',?,0)",(availability,observed))
                    if path==nas:
                        after={'Reported':0,'Availability':0,'Source':'source','Evidence':'evidence','ObservedUtc':4}
                        c.execute("INSERT INTO tracker_history VALUES(1,'tracker','owner','refresh','accepted',4,0,'reader','1','validated-save',4,?)",(json.dumps([{'ItemId':'item','Fields':1,'Before':{},'After':after}]),))
                    c.commit()
            with self.assertRaisesRegex(ValueError,'older source'):
                reconcile_tracking(pc,nas,'report','workspace',{'owner'},{('tracker','owner')})
            with closing(sqlite3.connect(pc)) as c:
                self.assertEqual(c.execute('SELECT availability,observed_utc FROM tracker_item_state').fetchone(),(1,5))


if __name__=='__main__':unittest.main()
