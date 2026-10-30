using System.Net;
using Jourfold.Application;
using TravelRepo.Core;
using TravelRepo.Repository;

namespace Jourfold.Mobile;

/// <summary>A file another app shared with Jourfold or the user picked, read into memory.</summary>
public sealed record SharedFile(string Name, string MediaType, byte[] Bytes);

/// <summary>What another app shared with Jourfold: files, text, or both, such as a place from a maps app.</summary>
public sealed record SharedContent(IReadOnlyList<SharedFile> Files, string? Text)
{
    /// <summary>The position in the text, from coordinates or a map link that carries them.</summary>
    public (double Latitude, double Longitude)? Position =>
        Text?.Split((char[])['\n', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Select(Read).FirstOrDefault(p => p is not null) ?? Read(Text);

    /// <summary>A map short link that only the maps service can turn into a position, such as maps.app.goo.gl.</summary>
    public Uri? ShortLink => Text?.Split((char[])['\n', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
        .Select(w => Uri.TryCreate(w.Trim(), UriKind.Absolute, out var uri) ? uri : null)
        .FirstOrDefault(u => u is { Scheme: "https" } && u.Host is "maps.app.goo.gl" or "goo.gl" or "g.co" or "maps.apple.com" && !Coordinates.TryParseLink(u.AbsoluteUri, out _, out _));

    /// <summary>The lines of the text that are not links: maps apps send the place's name first and its address after.</summary>
    public IReadOnlyList<string> Lines => (Text ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.Contains("://", StringComparison.Ordinal)).ToArray();

    private static (double, double)? Read(string? text) => Coordinates.TryParseLink(text, out var lat, out var lon) ? (lat, lon) : null;
}

/// <summary>
/// Sharing to Jourfold: files such as tickets, PDFs and e-mails become documents of the trip, attached to an item or
/// kept with the trip's files; a place from a maps app becomes a place; other text becomes a note.
/// </summary>
public sealed partial class MobileSession
{
    /// <summary>What another app shared and the user has not placed yet.</summary>
    public SharedContent? Incoming { get; private set; }

    /// <summary>Takes what another app shared. With a trip open, the share sheet opens; otherwise the trip list asks for the trip.</summary>
    public void Receive(SharedContent content)
    {
        if (content.Files.Count == 0 && string.IsNullOrWhiteSpace(content.Text)) return;
        Incoming = content;
        if (Workspace is not null) { Showing = Overlay.Share; OverlayItem = null; }
        Changed?.Invoke();
    }

    /// <summary>Forgets what was shared, for example when the user cancels.</summary>
    public void DropIncoming() { Incoming = null; if (Showing == Overlay.Share) { Showing = Overlay.None; OverlayItem = null; } Changed?.Invoke(); }

    /// <summary>
    /// Adds files to the trip, attached to <paramref name="attachTo"/> or only kept with its files. Files above 25 MB
    /// stay out, as the desktop asks before adding those to the trip's history.
    /// </summary>
    public async Task AddFilesAsync(IReadOnlyList<SharedFile> files, Guid? attachTo, CancellationToken ct = default)
    {
        var workspace = Workspace ?? throw new InvalidOperationException("No trip is open.");
        if (files.Any(f => f.Bytes.Length > 25 * 1024 * 1024)) throw new DomainException("asset.large", Strings["FileTooLarge"]);
        var edits = new List<EntityEdit>(); var resources = new List<ResourceEdit>();
        var owner = attachTo is { } id ? workspace.State.Trip.Find(id) : null;
        foreach (var file in files)
        {
            var (document, resource) = TravelRepository.PrepareDocument(file.Bytes, file.Name, file.MediaType);
            edits.Add(new(document.Id, document)); resources.Add(resource);
            if (owner is not null) owner = TripCommands.AttachDocument(owner, document);
        }
        if (owner is not null) edits.Add(new(owner.Id, owner));
        await workspace.ApplyAsync(edits.ToArray(), ct, resources.ToArray()); NoteChange();
        Changed?.Invoke();
    }

    /// <summary>Saves shared text as a note of the trip.</summary>
    public async Task AddNoteAsync(string title, string body, CancellationToken ct = default)
    {
        var workspace = Workspace ?? throw new InvalidOperationException("No trip is open.");
        var (_, edits) = QuickAdd.Build(new QuickAddDraft("note", title) { Body = body }, workspace.State.Trip);
        await workspace.ApplyAsync(edits, ct); NoteChange();
        Changed?.Invoke();
    }

    /// <summary>Adds shared text to the end of an item's notes.</summary>
    public Task AppendNoteAsync(Guid item, string text, CancellationToken ct = default) => UpdateAsync(item, e =>
    {
        var before = e.Data["body"]?.ToString();
        e.Data["body"] = string.IsNullOrWhiteSpace(before) ? text.Trim() : before.TrimEnd() + "\n\n" + text.Trim();
    }, ct);

    /// <summary>Done with what was shared: the item it went to is shown, or the trip as it was.</summary>
    public void FinishSharing(Guid? item)
    {
        Incoming = null;
        if (item is { } id && Trip?.Find(id) is not null) { Showing = Overlay.Details; OverlayItem = id; SelectedItem = id; }
        else { Showing = Overlay.None; OverlayItem = null; }
        Changed?.Invoke();
    }

    /// <summary>
    /// Asks the maps service where a short link leads, such as maps.app.goo.gl, and reads the position from there.
    /// Only the short link itself is sent, and only when the user asks for it.
    /// </summary>
    public async Task<(double Latitude, double Longitude)?> ResolveShortLinkAsync(Uri link, CancellationToken ct = default)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = Host.Http is null ? new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) } : new HttpClient(Host.Http, disposeHandler: false);
        var next = link;
        for (var hop = 0; hop < 6; hop++)
        {
            if (Coordinates.TryParseLink(next.AbsoluteUri, out var lat, out var lon)) return (lat, lon);
            using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, next), HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode is not (HttpStatusCode.Moved or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                || response.Headers.Location is not { } location) return null;
            next = location.IsAbsoluteUri ? location : new Uri(next, location);
        }
        return null;
    }
}
