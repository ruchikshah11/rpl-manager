using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Rpl;

internal static class Brushes
{
    public static SolidColorBrush Hex(string hex) => new(Color.FromArgb(255,
        Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16)));
    public static readonly SolidColorBrush Clear = new(Colors.Transparent);
    public static SolidColorBrush Category(string c) => c switch
    {
        "A+" => Hex("#6B0F1A"), "A" => Hex("#9A3412"), "B" => Hex("#B45309"), "C" => Hex("#4D7C0F"), "D" => Hex("#0F766E"), "" => Clear, _ => Hex("#64748B"),
    };
    public static SolidColorBrush Payment(string p) => p switch
    {
        "Online" => Hex("#2563EB"), "Cash" => Hex("#15803D"), "Pending" or "" => Hex("#B91C1C"), _ => Hex("#64748B"),
    };
}

/// <summary>A row of the players list (and of the fees list and team squads).</summary>
public sealed class PlayerRow(Player p, int no, Season s)
{
    public Player Player { get; } = p;
    public string No { get; } = p.Cancelled ? "–" : no.ToString();
    public string FormNo { get; } = p.FormNo.Length == 0 ? "—" : p.FormNo;
    public string Name { get; } = p.Name + (p.Cancelled ? "  (cancelled)" : "");
    public double NameOpacity { get; } = p.Cancelled ? 0.45 : 1;
    public string Age { get; } = p.Age.Length == 0 ? "—" : p.Age;
    public string Flat { get; } = p.Block.Length + p.Flat.Length == 0 ? "—" : $"{p.Block}{(p.Flat.Length > 0 ? "/" + p.Flat : "")}";
    public string Mobile { get; } = p.Mobile.Length == 0 ? "—" : p.Mobile;
    public string Gender { get; } = p.Gender;
    public string Team { get; } = p.Team.Length == 0 ? "—" : p.Team;
    public string Category { get; } = p.Category.Length == 0 ? "—" : p.Category;
    public SolidColorBrush CategoryBrush { get; } = Brushes.Category(p.Category);
    public string Payment { get; } = !Rules.PaysFee(p) ? "free" : p.Payment.Length == 0 ? "Pending" : p.Payment;
    public SolidColorBrush PaymentBrush { get; } = !Rules.PaysFee(p) ? Brushes.Hex("#94A3B8") : Brushes.Payment(p.Payment);
    public string PaidTo { get; } = p.PaidTo;
    public string Captain { get; } = s.Teams.Any(t => t.Captain == p.Name && Rules.SameTeam(t.Name, p.Team)) ? "Captain" : "";
    public string Tooltip { get; } = string.Join(" · ", new[] { p.Gender, p.PaidTo, p.Notes }.Where(x => x.Length > 0));
}

public sealed class TeamRow(Team t, Season s)
{
    public Team Team { get; } = t;
    public string Name { get; } = t.Name;
    public string Division { get; } = t.Division;
    public SolidColorBrush DivisionBrush { get; } = Brushes.Hex(t.Division switch { "Women" => "#7C3AED", "Kids" => "#0F766E", _ => "#6B0F1A" });
    public string Captain { get; } = t.Captain.Length == 0 ? "No captain set" : $"Captain: {t.Captain}";
    public string Group { get; } = t.Group.Length == 0 ? "" : $"Group {t.Group}";
    public string Players { get; } = $"{Calc.Squad(s, t.Name).Count} players";
    public string Mix { get; } = string.Join("  ", Calc.Squad(s, t.Name).GroupBy(p => p.Category.Length == 0 ? "?" : p.Category)
        .OrderBy(g => Lists.Categories.ToList().IndexOf(g.Key) is var i && i < 0 ? 99 : i).Select(g => $"{g.Key}×{g.Count()}"));
    public string Matches { get; } = s.Matches.Count(m => Rules.SameTeam(m.TeamA, t.Name) || Rules.SameTeam(m.TeamB, t.Name)) is int n && n > 0 ? $"{n} matches" : "no matches";
}

