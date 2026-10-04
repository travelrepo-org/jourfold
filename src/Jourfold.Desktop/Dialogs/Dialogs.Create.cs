using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Jourfold.Application;
using NodaTime;
using TravelRepo.Core;

namespace Jourfold.Desktop;

public sealed partial class Dialogs
{
    /// <summary>Minimal, skippable new-trip wizard: only a title is required.</summary>
    public async Task<TripDraft?> NewTripAsync(NewTripDefaults defaults)
    {
        var s = S; var step = 0;
        var title = new TextBox { Watermark = s["TripTitleExample"], FontSize = 16, MinHeight = 42, Tag = "title" }; AutomationProperties.SetName(title, s["TripTitle"]);
        var start = new CalendarDatePicker { HorizontalAlignment = HorizontalAlignment.Stretch, Watermark = s["OptionalShort"], Tag = "start" }; AutomationProperties.SetName(start, s["Start"]);
        var end = new CalendarDatePicker { HorizontalAlignment = HorizontalAlignment.Stretch, Watermark = s["OptionalShort"], Tag = "end" }; AutomationProperties.SetName(end, s["End"]);
        start.SelectedDateChanged += (_, _) => { if (start.SelectedDate is { } d && (end.SelectedDate is null || end.SelectedDate < d)) end.SelectedDate = d.AddDays(3); };
        var timezone = new SearchChoice(FieldOptions.Timezones, defaults.Timezone, s["Timezone"]);
        var language = new SearchChoice(FieldOptions.Languages, defaults.Language, s["Language"]);
        var me = new TextBox { Text = defaults.MyName ?? "", Watermark = s["YourName"], Tag = "me" }; AutomationProperties.SetName(me, s["YourName"]);
        var people = new ObservableCollection<string>(); var peopleList = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        var person = new TextBox { Watermark = s["CompanionName"], Tag = "person" }; AutomationProperties.SetName(person, s["CompanionName"]);
        var addPerson = Ui.Button(s["AddPerson"], "user-plus");
        void FillPeople()
        {
            peopleList.Children.Clear();
            foreach (var name in people)
            {
                var remove = Ui.IconButton("x", string.Format(s["RemoveNamed"], name), "small"); remove.Click += (_, _) => { people.Remove(name); FillPeople(); };
                peopleList.Children.Add(new Border { Child = Ui.H(6, Ui.Avatar(name, 22), Ui.Text(name), remove), Padding = new Thickness(4, 2, 2, 2), CornerRadius = new CornerRadius(14) }.Res(Border.BackgroundProperty, "Bg.Subtle"));
            }
        }
        void AddCompanion() { var name = person.Text?.Trim(); if (string.IsNullOrEmpty(name)) return; people.Add(name); person.Text = ""; FillPeople(); person.Focus(); }
        addPerson.Click += (_, _) => AddCompanion();
        person.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; AddCompanion(); } };
        var folder = new TextBlock { TextWrapping = TextWrapping.Wrap }.Classed("muted"); string? chosenFolder = null;
        void UpdateFolder() => folder.Text = chosenFolder ?? Path.Combine(defaults.Folder, MainViewModel.FolderName(string.IsNullOrWhiteSpace(title.Text) ? s["NewTrip"] : title.Text));
        title.TextChanged += (_, _) => UpdateFolder(); UpdateFolder();
        var change = Ui.Button(s["ChangeFolder"], "folder-open", "ghost", "compact");
        change.Click += async (_, _) => { if (await FolderAsync(s["ChooseLocation"]) is { } picked) { chosenFolder = Directory.Exists(picked) && Directory.EnumerateFileSystemEntries(picked).Any() ? Path.Combine(picked, MainViewModel.FolderName(title.Text ?? s["NewTrip"])) : picked; UpdateFolder(); } };
        var remote = new TextBox { Watermark = s["RemoteUrlOptional"], Tag = "remote" }; AutomationProperties.SetName(remote, s["RemoteUrl"]);
        // Sharing: keep the trip local (default), publish it to a new private GitHub repository, or connect an existing one.
        var share = "local"; var suggested = MainViewModel.RepositoryName(s["NewTrip"]);
        var repository = new TextBox { Text = suggested, Tag = "repository" }; AutomationProperties.SetName(repository, s["RepositoryName"]);
        // The repository name follows the title until the person changes it.
        title.TextChanged += (_, _) =>
        {
            var next = MainViewModel.RepositoryName(string.IsNullOrWhiteSpace(title.Text) ? s["NewTrip"] : title.Text);
            if (repository.Text == suggested) repository.Text = next;
            suggested = next;
        };
        RadioButton ShareOption(string mode, string heading, string hint, Control? details)
        {
            if (details is not null) details.IsVisible = mode == share;
            var option = new RadioButton
            {
                GroupName = "new-trip-share",
                IsChecked = mode == share,
                Tag = "share-" + mode,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = Ui.V(3, Ui.Text(heading, "strong"), Ui.Text(hint, "caption").Also(t => t.TextWrapping = TextWrapping.Wrap), details?.Margin(0, 6, 0, 0))
            };
            AutomationProperties.SetName(option, heading);
            option.IsCheckedChanged += (_, _) => { if (option.IsChecked == true) share = mode; if (details is not null) details.IsVisible = option.IsChecked == true; };
            return option;
        }
        var shareOptions = Ui.V(10,
            ShareOption("local", s["KeepLocal"], s["KeepLocalHint"], null),
            ShareOption("github", s["PublishToGitHub"], defaults.GitHubConnected && defaults.GitHubLogin is { } login ? string.Format(s["PublishToGitHubAs"], login) : s["PublishToGitHubHint"], Ui.V(4, Ui.Text(s["RepositoryName"], "label"), repository)),
            ShareOption("git", s["ConnectExistingRepository"], s["UseOwnGitHint"], Ui.V(4, remote, Ui.Text(s["RemoteUrlHint"], "caption"))));

        var pages = new Control[]
        {
            Ui.V(14, Ui.V(6, Ui.Text(s["TripTitle"], "label"), title), Ui.Columns("*,*", Ui.V(6, Ui.Text(s["Start"], "label"), start), Ui.V(6, Ui.Text(s["End"], "label"), end).Margin(10, 0, 0, 0)), Ui.Text(s["DatesHint"], "caption"),
                Ui.Columns("*,*", Ui.V(6, Ui.Text(s["MainTimezone"], "label"), timezone), Ui.V(6, Ui.Text(s["ContentLanguage"], "label"), language).Margin(10, 0, 0, 0))),
            Ui.V(14, Ui.V(6, Ui.Text(s["YourName"], "label"), me, Ui.Text(s["YourNameHint"], "caption")), Ui.V(6, Ui.Text(s["TravellingWith"], "label"), Ui.Columns("*,Auto", person, addPerson.Margin(8, 0, 0, 0)), peopleList)),
            Ui.V(14, Ui.V(6, Ui.Text(s["SavedAt"], "label"), Ui.Card(Ui.Columns("Auto,*,Auto", Ui.Icon("folder", 18, "Accent"), folder.Margin(10, 0), change), 12), Ui.Text(s["SavedAtHint"], "caption")),
                Ui.V(8, Ui.Text(s["ShareChoice"], "label"), shareOptions))
        };
        var stepTitles = new[] { s["WizardTrip"], s["WizardPeople"], s["WizardSave"] };
        var host = new ContentControl(); var stepper = Ui.H(8);
        var cancelWizard = Action("Cancel"); var back = Action("Back"); var next = Action("Next", true); var create = Ui.Button(s["CreateTrip"], "check", "primary"); var skip = Ui.Button(s["CreateNow"], null, "ghost");
        var error = Ui.Text("").Res(TextBlock.ForegroundProperty, "Danger"); error.IsVisible = false;
        void Show()
        {
            host.Content = pages[step]; stepper.Children.Clear();
            for (var i = 0; i < stepTitles.Length; i++)
            {
                var active = i == step; var done = i < step;
                var circle = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Child = done ? Ui.Icon("check", 13, "Text.OnAccent").Also(x => x.HorizontalAlignment = HorizontalAlignment.Center) : Ui.Text((i + 1).ToString(CultureInfo.CurrentCulture)).Also(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.FontWeight = FontWeight.Bold; t.FontSize = 12; t.Res(TextBlock.ForegroundProperty, active ? "Text.OnAccent" : "Text.Muted"); }) }
                    .Res(Border.BackgroundProperty, active || done ? "Accent" : "Bg.Muted");
                stepper.Children.Add(Ui.H(8, circle, Ui.Text(stepTitles[i], active ? "title" : "muted")));
                if (i < stepTitles.Length - 1) stepper.Children.Add(new Border { Width = 28, Height = 2, VerticalAlignment = VerticalAlignment.Center }.Res(Border.BackgroundProperty, "Line"));
            }
            back.IsVisible = step > 0; next.IsVisible = step < pages.Length - 1; next.IsDefault = next.IsVisible; create.IsDefault = !next.IsVisible; create.IsVisible = step == pages.Length - 1; skip.IsVisible = step < pages.Length - 1;
        }
        bool Valid() { if (string.IsNullOrWhiteSpace(title.Text)) { error.Text = s["TitleRequired"]; error.IsVisible = true; step = 0; Show(); title.Focus(); return false; } error.IsVisible = false; return true; }
        var sheet = new DialogSheet(s["NewTrip"], Ui.V(18, stepper, host, error, Ui.Columns("Auto,*,Auto", skip, null, Ui.H(8, cancelWizard, back, next, create))), 640, s["NewTripSubtitle"]);
        TripDraft Draft() => new(title.Text!.Trim(), language.Value ?? defaults.Language, start.SelectedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), end.SelectedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), timezone.Value, people.ToArray(), share != "git" || string.IsNullOrWhiteSpace(remote.Text) ? null : remote.Text.Trim(), chosenFolder ?? folder.Text, string.IsNullOrWhiteSpace(me.Text) ? null : me.Text.Trim(),
            share == "github" ? (string.IsNullOrWhiteSpace(repository.Text) ? MainViewModel.RepositoryName(title.Text!) : repository.Text.Trim()) : null);
        next.Click += (_, _) => { if (step == 0 && !Valid()) return; step++; Show(); };
        cancelWizard.Click += (_, _) => sheet.Close();
        back.Click += (_, _) => { step--; Show(); };
        create.Click += (_, _) => { if (Valid()) sheet.Close(Draft()); };
        skip.Click += (_, _) => { if (Valid()) sheet.Close(Draft()); };
        Show(); sheet.Opened += (_, _) => title.Focus();
        return await sheet.ShowDialog<TripDraft>(owner);
    }

    private static readonly (string Kind, string Icon, string Color)[] QuickKinds =
    [
        ("activity", "star", "Activity"), ("food", "utensils", "Food"), ("sightseeing", "camera", "Sightseeing"), ("transport", "plane", "Transport"), ("accommodation", "bed-double", "Accommodation"),
        ("booking", "ticket", "Transport"), ("task", "square-check", "Activity"), ("note", "sticky-note", "Nightlife"), ("place", "map-pin", "Sightseeing"), ("person", "user", "Accommodation"),
        ("expense", "receipt", "Food"), ("collection", "layers", "Culture")
    ];

    /// <summary>Quick Add: one sheet with a type picker and only the fields that type needs.</summary>
    public async Task<QuickAddDraft?> QuickAddAsync(QuickAddRequest request)
    {
        var s = S; var kind = QuickKinds.Any(k => k.Kind == request.Kind) ? request.Kind : request.Kind == "budget" ? "budget" : "activity";
        var trip = request.Trip;
        var title = new TextBox { MinHeight = 40, FontSize = 15, Tag = "title" }; AutomationProperties.SetName(title, s["Title"]);
        var date = new CalendarDatePicker { SelectedDate = request.Date.ToDateTimeUnspecified(), HorizontalAlignment = HorizontalAlignment.Stretch }; AutomationProperties.SetName(date, s["Date"]);
        var hasTime = new CheckBox { Content = s["AtATime"], IsChecked = true };
        var time = new TimePicker { Tag = "startTime", ClockIdentifier = "24HourClock", MinuteIncrement = 5, SelectedTime = (request.Start ?? new LocalTime(10, 0)).ToTimeOnly().ToTimeSpan(), HorizontalAlignment = HorizontalAlignment.Stretch }; AutomationProperties.SetName(time, s["StartTime"]);
        var length = new NumericUpDown { Tag = "length", Value = (decimal)(request.Length?.TotalMinutes ?? 60), Minimum = 5, Increment = 15, FormatString = "0", HorizontalAlignment = HorizontalAlignment.Stretch }; AutomationProperties.SetName(length, s["DurationMinutes"]);
        var unscheduled = new CheckBox { Content = s["JustAnIdea"], IsChecked = false };
        var mode = "flight"; var modes = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        void FillModes() { modes.Children.Clear(); foreach (var t in ScheduleCategories.TransportTypes) { var chip = Ui.Button(s[t], Visuals.TransportIcon(t), "chip"); if (t == mode) chip.Classes.Add("selected"); chip.Click += (_, _) => { mode = t; FillModes(); }; modes.Children.Add(chip); } }
        FillModes();
        var place = PlaceBox(trip, s["Place"]); var from = PlaceBox(trip, s["From"]); var to = PlaceBox(trip, s["To"]);
        var nights = new NumericUpDown { Value = 1, Minimum = 1, Maximum = 60, Increment = 1, FormatString = "0", HorizontalAlignment = HorizontalAlignment.Stretch }; AutomationProperties.SetName(nights, s["Nights"]);
        var amount = new NumericUpDown { Value = null, FormatString = "N2", Increment = 1, ShowButtonSpinner = false, HorizontalAlignment = HorizontalAlignment.Stretch, Watermark = "0.00" }; AutomationProperties.SetName(amount, s["Amount"]);
        var currency = new SearchChoice(Editors.Currencies, request.Currency, s["Currency"]) { Width = 130 };
        var body = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100, Watermark = s["NoteWatermark"] }; AutomationProperties.SetName(body, s["Body"]);
        var reference = new TextBox { Watermark = s["BookingReferenceHint"] }; AutomationProperties.SetName(reference, s["Reference"]);
        var peopleIds = request.Person is { } p ? new HashSet<Guid> { p } : new HashSet<Guid>();
        var people = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        foreach (var person in trip.Entities.Values.Where(e => e.Type == "person").OrderBy(e => e.Title))
        {
            var chip = Ui.Button(person.Title, null, "chip"); chip.Content = Ui.H(6, Ui.Avatar(person.Title, 18), Ui.Text(person.Title)); if (peopleIds.Contains(person.Id)) chip.Classes.Add("selected");
            chip.Click += (_, _) => { if (!peopleIds.Remove(person.Id)) peopleIds.Add(person.Id); chip.Classes.Set("selected", peopleIds.Contains(person.Id)); };
            people.Children.Add(chip);
        }

        // Layout() rebuilds the rows whenever the type or a toggle changes. The editors themselves are kept, so typed text
        // survives; each one is detached from its old row before it is placed again.
        Control[] editors = [title, date, hasTime, time, length, unscheduled, modes, place, from, to, nights, amount, currency, body, reference, people];
        Control Row(string label, Control editor) => Ui.V(6, Ui.Text(label, "label"), editor);
        Control WhenRow() => Ui.V(8, Ui.Columns("*,Auto", Row(s["Date"], date), unscheduled.Margin(12, 22, 0, 0)), hasTime, Ui.Columns("*,*", Row(s["StartTime"], time), Row(s["DurationMinutes"], length).Margin(10, 0, 0, 0)));
        var fields = Ui.V(14);
        var typeGrid = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        var dateLabel = Ui.Text(s["Date"], "label");
        void Layout()
        {
            fields.Children.Clear(); foreach (var editor in editors) Ui.Detach(editor);
            title.Watermark = s["placeholder." + kind];
            fields.Children.Add(Row(kind is "person" ? s["Name"] : s["Title"], title));
            var scheduled = kind is "activity" or "food" or "sightseeing" or "transport";
            if (kind == "transport") { fields.Children.Add(Row(s["TransportMode"], modes)); fields.Children.Add(Ui.Columns("*,Auto,*", Row(s["From"], from), Ui.Icon("arrow-right", 16, "Text.Subtle").Margin(8, 22, 8, 0), Row(s["To"], to))); }
            if (kind is "activity" or "food" or "sightseeing") fields.Children.Add(Row(s["Where"], place));
            if (scheduled) { fields.Children.Add(WhenRow()); time.IsEnabled = length.IsEnabled = hasTime.IsChecked == true && unscheduled.IsChecked != true; date.IsEnabled = hasTime.IsEnabled = unscheduled.IsChecked != true; }
            if (kind == "accommodation") { fields.Children.Add(Row(s["Place"], place)); fields.Children.Add(Ui.Columns("*,*", Row(s["CheckInDate"], date), Row(s["Nights"], nights).Margin(10, 0, 0, 0))); }
            if (kind is "booking") { fields.Children.Add(Row(s["Reference"], reference)); fields.Children.Add(Row(s["Amount"], Ui.Columns("*,Auto", amount, currency.Margin(8, 0, 0, 0)))); }
            if (kind is "expense" or "budget") fields.Children.Add(Row(s["Amount"], Ui.Columns("*,Auto", amount, currency.Margin(8, 0, 0, 0))));
            if (kind == "task") fields.Children.Add(Row(s["Due"], date));
            if (kind == "note") fields.Children.Add(Row(s["Body"], body));
            if (kind is "activity" or "food" or "sightseeing" or "transport" or "task" or "expense" or "booking" && people.Children.Count > 0) fields.Children.Add(Row(kind == "expense" ? s["Payer"] : kind == "task" ? s["Assignees"] : s["Who"], people));
            typeGrid.Children.Clear();
            foreach (var (k, icon, color) in QuickKinds)
            {
                var tile = new Button { Width = 96, Height = 74, Content = Ui.V(6, Ui.Tile(icon, color, 30).Also(t => t.HorizontalAlignment = HorizontalAlignment.Center), Ui.Text(s["quick." + k]).Also(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.FontSize = 12; t.TextAlignment = TextAlignment.Center; })) }.Classed("tile").Named(s["quick." + k]);
                if (k == kind) tile.Classes.Add("selected");
                tile.Click += (_, _) => { kind = k; Layout(); title.Focus(); };
                typeGrid.Children.Add(tile);
            }
        }
        hasTime.IsCheckedChanged += (_, _) => Layout(); unscheduled.IsCheckedChanged += (_, _) => Layout();
        Layout();
        var error = Ui.Text("").Res(TextBlock.ForegroundProperty, "Danger"); error.IsVisible = false;
        var save = Ui.Button(s["Add"], "plus", "primary"); save.IsDefault = true; var cancel = Action("Cancel");
        var content = Ui.V(18, request.ChooseType ? typeGrid : null, fields, error, Footer(cancel, save));
        var sheet = new DialogSheet(request.ChooseType ? s["AddToTrip"] : s["quick." + kind], content, request.ChooseType ? 660 : 560);
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(title.Text)) { error.Text = s["TitleRequired"]; error.IsVisible = true; title.Focus(); return; }
            var scheduled = kind is "activity" or "food" or "sightseeing" or "transport";
            var idea = scheduled && unscheduled.IsChecked == true;
            LocalDate? day = date.SelectedDate is { } d && !idea ? LocalDate.FromDateTime(d) : null;
            LocalTime? at = scheduled && !idea && hasTime.IsChecked == true && time.SelectedTime is { } t ? LocalTime.FromTicksSinceMidnight(t.Ticks) : null;
            var draft = new QuickAddDraft(kind, title.Text!.Trim())
            {
                Date = scheduled || kind is "accommodation" or "task" ? day : null,
                Start = at,
                Length = scheduled && length.Value is { } l ? Duration.FromMinutes((double)l) : null,
                Timezone = request.Zone,
                TransportType = mode,
                Place = place.Choice,
                From = from.Choice,
                To = to.Choice,
                Nights = (int)(nights.Value ?? 1),
                Amount = amount.Value,
                Currency = currency.Value ?? request.Currency,
                Body = body.Text,
                Reference = reference.Text,
                People = peopleIds.ToArray()
            };
            sheet.Close(draft);
        };
        cancel.Click += (_, _) => sheet.Close();
        sheet.Opened += (_, _) => title.Focus();
        return await sheet.ShowDialog<QuickAddDraft>(owner);
    }

    /// <summary>Type a new place name or pick an existing place.</summary>
    private PlacePicker PlaceBox(TripSnapshot trip, string label) => new(trip, label, S);

    private sealed class PlacePicker : UserControl
    {
        private readonly Dictionary<string, Guid> places;
        private readonly AutoCompleteBox box;
        public PlacePicker(TripSnapshot trip, string label, Localization s)
        {
            places = trip.Entities.Values.Where(e => e.Type == "place").GroupBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.CurrentCultureIgnoreCase);
            box = new AutoCompleteBox { ItemsSource = places.Keys.Order().ToArray(), FilterMode = AutoCompleteFilterMode.Contains, MinimumPrefixLength = 0, Watermark = s["PlaceWatermark"], HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(box, label); Content = box;
        }
        public PlaceChoice? Choice => string.IsNullOrWhiteSpace(box.Text) ? null : places.TryGetValue(box.Text.Trim(), out var id) ? PlaceChoice.Existing(id) : PlaceChoice.Create(box.Text);
    }
}
