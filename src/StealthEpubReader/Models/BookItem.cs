using System.Text.Json.Serialization;

namespace StealthEpubReader.Models;

public class BookItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int TotalChapters { get; set; } = 1;
    public int CurrentChapterIndex { get; set; } = 0;
    public string CurrentChapterTitle { get; set; } = string.Empty;
    public double ScrollRatio { get; set; } = 0.0;
    public double VerticalOffset { get; set; } = 0.0;
    public DateTime LastReadTime { get; set; } = DateTime.Now;
    public List<BookmarkItem> Bookmarks { get; set; } = new();

    [JsonIgnore]
    public double OverallProgressPercent
    {
        get
        {
            if (TotalChapters <= 0) return 0.0;
            double chapterProgress = Math.Clamp(CurrentChapterIndex + Math.Clamp(ScrollRatio, 0.0, 1.0), 0.0, TotalChapters);
            return Math.Round((chapterProgress / TotalChapters) * 100.0, 1);
        }
    }

    [JsonIgnore]
    public string ProgressDisplay =>
        $"{OverallProgressPercent:0.0}% · 第 {CurrentChapterIndex + 1}/{Math.Max(1, TotalChapters)} 章";

    [JsonIgnore]
    public string LastReadDisplay =>
        LastReadTime.ToString("yyyy-MM-dd HH:mm");
}
