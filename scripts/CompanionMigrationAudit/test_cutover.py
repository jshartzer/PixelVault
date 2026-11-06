import json
import sqlite3
import tempfile
import time
import unittest
from pathlib import Path

from cutover import frozen_automatic, check_running, apply_frozen


class CutoverGates(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name)
        self.state=self.root/'state';self.state.mkdir()
        self.frozen=self.root/'frozen';(self.frozen/'nas').mkdir(parents=True)
        self.plan=self.root/'plan.json'
        self.plan.write_text(json.dumps({'StateRoot':str(self.state),'EvidenceRoot':str(self.root)}))
        (self.root/'cutover-journal.json').write_text(json.dumps({'FrozenEvidence':str(self.frozen)}))
        for path in [self.state/'companion.sqlite',self.frozen/'nas/companion.sqlite']:
            db=sqlite3.connect(path);db.execute('CREATE TABLE companion_connector_activity(workspace TEXT,name TEXT,value TEXT,updated INTEGER)');db.commit();db.close()

    def tearDown(self):self.temp.cleanup()

    def settings(self, expected, actual, age=0):
        (self.frozen/'canonical-apply-proof.json').write_text(json.dumps({'AutomaticEnabled':expected}))
        db=sqlite3.connect(self.state/'companion.sqlite');db.execute('DELETE FROM companion_connector_activity')
        tick=621355968000000000+int((time.time()-age)*10000000)
        db.executemany('INSERT INTO companion_connector_activity VALUES(?,?,?,?)',[('approved','settings',json.dumps({'AutomaticEnabled':actual}),tick),('approved','local-worker-tick','',tick)])
        db.commit();db.close()

    def test_global_pause_uses_only_the_frozen_approved_workspace(self):
        db=sqlite3.connect(self.frozen/'nas/companion.sqlite')
        db.executemany('INSERT INTO companion_connector_activity VALUES(?,?,?,0)', [('other','settings',json.dumps({'AutomaticEnabled':True})),('approved','settings',json.dumps({'AutomaticEnabled':False}))])
        db.commit();db.close()
        self.assertFalse(frozen_automatic(self.frozen,'approved'))
        self.assertTrue(frozen_automatic(self.frozen,'other'))
        self.assertFalse(frozen_automatic(self.frozen,'missing'))

    def test_readiness_accepts_preserved_pause_and_enabled_policies_and_rejects_changed_policy(self):
        for expected,actual,ready in [(False,False,True),(True,True,True),(False,True,False),(True,False,False)]:
            with self.subTest(expected=expected,actual=actual):
                self.settings(expected,actual)
                self.assertEqual(ready,check_running(self.plan)['Ready'])

    def test_stale_worker_heartbeat_never_marks_production_ready(self):
        self.settings(True,True,age=40)
        self.assertFalse(check_running(self.plan)['Ready'])

    def test_unreviewed_production_root_is_rejected_before_journal_or_data_access(self):
        self.plan.write_text(json.dumps({'Purpose':'pc-companion-cutover','ApplicationRoot':str(self.root/'unexpected')}))
        with self.assertRaises(ValueError):apply_frozen(self.frozen,self.plan)
        self.assertFalse((self.root/'unexpected').exists())


if __name__=='__main__':unittest.main()
