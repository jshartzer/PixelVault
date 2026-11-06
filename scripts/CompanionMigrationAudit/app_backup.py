"""Explicit app-only SQLite/metadata backups. Restore only into a new isolated directory."""
import argparse
import csv
import ctypes
import hashlib
import io
import json
import os
import re
import shlex
import sqlite3
import subprocess
import tempfile
import time
import uuid
import zipfile
from contextlib import ExitStack, closing, contextmanager
from datetime import datetime, timezone
from pathlib import Path, PurePosixPath

FORMAT = 'pixelvault-app-backup-v1'


def utc():
    return datetime.now(timezone.utc).isoformat()


def digest(value):
    return hashlib.sha256(value).hexdigest()


def write_json(path, value):
    path = Path(path)
    temporary = path.with_name(path.name + '.tmp-' + uuid.uuid4().hex)
    temporary.write_text(json.dumps(value, indent=2), encoding='utf-8')
    temporary.replace(path)


def safe_relative(name):
    p = PurePosixPath(name)
    if not name or '\\' in name or ':' in name or p.is_absolute() or '..' in p.parts or str(p) != name:
        raise ValueError('Unsafe archive path')
    return p


def checked(root, relative):
    safe_relative(relative)
    root = Path(root).absolute()
    path = root.joinpath(*PurePosixPath(relative).parts)
    for ancestor in [path, *path.parents]:
        if ancestor.exists() and (ancestor.is_symlink() or ancestor.is_junction()):
            raise ValueError('Linked backup paths are not supported')
        if ancestor == root:
            break
    if not path.resolve().is_relative_to(root.resolve()):
        raise ValueError('Backup path escaped its source')
    return path


def protect(root):
    """A backup can contain API keys/owner state. Do not inherit broad workspace access."""
    root = Path(root)
    root.mkdir(parents=True, exist_ok=True)
    if os.name == 'nt':
        result = subprocess.run(['whoami', '/user', '/fo', 'csv', '/nh'], capture_output=True, text=True, check=True)
        sid = next(csv.reader(io.StringIO(result.stdout)))[1]
        subprocess.run(['icacls.exe', str(root), '/inheritance:r', '/grant:r',
                        f'*{sid}:(OI)(CI)F', '*S-1-5-18:(OI)(CI)F', '*S-1-5-32-544:(OI)(CI)F'],
                       check=True, capture_output=True)
    else:
        root.chmod(0o700)


def load_plan(path):
    plan = json.loads(Path(path).read_text(encoding='utf-8-sig'))
    if plan.get('purpose') != 'pixelvault-app-backups' or plan.get('version') != 1:
        raise ValueError('Unknown backup plan')
    roots = {name: Path(value).absolute() for name, value in plan['roots'].items()}
    if set(roots) - {'application', 'state', 'machine'} or 'application' not in roots:
        raise ValueError('Unknown source roles')
    backup_root = Path(plan['backupRoot']).absolute()
    for root in roots.values():
        if not root.is_dir() or backup_root.resolve().is_relative_to(root.resolve()) or root.resolve().is_relative_to(backup_root.resolve()):
            raise ValueError('Backup destination must be separate from existing source roots')
    for spec in plan['include']:
        if spec['root'] not in roots or spec['kind'] not in ('sqlite', 'file', 'glob'):
            raise ValueError('Unknown inclusion rule')
        # No recursive wildcards: each allowed directory and file type is deliberate.
        if '**' in spec['path']:
            raise ValueError('Recursive source globs are forbidden')
        safe_relative(spec['path'])
    return plan, roots, backup_root


def selection(plan, roots):
    files = {}
    for spec in plan['include']:
        root = roots[spec['root']]
        candidates = sorted(root.glob(spec['path'])) if spec['kind'] == 'glob' else [checked(root, spec['path'])]
        found = False
        for path in candidates:
            relative = path.relative_to(root).as_posix()
            path = checked(root, relative)
            if not path.is_file():
                continue
            found = True
            if path.name.endswith(('-wal', '-shm', '.lock', '.tmp')):
                raise ValueError('Transient files cannot be included')
            files[spec['root'] + '/' + relative] = (path, 'sqlite' if spec['kind'] == 'sqlite' else 'file', spec.get('role', 'metadata'))
        if spec.get('required') and not found:
            raise FileNotFoundError('Required backup input is missing: ' + spec['root'] + '/' + spec['path'])
    # Curated achievement artwork is included only when referenced by its manifest.
    manifest = roots['application'] / 'achievement-artwork/manifest.json'
    if manifest.exists():
        for row in json.loads(manifest.read_text(encoding='utf-8-sig')):
            asset = row.get('file', '')
            if not re.fullmatch(r'[a-f0-9]{64}\.(png|jpg|webp)', asset):
                raise ValueError('Unexpected curated-artwork asset')
            relative = 'achievement-artwork/' + asset
            path = checked(roots['application'], relative)
            if path.exists():
                files['application/' + relative] = (path, 'file', 'custom-artwork')
            elif not plan.get('allowMissingRehearsalArtwork', False):
                raise FileNotFoundError('Referenced curated artwork is missing')
    return dict(sorted(files.items()))


