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
        var parts = initial.Options.Parts; var detail = initial.Options.Detail; var paper = initial.Options.Paper; var person = initial.Options.Person; var timeline = initial.Options.Timeline;
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
                    if (format == "pdf")
                        options.Children.Add(Row("", Ui.Flow(4, Check(s["ExportTimeline"], timeline, on => timeline = on), Check(s["ExportMap"], (parts & ExportParts.Map) != 0, on => parts = on ? parts | ExportParts.Map : parts & ~ExportParts.Map),
                            Check(s["ExportAttachments"], (parts & ExportParts.Attachments) != 0, on => parts = on ? parts | ExportParts.Attachments : parts & ~ExportParts.Attachments))));
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
                if (format == "pdf") options.Children.Add(Paper());
                options.Children.Add(Language());
            }
            Update();
        }

        void Update()
        {
            var chosenPeople = packFor == "people" ? people.Count : 0;
            export.IsEnabled = kind == "plan"
                ? (format == "ics" || (parts & ~(ExportParts.Map | ExportParts.Attachments)) != ExportParts.None) && (from is null || to is null || from <= to)
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
                Timeline = timeline
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
