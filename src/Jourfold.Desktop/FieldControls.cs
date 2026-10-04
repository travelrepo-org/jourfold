using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using NodaTime;
using NodaTime.Text;

namespace Jourfold.Desktop;

public static class FieldOptions
{
    public static IReadOnlyList<Choice> Languages => CultureInfo.GetCultures(CultureTypes.NeutralCultures | CultureTypes.SpecificCultures)
        .Where(c => c.Name.Length > 0).OrderBy(c => c.DisplayName).Select(c => new Choice(c.Name, c.DisplayName + " · " + c.NativeName + " (" + c.Name + ")")).ToArray();
    /// <summary>
    /// Every IANA timezone, labelled so people can search by city, country, region note or UTC offset:
    /// "Shanghai · China (Beijing Time) · UTC+8". Zones without a country (Etc/GMT+8) and old aliases (PRC) follow
    /// with their plain ID, so stored values always resolve.
    /// </summary>
    public static IReadOnlyList<Choice> Timezones => timezones.Value;
    private static readonly Lazy<Choice[]> timezones = new(() =>
    {
        var source = NodaTime.TimeZones.TzdbDateTimeZoneSource.Default;
        var year = SystemClock.Instance.GetCurrentInstant().InUtc().Year;
        string Offsets(string id)
        {
            var zone = DateTimeZoneProviders.Tzdb[id];
            string Format(Offset o) => (o < Offset.Zero ? "−" : "+") + OffsetPattern.CreateWithInvariantCulture("H:mm").Format(o < Offset.Zero ? -o : o).Replace(":00", "");
            var winter = zone.GetUtcOffset(Instant.FromUtc(year, 1, 15, 12, 0)); var summer = zone.GetUtcOffset(Instant.FromUtc(year, 7, 15, 12, 0));
            return "UTC" + Format(winter < summer ? winter : summer) + (winter == summer ? "" : "/" + Format(winter < summer ? summer : winter));
        }
        var located = source.ZoneLocations!.GroupBy(l => l.ZoneId).ToDictionary(g => g.Key, g => g.First());
        var perCountry = source.ZoneLocations!.GroupBy(l => l.CountryCode).ToDictionary(g => g.Key, g => g.Count());
        var named = located.Values.Select(l =>
        {
            var city = l.ZoneId[(l.ZoneId.LastIndexOf('/') + 1)..].Replace('_', ' ');
            var note = perCountry[l.CountryCode] > 1 && !string.IsNullOrWhiteSpace(l.Comment) ? " (" + l.Comment + ")" : "";
            return (Sort: l.CountryName + " " + city, Choice: new Choice(l.ZoneId, city + " · " + l.CountryName + note + " · " + Offsets(l.ZoneId)));
        }).OrderBy(x => x.Sort, StringComparer.CurrentCulture).Select(x => x.Choice);
        var others = source.GetIds().Where(id => !located.ContainsKey(id)).Order(StringComparer.Ordinal)
            .Select(id => new Choice(id, (source.CanonicalIdMap[id] is var canonical && canonical != id ? id + " (" + canonical + ")" : id) + " · " + Offsets(id)));
        return named.Concat(others).ToArray();
    });
}

public sealed class SearchChoice : UserControl
{
    private readonly IReadOnlyList<Choice> choices;
    public AutoCompleteBox Input { get; } = new();
    public string? Text { get => Input.Text; set => Input.Text = value; }
    public SearchChoice(IReadOnlyList<Choice> options, string? selected, string label)
    {
        choices = options; Input.ItemsSource = options; Input.FilterMode = AutoCompleteFilterMode.Contains; Input.MinimumPrefixLength = 0;
        Input.ValueMemberBinding = new Binding(nameof(Choice.Label));
        Input.SelectedItem = options.FirstOrDefault(c => c.Id == selected); Input.Text = (Input.SelectedItem as Choice)?.Label;
        Input.Watermark = label; Input.HorizontalAlignment = HorizontalAlignment.Stretch;
        var browse = new Button { Content = new Icon("chevron-down", 14), Padding = new Thickness(9, 4) }; browse.Classes.Add("icon"); ToolTip.SetTip(browse, label); AutomationProperties.SetName(browse, label);
        browse.Click += (_, _) => { Input.Text = ""; Input.Focus(); Input.IsDropDownOpen = true; };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 4 }; grid.Children.Add(Input); Grid.SetColumn(browse, 1); grid.Children.Add(browse); Content = grid;
        AutomationProperties.SetName(Input, label); ToolTip.SetTip(Input, label);
    }
    /// <summary>Selects the option with <paramref name="id"/>, or clears the field when there is none.</summary>
    public void Select(string? id) { var choice = choices.FirstOrDefault(c => c.Id == id); Input.SelectedItem = choice; Input.Text = choice?.Label; }
    public string? Value => Input.SelectedItem is Choice selected && Text == selected.Label ? selected.Id : choices.FirstOrDefault(c => string.Equals(c.Label, Text, StringComparison.CurrentCultureIgnoreCase) || string.Equals(c.Id, Text, StringComparison.OrdinalIgnoreCase))?.Id;
}

