namespace StealthEpubReader.Models;

public class AppSettings
{
    // 字型與排版設定
    public string FontFamily { get; set; } = "Microsoft JhengHei UI";
    public double FontSize { get; set; } = 15.0;
    public double LineHeightMultiplier { get; set; } = 1.65;
    public double HorizontalPadding { get; set; } = 14.0;

    // 顏色與雙層獨立透明度
    public string BackgroundHex { get; set; } = "#1E1E1E";
    public string TextHex { get; set; } = "#7A9E65";
    public double BackgroundOpacity { get; set; } = 0.85; // 0.0 (全透明) ~ 1.0
    public double TextOpacity { get; set; } = 0.88;       // 0.1 ~ 1.0

    // 防抓包與隱蔽設定
    public bool AutoHideOnMouseLeave { get; set; } = false;
    public bool Topmost { get; set; } = true;
    public bool HideFromAltTab { get; set; } = true;
    public bool ExcludeFromCapture { get; set; } = true; // 防截圖與防螢幕分享 (WDA_EXCLUDEFROMCAPTURE)
    public string BossKeyGesture { get; set; } = "Alt+Q";
    public string BossKeyAction { get; set; } = "HideWindow"; // "HideWindow" 或 "FakeException"
    public string CamouflageMode { get; set; } = "None";      // "None", "SystemLog", "CSharpCode"

    // 閱讀與自動巡航控制設定
    public double ScrollStepPixels { get; set; } = 48.0;
    public double AutoScrollSpeed { get; set; } = 22.0; // 每秒自動捲動像素數
    public bool ShowMiniStatusBar { get; set; } = true;

    // 書庫存放目錄設定（空白代表預設程式目錄下 data/books）
    public string CustomBooksDirectory { get; set; } = string.Empty;

    // 視窗位置與尺寸記憶
    public double WindowLeft { get; set; } = 160.0;
    public double WindowTop { get; set; } = 540.0;
    public double WindowWidth { get; set; } = 680.0;
    public double WindowHeight { get; set; } = 220.0;

    // 上次開啟的書籍 ID
    public string? LastOpenedBookId { get; set; }
}
