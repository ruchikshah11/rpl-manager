using System.Globalization;
using System.Text.RegularExpressions;

namespace Rpl;

/// <summary>One season of the league: settings, players, teams, matches and money. Each year is separate.</summary>
public sealed class Season
{
    public string Year { get; set; } = DateTime.Today.Year.ToString(CultureInfo.InvariantCulture);
    public SeasonSettings Settings { get; set; } = new();
    public List<Player> Players { get; set; } = [];
    public List<Team> Teams { get; set; } = [];
    public List<Match> Matches { get; set; } = [];
    public List<Expense> Expenses { get; set; } = [];
    public List<Sponsor> Sponsors { get; set; } = [];

    /// <summary>Cash count: note/coin value -> how many.</summary>
    public Dictionary<int, int> Cash { get; set; } = [];
}

/// <summary>Things that change from season to season: fee, dates, contacts, rules on the registration form.</summary>
public sealed class SeasonSettings
{
    public int Fee { get; set; } = 250;
    public DateTime? FormDeadline { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string Contact1Name { get; set; } = "Ruchik";
    public string Contact1Phone { get; set; } = "9909964119";
    public string Contact2Name { get; set; } = "Parshwa";
    public string Contact2Phone { get; set; } = "8460717189";
    public string Tagline { get; set; } = "PLAY BOLD";

    /// <summary>Instructions on the registration form, one per line. {fee} {deadline} {start} {end} are filled in.</summary>
    public string RulesEnglish { get; set; } = FormText.DefaultEnglish;
    public string RulesGujarati { get; set; } = FormText.DefaultGujarati;

    // Auction
    public int AuctionPurse { get; set; } = 10000;
    public Dictionary<string, int> BasePrices { get; set; } = new() { ["A+"] = 1000, ["A"] = 800, ["B"] = 600, ["C"] = 400, ["D"] = 200 };

    // Online registration and reminders
    public string RegistrationUrl { get; set; } = "";     // Google Form link: printed as a QR code on the registration form
    public string UpiId { get; set; } = "";
    public string ReminderTemplate { get; set; } =
        "Hi {name}, this is a reminder from RPL {year}: the registration fee of ₹{fee} is still pending. Please pay by {deadline}{upi}. Thank you!";

    /// <summary>Man of the Series per division ("Men" -> "Meet Mehta").</summary>
    public Dictionary<string, string> ManOfSeries { get; set; } = [];
}

public sealed class Player
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FormNo { get; set; } = "";
    public string Name { get; set; } = "";
    public string Age { get; set; } = "";
    public string Block { get; set; } = "";
    public string Flat { get; set; } = "";
    public string Mobile { get; set; } = "";
    public string Gender { get; set; } = "Male";      // Male / Female / Kids
    public string Payment { get; set; } = "Pending";  // Online / Cash / Pending / Waived
    public string PaidTo { get; set; } = "";          // e.g. "Ruchik UPI"
    public string Team { get; set; } = "";
    public string Category { get; set; } = "";        // A+ / A / B / C / D / Kids
    public bool Cancelled { get; set; }
    public string Notes { get; set; } = "";
    public DateTime Registered { get; set; } = DateTime.Today;
    public int? SoldPrice { get; set; }                // auction price; null = not auctioned
    public string Photo { get; set; } = "";            // file name in the data folder's photos\ (for ID cards)

    /// <summary>Men / Women / Kids, from the gender.</summary>
    public string Division => Gender switch { "Female" => "Women", "Kids" => "Kids", _ => "Men" };

    public Player Copy() => (Player)MemberwiseClone();
}

public sealed class Team
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Captain { get; set; } = "";
    public string Group { get; set; } = "";           // optional, e.g. "Apex" / "Blaze"
    public string Division { get; set; } = "Men";     // Men / Women / Kids - each division has its own league
}

