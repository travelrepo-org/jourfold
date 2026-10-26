# GitHub App setup for development validation

The desktop client uses GitHub App device authorization. The GitHub CLI's own login is separate and does not validate Jourfold's authentication flow.

## Register the App

Open https://github.com/settings/apps/new while signed into the test owner account. For the current validation account, use these settings:

| Setting | Value |
| --- | --- |
| GitHub App name | `<your name> Jourfold Dev` (choose another unique name if unavailable) |
| Homepage URL | `https://github.com/<your-account>` |
| Callback URL | Leave blank; device flow does not use it |
| Expire user authorization tokens | Disabled |
| Request user authorization during installation | Disabled |
| Enable Device Flow | Enabled |
| Setup URL | Leave blank |
| Webhooks: Active | Disabled |
| Repository permissions: Contents | Read and write |
| Repository permissions: Administration | Read and write |
| Repository permissions: Metadata | Read-only (automatic) |
| Other repository, organization and account permissions | No access |
| Where can this GitHub App be installed? | Only on this account |

Leave token expiry off. Expiring tokens last eight hours and can only be renewed with the App's client secret, which a desktop or phone app cannot keep secret, so people would have to sign in again several times a day.

Administration write is needed for the required repository-creation and collaborator-invitation tests. This development App should be installed only on disposable trip repositories.

Create the App. Copy its public **Client ID**, not its numeric App ID. Record its settings URL/slug. The desktop device flow does not need a client secret or App private key.

## Install for a disposable fixture

Create an empty private repository, for example `jourfold-smoke`, without a README, license or gitignore. In the App settings, choose **Install App**, select your account, select **Only select repositories**, and select that disposable repository. A valid sample TravelRepo can then be pushed there for discovery testing.

The `travelrepo` and `jourfold` source-code repositories used by CI are separate from this trip-data fixture. The development App does not need access to those source repositories.

Build Jourfold with `-p:JourfoldGitHubClientId=<client ID>`, or use a build without a built-in ID and enter the Client ID when Jourfold asks for it. Then start Connect GitHub and complete the displayed device authorization in your browser. Tokens stay in the OS credential store when available. Never place a client secret in the desktop application.

Publishing a new private test repository is a separate test. Confirm that the newly created repository is included in the App's installation access before diagnosing authorization failures.

## Live test scope

Use synthetic itinerary data. Validate device authorization, granted-repository discovery, private publication and initial push, privacy inspection, HTTPS/SSH Git transport, and invitation of a specifically consenting test collaborator. Public-warning tests require a separately authorized public synthetic fixture. Do not change a source repository's visibility to test this warning.

## Official references

- [Registering a GitHub App](https://docs.github.com/en/apps/creating-github-apps/registering-a-github-app/registering-a-github-app)
- [Generating a user access token with device flow](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/generating-a-user-access-token-for-a-github-app)
- [Repository creation permissions](https://docs.github.com/en/rest/repos/repos#create-a-repository-for-the-authenticated-user)
- [Collaborator invitation permissions](https://docs.github.com/en/rest/collaborators/collaborators#add-a-repository-collaborator)
