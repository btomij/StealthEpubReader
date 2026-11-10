using System.IO;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using StealthEpubReader.Models;

namespace StealthEpubReader.Services;

public class StorageService
{
    private readonly string _baseDir;
    private readonly string _dataDir;
    private readonly string _defaultBooksDir;
    private readonly string _settingsPath;
    private readonly string _libraryPath;
    private readonly object _saveLock = new();
    private CancellationTokenSource? _debounceCts;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public AppSettings Settings { get; private set; } = new();
    public List<BookItem> Library { get; private set; } = new();

    public string DataDirectory => _dataDir;
    public string DefaultBooksDirectory => _defaultBooksDir;

    public StorageService()
    {
        _baseDir = AppDomain.CurrentDomain.BaseDirectory;
        _dataDir = Path.Combine(_baseDir, "data");
        _defaultBooksDir = Path.Combine(_dataDir, "books");
        Directory.CreateDirectory(_dataDir);
        Directory.CreateDirectory(_defaultBooksDir);

        _settingsPath = Path.Combine(_dataDir, "settings.json");
        _libraryPath = Path.Combine(_dataDir, "library.json");
    }

    /// <summary>
    /// 取得目前生效的書庫存放目錄（若未指定自訂路徑，預設為 data/books）
    /// </summary>
    public string GetActiveBooksDirectory()
    {
        if (!string.IsNullOrWhiteSpace(Settings.CustomBooksDirectory))
        {
            try
            {
                string customFull = Path.GetFullPath(Settings.CustomBooksDirectory.Trim());
                Directory.CreateDirectory(customFull);
                return customFull;
            }
            catch
            {
                // 若自訂路徑無效或無權限，自動退回預設本地目錄
            }
        }

        Directory.CreateDirectory(_defaultBooksDir);
        return _defaultBooksDir;
    }

