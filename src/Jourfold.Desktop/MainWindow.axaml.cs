using Avalonia.VisualTree;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Jourfold.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Nodes;
using TravelRepo.Core;
using TravelRepo.Git;
using TravelRepo.Repository;
namespace Jourfold.Desktop;

public partial class MainWindow : Window
{
    private static readonly Dictionary<string, MainWindow> OpenWindows = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    public MainViewModel Model { get; }
    private readonly DispatcherTimer poll;
    private int renderVersion;
    private NodaTime.LocalDate? renderedDate;
    public MainWindow()
    {
        InitializeComponent();
        MainViewModel? vm = null; var store = App.Services.GetRequiredService<LocalStore>();
        var interaction = new Dialogs(this, () => vm?.Strings ?? new Localization("en"));
        vm = new MainViewModel(store, App.Services.GetRequiredService<IGitBackend>(), App.Services.GetRequiredService<OsSecretStore>(), interaction); Model = vm; DataContext = Model;
        Model.ContentChanged += (_, _) => Render(); Model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Model.Notice)) NoticeVisibility(); if (e.PropertyName == nameof(Model.Selected)) { var key = InspectorFocusKey(); Inspector.Children.Clear(); RenderInspector(); RestoreInspectorFocus(key); } };
        Model.OpenRequested += path => Dispatcher.UIThread.Post(() => OpenTrip(path)); InboxToggle.Click += (_, _) => { Model.InboxOpen = !Model.InboxOpen; if (!Model.InboxOpen) Model.InboxPinned = false; };
        InboxList.SelectionChanged += (_, _) => { if (InboxList.SelectedItem is Entity entity) Model.Selected = entity; };
        PointerPressedEventArgs? inboxPress = null; Point inboxStart = default;
        InboxList.PointerPressed += (_, e) => { if (e.GetCurrentPoint(InboxList).Properties.IsLeftButtonPressed) { inboxPress = e; inboxStart = e.GetPosition(InboxList); } };
        InboxList.PointerReleased += (_, _) => inboxPress = null;
        InboxList.PointerMoved += async (_, e) =>
        {
            if (inboxPress is not { } pressed || !e.GetCurrentPoint(InboxList).Properties.IsLeftButtonPressed || Math.Abs(e.GetPosition(InboxList).X - inboxStart.X) + Math.Abs(e.GetPosition(InboxList).Y - inboxStart.Y) < 6 || InboxList.SelectedItem is not Entity entity) return;
            inboxPress = null; var data = new DataTransfer(); data.Add(DataTransferItem.Create(PlanningView.EntityFormat, entity.Id.ToString())); await DragDrop.DoDragDropAsync(pressed, data, DragDropEffects.Move);
        };
        poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) }; poll.Tick += async (_, _) => { if (FocusManager?.GetFocusedElement() is not TextBox) await Model.PollAsync(); }; poll.Start();
        Closed += (_, _) => { poll.Stop(); if (Model.Workspace is not null) OpenWindows.Remove(Model.Workspace.Repository.Root); Model.Dispose(); };
        if (PlatformSettings is { } platform) platform.ColorValuesChanged += (_, _) => Dispatcher.UIThread.Post(Render);
        SizeChanged += (_, _) => UpdateScale();
        Render();
    }
    private void OpenTrip(string path)
    {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (OpenWindows.TryGetValue(path, out var existing)) { existing.Activate(); return; }
        if (Model.Workspace is not null) { var window = new MainWindow(); window.Show(); window.OpenTrip(path); return; }
        _ = Model.RunAsync(async () => { await Model.OpenAsync(path); OpenWindows[path] = this; Render(); });
    }
    private Button Button(string key, Action action)
    { var b = new Button { Content = Model.Strings[key], HorizontalAlignment = HorizontalAlignment.Stretch }; AutomationProperties.SetName(b, Model.Strings[key]); ToolTip.SetTip(b, Model.Strings[key]); b.Click += (_, _) => action(); return b; }
    private Button AsyncButton(string key, Func<Task> action) => Button(key, () => _ = Model.RunAsync(action));
    private async Task LoadAsync(Func<Task> action) { try { await action(); } catch (Exception ex) when (ex is DomainException or IOException) { Model.Status = Model.Strings["Error"] + ": " + Model.Strings.Error(ex, Model.Advanced); } }
    private void NoticeVisibility() { ReviewNotice.IsVisible = CancelNotice.IsVisible = Model.Notice.Length > 0; }
    private void UpdateScale()
    {
        var scale = Model.Store.Get("textsize") switch { "Large" => 1.25, "Larger" => 1.5, _ => 1.0 };
        RootLayout.LayoutTransform = new ScaleTransform(scale, scale); WorkspaceLayout.Width = Math.Max(850, ClientSize.Width / scale); WorkspaceLayout.Height = Math.Max(580, ClientSize.Height / scale);
    }
    private void Render()
    {
        var previousScroll = (MainContent.Content as PlanningView)?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        var previousOffset = renderedDate == Model.StartDate ? previousScroll?.Offset : null;
        renderedDate = Model.StartDate;
        RequestedThemeVariant = Model.Store.Get("theme") switch { "Light" => ThemeVariant.Light, "Dark" => ThemeVariant.Dark, _ => ThemeVariant.Default }; Classes.Set("compact", Model.Store.Get("density") == "Compact");
        var highContrast = Model.Store.Get("highcontrast") == "true" || PlatformSettings?.GetColorValues().ContrastPreference.ToString() == "High";
        foreach (var key in new[] { "Surface", "Panel", "Ink", "Line", "Accent" }) Resources.Remove(key);
        if (highContrast) { Resources["Surface"] = Brushes.Black; Resources["Panel"] = Brushes.Black; Resources["Ink"] = Brushes.White; Resources["Line"] = Brushes.White; Resources["Accent"] = Brushes.Yellow; }
        BrandLockup.Source = new Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://Jourfold.Desktop/Assets/Branding/" + (highContrast || ActualThemeVariant == ThemeVariant.Dark ? "lockup-dark.png" : "lockup.png"))));
        UpdateScale();
        NoticeVisibility(); NavigationPanel.Children.Clear(); foreach (var key in Model.Navigation) { var b = Button(key, () => Model.Navigate(key)); b.Classes.Add("navigation"); if (key == Model.View) b.Classes.Add("selected"); NavigationPanel.Children.Add(b); }
        var focusKey = InspectorFocusKey(); ViewToolbar.Children.Clear(); Inspector.Children.Clear(); var version = ++renderVersion;
        if (Model.Workspace is null)
        {
            var panel = new StackPanel { Spacing = 12 }; panel.Children.Add(new TextBlock { Text = Model.Strings["Empty"], TextWrapping = TextWrapping.Wrap });
            var library = Model.Store.Recent();
            foreach (var group in library.GroupBy(r => r.State == "remote" || r.State == Model.Strings["Remote"] ? "RemoteTrips" : "LocalTrips"))
            {
                panel.Children.Add(new TextBlock { Text = Model.Strings[group.Key], Classes = { "heading" } });
                foreach (var recent in group)
                {
                    var button = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, Content = new StackPanel { Children = { new TextBlock { Text = recent.Title, FontWeight = FontWeight.SemiBold, FontSize = 18 }, new TextBlock { Text = DateTimeOffset.Parse(recent.Opened).ToLocalTime().ToString("g") + " · " + Model.Strings[recent.State == "remote" ? "Remote" : recent.State == "local" ? "Local" : recent.State] + " · " + (Model.Store.Get("sync:" + recent.Path) is { } sync ? Model.Strings[sync] : ""), Opacity = .7 } } } }; button.Click += (_, _) => Model.OpenRecent(recent); panel.Children.Add(button);
                    _ = LoadAsync(async () =>
                    {
                        if (!Directory.Exists(recent.Path)) return; var repository = new TravelRepository(recent.Path); var snapshot = (await repository.ReadAsync()).Trip;
                        if (version != renderVersion) return; var detail = (StackPanel)button.Content!; var dates = TripQueries.EffectiveDates(snapshot);
                        detail.Children.Add(new TextBlock { Text = dates.Start?.ToString("d", System.Globalization.CultureInfo.CurrentCulture) + " – " + dates.End?.ToString("d", System.Globalization.CultureInfo.CurrentCulture) });
                        detail.Children.Add(new TextBlock { Text = string.Join(" · ", snapshot.Entities.Values.Where(e => e.Type == "place").Take(3).Select(e => e.Title)), TextWrapping = TextWrapping.Wrap });
                        detail.Children.Add(new TextBlock { Text = Model.Strings["LastChanged"] + ": " + LocalStore.LastChanged(recent.Path).ToLocalTime().ToString("g"), Opacity = .65 });
                        if (Guid.TryParse(snapshot.Manifest.Data["cover"]?["document"]?.ToString(), out var coverId) && snapshot.Find(coverId) is { } cover)
                        { try { detail.Children.Insert(0, new Image { Source = new Bitmap(repository.DocumentPath(cover)), Height = 110, Stretch = Stretch.UniformToFill }); } catch (ArgumentException) { } }
                    });
                }
            }
            MainContent.Content = new ScrollViewer { Content = panel }; Inspector.Children.Add(new TextBlock { Text = Model.Strings["Recent"], FontSize = 20 }); return;
        }
        if (Model.View is "Plan" or "Map")
        {
            if (Model.View == "Plan")
            {
                var previous = Button("Previous", () => { Model.StartDate = Model.StartDate.PlusDays(-3); Render(); }); previous.Content = "‹"; ViewToolbar.Children.Add(previous);
                var date = new CalendarDatePicker { SelectedDate = Model.StartDate.ToDateTimeUnspecified(), Width = 160 };
                ToolTip.SetTip(date, Model.Strings["Date"]); date.SelectedDateChanged += (_, _) => { if (version == renderVersion && date.SelectedDate is { } selected && NodaTime.LocalDate.FromDateTime(selected) != Model.StartDate) { Model.StartDate = NodaTime.LocalDate.FromDateTime(selected); Render(); } }; ViewToolbar.Children.Add(date);
                var next = Button("Next", () => { Model.StartDate = Model.StartDate.PlusDays(3); Render(); }); next.Content = "›"; ViewToolbar.Children.Add(next);
                var zoomOut = Button("ZoomOut", () => { Model.Zoom = Math.Max(.5, Model.Zoom - .25); Render(); }); zoomOut.Content = "−"; zoomOut.IsEnabled = Model.Zoom > .5; ViewToolbar.Children.Add(zoomOut);
                var zoom = new ComboBox { ItemsSource = new[] { "50%", "75%", "100%", "125%", "150%", "175%", "200%" }, SelectedIndex = (int)Math.Round((Model.Zoom - .5) / .25), MinWidth = 85 };
                ToolTip.SetTip(zoom, Model.Strings["Zoom"]); zoom.SelectionChanged += (_, _) => { if (version == renderVersion && zoom.SelectedIndex >= 0 && Model.Zoom != .5 + zoom.SelectedIndex * .25) { Model.Zoom = .5 + zoom.SelectedIndex * .25; Render(); } }; ViewToolbar.Children.Add(zoom);
                var zoomIn = Button("ZoomIn", () => { Model.Zoom = Math.Min(2, Model.Zoom + .25); Render(); }); zoomIn.Content = "+"; zoomIn.IsEnabled = Model.Zoom < 2; ViewToolbar.Children.Add(zoomIn);
                ViewToolbar.Children.Add(Button("Lanes", () => { Model.Lanes = !Model.Lanes; Render(); }));
                ToolTip.SetTip(MainContent, Model.Workspace.State.Trip.Manifest.Data["default_timezone"]?.ToString() ?? "Etc/UTC");
            }
            var planner = new PlanningView(Model, Model.View == "Map"); MainContent.Content = planner;
            if (previousOffset is { } offset) Dispatcher.UIThread.Post(() => { if (MainContent.Content == planner && planner.Content is Grid grid && grid.Children.OfType<ScrollViewer>().FirstOrDefault() is { } scroll) scroll.Offset = offset; }, DispatcherPriority.Loaded);
        }
        else if (Model.View == "Variants")
        {
            ViewToolbar.Children.Add(Button("NewVariant", () => Model.NewVariantCommand.Execute(null))); var panel = new StackPanel { Spacing = 12 }; MainContent.Content = new ScrollViewer { Content = panel };
            _ = LoadAsync(async () => { foreach (var v in await Model.Workspace.Git.VariantsAsync()) { if (version != renderVersion) return; var row = new StackPanel { Spacing = 5 }; row.Children.Add(new TextBlock { Text = v.Manifest.Data["variant"]!["title"]!.ToString(), FontSize = 18, FontWeight = FontWeight.SemiBold }); row.Children.Add(new TextBlock { Text = (Model.Advanced ? v.Branch + " · " : "") + Model.Strings[v.Manifest.Data["variant"]!["state"]!.ToString()], Opacity = .65 }); var buttons = new WrapPanel { Orientation = Orientation.Horizontal }; foreach (var key in new[] { "Switch", "Compare", "Merge", "Rename", "Archive", "ShareVariant" }) { var b = Button(key, () => _ = Model.VariantActionAsync(v, key)); b.Margin = new Thickness(0, 0, 4, 4); buttons.Children.Add(b); } row.Children.Add(buttons); panel.Children.Add(new Border { Classes = { "panel" }, Padding = new Thickness(16), Child = row }); } });
        }
        else if (Model.View == "History")
        {
            var panel = new StackPanel { Spacing = 12 }; MainContent.Content = new ScrollViewer { Content = panel };
            _ = LoadAsync(async () =>
            {
                foreach (var entry in await Model.Workspace.Git.HistoryAsync())
                {
                    if (version != renderVersion) return;
                    var row = new StackPanel { Spacing = 5 };
                    var author = Model.Workspace.State.Trip.Entities.Values.FirstOrDefault(p => p.Type == "person" && p.Data["identities"]?["git"] is JsonArray identities && identities.Any(i => string.Equals(i?["email"]?.ToString(), entry.Email, StringComparison.OrdinalIgnoreCase)))?.Title ?? entry.Author;
                    var action = entry.Message.Split('\n').FirstOrDefault(line => line.StartsWith("TravelRepo-Action: ", StringComparison.Ordinal))?[19..];
                    var heading = !entry.ApplicationGenerated ? Model.Strings["ExternalVersion"] : Model.Strings[action switch { "semantic-merge" => "HistoryMerge", "initialize" => "HistoryCreate", "create-variant" => "NewVariant", "variant-metadata" => "HistoryVariant", _ => "Version" }];
                    row.Children.Add(new TextBlock { Text = author + " · " + heading, FontWeight = FontWeight.SemiBold });
                    row.Children.Add(new TextBlock { Text = entry.Message.Split('\n')[0], TextWrapping = TextWrapping.Wrap });
                    row.Children.Add(new TextBlock { Text = DateTimeOffset.Parse(entry.Timestamp).ToLocalTime().ToString("g"), Opacity = .7 });
                    var details = new StackPanel { Spacing = 5 }; var expander = new Expander { Header = Model.Strings["Details"], Content = details }; var loaded = false;
                    expander.PropertyChanged += (_, args) =>
                    {
                        if (args.Property.Name == "IsExpanded" && expander.IsExpanded && !loaded)
                        {
                            loaded = true; _ = LoadAsync(async () =>
                    {
                        var after = await Model.Workspace.Git.SnapshotAsync(entry.Commit); var parent = entry.Parents.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                        if (parent is null) details.Children.Add(new TextBlock { Text = Model.Strings["HistoryCreate"] });
                        else
                        {
                            var before = await Model.Workspace.Git.SnapshotAsync(parent);
                            foreach (var group in TravelRepo.Merge.SemanticMerge.Diff(before, after).GroupBy(c => c.Entity))
                            {
                                var previous = before.Find(group.Key); var next = after.Find(group.Key); var title = (next ?? previous)?.Title ?? Model.Strings["Content"];
                                details.Children.Add(new TextBlock { Text = string.Format(Model.Strings[previous is null ? "VersionAdd" : next is null ? "VersionRemove" : "VersionUpdate"], title), TextWrapping = TextWrapping.Wrap });
                            }
                        }
                    });
                        }
                    };
                    row.Children.Add(expander);
                    if (Model.Advanced) row.Children.Add(new SelectableTextBlock { Text = entry.Commit + "\n" + entry.Refs + "\n" + entry.Author + " <" + entry.Email + ">\n" + entry.Committer + " <" + entry.CommitterEmail + ">\n" + entry.Parents + "\n" + entry.Message, TextWrapping = TextWrapping.Wrap });
                    row.Children.Add(AsyncButton("Rollback", async () => { var plan = await Model.Workspace.Repository.PreviewRestoreAsync(await Model.Workspace.Git.SnapshotAsync(entry.Commit)); if (await Model.Interaction.ConfirmAsync(Model.Strings["Rollback"] + "\n" + string.Join('\n', plan.Changes.Select(c => c.Path)))) { await Model.Workspace.Repository.ApplyRestoreAsync(plan); await Model.Workspace.ReloadAsync(); Model.Refresh(); } }));
                    panel.Children.Add(new Border { Classes = { "panel" }, Padding = new Thickness(16), Child = row });
                }
            });
        }
        else
        {
            if (Model.View == "People") ViewToolbar.Children.Add(Button("AddIdentity", () => Model.AddIdentityCommand.Execute(null)));
            if (Model.View == "Files") ViewToolbar.Children.Add(Button("AddFile", () => Model.AddFileCommand.Execute(null)));
            var list = new ListBox { ItemsSource = Model.Items, SelectedItem = Model.Selected, ItemTemplate = new FuncDataTemplate<Entity>((e, _) => new StackPanel { Margin = new Thickness(4, 7), Children = { new TextBlock { Text = e?.Title, FontSize = 16, FontWeight = FontWeight.Medium }, new TextBlock { Text = e?.Data["time"]?["start"]?["local"]?.ToString() ?? Model.Strings[e?.Type ?? ""], Opacity = .65 } } }) };
            list.SelectionChanged += (_, _) => { if (list.SelectedItem is Entity e && Model.Selected?.Id != e.Id) Model.Selected = e; }; MainContent.Content = list;
        }
        if (Model.View is "Plan" or "Map" or "List" or "Bookings" or "Costs" or "Tasks" or "People" or "Places" or "Collections")
        {
            var kind = Model.View switch { "Map" or "Places" => "place", "Bookings" => "booking", "Costs" => "expense", "Tasks" => "task", "People" => "person", "Collections" => "collection", _ => "activity" };
            ViewToolbar.Children.Add(AsyncButton("Add", () => Model.AddAsync(kind)));
            if (Model.View == "Costs") ViewToolbar.Children.Add(AsyncButton("AddBudget", () => Model.AddAsync("budget")));
        }
        ViewToolbar.Children.Add(Button("Details", () => Model.Selected = Model.Workspace.State.Trip.Manifest));
        RenderInspector(); RestoreInspectorFocus(focusKey);
    }
    private string? InspectorFocusKey() => (FocusManager?.GetFocusedElement() as Control)?.Tag as string;
    private void RestoreInspectorFocus(string? key) { if (key is not null) Dispatcher.UIThread.Post(() => Inspector.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.Tag as string == key)?.Focus(), DispatcherPriority.Background); }
    private void RenderInspector()
    {
        var s = Model.Strings; var e = Model.Selected;
        Inspector.Children.Add(new TextBlock { Text = s["Details"], Classes = { "heading" } });
        if (e is null) { Inspector.Children.Add(new TextBlock { Text = s["SelectItem"], TextWrapping = TextWrapping.Wrap }); return; }
        var id = e.Id;
        foreach (var field in EditorModel.Fields(e))
        {
            Inspector.Children.Add(new TextBlock { Text = s[field.Label], FontWeight = FontWeight.SemiBold }); var current = EditorModel.Get(e, field.Path); var value = current?.ToString() ?? "";
            if (field.Kind is "ref" or "refs" or "money" or "coordinates" or "identities" or "github" or "stops" or "zoned" or "related" or "target" or "date" or "timezone" or "language" or "duration" or "distance")
            {
                var label = current is JsonArray a ? string.Join(", ", a.Select(n => Guid.TryParse(n?.ToString(), out var refId) ? Model.Workspace!.State.Trip.Find(refId)?.Title : n?.ToString())) : Guid.TryParse(value, out var refOne) ? Model.Workspace!.State.Trip.Find(refOne)?.Title : value;
                if (field.Kind == "money" && current is JsonObject money) label = money["value"] + " " + money["currency"];
                if (field.Kind == "coordinates" && current is JsonObject location) label = location["latitude"] + ", " + location["longitude"];
                if (field.Kind == "zoned" && current?["local"] is { } wall && DateTime.TryParse(wall.ToString(), out var localDate)) label = localDate.ToString("g") + " · " + current["timezone"];
                if (field.Kind == "date" && DateTime.TryParse(value, out var dateValue)) label = dateValue.ToString("d");
                if (field.Kind == "language") label = FieldOptions.Languages.FirstOrDefault(c => c.Id == value)?.Label ?? value;
                if (field.Kind == "timezone") label = value.Replace('_', ' ').Replace("/", " / ");
                if (field.Kind == "distance" && double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var length)) label = DisplayValues.Distance(length, Model.Store.Get("units") == "Imperial");
                if (field.Kind == "duration" && NodaTime.Text.PeriodPattern.NormalizingIso.Parse(value) is { Success: true } period) { try { label = period.Value.ToDuration().TotalMinutes.ToString("0.##") + " min"; } catch (InvalidOperationException) { } }
                var b = AsyncButton(field.Label, () => EditorModel.ComplexAsync(Model, id, field)); b.Tag = field.Path; b.Content = string.IsNullOrEmpty(label) ? "+ " + s[field.Label] : label; Inspector.Children.Add(b);
            }
            else if (field.Kind == "schedule")
            {
                var button = AsyncButton("Schedule", () => EditorModel.TimeAsync(Model));
                var time = e.Data["time"];
                button.Content = new TextBlock { Text = time?["start"]?["local"] is { } local && DateTime.TryParse(local.ToString(), out var date) ? date.ToString("g") + "\n" + time["start"]!["timezone"] + " · " + s["Edit"] : s["Schedule"], TextWrapping = TextWrapping.Wrap };
                Inspector.Children.Add(button);
            }
            else if (field.Kind == "readonly") Inspector.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap });
            else if (field.Kind == "choice")
            {
                var options = field.Choices!.Select(k => new Choice(k, s[k])).ToArray(); var combo = new ComboBox { Tag = field.Path, ItemsSource = options, SelectedItem = options.FirstOrDefault(c => c.Id == value), HorizontalAlignment = HorizontalAlignment.Stretch };
                AutomationProperties.SetName(combo, s[field.Label]); combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is Choice c && c.Id != value) _ = Model.RunEditAsync(() => EditorModel.UpdateAsync(Model, id, field, c.Id)); }; Inspector.Children.Add(combo);
            }
            else if (field.Kind == "bool")
            {
                var check = new CheckBox { Tag = field.Path, IsChecked = value == "true", Content = s[field.Label] }; check.IsCheckedChanged += (_, _) => _ = Model.RunEditAsync(() => EditorModel.UpdateAsync(Model, id, field, check.IsChecked == true ? "true" : "false")); Inspector.Children.Add(check);
            }
            else
            {
                if (field.Kind == "distance" && double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var meters)) value = DisplayValues.Distance(meters, Model.Store.Get("units") == "Imperial");
                if (field.Kind == "list" && current is JsonArray a) value = string.Join(", ", a.Select(n => n?.ToString()));
                var text = new TextBox { Tag = field.Path, Text = value, AcceptsReturn = field.Kind == "multiline", MinHeight = field.Kind == "multiline" ? 120 : 32 }; AutomationProperties.SetName(text, s[field.Label]); var original = value;
                text.LostFocus += (_, _) => { if ((text.Text ?? "") != original) { var next = text.Text ?? ""; _ = Model.RunEditAsync(() => EditorModel.UpdateAsync(Model, id, field, next)); } }; Inspector.Children.Add(text);
            }
        }
        if (e.Type is "schedule_item" or "note" or "document") Inspector.Children.Add(AsyncButton("ScheduleMaterial", async () =>
        {
            var zone = Model.Workspace!.State.Trip.Manifest.Data["default_timezone"]?.ToString() ?? "Etc/UTC";
            var time = await Model.Interaction.ScheduleAsync(null, Model.StartDate, zone); if (time is null) return;
            var scheduled = await Model.Workspace.PlanMaterialAsync(id, time); Model.Selected = Model.Workspace.State.Trip.Find(scheduled);
            Model.View = "Plan";
        }));
        if (e.Type == "schedule_item")
        {
            Inspector.Children.Add(AsyncButton("Precision", () => EditorModel.TimeAsync(Model)));
            Inspector.Children.Add(AsyncButton("Constraints", async () =>
            {
                var kind = await Model.Interaction.ChooseAsync(s["Constraints"], new[] { "after", "earliest_start", "arrive_before" }.Select(k => new Choice(k, s[k])).ToArray()); if (kind is null) return;
                var constraint = new JsonObject { ["type"] = kind };
                if (kind == "earliest_start") { var values = await Model.Interaction.FormAsync(s["Start"], [new("time", s["Start"], "time", "10:00:00", Required: true)]); if (values is null) return; constraint["local_time"] = values["time"]; }
                else { var item = await Model.Interaction.ChooseAsync(s["Items"], Model.Workspace!.State.Trip.Entities.Values.Where(x => x.Type == "schedule_item" && x.Id != e.Id).Select(x => new Choice(x.Id.ToString(), x.Title)).ToArray()); if (item is null) return; var values = await Model.Interaction.FormAsync(s["Offset"], [new("duration", s["DurationMinutes"], "duration", "PT30M", Required: true)]); if (values is null) return; constraint["item"] = item; constraint["offset"] = values["duration"]; }
                var updated = Model.Workspace!.State.Trip.Find(e.Id)!.Copy(); var list = updated.Data["constraints"] as JsonArray ?? []; if (updated.Data["constraints"] is null) updated.Data["constraints"] = list; list.Add(constraint); await Model.Workspace.EditAsync(updated);
            }));
            var parent = TripQueries.Parent(Model.Workspace!.State.Trip, e.Id); if (parent is not null) { var b = Button("Items", () => Model.Selected = parent); b.Content = "↑ " + parent.Title; Inspector.Children.Add(b); }
            foreach (var field in new[] { ("participants", "Participants"), ("tags", "Tags"), ("default_place", "DefaultPlace"), ("display", "Display"), ("category", "Category") })
            {
                var inherited = TripQueries.Resolve(Model.Workspace.State.Trip, e.Id, field.Item1);
                string Label(JsonNode? n) => Guid.TryParse(n?.ToString(), out var refId) ? Model.Workspace.State.Trip.Find(refId)?.Title ?? "" : n?.ToString() ?? "";
                var value = inherited.Value is JsonArray array ? string.Join(", ", array.Select(Label)) : Label(inherited.Value);
                Inspector.Children.Add(new TextBlock { Text = s[field.Item2] + " · " + s[inherited.Inherited ? "Inherited" : "Local"] + ": " + value, TextWrapping = TextWrapping.Wrap, Opacity = .7 });
            }
            Inspector.Children.Add(AsyncButton("UseInherited", async () => { var field = await Model.Interaction.ChooseAsync(s["UseInherited"], new[] { ("participants", "Participants"), ("tags", "Tags"), ("default_place", "DefaultPlace"), ("display", "Display"), ("category", "Category") }.Select(p => new Choice(p.Item1, s[p.Item2])).ToArray()); if (field is null) return; var updated = Model.Workspace.State.Trip.Find(id)!.Copy(); updated.Data.Remove(field); await Model.Workspace.EditAsync(updated); }));
            if (e.Data["children"] is JsonArray childIds) foreach (var childId in childIds) if (Guid.TryParse(childId?.ToString(), out var cid) && Model.Workspace.State.Trip.Find(cid) is { } child) { var childButton = Button("Children", () => Model.Selected = child); childButton.Content = "↳ " + child.Title; Inspector.Children.Add(childButton); }
            var range = TripQueries.Range(Model.Workspace.State.Trip, e); if (range.Start is { } first && range.End is { } last) Inspector.Children.Add(new TextBlock { Text = s["Duration"] + ": " + (last - first).TotalHours.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture) + " h" });
        }
        if (e.Type is "schedule_item" or "note")
        {
            Inspector.Children.Add(AsyncButton("Content", async () =>
            {
                var kind = await Model.Interaction.ChooseAsync(s["Content"], [new("markdown", s["Body"]), new("link", s["Link"]), new("reference", s["Reference"]), new("document", s["Files"])]); if (kind is null) return;
                ResourceEdit[] resources = []; var updated = Model.Workspace!.State.Trip.Find(id)!.Copy(); var content = updated.Data["content"] as JsonArray ?? new JsonArray(); if (updated.Data["content"] is null) updated.Data["content"] = content;
                if (kind is "document" or "reference") { var choice = await Model.Interaction.ChooseAsync(s["Files"], Model.Workspace.State.Trip.Entities.Values.Where(x => kind == "reference" || x.Type == "document").Select(x => new Choice(x.Id.ToString(), x.Title)).ToArray()); if (choice is null) return; var linked = Model.Workspace.State.Trip.Find(Guid.Parse(choice))!; content.Add(kind == "reference" ? new JsonObject { ["type"] = "reference", ["entity"] = choice } : new JsonObject { ["type"] = linked.Data["media_type"]?.ToString().StartsWith("image/", StringComparison.Ordinal) == true ? "image" : "pdf", ["document"] = choice }); }
                else { var text = await Model.Interaction.PromptAsync(s[kind == "link" ? "Link" : "Body"], multiline: kind == "markdown"); if (text is null) return; if (kind == "link") content.Add(new JsonObject { ["type"] = "link", ["url"] = text }); else { var file = "documents/" + Guid.CreateVersion7() + ".md"; resources = [new(file, System.Text.Encoding.UTF8.GetBytes(text))]; content.Add(new JsonObject { ["type"] = "markdown", ["file"] = file }); } }
                await Model.Workspace.ApplyAsync([new(updated.Id, updated)], resources: resources);
            }));
        }
        if (e.Data["content"] is JsonArray entries)
            foreach (var entry in entries.OfType<JsonObject>())
            {
                if (entry["file"] is { } file && Model.Workspace!.State.Trip.Resources.TryGetValue(file.ToString(), out var markdown))
                {
                    Inspector.Children.Add(new MarkdownView(System.Text.Encoding.UTF8.GetString(markdown)));
                    Inspector.Children.Add(AsyncButton("EditContent", async () => { var text = await Model.Interaction.PromptAsync(s["Body"], System.Text.Encoding.UTF8.GetString(markdown), true); if (text is not null) await Model.Workspace.ApplyAsync([], resources: [new(file.ToString(), System.Text.Encoding.UTF8.GetBytes(text))]); }));
                }
                var entryIndex = entries.IndexOf(entry);
                Inspector.Children.Add(AsyncButton("RemoveContent", async () => { var updated = Model.Workspace!.State.Trip.Find(id)!.Copy(); ((JsonArray)updated.Data["content"]!).RemoveAt(entryIndex); await Model.Workspace.EditAsync(updated); }));
                if (entry["url"] is { } url) { var b = Button("Link", () => { if (Uri.TryCreate(url.ToString(), UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http") Model.Interaction.Open(url.ToString()); }); b.Content = url.ToString(); Inspector.Children.Add(b); }
                if (Guid.TryParse((entry["document"] ?? entry["entity"])?.ToString(), out var reference) && Model.Workspace!.State.Trip.Find(reference) is { } linked) { var b = Button("Reference", () => Model.Selected = linked); b.Content = linked.Title; Inspector.Children.Add(b); if (linked.Type == "document" && linked.Data["media_type"]?.ToString().StartsWith("image/", StringComparison.Ordinal) == true) { try { Inspector.Children.Add(new Image { Source = new Bitmap(Model.Workspace.Repository.DocumentPath(linked)), MaxHeight = 180 }); } catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { Inspector.Children.Add(new TextBlock { Text = s["PreviewUnavailable"] }); } } }
            }
        if (e.Type == "document")
        {
            var path = Model.Workspace!.Repository.DocumentPath(e);
            var references = Model.Workspace.State.Trip.All.Where(other => other.Id != e.Id && other.Data.ToJsonString().Contains(e.Id.ToString(), StringComparison.Ordinal)).ToArray();
            var duplicates = Model.Workspace.State.Trip.Entities.Values.Count(other => other.Type == "document" && other.Data["blob"]?["hash"]?.ToString() == e.Data["blob"]?["hash"]?.ToString());
            Inspector.Children.Add(new TextBlock { Text = s["References"] + ": " + references.Length + " · " + s["Documents"] + ": " + duplicates });
            foreach (var related in references) { var relatedButton = Button("Reference", () => Model.Selected = related); relatedButton.Content = related.Title; Inspector.Children.Add(relatedButton); }
            Inspector.Children.Add(AsyncButton("Replace", async () =>
            {
                var file = await Model.Interaction.FileAsync(); if (file is null) return;
                var large = new FileInfo(file).Length > 25 * 1024 * 1024; if (large && !await Model.Interaction.ConfirmAsync(s["LargeFile"])) return;
                await Model.Workspace.ReplaceDocumentAsync(e.Id, file, e.Data["media_type"]!.ToString(), large);
            }));
            if (e.Data["media_type"]?.ToString().StartsWith("image/", StringComparison.Ordinal) == true && File.Exists(path)) { try { Inspector.Children.Add(new Image { Source = new Bitmap(path), MaxHeight = 220 }); } catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { Inspector.Children.Add(new TextBlock { Text = s["Error"] }); } }
            Inspector.Children.Add(AsyncButton("OpenFile", async () => { var target = Path.Combine(Model.Store.Root, "previews", e.Id + Path.GetExtension(e.Title)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(path, target, true); Model.Interaction.Open(target); await Task.CompletedTask; }));
            if (e.Data["media_type"]?.ToString().StartsWith("image/", StringComparison.Ordinal) == true) Inspector.Children.Add(AsyncButton("Cover", async () => { var manifest = Model.Workspace.State.Trip.Manifest.Copy(); manifest.Data["cover"] = new JsonObject { ["document"] = e.Id.ToString() }; await Model.Workspace.EditAsync(manifest); }));
        }
        foreach (var comment in Model.Workspace!.State.Trip.Entities.Values.Where(c => c.Type == "comment" && c.Data["target"]?["id"]?.ToString() == id.ToString()).OrderBy(c => c.Data["created_at"]?.ToString()))
        {
            var open = Button("comment", () => Model.Selected = comment); open.Content = (Guid.TryParse(comment.Data["author"]?.ToString(), out var authorId) ? Model.Workspace.State.Trip.Find(authorId)?.Title : s["Author"]) + " · " + comment.Data["created_at"]; Inspector.Children.Add(open); Inspector.Children.Add(new MarkdownView(comment.Data["body"]?.ToString() ?? ""));
        }
        if (e.Type is not "comment") Inspector.Children.Add(AsyncButton("comment", () => Model.AddAsync("comment")));
        foreach (var d in Model.Diagnostics.Where(d => d.Path.Contains(e.Id.ToString(), StringComparison.Ordinal))) Inspector.Children.Add(new TextBlock { Text = "⚠ " + (Model.Advanced ? d.Message : s.Diagnostic(d.Code)), TextWrapping = TextWrapping.Wrap });
        if (e.Type != "trip") Inspector.Children.Add(Button("Delete", () => Model.DeleteCommand.Execute(null)));
        if (Model.Advanced) Inspector.Children.Add(new SelectableTextBlock { Text = e.Data.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), TextWrapping = TextWrapping.Wrap, FontSize = 11 });
    }
}
