"""Read-only Restory save bridge. Publishes atomic observations for PixelVault's JSON adapter.

No achievements file is read and no game/save/database file is written.
"""
import argparse
import hashlib
import json
import os
import re
import time
import zlib
from pathlib import Path

TICKS_EPOCH = 621355968000000000


def atomic_json(path, value):
    path.parent.mkdir(parents=True,exist_ok=True)
    temp=path.with_suffix(path.suffix+'.tmp')
    temp.write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')
    os.replace(temp,path)


def walk(value):
    if isinstance(value,dict):
        yield value
        for v in value.values(): yield from walk(v)
    elif isinstance(value,list):
        for v in value: yield from walk(v)


def decode(raw):
    if len(raw)<29 or len(raw)>32*1024*1024 or raw[:3]!=b'RST' or raw[11:14]!=b'\x1f\x8b\x08':
        raise ValueError('Unsupported Restory save header')
    decoder=zlib.decompressobj(31)
    payload=decoder.decompress(raw[11:],64*1024*1024+1)
    if len(payload)>64*1024*1024 or not decoder.eof: raise ValueError('Incomplete or oversized save')
    save=json.loads(payload)
    if save.get('GameMode')!='Story' or save.get('$type')!='Restory.Data.SaveLoad.Containers.SaveSystemSaveData':
        raise ValueError('Unsupported save type')
    if save.get('GameVersion')!='1.0.033r': raise ValueError('Game version changed; reader needs validation')
    nodes=list(walk(save));ids={n['$id']:n for n in nodes if '$id' in n}
    if any(n['$ref'] not in ids for n in nodes if '$ref' in n): raise ValueError('Unresolved save reference')
    return save,nodes,ids


def measurements(nodes,ids,config):
    def typed(name):
        found=[n for n in nodes if n.get('$type','').rsplit('.',1)[-1]==name]
        if len(found)!=1: raise ValueError('Missing or ambiguous '+name)
        return found[0]
    def resolve(n):
        seen=set()
        while isinstance(n,dict) and '$ref' in n:
            ref=n['$ref']
            if ref in seen: raise ValueError('Reference cycle')
            seen.add(ref);n=ids[ref]
        return n
    def objects(values):
        if not isinstance(values,list): raise ValueError('Expected a saved collection')
        return {resolve(n)['ItemObjectID'] for n in values}
    stats=typed('GameStatisticsSaveData')
    d=stats['DevicesStatistics'];s=stats['StoreStatistics'];m=stats['MoneyStatistics']
    licenses=objects(typed('LicensesServiceSaveData')['ActiveLicenses'])
    wins=objects(stats['CompetitionsStatistics']['WonCompetitionsByDevice'])
    registry=typed('InteractiveObjectRegistrySaveData')['InteractiveObjects']
    received=licenses | {resolve(n['InteractiveObjectInfo'])['ItemObjectID'] for n in registry}
    result={'cleaned':d['CleanedElementsCount'],'email':stats['OrdersStatistics']['CompletedEmailOrdersCount'],
            'market':s['PurchasedDevicesInFleaMarketCount'],'sold':s['SoldDevicesCount'],
            'earned':m['EarnedMoneyAmount'],'daily':m['EarnedMoneyAmountPerDay'],
            'hacked':d['HackedDevicesCount'],'stickers':d['AppliedStickersCount'],
            'boxes':s['PurchasedElementsBoxesInFleaMarketCount'],
            'reviews':len(typed('WorkshopRatingsServiceSaveData')['Reviews']),
            'receivedLicense':int(bool(received & set(config['allLicenseIds']))),
            'licenseCount':len(licenses),'winCount':len(wins),
            'licenseChecklist':licenses,'winChecklist':wins}
    for key,value in result.items():
        if not isinstance(value,set) and (type(value)!=int or value<0): raise ValueError('Invalid counter '+key)
    return result


