using Avalonia.Headless.XUnit;
using Jourfold.Desktop;
using Jourfold.Infrastructure;
using TravelRepo.Core;
using TravelRepo.Git;
using TravelRepo.Repository;
using Xunit;
namespace Jourfold.Tests;

/// <summary>Taking a variant over into the main plan, deleting with a recycle bin, and cleaning up after others.</summary>
public sealed class VariantManagementTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-variants-" + Guid.NewGuid());
    private readonly GitCliBackend backend = new();
    public void Dispose() => TestFiles.Delete(root);
    private string Remote => Path.Combine(root, "remote.git");

    /// <summary>A shared trip: this computer's copy, opened in a view model, and a companion's clone.</summary>
    private async Task<(MainViewModel Model, TestInteraction Interaction, IDisposable Scope, TravelRepository Other, GitRepository OtherGit)> Shared()
    {
        var repo = new TravelRepository(Path.Combine(root, "mine")); await repo.InitializeAsync(Entity.CreateTrip("New Zealand", "en", "Pacific/Auckland"));
        var git = new GitRepository(repo, backend); await git.InitializeAsync("Me", "me@example.invalid");
        Directory.CreateDirectory(Remote); await backend.ExecuteAsync(Remote, ["init", "--bare", "-b", "main"]);
        await git.AddRemoteAsync("origin", Remote); await git.PushAsync("origin");
        var otherPath = Path.Combine(root, "other"); await GitRepository.CloneAsync(backend, Remote, otherPath);
        var other = new TravelRepository(otherPath, Path.Combine(root, "recovery-other")); var otherGit = new GitRepository(other, backend); await otherGit.SetIdentityAsync("Companion", "companion@example.invalid");
        var store = new LocalStore(Path.Combine(root, "local")); var interaction = new TestInteraction();
        var model = new MainViewModel(store, backend, new OsSecretStore(), interaction); await model.OpenAsync(repo.Root);
        return (model, interaction, new Both(model, store), other, otherGit);
    }
    private sealed class Both(IDisposable a, IDisposable b) : IDisposable { public void Dispose() { a.Dispose(); b.Dispose(); } }

    private static async Task Add(MainViewModel m, string title)
    {
        var item = Entity.Create("schedule_item", title); await m.Workspace!.EditAsync(item); await m.Workspace.Git.CreateVersionAsync("Add " + title);
    }

    [AvaloniaFact]
    public async Task AVariantIsTakenOverIntoTheMainPlanAndCanBeRestoredAfterDeleting()
    {
        var (m, interaction, scope, _, _) = await Shared(); using (scope)
        {
            await m.Workspace!.Git.CreateVariantAsync("variants/road-trip", "Road trip"); await m.Workspace.ReloadAsync();
            await Add(m, "Glow worm caves"); await m.Workspace.Git.PushAsync("origin");
            var variant = Assert.Single(await m.VariantOverviewAsync(), v => v.Title == "Road trip");

            await m.PromoteVariantAsync(variant);
            Assert.Equal("main", await m.Workspace.Git.CurrentBranchAsync());
            Assert.Contains(m.Workspace.State.Trip.Entities.Values, e => e.Title == "Glow worm caves");
            // Taken over, so deleting it online too was suggested and done.
            Assert.DoesNotContain(await m.VariantOverviewAsync(), v => v.Title == "Road trip");
            Assert.DoesNotContain("road-trip", (await backend.ExecuteAsync(Remote, ["for-each-ref", "refs/heads"])).Output);
            var deleted = Assert.Single(await m.DeletedVariantsAsync());
            Assert.Empty(interaction.Errors);

            await m.RestoreVariantAsync(deleted);
            var restored = Assert.Single(await m.VariantOverviewAsync(), v => v.Title == "Road trip");
            Assert.True(restored.Merged); Assert.True(restored.LocalOnly);
        }
    }

    [AvaloniaFact]
    public async Task VariantsDeletedOnlineAreOfferedForCleanUpAndKeptOnesStayLocal()
    {
        var (m, interaction, scope, _, otherGit) = await Shared(); using (scope)
        {
            var git = m.Workspace!.Git;
            await git.CreateVariantAsync("variants/cheap", "Cheap"); await git.PushAsync("origin");
            await git.SwitchAsync("main"); await git.CreateVariantAsync("variants/road-trip", "Road trip"); await git.PushAsync("origin");
            await m.Workspace.ReloadAsync(); await Add(m, "Only mine");
            await git.SwitchAsync("main"); await m.Workspace.ReloadAsync();

            // The companion deletes both variants online.
            await otherGit.FetchAsync("origin");
            foreach (var branch in new[] { "variants/cheap", "variants/road-trip" }) Assert.Equal(0, (await backend.ExecuteAsync(otherGit.Repository.Root, ["push", "origin", "--delete", branch])).ExitCode);

            await m.SyncCommand.ExecuteAsync(null);
            Assert.Equal(2, m.GoneVariants.Count);
            var reminder = Assert.Single(m.Reminders, r => r.Kind == ReminderKind.VariantsDeletedOnline);
            Assert.Contains("Cheap", m.ReminderText(reminder));

            await m.CleanUpVariantsCommand.ExecuteAsync(null);
            var left = await m.VariantOverviewAsync();
            Assert.DoesNotContain(left, v => v.Title == "Cheap");
            var kept = Assert.Single(left, v => v.Title == "Road trip");
            Assert.True(kept.LocalOnly); Assert.False(kept.UpstreamGone);
            Assert.Empty(m.GoneVariants); Assert.DoesNotContain(m.Reminders, r => r.Kind == ReminderKind.VariantsDeletedOnline);

            // A kept variant is not uploaded again by sync.
            await git.SwitchAsync("variants/road-trip"); await m.Workspace.ReloadAsync(); await m.SyncCommand.ExecuteAsync(null);
            Assert.DoesNotContain("road-trip", (await backend.ExecuteAsync(Remote, ["for-each-ref", "refs/heads"])).Output);
            Assert.Empty(interaction.Errors);
        }
    }
}
