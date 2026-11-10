using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Threading;

namespace StealthEpubReader.Services;

public class Win32StealthService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WH_KEYBOARD_LL = 13;

    private const int HOTKEY_BOSS_ID = 0x9527;
    private const int HOTKEY_GHOST_ID = 0x9528; // Alt+G: 切換滑鼠穿透
    private const int HOTKEY_GHOST_F8_ID = 0x952B; // F8: 備用切換滑鼠穿透
    private const int HOTKEY_NEXT_ID = 0x9529;  // Alt+J: 全域無焦點向下滾動
    private const int HOTKEY_PREV_ID = 0x952A;  // Alt+K: 全域無焦點向上滾動

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private const uint WDA_NONE = 0x00000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_NOREPEAT = 0x4000;

    private const int VK_MENU = 0x12;    // Alt
    private const int VK_CONTROL = 0x11; // Ctrl
    private const int VK_G = 0x47;
    private const int VK_J = 0x4A;
    private const int VK_K = 0x4B;
    private const int VK_F8 = 0x77;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    /// <summary>
    /// 獨立於 WPF 主視窗的隱藏訊息視窗，確保主視窗切換 WS_EX_TRANSPARENT 或 Hide() 時絕不影響 WM_HOTKEY 接收
    /// </summary>
    private sealed class HotkeyMessageWindow : NativeWindow, IDisposable
    {
        private readonly Win32StealthService _owner;

        public HotkeyMessageWindow(Win32StealthService owner)
        {
            _owner = owner;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                _owner.HandleHotkeyId(m.WParam.ToInt32());
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                DestroyHandle();
            }
        }
    }

    private IntPtr _windowHandle;
    private HotkeyMessageWindow? _hotkeyWindow;
    private IntPtr _keyboardHookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _keyboardHookProc;
    private DispatcherTimer? _keyPollTimer;

    private NotifyIcon? _notifyIcon;
    private Icon? _generatedTrayIcon;

    private uint _bossModifiers = MOD_ALT;
    private uint _bossVk = 0x51; // 'Q'

    private bool _prevBossKeyDown;
    private bool _prevGhostGDown;
    private bool _prevGhostF8Down;
    private bool _prevScrollDownKey;
    private bool _prevScrollUpKey;

    private long _lastBossTriggerTick;
    private long _lastGhostTriggerTick;
    private long _lastScrollTriggerTick;

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

        _hotkeyWindow = new HotkeyMessageWindow(this);

        SetHideFromAltTab(hideFromAltTab);
        SetExcludeFromCapture(excludeFromCapture);
        RegisterBossKey(bossKeyGesture);
        RegisterAuxiliaryHotKeys();
        InstallLowLevelKeyboardHook();
        StartHardwareKeyPolling();
        InitializeSystemTray();
    }

    public bool RegisterBossKey(string gesture)
    {
        var (modifiers, vk) = ParseBossKeyGesture(gesture);
        _bossModifiers = modifiers;
        _bossVk = vk;

        IntPtr targetHwnd = _hotkeyWindow?.Handle ?? _windowHandle;
        if (targetHwnd == IntPtr.Zero) return false;

        UnregisterHotKey(targetHwnd, HOTKEY_BOSS_ID);
        // 不加 MOD_NOREPEAT 的相容性限制，改由統一毫秒級防抖控制，確保按住 Alt 再連按也能穩定觸發
        return RegisterHotKey(targetHwnd, HOTKEY_BOSS_ID, modifiers, vk);
    }

    private void RegisterAuxiliaryHotKeys()
    {
        IntPtr targetHwnd = _hotkeyWindow?.Handle ?? _windowHandle;
        if (targetHwnd == IntPtr.Zero) return;

        RegisterHotKey(targetHwnd, HOTKEY_GHOST_ID, MOD_ALT, VK_G);
        RegisterHotKey(targetHwnd, HOTKEY_GHOST_F8_ID, 0, VK_F8);
        RegisterHotKey(targetHwnd, HOTKEY_NEXT_ID, MOD_ALT, VK_J);
        RegisterHotKey(targetHwnd, HOTKEY_PREV_ID, MOD_ALT, VK_K);
    }

    private void InstallLowLevelKeyboardHook()
    {
        try
        {
            _keyboardHookProc = LowLevelKeyboardCallback;
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            IntPtr hMod = GetModuleHandle(curModule?.ModuleName);
            _keyboardHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardHookProc, hMod, 0);
        }
        catch
        {
            // 若環境限制 Hook，仍有 RegisterHotKey 與 GetAsyncKeyState 雙重備援
        }
    }

    private IntPtr LowLevelKeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            uint vk = hookStruct.vkCode;

            bool altDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
            bool ctrlDown = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;

            // 1. 檢查 Alt+G 或 F8 切換滑鼠穿透
            if ((altDown && !ctrlDown && vk == VK_G) || (!altDown && !ctrlDown && vk == VK_F8))
            {
                TriggerGhostToggleDebounced();
            }
            // 2. 檢查老闆鍵
            else if (IsBossKeyMatch(vk, altDown, ctrlDown))
            {
                TriggerBossKeyDebounced();
            }
            // 3. 檢查 Alt+J / Alt+K 全域滾動
            else if (altDown && !ctrlDown && vk == VK_J)
            {
                TriggerScrollDebounced(down: true);
            }
            else if (altDown && !ctrlDown && vk == VK_K)
            {
                TriggerScrollDebounced(down: false);
            }
        }

        return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
    }

    /// <summary>
    /// 25ms 硬體按鍵輪詢（第三重保障：即使焦點在其他視窗、或系統攔截 WM_HOTKEY、或按住 Alt 不放再按一次 G，皆能 100% 偵測）
    /// </summary>
    private void StartHardwareKeyPolling()
    {
        _keyPollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(25)
        };
        _keyPollTimer.Tick += (_, _) => PollHardwareKeys();
        _keyPollTimer.Start();
    }

    private void PollHardwareKeys()
    {
        bool altDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
        bool ctrlDown = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;

        // 1. 偵測 Alt+G（僅追蹤 G 鍵的上升沿，因此按住 Alt 不放連續按 G 也能每次切換！）
        bool gDown = (GetAsyncKeyState(VK_G) & 0x8000) != 0;
        if (altDown && !ctrlDown && gDown && !_prevGhostGDown)
        {
            TriggerGhostToggleDebounced();
        }
        _prevGhostGDown = gDown;

        // 備用單鍵 F8 切換穿透
        bool f8Down = (GetAsyncKeyState(VK_F8) & 0x8000) != 0;
        if (!altDown && !ctrlDown && f8Down && !_prevGhostF8Down)
        {
            TriggerGhostToggleDebounced();
        }
        _prevGhostF8Down = f8Down;

        // 2. 偵測老闆鍵
        bool bossVkDown = (GetAsyncKeyState((int)_bossVk) & 0x8000) != 0;
        bool bossModMatch = _bossModifiers switch
        {
            MOD_ALT => altDown && !ctrlDown,
            MOD_CONTROL => ctrlDown && !altDown,
            _ => !altDown && !ctrlDown
        };
        if (bossModMatch && bossVkDown && !_prevBossKeyDown)
        {
            TriggerBossKeyDebounced();
        }
        _prevBossKeyDown = bossVkDown;

        // 3. 偵測 Alt+J / Alt+K 全域滾動
        bool jDown = (GetAsyncKeyState(VK_J) & 0x8000) != 0;
        if (altDown && !ctrlDown && jDown && !_prevScrollDownKey)
        {
            TriggerScrollDebounced(down: true);
        }
        _prevScrollDownKey = jDown;

        bool kDown = (GetAsyncKeyState(VK_K) & 0x8000) != 0;
        if (altDown && !ctrlDown && kDown && !_prevScrollUpKey)
        {
            TriggerScrollDebounced(down: false);
        }
        _prevScrollUpKey = kDown;
    }

    private bool IsBossKeyMatch(uint vk, bool altDown, bool ctrlDown)
    {
        if (vk != _bossVk) return false;
        return _bossModifiers switch
        {
            MOD_ALT => altDown && !ctrlDown,
            MOD_CONTROL => ctrlDown && !altDown,
            _ => !altDown && !ctrlDown
        };
    }

    private void HandleHotkeyId(int id)
    {
        switch (id)
        {
            case HOTKEY_BOSS_ID:
                TriggerBossKeyDebounced();
                break;
            case HOTKEY_GHOST_ID:
            case HOTKEY_GHOST_F8_ID:
                TriggerGhostToggleDebounced();
                break;
            case HOTKEY_NEXT_ID:
                TriggerScrollDebounced(down: true);
                break;
            case HOTKEY_PREV_ID:
                TriggerScrollDebounced(down: false);
                break;
        }
    }

    public void TriggerGhostToggleDebounced()
    {
        long now = Environment.TickCount64;
        if (now - _lastGhostTriggerTick < 260) return;
        _lastGhostTriggerTick = now;
        GhostModeToggleTriggered?.Invoke();
    }

    public void TriggerBossKeyDebounced()
    {
        long now = Environment.TickCount64;
        if (now - _lastBossTriggerTick < 260) return;
        _lastBossTriggerTick = now;
        BossKeyTriggered?.Invoke();
    }

    private void TriggerScrollDebounced(bool down)
    {
        long now = Environment.TickCount64;
        if (now - _lastScrollTriggerTick < 55) return;
        _lastScrollTriggerTick = now;

        if (down)
            GlobalScrollDownTriggered?.Invoke();
        else
            GlobalScrollUpTriggered?.Invoke();
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

    public bool SetExcludeFromCapture(bool exclude)
    {
        if (_windowHandle == IntPtr.Zero) return false;
        return SetWindowDisplayAffinity(_windowHandle, exclude ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
    }

    /// <summary>
    /// 滑鼠點擊穿透幽靈模式 (WS_EX_TRANSPARENT) 並透過 SWP_FRAMECHANGED 強制刷新 DWM 快取
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

        // 強制 Windows DWM 立即刷新視窗點擊穿透狀態快取，確保切換回來時滑鼠可立即點擊與滾動
        SetWindowPos(
            _windowHandle,
            IntPtr.Zero,
            0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
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
        SetWindowPos(
            _windowHandle,
            IntPtr.Zero,
            0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    private void InitializeSystemTray()
    {
        _generatedTrayIcon = CreateLowProfileTrayIcon();

        var menu = new ContextMenuStrip();
        menu.Items.Add("顯示 / 隱藏閱讀器 (老闆鍵 Alt+Q)", null, (_, _) => TrayToggleVisibilityRequested?.Invoke());
        menu.Items.Add("切換滑鼠穿透幽靈模式 (Alt+G / F8)", null, (_, _) => TriggerGhostToggleDebounced());
        menu.Items.Add("開啟極簡圖書館 / 搜尋 / 書籤 (Ctrl+B)", null, (_, _) => TrayOpenLibraryRequested?.Invoke());
        menu.Items.Add("外觀、字型與防抓包設定 (Ctrl+,)", null, (_, _) => TrayOpenSettingsRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("儲存並結束", null, (_, _) => TrayExitRequested?.Invoke());

        _notifyIcon = new NotifyIcon
        {
            Icon = _generatedTrayIcon,
            Text = "System Background Host (Alt+Q / Alt+G)",
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

    public void Dispose()
    {
        _keyPollTimer?.Stop();

        if (_keyboardHookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHookId);
            _keyboardHookId = IntPtr.Zero;
        }

        IntPtr targetHwnd = _hotkeyWindow?.Handle ?? _windowHandle;
        if (targetHwnd != IntPtr.Zero)
        {
            UnregisterHotKey(targetHwnd, HOTKEY_BOSS_ID);
            UnregisterHotKey(targetHwnd, HOTKEY_GHOST_ID);
            UnregisterHotKey(targetHwnd, HOTKEY_GHOST_F8_ID);
            UnregisterHotKey(targetHwnd, HOTKEY_NEXT_ID);
            UnregisterHotKey(targetHwnd, HOTKEY_PREV_ID);
        }

        _hotkeyWindow?.Dispose();
        _hotkeyWindow = null;

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
        _generatedTrayIcon?.Dispose();
    }
}
