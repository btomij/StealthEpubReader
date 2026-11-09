using System.Text.Json.Serialization;

namespace StealthEpubReader.Models;

public class BookmarkItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int ChapterIndex { get; set; }
    public string ChapterTitle { get; set; } = string.Empty;
    public double ScrollRatio { get; set; }
    public string Snippet { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [JsonIgnore]
    public string PositionDisplay => $"第 {ChapterIndex + 1} 章 ({Math.Round(ScrollRatio * 100)}%)";

    [JsonIgnore]
    public string TimeDisplay => CreatedAt.ToString("MM-dd HH:mm");
}

public class SearchResultItem
{
    public int ChapterIndex { get; set; }
    public string ChapterTitle { get; set; } = string.Empty;
    public double ScrollRatio { get; set; }
    public string Snippet { get; set; } = string.Empty;

    public string LocationDisplay => $"第 {ChapterIndex + 1} 章 ({Math.Round(ScrollRatio * 100)}%) · {ChapterTitle}";
}
