using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Jourfold.Application;
using TravelRepo.Core;

namespace Jourfold.Mobile.Views;

/// <summary>
/// "Who are you in this trip?", the first time a trip opens on this device: the trip's people, adding oneself, or not
/// being part of the trip. When the trip has no email for the chosen person, it asks for one next, offered from the
/// settings or the GitHub account, because versions are saved under it on every device.
/// </summary>
public sealed class TripWelcomeView
{
    private readonly MobileSession s;
    private readonly StackPanel body = Ui.V(14);
    // The person chosen, or a new one by name; the email step follows when the trip has no email for them.
    private Guid? person;
    private string? newName;

    public Control View { get; }
    public Control Header { get; }

    public TripWelcomeView(MobileSession session)
    {
        s = session;
        var later = Ui.Button(s.Strings["NotNow"], null, "ghost", "compact"); later.Click += (_, _) => s.CloseOverlay();
        Header = Ui.Columns("*,Auto", Ui.V(2, Ui.Text(s.Strings["WhoAmITitle"], "h3"), Ui.Text(s.Trip?.Manifest.Title ?? "", "caption")), later.Col(1)).Margin(16, 0, 8, 8);
        View = body.Margin(16, 0, 16, 24);
        Choose();
    }

    private static TextBlock Wrapped(string text, string style = "muted") => Ui.Text(text, style).Also(t => t.TextWrapping = TextWrapping.Wrap);

    /// <summary>The first step: the trip's people, a new person, or nobody.</summary>
    private void Choose()
    {
        var str = s.Strings; body.Children.Clear();
        body.Children.Add(Wrapped(str["TripWelcomeBody"]));
        var people = s.Trip?.Entities.Values.Where(e => e.Type == "person").OrderBy(e => e.Title, StringComparer.CurrentCulture).ToArray() ?? [];
        foreach (var p in people)
        {
            var email = TripPeople.Emails(p).FirstOrDefault();
            var row = new Button
            {
                Content = Ui.Columns("Auto,*", Ui.Avatar(p.Title, 40).Margin(0, 0, 14, 0), Ui.V(2, Ui.Text(p.Title, "strong"), email is null ? null : Ui.Text(email, "caption"))),
                MinHeight = 60,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            }.Classed("row");
            AutomationProperties.SetName(row, p.Title);
            var id = p.Id;
            row.Click += async (_, _) => { person = id; newName = null; if (TripWelcome.NeedsEmail(p)) AskEmail(p.Title); else await Answer(null, false); };
            body.Children.Add(row);
        }
        // Adding oneself asks for the name first, filled in from the settings or the GitHub account.
        var name = new TextBox { Text = s.Settings.Name, PlaceholderText = str["YourName"] }; AutomationProperties.SetName(name, str["YourName"]);
        var add = Ui.Button(people.Length == 0 ? str["AddMe"] : str["AddMeAsNewPerson"], "plus", people.Length == 0 ? "primary" : "soft").Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch);
        add.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) { name.Focus(); return; }
            person = null; newName = name.Text.Trim(); AskEmail(newName);
        };
        body.Children.Add(Ui.Card(Ui.V(10, Ui.Field(str["YourName"], name), add), 14));
        var notInTrip = Ui.Button(str["NotPartOfTrip"], "user", "ghost").Also(b => b.HorizontalAlignment = HorizontalAlignment.Left);
        AutomationProperties.SetHelpText(notInTrip, str["NotPartOfTripHint"]);
        notInTrip.Click += async (_, _) => { person = null; newName = null; await Answer(null, false); };
        body.Children.Add(notInTrip);
    }

    /// <summary>The second step: the email versions are saved under, with whether to keep it for other trips.</summary>
    private void AskEmail(string who)
    {
        var str = s.Strings; body.Children.Clear();
        var back = Ui.Button(str["Back"], "chevron-left", "ghost", "compact").Also(b => b.HorizontalAlignment = HorizontalAlignment.Left);
        back.Click += (_, _) => Choose();
        body.Children.Add(back);
        body.Children.Add(Ui.Text(string.Format(System.Globalization.CultureInfo.CurrentCulture, str["TripWelcomeEmailTitle"], who), "strong"));
        body.Children.Add(Wrapped(str["TripWelcomeEmailBody"]));
        var email = new TextBox { Text = s.SuggestedEmail, PlaceholderText = "name@example.com" }; AutomationProperties.SetName(email, str["Email"]);
        Avalonia.Input.TextInput.TextInputOptions.SetContentType(email, Avalonia.Input.TextInput.TextInputContentType.Email);
        body.Children.Add(Ui.Field(str["Email"], email));
        if (s.PrivateGitHubEmail is { } hidden)
        {
            var useHidden = Ui.Button(str["UsePrivateGitHubEmail"], "eye-off", "ghost", "compact").Also(b => b.HorizontalAlignment = HorizontalAlignment.Left);
            AutomationProperties.SetHelpText(useHidden, hidden);
            useHidden.Click += (_, _) => email.Text = hidden;
            body.Children.Add(useHidden);
        }
        var remember = new CheckBox { IsChecked = true, Content = Ui.V(2, Ui.Text(str["RememberEmail"]), Wrapped(str["RememberEmailHint"], "caption")), IsVisible = s.OfferRememberEmail };
        body.Children.Add(remember);
        var problem = Wrapped("", "caption").Also(t => { t.IsVisible = false; t.Res(TextBlock.ForegroundProperty, "Danger"); });
        body.Children.Add(problem);
        var save = Ui.Button(str["Save"], "check", "primary").Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch);
        save.Click += async (_, _) =>
        {
            if (!CommitIdentity.LooksLikeEmail(email.Text)) { problem.Text = str["EmailInvalid"]; problem.IsVisible = true; return; }
            await Answer(email.Text, remember.IsVisible && remember.IsChecked == true);
        };
        var without = Ui.Button(str["ContinueWithoutEmail"], null, "ghost").Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch);
        without.Click += async (_, _) => await Answer(null, false);
        body.Children.Add(save); body.Children.Add(without);
    }

    private Task Answer(string? email, bool remember) => s.AnswerWelcomeAsync(person, newName, email, remember);
}
