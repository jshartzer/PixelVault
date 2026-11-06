"""Frozen-input, narrowly scoped PC cutover. Never replace the canonical app folder."""
import argparse
import hashlib
import json
import shutil
import sqlite3
import subprocess
import time
from contextlib import closing
from pathlib import Path

from app_backup import write_json, ro, database_info, protect
from capture import snapshot, stable_copy
from compare import compare, digest, canonical, rows
from stage import stage, report_checked, reconcile_tracking


def validate(evidence):
    report = report_checked(evidence)
    s = report['summary']
    pending = s['pending']
    for key in ['trackerFieldConflicts', 'trackerTitleConflicts', 'unfinishedRefreshJobs', 'saveReviewJobs', 'pcAutomationReviews', 'achievementGroupPending', 'achievementGroupConflicts']:
        if pending.get(key):
            raise ValueError('Pending edits/reviews require resolution before cutover: ' + key)
    if not s['media']['rootAvailable'] or s['media'].get('unavailableListedPcFiles') or s['media'].get('pcOnlyCurrentPhotos'):
        raise ValueError('The frozen photo metadata is not ready for direct access')
    if any(s['areas'].get(name, {}).get(kind, 0) for name in ['tracker_definition', 'tracker_link', 'tracker_title', 'tracker_icon'] for kind in s['areas'].get(name, {}) if kind != 'equal'):
        raise ValueError('Authored catalogue metadata diverged')
    if s['artwork'].get('differentContentHashes') or s['library'].get('differentEditionRows'):
        raise ValueError('Authored artwork/library metadata diverged')
    return report


def immutable_state(path):
    with closing(ro(path)) as db:
        db.row_factory = sqlite3.Row
        result = {}
        for name in ['tracker_definition', 'tracker_link', 'tracker_title', 'tracker_icon']:
            result[name] = sorted(canonical(dict(row)) for row in db.execute('SELECT * FROM ' + name))
        result['manual'] = [list(row) for row in db.execute('SELECT tracker_id,account,item_id,manual_current,note FROM tracker_item_state ORDER BY tracker_id,account,item_id')]
        result['permissions'] = [list(row) for row in db.execute('SELECT tracker_id,account,binding FROM tracker_source ORDER BY tracker_id,account')]
        return digest(result)


def frozen_automatic(evidence, workspace):
    with closing(ro(evidence / 'nas/companion.sqlite')) as old:
        prior=old.execute("SELECT value FROM companion_connector_activity WHERE workspace=? AND name='settings'",(workspace,)).fetchone()
        return bool(prior and json.loads(prior[0]).get('AutomaticEnabled',False))


def prepare(evidence, companion_dll):
    report = validate(evidence)
    target = evidence / 'phase2-final-candidate'
    result = stage(evidence, target, companion_dll)
    options = json.loads((target / 'direct-local.json').read_text())
    options['Companion'].update(ScreenshotLibraryEnabled=True)
    photo_config = target / 'direct-photos.json'
    write_json(photo_config, options)
    subprocess.run(['dotnet', str(companion_dll), '--prepare-direct-local-photos', str(photo_config), str(evidence)], check=True, capture_output=True)
    write_json(evidence / 'cutover-candidate.json', {'reportId': report['reportId'], 'candidate': str(target), 'reconciliation': result, 'visiblePhotos': report['summary']['media']['visiblePublishedPhotos']})
    return target


