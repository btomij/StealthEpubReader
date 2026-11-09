using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;

namespace StealthEpubReader.Services;

public class Win32StealthService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 0x9527;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private IntPtr _windowHandle;
    private HwndSource? _hwndSource;
    private NotifyIcon? _notifyIcon;
    private Icon? _generatedTrayIcon;

    public event Action? BossKeyTriggered;
    public event Action? TrayToggleVisibilityRequested;
    public event Action? TrayOpenLibraryRequested;
    public event Action? TrayOpenSettingsRequested;
    public event Action? TrayExitRequested;

    public static IReadOnlyList<string> SupportedBossKeys { get; } = new[]
    {
        "Alt+Q",
        "Alt+W",
        "Alt+Z",
        "Alt+Space",
        "Ctrl+Space",
        "F9"
    };

    public void Initialize(Window window, string bossKeyGesture, bool hideFromAltTab)
    {
        var helper = new WindowInteropHelper(window);
        _windowHandle = helper.EnsureHandle();
        _hwndSource = HwndSource.FromHwnd(_windowHandle);
        _hwndSource?.AddHook(WndProc);

        SetHideFromAltTab(hideFromAltTab);
        RegisterBossKey(bossKeyGesture);
        InitializeSystemTray();
    }

    public bool RegisterBossKey(string gesture)
    {
        if (_windowHandle == IntPtr.Zero) return false;

        UnregisterHotKey(_windowHandle, HOTKEY_ID);

        var (modifiers, vk) = ParseBossKeyGesture(gesture);
        return RegisterHotKey(_windowHandle, HOTKEY_ID, modifiers | MOD_NOREPEAT, vk);
    }

    private static (uint Modifiers, uint Vk) ParseBossKeyGesture(string gesture)
    {
        return gesture switch
        {
            "Alt+W" => (MOD_ALT, 0x57),        // 'W'
            "Alt+Z" => (MOD_ALT, 0x5A),        // 'Z'
            "Alt+Space" => (MOD_ALT, 0x20),    // VK_SPACE
            "Ctrl+Space" => (MOD_CONTROL, 0x20),
            "F9" => (0, 0x78),                 // VK_F9
            _ => (MOD_ALT, 0x51)               // Default: Alt+Q ('Q')
        };
    }

    public void SetHideFromAltTab(bool hide)
    {
        if (_windowHandle == IntPtr.Zero) return;

        int exStyle = GetWindowLong(_windowHandle, GWL_EXSTYLE);
        if (hide)
        {
            exStyle |= WS_EX_TOOLWINDOW;
        }
        else
        {
            exStyle &= ~WS_EX_TOOLWINDOW;
        }
        SetWindowLong(_windowHandle, GWL_EXSTYLE, exStyle);
    }

    private void InitializeSystemTray()
    {
        _generatedTrayIcon = CreateLowProfileTrayIcon();

        var menu = new ContextMenuStrip();
        menu.Items.Add("顯示 / 隱藏閱讀器 (老闆鍵)", null, (_, _) => TrayToggleVisibilityRequested?.Invoke());
        menu.Items.Add("開啟極簡圖書館 (Ctrl+B)", null, (_, _) => TrayOpenLibraryRequested?.Invoke());
        menu.Items.Add("外觀與字型設定 (Ctrl+,)", null, (_, _) => TrayOpenSettingsRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("儲存並結束", null, (_, _) => TrayExitRequested?.Invoke());

        _notifyIcon = new NotifyIcon
        {
            Icon = _generatedTrayIcon,
            Text = "System Background Host (Alt+Q)", // 低調偽裝提示名稱
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                TrayToggleVisibilityRequested?.Invoke();
            }
        };
    }

    /// <summary>
    /// 動態繪製一個極低調的深灰色終端機小圖示，避免系統匣出現顯眼的書本圖案
    /// </summary>
    private static Icon CreateLowProfileTrayIcon()
    {
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);

        using var bgBrush = new SolidBrush(Color.FromArgb(220, 45, 48, 52));
        using var borderPen = new Pen(Color.FromArgb(220, 110, 115, 120), 1);
        using var textBrush = new SolidBrush(Color.FromArgb(240, 130, 175, 110));

        g.FillRectangle(bgBrush, 1, 2, 13, 11);
        g.DrawRectangle(borderPen, 1, 2, 13, 11);
        // 繪製類似 ">_" 的終端機符號
        g.FillRectangle(textBrush, 4, 6, 3, 2);
        g.FillRectangle(textBrush, 8, 9, 4, 2);

        IntPtr hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            handled = true;
            BossKeyTriggered?.Invoke();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_windowHandle != IntPtr.Zero)
        {
            UnregisterHotKey(_windowHandle, HOTKEY_ID);
        }
        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
        }
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
        _generatedTrayIcon?.Dispose();
    }
}
