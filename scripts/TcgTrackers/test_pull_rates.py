import copy
import json
from pathlib import Path
import re
import unittest
from fractions import Fraction as F

from build_catalogue import monsters
import pull_rates as p

ROOT = Path(__file__).resolve().parents[2]


class PullRateTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.monsters = monsters((ROOT/'artifacts/tcg-trackers/MonsterData_ScriptableObject.bin').read_bytes())
        cls.config = json.loads((ROOT/'PixelVaultData/achievement-tracking/tcg/config.json').read_text(encoding='utf-8'))

    def test_special_pack_distribution_against_installed_code(self):
        code = (ROOT/'artifacts/tcg-trackers/decompiled/all.cs').read_text(encoding='utf-8-sig')
        body = code.split('private int GetGodPackRollIndex()',1)[1].split('private void GetPackContent',1)[0]
        switches = {}
        for cases,mode in re.findall(r'((?:\s*case \d+:)+)\s*result = (\d+);',body):
            for value in re.findall(r'case (\d+):',cases): switches[int(value)] = int(mode)
        ranges = [(int(a),int(b),int(mode)) for a,b,mode in re.findall(
            r'num >= (\d+) && num <= (\d+)\)\s*\{\s*result = (\d+);',body)]
        from collections import Counter
        observed = Counter(switches.get(i,next((mode for a,b,mode in ranges if a<=i<=b),0)) for i in range(100000))
        self.assertEqual(observed,p.GOD_COUNTS)

    def test_all_variant_probabilities_partition_each_pack_mode(self):
        for mode in range(14):
            self.assertEqual(sum(p.variant_probability(b,f,mode) for b in range(6) for f in (False,True)),1)
        self.assertEqual(p.variant_probability(5,True,0),F(1,8000))
        self.assertEqual(p.variant_probability(4,True,0),F(399,800000))

    def test_unique_monsters_conserve_expected_non_ghost_cards(self):
        # Every card can occur at most once. Summing hit probabilities over all
        # species/variants must equal the expected number of non-Ghost cards.
        for pool in (26,27,34):
            for ghost in (F(1,1000),F(2,1000)):
                total = pool*sum(p.main_rate(pool,b,f,ghost_chance=ghost,duplicates=False)
                                 for b in range(6) for f in (False,True))
                self.assertEqual(total,F(99948,100000)*(7-ghost))

    def test_full_art_foil_includes_normal_and_special_routes(self):
        # Normal packs + full-art foil god packs, minus possible last-slot Ghost.
        expected=(F(98112,100000)*F(1,8000)+F(9,100000))*F(6999,1000)/26
        self.assertEqual(p.main_rate(26,5,True,ghost_chance=F(1,1000),duplicates=False),expected)

    def test_ghost_foil_uses_one_insert_or_seven_card_god_pack(self):
        ordinary_foil=F(98112,100000)*F(5,100)+F(121+100+49+21+9+201,100000)
        expected=F(1,1000)*ordinary_foil/52+F(1,100000)*(1-F(51,52)**7)
        self.assertEqual(p.ghost_rate(True,F(1,1000)),expected)

    def test_ascension_is_at_least_one_not_expected_copy_count(self):
        for b in range(6):
            for foil in (False,True):
                actual=p.main_rate(121,b,foil,ghost_chance=F(2,1000),duplicates=True)
                copies=p.main_rate(121,b,foil,ghost_chance=F(2,1000),duplicates=False)
                self.assertGreater(actual,0)
                self.assertLess(actual,copies)

    def test_enrichment_preserves_everything_except_card_details(self):
        before=copy.deepcopy(self.config);after=copy.deepcopy(before)
        rates=p.enrich(after,self.monsters,self.monsters['packSettings'])
        self.assertEqual(len(rates),4460)
        changed=0
        for old,new in zip(before['trackers'],after['trackers']):
            if old['metric']=='collection':
                for a,b in zip(old['definition']['Items'],new['definition']['Items']):
                    self.assertTrue(b['Detail'].startswith('Pull rate — '))
                    self.assertIn('per pack',b['Detail'])
                    b['Detail']=a['Detail'];changed+=1
        self.assertEqual(changed,sum(len(t['definition']['Items']) for t in before['trackers'] if t['metric']=='collection'))
        self.assertEqual(before,after)

    def test_repeated_enrichment_and_changed_game_guard(self):
        config=copy.deepcopy(self.config)
        p.enrich(config,self.monsters,self.monsters['packSettings']);once=copy.deepcopy(config)
        p.enrich(config,self.monsters,self.monsters['packSettings']);self.assertEqual(config,once)
        config['assemblySha256']='different'
        with self.assertRaises(ValueError):p.enrich(config,self.monsters,self.monsters['packSettings'])


if __name__=='__main__': unittest.main()
