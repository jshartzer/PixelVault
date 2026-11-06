"""Import higher-resolution trophy artwork without modifying provider data or tracker state.

Plan by default. --apply downloads verified matches and atomically updates the artwork manifest.
Requires Pillow: python -m pip install Pillow
"""
import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import re
import tempfile
import time
import unicodedata
from html.parser import HTMLParser
from urllib.parse import urljoin, urlsplit
from urllib.request import Request, build_opener, HTTPRedirectHandler
from urllib.error import HTTPError

from PIL import Image

MAX_IMAGE = 786432


def emit_progress(stage, completed=0, total=0, title='', message=''):
    print(json.dumps(dict(type='progress', stage=stage, completed=completed, total=total,
                          title=title, message=message)), flush=True)


def normalized(value):
    return ''.join(c for c in unicodedata.normalize('NFKC', value or '').casefold() if c.isalnum())


def exact_name(value):
    # Preserve meaningful punctuation. NFKC expands the ellipsis character to three dots;
    # typographic quotation marks and whitespace may differ between otherwise identical titles.
    text = unicodedata.normalize('NFKC', value or '').casefold()
    text = text.translate(str.maketrans({'“': '"', '”': '"', '‘': "'", '’': "'"}))
    return ' '.join(text.split())


def artwork_url(value):
    uri = urlsplit(urljoin('https://m.exophase.com/', value))
    if (uri.scheme != 'https' or uri.netloc != 'm.exophase.com'
            or not re.fullmatch(r'/psn/awards/[sl]/[A-Za-z0-9]+\.png', uri.path)):
        raise ValueError('Expected an Exophase PSN trophy PNG URL.')
    return 'https://m.exophase.com' + uri.path.replace('/awards/s/', '/awards/l/')


def page_url(value):
    uri = urlsplit(value)
    if (uri.scheme != 'https' or uri.netloc != 'www.exophase.com' or uri.query or uri.fragment
            or not re.fullmatch(r'/game/[a-z0-9-]+/(?:trophies|achievements)/', uri.path)):
        raise ValueError('Use the public Exophase game trophy/achievement list URL.')
    return value


class Node:
    def __init__(self, tag='', attrs=(), parent=None):
        self.tag, self.attrs, self.parent, self.children = tag, dict(attrs), parent, []

    def nodes(self):
        yield self
        for child in self.children:
            if isinstance(child, Node):
                yield from child.nodes()

    def text(self):
        return ' '.join(c.text() if isinstance(c, Node) else c for c in self.children).strip()


class TrophyHtml(HTMLParser):
    VOID = {'img', 'br', 'hr', 'meta', 'link', 'input', 'source', 'wbr', 'area', 'base', 'embed', 'param', 'track', 'col'}

    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.root = self.current = Node()

    def handle_starttag(self, tag, attrs):
        node = Node(tag, attrs, self.current)
        self.current.children.append(node)
        if tag not in self.VOID:
            self.current = node

    def handle_startendtag(self, tag, attrs):
        self.handle_starttag(tag, attrs)
        if tag not in self.VOID:
            self.handle_endtag(tag)

    def handle_endtag(self, tag):
        node = self.current
        while node.parent is not None:
            if node.tag == tag:
                self.current = node.parent
                return
            node = node.parent

    def handle_data(self, data):
        self.current.children.append(data)


def extract(html):
    parser = TrophyHtml()
    parser.feed(html)
    records, seen = [], set()
    for link in parser.root.nodes():
        if link.tag != 'a' or not re.search(r'/(?:trophy|achievement)/', link.attrs.get('href', '')) or not link.text():
            continue
        parent = link.parent
        while parent is not None:
            links = [n for n in parent.nodes() if n.tag == 'a' and n.text()
                     and re.search(r'/(?:trophy|achievement)/', n.attrs.get('href', ''))]
            images = set()
            for n in parent.nodes():
                # Prefer the image link, which also works in browser-saved HTML with rewritten img paths.
                for attr in ('href', 'data-src', 'src'):
                    try:
                        images.add(artwork_url(n.attrs.get(attr, '')))
                    except ValueError:
                        pass
            if len(links) > 1:
                break  # Never associate artwork across trophy rows.
            if len(links) == 1 and len(images) == 1:
                descriptions = [n.text() for n in parent.nodes() if n.tag == 'p' or
                                'description' in n.attrs.get('class', '').lower()]
                descriptions = list(dict.fromkeys(d for d in descriptions if d and d != link.text()))
                record = {'title': link.text(), 'description': descriptions[0] if len(descriptions) == 1 else '',
                          'sourceUrl': images.pop()}
                key = (record['title'], record['sourceUrl'])
                if key not in seen:
                    records.append(record)
                    seen.add(key)
                break
            parent = parent.parent
    return records


