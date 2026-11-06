import copy
import json
from pathlib import Path
import unittest

from collection_completion import obtained_only
import tcg_reader


class CollectionCompletionTests(unittest.TestCase):
    def test_only_discovery_controls_completion_even_when_binder_disagrees(self):
        root=Path(__file__).resolve().parents[2]/'PixelVaultData/achievement-tracking/tcg'
        config=json.loads((root/'config.json').read_text(encoding='utf-8'))
        for tracker in config['trackers']:
            if tracker['metric']=='collection':tracker['definition']=obtained_only(tracker['definition'])
        save=json.loads((Path(config['saveRoot'])/config['saveFile']).read_text(encoding='utf-8'))
        data=tcg_reader.extract(save,config)
        cards=data['cards'][:2]
        cards[0]['collectedBefore']=False;cards[0]['binderQuantity']=10
        cards[1]['collectedBefore']=True;cards[1]['binderQuantity']=0
        snapshots=tcg_reader.snapshots(data,config,'test',tcg_reader.TICKS_EPOCH+1)
        items={i['ItemId']:i['Current'] for i in snapshots['tcg.collection.tetramon']['Items']}
        self.assertEqual(items[cards[0]['id']+'.ever'],0)
        self.assertEqual(items[cards[1]['id']+'.ever'],1)
        self.assertFalse(any(key.endswith('.owned') for key in items))
        self.assertEqual(sum(len(t['definition']['Items']) for t in config['trackers'] if t['metric']=='collection'),4460)

    def test_retained_identity_pull_rate_and_user_metadata_survive(self):
        definition={'Milestones':['Collected','In Binder'],'Description':'old','Items':[
            {'Id':'card.ever','GroupId':'card','Target':1,'Milestone':'Collected','Detail':'Pull rate: 1%','Name':'Card','Order':4,'Missable':True},
            {'Id':'card.owned','GroupId':'card','Target':1,'Milestone':'In Binder','Detail':'binder','Name':'Card','Order':5}],
            'CategoryTargets':{'Set':1},'Id':'collection'}
        before=copy.deepcopy(definition);after=obtained_only(definition)
        self.assertEqual(definition,before)
        self.assertEqual(after['Items'],before['Items'][:1])
        self.assertEqual(after['CategoryTargets'],before['CategoryTargets'])
        self.assertEqual(obtained_only(after),after)


if __name__=='__main__':unittest.main()
