import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('art', Path(__file__).resolve().parents[1] / 'scripts/Import-AchievementArtwork.py')
art = importlib.util.module_from_spec(spec)
spec.loader.exec_module(art)
ROOT = Path(__file__).resolve().parents[1]
MANIFEST = json.loads((ROOT / 'src/PixelVault.Tracking/Artwork/manifest.json').read_text())


class ImportTests(unittest.TestCase):
    def row(self, title='Homo Faber', description='Fabricate everything.', identity='062'):
        return dict(Title=title, Description=description, ProviderAchievementId=identity, IconUrlColor=MANIFEST[0]['originalUrl'])

    def html(self, title='Homo Faber', description='Fabricate everything.', image='8714e15'):
        return f'<li><div><a href="https://m.exophase.com/psn/awards/s/{image}.png?x=1"><img src="local.png"></a></div><div><a href="/trophy/game/123-homo/">{title}</a><p>{description}</p></div></li>'

    def test_saved_html_pairs_rows_without_cross_matching_and_uses_original_image_links(self):
        records = art.extract('<ul>' + self.html() + self.html('Fount', 'Restore chips.', '1651163') + '</ul>')
        self.assertEqual(['Homo Faber', 'Fount'], [r['title'] for r in records])
        self.assertEqual('https://m.exophase.com/psn/awards/l/8714e15.png', records[0]['sourceUrl'])
        matched, review = art.match(records, [self.row()])
        self.assertEqual('062', matched[0]['achievementId'])
        self.assertEqual(1, len(review))

    def test_missing_row_image_does_not_borrow_next_trophy_art(self):
        html = '<ul><li><a href="/trophy/game/missing/">Missing</a><p>Missing image.</p></li>' + self.html() + '</ul>'
        self.assertEqual(['Homo Faber'], [r['title'] for r in art.extract(html)])

    def test_unique_names_match_despite_different_or_missing_descriptions(self):
        records = art.extract(self.html())
        matched, review = art.match(records, [self.row(description='A differently worded official description.')])
        self.assertEqual('062', matched[0]['achievementId'])
        self.assertEqual([], review)
        self.assertEqual(1, len(art.match([dict(records[0], description='')], [self.row()])[0]))
        self.assertEqual(1, len(art.match(records, [self.row(description='')])[0]))

    def test_name_matching_normalizes_case_spacing_and_punctuation(self):
        records = art.extract(self.html(title=' HOMO &mdash; FABER! ', description='Different text'))
        matched, review = art.match(records, [self.row()])
        self.assertEqual('062', matched[0]['achievementId'])
        self.assertEqual([], review)

    def test_duplicate_names_on_either_platform_still_require_review(self):
        records = art.extract(self.html())
        self.assertEqual(0, len(art.match(records, [self.row(), self.row(identity='999')])[0]))
        self.assertEqual(0, len(art.match(records + records, [self.row()])[0]))

    def test_punctuation_distinguishes_death_stranding_bb_achievements(self):
        rows = [self.row(title='BB...', identity='005'), self.row(title='"BB"', identity='008')]
        records = [dict(title='BB...', sourceUrl=MANIFEST[0]['sourceUrl']),
                   dict(title='"BB"', sourceUrl=MANIFEST[1]['sourceUrl'])]
        matched, review = art.match(records, rows)
        self.assertEqual([('BB...', '005'), ('"BB"', '008')], [(r['title'], r['achievementId']) for r in matched])
        self.assertEqual([], review)
        # A title with its distinguishing punctuation missing must still require review.
        self.assertEqual([], art.match([dict(records[0], title='BB')], rows)[0])
        # Even an explicit mapping cannot select the other achievement when an exact title exists.
        self.assertEqual([], art.match([dict(records[0], achievementId='008')], rows, approved=True)[0])

    def test_exact_matching_accepts_typographic_quotes_and_ellipsis(self):
        rows = [self.row(title='BB...', identity='005'), self.row(title='"BB"', identity='008')]
        records = [dict(title='  BB… ', sourceUrl=MANIFEST[0]['sourceUrl']),
                   dict(title='“BB”', sourceUrl=MANIFEST[1]['sourceUrl'])]
        matched, review = art.match(records, rows)
        self.assertEqual(['005', '008'], [r['achievementId'] for r in matched])
        self.assertEqual([], review)

    def test_unmatched_and_empty_names_remain_in_review(self):
        for title in ['Different achievement', '', '---']:
            records = [dict(title=title, description='Fabricate everything.', sourceUrl=MANIFEST[0]['sourceUrl'])]
            matched, review = art.match(records, [self.row(), self.row(title='', identity='999')])
            self.assertEqual([], matched)
            self.assertEqual(1, len(review))

    def test_explicit_mapping_requires_both_name_and_id(self):
        record = dict(title='Homo Faber', sourceUrl=MANIFEST[0]['sourceUrl'], achievementId='062')
        self.assertEqual(1, len(art.match([record], [self.row()], approved=True)[0]))
        record['achievementId'] = '999'
        self.assertEqual(0, len(art.match([record], [self.row()], approved=True)[0]))

    def test_sources_reject_external_hosts_credentials_redirect_targets_and_wrong_game(self):
        for value in ['https://evil.test/psn/awards/l/a.png', 'https://m.exophase.com@evil.test/psn/awards/l/a.png', 'http://m.exophase.com/psn/awards/l/a.png', 'https://m.exophase.com:443/psn/awards/l/a.png']:
            with self.assertRaises(ValueError):
                art.artwork_url(value)
        with self.assertRaises(ValueError):
            art.steam_url(MANIFEST[0]['originalUrl'], '123')
        with self.assertRaises(ValueError):
            art.page_url('https://evil.test/game/foo/trophies/')
        self.assertIsNone(art.NoRedirects().redirect_request(None, None, 302, '', {}, 'https://evil.test'))

    def test_apply_preserves_other_games_is_repeatable_and_failure_keeps_existing_manifest(self):
        image = (ROOT / 'src/PixelVault.Tracking/Artwork' / MANIFEST[0]['file']).read_bytes()
        # Use a real downloaded Steam original for integration below; here a verified minimal JPEG fixture.
        import io
        from PIL import Image
        stream = io.BytesIO()
        Image.new('RGB', (64, 64)).save(stream, format='JPEG')
        class Fetch:
            def get(self, url):
                return stream.getvalue() if 'steamstatic' in url else image
        record = dict(MANIFEST[0])
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            unrelated = dict(originalUrl='other-game', file='retained.png')
            (root / 'manifest.json').write_text(json.dumps([unrelated]))
            for _ in range(2):
                imported, failed = art.apply([record], root, '1850570', record['sourcePage'], Fetch())
                self.assertEqual(1, len(imported))
                self.assertEqual([], failed)
                saved = json.loads((root / 'manifest.json').read_text())
                self.assertEqual(2, len(saved))
                self.assertIn(unrelated, saved)
            before = (root / 'manifest.json').read_bytes()
            class Broken:
                def get(self, url):
                    return b'not an image'
            self.assertEqual(1, len(art.apply([record], root, '1850570', record['sourcePage'], Broken())[1]))
            self.assertEqual(before, (root / 'manifest.json').read_bytes())
            self.assertEqual([], list(root.glob('*.tmp')))

    def test_rejects_non_upgrade_and_corrupt_png(self):
        class SameResolution:
            def get(self, url):
                return (ROOT / 'src/PixelVault.Tracking/Artwork' / MANIFEST[0]['file']).read_bytes()
        with tempfile.TemporaryDirectory() as temp:
            imported, failed = art.apply([MANIFEST[0]], temp, '1850570', MANIFEST[0]['sourcePage'], SameResolution())
            self.assertEqual([], imported)
            self.assertEqual(1, len(failed))
            self.assertFalse((Path(temp) / 'manifest.json').exists())
        with self.assertRaises(OSError):
            art.image_size(b'broken')

    def test_live_progress_counts_every_processed_image_even_when_it_fails(self):
        import io
        from PIL import Image
        original = io.BytesIO()
        Image.new('RGB', (64, 64)).save(original, format='JPEG')
        replacement = (ROOT / 'src/PixelVault.Tracking/Artwork' / MANIFEST[0]['file']).read_bytes()
        class Fetch:
            def get(self, url):
                return original.getvalue() if 'steamstatic' in url else replacement
        events = []
        records = [dict(MANIFEST[0]), dict(MANIFEST[0], title='Failed image', sourceUrl='https://unrelated.invalid/no.png')]
        with tempfile.TemporaryDirectory() as temp:
            imported, failed = art.apply(records, temp, '1850570', MANIFEST[0]['sourcePage'], Fetch(), lambda *args: events.append(args))
        self.assertEqual(1, len(imported))
        self.assertEqual(1, len(failed))
        counts = [e[1] for e in events if e[0] == 'images']
        self.assertEqual(sorted(counts), counts)
        self.assertEqual(0, counts[0])
        self.assertEqual(2, counts[-1])
        self.assertTrue(all(e[2] == 2 for e in events))
        self.assertEqual('Failed image', [e for e in events if e[0] == 'images'][-1][3])
        self.assertEqual('staging', events[-1][0])

    def test_progress_output_is_one_json_event_per_line(self):
        import contextlib
        import io
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            art.emit_progress('images', 1, 6, 'Homo Faber', 'Downloading artwork')
        event = json.loads(output.getvalue())
        self.assertEqual('progress', event['type'])
        self.assertEqual(1, event['completed'])
        self.assertEqual(6, event['total'])
        self.assertEqual('Homo Faber', event['title'])


if __name__ == '__main__':
    unittest.main()