@contextmanager
def json_lease(path):
    """Use the same non-inheritable exclusive file lease as Native provider writers."""
    if os.name != 'nt':
        yield
        return
    import msvcrt
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.CreateFileW.argtypes = [ctypes.c_wchar_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p]
    kernel.CreateFileW.restype = ctypes.c_void_p
    lease = path.parent / '.leases' / (path.name + '.lock')
    lease.parent.mkdir(exist_ok=True)
    deadline = time.monotonic() + 3
    while True:
        handle = kernel.CreateFileW(str(lease), 0xC0000000, 0, None, 4, 0x80, None)
        if handle != ctypes.c_void_p(-1).value:
            break
        if time.monotonic() >= deadline:
            raise TimeoutError('Provider writer lease is busy')
        time.sleep(.05)
    with os.fdopen(msvcrt.open_osfhandle(handle, os.O_RDWR), 'r+b'):
        yield


def ro(path):
    return sqlite3.connect(path.resolve().as_uri() + '?mode=ro', uri=True, timeout=3)


def database_info(path):
    with closing(ro(path)) as db:
        if db.execute('PRAGMA quick_check').fetchone()[0] != 'ok':
            raise ValueError('Database integrity check failed')
        tables = {}
        for name, in db.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name"):
            identifier = '"' + name.replace('"', '""') + '"'
            tables[name] = db.execute('SELECT COUNT(*) FROM ' + identifier).fetchone()[0]
        return {'userVersion': db.execute('PRAGMA user_version').fetchone()[0], 'tables': tables}


def copy_database(source, target):
    deadline = time.monotonic() + 3
    def progress(status, remaining, total):
        if time.monotonic() > deadline:
            raise TimeoutError('SQLite backup exceeded its bounded writer reservation')
    with closing(ro(source)) as incoming, closing(sqlite3.connect(target)) as output:
        incoming.backup(output, pages=256, progress=progress, sleep=.02)


def snapshot(plan_path, label=None, replicate=True):
    _, _, root = load_plan(plan_path)
    protect(root)
    with json_lease(root / 'backup-run'):
        try:
            return _snapshot(plan_path, label, replicate)
        except Exception as error:
            path = root / 'latest-status.json'
            status = json.loads(path.read_text()) if path.exists() else {}
            status.update(lastFailureUtc=utc(), errorType=type(error).__name__)
            write_json(path, status)
            raise


