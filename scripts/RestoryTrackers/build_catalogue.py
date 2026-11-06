"""One-time catalogue preparation from installed data and existing provider metadata."""
import argparse
import json
from pathlib import Path
import UnityPy


def build(game_root, app_root, output):
    bundle = next((game_root / 'Restory_Data/StreamingAssets/aa/StandaloneWindows64').glob('restory.data_*.bundle'))
    env = UnityPy.load(str(bundle))
    data = {o.path_id: o.read_typetree() for o in env.objects if o.type.name in ('MonoBehaviour', 'TextAsset')}
    named = {t.get('m_Name'): t for t in data.values()}
    localization = json.loads(named['Localization']['m_Script'])
    english = localization['English']
    def device_name(device):
        key=device['nameLocalizationKey']
        return english.get(key) or localization['EnglishDraft'].get(key) or device['m_Name'].removesuffix(' - DeviceInfo').replace('_',' ')
    hub = [json.loads(p.read_text('utf-8-sig')) for p in (app_root / 'achievement-hub').glob('*.json')]
    hub = next(h for h in hub if h['Provider'] == 'steam' and '3812600' in h['Games'])
    provider = {r['ProviderAchievementId']: r for r in hub['Games']['3812600']['Rows']}
    achievements = {int(t['m_Name'].split(' - ')[1].split()[0]): t for t in data.values() if 'steamId' in t}
    def deref(p):
        assert p['m_FileID'] == 0
        return data[p['m_PathID']]
    devices = [deref(p) for p in named['DeviceInfoDatabase']['devices']]
    licenses = []
    for p in named['ElementsShop - Shop']['licenses']:
        lic = deref(p['License'])
        device = deref(lic['deviceInfo'])
        licenses.append({'id': lic['id'], 'name': device_name(device), 'price': p['Price']})
    config = {'version': 1, 'account': hub['Account'], 'gameId': '3812600',
              'saveRoot': r'C:\Users\justc\AppData\LocalLow\Mandragora\Restory\SaveData',
              'profile': 1, 'gameVersion': '1.0.033r', 'trackers': []}
    # Numbers are the installed game's asset indexes; links use existing Steam IDs.
    specs = [(18,'cleaned',100,'Parts cleaned'),(19,'cleaned',1000,'Parts cleaned'),
             (31,'email',25,'Email orders completed'),(32,'email',100,'Email orders completed'),
             (4,'market',5,'Marketplace devices purchased'),(25,'market',25,'Marketplace devices purchased'),
             (5,'sold',5,'Devices sold'),(26,'earned',1000000,'Total yen earned'),
             (40,'daily',100000,'Yen earned today'),(23,'hacked',1,'Devices reprogrammed'),
             (24,'hacked',25,'Devices reprogrammed'),(38,'stickers',1,'Stickers applied'),
             (39,'stickers',25,'Stickers applied'),(37,'stickers',100,'Stickers applied'),
             (44,'boxes',1,'Parts boxes purchased'),(45,'boxes',50,'Parts boxes purchased'),
             (34,'reviews',5,'Reviews received'),(35,'reviews',25,'Reviews received'),
             (36,'reviews',50,'Reviews received'),(2,'receivedLicense',1,'Licenses received'),
             (10,'licenseCount',5,'Active licenses'),(11,'licenseChecklist',len(licenses),'Licenses'),
             (7,'winCount',1,'Different device competitions won'),(8,'winCount',3,'Different device competitions won'),
             (9,'winChecklist',len(devices),'Device competitions')]
    specs = [s for s in specs if s[0] not in (23,38,44,2,7)]  # Single-action goals use achievements alone.
    for num, metric, target, label in specs:
        asset = achievements[num]
        row = provider[asset['steamId']]
        definition = {'Id': 'restory.' + asset['steamId'].lower().removeprefix('ach_').replace('_','-'),
                      'Provider':'steam','GameId':'3812600','GameTitle':'Restory',
                      'AchievementId':row['ProviderAchievementId'],'Title':row['Title'],
                      'Description':row['Description'],'CompletionLabel':'Completed',
                      'Milestones':[],'CategoryTargets':{},'Items':[]}
        if metric == 'licenseChecklist':
            definition['Items'] = [{'Id': l['id'], 'Name': l['name'] + ' license', 'Category':'Licenses',
                                    'Detail':f"Activate this license. Shop price: {l['price']:,} yen.",
                                    'Target':1,'Order':i} for i,l in enumerate(sorted(licenses,key=lambda x:x['name']))]
        elif metric == 'winChecklist':
            definition['Items'] = [{'Id': d['id'],'Name':device_name(d),
                                    'Category':'Assembly competitions','Detail':'Win an assembly competition for this device.',
                                    'Target':1,'Order':i} for i,d in enumerate(sorted(devices,key=device_name))]
        else:
            detail = row['Description']
            if metric == 'daily': detail = 'Earn 100,000 yen in one day. This counter resets at the start of a new in-game day.'
            if metric == 'receivedLicense': detail = 'Receive a license delivery. Active licenses also confirm receipt.'
            definition['Items'] = [{'Id':metric,'Name':label,'Detail':detail,'Target':target,'Order':0}]
        config['trackers'].append({'metric':metric,'definition':definition})
    config['licenseIds'] = [l['id'] for l in licenses]
    config['allLicenseIds'] = [t['id'] for t in data.values() if 'licenseLocalizationKey' in t]
    config['deviceIds'] = [d['id'] for d in devices]
    assert len(config['trackers']) == 20
    assert len({t['definition']['AchievementId'] for t in config['trackers']}) == 20
    output.mkdir(parents=True,exist_ok=True)
    (output/'config.json').write_text(json.dumps(config,indent=2,ensure_ascii=False),encoding='utf-8')
    print(json.dumps({'trackers':20,'shopLicenses':len(licenses),'competitionDevices':len(devices),'output':str(output)}))


if __name__ == '__main__':
    a=argparse.ArgumentParser();a.add_argument('game_root',type=Path);a.add_argument('app_root',type=Path);a.add_argument('output',type=Path)
    args=a.parse_args();build(args.game_root,args.app_root,args.output)
