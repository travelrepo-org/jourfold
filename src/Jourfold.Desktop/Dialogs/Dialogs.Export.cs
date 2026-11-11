using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NodaTime;
using TravelRepo.Repository;

namespace Jourfold.Desktop;

public sealed partial class Dialogs
{
    /// <summary>
    /// The export dialog: the trip plan (PDF, web page, calendar, text) with its parts, days, person, detail, paper and
    /// language, or packing lists (PDF, text, CSV) for everybody, one file per chosen person, or what nobody brings yet.
    /// "Copy as text" puts the same content on the clipboard instead of writing a file.
    /// </summary>
    public async Task<ExportChoice?> ExportAsync(ExportSetup setup)
    {
        var s = S; var initial = setup.Initial;
        var kind = initial.Kind; var format = initial.Format == "copy" ? "pdf" : initial.Format;
        var zone = initial.Options.TimeZone; var otherZone = zone is not null; var alsoIn = initial.Options.OtherTimeZones.ToList();
        var parts = initial.Options.Parts; var detail = initial.Options.Detail; var paper = initial.Options.Paper; var person = initial.Options.Person; var timeline = initial.Options.Timeline; var comments = initial.Options.Comments;
        LocalDate? from = initial.Options.From ?? setup.Start, to = initial.Options.To ?? setup.End;
        var language = initial.Language;
        var lists = initial.Lists.ToHashSet(); var people = initial.People.ToHashSet();
        var packFor = initial.UnassignedOnly ? "unassigned" : initial.People.Count > 0 ? "people" : "all";

        var options = Ui.V(12); var formats = new ContentControl(); var kinds = new ContentControl();
        var hint = Ui.Text("", "caption").Also(t => t.TextWrapping = TextWrapping.Wrap);
        var export = Ui.Button(s["ExportAction"], "download", "primary"); export.IsDefault = true;
        var copy = Ui.Button(s["ExportCopyText"], "copy"); var cancel = Action("Cancel");

        static Control Row(string label, Control control) => Ui.Columns("130,*", Ui.Text(label, "label").Also(t => { t.VerticalAlignment = VerticalAlignment.Top; t.Margin = new Thickness(0, 9, 12, 0); }), control);
        CheckBox Check(string label, bool value, Action<bool> changed)
        {
            var box = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 0, 14, 0) }; AutomationProperties.SetName(box, label);
            box.IsCheckedChanged += (_, _) => { changed(box.IsChecked == true); Update(); };
            return box;
        }
        ComboBox Combo(string name, IReadOnlyList<Choice> choices, string selected, Action<string> changed)
        {
            var combo = new ComboBox { ItemsSource = choices, SelectedItem = choices.FirstOrDefault(c => c.Id == selected) ?? choices[0], MinWidth = 220 }; AutomationProperties.SetName(combo, name);
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is Choice c) { changed(c.Id); Update(); } };
            return combo;
        }
        CalendarDatePicker Date(string name, LocalDate? value, Action<LocalDate?> changed)
        {
            var picker = new CalendarDatePicker { SelectedDate = value?.ToDateTimeUnspecified(), HorizontalAlignment = HorizontalAlignment.Stretch }; AutomationProperties.SetName(picker, name);
            picker.SelectedDateChanged += (_, _) => { changed(picker.SelectedDate is { } d ? LocalDate.FromDateTime(d) : null); Update(); };
            return picker;
        }
        Control Language() => Row(s["ExportLanguage"], Combo(s["ExportLanguage"], MainViewModel.ExportLanguages.Select(l => new Choice(l, s[l])).ToArray(), language, v => language = v));
        static string City(string id) => DateTimeZoneProviders.Tzdb.GetZoneOrNull(id) is { } z ? ExportTimes.City(z) : id;
        // Times in the trip's zone, or all of them in one chosen zone, for example to read the plan at home.
        Control Zone()
        {
            var choices = ViewToolbar.Segmented([("trip", string.Format(s["ExportTripZone"], City(setup.TripZone)), null), ("other", s["ExportOtherZone"], null)], otherZone ? "other" : "trip", v =>
            {
                otherZone = v == "other";
                if (otherZone && zone is null) zone = setup.Zones.FirstOrDefault() ?? DateTimeZoneProviders.Tzdb.GetSystemDefault().Id;
                Render();
            });
            if (!otherZone) return Row(s["ExportTimesIn"], choices);
            var search = new SearchChoice(FieldOptions.Timezones, zone, s["Timezone"]);
            // A chosen zone changes what "Also in" offers, so that row is drawn again once a zone is picked, not on every keystroke.
            void Chosen(bool picked) { var before = zone; zone = search.Value; if (picked && zone is not null && zone != before) Render(); else Update(); }
            search.Input.SelectionChanged += (_, _) => Chosen(true); search.Input.TextChanged += (_, _) => Chosen(false);
            var quick = Ui.Flow(4, setup.Zones.Where(z => z != setup.TripZone).Take(4).Select(z =>
            {
                var button = Ui.Button(City(z), null, "compact"); AutomationProperties.SetName(button, FieldOptions.ZoneLabel(z));
                button.Click += (_, _) => { zone = z; Render(); };
                return (Control)button;
            }).ToArray());
            return Row(s["ExportTimesIn"], Ui.V(6, choices, search, quick, Ui.Text(s["ExportZoneHint"], "caption").Also(t => t.TextWrapping = TextWrapping.Wrap)));
        }
        // More zones shown beside each time, for example home and the next stop.
        Control AlsoIn()
        {
            var main = otherZone && zone is not null ? zone : setup.TripZone;
            // The main zone is never shown beside itself.
            alsoIn.RemoveAll(z => z == main);
            var chosen = Ui.Flow(4, alsoIn.Select(z =>
            {
                var remove = Ui.Button(City(z), "x", "compact"); AutomationProperties.SetName(remove, string.Format(s["ExportRemoveZone"], FieldOptions.ZoneLabel(z)));
                remove.Click += (_, _) => { alsoIn.Remove(z); Render(); };
                return (Control)remove;
            }).ToArray());
            // Named after the row, so screen readers can tell it from the search under "Times in".
            var search = new SearchChoice(FieldOptions.Timezones, null, s["ExportAlsoIn"]); search.Input.PlaceholderText = s["Timezone"];
            var add = Ui.Button(s["ExportAddZone"], "plus", "compact"); add.IsEnabled = false;
            void Add(string z) { if (z != main && !alsoIn.Contains(z)) alsoIn.Add(z); Render(); }
            void Typed() => add.IsEnabled = search.Value is { } z && z != main && !alsoIn.Contains(z);
            search.Input.SelectionChanged += (_, _) => Typed(); search.Input.TextChanged += (_, _) => Typed();
            add.Click += (_, _) => { if (search.Value is { } z) Add(z); };
            var quick = Ui.Flow(4, setup.Zones.Prepend(setup.TripZone).Distinct().Where(z => z != main && !alsoIn.Contains(z)).Take(4).Select(z =>
            {
                var button = Ui.Button(City(z), "plus", "compact"); AutomationProperties.SetName(button, string.Format(s["ExportAddThisZone"], FieldOptions.ZoneLabel(z)));
                button.Click += (_, _) => Add(z);
                return (Control)button;
            }).ToArray());
            return Row(s["ExportAlsoIn"], Ui.V(6, alsoIn.Count > 0 ? chosen : null, Ui.Columns("*,Auto", search, add.Col(1).Margin(6, 0, 0, 0)), quick,
                Ui.Text(s["ExportAlsoHint"], "caption").Also(t => t.TextWrapping = TextWrapping.Wrap)));
        }
        Control Paper() => Row(s["ExportPaper"], ViewToolbar.Segmented([("a4", "A4", null), ("letter", s["ExportLetter"], null)], paper == PaperSize.Letter ? "letter" : "a4", v => { paper = v == "letter" ? PaperSize.Letter : PaperSize.A4; Render(); }));

        void Render()
        {
            kinds.Content = setup.Lists.Count == 0 ? null : ViewToolbar.Segmented([("plan", s["ExportPlan"], "calendar-days"), ("packing", s["ExportPackingLists"], "backpack")], kind, v => { kind = v; format = "pdf"; Render(); });
            var formatChoices = kind == "plan"
                ? new (string, string, string?)[] { ("pdf", s["PDF"], "file-text"), ("html", s["ExportWebPage"], "globe"), ("ics", s["ICS"], "calendar"), ("txt", s["ExportTextFile"], "list") }
                : [("pdf", s["PDF"], "file-text"), ("txt", s["ExportTextFile"], "list-checks"), ("csv", s["ExportCsv"], "layout-grid")];
            formats.Content = Row(s["ExportFormatLabel"], ViewToolbar.Segmented(formatChoices, format, v => { format = v; Render(); }));
            options.Children.Clear();
            if (kind == "plan")
            {
                if (setup.People.Count > 0)
                    options.Children.Add(Row(s["ExportFor"], Combo(s["ExportFor"], new[] { new Choice("", s["Everyone"]) }.Concat(setup.People.Select(p => new Choice(p.Id.ToString(), p.Id == setup.Me?.Id ? string.Format(s["ExportMe"], p.Title) : p.Title))).ToArray(), person?.ToString() ?? "", v => person = Guid.TryParse(v, out var id) ? id : null)));
                var whole = Ui.Button(s["ExportWholeTrip"], null, "compact"); whole.IsVisible = setup.Start is not null;
                whole.Click += (_, _) => { from = setup.Start; to = setup.End; Render(); };
                options.Children.Add(Row(s["ExportDays"], Ui.Columns("*,Auto,*,Auto", Date(s["ExportFrom"], from, v => from = v), Ui.Text("–", "muted").Margin(8, 0), Date(s["ExportTo"], to, v => to = v).Col(2), whole.Col(3).Margin(8, 0, 0, 0))));
                if (format != "ics")
                {
                    var contents = Ui.Flow(4);
                    foreach (var (part, key) in new[] { (ExportParts.Schedule, "Schedule"), (ExportParts.Stays, "ExportStays"), (ExportParts.Bookings, "Bookings"), (ExportParts.Contacts, "ExportContacts"), (ExportParts.Costs, "Costs"), (ExportParts.Tasks, "Tasks"), (ExportParts.Notes, "Notes"), (ExportParts.Packing, "ExportPackingLists") })
                        if (part != ExportParts.Packing || setup.Lists.Count > 0) contents.Children.Add(Check(s[key], (parts & part) == part, on => parts = on ? parts | part : parts & ~part));
                    options.Children.Add(Row(s["ExportContents"], contents));
                    var extras = Ui.Flow(4);
                    if (format == "pdf")
                        extras.Children.AddRange([Check(s["ExportTimeline"], timeline, on => timeline = on), Check(s["ExportMap"], (parts & ExportParts.Map) != 0, on => parts = on ? parts | ExportParts.Map : parts & ~ExportParts.Map),
                            Check(s["ExportAttachments"], (parts & ExportParts.Attachments) != 0, on => parts = on ? parts | ExportParts.Attachments : parts & ~ExportParts.Attachments)]);
                    extras.Children.Add(Check(s["ExportComments"], comments, on => comments = on));
                    options.Children.Add(Row("", extras));
                    options.Children.Add(Zone());
                    options.Children.Add(AlsoIn());
                    options.Children.Add(Row(s["ExportDetail"], ViewToolbar.Segmented([("detailed", s["ExportDetailed"], null), ("compact", s["Compact"], null)], detail == ExportDetail.Compact ? "compact" : "detailed", v => { detail = v == "compact" ? ExportDetail.Compact : ExportDetail.Detailed; Render(); })));
                    if (format == "pdf") options.Children.Add(Paper());
                    options.Children.Add(Language());
                }
            }
            else
            {
                if (setup.Lists.Count > 1)
                    options.Children.Add(Row(s["ExportLists"], Ui.Flow(4, setup.Lists.Select(l => (Control)Check(l.Collection.Title, lists.Contains(l.Collection.Id), on => { if (on) lists.Add(l.Collection.Id); else lists.Remove(l.Collection.Id); })).ToArray())));
                var who = Ui.V(4);
                RadioButton Radio(string id, string label, string? caption = null)
                {
                    var radio = new RadioButton { GroupName = "export-for", IsChecked = packFor == id, Content = Ui.V(1, Ui.Text(label), caption is null ? null : Ui.Text(caption, "caption")) }; AutomationProperties.SetName(radio, label);
                    radio.IsCheckedChanged += (_, _) => { if (radio.IsChecked == true && packFor != id) { packFor = id; Render(); } };
                    return radio;
                }
                who.Children.Add(Radio("all", s["ExportForEveryone"]));
                if (setup.People.Count > 0)
                {
                    who.Children.Add(Radio("people", s["ExportForPeople"], s["ExportForPeopleHint"]));
                    if (packFor == "people")
                        who.Children.Add(Ui.Flow(4, setup.People.Select(p => (Control)Check(p.Id == setup.Me?.Id ? string.Format(s["ExportMe"], p.Title) : p.Title, people.Contains(p.Id), on => { if (on) people.Add(p.Id); else people.Remove(p.Id); })).ToArray()).Margin(26, 2, 0, 4));
                }
                who.Children.Add(Radio("unassigned", s["ExportForUnassigned"]));
                options.Children.Add(Row(s["ExportFor"], who));
                // A spreadsheet has one row per item, so comments go only into PDF and text files.
                if (format != "csv") options.Children.Add(Row("", Check(s["ExportComments"], comments, on => comments = on)));
                if (format == "pdf") options.Children.Add(Paper());
                options.Children.Add(Language());
            }
            Update();
        }

        void Update()
        {
            var chosenPeople = packFor == "people" ? people.Count : 0;
            export.IsEnabled = kind == "plan"
                ? (format == "ics" || (parts & ~(ExportParts.Map | ExportParts.Attachments)) != ExportParts.None) && (from is null || to is null || from <= to) && (format == "ics" || !otherZone || zone is not null)
                : lists.Count > 0 && (packFor != "people" || chosenPeople > 0);
            copy.IsVisible = kind == "plan" ? format != "ics" : chosenPeople <= 1;
            copy.IsEnabled = export.IsEnabled;
            export.Content = Ui.H(6, Ui.Icon("download", 15), Ui.Text(kind == "packing" && chosenPeople > 1 ? string.Format(s["ExportFiles"], chosenPeople) : s["ExportAction"]));
            hint.Text = kind == "plan" && format == "pdf" && (parts & ExportParts.Map) != 0 && !setup.OnlineMaps ? s["ExportMapOffline"]
                : format switch { "pdf" => s[kind == "packing" ? "ExportPackingPdfHint" : "PDFHint"], "html" => s["HTMLHint"], "ics" => s["ICSHint"], "csv" => s["ExportCsvHint"], _ => s["ExportTextHint"] };
        }

        ExportChoice Result(string chosenFormat)
        {
            // Days that match the trip's own dates are left open, so later changes to the trip are included.
            var options = new ExportOptions
            {
                Parts = format == "pdf" ? parts : parts & ~(ExportParts.Map | ExportParts.Attachments),
                From = from == setup.Start ? null : from,
                To = to == setup.End ? null : to,
                Person = person,
                Detail = detail,
                Paper = paper,
                Timeline = timeline,
                Comments = comments && format != "ics" && (kind == "plan" || format != "csv"),
                // Calendar files keep exact times, which calendar apps show in their own zone.
                TimeZone = kind == "plan" && format != "ics" && otherZone ? zone : null,
                OtherTimeZones = kind == "plan" && format != "ics" ? [.. alsoIn.Where(z => z != (otherZone ? zone : setup.TripZone))] : []
            };
            return new(kind, chosenFormat, options, language, setup.Lists.Where(l => lists.Contains(l.Collection.Id)).Select(l => l.Collection.Id).ToArray(),
                packFor == "people" ? setup.People.Where(p => people.Contains(p.Id)).Select(p => p.Id).ToArray() : [], packFor == "unassigned");
        }

        var sheet = new DialogSheet(s["ExportTitle"], Ui.V(14, kinds, formats, new Border().Classed("divider"), new ScrollViewer { Content = options, MaxHeight = 420, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }, hint, Footer(cancel, copy, export)), 640);
        Render();
        export.Click += (_, _) => sheet.Close(Result(format));
        copy.Click += (_, _) => sheet.Close(Result("copy"));
        cancel.Click += (_, _) => sheet.Close(null);
        return await sheet.ShowDialog<ExportChoice?>(owner);
    }
}
