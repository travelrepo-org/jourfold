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
        // Adding oneself asks for the name; a person the trip has no email for, or a new one, asks for the email that
        // versions are saved under on every device.
        var name = new TextBox { Text = request.Name ?? "", PlaceholderText = s["YourName"] }; AutomationProperties.SetName(name, s["YourName"]);
        var nameField = Ui.Field(s["YourName"], name).Also(f => f.IsVisible = false);
        var email = new TextBox { Text = request.SuggestedEmail ?? "", PlaceholderText = "name@example.com" }; AutomationProperties.SetName(email, s["Email"]);
        var useHidden = request.PrivateGitHubEmail is { } hidden ? Ui.Button(s["UsePrivateGitHubEmail"], "eye-off", "link").Also(b => { ToolTip.SetTip(b, hidden); b.Click += (_, _) => email.Text = hidden; }) : null;
        var remember = new CheckBox { IsChecked = true, IsVisible = request.OfferRememberEmail, Content = Ui.V(2, Ui.Text(s["RememberEmail"]), Ui.Text(s["RememberEmailHint"], "caption").Also(t => t.TextWrapping = TextWrapping.Wrap)) };
        var emailProblem = Ui.Text(s["EmailInvalid"], "caption").Also(t => t.IsVisible = false).Res(TextBlock.ForegroundProperty, "Danger");
        var emailPart = Ui.V(8, Ui.Text(s["TripWelcomeEmailBody"], "caption").Also(t => t.TextWrapping = TextWrapping.Wrap), Ui.Field(s["Email"], email), useHidden, emailProblem, remember).Also(p => p.IsVisible = false);
        void Update()
        {
            var person = request.People.FirstOrDefault(p => p.Id.ToString() == choice);
            var asksEmail = choice == WhoAmIAnswer.AddMe || (person is not null && request.NeedsEmail(person));
            emailPart.IsVisible = asksEmail; nameField.IsVisible = choice == WhoAmIAnswer.AddMe;
            save.IsVisible = saveHint.IsVisible = person is not null && request.CanSaveIdentity && !asksEmail;
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
            new ScrollViewer { Content = list, MaxHeight = 300 }, warning, save, saveHint, nameField, emailPart, Footer(cancel, ok)), 520);
        Update();
        ok.Click += (_, _) =>
        {
            if (choice is null) return;
            // An empty email goes on without one; one that is no address is said so.
            var given = emailPart.IsVisible && !string.IsNullOrWhiteSpace(email.Text) ? email.Text.Trim() : null;
            if (given is not null && !Jourfold.Application.CommitIdentity.LooksLikeEmail(given)) { emailProblem.IsVisible = true; return; }
            if (choice == WhoAmIAnswer.AddMe && string.IsNullOrWhiteSpace(name.Text)) { name.Focus(); return; }
            sheet.Close(new WhoAmIAnswer(choice, save.IsVisible && save.IsChecked == true, given, given is not null && remember.IsVisible && remember.IsChecked == true, choice == WhoAmIAnswer.AddMe ? name.Text!.Trim() : null));
        };
        cancel.Click += (_, _) => sheet.Close(null);
        return await sheet.ShowDialog<WhoAmIAnswer?>(owner);
    }
}
