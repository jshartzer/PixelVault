import importlib.util
import json
import sqlite3
import tempfile
import unittest
import zipfile
from datetime import datetime, timedelta, timezone
from contextlib import closing
from pathlib import Path
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('app_backup', Path(__file__).with_name('app_backup.py'))
backup = importlib.util.module_from_spec(spec)
spec.loader.exec_module(backup)


class AppBackupTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.app = self.root / 'app'; self.app.mkdir()
        self.state = self.root / 'state'; self.state.mkdir()
        self.plan = self.root / 'plan.json'
        self.source = sqlite3.connect(self.app / 'records.sqlite')
        self.source.execute('PRAGMA journal_mode=WAL')
        self.source.execute('CREATE TABLE notes(id INTEGER PRIMARY KEY, note TEXT)')
        self.source.execute("INSERT INTO notes VALUES(1,'keep this note')")
        self.source.commit()
        self.other = sqlite3.connect(self.state / 'owner.sqlite')
        self.other.execute('CREATE TABLE owner(id INTEGER PRIMARY KEY, hash TEXT)')
        self.other.execute("INSERT INTO owner VALUES(1,'password-hash')"); self.other.commit()
        (self.app / 'settings.ini').write_text('steam_web_api_key=private-test-key')
        (self.app / 'photos').mkdir(); (self.app / 'photos/original.jpg').write_bytes(b'not backed up')
        (self.app / 'cache').mkdir(); (self.app / 'cache/thumbnail.jpg').write_bytes(b'not backed up')
        self.value = {'version': 1, 'purpose': 'pixelvault-app-backups', 'roots': {'application': str(self.app), 'state': str(self.state)}, 'backupRoot': str(self.root / 'backups'), 'include': [
            {'root': 'application', 'path': 'records.sqlite', 'kind': 'sqlite', 'required': True},
            {'root': 'application', 'path': 'settings.ini', 'kind': 'file', 'required': True},
            {'root': 'state', 'path': 'owner.sqlite', 'kind': 'sqlite', 'required': True}]}
        self.save_plan()

    def tearDown(self):
        self.source.close(); self.other.close(); self.temp.cleanup()

    def save_plan(self):
        self.plan.write_text(json.dumps(self.value))

    def test_committed_wal_notes_owner_and_secret_restore_without_photo_or_cache(self):
        self.assertTrue((self.app / 'records.sqlite-wal').stat().st_size)
        archive = backup.snapshot(self.plan, 'test', False)
        manifest = backup.restore(archive, self.root / 'restored')
        self.assertEqual(3, len(manifest['files']))
        self.assertEqual('private-test-key', (self.root / 'restored/application/settings.ini').read_text().split('=')[1])
        with closing(sqlite3.connect(self.root / 'restored/application/records.sqlite')) as db:
            self.assertEqual('keep this note', db.execute('SELECT note FROM notes').fetchone()[0])
        with closing(sqlite3.connect(self.root / 'restored/state/owner.sqlite')) as db:
            self.assertEqual('password-hash', db.execute('SELECT hash FROM owner').fetchone()[0])
        with zipfile.ZipFile(archive) as z:
            self.assertFalse(any('photos/' in name or 'cache/' in name or name.endswith(('-wal', '-shm')) for name in z.namelist()))

    def test_all_database_writers_reserved_while_snapshots_are_read(self):
        original = backup.database_info
        checks = []
        def check(path):
            for source in [self.app / 'records.sqlite', self.state / 'owner.sqlite']:
                db = sqlite3.connect(source, timeout=.01)
                try:
                    with self.assertRaises(sqlite3.OperationalError):
                        db.execute('BEGIN IMMEDIATE')
                    checks.append(True)
                finally:
                    db.close()
            return original(path)
        with patch.object(backup, 'database_info', side_effect=check):
            backup.snapshot(self.plan, replicate=False)
        self.assertEqual(4, len(checks))

    def test_changing_metadata_does_not_publish_inconsistent_archive(self):
        original = backup.database_info
        counter = 0
        def change(path):
            nonlocal counter
            counter += 1
            (self.app / 'settings.ini').write_text(str(counter))
            return original(path)
        with patch.object(backup, 'database_info', side_effect=change):
            with self.assertRaises(RuntimeError):
                backup.snapshot(self.plan, replicate=False)
        self.assertEqual([], list((self.root / 'backups').glob('app-*.zip')))

    def test_restore_refuses_existing_and_source_locations(self):
        archive = backup.snapshot(self.plan, replicate=False)
        with self.assertRaises(FileExistsError):
            backup.restore(archive, self.app)
        with self.assertRaises(ValueError):
            backup.restore(archive, self.app / 'nested')

    def test_archive_checksums_and_unmanifested_entries_are_rejected(self):
        archive = backup.snapshot(self.plan, replicate=False)
        with zipfile.ZipFile(archive, 'a') as z:
            z.writestr('state/unexpected.txt', 'extra')
        with self.assertRaises(ValueError):
            backup.verify(archive)

    def test_archive_path_traversal_and_recursive_source_patterns_are_rejected(self):
        for value in ['../escape', '/absolute', 'state/../escape', 'state\\bad', 'C:/bad']:
            with self.assertRaises(ValueError):
                backup.safe_relative(value)
        self.value['include'][0]['path'] = '**/*.sqlite'; self.save_plan()
        with self.assertRaises(ValueError):
            backup.load_plan(self.plan)

    def test_destination_cannot_be_inside_source_and_required_inputs_fail_closed(self):
        self.value['backupRoot'] = str(self.app / 'nested'); self.save_plan()
        with self.assertRaises(ValueError):
            backup.snapshot(self.plan, replicate=False)
        self.value['backupRoot'] = str(self.root / 'backups')
        self.value['include'][0]['path'] = 'missing.sqlite'; self.save_plan()
        with self.assertRaises(FileNotFoundError):
            backup.snapshot(self.plan, replicate=False)

    def test_failed_replication_is_queued_and_retried_with_verified_hash(self):
        backup.snapshot(self.plan, replicate=False)
        self.value['remote'] = {'host': 'backup@192.0.2.1', 'directory': '/safe/app-backups'}; self.save_plan()
        with patch.object(backup.subprocess, 'run', side_effect=OSError('offline')):
            status = backup.retry_remote(self.plan)
        self.assertFalse(next(iter(status['archives'].values()))['copied'])
        archive = next((self.root / 'backups').glob('app-*.zip'))
        class Result:
            stdout = backup.digest(archive.read_bytes()) + '  remote'
        with patch.object(backup.subprocess, 'run', return_value=Result()):
            status = backup.retry_remote(self.plan)
        self.assertTrue(next(iter(status['archives'].values()))['copied'])

    def test_retention_preserves_daily_weekly_named_and_uncopied_points(self):
        now = datetime(2026, 10, 31, tzinfo=timezone.utc)
        records = [(str(i), now - timedelta(days=i), 'migration' if i == 89 else None) for i in range(90)]
        keep = backup.retained(records, ['88'])
        self.assertTrue(set(str(i) for i in range(14)).issubset(keep))
        self.assertIn('89', keep); self.assertIn('88', keep)
        self.assertNotIn('87', keep)
        weeks = {stamp.isocalendar()[:2] for name, stamp, _ in records if name in keep and name not in ('88', '89')}
        self.assertEqual(8, len(weeks))

    def test_sqlite_backup_aborts_instead_of_waiting_indefinitely_on_busy_source(self):
        self.other.execute('BEGIN EXCLUSIVE')
        try:
            with patch.object(backup.time, 'monotonic', side_effect=[0, 10]):
                with self.assertRaises(TimeoutError):
                    backup.copy_database(self.state / 'owner.sqlite', self.root / 'busy-copy.sqlite')
        finally:
            self.other.rollback()


if __name__ == '__main__':
    unittest.main()
