#!/usr/bin/env python3
"""Keep README/guide snippets equal to their compiled C# sources; validate local links."""
import pathlib
import re
import sys
from urllib.parse import unquote

root = pathlib.Path(__file__).resolve().parent.parent
write = sys.argv[1:] == ['--write']
if sys.argv[1:] not in ([], ['--write']):
    raise SystemExit('Usage: python3 scripts/check-docs.py [--write]')
pattern = re.compile(r'<!-- snippet: ([^ ]+) -->\n```csharp\n.*?```\n<!-- /snippet -->', re.S)
errors = []
count = 0
for doc in [root / 'README.md', *sorted((root / 'docs').glob('*.md')), *sorted((root / 'examples').glob('*/README.md'))]:
    original = doc.read_text()
    def replace(match):
        global count
        count += 1
        target, _, region = match[1].partition('#')
        source = (root / target).resolve()
        if not source.is_relative_to(root):
            raise SystemExit('Snippet source escapes repository')
        code = source.read_text()
        if region:
            selected = re.search(r'^ *#region ' + re.escape(region) + r'\n(.*?)^ *#endregion', code, re.M | re.S)
            if not selected:
                raise SystemExit(f'Missing region {region} in {target}')
            import textwrap
            code = textwrap.dedent(selected[1])
        return f'<!-- snippet: {match[1]} -->\n```csharp\n{code.rstrip()}\n```\n<!-- /snippet -->'
    updated = pattern.sub(replace, original)
    if updated != original:
        if write:
            doc.write_text(updated)
        else:
            errors.append(f'{doc.relative_to(root)}: snippet drift; run python3 scripts/check-docs.py --write')
    for target in re.findall(r'\[[^\]]+\]\(([^\s)]+)\)', updated):
        if re.match(r'^[a-z]+:', target) or target.startswith('#'):
            continue
        path = unquote(target.split('#', 1)[0])
        if not (doc.parent / path).exists():
            errors.append(f'{doc.relative_to(root)}: missing link target {target}')
if errors:
    raise SystemExit('\n'.join(errors))
print(f'{count} compiled C# snippets and local documentation links verified')