public sealed class MatchRow(Match m, Season s)
{
    public Match Match { get; } = m;
    public string No { get; } = m.No.ToString();
    public string Date { get; } = m.Date?.ToString("ddd dd MMM") ?? "—";
    public string Time { get; } = m.Time.Length == 0 ? "—" : m.Time;
    public string Title { get; } = m.Title;
    public string Stage { get; } = (Calc.MatchDivision(s, m) is var d && d != "Men" ? d + " · " : "") + (Calc.IsLeague(m) ? "League" : m.Stage);
    public SolidColorBrush StageBrush { get; } = Calc.MatchDivision(s, m) switch { "Women" => Brushes.Hex("#7C3AED"), "Kids" => Brushes.Hex("#0F766E"), _ => Calc.IsLeague(m) ? Brushes.Clear : Brushes.Hex("#B45309") };
    public SolidColorBrush StageText { get; } = Calc.MatchDivision(s, m) == "Men" && Calc.IsLeague(m) ? Brushes.Hex("#94A3B8") : new SolidColorBrush(Colors.White);
    public string Result { get; } = (m.Winner.Length == 0 ? "not played" : m.Winner == Lists.NoResult ? "No result" : $"{m.Winner} won{(m.Result.Length > 0 ? " " + m.Result : "")}")
                                    + (m.ScoreA.HasScore || m.ScoreB.HasScore ? $"  ({m.ScoreA} · {m.ScoreB})" : "");
    public double ResultOpacity { get; } = m.Played ? 1 : 0.5;
    public string Pom { get; } = m.Awards.FirstOrDefault(a => a.Title == "Player of the Match")?.Player ?? "";
    public string Extra { get; } = string.Join("  ", new[] { m.Performances.Count > 0 ? "scorecard" : "", m.Absent.Count > 0 ? $"{m.Absent.Count} absent" : "" }.Where(x => x.Length > 0));
}

public sealed class StandingRow(Standing x, int rank)
{
    public string Rank { get; } = rank.ToString();
    public string Team { get; } = x.Team;
    public string Played { get; } = x.Played.ToString();
    public string Won { get; } = x.Won.ToString();
    public string Lost { get; } = x.Lost.ToString();
    public string NoResult { get; } = x.NoResult.ToString();
    public string Nrr { get; } = x.NetRunRate.ToString("+0.000;-0.000;0.000", System.Globalization.CultureInfo.InvariantCulture);
    public string Points { get; } = x.Points.ToString();
    public SolidColorBrush RankBrush { get; } = rank <= 4 && x.Played > 0 ? Brushes.Hex("#D4A64A") : Brushes.Clear;
}

/// <summary>A points table with its division name (dashboard shows one per division).</summary>
public sealed class DivisionTable(string title, List<StandingRow> rows)
{
    public string Title { get; } = title;
    public List<StandingRow> Rows { get; } = rows;
}

public sealed class BattingRow(BattingStat b, int rank)
{
    public string Rank { get; } = rank.ToString();
    public string Player { get; } = b.Player;
    public string Team { get; } = b.Team;
    public string Innings { get; } = b.Innings.ToString();
    public string Runs { get; } = b.Runs.ToString();
    public string Highest { get; } = b.Highest.ToString();
    public string StrikeRate { get; } = b.StrikeRate.ToString("0.0");
    public string Average { get; } = b.Average;
    public SolidColorBrush CapBrush { get; } = rank == 1 ? Brushes.Hex("#EA580C") : Brushes.Clear;
}

public sealed class BowlingRow(BowlingStat b, int rank)
{
    public string Rank { get; } = rank.ToString();
    public string Player { get; } = b.Player;
    public string Team { get; } = b.Team;
    public string Overs { get; } = b.Overs;
    public string Wickets { get; } = b.Wickets.ToString();
    public string Runs { get; } = b.Runs.ToString();
    public string Economy { get; } = b.Economy.ToString("0.00");
    public string Best { get; } = b.Best;
    public SolidColorBrush CapBrush { get; } = rank == 1 ? Brushes.Hex("#7C3AED") : Brushes.Clear;
}

