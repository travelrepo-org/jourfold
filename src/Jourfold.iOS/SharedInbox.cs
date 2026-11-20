namespace Jourfold.iOS;

/// <summary>
/// The folder in which the share extension leaves what other apps share with Jourfold, until the app takes it. It lies
/// in the app group that the app and its extension share, which iOS keeps apart from every other app. Each share is a
/// folder of its own: the files as they came, and the text, such as a place from a maps app, in <see cref="TextFile"/>.
/// Used by both the app and the extension (linked into Jourfold.iOS.Share), so it needs nothing but .NET.
/// </summary>
internal static class SharedInbox
{
    /// <summary>The ending of the extension's bundle ID after the app's, such as dev.jourfold.app.Share.</summary>
    public const string ExtensionSuffix = ".Share";
    /// <summary>The text of a share, beside its files.</summary>
    public const string TextFile = "shared-text.txt";
    /// <summary>The files of a share.</summary>
    public const string FilesFolder = "files";
    /// <summary>A share is complete once this file is there, so the app never takes one half written.</summary>
    public const string DoneFile = "done";

    /// <summary>The app group for a bundle ID, the app's or its extension's.</summary>
    public static string Group(string bundleId) =>
        "group." + (bundleId.EndsWith(ExtensionSuffix, StringComparison.Ordinal) ? bundleId[..^ExtensionSuffix.Length] : bundleId);

    /// <summary>The inbox in the app group's folder.</summary>
    public static string Folder(string groupFolder) => Path.Combine(groupFolder, "Inbox");

    /// <summary>The name a file keeps in a share: its own, without folders, and never one of the share's own files.</summary>
    public static string SafeName(string? name, int index)
    {
        var plain = Path.GetFileName(name ?? "");
        if (string.IsNullOrWhiteSpace(plain) || plain is TextFile or DoneFile) plain = "file-" + (index + 1);
        return string.Concat(plain.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    }
}
