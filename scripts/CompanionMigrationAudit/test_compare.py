import tempfile
import unittest
from contextlib import closing
from pathlib import Path

from capture import read_db, snapshot
from compare import MISSING, classify, job_state, media_classification, photo_id, projected_groups, proposal


class ReconciliationTests(unittest.TestCase):
    def test_independent_changes_require_review(self):
        # Includes a reversal to zero and an intentional note clearing.
        self.assertEqual(classify(0, 2, 1, True), 'conflict-both-changed')
        self.assertEqual(classify('PC edit', '', 'old note', True), 'conflict-both-changed')

    def test_missing_baseline_never_authorizes_replacement(self):
        self.assertEqual(classify(None, 3), 'review-no-common-baseline')
        self.assertEqual(classify(MISSING, {'note': ''}), 'review-no-common-baseline')

    def test_three_way_baseline_distinguishes_single_side_edits(self):
        self.assertEqual(classify(0, 1, 0, True), 'nas-only-change')
        self.assertEqual(classify(1, 0, 0, True), 'pc-only-change')
        self.assertEqual(classify('', '', 'old', True), 'equal')

    def test_operation_replay_identity_and_guards(self):
        first = proposal('note', ['tracker', 'account', 'item'], '', 'web', 'nas-only-change')
        self.assertEqual(first, proposal('note', ['tracker', 'account', 'item'], '', 'web', 'nas-only-change'))
        self.assertNotEqual(first['operationId'], proposal('note', ['tracker', 'account', 'item'], 'changed', 'web', 'nas-only-change')['operationId'])
        self.assertNotEqual(first['operationId'], proposal('note', ['tracker', 'different-account', 'item'], '', 'web', 'nas-only-change')['operationId'])

    def test_missing_drive_is_not_deletion_or_nas_only(self):
        p = {'id': 'photo'}
        self.assertEqual(media_classification(p, None, {}, False), 'source-root-unavailable')
        self.assertEqual(media_classification(p, None, {'photo': p}, False), 'intentionally-hidden')

    def test_photo_identity_retains_workspace_and_edition_scope(self):
        args = ('workspace', 'edition', 'E:\\Captures', 'E:\\Captures\\Game\\Photo.png')
        self.assertEqual(photo_id(*args), photo_id('workspace', 'edition', 'E:\\Captures', 'E:\\Captures\\game\\photo.PNG'))
        self.assertNotEqual(photo_id(*args), photo_id('other-workspace', *args[1:]))
        self.assertNotEqual(photo_id(*args), photo_id('workspace', 'other-edition', *args[2:]))
        with self.assertRaises(ValueError):
            photo_id('workspace', 'edition', 'E:\\Captures', 'E:\\Private\\secret.png')

    def test_historical_failed_jobs_are_not_pending_work(self):
        self.assertEqual(job_state({'Tasks': [{'Status': 'failed'}]}), 'failed')
        self.assertEqual(job_state({'Tasks': [{'Status': 'complete'}, {'Status': 'superseded'}]}), 'complete')
        self.assertEqual(job_state({'Tasks': [{'Status': 'review'}]}), 'review')
        self.assertEqual(job_state({'Tasks': [{'Status': 'reading'}]}), 'unfinished')

    def test_groups_follow_legacy_editions_after_storage_consolidation(self):
        catalogue = {'provider': 'steam', 'gameId': 'game', 'photos': [{'editionId': 'new-edition'}]}
        groups = {('old-edition', 'game\\photo.png'): 'Cinematic'}
        storage = {'old-edition': 'family', 'new-edition': 'family'}
        self.assertEqual(projected_groups(catalogue, storage, groups), {'game\\photo.png': 'Cinematic'})
        groups[('storage:family', 'game\\photo.png')] = 'Updated group'
        self.assertEqual(projected_groups(catalogue, storage, groups), {'game\\photo.png': 'Updated group'})

    def test_snapshots_include_committed_wal_and_leave_source_unchanged(self):
        import sqlite3
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / 'source.sqlite'
            writer = sqlite3.connect(source)
            writer.execute('PRAGMA journal_mode=WAL')
            writer.execute('CREATE TABLE state(value TEXT)')
            writer.execute("INSERT INTO state VALUES ('committed WAL value')")
            writer.commit()
            destination = root / 'snapshot.sqlite'
            snapshot(source, destination)
            with closing(read_db(destination)) as copied:
                self.assertEqual(copied.execute('SELECT value FROM state').fetchone()[0], 'committed WAL value')
            with closing(read_db(source)) as readonly:
                with self.assertRaises(sqlite3.OperationalError):
                    readonly.execute("UPDATE state SET value='forbidden'")
            self.assertEqual(writer.execute('SELECT value FROM state').fetchone()[0], 'committed WAL value')
            with self.assertRaises(FileExistsError):
                snapshot(source, destination)
            writer.close()


if __name__ == '__main__':
    unittest.main()
