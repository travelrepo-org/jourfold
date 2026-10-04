using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.Input;
using Jourfold.Application;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Repository;

namespace Jourfold.Desktop;

public partial class MainViewModel
{
    /// <summary>The Quick Add type that fits the current view.</summary>
    public string ContextKind => View switch
    {
        "Bookings" => "booking",
        "Costs" => "expense",
        "Tasks" => "task",
        "People" => "person",
        "Places" or "Map" => "place",
        "Collections" => "collection",
        "Files" => "note",
        _ => "activity"
    };

    [RelayCommand] private Task QuickAdd() => RunAsync(() => AddAsync(ContextKind, chooseType: true));

    /// <summary>Open Quick Add for a type, prefilled from context, and create the result as one undoable edit.</summary>
    public Task AddAsync(string kind) => AddAsync(kind, false);
    public Task AddAsync(string kind, bool chooseType, LocalDate? date = null, LocalTime? start = null, Duration? length = null, Guid? person = null) => RunAsync(async () =>
    {
        if (Workspace is null) return;
        if (kind == "comment") { await AddCommentAsync(Selected, null); return; }
        if (kind == "file") { await AddFilesAsync(null); return; }
        var trip = Workspace.State.Trip;
        var request = new QuickAddRequest(kind, trip, date ?? StartDate, start, length, ScheduleQueries.TripZone(trip).Id, person, chooseType, Store.Get("currency") ?? DefaultCurrency(trip));
        var draft = await Interaction.QuickAddAsync(request); if (draft is null) return;
        var (main, edits) = Application.QuickAdd.Build(draft, Workspace.State.Trip);
        await Workspace.ApplyAsync(edits);
        if (draft.Currency is { } currency) Store.Set("currency", currency);
        Selected = Workspace.State.Trip.Find(main.Id);
        var target = main.Type switch { "person" => "People", "place" => "Places", "booking" => "Bookings", "task" => "Tasks", "expense" or "budget" => "Costs", "collection" => "Collections", "note" => View, _ => View is "Plan" or "List" or "Map" ? View : "Plan" };
        if (main.Type == "schedule_item" && main.Data["time"] is null) InboxOpen = true;
        if (target != View) View = target;
        if (main.Type == "schedule_item" && ScheduleQueries.Span(Workspace.State.Trip, main).Start is { } begin) { var day = begin.InZone(ScheduleQueries.TripZone(Workspace.State.Trip)).Date; if (day < StartDate || day > StartDate.PlusDays(6)) StartDate = day; }
        Refresh();
    });

    private static string DefaultCurrency(TripSnapshot trip) => trip.Entities.Values.Select(e => TripSummaries.Money(e.Data["amount"] ?? e.Data["price"])?.Currency).FirstOrDefault(c => c is not null) ?? (System.Globalization.RegionInfo.CurrentRegion.ISOCurrencySymbol is { Length: 3 } code ? code : "EUR");

    /// <summary>Create an activity directly from a selected timetable range.</summary>
    public Task AddScheduledAsync(ZonedTime start, Duration duration, Guid? participant = null)
    {
        var local = NodaTime.Text.LocalDateTimePattern.ExtendedIso.Parse(start.Local).Value;
        return AddAsync("activity", false, local.Date, local.TimeOfDay, duration, participant);
    }

    /// <summary>Capture an idea in the Inbox without any dialog.</summary>
    public Task CaptureIdeaAsync(string title) => RunEditAsync(async () =>
    {
        if (Workspace is null || string.IsNullOrWhiteSpace(title)) return;
        var item = Entity.Create("schedule_item", title.Trim()); await Workspace.EditAsync(item);
    });

    [RelayCommand]
    private Task AddIdentity() => RunAsync(async () =>
    {
        if (Workspace is null) return;
        var name = Store.Get("identity.name"); var email = Store.Get("identity.email");
        if (name is null || email is null)
        {
            var values = await Interaction.FormAsync(Strings["AddMyself"], [new("name", Strings["YourName"], Value: name ?? Environment.UserName, Required: true), new("email", Strings["EmailOptional"], Value: email ?? "", Hint: Strings["EmailHint"])]);
            if (values is null) return; name = values["name"]; email = values["email"] is { Length: > 0 } e ? e : LocalEmail(name);
            Store.Set("identity.name", name); Store.Set("identity.email", email);
        }
        var person = TripSummaries.PersonForGitEmail(Workspace.State.Trip, email)?.Copy() ?? Entity.Create("person", name);
        person.Data["identities"] ??= new JsonObject(); person.Data["identities"]!["git"] ??= new JsonArray(new JsonObject { ["name"] = name, ["email"] = email });
        var manifest = Workspace.State.Trip.Manifest.Copy(); var participants = manifest.Data["participants"] as JsonArray ?? []; if (manifest.Data["participants"] is null) manifest.Data["participants"] = participants; if (!participants.Any(p => p?.ToString() == person.Id.ToString())) participants.Add(person.Id.ToString());
        await Workspace.ApplyAsync([new(person.Id, person), new(manifest.Id, manifest)]); await Workspace.Git.SetIdentityAsync(name, email); Selected = Workspace.State.Trip.Find(person.Id);
    });

