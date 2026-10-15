using Fluester.Core;
using Fluester.Engine;

namespace Fluester.Linux.Gui;

sealed partial class Pages
{
    // ───────────────────────── Wörterbuch

    Gtk.Widget Dictionary()
    {
        var page = Ui.Page();
        var form = Ui.Group("Neuer Eintrag", "Namen, Marken, Fachbegriffe – immer richtig geschrieben. Bei „klingt wie“ steht, wie die Erkennung es falsch schreibt.");
        var term = Ui.TextRow("Schreibweise", "");
        var sounds = Ui.TextRow("Klingt wie (mit Komma trennen)", "");
        form.Add(term.Row);
        form.Add(sounds.Row);
        Guid? editing = null;
        Gtk.Button? save = null;
        var list = Ui.Group("");
        var rows = new List<Gtk.Widget>();

        void Fill()
        {
            foreach (var r in rows) list.Remove(r);
            rows.Clear();
            var entries = Store.Current.Dictionary;
            list.Title = entries.Count == 0 ? "Einträge" : $"Einträge ({entries.Count})";
            if (entries.Count == 0)
            {
                var empty = Ui.Row("Noch keine Einträge", "Beispiel: Schreibweise „Wispr Flow“, klingt wie „Whisperflow, whisper flow“");
                empty.AddPrefix(Ui.Icon("accessories-dictionary-symbolic"));
                list.Add(empty);
                rows.Add(empty);
                return;
            }
            foreach (var e in entries.OrderBy(e => e.Term, StringComparer.CurrentCultureIgnoreCase))
            {
                var r = Ui.Row(e.Term, e.SoundsLike.Count > 0 ? "klingt wie: " + string.Join(", ", e.SoundsLike) : "nur Schreibweise");
                var id = e.Id;
                r.AddSuffix(Ui.IconButton("document-edit-symbolic", "Bearbeiten", () =>
                {
                    var cur = Store.Current.Dictionary.FirstOrDefault(x => x.Id == id);
                    if (cur == null) return;
                    editing = id;
                    term.Set(cur.Term);
                    sounds.Set(string.Join(", ", cur.SoundsLike));
                    save!.Label = "Änderung speichern";
                }));
                r.AddSuffix(Ui.ConfirmButton("Löschen", "Wirklich löschen?", () =>
                {
                    Update(x => x.Dictionary.RemoveAll(d => d.Id == id));
                    Fill();
                }, icon: "user-trash-symbolic"));
                list.Add(r);
                rows.Add(r);
            }
        }

        save = Ui.Button("Hinzufügen", () =>
        {
            var t = term.Get().Trim();
            if (t.Length == 0) { w.Toast("Erst die Schreibweise eingeben"); return; }
            var like = sounds.Get().Split(',').Select(v => v.Trim()).Where(v => v.Length > 0).ToArray();
            var id = editing;
            Update(x =>
            {
                var existing = id != null ? x.Dictionary.FirstOrDefault(d => d.Id == id) : x.Dictionary.FirstOrDefault(d => string.Equals(d.Term, t, StringComparison.CurrentCultureIgnoreCase));
                if (existing != null) { existing.Term = t; existing.SoundsLike = like.ToList(); }
                else x.Dictionary.Add(new DictionaryEntry(t, like));
            });
            w.Toast(id != null ? $"„{t}“ geändert" : $"„{t}“ im Wörterbuch");
            editing = null;
            term.Set("");
            sounds.Set("");
            save!.Label = "Hinzufügen";
            Fill();
        }, "suggested-action");
        var saveRow = Ui.Row("Gilt ab dem nächsten Diktat");
        saveRow.AddSuffix(save);
        form.Add(saveRow);
        page.Add(form);
        page.Add(list);
        Fill();
        return page;
    }

    // ───────────────────────── Textbausteine

    Gtk.Widget Snippets()
    {
        var page = Ui.Page();
        var form = Ui.Group("Neuer Baustein", "Ein Stichwort sagen, ein ganzer Text erscheint – etwa „meine Adresse“ oder „mein Calendly-Link“.");
        var trigger = Ui.TextRow("Stichwort", "");
        form.Add(trigger.Row);
        var text = Ui.TextArea("", 90, out var framed);
        var textLabel = Ui.Label("Text, der erscheinen soll (auch mehrzeilig)", "dim-label", "caption");
        textLabel.MarginTop = 14;
        textLabel.MarginBottom = 6;
        textLabel.MarginStart = 2;
        form.Add(textLabel);
        form.Add(framed);
        Guid? editing = null;
        Gtk.Button? save = null;
        var list = Ui.Group("");
        var rows = new List<Gtk.Widget>();

        void Fill()
        {
            foreach (var r in rows) list.Remove(r);
            rows.Clear();
            var all = Store.Current.Snippets;
            list.Title = all.Count == 0 ? "Bausteine" : $"Bausteine ({all.Count})";
            if (all.Count == 0)
            {
                var empty = Ui.Row("Noch keine Bausteine", "Sagst du nur das Stichwort, kommt nur der Baustein. Steht es in einem Satz, wird es darin ersetzt.");
                empty.AddPrefix(Ui.Icon("insert-text-symbolic"));
                list.Add(empty);
                rows.Add(empty);
                return;
            }
            foreach (var sn in all.OrderBy(x => x.Trigger, StringComparer.CurrentCultureIgnoreCase))
            {
                var preview = sn.Text.Replace("\n", " ⏎ ");
                var r = Ui.Row(sn.Trigger, preview.Length > 140 ? preview[..140] + " …" : preview);
                var id = sn.Id;
                r.AddSuffix(Ui.IconButton("document-edit-symbolic", "Bearbeiten", () =>
                {
                    var cur = Store.Current.Snippets.FirstOrDefault(x => x.Id == id);
                    if (cur == null) return;
                    editing = id;
                    trigger.Set(cur.Trigger);
                    text.Buffer!.Text = cur.Text;
                    save!.Label = "Änderung speichern";
                }));
                r.AddSuffix(Ui.ConfirmButton("Löschen", "Wirklich löschen?", () =>
                {
                    Update(x => x.Snippets.RemoveAll(d => d.Id == id));
                    Fill();
                }, icon: "user-trash-symbolic"));
                list.Add(r);
                rows.Add(r);
            }
        }

        save = Ui.Button("Hinzufügen", () =>
        {
            var t = trigger.Get().Trim();
            var body = text.Buffer!.Text ?? "";
            if (t.Length == 0 || body.Trim().Length == 0) { w.Toast("Stichwort und Text eingeben"); return; }
            var id = editing;
            Update(x =>
            {
                var existing = id != null ? x.Snippets.FirstOrDefault(d => d.Id == id) : x.Snippets.FirstOrDefault(d => string.Equals(d.Trigger, t, StringComparison.CurrentCultureIgnoreCase));
                if (existing != null) { existing.Trigger = t; existing.Text = body; }
                else x.Snippets.Add(new Snippet(t, body));
            });
            w.Toast(id != null ? $"„{t}“ geändert" : $"Baustein „{t}“ angelegt");
            editing = null;
            trigger.Set("");
            text.Buffer.Text = "";
            save!.Label = "Hinzufügen";
            Fill();
        }, "suggested-action");
        save.Halign = Gtk.Align.End;
        save.MarginTop = 12;
        form.Add(save);
        page.Add(form);
        page.Add(list);
        Fill();
        return page;
    }

