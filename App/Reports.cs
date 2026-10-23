using System.Globalization;
using System.Net;
using System.Text;

namespace Rpl;

/// <summary>
/// Printed pages as HTML: the season report (PDF via Edge, .docx via Word), the bilingual registration
/// form, and team schedules. Laid out with tables and one class per element so Word reads them too.
/// </summary>
public static class Reports
{
    private const string Maroon = "#6b0f1a", Gold = "#c9962e", Cream = "#fbf6ec";
    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
    private static string D(DateTime? d, string fmt = "dd MMM yyyy") => d?.ToString(fmt, CultureInfo.InvariantCulture) ?? "—";
    private const string Nil = "<span class=\"nil\">—</span>";
    private static string Dash(string s) => s.Length == 0 ? Nil : E(s);

    private static void Break(StringBuilder sb, bool forWord) => sb.Append(forWord
        ? "<br clear=\"all\" style=\"page-break-before:always\">"
        : "<div style=\"break-before:page;page-break-before:always\"></div>");

    private static void Head(StringBuilder sb, string no, string title, string sub) => sb.Append($"""
        <table class="sec"><tr><td class="secno">{no}</td><td><div class="h2">{E(title)}</div><div class="sub">{E(sub)}</div></td></tr></table>
        """);

    // =================================================================================== season report

