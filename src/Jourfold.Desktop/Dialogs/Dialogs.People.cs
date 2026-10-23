using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Jourfold.Desktop;

public sealed partial class Dialogs
{
    /// <summary>
    /// "Who are you in this trip?": the trip's people, adding oneself, or not being part of the trip, and whether to
    /// save the local e-mail and GitHub name on the chosen person. Warns when that person already has another identity.
    /// </summary>
    public async Task<WhoAmIAnswer?> WhoAmIAsync(WhoAmIRequest request)
    {
        var s = S; string? choice = request.Suggested?.ToString();
        var list = Ui.V(4); var radios = new List<RadioButton>();
        var save = new CheckBox { Content = Ui.Text(s["SaveMyIdentity"]).Also(t => t.TextWrapping = TextWrapping.Wrap), IsVisible = false };
        var warning = Ui.Text("", "caption").Also(t => { t.TextWrapping = TextWrapping.Wrap; t.IsVisible = false; }).Res(TextBlock.ForegroundProperty, "Warning");
        var saveHint = Ui.Text(s["SaveMyIdentityHint"], "caption").Also(t => { t.TextWrapping = TextWrapping.Wrap; t.IsVisible = false; });
        void Update()
        {
            var person = request.People.FirstOrDefault(p => p.Id.ToString() == choice);
            save.IsVisible = saveHint.IsVisible = person is not null && request.CanSaveIdentity;
            var other = person is not null && request.HasOtherIdentity(person);
            warning.IsVisible = other; warning.Text = other ? string.Format(s["OtherIdentityWarning"], person!.Title) : "";
            if (person is not null) save.IsChecked = !other;
        }
        void Option(string id, string label, Control icon, string? hint = null)
        {
            var radio = new RadioButton { GroupName = "whoami", IsChecked = id == choice, Content = Ui.Columns("Auto,*", icon, Ui.V(1, Ui.Text(label), hint is null ? null : Ui.Text(hint, "caption")).Margin(10, 0, 0, 0)), Margin = new Thickness(2) };
            AutomationProperties.SetName(radio, label);
            radio.IsCheckedChanged += (_, _) => { if (radio.IsChecked == true) { choice = id; Update(); } };
            radios.Add(radio); list.Children.Add(radio);
        }
        foreach (var person in request.People) Option(person.Id.ToString(), person.Title, Ui.Avatar(person.Title, 26));
        Option(WhoAmIAnswer.AddMe, s["AddMeAsNewPerson"], Ui.Icon("plus", 18, "Accent"));
        Option(WhoAmIAnswer.NotInTrip, s["NotPartOfTrip"], Ui.Icon("user", 18, "Text.Subtle"), s["NotPartOfTripHint"]);
        var ok = Ui.Button(s["ApplyChoice"], null, "primary"); ok.IsDefault = true; var cancel = Action("Cancel");
        var sheet = new DialogSheet(s["WhoAmITitle"], Ui.V(12, Ui.Text(s["WhoAmIBody"], "muted").Also(t => t.TextWrapping = TextWrapping.Wrap),
            new ScrollViewer { Content = list, MaxHeight = 360 }, warning, save, saveHint, Footer(cancel, ok)), 520);
        Update();
        ok.Click += (_, _) => { if (choice is not null) sheet.Close(new WhoAmIAnswer(choice, save.IsVisible && save.IsChecked == true)); };
        cancel.Click += (_, _) => sheet.Close(null);
        return await sheet.ShowDialog<WhoAmIAnswer?>(owner);
    }
}
