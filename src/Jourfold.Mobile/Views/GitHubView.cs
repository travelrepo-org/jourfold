using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input.Platform;
using Avalonia.Media;
using TravelRepo.Providers;

namespace Jourfold.Mobile.Views;

/// <summary>
/// The GitHub parts of the app: signing in with a code, the trip's sync state on the sync page, and the list of
/// trips on GitHub that the trip list offers.
/// </summary>
public static class GitHubView
{
    /// <summary>Sign in, the code to enter while GitHub waits, or the account that is signed in.</summary>
    public static Control Account(MobileSession s)
    {
        var str = s.Strings;
        if (s.SignInCode is { } code) return Code(s, code);
        if (s.GitHubLogin is { } login)
        {
            var signOut = Ui.Button(str["SignOut"], null, "ghost");
            signOut.Click += async (_, _) => await s.SignOutAsync();
            return Ui.Card(Ui.Columns("Auto,*,Auto", Ui.Avatar(login, 36).Margin(0, 0, 12, 0),
                Ui.V(1, Ui.Text(login, "strong"), Ui.Text(str["SignedInToGitHub"], "caption")), signOut), 14);
        }
        var body = Ui.V(10, Ui.Text(str["GitHubSignInHint"], "caption"));
        TextBox? client = null;
        if (s.ClientId is null)
        {
            client = MorePages.Draft(s, "client", new TextBox { PlaceholderText = "Iv23…" });
            AutomationProperties.SetName(client, str["GitHubClient"]);
            body.Children.Add(Ui.Field(str["GitHubClient"], client));
            body.Children.Add(Ui.Text(str["GitHubClientHint"], "caption"));
        }
        if (s.SignInProblem is { } problem) body.Children.Add(Ui.Text(problem, "caption").Also(t => t.Res(TextBlock.ForegroundProperty, "Danger")));
        var start = Ui.Button(str["SignInWithGitHub"], null, "primary").Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch);
        start.Click += (_, _) => { start.IsEnabled = false; _ = s.SignInAsync(client?.Text); };
        body.Children.Add(start);
        return Ui.Card(body, 14);
    }

    /// <summary>The code to type on GitHub. Opening GitHub copies it, so it only needs pasting there.</summary>
    private static Control Code(MobileSession s, DeviceAuthorization code)
    {
        var str = s.Strings;
        var digits = Ui.Text(code.UserCode, "h1").Also(t => { t.FontFamily = FontFamily.Parse("monospace"); t.LetterSpacing = 2; t.HorizontalAlignment = HorizontalAlignment.Center; });
        var open = Ui.Button(str["OpenGitHub"], "external-link", "primary").Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch);
        open.Click += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(open);
            if (top?.Clipboard is { } clipboard) await clipboard.SetTextAsync(code.UserCode);
            if (top?.Launcher is { } launcher) await launcher.LaunchUriAsync(new Uri(code.VerificationUri));
        };
        var cancel = Ui.Button(str["Cancel"], null, "ghost").Also(b => b.HorizontalAlignment = HorizontalAlignment.Center);
        cancel.Click += (_, _) => s.CancelSignIn();
        return Ui.Card(Ui.V(12, Ui.Text(str["EnterCodeOnGitHub"], "strong"), digits, Ui.Text(str["CodeIsCopied"], "caption"), open,
            Ui.H(8, new ProgressBar { IsIndeterminate = true, Width = 24, MinWidth = 24, Height = 4 }, Ui.Text(str["WaitingForGitHub"], "caption")).Also(h => h.HorizontalAlignment = HorizontalAlignment.Center),
            cancel), 16);
    }

    /// <summary>Whether the open trip is on GitHub, when it last synced, and the buttons to sync or share it.</summary>
    public static Control TripState(MobileSession s)
    {
        var str = s.Strings;
        if (s.Remote is null)
        {
            var body = Ui.V(10, Ui.Columns("Auto,*", Ui.Icon("cloud-off", 22, "Text.Muted").Margin(0, 0, 14, 0),
                Ui.V(2, Ui.Text(str["OnlyOnThisPhone"], "strong"), Ui.Text(str["ShareOnGitHubHint"], "caption"))));
            if (s.GitHubLogin is not null)
            {
                var share = Ui.Button(str["ShareOnGitHub"], "upload", "soft").Also(b => b.HorizontalAlignment = HorizontalAlignment.Stretch);
                share.Click += (_, _) => { share.IsEnabled = false; _ = s.ShareOnGitHubAsync(); };
                body.Children.Add(share);
            }
            return Ui.Card(body, 16);
        }
        var detail = s.Syncing ? str["Syncing"] : s.SyncProblem ?? (s.LastSync is { } at ? string.Format(CultureInfo.CurrentCulture, str["SyncedWithGitHub"], Formats.Relative(at, str)) : str["NotSyncedYet"]);
        var icon = s.Syncing ? "refresh-cw" : s.SyncProblem is not null ? "cloud-off" : "cloud";
        var state = Ui.Columns("Auto,*", Ui.Icon(icon, 22, s.SyncProblem is not null ? "Warning" : "Accent").Margin(0, 0, 14, 0),
            Ui.V(2, Ui.Text(str["SharedOnGitHub"], "strong"), Ui.Text(detail, "caption")));
        var sync = Ui.Button(str["SyncNow"], "refresh-cw", "soft").Also(b => { b.HorizontalAlignment = HorizontalAlignment.Stretch; b.IsEnabled = !s.Syncing && s.GitHubLogin is not null; });
        sync.Click += (_, _) => _ = s.SyncAsync(quiet: false);
        return Ui.Card(Ui.V(12, state, sync), 16);
    }

    /// <summary>The trips this GitHub account can open, to bring one to this phone.</summary>
    public static Control RemoteTrips(MobileSession s, IReadOnlyList<RemoteRepository> trips)
    {
        var str = s.Strings;
        var back = Ui.Button(str["Trips"], "chevron-left", "ghost").Also(b => { b.HorizontalAlignment = HorizontalAlignment.Left; b.Padding = new Thickness(8, 8, 12, 8); });
        back.Click += (_, _) => s.CloseRemoteTrips();
        var page = Ui.V(14, back.Margin(0, 8, 0, 0), Ui.Text(str["TripsOnGitHub"], "h1").Margin(16, 0));
        if (trips.Count == 0) page.Children.Add(Ui.Text(str["NoGitHubTrips"], "muted").Margin(20, 0));
        var list = Ui.V(0);
        var problem = Ui.Text("", "caption").Also(t => { t.IsVisible = false; t.Res(TextBlock.ForegroundProperty, "Danger"); });
        foreach (var trip in trips)
        {
            var row = new Button
            {
                Content = Ui.Columns("Auto,*,Auto", Ui.Icon(trip.IsPrivate ? "lock" : "globe", 20, "Text.Muted").Margin(0, 0, 14, 0),
                    Ui.V(1, Ui.Text(trip.FullName.Split('/')[^1], "strong"), Ui.Text(trip.FullName + " · " + (trip.IsPrivate ? str["Private"] : str["Public"]), "caption")),
                    Ui.Icon("download", 18, "Text.Subtle"))
            }.Classed("row");
            AutomationProperties.SetName(row, trip.FullName);
            row.Click += async (_, _) =>
            {
                row.IsEnabled = false;
                try { await s.GetTripAsync(trip); }
                catch (TravelRepo.Core.DomainException ex) { row.IsEnabled = true; problem.Text = s.Strings.Error(ex); problem.IsVisible = true; }
            };
            if (list.Children.Count > 0) list.Children.Add(Ui.Divider());
            list.Children.Add(row);
        }
        if (trips.Count > 0) page.Children.Add(new Border { Child = list, ClipToBounds = true }.Classed("card").Margin(16, 0));
        page.Children.Add(problem.Margin(20, 0));
        page.Children.Add(Ui.Text(str["PublicTripsWarning"], "caption").Margin(20, 0, 20, 24));
        return Ui.Scroll(page);
    }
}
