#!/usr/bin/env python3
"""Generate the iOS app icon and the launch screen's mark from the canonical mark.

Usage: python3 eng/ios-icons.py assets/branding/mark.svg src/Jourfold.iOS/Assets.xcassets

Needs cairosvg and Pillow. The app icon is a single 1024 px image, which Xcode scales for every size; iOS rounds its
corners itself and does not allow transparency, so the mark sits on white, at the same share of the icon as on
Android's adaptive icon. The launch screen shows the mark at 100 points on the app background, light or deep navy, as
Android's starting screen does.
"""
import io
import json
import sys
from pathlib import Path

import cairosvg
from PIL import Image

source, catalog = Path(sys.argv[1]), Path(sys.argv[2])
svg = source.read_bytes()


def mark(pixels):
    return Image.open(io.BytesIO(cairosvg.svg2png(bytestring=svg, output_width=pixels, output_height=pixels))).convert('RGBA')


def write(folder, contents):
    folder.mkdir(parents=True, exist_ok=True)
    (folder / 'Contents.json').write_text(json.dumps(contents, indent=2) + '\n', encoding='utf-8')


info = {'author': 'eng/ios-icons.py', 'version': 1}
write(catalog, {'info': info})

icon = Image.new('RGB', (1024, 1024), 'white')
inner = round(1024 * 58 / 108)
icon.paste(mark(inner), ((1024 - inner) // 2,) * 2, mark(inner))
write(catalog / 'AppIcon.appiconset', {'images': [{'filename': 'AppIcon.png', 'idiom': 'universal', 'platform': 'ios', 'size': '1024x1024'}], 'info': info})
icon.save(catalog / 'AppIcon.appiconset' / 'AppIcon.png', optimize=True)

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
