import copy
import hashlib
import json
import os
from pathlib import Path
import tempfile
import unittest

import tcg_reader as reader

ROOT=Path(r'C:\Codex\PixelVaultData\achievement-tracking\tcg')


class ReaderTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.config=json.loads((ROOT/'config.json').read_text('utf-8'))
        cls.save_path=Path(cls.config['saveRoot'])/cls.config['saveFile']
        cls.raw=cls.save_path.read_bytes();cls.save=json.loads(cls.raw)
        cls.data=reader.extract(cls.save,cls.config)

    def test_known_counters_and_zero_based_values(self):
        self.assertEqual((self.data['day'],self.data['level']),(261,71))
        self.assertEqual(self.data['achievementCounters'],{'album':2347,'level':71,'gem':195,'duels':0,'grades':5})
        self.assertEqual(self.data['gradeLeader']['grades'],[6,7,8,9,10])
        self.assertEqual(sum(s['collectedBefore'] for s in self.data['collectionSummary'].values()),2835)

    def test_discovery_and_duplicate_copies_do_not_inflate_achievement(self):
        s=copy.deepcopy(self.save);s['m_CardCollectedList'][0]+=100
        self.assertEqual(reader.extract(s,self.config)['achievementCounters']['album'],2347)
        s['m_CardCollectedList'][0]=0
        result=reader.extract(s,self.config)
        self.assertEqual(result['achievementCounters']['album'],2346)
        self.assertTrue(result['cards'][0]['collectedBefore'])

    def test_physical_stock_not_costing_cache(self):
        p=next(p for p in self.data['products'] if p['id']==0)
        self.assertEqual(p['availableUnits'],21)
        self.assertNotEqual(p['availableUnits'],self.save['m_CurrentTotalItemCountList'][0])
        s=copy.deepcopy(self.save);s['m_CurrentTotalItemCountList'][0]=999999
        self.assertEqual(reader.extract(s,self.config)['products'][0]['availableUnits'],21)

    def test_finance_signs_and_report_days(self):
        f=self.data['finances']
        self.assertEqual((f['reportWindowStartDay'],f['reportWindowEndDay']),(231,260))
        self.assertAlmostEqual(f['currentDay']['netProfit'],-7985.032318115234)
        self.assertAlmostEqual(f['cash'],212514.21)
        self.assertAlmostEqual(f['outstandingBills'],10350.096435546875)
        self.assertTrue(self.data['financeChecks']['recent-profit'])
        self.assertFalse(self.data['financeChecks']['today-profit'])

    def test_grade_amount_means_grade_not_copies_and_duel_exact_field(self):
        s=copy.deepcopy(self.save)
        s['m_GradedCardInventoryList']=[{'cardSaveIndex':100,'amount':g,'expansionType':0} for g in range(1,11)]
        s['m_GameReportDataCollect']['duelWinCount']=17
        s['m_GameReportDataCollectPermanent']['duelWinCount']=29
        m=reader.extract(s,self.config)['achievementCounters']
        self.assertEqual((m['grades'],m['duels']),(10,17))

    def test_invalid_or_new_schema_fails(self):
        for mutate in [lambda s:s.pop('m_CardCollectedListAscension'),
                       lambda s:s['m_CardCollectedList'].__setitem__(2099,1),
                       lambda s:s['m_CardCollectedList'].__setitem__(0,-1),
                       lambda s:s.__setitem__('m_PlayerName','Other shop'),
                       lambda s:s.__setitem__('m_CoinAmountDouble',float('nan'))]:
            s=copy.deepcopy(self.save);mutate(s)
            with self.assertRaises((ValueError,KeyError)):reader.extract(s,self.config)

    def test_snapshot_coverage_and_account(self):
        output=reader.snapshots(self.data,self.config,hashlib.sha256(self.raw).hexdigest(),reader.TICKS_EPOCH+1)
        self.assertEqual(len(output),12)
        for t in self.config['trackers']:
            d=t['definition'];s=output[d['Id']]
            self.assertEqual({i['Id'] for i in d['Items']},{i['ItemId'] for i in s['Items']})
            self.assertEqual(s['Account'],self.config['account'])
            self.assertLessEqual(len(s['Evidence']),4000)

    def test_stable_autosave_unchanged_and_failure_preservation(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);saves=root/'saves';saves.mkdir()
            config=copy.deepcopy(self.config);config['saveRoot']=str(saves)
            (root/'config.json').write_text(json.dumps(config),encoding='utf-8')
            cloud=Path(self.config['saveRoot'])/'steam_autocloud.vdf'
            (saves/cloud.name).write_bytes(cloud.read_bytes())
            save=saves/config['saveFile'];save.write_bytes(self.raw);os.utime(save,(1_790_000_000,1_790_000_000))
            # A different, newer manual slot must not become the autosave source.
            (saves/'savedGames_Release1.json').write_text('{}')
            first=reader.run_once(root);self.assertEqual(first['status'],'success')
            progress=root/'progress/tcg.achievement.cardcollector4.json';before=progress.read_bytes();stamp=progress.stat().st_mtime_ns
            self.assertEqual(reader.run_once(root)['status'],'unchanged');self.assertEqual(progress.stat().st_mtime_ns,stamp)
            save.write_bytes(b'{invalid');os.utime(save,(1_790_000_001,1_790_000_001))
            with self.assertRaises(ValueError):reader.run_once(root)
            self.assertEqual(progress.read_bytes(),before)
            save.write_bytes(self.raw);os.utime(save,(1_790_000_000,1_790_000_000))
            (saves/cloud.name).write_text('"accountid" "1"')
            with self.assertRaises(ValueError):reader.run_once(root)
            self.assertEqual(progress.read_bytes(),before)


if __name__=='__main__':unittest.main(verbosity=2)
