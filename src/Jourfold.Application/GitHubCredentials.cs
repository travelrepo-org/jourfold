using TravelRepo.Git;
using TravelRepo.Providers;

namespace Jourfold.Application;

/// <summary>Gives Git the GitHub sign-in stored on this device, and only for github.com.</summary>
public sealed class GitHubCredentialBroker(ISecretStore store) : ICredentialBroker
{
    public async Task<GitCredential?> GetAsync(Uri origin, CancellationToken ct = default)
    { if (origin.Host != "github.com") return null; var token = await store.ReadAsync("github.user-token", ct); return token is null ? null : new("x-access-token", token); }
}