def apply_frozen(evidence, plan_path):
    plan = json.loads(Path(plan_path).read_text(encoding='utf-8-sig'))
    app = Path(plan['ApplicationRoot'])
    if plan.get('Purpose') == 'pc-companion-cutover-rehearsal':
        if app.resolve() == Path(r'C:\Codex\PixelVaultData').resolve() or not (app.parent / 'restore-receipt.json').exists():
            raise ValueError('A cutover rehearsal requires a verified isolated restore')
    elif plan.get('Purpose') != 'pc-companion-cutover' or plan['ApplicationRoot'] != r'C:\Codex\PixelVaultData':
        raise ValueError('Only the reviewed existing canonical PC root can be changed')
    journal_path = Path(plan['EvidenceRoot']) / 'cutover-journal.json'
    journal = json.loads(journal_path.read_text(encoding='utf-8-sig'))
    if journal.get('Phase') != 'frozen-and-captured' or Path(journal['FrozenEvidence']).resolve() != evidence.resolve():
        raise ValueError('The administrator freeze/capture gate is not satisfied')
    report = validate(evidence)
    target = evidence / 'phase2-final-candidate'
    if not (target / 'stage-manifest.json').exists():
        raise ValueError('The final frozen reconciliation was not rehearsed')
    state = Path(plan['StateRoot'])
    if state.exists():
        raise FileExistsError('Production state must start in a new root, not a historical workspace')
    keys = evidence / 'nas/keys'
    if not keys.is_dir() or not list(keys.glob('*.xml')):
        raise ValueError('The frozen real NAS key ring was not captured')
    tracking = app / 'achievement-tracking/pixelvault-tracking.sqlite'
    immutable_before = immutable_state(tracking)
    # Schema ownership is explicit and checks all frozen live-file hashes first.
    guarded = []
    for relative in ['achievement-tracking/pixelvault-tracking.sqlite', 'guides/pixelvault-guides.sqlite']:
        for suffix in ['', '-wal', '-shm']:
            path = app / (relative + suffix)
            if path.exists():
                guarded.append({'Path': relative + suffix, 'Sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
    guard_file = evidence / 'canonical-schema-guard.json'
    write_json(guard_file, {'Purpose': 'pc-companion-cutover-frozen', 'WritersStopped': True, 'ApplicationRoot': str(app), 'Files': guarded})
    journal['Phase'] = 'canonical-modification-starting'; write_json(journal_path, journal)
    subprocess.run([plan['Executable'], '--prepare-canonical-direct-local', str(guard_file)], check=True, capture_output=True)
    config = json.loads((evidence / 'pc/connector-redacted.json').read_text())
    manifest = json.loads((evidence / 'nas/workspace.json').read_text())
    approved_sources = set()
    with closing(ro(evidence / 'pc/achievement-tracking/pixelvault-tracking.sqlite')) as source:
        source.row_factory = sqlite3.Row
        providers = {row['id']: {k.lower():v for k,v in json.loads(row['definition']).items()}['provider'] for row in source.execute('SELECT * FROM tracker_definition')}
        for item in config['sources']:
            definition={k.lower():v for k,v in item['definition'].items()}
            providers[definition['id']]=definition['provider']
        for row in source.execute('SELECT * FROM tracker_source'):
            provider=providers.get(row['tracker_id'])
            if provider in manifest['Accounts'] and row['account']==manifest['Accounts'][provider]: approved_sources.add((row['tracker_id'],row['account']))
    # Refuse fresh source-state drift relative to the frozen PC snapshot before applying observations.
    with closing(ro(tracking)) as current, closing(ro(evidence / 'pc/achievement-tracking/pixelvault-tracking.sqlite')) as captured:
        for db in [current, captured]: db.row_factory = sqlite3.Row
        if sorted(canonical(dict(r)) for r in current.execute('SELECT * FROM tracker_item_state')) != sorted(canonical(dict(r)) for r in captured.execute('SELECT * FROM tracker_item_state')):
            raise ValueError('The canonical PC was edited after the frozen capture')
    result = reconcile_tracking(tracking, evidence / 'nas/achievement-tracking/pixelvault-tracking.sqlite', report['reportId'], config['workspaceId'], set(manifest['Accounts'].values()), approved_sources)
    if immutable_before != immutable_state(tracking):
        raise ValueError('Manual fields, authored catalogue or approved source permissions changed')
    protect(state)
    for relative in ['companion.sqlite', 'workspace.json', 'achievement-accounts.json']:
        source = target / 'state' / relative
        if relative.endswith('.sqlite'): snapshot(source, state / relative)
        else: stable_copy(source, state / relative)
    # Preserve actual NAS authentication keys without logging/exporting their contents.
    (state / 'keys').mkdir()
    for key in keys.glob('*.xml'): stable_copy(key, state / 'keys' / key.name)
    # Only additive durable metadata is installed into the full canonical application root.
    for relative in ['local-photos/identities.sqlite']:
        destination = app / relative
        if destination.exists(): raise FileExistsError('Do not replace an existing canonical photo identity map')
        snapshot(target / 'application' / relative, destination)
    for name in ['local-provider-summaries.json']:
        if not (app / name).exists(): stable_copy(target / 'application' / name, app / name)
    for source in (target / 'application/companion-photo-removals').glob('*.json'):
        destination = app / 'companion-photo-removals' / source.name
        if not destination.exists(): stable_copy(source, destination)
    with closing(ro(evidence / 'nas/companion.sqlite')) as old, closing(ro(state / 'companion.sqlite')) as new:
        if old.execute('SELECT name,password_hash FROM owner').fetchall() != new.execute('SELECT name,password_hash FROM owner').fetchall():
            raise ValueError('The actual owner account was not preserved')
        if old.execute('SELECT * FROM companion_preference ORDER BY name').fetchall() != new.execute('SELECT * FROM companion_preference ORDER BY name').fetchall():
            raise ValueError('Web preferences were not preserved')
    for key in keys.glob('*.xml'):
        if key.read_bytes() != (state / 'keys' / key.name).read_bytes(): raise ValueError('Migrated owner keys differ')
    automatic=frozen_automatic(evidence,config['workspaceId'])
    receipt = {'Applied': True, 'ReportId': report['reportId'], 'Reconciliation': result, 'CanonicalRootRetained': True, 'AuthoredFieldsPreserved': True, 'ActualOwnerPreserved': True, 'ActualOwnerKeysPreserved': True, 'PreferencesPreserved': True, 'AutomaticEnabled': automatic, 'VisiblePhotos': report['summary']['media']['visiblePublishedPhotos'], 'PhotoOriginalsCopied': 0}
    write_json(evidence / 'canonical-apply-proof.json', receipt)
    journal['Phase'] = 'canonical-reconciled'; write_json(journal_path, journal)
    return receipt


def check_running(plan_path):
    plan=json.loads(Path(plan_path).read_text(encoding='utf-8-sig'))
    with closing(ro(Path(plan['StateRoot'])/'companion.sqlite')) as db:
        tick=db.execute("SELECT MAX(updated) FROM companion_connector_activity WHERE name='local-worker-tick'").fetchone()[0] or 0
        settings=db.execute("SELECT value FROM companion_connector_activity WHERE name='settings'").fetchone()
    now=621355968000000000+int(time.time()*10000000)
    journal=json.loads((Path(plan['EvidenceRoot'])/'cutover-journal.json').read_text(encoding='utf-8-sig'))
    proof=json.loads((Path(journal['FrozenEvidence'])/'canonical-apply-proof.json').read_text())
    expected=proof.get('AutomaticEnabled',False)
    actual=bool(settings and json.loads(settings[0]).get('AutomaticEnabled',False))
    ready=tick>now-30*10000000 and actual==expected
    return {'Ready':ready,'ServiceWorkerAccessValidated':tick>0,'AutomaticEnabled':actual,'PausePolicyPreserved':actual==expected}


if __name__ == '__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('action',choices=['prepare','apply-frozen','check-running'])
    parser.add_argument('evidence',type=Path)
    parser.add_argument('--companion-dll',type=Path)
    parser.add_argument('--plan',type=Path)
    args=parser.parse_args()
    if args.action=='prepare': print(prepare(args.evidence.resolve(),args.companion_dll.resolve()))
    elif args.action=='apply-frozen': print(json.dumps(apply_frozen(args.evidence.resolve(),args.plan.resolve())))
    else:
        result=check_running(args.plan.resolve());print(json.dumps(result));raise SystemExit(0 if result['Ready'] else 1)