public sealed class Match
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int No { get; set; }
    public DateTime? Date { get; set; }
    public string Time { get; set; } = "";
    public string TeamA { get; set; } = "";
    public string TeamB { get; set; } = "";
    public string Stage { get; set; } = "League";
    public string Winner { get; set; } = "";          // a team name, "No result", or empty = not played yet
    public string Result { get; set; } = "";          // e.g. "won by 12 runs"
    public List<Award> Awards { get; set; } = [];
    public string Division { get; set; } = "";        // Men / Women / Kids; empty = from the teams (old data)
    public TeamScore ScoreA { get; set; } = new();
    public TeamScore ScoreB { get; set; } = new();
    public List<Performance> Performances { get; set; } = [];
    public List<string> Absent { get; set; } = [];    // players not available for this match

    public string Title => TeamA.Length == 0 && TeamB.Length == 0 ? "" : $"{TeamA} vs {TeamB}";
    public bool Played => Winner.Length > 0;
}

/// <summary>A team's innings: runs, wickets and overs ("8.3" = 8 overs and 3 balls).</summary>
public sealed class TeamScore
{
    public int? Runs { get; set; }
    public int? Wickets { get; set; }
    public string Overs { get; set; } = "";

    public bool HasScore => Runs is not null;
    public override string ToString() => Runs is null ? "" : $"{Runs}/{Wickets ?? 0}{(Overs.Length > 0 ? $" ({Overs} ov)" : "")}";
}

/// <summary>One player's batting and bowling in a match.</summary>
public sealed class Performance
{
    public string Player { get; set; } = "";
    public string Team { get; set; } = "";
    public int? Runs { get; set; }
    public int? Balls { get; set; }
    public bool NotOut { get; set; }
    public string OversBowled { get; set; } = "";
    public int? RunsConceded { get; set; }
    public int? Wickets { get; set; }

    public bool Batted => Runs is not null || Balls is not null;
    public bool Bowled => OversBowled.Length > 0 || Wickets is not null || RunsConceded is not null;
}

public sealed class Award
{
    public string Player { get; set; } = "";
    public string Title { get; set; } = "";
}

public sealed class Expense
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Item { get; set; } = "";
    public string PaidBy { get; set; } = "";
    public decimal Amount { get; set; }
    public bool Paid { get; set; } = true;
    public string Mode { get; set; } = "Cash";        // Cash / Online
    public string Notes { get; set; } = "";
}

public sealed class Sponsor
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public decimal Amount { get; set; }
    public string Mode { get; set; } = "Cash";
    public string Notes { get; set; } = "";
}

/// <summary>Choices offered in the forms. Team names are not here: they belong to each season.</summary>
public static class Lists
{
    public static readonly IReadOnlyList<string> Genders = ["Male", "Female", "Kids"];
    public static readonly IReadOnlyList<string> Divisions = ["Men", "Women", "Kids"];
    public static readonly IReadOnlyList<string> Payments = ["Online", "Cash", "Pending", "Waived"];
    public static readonly IReadOnlyList<string> Categories = ["A+", "A", "B", "C", "D", "Kids"];
    public static readonly IReadOnlyList<string> Blocks = ["A", "B", "C", "D", "E", "F"];
    public static readonly IReadOnlyList<string> Stages = ["League", "Qualifier 1", "Eliminator", "Qualifier 2", "Final", "Girls", "Kids"];
    public static readonly IReadOnlyList<string> Times = ["8–9 PM", "9–10 PM", "10–11 PM", "11–12 PM"];
    public static readonly IReadOnlyList<string> AwardTitles = ["Player of the Match", "Best Batter", "Best Bowler", "Fighter of the Match", "Best Fielder", "Man of the Series"];
    public static readonly IReadOnlyList<string> ExpenseModes = ["Cash", "Online"];
    public static readonly IReadOnlyList<int> Denominations = [500, 200, 100, 50, 20, 10, 5, 2, 1];
    public const string NoResult = "No result";
}

public static partial class Rules
{
    /// <summary>Only men pay the registration fee; women and kids play free (as on the form).</summary>
    public static bool PaysFee(Player p) => !p.Cancelled && p.Gender == "Male";

    public static bool ValidMobile(string m) => MobilePattern().IsMatch(m);

    public static string Clean(string? s) => SpacesPattern().Replace((s ?? "").Trim(), " ");