/// <summary>A team's button and summary on the Auction page.</summary>
public sealed class AuctionTeam(Team t, Season s)
{
    public Team Team { get; } = t;
    public string Name { get; } = t.Name;
    public int Purse { get; } = Calc.Purse(s, t.Name);
    public string PurseText { get; } = $"{Calc.Purse(s, t.Name):N0} left";
    public string Players { get; } = $"{Calc.Squad(s, t.Name).Count} players";
    public string Spent { get; } = $"spent {Calc.Spent(s, t.Name):N0}";
    public string Mix { get; } = string.Join("  ", Calc.Squad(s, t.Name).GroupBy(p => p.Category.Length == 0 ? "?" : p.Category)
        .OrderBy(g => Lists.Categories.ToList().IndexOf(g.Key) is var i && i < 0 ? 99 : i).Select(g => $"{g.Key}×{g.Count()}"));
}

public sealed class SoldRow(Player p)
{
    public string Name { get; } = p.Name;
    public string Category { get; } = p.Category.Length == 0 ? "—" : p.Category;
    public SolidColorBrush CategoryBrush { get; } = Brushes.Category(p.Category);
    public string Team { get; } = p.Team;
    public string Price { get; } = p.SoldPrice?.ToString("N0") ?? "";
}

public sealed class HonourRow(SeasonHonours h)
{
    public string Year { get; } = h.Year;
    public string Division { get; } = h.Division;
    public string Champions { get; } = h.Champions.Length == 0 ? "—" : h.Champions;
    public string RunnersUp { get; } = h.RunnersUp.Length == 0 ? "—" : h.RunnersUp;
    public string ManOfSeries { get; } = h.ManOfSeries.Length == 0 ? "—" : h.ManOfSeries;
    public string TopScorer { get; } = h.TopScorer.Length == 0 ? "—" : h.TopScorer;
    public string TopWicketTaker { get; } = h.TopWicketTaker.Length == 0 ? "—" : h.TopWicketTaker;
}

public sealed class LeaderRow(int rank, string name, int value)
{
    public string Rank { get; } = rank.ToString();
    public string Name { get; } = name;
    public string Value { get; } = value.ToString();
}

/// <summary>A player whose fee is pending, with their WhatsApp reminder.</summary>
public sealed class ReminderRow(Player p, string message)
{
    public Player Player { get; } = p;
    public string Name { get; } = p.Name;
    public string Mobile { get; } = p.Mobile.Length == 0 ? "no mobile" : p.Mobile;
    public string Team { get; } = p.Team;
    public bool HasMobile { get; } = Rules.ValidMobile(p.Mobile);
    public string Message { get; } = message;
}

public sealed class AwardRow(AwardCount a)
{
    public string Player { get; } = a.Player;
    public string Pom { get; } = a.PlayerOfMatch.ToString();
    public string Total { get; } = a.Total.ToString();
    public string Titles { get; } = a.Titles;
}

public sealed class SponsorRow(Sponsor s)
{
    public Sponsor Sponsor { get; } = s;
    public string Name { get; } = s.Name;
    public string Amount { get; } = Rules.Money(s.Amount);
    public string Mode { get; } = s.Mode;
    public string Notes { get; } = s.Notes;
}

public sealed class ExpenseRow(Expense e)
{
    public Expense Expense { get; } = e;
    public string Item { get; } = e.Item;
    public string PaidBy { get; } = e.PaidBy.Length == 0 ? "—" : e.PaidBy;
    public string Amount { get; } = Rules.Money(e.Amount);
    public string Mode { get; } = e.Mode;
    public string Paid { get; } = e.Paid ? "Paid" : "Not paid";
    public SolidColorBrush PaidBrush { get; } = e.Paid ? Brushes.Hex("#15803D") : Brushes.Hex("#B91C1C");
    public string Notes { get; } = e.Notes;
}

public sealed class BarVm(string label, string value, double fraction, SolidColorBrush brush, double width = 220)
{
    public string Label { get; } = label;
    public string Value { get; } = value;
    public double BarWidth { get; } = Math.Max(2, Math.Clamp(fraction, 0, 1) * width);
    public double TrackWidth { get; } = width;
    public SolidColorBrush Brush { get; } = brush;
}
