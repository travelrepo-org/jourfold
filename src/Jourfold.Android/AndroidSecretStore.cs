using System.Text;
using Android.Content;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using TravelRepo.Providers;

namespace Jourfold.Android;

/// <summary>
/// Keeps secrets such as the GitHub sign-in encrypted with a key that never leaves the Android Keystore. The
/// encrypted values sit in the app's private preferences. If the key is gone, for example after a backup was restored
/// on another phone, a value reads as missing and the user signs in again.
/// </summary>
public sealed class AndroidSecretStore(Context context) : ISecretStore
{
    private const string Alias = "jourfold.secrets";
    private const int IvLength = 12;

    private ISharedPreferences Preferences => context.GetSharedPreferences("secrets", FileCreationMode.Private)!;

    private static IKey Key()
    {
        var store = KeyStore.GetInstance("AndroidKeyStore")!; store.Load(null);
        if (store.GetKey(Alias, null) is IKey existing) return existing;
        var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore")!;
        generator.Init(new KeyGenParameterSpec.Builder(Alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm).SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone).SetKeySize(256).Build());
        return generator.GenerateKey()!;
    }

    public Task<string?> ReadAsync(string key, CancellationToken ct = default)
    {
        if (Preferences.GetString(key, null) is not { } stored) return Task.FromResult<string?>(null);
        try
        {
            var bytes = Convert.FromBase64String(stored);
            var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            cipher.Init(CipherMode.DecryptMode, Key(), new GCMParameterSpec(128, bytes[..IvLength]));
            return Task.FromResult<string?>(Encoding.UTF8.GetString(cipher.DoFinal(bytes[IvLength..])!));
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or FormatException) { return Task.FromResult<string?>(null); }
    }

    public Task WriteAsync(string key, string value, CancellationToken ct = default)
    {
        var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        cipher.Init(CipherMode.EncryptMode, Key());
        var iv = cipher.GetIV()!; var data = cipher.DoFinal(Encoding.UTF8.GetBytes(value))!;
        Preferences.Edit()!.PutString(key, Convert.ToBase64String([.. iv, .. data]))!.Commit();
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        Preferences.Edit()!.Remove(key)!.Commit();
        return Task.CompletedTask;
    }
}