    public static string SeasonReport(Season s, bool forWord)
    {
        var a = Calc.Accounts(s);
        var divisions = Calc.ActiveDivisions(s);
        var tally = Calc.AwardTally(s);
        int played = s.Matches.Count(m => m.Played);
        var sb = new StringBuilder();
        sb.Append($"<!doctype html><html><head><meta charset=\"utf-8\"><title>RPL {E(s.Year)}</title><style>{Css}</style></head><body>");

        // Cover
        string period = s.Settings.StartDate is null ? "" : $"{D(s.Settings.StartDate, "dd MMM")} – {D(s.Settings.EndDate)}";
        sb.Append($"""
            <table class="hero"><tr>
              <td style="width:120px"><img src="{Office.Url(Office.Logo)}" width="110" height="110"></td>
              <td><div class="eyebrow">RATNADEEP PREMIER LEAGUE</div><div class="h1">RPL {E(s.Year)}</div>
                  <div class="lede">{E(s.Settings.Tagline)}{(period.Length > 0 ? " · " + E(period) : "")}</div>
                  <div class="meta">Season report · generated {DateTime.Now:dd MMM yyyy, HH:mm}</div></td>
            </tr></table>
            <table class="tiles"><tr>
              {Tile("Players", a.Registered.ToString(), $"{a.Male} men · {a.Female} women · {a.Kids} kids")}<td class="gap"></td>
              {Tile("Teams", s.Teams.Count.ToString(), $"{s.Teams.Sum(t => Calc.Squad(s, t.Name).Count)} players in teams")}<td class="gap"></td>
              {Tile("Matches", $"{played} / {s.Matches.Count}", "played / scheduled")}<td class="gap"></td>
              {Tile("Balance", Rules.Money(a.Balance), $"income {Rules.Money(a.Income)} · spent {Rules.Money(a.Expenses)}")}
            </tr></table>
            """);
        foreach (var d in divisions)
        {
            var table = Calc.PointsTable(s, d);
            if (!table.Any(t => t.Played > 0)) continue;
            sb.Append($"<div class=\"h3\">Points table{(divisions.Count > 1 ? " – " + E(d) : "")}</div>");
            Standings(sb, table);
        }
        if (tally.Count > 0)
        {
            sb.Append("<div class=\"h3\">Top performers</div><table class=\"tbl\"><tr><th>Player</th><th class=\"c\">Player of the Match</th><th class=\"c\">Awards</th><th>Titles</th></tr>");
            foreach (var t in tally.Take(8)) sb.Append($"<tr><td><b>{E(t.Player)}</b></td><td class=\"c\">{t.PlayerOfMatch}</td><td class=\"c\">{t.Total}</td><td class=\"small\">{E(t.Titles)}</td></tr>");
            sb.Append("</table>");
        }

        // 01 Teams & squads
        Break(sb, forWord);
        Head(sb, "01", "Teams & Squads", $"{s.Teams.Count} teams");
        sb.Append(forWord ? "<div>" : "<div class=\"cols\">");
        foreach (var t in s.Teams)
        {
            var squad = Calc.Squad(s, t.Name);
            sb.Append($"<table class=\"card\"><tr><td colspan=\"3\" class=\"cardh\"><b>{E(t.Name)}</b>{(t.Group.Length > 0 ? $" <span class=\"tag\">&nbsp;{E(t.Group)}&nbsp;</span>" : "")}" +
                      $"<span class=\"small\">&nbsp;&nbsp;Captain: {Dash(t.Captain)} · {squad.Count} players</span></td></tr>");
            foreach (var p in squad)
                sb.Append($"<tr><td>{E(p.Name)}{(p.Name == t.Captain ? " <b>(C)</b>" : "")}</td><td class=\"c\">{Cat(p.Category)}</td><td class=\"small\">{E(p.Block)}{(p.Flat.Length > 0 ? "/" + E(p.Flat) : "")}</td></tr>");
            if (squad.Count == 0) sb.Append($"<tr><td colspan=\"3\" class=\"small\">No players assigned yet</td></tr>");
            sb.Append("</table>");
            if (forWord) sb.Append("<p style=\"font-size:6pt;margin:0\">&nbsp;</p>");
        }
        sb.Append("</div>");

        // 02 Fixtures & results
        Break(sb, forWord);
        Head(sb, "02", "Fixtures & Results", $"{s.Matches.Count} matches · {played} played");
        sb.Append("<table class=\"tbl\"><tr><th class=\"c\">No</th><th>Date</th><th>Time</th><th>Match</th><th>Stage</th><th>Result</th><th>Player of the Match</th></tr>");
        int z = 0;
        foreach (var m in s.Matches.OrderBy(m => m.Date ?? DateTime.MaxValue).ThenBy(m => m.Time).ThenBy(m => m.No))
        {
            string result = m.Winner.Length == 0 ? Nil : m.Winner == Lists.NoResult ? "No result" : $"<b>{E(m.Winner)}</b>{(m.Result.Length > 0 ? " " + E(m.Result) : " won")}";
            string pom = m.Awards.FirstOrDefault(x => x.Title == "Player of the Match")?.Player ?? "";
            sb.Append($"<tr class=\"{(z++ % 2 == 1 ? "z" : "")}\"><td class=\"c\">{m.No}</td><td style=\"white-space:nowrap\">{D(m.Date)}</td><td style=\"white-space:nowrap\">{Dash(m.Time)}</td>" +
                      $"<td><b>{E(m.TeamA)}</b> vs <b>{E(m.TeamB)}</b></td><td>{Stage(m.Stage)}</td><td>{result}</td><td>{Dash(pom)}</td></tr>");
        }
        sb.Append("</table>");

        // 03 Points table + awards per match
        Break(sb, forWord);
        Head(sb, "03", "Points Table, Stats & Awards", "League matches: 2 points a win, 1 for no result; ties on points by net run rate");
        foreach (var d in divisions)
        {
            if (divisions.Count > 1) sb.Append($"<div class=\"h3\">{E(d)}</div>");
            Standings(sb, Calc.PointsTable(s, d));
            var bat = Calc.Batting(s, d).Take(5).ToList();
            var bowl = Calc.Bowling(s, d).Take(5).ToList();
            if (bat.Count + bowl.Count == 0) continue;
            sb.Append("<table style=\"width:100%;margin-top:8pt\"><tr><td style=\"width:50%;vertical-align:top;padding-right:6pt\">");
            sb.Append("<div class=\"h3\">Most runs (Orange Cap)</div><table class=\"tbl\"><tr><th>Player</th><th>Team</th><th class=\"c\">Inns</th><th class=\"c\">Runs</th><th class=\"c\">HS</th><th class=\"c\">SR</th></tr>");
            foreach (var b in bat) sb.Append($"<tr><td><b>{E(b.Player)}</b></td><td class=\"small\">{E(b.Team)}</td><td class=\"c\">{b.Innings}</td><td class=\"c\"><b>{b.Runs}</b></td><td class=\"c\">{b.Highest}</td><td class=\"c\">{b.StrikeRate:0.0}</td></tr>");
            sb.Append("</table></td><td style=\"width:50%;vertical-align:top;padding-left:6pt\">");
            sb.Append("<div class=\"h3\">Most wickets (Purple Cap)</div><table class=\"tbl\"><tr><th>Player</th><th>Team</th><th class=\"c\">Overs</th><th class=\"c\">Wkts</th><th class=\"c\">Best</th><th class=\"c\">Econ</th></tr>");
            foreach (var b in bowl) sb.Append($"<tr><td><b>{E(b.Player)}</b></td><td class=\"small\">{E(b.Team)}</td><td class=\"c\">{b.Overs}</td><td class=\"c\"><b>{b.Wickets}</b></td><td class=\"c\">{E(b.Best)}</td><td class=\"c\">{b.Economy:0.00}</td></tr>");
            sb.Append("</table></td></tr></table>");
        }
        sb.Append("<div class=\"h3\">Awards by match</div><table class=\"tbl\"><tr><th class=\"c\">No</th><th>Match</th><th>Awards</th></tr>");
        foreach (var m in s.Matches.Where(m => m.Awards.Count > 0).OrderBy(m => m.No))
            sb.Append($"<tr><td class=\"c\">{m.No}</td><td>{E(m.Title)}<br><span class=\"small\">{D(m.Date)} · {E(m.Stage)}</span></td><td>" +
                      string.Join("<br>", m.Awards.GroupBy(x => x.Player).Select(g => $"<b>{E(g.Key)}</b> – {E(string.Join(", ", g.Select(x => x.Title)))}")) + "</td></tr>");
        sb.Append("</table>");

        // 04 Accounts
        Break(sb, forWord);
        Head(sb, "04", "Accounts", $"Registration fee {Rules.Money(a.Fee)} per player (men); women and kids play free");
        sb.Append($"""
            <table class="tbl" style="width:60%">
              <tr><th>Income</th><th class="r">Amount</th></tr>
              <tr><td>Fees paid online ({a.PaidOnline} × {Rules.Money(a.Fee)})</td><td class="r">{Rules.Money(a.FeeOnline)}</td></tr>
              <tr><td>Fees paid in cash ({a.PaidCash} × {Rules.Money(a.Fee)})</td><td class="r">{Rules.Money(a.FeeCash)}</td></tr>
              <tr><td>Sponsors ({s.Sponsors.Count})</td><td class="r">{Rules.Money(a.Sponsors)}</td></tr>
              <tr class="z"><td><b>Total income</b></td><td class="r"><b>{Rules.Money(a.Income)}</b></td></tr>
              <tr><td>Expenses ({s.Expenses.Count}){(a.ExpensesUnpaid > 0 ? $", of which {Rules.Money(a.ExpensesUnpaid)} not yet paid" : "")}</td><td class="r">– {Rules.Money(a.Expenses)}</td></tr>
              <tr class="tot"><td><b>Balance</b></td><td class="r"><b>{Rules.Money(a.Balance)}</b></td></tr>
            </table>
            {(a.Pending > 0 ? $"<p class=\"warn\">{a.Pending} player(s) have not paid yet: {Rules.Money(a.FeePending)} pending.</p>" : "")}
            <div class="h3">Cash check</div>
            <table class="tbl" style="width:60%">
              <tr><td>Should be in hand (cash fees + cash sponsors – expenses paid in cash)</td><td class="r">{Rules.Money(a.CashExpected)}</td></tr>
              <tr><td>Counted ({string.Join(", ", s.Cash.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Key).Select(kv => $"₹{kv.Key} × {kv.Value}"))})</td><td class="r">{Rules.Money(a.CashCounted)}</td></tr>
              <tr class="tot"><td><b>Difference</b></td><td class="r"><b>{(a.CashDifference >= 0 ? "+" : "–")}{Rules.Money(Math.Abs(a.CashDifference))}</b></td></tr>
            </table>
            """);
        if (s.Expenses.Count > 0)
        {
            sb.Append("<div class=\"h3\">Expenses</div><table class=\"tbl\"><tr><th>Item</th><th>Paid by</th><th>Mode</th><th class=\"c\">Paid</th><th class=\"r\">Amount</th></tr>");
            foreach (var e in s.Expenses) sb.Append($"<tr><td>{E(e.Item)}{(e.Notes.Length > 0 ? $" <span class=\"small\">({E(e.Notes)})</span>" : "")}</td><td>{Dash(e.PaidBy)}</td><td>{E(e.Mode)}</td><td class=\"c\">{(e.Paid ? "Yes" : "<b>No</b>")}</td><td class=\"r\">{Rules.Money(e.Amount)}</td></tr>");
            sb.Append($"<tr class=\"tot\"><td colspan=\"4\"><b>Total</b></td><td class=\"r\"><b>{Rules.Money(a.Expenses)}</b></td></tr></table>");
        }
        if (s.Sponsors.Count > 0)
        {
            sb.Append("<div class=\"h3\">Sponsors</div><table class=\"tbl\"><tr><th>Sponsor</th><th>Mode</th><th>Notes</th><th class=\"r\">Amount</th></tr>");
            foreach (var sp in s.Sponsors) sb.Append($"<tr><td><b>{E(sp.Name)}</b></td><td>{E(sp.Mode)}</td><td>{Dash(sp.Notes)}</td><td class=\"r\">{Rules.Money(sp.Amount)}</td></tr>");
            sb.Append("</table>");
        }

        // 05 Player register
        Break(sb, forWord);
        Head(sb, "05", "Player Register", $"{s.Players.Count} forms · {a.Cancelled} cancelled");
        sb.Append("<table class=\"tbl\" style=\"font-size:8pt\"><tr><th class=\"c\">No</th><th class=\"c\">Form</th><th>Name</th><th class=\"c\">Age</th><th>Flat</th><th>Mobile</th><th>Team</th><th class=\"c\">Cat.</th><th>Fee</th><th>Notes</th></tr>");
        int no = 0;
        foreach (var p in s.Players)
        {
            string fee = !Rules.PaysFee(p) ? "<span class=\"small\">not charged</span>" : p.Payment is "Pending" or "" ? "<b style=\"color:#b91c1c\">Pending</b>" : E(p.Payment);
            sb.Append($"<tr class=\"{(p.Cancelled ? "x" : ++no % 2 == 0 ? "z" : "")}\"><td class=\"c\">{(p.Cancelled ? "–" : no.ToString())}</td><td class=\"c\">{Dash(p.FormNo)}</td>" +
                      $"<td><b>{E(p.Name)}</b>{(p.Cancelled ? " <span class=\"small\">(cancelled)</span>" : p.Gender != "Male" ? $" <span class=\"small\">({E(p.Gender)})</span>" : "")}</td>" +
                      $"<td class=\"c\">{Dash(p.Age)}</td><td style=\"white-space:nowrap\">{E(p.Block)}{(p.Flat.Length > 0 ? "/" + E(p.Flat) : "")}</td><td style=\"white-space:nowrap\">{Dash(p.Mobile)}</td>" +
                      $"<td>{Dash(p.Team)}</td><td class=\"c\">{Cat(p.Category)}</td><td>{fee}</td><td class=\"small\">{Dash(string.Join(". ", new[] { p.PaidTo, p.Notes }.Where(x => x.Length > 0)))}</td></tr>");
        }
        sb.Append("</table></body></html>");
        return sb.ToString();
    }

    private static void Standings(StringBuilder sb, List<Standing> table)
    {
        sb.Append("<table class=\"tbl\"><tr><th class=\"c\">#</th><th>Team</th><th class=\"c\">Played</th><th class=\"c\">Won</th><th class=\"c\">Lost</th><th class=\"c\">No result</th><th class=\"c\">NRR</th><th class=\"c\">Points</th></tr>");
        int i = 0;
        foreach (var t in table)
            sb.Append($"<tr class=\"{(++i <= 4 && t.Played > 0 ? "q" : i % 2 == 0 ? "z" : "")}\"><td class=\"c\">{i}</td><td><b>{E(t.Team)}</b></td><td class=\"c\">{t.Played}</td><td class=\"c\">{t.Won}</td>" +
                      $"<td class=\"c\">{t.Lost}</td><td class=\"c\">{t.NoResult}</td><td class=\"c\">{t.NetRunRate.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture)}</td><td class=\"c\"><b>{t.Points}</b></td></tr>");
        sb.Append("</table><div class=\"small\">Top 4 (shaded) go to the playoffs.</div>");
    }

    private static string Tile(string label, string value, string sub) =>
        $"<td class=\"tile\"><div class=\"klabel\">{E(label.ToUpperInvariant())}</div><div class=\"kvalue\">{E(value)}</div><div class=\"ksub\">{E(sub)}</div></td>";

    private static string Cat(string c) => c.Length == 0 ? Nil : $"<span class=\"cat\" style=\"background:{c switch { "A+" => Maroon, "A" => "#9a3412", "B" => "#b45309", "C" => "#4d7c0f", "D" => "#0f766e", _ => "#64748b" }}\">&nbsp;{E(c)}&nbsp;</span>";
    private static string Stage(string s) => s == "League" ? E(s) : $"<span class=\"tag\">&nbsp;{E(s)}&nbsp;</span>";

    private const string Css = $$"""
        @page { size: A4 landscape; margin: 12mm; }
        * { -webkit-print-color-adjust: exact; print-color-adjust: exact; }
        body { margin: 0; font-family: "Segoe UI", "Nirmala UI", Arial, sans-serif; color: #1f1a17; font-size: 10pt; }
        table { border-collapse: collapse; }
        .nil { color: #c8bfb4; } .small { font-size: 8pt; color: #6b625b; } .warn { color: #b91c1c; font-weight: bold; }
        .hero { width: 100%; background: {{Maroon}}; color: #ffffff; } .hero td { padding: 14pt 18pt; background: {{Maroon}}; vertical-align: middle; }
        .eyebrow { font-size: 9pt; letter-spacing: 3pt; color: {{Gold}}; font-weight: bold; }
        .h1 { font-size: 30pt; font-weight: bold; color: #ffffff; } .lede { font-size: 12pt; color: #f3e3c3; } .meta { font-size: 8pt; color: #e7cfa0; margin-top: 4pt; }
        .tiles { width: 100%; margin: 12pt 0; } .gap { width: 10pt; }
        .tile { width: 24%; background: {{Cream}}; border: 1px solid #ead9b8; padding: 10pt 14pt; vertical-align: top; }
        .klabel { font-size: 8pt; font-weight: bold; color: #8a6d3b; letter-spacing: 1pt; } .kvalue { font-size: 22pt; font-weight: bold; color: {{Maroon}}; } .ksub { font-size: 8pt; color: #6b625b; }
        .h3 { font-size: 11pt; font-weight: bold; color: {{Maroon}}; margin: 12pt 0 6pt; text-transform: uppercase; letter-spacing: 1pt; }
        .sec { width: 100%; border-bottom: 3px solid {{Gold}}; margin-bottom: 10pt; } .sec td { padding: 0 0 8pt 0; vertical-align: middle; }
        .secno { width: 44px; background: {{Maroon}}; color: #ffffff; font-weight: bold; font-size: 15pt; text-align: center; padding: 6pt !important; }
        .h2 { font-size: 18pt; font-weight: bold; color: {{Maroon}}; padding-left: 10pt; } .sub { font-size: 9pt; color: #6b625b; padding-left: 10pt; }
        .tbl { width: 100%; border: 1px solid #e5d9c6; }
        .tbl th { background: {{Maroon}}; color: #ffffff; text-align: left; font-size: 8pt; text-transform: uppercase; padding: 6pt; }
        .tbl td { padding: 4pt 6pt; border-top: 1px solid #efe6d8; vertical-align: middle; }
        .tbl tr.z td { background: {{Cream}}; } .tbl tr.q td { background: #fdf0d5; } .tbl tr.x td { color: #9a8f86; text-decoration: line-through; }
        .tbl tr.tot td { background: #f6e7c8; border-top: 2px solid {{Gold}}; }
        .tbl tr { page-break-inside: avoid; }
        .c { text-align: center; } .r { text-align: right; }
        .tag { background: #f6e7c8; color: #7c2d12; font-weight: bold; font-size: 8pt; }
        .cat { color: #ffffff; font-weight: bold; font-size: 8pt; }
        .cols { column-count: 3; column-gap: 12pt; }
        .card { width: 100%; border: 1px solid #e5d9c6; margin-bottom: 10pt; font-size: 9pt; break-inside: avoid; page-break-inside: avoid; }
        .card td { padding: 3pt 6pt; border-top: 1px solid #f3ebdd; } .cardh { background: {{Cream}}; border-bottom: 2px solid {{Gold}}; padding: 6pt !important; font-size: 10.5pt; color: {{Maroon}}; }
        """;

    // =================================================================================== registration form

    /// <summary>Two pages (Gujarati, then English), like RPL_2026.docx, with this season's fee, dates and rules.</summary>
    public static string RegistrationForm(Season s)
    {
        var st = s.Settings;
        string qr = st.RegistrationUrl.Trim().Length > 0 ? Printouts.QrDataUri(st.RegistrationUrl.Trim()) : "";
        string Dt(DateTime? d) => d?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "__/__/____";   // always "/", whatever the PC's date setting
        string Fill(string text, bool guj)
        {
            string f(string v) => guj ? Rules.GujaratiDigits(v) : v;
            return E(text).Replace("{fee}", $"<b>{f(st.Fee.ToString())}</b>").Replace("{deadline}", $"<b>{f(Dt(st.FormDeadline))}</b>")
                .Replace("{start}", $"<b>{f(Dt(st.StartDate))}</b>").Replace("{end}", $"<b>{f(Dt(st.EndDate))}</b>");
        }
        string Page(bool guj)
        {
            string g(string v) => guj ? Rules.GujaratiDigits(v) : v;
            var facts = guj
                ? new[] { $"ફોર્મ ભરવાની છેલ્લી તારીખ - <b>{g(Dt(st.FormDeadline))}</b>", $"આર.પી.એલ. નો સમયગાળો - <b>{g(Dt(st.StartDate))} થી {g(Dt(st.EndDate))}</b>", $"રજિસ્ટ્રેશન ફી <b>રૂ.{g(st.Fee.ToString())}</b> પ્રતિ વ્યક્તિ <b>(Non-Refundable)</b>" }
                : new[] { $"Deadline for submitting the form - <b>{Dt(st.FormDeadline)}</b>", $"RPL period - <b>{Dt(st.StartDate)} to {Dt(st.EndDate)}</b>", $"Registration fee <b>₹{st.Fee}</b> per person <b>(Non-Refundable)</b>" };
            var rules = (guj ? st.RulesGujarati : st.RulesEnglish).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            var sb = new StringBuilder();
            sb.Append($"""
                <div class="page">
                <table class="top"><tr><td>Form No: ____________</td><td style="text-align:right">Date: ___ / ___ / {E(s.Year)}</td></tr></table>
                <table class="brand"><tr>
                  <td style="width:130px"><img src="{Office.Url(Office.Logo)}" width="120" height="120"></td>
                  <td class="title">RPL {E(s.Year)}</td>
                  <td style="width:130px;text-align:right"><img src="{Office.Url(Office.Cricket)}" width="110" height="110"></td>
                </tr></table>
                <ol class="facts">{string.Concat(facts.Select(f => $"<li>{f}</li>"))}</ol>
                <div class="h">{(guj ? "અગત્યની સૂચનાઓ" : "Important Instructions")}</div>
                <ol class="rules">{string.Concat(rules.Select(r => $"<li>{Fill(r, guj)}</li>"))}</ol>
                <div class="fill">{(guj ? "પોતાની વિગત ટેબલ ભરો" : "Please fill in your details in the table below.")}</div>
                <table class="grid"><tr><th style="width:40px">Sr.no</th><th>Name of Participation</th><th style="width:50px">Age</th><th style="width:80px">Flat No</th><th style="width:120px">Contact No</th></tr>
                {string.Concat(Enumerable.Range(1, 5).Select(i => $"<tr><td>{i}.</td><td></td><td></td><td></td><td></td></tr>"))}</table>
                <table style="width:100%;margin-top:4mm"><tr><td style="vertical-align:top">
                <div class="contact" style="margin-top:0">{(guj ? "જો તમને કોઈ પ્રશ્નો હોય, તો કૃપા કરીને સંપર્ક કરો:" : "If you have any questions, please contact:")}<br>
                  📞 {E(st.Contact1Phone)} – {E(st.Contact1Name)}{(st.Contact2Phone.Length > 0 ? $"<br>📞 {E(st.Contact2Phone)} – {E(st.Contact2Name)}" : "")}</div></td>
                {(qr.Length > 0 ? $"<td style=\"width:30mm;text-align:center;vertical-align:top\"><img src=\"{qr}\" width=\"92\" height=\"92\"><div style=\"font-size:8.5pt;font-weight:bold;color:{Maroon}\">{(guj ? "ઓનલાઇન નોંધણી માટે સ્કેન કરો" : "Scan to register online")}</div></td>" : "")}
                </tr></table>
                <div class="foot">RPL {E(s.Year)} – {E(st.Tagline)}</div>
                </div>
                """);
            return sb.ToString();
        }
        return $$"""
            <!doctype html><html><head><meta charset="utf-8"><title>RPL {{E(s.Year)}} registration form</title><style>
            @page { size: A4 portrait; margin: 0; }
            * { -webkit-print-color-adjust: exact; print-color-adjust: exact; box-sizing: border-box; }
            body { margin: 0; font-family: "Nirmala UI", "Segoe UI", sans-serif; color: #1f1a17; }
            .page { width: 210mm; height: 296mm; padding: 14mm 16mm 10mm; position: relative; break-after: page; overflow: hidden; }
            .page:last-child { break-after: auto; }
            .top { width: 100%; font-weight: bold; font-size: 14pt; font-family: Arial, sans-serif; }
            .brand { width: 100%; border-bottom: 2px solid {{Gold}}; margin: 4mm 0 4mm; } .brand td { vertical-align: middle; padding-bottom: 3mm; }
            .title { text-align: center; font-size: 40pt; font-weight: bold; color: {{Maroon}}; font-family: "Segoe UI", sans-serif; letter-spacing: 1pt; }
            .facts { font-size: 13pt; line-height: 1.6; margin: 0 0 3mm; padding-left: 8mm; }
            .h { text-align: center; font-size: 19pt; color: {{Maroon}}; font-weight: bold; border-bottom: 1px solid {{Gold}}; padding-bottom: 2mm; margin: 2mm 0 3mm; }
            .rules { font-size: 10.5pt; line-height: 1.4; padding-left: 8mm; margin: 0; } .rules li { margin-bottom: 1mm; }
            .fill { font-weight: bold; margin: 4mm 0 2mm; font-size: 11pt; }
            .grid { width: 100%; border-collapse: collapse; font-size: 10pt; } .grid th { background: {{Maroon}}; color: #fff; padding: 2mm; }
            .grid td { border: 1px solid #b8a99a; height: 7mm; padding: 0 2mm; }
            .contact { margin-top: 4mm; font-size: 10.5pt; line-height: 1.5; }
            .foot { position: absolute; left: 16mm; bottom: 8mm; font-family: Georgia, serif; font-weight: bold; color: {{Maroon}}; font-size: 10pt; }
            </style></head><body>{{Page(true)}}{{Page(false)}}</body></html>
            """;
    }

    // =================================================================================== team schedules

    /// <summary>First the full schedule by date, then one page per team: squad and its matches.</summary>
    public static string TeamSchedules(Season s)
    {
        var sb = new StringBuilder();
        sb.Append($"<!doctype html><html><head><meta charset=\"utf-8\"><title>RPL {E(s.Year)} schedules</title><style>{Css}" +
                  " @page { size: A4 portrait; margin: 12mm; } .pg { break-after: page; } .pg:last-child { break-after: auto; }</style></head><body>");

        var slots = s.Matches.Select(m => m.Time).Where(t => t.Length > 0).Distinct().OrderBy(t => Lists.Times.ToList().IndexOf(t) is var i && i < 0 ? 99 : i).ToList();
        sb.Append($"<div class=\"pg\"><table class=\"hero\"><tr><td style=\"width:90px\"><img src=\"{Office.Url(Office.Logo)}\" width=\"80\" height=\"80\"></td>" +
                  $"<td><div class=\"eyebrow\">RATNADEEP PREMIER LEAGUE</div><div class=\"h1\" style=\"font-size:22pt\">RPL {E(s.Year)} – Match Schedule</div></td></tr></table><br>" +
                  $"<table class=\"tbl\"><tr><th>Date</th>{string.Concat(slots.Select(t => $"<th>{E(t)}</th>"))}<th>Stage</th></tr>");
        int z = 0;
        foreach (var day in s.Matches.Where(m => m.Date is not null).GroupBy(m => m.Date!.Value.Date).OrderBy(g => g.Key))
        {
            sb.Append($"<tr class=\"{(z++ % 2 == 1 ? "z" : "")}\"><td style=\"white-space:nowrap\"><b>{day.Key:dd MMM yyyy}</b><br><span class=\"small\">{day.Key:dddd}</span></td>");
            foreach (var t in slots) sb.Append($"<td>{string.Join("<br>", day.Where(m => m.Time == t).Select(m => E(m.Title)))}</td>");
            sb.Append($"<td>{E(string.Join(", ", day.Select(m => m.Stage).Distinct()))}</td></tr>");
        }
        sb.Append("</table></div>");

        foreach (var team in s.Teams)
        {
            var squad = Calc.Squad(s, team.Name);
            var games = s.Matches.Where(m => Rules.SameTeam(m.TeamA, team.Name) || Rules.SameTeam(m.TeamB, team.Name)).OrderBy(m => m.Date ?? DateTime.MaxValue).ThenBy(m => m.Time).ToList();
            sb.Append($"<div class=\"pg\"><table class=\"hero\"><tr><td style=\"width:90px\"><img src=\"{Office.Url(Office.Logo)}\" width=\"80\" height=\"80\"></td>" +
                      $"<td><div class=\"eyebrow\">RPL {E(s.Year)}{(team.Group.Length > 0 ? " · GROUP " + E(team.Group.ToUpperInvariant()) : "")}</div><div class=\"h1\" style=\"font-size:24pt\">{E(team.Name.ToUpperInvariant())}</div>" +
                      $"<div class=\"lede\">Captain: {Dash(team.Captain)} · {squad.Count} players</div></td></tr></table>");
            sb.Append("<div class=\"h3\">Matches</div><table class=\"tbl\"><tr><th>Date</th><th>Opponent</th><th>Time</th><th>Stage</th><th>Result</th></tr>");
            foreach (var m in games)
            {
                string opp = Rules.SameTeam(m.TeamA, team.Name) ? m.TeamB : m.TeamA;
                string res = m.Winner.Length == 0 ? Nil : m.Winner == Lists.NoResult ? "No result" : Rules.SameTeam(m.Winner, team.Name) ? "<b style=\"color:#15803d\">Won</b>" : "<b style=\"color:#b91c1c\">Lost</b>";
                sb.Append($"<tr><td>{D(m.Date)}</td><td>vs <b>{E(opp)}</b></td><td>{Dash(m.Time)}</td><td>{Stage(m.Stage)}</td><td>{res}</td></tr>");
            }
            if (games.Count == 0) sb.Append("<tr><td colspan=\"5\" class=\"small\">No matches scheduled yet</td></tr>");
            sb.Append("</table><div class=\"h3\">Squad</div><table class=\"tbl\"><tr><th class=\"c\">#</th><th>Player</th><th class=\"c\">Category</th><th>Flat</th><th>Mobile</th></tr>");
            int i = 0;
            foreach (var p in squad)
                sb.Append($"<tr class=\"{(i % 2 == 1 ? "z" : "")}\"><td class=\"c\">{++i}</td><td><b>{E(p.Name)}</b>{(p.Name == team.Captain ? " (C)" : "")}</td><td class=\"c\">{Cat(p.Category)}</td>" +
                          $"<td>{E(p.Block)}{(p.Flat.Length > 0 ? "/" + E(p.Flat) : "")}</td><td>{Dash(p.Mobile)}</td></tr>");
            sb.Append("</table></div>");
        }
        sb.Append("</body></html>");
        return sb.ToString();
    }
}