    /// <summary>The trip-local person that represents the local user, if mapped.</summary>
    public Entity? Me => Trip is { } trip && Store.Get("identity.email") is { } email ? TripSummaries.PersonForGitEmail(trip, email) : null;

    [RelayCommand] private Task Undo() => RunEditAsync(async () => { if (Workspace?.CanUndo == true) { await Workspace.UndoAsync(); Interaction.Toast(Strings["Undone"]); } });
    [RelayCommand] private Task Redo() => RunEditAsync(async () => { if (Workspace?.CanRedo == true) await Workspace.RedoAsync(); });

    /// <summary>Delete the selection. Deletion is undoable; a notice offers Undo instead of asking first.</summary>
    [RelayCommand] private Task Delete() => DeleteAsync(Selected?.Id);
    public Task DeleteAsync(Guid? id) => RunEditAsync(async () =>
    {
        if (Workspace is null || id is not { } target || Workspace.State.Trip.Find(target) is not { Type: not "trip" } entity) return;
        var edits = new List<EntityEdit> { new(target, null) };
        // Detach the item from a parent and from the manifest so no dangling references remain.
        foreach (var parent in Workspace.State.Trip.Entities.Values.Where(e => e.Data["children"] is JsonArray a && a.Any(c => c?.ToString() == target.ToString())))
        { var copy = parent.Copy(); var list = (JsonArray)copy.Data["children"]!; foreach (var node in list.Where(c => c?.ToString() == target.ToString()).ToArray()) list.Remove(node); edits.Add(new(copy.Id, copy)); }
        if (entity.Type == "person" && Workspace.State.Trip.Manifest.Data["participants"] is JsonArray people && people.Any(p => p?.ToString() == target.ToString()))
        { var manifest = Workspace.State.Trip.Manifest.Copy(); var list = (JsonArray)manifest.Data["participants"]!; foreach (var node in list.Where(c => c?.ToString() == target.ToString()).ToArray()) list.Remove(node); edits.Add(new(manifest.Id, manifest)); }
        await Workspace.ApplyAsync(edits.ToArray());
        if (Selected?.Id == target) Selected = null;
        Interaction.Toast(string.Format(Strings["DeletedItem"], entity.Title), Strings["Undo"], () => UndoCommand.ExecuteAsync(null));
    });

    public Task DuplicateAsync(Guid id) => RunEditAsync(async () =>
    {
        if (Workspace is null) return;
        var copies = TripCommands.DuplicateSubtree(Workspace.State.Trip, id);
        var root = copies[0]; root.Data[root.Type == "person" ? "display_name" : root.Type is "place" or "document" ? "name" : "title"] = string.Format(Strings["CopyOf"], root.Title);
        await Workspace.ApplyAsync(copies.Select(c => new EntityEdit(c.Id, c)).ToArray()); Selected = Workspace.State.Trip.Find(root.Id);
    });

    /// <summary>Apply a modified copy of an entity as one undoable edit.</summary>
    public Task UpdateAsync(Entity changed) => RunEditAsync(async () => { if (Workspace is not null) await Workspace.EditAsync(changed); });
    public Task UpdateAsync(Guid id, Action<Entity> change) => RunEditAsync(async () =>
    {
        if (Workspace?.State.Trip.Find(id) is not { } existing) return;
        var copy = existing.Copy(); change(copy);
        if (JsonNode.DeepEquals(copy.Data, existing.Data)) return;
        await Workspace.EditAsync(copy);
    });

    public Task ToggleTaskAsync(Guid id) => UpdateAsync(id, task => task.Data["status"] = task.Data["status"]?.ToString() == "completed" ? "open" : "completed");

