using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Rpl;

/// <summary>Accounts page, Share &amp; print page (posters, WhatsApp reminders, printouts) and Hall of fame.</summary>
public sealed partial class MainWindow
{
    private static readonly string[] FeeOptions = ["All who pay", "Pending", "Online", "Cash", "Waived"];
    private Sponsor? _editingSponsor;
    private Expense? _editingExpense;
    private readonly Dictionary<int, NumberBox> _cashBoxes = [];
    private string? _posterPath;
    private List<Match> _posterMatches = [];
    private static string PosterFolder => Path.Combine(Store.Folder, "posters");

    private void SetupAccounts()
    {
        FeeFilter.ItemsSource = FeeOptions; FeeFilter.SelectedIndex = 0;
        SMode.ItemsSource = Lists.ExpenseModes;
        EMode.ItemsSource = Lists.ExpenseModes;
        BuildCashInputs();
    }

    private void SetupShare()
    {
        PosterDivision.ItemsSource = Lists.Divisions; PosterDivision.SelectedIndex = 0;
        IdCardWho.ItemsSource = new[] { "All active players", "Men", "Women", "Kids" }; IdCardWho.SelectedIndex = 0;
    }

    // ================================================================================== accounts

    private void RefreshAccounts()
    {
        var a = Calc.Accounts(S);
        AFees.Text = Rules.Money(a.FeeCollected);
        AFeesSub.Text = $"{a.PaidOnline} online ({Rules.Money(a.FeeOnline)}) · {a.PaidCash} cash ({Rules.Money(a.FeeCash)}) · fee {Rules.Money(a.Fee)}";
        APending.Text = Rules.Money(a.FeePending);
        APendingSub.Text = $"{a.Pending} player(s){(a.Waived > 0 ? $" · {a.Waived} waived" : "")}";
        ASponsors.Text = Rules.Money(a.Sponsors);
        ASponsorsSub.Text = $"{S.Sponsors.Count} sponsor(s)";
        AExpenses.Text = Rules.Money(a.Expenses);
        AExpensesSub.Text = a.ExpensesUnpaid > 0 ? $"{Rules.Money(a.ExpensesUnpaid)} not paid yet" : $"{S.Expenses.Count} item(s), all paid";
        ABalance.Text = Rules.Money(a.Balance);
        ABalanceSub.Text = $"income {Rules.Money(a.Income)}";
        RefreshFees();
        SponsorList.ItemsSource = S.Sponsors.Select(x => new SponsorRow(x)).ToList();
        ExpenseList.ItemsSource = S.Expenses.Select(x => new ExpenseRow(x)).ToList();
        bool was = _refreshing;
        _refreshing = true;
        foreach (var (note, box) in _cashBoxes) box.Value = S.Cash.GetValueOrDefault(note);
        _refreshing = was;
        ShowCash(a);
    }

    private void RefreshFees()
    {
        int f = Math.Max(0, FeeFilter.SelectedIndex);
        var payers = S.Players.Where(Rules.PaysFee).Where(p => f switch
        {
            1 => p.Payment is "Pending" or "",
            2 => p.Payment == "Online",
            3 => p.Payment == "Cash",
            4 => p.Payment == "Waived",
            _ => true,
        }).ToList();
        FeeList.ItemsSource = payers.Select((p, i) => new PlayerRow(p, i + 1, S)).ToList();
        FeeHint.Text = $"{payers.Count} shown. Select several with Ctrl or Shift.";
    }