    /// <summary>
    /// 將匯入的原始 EPUB 複製至受管理的書庫目錄，並以低調 GUID ({BookId}.epub) 命名
    /// </summary>
    public (string StoredPath, string ResolvedFullPath, string ContentHash) CopyBookToManagedStorage(string sourceFilePath, string bookId)
    {
        string fullSource = Path.GetFullPath(sourceFilePath);
        string hash = ComputeFileSha256(fullSource);

        string targetDir = GetActiveBooksDirectory();
        string destFileName = $"{bookId}.epub";
        string destFullPath = Path.Combine(targetDir, destFileName);

        if (!string.Equals(fullSource, destFullPath, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(fullSource, destFullPath, overwrite: true);
        }

        // 若存放於程式目錄下，儲存相對路徑以確保整個資料夾搬移或放隨身碟時 100% 免設定可攜
        string storedPath = IsSubPathOf(_baseDir, destFullPath)
            ? Path.GetRelativePath(_baseDir, destFullPath)
            : destFullPath;

        return (storedPath, destFullPath, hash);
    }

    /// <summary>
    /// 解析書籍實際檔案路徑（支援相對路徑、自訂書庫路徑與資料夾搬移後的自動尋回）
    /// </summary>
    public string ResolveBookFilePath(BookItem book)
    {
        if (string.IsNullOrWhiteSpace(book.FilePath))
        {
            return string.Empty;
        }

        // 1. 若為相對路徑，優先相對於程式根目錄解析
        if (!Path.IsPathRooted(book.FilePath))
        {
            string combined = Path.GetFullPath(Path.Combine(_baseDir, book.FilePath));
            if (File.Exists(combined)) return combined;
        }

        // 2. 若直接存在
        if (File.Exists(book.FilePath))
        {
            return Path.GetFullPath(book.FilePath);
        }

        // 3. 若整個閱讀器資料夾曾被搬移，自動在目前生效的書庫目錄與預設 data/books 中尋找同名或 {BookId}.epub
        string fileName = Path.GetFileName(book.FilePath);
        string[] candidateDirs = { GetActiveBooksDirectory(), _defaultBooksDir, _dataDir };
        foreach (string dir in candidateDirs)
        {
            string candidate1 = Path.Combine(dir, $"{book.Id}.epub");
            if (File.Exists(candidate1)) return candidate1;

            if (!string.IsNullOrEmpty(fileName))
            {
                string candidate2 = Path.Combine(dir, fileName);
                if (File.Exists(candidate2)) return candidate2;
            }
        }

        return book.FilePath;
    }

    /// <summary>
    /// 從書庫移除書籍時，同步清理書庫管理目錄內的託管副本檔案（不動到使用者外部其他資料夾的檔案）
    /// </summary>
    public void DeleteManagedBookFile(BookItem book)
    {
        try
        {
            string resolved = ResolveBookFilePath(book);
            if (!File.Exists(resolved)) return;

            string activeDir = GetActiveBooksDirectory();
            if (IsSubPathOf(activeDir, resolved) || IsSubPathOf(_defaultBooksDir, resolved))
            {
                File.Delete(resolved);
            }
        }
        catch
        {
            // Ignore file deletion lock
        }
    }

    public static string ComputeFileSha256(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            byte[] hashBytes = SHA256.HashData(stream);
            return Convert.ToHexString(hashBytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsSubPathOf(string baseDirPath, string candidatePath)
    {
        string normBase = Path.GetFullPath(baseDirPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string normCandidate = Path.GetFullPath(candidatePath);
        return normCandidate.StartsWith(normBase, StringComparison.OrdinalIgnoreCase);
    }

    public void LoadAll()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                string json = File.ReadAllText(_settingsPath);
                Settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch
        {
            Settings = new AppSettings();
        }

        try
        {
            if (File.Exists(_libraryPath))
            {
                string json = File.ReadAllText(_libraryPath);
                Library = JsonSerializer.Deserialize<List<BookItem>>(json, JsonOptions) ?? new List<BookItem>();
            }
        }
        catch
        {
            Library = new List<BookItem>();
        }

        // 自動將舊版尚未複製進書庫目錄的書籍遷移備份至託管書庫目錄
        MigrateExistingBooksToManagedStorage();
    }

    private void MigrateExistingBooksToManagedStorage()
    {
        bool changed = false;
        string activeDir = GetActiveBooksDirectory();

        foreach (var book in Library)
        {
            string resolved = ResolveBookFilePath(book);
            if (!File.Exists(resolved)) continue;

            if (!IsSubPathOf(activeDir, resolved) && !IsSubPathOf(_defaultBooksDir, resolved))
            {
                try
                {
                    var (storedPath, _, hash) = CopyBookToManagedStorage(resolved, book.Id);
                    book.FilePath = storedPath;
                    if (string.IsNullOrEmpty(book.ContentHash))
                    {
                        book.ContentHash = hash;
                    }
                    changed = true;
                }
                catch
                {
                    // Keep existing path if migration fails
                }
            }
            else if (string.IsNullOrEmpty(book.ContentHash))
            {
                book.ContentHash = ComputeFileSha256(resolved);
                changed = true;
            }
        }

        if (changed)
        {
            SaveImmediately();
        }
    }

    /// <summary>
    /// 500ms 防抖非同步存檔，避免頻繁滾動時阻塞 UI 執行緒
    /// </summary>
    public void ScheduleSave(int delayMs = 500)
    {
        _debounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs, cts.Token);
                if (!cts.Token.IsCancellationRequested)
                {
                    SaveImmediately();
                }
            }
            catch (TaskCanceledException)
            {
                // Ignored on debounce reset
            }
        });
    }

    /// <summary>
    /// 老闆鍵觸發、切換書籍或關閉程式時立即同步落盤
    /// </summary>
    public void SaveImmediately()
    {
        lock (_saveLock)
        {
            try
            {
                string settingsJson = JsonSerializer.Serialize(Settings, JsonOptions);
                File.WriteAllText(_settingsPath, settingsJson);

                string libraryJson = JsonSerializer.Serialize(Library, JsonOptions);
                File.WriteAllText(_libraryPath, libraryJson);
            }
            catch
            {
                // Ignore disk write contention in stealth mode
            }
        }
    }
}
