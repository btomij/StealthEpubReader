using System.Windows.Markup;
using System.Windows.Media;
using StealthEpubReader.Models;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using FontFamily = System.Windows.Media.FontFamily;

namespace StealthEpubReader.Services;

public static class ThemeAndFontService
{
    public static List<ThemePreset> GetStealthPresets() => new()
    {
        new ThemePreset
        {
            Name = "IDE 註解綠 (VS Code 深色)",
            Description = "深灰底配程式碼註解綠，遠看完全像 VS Code 註解",
            BackgroundHex = "#1E1E1E",
            TextHex = "#6A9955",
            BackgroundOpacity = 0.90,
            TextOpacity = 0.90,
            RecommendedFont = "Consolas"
        },
        new ThemePreset
        {
            Name = "IDE 終端機暗灰 (深色低調)",
            Description = "極低對比度深灰配色，像背景執行中的 Log 輸出",
            BackgroundHex = "#181818",
            TextHex = "#808080",
            BackgroundOpacity = 0.85,
            TextOpacity = 0.82,
            RecommendedFont = "Consolas"
        },
        new ThemePreset
        {
            Name = "Office 報表灰 (Excel/Word 淺色)",
            Description = "白底配淺灰字，貼在 Excel、Word 或網頁上毫不突兀",
            BackgroundHex = "#FFFFFF",
            TextHex = "#595959",
            BackgroundOpacity = 0.92,
            TextOpacity = 0.88,
            RecommendedFont = "Microsoft JhengHei UI"
        },
        new ThemePreset
        {
            Name = "幽靈全透明 (深色背景懸浮)",
            Description = "背景 0% 完全透明，僅留淡灰文字懸浮於深色視窗上",
            BackgroundHex = "#1E1E1E",
            TextHex = "#8C8C8C",
            BackgroundOpacity = 0.0,
            TextOpacity = 0.78
        },
        new ThemePreset
        {
            Name = "幽靈全透明 (淺色背景懸浮)",
            Description = "背景 0% 完全透明，僅留深灰文字懸浮於白色文件上",
            BackgroundHex = "#FFFFFF",
            TextHex = "#454545",
            BackgroundOpacity = 0.0,
            TextOpacity = 0.80
        },
        new ThemePreset
        {
            Name = "護眼羊皮紙 (一般舒適閱讀)",
            Description = "柔和暖色背景，適合午休或無需偽裝時舒適閱讀",
            BackgroundHex = "#F4ECD8",
            TextHex = "#3B3228",
            BackgroundOpacity = 0.96,
            TextOpacity = 0.95
        }
    };

    /// <summary>
    /// 讀取 Windows 系統已安裝的所有字型，並將常用偽裝等寬字型與中文字型置頂
    /// </summary>
    public static List<string> GetSystemFontNames()
    {
        var pinnedFonts = new[]
        {
            "Microsoft JhengHei UI",
            "Microsoft JhengHei",
            "Consolas",
            "Cascadia Mono",
            "Cascadia Code",
            "PMingLiU",
            "MingLiU",
            "DFKai-SB",
            "Segoe UI",
            "Arial"
        };

        var allFonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var zhTwLang = XmlLanguage.GetLanguage("zh-tw");
        var enUsLang = XmlLanguage.GetLanguage("en-us");

        foreach (FontFamily family in Fonts.SystemFontFamilies)
        {
            if (family.FamilyNames.TryGetValue(zhTwLang, out string? zhName) && !string.IsNullOrWhiteSpace(zhName))
            {
                allFonts.Add(zhName);
            }
            if (family.FamilyNames.TryGetValue(enUsLang, out string? enName) && !string.IsNullOrWhiteSpace(enName))
            {
                allFonts.Add(enName);
            }
            else if (!string.IsNullOrWhiteSpace(family.Source))
            {
                allFonts.Add(family.Source);
            }
        }

        var result = new List<string>();
        foreach (string pinned in pinnedFonts)
        {
            if (allFonts.Contains(pinned))
            {
                result.Add(pinned);
                allFonts.Remove(pinned);
            }
        }

        result.AddRange(allFonts.OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase));
        return result;
    }

    /// <summary>
    /// 建立背景筆刷（當透明度為 0% 時保留 Alpha=2 以確保滑鼠滾輪與懸停事件不穿透）
    /// </summary>
    public static SolidColorBrush CreateBackgroundBrush(string hex, double opacity)
    {
        Color baseColor = ParseColorOrDefault(hex, Color.FromRgb(30, 30, 30));
        double clamped = Math.Clamp(opacity, 0.0, 1.0);
        // Alpha=2 肉眼完全透明，但讓 WPF 視窗仍能攔截滾輪與滑鼠進出事件
        byte alpha = clamped <= 0.005 ? (byte)2 : (byte)Math.Round(clamped * 255);
        var brush = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// 建立文字筆刷（獨立於背景透明度）
    /// </summary>
    public static SolidColorBrush CreateTextBrush(string hex, double opacity)
    {
        Color baseColor = ParseColorOrDefault(hex, Color.FromRgb(106, 153, 85));
        double clamped = Math.Clamp(opacity, 0.05, 1.0);
        byte alpha = (byte)Math.Round(clamped * 255);
        var brush = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
        brush.Freeze();
        return brush;
    }

    public static Color ParseColorOrDefault(string hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try
        {
            string normalized = hex.Trim();
            if (!normalized.StartsWith('#'))
            {
                normalized = "#" + normalized;
            }
            if (ColorConverter.ConvertFromString(normalized) is Color c)
            {
                return c;
            }
        }
        catch
        {
            // Return fallback on invalid hex input
        }
        return fallback;
    }
}
