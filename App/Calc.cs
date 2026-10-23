namespace Rpl;

/// <summary>Money for the season, the same way the Accounts sheet works it out (fee from the season settings).</summary>
public sealed class Accounts
{
    public required int Fee { get; init; }
    public int Registered { get; init; }            // forms not cancelled
    public int Cancelled { get; init; }
    public int Male { get; init; }
    public int Female { get; init; }
    public int Kids { get; init; }
    public int FeePlayers { get; init; }            // men, not cancelled
    public int PaidOnline { get; init; }
    public int PaidCash { get; init; }
    public int Pending { get; init; }
    public int Waived { get; init; }
    public decimal SponsorCash { get; init; }
    public decimal SponsorOnline { get; init; }
    public decimal ExpensesPaid { get; init; }
    public decimal ExpensesUnpaid { get; init; }
    public decimal ExpensesCash { get; init; }      // paid in cash
    public decimal CashCounted { get; init; }

    public decimal FeeOnline => PaidOnline * Fee;
    public decimal FeeCash => PaidCash * Fee;
    public decimal FeeCollected => FeeOnline + FeeCash;
    public decimal FeePending => Pending * Fee;
    public decimal Sponsors => SponsorCash + SponsorOnline;
    public decimal Income => FeeCollected + Sponsors;
    public decimal Expenses => ExpensesPaid + ExpensesUnpaid;
    public decimal Balance => Income - Expenses;

    /// <summary>Cash that should be in hand: cash fees + cash sponsors - expenses paid in cash.</summary>
    public decimal CashExpected => FeeCash + SponsorCash - ExpensesCash;
    public decimal CashDifference => CashCounted - CashExpected;
}

public sealed record Standing(string Team, int Played, int Won, int Lost, int NoResult, double NetRunRate = 0)
{
    public int Points => 2 * Won + NoResult;
}

public sealed record BattingStat(string Player, string Team, int Innings, int Runs, int Balls, int Highest, int NotOuts)
{
    public double StrikeRate => Balls == 0 ? 0 : Math.Round(100.0 * Runs / Balls, 1);
    public string Average => Innings - NotOuts == 0 ? (Innings > 0 ? Runs.ToString() : "—") : (1.0 * Runs / (Innings - NotOuts)).ToString("0.0");
}

public sealed record BowlingStat(string Player, string Team, int Balls, int Runs, int Wickets, string Best)
{
    public string Overs => $"{Balls / 6}.{Balls % 6}";
    public double Economy => Balls == 0 ? 0 : Math.Round(Runs * 6.0 / Balls, 2);
}

public sealed record SeasonHonours(string Year, string Division, string Champions, string RunnersUp, string ManOfSeries, string TopScorer, string TopWicketTaker);

public sealed record AwardCount(string Player, int PlayerOfMatch, int Total, string Titles);

public static class Calc
{
    public static Accounts Accounts(Season s)
    {
        var live = s.Players.Where(p => !p.Cancelled).ToList();
        var payers = s.Players.Where(Rules.PaysFee).ToList();
        return new Accounts
        {
            Fee = s.Settings.Fee,
            Registered = live.Count,
            Cancelled = s.Players.Count(p => p.Cancelled),
            Male = live.Count(p => p.Gender == "Male"),
            Female = live.Count(p => p.Gender == "Female"),
            Kids = live.Count(p => p.Gender == "Kids"),
            FeePlayers = payers.Count,
            PaidOnline = payers.Count(p => p.Payment == "Online"),
            PaidCash = payers.Count(p => p.Payment == "Cash"),
            Pending = payers.Count(p => p.Payment is "Pending" or ""),
            Waived = payers.Count(p => p.Payment == "Waived"),
            SponsorCash = s.Sponsors.Where(x => x.Mode != "Online").Sum(x => x.Amount),
            SponsorOnline = s.Sponsors.Where(x => x.Mode == "Online").Sum(x => x.Amount),
            ExpensesPaid = s.Expenses.Where(e => e.Paid).Sum(e => e.Amount),
            ExpensesUnpaid = s.Expenses.Where(e => !e.Paid).Sum(e => e.Amount),
            ExpensesCash = s.Expenses.Where(e => e.Paid && e.Mode != "Online").Sum(e => e.Amount),
            CashCounted = s.Cash.Sum(kv => (decimal)kv.Key * kv.Value),
        };
    }

