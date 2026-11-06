"""Exact per-pack probabilities for the locally verified Assembly-CSharp build.

GetGodPackRollIndex, GetPackContent and both single-pack opening paths, all.cs
4147-4816. Rates include special packs and replacement of the seventh card by
a Ghost. They apply after the tutorial, at displayed shop level 3 or higher.
"""
from collections import Counter
from fractions import Fraction as F
from functools import lru_cache

ASSEMBLY_SHA256 = 'bc83eec70f3feea51a2271f3eced3589ea09a4d41061e7ff8702dee3b3a9b227'
PACK_NAMES = ('Basic', 'Rare', 'Epic', 'Legendary')
# Inclusive integer ranges; 1130 belongs to mode 8, not mode 7.
GOD_COUNTS = {1:501, 2:311, 3:251, 4:201, 5:71, 6:121, 7:100,
              8:49, 9:21, 10:9, 11:51, 12:1, 13:201}
GOD_COUNTS[0] = 100000 - sum(GOD_COUNTS.values())


@lru_cache(None)
def variant_probability(border, foil, mode):
    if mode == 0:
        remaining = F(1)
        borders = {}
        for index, chance in [(5,F(25,10000)), (4,F(1,100)), (3,F(4,100)),
                              (2,F(8,100)), (1,F(20,100))]:
            borders[index] = remaining * chance
            remaining *= 1-chance
        borders[0] = remaining
        foil_chance = F(5,100)
        border_chance = borders[border]
    else:
        forced_border = mode if 1 <= mode <= 5 else mode-5 if 6 <= mode <= 10 else 0
        border_chance = F(border == forced_border)
        foil_chance = F(mode in (6,7,8,9,10,12,13))
    return border_chance * (foil_chance if foil else 1-foil_chance)


@lru_cache(None)
def main_rate(pool, border, foil, *, ghost_chance, duplicates):
    result = F(0)
    for mode, count in GOD_COUNTS.items():
        if mode in (11,12):
            continue  # Every card is replaced by a Ghost.
        variant = variant_probability(border, foil, mode)
        if duplicates:
            q = variant / pool
            hit = (1-ghost_chance)*(1-(1-q)**7) + ghost_chance*(1-(1-q)**6)
        else:
            # Unique monsters within the seven draws: target can occur once.
            hit = variant * (7-ghost_chance) / pool
        result += F(count,100000) * hit
    return result


@lru_cache(None)
def ghost_rate(foil, ghost_chance, pool=26):
    result = F(0)
    for mode, count in GOD_COUNTS.items():
        foil_chance = F(5,100) if mode == 0 else F(mode in (6,7,8,9,10,12,13))
        q = (foil_chance if foil else 1-foil_chance) / (2*pool)
        hit = 1-(1-q)**7 if mode in (11,12) else ghost_chance*q
        result += F(count,100000)*hit
    return result


def validate_settings(settings):
    indexed = {i:(s['openPackCanUseRarity'],s['openPackCanHaveDuplicate']) for i,s in enumerate(settings)}
    for index, expected in {0:(1,0),1:(1,0),2:(0,1),7:(0,1)}.items():
        if indexed.get(index) != expected:
            raise ValueError('Pack selection settings changed; revalidate pull rates')


def percentages(config, monsters, settings):
    if config['assemblySha256'] != ASSEMBLY_SHA256:
        raise ValueError('Game code changed; revalidate pull rates')
    validate_settings(settings)
    by_id = {m['MonsterType']:m for m in monsters['m_DataList']}
    pools = Counter(by_id[i]['Rarity'] for i in monsters['m_ShownMonsterList'])
    if dict(pools) != {0:27,1:34,2:34,3:26} or len(monsters['m_ShownGhostMonsterList']) != 26:
        raise ValueError('Card pools changed; revalidate pull rates')
    result = {}
    for collection in config['sets']:
        for card in collection['cards']:
            key = collection['key']
            if key == 'ghost':
                rates = {'Tetramon':ghost_rate(card['foil'],F(1,1000)),
                         'Destiny / Ascension':ghost_rate(card['foil'],F(2,1000))}
            elif key == 'ascension':
                rates = {'Ascension':main_rate(121,card['border'],card['foil'],ghost_chance=F(2,1000),duplicates=True)}
            else:
                rarity = by_id[card['monster']]['Rarity']
                pack = PACK_NAMES[rarity]+' '+collection['title']
                rates = {pack:main_rate(pools[rarity],card['border'],card['foil'],
                         ghost_chance=F(1 if key=='tetramon' else 2,1000),duplicates=False)}
            result[card['id']] = rates
    return result


def detail_text(rates):
    values = '; '.join(f'{pack} pack: {float(rate*100):.6f}%' for pack,rate in rates.items())
    return ('Pull rate — '+values+'. Chance of at least one copy of this exact variant per pack, '
            'including special packs (shop level 3+; after the tutorial).')


def enrich(config, monsters, settings):
    rates = percentages(config,monsters,settings)
    for tracker in config['trackers']:
        if tracker['metric'] != 'collection':
            continue
        for item in tracker['definition']['Items']:
            # Keep existing non-rate details, including any custom text.
            detail = item.get('Detail','')
            if detail.startswith('Pull rate — '):
                detail = detail.split('\n\n',1)[1] if '\n\n' in detail else ''
            item['Detail'] = detail_text(rates[item['GroupId']]) + ('\n\n'+detail if detail else '')
    return rates
