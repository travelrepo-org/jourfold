using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Jourfold.Infrastructure;
using NodaTime;
using NodaTime.Text;
using TravelRepo.Core;

namespace Jourfold.Desktop;

/// <summary>
/// Inline editors used by the inspector. Each commits one undoable edit through the view model when the
/// user finishes (focus leaves, Enter, or a choice is made). Canonical formats are produced here only from
/// typed controls; users never type storage strings.
/// </summary>
public static class Editors
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static Control Text(MainWindow w, Guid id, string path, string label, string? value, bool multiline = false, string? watermark = null)
    {
        var box = new TextBox { Text = value ?? "", AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 110 : 34, Tag = path, Watermark = watermark };
        AutomationProperties.SetName(box, label);
        var original = value ?? "";
        async Task Commit() { var next = box.Text ?? ""; if (next.Trim() == original.Trim()) return; original = next; await w.Model.UpdateAsync(id, e => EditorModel.Set(e, path, next.Trim().Length == 0 ? null : JsonValue.Create(multiline ? next : next.Trim()))); }
        box.LostFocus += async (_, _) => await Commit();
        box.KeyDown += async (_, e) => { if (e.Key == Key.Enter && !multiline) { e.Handled = true; await Commit(); } if (e.Key == Key.Escape) { box.Text = original; e.Handled = true; } };
        return box;
    }

    public static Control Choice(MainWindow w, Guid id, string path, string label, string? value, IReadOnlyList<string> options)
    {
        var s = w.Model.Strings; var choices = options.Select(o => new Choice(o, s[o])).ToList();
        if (value is not null && choices.All(c => c.Id != value)) choices.Add(new(value, value));
        var combo = new ComboBox { ItemsSource = choices, SelectedItem = choices.FirstOrDefault(c => c.Id == value), HorizontalAlignment = HorizontalAlignment.Stretch, Tag = path, PlaceholderText = s["Choose"] };
        AutomationProperties.SetName(combo, label);
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is Choice c && c.Id != value) _ = w.Model.UpdateAsync(id, e => EditorModel.Set(e, path, JsonValue.Create(c.Id))); };
        return combo;
    }

    /// <summary>Status as a row of chips with icons, so state is never shown by color alone.</summary>
    public static Control StatusChips(MainWindow w, Guid id, string? value, IReadOnlyList<string> options)
    {
        var s = w.Model.Strings; var panel = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        foreach (var option in options)
        {
            var (icon, _, _) = Visuals.Status(option);
            var chip = Ui.Button(s[option], option == value ? icon : null, "chip"); if (option == value) chip.Classes.Add("selected");
            chip.Click += (_, _) => { if (option != value) _ = w.Model.UpdateAsync(id, e => e.Data["status"] = option); };
            panel.Children.Add(chip);
        }
        AutomationProperties.SetName(panel, s["Status"]);
        return panel;
    }

    public static Control Toggle(MainWindow w, Guid id, string path, string label, bool value)
    {
        var toggle = new ToggleSwitch { IsChecked = value, OnContent = label, OffContent = label, Tag = path };
        AutomationProperties.SetName(toggle, label);
        toggle.IsCheckedChanged += (_, _) => _ = w.Model.UpdateAsync(id, e => EditorModel.Set(e, path, JsonValue.Create(toggle.IsChecked == true)));
        return toggle;
    }

    public static Control Date(MainWindow w, Guid id, string path, string label, string? value)
    {
        var picker = new CalendarDatePicker { SelectedDate = DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null, HorizontalAlignment = HorizontalAlignment.Stretch, Tag = path, Watermark = w.Model.Strings["ChooseDate"] };
        AutomationProperties.SetName(picker, label);
        picker.SelectedDateChanged += (_, _) =>
        {
            var next = picker.SelectedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (next != value) _ = w.Model.UpdateAsync(id, e => { EditorModel.Set(e, path, next is null ? null : JsonValue.Create(next)); if (e.Data[path.Split('/')[0]] is JsonObject { Count: 0 }) e.Data.Remove(path.Split('/')[0]); });
        };
        return picker;
    }

    public static Control Search(MainWindow w, Guid id, string path, string label, string? value, IReadOnlyList<Choice> options)
    {
        var search = new SearchChoice(options, value, label) { Tag = path };
        search.Input.LostFocus += (_, _) => { var next = search.Value; if (next is not null && next != value) _ = w.Model.UpdateAsync(id, e => EditorModel.Set(e, path, JsonValue.Create(next))); };
        search.Input.SelectionChanged += (_, _) => { var next = search.Value; if (next is not null && next != value) { value = next; _ = w.Model.UpdateAsync(id, e => EditorModel.Set(e, path, JsonValue.Create(next))); } };
        return search;
    }

    public static Control Number(MainWindow w, Guid id, string path, string label, double? value, string? suffix = null, Func<double, JsonNode>? convert = null, double increment = 1)
    {
        var box = new NumericUpDown { Value = value is null ? null : (decimal)value, Increment = (decimal)increment, FormatString = "0.##", HorizontalAlignment = HorizontalAlignment.Stretch, Tag = path, ShowButtonSpinner = false, Watermark = suffix };
        AutomationProperties.SetName(box, label);
        box.LostFocus += (_, _) =>
        {
            var next = (double?)box.Value; if (next == value) return; value = next;
            _ = w.Model.UpdateAsync(id, e => EditorModel.Set(e, path, next is null ? null : convert?.Invoke(next.Value) ?? JsonValue.Create(next.Value)));
        };
        return suffix is null ? box : Ui.Columns("*,Auto", box, Ui.Text(suffix, "caption").Margin(8, 0, 0, 0));
    }

    /// <summary>
    /// Latitude and longitude as one value. It is saved only when both are valid, so a place never holds half a location;
    /// clearing both removes it. Point and comma both work as decimal separator. Pasting a pair such as
    /// "-37.0093, 174.7841" into either field while both are empty fills both.
    /// </summary>
    public static Control Coordinates(MainWindow w, Guid id, (double Lat, double Lon)? value)
    {
        var s = w.Model.Strings; var saved = value;
        static string Format(double? v) => v?.ToString("0.######", CultureInfo.InvariantCulture) ?? "";
        var latitude = new TextBox { Text = Format(value?.Lat), Watermark = "-37.0093", Tag = "latitude" }; AutomationProperties.SetName(latitude, s["Latitude"]);
        var longitude = new TextBox { Text = Format(value?.Lon), Watermark = "174.7841", Tag = "longitude" }; AutomationProperties.SetName(longitude, s["Longitude"]);
        var problem = Ui.Text("", "caption").Res(TextBlock.ForegroundProperty, "Danger").Also(t => { t.IsVisible = false; t.TextWrapping = TextWrapping.Wrap; });
        void Show(string? message) { problem.Text = message ?? ""; problem.IsVisible = message is not null; }
        void Commit()
        {
            var latText = latitude.Text?.Trim() ?? ""; var lonText = longitude.Text?.Trim() ?? "";
            if (latText.Length == 0 && lonText.Length == 0)
            {
                Show(null);
                if (saved is not null) { saved = null; _ = w.Model.UpdateAsync(id, e => e.Data.Remove("location")); }
                return;
            }
            if (latText.Length == 0 || lonText.Length == 0) { Show(s["CoordinatesNeedBoth"]); return; }
            if (!Jourfold.Application.Coordinates.TryParse(latText, out var lat) || !Jourfold.Application.Coordinates.IsLatitude(lat)) { Show(s["LatitudeRange"]); return; }
            if (!Jourfold.Application.Coordinates.TryParse(lonText, out var lon) || !Jourfold.Application.Coordinates.IsLongitude(lon)) { Show(s["LongitudeRange"]); return; }
            Show(null); lat = Math.Round(lat, 6); lon = Math.Round(lon, 6);
            if (saved == (lat, lon)) return;
            saved = (lat, lon);
            _ = w.Model.UpdateAsync(id, e => { var location = e.Data["location"] as JsonObject ?? new JsonObject(); location["latitude"] = lat; location["longitude"] = lon; e.Data["location"] = location.DeepClone(); });
        }
        async void Paste(object? sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(latitude.Text) || !string.IsNullOrWhiteSpace(longitude.Text) || sender is not TextBox target || TopLevel.GetTopLevel(target)?.Clipboard is not { } clipboard) return;
            e.Handled = true;
            var text = await clipboard.TryGetTextAsync();
            if (Jourfold.Application.Coordinates.TryParsePair(text, out var lat, out var lon)) { latitude.Text = Format(lat); longitude.Text = Format(lon); Commit(); }
            else target.Text = text?.Trim();
        }
        latitude.PastingFromClipboard += Paste; longitude.PastingFromClipboard += Paste;
        latitude.LostFocus += (_, _) => Commit(); longitude.LostFocus += (_, _) => Commit();
        latitude.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); }; longitude.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        var tip = Ui.Text(s["CoordinatesPasteHint"], "caption").Also(t => t.TextWrapping = TextWrapping.Wrap);
        void UpdateTip() => tip.IsVisible = string.IsNullOrWhiteSpace(latitude.Text) && string.IsNullOrWhiteSpace(longitude.Text);
        latitude.TextChanged += (_, _) => UpdateTip(); longitude.TextChanged += (_, _) => UpdateTip(); UpdateTip();
        return Ui.V(6, Ui.Columns("*,*", Ui.Field(s["Latitude"], latitude), Ui.Field(s["Longitude"], longitude).Margin(10, 0, 0, 0)), problem, tip);
    }

    public static Control Money(MainWindow w, Guid id, string path, string label, JsonNode? value)
    {
        var money = TripSummaries.Money(value);
        var amount = new NumericUpDown { Value = money?.Amount, FormatString = "N2", Increment = 1, HorizontalAlignment = HorizontalAlignment.Stretch, ShowButtonSpinner = false, Tag = path };
        AutomationProperties.SetName(amount, label);
        var currency = new SearchChoice(Currencies, money?.Currency ?? w.Model.Store.Get("currency") ?? "EUR", w.Model.Strings["Currency"]) { Width = 150 };
        void Commit()
        {
            var code = currency.Value ?? money?.Currency ?? "EUR"; var next = amount.Value ?? 0m;
            if (money is not null && money.Amount == next && money.Currency == code) return;
            money = new TravelRepo.Core.Money(next.ToString("0.00", CultureInfo.InvariantCulture), code);
            _ = w.Model.UpdateAsync(id, e => EditorModel.Set(e, path, new JsonObject { ["value"] = money.Value, ["currency"] = code }));
        }
        amount.LostFocus += (_, _) => Commit(); currency.Input.LostFocus += (_, _) => Commit();
        return Ui.Columns("*,Auto", amount, currency.Margin(8, 0, 0, 0));
    }

    public static IReadOnlyList<Choice> Currencies { get; } = CultureInfo.GetCultures(CultureTypes.SpecificCultures).Select(c => { try { return new RegionInfo(c.Name); } catch (ArgumentException) { return null; } }).OfType<RegionInfo>()
        .DistinctBy(r => r.ISOCurrencySymbol).Where(r => r.ISOCurrencySymbol.Length == 3).OrderBy(r => r.ISOCurrencySymbol).Select(r => new Choice(r.ISOCurrencySymbol, r.ISOCurrencySymbol + " " + r.CurrencySymbol)).ToArray();

    /// <summary>A button showing the current reference; opens a searchable picker that can also create entities.</summary>
    public static Control Reference(MainWindow w, Guid id, string path, string label, string? type, bool allowCreate = true)
    {
        var m = w.Model; var trip = m.Trip!; var current = Guid.TryParse(EditorModel.Get(trip.Find(id)!, path)?.ToString(), out var refId) ? trip.Find(refId) : null;
        Control content = current is null ? Ui.H(6, Ui.Icon("plus", 14, "Text.Subtle"), Ui.Text(m.Strings["Choose"], "subtle")) : Ui.H(8, Ui.Icon(Visuals.For(trip, current).Icon, 14, "Kind." + Visuals.For(trip, current).KindKey + ".Fg"), Ui.Line(current.Title));
        var button = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, FontWeight = FontWeight.Normal, Tag = path }.Named(label);
        button.Flyout = Picker(w, type, current is null ? [] : [current.Id], multi: false, picked => m.UpdateAsync(id, e => EditorModel.Set(e, path, picked is null ? null : JsonValue.Create(picked.Id.ToString()))), allowCreate, allowClear: current is not null);
        if (current is null) return button;
        var open = Ui.IconButton("arrow-up-right", string.Format(m.Strings["OpenItem"], current.Title), "small"); open.Click += (_, _) => m.Selected = current;
        return Ui.Columns("*,Auto", button, open.Margin(4, 0, 0, 0));
    }

    /// <summary>A set of references (people, items, documents) as chips with an add picker.</summary>
    public static Control References(MainWindow w, Guid id, string path, string label, string? type, Func<Entity, JsonArray?>? read = null, Action<Entity, JsonArray>? write = null)
    {
        var m = w.Model; var trip = m.Trip!; var entity = trip.Find(id)!;
        var values = (read?.Invoke(entity) ?? EditorModel.Get(entity, path) as JsonArray ?? []).Select(n => Guid.TryParse(n?.ToString(), out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList();
        var panel = new WrapPanel { ItemSpacing = 6, LineSpacing = 6, Tag = path };
        foreach (var refId in values)
        {
            if (trip.Find(refId) is not { } target) continue;
            Control avatar = target.Type == "person" ? Ui.Avatar(target.Title, 20) : Ui.Icon(Visuals.For(trip, target).Icon, 13);
            var remove = Ui.IconButton("x", string.Format(m.Strings["RemoveNamed"], target.Title), "small"); remove.Padding = new Thickness(2); remove.MinWidth = 20; remove.MinHeight = 20;
            remove.Click += (_, _) => _ = m.UpdateAsync(id, e => { var next = new JsonArray(values.Where(v => v != refId).Select(v => (JsonNode?)JsonValue.Create(v.ToString())).ToArray()); if (write is not null) write(e, next); else EditorModel.Set(e, path, next); });
            var open = new Button { Content = Ui.Line(target.Title), Padding = new Thickness(4, 0), MinHeight = 0, FontWeight = FontWeight.Medium }.Classed("ghost"); open.Click += (_, _) => m.Selected = target;
            panel.Children.Add(new Border { Child = Ui.H(2, avatar, open, remove), Padding = new Thickness(4, 2), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1) }.Res(Border.BackgroundProperty, "Bg.Subtle").Res(Border.BorderBrushProperty, "Line"));
        }
        var add = Ui.Button(m.Strings["Add"], "plus", "chip").Named(string.Format(m.Strings["AddTo"], label));
        add.Flyout = Picker(w, type, values, multi: true, picked =>
        {
            if (picked is null) return Task.CompletedTask;
            return m.UpdateAsync(id, e => { var next = new JsonArray(values.Append(picked.Id).Distinct().Select(v => (JsonNode?)JsonValue.Create(v.ToString())).ToArray()); if (write is not null) write(e, next); else EditorModel.Set(e, path, next); });
        }, allowCreate: type is "person" or "place", allowClear: false);
        panel.Children.Add(add);
        AutomationProperties.SetName(panel, label);
        return panel;
    }

    /// <summary>Searchable entity picker. Places can be created by name or found on the map when online search is enabled.</summary>
    public static Flyout Picker(MainWindow w, string? type, IReadOnlyCollection<Guid> selected, bool multi, Func<Entity?, Task> picked, bool allowCreate, bool allowClear)
    {
        var m = w.Model; var s = m.Strings; var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        var search = new TextBox { Watermark = s["SearchOrCreate"], Width = 300 }; AutomationProperties.SetName(search, s["SearchOrCreate"]);
        var results = Ui.V(2); var online = Ui.V(2);
        var panel = Ui.V(8, search, new ScrollViewer { Content = Ui.V(4, results, online), MaxHeight = 320 });
        flyout.Content = panel;
        async Task Choose(Entity? entity) { flyout.Hide(); await picked(entity); }
        void Fill()
        {
            results.Children.Clear(); var query = search.Text?.Trim() ?? ""; var trip = m.Trip; if (trip is null) return;
            var candidates = trip.Entities.Values.Where(e => (type is null ? e.Type is not ("comment" or "trip") : e.Type == type) && (query.Length == 0 || e.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)) && !(multi && selected.Contains(e.Id)))
                .OrderBy(e => e.Title, StringComparer.CurrentCulture).Take(40);
            foreach (var e in candidates)
            {
                var (icon, kind) = Visuals.For(trip, e);
                var row = new Button { Content = Ui.Columns("Auto,*,Auto", Ui.Icon(icon, 15, "Kind." + kind + ".Fg"), Ui.Line(e.Title).Margin(8, 0), selected.Contains(e.Id) ? Ui.Icon("check", 14, "Accent") : null) }.Classed("row").Named(e.Title);
                row.Click += async (_, _) => await Choose(e); results.Children.Add(row);
            }
            if (allowClear) { var clear = new Button { Content = Ui.H(8, Ui.Icon("x", 14, "Text.Subtle"), Ui.Text(s["ClearSelection"], "muted")) }.Classed("row"); clear.Click += async (_, _) => await Choose(null); results.Children.Add(clear); }
            if (allowCreate && query.Length > 0 && type is "place" or "person" or "booking" && !trip.Entities.Values.Any(e => e.Type == type && string.Equals(e.Title, query, StringComparison.CurrentCultureIgnoreCase)))
            {
                var create = new Button { Content = Ui.H(8, Ui.Icon("plus", 15, "Accent"), Ui.Text(string.Format(s["CreateNamed"], query)).Res(TextBlock.ForegroundProperty, "Accent")) }.Classed("row");
                create.Click += async (_, _) => { var e = Entity.Create(type!, query); if (type == "booking") e.Data["status"] = "pending"; await m.RunEditAsync(() => m.Workspace!.EditAsync(e)); await Choose(m.Workspace!.State.Trip.Find(e.Id)); };
                results.Children.Add(create);
            }
            if (type == "place" && query.Length > 2)
            {
                if (m.OnlineMaps) { var find = new Button { Content = Ui.H(8, Ui.Icon("globe", 15, "Info"), Ui.Text(string.Format(s["SearchMapFor"], query)).Res(TextBlock.ForegroundProperty, "Info")) }.Classed("row"); find.Click += async (_, _) => await OnlineAsync(query); results.Children.Add(find); }
                else results.Children.Add(Ui.Text(s["EnableOnlineSearchHint"], "caption").Margin(10, 4));
            }
            if (results.Children.Count == 0) results.Children.Add(Ui.Text(s["NothingFound"], "caption").Margin(10, 6));
        }
        async Task OnlineAsync(string query)
        {
            online.Children.Clear(); online.Children.Add(Ui.Text(s["Searching"], "caption").Margin(10, 4));
            try
            {
                var found = await new PlaceSearch(Http).SearchAsync(query, s.Language);
                online.Children.Clear(); if (found.Count == 0) online.Children.Add(Ui.Text(s["NothingFound"], "caption").Margin(10, 4));
                foreach (var place in found)
                {
                    var row = new Button { Content = Ui.Columns("Auto,*", Ui.Icon("map-pin", 15, "Info"), Ui.V(0, Ui.Line(place.Name, "strong"), Ui.Line(place.Address, "caption")).Margin(8, 0, 0, 0)) }.Classed("row").Named(place.Address);
                    row.Click += async (_, _) =>
                    {
                        var e = Entity.Create("place", place.Name); e.Data["location"] = new JsonObject { ["latitude"] = place.Latitude, ["longitude"] = place.Longitude }; e.Data["address"] = new JsonObject { ["formatted"] = place.Address };
                        if (place.OsmId is not null) e.Data["external_ids"] = new JsonObject { ["openstreetmap"] = place.OsmId };
                        await m.RunEditAsync(() => m.Workspace!.EditAsync(e)); await Choose(m.Workspace!.State.Trip.Find(e.Id));
                    };
                    online.Children.Add(row);
                }
                online.Children.Add(Ui.Text("© OpenStreetMap contributors", "caption").Margin(10, 4));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) { online.Children.Clear(); online.Children.Add(Ui.Text(s["NetworkError"], "caption").Margin(10, 4)); }
        }
        search.TextChanged += (_, _) => Fill();
        search.KeyDown += async (_, e) => { if (e.Key == Key.Enter && type == "place" && m.OnlineMaps && (search.Text?.Trim().Length ?? 0) > 2) { e.Handled = true; await OnlineAsync(search.Text!.Trim()); } };
        flyout.Opened += (_, _) => { search.Text = ""; Fill(); search.Focus(); };
        return flyout;
    }

    /// <summary>A read-only value row that opens a richer editor (time, coordinates and similar).</summary>
    public static Button ValueButton(string text, string icon, string label, Func<Task> open, string? tag = null)
    {
        var button = new Button { Content = Ui.Columns("Auto,*,Auto", Ui.Icon(icon, 15, "Text.Muted"), Ui.Text(text).Margin(10, 0), Ui.Icon("pencil", 13, "Text.Subtle")), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, FontWeight = FontWeight.Normal, Tag = tag }.Named(label);
        button.Click += async (_, _) => await open();
        return button;
    }
}
