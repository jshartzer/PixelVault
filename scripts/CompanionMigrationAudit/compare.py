"""Read-only migration analysis. Generates a deterministic proposal, never applies it."""
import argparse
import hashlib
import json
import ntpath
import sqlite3
from collections import Counter
from pathlib import Path


MISSING = {'$missing': True}
IMAGES = {'.jpg', '.jpeg', '.png', '.jxr', '.bmp', '.gif', '.webp'}


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=False)


def digest(value):
    return hashlib.sha256(canonical(value).encode()).hexdigest()


def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def database(path):
    c = sqlite3.connect(path.resolve().as_uri() + '?mode=ro', uri=True)
    c.row_factory = sqlite3.Row
    c.execute('PRAGMA query_only=ON')
    return c


def tables(c):
    return sorted(r[0] for r in c.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'"))


def rows(c, table):
    if table not in tables(c):
        return []
    return [dict(r) for r in c.execute('SELECT * FROM "' + table.replace('"', '""') + '"')]


def keyed(c, table, columns):
    return {tuple(r[k] for k in columns): r for r in rows(c, table)}


def normalize(value):
    if isinstance(value, str) and value[:1] in '{[':
        try:
            return json.loads(value)
        except json.JSONDecodeError:
            pass
    return value


def classify(pc, nas, baseline=MISSING, baseline_proven=False):
    if pc == nas:
        return 'equal'
    if not baseline_proven:
        return 'review-no-common-baseline'
    if pc == baseline:
        return 'nas-only-change'
    if nas == baseline:
        return 'pc-only-change'
    return 'conflict-both-changed'


def proposal(kind, key, before, incoming, classification):
    operation = {'kind': kind, 'key': key, 'beforeHash': digest(before),
                 'incomingHash': digest(incoming), 'classification': classification,
                 'execution': 'proposal-only'}
    operation['operationId'] = digest(operation)
    return operation


def photo_id(workspace, edition, library, file):
    relative = ntpath.relpath(file, library)
    if relative == '..' or relative.startswith('..\\') or ntpath.isabs(relative):
        raise ValueError('Photo outside library')
    return hashlib.sha256((workspace + '\n' + edition + '\n' + relative.replace('\\', '/').lower()).encode()).hexdigest()


def media_classification(photo, pc, removed, root_available):
    if photo['id'] in removed:
        return 'intentionally-hidden'
    if not root_available:
        return 'source-root-unavailable'
    if pc is None:
        return 'nas-only-identity-needs-mapping'
    if not pc['exists']:
        return 'pc-source-file-missing'
    if not pc['listed']:
        return 'pc-file-outside-current-inventory'
    if not pc['indexed']:
        return 'pc-file-not-indexed'
    original = photo.get('original')
    if original and original['bytes'] != pc['bytes']:
        return 'pc-original-size-changed'
    return 'direct-source-present'


def job_state(job):
    statuses = {t.get('status', t.get('Status', 'unknown')) for t in job.get('tasks', job.get('Tasks', []))}
    if statuses & {'queued', 'reading'}:
        return 'unfinished'
    for name in ['review', 'failed', 'paused']:
        if name in statuses:
            return name
    return 'complete' if statuses and statuses <= {'complete', 'superseded'} else 'unknown'


def projected_groups(catalogue, storage, groups):
    # Legacy console editions can own assignments for files now indexed under a
    # consolidated edition. Match ExportCore's all-edition then storage scopes.
    editions = {p['editionId'] for p in catalogue['photos']}
    storage_ids = {storage.get(e, '') for e in editions} - {''}
    editions.update(e for e, group in storage.items() if group and group in storage_ids)
    scopes = list(dict.fromkeys('storage:' + storage[e] if storage.get(e) else e for e in sorted(editions)))
    main = scopes[0] if len(scopes) == 1 else 'game:' + catalogue['provider'] + ':' + catalogue['gameId']
    result = {}
    for scope in sorted(editions) + [main] + [s for s in scopes if s != main]:
        result.update({file: name for (game, file), name in groups.items() if game == scope.lower()})
    return result


def table_inventory(c):
    result = {}
    for name in tables(c):
        values = rows(c, name)
        result[name] = {'rows': len(values), 'rowDigest': digest(sorted((canonical(r) for r in values))),
                        'columns': [r[1] for r in c.execute('PRAGMA table_info("' + name + '")')]}
    return result


def compare(root):
    pc = database(root / 'pc/achievement-tracking/pixelvault-tracking.sqlite')
    nas = database(root / 'nas/achievement-tracking/pixelvault-tracking.sqlite')
    baseline = database(root / 'baseline/tracking.sqlite')
    web = database(root / 'nas/companion.sqlite')
    guides = database(root / 'pc/guides/pixelvault-guides.sqlite')
    index = database(root / 'pc/cache/pixelvault-index-e_game_captures.sqlite')
    capture_groups = database(root / 'pc/capture-groups.sqlite')
    map_database = database(root / 'pc/game-maps/maps.sqlite')
    manifest = load(root / 'nas/workspace.json')
    old_manifest = load(root / 'baseline/workspace.json')
    config = load(root / 'pc/connector-redacted.json')
    # The initial migrated workspace has the same immutable copy identity and account scope.
    baseline_history = rows(baseline, 'tracker_history')
    pc_history = {digest(r) for r in rows(pc, 'tracker_history')}
    nas_history = {digest(r) for r in rows(nas, 'tracker_history')}
    baseline_shared = bool(baseline_history) and all(digest(r) in pc_history and digest(r) in nas_history for r in baseline_history)
    proven = manifest == old_manifest and baseline_shared
    workspace = config['workspaceId']
    scope_match = config['accounts'] == manifest['Accounts']
    achievement_scope_match = config['achievementAccounts'] == load(root / 'nas/achievement-accounts.json')
    operations, differences = [], []
    state_keys = ['tracker_id', 'account', 'item_id']
    states = [keyed(c, 'tracker_item_state', state_keys) for c in (pc, nas, baseline)]
    authored = Counter()
    for key in sorted(set(states[0]) | set(states[1])):
        for field in ['manual_current', 'note']:
            values = [s.get(key, {}).get(field, None if field == 'manual_current' else '') for s in states]
            # A missing row has the repository's empty-record values for manual fields.
            disposition = classify(*values, baseline_proven=proven)
            authored[disposition] += 1
            if disposition != 'equal':
                detail = {'area': 'manual-field', 'key': list(key) + [field], 'pc': values[0],
                          'nas': values[1], 'baseline': values[2], 'classification': disposition}
                differences.append(detail)
                operations.append(proposal('manual-field', detail['key'], values[0], values[1], disposition))
    source_state = Counter()
    source_field_diffs = Counter()
    source_by_tracker = {}
    for key in sorted(set(states[0]) | set(states[1])):
        fields = ['reported_current', 'availability', 'source', 'evidence']
        left = {f: states[0].get(key, {}).get(f) for f in fields}
        right = {f: states[1].get(key, {}).get(f) for f in fields}
        source_state['equal' if left == right else 'different'] += 1
        if left != right:
            field_names = [f for f in fields if left[f] != right[f]]
            source_field_diffs.update(field_names)
            source_by_tracker.setdefault(key[0], Counter()).update(field_names)
            differences.append({'area': 'source-observation', 'key': list(key), 'pc': left, 'nas': right,
                                'classification': 'retain-evidence-review-source-lineage'})
            operations.append(proposal('source-observation', list(key), left, right,
                                       'retain-evidence-review-source-lineage'))
    area_summary = {}
    for table, keys, fields in [
        ('tracker_definition', ['id'], ['definition']),
        ('tracker_link', ['tracker_id'], ['achievement_id']),
        ('tracker_title', ['tracker_id'], ['title']),
        ('tracker_icon', ['tracker_id'], ['icon_id']),
        ('tracker_source', ['tracker_id', 'account'], ['binding']),
        ('tracker_automation', ['tracker_id', 'account'], ['settings']),
        ('century_progress', ['provider', 'game', 'id', 'account'], ['value']),
    ]:
        maps = [keyed(c, table, keys) for c in (pc, nas, baseline)]
        counts = Counter()
        for key in sorted(set(maps[0]) | set(maps[1])):
            for field in fields:
                values = [normalize(m.get(key, {}).get(field, MISSING)) for m in maps]
                disposition = classify(*values, baseline_proven=proven)
                if table == 'tracker_source' and all(isinstance(v, dict) and v != MISSING for v in values[:2]):
                    if {k: v for k, v in values[0].items() if k != 'Id'} == {k: v for k, v in values[1].items() if k != 'Id'} and values[0] != values[1]:
                        disposition = 'equivalent-binding-needs-id-map'
                if table == 'century_progress' and all(isinstance(v, dict) and v != MISSING for v in values[:2]):
                    if {k: v for k, v in values[0].items() if k not in ['BindingId', 'VerifiedUtc']} == {k: v for k, v in values[1].items() if k not in ['BindingId', 'VerifiedUtc']} and values[0] != values[1]:
                        disposition = 'equivalent-century-observation-needs-provenance-map'
                counts[disposition] += 1
                if disposition != 'equal':
                    differences.append({'area': table, 'key': list(key) + [field], 'pc': values[0],
                                        'nas': values[1], 'baseline': values[2], 'classification': disposition})
                    operations.append(proposal(table, list(key) + [field], values[0], values[1], disposition))
        area_summary[table] = dict(counts)
    # Histories retain origin IDs. Matching content is mapped, never inserted twice.
    history = {}
    for table in ['tracker_history', 'tracker_catalogue_history', 'tracker_automation_result', 'tracker_import', 'tracker_client_request']:
        left, right = rows(pc, table), rows(nas, table)
        left_digests = {digest(r) for r in left}
        common = sum(digest(r) in left_digests for r in right)
        history[table] = {'pc': len(left), 'nas': len(right), 'exactCommon': common,
                          'nasOriginRowsToPreserve': len(right) - common}
        for row in right:
            if digest(row) not in left_digests:
                identity = {k: row[k] for k in ['id', 'tracker_id', 'account', 'request_id', 'digest'] if k in row}
                operations.append(proposal('preserve-origin-history', [table, 'nas', identity, digest(row)],
                                           MISSING, row, 'preserve-lineage-before-import'))
    receipts = rows(pc, 'tracker_web_sync')
    pending = {'trackerFieldConflicts': sum(r['pending'] is not None for r in receipts),
               'trackerTitleConflicts': sum(r['conflict'] != 0 for r in rows(web, 'companion_tracker_title'))}
    requests = [json.loads(r['value']) for r in rows(web, 'companion_refresh_job')]
    pending['refreshJobStatuses'] = dict(Counter(job_state(r) for r in requests))
    pending['unfinishedRefreshJobs'] = sum(job_state(r) == 'unfinished' for r in requests)
    pending['saveReviewJobs'] = sum(job_state(r) == 'review' for r in requests)
    pending['pcAutomationReviews'] = sum(bool(json.loads(r['state']).get('ReviewPending')) for r in rows(pc, 'tracker_automation'))
    pending['connectorActivity'] = {r['name']: {'value': r['value'], 'updated': r['updated']}
                                    for r in rows(web, 'companion_connector_activity')}
    guide_rows = {r['achievement_id']: r for r in rows(guides, 'achievement_guide')}
    definitions = {(r['provider'], r['provider_game_id'], r['provider_achievement_id']): r
                   for r in rows(guides, 'achievement_definition')}
    achievement_counts = Counter()
    achievement_diffs = []
    provider_rows = {}
    for file in (root / 'pc/achievement-hub').glob('*.json'):
        account = load(file)
        provider = account.get('Provider')
        if str(account.get('Account', '')).lower() != str(config['achievementAccounts'].get(provider, '')).lower():
            continue
        for game, data in account.get('Games', {}).items():
            for r in data.get('Rows', []):
                provider_rows[(provider, game, r.get('ProviderAchievementId'))] = r
    for row in rows(web, 'companion_achievement'):
        saved = json.loads(row['value'])
        key = (row['provider'], row['game'], row['id'])
        definition = definitions.get(key)
        achievement_counts['nasRows'] += 1
        achievement_counts['pendingGroups'] += saved.get('pending') is not None
        achievement_counts['groupConflicts'] += bool(saved.get('conflict'))
        if definition is None:
            achievement_counts['nasOnlyDefinitions'] += 1
            achievement_diffs.append({'key': key, 'classification': 'nas-only-definition'})
            continue
        provider_row = provider_rows.get(key)
        if provider_row:
            fields = [('progressKnown', 'ProgressKnown'), ('unlocked', 'Unlocked'),
                      ('unlockUtc', 'UnlockUtcTicks'), ('counterCurrent', 'CounterCurrent'), ('counterTarget', 'CounterTarget')]
            for remote_field, local_field in fields:
                local = provider_row.get(local_field)
                if remote_field in ['progressKnown', 'unlocked']:
                    local = bool(local)
                if remote_field == 'unlockUtc':
                    local = local or 0
                if saved['data'].get(remote_field) != local:
                    achievement_counts['differentProgressFields'] += 1
                    achievement_diffs.append({'key': key + (remote_field,), 'classification': 'provider-progress-review',
                                              'pc': local, 'nas': saved['data'].get(remote_field)})
        else:
            achievement_counts['withoutPcProviderSnapshot'] += 1
            achievement_counts['withoutPcSnapshotKnownProgress'] += bool(saved['data'].get('progressKnown'))
        local_group = normalize_groups(definition['group_name'], definition['dlc_name'], definition['group_sort_order'])
        desired = saved.get('web') or saved['desktop']
        if local_group != desired:
            achievement_counts['differentGroups'] += 1
            achievement_diffs.append({'key': key, 'classification': 'group-review', 'pc': local_group, 'nas': desired})
        guide = guide_rows.get(definition['achievement_id'], {})
        for remote_name, local_name in [('guide', 'guide_text'), ('sourceUrl', 'source_url'), ('sourceTitle', 'source_title'), ('tags', 'tags')]:
            local_value = guide.get(local_name, '')
            if saved['data'].get(remote_name, '') != local_value:
                achievement_counts['differentGuideFields'] += 1
                achievement_diffs.append({'key': key + (remote_name,), 'classification': 'guide-review',
                                          'pcHash': digest(local_value), 'nasHash': digest(saved['data'].get(remote_name, ''))})
    pending['achievementGroupPending'] = achievement_counts['pendingGroups']
    pending['achievementGroupConflicts'] = achievement_counts['groupConflicts']
    # Every exact NAS provider row must also exist locally; inverse membership is reported.
    nas_keys = {(r['provider'], r['game'], r['id']) for r in rows(web, 'companion_achievement')}
    achievement_counts['pcOnlyDefinitions'] = len(set(definitions) - nas_keys)
    media_summary, media_details = compare_media(root, index, capture_groups, workspace)
    coverage = {name: table_inventory(c) for name, c in [('pc-trackers', pc), ('nas-trackers', nas),
                ('nas-companion', web), ('pc-guides', guides), ('pc-index', index), ('pc-photo-groups', capture_groups), ('pc-maps', map_database)]}
    preferences = rows(web, 'companion_preference')
    artwork = [{r['originalUrl']: r for r in load(root / folder / 'achievement-artwork/manifest.json')}
               for folder in ['pc', 'nas']]
    artwork_summary = {'pcImports': len(artwork[0]), 'nasImports': len(artwork[1]),
                       'pcOnlyImports': len(artwork[0].keys() - artwork[1].keys()),
                       'nasOnlyImports': len(artwork[1].keys() - artwork[0].keys())}
    artwork_summary['differentContentHashes'] = sum(artwork[0][key]['sha256'] != artwork[1][key]['sha256'] for key in artwork[0].keys() & artwork[1].keys())
    catalogues = [{(g['provider'], g['gameId']): g for g in load(root / file)['games']}
                  for file in ['pc/connector-library-catalog.json', 'nas/library/catalog.json']]
    library_summary = {'pcEditions': len(catalogues[0]), 'nasEditions': len(catalogues[1]),
                       'pcOnlyEditions': len(catalogues[0].keys() - catalogues[1].keys()),
                       'nasOnlyEditions': len(catalogues[1].keys() - catalogues[0].keys()),
                       'playtimeSummaries': sum(g['stats'].get('playtimeMinutes') is not None for g in catalogues[1].values())}
    library_differences = []
    for key in sorted(catalogues[0].keys() & catalogues[1].keys()):
        left, right = catalogues[0][key], catalogues[1][key]
        if left != right:
            library_differences.append({'key': key, 'pc': left, 'nas': right, 'classification': 'local-provider-summary-reconciliation'})
            operations.append(proposal('provider-summary', key, left, right, 'local-provider-summary-reconciliation'))
    library_summary['differentEditionRows'] = len(library_differences)
    scope = {'baselineManifestMatches': manifest == old_manifest, 'baselineHistoryShared': baseline_shared,
             'baselineHistoryRows': len(baseline_history), 'trackerAccountsMatch': scope_match,
             'achievementAccountsMatch': achievement_scope_match,
             'trackerProviders': sorted(manifest['Accounts']),
             'achievementProviders': sorted(config['achievementAccounts'])}
    century = [{'id': r['id'], **{k: v for k, v in json.loads(r['value']).items() if k in ['Current', 'Target', 'Unknown', 'UnlockUtc']}}
               for r in rows(pc, 'century_progress')]
    summary = {'scope': scope, 'manualFields': dict(authored), 'sourceStateRows': dict(source_state),
               'sourceFieldDifferences': dict(source_field_diffs),
               'sourceDifferencesByTracker': {k: dict(v) for k, v in source_by_tracker.items()}, 'century': century,
               'areas': area_summary, 'history': history, 'pending': pending,
               'achievements': dict(achievement_counts), 'media': media_summary,
               'artwork': artwork_summary, 'library': library_summary,
               'webPreferences': [{'name': r['name'], 'revision': r['revision']} for r in preferences],
               'proposalOperations': len(operations),
               'limitations': ['Independent live snapshots; repeat at cutover.',
                              'No photo bytes were hashed: direct-source presence/size does not establish byte identity.',
                              'Historical lineage and source observation differences require reconciliation design, not blind row append.',
                              'All proposal operations are non-executable and source-guarded.']}
    report = {'version': 1, 'mode': 'dry-run-only', 'summary': summary, 'differences': differences,
              'achievementDifferences': achievement_diffs, 'mediaDifferences': media_details,
              'libraryDifferences': library_differences,
              'tableCoverage': coverage, 'operations': sorted(operations, key=lambda r: r['operationId'])}
    report['reportId'] = digest(report)
    for c in [pc, nas, baseline, web, guides, index, capture_groups, map_database]:
        c.close()
    return report


def normalize_groups(category, dlc, order):
    category, dlc = category.strip(), dlc.strip()
    if dlc.lower() == 'base game':
        dlc = ''
    if category.lower().startswith('dlc: '):
        dlc = dlc or category[5:].strip()
        category = ''
    if category.lower() == 'dlc':
        dlc = dlc or 'DLC'
        category = ''
    if category.lower() in ['base game', 'uncategorized']:
        category = ''
    return {'category': category, 'dlc': dlc, 'sortOrder': order}


def compare_media(root, index, capture_groups, workspace):
    ini = dict(line.split('=', 1) for line in (root / 'pc/PixelVault.settings.ini').read_text(encoding='utf-8-sig').splitlines() if '=' in line)
    library = ini['library'].strip()
    available = Path(library).is_dir()
    folders = [line.split('\t') for line in (root / 'pc/cache/library-folders-e_game_captures.cache').read_text(encoding='utf-8-sig').splitlines()]
    listed = {file.lower() for f in folders if len(f) >= 14 and not (len(f) > 15 and f[15] == '1') for file in f[6].split('|') if file}
    source_rows = rows(index, 'photo_index')
    groups = {(r['game'].lower(), r['file'].lower()): r['name'] for r in rows(capture_groups, 'capture_group_member') if r['root'].lower() == library.lower()}
    storage = {r['game_id']: r['storage_group_id'] for r in rows(index, 'game_index')}
    mapping = {}
    for r in source_rows:
        file = r['file_path']
        if r['root'].lower() != library.lower() or ntpath.splitext(file)[1].lower() not in IMAGES:
            continue
        identity = photo_id(workspace, r['game_id'], library, file)
        info = Path(file)
        exists = info.is_file() if available else False
        mapping[identity] = {'file': file, 'exists': exists, 'listed': file.lower() in listed,
                             'indexed': True, 'bytes': info.stat().st_size if exists else None,
                             'ticks': r['capture_utc_ticks'], 'edition': r['game_id']}
    removed = {}
    for path in (root / 'nas/screenshots').glob('*/removals/*.json'):
        removed.update(load(path))
    catalogues = list((root / 'nas/screenshots').glob('*/catalogue.json')) + list((root / 'nas/screenshots').glob('*/catalogues/*.json'))
    counts, details, seen = Counter(), [], set()
    fields = Counter()
    for path in sorted(catalogues):
        catalogue = load(path)
        group_map = projected_groups(catalogue, storage, groups)
        counts['catalogues'] += 1
        for p in catalogue['photos']:
            key = (catalogue['provider'], catalogue['gameId'], p['id'])
            if key in seen:
                raise ValueError('Duplicate published photo identity')
            seen.add(key)
            value = mapping.get(p['id'])
            status = media_classification(p, value, removed, available)
            counts[status] += 1
            if status not in ['direct-source-present', 'intentionally-hidden']:
                details.append({'key': key, 'classification': status, 'fileName': p.get('original', {}).get('fileName'), 'pc': value})
            if value and status == 'direct-source-present':
                if value['ticks'] != p['captureUtcTicks']:
                    fields['differentCaptureDates'] += 1
                if value['edition'] != p['editionId']:
                    fields['differentEditions'] += 1
                file_key = ntpath.relpath(value['file'], library).lower()
                group = group_map.get(file_key, '')
                if group != p['group']:
                    fields['differentPhotoGroups'] += 1
                    details.append({'key': key, 'classification': 'photo-group-review', 'pc': group, 'nas': p['group']})
                if p.get('metadata'):
                    sidecar = Path(value['file'] + '.xmp')
                    fields['publishedSidecars'] += 1
                    if not sidecar.is_file() or sidecar.stat().st_size != p['metadata']['bytes']:
                        fields['missingOrSizeChangedSidecars'] += 1
    counts['removedOverlayIdentities'] = len(removed)
    counts['visiblePublishedPhotos'] = sum(1 for key in seen if key[2] not in removed)
    live_ids = {key[2] for key in seen if key[2] not in removed}
    counts['indexedListedPcPhotos'] = sum(v['listed'] for v in mapping.values())
    counts['pcOnlyCurrentPhotos'] = sum(v['listed'] and key not in live_ids for key, v in mapping.items())
    counts['unavailableListedPcFiles'] = sum(v['listed'] and not v['exists'] for v in mapping.values())
    for key, value in mapping.items():
        if value['listed'] and key not in live_ids:
            details.append({'key': key, 'classification': 'pc-only-current-photo', 'pc': value})
    return {'root': library, 'rootAvailable': available, **dict(counts), **dict(fields)}, details


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('evidence', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    root, output = args.evidence.resolve(), args.output.resolve()
    # Reports must stay in this protected evidence directory, never a live app root.
    if output.parent != root or output.suffix != '.json' or not output.name.startswith('comparison'):
        raise ValueError('Report must be named comparison*.json directly inside the evidence directory')
    result = compare(root)
    output.write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding='utf-8')
    print(json.dumps({'reportId': result['reportId'], 'summary': result['summary']}, indent=2))