    private void FeeFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_store is not null) RefreshFees();
    }

    private void AccountTabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        FeesPanel.Visibility = sender.SelectedItem == FeesTab ? Visibility.Visible : Visibility.Collapsed;
        SponsorsPanel.Visibility = sender.SelectedItem == SponsorsTab ? Visibility.Visible : Visibility.Collapsed;
        ExpensesPanel.Visibility = sender.SelectedItem == ExpensesTab ? Visibility.Visible : Visibility.Collapsed;
        CashPanel.Visibility = sender.SelectedItem == CashTab ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MarkOnline_Click(object sender, RoutedEventArgs e) => MarkPayment("Online");
    private void MarkCash_Click(object sender, RoutedEventArgs e) => MarkPayment("Cash");
    private void MarkPending_Click(object sender, RoutedEventArgs e) => MarkPayment("Pending");
    private void MarkWaived_Click(object sender, RoutedEventArgs e) => MarkPayment("Waived");

    private void MarkPayment(string payment)
    {
        var chosen = FeeList.SelectedItems.OfType<PlayerRow>().Select(r => r.Player).ToList();
        if (chosen.Count == 0) { Show(InfoBarSeverity.Informational, "Select players", "Select one or more players in the list first."); return; }
        foreach (var p in chosen) p.Payment = payment;
        if (!TrySave()) return;
        RefreshAll();
        Show(InfoBarSeverity.Success, "Fees updated", $"{chosen.Count} player(s) marked {payment.ToLowerInvariant()}.");
    }

    private void SponsorList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SponsorList.SelectedItem is not SponsorRow row) return;
        _editingSponsor = row.Sponsor;
        SName.Text = row.Sponsor.Name; SAmount.Value = (double)row.Sponsor.Amount; SMode.SelectedItem = row.Sponsor.Mode; SNotes.Text = row.Sponsor.Notes;
        SSave.Content = "Save";
    }

    private void SSave_Click(object sender, RoutedEventArgs e)
    {
        string name = Rules.TitleCase(SName.Text);
        if (name.Length == 0 || double.IsNaN(SAmount.Value)) { Show(InfoBarSeverity.Error, "Please fix", "Enter the sponsor's name and amount."); return; }
        var sp = _editingSponsor ?? new Sponsor();
        sp.Name = name; sp.Amount = (decimal)SAmount.Value; sp.Mode = SMode.SelectedItem as string ?? "Cash"; sp.Notes = Rules.Clean(SNotes.Text);
        if (_editingSponsor is null) S.Sponsors.Add(sp);
        if (!TrySave()) return;
        ClearSponsor();
        RefreshAll();
        Show(InfoBarSeverity.Success, "Sponsor saved", $"{sp.Name}: {Rules.Money(sp.Amount)}.");
    }

    private void SClear_Click(object sender, RoutedEventArgs e) => ClearSponsor();

    private void ClearSponsor()
    {
        _editingSponsor = null;
        SponsorList.SelectedItem = null;
        SName.Text = SNotes.Text = ""; SAmount.Value = double.NaN; SMode.SelectedItem = "Cash";
        SSave.Content = "Add";
    }

    private async void SDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_editingSponsor is null) return;
        if (!await Confirm("Delete sponsor?", $"{_editingSponsor.Name} ({Rules.Money(_editingSponsor.Amount)}) will be removed.", "Delete")) return;
        S.Sponsors.Remove(_editingSponsor);
        if (!TrySave()) return;
        ClearSponsor();
        RefreshAll();
    }

    private void ExpenseList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ExpenseList.SelectedItem is not ExpenseRow row) return;
        var x = row.Expense;
        _editingExpense = x;
        EItem.Text = x.Item; EPaidBy.Text = x.PaidBy; EAmount.Value = (double)x.Amount; EMode.SelectedItem = x.Mode; EPaid.IsChecked = x.Paid; ENotes.Text = x.Notes;
        ESave.Content = "Save";
    }

    private void ESave_Click(object sender, RoutedEventArgs e)
    {
        string item = Rules.Clean(EItem.Text);
        if (item.Length == 0) { Show(InfoBarSeverity.Error, "Please fix", "Enter what the expense was for."); return; }
        var x = _editingExpense ?? new Expense();
        x.Item = item; x.PaidBy = Rules.Clean(EPaidBy.Text); x.Amount = double.IsNaN(EAmount.Value) ? 0 : (decimal)EAmount.Value;
        x.Mode = EMode.SelectedItem as string ?? "Cash"; x.Paid = EPaid.IsChecked == true; x.Notes = Rules.Clean(ENotes.Text);
        if (_editingExpense is null) S.Expenses.Add(x);
        if (!TrySave()) return;
        ClearExpense();
        RefreshAll();
        Show(InfoBarSeverity.Success, "Expense saved", $"{x.Item}: {Rules.Money(x.Amount)}.");
    }

    private void EClear_Click(object sender, RoutedEventArgs e) => ClearExpense();

    private void ClearExpense()
    {
        _editingExpense = null;
        ExpenseList.SelectedItem = null;
        EItem.Text = ENotes.Text = ""; EPaidBy.Text = ""; EAmount.Value = double.NaN; EMode.SelectedItem = "Cash"; EPaid.IsChecked = true;
        ESave.Content = "Add";
    }

    private async void EDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_editingExpense is null) return;
        if (!await Confirm("Delete expense?", $"{_editingExpense.Item} ({Rules.Money(_editingExpense.Amount)}) will be removed.", "Delete")) return;
        S.Expenses.Remove(_editingExpense);
        if (!TrySave()) return;
        ClearExpense();
        RefreshAll();
    }

    private void BuildCashInputs()
    {
        foreach (int note in Lists.Denominations)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(new TextBlock { Text = $"₹{note}", Width = 60, VerticalAlignment = VerticalAlignment.Center, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            row.Children.Add(new TextBlock { Text = "×", VerticalAlignment = VerticalAlignment.Center });
            var box = new NumberBox { Width = 120, Minimum = 0, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Value = 0 };
            int n = note;
            box.ValueChanged += (_, _) =>
            {
                if (_refreshing || _store is null) return;
                S.Cash[n] = double.IsNaN(box.Value) ? 0 : (int)box.Value;
                if (S.Cash[n] == 0) S.Cash.Remove(n);
                TrySave();
                RefreshDashboard();
                ShowCash(Calc.Accounts(S));
            };
            _cashBoxes[note] = box;
            row.Children.Add(box);
            CashInputs.Children.Add(row);
        }
    }

    private void ShowCash(Accounts a)
    {
        CashExpectedText.Text = $"Should be in hand: {Rules.Money(a.CashExpected)}  (cash fees {Rules.Money(a.FeeCash)} + cash sponsors {Rules.Money(a.SponsorCash)} – cash expenses {Rules.Money(a.ExpensesCash)})";
        CashCountedText.Text = $"Counted: {Rules.Money(a.CashCounted)}";
        CashDiffText.Text = a.CashDifference == 0 ? "Cash matches ✓" : a.CashDifference > 0 ? $"{Rules.Money(a.CashDifference)} more than expected" : $"{Rules.Money(-a.CashDifference)} short";
        CashDiffText.Foreground = Brushes.Hex(a.CashDifference == 0 ? "#15803D" : a.CashDifference > 0 ? "#B45309" : "#B91C1C");
    }

    // ================================================================================== share: posters

    private void RefreshShare()
    {
        _posterMatches = S.Matches.OrderBy(m => m.Date ?? DateTime.MaxValue).ThenBy(m => m.No).ToList();
        int keep = PosterMatch.SelectedIndex;
        PosterMatch.ItemsSource = _posterMatches.Select(m => $"{m.No}. {m.Title}{(m.Played ? " ✓" : "")}").ToList();
        if (_posterMatches.Count > 0)
            PosterMatch.SelectedIndex = keep >= 0 && keep < _posterMatches.Count ? keep : Math.Max(0, _posterMatches.FindLastIndex(m => m.Played));
        var nextDay = S.Matches.Where(m => !m.Played && m.Date is not null).Select(m => m.Date!.Value).DefaultIfEmpty(DateTime.Today).Min();
        PosterDate.Date ??= new DateTimeOffset(nextDay);
        ScorerDate.Date ??= new DateTimeOffset(nextDay);

        var pending = S.Players.Where(p => Rules.PaysFee(p) && p.Payment is "Pending" or "").ToList();
        ReminderTitle.Text = $"Fee reminders – {pending.Count} pending ({Rules.Money(pending.Count * S.Settings.Fee)})";
        ReminderList.ItemsSource = pending.Select(p => new ReminderRow(p, WhatsAppUrl(p))).ToList();
    }

    private string ReminderText(Player p)
    {
        var st = S.Settings;
        return st.ReminderTemplate
            .Replace("{name}", p.Name.Split(' ')[0])
            .Replace("{fee}", st.Fee.ToString())
            .Replace("{deadline}", st.FormDeadline?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "the deadline")
            .Replace("{year}", S.Year)
            .Replace("{upi}", st.UpiId.Length > 0 ? $" (UPI: {st.UpiId})" : "");
    }

    private string WhatsAppUrl(Player p) => $"https://wa.me/91{p.Mobile}?text={Uri.EscapeDataString(ReminderText(p))}";

    private void WhatsApp_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is string url) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void CopyGroupReminder_Click(object sender, RoutedEventArgs e)
    {
        var pending = S.Players.Where(p => Rules.PaysFee(p) && p.Payment is "Pending" or "").ToList();
        if (pending.Count == 0) { Show(InfoBarSeverity.Success, "Nobody to remind", "Every player has paid."); return; }
        var st = S.Settings;
        string text = $"RPL {S.Year} – registration fee ₹{st.Fee} still pending for:\n" + string.Join("\n", pending.Select((p, i) => $"{i + 1}. {p.Name}{(p.Flat.Length > 0 ? $" ({p.Block}/{p.Flat})" : "")}")) +
                      $"\nPlease pay{(st.FormDeadline is { } d ? $" by {d:dd/MM/yyyy}" : "")}{(st.UpiId.Length > 0 ? $" – UPI: {st.UpiId}" : "")}. Thank you!";
        var dp = new DataPackage();
        dp.SetText(text);
        Clipboard.SetContent(dp);
        Show(InfoBarSeverity.Success, "Copied", $"Group message with {pending.Count} names is on the clipboard – paste it in WhatsApp.");
    }

    private async Task MakePoster(string html, string name)
    {
        Directory.CreateDirectory(PosterFolder);
        string path = Path.Combine(PosterFolder, $"{name}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        try
        {
            Show(InfoBarSeverity.Informational, "Making poster…", name, closable: false);
            await Office.ScreenshotPng(html, path, Printouts.PosterWidth, Printouts.PosterHeight);
            _posterPath = path;
            PosterImage.Source = new BitmapImage(new Uri(path));
            PosterCopy.IsEnabled = PosterSave.IsEnabled = true;
            Show(InfoBarSeverity.Success, "Poster ready", "Copy it and paste into WhatsApp, or save it.", ("Copy image", () => PosterCopy_Click(this, new RoutedEventArgs())));
        }
        catch (Exception ex) { Show(InfoBarSeverity.Error, "Poster failed", ex.Message); }
    }

    private async void PosterFixtures_Click(object sender, RoutedEventArgs e)
    {
        var date = PosterDate.Date?.Date ?? DateTime.Today;
        if (!S.Matches.Any(m => m.Date?.Date == date)) { Show(InfoBarSeverity.Warning, "No matches", $"There are no matches on {date:d MMM}."); return; }
        await MakePoster(Printouts.FixturesPoster(S, date), $"RPL_{S.Year}_matchday_{date:MMdd}");
    }

    private async void PosterResult_Click(object sender, RoutedEventArgs e)
    {
        if (PosterMatch.SelectedIndex < 0 || PosterMatch.SelectedIndex >= _posterMatches.Count) { Show(InfoBarSeverity.Warning, "Pick a match", "Choose the match first."); return; }
        var m = _posterMatches[PosterMatch.SelectedIndex];
        await MakePoster(Printouts.ResultPoster(S, m), $"RPL_{S.Year}_match{m.No}_result");
    }

    private async void PosterPoints_Click(object sender, RoutedEventArgs e)
    {
        string d = PosterDivision.SelectedItem as string ?? "Men";
        await MakePoster(Printouts.PointsPoster(S, d), $"RPL_{S.Year}_points_{d}");
    }

    private async void PosterCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_posterPath is null) return;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(_posterPath);
            var dp = new DataPackage();
            dp.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
            dp.SetStorageItems([file]);
            Clipboard.SetContent(dp);
            Clipboard.Flush();
            Show(InfoBarSeverity.Success, "Poster copied", "Paste it (Ctrl+V) into a WhatsApp chat on WhatsApp Desktop or web.whatsapp.com.");
        }
        catch (Exception ex) { Show(InfoBarSeverity.Error, "Could not copy", ex.Message); }
    }

    private async void PosterSave_Click(object sender, RoutedEventArgs e)
    {
        if (_posterPath is null) return;
        string? path = await SaveAs(Path.GetFileNameWithoutExtension(_posterPath), "PNG image", ".png");
        if (path is null) return;
        File.Copy(_posterPath, path, overwrite: true);
        Show(InfoBarSeverity.Success, "Poster saved", path, ("Open folder", () => OpenFolder(Path.GetDirectoryName(path)!)));
    }

    private void PosterFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(PosterFolder);
        OpenFolder(PosterFolder);
    }

    // ================================================================================== share: print

    private async void ScorerSheets_Click(object sender, RoutedEventArgs e)
    {
        var date = ScorerDate.Date?.Date ?? DateTime.Today;
        var games = S.Matches.Where(m => m.Date?.Date == date).OrderBy(m => Lists.Times.ToList().IndexOf(m.Time) is var i && i < 0 ? 99 : i).ToList();
        if (games.Count == 0) { Show(InfoBarSeverity.Warning, "No matches", $"There are no matches on {date:d MMM}."); return; }
        await MakePdf(Printouts.ScorerSheets(S, games), $"RPL_{S.Year}_scorer_sheets_{date:dd-MMM}", "Scorer's sheets");
    }

    private async void IdCards_Click(object sender, RoutedEventArgs e)
    {
        string who = IdCardWho.SelectedItem as string ?? "All active players";
        var players = S.Players.Where(p => !p.Cancelled && (who == "All active players" || p.Division == who)).OrderBy(p => p.Team).ThenBy(p => p.Name).ToList();
        if (players.Count == 0) { Show(InfoBarSeverity.Warning, "No players", "Nobody to make cards for."); return; }
        await MakePdf(Printouts.IdCards(S, players, PhotoFolder), $"RPL_{S.Year}_id_cards", $"ID cards ({players.Count})");
    }

    private async void Certificates_Click(object sender, RoutedEventArgs e)
    {
        var list = Printouts.CertificateList(S, CertMatch.IsChecked == true, CertSeason.IsChecked == true);
        if (list.Count == 0) { Show(InfoBarSeverity.Warning, "No certificates", "Add match awards, scorecards, a Final winner or Man of the Series first."); return; }
        await MakePdf(Printouts.Certificates(S, list), $"RPL_{S.Year}_certificates", $"Certificates ({list.Count})");
    }

    // ================================================================================== hall of fame

    private void RefreshFame()
    {
        HonoursList.ItemsSource = Calc.HallOfFame(_store.Data.Seasons).Select(h => new HonourRow(h)).ToList();
        var (pom, runs, wkts, titles) = Calc.AllTime(_store.Data.Seasons);
        FamePom.ItemsSource = pom.Select((x, i) => new LeaderRow(i + 1, x.Name, x.Count)).ToList();
        FameRuns.ItemsSource = runs.Select((x, i) => new LeaderRow(i + 1, x.Name, x.Runs)).ToList();
        FameWickets.ItemsSource = wkts.Select((x, i) => new LeaderRow(i + 1, x.Name, x.Wickets)).ToList();
        FameTitles.ItemsSource = titles.Select((x, i) => new LeaderRow(i + 1, x.Team, x.Titles)).ToList();
    }

    private async void FamePdf_Click(object sender, RoutedEventArgs e) =>
        await MakePdf(Printouts.HallOfFame(_store.Data.Seasons), "RPL_hall_of_fame", "Hall of fame");
}
