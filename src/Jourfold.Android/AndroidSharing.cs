using Android.Content;
using Android.Database;
using Android.Provider;
using Jourfold.Mobile;

namespace Jourfold.Android;

/// <summary>
/// Reads what another app shares with Jourfold through the system share sheet: files (tickets, PDFs, e-mails, images)
/// and text such as a place from a maps app. The files are read into memory right away, because the permission to read
/// them ends with the intent.
/// </summary>
public static class AndroidSharing
{
    /// <summary>The shared content of an intent, or null when it shares nothing.</summary>
    public static SharedContent? Read(Context context, Intent? intent)
    {
        if (intent?.Action is not (Intent.ActionSend or Intent.ActionSendMultiple)) return null;
        var uris = new List<global::Android.Net.Uri>();
        if (intent.Action == Intent.ActionSendMultiple)
            uris.AddRange((OperatingSystem.IsAndroidVersionAtLeast(33) ? intent.GetParcelableArrayListExtra(Intent.ExtraStream, Java.Lang.Class.FromType(typeof(global::Android.Net.Uri))) : intent.GetParcelableArrayListExtra(Intent.ExtraStream))?.OfType<global::Android.Net.Uri>() ?? []);
        else if ((OperatingSystem.IsAndroidVersionAtLeast(33) ? intent.GetParcelableExtra(Intent.ExtraStream, Java.Lang.Class.FromType(typeof(global::Android.Net.Uri))) : intent.GetParcelableExtra(Intent.ExtraStream)) is global::Android.Net.Uri one) uris.Add(one);
        var files = new List<SharedFile>();
        foreach (var uri in uris)
        {
            try
            {
                using var stream = context.ContentResolver!.OpenInputStream(uri); if (stream is null) continue;
                using var copy = new MemoryStream(); stream.CopyTo(copy);
                var name = Name(context, uri) ?? "file";
                var type = context.ContentResolver.GetType(uri) is { Length: > 0 } given && given != "application/octet-stream" ? given : TravelRepo.Documents.MediaTypes.FromFileName(name);
                files.Add(new(name, type, copy.ToArray()));
            }
            catch (Exception ex) when (ex is Java.Lang.Exception or IOException) { }
        }
        var subject = intent.GetStringExtra(Intent.ExtraSubject);
        var text = intent.GetStringExtra(Intent.ExtraText);
        // Some maps apps put the place's name in the subject and only the link in the text.
        if (!string.IsNullOrWhiteSpace(subject) && !string.IsNullOrWhiteSpace(text) && !text.Contains(subject, StringComparison.Ordinal)) text = subject + "\n" + text;
        return files.Count == 0 && string.IsNullOrWhiteSpace(text) ? null : new SharedContent(files, files.Count > 0 ? null : text);
    }

    private static string? Name(Context context, global::Android.Net.Uri uri)
    {
        using ICursor? cursor = context.ContentResolver!.Query(uri, [IOpenableColumns.DisplayName], null, null, null);
        return cursor is not null && cursor.MoveToFirst() ? cursor.GetString(0) : uri.LastPathSegment;
    }
}
