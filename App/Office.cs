using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Rpl;

/// <summary>Office and Edge helpers: STA thread for automation, HTML -> PDF (Edge), HTML -> .docx (Word), email (Outlook).</summary>
public static class Office
{
    public static readonly string AssetsFolder = Path.Combine(AppContext.BaseDirectory, "Assets");
    public static readonly string TemplatePath = Path.Combine(AppContext.BaseDirectory, "Templates", "RPL_Template.xltx");
    public static string Logo => Path.Combine(AssetsFolder, "RPL_Logo_classic.png");
    public static string Cricket => Path.Combine(AssetsFolder, "Cricket_512.png");
    public static string Url(string path) => new Uri(path).AbsoluteUri;

    public static dynamic NewApp(string progId, string product) =>
        Activator.CreateInstance(Type.GetTypeFromProgID(progId) ?? throw new InvalidOperationException($"{product} is not installed on this computer."))!;

    /// <summary>Office automation must run on an STA thread.</summary>
    public static Task Sta(Action work)
    {
        var done = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try { work(); done.SetResult(); }
            catch (Exception ex) { done.SetException(ex); }
            finally { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return done.Task;
    }

    /// <summary>Print an HTML page to PDF with headless Microsoft Edge.</summary>
    public static Task PrintPdf(string pageHtml, string path) => Task.Run(() =>
    {
        string edge = EdgePath();

        string work = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "RPL", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string html = Path.Combine(work, "page.html"), pdf = Path.Combine(work, "page.pdf");
            File.WriteAllText(html, pageHtml, Encoding.UTF8);
            // New headless mode first; the older one as a fallback (some Edge versions / policies refuse one or the other).
            string log = "";
            foreach (var mode in new[] { "--headless=new", "--headless" })
            {
                if (RunEdge(edge, mode, work, html, pdf, out log)) { File.Copy(pdf, path, overwrite: true); return; }
            }
            throw new InvalidOperationException("Edge did not create the PDF." + (log.Length > 0 ? " Edge said: " + log : ""));
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { /* Edge may still hold its profile for a moment */ }
        }
    });

