import copy
import hashlib
import json
import os
import tempfile
import time
import unittest
import zlib
from pathlib import Path

import restory_reader as reader

LIVE=Path(r'C:\Users\justc\AppData\LocalLow\Mandragora\Restory\SaveData')
CONFIG=Path(r'C:\Codex\PixelVaultData\achievement-tracking\restory\config.json')


class ReaderTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.config=json.loads(CONFIG.read_text('utf-8'))
        cls.raw=(LIVE/'Story.slot2.release-No-7.restory').read_bytes()

    def test_all_real_saves_decode_without_mutation(self):
        for path in LIVE.glob('Story*.restory'):
            raw=path.read_bytes();before=hashlib.sha256(raw).hexdigest()
            _,nodes,ids=reader.decode(raw);values=reader.measurements(nodes,ids,self.config)
            self.assertGreaterEqual(values['cleaned'],0)
            self.assertEqual(before,hashlib.sha256(path.read_bytes()).hexdigest())

    def test_known_current_counters(self):
        _,nodes,ids=reader.decode(self.raw);values=reader.measurements(nodes,ids,self.config)
        for key,expected in {'cleaned':30,'email':3,'market':5,'sold':1,'earned':25263,'daily':8015,'reviews':2}.items():
            self.assertEqual(values[key],expected,key)

    def test_reference_resolution_for_license_and_competition(self):
        save,_,_=reader.decode(self.raw)
        nodes=list(reader.walk(save))
        lic=next(n for n in nodes if 'ActiveLicenses' in n)
        lic['ActiveLicenses']=[{'$id':'test-license','ItemObjectID':self.config['licenseIds'][0]},{'$ref':'test-license'}]
        wins=next(n for n in nodes if 'WonCompetitionsByDevice' in n)
        wins['WonCompetitionsByDevice']=[{'$id':'test-device','ItemObjectID':self.config['deviceIds'][0]},{'$ref':'test-device'}]
        nodes=list(reader.walk(save));ids={n['$id']:n for n in nodes if '$id' in n}
        values=reader.measurements(nodes,ids,self.config)
        self.assertEqual(values['licenseCount'],1);self.assertEqual(values['receivedLicense'],1)
        self.assertEqual(values['winCount'],1)

    def test_truncated_corrupt_and_future_version_rejected(self):
        for raw in (self.raw[:50], b'BAD'+self.raw[3:]):
            with self.assertRaises((ValueError,zlib.error)):reader.decode(raw)
        save,_,_=reader.decode(self.raw);save['GameVersion']='unknown'
        raw=self.raw[:11]+zlib.compress(json.dumps(save).encode(),wbits=31)
        with self.assertRaisesRegex(ValueError,'version'):reader.decode(raw)

    def test_atomic_export_unchanged_and_profile_guard(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);save_root=root/'saves';save_root.mkdir()
            source=save_root/'Story.slot2.release-No-7.restory';source.write_bytes(self.raw)
            os.utime(source,(time.time()-10,time.time()-10))
            (save_root/'steam_autocloud.vdf').write_bytes((LIVE/'steam_autocloud.vdf').read_bytes())
            profile=save_root/'CurrentProfileIndex.release.json';profile.write_text('{"Profile":1,"LastUsedSlot":2}')
            config=copy.deepcopy(self.config);config['saveRoot']=str(save_root)
            reader.atomic_json(root/'config.json',config)
            self.assertEqual(reader.run_once(root)['status'],'success')
            files=list((root/'progress').glob('*.json'));self.assertEqual(len(files),20)
            times={f.name:f.stat().st_mtime_ns for f in files}
            self.assertEqual(reader.run_once(root)['status'],'unchanged')
            self.assertEqual(times,{f.name:f.stat().st_mtime_ns for f in files})
            self.assertEqual(self.raw,source.read_bytes())
            save,_,_=reader.decode(self.raw)
            money=next(n for n in reader.walk(save) if 'EarnedMoneyAmountPerDay' in n)
            money['EarnedMoneyAmountPerDay']=0
            source.write_bytes(self.raw[:11]+zlib.compress(json.dumps(save).encode(),wbits=31))
            os.utime(source,(time.time()-5,time.time()-5))
            self.assertEqual(reader.run_once(root)['status'],'success')
            daily=next(t for t in config['trackers'] if t['metric']=='daily')['definition']['Id']
            self.assertEqual(json.loads((root/'progress'/(daily+'.json')).read_text('utf-8'))['Items'][0]['Current'],0)
            profile.write_text('{"Profile":2}')
            with self.assertRaisesRegex(ValueError,'profile'):reader.run_once(root)
            profile.write_text('{"Profile":1,"LastUsedSlot":2}')
            (save_root/'steam_autocloud.vdf').write_text('"accountid" "123"')
            with self.assertRaisesRegex(ValueError,'Steam account'):reader.run_once(root)

    def test_missing_statistic_never_becomes_zero(self):
        _,nodes,ids=reader.decode(self.raw)
        del next(n for n in nodes if 'CleanedElementsCount' in n)['CleanedElementsCount']
        with self.assertRaises(KeyError):reader.measurements(nodes,ids,self.config)

    def test_requested_scope_and_catalogue(self):
        definitions=[t['definition'] for t in self.config['trackers']]
        self.assertEqual(len(definitions),20)
        self.assertFalse(any(len(d['Items'])==1 and d['Items'][0]['Target']==1 for d in definitions))
        self.assertEqual(len(self.config['licenseIds']),23)
        self.assertEqual(len(self.config['deviceIds']),31)
        self.assertFalse(any(d['AchievementId'] in ('ACH_HOW_DID_I_DO_IT','ACH_PAID_THE_BILLS') for d in definitions))


if __name__=='__main__':unittest.main()