    // --- divisions ------------------------------------------------------------------------------

    public static string TeamDivision(Season s, string team) =>
        s.Teams.FirstOrDefault(t => Rules.SameTeam(t.Name, team))?.Division is { Length: > 0 } d ? d : "Men";

    /// <summary>Men / Women / Kids. Old data without a division: Girls / Kids stage, else Team A's division.</summary>
    public static string MatchDivision(Season s, Match m) =>
        m.Division.Length > 0 ? m.Division : m.Stage == "Girls" ? "Women" : m.Stage == "Kids" ? "Kids" : TeamDivision(s, m.TeamA);

    /// <summary>League (round-robin) match: counts in the points table. "Girls" / "Kids" stages are old names for a league match.</summary>
    public static bool IsLeague(Match m) => m.Stage is "League" or "Girls" or "Kids";

    /// <summary>Divisions that have teams or matches this season, Men first.</summary>
    public static List<string> ActiveDivisions(Season s) =>
        Lists.Divisions.Where(d => s.Teams.Any(t => t.Division == d) || s.Matches.Any(m => MatchDivision(s, m) == d)).DefaultIfEmpty("Men").ToList();

    /// <summary>"8.3" overs -> 51 balls.</summary>
    public static int Balls(string overs)
    {
        if (string.IsNullOrWhiteSpace(overs)) return 0;
        var parts = overs.Trim().Split('.');
        int whole = int.TryParse(parts[0], out int o) ? o : 0;
        int balls = parts.Length > 1 && int.TryParse(parts[1], out int b) ? Math.Min(b, 5) : 0;
        return whole * 6 + balls;
    }

    /// <summary>
    /// League matches of a division with a result: 2 points a win, 1 each for no result.
    /// Net run rate from the scores (runs per over scored minus conceded) when they are entered.
    /// Order: points, net run rate, wins, fewer losses, name - the same as the Excel template.
    /// </summary>
    public static List<Standing> PointsTable(Season s, string division = "Men")
    {
        var played = s.Matches.Where(m => IsLeague(m) && m.Played && MatchDivision(s, m) == division).ToList();
        var names = s.Teams.Where(t => t.Division == division).Select(t => t.Name)
            .Concat(s.Matches.Where(m => IsLeague(m) && MatchDivision(s, m) == division).SelectMany(m => new[] { m.TeamA, m.TeamB }))
            .Where(n => n.Length > 0)
            .Aggregate(new List<string>(), (acc, n) => { if (!acc.Any(a => Rules.SameTeam(a, n))) acc.Add(n); return acc; });

        return names.Select(team =>
        {
            var mine = played.Where(m => Rules.SameTeam(m.TeamA, team) || Rules.SameTeam(m.TeamB, team)).ToList();
            int won = mine.Count(m => Rules.SameTeam(m.Winner, team));
            int nr = mine.Count(m => m.Winner == Lists.NoResult);
            double runsFor = 0, ballsFaced = 0, runsAgainst = 0, ballsBowled = 0;
            foreach (var m in mine)
            {
                bool isA = Rules.SameTeam(m.TeamA, team);
                var us = isA ? m.ScoreA : m.ScoreB;
                var them = isA ? m.ScoreB : m.ScoreA;
                runsFor += us.Runs ?? 0; ballsFaced += Balls(us.Overs);
                runsAgainst += them.Runs ?? 0; ballsBowled += Balls(them.Overs);
            }
            double nrr = ballsFaced == 0 || ballsBowled == 0 ? 0 : Math.Round(runsFor / (ballsFaced / 6) - runsAgainst / (ballsBowled / 6), 3, MidpointRounding.AwayFromZero);
            return new Standing(team, mine.Count, won, mine.Count - won - nr, nr, nrr);
        })
        .OrderByDescending(x => x.Points).ThenByDescending(x => x.NetRunRate).ThenByDescending(x => x.Won).ThenBy(x => x.Lost)
        .ThenBy(x => x.Team, StringComparer.OrdinalIgnoreCase)
        .ToList();
    }

