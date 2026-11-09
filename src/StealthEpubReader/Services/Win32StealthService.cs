using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;

namespace StealthEpubReader.Services;

public class Win32StealthService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    private const int HOTKEY_BOSS_ID = 0x9527;
    private const int HOTKEY_GHOST_ID = 0x9528; // Alt+G: 切換滑鼠穿透
    private const int HOTKEY_NEXT_ID = 0x9529;  // Alt+J: 全域無焦點向下滾動
    private const int HOTKEY_PREV_ID = 0x952A;  // Alt+K: 全域無焦點向上滾動

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;

    private const uint WDA_NONE = 0x00000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    private IntPtr _windowHandle;
    private HwndSource? _hwndSource;
    private NotifyIcon? _notifyIcon;
    private Icon? _generatedTrayIcon;

    public bool IsClickThroughEnabled { get; private set; }

    public event Action? BossKeyTriggered;
    public event Action? GhostModeToggleTriggered;
    public event Action? GlobalScrollDownTriggered;
    public event Action? GlobalScrollUpTriggered;

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

    public void Initialize(Window window, string bossKeyGesture, bool hideFromAltTab, bool excludeFromCapture)
    {
        var helper = new WindowInteropHelper(window);
        _windowHandle = helper.EnsureHandle();
        _hwndSource = HwndSource.FromHwnd(_windowHandle);
        _hwndSource?.AddHook(WndProc);

        SetHideFromAltTab(hideFromAltTab);
        SetExcludeFromCapture(excludeFromCapture);
        RegisterBossKey(bossKeyGesture);
        RegisterAuxiliaryHotKeys();
        InitializeSystemTray();
    }

    public bool RegisterBossKey(string gesture)
    {
        if (_windowHandle == IntPtr.Zero) return false;

        UnregisterHotKey(_windowHandle, HOTKEY_BOSS_ID);

        var (modifiers, vk) = ParseBossKeyGesture(gesture);
        return RegisterHotKey(_windowHandle, HOTKEY_BOSS_ID, modifiers | MOD_NOREPEAT, vk);
    }

    private void RegisterAuxiliaryHotKeys()
    {
        if (_windowHandle == IntPtr.Zero) return;

        // Alt+G (0x47): 切換滑鼠穿透幽靈模式
        RegisterHotKey(_windowHandle, HOTKEY_GHOST_ID, MOD_ALT | MOD_NOREPEAT, 0x47);
        // Alt+J (0x4A): 全域無焦點向下滾動
        RegisterHotKey(_windowHandle, HOTKEY_NEXT_ID, MOD_ALT, 0x4A);
        // Alt+K (0x4B): 全域無焦點向上滾動
        RegisterHotKey(_windowHandle, HOTKEY_PREV_ID, MOD_ALT, 0x4B);
    }

    private static (uint Modifiers, uint Vk) ParseBossKeyGesture(string gesture)
    {
        return gesture switch
        {
            "Alt+W" => (MOD_ALT, 0x57),
            "Alt+Z" => (MOD_ALT, 0x5A),
            "Alt+Space" => (MOD_ALT, 0x20),
            "Ctrl+Space" => (MOD_CONTROL, 0x20),
            "F9" => (0, 0x78),
            _ => (MOD_ALT, 0x51) // Default: Alt+Q ('Q')
        };
    }

    /// <summary>
    /// 防截圖與防螢幕分享曝光 (WDA_EXCLUDEFROMCAPTURE)
    /// 開啟後，Teams / Zoom / OBS / 截圖工具將完全拍不到此視窗！
    /// </summary>
    public bool SetExcludeFromCapture(bool exclude)
    {
        if (_windowHandle == IntPtr.Zero) return false;
        return SetWindowDisplayAffinity(_windowHandle, exclude ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
    }

    /// <summary>
    /// 滑鼠點擊穿透幽靈模式 (WS_EX_TRANSPARENT)
    /// 開啟後滑鼠點擊與滾輪直接穿透到底層 Excel / VS Code
    /// </summary>
    public void SetClickThrough(bool enable)
    {
        if (_windowHandle == IntPtr.Zero) return;
        IsClickThroughEnabled = enable;

        int exStyle = GetWindowLong(_windowHandle, GWL_EXSTYLE);
        if (enable)
        {
            exStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
        }
        else
        {
            exStyle &= ~WS_EX_TRANSPARENT;
        }
        SetWindowLong(_windowHandle, GWL_EXSTYLE, exStyle);
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
        menu.Items.Add("顯示 / 隱藏閱讀器 (老闆鍵 Alt+Q)", null, (_, _) => TrayToggleVisibilityRequested?.Invoke());
        menu.Items.Add("切換滑鼠穿透幽靈模式 (Alt+G)", null, (_, _) => GhostModeToggleTriggered?.Invoke());
        menu.Items.Add("開啟極簡圖書館 / 搜尋 / 書籤 (Ctrl+B)", null, (_, _) => TrayOpenLibraryRequested?.Invoke());
        menu.Items.Add("外觀、字型與防抓包設定 (Ctrl+,)", null, (_, _) => TrayOpenSettingsRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("儲存並結束", null, (_, _) => TrayExitRequested?.Invoke());

        _notifyIcon = new NotifyIcon
        {
            Icon = _generatedTrayIcon,
            Text = "System Background Host (Alt+Q)",
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
        g.FillRectangle(textBrush, 4, 6, 3, 2);
        g.FillRectangle(textBrush, 8, 9, 4, 2);

        IntPtr hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HOTKEY_BOSS_ID)
            {
                handled = true;
                BossKeyTriggered?.Invoke();
            }
            else if (id == HOTKEY_GHOST_ID)
            {
                handled = true;
                GhostModeToggleTriggered?.Invoke();
            }
            else if (id == HOTKEY_NEXT_ID)
            {
                handled = true;
                GlobalScrollDownTriggered?.Invoke();
            }
            else if (id == HOTKEY_PREV_ID)
            {
                handled = true;
                GlobalScrollUpTriggered?.Invoke();
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_windowHandle != IntPtr.Zero)
        {
            UnregisterHotKey(_windowHandle, HOTKEY_BOSS_ID);
            UnregisterHotKey(_windowHandle, HOTKEY_GHOST_ID);
            UnregisterHotKey(_windowHandle, HOTKEY_NEXT_ID);
            UnregisterHotKey(_windowHandle, HOTKEY_PREV_ID);
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
