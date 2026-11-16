using Microsoft.Data.Sqlite;
using TravelRepo.Core;
namespace Jourfold.Infrastructure;

public sealed record RecentTrip(string Path, string Title, string Opened, string Changed, string State);
/// <summary>Rebuildable local preferences, recent library and full-text index. No canonical trip data is owned here.</summary>
public sealed class LocalStore : Jourfold.Application.IValueStore, IDisposable
{
    private readonly SqliteConnection db;
    public string Root { get; }
    public LocalStore(string? root = null)
    {
        Root = root ?? Environment.GetEnvironmentVariable("JOURFOLD_DATA_HOME") ?? Path.Combine(TravelRepo.Repository.TravelRepository.DefaultStateRoot(), "Jourfold"); Directory.CreateDirectory(Root);
        // One connection lives as long as the store. Without pooling, Dispose releases the file, so the folder can be removed.
        db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(Root, "local.db"), Pooling = false }.ToString()); db.Open();
        Execute("CREATE TABLE IF NOT EXISTS recent(path TEXT PRIMARY KEY,title TEXT,opened TEXT,changed TEXT,state TEXT); CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY,value TEXT);");
        // Earlier versions kept a copy of the trips' text here for the search. TravelRepo searches the trip itself now.
        Execute("DROP TABLE IF EXISTS search");
    }
    private void Execute(string sql, params (string, object?)[] values) { using var cmd = db.CreateCommand(); cmd.CommandText = sql; foreach (var (k, v) in values) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value); cmd.ExecuteNonQuery(); }
    public string? Get(string key) { using var c = db.CreateCommand(); c.CommandText = "SELECT value FROM settings WHERE key=$key"; c.Parameters.AddWithValue("$key", key); return c.ExecuteScalar() as string; }
    public void Set(string key, string value) => Execute("INSERT INTO settings VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=$v", ("$k", key), ("$v", value));
    public void Remember(string path, TripSnapshot trip, string state) => Execute("INSERT INTO recent VALUES($p,$t,$o,$c,$s) ON CONFLICT(path) DO UPDATE SET title=$t,opened=$o,changed=$c,state=$s", ("$p", path), ("$t", trip.Manifest.Title), ("$o", DateTimeOffset.Now.ToString("O")), ("$c", LastChanged(path).ToString("O")), ("$s", state));
    public static DateTime LastChanged(string path)
    {
        var files = new List<string> { Path.Combine(path, "travel.yaml") };
        foreach (var folder in TravelRepo.Repository.TravelRepository.Folders.Values.Distinct().Append("assets").Append(".travelrepo"))
        {
            var directory = Path.Combine(path, folder); if (Directory.Exists(directory)) files.AddRange(Directory.EnumerateFiles(directory, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }));
        }
        return files.Where(File.Exists).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();
    }
    public void Forget(string path) => Execute("DELETE FROM recent WHERE path=$p", ("$p", path));
    public IReadOnlyList<RecentTrip> Recent()
    { using var c = db.CreateCommand(); c.CommandText = "SELECT path,title,opened,changed,state FROM recent ORDER BY opened DESC"; using var r = c.ExecuteReader(); var list = new List<RecentTrip>(); while (r.Read()) list.Add(new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4))); return list; }
    public void CacheTravelEstimate(string trip, Guid from, Guid to, NodaTime.Duration duration)
    {
        Set("route:" + trip + ":" + from + ":" + to, duration.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
    public IReadOnlyList<TravelGapEstimate> TravelEstimates(string trip)
    {
        using var command = db.CreateCommand(); command.CommandText = "SELECT key,value FROM settings WHERE substr(key,1,length($prefix))=$prefix"; var prefix = "route:" + trip + ":"; command.Parameters.AddWithValue("$prefix", prefix);
        using var reader = command.ExecuteReader(); var result = new List<TravelGapEstimate>();
        while (reader.Read()) { var ids = reader.GetString(0)[prefix.Length..].Split(':'); if (ids.Length == 2 && Guid.TryParse(ids[0], out var from) && Guid.TryParse(ids[1], out var to) && double.TryParse(reader.GetString(1), System.Globalization.CultureInfo.InvariantCulture, out var seconds)) result.Add(new(from, to, NodaTime.Duration.FromSeconds(seconds))); }
        return result;
    }
    public void Dispose() => db.Dispose();
}
