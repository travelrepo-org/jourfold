using Android;
using Android.Accounts;
using Android.Content;
using Android.Content.PM;
using Android.Provider;
using Jourfold.Mobile;

namespace Jourfold.Android;

/// <summary>
/// The trips in Android's calendar: an account "Jourfold" on this phone holds a calendar per trip (or per trip and
/// kind), which calendar apps show like any other and the user can hide one by one. The calendars are read-only and are
/// never uploaded; Jourfold writes them as the calendar provider's sync adapter, and only what changed.
/// </summary>
public sealed class AndroidCalendar(Context context) : ISystemCalendar
{
    public const string AccountType = "dev.jourfold.app";
    public static readonly Account Account = new("Jourfold", AccountType);
    private const string Authority = "com.android.calendar";
    private const int RequestCode = 4712;
    private static TaskCompletionSource<bool>? asking;

    // Column names of CalendarContract, written out: the binding spreads them over several nested classes.
    private const string Id = "_id", SyncId = "_sync_id", SyncData = "sync_data1", Name = "name", DisplayName = "calendar_displayName", CalendarColor = "calendar_color",
        AccessLevel = "calendar_access_level", Owner = "ownerAccount", Visible = "visible", SyncEvents = "sync_events", AccountName = "account_name", AccountTypeColumn = "account_type",
        CalendarId = "calendar_id", Title = "title", Description = "description", Location = "eventLocation", Status = "eventStatus", Start = "dtstart", End = "dtend",
        Zone = "eventTimezone", AllDay = "allDay", ColorKey = "eventColor_index", ColorIndex = "color_index", Color = "color", ColorType = "color_type", Organizer = "organizer";
    private const int ReadOnly = 200, Tentative = 0, Confirmed = 1, EventColor = 1;
    // Goes in front of each entry's fingerprint; a new number rewrites every entry once, when the way entries are
    // written changes.
    private const string Format = "2:";

    public bool SyncsFromSettings => true;
    public bool IsReadOnly => true;

    public async Task<bool> AllowAsync(bool ask, CancellationToken ct = default)
    {
        bool Granted() => context.CheckSelfPermission(Manifest.Permission.ReadCalendar) == Permission.Granted && context.CheckSelfPermission(Manifest.Permission.WriteCalendar) == Permission.Granted;
        if (Granted() || !ask || MainActivity.Current is not { } activity) return Granted();
        asking = new TaskCompletionSource<bool>();
        activity.RequestPermissions([Manifest.Permission.ReadCalendar, Manifest.Permission.WriteCalendar], RequestCode);
        return await asking.Task && Granted();
    }

    /// <summary>The user's answer to the permission question.</summary>
    public static void Answered(int requestCode, Permission[] results)
    {
        if (requestCode == RequestCode) asking?.TrySetResult(results.Length > 0 && results.All(r => r == Permission.Granted));
    }

    public Task PublishAsync(IReadOnlyList<SystemCalendar> calendars, CancellationToken ct = default) => Task.Run(() =>
    {
        EnsureAccount();
        var resolver = context.ContentResolver!;
        var colors = EnsureColors(resolver, calendars.SelectMany(c => c.Entries));
        var existing = Rows(resolver, AsAdapter(CalendarContract.Calendars.ContentUri!), [Id, Name], AccountName + "=? AND " + AccountTypeColumn + "=?", [Account.Name!, AccountType])
            .ToDictionary(r => r[1] ?? "", r => long.Parse(r[0]!, System.Globalization.CultureInfo.InvariantCulture));
        foreach (var calendar in calendars)
        {
            ct.ThrowIfCancellationRequested();
            var values = new ContentValues();
            values.Put(DisplayName, calendar.Name); values.Put(CalendarColor, unchecked((int)calendar.Color));
            if (!existing.TryGetValue(calendar.Key, out var id))
            {
                values.Put(Name, calendar.Key); values.Put(AccountName, Account.Name); values.Put(AccountTypeColumn, AccountType); values.Put(Owner, Account.Name);
                values.Put(AccessLevel, ReadOnly); values.Put(Visible, 1); values.Put(SyncEvents, 1);
                var uri = resolver.Insert(AsAdapter(CalendarContract.Calendars.ContentUri!), values) ?? throw new IOException("The calendar could not be added.");
                id = ContentUris.ParseId(uri);
            }
            else resolver.Update(AsAdapter(ContentUris.WithAppendedId(CalendarContract.Calendars.ContentUri!, id)), values, null, null);
            WriteEntries(resolver, id, calendar.Entries, colors);
        }
        // Calendars of trips that are gone, or of the other way of splitting, go with their entries.
        foreach (var (key, id) in existing.Where(c => calendars.All(k => k.Key != c.Key)))
            resolver.Delete(AsAdapter(ContentUris.WithAppendedId(CalendarContract.Calendars.ContentUri!, id)), null, null);
    }, ct);

