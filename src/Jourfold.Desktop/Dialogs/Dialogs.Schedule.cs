using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NodaTime;
using NodaTime.Text;
using TravelRepo.Core;

namespace Jourfold.Desktop;

public sealed partial class Dialogs
{
    /// <summary>Edit when something happens. Returns canonical time JSON, or precision "unscheduled".</summary>
    public async Task<JsonObject?> ScheduleAsync(JsonObject? initial, LocalDate date, string zone)
    {
        var s = S;
        var kinds = new[] { ("exact", s["exact"], "clock"), ("approximate", s["approximate"], "timer"), ("window", s["window"], "calendar-clock"), ("day_part", s["day_part"], "sun"), ("all_day", s["all_day"], "calendar"), ("unscheduled", s["unscheduled"], "inbox") };
        var kind = initial?["precision"]?.ToString() ?? (initial is null ? "exact" : "exact");
        var first = initial?["start"] ?? initial?["earliest"]; var last = initial?["end"] ?? initial?["latest"];
        LocalDateTime Parse(JsonNode? node, LocalDateTime fallback) => node?["local"] is { } v && LocalDateTimePattern.ExtendedIso.Parse(v.ToString()).TryGetValue(default, out var t) ? t : fallback;
        var start = Parse(first, date.At(new LocalTime(9, 0))); var end = Parse(last, start.PlusHours(1));
        var startDate = new CalendarDatePicker { Tag = "startDate", SelectedDate = start.Date.ToDateTimeUnspecified(), HorizontalAlignment = HorizontalAlignment.Stretch }; AutomationProperties.SetName(startDate, s["StartDate"]);
        var startTime = new TimePicker { Tag = "startTime", ClockIdentifier = "24HourClock", SelectedTime = start.TimeOfDay.ToTimeOnly().ToTimeSpan(), MinuteIncrement = 5, HorizontalAlignment = HorizontalAlignment.Stretch }; AutomationProperties.SetName(startTime, s["StartTime"]);
        var endDate = new CalendarDatePicker { Tag = "endDate", SelectedDate = end.Date.ToDateTimeUnspecified(), HorizontalAlignment = HorizontalAlignment.Stretch }; AutomationProperties.SetName(endDate, s["EndDate"]);
        var endTime = new TimePicker { Tag = "endTime", ClockIdentifier = "24HourClock", SelectedTime = end.TimeOfDay.ToTimeOnly().ToTimeSpan(), MinuteIncrement = 5, HorizontalAlignment = HorizontalAlignment.Stretch }; AutomationProperties.SetName(endTime, s["EndTime"]);
        var startZone = new SearchChoice(FieldOptions.Timezones, first?["timezone"]?.ToString() ?? zone, s["StartTimezone"]);
        var endZone = new SearchChoice(FieldOptions.Timezones, last?["timezone"]?.ToString() ?? first?["timezone"]?.ToString() ?? zone, s["EndTimezone"]);
        var differentZone = new CheckBox { Content = s["ArrivesInOtherZone"], IsChecked = (last?["timezone"]?.ToString() ?? first?["timezone"]?.ToString()) != (first?["timezone"]?.ToString() ?? zone) };
        var day = new CalendarDatePicker { Tag = "day", SelectedDate = (initial?["date"] is { } d && LocalDatePattern.Iso.Parse(d.ToString()).TryGetValue(default, out var parsed) ? parsed : date).ToDateTimeUnspecified(), HorizontalAlignment = HorizontalAlignment.Stretch }; AutomationProperties.SetName(day, s["Date"]);
        var dayPart = initial?["day_part"]?.ToString() ?? "morning";
        var dayZone = new SearchChoice(FieldOptions.Timezones, initial?["timezone"]?.ToString() ?? zone, s["Timezone"]) { Tag = "dayZone" };

        var durations = Ui.H(6);
        foreach (var minutes in new[] { 30, 60, 90, 120, 180, 240 })
        {
            var chip = Ui.Button(Formats.Duration(Duration.FromMinutes(minutes), s), null, "chip");
            chip.Click += (_, _) => { if (startDate.SelectedDate is { } sd && startTime.SelectedTime is { } st) { var e = LocalDateTime.FromDateTime(sd.Date + st).PlusMinutes(minutes); endDate.SelectedDate = e.Date.ToDateTimeUnspecified(); endTime.SelectedTime = e.TimeOfDay.ToTimeOnly().ToTimeSpan(); } };
            durations.Children.Add(chip);
        }
        var dayParts = Ui.H(6);
        void FillParts()
        {
            dayParts.Children.Clear();
            foreach (var part in new[] { "morning", "afternoon", "evening", "night" })
            {
                var range = ScheduleQueries.DayPartRange(part);
                var chip = Ui.Button(s[part], part switch { "morning" => "sun", "night" => "moon", _ => "clock" }, "chip"); if (part == dayPart) chip.Classes.Add("selected");
                ToolTip.SetTip(chip, Formats.Time(range.Start) + " – " + Formats.Time(range.End));
                chip.Click += (_, _) => { dayPart = part; FillParts(); }; dayParts.Children.Add(chip);
            }
        }
        FillParts();
        var startLabel = Ui.Text(s["Start"], "label"); var endLabel = Ui.Text(s["End"], "label");
        var timed = Ui.V(12,
            Ui.V(5, startLabel, Ui.Columns("*,*", startDate, startTime.Margin(8, 0, 0, 0))),
            Ui.V(5, endLabel, Ui.Columns("*,*", endDate, endTime.Margin(8, 0, 0, 0))),
            durations,
            Ui.V(5, Ui.Text(s["Timezone"], "label"), startZone), differentZone, endZone);
        var parted = Ui.V(12, Ui.V(5, Ui.Text(s["Date"], "label"), day), dayParts, Ui.V(5, Ui.Text(s["Timezone"], "label"), dayZone));
        var hint = Ui.Text("", "caption");
        var segmentHost = new ContentControl();
        void Update()
        {
            segmentHost.Content = ViewToolbar.Segmented(kinds.Select(k => (k.Item1, k.Item2, (string?)k.Item3)), kind, k => { kind = k; Update(); });
            timed.IsVisible = kind is "exact" or "approximate" or "window"; parted.IsVisible = kind is "day_part" or "all_day";
            dayParts.IsVisible = dayZone.IsVisible = kind == "day_part"; endZone.IsVisible = differentZone.IsChecked == true;
            startLabel.Text = kind == "window" ? s["Earliest"] : s["Start"]; endLabel.Text = kind == "window" ? s["Latest"] : s["End"];
            durations.IsVisible = kind != "window";
            hint.Text = s["hint." + kind];
        }
        differentZone.IsCheckedChanged += (_, _) => Update(); Update();
        var error = Ui.Text("").Res(TextBlock.ForegroundProperty, "Danger"); error.IsVisible = false;
        var save = Action("Save", true); var cancel = Action("Cancel");
        var sheet = new DialogSheet(s["WhenTitle"], Ui.V(16, segmentHost, hint, timed, parted, error, Footer(cancel, save)), 640);
        save.Click += async (_, _) =>
        {
            try
            {
                var result = initial?.DeepClone() as JsonObject ?? new JsonObject();
                foreach (var key in new[] { "start", "end", "earliest", "latest", "date", "day_part", "timezone" }) result.Remove(key);
                result["precision"] = kind;
                if (kind == "unscheduled") { sheet.Close(result); return; }
                if (kind is "exact" or "approximate" or "window")
                {
                    if (startDate.SelectedDate is not { } sd || startTime.SelectedTime is not { } st || endDate.SelectedDate is not { } ed || endTime.SelectedTime is not { } et || startZone.Value is not { } sz) throw new DomainException("form.invalid", s["InvalidFields"]);
                    var ez = differentZone.IsChecked == true ? endZone.Value ?? sz : sz;
                    string Local(DateTime dateValue, TimeSpan time) => LocalDateTimePattern.ExtendedIso.Format(LocalDateTime.FromDateTime(dateValue.Date + time));
                    var a = await ResolveTimeAsync(Local(sd, st), sz, first?["offset"]?.ToString()); if (a is null) return;
                    var b = await ResolveTimeAsync(Local(ed, et), ez, last?["offset"]?.ToString()); if (b is null) return;
                    if (b.ToInstant() <= a.ToInstant()) throw new DomainException("time.range", s["EndAfterStart"]);
                    result[kind == "window" ? "earliest" : "start"] = a.ToJson(); result[kind == "window" ? "latest" : "end"] = b.ToJson();
                }
                else
                {
                    if (day.SelectedDate is not { } dd) throw new DomainException("form.invalid", s["InvalidFields"]);
                    result["date"] = dd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    if (kind == "day_part") { result["day_part"] = dayPart; result["timezone"] = dayZone.Value ?? zone; }
                }
                sheet.Close(result);
            }
            catch (DomainException ex) { error.Text = ex.Message; error.IsVisible = true; }
        };
        cancel.Click += (_, _) => sheet.Close();
        return await sheet.ShowDialog<JsonObject>(owner);
    }

