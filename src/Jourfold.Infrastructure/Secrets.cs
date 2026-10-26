using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using TravelRepo.Providers;
namespace Jourfold.Infrastructure;

/// <summary>
/// Windows Credential Manager, the macOS keychain or Linux Secret Service. Unavailable stores retain credentials in
/// memory only.
/// </summary>
/// <param name="interactive">
/// Whether the system may ask the user, for example to unlock the macOS keychain or to allow access after an update.
/// Only the desktop app itself passes true; tests and tools must never wait for a dialog nobody sees.
/// </param>
public sealed class OsSecretStore(bool interactive = false) : ISecretStore
{
    private readonly Dictionary<string, string> session = new();
    public bool SessionOnly { get; private set; }
    public async Task<string?> ReadAsync(string key, CancellationToken ct = default)
    {
        if (session.TryGetValue(key, out var value)) return value;
        if (OperatingSystem.IsWindows()) return WindowsRead(key);
        if (OperatingSystem.IsMacOS()) return MacKeychain.Read(key, interactive);
        var result = await SecretTool(["lookup", "application", "Jourfold", "key", key], null, ct); return result.Code == 0 ? result.Text.TrimEnd('\r', '\n') : null;
    }
    public async Task WriteAsync(string key, string value, CancellationToken ct = default)
    {
        session[key] = value;
        if (OperatingSystem.IsWindows()) { SessionOnly = !WindowsWrite(key, value); return; }
        if (OperatingSystem.IsMacOS()) { SessionOnly = !MacKeychain.Write(key, value, interactive); return; }
        SessionOnly = (await SecretTool(["store", "--label=Jourfold", "application", "Jourfold", "key", key], value, ct)).Code != 0;
    }
    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        session.Remove(key); if (OperatingSystem.IsWindows()) { CredDelete("Jourfold/" + key, 1, 0); return; }
        if (OperatingSystem.IsMacOS()) { MacKeychain.Delete(key, interactive); return; }
        await SecretTool(["clear", "application", "Jourfold", "key", key], null, ct);
    }
    private static async Task<(int Code, string Text)> SecretTool(string[] args, string? input, CancellationToken ct)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(15));
            var info = new ProcessStartInfo("secret-tool") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in args) info.ArgumentList.Add(arg); using var p = Process.Start(info)!;
            using var reg = deadline.Token.Register(() => { try { p.Kill(true); } catch (InvalidOperationException) { } });
            var output = p.StandardOutput.ReadToEndAsync(deadline.Token); var error = p.StandardError.ReadToEndAsync(deadline.Token);
            if (input is not null) await p.StandardInput.WriteAsync(input.AsMemory(), deadline.Token); p.StandardInput.Close(); await p.WaitForExitAsync(deadline.Token); await error; return (p.ExitCode, await output);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or OperationCanceledException) { if (ct.IsCancellationRequested) throw; return (-1, ""); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    { public uint Flags; public uint Type; public string TargetName; public string? Comment; public long LastWritten; public uint CredentialBlobSize; public IntPtr CredentialBlob; public uint Persist; public uint AttributeCount; public IntPtr Attributes; public string? TargetAlias; public string? UserName; }
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredWrite(ref Credential c, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredRead(string target, uint type, uint flags, out IntPtr ptr);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr ptr);
    private static bool WindowsWrite(string key, string value)
    {
        var bytes = Encoding.Unicode.GetBytes(value); var ptr = Marshal.AllocHGlobal(bytes.Length);
        try { Marshal.Copy(bytes, 0, ptr, bytes.Length); var c = new Credential { Type = 1, TargetName = "Jourfold/" + key, CredentialBlob = ptr, CredentialBlobSize = (uint)bytes.Length, Persist = 2, UserName = "Jourfold" }; return CredWrite(ref c, 0); }
        finally { for (var i = 0; i < bytes.Length; i++) Marshal.WriteByte(ptr, i, 0); Marshal.FreeHGlobal(ptr); }
    }
    private static string? WindowsRead(string key)
    {
        if (!CredRead("Jourfold/" + key, 1, 0, out var ptr)) return null;
        try { var c = Marshal.PtrToStructure<Credential>(ptr); return Marshal.PtrToStringUni(c.CredentialBlob, (int)c.CredentialBlobSize / 2); } finally { CredFree(ptr); }
    }
}

