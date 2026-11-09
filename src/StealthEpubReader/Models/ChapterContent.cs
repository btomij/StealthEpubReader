namespace StealthEpubReader.Models;

public class ChapterItem
{
    public int Index { get; set; }
    public string Title { get; set; } = string.Empty;
}

public class ChapterContent
{
    public int Index { get; set; }
    public string Title { get; set; } = string.Empty;
    public List<string> Paragraphs { get; set; } = new();
    public string FullText { get; set; } = string.Empty;
}
