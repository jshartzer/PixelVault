"""Prepare/reconcile an isolated Phase 2 store. No live-apply option exists."""
import argparse
import json
import shutil
import sqlite3
import subprocess
from contextlib import closing
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path

from capture import snapshot, stable_copy, read_db
from compare import canonical, digest, rows, keyed, database, normalize


def report_checked(evidence):
    report = json.loads((evidence / 'comparison.json').read_text(encoding='utf-8'))
    content = {k: v for k, v in report.items() if k != 'reportId'}
    if digest(content) != report['reportId'] or report['mode'] != 'dry-run-only':
        raise ValueError('The reviewed comparison report failed validation')
    scope = report['summary']['scope']
    if not all(scope.get(k) for k in ['baselineManifestMatches', 'baselineHistoryShared', 'trackerAccountsMatch', 'achievementAccountsMatch']):
        raise ValueError('The account scope/common baseline needs review')
    if set(report['summary']['manualFields']) != {'equal'}:
        raise ValueError('Resolve authored differences before staging')
    return report


def require_staged(evidence, target):
    evidence, target = evidence.resolve(), target.resolve()
    if target.parent != evidence or not target.name.startswith('phase2-') or target.is_symlink():
        raise ValueError('Only a phase2-* rehearsal directly under the protected evidence directory is supported')


def manual_signature(row):
    changes = json.loads(row['changes'])
    normalized = []
    for change in changes:
        fields = change['Fields']
        if fields & 1 or not fields & 6:
            return None
        before, after = change['Before'], change['After']
        selected = lambda record: {**({'Manual': record.get('Manual')} if fields & 2 else {}),
                                   **({'Note': record.get('Note', '')} if fields & 4 else {})}
        normalized.append([change['ItemId'], fields, selected(before), selected(after)])
    return canonical([row['tracker_id'], row['account'], normalized])


def reported_record(row):
    return {'Reported': row['reported_current'], 'Availability': row['availability'],
            'Source': row['source'], 'Evidence': row['evidence'], 'ObservedUtc': row['observed_utc'],
            'Manual': None, 'ManualUtc': 0, 'Note': ''}


def binding_aliases(pc, nas, approved_sources=None):
    left = keyed(pc, 'tracker_source', ['tracker_id', 'account'])
    aliases = {}
    for key, remote in keyed(nas, 'tracker_source', ['tracker_id', 'account']).items():
        if approved_sources is not None and key not in approved_sources:
            continue
        if key not in left:
            raise ValueError('Unapproved NAS-only source binding')
        a, b = json.loads(left[key]['binding']), json.loads(remote['binding'])
        if {k: v for k, v in a.items() if k != 'Id'} != {k: v for k, v in b.items() if k != 'Id'}:
            raise ValueError('A source permission or reader configuration differs')
        aliases[key] = (b['Id'], a['Id'], a)
    return aliases