def _snapshot(plan_path, label=None, replicate=True):
    plan, roots, backup_root = load_plan(plan_path)
    protect(backup_root)
    if label is not None and not re.fullmatch(r'[a-zA-Z0-9-]{1,60}', label):
        raise ValueError('Unsafe snapshot label')
    snapshot_id = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '-' + uuid.uuid4().hex[:8]
    archive = backup_root / ('app-' + snapshot_id + '.zip')
    manifest = {'format': FORMAT, 'id': snapshot_id, 'startedUtc': utc(), 'label': label,
                'roots': {k: str(v) for k, v in roots.items()}, 'files': [],
                'consistency': 'All included SQLite writers reserved together; provider leases held; metadata set and bytes validated across capture.',
                'photoOriginalsIncluded': 0, 'derivativeCachesIncluded': 0}
    progress_file = backup_root / 'capture-progress.json'
    def report(stage, name=None):
        write_json(progress_file, {'utc': utc(), 'stage': stage, 'input': name, 'id': snapshot_id})
    with tempfile.TemporaryDirectory(prefix='app-capture-', dir=backup_root) as temporary:
        staging = Path(temporary)
        if staging.resolve().parent != backup_root.resolve():
            raise ValueError('Capture staging escaped the approved backup directory')
        for attempt in range(3):
            try:
                with ExitStack() as guards:
                    selected = selection(plan, roots)
                    for name, (source, kind, role) in selected.items():
                        if kind == 'file' and (name.startswith('application/achievement-hub/') or name == 'application/local-provider-summaries.json'):
                            guards.enter_context(json_lease(source))
                    # Reading large custom artwork before reserving SQLite writers keeps the
                    # barrier short. Rechecking all bytes afterward still brackets every DB copy.
                    report('read-metadata')
                    metadata = {name: source.read_bytes() for name, (source, kind, _) in selected.items() if kind == 'file'}
                    database_details = {}
                    with ExitStack() as database_guards:
                        for source, kind, role in selected.values():
                            if kind == 'sqlite':
                                report('reserve-database', str(source.relative_to(next(root for root in roots.values() if source.is_relative_to(root)))))
                                guard = database_guards.enter_context(closing(sqlite3.connect(source.resolve().as_uri() + '?mode=rw', uri=True, timeout=1)))
                                guard.execute('BEGIN IMMEDIATE')
                        manifest['writeBarrierUtc'] = utc()
                        for name, (source, kind, role) in selected.items():
                            if kind != 'sqlite':
                                continue
                            report('snapshot-database', name)
                            target = checked(staging, name)
                            target.parent.mkdir(parents=True, exist_ok=True)
                            if target.exists():
                                target.unlink()
                            copy_database(source, target)
                            database_details[name] = database_info(target)
                        manifest['writeBarrierReleasedUtc'] = utc()
                    manifest['files'] = []
                    for name, (source, kind, role) in selected.items():
                        target = checked(staging, name)
                        target.parent.mkdir(parents=True, exist_ok=True)
                        if kind == 'sqlite':
                            db_info = database_details[name]
                        else:
                            target.write_bytes(metadata[name])
                            db_info = None
                        value = target.read_bytes()
                        manifest['files'].append({'path': name, 'kind': kind, 'role': role, 'bytes': len(value), 'sha256': digest(value), 'database': db_info})
                    if set(selection(plan, roots)) != set(selected):
                        raise RuntimeError('Metadata file set changed during capture')
                    if any(source.read_bytes() != metadata[name] for name, (source, kind, _) in selected.items() if kind == 'file'):
                        raise RuntimeError('Metadata bytes changed during capture')
                break
            except (sqlite3.OperationalError, RuntimeError, TimeoutError):
                if attempt == 2:
                    raise
                time.sleep(.2)
        manifest['completedUtc'] = utc()
        report('write-archive')
        pending = archive.with_suffix('.zip.partial')
        with zipfile.ZipFile(pending, 'x', zipfile.ZIP_DEFLATED, compresslevel=6) as bundle:
            for entry in manifest['files']:
                bundle.write(checked(staging, entry['path']), entry['path'])
            bundle.writestr('manifest.json', json.dumps(manifest, indent=2))
        verify(pending)
        pending.replace(archive)
    status = {'lastLocalSuccessUtc': utc(), 'archive': archive.name, 'files': len(manifest['files']), 'bytes': archive.stat().st_size}
    write_json(backup_root / 'latest-status.json', status)
    if replicate:
        report('replicate')
        retry_remote(plan_path)
    prune(plan_path)
    report('complete')
    return archive


def verify(archive):
    with zipfile.ZipFile(archive) as bundle:
        if bundle.testzip() is not None:
            raise ValueError('Archive CRC check failed')
        names = bundle.namelist()
        if len(names) != len(set(names)):
            raise ValueError('Duplicate archive entry')
        for name in names:
            safe_relative(name)
        manifest = json.loads(bundle.read('manifest.json'))
        if manifest.get('format') != FORMAT:
            raise ValueError('Unknown archive format')
        expected = {'manifest.json'}
        for entry in manifest['files']:
            safe_relative(entry['path'])
            if entry['path'] in expected or entry['path'].split('/')[0] not in ('application', 'state', 'machine'):
                raise ValueError('Unexpected archive entry')
            expected.add(entry['path'])
            value = bundle.read(entry['path'])
            if len(value) != entry['bytes'] or digest(value) != entry['sha256']:
                raise ValueError('Archive checksum mismatch')
        if set(names) != expected:
            raise ValueError('Unmanifested archive content')
        return manifest


