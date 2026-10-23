using System.Globalization;
using System.Net;
using System.Text;
using QRCoder;

namespace Rpl;

/// <summary>
/// Posters for WhatsApp (1080×1350 PNG), award certificates, player ID cards, scorer's sheets,
/// the hall of fame, and the registration QR code. All as HTML for Edge.
/// </summary>
public static class Printouts
{
    private const string Maroon = "#6b0f1a", Gold = "#d4a64a", Cream = "#fbf6ec";
    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
    private static string Logo => Office.Url(Office.Logo);
    public const int PosterWidth = 1080, PosterHeight = 1350;

    // ==================================================================================== QR code

    /// <summary>PNG data URI of a QR code for the online registration link.</summary>
    public static string QrDataUri(string text)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        byte[] png = new PngByteQRCode(data).GetGraphic(10, [107, 15, 26], [255, 255, 255]);
        return "data:image/png;base64," + Convert.ToBase64String(png);
    }

    // ==================================================================================== posters

    private static string PosterPage(string eyebrow, string title, string body, string footer) => $$"""
        <!doctype html><html><head><meta charset="utf-8"><style>
        * { box-sizing: border-box; margin: 0; padding: 0; }
        html, body { width: {{PosterWidth}}px; height: {{PosterHeight}}px; overflow: hidden; }
        body { font-family: "Segoe UI", "Nirmala UI", sans-serif; color: #fff;
               background: radial-gradient(ellipse at 50% 30%, #9b1027 0%, #6b0f1a 45%, #3d0710 100%); }
        .wrap { position: absolute; inset: 28px; border: 3px solid {{Gold}}; border-radius: 26px; padding: 44px 56px; }
        .head { display: flex; align-items: center; gap: 28px; }
        .head img { width: 150px; height: 150px; border-radius: 20px; box-shadow: 0 0 0 4px {{Gold}}; }
        .eyebrow { color: {{Gold}}; font-size: 30px; letter-spacing: 8px; font-weight: 700; }
        .title { font-size: 74px; font-weight: 800; line-height: 1.05; margin-top: 6px; }
        .body { margin-top: 46px; }
        .foot { position: absolute; left: 56px; right: 56px; bottom: 36px; display: flex; justify-content: space-between; color: {{Gold}}; font-size: 26px; font-weight: 700; letter-spacing: 3px; }
        .card { background: rgba(255,255,255,0.08); border: 1px solid rgba(212,166,74,0.55); border-radius: 22px; padding: 26px 32px; margin-bottom: 24px; }
        .slot { display: inline-block; background: {{Gold}}; color: #3d0710; font-weight: 800; font-size: 28px; padding: 6px 18px; border-radius: 30px; }
        .stage { display: inline-block; margin-left: 12px; border: 2px solid {{Gold}}; color: {{Gold}}; font-weight: 700; font-size: 24px; padding: 4px 14px; border-radius: 30px; }
        .vs { display: flex; align-items: center; justify-content: space-between; margin-top: 18px; font-size: 40px; font-weight: 800; }
        .vs .x { color: {{Gold}}; font-size: 30px; padding: 0 20px; }
        .vs .t { flex: 1; } .vs .t2 { flex: 1; text-align: right; }
        .score { font-size: 30px; color: #f3e3c3; font-weight: 600; }
        .big { font-size: 64px; font-weight: 800; color: {{Gold}}; }
        table { width: 100%; border-collapse: collapse; font-size: 32px; }
        th { color: {{Gold}}; text-align: left; font-size: 24px; letter-spacing: 2px; padding: 10px 8px; border-bottom: 2px solid {{Gold}}; }
        td { padding: 16px 8px; border-bottom: 1px solid rgba(255,255,255,0.15); font-weight: 600; }
        td.n, th.n { text-align: center; } tr.q td { color: #ffe7a8; }
        </style></head><body><div class="wrap">
          <div class="head"><img src="{{Logo}}"><div><div class="eyebrow">{{E(eyebrow)}}</div><div class="title">{{E(title)}}</div></div></div>
          <div class="body">{{body}}</div>
          <div class="foot"><span>RATNADEEP PREMIER LEAGUE</span><span>{{E(footer)}}</span></div>
        </div></body></html>
        """;

    /// <summary>"Match day" poster: every match on a date with time and stage.</summary>
    public static string FixturesPoster(Season s, DateTime date)
    {
        var games = s.Matches.Where(m => m.Date?.Date == date.Date).OrderBy(m => Lists.Times.ToList().IndexOf(m.Time) is var i && i < 0 ? 99 : i).ToList();
        var body = new StringBuilder();
        foreach (var m in games)
            body.Append($"<div class=\"card\"><span class=\"slot\">{E(m.Time.Length > 0 ? m.Time : "Time TBA")}</span>" +
                        $"<span class=\"stage\">{E(StageLabel(s, m))}</span>" +
                        $"<div class=\"vs\"><span class=\"t\">{E(m.TeamA)}</span><span class=\"x\">VS</span><span class=\"t2\">{E(m.TeamB)}</span></div></div>");
        if (games.Count == 0) body.Append("<div class=\"card\" style=\"font-size:36px\">No matches on this date.</div>");
        else body.Append("<div style=\"font-size:28px;color:#f3e3c3;margin-top:10px\">Please be on the ground on time – no replacements for absent players.</div>");
        return PosterPage($"RPL {s.Year} · MATCH DAY", date.ToString("dddd, d MMMM", CultureInfo.InvariantCulture), body.ToString(), s.Settings.Tagline);
    }

    /// <summary>Result poster: score, winner, Player of the Match and the other awards.</summary>
    public static string ResultPoster(Season s, Match m)
    {
        string winner = m.Winner == Lists.NoResult ? "NO RESULT" : m.Winner.Length == 0 ? "RESULT TO COME" : $"{m.Winner.ToUpperInvariant()} WON";
        var pom = m.Awards.Where(a => a.Title == "Player of the Match").Select(a => a.Player).FirstOrDefault();
        var others = m.Awards.Where(a => a.Title != "Player of the Match").GroupBy(a => a.Player).Select(g => $"<b>{E(g.Key)}</b> – {E(string.Join(", ", g.Select(a => a.Title)))}");
        string body = $"""
            <div class="card"><span class="stage" style="margin-left:0">{E(StageLabel(s, m))}</span>
              <div class="vs"><span class="t">{E(m.TeamA)}<div class="score">{E(m.ScoreA.ToString())}</div></span><span class="x">VS</span>
                   <span class="t2">{E(m.TeamB)}<div class="score">{E(m.ScoreB.ToString())}</div></span></div></div>
            <div class="card" style="text-align:center"><div class="big">{E(winner)}</div>
              {(m.Result.Length > 0 ? $"<div style=\"font-size:34px;margin-top:8px\">{E(m.Result)}</div>" : "")}</div>
            {(pom is null ? "" : $"<div class=\"card\" style=\"text-align:center\"><div style=\"color:{Gold};font-size:28px;letter-spacing:6px;font-weight:700\">PLAYER OF THE MATCH</div><div style=\"font-size:58px;font-weight:800;margin-top:8px\">{E(pom)}</div></div>")}
            {(others.Any() ? $"<div class=\"card\" style=\"font-size:30px;line-height:1.6\">{string.Join("<br>", others)}</div>" : "")}
            """;
        return PosterPage($"RPL {s.Year} · MATCH {m.No}", m.Date?.ToString("d MMMM", CultureInfo.InvariantCulture) ?? "Result", body, m.Time);
    }

    public static string PointsPoster(Season s, string division)
    {
        var rows = Calc.PointsTable(s, division);
        var body = new StringBuilder("<table><tr><th class=\"n\">#</th><th>TEAM</th><th class=\"n\">P</th><th class=\"n\">W</th><th class=\"n\">L</th><th class=\"n\">NRR</th><th class=\"n\">PTS</th></tr>");
        int i = 0;
        foreach (var r in rows)
            body.Append($"<tr class=\"{(++i <= 4 && r.Played > 0 ? "q" : "")}\"><td class=\"n\">{i}</td><td>{E(r.Team)}</td><td class=\"n\">{r.Played}</td><td class=\"n\">{r.Won}</td><td class=\"n\">{r.Lost}</td>" +
                        $"<td class=\"n\">{r.NetRunRate.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture)}</td><td class=\"n\" style=\"color:{Gold};font-size:38px\">{r.Points}</td></tr>");
        body.Append("</table><div style=\"font-size:26px;color:#f3e3c3;margin-top:22px\">Top 4 go to the playoffs</div>");
        return PosterPage($"RPL {s.Year} · {division.ToUpperInvariant()}", "Points Table", body.ToString(), $"after {s.Matches.Count(m => m.Played && Calc.IsLeague(m) && Calc.MatchDivision(s, m) == division)} matches");
    }

    private static string StageLabel(Season s, Match m)
    {
        string d = Calc.MatchDivision(s, m);
        string stage = Calc.IsLeague(m) ? "League" : m.Stage;
        return d == "Men" ? stage : $"{d} · {stage}";
    }

    // ==================================================================================== certificates

    public sealed record CertificateItem(string Title, string Name, string Line, string Medal);

    /// <summary>Who gets a certificate: Player of the Match for every match, plus season awards per division.</summary>
    public static List<CertificateItem> CertificateList(Season s, bool matchAwards, bool seasonAwards)
    {
        var list = new List<CertificateItem>();
        if (matchAwards)
            foreach (var m in s.Matches.OrderBy(m => m.No))
                foreach (var g in m.Awards.Where(a => a.Player.Length > 0).GroupBy(a => a.Player))
                {
                    var titles = g.Select(a => a.Title).ToList();
                    string main = titles.Contains("Player of the Match") ? "Player of the Match" : titles[0];
                    string extra = string.Join(", ", titles.Where(t => t != main));
                    list.Add(new CertificateItem(main, g.Key,
                        $"for an outstanding performance in Match {m.No}, <b>{E(m.TeamA)}</b> vs <b>{E(m.TeamB)}</b>{(m.Date is { } d ? $" on {d:d MMMM yyyy}" : "")}" +
                        (extra.Length > 0 ? $" – also {E(extra)}" : "") + ".", "★"));
                }
        if (seasonAwards)
            foreach (var div in Calc.ActiveDivisions(s))
            {
                string suffix = div == "Men" ? "" : $" ({div})";
                if (s.Settings.ManOfSeries.GetValueOrDefault(div, "") is { Length: > 0 } mos)
                    list.Add(new CertificateItem("Man of the Series" + suffix, mos, $"for the most valuable all-round contribution in RPL {s.Year}.", "MVP"));
                if (Calc.Batting(s, div).FirstOrDefault() is { } bat)
                    list.Add(new CertificateItem("Orange Cap" + suffix, bat.Player, $"for scoring the most runs in RPL {s.Year}: <b>{bat.Runs} runs</b> in {bat.Innings} innings.", "🧢"));
                if (Calc.Bowling(s, div).FirstOrDefault() is { } bowl)
                    list.Add(new CertificateItem("Purple Cap" + suffix, bowl.Player, $"for taking the most wickets in RPL {s.Year}: <b>{bowl.Wickets} wickets</b>, best {E(bowl.Best)}.", "🧢"));
                var (champ, runner) = Calc.Final(s, div);
                foreach (var p in champ.Length > 0 ? Calc.Squad(s, champ) : [])
                    list.Add(new CertificateItem("Champions" + suffix, p.Name, $"as a member of <b>{E(champ)}</b>, winners of RPL {s.Year}.", "🏆"));
                foreach (var p in runner.Length > 0 ? Calc.Squad(s, runner) : [])
                    list.Add(new CertificateItem("Runners-up" + suffix, p.Name, $"as a member of <b>{E(runner)}</b>, runners-up of RPL {s.Year}.", "🥈"));
            }
        return list;
    }

    public static string Certificates(Season s, IReadOnlyList<CertificateItem> items)
    {
        var sb = new StringBuilder();
        sb.Append($$"""
            <!doctype html><html><head><meta charset="utf-8"><style>
            @page { size: A4 landscape; margin: 0; }
            * { box-sizing: border-box; -webkit-print-color-adjust: exact; print-color-adjust: exact; }
            body { margin: 0; font-family: "Segoe UI", sans-serif; color: #2a1215; }
            .page { width: 297mm; height: 209mm; padding: 9mm; break-after: page; }
            .page:last-child { break-after: auto; }
            .outer { height: 100%; border: 4px solid {{Maroon}}; padding: 3mm; background: {{Cream}}; }
            .inner { height: 100%; border: 1.5px solid {{Gold}}; position: relative; text-align: center; padding: 10mm 24mm 0; }
            .logo { width: 34mm; height: 34mm; border-radius: 3mm; }
            .league { font-size: 12pt; letter-spacing: 5pt; color: {{Maroon}}; font-weight: 700; margin-top: 3mm; }
            .title { font-family: Georgia, serif; font-size: 38pt; color: {{Maroon}}; margin: 3mm 0 1mm; }
            .rule { width: 80mm; height: 2px; background: linear-gradient(90deg, transparent, {{Gold}}, transparent); margin: 0 auto 6mm; }
            .lead { font-family: Georgia, serif; font-style: italic; font-size: 13pt; color: #6b5a52; }
            .name { font-family: Georgia, serif; font-size: 34pt; font-weight: bold; margin: 3mm 0; }
            .text { font-size: 14pt; line-height: 1.55; max-width: 210mm; margin: 0 auto; color: #3b2a2c; }
            .medal { position: absolute; right: 22mm; top: 16mm; width: 30mm; height: 30mm; border-radius: 50%; background: {{Maroon}};
                     color: {{Gold}}; display: flex; align-items: center; justify-content: center; font-weight: 800; font-size: 18pt; box-shadow: 0 0 0 3px {{Gold}}; }
            .sign { position: absolute; left: 30mm; right: 30mm; bottom: 14mm; display: flex; justify-content: space-between; align-items: flex-end; }
            .sign div { width: 60mm; border-top: 1px solid #8a7a72; padding-top: 2mm; font-size: 10pt; color: #6b5a52; }
            </style></head><body>
            """);
        foreach (var c in items)
            sb.Append($"""
                <div class="page"><div class="outer"><div class="inner">
                  <div class="medal">{E(c.Medal)}</div>
                  <img class="logo" src="{Logo}"><div class="league">RATNADEEP PREMIER LEAGUE {E(s.Year)}</div>
                  <div class="title">{E(c.Title)}</div><div class="rule"></div>
                  <div class="lead">This certificate is proudly presented to</div>
                  <div class="name">{E(c.Name)}</div>
                  <div class="text">{c.Line}</div>
                  <div class="sign"><div>Organiser</div><span style="font-size:10pt;color:#6b5a52">{DateTime.Today:dd MMMM yyyy}</span><div>Committee</div></div>
                </div></div></div>
                """);
        sb.Append("</body></html>");
        return sb.ToString();
    }

    // ==================================================================================== ID cards

    /// <summary>Credit-card size player cards, 10 per A4 page.</summary>
    public static string IdCards(Season s, IEnumerable<Player> players, string photoFolder)
    {
        var sb = new StringBuilder();
        sb.Append($$"""
            <!doctype html><html><head><meta charset="utf-8"><style>
            @page { size: A4 portrait; margin: 10mm 12mm; }
            * { box-sizing: border-box; -webkit-print-color-adjust: exact; print-color-adjust: exact; }
            body { margin: 0; font-family: "Segoe UI", "Nirmala UI", sans-serif; }
            .grid { display: grid; grid-template-columns: 86mm 86mm; gap: 5mm 8mm; justify-content: center; }
            .card { width: 86mm; height: 54mm; border-radius: 3.5mm; overflow: hidden; display: flex; border: 0.4mm solid {{Gold}}; break-inside: avoid; }
            .band { width: 26mm; background: linear-gradient(180deg, #9b1027, {{Maroon}}); display: flex; flex-direction: column; align-items: center; justify-content: space-between; padding: 3mm 0; color: {{Gold}}; }
            .band img { width: 20mm; height: 20mm; border-radius: 2mm; }
            .band .yr { font-weight: 800; font-size: 10pt; letter-spacing: 1pt; }
            .band .div { font-size: 7pt; letter-spacing: 1pt; color: #f3e3c3; }
            .main { flex: 1; background: {{Cream}}; padding: 3mm 3.5mm; position: relative; }
            .photo { position: absolute; right: 3.5mm; top: 3mm; width: 17mm; height: 21mm; border-radius: 1.5mm; object-fit: cover; border: 0.3mm solid {{Gold}}; }
            .initials { position: absolute; right: 3.5mm; top: 3mm; width: 17mm; height: 21mm; border-radius: 1.5mm; background: {{Maroon}}; color: {{Gold}}; display: flex; align-items: center; justify-content: center; font-size: 15pt; font-weight: 800; }
            .name { font-size: 12pt; font-weight: 800; color: {{Maroon}}; width: 36mm; line-height: 1.15; }
            .team { font-size: 8.5pt; font-weight: 700; color: #3b2a2c; margin-top: 1.5mm; width: 36mm; }
            .rows { position: absolute; left: 3.5mm; right: 3.5mm; bottom: 3mm; font-size: 7.5pt; color: #4b3a3c; }
            .rows b { color: {{Maroon}}; }
            .cat { display: inline-block; background: {{Maroon}}; color: #fff; font-weight: 800; padding: 0.4mm 1.8mm; border-radius: 1mm; font-size: 8pt; }
            </style></head><body><div class="grid">
            """);
        foreach (var p in players)
        {
            string photo = p.Photo.Length > 0 && File.Exists(Path.Combine(photoFolder, p.Photo))
                ? $"<img class=\"photo\" src=\"{Office.Url(Path.Combine(photoFolder, p.Photo))}\">"
                : $"<div class=\"initials\">{E(string.Concat(p.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => w[0])).ToUpperInvariant())}</div>";
            sb.Append($"""
                <div class="card"><div class="band"><img src="{Logo}"><div class="yr">RPL {E(s.Year)}</div><div class="div">{E(p.Division.ToUpperInvariant())}</div></div>
                  <div class="main">{photo}
                    <div class="name">{E(p.Name)}</div>
                    <div class="team">{E(p.Team.Length > 0 ? p.Team : "Team to be decided")}</div>
                    <div class="rows">{(p.Category.Length > 0 ? $"<span class=\"cat\">{E(p.Category)}</span> &nbsp;" : "")}
                      <b>Flat</b> {E(p.Block)}{(p.Flat.Length > 0 ? "/" + E(p.Flat) : "")} &nbsp; <b>Form</b> {E(p.FormNo.Length > 0 ? p.FormNo : "—")}<br>
                      <b>Age</b> {E(p.Age.Length > 0 ? p.Age : "—")} &nbsp; <b>Player</b> #{E(p.Id.ToString()[..4].ToUpperInvariant())}</div>
                  </div></div>
                """);
        }
        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    // ==================================================================================== scorer's sheet

    /// <summary>One A4 page per match: both innings with the batting side's squad filled in, bowling figures, extras.</summary>
    public static string ScorerSheets(Season s, IEnumerable<Match> matches)
    {
        var sb = new StringBuilder();
        sb.Append($$"""
            <!doctype html><html><head><meta charset="utf-8"><style>
            @page { size: A4 portrait; margin: 8mm 10mm; }
            * { box-sizing: border-box; -webkit-print-color-adjust: exact; print-color-adjust: exact; }
            body { margin: 0; font-family: "Segoe UI", sans-serif; font-size: 8.5pt; color: #1f1a17; }
            .page { break-after: page; } .page:last-child { break-after: auto; }
            .hd { display: flex; align-items: center; gap: 4mm; background: {{Maroon}}; color: #fff; padding: 2.5mm 4mm; border-radius: 2mm; }
            .hd img { width: 13mm; height: 13mm; border-radius: 1.5mm; }
            .hd .t { font-size: 13pt; font-weight: 800; } .hd .m { color: {{Gold}}; font-size: 9pt; font-weight: 700; }
            .inn { margin-top: 3mm; border: 1px solid #b8a99a; border-radius: 2mm; padding: 2mm 3mm; }
            .inn h3 { margin: 0 0 1.5mm; color: {{Maroon}}; font-size: 10pt; }
            table { width: 100%; border-collapse: collapse; }
            th { background: #f3e9d8; font-size: 7.5pt; text-align: left; padding: 1mm 1.5mm; border: 1px solid #c9b9a6; }
            td { border: 1px solid #c9b9a6; height: 5.6mm; padding: 0 1.5mm; }
            .n { text-align: center; width: 9mm; } .w { width: 34mm; }
            .line { margin-top: 1.5mm; display: flex; gap: 6mm; font-weight: 700; }
            .line span { flex: 1; border-bottom: 1px solid #8a7a72; padding-bottom: 0.5mm; }
            .absent { color: #b91c1c; font-weight: 700; }
            </style></head><body>
            """);
        foreach (var m in matches)
        {
            sb.Append($"<div class=\"page\"><div class=\"hd\"><img src=\"{Logo}\"><div><div class=\"t\">{E(m.TeamA)} vs {E(m.TeamB)}</div>" +
                      $"<div class=\"m\">RPL {E(s.Year)} · Match {m.No} · {E(StageLabel(s, m))} · {(m.Date is { } d ? d.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture) : "date TBA")} · {E(m.Time)}</div></div></div>");
            sb.Append("<div class=\"line\"><span>Toss won by:</span><span>Elected to:</span><span>Umpires:</span></div>");
            foreach (var (bat, bowl) in new[] { (m.TeamA, m.TeamB), (m.TeamB, m.TeamA) })
            {
                var squad = Calc.Squad(s, bat);
                var absent = squad.Where(p => m.Absent.Contains(p.Name)).Select(p => p.Name).ToList();
                int rows = Math.Clamp(squad.Count, 8, 11);
                sb.Append($"<div class=\"inn\"><h3>{E(bat)} batting{(absent.Count > 0 ? $" <span class=\"absent\">– absent: {E(string.Join(", ", absent))}</span>" : "")}</h3>" +
                          "<table><tr><th class=\"n\">#</th><th class=\"w\">Batter</th><th>How out</th><th style=\"width:26mm\">Bowler</th><th class=\"n\">R</th><th class=\"n\">B</th><th class=\"n\">4s</th><th class=\"n\">6s</th></tr>");
                var names = squad.Where(p => !absent.Contains(p.Name)).Select(p => p.Name).ToList();
                for (int i = 0; i < rows; i++)
                    sb.Append($"<tr><td class=\"n\">{i + 1}</td><td>{(i < names.Count ? E(names[i]) : "")}</td><td></td><td></td><td></td><td></td><td></td><td></td></tr>");
                sb.Append("</table><div class=\"line\"><span>Extras:</span><span>Total:</span><span>Wickets:</span><span>Overs:</span></div>" +
                          $"<table style=\"margin-top:1.5mm\"><tr><th class=\"w\">{E(bowl)} bowling</th><th class=\"n\">O</th><th class=\"n\">M</th><th class=\"n\">R</th><th class=\"n\">W</th><th>Over by over</th></tr>");
                for (int i = 0; i < 5; i++) sb.Append("<tr><td></td><td></td><td></td><td></td><td></td><td></td></tr>");
                sb.Append("</table></div>");
            }
            sb.Append("<div class=\"line\" style=\"margin-top:3mm\"><span>Result:</span><span>Player of the Match:</span><span>Scorer's signature:</span></div></div>");
        }
        sb.Append("</body></html>");
        return sb.ToString();
    }

    // ==================================================================================== hall of fame

    public static string HallOfFame(IReadOnlyList<Season> seasons)
    {
        var honours = Calc.HallOfFame(seasons);
        var (pom, runs, wkts, titles) = Calc.AllTime(seasons);
        string Dash(string v) => v.Length == 0 ? "<span style=\"color:#c8bfb4\">—</span>" : E(v);
        var sb = new StringBuilder();
        sb.Append($$"""
            <!doctype html><html><head><meta charset="utf-8"><style>
            @page { size: A4 landscape; margin: 12mm; }
            * { -webkit-print-color-adjust: exact; print-color-adjust: exact; }
            body { margin: 0; font-family: "Segoe UI", sans-serif; color: #1f1a17; font-size: 10pt; }
            .hero { display: flex; align-items: center; gap: 16px; background: {{Maroon}}; color: #fff; padding: 14px 18px; }
            .hero img { width: 80px; height: 80px; } .hero .e { color: {{Gold}}; letter-spacing: 3pt; font-weight: 700; font-size: 9pt; } .hero .t { font-size: 26pt; font-weight: 800; }
            h3 { color: {{Maroon}}; text-transform: uppercase; letter-spacing: 1pt; font-size: 11pt; margin: 14pt 0 6pt; }
            table { width: 100%; border-collapse: collapse; border: 1px solid #e5d9c6; }
            th { background: {{Maroon}}; color: #fff; text-align: left; font-size: 8pt; text-transform: uppercase; padding: 6pt; }
            td { padding: 5pt 6pt; border-top: 1px solid #efe6d8; } tr:nth-child(even) td { background: {{Cream}}; }
            .cols { display: flex; gap: 12pt; } .cols > div { flex: 1; }
            </style></head><body>
            <div class="hero"><img src="{{Logo}}"><div><div class="e">RATNADEEP PREMIER LEAGUE</div><div class="t">Hall of Fame</div></div></div>
            <h3>Season honours</h3>
            <table><tr><th>Season</th><th>Division</th><th>Champions</th><th>Runners-up</th><th>Man of the Series</th><th>Most runs</th><th>Most wickets</th></tr>
            """);
        foreach (var h in honours)
            sb.Append($"<tr><td><b>{E(h.Year)}</b></td><td>{E(h.Division)}</td><td><b>{Dash(h.Champions)}</b></td><td>{Dash(h.RunnersUp)}</td><td>{Dash(h.ManOfSeries)}</td><td>{Dash(h.TopScorer)}</td><td>{Dash(h.TopWicketTaker)}</td></tr>");
        sb.Append("</table><div class=\"cols\">");
        void Board(string title, string unit, IEnumerable<(string Name, int Value)> rows)
        {
            sb.Append($"<div><h3>{E(title)}</h3><table><tr><th>#</th><th>Name</th><th style=\"text-align:right\">{E(unit)}</th></tr>");
            int i = 0;
            foreach (var (name, value) in rows) sb.Append($"<tr><td>{++i}</td><td><b>{E(name)}</b></td><td style=\"text-align:right\">{value}</td></tr>");
            if (i == 0) sb.Append("<tr><td colspan=\"3\" style=\"color:#8a7a72\">Not recorded yet</td></tr>");
            sb.Append("</table></div>");
        }
        Board("Most Player of the Match", "Awards", pom);
        Board("Most runs (all seasons)", "Runs", runs);
        Board("Most wickets (all seasons)", "Wickets", wkts);
        Board("Most titles", "Titles", titles);
        sb.Append("</div></body></html>");
        return sb.ToString();
    }
}
