using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Jourfold.Application;
using TravelRepo.Core;

namespace Jourfold.UI;

/// <summary>
/// The line that adds to a packing list, the same on the desktop and the phone. While a word starts with "@" or "#",
/// the matching people or groups show as chips below; a tap puts the word in. Enter or the plus adds the item. A name
/// that matches nobody or several people stops the line and says so, with a way to add the missing people.
/// </summary>
public sealed class PackingLineInput : StackPanel
{
    /// <summary>Adds the line; with true, the names nobody has become travellers first. Returns the line as read.</summary>
    public delegate Task<PackingLine?> AddLine(string text, bool addPeople);

    public TextBox Box { get; }
    private readonly Localization strings;
    private readonly Func<TripSnapshot?> trip;
    private readonly AddLine add;
    private readonly WrapPanel suggestions = new() { ItemSpacing = 6, LineSpacing = 6 };
    private readonly StackPanel problem = new() { Spacing = 6 };
    private string? refused;

    /// <param name="refused">
    /// The text that was sent and refused, for a page that is drawn again: while the box holds that text, its problem
    /// shows. <paramref name="refusedChanged"/> hears of a newly refused text.
    /// </param>
    public PackingLineInput(Localization strings, TextBox box, Func<TripSnapshot?> trip, AddLine add, string? refused = null, Action<string>? refusedChanged = null)
    {
        this.strings = strings; this.trip = trip; this.add = add; this.refused = refused; Box = box;
        Spacing = 6;
        box.PlaceholderText ??= strings["AddPackingItemPlaceholder"];
        if (AutomationProperties.GetName(box) is null) AutomationProperties.SetName(box, strings["AddPackingItem"]);
        var plus = Ui.IconButton("plus", strings["AddPackingItem"]);
        plus.Click += async (_, _) => await SendAsync(false);
        box.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await SendAsync(false); } };
        // A keyboard may put back the same word after the page is drawn again; only a different text hides the problem.
        box.TextChanged += (_, _) => Update();
        box.PropertyChanged += (_, e) => { if (e.Property == TextBox.CaretIndexProperty) Update(); };
        Children.Add(Ui.Columns("*,Auto", box, plus.Margin(4, 0, 0, 0)));
        Children.Add(suggestions);
        Children.Add(problem);
        this.refusedChanged = refusedChanged;
        Update();
    }

    private readonly Action<string>? refusedChanged;

    private async Task SendAsync(bool addPeople)
    {
        var text = Box.Text ?? "";
        if (string.IsNullOrWhiteSpace(text)) return;
        var line = await add(text, addPeople);
        if (line is null || line.CanAdd) return;
        refused = text; refusedChanged?.Invoke(text);
        Update();
    }

    /// <summary>Shows the suggestions for the word at the caret, and the problem of a line that was refused.</summary>
    private void Update()
    {
        suggestions.Children.Clear(); problem.Children.Clear();
        if (trip() is not { } snapshot) return;
        var text = Box.Text ?? "";
        foreach (var suggestion in PackingInput.Suggest(snapshot, text, Box.CaretIndex, strings["Everyone"]))
        {
            var chip = Ui.Button(suggestion.Label, suggestion.Kind switch { "everyone" => "users", "person" => "user", _ => "tag" }, "chip");
            // The chip leaves the focus in the field, so the keyboard stays open.
            chip.Focusable = false;
            chip.Click += (_, _) =>
            {
                var (next, caret) = PackingInput.Accept(Box.Text ?? "", Box.CaretIndex, suggestion);
                Box.Text = next; Box.CaretIndex = caret; Box.Focus();
            };
            suggestions.Children.Add(chip);
        }
        suggestions.IsVisible = suggestions.Children.Count > 0;
        if (refused is null || refused.Trim() != text.Trim()) { problem.IsVisible = false; return; }

        var line = PackingInput.Read(snapshot, text);
        foreach (var name in line.Unknown.Distinct(StringComparer.CurrentCultureIgnoreCase))
            problem.Children.Add(Message(string.Format(System.Globalization.CultureInfo.CurrentCulture, strings["PackingUnknownPerson"], name)));
        foreach (var name in line.Ambiguous.Distinct(StringComparer.CurrentCultureIgnoreCase))
            problem.Children.Add(Message(string.Format(System.Globalization.CultureInfo.CurrentCulture, strings["PackingAmbiguousPerson"], name)));
        if (line.Unknown.Count > 0 && line.Ambiguous.Count == 0)
        {
            var names = string.Join(", ", line.Unknown.Distinct(StringComparer.CurrentCultureIgnoreCase));
            var addPeople = Ui.Button(string.Format(System.Globalization.CultureInfo.CurrentCulture, strings["PackingAddPeople"], names), "user-plus", "soft", "compact");
            addPeople.HorizontalAlignment = HorizontalAlignment.Left;
            addPeople.Click += async (_, _) => await SendAsync(true);
            problem.Children.Add(addPeople);
        }
        problem.IsVisible = problem.Children.Count > 0;
    }

    private static TextBlock Message(string text) => Ui.Text(text, "caption").Also(t => { t.TextWrapping = Avalonia.Media.TextWrapping.Wrap; t.Res(TextBlock.ForegroundProperty, "Danger"); });
}