    /// <summary>Validate a wall time in a zone. Clock gaps are explained; repeated times ask which offset.</summary>
    public async Task<ZonedTime?> ResolveTimeAsync(string local, string zone, string? offset = null)
    {
        var parsed = LocalDateTimePattern.ExtendedIso.Parse(local);
        if (!parsed.Success) throw new DomainException("time.local", S["InvalidFields"]);
        var tz = DateTimeZoneProviders.Tzdb.GetZoneOrNull(zone) ?? throw new DomainException("time.zone", S["diagnostic.time.zone"]);
        var mapped = tz.MapLocal(parsed.Value);
        if (mapped.Count == 0) throw new DomainException("time.gap", S["ClockGap"]);
        if (mapped.Count == 2)
        {
            var offsets = new[] { mapped.First().Offset, mapped.Last().Offset }.Select(o => OffsetPattern.GeneralInvariant.Format(o)).ToArray();
            var choice = await ChooseAsync(S["ClockRepeated"], offsets.Select((o, i) => new Choice(o, "UTC" + o, "clock", S[i == 0 ? "FirstOccurrence" : "SecondOccurrence"])).ToArray());
            if (choice is null) return null; offset = choice;
        }
        else offset = null;
        return new ZonedTime(local, zone, offset);
    }
}
