# Third-party notices

Jourfold is licensed under GPL-3.0-or-later. The TravelRepo libraries, specifications and schemas it builds on are licensed under Apache-2.0; the full text is in `licenses/TravelRepo-Apache-2.0.txt`.

Jourfold includes the following open-source software. License files from each package are in the `licenses` folder, and `licenses/dependencies.json` lists every resolved package with its NuGet license metadata.

## Application framework

- Avalonia 11.3: MIT, https://github.com/AvaloniaUI/Avalonia
- ANGLE for Windows (Avalonia.Angle.Windows.Natives): BSD-3-Clause, https://github.com/google/angle
- SkiaSharp and HarfBuzzSharp: MIT, https://github.com/mono/SkiaSharp
- MicroCom.Runtime: MIT, https://github.com/kekekeks/MicroCom
- Tmds.DBus.Protocol: MIT, https://github.com/tmds/Tmds.DBus
- CommunityToolkit.Mvvm: MIT, https://github.com/CommunityToolkit/dotnet
- Microsoft .NET runtime and extensions: MIT, https://github.com/dotnet/runtime

## Data and documents

- Noda Time: Apache-2.0, https://github.com/nodatime/nodatime
- YamlDotNet: MIT, https://github.com/aaubry/YamlDotNet
- JsonSchema.Net, JsonPointer.Net and Json.More.Net: MIT, https://github.com/json-everything/json-everything
- Humanizer: MIT, https://github.com/Humanizr/Humanizer
- Microsoft.Data.Sqlite and SQLitePCLRaw: MIT and Apache-2.0. The SQLite engine is in the public domain.
- PDFsharp: MIT, https://github.com/empira/PDFsharp
- Markdig: BSD-2-Clause, https://github.com/xoofx/markdig. Used for local CommonMark parsing; no remote content is executed.

## Fonts, icons and maps

- Plus Jakarta Sans and Outfit: SIL Open Font License 1.1. Upstream commits, hashes and official URLs are in `assets/fonts/manifest.json`, and the license files are distributed beside the fonts. Font files are bundled; no font service is contacted. The logo uses converted Outfit outlines and does not need Outfit at runtime.
- Lucide icons (lucide-static 1.51.0): ISC License, https://lucide.dev. The license is in `licenses/lucide/LICENSE`. Icon outlines are converted into `src/Jourfold.Desktop/Theme/Icons.axaml`.
- Map images and address search come from OpenStreetMap when the user turns on online maps. Map data © OpenStreetMap contributors, available under the Open Database License, https://www.openstreetmap.org/copyright. No OpenStreetMap data is bundled.

## Windows packages

- Git for Windows (MinGit 2.56.0.windows.1): GPL-2.0 and the licenses of its bundled components. The Windows packages include the upstream notices in the `git` folder. Corresponding source: https://github.com/git-for-windows/git/tree/v2.56.0.windows.1
