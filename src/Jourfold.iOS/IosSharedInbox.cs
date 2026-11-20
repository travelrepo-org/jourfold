using Foundation;
using Jourfold.Mobile;

namespace Jourfold.iOS;

/// <summary>
/// Takes what the share extension kept (<see cref="SharedInbox"/>) and hands it to the app, as Android's share intent
/// does: files go to an item, a booking or the trip's files, a place from a maps app becomes a place, other text a
/// note. Shares are taken oldest first and removed once read; a share the extension is still writing waits.
/// </summary>
internal static class IosSharedInbox
{
    /// <summary>What was shared since Jourfold last looked, as one share; null when nothing was.</summary>
    public static SharedContent? Take()
    {
        if (NSFileManager.DefaultManager.GetContainerUrl(SharedInbox.Group(NSBundle.MainBundle.BundleIdentifier ?? ""))?.Path is not { } group) return null;
        var inbox = SharedInbox.Folder(group);
        if (!Directory.Exists(inbox)) return null;
        var files = new List<SharedFile>(); var texts = new List<string>();
        foreach (var share in Directory.GetDirectories(inbox).Order(StringComparer.Ordinal))
        {
            if (!File.Exists(Path.Combine(share, SharedInbox.DoneFile)))
            {
                // Left half written, such as when the extension was ended; after a day it is not coming.
                if (Directory.GetCreationTimeUtc(share) < DateTime.UtcNow.AddDays(-1)) Delete(share);
                continue;
            }
            try
            {
                var folder = Path.Combine(share, SharedInbox.FilesFolder);
                if (Directory.Exists(folder))
                    foreach (var path in Directory.GetFiles(folder).Order(StringComparer.Ordinal))
                    {
                        var name = Path.GetFileName(path);
                        files.Add(new SharedFile(name, TravelRepo.Documents.MediaTypes.FromFileName(name), File.ReadAllBytes(path)));
                    }
                var text = Path.Combine(share, SharedInbox.TextFile);
                if (File.Exists(text)) texts.Add(File.ReadAllText(text));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            Delete(share);
        }
        // As on Android: text only goes with a share without files, such as a place from a maps app.
        return files.Count == 0 && texts.Count == 0 ? null : new SharedContent(files, files.Count > 0 ? null : string.Join('\n', texts));
    }

    private static void Delete(string share)
    {
        try { Directory.Delete(share, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
