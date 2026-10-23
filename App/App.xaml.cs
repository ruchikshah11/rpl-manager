using Microsoft.UI.Xaml;

namespace Rpl;

public partial class App : Application
{
    private Window? _window;

    /// <summary>Unexpected errors are written here (and the app keeps running when it can).</summary>
    public static readonly string CrashLog = Path.Combine(Store.Folder, "error.log");

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Log(e.Exception);
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception ex)
        {
            Log(ex);
            throw;
        }
    }

    public static void Log(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Store.Folder);
            File.AppendAllText(CrashLog, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* nothing more we can do */ }
    }
}
