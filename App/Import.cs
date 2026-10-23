using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace Rpl;

/// <summary>What was read from a season folder, and anything worth checking afterwards.</summary>
public sealed class ImportResult
{
    public required Season Season { get; init; }
    public List<string> Files { get; } = [];
    public List<string> Notes { get; } = [];
}

/// <summary>
/// Reads an existing season folder: the players/accounts workbook (table "Players"), the awards workbook
/// (columns "Match No" and "POM") and the fixtures document (first table of Match.docx, for dates and times).
/// </summary>
public static partial class Import
{
    public static ImportResult FromFolder(string folder)
    {
        var season = new Season();
        var result = new ImportResult { Season = season };
        var fixtures = new List<Fixture>();

        foreach (var path in Directory.GetFiles(folder, "*.xlsx").Where(p => !Path.GetFileName(p).StartsWith("~$")))
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var wb = new XLWorkbook(stream);
            var tables = wb.Worksheets.SelectMany(w => w.Tables).ToList();
            if (tables.Any(t => t.Name == "Players"))
            {
                ReadPlayersWorkbook(wb, season, result);
                result.Files.Add(Path.GetFileName(path));
                if (YearPattern().Match(Path.GetFileNameWithoutExtension(path)) is { Success: true } y) season.Year = y.Value;
            }
            else if (tables.FirstOrDefault(t => Headers(t).Contains("POM") && Headers(t).Contains("Match No")) is { } awards)
            {
                ReadAwards(awards, season);
                result.Files.Add(Path.GetFileName(path));
            }
        }

        string? docx = Directory.GetFiles(folder, "*.docx").FirstOrDefault(p => Path.GetFileName(p).StartsWith("Match", StringComparison.OrdinalIgnoreCase));
        if (docx is not null)
        {
            fixtures = ReadFixtures(docx);
            if (fixtures.Count > 0) result.Files.Add(Path.GetFileName(docx));
        }

        if (season.Matches.Count > 0) AttachDates(season, fixtures, result);
        else if (fixtures.Count > 0) season.Matches = fixtures.Select((f, i) => new Match
            { No = i + 1, Date = f.Date, Time = f.Time, TeamA = f.A, TeamB = f.B, Stage = f.Stage }).ToList();

        // Girls' and kids' matches are a league of their own division.
        foreach (var m in season.Matches)
        {
            if (m.Stage == "Girls") (m.Stage, m.Division) = ("League", "Women");
            else if (m.Stage == "Kids") (m.Stage, m.Division) = ("League", "Kids");
            else if (m.Division.Length == 0) m.Division = "Men";
        }

        // Teams: from the players' Team column (captain names) and from league matches (team names).
        foreach (var name in season.Players.Where(p => !p.Cancelled).Select(p => p.Team).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var members = season.Players.Where(p => Rules.SameTeam(p.Team, name)).ToList();
            var captain = members.FirstOrDefault(p => p.Name.Split(' ')[0].Equals(name, StringComparison.OrdinalIgnoreCase));
            string division = members.GroupBy(p => p.Division).OrderByDescending(g => g.Count()).First().Key;
            season.Teams.Add(new Team { Name = name, Captain = captain?.Name ?? "", Division = division });
        }
        foreach (var m in season.Matches.Where(m => m.Stage == "League"))
            foreach (var name in new[] { m.TeamA, m.TeamB }.Where(n => n.Length > 0))
                if (!season.Teams.Any(t => Rules.SameTeam(t.Name, name))) season.Teams.Add(new Team { Name = name, Division = m.Division });

