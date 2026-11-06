"""Extract private TCG catalogues from the installed Unity assets; never modify the game."""
import argparse
import hashlib
import json
import struct
from pathlib import Path


class Binary:
    def __init__(self, data, offset=0): self.data, self.pos = data, offset
    def integer(self):
        value = struct.unpack_from('<i', self.data, self.pos)[0]; self.pos += 4
        return value
    def string(self):
        size = self.integer()
        if not 0 <= size <= 100000: raise ValueError('Invalid serialized string')
        value = self.data[self.pos:self.pos+size].decode('utf-8')
        self.pos = (self.pos+size+3)//4*4
        return value
    def array(self, read):
        size = self.integer()
        if not 0 <= size <= 10000: raise ValueError('Invalid serialized array')
        return [read() for _ in range(size)]
    def skip(self, count): self.pos += count
    def pointer(self): self.skip(12)


def monsters(raw):
    # Unity's serialized layout, verified against MonsterData and Stats in this build.
    b = Binary(raw, 28); name = b.string()
    if name != 'SO_MonsterData': raise ValueError('Unexpected monster asset')
    result = {key: b.array(b.integer) for key in ['m_ShownMonsterList', 'm_ShownGhostMonsterList',
              'm_ShownMegabotList', 'm_ShownFantasyRPGList', 'm_ShownCatJobList']}
    def monster():
        value = {'Name': b.string(), 'ArtistName': b.string(), 'ArtistNameList': b.array(b.string),
                 'Description': b.string(), 'GenericTextList': b.array(b.string)}
        b.skip(12)
        for key in ['ElementIndex','Rarity','MonsterType','NextEvolution','PreviousEvolution']:
            value[key] = b.integer()
        value['Roles'] = b.array(b.integer)
        b.skip(64); b.array(b.integer); b.pointer(); b.pointer(); b.array(b.pointer); b.array(b.pointer)
        return value
    for key in ['m_DataList','m_MegabotDataList','m_FantasyRPGDataList','m_CatJobDataList']:
        result[key] = b.array(monster)
    result['packSettings'] = pack_settings(b)
    if [len(result[k]) for k in list(result)[:5]] != [121,26,113,50,40]:
        raise ValueError('Card catalogue changed; revalidate mapping')
    return result


def pack_settings(b):
    # Remaining MonsterData fields through CardUISetting, from this assembly.
    b.array(lambda:b.skip(16))  # rarity colors
    for _ in range(3): b.array(b.pointer)
    b.array(lambda:(b.string(),b.skip(8)))  # grading service name, days, cost
    def setting_data():
        b.array(b.integer)
        b.array(lambda:b.skip(48))  # two aligned bools, two Vector3s, material, style
        b.array(b.pointer);b.array(b.pointer)
        b.array(b.string);b.array(b.string)
        b.array(b.pointer);b.array(b.pointer)
        b.skip(36+80)  # three sprites, five colors
        for _ in range(19):
            if b.integer() not in (0,1): raise ValueError('Invalid card UI flag')
        b.skip(30*12)  # Vector3 offsets
        for _ in range(4): b.array(b.pointer)
    def setting():
        value=dict(zip(['expansionType','openPackCanUseRarity','openPackCanHaveDuplicate'],
                       [b.integer(),b.integer(),b.integer()]))
        if any(value[k] not in (0,1) for k in ['openPackCanUseRarity','openPackCanHaveDuplicate']):
            raise ValueError('Invalid pack flag')
        b.skip(16);b.array(setting_data)
        return value
    result=b.array(setting)
    if len(result)!=8: raise ValueError('Pack settings changed')
    return result


def english(raw):
    b = Binary(raw, 28)
    if b.string() != 'I2Languages': raise ValueError('Unexpected language asset')
    b.skip(12)  # Three separately aligned serialized booleans.
    result = {}
    for _ in range(b.integer()):
        key = b.string(); b.integer(); values = b.array(b.string)
        size = b.integer(); b.skip(size); b.pos = (b.pos+3)//4*4
        b.array(b.string)
        result[key] = values[0] if values else key
    return result