    public Task TurnOffAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        // Removing the account removes its calendars; deleting them first does not wait for the provider to notice.
        context.ContentResolver!.Delete(AsAdapter(CalendarContract.Calendars.ContentUri!), AccountName + "=? AND " + AccountTypeColumn + "=?", [Account.Name!, AccountType]);
        AccountManager.Get(context)!.RemoveAccountExplicitly(Account);
    }, ct);

    /// <summary>
    /// Adds the account once. Its sync is on, because Android's "Sync now" asks only accounts whose sync is on, but
    /// without a schedule; the sync adapter acts only when the user asked (see <see cref="CalendarSyncService"/>).
    /// </summary>
    private void EnsureAccount()
    {
        if (AccountManager.Get(context)!.GetAccountsByType(AccountType).Length == 0) AccountManager.Get(context)!.AddAccountExplicitly(Account, null, null);
        ContentResolver.SetIsSyncable(Account, Authority, 1);
        ContentResolver.SetSyncAutomatically(Account, Authority, true);
        ContentResolver.RemovePeriodicSync(Account, Authority, new global::Android.OS.Bundle());
    }

    /// <summary>The kinds' colours, which events refer to by key. A colour of 0 keeps the one the calendar has.</summary>
    private static HashSet<string> EnsureColors(ContentResolver resolver, IEnumerable<SystemCalendarEntry> entries)
    {
        var uri = AsAdapter(CalendarContract.Colors.ContentUri!);
        var known = Rows(resolver, uri, [ColorIndex, Color], AccountName + "=? AND " + AccountTypeColumn + "=? AND " + ColorType + "=" + EventColor, [Account.Name!, AccountType])
            .ToDictionary(r => r[0] ?? "", r => r[1]);
        foreach (var (key, color) in entries.Where(e => e.Color != 0).Select(e => (e.ColorKey, e.Color)).Distinct())
        {
            var argb = unchecked((int)color).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (known.TryGetValue(key, out var have) && have == argb) continue;
            var values = new ContentValues(); values.Put(Color, unchecked((int)color));
            if (known.ContainsKey(key)) resolver.Update(uri, values, AccountName + "=? AND " + AccountTypeColumn + "=? AND " + ColorIndex + "=?", [Account.Name!, AccountType, key]);
            else
            {
                values.Put(AccountName, Account.Name); values.Put(AccountTypeColumn, AccountType); values.Put(ColorIndex, key); values.Put(ColorType, EventColor);
                resolver.Insert(uri, values);
            }
            known[key] = argb;
        }
        return [.. known.Keys];
    }

    /// <summary>Adds, changes and removes the calendar's entries so they match; unchanged entries are left alone.</summary>
    private static void WriteEntries(ContentResolver resolver, long calendar, IReadOnlyList<SystemCalendarEntry> entries, HashSet<string> colors)
    {
        var events = AsAdapter(CalendarContract.Events.ContentUri!);
        var have = Rows(resolver, events, [Id, SyncId, SyncData], CalendarId + "=?", [calendar.ToString(System.Globalization.CultureInfo.InvariantCulture)])
            .Where(r => r[1] is not null).GroupBy(r => r[1]!).ToDictionary(g => g.Key, g => g.ToArray());
        var operations = new List<ContentProviderOperation>();
        foreach (var entry in entries)
        {
            var fingerprint = Format + entry.Fingerprint;
            if (have.Remove(entry.Uid, out var rows))
            {
                // Duplicates of one entry should not exist; if they do, one stays.
                foreach (var extra in rows.Skip(1)) operations.Add(ContentProviderOperation.NewDelete(Row(events, extra[0]!))!.Build()!);
                if (rows[0][2] == fingerprint) continue;
                operations.Add(Fill(ContentProviderOperation.NewUpdate(Row(events, rows[0][0]!))!, entry, fingerprint, colors).Build()!);
            }
            else operations.Add(Fill(ContentProviderOperation.NewInsert(events)!.WithValue(CalendarId, calendar)!.WithValue(SyncId, entry.Uid)!, entry, fingerprint, colors).Build()!);
        }
        foreach (var row in have.Values.SelectMany(r => r)) operations.Add(ContentProviderOperation.NewDelete(Row(events, row[0]!))!.Build()!);
        // The provider takes a few hundred operations at a time.
        foreach (var chunk in operations.Chunk(200)) resolver.ApplyBatch(Authority, [.. chunk]);
    }

    private static ContentProviderOperation.Builder Fill(ContentProviderOperation.Builder builder, SystemCalendarEntry entry, string fingerprint, HashSet<string> colors)
    {
        // Some calendar apps expect the text columns and an organizer in every entry, as other accounts write them.
        builder.WithValue(SyncData, fingerprint)!.WithValue(Title, entry.Title)!.WithValue(Description, entry.Description ?? "")!.WithValue(Location, entry.Location ?? "")!
            .WithValue(Organizer, Account.Name)!.WithValue(Status, entry.Tentative ? Tentative : Confirmed);
        if (entry.Date is { } date)
        {
            // All-day entries are kept at midnight UTC, as the calendar provider expects.
            var start = date.AtMidnight().InUtc().ToInstant().ToUnixTimeMilliseconds();
            builder.WithValue(Start, start)!.WithValue(End, start + 86_400_000L)!.WithValue(AllDay, 1)!.WithValue(Zone, "UTC");
        }
        else builder.WithValue(Start, entry.Start!.Value.ToUnixTimeMilliseconds())!.WithValue(End, entry.End!.Value.ToUnixTimeMilliseconds())!.WithValue(AllDay, 0)!.WithValue(Zone, entry.Zone ?? "UTC");
        if (colors.Contains(entry.ColorKey)) builder.WithValue(ColorKey, entry.ColorKey);
        return builder;
    }

    private static global::Android.Net.Uri Row(global::Android.Net.Uri table, string id) => ContentUris.WithAppendedId(table, long.Parse(id, System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Written as the account's sync adapter, which may change read-only calendars and delete rows outright.</summary>
    private static global::Android.Net.Uri AsAdapter(global::Android.Net.Uri uri) => uri.BuildUpon()!
        .AppendQueryParameter(CalendarContract.CallerIsSyncadapter, "true")!.AppendQueryParameter(AccountName, Account.Name)!.AppendQueryParameter(AccountTypeColumn, AccountType)!.Build()!;

    private static List<string?[]> Rows(ContentResolver resolver, global::Android.Net.Uri uri, string[] columns, string selection, string[] arguments)
    {
        var rows = new List<string?[]>();
        using var cursor = resolver.Query(uri, columns, selection, arguments, null);
        while (cursor?.MoveToNext() == true) rows.Add(columns.Select((_, i) => cursor.GetString(i)).ToArray());
        return rows;
    }
}
