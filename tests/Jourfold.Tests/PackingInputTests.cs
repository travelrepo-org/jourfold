using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Jourfold.Application;
using Jourfold.Mobile;
using Jourfold.UI;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Git;
using Xunit;
namespace Jourfold.Tests;

/// <summary>The packing line's shorthand: "@" people, "#" groups, "2x" quantities, suggestions while typing, and names nobody has.</summary>
public sealed class PackingInputTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "jourfold-packing-input-" + Guid.NewGuid());
    public void Dispose() => TestFiles.Delete(root);

    private static readonly Entity Sam = Entity.Create("person", "Sam Lee"), SamTwo = Entity.Create("person", "Sam Ito"), Ana = Entity.Create("person", "Ana Silva");

    private static (TripSnapshot Trip, Entity List) Trip(params Entity[] people)
    {
        var (item, list) = PackingLists.AddItem(PackingLists.CreateList("Beach"), "Towel", "First aid");
        return (new(Entity.CreateTrip("Coast", "en", "Europe/Berlin"), people.Concat([list, item]).ToDictionary(e => e.Id)), list);
    }

    [Fact]
    public void TheLineIsReadTheSameWayEverywhere()
    {
        var (trip, _) = Trip(Sam, Ana);
        var line = PackingInput.Read(trip, "2x Sunscreen @sam #First_aid");
        Assert.Equal(("Sunscreen", "First aid", "2"), (line.Title, line.Group, line.Quantity));
        Assert.Equal([Sam.Id], line.People); Assert.True(line.CanAdd);
        Assert.Equal("3", PackingInput.Read(trip, "Batteries x3").Quantity);
        Assert.Equal("2-person tent", PackingInput.Read(trip, "2-person tent").Title);
        Assert.True(PackingInput.Read(trip, "Water @alle").Everyone);

        // A name nobody has, or several people have, stops the line; it is never kept in the title.
        var unknown = PackingInput.Read(trip, "Rubber bands @Sarah");
        Assert.False(unknown.CanAdd); Assert.Equal(["Sarah"], unknown.Unknown); Assert.Equal("Rubber bands", unknown.Title);
        var (two, _) = Trip(Sam, SamTwo);
        Assert.Equal(["Sam"], PackingInput.Read(two, "Kite @Sam").Ambiguous);
        Assert.Equal([SamTwo.Id], PackingInput.Read(two, "Kite @Sam_Ito").People);
        Assert.Equal("Sam_Ito", PackingInput.Mention(two, SamTwo)); Assert.Equal("Ana", PackingInput.Mention(trip, Ana));
    }

    [Fact]
    public void SuggestionsFollowTheWordAtTheCaret()
    {
        var (trip, list) = Trip(Sam, SamTwo, Ana);
        Assert.Equal(["Everyone", "Ana Silva", "Sam Ito", "Sam Lee"], PackingInput.Suggest(trip, "Kite @", 6, "Everyone").Select(s => s.Label));
        var sam = PackingInput.Suggest(trip, "Kite @sa", 8, "Everyone");
        Assert.Equal(["Sam Ito", "Sam Lee"], sam.Select(s => s.Label));
        Assert.Equal(("Kite @Sam_Ito ", 14), PackingInput.Accept("Kite @sa", 8, sam[0]));
        // In the middle of the line, only the word at the caret changes.
        Assert.Equal(("Kite #First_aid @Ana", 16), PackingInput.Accept("Kite #fi @Ana", 8, PackingInput.Suggest(trip, "Kite #fi @Ana", 8, "Everyone").Single()));
        Assert.Empty(PackingInput.Suggest(trip, "Kite", 4, "Everyone"));
        Assert.Empty(PackingInput.Suggest(trip, "Kite @Zo", 8, "Everyone"));

        // Adding the missing people adds them as travellers and gives them the item.
        var (line, edits) = PackingInput.Add(trip, list.Id, "Rubber bands @Sarah", null, addPeople: true);
        Assert.True(line.CanAdd);
        var sarah = Assert.Single(edits, e => e.Value?.Type == "person").Value!;
        Assert.Equal("Sarah", sarah.Title); Assert.Equal([sarah.Id], line.People);
        Assert.Contains(edits, e => e.Id == trip.Manifest.Id);
        Assert.Empty(PackingInput.Add(trip, list.Id, "Rubber bands @Sarah", null).Edits);
    }

    [AvaloniaFact]
    public async Task ThePhoneSuggestsAndStopsANameNobodyHas()
    {
        var session = new MobileSession(new MobileHost(root, new GitCliBackend(), new FakeClock(Instant.FromUtc(2027, 5, 1, 9, 0)), () => DateTimeZoneProviders.Tzdb["Europe/Berlin"], "en", new MemorySecrets()));
        await session.OpenAsync(await session.CreateSampleAsync(new LocalDate(2027, 5, 14)));
        session.ShowTab(MobileTab.More); session.ShowPage("packing");
        var window = new Window { Width = 412, Height = 915, Content = new AppShell(session) }; window.Show();
        try
        {
            Probe.Layout();
            var line = Probe.All<PackingLineInput>(window).First(); var box = line.Box;
            box.Focus(); box.Text = "Kite @Sa"; box.CaretIndex = box.Text.Length; Probe.Layout();
            var chip = Probe.All<Button>(line).Single(b => AutomationProperties.GetName(b)?.StartsWith("Sam", StringComparison.Ordinal) == true);
            chip.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.StartsWith("Kite @Sam", box.Text);

            box.Text = "Rubber bands @Sarah";
            box.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter, Source = box });
            await Probe.Until(() => Probe.All<TextBlock>(window).Any(t => t.Text == "Nobody in the trip is called “Sarah”." && t.IsEffectivelyVisible));
            Assert.DoesNotContain(PackingLists.All(session.Trip!).SelectMany(l => l.Items), i => i.Task.Title.StartsWith("Rubber", StringComparison.Ordinal));
            Probe.Click(window, "Add Sarah to the trip and the item");
            await Probe.Until(() => PackingLists.All(session.Trip!).SelectMany(l => l.Items).Any(i => i.Task.Title == "Rubber bands" && i.Assignees.Any(a => a.Title == "Sarah")));
        }
        finally { window.Close(); session.Dispose(); }
    }

    private sealed class FakeClock(Instant now) : IClock { public Instant GetCurrentInstant() => now; }

    private sealed class MemorySecrets : TravelRepo.Providers.ISecretStore
    {
        private readonly Dictionary<string, string> values = [];
        public Task<string?> ReadAsync(string key, CancellationToken ct = default) => Task.FromResult(values.GetValueOrDefault(key));
        public Task WriteAsync(string key, string value, CancellationToken ct = default) { values[key] = value; return Task.CompletedTask; }
        public Task DeleteAsync(string key, CancellationToken ct = default) { values.Remove(key); return Task.CompletedTask; }
    }
}
