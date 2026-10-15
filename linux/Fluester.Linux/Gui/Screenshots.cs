using Fluester.Engine;

namespace Fluester.Linux.Gui;

/// <summary><c>fluester app --screenshots ordner</c>: zeigt jede Seite und jeden Schritt der Einrichtung und speichert sie als PNG.
/// Für Selbsttest und CI – beweist, dass das Fenster auf diesem System wirklich aufgeht und jede Seite sich aufbauen lässt.
/// Ändert nichts: kein Dienst-Start, keine Aufnahme, kein Update-Check.</summary>
static class Screenshots
{
    public static void Run(MainWindow main, string dir, Action<int> done)
    {
        Directory.CreateDirectory(dir);
        Console.WriteLine($"GTK {Gtk.Functions.GetMajorVersion()}.{Gtk.Functions.GetMinorVersion()}.{Gtk.Functions.GetMicroVersion()} · " +
            $"libadwaita {Adw.Functions.GetMajorVersion()}.{Adw.Functions.GetMinorVersion()}.{Adw.Functions.GetMicroVersion()} · {Desktop.Session}");
        var shots = new List<(string File, Action Prepare, Func<Gtk.Window> Target)>();
        int n = 0;
        foreach (var p in MainWindow.All)
        {
            var page = p.Page;
            shots.Add(($"app-{++n:00}-{Slug(p.Title)}.png", () => main.Select(page), () => (Gtk.Window)main.Window));
        }
        Onboarding? ob = null;
        for (int i = 0; i < 6; i++)
        {
            int step = i;
            shots.Add(($"einrichtung-{step + 1}.png", () =>
            {
                if (ob == null) { main.ShowOnboarding(); ob = main.OnboardingWindow; }
                ob?.Go(step);
            }, () => ob != null ? ob.Window : main.Window));
        }

        int index = -1, failures = 0;
        Gtk.Window? target = null;
        // Abwechselnd: Seite vorbereiten, dann (nach dem Zeichnen) speichern
        bool prepare = true;
        Ui.Every(450, () =>
        {
            if (prepare)
            {
                index++;
                if (index >= shots.Count)
                {
                    Console.WriteLine(failures == 0 ? $"✓ {shots.Count} Bilder in {dir}" : $"✗ {failures} von {shots.Count} Bildern fehlgeschlagen");
                    ob?.Window.Close();
                    done(failures == 0 ? 0 : 1);
                    return false;
                }
                try { shots[index].Prepare(); target = shots[index].Target(); }
                catch (Exception e) { Console.WriteLine($"✗ {shots[index].File}: {e.Message}"); failures++; }
                prepare = false;
                return true;
            }
            prepare = true;
            var file = Path.Combine(dir, shots[index].File);
            string? error = null;
            try { if (target == null || !Save(target, file)) error = "nichts gezeichnet"; }
            catch (Exception e) { error = e.Message; }
            if (error != null) { failures++; Console.WriteLine($"✗ {shots[index].File}: {error}"); Log.Write($"Bild {file}: {error}"); }
            else Console.WriteLine($"  {shots[index].File}  {target!.GetWidth()}×{target.GetHeight()}");
            return true;
        });
    }

    static string Slug(string s) => new(s.ToLowerInvariant().Replace("ü", "ue").Replace("ö", "oe").Replace("ä", "ae").Replace(' ', '-')
        .Where(c => char.IsAsciiLetterOrDigit(c) || c == '-').ToArray());

    /// <summary>Fenster so rendern, wie es auf dem Bildschirm steht, und als PNG speichern.</summary>
    public static bool Save(Gtk.Window win, string path)
    {
        int width = win.GetWidth(), height = win.GetHeight();
        if (width <= 0 || height <= 0) return false;
        var paintable = Gtk.WidgetPaintable.New(win);
        var snapshot = Gtk.Snapshot.New();
        paintable.Snapshot(snapshot, width, height);
        var node = snapshot.ToNode();
        if (node == null) return false;
        var renderer = win.GetRenderer();
        if (renderer == null) return false;
        var texture = renderer.RenderTexture(node, null);
        return texture.SaveToPng(path) && new FileInfo(path).Length > 2000;
    }
}