def restore(archive, destination):
    manifest = verify(archive)
    destination = Path(destination).absolute()
    if destination.exists():
        raise FileExistsError('Restore requires a new isolated destination')
    for root in manifest['roots'].values():
        source = Path(root).resolve()
        if destination.resolve().is_relative_to(source) or source.is_relative_to(destination.resolve()):
            raise ValueError('Restore overlaps a captured source root')
    protect(destination)
    with zipfile.ZipFile(archive) as bundle:
        for entry in manifest['files']:
            target = checked(destination, entry['path'])
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(bundle.read(entry['path']))
            if entry['kind'] == 'sqlite' and database_info(target) != entry['database']:
                raise ValueError('Restored database does not match its manifest')
    write_json(destination / 'restore-receipt.json', {'format': FORMAT, 'id': manifest['id'], 'files': len(manifest['files']), 'verifiedUtc': utc(), 'sourceArchiveSha256': digest(Path(archive).read_bytes())})
    return manifest


def retry_remote(plan_path):
    plan, _, backup_root = load_plan(plan_path)
    remote = plan.get('remote')
    if not remote:
        return {'configured': False}
    if not re.fullmatch(r'[a-zA-Z0-9_.-]+@[a-zA-Z0-9_.-]+', remote['host']) or not re.fullmatch(r'/[a-zA-Z0-9_./-]+', remote['directory']) or '..' in PurePosixPath(remote['directory']).parts:
        raise ValueError('Unsafe replication destination')
    status_path = backup_root / 'replication-status.json'
    status = json.loads(status_path.read_text()) if status_path.exists() else {'archives': {}}
    ssh = ['ssh', *ssh_options(remote)]
    for archive in sorted(backup_root.glob('app-*.zip')):
        if status['archives'].get(archive.name, {}).get('copied'):
            continue
        try:
            archive_manifest = verify(archive)
            remote_dir = remote['directory']
            quote = shlex.quote
            subprocess.run(ssh + [remote['host'], 'umask 077; mkdir -p -- ' + quote(remote_dir) + ' && chmod 700 -- ' + quote(remote_dir)], check=True, capture_output=True, timeout=20)
            part = remote_dir + '/' + archive.name + '.partial'
            subprocess.run(['scp', '-B', *ssh_options(remote), str(archive), remote['host'] + ':' + part], check=True, capture_output=True, timeout=120)
            found = subprocess.run(ssh + [remote['host'], 'sha256sum -- ' + quote(part)], check=True, capture_output=True, text=True, timeout=20).stdout.split()[0]
            expected = digest(archive.read_bytes())
            if found != expected:
                raise ValueError('Remote archive checksum mismatch')
            subprocess.run(ssh + [remote['host'], 'chmod 600 -- ' + quote(part) + ' && mv -- ' + quote(part) + ' ' + quote(remote_dir + '/' + archive.name)], check=True, capture_output=True, timeout=20)
            status['archives'][archive.name] = {'copied': True, 'sha256': expected, 'utc': utc(), 'snapshotUtc': archive_manifest['completedUtc'], 'label': archive_manifest.get('label')}
            status['lastSuccessfulCopyUtc'] = utc()
        except (subprocess.SubprocessError, ValueError, OSError) as error:
            # Remote diagnostics can include account paths. Persist only failure class.
            status['archives'][archive.name] = {'copied': False, 'errorType': type(error).__name__, 'utc': utc()}
            if isinstance(error, subprocess.CalledProcessError) and error.stderr:
                detail = error.stderr.decode(errors='replace') if isinstance(error.stderr, bytes) else error.stderr
                # SSH/scp errors contain no provider request URLs or archive contents.
                (backup_root / 'replication-error.private.txt').write_text(detail[-4096:], encoding='utf-8')
        write_json(status_path, status)
    return status


def ssh_options(remote):
    options = ['-o', 'BatchMode=yes', '-o', 'ConnectTimeout=8', '-o', 'StrictHostKeyChecking=yes']
    if remote.get('identityFile'):
        options += ['-i', remote['identityFile'], '-o', 'IdentitiesOnly=yes']
    if remote.get('knownHostsFile'):
        options += ['-o', 'UserKnownHostsFile=' + remote['knownHostsFile']]
    return options


def retained(records, pending=()):
    """Keep 14 distinct daily points, eight weekly points, named points and uncopied points."""
    keep = set(pending)
    days, weeks = set(), set()
    for name, stamp, label in sorted(records, key=lambda row: row[1], reverse=True):
        day = stamp.date()
        week = stamp.isocalendar()[:2]
        if label:
            keep.add(name)
        if day not in days and len(days) < 14:
            days.add(day); keep.add(name)
        if week not in weeks and len(weeks) < 8:
            weeks.add(week); keep.add(name)
    return keep


