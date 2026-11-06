"""Capture app-only audit evidence. Never stop services or copy photo payloads."""
import argparse
import hashlib
import json
import sqlite3
import subprocess
import uuid
from contextlib import closing
from datetime import datetime, timezone
from pathlib import Path


def read_db(path):
    connection = sqlite3.connect(path.resolve().as_uri() + '?mode=ro', uri=True, timeout=30)
    connection.execute('PRAGMA query_only=ON')
    return connection


def snapshot(source, target):
    target.parent.mkdir(parents=True, exist_ok=True)
    if target.exists():
        raise FileExistsError(target)
    with closing(read_db(source)) as incoming, closing(sqlite3.connect(target)) as output:
        incoming.backup(output)
        if output.execute('PRAGMA quick_check').fetchone()[0] != 'ok':
            raise RuntimeError('Snapshot integrity failure')


def stable_copy(source, target):
    for _ in range(3):
        value = source.read_bytes()
        if value == source.read_bytes():
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(value)
            return
    raise RuntimeError('Changing input: ' + str(source))


def ssh(script):
    result = subprocess.run(['ssh', '-o', 'BatchMode=yes', '-o', 'ConnectTimeout=10',
                           'admin1@192.168.1.99', 'sh'], input=script.encode(),
                          capture_output=True)
    if result.returncode:
        raise RuntimeError(result.stderr.decode(errors='replace'))
    return result.stdout


def capture(root, repo, include_auth_keys=False):
    if any(root.iterdir()):
        raise FileExistsError('Evidence directory must be empty')
    run = root.name
    if not run.replace('-', '').replace('T', '').replace('Z', '').isalnum():
        raise ValueError('Unsafe run name')
    # LF input is intentional: native PowerShell pipelines may append CR to shell lines.
    remote = f'/share/CACHEDEV1_DATA/Container/pixelvault-companion/build/{run}-capture-{uuid.uuid4().hex[:8]}'
    key_argument = ' --include-auth-keys' if include_auth_keys else ''
    script = f'''set -eu
umask 077
mkdir {remote}
mkdir {remote}/helper
mkdir {remote}/output
tar -xf /share/CACHEDEV1_DATA/Container/pixelvault-companion/build/{run}-helper.tar -C {remote}/helper
/share/CACHEDEV1_DATA/.qpkg/container-station/bin/docker run --rm --network none --read-only --tmpfs /tmp:rw,noexec,nosuid,size=64m --entrypoint dotnet --user 1000:100 --cap-drop ALL --security-opt no-new-privileges:true -v /share/CACHEDEV1_DATA/Container/pixelvault-companion/data:/data -v {remote}/helper:/helper:ro -v {remote}/output:/audit pixelvault-companion:0.38.0 /helper/Snapshot.dll /data /audit/nas{key_argument}
'''
    print(ssh(script).decode().strip())
    archive = root / 'nas-app-metadata.tar'
    with archive.open('wb') as output:
        subprocess.run(['ssh', '-o', 'BatchMode=yes', 'admin1@192.168.1.99',
                        f'tar -cf - -C {remote}/output nas'], stdout=output, check=True)
    import tarfile
    with tarfile.open(archive) as incoming:
        incoming.extractall(root, filter='data')
    pc = root / 'pc'
    source = repo / 'PixelVaultData'
    started = datetime.now(timezone.utc).isoformat()
    for name in ['achievement-tracking/pixelvault-tracking.sqlite', 'guides/pixelvault-guides.sqlite',
                 'capture-groups.sqlite', 'game-maps/maps.sqlite', 'cache/pixelvault-index-e_game_captures.sqlite']:
        snapshot(source / name, pc / name)
    files = [source / 'PixelVault.settings.ini', source / 'achievement-hub-pins.json', source / 'achievement-artwork/manifest.json']
    connector_catalog = repo / 'PixelVaultCompanionConnectorData/library-cache/catalog.json'
    if connector_catalog.exists():
        files.append(connector_catalog)
    files += list((source / 'achievement-hub').glob('*.json'))
    files += list((source / 'cache').glob('library-folders-e_game_captures.cache'))
    for file in files:
        if file.exists():
            relative = file.relative_to(source) if file.is_relative_to(source) else Path('connector-library-catalog.json')
            stable_copy(file, pc / relative)
    connector = repo / 'PixelVaultCompanionConnectorData'
    config = json.loads((connector / 'connector.json').read_text(encoding='utf-8-sig'))
    config = {key: value for key, value in config.items() if key.casefold() != 'token'}
    (pc / 'connector-redacted.json').write_text(json.dumps(config, indent=2), encoding='utf-8')
    for name in ['automatic-photos.json', 'monitored-saves.json', 'photo-sync-status.json']:
        stable_copy(connector / name, pc / name)
    # Common baseline is evidence only; its provenance must be validated by the comparison.
    baseline = repo / 'Companion Backups/nas-migration-20261025T164321Z/workspace'
    snapshot(baseline / 'achievement-tracking/pixelvault-tracking.sqlite', root / 'baseline/tracking.sqlite')
    stable_copy(baseline / 'workspace.json', root / 'baseline/workspace.json')
    stable_copy(repo / 'PixelVaultCompanionData/hosting.json', pc / 'dormant-companion-hosting.json')
    records = []
    for file in sorted(root.rglob('*')):
        if file.is_file():
            with file.open('rb') as stream:
                checksum = hashlib.file_digest(stream, 'sha256').hexdigest()
            records.append({'path': file.relative_to(root).as_posix(), 'bytes': file.stat().st_size,
                            'sha256': checksum})
    (root / 'capture-manifest.json').write_text(json.dumps({
        'version': 1, 'pcStartedUtc': started, 'completedUtc': datetime.now(timezone.utc).isoformat(),
        'consistency': 'Independent SQLite backup snapshots, not a global cutover transaction',
        'files': records, 'photoPayloadsCopied': 0, 'remoteEvidence': remote,
    }, indent=2), encoding='utf-8')
    print('PC/NAS app and catalogue evidence captured; no runtime settings changed.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('evidence', type=Path)
    parser.add_argument('--repo', type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument('--include-auth-keys', action='store_true')
    args = parser.parse_args()
    capture(args.evidence.resolve(), args.repo.resolve(), args.include_auth_keys)
