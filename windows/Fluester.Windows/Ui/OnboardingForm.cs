using System.Drawing;
using System.Windows.Forms;
using Fluester.Core;
using Fluester.Engine;

namespace Fluester.Windows.Ui;

/// <summary>Erster Start: Mikrofon, Taste, Erkennung (Modell laden), ausprobieren.</summary>
sealed class OnboardingForm : Form
{
    readonly SettingsStore store;
    readonly ISecretStore secrets;
    readonly Action onDone;

    public OnboardingForm(SettingsStore store, ISecretStore secrets, Action onDone)
    {
        this.store = store;
        this.secrets = secrets;
        this.onDone = onDone;
        Text = "Willkommen bei Flüster";
        Icon = TrayApp.AppIcon;
        Size = new Size(720, 820);
        MinimumSize = new Size(640, 600);
        StartPosition = FormStartPosition.CenterScreen;
        Theme.Apply(this);
        Build();
    }

    void Build()
    {
        var s = store.Current;
        var pb = new PageBuilder();
        var head = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Window };
        head.Controls.Add(new PictureBox { Image = new Icon(TrayApp.AppIcon, 64, 64).ToBitmap(), Size = new Size(64, 64), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 14, 0) });
        var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Theme.Window };
        titles.Controls.Add(new Label { Text = "Willkommen bei Flüster", Font = Theme.Title, AutoSize = true, ForeColor = Theme.Text });
        titles.Controls.Add(new Label { Text = "Halten. Sprechen. Loslassen. – In jeder App, kostenlos.", AutoSize = true, ForeColor = Theme.Secondary });
        head.Controls.Add(titles);
        pb.Add(head);

        // 1) Mikrofon
        pb.Section("1  Mikrofon");
        var mic = pb.Card();
        var micStatus = new Label { AutoSize = true, MaximumSize = new Size(560, 0) };
        void CheckMic()
        {
            var err = AudioRecorder.Probe(store.Current.MicrophoneId);
            micStatus.Text = err ?? "✓ Mikrofon bereit";
            micStatus.ForeColor = err == null ? Theme.Good : Theme.Warn;
        }
        var openPrivacy = Theme.SecondaryButton("Datenschutz öffnen");
        openPrivacy.Click += (_, _) => Controls2.Open("ms-settings:privacy-microphone");
        var retry = Theme.SecondaryButton("Erneut prüfen");
        retry.Click += (_, _) => CheckMic();
        mic.Full(micStatus);
        mic.Full(Controls2.Line(retry, openPrivacy));
        CheckMic();

        // 2) Taste
        pb.Section("2  Diktat-Taste");
        var key = pb.Card();
        key.Row("Taste", Controls2.Combo(Enum.GetValues<HotkeyChoice>(), h => h.Label(), s.Hotkey, v => store.Update(x => x.Hotkey = v)),
            "Halten = sprechen. Doppelt tippen = freihändig. + Umschalt = Command Mode.");

        // 3) Erkennung
        pb.Section("3  Erkennung");
        var rec = pb.Card();
        var engine = rec.Row("Erkennung", Controls2.Combo(Enum.GetValues<EngineChoice>(), e => e.Label(), s.Engine, v => { store.Update(x => x.Engine = v); Rebuild(); }, 300));
        if (s.Engine == EngineChoice.WhisperLocal)
        {
            rec.Full(new Label { Text = "Lade einmal ein Modell (offline, kostenlos). Empfohlen: Large v3 Turbo. Ohne starke Grafikkarte: Small.", AutoSize = true, MaximumSize = new Size(600, 0), ForeColor = Theme.Secondary });
            foreach (var m in WhisperModel.All.Where(m => m.Id is "turbo-q5" or "small"))
                rec.Full(new ModelRow(m, store, () => { }));
        }
        else
        {
            pb.Add(new ProviderEditor(s.CloudTranscription, secrets, transcription: true, cfg => store.Update(x => x.CloudTranscription = cfg)));
        }

        // 4) Ausprobieren
        pb.Section("4  Ausprobieren");
        var tryIt = pb.Card();
        tryIt.Full(new Label { Text = $"Klick ins Feld, halte {s.Hotkey.Label()}, sprich, lass los.", AutoSize = true, ForeColor = Theme.Secondary });
        tryIt.Full(new TextBox { Multiline = true, Height = 90, BackColor = Theme.Window, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Body });

        var go = Theme.PrimaryButton("Los geht's");
        go.Click += (_, _) =>
        {
            store.Update(x => x.OnboardingDone = true);
            if (!Autostart.IsEnabled && store.Current.LaunchAtLogin) Autostart.Set(true);
            onDone();
            Close();
        };
        var settings = Theme.SecondaryButton("Alle Einstellungen …");
        settings.Click += (_, _) => TrayApp.Instance?.ShowSettings(SettingsPage.General);
        var buttons = Controls2.Line(go, settings);
        buttons.Margin = new Padding(0, 14, 0, 0);
        pb.Root.Controls.Add(buttons);
        Controls.Add(pb.Root);
        AcceptButton = go;
    }

    void Rebuild()
    {
        SuspendLayout();
        foreach (Control c in Controls) c.Dispose();
        Controls.Clear();
        Build();
        ResumeLayout();
    }
}
