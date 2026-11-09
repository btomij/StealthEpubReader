using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using StealthEpubReader.Models;

namespace StealthEpubReader.Services;

public class StorageService
{
    private readonly string _dataDir;
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

    public StorageService()
    {
        _dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
        Directory.CreateDirectory(_dataDir);
        _settingsPath = Path.Combine(_dataDir, "settings.json");
        _libraryPath = Path.Combine(_dataDir, "library.json");
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
