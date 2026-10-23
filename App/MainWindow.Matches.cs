using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Rpl;

/// <summary>Auction page, Matches page (fixtures, scores, scorecard, availability) and Stats page.</summary>
public sealed partial class MainWindow
{
    private const string NotPlayed = "Not played yet", AllCategories = "All categories";
    private Match? _editingMatch;
    private Player? _onBlock;
    private readonly Stack<(Player Player, string Team, int? Price)> _sales = new();
    private readonly HashSet<Guid> _skipped = [];

    private void SetupAuction()
    {
        ADivision.ItemsSource = Lists.Divisions; ADivision.SelectedIndex = 0;
        ACategory.ItemsSource = Lists.Categories.Prepend(AllCategories).ToList(); ACategory.SelectedIndex = 0;
        AOrder.ItemsSource = new[] { "Category order", "Random" }; AOrder.SelectedIndex = 0;
    }

    private void SetupMatches()
    {
        MTime.ItemsSource = Lists.Times;
        MStage.ItemsSource = Lists.Stages.Where(s => s is not ("Girls" or "Kids")).ToList();
        MDivision.ItemsSource = Lists.Divisions;
        MDivision.SelectedItem = "Men";
        foreach (var box in new[] { MTitle1, MTitle2, MTitle3, MTitle4 }) box.ItemsSource = Lists.AwardTitles;
        StatsDivision.ItemsSource = Lists.Divisions; StatsDivision.SelectedIndex = 0;
    }

    // ================================================================================== auction

    private string AuctionDivision => ADivision.SelectedItem as string ?? "Men";

    private List<Player> AuctionCandidates()
    {
        string cat = ACategory.SelectedItem as string ?? AllCategories;
        return Calc.AuctionPool(S, AuctionDivision).Where(p => cat == AllCategories || p.Category == cat).ToList();
    }

    private void RefreshAuction()
    {
        var teams = S.Teams.Where(t => t.Division == AuctionDivision).Select(t => new AuctionTeam(t, S)).ToList();
        ATeamButtons.ItemsSource = teams;
        ATeamSummary.ItemsSource = teams;
        ASoldList.ItemsSource = S.Players.Where(p => p.SoldPrice is not null && p.Division == AuctionDivision).Reverse().Select(p => new SoldRow(p)).ToList();
        var pool = AuctionCandidates();
        APoolText.Text = $"{pool.Count} player(s) to auction in {AuctionDivision}{(ACategory.SelectedItem is string c && c != AllCategories ? $", category {c}" : "")}" +
                         $"{(_skipped.Count > 0 ? $" · {_skipped.Count} skipped this round" : "")}. Purse {S.Settings.AuctionPurse:N0} per team.";
        AUndo.IsEnabled = _sales.Count > 0;
    }

