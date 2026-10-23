using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;

namespace Rpl;

/// <summary>Players page (registration form and list) and Teams page (teams, divisions, balanced teams).</summary>
public sealed partial class MainWindow
{
    private const string AllTeams = "All teams", NoTeam = "No team", AllDivisions = "All divisions";
    private static readonly string[] StatusOptions = ["Active players", "Fee pending", "Cancelled forms", "All forms"];
    private static readonly string[] SortOptions = ["Entry order", "Name", "Team", "Category", "Form No"];

    private Player? _editingPlayer;
    private Team? _editingTeam;
    private (Player Player, int Index)? _deletedPlayer;
    private string? _newPhoto;    // picked but not saved yet
    private static string PhotoFolder => Path.Combine(Store.Folder, "photos");

    private void SetupPlayers()
    {
        PBlock.ItemsSource = Lists.Blocks;
        PGender.ItemsSource = Lists.Genders;
        PCategory.ItemsSource = Lists.Categories;
        PPayment.ItemsSource = Lists.Payments;
        PDivisionFilter.ItemsSource = Lists.Divisions.Prepend(AllDivisions).ToList(); PDivisionFilter.SelectedIndex = 0;
        PStatusFilter.ItemsSource = StatusOptions; PStatusFilter.SelectedIndex = 0;
        PSort.ItemsSource = SortOptions; PSort.SelectedIndex = 0;
    }

    private void SetupTeams() => TDivision.ItemsSource = Lists.Divisions;

    // ================================================================================== players

    private void RefreshPlayers()
    {
        string q = PSearch.Text.Trim();
        string team = PTeamFilter.SelectedItem as string ?? AllTeams;
        string division = PDivisionFilter.SelectedItem as string ?? AllDivisions;
        int status = Math.Max(0, PStatusFilter.SelectedIndex);
        var numbered = S.Players.Select((p, i) => (p, no: S.Players.Take(i + 1).Count(x => !x.Cancelled))).ToList();
        var rows = numbered.Where(x =>
            (q.Length == 0 || new[] { x.p.Name, x.p.Flat, x.p.Mobile, x.p.FormNo, x.p.Notes }.Any(v => v.Contains(q, StringComparison.OrdinalIgnoreCase))) &&
            (division == AllDivisions || x.p.Division == division) &&
            (team == AllTeams || (team == NoTeam ? x.p.Team.Length == 0 : Rules.SameTeam(x.p.Team, team))) &&
            status switch
            {
                0 => !x.p.Cancelled,
                1 => Rules.PaysFee(x.p) && x.p.Payment is "Pending" or "",
                2 => x.p.Cancelled,
                _ => true,
            });
        rows = PSort.SelectedIndex switch
        {
            1 => rows.OrderBy(x => x.p.Name),
            2 => rows.OrderBy(x => x.p.Team.Length == 0 ? "~" : x.p.Team).ThenBy(x => x.p.Name),
            3 => rows.OrderBy(x => Lists.Categories.ToList().IndexOf(x.p.Category) is var i && i < 0 ? 99 : i).ThenBy(x => x.p.Name),
            4 => rows.OrderBy(x => int.TryParse(x.p.FormNo, out int f) ? f : int.MaxValue).ThenBy(x => x.no),
            _ => rows,
        };
        var list = rows.Select(x => new PlayerRow(x.p, x.no, S)).ToList();
        PlayerList.ItemsSource = list;
        PlayerCount.Text = $"{list.Count} shown · {S.Players.Count(p => !p.Cancelled)} active";
    }

    private void PFilter_Changed(object sender, object e)
    {
        if (!_refreshing) RefreshPlayers();
    }

    private void PlayerList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        PEdit.IsEnabled = PDelete.IsEnabled = PlayerList.SelectedItem is PlayerRow;

