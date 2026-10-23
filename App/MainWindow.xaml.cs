using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Storage.Pickers;

namespace Rpl;

/// <summary>
/// Main window. This file: start-up, refresh, navigation, dashboard, seasons, import, backups, settings, generate, helpers.
/// The pages live in MainWindow.Players / Teams / Auction / Matches / Accounts / Share.cs.
/// </summary>
public sealed partial class MainWindow : Window
{
    private static readonly string Default2026Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "New folder", "2026");

    private readonly Store _store;
    private Season S => _store.Current;
    private bool _refreshing;

    public MainWindow()
    {
        InitializeComponent();
        Title = "RPL Manager";
        AppWindow.Resize(new SizeInt32(1660, 990));
        string icon = Path.Combine(Office.AssetsFolder, "RPL.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon);
        if (File.Exists(Office.Logo)) LogoImage.Source = new BitmapImage(new Uri(Office.Logo));

        // Load the data before filling any list: filling a list raises change events that read the season.
        try
        {
            _store = Store.Open();
        }
        catch (Exception ex)
        {
            _store = new Store();
            Show(InfoBarSeverity.Error, "Saved data could not be read", $"{Store.FilePath}: {ex.Message}. Use More > Backups and restore to bring back an earlier copy.");
        }

        _refreshing = true;
        SetupPlayers();
        SetupTeams();
        SetupAuction();
        SetupMatches();
        SetupAccounts();
        SetupShare();
        _refreshing = false;

        RefreshSeasons();
        LoadSeason();
        ShowPage("Players");

        if (S.Players.Count == 0 && Directory.Exists(Default2026Folder))
            Show(InfoBarSeverity.Informational, "No players yet", $"Import the 2026 season from {Default2026Folder}?",
                ("Import", () => _ = ImportFrom(Default2026Folder)));
    }

    // ================================================================================== refresh

    private void RefreshSeasons()
    {
        _refreshing = true;
        SeasonBox.ItemsSource = _store.Years();
        SeasonBox.SelectedItem = S.Year;
        _refreshing = false;
    }

    /// <summary>Everything for a newly opened season.</summary>
    private void LoadSeason()
    {
        ClearPlayerForm(); ClearTeamForm(); ClearMatchForm(); ClearSponsor(); ClearExpense();
        _onBlock = null; _sales.Clear(); _skipped.Clear(); ShowOnBlock();
        LoadSettings();
        RefreshAll();
    }

    private void RefreshAll()
    {
        var a = Calc.Accounts(S);
        SubtitleText.Text = $"Season {S.Year} · {a.Registered} players · {S.Teams.Count} teams · {S.Matches.Count} matches · fee {Rules.Money(S.Settings.Fee)} · saved automatically";
        RefreshTeamChoices();
        RefreshPlayers();
        RefreshTeams();
        RefreshAuction();
        RefreshMatches();
        RefreshStats();
        RefreshAccounts();
        RefreshShare();
        RefreshFame();
        RefreshDashboard();
    }

    private List<string> TeamNames(string? division = null) =>
        S.Teams.Where(t => t.Name.Length > 0 && (division is null || t.Division == division)).Select(t => t.Name).ToList();

    private void RefreshTeamChoices()
    {
        _refreshing = true;
        string pt = PTeam.Text, ma = MTeamA.Text, mb = MTeamB.Text;
        PTeam.ItemsSource = TeamNames(DivisionOfGender(PGender.SelectedItem as string)); PTeam.Text = pt;
        var matchTeams = TeamNames(MDivision.SelectedItem as string ?? "Men");
        MTeamA.ItemsSource = matchTeams; MTeamA.Text = ma;
        MTeamB.ItemsSource = matchTeams; MTeamB.Text = mb;
        string filter = PTeamFilter.SelectedItem as string ?? AllTeams;
        var filters = TeamNames().Prepend(AllTeams).Append(NoTeam).ToList();
        PTeamFilter.ItemsSource = filters;
        PTeamFilter.SelectedItem = filters.Contains(filter) ? filter : AllTeams;
        EPaidBy.ItemsSource = S.Expenses.Select(e => e.PaidBy).Append(S.Settings.Contact1Name).Append(S.Settings.Contact2Name)
            .Where(x => x.Length > 0).Distinct().ToList();
        _refreshing = false;
    }

    private static string DivisionOfGender(string? gender) => gender switch { "Female" => "Women", "Kids" => "Kids", _ => "Men" };

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item) ShowPage((string)item.Tag);
    }

    private void ShowPage(string tag)
    {
        var pages = new Dictionary<string, FrameworkElement>
        {
            ["Dashboard"] = DashboardPage, ["Players"] = PlayersPage, ["Teams"] = TeamsPage, ["Auction"] = AuctionPage, ["Matches"] = MatchesPage,
            ["Stats"] = StatsPage, ["Accounts"] = AccountsPage, ["Share"] = SharePage, ["Fame"] = FamePage, ["Season"] = SeasonPage,
        };
        foreach (var (key, page) in pages) page.Visibility = key == tag ? Visibility.Visible : Visibility.Collapsed;
        foreach (var o in Nav.MenuItems.OfType<NavigationViewItem>())
            if ((string)o.Tag == tag && !ReferenceEquals(Nav.SelectedItem, o)) Nav.SelectedItem = o;
    }

    // ================================================================================== dashboard

    private void RefreshDashboard()
    {
        var a = Calc.Accounts(S);
        int played = S.Matches.Count(m => m.Played);
        KpiPlayers.Text = a.Registered.ToString();
        KpiPlayersSub.Text = $"{a.Male} men · {a.Female} women · {a.Kids} kids{(a.Cancelled > 0 ? $" · {a.Cancelled} cancelled" : "")}";
        KpiFees.Text = Rules.Money(a.FeeCollected);
        KpiFeesSub.Text = a.Pending > 0 ? $"{a.Pending} pending ({Rules.Money(a.FeePending)})" : $"all {a.FeePlayers} paid";
        KpiMatches.Text = $"{played} / {S.Matches.Count}";
        KpiMatchesSub.Text = "played / scheduled";
        KpiBalance.Text = Rules.Money(a.Balance);
        KpiBalanceSub.Text = $"income {Rules.Money(a.Income)} · spent {Rules.Money(a.Expenses)}";
        KpiCash.Text = a.CashDifference == 0 ? "OK" : $"{(a.CashDifference > 0 ? "+" : "–")}{Rules.Money(Math.Abs(a.CashDifference))}";
        KpiCashSub.Text = $"counted {Rules.Money(a.CashCounted)} · should be {Rules.Money(a.CashExpected)}";

        // Next match day, with who can't play.
        var next = S.Matches.Where(m => !m.Played && m.Date is not null && m.Date.Value.Date >= DateTime.Today).Select(m => m.Date!.Value.Date).DefaultIfEmpty().Min();
        if (next == default)
        {
            NextTitle.Text = "No upcoming matches";
            NextText.Text = S.Matches.Count == 0 ? "Create fixtures on the Matches page." : "Every scheduled match is in the past or played.";
        }
        else
        {
            var games = S.Matches.Where(m => m.Date?.Date == next).OrderBy(m => Lists.Times.ToList().IndexOf(m.Time) is var i && i < 0 ? 99 : i).ToList();
            NextTitle.Text = $"Next match day: {next:dddd, d MMMM}";
            NextText.Text = string.Join("   ·   ", games.Select(m => $"{m.Time} {m.Title}{(m.Absent.Count > 0 ? $" (absent: {string.Join(", ", m.Absent)})" : "")}"));
        }

        var tables = Calc.ActiveDivisions(S).Select(d => new DivisionTable($"Points table – {d}", Calc.PointsTable(S, d).Select((x, i) => new StandingRow(x, i + 1)).ToList()))
            .Where(t => t.Rows.Count > 0).ToList();
        DashTables.ItemsSource = tables;
        DashNoResults.Visibility = S.Matches.Any(m => m.Played) ? Visibility.Collapsed : Visibility.Visible;

        var squads = S.Teams.Select(t => (t.Name, n: Calc.Squad(S, t.Name).Count)).ToList();
        int maxT = Math.Max(1, squads.Count == 0 ? 1 : squads.Max(x => x.n));
        TeamBars.ItemsSource = squads.Select(x => new BarVm(x.Name, x.n.ToString(), (double)x.n / maxT, Brushes.Hex("#6B0F1A"))).ToList();
        var live = S.Players.Where(p => !p.Cancelled).ToList();
        var cats = Lists.Categories.Select(c => (c, n: live.Count(p => p.Category == c))).Where(x => x.n > 0).ToList();
        int maxC = Math.Max(1, cats.Count == 0 ? 1 : cats.Max(x => x.n));
        CategoryBars.ItemsSource = cats.Select(x => new BarVm(x.c, x.n.ToString(), (double)x.n / maxC, Brushes.Category(x.c))).ToList();
        var blocks = live.GroupBy(p => p.Block.Length == 0 ? "?" : p.Block).OrderBy(g => g.Key).Select(g => (g.Key, n: g.Count())).ToList();
        int maxB = Math.Max(1, blocks.Count == 0 ? 1 : blocks.Max(x => x.n));
        BlockBars.ItemsSource = blocks.Select(x => new BarVm($"Block {x.Key}", x.n.ToString(), (double)x.n / maxB, Brushes.Hex("#D4A64A"))).ToList();
        DashAwards.ItemsSource = Calc.AwardTally(S).Take(8).Select(x => new AwardRow(x)).ToList();
    }

    // ================================================================================== season settings

    private void LoadSettings()
    {
        var st = S.Settings;
        SeasonTitle.Text = $"Season {S.Year} settings";
        SetFee.Value = st.Fee;
        SetDeadline.Date = st.FormDeadline is { } d ? new DateTimeOffset(d) : null;
        SetStart.Date = st.StartDate is { } s ? new DateTimeOffset(s) : null;
        SetEnd.Date = st.EndDate is { } en ? new DateTimeOffset(en) : null;
        SetC1Name.Text = st.Contact1Name; SetC1Phone.Text = st.Contact1Phone;
        SetC2Name.Text = st.Contact2Name; SetC2Phone.Text = st.Contact2Phone;
        SetTagline.Text = st.Tagline;
        SetRulesEn.Text = st.RulesEnglish.Replace("\r\n", "\n").Replace("\n", "\r");
        SetRulesGu.Text = st.RulesGujarati.Replace("\r\n", "\n").Replace("\n", "\r");
        SetRegUrl.Text = st.RegistrationUrl;
        SetUpi.Text = st.UpiId;
        SetReminder.Text = st.ReminderTemplate;
        SetMosMen.Text = st.ManOfSeries.GetValueOrDefault("Men", "");
        SetMosWomen.Text = st.ManOfSeries.GetValueOrDefault("Women", "");
        SetMosKids.Text = st.ManOfSeries.GetValueOrDefault("Kids", "");
        SetPurse.Value = st.AuctionPurse;
        SetBaseAp.Value = st.BasePrices.GetValueOrDefault("A+", 1000);
        SetBaseA.Value = st.BasePrices.GetValueOrDefault("A", 800);
        SetBaseB.Value = st.BasePrices.GetValueOrDefault("B", 600);
        SetBaseC.Value = st.BasePrices.GetValueOrDefault("C", 400);
        SetBaseD.Value = st.BasePrices.GetValueOrDefault("D", 200);
    }

    private bool ApplySettings()
    {
        var st = S.Settings;
        if (double.IsNaN(SetFee.Value) || SetFee.Value < 0) { Show(InfoBarSeverity.Error, "Please fix", "Enter the registration fee."); return false; }
        static int N(NumberBox b, int fallback) => double.IsNaN(b.Value) ? fallback : (int)b.Value;
        st.Fee = (int)SetFee.Value;
        st.FormDeadline = SetDeadline.Date?.Date; st.StartDate = SetStart.Date?.Date; st.EndDate = SetEnd.Date?.Date;
        st.Contact1Name = Rules.Clean(SetC1Name.Text); st.Contact1Phone = Rules.Clean(SetC1Phone.Text);
        st.Contact2Name = Rules.Clean(SetC2Name.Text); st.Contact2Phone = Rules.Clean(SetC2Phone.Text);
        st.Tagline = Rules.Clean(SetTagline.Text);
        st.RulesEnglish = SetRulesEn.Text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        st.RulesGujarati = SetRulesGu.Text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        st.RegistrationUrl = SetRegUrl.Text.Trim();
        st.UpiId = SetUpi.Text.Trim();
        st.ReminderTemplate = SetReminder.Text.Trim().Length > 0 ? SetReminder.Text.Trim() : new SeasonSettings().ReminderTemplate;
        st.ManOfSeries = new Dictionary<string, string> { ["Men"] = Rules.TitleCase(SetMosMen.Text), ["Women"] = Rules.TitleCase(SetMosWomen.Text), ["Kids"] = Rules.TitleCase(SetMosKids.Text) }
            .Where(kv => kv.Value.Length > 0).ToDictionary();
        st.AuctionPurse = N(SetPurse, 10000);
        st.BasePrices = new Dictionary<string, int> { ["A+"] = N(SetBaseAp, 1000), ["A"] = N(SetBaseA, 800), ["B"] = N(SetBaseB, 600), ["C"] = N(SetBaseC, 400), ["D"] = N(SetBaseD, 200) };
        return true;
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!ApplySettings() || !TrySave()) return;
        RefreshAll();
        UpdateFeeNote();
        Show(InfoBarSeverity.Success, "Settings saved", $"Season {S.Year}: fee {Rules.Money(S.Settings.Fee)}. Totals, reports, reminders and the registration form use the new values.");
    }

    private void ResetRules_Click(object sender, RoutedEventArgs e)
    {
        SetRulesEn.Text = FormText.DefaultEnglish.Replace("\r\n", "\n").Replace("\n", "\r");
        SetRulesGu.Text = FormText.DefaultGujarati.Replace("\r\n", "\n").Replace("\n", "\r");
        Show(InfoBarSeverity.Informational, "Rules reset", "The generated rules are back in the boxes. Click Save settings to keep them.");
    }

    private async void PreviewForm_Click(object sender, RoutedEventArgs e)
    {
        if (!ApplySettings()) return;
        TrySave();
        // A new name each time: an earlier preview may still be open in the PDF viewer.
        string path = Path.Combine(Path.GetTempPath(), $"RPL_{S.Year}_registration_form_{DateTime.Now:HHmmss}.pdf");
        try
        {
            Show(InfoBarSeverity.Informational, "Making preview…", "Registration form", closable: false);
            await Office.PrintPdf(Reports.RegistrationForm(S), path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            Status.IsOpen = false;
        }
        catch (Exception ex) { Show(InfoBarSeverity.Error, "Preview failed", ex.Message); }
    }

    // ================================================================================== seasons, import, backups

    private void Season_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || SeasonBox.SelectedItem is not string year || year == S.Year) return;
        _store.Data.CurrentYear = year;
        TrySave();
        LoadSeason();
        Show(InfoBarSeverity.Informational, $"Season {year}", $"{S.Players.Count(p => !p.Cancelled)} players, {S.Teams.Count} teams, {S.Matches.Count} matches.");
    }

    private async void NewSeason_Click(object sender, RoutedEventArgs e)
    {
        string next = int.TryParse(_store.Years().Last(), out int y) ? (y + 1).ToString() : DateTime.Today.Year.ToString();
        var keep = new CheckBox { Content = $"Keep this season's {S.Teams.Count} team names (no players)", IsChecked = S.Teams.Count > 0, IsEnabled = S.Teams.Count > 0 };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Text = $"Season {next} starts with no players, matches or accounts. The fee, contacts, form rules and auction settings are copied (change them in Season settings). Season {S.Year} stays as it is." });
        panel.Children.Add(keep);
        if (!await Ask($"Start season {next}", panel, "Start")) return;
        _store.Snapshot("before-new-season");
        _store.NewSeason(keep.IsChecked == true);
        if (!TrySave()) return;
        RefreshSeasons();
        LoadSeason();
        ShowPage("Season");
        Show(InfoBarSeverity.Success, $"Season {S.Year} started", "Check the fee and dates in Season settings, then add teams and players.");
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null) await ImportFrom(folder.Path);
    }

    private async Task ImportFrom(string folder)
    {
        try
        {
            var result = await Task.Run(() => Import.FromFolder(folder));
            var s = result.Season;
            if (result.Files.Count == 0) { Show(InfoBarSeverity.Warning, "Nothing to import", $"No RPL workbook or Match.docx in {folder}."); return; }
            var existing = _store.Data.Seasons.FirstOrDefault(x => x.Year == s.Year);
            bool hasData = existing is not null && (existing.Players.Count > 0 || existing.Matches.Count > 0);
            string text = $"Season {s.Year} from {string.Join(", ", result.Files)}:\n" +
                          $"• {s.Players.Count} players ({s.Players.Count(p => p.Cancelled)} cancelled), {s.Teams.Count} teams\n" +
                          $"• {s.Matches.Count} matches with {s.Matches.Sum(m => m.Awards.Count)} awards\n" +
                          $"• {s.Expenses.Count} expenses, {s.Sponsors.Count} sponsors, fee {Rules.Money(s.Settings.Fee)}" +
                          (hasData ? $"\n\nSeason {s.Year} already has data - importing replaces it (a backup is kept)." : "");
            if (!await Confirm("Import season?", text, hasData ? "Replace season" : "Import")) return;

            _store.Snapshot("before-import");
            if (existing is not null)
            {
                s.Settings.Contact1Name = existing.Settings.Contact1Name; s.Settings.Contact1Phone = existing.Settings.Contact1Phone;
                s.Settings.Contact2Name = existing.Settings.Contact2Name; s.Settings.Contact2Phone = existing.Settings.Contact2Phone;
                _store.Data.Seasons[_store.Data.Seasons.IndexOf(existing)] = s;
            }
            else
            {
                var empty = _store.Data.Seasons.FirstOrDefault(x => x.Players.Count == 0 && x.Matches.Count == 0 && x.Teams.Count == 0);
                if (empty is not null && _store.Data.Seasons.Count == 1) _store.Data.Seasons.Remove(empty);
                _store.Data.Seasons.Add(s);
            }
            _store.Data.CurrentYear = s.Year;
            if (!TrySave()) return;
            RefreshSeasons();
            LoadSeason();
            Show(result.Notes.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success, $"Season {s.Year} imported",
                $"{s.Players.Count} players, {s.Matches.Count} matches. " + string.Join(" ", result.Notes));
        }
        catch (Exception ex)
        {
            Show(InfoBarSeverity.Error, "Import failed", ex.Message);
        }
    }

    private async void ImportResponses_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        picker.FileTypeFilter.Add(".csv");
        picker.FileTypeFilter.Add(".xlsx");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        try
        {
            var r = await Task.Run(() => Import.Responses(file.Path, S));
            if (!r.Columns.ContainsKey("Name")) { Show(InfoBarSeverity.Error, "No name column", "The file needs a column with \"Name\" in its heading."); return; }
            string text = $"{file.Name}: {r.New.Count} new player(s), {r.Duplicates} already registered (same mobile or name and flat), {r.Skipped} row(s) without a name.\n\n" +
                          "Columns found:\n" + string.Join("\n", r.Columns.Select(c => $"• {c.Key} ← \"{c.Value}\"")) +
                          (r.New.Count > 0 ? "\n\nNew: " + string.Join(", ", r.New.Take(15).Select(p => p.Name)) + (r.New.Count > 15 ? " …" : "") : "");
            if (r.New.Count == 0) { Show(InfoBarSeverity.Informational, "Nothing new", text.Split('\n')[0]); return; }
            if (!await Confirm("Add online registrations?", text, $"Add {r.New.Count} players")) return;
            _store.Snapshot("before-online-import");
            S.Players.AddRange(r.New);
            if (!TrySave()) return;
            RefreshAll();
            ShowPage("Players");
            Show(InfoBarSeverity.Success, "Registrations added", $"{r.New.Count} players added from {file.Name}. Their fee is Pending until you mark it paid.");
        }
        catch (Exception ex) { Show(InfoBarSeverity.Error, "Import failed", ex.Message); }
    }

    private async void Backups_Click(object sender, RoutedEventArgs e)
    {
        var backups = _store.Backups();
        var list = new ListView { ItemsSource = backups.Select(b => b.Label).ToList(), SelectionMode = ListViewSelectionMode.Single, MaxHeight = 320 };
        if (backups.Count > 0) list.SelectedIndex = 0;
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = backups.Count == 0 ? "No backups yet. A copy is kept before the first change of each day, and before every import, restore, new season and fixture replacement."
                : "Copies are kept before the first change of each day, and before every import, restore, new season and fixture replacement (latest 60). Restoring replaces everything; the current data is backed up first.",
        });
        if (backups.Count > 0) panel.Children.Add(list);
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot, Title = "Backups and restore", Content = panel,
            PrimaryButtonText = backups.Count > 0 ? "Restore selected" : "", SecondaryButtonText = "Open backups folder", CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
        };
        var answer = await dialog.ShowAsync();
        if (answer == ContentDialogResult.Secondary) { Directory.CreateDirectory(Store.BackupFolder); OpenFolder(Store.BackupFolder); return; }
        if (answer != ContentDialogResult.Primary || list.SelectedIndex < 0) return;
        try { _store.Restore(backups[list.SelectedIndex]); }
        catch (Exception ex) { Show(InfoBarSeverity.Error, "Restore failed", ex.Message); return; }
        RefreshSeasons();
        LoadSeason();
        Show(InfoBarSeverity.Success, "Restored", $"Data from {backups[list.SelectedIndex].Saved:dd MMM yyyy, HH:mm} is back. The data you had before was backed up.");
    }

    private void DataFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Store.Folder);
        OpenFolder(Store.Folder);
    }

    // ================================================================================== generate

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        int certs = Printouts.CertificateList(S, true, true).Count;
        var unplayed = S.Matches.Where(m => !m.Played).OrderBy(m => m.Date ?? DateTime.MaxValue).ThenBy(m => m.No).ToList();
        var boxes = new (CheckBox Box, string Key)[]
        {
            (new CheckBox { Content = "Excel workbook (.xlsx) – from the RPL template; Excel works out the totals and points tables", IsChecked = true }, "xlsx"),
            (new CheckBox { Content = "CSV files – players, matches, accounts, stats", IsChecked = true }, "csv"),
            (new CheckBox { Content = "Season report – Word (.docx)", IsChecked = true }, "docx"),
            (new CheckBox { Content = "Season report – PDF", IsChecked = true }, "pdf"),
            (new CheckBox { Content = $"Registration form – PDF (Gujarati + English, fee {Rules.Money(S.Settings.Fee)})", IsChecked = true }, "form"),
            (new CheckBox { Content = $"Match schedule + one page per team – PDF ({S.Teams.Count} teams)", IsChecked = S.Teams.Count > 0, IsEnabled = S.Teams.Count > 0 }, "sched"),
            (new CheckBox { Content = $"Award certificates – PDF ({certs})", IsChecked = false, IsEnabled = certs > 0 }, "certs"),
            (new CheckBox { Content = $"Player ID cards – PDF ({S.Players.Count(p => !p.Cancelled)})", IsChecked = false }, "ids"),
            (new CheckBox { Content = $"Scorer's sheets for matches not played yet – PDF ({unplayed.Count})", IsChecked = false, IsEnabled = unplayed.Count > 0 }, "scorer"),
            (new CheckBox { Content = "Hall of fame – PDF (all seasons)", IsChecked = false }, "fame"),
        };
        var name = new TextBox { Header = "File name", Text = $"RPL_{S.Year}" };
        var panel = new StackPanel { Spacing = 4 };
        foreach (var (b, _) in boxes) panel.Children.Add(b);
        panel.Children.Add(name);
        if (!await Ask($"Generate files for season {S.Year}", panel, "Choose folder…")) return;

        string baseName = string.Concat(name.Text.Trim().Split(Path.GetInvalidFileNameChars()));
        if (baseName.Length == 0) return;
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        string Out(string suffix) => Path.Combine(folder.Path, baseName + suffix);
        bool On(string key) => boxes.First(b => b.Key == key).Box.IsChecked == true;

        var jobs = new List<(string Label, string[] Files, Func<Task<IEnumerable<string>>> Make)>();
        async Task<IEnumerable<string>> Pdf(string html, string file) { await Office.PrintPdf(html, file); return [file]; }
        if (On("xlsx")) jobs.Add(("Excel", [Out(".xlsx")], async () => { await Exporters.Excel(S, Out(".xlsx")); return [Out(".xlsx")]; }));
        if (On("csv")) jobs.Add(("CSV", [Out("_players.csv"), Out("_matches.csv"), Out("_accounts.csv"), Out("_stats.csv")], async () => await Exporters.Csv(S, Out(""))));
        if (On("docx")) jobs.Add(("Word report", [Out("_report.docx")], async () => { await Office.WordFromHtml(Reports.SeasonReport(S, forWord: true), Out("_report.docx"), landscape: true); return [Out("_report.docx")]; }));
        if (On("pdf")) jobs.Add(("PDF report", [Out("_report.pdf")], () => Pdf(Reports.SeasonReport(S, forWord: false), Out("_report.pdf"))));
        if (On("form")) jobs.Add(("Registration form", [Out("_registration_form.pdf")], () => Pdf(Reports.RegistrationForm(S), Out("_registration_form.pdf"))));
        if (On("sched")) jobs.Add(("Team schedules", [Out("_schedules.pdf")], () => Pdf(Reports.TeamSchedules(S), Out("_schedules.pdf"))));
        if (On("certs")) jobs.Add(("Certificates", [Out("_certificates.pdf")], () => Pdf(Printouts.Certificates(S, Printouts.CertificateList(S, true, true)), Out("_certificates.pdf"))));
        if (On("ids")) jobs.Add(("ID cards", [Out("_id_cards.pdf")], () => Pdf(Printouts.IdCards(S, S.Players.Where(p => !p.Cancelled), PhotoFolder), Out("_id_cards.pdf"))));
        if (On("scorer")) jobs.Add(("Scorer's sheets", [Out("_scorer_sheets.pdf")], () => Pdf(Printouts.ScorerSheets(S, unplayed), Out("_scorer_sheets.pdf"))));
        if (On("fame")) jobs.Add(("Hall of fame", [Out("_hall_of_fame.pdf")], () => Pdf(Printouts.HallOfFame(_store.Data.Seasons), Out("_hall_of_fame.pdf"))));
        if (jobs.Count == 0) return;

        var existing = jobs.SelectMany(j => j.Files).Where(File.Exists).Select(Path.GetFileName).ToList();
        if (existing.Count > 0 && !await Confirm("Replace files?", $"Already in {folder.Path}:\n{string.Join("\n", existing)}", "Replace")) return;

        SetBusy(true);
        var made = new List<string>();
        var failed = new List<string>();
        foreach (var job in jobs)
        {
            Show(InfoBarSeverity.Informational, "Generating…", job.Label, closable: false);
            try { made.AddRange(await job.Make()); }
            catch (Exception ex) { failed.Add($"{job.Label}: {ex.Message}"); }
        }
        SetBusy(false);

        var actions = new List<(string, Action)> { ("Open folder", () => OpenFolder(folder.Path)) };
        if (made.Count > 0) actions.Add(("Email…", () => _ = Email(made)));
        string names = string.Join(", ", made.Select(Path.GetFileName));
        if (failed.Count == 0) Show(InfoBarSeverity.Success, "Files generated", $"{names} in {folder.Path}", actions.ToArray());
        else Show(made.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Error, made.Count > 0 ? "Some files were not generated" : "Nothing was generated",
            (made.Count > 0 ? $"Made: {names}. " : "") + "Failed: " + string.Join(" | ", failed), actions.ToArray());
    }

    private async Task Email(IReadOnlyList<string> files)
    {
        try
        {
            var a = Calc.Accounts(S);
            await Office.EmailAsync(files, $"RPL {S.Year}",
                $"Ratnadeep Premier League {S.Year}: {a.Registered} players, {S.Teams.Count} teams, {S.Matches.Count(m => m.Played)} of {S.Matches.Count} matches played, balance {Rules.Money(a.Balance)}.");
            if (!Office.OutlookInstalled) Show(InfoBarSeverity.Informational, "Attach the files", "Outlook isn't installed, so your mail app opened without attachments. Drag the files from the folder that opened.");
        }
        catch (Exception ex) { Show(InfoBarSeverity.Error, "Could not open email", ex.Message); }
    }

    private void SetBusy(bool busy) =>
        GenerateButton.IsEnabled = ImportButton.IsEnabled = MoreButton.IsEnabled = SeasonBox.IsEnabled = PSave.IsEnabled = !busy;

    // ================================================================================== helpers

    private bool TrySave()
    {
        try { _store.Save(); return true; }
        catch (Exception ex) { Show(InfoBarSeverity.Error, "Could not save", $"{Store.FilePath}: {ex.Message}"); return false; }
    }

    private static void OpenFolder(string path) => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });

    private static void OpenFile(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    /// <summary>Ask where to save one file; null when cancelled.</summary>
    private async Task<string?> SaveAs(string suggestedName, string label, string extension)
    {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = suggestedName };
        picker.FileTypeChoices.Add(label, [extension]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    /// <summary>Make a PDF from a page, ask where to save it, then open it.</summary>
    private async Task MakePdf(string html, string suggestedName, string label)
    {
        string? path = await SaveAs(suggestedName, "PDF", ".pdf");
        if (path is null) return;
        try
        {
            Show(InfoBarSeverity.Informational, "Making PDF…", label, closable: false);
            await Office.PrintPdf(html, path);
            Show(InfoBarSeverity.Success, $"{label} saved", path, ("Open", () => OpenFile(path)), ("Open folder", () => OpenFolder(Path.GetDirectoryName(path)!)));
        }
        catch (Exception ex) { Show(InfoBarSeverity.Error, $"{label} failed", ex.Message); }
    }

    private void Show(InfoBarSeverity severity, string title, string message, params (string Text, Action Run)[] actions) =>
        Show(severity, title, message, closable: true, actions);

    private void Show(InfoBarSeverity severity, string title, string message, bool closable, params (string Text, Action Run)[] actions)
    {
        Status.Severity = severity;
        Status.Title = title;
        Status.Message = message;
        Status.IsClosable = closable;
        if (actions.Length > 0)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
            foreach (var (text, run) in actions)
            {
                var b = new Button { Content = text };
                b.Click += (_, _) => run();
                row.Children.Add(b);
            }
            Status.Content = row;
        }
        else Status.Content = null;
        Status.IsOpen = true;
    }

    private async Task<bool> Confirm(string title, string message, string yes) =>
        await Ask(title, new ScrollViewer { MaxHeight = 380, Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap } }, yes);

    private async Task<bool> Ask(string title, UIElement content, string yes, double maxWidth = 0)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot, Title = title, Content = content,
            PrimaryButtonText = yes, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary,
        };
        if (maxWidth > 0) dialog.Resources["ContentDialogMaxWidth"] = maxWidth;
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static int? Num(NumberBox b) => double.IsNaN(b.Value) ? null : (int)b.Value;
    private static double ToBox(int? v) => v is { } n ? n : double.NaN;
}
