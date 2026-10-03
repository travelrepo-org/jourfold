using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NodaTime;
using NodaTime.Text;
using TravelRepo.Core;

namespace Jourfold.Desktop;

public sealed partial class Dialogs
{
    private Button Action(string key, bool primary = false)
    {
        var button = new Button { Content = strings()[key], IsDefault = primary };
        if (primary) button.Classes.Add("primary");
        ToolTip.SetTip(button, strings()[key]); return button;
    }
    private static StackPanel Buttons(params Control[] buttons) => new StackPanel() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { } }.With(buttons);
    public async Task<IReadOnlyDictionary<string, string>?> FormAsync(string title, IReadOnlyList<FormField> fields)
    {
        var panel = new StackPanel { Spacing = 14 }; var controls = fields.Select(f => new FormControl(f)).ToArray();
        foreach (var field in controls) panel.Children.Add(field);
        var error = new TextBlock { Text = strings()["InvalidFields"], IsVisible = false, TextWrapping = TextWrapping.Wrap };
        var save = Action("Save", true); var cancel = Action("Cancel"); panel.Children.Add(error); panel.Children.Add(Buttons(cancel, save));
        var sheet = Window(title, panel);
        save.Click += (_, _) => { if (controls.Any(c => !c.IsValid)) { error.IsVisible = true; return; } sheet.Close(controls.ToDictionary(c => c.Field.Key, c => c.Value) as IReadOnlyDictionary<string, string>); };
        cancel.Click += (_, _) => sheet.Close(); sheet.Opened += (_, _) => controls.FirstOrDefault()?.Editor.Focus();
        return await sheet.ShowDialog<IReadOnlyDictionary<string, string>>(owner);
    }
    public async Task<IReadOnlyList<string>?> SelectManyAsync(string title, IReadOnlyList<Choice> choices, IReadOnlyList<string> selected)
    {
        var available = new ObservableCollection<Choice>(choices.Where(c => !selected.Contains(c.Id)).OrderBy(c => c.Label));
        var assigned = new ObservableCollection<Choice>(selected.Select(id => choices.FirstOrDefault(c => c.Id == id) ?? new Choice(id, id)));
        var left = new ListBox { ItemsSource = available, MinHeight = 220, SelectionMode = SelectionMode.Multiple, Tag = "available" };
        var right = new ListBox { ItemsSource = assigned, MinHeight = 220, SelectionMode = SelectionMode.Multiple, Tag = "assigned" };
        var add = Action("Assign"); var remove = Action("Unassign");
        void Move(ListBox list, ObservableCollection<Choice> from, ObservableCollection<Choice> to) { foreach (var choice in list.SelectedItems!.Cast<Choice>().ToArray()) { from.Remove(choice); to.Add(choice); } }
        add.Click += (_, _) => Move(left, available, assigned); remove.Click += (_, _) => Move(right, assigned, available);
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), ColumnSpacing = 12 };
        columns.Children.Add(new StackPanel { Spacing = 8, Children = { new TextBlock { Text = strings()["Available"] }, left } });
        var arrows = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { add, remove } }; Grid.SetColumn(arrows, 1); columns.Children.Add(arrows);
        var chosen = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = strings()["SelectedPeople"] }, right } }; Grid.SetColumn(chosen, 2); columns.Children.Add(chosen);
        var save = Action("Save", true); var cancel = Action("Cancel");
        var sheet = Window(title, new StackPanel { Spacing = 18, Children = { columns, Buttons(cancel, save) } }, 800);
        save.Click += (_, _) => sheet.Close(assigned.Select(c => c.Id).ToArray() as IReadOnlyList<string>); cancel.Click += (_, _) => sheet.Close();
        sheet.Opened += (_, _) => left.Focus(); return await sheet.ShowDialog<IReadOnlyList<string>>(owner);
    }
    public async Task<TripDraft?> NewTripAsync()
    {
        var s = strings(); var title = new FormControl(new("title", s["Title"], Required: true));
        var destination = new TextBox { Tag = "destination", IsReadOnly = true, Watermark = s["ChooseLocation"] }; var browse = Action("ChooseLocation");
        browse.Click += async (_, _) => { if (await FolderAsync() is { } folder) destination.Text = folder; };
        var language = new FormControl(new("language", s["Language"], "language", s.Language, Required: true));
        var start = new FormControl(new("start", s["Start"], "date")); var end = new FormControl(new("end", s["End"], "date"));
        var timezone = new FormControl(new("timezone", s["Timezone"], "timezone", DateTimeZoneProviders.Tzdb.GetSystemDefault().Id));
        var people = new ObservableCollection<string>(); var participants = new ListBox { ItemsSource = people, MinHeight = 50, MaxHeight = 140 };
        var add = Action("AddPerson"); var remove = Action("Remove");
        add.Click += async (_, _) => { var result = await FormAsync(s["AddPerson"], [new("name", s["Name"], Required: true)]); if (result is not null) people.Add(result["name"]); };
        remove.Click += (_, _) => { if (participants.SelectedItem is string person) people.Remove(person); };
        var remote = new FormControl(new("remote", s["RemoteUrl"]));
        var optional = new StackPanel { Spacing = 12, Children = { language, start, end, timezone, new TextBlock { Text = s["Participants"] }, participants, Buttons(remove, add), remote } };
        var create = Action("NewTrip", true); var cancel = Action("Cancel"); var error = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var panel = new StackPanel { Spacing = 14, Children = { title, new TextBlock { Text = s["TripFolderHint"], TextWrapping = TextWrapping.Wrap }, destination, browse, new Expander { Header = s["Optional"], Content = optional }, error, Buttons(cancel, create) } };
        var sheet = Window(s["NewTrip"], panel, 640);
        create.Click += async (_, _) =>
        {
            if (!title.IsValid || !language.IsValid || !timezone.IsValid || (start.Value.Length > 0 && end.Value.Length > 0 && string.CompareOrdinal(start.Value, end.Value) > 0)) { error.Text = s["InvalidFields"]; error.IsVisible = true; return; }
            if (string.IsNullOrWhiteSpace(destination.Text)) destination.Text = await FolderAsync();
            if (string.IsNullOrWhiteSpace(destination.Text)) return;
            if (Directory.Exists(destination.Text) && Directory.EnumerateFileSystemEntries(destination.Text).Any()) { error.Text = s["EmptyFolderRequired"]; error.IsVisible = true; return; }
            string? Optional(string value) => value.Length > 0 ? value : null;
            sheet.Close(new TripDraft(title.Value, language.Value, Optional(start.Value), Optional(end.Value), Optional(timezone.Value), people.ToArray(), Optional(remote.Value), destination.Text));
        };
        cancel.Click += (_, _) => sheet.Close(); sheet.Opened += (_, _) => title.Editor.Focus(); return await sheet.ShowDialog<TripDraft>(owner);
    }
    public async Task<JsonObject?> ScheduleAsync(JsonObject? initial, LocalDate date, string zone)
    {
        var s = strings(); var options = new[] { "unscheduled", "exact", "approximate", "window", "day_part", "all_day" }.Select(k => new Choice(k, s[k])).ToArray();
        var precision = new ComboBox { ItemsSource = options, SelectedItem = options.First(c => c.Id == (initial?["precision"]?.ToString() ?? "exact")), HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "precision" };
        var first = initial?["start"] ?? initial?["earliest"]; var last = initial?["end"] ?? initial?["latest"];
        var start = new FormControl(new("start", s["Start"], "datetime", first?["local"]?.ToString() ?? date.ToString("yyyy-MM-dd", null) + "T09:00:00", Required: true));
        var end = new FormControl(new("end", s["End"], "datetime", last?["local"]?.ToString() ?? date.ToString("yyyy-MM-dd", null) + "T10:00:00", Required: true));
        var startZone = new FormControl(new("startZone", s["StartTimezone"], "timezone", first?["timezone"]?.ToString() ?? zone, Required: true));
        var endZone = new FormControl(new("endZone", s["EndTimezone"], "timezone", last?["timezone"]?.ToString() ?? zone, Required: true));
        var day = new FormControl(new("date", s["Date"], "date", initial?["date"]?.ToString() ?? date.ToString("yyyy-MM-dd", null), Required: true));
        var dayZone = new FormControl(new("dayZone", s["Timezone"], "timezone", initial?["timezone"]?.ToString() ?? zone, Required: true));
        var dayPart = new FormControl(new("dayPart", s["DayPart"], "choice", initial?["day_part"]?.ToString() ?? "morning", new[] { "morning", "afternoon", "evening", "night" }.Select(k => new Choice(k, s[k])).ToArray(), true));
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var fields = new[] { start, startZone, end, endZone, day, dayPart, dayZone };
        void Update() { var kind = ((Choice)precision.SelectedItem!).Id; start.IsVisible = startZone.IsVisible = end.IsVisible = endZone.IsVisible = kind is "exact" or "approximate" or "window"; day.IsVisible = kind is "all_day" or "day_part"; dayPart.IsVisible = dayZone.IsVisible = kind == "day_part"; }
        precision.SelectionChanged += (_, _) => Update(); Update();
        var panel = new StackPanel { Spacing = 12, Children = { precision } }; foreach (var field in fields) panel.Children.Add(field);
        var save = Action("Save", true); var cancel = Action("Cancel"); panel.Children.Add(error); panel.Children.Add(Buttons(cancel, save));
        var sheet = Window(s["Schedule"], panel, 620);
        save.Click += async (_, _) =>
        {
            try
            {
                if (fields.Any(f => f.IsVisible && !f.IsValid)) throw new DomainException("form.invalid", s["InvalidFields"]);
                var kind = ((Choice)precision.SelectedItem!).Id; var result = initial?.DeepClone() as JsonObject ?? new JsonObject();
                foreach (var key in new[] { "start", "end", "earliest", "latest", "date", "day_part", "timezone" }) result.Remove(key);
                result["precision"] = kind;
                if (kind is "exact" or "approximate" or "window")
                {
                    var a = await ResolveTimeAsync(start.Value, startZone.Value, first?["offset"]?.ToString()); if (a is null) return;
                    var b = await ResolveTimeAsync(end.Value, endZone.Value, last?["offset"]?.ToString()); if (b is null) return;
                    if (b.ToInstant() <= a.ToInstant()) throw new DomainException("time.range", s["EndAfterStart"]);
                    result[kind == "window" ? "earliest" : "start"] = a.ToJson(); result[kind == "window" ? "latest" : "end"] = b.ToJson();
                }
                if (kind is "day_part" or "all_day") { result["date"] = day.Value; if (kind == "day_part") { result["day_part"] = dayPart.Value; result["timezone"] = dayZone.Value; } }
                sheet.Close(result);
            }
            catch (DomainException ex) { error.Text = ex.Message; error.IsVisible = true; }
        };
        cancel.Click += (_, _) => sheet.Close(); return await sheet.ShowDialog<JsonObject>(owner);
    }
    public async Task<ZonedTime?> ResolveTimeAsync(string local, string zone, string? offset = null)
    {
        var parsed = LocalDateTimePattern.ExtendedIso.Parse(local);
        if (!parsed.Success) throw new DomainException("time.local", strings()["InvalidFields"]);
        var mapped = DateTimeZoneProviders.Tzdb[zone].MapLocal(parsed.Value);
        if (mapped.Count == 0) throw new DomainException("time.gap", strings()["ClockGap"]);
        if (mapped.Count == 2)
        {
            var offsets = new[] { mapped.First().Offset, mapped.Last().Offset }.Select(o => OffsetPattern.GeneralInvariant.Format(o)).ToArray();
            var result = await FormAsync(strings()["ClockRepeated"], [new("offset", strings()["Offset"], "choice", offsets.Contains(offset) ? offset! : "", offsets.Select(o => new Choice(o, "UTC" + o)).ToArray(), true)]);
            if (result is null) return null; offset = result["offset"];
        }
        else offset = null;
        return new ZonedTime(local, zone, offset);
    }
}
internal static class PanelExtensions
{
    public static T With<T>(this T panel, params Control[] controls) where T : Panel { foreach (var control in controls) panel.Children.Add(control); return panel; }
}