    private void PlayerList_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e) => PEdit_Click(sender, e);

    private void PGender_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateFeeNote();
        if (_store is null) return;
        // Only teams of the player's division (men's, women's or kids' teams).
        string text = PTeam.Text;
        PTeam.ItemsSource = TeamNames(DivisionOfGender(PGender.SelectedItem as string));
        PTeam.Text = text;
    }

    private void UpdateFeeNote()
    {
        if (_store is null) return;
        bool pays = (PGender.SelectedItem as string ?? "Male") == "Male";
        PPayment.IsEnabled = PPaidTo.IsEnabled = pays;
        PFeeNote.Text = pays ? $"Registration fee {Rules.Money(S.Settings.Fee)} (set in Season settings)." : "Women and kids play free - no fee. Their teams are in the Women / Kids division.";
    }

    private void ShowPhoto(string? path)
    {
        if (path is not null && File.Exists(path))
        {
            PPhotoImage.Source = new BitmapImage(new Uri(path));
            PPhotoHint.Visibility = Visibility.Collapsed;
        }
        else
        {
            PPhotoImage.Source = null;
            PPhotoHint.Visibility = Visibility.Visible;
        }
    }

    private async void PPhoto_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        foreach (var ext in new[] { ".jpg", ".jpeg", ".png" }) picker.FileTypeFilter.Add(ext);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        _newPhoto = file.Path;
        ShowPhoto(file.Path);
    }

    private void PEdit_Click(object sender, RoutedEventArgs e)
    {
        if (PlayerList.SelectedItem is not PlayerRow row) return;
        var p = row.Player;
        _editingPlayer = p;
        _newPhoto = null;
        PFormNo.Text = p.FormNo; PName.Text = p.Name; PAge.Text = p.Age; PBlock.SelectedItem = Lists.Blocks.FirstOrDefault(b => b == p.Block);
        PFlat.Text = p.Flat; PMobile.Text = p.Mobile; PGender.SelectedItem = p.Gender; PCategory.SelectedItem = Lists.Categories.FirstOrDefault(c => c == p.Category);
        PTeam.Text = p.Team; PPayment.SelectedItem = Lists.Payments.FirstOrDefault(x => x == p.Payment) ?? "Pending"; PPaidTo.Text = p.PaidTo;
        PNotes.Text = p.Notes; PCancelled.IsChecked = p.Cancelled;
        ShowPhoto(p.Photo.Length > 0 ? Path.Combine(PhotoFolder, p.Photo) : null);
        PlayerFormTitle.Text = $"Edit: {p.Name}";
        PSave.Content = "Save changes"; PClear.Content = "Cancel";
        UpdateFeeNote();
        PName.Focus(FocusState.Programmatic);
    }

    private void PClear_Click(object sender, RoutedEventArgs e) => ClearPlayerForm();

    private void ClearPlayerForm(bool keepTeam = false)
    {
        _editingPlayer = null;
        _newPhoto = null;
        string team = PTeam.Text;
        PFormNo.Text = PName.Text = PAge.Text = PFlat.Text = PMobile.Text = PPaidTo.Text = PNotes.Text = "";
        PBlock.SelectedItem = null; PCategory.SelectedItem = null; PGender.SelectedItem = "Male"; PPayment.SelectedItem = "Pending";
        PCancelled.IsChecked = false;
        PTeam.Text = keepTeam ? team : "";
        ShowPhoto(null);
        PlayerFormTitle.Text = "Add player";
        PSave.Content = "Add player"; PClear.Content = "Clear";
        UpdateFeeNote();
    }

    private async void PSave_Click(object sender, RoutedEventArgs e)
    {
        var p = _editingPlayer?.Copy() ?? new Player();
        p.FormNo = Rules.Clean(PFormNo.Text);
        p.Name = Rules.TitleCase(PName.Text);
        p.Age = Rules.Clean(PAge.Text);
        p.Block = PBlock.SelectedItem as string ?? "";
        p.Flat = Rules.Clean(PFlat.Text);
        p.Mobile = new string(PMobile.Text.Where(char.IsDigit).ToArray());
        p.Gender = PGender.SelectedItem as string ?? "Male";
        p.Category = PCategory.SelectedItem as string ?? "";
        p.Team = Rules.Clean(PTeam.Text);
        p.Payment = p.Gender == "Male" ? PPayment.SelectedItem as string ?? "Pending" : "";
        p.PaidTo = p.Gender == "Male" ? Rules.Clean(PPaidTo.Text) : "";
        p.Notes = Rules.Clean(PNotes.Text);
        p.Cancelled = PCancelled.IsChecked == true;

        var errors = new List<string>();
        if (p.Name.Length == 0) errors.Add("Enter the player's name.");
        if (p.Mobile.Length > 0 && !Rules.ValidMobile(p.Mobile)) errors.Add("The mobile number should be 10 digits starting with 6-9.");
        if (p.Age.Length > 0 && (!int.TryParse(p.Age, out int age) || age is < 4 or > 99)) errors.Add("Age should be a number.");
        if (errors.Count > 0) { Show(InfoBarSeverity.Error, "Please fix", string.Join(" ", errors)); return; }

        var warnings = new List<string>();
        var others = S.Players.Where(x => x.Id != p.Id && !x.Cancelled).ToList();
        if (p.Mobile.Length > 0 && others.FirstOrDefault(x => x.Mobile == p.Mobile) is { } sameMobile) warnings.Add($"{sameMobile.Name} has the same mobile number.");
        if (others.FirstOrDefault(x => x.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase)) is { } sameName) warnings.Add($"{sameName.Name} is already registered{(sameName.Flat.Length > 0 ? $" (flat {sameName.Block}/{sameName.Flat})" : "")}.");
        if (p.Mobile.Length == 0) warnings.Add("No mobile number.");
        var known = S.Teams.FirstOrDefault(t => Rules.SameTeam(t.Name, p.Team));
        if (p.Team.Length > 0 && known is null) warnings.Add($"\"{p.Team}\" is a new team - it will be added to the {p.Division} division.");
        if (known is not null && known.Division != p.Division) warnings.Add($"{known.Name} is a {known.Division} team, but this player is in {p.Division}.");
        if (warnings.Count > 0 && !await Confirm("Save anyway?", string.Join(Environment.NewLine, warnings.Select(w => "• " + w)), "Save")) return;

        if (p.Team.Length > 0)
        {
            if (known is null) S.Teams.Add(new Team { Name = p.Team, Division = p.Division }); else p.Team = known.Name;
        }
        if (_newPhoto is not null)
        {
            try
            {
                Directory.CreateDirectory(PhotoFolder);
                string file = p.Id.ToString("N") + Path.GetExtension(_newPhoto).ToLowerInvariant();
                File.Copy(_newPhoto, Path.Combine(PhotoFolder, file), overwrite: true);
                p.Photo = file;
            }
            catch (Exception ex) { Show(InfoBarSeverity.Warning, "Photo not saved", ex.Message); }
        }
        bool editing = _editingPlayer is not null;
        int i = S.Players.FindIndex(x => x.Id == p.Id);
        if (i >= 0) S.Players[i] = p; else S.Players.Add(p);
        if (!TrySave()) return;
        RefreshAll();
        Show(InfoBarSeverity.Success, editing ? "Player updated" : "Player added",
            $"{p.Name}{(p.Team.Length > 0 ? $" – {p.Team}" : "")}{(Rules.PaysFee(p) ? $" – fee {p.Payment.ToLowerInvariant()}" : "")}. {S.Players.Count(x => !x.Cancelled)} players registered.");
        ClearPlayerForm(keepTeam: !editing);
        PName.Focus(FocusState.Programmatic);
    }

    private async void PDelete_Click(object sender, RoutedEventArgs e)
    {
        if (PlayerList.SelectedItem is not PlayerRow row) return;
        if (!await Confirm("Delete player?", $"{row.Player.Name} will be removed from season {S.Year}. (To keep the form on record, edit and tick 'Form cancelled' instead.)", "Delete")) return;
        int index = S.Players.IndexOf(row.Player);
        S.Players.RemoveAt(index);
        if (!TrySave()) { S.Players.Insert(index, row.Player); return; }
        _deletedPlayer = (row.Player, index);
        RefreshAll();
        Show(InfoBarSeverity.Success, "Deleted", $"{row.Player.Name} was removed.", ("Undo", () =>
        {
            if (_deletedPlayer is not { } d) return;
            S.Players.Insert(Math.Min(d.Index, S.Players.Count), d.Player);
            if (TrySave()) { _deletedPlayer = null; RefreshAll(); Show(InfoBarSeverity.Success, "Restored", $"{d.Player.Name} is back."); }
        }));
    }

    // ================================================================================== teams

    private void RefreshTeams()
    {
        var selected = (TeamList.SelectedItem as TeamRow)?.Team ?? _editingTeam;
        var rows = S.Teams.OrderBy(t => Lists.Divisions.ToList().IndexOf(t.Division)).Select(t => new TeamRow(t, S)).ToList();
        TeamList.ItemsSource = rows;
        if (selected is not null && rows.FirstOrDefault(r => r.Team == selected) is { } again) TeamList.SelectedItem = again;
        ShowSquad(_editingTeam);
    }

    private void TeamList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TeamList.SelectedItem is not TeamRow row) return;
        _editingTeam = row.Team;
        TName.Text = row.Team.Name;
        TGroup.Text = row.Team.Group;
        TDivision.SelectedItem = row.Team.Division;
        TCaptain.ItemsSource = Calc.Squad(S, row.Team.Name).Select(p => p.Name).ToList();
        TCaptain.Text = row.Team.Captain;
        TeamFormTitle.Text = $"Edit team: {row.Team.Name}";
        TSave.Content = "Save team";
        TDelete.IsEnabled = true;
        ShowSquad(row.Team);
    }

    private void ShowSquad(Team? team)
    {
        if (team is null) { SquadTitle.Text = $"Players without a team: {S.Players.Count(p => !p.Cancelled && p.Team.Length == 0)}"; SquadList.ItemsSource = null; return; }
        var squad = Calc.Squad(S, team.Name);
        SquadTitle.Text = $"Squad – {squad.Count} players (assign players on the Players page, by auction, or with Balanced teams)";
        SquadList.ItemsSource = squad.Select((p, i) => new PlayerRow(p, i + 1, S)).ToList();
    }

    private void TNew_Click(object sender, RoutedEventArgs e) => ClearTeamForm();

    private void ClearTeamForm()
    {
        _editingTeam = null;
        TeamList.SelectedItem = null;
        TName.Text = TGroup.Text = TCaptain.Text = "";
        TDivision.SelectedItem = "Men";
        TCaptain.ItemsSource = S.Players.Where(p => !p.Cancelled).Select(p => p.Name).Order().ToList();
        TeamFormTitle.Text = "Add team";
        TSave.Content = "Add team";
        TDelete.IsEnabled = false;
        ShowSquad(null);
    }

    private async void TSave_Click(object sender, RoutedEventArgs e)
    {
        string name = Rules.Clean(TName.Text), captain = Rules.TitleCase(TCaptain.Text), group = Rules.Clean(TGroup.Text);
        string division = TDivision.SelectedItem as string ?? "Men";
        if (name.Length == 0) { Show(InfoBarSeverity.Error, "Please fix", "Enter the team name."); return; }
        var existing = S.Teams.FirstOrDefault(t => t != _editingTeam && Rules.SameTeam(t.Name, name));

        if (_editingTeam is null)
        {
            if (existing is not null) { Show(InfoBarSeverity.Error, "Team exists", $"{existing.Name} is already a team this season."); return; }
            S.Teams.Add(new Team { Name = name, Captain = captain, Group = group, Division = division });
        }
        else
        {
            string old = _editingTeam.Name;
            if (old != name)
            {
                if (existing is not null && !await Confirm("Merge teams?",
                        $"\"{existing.Name}\" already exists. Renaming \"{old}\" to it moves {Calc.Squad(S, old).Count} players and its matches into \"{existing.Name}\".", "Merge")) return;
                Calc.RenameTeam(S, old, existing?.Name ?? name);
            }
            var team = S.Teams.First(t => Rules.SameTeam(t.Name, existing?.Name ?? name));
            if (captain.Length > 0 || existing is null) team.Captain = captain;
            if (group.Length > 0 || existing is null) team.Group = group;
            team.Division = division;
            _editingTeam = team;
        }
        if (captain.Length > 0 && S.Players.FirstOrDefault(p => p.Name.Equals(captain, StringComparison.OrdinalIgnoreCase)) is { } cap && cap.Team.Length == 0)
            cap.Team = name;
        if (!TrySave()) return;
        RefreshAll();
        Show(InfoBarSeverity.Success, "Team saved", $"{name} ({division}){(captain.Length > 0 ? $" – captain {captain}" : "")}.");
    }

    private async void TDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_editingTeam is null) return;
        int players = Calc.Squad(S, _editingTeam.Name).Count;
        if (!await Confirm("Delete team?", $"{_editingTeam.Name} will be removed.{(players > 0 ? $" Its {players} players will have no team." : "")} Matches keep the team's name.", "Delete")) return;
        foreach (var p in S.Players.Where(p => Rules.SameTeam(p.Team, _editingTeam.Name))) { p.Team = ""; p.SoldPrice = null; }
        S.Teams.Remove(_editingTeam);
        if (!TrySave()) return;
        ClearTeamForm();
        RefreshAll();
    }

    /// <summary>Share players out by category (snake order) so every team gets a similar mix.</summary>
    private async void Balance_Click(object sender, RoutedEventArgs e)
    {
        var divisions = Lists.Divisions.Where(d => S.Teams.Count(t => t.Division == d) >= 2).ToList();
        if (divisions.Count == 0) { Show(InfoBarSeverity.Warning, "Add teams first", "Balanced teams needs at least two teams in a division."); return; }
        var division = new ComboBox { Header = "Division", ItemsSource = divisions, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var keep = new CheckBox { Content = "Keep players who already have a team (only place the others)", IsChecked = true };
        var preview = new TextBlock { TextWrapping = TextWrapping.Wrap, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 12 };
        int seed = Environment.TickCount;
        Dictionary<Player, string> plan = [];
        void Plan()
        {
            plan = Calc.BalancedTeams(S, division.SelectedItem as string ?? "Men", keep.IsChecked == true, seed);
            preview.Text = string.Join("\n", plan.GroupBy(kv => kv.Value).OrderBy(g => g.Key).Select(g =>
                $"{g.Key} ({g.Count()}): " + string.Join("  ", g.GroupBy(kv => kv.Key.Category.Length == 0 ? "?" : kv.Key.Category)
                    .OrderBy(c => Lists.Categories.ToList().IndexOf(c.Key) is var i && i < 0 ? 99 : i).Select(c => $"{c.Key}×{c.Count()}"))));
        }
        division.SelectionChanged += (_, _) => Plan();
        keep.Checked += (_, _) => Plan(); keep.Unchecked += (_, _) => Plan();
        var shuffle = new Button { Content = "Shuffle again" };
        shuffle.Click += (_, _) => { seed++; Plan(); };
        Plan();
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "Players are dealt strongest category first (A+, A, B…) in snake order, so each team gets a similar mix. Captains stay with their teams." });
        panel.Children.Add(division); panel.Children.Add(keep); panel.Children.Add(shuffle);
        panel.Children.Add(new ScrollViewer { Content = preview, MaxHeight = 260 });
        if (!await Ask("Balanced teams", panel, "Apply", 760)) return;
        _store.Snapshot("before-balanced-teams");
        foreach (var (p, team) in plan) p.Team = team;
        if (!TrySave()) return;
        RefreshAll();
        Show(InfoBarSeverity.Success, "Teams balanced", $"{plan.Count} players placed in {plan.Values.Distinct().Count()} {division.SelectedItem} teams. A backup was kept.");
    }
}
