#!/usr/bin/env python3
"""Read the Jourfold version from Directory.Build.props, the only place it is defined.

  python eng/version.py                  prints the Jourfold version, for example 0.2.0
  python eng/version.py --travelrepo     prints the TravelRepo version Jourfold is built against
  python eng/version.py --check-tag v0.2.0
                                         fails unless the tag is v<Version> and a TravelRepo checkout
                                         beside this repository has the expected version
"""
from pathlib import Path
import argparse
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
    parser.add_argument('--check-tag')
    parser.add_argument('--github', action='store_true', help='format errors as GitHub Actions annotations')
    args = parser.parse_args()
    if args.check_tag:
        sys.exit(check_tag(args.check_tag))
    print(read('TravelRepoVersion' if args.travelrepo else 'Version'))