/// <summary>
/// Generic passwords in the user's login keychain, service "Jourfold", one account per key. Uses the keychain
/// functions of Security.framework directly, so the secret never appears on a command line.
/// </summary>
internal static class MacKeychain
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string Service = "Jourfold";
    private const int ItemNotFound = -25300;
    [DllImport(Security)] private static extern int SecKeychainFindGenericPassword(IntPtr keychain, uint serviceLength, byte[] service, uint accountLength, byte[] account, out uint passwordLength, out IntPtr password, out IntPtr item);
    [DllImport(Security)] private static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceLength, byte[] service, uint accountLength, byte[] account, uint passwordLength, byte[] password, IntPtr item);
    [DllImport(Security)] private static extern int SecKeychainItemModifyAttributesAndData(IntPtr item, IntPtr attributes, uint length, byte[] data);
    [DllImport(Security)] private static extern int SecKeychainItemDelete(IntPtr item);
    [DllImport(Security)] private static extern int SecKeychainItemFreeContent(IntPtr attributes, IntPtr data);
    [DllImport(Security)] private static extern int SecKeychainSetUserInteractionAllowed([MarshalAs(UnmanagedType.U1)] bool allowed);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] private static extern void CFRelease(IntPtr value);

    private static readonly byte[] ServiceName = Encoding.UTF8.GetBytes(Service);

    /// <summary>
    /// Finds the item for a key; the caller releases <paramref name="item"/> and frees <paramref name="data"/>. Without
    /// interaction a locked keychain answers at once instead of showing a dialog.
    /// </summary>
    private static int Find(string key, bool interactive, out uint length, out IntPtr data, out IntPtr item)
    {
        SecKeychainSetUserInteractionAllowed(interactive);
        var account = Encoding.UTF8.GetBytes(key);
        return SecKeychainFindGenericPassword(IntPtr.Zero, (uint)ServiceName.Length, ServiceName, (uint)account.Length, account, out length, out data, out item);
    }

    public static string? Read(string key, bool interactive)
    {
        try
        {
            if (Find(key, interactive, out var length, out var data, out var item) != 0) return null;
            try { return Marshal.PtrToStringUTF8(data, (int)length); }
            finally { SecKeychainItemFreeContent(IntPtr.Zero, data); if (item != IntPtr.Zero) CFRelease(item); }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return null; }
    }

    public static bool Write(string key, string value, bool interactive)
    {
        var secret = Encoding.UTF8.GetBytes(value);
        try
        {
            var found = Find(key, interactive, out _, out var data, out var item);
            if (found == 0)
            {
                // An existing item keeps its access rules; only the secret changes.
                try { SecKeychainItemFreeContent(IntPtr.Zero, data); return SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)secret.Length, secret) == 0; }
                finally { if (item != IntPtr.Zero) CFRelease(item); }
            }
            if (found != ItemNotFound) return false;
            var account = Encoding.UTF8.GetBytes(key);
            return SecKeychainAddGenericPassword(IntPtr.Zero, (uint)ServiceName.Length, ServiceName, (uint)account.Length, account, (uint)secret.Length, secret, IntPtr.Zero) == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
        finally { Array.Clear(secret); }
    }

    public static void Delete(string key, bool interactive)
    {
        try
        {
            if (Find(key, interactive, out _, out var data, out var item) != 0) return;
            SecKeychainItemFreeContent(IntPtr.Zero, data);
            try { SecKeychainItemDelete(item); } finally { CFRelease(item); }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }
}

public sealed class GitHubCredentialBroker(ISecretStore store) : TravelRepo.Git.ICredentialBroker
{
    public async Task<TravelRepo.Git.GitCredential?> GetAsync(Uri origin, CancellationToken ct = default)
    { if (origin.Host != "github.com") return null; var token = await store.ReadAsync("github.user-token", ct); return token is null ? null : new("x-access-token", token); }
}
