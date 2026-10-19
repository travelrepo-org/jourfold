using System.Globalization;
using System.Net.Mail;
using System.Reflection;
using System.Text.Json.Nodes;

namespace Jourfold.Infrastructure;

/// <summary>
/// Where problem reports go: the public e-mail address of the GitHub account that publishes Jourfold, taken from the
/// repository address the build was made from, so forks report to themselves. It is looked up only when someone
/// chooses to report by e-mail, and remembered for a day.
/// </summary>
public static class SupportContact
{
    public const string EmailKey = "support.email", CheckedKey = "support.checked";
    /// <summary>Longer <c>mailto:</c> links are cut off by some mail programs and by Windows.</summary>
    public const int MaxMailtoLength = 1800;

    /// <summary>The GitHub account in the repository address, for example <c>travelrepo-org</c>.</summary>
    public static string? Owner(string? repositoryUrl = null)
    {
        repositoryUrl ??= typeof(SupportContact).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "RepositoryUrl")?.Value;
        return Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri) && uri.Host == "github.com" && uri.AbsolutePath.Trim('/').Split('/') is [{ Length: > 0 } owner, ..] ? owner : null;
    }

    /// <summary>The contact address, or null when the account has none public or GitHub cannot be reached.</summary>
    public static async Task<string?> EmailAsync(LocalStore store, HttpClient http, string? owner = null, CancellationToken ct = default)
    {
        if (DateTimeOffset.TryParse(store.Get(CheckedKey), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var checkedAt) && DateTimeOffset.UtcNow - checkedAt < TimeSpan.FromDays(1))
            return store.Get(EmailKey) is { Length: > 0 } cached ? cached : null;
        if ((owner ?? Owner()) is not { } account) return null;
        string? email = null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/users/" + Uri.EscapeDataString(account));
            OnlineIdentity.Apply(request); request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            email = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))?["email"]?.ToString()?.Trim();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) { return null; }
        if (email is not null && !MailAddress.TryCreate(email, out _)) email = null;
        store.Set(EmailKey, email ?? ""); store.Set(CheckedKey, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        return email;
    }

    /// <summary>
    /// A <c>mailto:</c> link with subject and body. When the body would make the link too long, the link carries
    /// <paramref name="shortBody"/> instead and <c>Complete</c> is false: the caller puts the full text on the clipboard.
    /// </summary>
    public static (string Link, bool Complete) MailTo(string to, string subject, string body, string shortBody)
    {
        string Link(string text) => "mailto:" + Uri.EscapeDataString(to).Replace("%40", "@") + "?subject=" + Uri.EscapeDataString(subject) + "&body=" + Uri.EscapeDataString(text.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        var full = Link(body);
        return full.Length <= MaxMailtoLength ? (full, true) : (Link(shortBody), false);
    }
}