def catalogue(path, game):
    data = json.loads(Path(path).read_text(encoding='utf-8-sig'))
    rows = data.get('Games', {}).get(game, {}).get('Rows', []) if isinstance(data, dict) else data
    if not rows:
        raise ValueError('Catalogue has no achievement rows for this game.')
    rows = [r for r in rows if r.get('Provider', 'steam') == 'steam' and str(r.get('ProviderGameId', game)) == game]
    ids = [r['ProviderAchievementId'] for r in rows]
    if len(ids) != len(set(ids)):
        raise ValueError('Catalogue contains duplicate achievement IDs.')
    return rows


def match(records, rows, approved=False):
    matched, review = [], []
    for record in records:
        name = normalized(record.get('title'))
        candidates = [r for r in rows if normalized(r['Title']) == name] if name else []
        exact = [r for r in candidates if exact_name(r['Title']) == exact_name(record.get('title'))]
        if exact:
            candidates = exact
        if approved:
            requested = record.get('achievementId', record.get('steamAchievementId'))
            candidates = [r for r in candidates if r['ProviderAchievementId'] == requested]
        if len(candidates) != 1:
            review.append(dict(record, reason='Missing or ambiguous achievement name/ID.'))
            continue
        row = candidates[0]
        # Descriptions remain informational. Prefer punctuation-preserving titles before
        # the unique loose-name fallback so achievements such as BB... and "BB" stay distinct.
        # Steam remains authoritative when it supplies a description. Carry Exophase text
        # only for achievements whose Steam definition has no description.
        description = (record.get('description') or '').strip() if not (row.get('Description') or '').strip() else ''
        matched.append(dict(record, description=description, achievementId=row['ProviderAchievementId'], originalUrl=row['IconUrlColor']))
    # Multiple source entries for one provider achievement require review, even when names match.
    duplicates = {r['achievementId'] for r in matched if sum(x['achievementId'] == r['achievementId'] for x in matched) > 1}
    review.extend(dict(r, reason='Multiple source trophies matched this achievement.') for r in matched if r['achievementId'] in duplicates)
    return [r for r in matched if r['achievementId'] not in duplicates], review


