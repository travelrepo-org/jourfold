using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jourfold.Application;
using Jourfold.Desktop;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Merge;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Jourfold.Tests.TestBuilder))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace Jourfold.Tests;

public static class TestBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    { Environment.SetEnvironmentVariable("JOURFOLD_DATA_HOME", Path.Combine(Path.GetTempPath(), "jourfold-headless-" + Guid.NewGuid())); return AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }); }
}

/// <summary>Scripted interaction. Prompts, choices and quick-add titles come from <see cref="Answers"/> in order.</summary>
public sealed class TestInteraction : IInteraction
{
    public Queue<string?> Answers { get; } = new();
    public Queue<Func<QuickAddRequest, QuickAddDraft?>> QuickAdds { get; } = new();
    public List<string> Errors { get; } = [];
    public List<string> Toasts { get; } = [];
    public List<QuickAddRequest> QuickAddRequests { get; } = [];
    public ChangeSummary? LastVersionSummary { get; private set; }
    private string? Next(string? fallback = null) => Answers.Count > 0 ? Answers.Dequeue() : fallback;

    /// <summary>Answers the new-trip wizard; without it the next two answers become the title and the folder.</summary>
    public Func<NewTripDefaults, TripDraft?>? NewTrip { get; set; }
    public Task<TripDraft?> NewTripAsync(NewTripDefaults defaults) => Task.FromResult(NewTrip is { } answer ? answer(defaults) : new TripDraft(Next()!, "en", null, null, null, [], null, Next()));
    public Task<QuickAddDraft?> QuickAddAsync(QuickAddRequest request)
    {
        QuickAddRequests.Add(request);
        if (QuickAdds.Count > 0) return Task.FromResult(QuickAdds.Dequeue()(request));
        var title = Next(); return Task.FromResult<QuickAddDraft?>(title is null ? null : new QuickAddDraft(request.Kind, title) { Date = request.Start is null ? null : request.Date, Start = request.Start, Length = request.Length, Timezone = request.Zone });
    }
    public Task<string?> CreateVersionAsync(string suggestion, ChangeSummary changes) { LastVersionSummary = changes; return Task.FromResult<string?>(Next(suggestion)); }
    public Task<PaletteChoice?> PaletteAsync(MainViewModel model) => Task.FromResult<PaletteChoice?>(null);
    public Task<IReadOnlyList<ConflictAnswer>?> ResolveConflictsAsync(IReadOnlyList<ConflictPrompt> conflicts, string currentTitle, string otherTitle)
    {
        var answers = new List<ConflictAnswer>();
        foreach (var _ in conflicts) { var action = Next("UseCurrent")!; answers.Add(new(action, action == "EditResult" ? Next() : null)); }
        return Task.FromResult<IReadOnlyList<ConflictAnswer>?>(answers);
    }
    public Task ShareAsync(MainViewModel model) => Task.CompletedTask;
    public Task<string?> PromptAsync(string title, string initial = "", bool multiline = false) => Task.FromResult(Next(initial));
    public Task<string?> ChooseAsync(string title, IReadOnlyList<Choice> choices) => Task.FromResult(Next(choices.FirstOrDefault()?.Id));
    public Task<bool> ConfirmAsync(string message) => Task.FromResult(true);
    public Task<bool> ConfirmAsync(string title, string message, string confirm, bool danger = false) => Task.FromResult(true);
    public Task<string?> FolderAsync(string? title = null) => Task.FromResult(Next());
    public Task<string?> FileAsync(bool save = false, string? extension = null, string? suggestedName = null) => Task.FromResult(Next());
    public Task ShowAsync(string title, string message) { Errors.Add(message); return Task.CompletedTask; }
    public Task CompareAsync(TripSnapshot current, TripSnapshot incoming, string currentTitle, string otherTitle) => Task.CompletedTask;
    public Task CopyAsync(string value) => Task.CompletedTask;
    public Task<IReadOnlyDictionary<string, string>?> FormAsync(string title, IReadOnlyList<FormField> fields) => Task.FromResult<IReadOnlyDictionary<string, string>?>(fields.ToDictionary(f => f.Key, f => Next(f.Value) ?? ""));
    public Task<IReadOnlyList<string>?> SelectManyAsync(string title, IReadOnlyList<Choice> choices, IReadOnlyList<string> selected) => Task.FromResult<IReadOnlyList<string>?>(Answers.Count > 0 ? new[] { Answers.Dequeue()! } : selected);
    public Task<JsonObject?> ScheduleAsync(JsonObject? initial, LocalDate date, string zone) => Task.FromResult(initial);
    public Task<ZonedTime?> ResolveTimeAsync(string local, string zone, string? offset = null) => Task.FromResult<ZonedTime?>(new(local, zone, offset));
    public Task SettingsAsync(MainViewModel model) => Task.CompletedTask;
    public int AboutShown { get; private set; }
    public Task AboutAsync(MainViewModel model) { AboutShown++; return Task.CompletedTask; }
    public int AssistantShown { get; private set; }
    public Task ConnectAssistantAsync(MainViewModel model) { AssistantShown++; return Task.CompletedTask; }
    public void Toast(string message, string? action = null, Func<Task>? onAction = null) => Toasts.Add(message);
    public void Open(string path) { }
}

public static class Probe
{
    public static void Layout() => Dispatcher.UIThread.RunJobs();
    public static IEnumerable<T> All<T>(Visual root) where T : Visual => root.GetVisualDescendants().OfType<T>();
    public static T Named<T>(Visual root, string name) where T : Control => All<T>(root).First(c => c.Name == name);
    public static T Tagged<T>(Visual root, string tag) where T : Control => All<T>(root).First(c => c.Tag as string == tag);
    public static Control Sheet(MainWindow w) => (Control)w.FindControl<Grid>("DialogLayer")!.Children.Last();
    public static Button Button(Visual root, string label) => All<Button>(root).First(b => Avalonia.Automation.AutomationProperties.GetName(b) == label || b.Content as string == label);
    public static void Click(Visual root, string label) => Button(root, label).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    public static async Task Until(Func<bool> condition, int timeoutMs = 4000)
    {
        for (var waited = 0; waited < timeoutMs && !condition(); waited += 20) { await Task.Delay(20); Layout(); }
        Assert.True(condition(), "Condition not met in time.");
    }
}
