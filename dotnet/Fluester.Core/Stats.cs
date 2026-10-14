namespace Fluester.Core;

/// <summary>Ein Diktat im Verlauf.</summary>
public sealed class HistoryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Date { get; set; } = DateTime.Now;
    public string Raw { get; set; } = "";
    public string Text { get; set; } = "";
    public string AppName { get; set; } = "";
    public string AppId { get; set; } = "";
    public double Seconds { get; set; }
    public bool IsCommand { get; set; }
    public string Engine { get; set; } = "";

    public int Words => Stats.WordCount(Text);
}

public sealed record StatsSummary(int TotalWords, int WeekWords, int TodayWords, int Dictations, int Wpm, int MinutesSaved, int StreakDays);

public static class Stats
{
    public static int WordCount(string s) =>
        Fluester.Core.Text.Words(s).Count(w => w.Any(char.IsLetterOrDigit));

    /// <summary>Wörter, WPM, gesparte Minuten (gegenüber Tippen mit 40 WPM) und Tage in Folge.</summary>
    public static StatsSummary Summary(IEnumerable<HistoryItem> items, DateTime? now = null)
    {
        var n = now ?? DateTime.Now;
        var dictations = items.Where(i => !i.IsCommand).ToList();
        int total = dictations.Sum(i => i.Words);
        var startOfDay = n.Date;
        var weekAgo = n.AddDays(-7);
        int today = dictations.Where(i => i.Date >= startOfDay).Sum(i => i.Words);
        int week = dictations.Where(i => i.Date >= weekAgo).Sum(i => i.Words);
        double secs = dictations.Sum(i => i.Seconds);
        int wpm = secs > 5 ? (int)Math.Round(total / (secs / 60)) : 0;
        int saved = Math.Max(0, (int)Math.Round(total / 40.0 - secs / 60));
        var days = dictations.Select(i => i.Date.Date).ToHashSet();
        int streak = 0;
        for (var d = startOfDay; days.Contains(d); d = d.AddDays(-1)) streak++;
        return new StatsSummary(total, week, today, dictations.Count, wpm, saved, streak);
    }
}