        if (season.Players.Any(p => p.Team.Length > 0) && season.Matches.Count > 0 &&
            !season.Players.Any(p => season.Matches.Any(m => Rules.SameTeam(m.TeamA, p.Team) || Rules.SameTeam(m.TeamB, p.Team))))
            result.Notes.Add("Players' Team column uses captain names while matches use team names. Link them on the Teams page: rename a captain team (e.g. \"Ruchik\") to its full team name and the two merge.");
        if (season.Matches.Count > 0 && season.Matches.All(m => !m.Played))
            result.Notes.Add("No match winners were in the files. Add each winner on the Matches page to fill the points table.");
        return result;
    }

    private static HashSet<string> Headers(IXLTable t) => t.HeadersRow().Cells().Select(c => c.GetString().Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

    // --- online registrations (Google Forms responses, .csv or .xlsx) --------------------------

    public sealed record ResponsesResult(List<Player> New, int Duplicates, int Skipped, Dictionary<string, string> Columns);

    /// <summary>
    /// Reads a form-responses sheet. Columns are recognised by their heading (Name, Age, Block / Wing, Flat,
    /// Mobile / Phone / WhatsApp, Gender, Payment / Paid / UPI, Timestamp). Players already in the season
    /// (same mobile, or same name and flat) are skipped.
    /// </summary>
    public static ResponsesResult Responses(string path, Season season)
    {
        var rows = path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? ReadCsv(path) : ReadSheet(path);
        if (rows.Count < 2) return new([], 0, 0, []);
        var head = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Find(params string[] words) => head.FindIndex(h => words.Any(h.Contains));
        var cols = new Dictionary<string, int>
        {
            ["Name"] = head.FindIndex(h => h.Contains("name") && !h.Contains("team")),
            ["Age"] = Find("age"),
            ["Block"] = Find("block", "wing", "tower"),
            ["Flat"] = Find("flat", "house", "unit"),
            ["Mobile"] = Find("mobile", "phone", "whatsapp", "contact"),
            ["Gender"] = Find("gender", "male", "female"),
            ["Payment"] = Find("payment", "paid", "upi", "fee"),
            ["Timestamp"] = Find("timestamp", "date"),
        };
        string Cell(List<string> r, string key) => cols[key] is int i && i >= 0 && i < r.Count ? Rules.Clean(r[i]) : "";

        var fresh = new List<Player>();
        int dup = 0, skipped = 0;
        foreach (var r in rows.Skip(1))
        {
            string name = Rules.TitleCase(Cell(r, "Name"));
            if (name.Length == 0) { skipped++; continue; }
            string mobile = new string(Cell(r, "Mobile").Where(char.IsDigit).ToArray());
            if (mobile.Length == 12 && mobile.StartsWith("91")) mobile = mobile[2..];
            string flat = Cell(r, "Flat"), block = Cell(r, "Block").ToUpperInvariant();
            // "B-904" / "B/904" typed in one box
            if (block.Length == 0 && flat.Length > 1 && char.IsLetter(flat[0]) && (flat[1] is '-' or '/' or ' ')) (block, flat) = (flat[..1].ToUpperInvariant(), flat[2..].Trim());
            string g = Cell(r, "Gender").ToLowerInvariant();
            string gender = g.StartsWith("f") || g.Contains("girl") || g.Contains("women") ? "Female" : g.Contains("kid") || g.Contains("child") ? "Kids" : "Male";
            string pay = Cell(r, "Payment").ToLowerInvariant();
            var p = new Player
            {
                Name = name, Age = new string(Cell(r, "Age").Where(char.IsDigit).ToArray()), Block = block, Flat = flat, Mobile = mobile, Gender = gender,
                Payment = gender != "Male" ? "" : pay.Contains("online") || pay.Contains("upi") || pay.Contains("gpay") || pay.Contains("paytm") ? "Online"
                    : pay.Contains("cash") ? "Cash" : "Pending",
                Notes = "Online registration",
                Registered = DateTime.TryParse(Cell(r, "Timestamp"), CultureInfo.GetCultureInfo("en-IN"), DateTimeStyles.None, out var ts) ? ts.Date : DateTime.Today,
            };
            bool exists = season.Players.Concat(fresh).Any(x => (mobile.Length > 0 && x.Mobile == mobile) ||
                (x.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && x.Flat == flat));
            if (exists) { dup++; continue; }
            fresh.Add(p);
        }
        var found = cols.Where(c => c.Value >= 0).ToDictionary(c => c.Key, c => rows[0][c.Value].Trim());
        return new(fresh, dup, skipped, found);
    }

    private static List<List<string>> ReadSheet(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.First();
        var used = ws.RangeUsed();
        if (used is null) return [];
        return used.Rows().Select(r => r.Cells().Select(c => c.GetFormattedString()).ToList()).ToList();
    }

    /// <summary>CSV with quoted fields (commas, quotes and line breaks inside quotes).</summary>
    private static List<List<string>> ReadCsv(string path)
    {
        string text = File.ReadAllText(path);
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new System.Text.StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { row.Add(cell.ToString()); cell.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString()); cell.Clear();
                if (row.Any(x => x.Length > 0)) rows.Add(row);
                row = [];
            }
            else cell.Append(c);
        }
        row.Add(cell.ToString());
        if (row.Any(x => x.Length > 0)) rows.Add(row);
        return rows;
    }

    // --- players / accounts workbook -------------------------------------------------------------

    private static void ReadPlayersWorkbook(XLWorkbook wb, Season season, ImportResult result)
    {
        var players = wb.Worksheets.SelectMany(w => w.Tables).First(t => t.Name == "Players");
        var cols = players.HeadersRow().Cells().ToDictionary(c => c.GetString().Trim(), c => c.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase);
        var ws = players.Worksheet;
        string Get(int r, string h) => cols.TryGetValue(h, out int c) ? Rules.Clean(ws.Cell(r, c).GetFormattedString()) : "";
        int skipped = 0;

        for (int r = players.RangeAddress.FirstAddress.RowNumber + 1; r <= players.RangeAddress.LastAddress.RowNumber; r++)
        {
            string name = Get(r, "Name");
            if (name.Length == 0) continue;
            if (Get(r, "Sponsor").Equals("Yes", StringComparison.OrdinalIgnoreCase))
            {
                decimal.TryParse(Get(r, "Sponsor Amount"), NumberStyles.Any, CultureInfo.InvariantCulture, out var amount);
                season.Sponsors.Add(new Sponsor { Name = Rules.TitleCase(name), Amount = amount, Mode = Get(r, "IsOnline") == "Yes" ? "Online" : "Cash", Notes = Get(r, "Description") });
                continue;
            }
            string gender = Get(r, "Gender");
            if (gender.Length == 0) { skipped++; continue; }   // e.g. "Shree Ganesh" - an opening entry, not a player
            bool cancelled = gender.Equals("Cancelled", StringComparison.OrdinalIgnoreCase);
            string online = Get(r, "IsOnline");
            string team = Get(r, "Team"), cat = Get(r, "Category");
            var p = new Player
            {
                FormNo = Get(r, "Form No"),
                Name = Rules.TitleCase(name),
                Age = Get(r, "Age"),
                Block = Get(r, "Block").ToUpperInvariant(),
                Flat = Get(r, "Flat Number"),
                Mobile = Get(r, "Mobile"),
                Gender = cancelled ? "Male" : Lists.Genders.FirstOrDefault(g => g.Equals(gender, StringComparison.OrdinalIgnoreCase)) ?? "Male",
                Cancelled = cancelled,
                Team = team is "-" ? "" : team,
                Category = cat is "-" ? "" : cat,
                Notes = Get(r, "Description") is "*" ? "" : Get(r, "Description"),
            };
            p.Payment = online.Equals("Yes", StringComparison.OrdinalIgnoreCase) ? "Online" : online.Equals("No", StringComparison.OrdinalIgnoreCase) ? "Cash" : Rules.PaysFee(p) ? "Pending" : "";
            if (p.Payment == "Online" && p.Notes.Contains("UPI", StringComparison.OrdinalIgnoreCase)) { p.PaidTo = p.Notes; p.Notes = ""; }
            season.Players.Add(p);
        }
        if (skipped > 0) result.Notes.Add($"{skipped} row(s) without a Gender were skipped (for example \"Shree Ganesh\").");

        // Fee: the number multiplied in the Accounts fee formula, or the "Fee" cell.
        if (wb.DefinedNames.TryGetValue("Fee", out var fee) && fee.Ranges.FirstOrDefault()?.FirstCell().GetValue<double>() is double f and > 0) season.Settings.Fee = (int)f;
        else if (wb.Worksheets.SelectMany(w => w.Tables).FirstOrDefault(t => Headers(t).Contains("Online Amount")) is { } acc)
        {
            var cell = acc.DataRange.FirstRow().Cells().FirstOrDefault(c => c.HasFormula && FeePattern().IsMatch(c.FormulaA1));
            if (cell is not null) season.Settings.Fee = int.Parse(FeePattern().Match(cell.FormulaA1).Groups[1].Value);
        }

        // Expenses.
        if (wb.Worksheets.SelectMany(w => w.Tables).FirstOrDefault(t => t.Name == "Expenses") is { } ex)
        {
            var ec = ex.HeadersRow().Cells().ToDictionary(c => c.GetString().Trim(), c => c.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase);
            var ews = ex.Worksheet;
            string E(int r, string h) => ec.TryGetValue(h, out int c) ? Rules.Clean(ews.Cell(r, c).GetFormattedString()) : "";
            for (int r = ex.RangeAddress.FirstAddress.RowNumber + 1; r <= ex.RangeAddress.LastAddress.RowNumber; r++)
            {
                string item = E(r, "Expenses");
                if (item.Length == 0) continue;
                decimal.TryParse(ews.Cell(r, ec.GetValueOrDefault("Amount")).GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var amount);
                string mode = E(r, "Description");
                season.Expenses.Add(new Expense
                {
                    Item = item, PaidBy = E(r, "Paid By"), Amount = amount, Paid = !E(r, "Payed").Equals("No", StringComparison.OrdinalIgnoreCase),
                    Mode = mode.Equals("Online", StringComparison.OrdinalIgnoreCase) ? "Online" : "Cash",
                    Notes = mode is "Cash" or "Online" or "" ? "" : mode,
                });
            }
        }

        // Cash count.
        if (wb.Worksheets.SelectMany(w => w.Tables).FirstOrDefault(t => t.Name == "Currency") is { } cur)
            foreach (var row in cur.DataRange.Rows())
                if (int.TryParse(row.Cell(1).GetString().Trim(), out int note) && int.TryParse(row.Cell(2).GetString().Trim(), out int n) && n > 0)
                    season.Cash[note] = n;
    }

    // --- awards workbook ---------------------------------------------------------------------------

    private static void ReadAwards(IXLTable t, Season season)
    {
        var cells = t.HeadersRow().Cells().ToList();
        int Col(string h) => cells.FindIndex(c => c.GetString().Trim().Equals(h, StringComparison.OrdinalIgnoreCase));
        int no = Col("Match No"), a = Col("Team A"), b = Col("Team B"), pom = Col("POM");

        foreach (var row in t.DataRange.Rows())
        {
            string A = Rules.Clean(row.Cell(a + 1).GetString()), B = Rules.Clean(row.Cell(b + 1).GetString());
            if (A.Length == 0 && B.Length == 0) continue;
            var m = new Match { TeamA = A, TeamB = B, Stage = "League" };
            m.No = int.TryParse(row.Cell(no + 1).GetString(), out int n) ? n : season.Matches.Count + 1;

            // Player columns are followed by a description column: (POM, Description), (Award, Description2), ...
            for (int i = pom; i < cells.Count - 1; i += 2)
            {
                string player = Rules.TitleCase(row.Cell(i + 1).GetString());
                string what = Rules.Clean(row.Cell(i + 2).GetString());
                if (player.Length == 0) continue;
                foreach (var title in Titles(what, i == pom)) m.Awards.Add(new Award { Player = player, Title = title });
            }
            season.Matches.Add(m);
        }
        // Re-number in order if the sheet used letters (e.g. "W" for the women's final).
        for (int i = 0; i < season.Matches.Count; i++) season.Matches[i].No = i + 1;
    }

    /// <summary>"POM, Best Batter and Best Bowler" -> Player of the Match, Best Batter, Best Bowler.</summary>
    public static List<string> Titles(string text, bool isPom)
    {
        var list = new List<string>();
        foreach (var part in SplitPattern().Split(text).Select(x => x.Trim()).Where(x => x.Length > 0))
        {
            string t = part.ToLowerInvariant() switch
            {
                "pom" or "player of the match" or "man of the match" => "Player of the Match",
                var x when x.Contains("batter") || x.Contains("batsman") => "Best Batter",
                var x when x.Contains("bowler") => "Best Bowler",
                var x when x.Contains("fighter") => "Fighter of the Match",
                var x when x.Contains("fielder") => "Best Fielder",
                _ => Rules.TitleCase(part),
            };
            if (!list.Contains(t)) list.Add(t);
        }
        if (isPom && !list.Contains("Player of the Match")) list.Insert(0, "Player of the Match");
        return list;
    }

    // --- fixtures document (Match.docx) -----------------------------------------------------------

    private sealed record Fixture(DateTime? Date, string Time, string A, string B, string Stage, string Text);

    private static List<Fixture> ReadFixtures(string docx)
    {
        using var zip = ZipFile.OpenRead(docx);
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        string xml = reader.ReadToEnd();
        var table = TablePattern().Match(xml);
        if (!table.Success) return [];

        var rows = RowPattern().Matches(table.Value)
            .Select(r => CellPattern().Matches(r.Value).Select(c => WebUtility.HtmlDecode(string.Concat(TextPattern().Matches(c.Value).Select(t => t.Groups[1].Value))).Trim()).ToList())
            .ToList();
        if (rows.Count < 2) return [];
        var head = rows[0];
        var list = new List<Fixture>();
        foreach (var row in rows.Skip(1))
        {
            if (row.Count < 2) continue;
            DateTime? date = ParseDate(row[0]);
            for (int i = 1; i < Math.Min(row.Count, head.Count) - (head[^1].StartsWith("Stage", StringComparison.OrdinalIgnoreCase) ? 1 : 0); i++)
            {
                string text = row[i];
                if (text.Length == 0 || text is "—" or "-") continue;
                string time = head[i].Replace(" ", "").Replace("PM", " PM").Trim();
                // Only league cells name real teams; playoff, girls and kids cells are placeholders
                // ("Eliminator (3rd vs 4th)", "Girls Team – 1 vs Girls Team – 2").
                var vs = VsPattern().Match(text);
                string stage = StageOf(text);
                bool real = vs.Success && stage == "League" && !text.Contains('(');
                list.Add(new Fixture(date, time, real ? vs.Groups[1].Value.Trim() : "", real ? vs.Groups[2].Value.Trim() : "", stage, text));
            }
        }
        return list;
    }

    /// <summary>Stage from the cell text. Qualifiers first: "Qualifier 2 (Loser Q1 vs Winner Eliminator)" is not an eliminator.</summary>
    private static string StageOf(string text)
    {
        string t = text.ToLowerInvariant();
        if (t.Contains("girls")) return "Girls";
        if (t.Contains("kids")) return "Kids";
        if (QualifierPattern().Match(t) is { Success: true } q) return $"Qualifier {q.Groups[1].Value}";
        if (t.Contains("eliminator")) return "Eliminator";
        if (t.Contains("final")) return "Final";
        return "League";
    }

    private static DateTime? ParseDate(string s)
    {
        string clean = SuperscriptPattern().Replace(s, "").Replace("th ", " ").Replace("st ", " ").Replace("nd ", " ").Replace("rd ", " ");
        clean = Rules.Clean(clean);
        return DateTime.TryParseExact(clean, ["d MMMM yyyy", "d MMM yyyy", "dd MMMM yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    /// <summary>
    /// Give each match from the awards sheet its date, time and stage from the fixtures document:
    /// first by team pair; matches left over take the playoff / girls slots in date order.
    /// </summary>
    private static void AttachDates(Season season, List<Fixture> fixtures, ImportResult result)
    {
        if (fixtures.Count == 0) return;
        var used = new HashSet<Fixture>();
        var boysTeams = fixtures.Where(f => f.A.Length > 0).SelectMany(f => new[] { f.A, f.B }).ToList();
        bool IsBoys(string n) => boysTeams.Any(t => Rules.SameTeam(t, n));
        var leftover = new List<Match>();

        foreach (var m in season.Matches)
        {
            var f = fixtures.FirstOrDefault(x => !used.Contains(x) && x.A.Length > 0 &&
                ((Rules.SameTeam(x.A, m.TeamA) && Rules.SameTeam(x.B, m.TeamB)) || (Rules.SameTeam(x.A, m.TeamB) && Rules.SameTeam(x.B, m.TeamA))));
            if (f is null) { leftover.Add(m); continue; }
            used.Add(f);
            (m.Date, m.Time, m.Stage) = (f.Date, f.Time, f.Stage);
        }

        var boysSlots = fixtures.Where(f => !used.Contains(f) && f.A.Length == 0 && f.Stage is not ("Girls" or "Kids")).ToList();
        var girlsSlots = fixtures.Where(f => !used.Contains(f) && f.Stage == "Girls").ToList();
        foreach (var m in leftover)
        {
            var pool = IsBoys(m.TeamA) && IsBoys(m.TeamB) ? boysSlots : girlsSlots;
            if (pool.Count == 0) continue;
            var f = pool[0]; pool.RemoveAt(0); used.Add(f);
            (m.Date, m.Time, m.Stage) = (f.Date, f.Time, f.Stage);
        }
        int noDate = season.Matches.Count(m => m.Date is null);
        if (noDate > 0) result.Notes.Add($"{noDate} match(es) had no date in Match.docx - add them on the Matches page.");
        if (leftover.Count > 0) result.Notes.Add($"{leftover.Count} match(es) were not in the league fixtures, so they were given the playoff / girls slots in date order: check their stage on the Matches page.");
    }

    [GeneratedRegex(@"\b(20\d\d)\b")] private static partial Regex YearPattern();
    [GeneratedRegex(@"\*\s*(\d+)\s*$")] private static partial Regex FeePattern();
    [GeneratedRegex(@"\s*(?:,|&|\band\b)\s*", RegexOptions.IgnoreCase)] private static partial Regex SplitPattern();
    [GeneratedRegex(@"<w:tbl>.*?</w:tbl>", RegexOptions.Singleline)] private static partial Regex TablePattern();
    [GeneratedRegex(@"<w:tr[ >].*?</w:tr>", RegexOptions.Singleline)] private static partial Regex RowPattern();
    [GeneratedRegex(@"<w:tc>.*?</w:tc>", RegexOptions.Singleline)] private static partial Regex CellPattern();
    [GeneratedRegex(@"<w:t(?: [^>]*)?>([^<]*)</w:t>")] private static partial Regex TextPattern();
    [GeneratedRegex(@"^(.*?)\s+vs\.?\s+(.*)$", RegexOptions.IgnoreCase)] private static partial Regex VsPattern();
    [GeneratedRegex(@"[ᵗʰˢⁿᵈʳ]")] private static partial Regex SuperscriptPattern();
    [GeneratedRegex(@"qualifier\s*[–-]?\s*([12])")] private static partial Regex QualifierPattern();
}