def build(game, app, root):
    import UnityPy
    from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
    data = game/'Card Shop Simulator_Data'
    env = UnityPy.load(str(data/'sharedassets2.assets'))
    generator = TypeTreeGenerator(next(iter(env.objects)).assets_file.unity_version)
    generator.load_local_dll_folder(str(data/'Managed')); env.typetree_generator = generator
    objects = next(f for f in env.files.values() if hasattr(f, 'objects') and 102233 in f.objects).objects
    monster = monsters(objects[102233].get_raw_data())
    stock = objects[102251].read_typetree()
    if stock['m_Name'] != 'StockItemData_ScriptableObjectOptimized': raise ValueError('Unexpected stock asset')
    locale_env = UnityPy.load(str(data/'resources.assets'))
    locale = next(o for o in locale_env.objects if o.path_id == 2943 and o.type.name == 'MonoBehaviour')
    translations = english(locale.get_raw_data())
    def label(value): return translations.get(value) or value
    hubs = [json.loads(p.read_text('utf-8-sig')) for p in (app/'achievement-hub').glob('*.json')]
    hub = next(h for h in hubs if h.get('Provider') == 'steam' and '3070070' in h.get('Games', {}))
    achievements = {r['ProviderAchievementId']: r for r in hub['Games']['3070070']['Rows']}
    save_root = Path.home()/'AppData/LocalLow/OPNeonGames/Card Shop Simulator'
    save = json.loads((save_root/'savedGames_Release0.json').read_text('utf-8'))
    config = {'version': 1, 'account': hub['Account'], 'gameId': '3070070', 'saveRoot': str(save_root),
              'saveFile': 'savedGames_Release0.json', 'shopName': save['m_PlayerName'],
              'gameAssembly': str(data/'Managed/Assembly-CSharp.dll'),
              'assemblySha256': hashlib.sha256((data/'Managed/Assembly-CSharp.dll').read_bytes()).hexdigest(),
              'trackers': [], 'sets': [], 'products': [], 'allSetLengths': {
                  '':1452,'Destiny':1452,'Ghost':52,'GhostBlack':52,'Ascension':1452,
                  'Megabot':1356,'FantasyRPG':600,'CatJob':480}}
    config['assetFiles'] = [{'path':str(data/name),'size':(data/name).stat().st_size,
                             'mtimeNs':(data/name).stat().st_mtime_ns}
                            for name in ['resources.assets','sharedassets2.assets']]
    config['shownMonsters'] = {suffix:monster[key] for suffix,key in {
        '':'m_ShownMonsterList','Destiny':'m_ShownMonsterList','Ascension':'m_ShownMonsterList',
        'Ghost':'m_ShownGhostMonsterList','Megabot':'m_ShownMegabotList',
        'FantasyRPG':'m_ShownFantasyRPGList','CatJob':'m_ShownCatJobList'}.items()}
    names = {m['MonsterType']: label(m['Name']) for key in ['m_DataList','m_MegabotDataList',
             'm_FantasyRPGDataList','m_CatJobDataList'] for m in monster[key]}
    borders = ['Base','First Edition','Silver','Gold','EX','Full Art']
    def definition(key, title, description, items, icon='checklist', achievement=None):
        return {'Id':'tcg.'+key,'Provider':'steam','GameId':'3070070','GameTitle':'TCG Card Shop Simulator',
                'Title':title,'Description':description,'IconId':icon,'AchievementId':achievement,
                'CompletionLabel':'Completed','Milestones':[],'CategoryTargets':{},'Items':items}
    # These expansions have obtainable packs in the active stock catalogue. Ghosts are pack inserts.
    for key, title, expansions in [('tetramon','Tetramon',[('',0)]),('destiny','Destiny',[('Destiny',1)]),
                                   ('ghost','Ghost',[('Ghost',2),('GhostBlack',2)]),('ascension','Ascension',[('Ascension',7)])]:
        rows = []; cards = []
        for suffix, expansion in expansions:
            shown = monster['m_ShownGhostMonsterList' if expansion == 2 else 'm_ShownMonsterList']
            width = 2 if expansion == 2 else 12
            for position, species in enumerate(shown):
                for variant in range(width):
                    index = position*width+variant
                    border = 'Full Art' if expansion == 2 else borders[variant%6]
                    foil = variant >= width//2
                    theme = ('Black' if suffix == 'GhostBlack' else 'White') if expansion == 2 else title
                    card_id = (suffix.lower() or 'tetramon')+'-'+str(index)
                    name = names[species]+' — '+border+(' Foil' if foil else '')
                    if expansion == 2: name += ' ('+theme+')'
                    cards.append({'id':card_id,'name':name,'suffix':suffix,'index':index,'monster':species,
                                  'expansion':expansion,'foil':foil,'border':variant%(width//2)})
                    for milestone in ['Collected']:
                        rows.append({'Id':card_id+('.ever' if milestone=='Collected' else '.owned'),
                                     'Name':name,'Category':theme+' / '+('Foil' if foil else 'Standard'),
                                     'Detail':'Collected uses the saved originally obtained indicator. Selling, displaying or grading the card does not remove collection credit.',
                                     'Target':1,'Order':len(rows),'GroupId':card_id,'Milestone':milestone})
        d = definition('collection.'+key,title+' — card collection',
                       'Completion follows Collected: whether each card variant has ever been obtained. Current binder ownership does not affect completion. Includes only real catalogue entries.',rows,'collectibles')
        d['Milestones']=['Collected']
        for card in rows: d['CategoryTargets'][card['Category']] = d['CategoryTargets'].get(card['Category'],0)+1
        config['sets'].append({'key':key,'title':title,'cards':cards})
        config['trackers'].append({'metric':'collection','set':key,'definition':d})
    # In-game m_CurrentTotalItemCountList is a costing cache and does not reconcile to physical stock.
    # Count shelves and package boxes instead. Use one smallest order carton as the initial reserve goal.
    retail_ids = list(dict.fromkeys(i for key in ['m_ShownAllItemType','m_ShownItemType',
                           'm_ShownAccessoryItemType','m_ShownFigurineItemType','m_ShownBoardGameItemType'] for i in stock[key]))
    categories = ['Supplies','Packs and decks','Deck boxes','Dice','Sleeves','Manga','Playmats','Figurines','Board games']
    for product_id in retail_ids:
        if product_id < 0: continue
        product = stock['m_ItemDataList'][product_id]
        licenses = [(i,r) for i,r in enumerate(stock['m_RestockDataList']) if r['itemType']==product_id]
        if not licenses: continue  # Crafted bulk boxes are not orderable retail stock.
        config['products'].append({'id':product_id,'name':label(product['name']),
                                  'category':categories[product['category']],'carton':min(r['amount'] for _,r in licenses),
                                  'licenses':[i for i,_ in licenses]})
    items = [{'Id':'product-'+str(p['id']),'Name':p['name'],'Category':str(p['category']),
              'Detail':f"Available units on shelves and in delivery/storage boxes. Initial reserve target: one smallest order carton ({p['carton']} units). Customer-held and machine-reserved goods excluded.",
              'Target':p['carton'],'Order':i} for i,p in enumerate(config['products'])]
    config['trackers'].append({'metric':'stock','definition':definition('stock','Stock — carton reserves',
        'Physical stock against one smallest order carton per active retail product. Current values are units, not percent. Source evidence lists shortages; full stock and prices are in the extracted ledger. This is a replenishment checklist, not an achievement.',items,'crafting')})
    checks = [('cash-bills','Cash covers outstanding bills','Cash balance is at least the total outstanding bills.'),
              ('bills-paid','Outstanding bills paid','No unpaid bill balance remains.'),
              ('today-profit','Current day profitable','Current-day revenue plus all recorded costs is positive; this is an unfinished trading day.'),
              ('last-profit','Last completed day profitable','The most recent completed daily report has positive net profit.'),
              ('recent-profit','Last 30 reports profitable overall','Net profit summed across available completed daily reports is positive.'),
              ('recent-margin','Last 30 reports have at least 10% net margin','Net profit / revenue is at least 10% across available completed reports. This is a planning target.')]
    config['trackers'].append({'metric':'finance','definition':definition('finance','Shop finances — health checks',
        'Reversible operating checks, not lifetime achievements. Current balances, earnings, expenses and net profit appear in Source evidence. The complete 30-report ledger is exported beside the tracker files.',
        [{'Id':key,'Name':name,'Detail':detail,'Target':1,'Order':i} for i,(key,name,detail) in enumerate(checks)],'building')})
    specs = [('ACH_CardCollector4','album',2500,'Distinct cards currently in binder'),
             ('ACH_CardCollector5','album',4000,'Distinct cards currently in binder'),
             ('ACH_ShopLevel4','level',100,'Displayed shop level'),
             ('ACH_GradingGem3','gem',500,'Gem Mint cards obtained'),
             ('ACH_TCGDuel','duels',50,'Duel wins in the achievement counter'),
             ('ACH_GradingCollector','grades',10,'Grades collected for one card')]
    for aid, metric, target, name in specs:
        row = achievements[aid]
        if row['Unlocked']: continue
        detail = row['Description']
        if metric=='album': detail += '. Counts distinct currently held ungraded binder variants, not copies or historical discovery flags.'
        if metric=='grades': detail += '. Follows the installed achievement check: grades in held cards and graded inventory, grouped by card save index. Source evidence identifies the leading group and missing grades.'
        d = definition('achievement.'+aid.lower().removeprefix('ach_'),row['Title'],detail,
                       [{'Id':metric,'Name':name,'Detail':detail,'Target':target,'Order':0}], None,aid)
        d['AggregateAsSingleItem']=True
        config['trackers'].append({'metric':metric,'definition':d})
    from pull_rates import enrich
    enrich(config,monster,monster['packSettings'])
    root.mkdir(parents=True,exist_ok=True)
    for name,value in [('config.json',config),('extracted-monsters.json',monster),('extracted-stock.json',stock),
                       ('extracted-english.json',translations)]:
        (root/name).write_text(json.dumps(value,indent=2,ensure_ascii=False),encoding='utf-8')
    print(json.dumps({'trackers':len(config['trackers']),'cards':sum(len(s['cards']) for s in config['sets']),
                      'products':len(config['products']),'output':str(root)}))


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('game',type=Path);p.add_argument('app',type=Path);p.add_argument('root',type=Path)
    a=p.parse_args();build(a.game,a.app,a.root)