    // --- batting / bowling ----------------------------------------------------------------------

    public static List<BattingStat> Batting(Season s, string division = "Men") =>
        s.Matches.Where(m => MatchDivision(s, m) == division).SelectMany(m => m.Performances).Where(p => p.Batted && p.Player.Length > 0)
            .GroupBy(p => Rules.TitleCase(p.Player), StringComparer.OrdinalIgnoreCase)
            .Select(g => new BattingStat(g.Key, g.Last().Team, g.Count(), g.Sum(p => p.Runs ?? 0), g.Sum(p => p.Balls ?? 0), g.Max(p => p.Runs ?? 0), g.Count(p => p.NotOut)))
            .OrderByDescending(b => b.Runs).ThenByDescending(b => b.StrikeRate).ToList();

    public static List<BowlingStat> Bowling(Season s, string division = "Men") =>
        s.Matches.Where(m => MatchDivision(s, m) == division).SelectMany(m => m.Performances).Where(p => p.Bowled && p.Player.Length > 0)
            .GroupBy(p => Rules.TitleCase(p.Player), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var best = g.OrderByDescending(p => p.Wickets ?? 0).ThenBy(p => p.RunsConceded ?? 999).First();
                return new BowlingStat(g.Key, g.Last().Team, g.Sum(p => Balls(p.OversBowled)), g.Sum(p => p.RunsConceded ?? 0), g.Sum(p => p.Wickets ?? 0),
                    $"{best.Wickets ?? 0}/{best.RunsConceded ?? 0}");
            })
            .OrderByDescending(b => b.Wickets).ThenBy(b => b.Economy).ToList();

    // --- teams: balancing and auction -----------------------------------------------------------

    private static int CategoryRank(string c) => Lists.Categories.ToList().IndexOf(c) is var i && i < 0 ? 99 : i;

    /// <summary>
    /// Balanced teams: players of the division sorted by category (A+ first) and dealt in snake order
    /// (1,2,3…n, n…3,2,1) so every team gets a similar mix. Captains stay in their own team.
    /// Returns player -> team without changing anything.
    /// </summary>
    public static Dictionary<Player, string> BalancedTeams(Season s, string division, bool keepAssigned, int seed = 0)
    {
        var teams = s.Teams.Where(t => t.Division == division).ToList();
        var result = new Dictionary<Player, string>();
        if (teams.Count == 0) return result;
        var pool = s.Players.Where(p => !p.Cancelled && p.Division == division).ToList();
        var counts = teams.ToDictionary(t => t.Name, _ => 0);
        foreach (var t in teams)
            if (pool.FirstOrDefault(p => p.Name.Equals(t.Captain, StringComparison.OrdinalIgnoreCase)) is { } cap) { result[cap] = t.Name; counts[t.Name]++; }
        if (keepAssigned)
            foreach (var p in pool.Where(p => !result.ContainsKey(p) && teams.Any(t => Rules.SameTeam(t.Name, p.Team))))
            { var t = teams.First(t => Rules.SameTeam(t.Name, p.Team)); result[p] = t.Name; counts[t.Name]++; }

        // Strongest first; each goes to the team with the fewest players, ties in snake order
        // (1..n, then n..1). With equal starting numbers this is exactly a snake draft.
        var rnd = new Random(seed);
        var rest = pool.Where(p => !result.ContainsKey(p)).OrderBy(p => CategoryRank(p.Category)).ThenBy(_ => rnd.Next()).ToList();
        int dealt = 0;
        foreach (var p in rest)
        {
            bool forward = dealt / teams.Count % 2 == 0;
            var order = (forward ? teams : Enumerable.Reverse(teams)).Select(t => t.Name).ToList();
            string pick = order.OrderBy(n => counts[n]).ThenBy(order.IndexOf).First();
            result[p] = pick; counts[pick]++; dealt++;
        }
        return result;
    }

    public static int BasePrice(Season s, string category) => s.Settings.BasePrices.GetValueOrDefault(category, 100);

    public static int Spent(Season s, string team) => s.Players.Where(p => Rules.SameTeam(p.Team, team) && p.SoldPrice is not null).Sum(p => p.SoldPrice!.Value);

    public static int Purse(Season s, string team) => s.Settings.AuctionPurse - Spent(s, team);

    /// <summary>Players still to be auctioned: active, division, no team, not a captain.</summary>
    public static List<Player> AuctionPool(Season s, string division) =>
        s.Players.Where(p => !p.Cancelled && p.Division == division && p.Team.Length == 0 && !s.Teams.Any(t => t.Captain.Equals(p.Name, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(p => CategoryRank(p.Category)).ThenBy(p => p.Name).ToList();

    // --- honours --------------------------------------------------------------------------------

    /// <summary>Champions and runners-up from the division's Final (a played final with a winner).</summary>
    public static (string Champions, string RunnersUp) Final(Season s, string division)
    {
        var f = s.Matches.LastOrDefault(m => m.Stage == "Final" && m.Played && m.Winner != Lists.NoResult && MatchDivision(s, m) == division);
        if (f is null) return ("", "");
        return (f.Winner, Rules.SameTeam(f.Winner, f.TeamA) ? f.TeamB : f.TeamA);
    }

    public static List<SeasonHonours> HallOfFame(IEnumerable<Season> seasons) =>
        seasons.OrderByDescending(s => s.Year).SelectMany(s => ActiveDivisions(s).Select(d =>
        {
            var (champ, runner) = Final(s, d);
            var bat = Batting(s, d).FirstOrDefault();
            var bowl = Bowling(s, d).FirstOrDefault();
            return new SeasonHonours(s.Year, d, champ, runner, s.Settings.ManOfSeries.GetValueOrDefault(d, ""),
                bat is null ? "" : $"{bat.Player} ({bat.Runs})", bowl is null ? "" : $"{bowl.Player} ({bowl.Wickets})");
        })).ToList();

    /// <summary>All-time leaders across seasons: Player of the Match awards, runs, wickets, titles.</summary>
    public static (List<(string Name, int Count)> Pom, List<(string Name, int Runs)> Runs, List<(string Name, int Wickets)> Wickets, List<(string Team, int Titles)> Titles) AllTime(IEnumerable<Season> seasons)
    {
        var list = seasons.ToList();
        var pom = list.SelectMany(s => s.Matches.SelectMany(m => m.Awards)).Where(a => a.Title == "Player of the Match")
            .GroupBy(a => Rules.TitleCase(a.Player), StringComparer.OrdinalIgnoreCase).Select(g => (g.Key, g.Count())).OrderByDescending(x => x.Item2).Take(10).ToList();
        var perf = list.SelectMany(s => s.Matches.SelectMany(m => m.Performances)).ToList();
        var runs = perf.Where(p => p.Batted).GroupBy(p => Rules.TitleCase(p.Player), StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, g.Sum(p => p.Runs ?? 0))).OrderByDescending(x => x.Item2).Take(10).ToList();
        var wkts = perf.Where(p => p.Bowled).GroupBy(p => Rules.TitleCase(p.Player), StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, g.Sum(p => p.Wickets ?? 0))).OrderByDescending(x => x.Item2).Take(10).ToList();
        var titles = list.SelectMany(s => ActiveDivisions(s).Select(d => Final(s, d).Champions)).Where(c => c.Length > 0)
            .GroupBy(c => c, StringComparer.OrdinalIgnoreCase).Select(g => (g.Key, g.Count())).OrderByDescending(x => x.Item2).ToList();
        return (pom, runs, wkts, titles);
    }

    /// <summary>Who won what across the season, most Player-of-the-Match awards first.</summary>
    public static List<AwardCount> AwardTally(Season s) =>
        s.Matches.SelectMany(m => m.Awards).Where(a => a.Player.Length > 0)
            .GroupBy(a => Rules.TitleCase(a.Player), StringComparer.OrdinalIgnoreCase)
            .Select(g => new AwardCount(g.Key,
                g.Count(a => a.Title == "Player of the Match"), g.Count(),
                string.Join(", ", g.GroupBy(a => a.Title).Select(t => t.Count() > 1 ? $"{t.Key} ×{t.Count()}" : t.Key))))
            .OrderByDescending(a => a.PlayerOfMatch).ThenByDescending(a => a.Total).ThenBy(a => a.Player)
            .ToList();

    /// <summary>Players of a team (not cancelled).</summary>
    public static List<Player> Squad(Season s, string team) =>
        s.Players.Where(p => !p.Cancelled && Rules.SameTeam(p.Team, team))
            .OrderBy(p => Lists.Categories.ToList().IndexOf(p.Category) is var i && i < 0 ? 99 : i).ThenBy(p => p.Name).ToList();

    /// <summary>Rename a team everywhere (players, matches, winners); merges into an existing team of that name.</summary>
    public static void RenameTeam(Season s, string from, string to)
    {
        foreach (var p in s.Players.Where(p => Rules.SameTeam(p.Team, from))) p.Team = to;
        foreach (var m in s.Matches)
        {
            if (Rules.SameTeam(m.TeamA, from)) m.TeamA = to;
            if (Rules.SameTeam(m.TeamB, from)) m.TeamB = to;
            if (Rules.SameTeam(m.Winner, from)) m.Winner = to;
        }
        var team = s.Teams.FirstOrDefault(t => Rules.SameTeam(t.Name, from));
        var existing = s.Teams.FirstOrDefault(t => t != team && Rules.SameTeam(t.Name, to));
        if (team is null) return;
        if (existing is null) team.Name = to;
        else
        {
            if (existing.Captain.Length == 0) existing.Captain = team.Captain;
            if (existing.Group.Length == 0) existing.Group = team.Group;
            s.Teams.Remove(team);
        }
    }

    /// <summary>
    /// League fixtures: every team plays every other team in its group once, in rounds - all teams play
    /// their next match before anyone plays again (one team rests each round when the count is odd).
    /// Within that, matches are ordered so no team plays two matches in a row or twice on the same night.
    /// Groups play side by side. <paramref name="slots"/> are the time slots of each night.
    /// </summary>
    public static List<Match> LeagueFixtures(Season s, DateTime start, IReadOnlyList<string> slots, int firstNo, string division = "Men")
    {
        var groups = s.Teams.Where(t => t.Name.Length > 0 && t.Division == division).GroupBy(t => t.Group).Select(g => g.Select(t => t.Name).ToList()).ToList();
        if (groups.Count == 0) return [];
        var groupRounds = groups.Select(RoundRobin).ToList();
        var rounds = new List<List<(string A, string B)>>();
        for (int r = 0; r < groupRounds.Max(x => x.Count); r++)
            rounds.Add(groupRounds.Where(g => r < g.Count).SelectMany(g => g[r]).ToList());

        var order = new List<(string A, string B)>();
        if (!Place(rounds, 0, order, slots.Count))
            order = rounds.SelectMany(r => r).ToList();   // not possible with these slots (e.g. 3 teams, 3 slots) - keep round order

        return order.Select((p, i) => new Match
        {
            No = firstNo + i,
            Date = start.AddDays(i / slots.Count),
            Time = slots[i % slots.Count],
            TeamA = p.A,
            TeamB = p.B,
            Stage = "League",
            Division = division,
        }).ToList();
    }

    /// <summary>Backtracking: order the matches of each round so no team plays back to back or twice in a night.</summary>
    private static bool Place(List<List<(string A, string B)>> rounds, int r, List<(string A, string B)> order, int perNight)
    {
        if (r == rounds.Count) return true;
        foreach (var perm in Permutations(rounds[r]))
        {
            int start = order.Count;
            bool ok = true;
            foreach (var m in perm)
            {
                if (Clashes(order, m, perNight)) { ok = false; break; }
                order.Add(m);
            }
            if (ok && Place(rounds, r + 1, order, perNight)) return true;
            order.RemoveRange(start, order.Count - start);
        }
        return false;
    }

    /// <summary>A team already plays earlier the same night (back to back, or twice in one night).</summary>
    private static bool Clashes(List<(string A, string B)> order, (string A, string B) m, int perNight)
    {
        int i = order.Count;
        bool Shares((string A, string B) x) => x.A == m.A || x.A == m.B || x.B == m.A || x.B == m.B;
        for (int j = i - i % perNight; j < i; j++) if (Shares(order[j])) return true;
        return false;
    }

    private static IEnumerable<List<T>> Permutations<T>(List<T> items)
    {
        if (items.Count <= 1) { yield return [.. items]; yield break; }
        for (int i = 0; i < items.Count; i++)
        {
            var rest = items.Where((_, k) => k != i).ToList();
            foreach (var p in Permutations(rest)) { p.Insert(0, items[i]); yield return p; }
        }
    }

    /// <summary>Problems in any fixture list (generated, imported or typed): a team playing more than once in a night.</summary>
    public static List<string> FixtureWarnings(Season s)
    {
        var list = new List<string>();
        var nights = s.Matches.Where(m => m.Date is not null && m.TeamA.Length > 0 && m.TeamB.Length > 0).GroupBy(m => m.Date!.Value.Date).OrderBy(g => g.Key);
        foreach (var night in nights)
        {
            var slots = night.OrderBy(m => Lists.Times.ToList().IndexOf(m.Time) is var i && i < 0 ? 99 : i).ToList();
            foreach (var team in slots.SelectMany(m => new[] { m.TeamA, m.TeamB }).GroupBy(t => t, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            {
                var games = slots.Where(m => Rules.SameTeam(m.TeamA, team.Key) || Rules.SameTeam(m.TeamB, team.Key)).ToList();
                bool backToBack = games.Zip(games.Skip(1)).Any(p => slots.IndexOf(p.Second) - slots.IndexOf(p.First) == 1);
                list.Add($"{team.Key} plays {(backToBack ? "back to back" : "twice")} on {night.Key:dd MMM} (matches {string.Join(" and ", games.Select(g => g.No))}).");
            }
        }
        return list;
    }

    private static List<List<(string, string)>> RoundRobin(List<string> teams)
    {
        var t = new List<string?>(teams);
        if (t.Count % 2 == 1) t.Add(null);   // bye
        int n = t.Count;
        var rounds = new List<List<(string, string)>>();
        for (int r = 0; r < n - 1; r++)
        {
            var round = new List<(string, string)>();
            for (int i = 0; i < n / 2; i++)
            {
                string? a = t[i], b = t[n - 1 - i];
                if (a is not null && b is not null) round.Add(r % 2 == 0 ? (a, b) : (b, a));
            }
            rounds.Add(round);
            t.Insert(1, t[^1]);   // rotate all but the first
            t.RemoveAt(t.Count - 1);
        }
        return rounds;
    }

    /// <summary>Qualifier 1, Eliminator, Qualifier 2 and Final after the league, filled from the current top 4.</summary>
    public static List<Match> Playoffs(Season s, DateTime start, string slot, int firstNo, string division = "Men")
    {
        var top = PointsTable(s, division).Where(x => x.Played > 0).Select(x => x.Team).Take(4).ToList();
        string T(int i, string fallback) => i < top.Count ? top[i] : fallback;
        return
        [
            new() { No = firstNo, Date = start, Time = slot, Stage = "Qualifier 1", Division = division, TeamA = T(0, "1st in league"), TeamB = T(1, "2nd in league") },
            new() { No = firstNo + 1, Date = start.AddDays(1), Time = slot, Stage = "Eliminator", Division = division, TeamA = T(2, "3rd in league"), TeamB = T(3, "4th in league") },
            new() { No = firstNo + 2, Date = start.AddDays(2), Time = slot, Stage = "Qualifier 2", Division = division, TeamA = "Loser of Qualifier 1", TeamB = "Winner of Eliminator" },
            new() { No = firstNo + 3, Date = start.AddDays(3), Time = slot, Stage = "Final", Division = division, TeamA = "Winner of Qualifier 1", TeamB = "Winner of Qualifier 2" },
        ];
    }
}