class NoRedirects(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class Fetcher:
    def __init__(self, cache):
        self.cache, self.last = Path(cache), 0
        self.opener = build_opener(NoRedirects())

    def get(self, url, limit=MAX_IMAGE):
        self.cache.mkdir(parents=True, exist_ok=True)
        path = self.cache / hashlib.sha256(url.encode()).hexdigest()
        if path.exists() and path.stat().st_size <= limit:
            return path.read_bytes()
        time.sleep(max(0, 1 - (time.monotonic() - self.last)))
        self.last = time.monotonic()
        try:
            with self.opener.open(Request(url, headers={'User-Agent': 'PixelVault-ArtworkImporter/1.0'}), timeout=20) as response:
                data = response.read(limit + 1)
        except HTTPError as error:
            raise ValueError(f'HTTP {error.code}: access was refused or redirected. For a blocked page, save its HTML in your browser and use --html. No bypass or automatic retry was attempted.') from error
        if len(data) > limit:
            raise ValueError('Response exceeds the import size limit.')
        atomic(path, data)
        return data


def atomic(path, data):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, temp = tempfile.mkstemp(prefix='pv-art-', suffix='.tmp', dir=path.parent)
    try:
        with os.fdopen(fd, 'wb') as file:
            file.write(data)
        os.replace(temp, path)
    finally:
        if os.path.exists(temp):
            os.unlink(temp)


def image_size(data, require_png=False):
    if len(data) > MAX_IMAGE:
        raise ValueError('Image exceeds the import size limit.')
    with Image.open(io.BytesIO(data)) as image:
        if (image.format not in ('PNG', 'JPEG') or (require_png and image.format != 'PNG')
                or image.width > 2048 or image.height > 2048):
            raise ValueError('Unsupported image format or dimensions.')
        size = image.size
        image.verify()
    # Decode as well as checking the container, before publishing a manifest entry.
    with Image.open(io.BytesIO(data)) as image:
        image.load()
    return size


def steam_url(value, game):
    uri = urlsplit(value)
    if (uri.scheme != 'https' or uri.netloc not in ('shared.fastly.steamstatic.com', 'shared.akamai.steamstatic.com',
            'cdn.akamai.steamstatic.com', 'cdn.cloudflare.steamstatic.com', 'steamcdn-a.akamaihd.net', 'media.steampowered.com')
            or not re.fullmatch(r'/(?:community_assets|steamcommunity/public)/images/apps/' + re.escape(game) + r'/[a-fA-F0-9]{40}\.(?:jpg|png)', uri.path)):
        raise ValueError('Catalogue image URL is not a supported Steam achievement URL.')
    return 'https://shared.fastly.steamstatic.com' + uri.path.replace('/steamcommunity/public/', '/community_assets/')


def apply(matches, output, game, source_page, fetcher, progress=None):
    output = Path(output)
    manifest = output / 'manifest.json'
    existing = json.loads(manifest.read_text(encoding='utf-8')) if manifest.exists() else []
    if not isinstance(existing, list):
        raise ValueError('Existing artwork manifest must be a JSON array.')
    saved = {e['originalUrl']: e for e in existing}
    imported, failed = [], []
    if progress:
        progress('images', 0, len(matches), '', 'Starting image downloads')
    for index, record in enumerate(matches):
        if progress:
            progress('images', index, len(matches), record['title'], 'Downloading artwork')
        try:
            original = steam_url(record['originalUrl'], game)
            url = artwork_url(record['sourceUrl'])
            data = fetcher.get(url)
            width, height = image_size(data, require_png=True)
            if progress:
                progress('images', index, len(matches), record['title'], 'Checking original image and resolution')
            old_width, old_height = image_size(fetcher.get(original))
            if width != height or width <= max(64, old_width) or height <= max(64, old_height):
                raise ValueError('Replacement must be square and higher resolution than the Steam original.')
            sha = hashlib.sha256(data).hexdigest()
            entry = dict(record, provider='steam', gameId=game, originalUrl=original, sourceUrl=url,
                         sourcePage=source_page, file=sha + '.png', sha256=sha, width=width, height=height)
            if not entry.get('description') and original in saved:
                entry['description'] = saved[original].get('description', '')
            atomic(output / entry['file'], data)
            saved[original] = entry
            imported.append({'title': entry['title'], 'achievementId': entry['achievementId'], 'width': width, 'height': height})
        except (ValueError, OSError, EOFError) as error:
            failed.append({'title': record['title'], 'error': str(error)})
        if progress:
            progress('images', index + 1, len(matches), record['title'], 'Image processed')
    if imported:
        if progress:
            progress('staging', len(matches), len(matches), '', 'Saving verified images')
        # Assets are immutable and complete before the manifest becomes visible to either app.
        atomic(manifest, (json.dumps(list(saved.values()), indent=2) + '\n').encode())
    return imported, failed


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--catalogue', required=True, help='Native achievement-hub JSON or an exported array of achievement rows.')
    parser.add_argument('--game', required=True, help='Steam app ID. Choose the correct edition of the source game.')
    parser.add_argument('--page-url', required=True, help='Public Exophase game trophy list URL (source provenance).')
    source = parser.add_mutually_exclusive_group()
    source.add_argument('--html', help='Browser-saved trophy list HTML, for pages that block automated requests.')
    source.add_argument('--mapping', help='Explicitly reviewed JSON array: title, achievementId (or steamAchievementId), sourceUrl.')
    parser.add_argument('--output', required=True, help='Artwork folder: <data root>/achievement-artwork, or a staging folder.')
    parser.add_argument('--cache', help='Optional shared download cache, for staged imports from the app.')
    parser.add_argument('--progress', action='store_true', help='Emit live JSON progress lines for the app.')
    parser.add_argument('--apply', action='store_true', help='Download verified matches and merge the manifest. Default only plans.')
    args = parser.parse_args()
    progress = emit_progress if args.progress else None
    if progress:
        progress('prepare', message='Reading achievement definitions')
    page = page_url(args.page_url)
    rows = catalogue(args.catalogue, args.game)
    output = Path(args.output)
    fetcher = Fetcher(args.cache or output / '.downloads')
    if args.mapping:
        if progress:
            progress('source', message='Reading reviewed artwork matches')
        records = json.loads(Path(args.mapping).read_text(encoding='utf-8-sig'))
        if not isinstance(records, list):
            raise ValueError('Mapping must be an array of reviewed entries.')
        if any(str(r.get('gameId', r.get('steamAppId', args.game))) != args.game for r in records):
            raise ValueError('Mapping belongs to a different game.')
    else:
        if progress:
            progress('source', message='Reading saved trophy page' if args.html else 'Downloading trophy page')
        html = Path(args.html).read_text(encoding='utf-8-sig') if args.html else fetcher.get(page, 4 * 1024 * 1024).decode('utf-8')
        records = extract(html)
        if not records:
            raise ValueError('No trophy rows found. Save the rendered trophy list HTML, or use an explicitly reviewed --mapping file.')
    matches, review = match(records, rows, approved=bool(args.mapping))
    if progress:
        progress('matching', message='Matching achievement names')
    report = {'gameId': args.game, 'sourcePage': page, 'mode': 'apply' if args.apply else 'plan',
              'matched': matches, 'review': review, 'imported': [], 'failed': []}
    if args.apply:
        report['imported'], report['failed'] = apply(matches, output, args.game, page, fetcher, progress)
    atomic(output / 'import-report.json', (json.dumps(report, indent=2) + '\n').encode())
    print(json.dumps({'matched': len(matches), 'review': len(review), 'imported': len(report['imported']),
                      'failed': len(report['failed']), 'report': str(output / 'import-report.json')}))
    return 2 if report['failed'] or not matches else 0


if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except (ValueError, OSError) as error:
        raise SystemExit(str(error))
