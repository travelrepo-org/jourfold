using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.Input;
using Jourfold.Application;
using Jourfold.Infrastructure;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Git;
using TravelRepo.Repository;

namespace Jourfold.Desktop;

public partial class MainViewModel
{
    /// <summary>Default parent folder for new trips. Users can choose another folder in the wizard.</summary>
    public string DefaultTripsFolder => Store.Get("trips.folder") is { Length: > 0 } folder ? folder
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments, Environment.SpecialFolderOption.DoNotVerify) is { Length: > 0 } docs ? docs : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Jourfold");

    public static string FolderName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var name = new string(title.Trim().Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim('.', ' ');
        return name.Length == 0 ? "Trip" : name;
    }
    /// <summary>A not-yet-existing folder under <paramref name="parent"/> for the given title.</summary>
    public static string UniqueFolder(string parent, string title)
    {
        var path = Path.Combine(parent, FolderName(title));
        for (var i = 2; Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any(); i++) path = Path.Combine(parent, FolderName(title) + " " + i);
        return path;
    }

    [RelayCommand]
    private Task NewTrip() => RunAsync(async () =>
    {
        var defaults = new NewTripDefaults(DefaultTripsFolder, Strings.Language, DateTimeZoneProviders.Tzdb.GetSystemDefault().Id, Store.Get("identity.name"));
        var draft = await Interaction.NewTripAsync(defaults); if (draft is null) return;
        var path = draft.Destination ?? UniqueFolder(DefaultTripsFolder, draft.Title);
        if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any()) throw new DomainException("folder.not_empty", Strings["EmptyFolderRequired"]);
        var repo = new TravelRepository(path); var manifest = Entity.CreateTrip(draft.Title.Trim(), draft.Language, draft.Timezone);
        if (draft.Start is not null || draft.End is not null) manifest.Data["dates"] = new JsonObject { ["start"] = draft.Start, ["end"] = draft.End };
        manifest.Data["variant"]!["title"] = Strings["CurrentPlan"];
        var semantic = SemanticValidation.Validate(new(manifest, new Dictionary<Guid, Entity>())); if (semantic.Any(d => d.Severity == Severity.Error)) throw new DomainException("trip.invalid", string.Join("; ", semantic.Select(d => d.Message)));
        await repo.InitializeAsync(manifest);
        if (draft.MyName is { Length: > 0 } me) { Store.Set("identity.name", me); if (Store.Get("identity.email") is null) Store.Set("identity.email", LocalEmail(me)); }
        var people = new List<Entity>();
        if (draft.MyName is { Length: > 0 } myName)
        {
            var person = Entity.Create("person", myName); person.Data["roles"] = new JsonArray("traveler");
            person.Data["identities"] = new JsonObject { ["git"] = new JsonArray(new JsonObject { ["name"] = myName, ["email"] = Store.Get("identity.email") }) }; people.Add(person);
        }
        people.AddRange(draft.Participants.Where(n => !string.IsNullOrWhiteSpace(n)).Select(name => Entity.Create("person", name.Trim()).Also(p => p.Data["roles"] = new JsonArray("traveler"))));
        if (people.Count > 0)
        {
            var state = await repo.ReadAsync(); var updated = manifest.Copy(); updated.Data["participants"] = new JsonArray(people.Select(p => (JsonNode?)JsonValue.Create(p.Id.ToString())).ToArray());
            await repo.ApplyAsync(state, people.Select(p => new EntityEdit(p.Id, p)).Append(new(updated.Id, updated)).ToArray());
        }
        await new GitRepository(repo, backend).InitializeAsync(Store.Get("identity.name") ?? Environment.UserName, Store.Get("identity.email") ?? "traveler@localhost");
        if (draft.RemoteUrl is not null) { await new GitRepository(repo, backend).AddRemoteAsync("origin", draft.RemoteUrl); Store.Set("remote:" + path, "origin"); }
        if (OpenRequested is not null) OpenRequested(path); else await OpenAsync(path);
    });

    private static string LocalEmail(string name) => new string(name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray()) is { Length: > 0 } local ? local + "@localhost" : "traveler@localhost";

    [RelayCommand]
    private Task OpenSample() => RunAsync(async () =>
    {
        var path = UniqueFolder(DefaultTripsFolder, "Spring in Japan (sample)");
        var today = SystemClock.Instance.GetCurrentInstant().InZone(DateTimeZoneProviders.Tzdb.GetSystemDefault()).Date;
        await SampleTrip.CreateAsync(path, backend, SampleTrip.SuggestedStart(today), Store.Get("identity.name") ?? "Jourfold", Store.Get("identity.email") ?? "sample@localhost");
        if (OpenRequested is not null) OpenRequested(path); else await OpenAsync(path);
        Interaction.Toast(Strings["SampleCreated"]);
    });

    [RelayCommand] private Task OpenTrip() => RunAsync(async () => { var path = await Interaction.FolderAsync(Strings["OpenTripFolder"]); if (path is not null) { if (OpenRequested is not null) OpenRequested(path); else await OpenAsync(path); } });
    [RelayCommand]
    private Task Clone() => RunAsync(async () =>
    {
        var url = await Interaction.PromptAsync(Strings["RemoteUrl"]); if (string.IsNullOrWhiteSpace(url)) return;
        var folder = UniqueFolder(DefaultTripsFolder, Path.GetFileNameWithoutExtension(url.TrimEnd('/')));
        await GitRepository.CloneAsync(backend, url.Trim(), folder); if (OpenRequested is not null) OpenRequested(folder); else await OpenAsync(folder);
    });
    public void OpenRecent(RecentTrip recent) { if (OpenRequested is not null) OpenRequested(recent.Path); else _ = RunAsync(() => OpenAsync(recent.Path)); }
    public void ForgetRecent(RecentTrip recent) { Store.Forget(recent.Path); RefreshRecent(); ContentChanged?.Invoke(this, EventArgs.Empty); }

    [RelayCommand]
    private Task SaveAs() => RunAsync(async () =>
    {
        if (Workspace is null) return;
        var destination = await Interaction.FolderAsync(Strings["SaveACopy"]); if (destination is null) return;
        await Workspace.Git.CopyWorkingTreeAsync(destination);
        if (OpenRequested is not null) OpenRequested(destination); else await OpenAsync(destination);
    });

    [RelayCommand]
    private Task Export() => RunAsync(async () =>
    {
        if (Workspace is null) return;
        var format = await Interaction.ChooseAsync(Strings["ExportFormat"], [new("pdf", Strings["PDF"], "file-text", Strings["PDFHint"]), new("ics", Strings["ICS"], "calendar", Strings["ICSHint"]), new("html", Strings["HTML"], "globe", Strings["HTMLHint"])]); if (format is null) return;
        var path = await Interaction.FileAsync(true, format, FolderName(Workspace.State.Trip.Manifest.Title)); if (path is null) return;
        if (format == "pdf") Infrastructure.PdfExport.Write(Workspace.State.Trip, path, Path.Combine(AppContext.BaseDirectory, "fonts", "PlusJakartaSans-Regular.ttf"), ExportLabels(), System.Globalization.CultureInfo.CurrentCulture);
        else await File.WriteAllTextAsync(path, format == "ics" ? TripExport.Ics(Workspace.State.Trip) : TripExport.Html(Workspace.State.Trip, ExportLabels(), System.Globalization.CultureInfo.CurrentCulture));
        Interaction.Toast(string.Format(Strings["Exported"], Path.GetFileName(path)), Strings["ShowFile"], () => { Interaction.Open(Path.GetDirectoryName(path)!); return Task.CompletedTask; });
    });

    /// <summary>Export wording in the interface language. Trip content itself is never translated.</summary>
    public ExportLabels ExportLabels() => new()
    {
        Day = Strings["DayN"], AllDay = Strings["AllDay"], Unscheduled = Strings["NotScheduledYet"], Bookings = Strings["Bookings"], Costs = Strings["Costs"],
        Travellers = Strings["ExportTravellers"], Paid = Strings["ExportPaid"], Estimated = Strings["ExportEstimated"], Budget = Strings["ExportBudget"], Between = Strings["BetweenTimes"],
        Reference = Strings["ExportRef"], GeneratedBy = Strings["ExportFooter"],
        DayParts = new[] { "morning", "afternoon", "evening", "night" }.ToDictionary(k => k, k => Strings[k]),
        Statuses = TravelRepo.Core.ScheduleCategories.Statuses.Concat(["pending", "open", "in_progress"]).Distinct().ToDictionary(k => k, k => Strings[k])
    };

    public Task OpenLinkAsync(string link) => RunAsync(async () =>
    {
        var context = ShareLink.Parse(link);
        if (!await Interaction.ConfirmAsync(Strings["CloneLink"] + "\n\n" + context.Remote)) return;
        var folder = UniqueFolder(DefaultTripsFolder, Path.GetFileNameWithoutExtension(context.Remote.TrimEnd('/')));
        await GitRepository.CloneAsync(backend, context.Remote, folder);
        if (context.Branch is not null)
        {
            var git = new GitRepository(new TravelRepository(folder), backend); var variants = await git.VariantsAsync();
            var variant = variants.FirstOrDefault(v => v.Branch == context.Branch) ?? variants.FirstOrDefault(v => v.Branch == "origin/" + context.Branch);
            if (variant is null) throw new DomainException("share.variant", "The shared variant is not available on this remote.");
            await git.SwitchAsync(variant.Branch);
        }
        if (OpenRequested is not null) OpenRequested(folder); else await OpenAsync(folder);
    });
}
