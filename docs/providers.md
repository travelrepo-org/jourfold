# Provider architecture

Capabilities live in `TravelRepo.Providers.Abstractions`. A provider implements only supported capabilities: authentication, discovery, creation, privacy, collaboration, search or sharing. Git operations remain in `TravelRepo.Git`; local trip editing never requires a provider.

`GitHubProvider` implements GitHub App device authorization. Register an App with device flow enabled, supply its public client ID, and install it for the desired repositories. Do not embed an App private key or client secret in the desktop client. The default path requests no classic OAuth `repo` scope.

Discovery enumerates user installations and granted repositories, paginates results, and reads `travel.yaml` to determine compatibility. Repository creation sends `private: true` and leaves initialization to the local trip. Privacy is exposed as data for clients to warn appropriately. Collaborator invitations request push access and may fail when account/App permissions are insufficient.

Tokens use the supplied `ISecretStore`. Jourfold uses Windows Credential Manager or Linux Secret Service and otherwise keeps session-only credentials. `ICredentialBroker` supplies HTTPS Git credentials through scoped child-process configuration in environment variables; they are not included in command arguments or repository config. Other hosts retain system helpers and normal SSH behavior.

Inject `HttpClient` to test network boundaries. Live tests require a configured App and consenting disposable test accounts. HTTP seam tests do not prove GitHub has granted the required permissions.

GitHub documentation consulted: [GitHub App user tokens and device flow](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/generating-a-user-access-token-for-a-github-app).

For development validation, follow the [GitHub App registration walkthrough](github-app-setup.md).

## Online maps and address search

Jourfold's map works without any service: places are drawn from the coordinates stored in the trip. A street map and address search are optional and off until the user enables them, either from the prompt on the map or in Settings under Maps and places.

When enabled:

- **Map images** come from the configured tile server, by default `tile.openstreetmap.org`, for the area on screen only. Requests carry an identifying User-Agent with the Jourfold version and repository address, at most two run at once, and tiles that scroll out of view before their turn are not requested. Tiles are cached under the Jourfold data folder for as long as the server's `Cache-Control` allows (max-age minus `Age`, or seven days without headers) and renewed with conditional requests (`If-None-Match`, `If-Modified-Since`). When the server cannot be reached, a cached tile is shown only within its `stale-if-error` window. There is no download for offline use. The cache is capped at 250 MB and drops the oldest tiles first; Settings shows its size and can clear it. The map shows the attribution the provider requires.
- **Address search** sends the text the user submits to the configured Nominatim server, by default `nominatim.openstreetmap.org`, only when the user starts a search (no search as you type), at most one request per second. Repeated searches in a session are answered from memory. Choosing a result creates or updates a place with its name, address, coordinates and OpenStreetMap ID.
- **Server list:** once a day at most, Jourfold reads `config/map-providers.json` from the repository that published the build. Changing that file switches the servers for every installation without a software update, which the Nominatim policy requires. Invalid lists are ignored, and the built-in OpenStreetMap servers remain the fallback.
- **Own servers:** Settings → Maps and places → Map servers takes a tile address with `{z}`, `{x}` and `{y}`, an attribution, and a Nominatim search address, all over HTTPS. They take precedence over the list, for example to use a commercial OpenStreetMap-based provider with an API key.

No trip data, names or identifiers are sent. If Jourfold is used by many people, switch the list to a commercial or self-hosted OpenStreetMap-based service, as the OpenStreetMap tile policy recommends for heavy use.
