# Sets SETTINGS, the build settings every script passes to xcodebuild: the version from Directory.Build.props, the build
# number as the other apps count it (0.8.0 becomes 800000, plus the build), the GitHub App, and the debug tools.
# Environment: JourfoldBuild, JourfoldGitHubClientId, JourfoldGitHubAppSlug, JourfoldDistribution, JourfoldDebugTools=true.
root=${0:A:h}/../..; version=$(python3 $root/eng/version.py); plain=${version%%-*}; parts=(${(s:.:)plain})
SETTINGS=(JF_VERSION=$plain JF_BUILD=$(( (parts[1] * 10000 + parts[2] * 100 + parts[3]) * 1000 + ${JourfoldBuild:-0} ))
  JF_GITHUB_CLIENT_ID=$JourfoldGitHubClientId JF_GITHUB_APP_SLUG=$JourfoldGitHubAppSlug JF_DISTRIBUTION=$JourfoldDistribution)
[[ $JourfoldDebugTools == true ]] && SETTINGS+=(JF_DEBUG_TOOLS=JOURFOLD_DEBUG_TOOLS)
true