def run_once(root):
    config=json.loads((root/'config.json').read_text('utf-8'))
    save_root=Path(config['saveRoot'])
    cloud=(save_root/'steam_autocloud.vdf').read_text('utf-8-sig')
    match=re.search(r'"accountid"\s+"(\d+)"',cloud)
    if not match or int(match[1])+76561197960265728!=int(config['account']):
        raise ValueError('Restory save belongs to a different Steam account')
    profile=json.loads((save_root/'CurrentProfileIndex.release.json').read_text('utf-8-sig'))
    if profile['Profile']!=config['profile']: raise ValueError('Different Restory profile selected')
    candidates=[p for p in save_root.glob('Story.slot*.release-No-*.restory') if re.fullmatch(r'Story\.slot\d+\.release-No-\d+\.restory',p.name)]
    if not candidates: raise ValueError('No story saves found')
    # Newest written checkpoint is authoritative, including a lower-progress replay.
    chosen=max(candidates,key=lambda p:(p.stat().st_mtime_ns,int(re.search(r'-No-(\d+)',p.name)[1])))
    before=chosen.stat()
    if time.time_ns()-before.st_mtime_ns<2_000_000_000: return {'status':'waiting-for-stable-save'}
    raw=chosen.read_bytes();after=chosen.stat()
    if before.st_mtime_ns!=after.st_mtime_ns or before.st_size!=after.st_size: raise ValueError('Save changed during read')
    if json.loads((save_root/'CurrentProfileIndex.release.json').read_text('utf-8-sig'))!=profile:
        raise ValueError('Profile changed during read')
    digest=hashlib.sha256(raw).hexdigest()
    state_path=root/'reader-state.json'
    state=json.loads(state_path.read_text('utf-8')) if state_path.exists() else {}
    config_digest=hashlib.sha256((root/'config.json').read_bytes()).hexdigest()
    paths=[root/'progress'/(t['definition']['Id']+'.json') for t in config['trackers']]
    if state.get('digest')==digest and state.get('configDigest')==config_digest and all(p.exists() for p in paths):
        return {'status':'unchanged','save':chosen.name,'digest':digest}
    save,nodes,ids=decode(raw)
    counts=measurements(nodes,ids,config)
    ticks=TICKS_EPOCH+before.st_mtime_ns//100
    for tracker,path in zip(config['trackers'],paths):
        definition=tracker['definition'];value=counts[tracker['metric']]
        items=[{'ItemId':item['Id'],'Current':int(item['Id'] in value) if isinstance(value,set) else min(value,item['Target']),
                'Availability':0} for item in definition['Items']]
        atomic_json(path,{'Version':1,'TrackerId':definition['Id'],'Provider':'steam','GameId':config['gameId'],
                         'Account':config['account'],'Source':'Restory local save — '+chosen.name,
                         'Evidence':f"Read-only Restory 1.0.033r save; iteration {save['Iteration']}; SHA256 {digest}; metric {tracker['metric']}",
                         'ObservedUtcTicks':ticks,'Items':items})
    state={'digest':digest,'configDigest':config_digest,'save':chosen.name,'iteration':save['Iteration'],
           'saveTimeTicks':ticks,'updatedUtc':time.time(),'trackers':len(paths),'status':'success'}
    atomic_json(state_path,state)
    return state


def main():
    parser=argparse.ArgumentParser();parser.add_argument('root',type=Path);parser.add_argument('--watch',action='store_true')
    args=parser.parse_args();root=args.root.resolve()
    # A single startup watcher per bridge; the operating system releases the lock on exit.
    lock=None
    if args.watch:
        import msvcrt
        lock=(root/'watcher.lock').open('a+b');lock.seek(0)
        if lock.read(1)==b'': lock.write(b'0');lock.flush()
        lock.seek(0)
        try: msvcrt.locking(lock.fileno(),msvcrt.LK_NBLCK,1)
        except OSError: return
    while True:
        try:
            status=run_once(root)
        except Exception as error:
            status={'status':'error','message':str(error)}
            if not args.watch: raise
        atomic_json(root/'status.json',{**status,'pid':os.getpid(),'checkedUtc':time.time()})
        if not args.watch:
            print(json.dumps(status));break
        time.sleep(10)


if __name__=='__main__':main()
