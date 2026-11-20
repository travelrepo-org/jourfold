#!/usr/bin/env python3
"""Generate the iOS app icon and the launch screen's mark from the canonical mark.

Usage: python3 eng/ios-icons.py assets/branding/mark.svg src/Jourfold.iOS/Assets.xcassets

Needs cairosvg and Pillow. The app icon is a 1024 px image, which Xcode scales for every size; iOS rounds its corners
itself. The light icon may not be transparent, so the mark sits on white, at the same share of the icon as on Android's
adaptive icon. Since iOS 18 the home screen can also show icons dark or tinted, and since iOS 26 clear, which iOS
derives from these two:

- dark: the mark in the colours of the dark lockup (navy becomes #EDF7FA) on a transparent ground, which iOS fills with
  its dark background;
- tinted: the mark in grey on a transparent ground; iOS takes the brightness as the strength of the tint.

The white wave and pin are holes in the mark in both, as they show the ground in the light icon, so they stay visible
where the navy part turns light. Icon Composer's .icon files would be the alternative: actool of Xcode 26.6 compiles
them, but .NET for iOS 26.5 passes only asset catalogs to it; .NET for iOS 27 does, and it needs Xcode 27.

The launch screen shows the mark at 100 points on the app background, light or deep navy, as Android's starting screen
does.
"""
import io
import json
import re
import sys
from pathlib import Path

import cairosvg
from PIL import Image, ImageChops

source, catalog = Path(sys.argv[1]), Path(sys.argv[2])
svg = source.read_bytes()


def mark(pixels):
    return Image.open(io.BytesIO(cairosvg.svg2png(bytestring=svg, output_width=pixels, output_height=pixels))).convert('RGBA')


def write(folder, contents):
    folder.mkdir(parents=True, exist_ok=True)
    (folder / 'Contents.json').write_text(json.dumps(contents, indent=2) + '\n', encoding='utf-8')


info = {'author': 'eng/ios-icons.py', 'version': 1}
write(catalog, {'info': info})

def render(document, pixels):
    return Image.open(io.BytesIO(cairosvg.svg2png(bytestring=document, output_width=pixels, output_height=pixels))).convert('RGBA')


def recolour(colours):
    """The mark with its fills replaced and its white details left out, and the details alone."""
    text = svg.decode('utf-8')
    details = re.findall(r'<path fill="#fff"[^>]*/>', text)
    body = text
    for detail in details:
        body = body.replace(detail, '')
    for old, new in colours.items():
        body = body.replace(f'fill="{old}"', f'fill="{new}"')
    return body.encode('utf-8'), re.sub(r'(<svg[^>]*>).*(</svg>)', lambda m: m[1] + ''.join(details) + m[2], text, flags=re.S).encode('utf-8')


def cut_out(colours, pixels):
    body, details = recolour(colours)
    image = render(body, pixels)
    holes = render(details, pixels).getchannel('A')
    image.putalpha(ImageChops.subtract(image.getchannel('A'), holes))
    return image


inner = round(1024 * 58 / 108)
offset = ((1024 - inner) // 2,) * 2
icon = Image.new('RGB', (1024, 1024), 'white')
icon.paste(mark(inner), offset, mark(inner))

# The colours of assets/branding/lockup-dark.svg.
dark = Image.new('RGBA', (1024, 1024), (0, 0, 0, 0))
dark.alpha_composite(cut_out({'#103b5c': '#edf7fa'}, inner), offset)

# Three greys, so the three parts stay apart when iOS tints them.
tinted = Image.new('RGBA', (1024, 1024), (0, 0, 0, 0))
tinted.alpha_composite(cut_out({'#103b5c': '#ffffff', '#45c4af': '#c4c4c4', '#1593a5': '#8c8c8c'}, inner), offset)

icons = [('AppIcon.png', icon, None), ('AppIcon-Dark.png', dark, 'dark'), ('AppIcon-Tinted.png', tinted, 'tinted')]
images = []
for name, image, appearance in icons:
    entry = {'filename': name, 'idiom': 'universal', 'platform': 'ios', 'size': '1024x1024'}
    if appearance:
        entry = {'appearances': [{'appearance': 'luminosity', 'value': appearance}], **entry}
    images.append(entry)
write(catalog / 'AppIcon.appiconset', {'images': images, 'info': info})
for name, image, _ in icons:
    image.save(catalog / 'AppIcon.appiconset' / name, optimize=True)

images = []
for scale in (1, 2, 3):
    name = f'LaunchMark@{scale}x.png' if scale > 1 else 'LaunchMark.png'
    images.append({'filename': name, 'idiom': 'universal', 'scale': f'{scale}x'})
write(catalog / 'LaunchMark.imageset', {'images': images, 'info': info})
for scale in (1, 2, 3):
    mark(100 * scale).save(catalog / 'LaunchMark.imageset' / (f'LaunchMark@{scale}x.png' if scale > 1 else 'LaunchMark.png'), optimize=True)


def color(hex_):
    r, g, b = (int(hex_[i:i + 2], 16) for i in (1, 3, 5))
    return {'color-space': 'srgb', 'components': {'red': f'0x{r:02X}', 'green': f'0x{g:02X}', 'blue': f'0x{b:02X}', 'alpha': '1.000'}}


# The app background of the light and dark themes, as on Android (Resources/values*/colors.xml).
write(catalog / 'LaunchBackground.colorset', {'colors': [
    {'idiom': 'universal', 'color': color('#F3F6F6')},
    {'idiom': 'universal', 'appearances': [{'appearance': 'luminosity', 'value': 'dark'}], 'color': color('#09182A')}],
    'info': info})
print('wrote the app icon, the launch mark and the launch background to', catalog)
