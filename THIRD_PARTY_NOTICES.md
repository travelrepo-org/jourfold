# Third-party notices

Jourfold is GPL-3.0-or-later. TravelRepo libraries are Apache-2.0.

- Avalonia: MIT, https://github.com/AvaloniaUI/Avalonia
- CommunityToolkit.Mvvm: MIT, https://github.com/CommunityToolkit/dotnet
- Microsoft .NET runtime and extensions: MIT, https://github.com/dotnet/runtime
- Microsoft.Data.Sqlite and SQLitePCLRaw: MIT/Apache-2.0; SQLite engine is public domain.
- PDFsharp: MIT, https://github.com/empira/PDFsharp
- Noda Time: Apache-2.0, https://github.com/nodatime/nodatime
- YamlDotNet: MIT, https://github.com/aaubry/YamlDotNet
- JsonSchema.Net: MIT, https://github.com/json-everything/json-everything
- Plus Jakarta Sans and Outfit: SIL Open Font License 1.1. Exact upstream commits, hashes and official URLs are in `assets/fonts/manifest.json`. License files are distributed beside the fonts.
- Git for Windows / MinGit 2.56.0.windows.1: GPL-2.0 and bundled component licenses. The Windows distribution includes upstream notices in its `git` directory. Corresponding source: https://github.com/git-for-windows/git/tree/v2.56.0.windows.1

The generated `licenses/dependencies.json` enumerates resolved packages and their NuGet license metadata. Font binaries are bundled locally; no runtime font CDN is used. The logo uses converted Outfit outlines and does not need Outfit at runtime.

- Markdig: BSD-2-Clause, https://github.com/xoofx/markdig. Used for local CommonMark parsing; no remote content is executed.

TravelRepo specifications and schemas copied into this repository retain their Apache-2.0 license. The full license is included as `licenses/TravelRepo-Apache-2.0.txt` in source and packages.
