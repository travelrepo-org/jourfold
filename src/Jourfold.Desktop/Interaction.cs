using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jourfold.Application;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Merge;

namespace Jourfold.Desktop;

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
/// <summary>What the export dialog offers for the open trip, and the choices it starts with.</summary>
public sealed record ExportSetup(IReadOnlyList<Entity> People, Entity? Me, IReadOnlyList<PackingList> Lists, LocalDate? Start, LocalDate? End, bool OnlineMaps, ExportChoice Initial, string TripZone = "UTC", IReadOnlyList<string>? ZoneSuggestions = null)
{
    /// <summary>Time zones worth offering for an export in another zone: the second zone, those the trip uses, this computer's.</summary>
    public IReadOnlyList<string> Zones => ZoneSuggestions ?? [];
}
/// <summary>The choices made in the export dialog.</summary>
/// <param name="Kind"><c>plan</c> or <c>packing</c>.</param>
/// <param name="Format"><c>pdf</c>, <c>html</c>, <c>ics</c>, <c>txt</c>, <c>csv</c>, or <c>copy</c> for text on the clipboard.</param>
/// <param name="Options">Parts, dates, person, detail and paper of a trip plan; packing lists use only the paper size.</param>
/// <param name="Language">Language of the export's labels. Trip content is never translated.</param>
/// <param name="Lists">Packing lists to export.</param>
/// <param name="People">Packing lists for these people, one file each; empty for one list with everybody.</param>
/// <param name="UnassignedOnly">Only the packing items nobody brings yet.</param>
public sealed record ExportChoice(string Kind, string Format, TravelRepo.Repository.ExportOptions Options, string Language, IReadOnlyList<Guid> Lists, IReadOnlyList<Guid> People, bool UnassignedOnly);
/// <summary>What the pay function offers: the trip, the people to pay for, who starts chosen, and the local user.</summary>
public sealed record PayRequest(TripSnapshot Trip, IReadOnlyList<Entity> People, Guid Person, Guid? Me)
{
    /// <summary>The currency to pay in, or null for each expense's own; amounts charged convert exactly, others with <see cref="Rates"/> as estimates.</summary>
    public string? Currency { get; init; }
    public ExchangeRates? Rates { get; init; }
}
/// <summary>The open items a person chose to settle now.</summary>
public sealed record PayAnswer(Guid Person, IReadOnlyList<DebtKey> Items);
public sealed record FormField(string Key, string Label, string Kind = "text", string Value = "", IReadOnlyList<Choice>? Choices = null, bool Required = false, string? Hint = null);
public sealed record QuickAddRequest(string Kind, TripSnapshot Trip, LocalDate Date, LocalTime? Start, Duration? Length, string Zone, Guid? Person, bool ChooseType, string Currency)
{
    /// <summary>A title to start with, for example a booking's when its price becomes an expense.</summary>
    public string? Title { get; init; }
    /// <summary>An amount to start with, in <see cref="Currency"/>.</summary>
    public decimal? Amount { get; init; }
    /// <summary>What the new expense belongs to, such as the booking it pays for.</summary>
    public Guid? Related { get; init; }
    /// <summary>Whether the new expense is still to pay, such as the rest of a booking due on site.</summary>
    public bool Planned { get; init; }
}
public sealed record PaletteChoice(string Id, string Title, string? Subtitle, string Icon, string Group, string? Shortcut);
public sealed record ConflictPrompt(string Title, string Field, string Current, string Incoming, IReadOnlyList<string> Actions, string CurrentRaw);
public sealed record ConflictAnswer(string Action, string? Text = null);
/// <summary>How far a long task has come, for a progress bar: <paramref name="Done"/> of <paramref name="Total"/> steps, or an open-ended wait when the total is 0.</summary>
public sealed record WorkProgress(int Done, int Total, string Text);

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
    /// <summary>The pay function: choose which open items to settle now, seeing what they come to.</summary>
    Task<PayAnswer?> PayAsync(PayRequest request);
    Task<IReadOnlyList<string>?> PickAsync(string title, string message, IReadOnlyList<Choice> choices, IReadOnlyList<string> selected, string confirm);
    /// <summary>Asks what to export and how. Null when cancelled.</summary>
    Task<ExportChoice?> ExportAsync(ExportSetup setup);
    /// <summary>Asks which person of the trip the local user is. Null when cancelled.</summary>
    Task<WhoAmIAnswer?> WhoAmIAsync(WhoAmIRequest request);
    /// <summary>Shows a problem report to review and edit before it is copied or opened in the mail program. Null when cancelled.</summary>
    Task<ReportReview?> ReviewReportAsync(string text);
    /// <summary>Explains a failure, offers fixes and lets the person copy the technical details. Returns the chosen action ID.</summary>
    Task<string?> ProblemAsync(Problem problem);
    /// <summary>Shows release notes: only the highlights after an update, or every change for the version history.</summary>
    Task ReleaseNotesAsync(IReadOnlyList<ReleaseNote> releases, bool highlightsOnly);
    /// <summary>Shows a PDF, e-mail or image of the trip in the document window, next to the main window.</summary>
    void ViewDocument(Entity document);
    /// <summary>
    /// Runs <paramref name="work"/> while a sheet shows its progress. Cancel stops it through the token; the result is
    /// then <c>default</c>.
    /// </summary>
    Task<T?> ProgressAsync<T>(string title, Func<IProgress<WorkProgress>, CancellationToken, Task<T>> work);
    /// <summary>A short non-modal notice with an optional action such as Undo.</summary>
    void Toast(string message, string? action = null, Func<Task>? onAction = null);
    void Open(string path);
}
