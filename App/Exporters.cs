using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Rpl;

/// <summary>Excel (from the template, calculated by Excel) and CSV files for a season.</summary>
public static class Exporters
{
    // --- Excel ---------------------------------------------------------------------------------

    public static Task Excel(Season s, string path) => Office.Sta(() =>
    {
        if (!File.Exists(Office.TemplatePath)) throw new FileNotFoundException("The RPL Excel template is missing next to the app.", Office.TemplatePath);
        dynamic xl = Office.NewApp("Excel.Application", "Microsoft Excel");
        dynamic? wb = null;
        try
        {
            xl.Visible = false; xl.DisplayAlerts = false; xl.ScreenUpdating = false;
            wb = xl.Workbooks.Add(Office.TemplatePath);
            for (int i = 0; wb is null && i < 20; i++) { Thread.Sleep(500); if ((int)xl.Workbooks.Count >= 1) wb = xl.Workbooks.Item(1); }
            if (wb is null) throw new InvalidOperationException("Excel could not create a workbook from the template.");

            // Teams (Misc. sheet) - the dropdowns and the points table use this list.
            Fill(Table(wb, "Teams"), s.Teams.Select(t => new object?[] { t.Name, Blank(t.Captain), t.Division }).ToList(), 1);

            // Players (sponsors go in the same table with Sponsor = Yes, as the Accounts formulas expect).
            var rows = s.Players.Select((p, i) => new object?[]
            {
                null, p.Name, int.TryParse(p.Age, out int age) ? age : Blank(p.Age), Blank(p.Block), Blank(p.Flat), Blank(p.Mobile),
                p.Cancelled ? "Cancelled" : p.Gender, null, null,
                p.Payment == "Online" ? "Yes" : p.Payment == "Cash" ? "No" : null,
                Blank(string.Join(". ", new[] { p.PaidTo, p.Notes }.Where(x => x.Length > 0))),
                int.TryParse(p.FormNo, out int form) ? form : Blank(p.FormNo), Blank(p.Team), i + 1, Blank(p.Category),
            }).Concat(s.Sponsors.Select(sp => new object?[]
            {
                null, sp.Name, null, null, null, null, null, "Yes", (double)sp.Amount, sp.Mode == "Online" ? "Yes" : "No", Blank(sp.Notes), null, null, null, null,
            })).ToList();
            dynamic players = Table(wb, "Players");
            foreach (int c in new[] { 5, 6 }) players.ListColumns.Item(c).Range.NumberFormat = "@";   // flat, mobile as typed
            Fill(players, rows, 2);

            // Accounts: fee, expenses, cash count.
            dynamic accounts = Sheet(wb, "Accounts");
            accounts.Range("N2").Value2 = s.Settings.Fee;
            Fill(Table(wb, "Expenses"), s.Expenses.Select(e => new object?[]
                { e.Item, Blank(e.PaidBy), (double)e.Amount, e.Paid ? "Yes" : "No", null, string.Join(". ", new[] { e.Mode, e.Notes }.Where(x => x.Length > 0)) }).ToList(), 1);
            for (int r = 33; r <= 39; r++)
            {
                int note = (int)(double)accounts.Range($"D{r}").Value2;
                accounts.Range($"E{r}").Value2 = s.Cash.GetValueOrDefault(note);
            }

            // Matches & awards.
            Fill(Table(wb, "Matches"), s.Matches.OrderBy(m => m.No).Select(m =>
            {
                var byPlayer = m.Awards.GroupBy(a => a.Player).Select(g => (Player: g.Key, Titles: string.Join(", ", g.Select(a => a.Title)))).ToList();
                var pom = byPlayer.FirstOrDefault(x => x.Titles.Contains("Player of the Match"));
                var others = byPlayer.Where(x => x != pom).ToList();
                return new object?[]
                {
                    null, m.Date?.ToOADate(), Blank(m.Time), Blank(m.TeamA), Blank(m.TeamB), null, m.Stage, Blank(m.Winner), Blank(m.Result),
                    Blank(pom.Player ?? ""), Blank(pom.Titles ?? ""),
                    others.Count > 0 ? others[0].Player : null, others.Count > 0 ? others[0].Titles : null,
                    others.Count > 1 ? others[1].Player : null, others.Count > 1 ? string.Join("; ", others.Skip(1).Select(o => o.Player == others[1].Player ? o.Titles : $"{o.Player}: {o.Titles}")) : null,
                    Calc.MatchDivision(s, m),
                    m.ScoreA.Runs, Overs(m.ScoreA.Overs), m.ScoreB.Runs, Overs(m.ScoreB.Overs),
                };
            }).ToList(), 2);

            xl.CalculateFull();
            ((dynamic)Sheet(wb, "Players")).Activate();
            wb.SaveAs(path, 51);
        }
        finally
        {
            try { wb?.Close(false); } catch { /* closed */ }
            try { xl.Quit(); } catch { /* gone */ }
            Marshal.FinalReleaseComObject((object)xl);
        }
    });

