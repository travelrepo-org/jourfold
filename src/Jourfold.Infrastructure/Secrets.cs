using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using TravelRepo.Providers;
namespace Jourfold.Infrastructure;

/// <summary>Windows Credential Manager or Linux Secret Service. Unavailable stores retain credentials in memory only.</summary>
public sealed class OsSecretStore : ISecretStore
{
    private readonly Dictionary<string, string> session = new();
    public bool SessionOnly { get; private set; }
    public async Task<string?> ReadAsync(string key, CancellationToken ct = default)
    {
        if (session.TryGetValue(key, out var value)) return value;
        if (OperatingSystem.IsWindows()) return WindowsRead(key);
        var result = await SecretTool(["lookup", "application", "Jourfold", "key", key], null, ct); return result.Code == 0 ? result.Text.TrimEnd('\r', '\n') : null;
    }
    public async Task WriteAsync(string key, string value, CancellationToken ct = default)
    {
        session[key] = value;
        if (OperatingSystem.IsWindows()) { SessionOnly = !WindowsWrite(key, value); return; }
        SessionOnly = (await SecretTool(["store", "--label=Jourfold", "application", "Jourfold", "key", key], value, ct)).Code != 0;
    }
    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        session.Remove(key); if (OperatingSystem.IsWindows()) { CredDelete("Jourfold/" + key, 1, 0); return; }
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

public sealed class GitHubCredentialBroker(ISecretStore store) : TravelRepo.Git.ICredentialBroker
{
    public async Task<TravelRepo.Git.GitCredential?> GetAsync(Uri origin, CancellationToken ct = default)
    { if (origin.Host != "github.com") return null; var token = await store.ReadAsync("github.user-token", ct); return token is null ? null : new("x-access-token", token); }
}
