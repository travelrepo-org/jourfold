# Plugin API 1

`TravelRepo.Plugins.Abstractions` is Apache-2.0 and independent of Avalonia and Jourfold UI types.

Implement `ITravelPlugin`. `Describe()` returns a stable ID, API version, component definitions, declarative editor fields and commands. `InvokeAsync` accepts/returns JSON-compatible data and supports cancellation. Custom types and component keys use reverse-domain namespaces.

A package directory contains:

```text
jourfold.plugin.json
Plugin.dll
other managed dependencies
LICENSES/
```

The manifest requires `id`, `version`, `minimumApiVersion` and `entryAssembly`, and lists `capabilities` and declared `permissions`. Optional `name`, `description`, `authors`, `license` (SPDX), `homepage` (https) and `licenseFile` describe the plugin; the TravelRepo plugin documentation defines them. Jourfold shows them in **Settings → Plugins** and in the About window, where people can read the plugin's license text and open its homepage. Ship the license text in `LICENSES/`. See `jourfold/samples/Jourfold.ExamplePlugin` for an executable example.

The host exchanges JSON-RPC 2.0 on stdin/stdout, one JSON object per line. `describe` queries metadata; other methods invoke commands. Requests have integer IDs, `method` and `params`; responses contain the same ID and `result` or `error`. Plugins must not write logs to stdout. Use stderr for diagnostics without secrets.

Jourfold starts one host process per plugin and loads it into an AssemblyLoadContext. The application renders declared fields using its own controls. The v1 host is a crash/dependency boundary, **not a security sandbox**. Only run trusted installed software. Permission declarations are informational and consent-oriented.

Missing plugins do not affect retention of unknown compatible data.

Packaged builds include the tested example under `Examples/Walking`. Choose that directory from Settings → Plugins after reviewing the trust notice. Jourfold remembers loaded plugin folders and lists them there with **Use** and a button to remove them from the list; only the folder path is stored, and removing it from the list does not delete anything. The host runs separately and is terminated when the operation finishes. The plugin's declared fields are rendered by Jourfold controls, and unknown namespaced component data remains in the repository after the plugin is removed.