def reconcile_tracking(target, source, report_id, workspace, approved_accounts, approved_sources):
    """One transaction: archive origins, map receipts, and import proven reported fields only."""
    c = sqlite3.connect(target, timeout=30)
    c.row_factory = sqlite3.Row
    incoming = database(source)
    try:
        c.execute('BEGIN IMMEDIATE')
        c.execute('CREATE TABLE IF NOT EXISTS tracker_migration_run(id TEXT PRIMARY KEY,result TEXT NOT NULL)')
        replay = c.execute('SELECT result FROM tracker_migration_run WHERE id=?', (report_id,)).fetchone()
        if replay:
            c.rollback()
            return {**json.loads(replay[0]), 'replayed': True, 'newWrites': 0}
        aliases = binding_aliases(c, incoming, approved_sources)
        for table, columns in [('tracker_definition', ['id']), ('tracker_link', ['tracker_id']),
                               ('tracker_title', ['tracker_id']), ('tracker_icon', ['tracker_id'])]:
            clean = lambda connection: {key: {k: normalize(v) for k, v in row.items()} for key, row in keyed(connection, table, columns).items()}
            if clean(c) != clean(incoming):
                raise ValueError('Authored catalogue metadata changed; repeat the review')
        pc_states = keyed(c, 'tracker_item_state', ['tracker_id', 'account', 'item_id'])
        nas_states = keyed(incoming, 'tracker_item_state', ['tracker_id', 'account', 'item_id'])
        if pc_states.keys() != nas_states.keys():
            raise ValueError('Item identities changed; repeat the review')
        for key in pc_states:
            if key[1] not in approved_accounts:
                continue
            if any(pc_states[key][field] != nas_states[key][field] for field in ['manual_current', 'note']):
                raise ValueError('Manual fields diverged; no state was changed')
        c.execute('''CREATE TABLE IF NOT EXISTS tracker_migration_origin(
            workspace TEXT NOT NULL,table_name TEXT NOT NULL,origin_key TEXT NOT NULL,payload_hash TEXT NOT NULL,
            payload TEXT NOT NULL,canonical_id INTEGER NULL,
            PRIMARY KEY(workspace,table_name,origin_key,payload_hash))''')
        c.execute('''CREATE TABLE IF NOT EXISTS tracker_migration_binding(
            workspace TEXT NOT NULL,tracker_id TEXT NOT NULL,account TEXT NOT NULL,origin_id TEXT NOT NULL,canonical_id TEXT NOT NULL,
            PRIMARY KEY(workspace,tracker_id,account,origin_id))''')
        for (tracker, account), (old, new, _) in aliases.items():
            c.execute('INSERT INTO tracker_migration_binding VALUES(?,?,?,?,?)', (workspace, tracker, account, old, new))
        pc_history = rows(c, 'tracker_history')
        nas_history = rows(incoming, 'tracker_history')
        exact = {digest(r): r['id'] for r in pc_history}
        mirrors = defaultdict(list)
        for row in pc_history:
            if row['adapter_id'] == 'companion-web':
                signature = manual_signature(row)
                if signature:
                    mirrors[signature].append(row['id'])
        mapped = {}
        for row in nas_history:
            canonical_id = exact.get(digest(row))
            if canonical_id is None and row['adapter_id'] == 'manual':
                matches = mirrors.get(manual_signature(row), [])
                if len(matches) == 1:
                    canonical_id = matches[0]
            if canonical_id is not None:
                mapped[row['id']] = canonical_id
        def catalogue_key(row):
            return digest([row['tracker_id'],normalize(row['before_definition']),normalize(row['after_definition']),bool(row['undone_utc'])])
        catalogue_candidates=defaultdict(list)
        for row in rows(c,'tracker_catalogue_history'):
            catalogue_candidates[catalogue_key(row)].append(row['id'])
        catalogue_mapped={}
        for row in rows(incoming,'tracker_catalogue_history'):
            candidates=catalogue_candidates.get(catalogue_key(row),[])
            if len(candidates)==1:
                catalogue_mapped[row['id']]=candidates[0]
        archived = 0
        for table in [r[0] for r in incoming.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")]:
            for row in rows(incoming, table):
                origin_key = canonical({k: row[k] for k in ['id', 'tracker_id', 'account', 'item_id', 'request_id', 'provider', 'game', 'digest'] if k in row})
                c.execute('INSERT INTO tracker_migration_origin VALUES(?,?,?,?,?,?)',
                          (workspace, table, origin_key, digest(row), canonical(row), mapped.get(row.get('id')) if table == 'tracker_history' else catalogue_mapped.get(row.get('id')) if table=='tracker_catalogue_history' else None))
                archived += 1
        receipt_count = 0
        for row in rows(incoming, 'tracker_client_request'):
            if row['account'] not in approved_accounts:
                continue
            if row['history_id'] is not None and row['history_id'] not in mapped:
                raise ValueError('A web request history pointer has no proven canonical mapping')
            record = (row['tracker_id'], row['account'], row['request_id'], row['fingerprint'], mapped.get(row['history_id']))
            old = c.execute('SELECT fingerprint,history_id FROM tracker_client_request WHERE tracker_id=? AND account=? AND request_id=?', record[:3]).fetchone()
            if old is not None and tuple(old) != record[3:]:
                raise ValueError('A request ID conflicts with an existing PC receipt')
            if old is None:
                c.execute('INSERT INTO tracker_client_request VALUES(?,?,?,?,?)', record)
                receipt_count += 1
        latest = {}
        for history in sorted(nas_history, key=lambda r: r['id'], reverse=True):
            if history['undone_utc']:
                continue
            for change in json.loads(history['changes']):
                if change['Fields'] & 1:
                    latest.setdefault((history['tracker_id'], history['account'], change['ItemId']), (history, change['After']))
        batches = defaultdict(list)
        effective = 0
        for key, remote in nas_states.items():
            if key[:2] not in approved_sources:
                continue
            old = pc_states[key]
            core = ['reported_current', 'availability', 'source', 'evidence']
            if all(old[f] == remote[f] for f in core):
                continue
            if key[:2] not in aliases or key not in latest:
                raise ValueError('A changed observation has no approved binding/history evidence')
            history, after = latest[key]
            expected = reported_record(remote)
            # Equality with the complete persisted reported-field record is the same
            # protection invariant used by TrackerRepository.LatestInlineReports.
            if any(after.get(k, 0 if k in ['Availability', 'ObservedUtc'] else '') != v for k, v in expected.items() if k not in ['Manual', 'ManualUtc', 'Note']):
                raise ValueError('A reported value does not match its last accepted history evidence')
            binding = aliases[key[:2]][2]
            if history['adapter_id'] != binding['AdapterId'] or history['adapter_version'] != binding['AdapterVersion'] or not history['fingerprint']:
                raise ValueError('The observation reader/provenance is not approved')
            if remote['observed_utc'] < old['observed_utc']:
                raise ValueError('An older source would replace a newer local value; review required')
            c.execute('''UPDATE tracker_item_state SET reported_current=?,availability=?,source=?,evidence=?,observed_utc=?
                         WHERE tracker_id=? AND account=? AND item_id=?''',
                      tuple(remote[f] for f in ['reported_current', 'availability', 'source', 'evidence', 'observed_utc']) + key)
            batches[(key[0], key[1], history['adapter_id'], history['adapter_version'], history['fingerprint'])].append(
                {'ItemId': key[2], 'Fields': 1, 'Before': reported_record(old), 'After': expected})
            effective += old['reported_current'] != remote['reported_current']
        now = 621355968000000000 + int(datetime.now(timezone.utc).timestamp() * 10_000_000)
        for (tracker, account, adapter, version, fingerprint), changes in batches.items():
            c.execute('''INSERT INTO tracker_history(tracker_id,account,kind,label,created_utc,undone_utc,adapter_id,adapter_version,fingerprint,observed_utc,changes)
                         VALUES(?,?, 'refresh','Reconciled approved observations',?,0,?,?,?,?,?)''',
                      (tracker, account, now, adapter, version, fingerprint, max(r['After']['ObservedUtc'] for r in changes), json.dumps(changes, separators=(',', ':'))))
        # Century state remains canonical PC state; NAS evidence is archived with explicit binding aliases.
        for row in rows(incoming, 'century_progress'):
            if row['account'] not in approved_accounts:
                continue
            local = c.execute('SELECT value FROM century_progress WHERE provider=? AND game=? AND id=? AND account=?', tuple(row[k] for k in ['provider', 'game', 'id', 'account'])).fetchone()
            if local is None:
                raise ValueError('NAS-only Century state needs explicit reconciliation')
            a, b = json.loads(local[0]), json.loads(row['value'])
            if {k: v for k, v in a.items() if k not in ['BindingId', 'VerifiedUtc']} != {k: v for k, v in b.items() if k not in ['BindingId', 'VerifiedUtc']}:
                raise ValueError('Independent Century observations require review')
        result = {'archivedOriginRows': archived, 'bindingAliases': sum(a != b for a, b, _ in aliases.values()),
                  'mappedHistoryRows': len(mapped), 'copiedRequestReceipts': receipt_count,
                  'mappedCatalogueRows':len(catalogue_mapped),
                  'reportedValueChanges': effective, 'checkpointRows': len(batches), 'replayed': False}
        c.execute('INSERT INTO tracker_migration_run VALUES(?,?)', (report_id, canonical(result)))
        c.commit()
        return result
    except BaseException:
        c.rollback()
        raise
    finally:
        c.close()
        incoming.close()


def stage(evidence, target, companion_dll):
    require_staged(evidence, target)
    report = report_checked(evidence)
    marker = target / 'stage-manifest.json'
    tool_digest=digest(Path(__file__).read_text(encoding='utf-8'))
    if target.exists():
        if not marker.exists() or json.loads(marker.read_text())['sourceReportId'] != report['reportId'] or json.loads(marker.read_text()).get('toolDigest')!=tool_digest:
            raise ValueError('Existing rehearsal does not match this source report')
    else:
        target.mkdir()
        marker.write_text(json.dumps({'sourceReportId': report['reportId'], 'toolDigest':tool_digest, 'complete': False}))
        app, state = target / 'application', target / 'state'
        app.mkdir();state.mkdir()
        for name in ['achievement-tracking/pixelvault-tracking.sqlite', 'guides/pixelvault-guides.sqlite', 'game-maps/maps.sqlite', 'capture-groups.sqlite', 'cache/pixelvault-index-e_game_captures.sqlite']:
            snapshot(evidence / 'pc' / name, app / name)
        for name in ['PixelVault.settings.ini', 'achievement-hub-pins.json', 'cache/library-folders-e_game_captures.cache', 'achievement-artwork/manifest.json']:
            if (evidence / 'pc' / name).exists():
                stable_copy(evidence / 'pc' / name, app / name)
        shutil.copytree(evidence / 'pc/achievement-hub', app / 'achievement-hub')
        (app / 'local-store-preparation.json').write_text(json.dumps({'purpose': 'pc-companion-rehearsal', 'sourceReportId': report['reportId']}))
        manifest = json.loads((evidence / 'nas/workspace.json').read_text());manifest['Mode'] = 'direct-local'
        (state / 'workspace.json').write_text(json.dumps(manifest))
        stable_copy(evidence / 'nas/achievement-accounts.json', state / 'achievement-accounts.json')
        # Keep auth/preferences, not the copied achievement/library/connector authority.
        source = database(evidence / 'nas/companion.sqlite')
        destination = sqlite3.connect(state / 'companion.sqlite')
        for table in ['owner', 'companion_preference']:
            destination.execute(source.execute('SELECT sql FROM sqlite_master WHERE name=?', (table,)).fetchone()[0])
            for row in rows(source, table):
                destination.execute('INSERT INTO ' + table + ' VALUES(' + ','.join('?' for _ in row) + ')', tuple(row.values()))
        destination.commit();destination.close();source.close()
        catalogue = json.loads((evidence / 'nas/library/catalog.json').read_text())
        pc_catalogue = json.loads((evidence / 'pc/connector-library-catalog.json').read_text())
        catalogue['games'] = list({(g['provider'],g['gameId']):g for g in catalogue['games']+pc_catalogue['games']}.values())
        accounts = json.loads((state / 'achievement-accounts.json').read_text())
        summaries = [{'provider': g['provider'], 'gameId': g['gameId'], 'account': accounts[g['provider']],
                      'playtimeMinutes': g['stats'].get('playtimeMinutes'), 'lastPlayedUtc': g['stats'].get('lastPlayedUtc')}
                     for g in catalogue['games'] if g['provider'] in accounts and (g['stats'].get('playtimeMinutes') is not None or g['stats'].get('lastPlayedUtc') is not None)]
        (app / 'local-provider-summaries.json').write_text(json.dumps(summaries))
        # Preserve removal/identity metadata for Phase 3; never copy raw media.
        for path in (evidence / 'nas/screenshots').glob('*/removals/*.json'):
            stable_copy(path, app / 'companion-photo-removals' / path.name)
        options = {'Companion': {'StorageMode': 'direct-local', 'ApplicationDataRoot': str(app), 'DataRoot': str(state),
                                 'CacheRoot': str(target / 'cache'), 'AllowHttp': True}}
        (target / 'direct-local.json').write_text(json.dumps(options, indent=2))
    subprocess.run(['dotnet', str(companion_dll), '--prepare-direct-local-schema', str(target / 'application')], check=True)
    config = json.loads((evidence / 'pc/connector-redacted.json').read_text())
    workspace = config['workspaceId']
    approved = json.loads((evidence / 'nas/workspace.json').read_text())['Accounts']
    sources = set()
    for source in config['sources']:
        definition = {k.lower():v for k,v in source['definition'].items()}
        binding = {k.lower():v for k,v in source['binding'].items()}
        if definition['provider'] not in approved or binding['account'] != approved[definition['provider']] or binding['trackerid'] != definition['id']:
            raise ValueError('A source is outside the approved provider/account scope')
        sources.add((definition['id'],binding['account']))
    # Native-approved sources need not all have been paired to the old NAS worker.
    # Resolve their provider from stored definitions or the approved connector
    # definitions, then require the same fixed account and full binding equality.
    providers={source['definition']['id']:source['definition']['provider'] for source in config['sources']}
    with closing(database(evidence/'pc/achievement-tracking/pixelvault-tracking.sqlite')) as local:
        for row in rows(local,'tracker_definition'):
            definition={k.lower():v for k,v in json.loads(row['definition']).items()}
            providers[row['id']]=definition['provider']
        for row in rows(local,'tracker_source'):
            provider=providers.get(row['tracker_id'])
            if provider in approved and row['account']==approved[provider]:
                sources.add((row['tracker_id'],row['account']))
    result = reconcile_tracking(target / 'application/achievement-tracking/pixelvault-tracking.sqlite',
                                evidence / 'nas/achievement-tracking/pixelvault-tracking.sqlite', report['reportId'], workspace,set(approved.values()),sources)
    marker.write_text(json.dumps({'sourceReportId': report['reportId'], 'toolDigest':tool_digest, 'complete': True, 'result': result}, indent=2))
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('evidence', type=Path)
    parser.add_argument('target', type=Path)
    parser.add_argument('--companion-dll', type=Path, required=True)
    args = parser.parse_args()
    stage(args.evidence.resolve(), args.target.resolve(), args.companion_dll.resolve())