    // ───────────────────────── Verlauf

    Gtk.Widget History()
    {
        var history = w.History;
        var st = history.Summary;
        var page = Ui.Page();

        var stats = Ui.Group("");
        var tiles = Gtk.FlowBox.New();
        tiles.SelectionMode = Gtk.SelectionMode.None;
        tiles.MaxChildrenPerLine = 4;
        tiles.MinChildrenPerLine = 2;
        tiles.ColumnSpacing = 12;
        tiles.RowSpacing = 12;
        tiles.Homogeneous = true;
        void Tile(string value, string label)
        {
            var v = Ui.Label(value, "fl-stat-value");
            var l = Ui.Label(label, "dim-label", "caption");
            var box = Ui.VBox(2, v, l);
            box.AddCssClass("card");
            box.AddCssClass("fl-stat");
            tiles.Append(box);
        }
        // InvariantGlobalization: keine de-DE-Kultur – Tausenderpunkt selbst setzen
        Tile(st.WeekWords.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', '.'), "Wörter diese Woche");
        Tile(st.Wpm.ToString(), "Wörter pro Minute");
        Tile(st.MinutesSaved + " min", "gespart ggü. Tippen");
        Tile(st.StreakDays + (st.StreakDays == 1 ? " Tag" : " Tage"), "in Folge");
        stats.Add(tiles);
        page.Add(stats);

        var items = history.Items;
        var group = Ui.Group(items.Count == 0 ? "Diktate" : $"Diktate ({items.Count})", "Klick auf ein Diktat kopiert den Text.");
        if (!Store.Current.KeepHistory)
            group.Description = "Der Verlauf ist aus (Allgemein › System) – neue Diktate erscheinen hier nicht.";
        if (items.Count > 0)
            group.HeaderSuffix = Ui.ConfirmButton("Alles löschen", "Wirklich alles löschen?", () =>
            {
                history.Clear();
                w.Toast("Verlauf gelöscht");
                w.Rebuild(Page.History);
            });
        var search = Gtk.SearchEntry.New();
        search.PlaceholderText = "Suchen …";
        search.MarginBottom = 12;
        var list = Gtk.ListBox.New();
        list.AddCssClass("boxed-list");
        list.SelectionMode = Gtk.SelectionMode.None;
        if (items.Count > 0) group.Add(search);
        group.Add(list);

        void Fill()
        {
            while (list.GetRowAtIndex(0) is { } r) list.Remove(r);
            var q = search.GetText()?.Trim() ?? "";
            var shown = items.Where(i => q.Length == 0 || i.Text.Contains(q, StringComparison.OrdinalIgnoreCase) || i.AppName.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(300).ToList();
            if (shown.Count == 0)
            {
                var empty = Ui.Row(items.Count == 0 ? "Noch keine Diktate" : "Nichts gefunden",
                    items.Count == 0 ? "Halte die Diktat-Taste, sprich und lass los – hier erscheint dann alles, was du diktiert hast." : null);
                empty.AddPrefix(Ui.Icon("document-open-recent-symbolic"));
                list.Append(empty);
                return;
            }
            foreach (var h in shown)
            {
                var meta = new List<string> { h.Date.ToString("dd.MM. HH:mm") };
                if (h.AppName.Length > 0) meta.Add(h.AppName);
                meta.Add(h.Words == 1 ? "1 Wort" : $"{h.Words} Wörter");
                if (h.IsCommand) meta.Add("Command Mode");
                var row = Ui.Row(h.Text.Replace("\n", " ⏎ "), string.Join(" · ", meta));
                row.TitleLines = 3;
                var text = h.Text;
                void CopyIt() { Ui.Copy(text); w.Toast("Kopiert – mit Strg+V einfügen"); }
                row.AddSuffix(Ui.IconButton("edit-copy-symbolic", "Kopieren", CopyIt));
                row.Activatable = true;
                row.OnActivated += (_, _) => Ui.Safe(CopyIt);
                list.Append(row);
            }
        }
        search.OnSearchChanged += (_, _) => Ui.Safe(Fill);
        Fill();
        page.Add(group);
        return page;
    }
}