def prune(plan_path):
    plan, _, backup_root = load_plan(plan_path)
    records, archives = [], {}
    replication = backup_root / 'replication-status.json'
    copies = json.loads(replication.read_text()).get('archives', {}) if replication.exists() else {}
    pending = []
    for path in backup_root.glob('app-*.zip'):
        if not re.fullmatch(r'app-\d{8}T\d{6}Z-[a-f0-9]{8}\.zip', path.name):
            continue
        manifest = verify(path)
        if path.name != 'app-' + manifest['id'] + '.zip':
            raise ValueError('Archive identity does not match its managed name')
        records.append((path.name, datetime.fromisoformat(manifest['completedUtc']), manifest.get('label')))
        archives[path.name] = path
        if plan.get('remote') and not copies.get(path.name, {}).get('copied'):
            pending.append(path.name)
    keep = retained(records, pending)
    removed = []
    for name, path in archives.items():
        if name not in keep:
            # No recursive deletion; only verified engine archives directly in this managed root.
            if path.is_symlink() or path.resolve().parent != backup_root.resolve():
                raise ValueError('Retention target escaped its managed root')
            path.unlink(); removed.append(name)
    remote_removed = []
    remote_records = [(name, datetime.fromisoformat(row['snapshotUtc']), row.get('label')) for name, row in copies.items() if row.get('copied') and row.get('snapshotUtc') and not row.get('removedUtc')]
    remote_keep = retained(remote_records, pending)
    remote = plan.get('remote')
    if remote:
        if not re.fullmatch(r'[a-zA-Z0-9_.-]+@[a-zA-Z0-9_.-]+', remote['host']) or not re.fullmatch(r'/[a-zA-Z0-9_./-]+', remote['directory']) or '..' in PurePosixPath(remote['directory']).parts:
            raise ValueError('Unsafe replication destination')
        ssh = ['ssh', *ssh_options(remote)]
        for name, _, _ in remote_records:
            if name in remote_keep:
                continue
            if not re.fullmatch(r'app-\d{8}T\d{6}Z-[a-f0-9]{8}\.zip', name):
                raise ValueError('Unsafe remote retention name')
            target = remote['directory'].rstrip('/') + '/' + name
            try:
                found = subprocess.run(ssh + [remote['host'], 'sha256sum -- ' + shlex.quote(target)], check=True, capture_output=True, text=True, timeout=20).stdout.split()[0]
                if found != copies[name]['sha256']:
                    raise ValueError('Remote retention hash differs from the managed archive')
                subprocess.run(ssh + [remote['host'], 'rm -- ' + shlex.quote(target)], check=True, capture_output=True, timeout=20)
                copies[name]['removedUtc'] = utc(); remote_removed.append(name)
            except (subprocess.SubprocessError, ValueError, OSError) as error:
                copies[name]['pruneErrorType'] = type(error).__name__
        if replication.exists():
            state = json.loads(replication.read_text()); state['archives'] = copies; write_json(replication, state)
    write_json(backup_root / 'retention-status.json', {'utc': utc(), 'daily': 14, 'weekly': 8, 'namedPointsRetained': True, 'pendingCopiesRetained': len(pending), 'removedLocalArchives': removed, 'removedRemoteArchives': remote_removed})
    return removed


def main():
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest='action', required=True)
    create = sub.add_parser('snapshot'); create.add_argument('plan'); create.add_argument('--label'); create.add_argument('--no-remote', action='store_true')
    check = sub.add_parser('verify'); check.add_argument('archive')
    recover = sub.add_parser('restore'); recover.add_argument('archive'); recover.add_argument('destination')
    retry = sub.add_parser('retry'); retry.add_argument('plan')
    status = sub.add_parser('status'); status.add_argument('plan')
    cleanup = sub.add_parser('prune'); cleanup.add_argument('plan')
    args = parser.parse_args()
    if args.action == 'snapshot':
        print(snapshot(args.plan, args.label, not args.no_remote))
    elif args.action == 'verify':
        result = verify(args.archive); print(json.dumps({'verified': True, 'files': len(result['files']), 'photoOriginalsIncluded': result['photoOriginalsIncluded']}))
    elif args.action == 'restore':
        result = restore(args.archive, args.destination); print(json.dumps({'restored': True, 'files': len(result['files'])}))
    elif args.action == 'retry':
        print(json.dumps(retry_remote(args.plan)))
    elif args.action == 'prune':
        print(json.dumps(prune(args.plan)))
    else:
        _, _, root = load_plan(args.plan)
        print(json.dumps({name: json.loads((root / name).read_text()) if (root / name).exists() else None for name in ['latest-status.json', 'replication-status.json', 'retention-status.json']}))


if __name__ == '__main__':
    main()