    /// <summary>"ruchik shah" -> "Ruchik Shah".</summary>
    public static string TitleCase(string s) =>
        string.Join(' ', Clean(s).Split(' ').Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..]));

    /// <summary>Same name ignoring case, spaces and a trailing "s" (Ultimate Warrior / Ultimate Warriors).</summary>
    public static bool SameTeam(string a, string b)
    {
        static string N(string x) => SpacesPattern().Replace(x.Trim().ToLowerInvariant(), " ").TrimEnd('s');
        return N(a) == N(b);
    }

    public static string Money(decimal v) => "₹" + v.ToString("#,##0", CultureInfo.GetCultureInfo("en-IN"));

    public static string Ordinal(int n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

    /// <summary>Digits in Gujarati script, for the Gujarati page of the registration form.</summary>
    public static string GujaratiDigits(string s) => new(s.Select(c => c is >= '0' and <= '9' ? (char)('૦' + (c - '0')) : c).ToArray());

    [GeneratedRegex(@"^[6-9]\d{9}$")]
    private static partial Regex MobilePattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesPattern();
}

/// <summary>Default registration-form rules (from RPL_2026.docx). Placeholders are filled from the season settings.</summary>
public static class FormText
{
    public const string DefaultEnglish = """
        Submit the registration form you received at your residence by {deadline}; otherwise, it will not be accepted.
        The registration fee is ₹{fee} per person (non-refundable).
        Only individuals who have completed the 8th standard are eligible to fill out the form.
        Everyone is requested to submit their form to the RPL Group members before {deadline}.
        This event is for flat members only. Outside members will not be allowed.
        All members are requested not to park 2-wheelers or 4-wheelers in the ground (Blocks A, B, C, D) after 8 PM. Vehicles should be parked outside.
        All participants must be present on time on match day. If a player is absent, no replacement will be allowed in that team.
        The decision of the committee and management members will be final, and you should not raise any objection to that decision.
        Sponsorships are welcome during the tournament. There is no fixed contribution amount.
        This registration form is valid for men. For women and children below 8th standard, matches will be held during the current season. They do not need to fill in the form.
        """;

    public const string DefaultGujarati = """
        રજિસ્ટ્રેશન ફોર્મ તમારા રેસિડેન્સી ખાતે આપશે તે તારીખ {deadline} સુધીમાં સબમિટ કરો, અન્યથા ફોર્મ સ્વીકારવામાં આવશે નહીં.
        રજિસ્ટ્રેશન ફી રૂ. {fee} પ્રતિ વ્યક્તિ છે (Non-Refundable).
        જે વ્યક્તિએ ૮ મા ધોરણની પરીક્ષા પાસ કરી છે તે ફોર્મ ભરવા માટે પાત્ર છે.
        દરેક વ્યક્તિને વિનંતી છે કે તેઓ {deadline} પહેલા RPL Group સભ્યોને પોતાનું ફોર્મ મોકલે.
        આર.પી.એલ. ઇવેન્ટ ફક્ત ફ્લેટ સભ્યો માટે છે. બહારના સભ્યો ને મંજૂરી અપાસે નહીં.
        દરેક સભ્યને નમ્ર વિનંતી છે કે તેઓ રાત્રે ૮ વાગ્યા પછી ગ્રાઉન્ડ (બ્લોક A, B, C, D) માં ૨ વ્હીલર અને ૪ વ્હીલર વાહનો પાર્ક ન કરે, તેઓએ બહાર પાર્ક કરવા જોઈએ.
        દરેક વ્યક્તિને મેચના દિવસે સમયસર હાજર રહેવા વિનંતી કરવામાં આવે છે જો તે વ્યક્તિ ઉપલબ્ધ ન હોય તો તે ટીમમાં કોઈ નવો સભ્ય રમાડી શકશે નહીં.
        કમિટી અને મેનેજમેન્ટ સભ્યોનો નિર્ણય આખરી રહેશે તમારે તે નિર્ણય પર કોઈ વાંધો ન આપવો જોઈએ.
        ટુર્નામેન્ટ દરમિયાન દરેક સ્પોન્સરને સ્વીકારવામાં આવશે. કોઈ રકમ ફિક્સ નથી.
        આ રજીસ્ટ્રેશન ફોર્મ પુરૂષો માટે માન્ય છે. મહિલાઓ અને ૮મા ધોરણથી નીચેના બાળકો માટે, ચાલુ સિઝન દરમિયાન મેચો યોજાશે. તેમણે ફોર્મ ભરવાની જરૂર નથી.
        """;
}
