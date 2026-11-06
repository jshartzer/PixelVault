"""Read-only TCG save bridge for PixelVault. Writes only its own exports and snapshots."""
import argparse
from collections import Counter, defaultdict
from datetime import datetime, timezone
import hashlib
import json
import math
import os
from pathlib import Path
import re
import time

TICKS_EPOCH = 621355968000000000
REVENUE = ('totalItemEarning','totalCardEarning','totalPlayTableEarning')
COSTS = ('supplyCost','upgradeCost','employeeCost','rentCost','billCost')
_assembly_cache = {}


def atomic_json(path, value):
    path.parent.mkdir(parents=True,exist_ok=True)
    temporary=path.with_suffix(path.suffix+'.tmp')
    temporary.write_text(json.dumps(value,ensure_ascii=False,indent=2,allow_nan=False),encoding='utf-8')
    os.replace(temporary,path)


def integer(value, name, minimum=0, maximum=1000000):
    if type(value)!=int or not minimum<=value<=maximum: raise ValueError('Invalid '+name)
    return value


def number(value,name):
    if type(value) not in (int,float) or not math.isfinite(value): raise ValueError('Invalid '+name)
    return value


def report(value):
    result=dict(value)
    revenue=sum(number(value[k],k) for k in REVENUE)
    costs=sum(number(value[k],k) for k in COSTS)
    result.update(revenue=revenue,costs=costs,netProfit=revenue+costs,
                  netMargin=(revenue+costs)/revenue if revenue>0 else None)
    return result


