using Foundation;
using Security;
using TravelRepo.Providers;

namespace Jourfold.iOS;

/// <summary>
/// Keeps secrets such as the GitHub sign-in in the Keychain, for this device only: they are not part of backups or
/// iCloud Keychain, so a restored or new device signs in again.
/// </summary>
public sealed class IosSecretStore : ISecretStore
{
    private const string Service = "dev.jourfold.app";

    private static SecRecord Query(string key) => new(SecKind.GenericPassword) { Service = Service, Account = key };

    public Task<string?> ReadAsync(string key, CancellationToken ct = default)
    {
        using var data = SecKeyChain.QueryAsData(Query(key), false, out var status);
        return Task.FromResult(status == SecStatusCode.Success && data is not null ? NSString.FromData(data, NSStringEncoding.UTF8)?.ToString() : null);
    }

    public Task WriteAsync(string key, string value, CancellationToken ct = default)
    {
        // Changed in place, so a failure, such as before the first unlock, keeps the old value instead of signing out.
        var data = NSData.FromString(value, NSStringEncoding.UTF8);
        var status = SecKeyChain.Update(Query(key), new SecRecord(SecKind.GenericPassword) { ValueData = data });
        if (status == SecStatusCode.ItemNotFound)
            status = SecKeyChain.Add(new SecRecord(SecKind.GenericPassword)
            {
                Service = Service, Account = key, ValueData = data, Accessible = SecAccessible.AfterFirstUnlockThisDeviceOnly,
            });
        if (status != SecStatusCode.Success) throw new IOException("The Keychain did not keep the value: " + status);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        SecKeyChain.Remove(Query(key));
        return Task.CompletedTask;
    }
}
