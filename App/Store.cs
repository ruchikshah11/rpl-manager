using System.Text.Encodings.Web;
using System.Text.Json;

namespace Rpl;

public sealed record BackupInfo(string Path, DateTime Saved, string Reason, int Players)
{
    public string Label => $"{Saved:dd MMM yyyy, HH:mm}  ·  {Players} players{(Reason.Length > 0 ? "  ·  " + Reason : "")}";
}

/// <summary>Everything saved: all seasons, plus which one is open.</summary>
public sealed class LeagueData
{
    public string CurrentYear { get; set; } = "";
    public List<Season> Seasons { get; set; } = [];
}

/// <summary>
/// %LOCALAPPDATA%\RPL\rpl.json, written after every change. Dated copies go to backups\
/// (before the first change of each day, and before imports, restores and new seasons).
/// </summary>
public sealed class Store
{
    // RPL_DATA_DIR lets tests (or a second league) use another folder.
    public static readonly string Folder = Environment.GetEnvironmentVariable("RPL_DATA_DIR") is { Length: > 0 } dir
        ? dir
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RPL");
    public static readonly string FilePath = Path.Combine(Folder, "rpl.json");
    public static readonly string BackupFolder = Path.Combine(Folder, "backups");
    private const int KeepBackups = 60;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // keep Gujarati readable
    };

    public LeagueData Data { get; private set; } = new();

    public Season Current => Data.Seasons.First(s => s.Year == Data.CurrentYear);

    public static Store Open()
    {
        var store = new Store();
        if (File.Exists(FilePath)) store.Data = Read(FilePath);
        store.EnsureSeason();
        return store;
    }

    private static LeagueData Read(string path) => JsonSerializer.Deserialize<LeagueData>(File.ReadAllText(path), Options) ?? new();

    private void EnsureSeason()
    {
        if (Data.Seasons.Count == 0) Data.Seasons.Add(new Season());
        if (Data.Seasons.All(s => s.Year != Data.CurrentYear)) Data.CurrentYear = Data.Seasons.Max(s => s.Year)!;
    }

    public List<string> Years() => Data.Seasons.Select(s => s.Year).Order().ToList();

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        DailyBackup();
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Data, Options));
        File.Move(temp, FilePath, overwrite: true);
    }

    /// <summary>Next season: same fee, contacts, form rules and (optionally) team names; no players or matches.</summary>
    public Season NewSeason(bool keepTeams)
    {
        var from = Current;
        string year = int.TryParse(Years().Last(), out int y) ? (y + 1).ToString() : DateTime.Today.Year.ToString();
        var s = new Season
        {
            Year = year,
            Settings = JsonSerializer.Deserialize<SeasonSettings>(JsonSerializer.Serialize(from.Settings, Options), Options)!,
            Teams = keepTeams ? from.Teams.Select(t => new Team { Name = t.Name, Group = t.Group }).ToList() : [],
        };
        s.Settings.FormDeadline = s.Settings.FormDeadline?.AddYears(1);
        s.Settings.StartDate = s.Settings.StartDate?.AddYears(1);
        s.Settings.EndDate = s.Settings.EndDate?.AddYears(1);
        Data.Seasons.Add(s);
        Data.CurrentYear = year;
        return s;
    }

    // --- backups -------------------------------------------------------------------------------

    private void DailyBackup()
    {
        if (!File.Exists(FilePath)) return;
        string daily = Path.Combine(BackupFolder, $"rpl_{DateTime.Now:yyyy-MM-dd}.json");
        if (File.Exists(daily)) return;
        Directory.CreateDirectory(BackupFolder);
        File.Copy(FilePath, daily);
        Prune();
    }

    public void Snapshot(string reason)
    {
        if (!File.Exists(FilePath)) return;
        Directory.CreateDirectory(BackupFolder);
        File.Copy(FilePath, Path.Combine(BackupFolder, $"rpl_{DateTime.Now:yyyy-MM-dd_HHmmss}_{reason}.json"), overwrite: true);
        Prune();
    }

    public List<BackupInfo> Backups()
    {
        if (!Directory.Exists(BackupFolder)) return [];
        return Directory.GetFiles(BackupFolder, "rpl_*.json").Select(path =>
        {
            var name = Path.GetFileNameWithoutExtension(path).Split('_', 4);
            string reason = name.Length >= 4 ? name[3].Replace('-', ' ') : "start of day";
            int players;
            try { players = Read(path).Seasons.Sum(s => s.Players.Count); } catch { players = 0; }
            return new BackupInfo(path, File.GetLastWriteTime(path), reason, players);
        }).OrderByDescending(b => b.Saved).ToList();
    }

    public void Restore(BackupInfo backup)
    {
        var restored = Read(backup.Path);
        Snapshot("before-restore");
        Data = restored;
        EnsureSeason();
        Save();
    }

    private static void Prune()
    {
        foreach (var old in new DirectoryInfo(BackupFolder).GetFiles("rpl_*.json").OrderByDescending(f => f.LastWriteTime).Skip(KeepBackups))
            old.Delete();
    }
}