    private static object? Blank(string s) => s.Length == 0 ? null : s;

    /// <summary>"8.3" -> 8.3 as a number for Excel (the template reads x.y as overs.balls).</summary>
    private static object? Overs(string o) => double.TryParse(o, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;

    /// <summary>Resize a table to the rows and write them, skipping formula columns (null in every row).</summary>
    private static void Fill(dynamic table, List<object?[]> rows, int firstDataColumn)
    {
        if (rows.Count == 0) return;
        dynamic ws = table.Parent;
        int header = table.HeaderRowRange.Row, left = table.Range.Column, width = table.ListColumns.Count;
        table.Resize(ws.Range(ws.Cells.Item(header, left), ws.Cells.Item(header + rows.Count, left + width - 1)));
        for (int c = firstDataColumn; c <= width; c++)
        {
            if (rows.All(r => c - 1 >= r.Length || r[c - 1] is null)) continue;
            var values = new object?[rows.Count, 1];
            for (int r = 0; r < rows.Count; r++) values[r, 0] = c - 1 < rows[r].Length ? rows[r][c - 1] : null;
            table.ListColumns.Item(c).DataBodyRange.Value2 = values;
        }
        // Formula columns reach the last row (Resize normally does this already).
        for (int c = 1; c <= width; c++)
        {
            dynamic body = table.ListColumns.Item(c).DataBodyRange;
            dynamic top = body.Cells.Item(1, 1), bottom = body.Cells.Item(rows.Count, 1);
            if ((bool)top.HasFormula && !(bool)bottom.HasFormula) body.Formula = (string)top.Formula;
        }
    }

    private static object Sheet(dynamic wb, string name)
    {
        int n = wb.Worksheets.Count;
        for (int i = 1; i <= n; i++) { dynamic ws = wb.Worksheets.Item(i); if ((string)ws.Name == name) return ws; }
        throw new InvalidOperationException($"The template has no '{name}' sheet.");
    }

    private static dynamic Table(dynamic wb, string name)
    {
        int n = wb.Worksheets.Count;
        for (int i = 1; i <= n; i++)
        {
            dynamic ws = wb.Worksheets.Item(i);
            int t = ws.ListObjects.Count;
            for (int j = 1; j <= t; j++) { dynamic lo = ws.ListObjects.Item(j); if ((string)lo.Name == name) return lo; }
        }
        throw new InvalidOperationException($"The template has no '{name}' table.");
    }

    // --- CSV -----------------------------------------------------------------------------------

    /// <summary>Four files: _players.csv, _matches.csv, _accounts.csv (fees, sponsors, expenses), _stats.csv (batting, bowling). Returns their paths.</summary>
    public static Task<List<string>> Csv(Season s, string basePath) => Task.Run(() =>
    {
        string Players()
        {
            var sb = Line(null, "No", "Form No", "Name", "Age", "Block", "Flat", "Mobile", "Gender", "Division", "Status", "Team", "Category", "Auction price", "Pays fee", "Payment", "Paid to", "Notes");
            int i = 0;
            foreach (var p in s.Players)
                Line(sb, (++i).ToString(), p.FormNo, p.Name, p.Age, p.Block, p.Flat, p.Mobile, p.Gender, p.Division, p.Cancelled ? "Cancelled" : "Active",
                    p.Team, p.Category, p.SoldPrice?.ToString() ?? "", Rules.PaysFee(p) ? "Yes" : "No", Rules.PaysFee(p) ? p.Payment : "", p.PaidTo, p.Notes);
            return sb.ToString();
        }
        string Matches()
        {
            var sb = Line(null, "No", "Date", "Time", "Division", "Team A", "Team B", "Stage", "Team A score", "Team B score", "Winner", "Result", "Awards", "Absent");
            foreach (var m in s.Matches.OrderBy(m => m.No))
                Line(sb, m.No.ToString(), m.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "", m.Time, Calc.MatchDivision(s, m), m.TeamA, m.TeamB, m.Stage,
                    m.ScoreA.ToString(), m.ScoreB.ToString(), m.Winner, m.Result,
                    string.Join("; ", m.Awards.Select(a => $"{a.Title}: {a.Player}")), string.Join("; ", m.Absent));
            return sb.ToString();
        }
        string Accounts()
        {
            var a = Calc.Accounts(s);
            var sb = Line(null, "Type", "Item", "Amount", "Mode", "Paid", "Paid by / notes");
            Line(sb, "Fees", $"{a.PaidOnline} paid online × {Rules.Money(a.Fee)}", a.FeeOnline.ToString(CultureInfo.InvariantCulture), "Online", "Yes", "");
            Line(sb, "Fees", $"{a.PaidCash} paid in cash × {Rules.Money(a.Fee)}", a.FeeCash.ToString(CultureInfo.InvariantCulture), "Cash", "Yes", "");
            if (a.Pending > 0) Line(sb, "Fees", $"{a.Pending} pending × {Rules.Money(a.Fee)}", a.FeePending.ToString(CultureInfo.InvariantCulture), "", "No", "");
            foreach (var sp in s.Sponsors) Line(sb, "Sponsor", sp.Name, sp.Amount.ToString(CultureInfo.InvariantCulture), sp.Mode, "Yes", sp.Notes);
            foreach (var e in s.Expenses) Line(sb, "Expense", e.Item, (-e.Amount).ToString(CultureInfo.InvariantCulture), e.Mode, e.Paid ? "Yes" : "No", string.Join(". ", new[] { e.PaidBy, e.Notes }.Where(x => x.Length > 0)));
            Line(sb, "Balance", "Income - expenses", a.Balance.ToString(CultureInfo.InvariantCulture), "", "", "");
            return sb.ToString();
        }

        string Stats()
        {
            var sb = Line(null, "Division", "Type", "Player", "Team", "Innings / Overs", "Runs", "Balls / Runs conceded", "Wickets", "Strike rate / Economy", "Best");
            foreach (var d in Calc.ActiveDivisions(s))
            {
                foreach (var b in Calc.Batting(s, d))
                    Line(sb, d, "Batting", b.Player, b.Team, b.Innings.ToString(), b.Runs.ToString(), b.Balls.ToString(), "", b.StrikeRate.ToString(CultureInfo.InvariantCulture), b.Highest.ToString());
                foreach (var b in Calc.Bowling(s, d))
                    Line(sb, d, "Bowling", b.Player, b.Team, b.Overs, "", b.Runs.ToString(), b.Wickets.ToString(), b.Economy.ToString(CultureInfo.InvariantCulture), b.Best);
            }
            return sb.ToString();
        }

        var files = new List<string>();
        foreach (var (suffix, text) in new[] { ("_players.csv", Players()), ("_matches.csv", Matches()), ("_accounts.csv", Accounts()), ("_stats.csv", Stats()) })
        {
            string file = basePath + suffix;
            File.WriteAllText(file, text, new UTF8Encoding(true));   // BOM: Excel shows Gujarati and ₹ correctly
            files.Add(file);
        }
        return files;
    });

    private static StringBuilder Line(StringBuilder? sb, params string[] cells)
    {
        sb ??= new StringBuilder();
        sb.AppendLine(string.Join(",", cells.Select(Quote)));
        return sb;
    }

    private static string Quote(string v)
    {
        if (v.Length > 0 && "=+@".Contains(v[0])) v = "'" + v;   // never run as a formula in Excel
        return v.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
    }
}
