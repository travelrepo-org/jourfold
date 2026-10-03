# ADR 001: Explicit v1 implementation profile

The repositories target .NET 10.0.401 and use the boundaries in the architecture document.

Entity data is exposed as a lossless `Entity` envelope with typed identity, time, money, diagnostics, snapshots and commands. The JSON-compatible envelope preserves unknown core and extension fields. JSON Schema 2020-12 artifacts constrain recognized types. This avoids a second DTO serialization path that could lose future data. A richer typed facade can be added without changing the file format.

The current merge engine handles identity, structural fields, ID sets and immutable asset resources. Children are treated as ordered lists. Markdown merges automatically when independent changes affect corresponding lines; edits that change line alignment remain typed conflicts for user resolution. No conflict markers are written to canonical files.

The local map renders coordinates and transport connections without a network service. It has no basemap tiles. Coordinates and selected entities remain useful offline.

Linux packaging uses a self-contained tar archive and a Debian package. The Debian package declares Git as a dependency. Windows packaging uses a self-contained portable directory, pinned MinGit and an Inno Setup installer definition. Native validation on both target operating systems remains a release gate.

These choices do not waive acceptance requirements. See the acceptance matrix for features and verification that remain incomplete.
