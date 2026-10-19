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
    public string Error(Exception exception, bool advanced = false) =>
        exception is DomainException { Code: "schema.invalid" } schema ? Diagnostic(schema.Code) + SchemaDetails(schema.Message)
        : advanced ? exception.Message : exception is DomainException domain ? Diagnostic(domain.Code) : exception is HttpRequestException ? this["NetworkError"] : this["OperationFailed"];

    /// <summary>The failed rules of a schema validation result as short lines, for example "location: Required properties [longitude] are not present".</summary>
    private static string SchemaDetails(string json) => TravelRepo.Serialization.SchemaValidation.Explain(json, 5) is { Count: > 0 } lines ? "\n\n" + string.Join("\n", lines) : "";
    public IReadOnlyDictionary<string, string> All => strings;
}

/// <param name="Dependents">Entities deleted with the target because they cannot exist without it, such as comments.</param>
/// <param name="Affected">Entities that lose their link to the target.</param>
/// <param name="Unused">Places, files, notes and bookings that nothing uses after the deletion.</param>
/// <param name="Preselected">Unused entities suggested for deletion.</param>
public sealed record DeletionPrompt(Entity Target, IReadOnlyList<Entity> Dependents, IReadOnlyList<Entity> Affected, IReadOnlyList<Entity> Unused, IReadOnlyList<Guid> Preselected);
/// <param name="Details">Technical details for a bug report: versions, error codes and redacted Git output. Shown and copied, never sent.</param>
public sealed record Problem(string Title, string Message, string Details, IReadOnlyList<Choice> Actions);
public sealed record Choice(string Id, string Label, string? Icon = null, string? Hint = null) { public override string ToString() => Label; }
/// <summary>A problem report after review: the text as edited, and whether to send it by e-mail or only copy it.</summary>
public sealed record ReportReview(string Text, bool ByMail);
/// <param name="GitHubRepository">When set, the trip is published to a new private GitHub repository with this name.</param>
public sealed record TripDraft(string Title, string Language, string? Start, string? End, string? Timezone, string[] Participants, string? RemoteUrl, string? Destination = null, string? MyName = null, string? GitHubRepository = null);
public sealed record NewTripDefaults(string Folder, string Language, string Timezone, string? MyName, bool GitHubConnected = false, string? GitHubLogin = null);
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
    Task AboutAsync(MainViewModel model);
    /// <summary>Shows how to connect an AI assistant to the open trip over MCP.</summary>
    Task ConnectAssistantAsync(MainViewModel model);
    /// <summary>
    /// Asks before a deletion that changes other items or leaves some unused. Returns the IDs of unused entities to
    /// delete as well, or <c>null</c> when the person cancels.
    /// </summary>
    Task<IReadOnlyList<Guid>?> ConfirmDeletionAsync(DeletionPrompt prompt);
    /// <summary>A list of options with check boxes, all explained by <paramref name="message"/>. Returns the chosen IDs, or <c>null</c> when cancelled.</summary>
    Task<IReadOnlyList<string>?> PickAsync(string title, string message, IReadOnlyList<Choice> choices, IReadOnlyList<string> selected, string confirm);
    /// <summary>Shows a problem report to review and edit before it is copied or opened in the mail program. Null when cancelled.</summary>
    Task<ReportReview?> ReviewReportAsync(string text);
    /// <summary>Explains a failure, offers fixes and lets the person copy the technical details. Returns the chosen action ID.</summary>
    Task<string?> ProblemAsync(Problem problem);
    /// <summary>Shows release notes: only the highlights after an update, or every change for the version history.</summary>
    Task ReleaseNotesAsync(IReadOnlyList<ReleaseNote> releases, bool highlightsOnly);
    /// <summary>Shows a PDF, e-mail or image of the trip in the document window, next to the main window.</summary>
    void ViewDocument(Entity document);
    /// <summary>A short non-modal notice with an optional action such as Undo.</summary>
    void Toast(string message, string? action = null, Func<Task>? onAction = null);
    void Open(string path);
}
