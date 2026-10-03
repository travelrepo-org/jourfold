using System.Globalization;
using System.Text.Json;
namespace Jourfold.Desktop;

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
    public string Diagnostic(string code) => strings.GetValueOrDefault("diagnostic." + code) ?? this["ValidationIssue"] + " (" + code + ")";
    public string Error(Exception exception, bool advanced = false) => advanced ? exception.Message : exception is TravelRepo.Core.DomainException domain ? Diagnostic(domain.Code) : this["OperationFailed"];
    public IReadOnlyDictionary<string, string> All => strings;
}
public sealed record Choice(string Id, string Label) { public override string ToString() => Label; }
public sealed record TripDraft(string Title, string Language, string? Start, string? End, string? Timezone, string[] Participants, string? RemoteUrl, string? Destination = null);
public sealed record FormField(string Key, string Label, string Kind = "text", string Value = "", IReadOnlyList<Choice>? Choices = null, bool Required = false);
public interface IInteraction
{
    Task<TripDraft?> NewTripAsync();
    Task<string?> PromptAsync(string title, string initial = "", bool multiline = false);
    Task<string?> ChooseAsync(string title, IReadOnlyList<Choice> choices);
    Task<bool> ConfirmAsync(string message);
    Task<string?> FolderAsync();
    Task<string?> FileAsync(bool save = false, string? extension = null);
    Task ShowAsync(string title, string message);
    Task CopyAsync(string value);
    Task CompareAsync(TravelRepo.Core.TripSnapshot current, TravelRepo.Core.TripSnapshot incoming);
    Task<IReadOnlyDictionary<string, string>?> FormAsync(string title, IReadOnlyList<FormField> fields);
    Task<IReadOnlyList<string>?> SelectManyAsync(string title, IReadOnlyList<Choice> choices, IReadOnlyList<string> selected);
    Task<System.Text.Json.Nodes.JsonObject?> ScheduleAsync(System.Text.Json.Nodes.JsonObject? initial, NodaTime.LocalDate date, string zone);
    Task<TravelRepo.Core.ZonedTime?> ResolveTimeAsync(string local, string zone, string? offset = null);
    Task SettingsAsync(MainViewModel model);
    void Open(string path);
}