    [RelayCommand] private Task AddFile() => AddFilesAsync(null);
    /// <summary>Import files as content-addressed documents. Without paths, a file picker opens.</summary>
    public Task AddFilesAsync(IReadOnlyList<string>? paths, Guid? attachTo = null) => RunAsync(async () =>
    {
        if (Workspace is null) return;
        if (paths is null) { var picked = await Interaction.FileAsync(); if (picked is null) return; paths = [picked]; }
        Entity? last = null;
        foreach (var path in paths.Where(File.Exists))
        {
            var large = new FileInfo(path).Length > 25 * 1024 * 1024; if (large && !await Interaction.ConfirmAsync(string.Format(Strings["LargeFileNamed"], Path.GetFileName(path)))) continue;
            var import = await TravelRepository.PrepareDocumentAsync(path, MediaType(path), large);
            var edits = new List<EntityEdit> { new(import.Document.Id, import.Document) };
            if (attachTo is { } owner && Workspace.State.Trip.Find(owner) is { } target)
            {
                var copy = target.Copy(); var content = copy.Data["content"] as JsonArray ?? new JsonArray(); copy.Data["content"] = content;
                content.Add(new JsonObject { ["type"] = import.Document.Data["media_type"]!.ToString().StartsWith("image/", StringComparison.Ordinal) ? "image" : import.Document.Data["media_type"]!.ToString() == "application/pdf" ? "pdf" : "reference", [import.Document.Data["media_type"]!.ToString().StartsWith("image/", StringComparison.Ordinal) || import.Document.Data["media_type"]!.ToString() == "application/pdf" ? "document" : "entity"] = import.Document.Id.ToString() });
                edits.Add(new(copy.Id, copy));
            }
            await Workspace.ApplyAsync(edits.ToArray(), resources: [import.Resource]); last = import.Document;
        }
        if (last is null) return;
        if (attachTo is null) { View = "Files"; Selected = Workspace.State.Trip.Find(last.Id); }
        Interaction.Toast(paths.Count == 1 ? string.Format(Strings["FileAdded"], last.Title) : string.Format(Strings["FilesAdded"], paths.Count));
    });

    public static string MediaType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".pdf" => "application/pdf",
        ".txt" => "text/plain",
        ".md" => "text/markdown",
        ".ics" => "text/calendar",
        ".html" or ".htm" => "text/html",
        ".json" => "application/json",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        _ => "application/octet-stream"
    };

    public Task AddCommentAsync(Entity? target, string? body) => RunEditAsync(async () =>
    {
        if (Workspace is null || target is null) return;
        var people = Workspace.State.Trip.Entities.Values.Where(x => x.Type == "person").ToArray();
        var author = Me?.Id.ToString();
        if (author is null)
        {
            if (people.Length == 0) { await Interaction.ShowAsync(Strings["comment"], Strings["CommentNeedsPerson"]); return; }
            author = people.Length == 1 ? people[0].Id.ToString() : await Interaction.ChooseAsync(Strings["Author"], people.Select(x => new Choice(x.Id.ToString(), x.Title, "user")).ToArray());
            if (author is null) return;
        }
        body ??= await Interaction.PromptAsync(Strings["AddComment"], "", true); if (string.IsNullOrWhiteSpace(body)) return;
        var comment = Entity.Create("comment", "Comment"); comment.Data.Remove("title");
        comment.Data["author"] = author; comment.Data["target"] = new JsonObject { ["id"] = target.Id.ToString(), ["type"] = target.Type }; comment.Data["body"] = body.Trim(); comment.Data["created_at"] = SystemClock.Instance.GetCurrentInstant().ToString();
        await Workspace.EditAsync(comment);
    });

    /// <summary>Schedule an Inbox entry or reschedule an item from the time dialog.</summary>
    public Task ScheduleSelectedAsync() => RunEditAsync(async () =>
    {
        if (Workspace is null || Selected is null) return;
        var id = Selected.Id; var source = Workspace.State.Trip.Find(id)!;
        var time = await Interaction.ScheduleAsync(source.Data["time"] as JsonObject, StartDate, ScheduleQueries.TripZone(Workspace.State.Trip).Id); if (time is null) return;
        if (source.Type == "schedule_item") { var e = source.Copy(); e.Data["time"] = time["precision"]?.ToString() == "unscheduled" ? null : time; if (e.Data["time"] is not null && e.Data["status"]?.ToString() == "idea") e.Data["status"] = "planned"; await Workspace.EditAsync(e); }
        else { var scheduled = await Workspace.PlanMaterialAsync(id, time); Selected = Workspace.State.Trip.Find(scheduled); }
        if (Selected is { } now && ScheduleQueries.Span(Workspace.State.Trip, now).Start is { } begin) StartDate = begin.InZone(ScheduleQueries.TripZone(Workspace.State.Trip)).Date;
        if (View is not ("Plan" or "List")) View = "Plan"; else Refresh();
    });
}