    /// <summary>Save an HTML page as a PNG image of the given size (WhatsApp posters).</summary>
    public static Task ScreenshotPng(string pageHtml, string path, int width, int height) => Task.Run(() =>
    {
        string edge = EdgePath();
        string work = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "RPL", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string html = Path.Combine(work, "page.html"), png = Path.Combine(work, "page.png");
            File.WriteAllText(html, pageHtml, Encoding.UTF8);
            string log = "";
            foreach (var mode in new[] { "--headless=new", "--headless" })
            {
                if (RunEdge(edge, mode, work, html, png, out log, $"--screenshot={png}", $"--window-size={width},{height}", "--hide-scrollbars", "--force-device-scale-factor=1"))
                { File.Copy(png, path, overwrite: true); return; }
            }
            throw new InvalidOperationException("Edge did not create the image." + (log.Length > 0 ? " Edge said: " + log : ""));
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { /* best effort */ }
        }
    });

    private static string EdgePath() => new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
    }.FirstOrDefault(File.Exists) ?? throw new InvalidOperationException("Microsoft Edge was not found; it is needed to make PDFs and images.");

    /// <summary>
    /// Run Edge once to print a page. Returns true as soon as the output file is complete; Edge sometimes writes
    /// the file but never exits, so it is closed then rather than waited for. <paramref name="log"/> gets the end
    /// of Edge's error output (for the message if it fails).
    /// </summary>
    private static bool RunEdge(string edge, string mode, string work, string html, string pdf, out string log, params string[] instead)
    {
        var psi = new ProcessStartInfo(edge)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true,
            WorkingDirectory = work,
        };
        // Default: print to PDF. "instead" replaces the output arguments (e.g. --screenshot for images).
        var output = instead.Length > 0 ? instead : [$"--print-to-pdf={pdf}"];
        foreach (var a in new[] { mode, "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-extensions", "--no-pdf-header-footer",
                     "--allow-file-access-from-files", $"--user-data-dir={Path.Combine(work, "edge" + mode.Length)}" }.Concat(output).Append(new Uri(html).AbsoluteUri))
            psi.ArgumentList.Add(a);
        // The app's own runtime settings must not leak into Edge.
        foreach (var key in psi.Environment.Keys.Where(k => k.StartsWith("MICROSOFT_WINDOWSAPPRUNTIME", StringComparison.OrdinalIgnoreCase) || k.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase)).ToList())
            psi.Environment.Remove(key);
        using var p = Process.Start(psi)!;
        var err = new StringBuilder();
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (err) err.AppendLine(e.Data); };
        p.OutputDataReceived += (_, _) => { };
        p.BeginErrorReadLine();
        p.BeginOutputReadLine();

        bool done = false;
        long lastSize = -1;
        var until = DateTime.Now + TimeSpan.FromSeconds(60);
        while (DateTime.Now < until)
        {
            // Complete = the file exists and has stopped growing.
            long size = File.Exists(pdf) ? new FileInfo(pdf).Length : -1;
            if (size > 0 && size == lastSize) { done = true; break; }
            lastSize = size;
            if (p.HasExited && size <= 0) break;
            Thread.Sleep(400);
        }
        if (!p.HasExited) { try { p.Kill(true); p.WaitForExit(5000); } catch { /* already gone */ } }

        string text;
        lock (err) text = err.ToString().Trim();
        if (!done && text.Length == 0 && DateTime.Now >= until) text = "timed out";
        log = text.Length > 300 ? text[^300..] : text;
        return done;
    }

    /// <summary>Open an HTML page in Word and save it as .docx, with pictures stored inside the file.</summary>
    public static Task WordFromHtml(string pageHtml, string path, bool landscape) => Sta(() =>
    {
        string work = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "RPL", Guid.NewGuid().ToString("N"))).FullName;
        string html = Path.Combine(work, "page.html");
        File.WriteAllText(html, pageHtml, new UTF8Encoding(true));
        dynamic word = NewApp("Word.Application", "Microsoft Word");
        dynamic? doc = null;
        try
        {
            word.Visible = false;
            word.DisplayAlerts = 0;
            doc = word.Documents.Open(html, false, true);
            doc.ActiveWindow.View.Type = 3;   // print layout
            doc.PageSetup.Orientation = landscape ? 1 : 0;
            doc.PageSetup.TopMargin = 36; doc.PageSetup.BottomMargin = 36; doc.PageSetup.LeftMargin = 36; doc.PageSetup.RightMargin = 36;
            int shapes = doc.InlineShapes.Count;
            for (int i = 1; i <= shapes; i++)
            {
                dynamic s = doc.InlineShapes.Item(i);
                try { if (s.LinkFormat is not null) { s.LinkFormat.SavePictureWithDocument = true; s.LinkFormat.BreakLink(); } } catch { /* not a linked picture */ }
            }
            doc.SaveAs2(path, 16);   // .docx
        }
        finally
        {
            try { doc?.Close(false); } catch { /* closed */ }
            try { word.Quit(false); } catch { /* gone */ }
            Marshal.FinalReleaseComObject((object)word);
            try { Directory.Delete(work, true); } catch { /* best effort */ }
        }
    });

    public static bool OutlookInstalled => Type.GetTypeFromProgID("Outlook.Application") is not null;

    /// <summary>New Outlook message with the files attached; without Outlook, the mail app plus the folder to attach from.</summary>
    public static async Task EmailAsync(IReadOnlyList<string> files, string subject, string body)
    {
        if (OutlookInstalled)
        {
            await Sta(() =>
            {
                dynamic outlook = Activator.CreateInstance(Type.GetTypeFromProgID("Outlook.Application")!)!;
                dynamic mail = outlook.CreateItem(0);
                mail.Subject = subject;
                mail.Body = body;
                foreach (var f in files) mail.Attachments.Add(f);
                mail.Display(false);
            });
            return;
        }
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Path.GetDirectoryName(files[0])}\"") { UseShellExecute = true });
        Process.Start(new ProcessStartInfo($"mailto:?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}") { UseShellExecute = true });
    }
}
