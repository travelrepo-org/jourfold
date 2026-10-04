using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.Input;
using Jourfold.Infrastructure;
using TravelRepo.Core;

namespace Jourfold.Desktop;

public partial class MainViewModel
{
    [RelayCommand] private Task Settings() => Interaction.SettingsAsync(this);

    /// <summary>Opt-in for OpenStreetMap tiles and Nominatim place search. Off until the user enables it.</summary>
    public bool OnlineMaps => Store.Get("map.online") == "true";
    public void SetOnlineMaps(bool enabled) { Store.Set("map.online", enabled ? "true" : "false"); OnPropertyChanged(nameof(OnlineMaps)); ContentChanged?.Invoke(this, EventArgs.Empty); }
    public bool MapPromptDismissed { get => Store.Get("map.prompt") == "dismissed"; set => Store.Set("map.prompt", value ? "dismissed" : "shown"); }

    public string Preference(string key, string fallback) => Store.Get(key == "OpenView" ? "openingView" : key.ToLowerInvariant()) ?? fallback;

    public void SetPreference(string key, string value)
    {
        Store.Set(key == "OpenView" ? "openingView" : key.ToLowerInvariant(), value);
        if (key == "Language")
        {
            Strings = new(value); CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(value == "de" ? "de-DE" : "en-GB");
            OnPropertyChanged(nameof(Strings)); OnPropertyChanged(nameof(SharingLabel)); OnPropertyChanged(nameof(TripTitle));
        }
        if (key == "Advanced") Advanced = value == "true";
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task SaveIdentityAsync(string name, string email) => RunAsync(async () =>
    {
        Store.Set("identity.name", name.Trim()); Store.Set("identity.email", email.Trim().Length > 0 ? email.Trim() : LocalEmail(name));
        if (Workspace is not null && name.Trim().Length > 0) await Workspace.Git.SetIdentityAsync(name.Trim(), Store.Get("identity.email")!);
        Interaction.Toast(Strings["SavedIdentity"]);
    });

    public async Task<string> DiagnosticsTextAsync()
    {
        var lines = new List<string> { "Jourfold " + AppVersion.Informational, "TravelRepo " + TravelRepoInfo.InformationalVersion + ", format " + TravelRepoInfo.FormatVersion, AppVersion.Platform };
        try { lines.Add("Git: " + new TravelRepo.Git.GitCliBackend().Executable); } catch (DomainException ex) { lines.Add("Git: " + ex.Message); }
        if (Workspace is not null)
        {
            var remotes = await Workspace.Git.RemotesAsync();
            static string SafeUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" }.Uri.ToString() : url;
            var extensions = Workspace.State.Trip.All.SelectMany(e => (e.Data["extensions"] as JsonObject ?? []).Select(p => p.Key).Concat((e.Data["components"] as JsonObject ?? []).Where(p => p.Key.Contains('.')).Select(p => p.Key))).Distinct();
            lines.AddRange([Strings["TripFolder"] + ": " + Workspace.Repository.Root, Strings["Branch"] + ": " + await Workspace.Git.CurrentBranchAsync(), "HEAD: " + await Workspace.Git.HeadAsync(), Strings["GitStatus"] + ":", await Workspace.Git.StatusAsync()]);
            lines.AddRange(remotes.Select(r => Strings["Remote"] + " " + r.Key + ": " + SafeUrl(r.Value)));
            lines.AddRange(Workspace.State.Diagnostics.Select(d => d.Code + ": " + d.Message));
            lines.Add(Strings["Extensions"] + ": " + string.Join(", ", extensions));
        }
        lines.Add("GitHub: " + (Store.Get("github.client") is null ? Strings["NotConfigured"] : Strings["Configured"]));
        var plugins = PluginCatalog.Known(Store);
        lines.Add(Strings["Plugins"] + ": " + (plugins.Count == 0 ? Strings["NotConfigured"] : string.Join(", ", plugins.Select(p => p.Manifest.Id + " " + p.Manifest.Version))));
        return string.Join('\n', lines.Where(l => l.Length > 0));
    }

    public Task SettingsActionAsync(string key) => RunAsync(async () =>
    {
        if (key == "HighContrast") { Store.Set("highcontrast", Store.Get("highcontrast") == "true" ? "false" : "true"); ContentChanged?.Invoke(this, EventArgs.Empty); }
        if (key == "Advanced") { Advanced = !Advanced; ContentChanged?.Invoke(this, EventArgs.Empty); }
        if (key == "PreferredRemote" && Workspace is not null) { var remotes = await Workspace.Git.RemotesAsync(); var name = await Interaction.ChooseAsync(Strings["PreferredRemote"], remotes.Select(k => new Choice(k.Key, k.Key, "cloud", k.Value)).ToArray()); if (name is not null) Store.Set("remote:" + Workspace.Repository.Root, name); }
        if (key == "Diagnostics") await Interaction.ShowAsync(Strings["Diagnostics"], await DiagnosticsTextAsync());
        if (key == "Repair" && Workspace is not null)
        {
            await Workspace.Repository.RecoverAsync(); var plan = await Workspace.Repository.PreviewRestoreAsync(await Workspace.Git.SnapshotAsync("HEAD"));
            if (plan.Changes.Count == 0) { Interaction.Toast(Strings["NothingToRepair"]); return; }
            if (await Interaction.ConfirmAsync(Strings["Rollback"] + "\n\n" + string.Join('\n', plan.Changes.Select(c => c.Path)))) { await Workspace.Repository.ApplyRestoreAsync(plan); await Workspace.ReloadAsync(); Refresh(); }
        }
        if (key == "Plugins")
        {
            if (!await Interaction.ConfirmAsync(Strings["Plugins"], Strings["PluginTrust"], Strings["ChoosePluginFolder"])) return; var folder = await Interaction.FolderAsync(Strings["Plugins"]); if (folder is null) return;
            await UsePluginAsync(folder);
        }
        if (key.StartsWith("UsePlugin:", StringComparison.Ordinal)) await UsePluginAsync(key["UsePlugin:".Length..]);
        if (key.StartsWith("ForgetPlugin:", StringComparison.Ordinal)) PluginCatalog.Forget(Store, key["ForgetPlugin:".Length..]);
        if (key == "Report")
        {
            var report = await Interaction.PromptAsync(Strings["ReportInfo"], Strings["ReportTemplate"] + "\n\n" + await DiagnosticsTextAsync(), true);
            if (report is not null) { await Interaction.CopyAsync(report); Interaction.Toast(Strings["ReportCopied"]); }
        }
        if (key == "ClearMapCache") { MapTiles.ClearCache(Store.Root); Interaction.Toast(Strings["MapCacheCleared"]); }
        if (key is "ConnectGitHub" or "DiscoverGitHub" or "AddRemote") { Busy = false; await (key == "ConnectGitHub" ? ConnectGitHubCommand : key == "DiscoverGitHub" ? DiscoverGitHubCommand : AddRemoteCommand).ExecuteAsync(null); }
    });

    /// <summary>Starts the plugin in <paramref name="folder"/>, remembers it and, with an item selected, adds one of its components.</summary>
    private async Task UsePluginAsync(string folder)
    {
        var host = Path.Combine(AppContext.BaseDirectory, "PluginHost", OperatingSystem.IsWindows() ? "Jourfold.PluginHost.exe" : "Jourfold.PluginHost");
        using var plugin = new PluginProcess(host, folder); var description = await plugin.DescribeAsync(); PluginCatalog.Remember(Store, folder);
        if (Workspace is null || Selected is null) { Interaction.Toast(string.Format(Strings["PluginLoaded"], plugin.Manifest.DisplayName)); return; }
        var component = await Interaction.ChooseAsync(Strings["Plugins"], description.Components.Select(c => new Choice(c.Type, c.Title, "plug")).ToArray()); if (component is null) return;
        var definition = description.Components.Single(c => c.Type == component);
        var values = await Interaction.FormAsync(definition.Title, definition.Fields.Select(f => new FormField(f.Key, f.Label, f.Choices is not null ? "choice" : f.Kind == "number" ? "number" : "text", Choices: f.Choices?.Select(v => new Choice(v, v)).ToArray(), Required: true)).ToArray()); if (values is null) return;
        var data = new JsonObject(); foreach (var field in definition.Fields) data[field.Key] = field.Kind == "number" ? JsonValue.Create(double.Parse(values[field.Key], CultureInfo.InvariantCulture)) : JsonValue.Create(values[field.Key]);
        var entity = Selected.Copy(); if (entity.Data["components"] is not JsonObject) entity.Data["components"] = new JsonObject(); entity.Data["components"]![component] = data; await Workspace.EditAsync(entity);
    }

    // Command palette -------------------------------------------------------------------------------------

    public IReadOnlyList<PaletteChoice> PaletteSearch(string query)
    {
        query = query.Trim(); var s = Strings; var results = new List<PaletteChoice>();
        bool Match(string text) => query.Length == 0 || text.Contains(query, StringComparison.CurrentCultureIgnoreCase);
        void Command(string id, string label, string icon, string? shortcut = null) { if (Match(label)) results.Add(new("command:" + id, label, null, icon, s["Commands"], shortcut)); }
        if (HasTrip)
        {
            Command("QuickAdd", s["QuickAdd"], "plus", "Ctrl+N");
            Command("Version", s["Version"], "save", "Ctrl+S");
            Command("NewVariant", s["NewVariant"], "git-branch");
            Command("Sync", s["SyncNow"], "refresh-cw");
            Command("Share", SharingLabel, "share-2");
            Command("Export", s["Export"], "download");
            Command("AddFile", s["AddFile"], "paperclip");
            Command("Undo", s["Undo"], "undo-2", "Ctrl+Z");
            Command("Redo", s["Redo"], "redo-2", "Ctrl+Shift+Z");
            Command("Library", s["AllTrips"], "layout-grid");
            foreach (var key in Navigation) if (Match(s[key])) results.Add(new("view:" + key, s[key], s["GoTo"], NavIcon(key), s["Navigation"], null));
        }
        else
        {
            Command("NewTrip", s["NewTrip"], "plus"); Command("OpenTrip", s["OpenTrip"], "folder-open"); Command("Sample", s["ExploreSample"], "sparkles");
            foreach (var recent in Recent.Where(r => Match(r.Title)).Take(8)) results.Add(new("recent:" + recent.Path, recent.Title, recent.Path, "map", s["Recent"], null));
        }
        Command("Settings", s["Settings"], "settings", "Ctrl+,");
        Command("Theme", s["ToggleTheme"], "moon");
        Command("About", s["About"], "info");
        if (Workspace is not null && query.Length > 0)
        {
            Store.Index(Workspace.Repository.Root, Workspace.State.Trip);
            foreach (var id in Store.Search(Workspace.Repository.Root, query).Take(25))
                if (Workspace.State.Trip.Find(id) is { Type: not "trip" and not "comment" } e) { var (icon, _) = Visuals.For(Workspace.State.Trip, e); results.Add(new(id.ToString(), e.Title, s[e.Type == "schedule_item" ? "activity" : e.Type], icon, s["InThisTrip"], null)); }
        }
        return results;
    }

    public async Task ExecutePaletteAsync(PaletteChoice choice)
    {
        var id = choice.Id;
        if (id.StartsWith("view:", StringComparison.Ordinal)) { View = id[5..]; return; }
        if (id.StartsWith("recent:", StringComparison.Ordinal)) { OpenRequested?.Invoke(id[7..]); return; }
        if (id.StartsWith("variant:", StringComparison.Ordinal) && Workspace is not null) { var variant = (await Workspace.Git.VariantsAsync()).FirstOrDefault(v => v.Branch == id[8..]); if (variant is not null) await VariantActionAsync(variant, "Switch"); return; }
        if (id.StartsWith("command:", StringComparison.Ordinal))
        {
            switch (id[8..])
            {
                case "QuickAdd": await QuickAddCommand.ExecuteAsync(null); break;
                case "Version": await CreateVersionCommand.ExecuteAsync(null); break;
                case "NewVariant": await NewVariantCommand.ExecuteAsync(null); break;
                case "Sync": await SyncCommand.ExecuteAsync(null); break;
                case "Share": await ShareCommand.ExecuteAsync(null); break;
                case "Export": await ExportCommand.ExecuteAsync(null); break;
                case "AddFile": await AddFileCommand.ExecuteAsync(null); break;
                case "Undo": await UndoCommand.ExecuteAsync(null); break;
                case "Redo": await RedoCommand.ExecuteAsync(null); break;
                case "Library": CloseTrip(); break;
                case "NewTrip": await NewTripCommand.ExecuteAsync(null); break;
                case "OpenTrip": await OpenTripCommand.ExecuteAsync(null); break;
                case "Sample": await OpenSampleCommand.ExecuteAsync(null); break;
                case "Settings": await SettingsCommand.ExecuteAsync(null); break;
                case "About": await AboutCommand.ExecuteAsync(null); break;
                case "Theme": SetPreference("Theme", Preference("Theme", "System") == "Dark" ? "Light" : "Dark"); break;
            }
            return;
        }
        if (Guid.TryParse(id, out var entityId) && Workspace?.State.Trip.Find(entityId) is { } entity)
        {
            View = entity.Type switch { "booking" => "Bookings", "task" => "Tasks", "expense" or "budget" => "Costs", "document" => "Files", "person" => "People", "place" => "Places", "collection" => "Collections", _ => View is "Plan" or "List" or "Map" ? View : "Plan" };
            if (entity.Type == "schedule_item" && ScheduleQueries.Span(Workspace.State.Trip, entity).Start is { } start) ShowDate(start.InZone(ScheduleQueries.TripZone(Workspace.State.Trip)).Date);
            Selected = entity;
        }
    }

    [RelayCommand] private async Task Search() { var choice = await Interaction.PaletteAsync(this); if (choice is not null) await ExecutePaletteAsync(choice); }

    public static string NavIcon(string key) => key switch
    {
        "Plan" => "calendar-days",
        "Map" => "map",
        "List" => "list",
        "Bookings" => "ticket",
        "Costs" => "wallet",
        "Tasks" => "list-checks",
        "Files" => "folder",
        "People" => "users",
        "Places" => "map-pin",
        "Collections" => "layers",
        "History" => "history",
        "Variants" => "git-branch",
        _ => "circle"
    };
}
