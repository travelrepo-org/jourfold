using System.Text.Json.Nodes;
using Jourfold.Application;
using TravelRepo.Core;
using Xunit;

namespace Jourfold.Tests;

/// <summary>The name and email versions are saved under: the trip's person, the settings, then GitHub.</summary>
public sealed class CommitIdentityTests
{
    private static Entity Person(string title, params (string Name, string Email)[] git)
    {
        var person = Entity.Create("person", title);
        if (git.Length > 0) person.Data["identities"] = new JsonObject { ["git"] = new JsonArray(git.Select(g => (JsonNode)new JsonObject { ["name"] = g.Name, ["email"] = g.Email }).ToArray()) };
        return person;
    }

    [Fact]
    public void TheEmailComesFromTheTripThenTheSettingsThenGitHub()
    {
        var gitHub = ("alex", (long?)1234567, (string?)"alex@public.invalid");
        var known = Person("Alex", ("Alex K.", "alex@trip.invalid"));
        Assert.Equal(("Alex K.", "alex@trip.invalid"), CommitIdentity.Resolve(known, "Alex", "alex@app.invalid", gitHub));
        // Of several addresses the person has, the one set in the app.
        var two = Person("Alex", ("Alex", "alex@laptop.invalid"), ("Alex", "alex@app.invalid"));
        Assert.Equal("alex@app.invalid", CommitIdentity.Resolve(two, "Alex", "alex@app.invalid", gitHub)?.Email);
        var unknown = Person("Alex");
        Assert.Equal(("Alex", "alex@app.invalid"), CommitIdentity.Resolve(unknown, "Alex", "alex@app.invalid", gitHub));
        Assert.Equal(("Alex", "alex@public.invalid"), CommitIdentity.Resolve(unknown, null, null, gitHub));
        Assert.Equal(("Alex", "1234567+alex@users.noreply.github.com"), CommitIdentity.Resolve(unknown, null, null, ("alex", 1234567, null)));
        // Without the account's number, GitHub's older private address.
        Assert.Equal(("Alex Example", "alex@users.noreply.github.com"), CommitIdentity.Resolve(null, null, " ", ("alex", null, null), "Alex Example"));
        Assert.Null(CommitIdentity.Resolve(unknown, "Alex", null, null));
    }

    [Fact]
    public void OnAComputerGitsOwnSettingsComeAfterTheAppAndBeforeGitHub()
    {
        var gitHub = ("alex", (long?)1234567, (string?)"alex@public.invalid");
        var unknown = Person("Alex");
        var gitConfig = ((string?)"Alex Git", (string?)"alex@git.invalid");
        Assert.Equal("alex@app.invalid", CommitIdentity.Resolve(unknown, "Alex", "alex@app.invalid", gitHub, gitConfig: gitConfig)?.Email);
        Assert.Equal(("Alex", "alex@git.invalid"), CommitIdentity.Resolve(unknown, null, null, gitHub, gitConfig: gitConfig));
        // The placeholder a computer makes from a name is no address.
        Assert.Equal("alex@git.invalid", CommitIdentity.Resolve(unknown, "Alex", "alex@localhost", gitHub, gitConfig: gitConfig)?.Email);
        Assert.Equal("alex@git.invalid", CommitIdentity.Suggest("alex@localhost", "alex@git.invalid", gitHub));
        Assert.Equal("1234567+alex@users.noreply.github.com", CommitIdentity.Suggest(null, null, ("alex", 1234567, null)));
        Assert.True(CommitIdentity.IsPlaceholder("sam@localhost"));
    }

    [Theory]
    [InlineData("alex@example.org", true)]
    [InlineData(" alex@example.org ", true)]
    [InlineData("alex@example", false)]
    [InlineData("alex example.org", false)]
    [InlineData("@example.org", false)]
    [InlineData("alex@.org", false)]
    [InlineData("", false)]
    public void AnEmailNeedsAnAtAndADomain(string text, bool email) => Assert.Equal(email, CommitIdentity.LooksLikeEmail(text));
}
