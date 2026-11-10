using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jourfold.Application;
using NodaTime;
using TravelRepo.Core;
using TravelRepo.Merge;

namespace Jourfold.UI;

/// <summary>UI strings. English is the source language; German is complete. Missing keys fall back to the key.</summary>
public sealed class Localization
{
    private readonly Dictionary<string, string> strings;
    public string Language { get; }
    public Localization(string language)
    {
        Language = language == "de" ? "de" : "en";
        using var stream = typeof(Localization).Assembly.GetManifestResourceStream("Jourfold.UI.Strings." + Language + ".json")!; strings = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
    /// <summary>
    /// What the app runs on, for texts that name it: <c>phone</c>, <c>tablet</c> or <c>device</c>. Texts write
    /// <c>{device}</c> where it goes ("Only on this {device}"); in German all three words are neuter, so the sentence
    /// around them stays the same.
    /// </summary>
    public string Device { get; set; } = "phone";

    public string this[string key] => strings.GetValueOrDefault(key) is { } text ? text.Contains("{device}", StringComparison.Ordinal) ? text.Replace("{device}", strings.GetValueOrDefault("device." + Device) ?? Device, StringComparison.Ordinal) : text : key;
    /// <summary>Translate a canonical value such as a status when a label exists; otherwise show it unchanged.</summary>
    public string Optional(string value) => strings.GetValueOrDefault(value) ?? value;
    public string Diagnostic(string code) => strings.GetValueOrDefault("diagnostic." + code) ?? this["ValidationIssue"] + " (" + code + ")";
    public string Error(Exception exception, bool advanced = false) =>
        exception is DomainException { Code: "schema.invalid" } schema ? Diagnostic(schema.Code) + SchemaDetails(schema.Message)
        : advanced ? exception.Message : exception is DomainException domain ? Diagnostic(domain.Code) : exception is HttpRequestException ? this["NetworkError"] : this["OperationFailed"];

    /// <summary>The failed rules of a schema validation result as short lines, for example "location: Required properties [longitude] are not present".</summary>
    private static string SchemaDetails(string json) => TravelRepo.Serialization.SchemaValidation.Explain(json, 5) is { Count: > 0 } lines ? "\n\n" + string.Join("\n", lines) : "";
    public IReadOnlyDictionary<string, string> All => strings;

    /// <summary>
    /// Puts the words that Avalonia's time and date pickers show before a value is chosen, such as "hour" and
    /// "minute", into <paramref name="resources"/> in this language. The Fluent theme reads them from there and only
    /// has English ones.
    /// </summary>
    public void Pickers(Avalonia.Controls.IResourceDictionary resources)
    {
        foreach (var (resource, key) in PickerWords) resources[resource] = this[key];
    }

    private static readonly (string Resource, string Key)[] PickerWords =
    [
        ("StringTimePickerHourText", "PickerHour"), ("StringTimePickerMinuteText", "PickerMinute"), ("StringTimePickerSecondText", "PickerSecond"),
        ("StringDatePickerDayText", "PickerDay"), ("StringDatePickerMonthText", "PickerMonth"), ("StringDatePickerYearText", "PickerYear"),
    ];
}