    private void AFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || _store is null) return;
        _onBlock = null; ShowOnBlock();
        RefreshAuction();
    }

    private void ShowOnBlock()
    {
        if (_onBlock is null) { ABlockName.Text = "Press Next player"; ABlockInfo.Text = ""; return; }
        var p = _onBlock;
        ABlockName.Text = p.Name;
        ABlockInfo.Text = $"Category {(p.Category.Length > 0 ? p.Category : "—")} · base price {Calc.BasePrice(S, p.Category):N0}" +
                          $"{(p.Age.Length > 0 ? $" · age {p.Age}" : "")}{(p.Flat.Length > 0 ? $" · flat {p.Block}/{p.Flat}" : "")}";
        ABid.Value = Calc.BasePrice(S, p.Category);
    }

    private void ANext_Click(object sender, RoutedEventArgs e)
    {
        var pool = AuctionCandidates().Where(p => !_skipped.Contains(p.Id)).ToList();
        if (pool.Count == 0 && _skipped.Count > 0) { _skipped.Clear(); pool = AuctionCandidates(); Show(InfoBarSeverity.Informational, "Second round", "Everyone left was skipped once - they come up again now."); }
        if (pool.Count == 0) { _onBlock = null; ShowOnBlock(); Show(InfoBarSeverity.Success, "Auction done", $"Every {AuctionDivision} player in this selection has a team."); return; }
        _onBlock = (AOrder.SelectedIndex == 1 ? pool[Random.Shared.Next(pool.Count)] : pool[0]);
        ShowOnBlock();
        RefreshAuction();
    }

    private void ASkip_Click(object sender, RoutedEventArgs e)
    {
        if (_onBlock is not null) _skipped.Add(_onBlock.Id);
        ANext_Click(sender, e);
    }

    private void ABidUp_Click(object sender, RoutedEventArgs e)
    {
        int step = int.Parse((string)((Button)sender).Tag);
        ABid.Value = (double.IsNaN(ABid.Value) ? 0 : ABid.Value) + step;
    }

    private async void ASell_Click(object sender, RoutedEventArgs e)
    {
        if (_onBlock is null) { Show(InfoBarSeverity.Informational, "No player on the block", "Press Next player first."); return; }
        string team = (string)((Button)sender).Tag;
        int bid = double.IsNaN(ABid.Value) ? Calc.BasePrice(S, _onBlock.Category) : (int)ABid.Value;
        int purse = Calc.Purse(S, team);
        if (bid > purse && !await Confirm("Over the purse?", $"{team} has {purse:N0} left; the bid is {bid:N0}. Sell anyway?", "Sell")) return;
        var p = _onBlock;
        _sales.Push((p, p.Team, p.SoldPrice));
        p.Team = team;
        p.SoldPrice = bid;
        if (!TrySave()) return;
        Show(InfoBarSeverity.Success, "Sold!", $"{p.Name} to {team} for {bid:N0}. {team} has {Calc.Purse(S, team):N0} left.");
        RefreshAll();
        ANext_Click(sender, e);
    }

    private void AUndo_Click(object sender, RoutedEventArgs e)
    {
        if (_sales.Count == 0) return;
        var (p, team, price) = _sales.Pop();
        string soldTo = p.Team;
        p.Team = team; p.SoldPrice = price;
        if (!TrySave()) return;
        _onBlock = p; ShowOnBlock();
        RefreshAll();
        Show(InfoBarSeverity.Informational, "Sale undone", $"{p.Name} is back on the block (was sold to {soldTo}).");
    }

    // ================================================================================== matches

    private void RefreshMatches()
    {
        MatchList.ItemsSource = S.Matches.OrderBy(m => m.Date ?? DateTime.MaxValue).ThenBy(m => Lists.Times.ToList().IndexOf(m.Time) is var i && i < 0 ? 99 : i).ThenBy(m => m.No)
            .Select(m => new MatchRow(m, S)).ToList();
        PointsTables.ItemsSource = Calc.ActiveDivisions(S).Select(d => new DivisionTable(d, Calc.PointsTable(S, d).Select((x, i) => new StandingRow(x, i + 1)).ToList())).ToList();
        AwardList.ItemsSource = Calc.AwardTally(S).Select(x => new AwardRow(x)).ToList();
        var warnings = Calc.FixtureWarnings(S);
        FixtureWarning.Message = string.Join("  ", warnings.Take(4)) + (warnings.Count > 4 ? $"  … and {warnings.Count - 4} more" : "");
        FixtureWarning.IsOpen = warnings.Count > 0;
        if (_editingMatch is null && MNo.Value is double.NaN or 0) MNo.Value = S.Matches.Count == 0 ? 1 : S.Matches.Max(m => m.No) + 1;
    }

    private void MatchTabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        FixturesPanel.Visibility = sender.SelectedItem == FixturesTab ? Visibility.Visible : Visibility.Collapsed;
        PointsPanel.Visibility = sender.SelectedItem == PointsTab ? Visibility.Visible : Visibility.Collapsed;
        AwardsPanel.Visibility = sender.SelectedItem == AwardsTab ? Visibility.Visible : Visibility.Collapsed;
    }

    private Match? SelectedMatch => (MatchList.SelectedItem as MatchRow)?.Match;

    private void MatchList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        MEdit.IsEnabled = MDelete.IsEnabled = MScorecard.IsEnabled = MAvailability.IsEnabled = SelectedMatch is not null;

    private void MatchList_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e) => MEdit_Click(sender, e);

    private void MDivision_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_store is null) return;
        string a = MTeamA.Text, b = MTeamB.Text;
        var names = TeamNames(MDivision.SelectedItem as string ?? "Men");
        MTeamA.ItemsSource = names; MTeamA.Text = a;
        MTeamB.ItemsSource = names; MTeamB.Text = b;
    }

    private void MTeams_Changed(object sender, SelectionChangedEventArgs e) => RefreshWinnerChoices();

    private void MTeams_TextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args) => RefreshWinnerChoices();

    private void RefreshWinnerChoices()
    {
        string a = (MTeamA.SelectedItem as string) ?? MTeamA.Text, b = (MTeamB.SelectedItem as string) ?? MTeamB.Text;
        string current = MWinner.SelectedItem as string ?? "";
        var items = new[] { NotPlayed, a, b, Lists.NoResult }.Where(x => x.Length > 0).Distinct().ToList();
        MWinner.ItemsSource = items;
        MWinner.SelectedItem = items.Contains(current) ? current : NotPlayed;
    }

    private void MAward_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        string q = sender.Text.Trim();
        sender.ItemsSource = q.Length == 0 ? null : S.Players.Where(p => !p.Cancelled).Select(p => p.Name)
            .Concat(S.Matches.SelectMany(m => m.Awards).Select(x => x.Player)).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(n => n.Contains(q, StringComparison.OrdinalIgnoreCase)).Order().Take(8).ToList();
    }

    private (AutoSuggestBox Player, ComboBox Title)[] AwardRows => [(MAward1, MTitle1), (MAward2, MTitle2), (MAward3, MTitle3), (MAward4, MTitle4)];

    private void MEdit_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMatch is not { } m) return;
        _editingMatch = m;
        MNo.Value = m.No;
        MDate.Date = m.Date is { } d ? new DateTimeOffset(d) : null;
        MTime.Text = m.Time;
        MDivision.SelectedItem = Calc.MatchDivision(S, m);
        MTeamA.Text = m.TeamA; MTeamB.Text = m.TeamB;
        MStage.SelectedItem = Calc.IsLeague(m) ? "League" : m.Stage;
        RefreshWinnerChoices();
        MWinner.SelectedItem = m.Winner.Length == 0 ? NotPlayed : m.Winner;
        MResult.Text = m.Result;
        MRunsA.Value = ToBox(m.ScoreA.Runs); MWktsA.Value = ToBox(m.ScoreA.Wickets); MOversA.Text = m.ScoreA.Overs;
        MRunsB.Value = ToBox(m.ScoreB.Runs); MWktsB.Value = ToBox(m.ScoreB.Wickets); MOversB.Text = m.ScoreB.Overs;
        var rows = AwardRows;
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i].Player.Text = i < m.Awards.Count ? m.Awards[i].Player : "";
            rows[i].Title.Text = i < m.Awards.Count ? m.Awards[i].Title : Lists.AwardTitles[Math.Min(i, 3)];
        }
        MatchFormTitle.Text = $"Edit match {m.No}";
        MSave.Content = "Save match"; MClear.Content = "Cancel";
        if (m.Awards.Count > rows.Length) Show(InfoBarSeverity.Warning, "More awards than fit", $"Match {m.No} has {m.Awards.Count} awards; only the first {rows.Length} are shown and saving keeps just those.");
    }

    private void MClear_Click(object sender, RoutedEventArgs e) => ClearMatchForm();

    private void ClearMatchForm()
    {
        _editingMatch = null;
        MNo.Value = S.Matches.Count == 0 ? 1 : S.Matches.Max(m => m.No) + 1;
        MDate.Date = null; MTime.Text = "";
        MTeamA.Text = MTeamB.Text = MResult.Text = MOversA.Text = MOversB.Text = "";
        MTeamA.SelectedItem = MTeamB.SelectedItem = null;
        MRunsA.Value = MWktsA.Value = MRunsB.Value = MWktsB.Value = double.NaN;
        MStage.SelectedItem = "League";
        RefreshWinnerChoices();
        var rows = AwardRows;
        for (int i = 0; i < rows.Length; i++) { rows[i].Player.Text = ""; rows[i].Title.Text = Lists.AwardTitles[Math.Min(i, 3)]; }
        MatchFormTitle.Text = "Add match";
        MSave.Content = "Add match"; MClear.Content = "Clear";
    }

    private void MSave_Click(object sender, RoutedEventArgs e)
    {
        string a = Rules.Clean((MTeamA.SelectedItem as string) ?? MTeamA.Text), b = Rules.Clean((MTeamB.SelectedItem as string) ?? MTeamB.Text);
        if (a.Length == 0 || b.Length == 0) { Show(InfoBarSeverity.Error, "Please fix", "Enter both teams."); return; }
        if (Rules.SameTeam(a, b)) { Show(InfoBarSeverity.Error, "Please fix", "A team can't play itself."); return; }
        foreach (var o in new[] { MOversA.Text, MOversB.Text }.Where(o => o.Trim().Length > 0))
            if (!System.Text.RegularExpressions.Regex.IsMatch(o.Trim(), @"^\d+(\.[0-5])?$")) { Show(InfoBarSeverity.Error, "Please fix", $"Overs \"{o}\" should look like 10 or 8.3 (8 overs and 3 balls)."); return; }
        var m = _editingMatch ?? new Match();
        m.No = MNo.Value is double n && !double.IsNaN(n) ? (int)n : S.Matches.Count + 1;
        m.Date = MDate.Date?.Date;
        m.Time = Rules.Clean(MTime.Text);
        m.TeamA = a; m.TeamB = b;
        m.Division = MDivision.SelectedItem as string ?? "Men";
        m.Stage = MStage.SelectedItem as string ?? "League";
        string w = MWinner.SelectedItem as string ?? NotPlayed;
        m.Winner = w == NotPlayed ? "" : w;
        m.Result = Rules.Clean(MResult.Text);
        m.ScoreA = new TeamScore { Runs = Num(MRunsA), Wickets = Num(MWktsA), Overs = MOversA.Text.Trim() };
        m.ScoreB = new TeamScore { Runs = Num(MRunsB), Wickets = Num(MWktsB), Overs = MOversB.Text.Trim() };
        m.Awards = AwardRows.Where(r => r.Player.Text.Trim().Length > 0)
            .Select(r => new Award { Player = Rules.TitleCase(r.Player.Text), Title = Rules.Clean(r.Title.Text).Length > 0 ? Rules.Clean(r.Title.Text) : "Player of the Match" }).ToList();
        foreach (var t in new[] { a, b }.Where(t => !S.Teams.Any(x => Rules.SameTeam(x.Name, t)) && !t.Contains("Winner") && !t.Contains("Loser") && !t.Contains("league")))
            S.Teams.Add(new Team { Name = t, Division = m.Division });
        bool adding = _editingMatch is null;
        if (adding) S.Matches.Add(m);
        if (!TrySave()) { if (adding) S.Matches.Remove(m); return; }
        RefreshAll();
        Show(InfoBarSeverity.Success, adding ? "Match added" : "Match saved", $"Match {m.No}: {m.Title}{(m.Played ? $" – {(m.Winner == Lists.NoResult ? "no result" : m.Winner + " won")}" : "")}.");
        ClearMatchForm();
    }

    private async void MDelete_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMatch is not { } m) return;
        if (!await Confirm("Delete match?", $"Match {m.No}: {m.Title}, its scorecard and awards will be removed.", "Delete")) return;
        S.Matches.Remove(m);
        if (!TrySave()) return;
        if (_editingMatch == m) ClearMatchForm();
        RefreshAll();
    }

    /// <summary>Runs, balls, overs and wickets per player, for both teams' squads.</summary>
    private async void Scorecard_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMatch is not { } m) return;
        var entries = new List<(string Team, string Name, NumberBox Runs, NumberBox Balls, CheckBox NotOut, TextBox Overs, NumberBox Given, NumberBox Wkts)>();
        var panel = new StackPanel { Spacing = 4 };
        static NumberBox Box(int? v) => new() { Width = 64, Minimum = 0, Value = v is { } n ? n : double.NaN, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden };
        Grid Row(params UIElement[] cells)
        {
            var g = new Grid { ColumnSpacing = 6 };
            foreach (var w in new[] { 190, 70, 70, 70, 70, 80, 64 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
            for (int i = 0; i < cells.Length; i++) { Grid.SetColumn((FrameworkElement)cells[i], i); g.Children.Add(cells[i]); }
            return g;
        }
        foreach (var team in new[] { m.TeamA, m.TeamB })
        {
            panel.Children.Add(new TextBlock { Text = team, FontWeight = Microsoft.UI.Text.FontWeights.Bold, FontSize = 16, Margin = new Thickness(0, 10, 0, 2) });
            panel.Children.Add(Row(new TextBlock { Text = "Player" }, new TextBlock { Text = "Runs" }, new TextBlock { Text = "Balls" }, new TextBlock { Text = "Not out" },
                new TextBlock { Text = "Overs" }, new TextBlock { Text = "Runs given" }, new TextBlock { Text = "Wkts" }));
            var names = Calc.Squad(S, team).Select(p => p.Name)
                .Concat(m.Performances.Where(x => Rules.SameTeam(x.Team, team)).Select(x => x.Player)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (names.Count == 0) panel.Children.Add(new TextBlock { Text = "No players in this team yet - add them on the Players page.", Opacity = 0.6 });
            foreach (var name in names)
            {
                var old = m.Performances.FirstOrDefault(x => x.Player.Equals(name, StringComparison.OrdinalIgnoreCase));
                var runs = Box(old?.Runs); var balls = Box(old?.Balls); var given = Box(old?.RunsConceded); var wkts = Box(old?.Wickets);
                var notOut = new CheckBox { IsChecked = old?.NotOut == true, MinWidth = 0 };
                var overs = new TextBox { Width = 64, Text = old?.OversBowled ?? "", PlaceholderText = "2.0" };
                bool absent = m.Absent.Contains(name);
                panel.Children.Add(Row(new TextBlock { Text = name + (absent ? "  (absent)" : ""), VerticalAlignment = VerticalAlignment.Center, Opacity = absent ? 0.5 : 1 },
                    runs, balls, notOut, overs, given, wkts));
                entries.Add((team, name, runs, balls, notOut, overs, given, wkts));
            }
        }
        var scroll = new ScrollViewer { Content = panel, MaxHeight = 560, Padding = new Thickness(0, 0, 14, 0) };
        if (!await Ask($"Scorecard – match {m.No}: {m.Title}", scroll, "Save scorecard", 820)) return;
        m.Performances = entries.Select(x => new Performance
        {
            Player = x.Name, Team = x.Team, Runs = Num(x.Runs), Balls = Num(x.Balls), NotOut = x.NotOut.IsChecked == true,
            OversBowled = x.Overs.Text.Trim(), RunsConceded = Num(x.Given), Wickets = Num(x.Wkts),
        }).Where(p => p.Batted || p.Bowled).ToList();
        if (!TrySave()) return;
        RefreshAll();
        var top = m.Performances.OrderByDescending(p => p.Runs ?? 0).FirstOrDefault();
        Show(InfoBarSeverity.Success, "Scorecard saved", $"{m.Performances.Count} player entries for match {m.No}." +
            (top?.Runs is { } r ? $" Top score: {top.Player} {r}." : "") + " Stats and caps are updated.");
    }

    /// <summary>Who can't play: unticked players are absent (the rules allow no replacement).</summary>
    private async void Availability_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMatch is not { } m) return;
        var boxes = new List<CheckBox>();
        var panel = new StackPanel { Spacing = 2 };
        foreach (var team in new[] { m.TeamA, m.TeamB })
        {
            panel.Children.Add(new TextBlock { Text = team, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Margin = new Thickness(0, 8, 0, 2) });
            var squad = Calc.Squad(S, team);
            if (squad.Count == 0) panel.Children.Add(new TextBlock { Text = "No players in this team yet.", Opacity = 0.6 });
            foreach (var p in squad)
            {
                var box = new CheckBox { Content = p.Name, IsChecked = !m.Absent.Contains(p.Name) };
                boxes.Add(box); panel.Children.Add(box);
            }
        }
        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(new TextBlock { Text = "Untick players who can't play. They're shown on the dashboard, the scorer's sheet and the scorecard.", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new ScrollViewer { Content = panel, MaxHeight = 460 });
        if (!await Ask($"Availability – match {m.No}", content, "Save")) return;
        m.Absent = boxes.Where(b => b.IsChecked != true).Select(b => (string)b.Content).ToList();
        if (!TrySave()) return;
        RefreshAll();
        Show(InfoBarSeverity.Success, "Availability saved", m.Absent.Count == 0 ? $"Everyone is available for match {m.No}." : $"Absent for match {m.No}: {string.Join(", ", m.Absent)}.");
    }

    private async void Fixtures_Click(object sender, RoutedEventArgs e)
    {
        var divisions = Lists.Divisions.Where(d => S.Teams.Count(t => t.Division == d) >= 2).ToList();
        if (divisions.Count == 0) { Show(InfoBarSeverity.Warning, "Add teams first", "Create at least two teams in a division on the Teams page."); return; }
        var division = new ComboBox { Header = "Division", ItemsSource = divisions, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var start = new CalendarDatePicker { Header = "First match night", Date = new DateTimeOffset(S.Settings.StartDate ?? DateTime.Today), HorizontalAlignment = HorizontalAlignment.Stretch };
        var slotBoxes = Lists.Times.Select(t => new CheckBox { Content = t, IsChecked = t is "9–10 PM" or "10–11 PM", MinWidth = 0 }).ToList();
        var replace = new CheckBox { IsChecked = true };
        var info = new TextBlock { TextWrapping = TextWrapping.Wrap };
        void Update()
        {
            string d = division.SelectedItem as string ?? "Men";
            var teams = S.Teams.Where(t => t.Division == d).ToList();
            int groups = teams.Select(t => t.Group).Distinct().Count();
            int existing = S.Matches.Count(m => Calc.IsLeague(m) && Calc.MatchDivision(S, m) == d);
            info.Text = $"{teams.Count} {d} teams{(groups > 1 ? $" in {groups} groups" : "")}: every team plays every other team{(groups > 1 ? " in its group" : "")} once. " +
                        "Matches go in rounds - every team plays before anyone plays again - and no team plays twice in a night.";
            replace.Content = $"Replace the {existing} {d} league matches already in the list";
            replace.Visibility = existing > 0 ? Visibility.Visible : Visibility.Collapsed;
            replace.IsChecked = existing > 0;
        }
        division.SelectionChanged += (_, _) => Update();
        Update();
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(division); panel.Children.Add(info); panel.Children.Add(start);
        panel.Children.Add(new TextBlock { Text = "Time slots each night" });
        var slotRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var c in slotBoxes) slotRow.Children.Add(c);
        panel.Children.Add(slotRow);
        panel.Children.Add(replace);
        if (!await Ask("Create league fixtures", panel, "Create", 640)) return;

        string div = division.SelectedItem as string ?? "Men";
        var slots = slotBoxes.Where(c => c.IsChecked == true).Select(c => (string)c.Content).ToList();
        if (slots.Count == 0) { Show(InfoBarSeverity.Error, "Please fix", "Pick at least one time slot."); return; }
        if (replace.IsChecked == true && replace.Visibility == Visibility.Visible)
        {
            _store.Snapshot("before-fixtures");
            S.Matches.RemoveAll(m => Calc.IsLeague(m) && Calc.MatchDivision(S, m) == div);
        }
        var made = Calc.LeagueFixtures(S, start.Date?.Date ?? DateTime.Today, slots, (S.Matches.Count == 0 ? 0 : S.Matches.Max(m => m.No)) + 1, div);
        S.Matches.AddRange(made);
        Renumber();
        if (!TrySave()) return;
        RefreshAll();
        var warnings = Calc.FixtureWarnings(S);
        Show(warnings.Count == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning, $"{div} fixtures created",
            $"{made.Count} league matches from {made.First().Date:dd MMM} to {made.Last().Date:dd MMM} ({made.Select(m => m.Date).Distinct().Count()} nights). " +
            (warnings.Count == 0 ? "No team plays twice in a night." : string.Join(" ", warnings)));
    }

    private async void Playoffs_Click(object sender, RoutedEventArgs e)
    {
        var division = new ComboBox { Header = "Division", ItemsSource = Calc.ActiveDivisions(S), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var start = new CalendarDatePicker { Header = "Qualifier 1 date", HorizontalAlignment = HorizontalAlignment.Stretch };
        var slot = new ComboBox { Header = "Time", ItemsSource = Lists.Times, SelectedItem = "10–11 PM", HorizontalAlignment = HorizontalAlignment.Stretch };
        var info = new TextBlock { TextWrapping = TextWrapping.Wrap };
        void Update()
        {
            string d = division.SelectedItem as string ?? "Men";
            var last = S.Matches.Where(m => Calc.IsLeague(m) && m.Date is not null && Calc.MatchDivision(S, m) == d).Select(m => m.Date!.Value).DefaultIfEmpty(DateTime.Today).Max();
            start.Date = new DateTimeOffset(last.AddDays(1));
            var top = Calc.PointsTable(S, d).Where(x => x.Played > 0).Take(4).Select(x => x.Team).ToList();
            info.Text = "Adds Qualifier 1 (1st vs 2nd), Eliminator (3rd vs 4th), Qualifier 2 (loser of Q1 vs winner of the Eliminator) and the Final, one per night. " +
                        (top.Count == 4 ? $"Current top 4: {string.Join(", ", top)}." : "The league isn't finished, so the teams are placeholders - edit them later.");
        }
        division.SelectionChanged += (_, _) => Update();
        Update();
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(division); panel.Children.Add(info); panel.Children.Add(start); panel.Children.Add(slot);
        if (!await Ask("Add playoffs", panel, "Add")) return;
        var made = Calc.Playoffs(S, start.Date?.Date ?? DateTime.Today, slot.SelectedItem as string ?? "10–11 PM",
            S.Matches.Count == 0 ? 1 : S.Matches.Max(m => m.No) + 1, division.SelectedItem as string ?? "Men");
        S.Matches.AddRange(made);
        if (!TrySave()) return;
        RefreshAll();
        Show(InfoBarSeverity.Success, "Playoffs added", string.Join("; ", made.Select(m => $"{m.Stage} {m.Date:dd MMM}: {m.Title}")));
    }

    /// <summary>Match numbers in date and time order.</summary>
    private void Renumber()
    {
        int no = 1;
        foreach (var m in S.Matches.OrderBy(m => m.Date ?? DateTime.MaxValue).ThenBy(m => Lists.Times.ToList().IndexOf(m.Time) is var i && i < 0 ? 99 : i).ThenBy(m => m.No))
            m.No = no++;
    }

    // ================================================================================== stats

    private void RefreshStats()
    {
        string d = StatsDivision.SelectedItem as string ?? "Men";
        var bat = Calc.Batting(S, d);
        var bowl = Calc.Bowling(S, d);
        BattingList.ItemsSource = bat.Select((b, i) => new BattingRow(b, i + 1)).ToList();
        BowlingList.ItemsSource = bowl.Select((b, i) => new BowlingRow(b, i + 1)).ToList();
        StatsHint.Text = bat.Count + bowl.Count == 0
            ? "No scorecards yet: on the Matches page select a match and click Scorecard… to enter each player's runs and wickets."
            : $"From {S.Matches.Count(m => m.Performances.Count > 0 && Calc.MatchDivision(S, m) == d)} scorecards.";
    }

    private void StatsDivision_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing && _store is not null) RefreshStats();
    }
}
