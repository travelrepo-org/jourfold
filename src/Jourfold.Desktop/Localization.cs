using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jourfold.Application;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Merge;

namespace Jourfold.Desktop;

/// <summary>UI strings. English is the source language; German is complete. Missing keys fall back to the key.</summary>
public sealed class Localization
{
    private readonly Dictionary<string, string> strings;
    public string Language { get; }
    public Localization(string language)
    {
        Language = language == "de" ? "de" : "en";
        using var stream = typeof(Localization).Assembly.GetManifestResourceStream("Jourfold.Desktop.Strings." + Language + ".json")!; strings = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
    public string this[string key] => strings.GetValueOrDefault(key) ?? key;
    /// <summary>Translate a canonical value such as a status when a label exists; otherwise show it unchanged.</summary>
    public string Optional(string value) => strings.GetValueOrDefault(value) ?? value;
    public string Diagnostic(string code) => strings.GetValueOrDefault("diagnostic." + code) ?? this["ValidationIssue"] + " (" + code + ")";
    public string Error(Exception exception, bool advanced = false) => advanced ? exception.Message : exception is DomainException domain ? Diagnostic(domain.Code) : exception is HttpRequestException ? this["NetworkError"] : this["OperationFailed"];
    public IReadOnlyDictionary<string, string> All => strings;
}

public sealed record Choice(string Id, string Label, string? Icon = null, string? Hint = null) { public override string ToString() => Label; }
public sealed record TripDraft(string Title, string Language, string? Start, string? End, string? Timezone, string[] Participants, string? RemoteUrl, string? Destination = null, string? MyName = null);
public sealed record NewTripDefaults(string Folder, string Language, string Timezone, string? MyName);
public sealed record FormField(string Key, string Label, string Kind = "text", string Value = "", IReadOnlyList<Choice>? Choices = null, bool Required = false, string? Hint = null);
public sealed record QuickAddRequest(string Kind, TripSnapshot Trip, LocalDate Date, LocalTime? Start, Duration? Length, string Zone, Guid? Person, bool ChooseType, string Currency);
public sealed record PaletteChoice(string Id, string Title, string? Subtitle, string Icon, string Group, string? Shortcut);
public sealed record ConflictPrompt(string Title, string Field, string Current, string Incoming, IReadOnlyList<string> Actions, string CurrentRaw);
public sealed record ConflictAnswer(string Action, string? Text = null);

/// <summary>Everything that needs the user. Implemented by in-window dialogs; tests use a scripted double.</summary>
public interface IInteraction
{
    Task<TripDraft?> NewTripAsync(NewTripDefaults defaults);
    Task<QuickAddDraft?> QuickAddAsync(QuickAddRequest request);
    Task<string?> CreateVersionAsync(string suggestion, ChangeSummary changes);
    Task<PaletteChoice?> PaletteAsync(MainViewModel model);
    Task<IReadOnlyList<ConflictAnswer>?> ResolveConflictsAsync(IReadOnlyList<ConflictPrompt> conflicts, string currentTitle, string otherTitle);
    Task ShareAsync(MainViewModel model);
    Task<string?> PromptAsync(string title, string initial = "", bool multiline = false);
    Task<string?> ChooseAsync(string title, IReadOnlyList<Choice> choices);
    Task<bool> ConfirmAsync(string message);
    Task<bool> ConfirmAsync(string title, string message, string confirm, bool danger = false);
    Task<string?> FolderAsync(string? title = null);
    Task<string?> FileAsync(bool save = false, string? extension = null, string? suggestedName = null);
    Task ShowAsync(string title, string message);
    Task CopyAsync(string value);
    Task CompareAsync(TripSnapshot current, TripSnapshot incoming, string currentTitle, string otherTitle);
    Task<IReadOnlyDictionary<string, string>?> FormAsync(string title, IReadOnlyList<FormField> fields);
    Task<IReadOnlyList<string>?> SelectManyAsync(string title, IReadOnlyList<Choice> choices, IReadOnlyList<string> selected);
    Task<JsonObject?> ScheduleAsync(JsonObject? initial, LocalDate date, string zone);
    Task<ZonedTime?> ResolveTimeAsync(string local, string zone, string? offset = null);
    Task SettingsAsync(MainViewModel model);
    /// <summary>A short non-modal notice with an optional action such as Undo.</summary>
    void Toast(string message, string? action = null, Func<Task>? onAction = null);
    void Open(string path);
}
