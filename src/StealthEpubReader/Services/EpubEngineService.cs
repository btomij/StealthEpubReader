using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using StealthEpubReader.Models;
using VersOne.Epub;

namespace StealthEpubReader.Services;

public class EpubEngineService : IDisposable
{
    private EpubBookRef? _currentBookRef;
    private List<EpubLocalTextContentFileRef> _spineFiles = new();
    private List<ChapterItem> _chapters = new();
    private readonly ConcurrentDictionary<int, ChapterContent> _chapterCache = new();

    public string? CurrentFilePath { get; private set; }
    public IReadOnlyList<ChapterItem> Chapters => _chapters;
    public int TotalChapters => _chapters.Count;

    /// <summary>
    /// 按需開啟 EPUB 檔案（僅載入結構與目錄，不將整本書載入記憶體）
    /// </summary>
    public async Task<(string Title, string Author, int TotalChapters)> OpenBookAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("找不到指定的 EPUB 檔案", filePath);
        }

        DisposeCurrentBook();

        _currentBookRef = await EpubReader.OpenBookAsync(filePath).ConfigureAwait(false);
        CurrentFilePath = filePath;

        var readingOrder = await _currentBookRef.GetReadingOrderAsync().ConfigureAwait(false);
        _spineFiles = readingOrder.ToList();

        // 建立目錄標題對應表
        var titleMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var navItems = await _currentBookRef.GetNavigationAsync().ConfigureAwait(false);
            if (navItems != null)
            {
                FlattenNavItems(navItems, titleMap);
            }
        }
        catch
        {
            // 部分非標準 EPUB 可能無 Navigation，後續自動從章節內文 H1/Title 擷取
        }

        _chapters = new List<ChapterItem>(_spineFiles.Count);
        for (int i = 0; i < _spineFiles.Count; i++)
        {
            var fileRef = _spineFiles[i];
            string fileName = Path.GetFileName(fileRef.FilePath ?? string.Empty);
            string key = fileRef.Key ?? fileName;

            if (!titleMap.TryGetValue(fileName, out string? title) &&
                !titleMap.TryGetValue(key, out title))
            {
                title = $"第 {i + 1} 章";
            }

            _chapters.Add(new ChapterItem
            {
                Index = i,
                Title = title
            });
        }

        string bookTitle = string.IsNullOrWhiteSpace(_currentBookRef.Title)
            ? Path.GetFileNameWithoutExtension(filePath)
            : _currentBookRef.Title.Trim();

        string bookAuthor = (_currentBookRef.AuthorList != null && _currentBookRef.AuthorList.Count > 0)
            ? string.Join(", ", _currentBookRef.AuthorList)
            : "未知作者";

        return (bookTitle, bookAuthor, _chapters.Count);
    }

    private void FlattenNavItems(IEnumerable<EpubNavigationItemRef> items, Dictionary<string, string> titleMap)
    {
        foreach (var item in items)
        {
            if (item.Link?.ContentFileUrl != null && !string.IsNullOrWhiteSpace(item.Title))
            {
                string cleanUrl = item.Link.ContentFileUrl.Split('#')[0];
                string fileName = Path.GetFileName(cleanUrl);
                if (!string.IsNullOrEmpty(fileName) && !titleMap.ContainsKey(fileName))
                {
                    titleMap[fileName] = item.Title.Trim();
                }
                if (!titleMap.ContainsKey(cleanUrl))
                {
                    titleMap[cleanUrl] = item.Title.Trim();
                }
            }

            if (item.NestedItems != null && item.NestedItems.Count > 0)
            {
                FlattenNavItems(item.NestedItems, titleMap);
            }
        }
    }

    /// <summary>
    /// 取得指定章節內容（若已在預載快取中則 0 延遲直接回傳，並自動背景預載上下章）
    /// </summary>
    public async Task<ChapterContent> GetChapterAsync(int chapterIndex)
    {
        if (_spineFiles.Count == 0)
        {
            return new ChapterContent
            {
                Index = 0,
                Title = "尚未載入章節",
                Paragraphs = new List<string> { "此書籍不包含可讀取的文字章節。" },
                FullText = "此書籍不包含可讀取的文字章節。"
            };
        }

        chapterIndex = Math.Clamp(chapterIndex, 0, _spineFiles.Count - 1);

        if (!_chapterCache.TryGetValue(chapterIndex, out var content))
        {
            content = await ParseChapterInternalAsync(chapterIndex).ConfigureAwait(false);
            _chapterCache[chapterIndex] = content;
        }

        // 背景非同步預載上一章與下一章，確保翻章節 0 卡頓
        _ = Task.Run(async () =>
        {
            await PrefetchChapterIfNeededAsync(chapterIndex + 1).ConfigureAwait(false);
            await PrefetchChapterIfNeededAsync(chapterIndex - 1).ConfigureAwait(false);
        });

        return content;
    }

    private async Task PrefetchChapterIfNeededAsync(int index)
    {
        if (index < 0 || index >= _spineFiles.Count) return;
        if (_chapterCache.ContainsKey(index)) return;

        try
        {
            var parsed = await ParseChapterInternalAsync(index).ConfigureAwait(false);
            _chapterCache.TryAdd(index, parsed);
        }
        catch
        {
            // Ignore background prefetch errors
        }
    }

    private async Task<ChapterContent> ParseChapterInternalAsync(int index)
    {
        var fileRef = _spineFiles[index];
        string html = await fileRef.ReadContentAsTextAsync().ConfigureAwait(false);

        var doc = new HtmlAgilityPack.HtmlDocument();
        doc.LoadHtml(html);

        // 移除圖片、腳本、樣式等非純文字元素（防穿幫、防卡頓）
        var nodesToRemove = doc.DocumentNode.SelectNodes("//script|//style|//img|//svg|//image|//video|//audio|//noscript");
        if (nodesToRemove != null)
        {
            foreach (var node in nodesToRemove)
            {
                node.Remove();
            }
        }

        // 嘗試從標題標籤更新更精確的章節名稱
        string chapterTitle = _chapters[index].Title;
        if (chapterTitle == $"第 {index + 1} 章")
        {
            var headingNode = doc.DocumentNode.SelectSingleNode("//h1|//h2|//h3|//title");
            if (headingNode != null)
            {
                string candidate = CleanText(headingNode.InnerText);
                if (!string.IsNullOrWhiteSpace(candidate) && candidate.Length <= 60)
                {
                    chapterTitle = candidate;
                    _chapters[index].Title = candidate;
                }
            }
        }

        var bodyNode = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;

        // 將塊級元素結尾換行，確保提取出的段落乾淨分明
        var paragraphs = new List<string>();
        ExtractParagraphs(bodyNode, paragraphs);

        if (paragraphs.Count == 0)
        {
            string fallback = CleanText(bodyNode.InnerText);
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                paragraphs.Add(fallback);
            }
            else
            {
                paragraphs.Add($"（本頁為封面或純圖片頁，已自動過濾圖片以防穿幫 — 可按右方向鍵前往下一章）");
            }
        }

        var sb = new StringBuilder();
        for (int i = 0; i < paragraphs.Count; i++)
        {
            sb.AppendLine(paragraphs[i]);
            if (i < paragraphs.Count - 1)
            {
                sb.AppendLine();
            }
        }

        return new ChapterContent
        {
            Index = index,
            Title = chapterTitle,
            Paragraphs = paragraphs,
            FullText = sb.ToString().TrimEnd()
        };
    }

    private static void ExtractParagraphs(HtmlNode root, List<string> output)
    {
        var blockTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "p", "h1", "h2", "h3", "h4", "h5", "h6", "li", "blockquote", "pre", "div", "section", "article", "tr"
        };

        var currentLine = new StringBuilder();

        void FlushLine()
        {
            if (currentLine.Length == 0) return;
            string cleaned = CleanText(currentLine.ToString());
            currentLine.Clear();
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                // 避免完全重複的相鄰標題列
                if (output.Count == 0 || output[^1] != cleaned)
                {
                    output.Add(cleaned);
                }
            }
        }

        void Walk(HtmlNode node)
        {
            if (node.NodeType == HtmlNodeType.Text)
            {
                string text = WebUtility.HtmlDecode(node.InnerText);
                if (!string.IsNullOrEmpty(text))
                {
                    currentLine.Append(text);
                }
                return;
            }

            if (node.Name.Equals("br", StringComparison.OrdinalIgnoreCase))
            {
                FlushLine();
                return;
            }

            bool isBlock = blockTags.Contains(node.Name);
            if (isBlock)
            {
                FlushLine();
            }

            foreach (var child in node.ChildNodes)
            {
                Walk(child);
            }

            if (isBlock)
            {
                FlushLine();
            }
        }

        Walk(root);
        FlushLine();
    }

    private static string CleanText(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        string decoded = WebUtility.HtmlDecode(input);
        // 將多個空白或換行壓縮為單一空白，保留段落首尾整潔
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }

    /// <summary>
    /// 跨章節全書關鍵字搜尋，回傳匹配的章節、預估捲動位置比例與前後文摘要
    /// </summary>
    public async Task<List<SearchResultItem>> SearchInBookAsync(string keyword, int maxResults = 120)
    {
        var results = new List<SearchResultItem>();
        if (string.IsNullOrWhiteSpace(keyword) || _spineFiles.Count == 0)
        {
            return results;
        }

        string query = keyword.Trim();
        for (int chIdx = 0; chIdx < _spineFiles.Count; chIdx++)
        {
            if (!_chapterCache.TryGetValue(chIdx, out var chapter))
            {
                chapter = await ParseChapterInternalAsync(chIdx).ConfigureAwait(false);
                _chapterCache.TryAdd(chIdx, chapter);
            }

            int totalParas = Math.Max(1, chapter.Paragraphs.Count);
            for (int pIdx = 0; pIdx < chapter.Paragraphs.Count; pIdx++)
            {
                string para = chapter.Paragraphs[pIdx];
                int matchPos = para.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                if (matchPos >= 0)
                {
                    int start = Math.Max(0, matchPos - 18);
                    int len = Math.Min(para.Length - start, query.Length + 46);
                    string snippet = (start > 0 ? "..." : "") +
                                     para.Substring(start, len) +
                                     (start + len < para.Length ? "..." : "");

                    double ratio = totalParas > 1 ? (double)pIdx / (totalParas - 1) : 0.0;
                    results.Add(new SearchResultItem
                    {
                        ChapterIndex = chIdx,
                        ChapterTitle = chapter.Title,
                        ScrollRatio = Math.Clamp(ratio, 0.0, 1.0),
                        Snippet = snippet
                    });

                    if (results.Count >= maxResults)
                    {
                        return results;
                    }
                }
            }
        }

        return results;
    }

    public void DisposeCurrentBook()
    {
        _chapterCache.Clear();
        _spineFiles.Clear();
        _chapters.Clear();
        if (_currentBookRef != null)
        {
            try
            {
                _currentBookRef.Dispose();
            }
            catch
            {
                // Ignore dispose errors
            }
            _currentBookRef = null;
        }
    }

    public void Dispose()
    {
        DisposeCurrentBook();
    }
}
