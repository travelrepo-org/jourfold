#!/usr/bin/env python3
"""Read the Jourfold version from Directory.Build.props, the only place it is defined.

  python eng/version.py                  prints the Jourfold version, for example 0.2.0
  python eng/version.py --travelrepo     prints the TravelRepo version Jourfold is built against
  python eng/version.py --codename       prints the release code name, for example Curious Caribou
  python eng/version.py --check-tag v0.2.0
                                         fails unless the tag is v<Version> and a TravelRepo checkout
                                         beside this repository has the expected version
"""
from pathlib import Path
import argparse
import hashlib
import json
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
VERSION = re.compile(r'^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')


def read(name='Version', props=ROOT / 'Directory.Build.props'):
    for group in ET.parse(props).getroot().iter('PropertyGroup'):
        node = group.find(name)
        if node is not None and node.text and node.text.strip():
            value = node.text.strip()
            if not VERSION.match(value):
                raise SystemExit(f'{props}: {name} "{value}" is not a version like 1.2.3 or 1.2.3-beta.1')
            return value
    raise SystemExit(f'{props} does not define {name}')


def codename(version):
    """Same rule as CodeNames.For in the app: an alliterative adjective and animal derived from major.minor."""
    words = json.loads((ROOT / 'src' / 'Jourfold.Desktop' / 'CodeNames.json').read_text(encoding='utf-8'))
    key = '.'.join(version.split('+')[0].split('-')[0].split('.')[:2])
    digest = hashlib.sha256(('jourfold:' + key).encode('utf-8')).digest()
    pick = lambda offset, count: int.from_bytes(digest[offset:offset + 4], 'big') % count
    letter = words[sorted(words)[pick(0, len(words))]]
    return letter['adjectives'][pick(4, len(letter['adjectives']))] + ' ' + letter['animals'][pick(8, len(letter['animals']))]


def check_tag(tag):
    version = read()
    errors = []
    if tag != 'v' + version:
        errors.append(f'Tag {tag} does not match the version in Directory.Build.props ({version}). '
                      f'Either tag v{version} or change <Version> and commit before tagging.')
    travelrepo = ROOT.parent / 'travelrepo' / 'Directory.Build.props'
    if travelrepo.exists():
        expected, actual = read('TravelRepoVersion'), read('Version', travelrepo)
        if expected != actual:
            errors.append(f'Jourfold expects TravelRepo {expected} (TravelRepoVersion), but the TravelRepo checkout is {actual}.')
    for error in errors:
        print('::error::' + error if '--github' in sys.argv else error, file=sys.stderr)
    return 1 if errors else 0


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--travelrepo', action='store_true')
    parser.add_argument('--codename', nargs='?', const='', metavar='VERSION', help='code name of VERSION (default: this version)')
    parser.add_argument('--check-tag')
    parser.add_argument('--github', action='store_true', help='format errors as GitHub Actions annotations')
    args = parser.parse_args()
    if args.check_tag:
        sys.exit(check_tag(args.check_tag))
    if args.codename is not None:
        print(codename(args.codename.lstrip('v') or read()))
        sys.exit(0)
    print(read('TravelRepoVersion' if args.travelrepo else 'Version'))
