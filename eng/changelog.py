#!/usr/bin/env python3
"""Release notes from docs/releases/<version>.yaml, the only place they are written.

  python eng/changelog.py                 writes CHANGELOG.md
  python eng/changelog.py --check         fails when a release file is invalid, the current version has no
                                          complete release file, or CHANGELOG.md is out of date
  python eng/changelog.py --release-body v0.4.0 --repository owner/jourfold [--existing body.md]
                                          prints the GitHub release text: the existing text, Install, Changes

English is required for every entry. The release being built needs every language the app ships
(src/Jourfold.Desktop/Strings/*.json); older releases may lack later languages, the app then shows English.
"""
from pathlib import Path
import argparse
import re
import sys

import yaml

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'eng'))
import version as versions  # noqa: E402

RELEASES = ROOT / 'docs' / 'releases'
CHANGELOG = ROOT / 'CHANGELOG.md'
MARKER = '<!-- jourfold-release-notes: generated from docs/releases, edit above this line -->'
SECTIONS = [('highlights', 'Highlights'), ('changes', 'Changes'), ('fixes', 'Fixes')]


def key(version):
    core, _, pre = version.partition('-')
    return tuple(int(p) for p in core.split('.')) + ((1,) if not pre else (0, pre))


def load():
    releases = []
    for path in RELEASES.glob('*.yaml'):
        data = yaml.safe_load(path.read_text(encoding='utf-8')) or {}
        data['_path'] = path
        releases.append(data)
    return sorted(releases, key=lambda r: key(str(r.get('version', '0.0.0'))), reverse=True)


def languages():
    return sorted(p.stem for p in (ROOT / 'src' / 'Jourfold.Desktop' / 'Strings').glob('*.json'))


def icons():
    text = (ROOT / 'src' / 'Jourfold.Desktop' / 'Theme' / 'Icons.axaml').read_text(encoding='utf-8')
    return set(re.findall(r'x:Key="Icon\.([a-z0-9-]+)"', text))


def problems(releases, current):
    found, known = [], icons()
    if not any(str(r.get('version')) == current for r in releases):
        found.append(f'docs/releases/{current}.yaml is missing: every release needs notes.')
    for r in releases:
        name = r['_path'].name; version = str(r.get('version', ''))
        if name != version + '.yaml':
            found.append(f'{name}: version "{version}" does not match the file name.')
        if not any(r.get(s) for s, _ in SECTIONS):
            found.append(f'{name}: has no highlights, changes or fixes.')
        for section, _ in SECTIONS:
            for i, entry in enumerate(r.get(section) or []):
                where = f'{name}: {section}[{i}]'
                if not isinstance(entry, dict) or not str(entry.get('en', '')).strip():
                    found.append(f'{where} needs an English text (en).'); continue
                if section == 'highlights' and entry.get('icon') not in known:
                    found.append(f'{where}: icon "{entry.get("icon")}" is not in Theme/Icons.axaml.')
                if version == current:
                    for language in languages():
                        if not str(entry.get(language, '')).strip():
                            found.append(f'{where} needs a {language} text: the release being built must cover every app language.')
    return found


def render_changelog(releases):
    lines = ['# Changelog', '', 'All notable changes to Jourfold. Generated from `docs/releases` by `python eng/changelog.py`; do not edit by hand.', '']
    for r in releases:
        version = str(r['version'])
        title = f'## {version} “{versions.codename(version)}”'
        lines += [title + (f' – {r["date"]}' if r.get('date') else ''), '']
        lines += section_lines(r, '###')
    return '\n'.join(lines).rstrip() + '\n'


def section_lines(release, level):
    lines = []
    for section, heading in SECTIONS:
        entries = release.get(section) or []
        if not entries:
            continue
        lines += [f'{level} {heading}', ''] + ['- ' + ' '.join(str(e['en']).split()) for e in entries] + ['']
    return lines


def release_body(release, repository, existing):
    own = existing.split(MARKER)[0].rstrip()
    install = [
        '## Install', '',
        'Linux:', '', '```sh', f'curl -fsSL https://github.com/{repository}/releases/latest/download/install.sh | bash', '```', '',
        'Windows (PowerShell):', '', '```powershell', f'irm https://github.com/{repository}/releases/latest/download/install.ps1 | iex', '```', '',
        'The packages and `SHA256SUMS` are attached below.', '',
    ]
    changes = ['## Changes', ''] + section_lines(release, '###')
    return '\n'.join(([own, ''] if own else []) + [MARKER, ''] + install + changes).rstrip() + '\n'


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--check', action='store_true')
    parser.add_argument('--release-body', metavar='TAG')
    parser.add_argument('--repository', default='travelrepo-org/jourfold')
    parser.add_argument('--existing', metavar='FILE', help='file with the release text written so far')
    parser.add_argument('--github', action='store_true', help='format errors as GitHub Actions annotations')
    args = parser.parse_args()
    releases = load(); current = versions.read()
    if args.release_body:
        version = args.release_body.lstrip('v')
        release = next((r for r in releases if str(r.get('version')) == version), None)
        if release is None:
            raise SystemExit(f'No docs/releases/{version}.yaml.')
        existing = Path(args.existing).read_text(encoding='utf-8') if args.existing and Path(args.existing).exists() else ''
        sys.stdout.write(release_body(release, args.repository, existing)); return 0
    errors = problems(releases, current)
    text = render_changelog(releases)
    if args.check:
        if not CHANGELOG.exists() or CHANGELOG.read_text(encoding='utf-8') != text:
            errors.append('CHANGELOG.md is out of date. Run: python eng/changelog.py')
        for error in errors:
            print('::error::' + error if args.github else error, file=sys.stderr)
        if not errors:
            print(f'Release notes are complete for {current} and CHANGELOG.md is up to date.')
        return 1 if errors else 0
    if errors:
        for error in errors:
            print(error, file=sys.stderr)
        return 1
    CHANGELOG.write_text(text, encoding='utf-8'); print(f'Wrote {CHANGELOG.relative_to(ROOT)} with {len(releases)} releases.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