public sealed class LocalDateTimeEditor : StackPanel
{
    public CalendarDatePicker Date { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    public TimePicker Time { get; } = new() { ClockIdentifier = "24HourClock", HorizontalAlignment = HorizontalAlignment.Stretch };
    public LocalDateTimeEditor(string value, string label)
    {
        Spacing = 6; Children.Add(Date); Children.Add(Time);
        var parsed = LocalDateTimePattern.ExtendedIso.Parse(value);
        if (parsed.Success) { Date.SelectedDate = parsed.Value.Date.ToDateTimeUnspecified(); Time.SelectedTime = TimeSpan.FromTicks(parsed.Value.TimeOfDay.TickOfDay); }
        AutomationProperties.SetName(Date, label); AutomationProperties.SetName(Time, label);
    }
    public string? Value => Date.SelectedDate is { } date && Time.SelectedTime is { } time
        ? LocalDateTimePattern.ExtendedIso.Format(LocalDateTime.FromDateTime(date.Date + time)) : null;
}

public sealed class FormControl : StackPanel
{
    public FormField Field { get; }
    public Control Editor { get; }
    public FormControl(FormField field)
    {
        Field = field; Spacing = 6;
        Children.Add(new TextBlock { Text = field.Label + (field.Required ? "" : ""), Classes = { "label" } });
        Editor = field.Kind switch
        {
            "date" => new CalendarDatePicker { SelectedDate = DateTime.TryParseExact(field.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null },
            "datetime" => new LocalDateTimeEditor(field.Value, field.Label),
            "time" => new TimePicker { ClockIdentifier = "24HourClock", SelectedTime = TimeSpan.TryParse(field.Value, CultureInfo.InvariantCulture, out var time) ? time : null },
            "timezone" => new SearchChoice(FieldOptions.Timezones, field.Value, field.Label),
            "language" => new SearchChoice(FieldOptions.Languages, field.Value, field.Label),
            "choice" => new SearchChoice(field.Choices ?? [], field.Value, field.Label),
            "number" => new NumericUpDown { Value = decimal.TryParse(field.Value, CultureInfo.InvariantCulture, out var number) ? number : null, FormatString = "0.######" },
            "duration" => new NumericUpDown { Value = Minutes(field.Value), Minimum = 0, Increment = 15, FormatString = "0.##" },
            "readonly" => new TextBlock { Text = field.Value, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            _ => new TextBox { Text = field.Value }
        };
        Editor.Tag = field.Key; Editor.HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(Editor, field.Label); Children.Add(Editor);
        if (field.Hint is { Length: > 0 } hint) { if (Editor is TextBox box && box.Watermark is null) box.Watermark = hint; else Children.Add(new TextBlock { Text = hint, Classes = { "caption" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap }); }
    }
    private static decimal? Minutes(string value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        var parsed = PeriodPattern.NormalizingIso.Parse(value);
        if (!parsed.Success) return null;
        try { return (decimal)parsed.Value.ToDuration().TotalMinutes; } catch (InvalidOperationException) { return null; }
    }
    public string Value => Editor switch
    {
        CalendarDatePicker p => p.SelectedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
        LocalDateTimeEditor p => p.Value ?? "",
        TimePicker p => p.SelectedTime?.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture) ?? "",
        SearchChoice p => p.Value ?? "",
        NumericUpDown p => p.Value is { } n ? Field.Kind == "duration" ? PeriodPattern.NormalizingIso.Format(Period.FromSeconds((long)(n * 60))) : n.ToString(CultureInfo.InvariantCulture) : "",
        TextBox p => p.Text?.Trim() ?? "",
        _ => Field.Value
    };
    public bool IsValid => (!Field.Required || Value.Length > 0) && (Editor is not SearchChoice search || string.IsNullOrWhiteSpace(search.Text) || search.Value is not null);
}
