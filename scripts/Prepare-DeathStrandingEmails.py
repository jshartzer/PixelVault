"""Prepare a private mail reference checklist from read-only localized exports.

No read-state inference. Extracted prose and generated JSON stay outside source.
"""
import html
import json
import re
import sys
from pathlib import Path

def plain(value):
    text = re.sub(r'<br\s*/?>', '\n', value or '', flags=re.I)
    text = re.sub(r'</?color(?:\s+[^>]*)?>', '', text, flags=re.I)
    return html.unescape(text).replace('\r\n', '\n').strip()

def prepare(rows):
    usable = [r for r in rows if plain(r.get('Title')) and plain(r.get('Text'))]
    if len({r['ResourceUUID'] for r in usable}) != len(usable):
        raise ValueError('Duplicate mail identity')
    items = []
    for order, row in enumerate(sorted(usable, key=lambda r: (r.get('SenderName', ''), r['Title'], r['ResourceUUID']))):
        body = plain(row['Text'])
        if len(body) > 100000:
            raise ValueError('Mail body exceeds content limit')
        items.append(dict(Id=row['ResourceUUID'], Name=plain(row['Title']), Category=plain(row.get('SenderName')) or 'Other senders',
                          Detail='\n'.join(v for v in [plain(row.get('SenderName')), plain(row.get('Location'))] if v)
                          + '\n\nMark complete after reading this email in the game. Automatic read detection is awaiting validation.',
                          Content=body, Target=1, Order=order,
                          Identifiers={'ResourceUUID': row['ResourceUUID'], 'Language': 'English', 'ContentSource': 'Installed game: ds/archives/mail_resource.core'}))
    if not items or sum(len(i['Content']) for i in items) > 2000000:
        raise ValueError('Empty or oversized mail catalogue')
    return dict(Id='death-stranding-dc.email-catalogue', Provider='steam', GameId='1850570',
                GameTitle="Death Stranding Director's Cut", Title='Email catalogue',
                Description='Installed English email reference library, grouped by sender. Mark emails read manually. Automatic read detection is awaiting validation. Opening stored content does not mark mail read in the game.',
                CompletionLabel='Marked read', Milestones=[], CategoryTargets={}, Items=items)

if __name__ == '__main__':
    source, destination = map(Path, sys.argv[1:])
    rows = json.loads(source.read_text(encoding='utf-8-sig'))
    result = prepare(rows)
    destination.mkdir(parents=True, exist_ok=True)
    (destination/'tracker-definition.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    summary = dict(Resources=len(rows), Records=len(result['Items']), EmptyExcluded=len(rows)-len(result['Items']),
                   Senders=len({i['Category'] for i in result['Items']}), ContentCharacters=sum(len(i['Content']) for i in result['Items']))
    (destination/'catalogue-evidence.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
    print(json.dumps(summary))