def extract(save, config):
    if save['m_PlayerName'] != config['shopName']: raise ValueError('Different shop in autosave; choose its profile explicitly')
    if save['m_IsMainGame'] is not True: raise ValueError('Unsupported game mode')
    day=integer(save['m_CurrentDay'],'day')+1
    level=integer(save['m_ShopLevel'],'shop level')+1
    integer(save['m_SaveIndex'],'save index')
    cash=number(save['m_CoinAmountDouble'],'cash')
    for suffix,length in config['allSetLengths'].items():
        quantities=save['m_CardCollectedList'+suffix];seen=save['m_IsCardCollectedList'+suffix]
        if len(quantities)!=2100 or len(seen)!=2100: raise ValueError('Unexpected card array length')
        for n in quantities: integer(n,'card quantity')
        if any(type(v)!=bool for v in seen): raise ValueError('Invalid discovery flag')
        if any(quantities[length:]) or any(seen[length:]): raise ValueError('Previously unused card slot now active; revalidate catalogue')
    album=sum(sum(q>0 for q in save['m_CardCollectedList'+suffix][:length])
              for suffix,length in config['allSetLengths'].items())
    cards=[];set_summary={}
    for cardset in config['sets']:
        rows=[]
        for card in cardset['cards']:
            suffix,index=card['suffix'],card['index']
            quantities=save['m_CardCollectedList'+suffix];seen=save['m_IsCardCollectedList'+suffix]
            price=number(save['m_GenCardMarketPriceList'+suffix][index]['generatedMarketPrice'],'card market price')
            rows.append({**card,'set':cardset['title'],'collectedBefore':seen[index],
                         'binderQuantity':quantities[index], 'rawGeneratedMarketPrice':price,
                         'rawAskingPrice':number(save['m_CardPriceSetList'+suffix][index],'card asking price')})
        cards.extend(rows)
        set_summary[cardset['key']]={'total':len(rows),'collectedBefore':sum(r['collectedBefore'] for r in rows),
                                     'inBinder':sum(r['binderQuantity']>0 for r in rows),
                                     'binderCopies':sum(r['binderQuantity'] for r in rows)}
    shelves=Counter();boxed_shelves=Counter();warehouse=Counter();delivery=Counter();reserved=Counter();customers=Counter()
    product_count=138
    def count(target,item,amount):
        integer(item,'item type',-1,product_count-1);integer(amount,'stock quantity')
        if item>=0: target[item]+=amount
        elif amount: raise ValueError('Unidentified nonempty stock slot')
    for field in ['m_ShelfSaveDataList','m_CardItemCombiShelfSaveDataList']:
        for shelf in save[field]:
            for entry in shelf['itemTypeAmountList']:
                count(boxed_shelves if shelf['isBoxed'] else shelves,entry['itemType'],entry['amount'])
    for box in save['m_PackageBoxItemSaveDataList']:
        entry=box['itemTypeAmount'];count(warehouse if box['isStored'] else delivery,entry['itemType'],entry['amount'])
    for field in ['m_AutoPackOpenerSaveDataList','m_WorkbenchSaveDataList']:
        for machine in save[field]:
            for item in machine['itemTypeList']: count(reserved,item,1)
    for item in save['m_HoldItemTypeList']: count(reserved,item,1)
    for customer in save['m_CustomerSaveDataList']:
        for field in ['itemInBagList','itemInHandList']:
            for item in customer[field]: count(customers,item,1)
    products=[]
    for product in config['products']:
        i=product['id'];quantity=shelves[i]+warehouse[i]+delivery[i]+boxed_shelves[i]
        flags=[save['m_IsItemLicenseUnlocked'][j] for j in product['licenses']]
        if any(type(v)!=bool for v in flags): raise ValueError('Invalid license flag')
        row={**product,'availableUnits':quantity,'shelfUnits':shelves[i],'storedBoxUnits':warehouse[i],
             'deliveryBoxUnits':delivery[i],'boxedShelfUnits':boxed_shelves[i],
             'reservedUnits':reserved[i],'customerHeldUnits':customers[i],
             'licenseUnlocked':any(flags),'shortfall':max(0,product['carton']-quantity)}
        for key,field in [('rawAskingPrice','m_SetItemPriceList'),('rawAverageUnitCost','m_AverageItemCostList'),
                          ('rawGeneratedUnitCost','m_GeneratedCostPriceList'),('rawGeneratedMarketPrice','m_GeneratedMarketPriceList')]:
            row[key]=number(save[field][i],key)
        products.append(row)
    today=report(save['m_GameReportDataCollect']);permanent=report(save['m_GameReportDataCollectPermanent'])
    past=save['m_GameReportDataCollectPastList']
    if not isinstance(past,list) or not 1<=len(past)<=30: raise ValueError('Missing or invalid completed daily reports')
    reports=[dict(report(r),day=day-len(past)+i) for i,r in enumerate(past)]
    totals={k:sum(r[k] for r in reports) for k in REVENUE+COSTS}
    recent=report(totals)
    bills=save['m_BillList'];debt=sum(number(b['amountToPay'],'bill') for b in bills)
    grades=defaultdict(set)
    for card in save['m_GradedCardInventoryList']:
        index=integer(card['cardSaveIndex'],'graded card index',0,2099)
        # CompactCardDataAmount.amount is the grade here, NOT a quantity.
        grades[index].add(integer(card['amount'],'grade',1,10))
    expansion_suffix={0:'',1:'Destiny',2:'Ghost',3:'Megabot',4:'FantasyRPG',5:'CatJob',7:'Ascension'}
    all_monsters=config.get('shownMonsters',{})
    for card in save['m_HoldCardDataList']:
        expansion=card['expansionType'];suffix=expansion_suffix[expansion]
        shown=all_monsters[suffix];position=shown.index(card['monsterType'])
        width=2 if expansion==2 else 12
        index=position*width+card['borderType']+(width//2 if card['isFoil'] else 0)
        # The installed achievement code also includes held grade-zero cards. Match that code.
        grades[index].add(integer(card['cardGrade'],'held grade',0,10))
    best_index=min(grades,key=lambda i:(-len(grades[i]),i)) if grades else None
    best_grades=sorted(grades[best_index]) if best_index is not None else []
    finance={'cash':cash,'outstandingBills':debt,'cashAfterBills':cash-debt,'bills':bills,
             'currentDay':today,'completedReports':reports,'recentTotals':recent,'lifetimeReport':permanent,
             'reportWindowStartDay':reports[0]['day'],'reportWindowEndDay':reports[-1]['day']}
    metrics={'album':album,'level':level,
             'gem':integer(permanent['gemMintCardObtained'],'Gem Mint count'),
             'duels':integer(today['duelWinCount'],'duel achievement counter'),
             'grades':len(best_grades)}
    checks={'cash-bills':cash>=debt,'bills-paid':debt<=0,'today-profit':today['netProfit']>0,
            'last-profit':reports[-1]['netProfit']>0,'recent-profit':recent['netProfit']>0,
            'recent-margin':recent['netMargin'] is not None and recent['netMargin']>=0.1}
    return {'shop':save['m_PlayerName'],'day':day,'level':level,'saveIndex':save['m_SaveIndex'],
            'cards':cards,'collectionSummary':set_summary,'products':products,'finances':finance,
            'achievementCounters':metrics,'financeChecks':checks,
            'gradeLeader':{'cardSaveIndex':best_index,'grades':best_grades,
                           'missingGrades':[g for g in range(1,11) if g not in best_grades]}}


def finance_text(data):
    f=data['finances'];t=f['currentDay'];r=f['recentTotals'];last=f['completedReports'][-1]
    return (f"{data['shop']} | Day {data['day']} | Level {data['level']}\n"
            f"Cash ${f['cash']:,.2f}; unpaid bills ${f['outstandingBills']:,.2f}; after bills ${f['cashAfterBills']:,.2f}.\n"
            f"Today (partial): revenue ${t['revenue']:,.2f}; costs ${-t['costs']:,.2f}; net ${t['netProfit']:,.2f}.\n"
            f"Last completed day {last['day']}: net ${last['netProfit']:,.2f}.\n"
            f"Completed days {f['reportWindowStartDay']}-{f['reportWindowEndDay']}: revenue ${r['revenue']:,.2f}; costs ${-r['costs']:,.2f}; net ${r['netProfit']:,.2f}; margin {100*(r['netMargin'] or 0):.1f}%.\n"
            f"Today cost detail: supplies ${-t['supplyCost']:,.2f}, upgrades ${-t['upgradeCost']:,.2f}, wages ${-t['employeeCost']:,.2f}, rent ${-t['rentCost']:,.2f}, bills ${-t['billCost']:,.2f}.")


def snapshots(data, config, digest, ticks):
    by_id={r['id']:r for r in data['cards']};products={'product-'+str(p['id']):p for p in data['products']}
    result={}
    base=f"Read-only autosave; day {data['day']}; save index {data['saveIndex']}; SHA256 {digest}."
    for tracker in config['trackers']:
        d=tracker['definition'];metric=tracker['metric'];items=[];evidence=base
        if metric=='collection':
            for item in d['Items']:
                card=by_id[item['GroupId']]
                if not item['Id'].endswith(('.ever','.owned')): raise ValueError('Unknown collection milestone identity')
                value=card['collectedBefore'] if item['Id'].endswith('.ever') else card['binderQuantity']>0
                items.append({'ItemId':item['Id'],'Current':int(value),'Availability':0})
            summary=data['collectionSummary'][tracker['set']]
            evidence+='\n'+json.dumps(summary)+'\nCollection is historical; binder ownership can decrease after sales, transfers or grading.'
        elif metric=='stock':
            for item in d['Items']:
                product=products[item['Id']]
                items.append({'ItemId':item['Id'],'Current':product['availableUnits'],
                              'Availability':1 if product['licenseUnlocked'] else 2})
            shortages=[p for p in data['products'] if p['shortfall'] and p['licenseUnlocked']]
            evidence+='\nOne-carton shortages among unlocked products: '+('; '.join(f"{p['name']}: {p['availableUnits']}/{p['carton']}" for p in shortages) or 'none')
            evidence+='\nCounts come from shelves and package boxes. Reserved and customer-held goods excluded. Full stock/prices: stock-latest.json and ledger-latest.txt.'
        elif metric=='finance':
            items=[{'ItemId':i['Id'],'Current':int(data['financeChecks'][i['Id']]),'Availability':0} for i in d['Items']]
            evidence+='\n'+finance_text(data)
        else:
            items=[{'ItemId':d['Items'][0]['Id'],'Current':data['achievementCounters'][metric],'Availability':0}]
            if metric=='grades': evidence+='\nLeading grade group (matches installed check): '+json.dumps(data['gradeLeader'])
            if metric=='album': evidence+='\nDistinct ungraded binder variants across all supported expansions. Historical collection flags are not counted.'
            if metric=='duels': evidence+='\nUses m_GameReportDataCollect.duelWinCount, the exact counter passed to OnDuelWin; this field is not cleared by this build’s daily ResetData.'
        if len(evidence)>4000: raise ValueError('Source evidence exceeds PixelVault limit')
        result[d['Id']]={'Version':1,'TrackerId':d['Id'],'Provider':'steam','GameId':config['gameId'],
                         'Account':config['account'],'Source':'TCG Card Shop Simulator — autosave JSON',
                         'Evidence':evidence,'ObservedUtcTicks':ticks,'Items':items}
    return result


def run_once(root):
    config_bytes=(root/'config.json').read_bytes();config=json.loads(config_bytes)
    cloud=(Path(config['saveRoot'])/'steam_autocloud.vdf').read_text('utf-8-sig')
    match=re.search(r'"accountid"\s+"(\d+)"',cloud)
    if not match or int(match[1])+76561197960265728!=int(config['account']): raise ValueError('Steam account mismatch')
    assembly=Path(config['gameAssembly']);stat=assembly.stat();cache_key=(str(assembly),stat.st_size,stat.st_mtime_ns)
    if cache_key not in _assembly_cache: _assembly_cache[cache_key]=hashlib.sha256(assembly.read_bytes()).hexdigest()
    if _assembly_cache[cache_key]!=config['assemblySha256']: raise ValueError('Game assembly changed; revalidate reader')
    for asset in config['assetFiles']:
        stat=Path(asset['path']).stat()
        if stat.st_size!=asset['size'] or stat.st_mtime_ns!=asset['mtimeNs']: raise ValueError('Game catalogue changed; re-extract and validate')
    path=Path(config['saveRoot'])/config['saveFile'];before=path.stat()
    if time.time_ns()-before.st_mtime_ns<2_000_000_000: return {'status':'waiting-for-stable-save'}
    if not 10000<=before.st_size<=32*1024*1024: raise ValueError('Invalid save size')
    raw=path.read_bytes();after=path.stat()
    if (before.st_size,before.st_mtime_ns)!=(after.st_size,after.st_mtime_ns): raise ValueError('Save changed during read')
    digest=hashlib.sha256(raw).hexdigest();config_digest=hashlib.sha256(config_bytes).hexdigest()
    state_path=root/'reader-state.json';state=json.loads(state_path.read_text()) if state_path.exists() else {}
    if state.get('digest')==digest and state.get('configDigest')==config_digest and all((root/'progress'/(t['definition']['Id']+'.json')).exists() for t in config['trackers']):
        return {'status':'unchanged','save':path.name,'digest':digest,'day':state['day']}
    if state.get('saveMtimeNs',0)>before.st_mtime_ns: raise ValueError('Older autosave timestamp; preserve last observations')
    save=json.loads(raw);data=extract(save,config)
    ticks=TICKS_EPOCH+before.st_mtime_ns//100
    output=snapshots(data,config,digest,ticks)
    metadata={'save':str(path),'saveSha256':digest,'saveTimeUtc':datetime.fromtimestamp(before.st_mtime,timezone.utc).isoformat(),
              'day':data['day'],'shop':data['shop'],'level':data['level']}
    # Validate every snapshot before publishing any; game and save files are never written.
    for tracker_id,value in output.items(): atomic_json(root/'progress'/(tracker_id+'.json'),value)
    for name,value in [('cards',data['cards']),('stock',data['products']),('finances',data['finances'])]:
        atomic_json(root/(name+'-latest.json'),{'source':metadata,'data':value})
    summary={**metadata,'collections':data['collectionSummary'],'achievements':data['achievementCounters'],
             'gradeLeader':data['gradeLeader'],'finances':data['finances'],'stockUnits':sum(p['availableUnits'] for p in data['products'])}
    atomic_json(root/'summary-latest.json',summary)
    atomic_json(root/'history'/(digest+'.json'),summary)
    lines=['TCG CARD SHOP SIMULATOR — EXTRACTED LEDGER', 'Source save: '+metadata['saveTimeUtc'],
           finance_text(data),'','DAILY REPORTS (completed days; current day is excluded)',
           'Day       Revenue        Costs     Net profit']
    lines += [f"{r['day']:3d} {r['revenue']:13,.2f} {-r['costs']:12,.2f} {r['netProfit']:14,.2f}" for r in data['finances']['completedReports']]
    lines += ['','STOCK (units; price fields are raw saved values before game rounding/fallbacks)',
              'Product                                      Available Shelf Stored Boxes Target Ask price']
    lines += [f"{p['name'][:44]:44s} {p['availableUnits']:7d} {p['shelfUnits']:5d} {p['storedBoxUnits']:6d} {p['deliveryBoxUnits']:5d} {p['carton']:6d} {p['rawAskingPrice']:9.2f}" for p in data['products']]
    lines += ['','COLLECTIONS (historical / total; currently in binder)']
    lines += [f"{name}: {v['collectedBefore']}/{v['total']}; {v['inBinder']} currently held variants; {v['binderCopies']:,} ungraded binder copies" for name,v in data['collectionSummary'].items()]
    lines += ['','ACHIEVEMENT COUNTERS',json.dumps(data['achievementCounters']),json.dumps(data['gradeLeader']),
              'Card collection achievements use currently held distinct binder variants.',
              'Stock reserve targets are one smallest order carton; finance health checks are planning checks, not achievements.',
              'Prices are raw saved fields. No sale value or stock value is inferred from them.']
    text=root/'ledger-latest.txt';temp=text.with_suffix('.tmp');temp.write_text('\n'.join(lines),encoding='utf-8');os.replace(temp,text)
    result={'status':'success','digest':digest,'configDigest':config_digest,'saveMtimeNs':before.st_mtime_ns,
            'day':data['day'],'trackers':len(output),'save':path.name,'updatedUtc':time.time()}
    atomic_json(state_path,result)
    return result


def main():
    parser=argparse.ArgumentParser();parser.add_argument('root',type=Path);parser.add_argument('--watch',action='store_true')
    args=parser.parse_args();root=args.root.resolve();lock=None
    if args.watch:
        import msvcrt
        lock=(root/'watcher.lock').open('a+b');lock.seek(0)
        if not lock.read(1): lock.write(b'0');lock.flush()
        lock.seek(0)
        try: msvcrt.locking(lock.fileno(),msvcrt.LK_NBLCK,1)
        except OSError: return
    while True:
        try: status=run_once(root)
        except Exception as error:
            status={'status':'error','message':str(error)}
            atomic_json(root/'status.json',{**status,'pid':os.getpid(),'checkedUtc':time.time()})
            if not args.watch: raise
        atomic_json(root/'status.json',{**status,'pid':os.getpid(),'checkedUtc':time.time()})
        if not args.watch: print(json.dumps(status));break
        time.sleep(10)


if __name__=='__main__':main()
