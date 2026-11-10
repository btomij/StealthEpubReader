using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using StealthEpubReader.Models;
using StealthEpubReader.Services;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using FontFamily = System.Windows.Media.FontFamily;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace StealthEpubReader;

public partial class MainWindow : Window
{
    private enum DrawerTab
    {
        Books,
        Chapters,
        Bookmarks,
        Search
    }

    private readonly StorageService _storage = new();
    private readonly EpubEngineService _epubEngine = new();
    private readonly Win32StealthService _win32Stealth = new();
    private readonly DispatcherTimer _autoScrollTimer;

    private BookItem? _currentBook;
    private ChapterContent? _currentChapter;
    private DrawerTab _activeDrawerTab = DrawerTab.Books;

    private bool _isInitializing = true;
    private bool _isRestoringScroll = false;
    private bool _isPanicFakeScreenActive = false;
    private double _savedPanicScrollOffset = 0.0;
    private double? _savedCompactHeight;

    private static readonly SolidColorBrush HitTestInvisibleBrush = CreateFrozenBrush(Color.FromArgb(2, 0, 0, 0));
    private static readonly SolidColorBrush SubtleBorderBrush = CreateFrozenBrush(Color.FromArgb(36, 136, 136, 136));

    public MainWindow()
    {
        InitializeComponent();

        _autoScrollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _autoScrollTimer.Tick += AutoScrollTimer_Tick;

        Loaded += MainWindow_Loaded;
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _storage.LoadAll();
        RestoreWindowBounds();

        // 初始化 Win32 全域老闆鍵、滑鼠穿透熱鍵、全域滾動與低調系統匣
        _win32Stealth.BossKeyTriggered += () => Dispatcher.Invoke(TriggerBossKeyAction);
        _win32Stealth.GhostModeToggleTriggered += () => Dispatcher.Invoke(ToggleClickThroughGhostMode);
        _win32Stealth.GlobalScrollDownTriggered += () => Dispatcher.Invoke(async () => await ScrollStepOrNextChapterAsync(down: true));
        _win32Stealth.GlobalScrollUpTriggered += () => Dispatcher.Invoke(async () => await ScrollStepOrNextChapterAsync(down: false));

        _win32Stealth.TrayToggleVisibilityRequested += () => Dispatcher.Invoke(TriggerBossKeyAction);
        _win32Stealth.TrayOpenLibraryRequested += () => Dispatcher.Invoke(() =>
        {
            EnsureWindowVisibleAndInteractive();
            OpenLibraryDrawer(DrawerTab.Books);
        });
        _win32Stealth.TrayOpenSettingsRequested += () => Dispatcher.Invoke(() =>
        {
            EnsureWindowVisibleAndInteractive();
            OpenSettingsDrawer();
        });
        _win32Stealth.TrayExitRequested += () => Dispatcher.Invoke(Close);

        _win32Stealth.Initialize(
            this,
            _storage.Settings.BossKeyGesture,
            _storage.Settings.HideFromAltTab,
            _storage.Settings.ExcludeFromCapture);

        InitializeSettingsControls();
        ApplyAppearanceToUI();

        _isInitializing = false;

        if (_storage.Library.Count == 0)
        {
            string samplePath = SampleEpubGenerator.EnsureSampleEpubCreated(_storage.DataDirectory);
            await ImportAndOpenBookAsync(samplePath, switchImmediately: true);
        }
        else
        {
            var targetBook = _storage.Library.FirstOrDefault(b => b.Id == _storage.Settings.LastOpenedBookId)
                             ?? _storage.Library.OrderByDescending(b => b.LastReadTime).First();

            string resolvedPath = _storage.ResolveBookFilePath(targetBook);
            if (File.Exists(resolvedPath))
            {
                await LoadBookAsync(targetBook);
            }
            else
            {
                string samplePath = SampleEpubGenerator.EnsureSampleEpubCreated(_storage.DataDirectory);
                await ImportAndOpenBookAsync(samplePath, switchImmediately: true);
            }
        }
    }

    private void RestoreWindowBounds()
    {
        var s = _storage.Settings;
        double screenW = SystemParameters.VirtualScreenWidth;
        double screenH = SystemParameters.VirtualScreenHeight;

        Width = Math.Clamp(s.WindowWidth, MinWidth, Math.Max(400, screenW));
        Height = Math.Clamp(s.WindowHeight, MinHeight, Math.Max(200, screenH));

        if (s.WindowLeft >= SystemParameters.VirtualScreenLeft &&
            s.WindowLeft + 80 <= SystemParameters.VirtualScreenLeft + screenW &&
            s.WindowTop >= SystemParameters.VirtualScreenTop &&
            s.WindowTop + 30 <= SystemParameters.VirtualScreenTop + screenH)
        {
            Left = s.WindowLeft;
            Top = s.WindowTop;
        }

        Topmost = s.Topmost;
        TopMicroBar.Visibility = s.ShowMiniStatusBar ? Visibility.Visible : Visibility.Collapsed;
    }

    private void InitializeSettingsControls()
    {
        var s = _storage.Settings;

        // 1. 建立保護色主題按鈕
        PresetButtonsPanel.Children.Clear();
        foreach (var preset in ThemeAndFontService.GetStealthPresets())
        {
            var btn = new Button
            {
                Style = (Style)FindResource("StealthBtn"),
                Content = preset.Name,
                ToolTip = preset.Description,
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 6, 4),
                Tag = preset
            };
            btn.Click += PresetButton_Click;
            PresetButtonsPanel.Children.Add(btn);
        }

        // 2. 載入系統字型清單
        var fonts = ThemeAndFontService.GetSystemFontNames();
        FontFamilyCombo.ItemsSource = fonts;
        FontFamilyCombo.SelectedItem = fonts.FirstOrDefault(f => f.Equals(s.FontFamily, StringComparison.OrdinalIgnoreCase))
                                       ?? fonts.FirstOrDefault();

        // 3. 載入滑桿與色碼
        FontSizeSlider.Value = Math.Clamp(s.FontSize, 10, 36);
        LineHeightSlider.Value = Math.Clamp(s.LineHeightMultiplier, 1.1, 2.6);
        AutoScrollSpeedSlider.Value = Math.Clamp(s.AutoScrollSpeed, 5, 120);
        BgOpacitySlider.Value = Math.Clamp(s.BackgroundOpacity, 0.0, 1.0);
        TextOpacitySlider.Value = Math.Clamp(s.TextOpacity, 0.10, 1.0);

        BgHexBox.Text = s.BackgroundHex;
        TextHexBox.Text = s.TextHex;

        // 4. 載入偽裝外殼、老闆鍵動作與防抓包選項
        SelectComboByTag(CamouflageModeCombo, s.CamouflageMode);
        SelectComboByTag(BossKeyActionCombo, s.BossKeyAction);

        BossKeyCombo.ItemsSource = Win32StealthService.SupportedBossKeys;
        BossKeyCombo.SelectedItem = Win32StealthService.SupportedBossKeys.Contains(s.BossKeyGesture)
            ? s.BossKeyGesture
            : "Alt+Q";

        ExcludeCaptureCheckBox.IsChecked = s.ExcludeFromCapture;
        AutoHideCheckBox.IsChecked = s.AutoHideOnMouseLeave;
        TopmostCheckBox.IsChecked = s.Topmost;
        HideAltTabCheckBox.IsChecked = s.HideFromAltTab;
        CustomBooksDirBox.Text = s.CustomBooksDirectory;

        UpdateTopBarStatusButtons();
    }

    private static void SelectComboByTag(System.Windows.Controls.ComboBox combo, string tagValue)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, tagValue, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private void ApplyAppearanceToUI()
    {
        var s = _storage.Settings;

        RootBorder.Background = ThemeAndFontService.CreateBackgroundBrush(s.BackgroundHex, s.BackgroundOpacity);
        RootBorder.BorderBrush = s.BackgroundOpacity <= 0.02
            ? System.Windows.Media.Brushes.Transparent
            : SubtleBorderBrush;

        var textBrush = ThemeAndFontService.CreateTextBrush(s.TextHex, s.TextOpacity);
        ReaderTextBlock.Foreground = textBrush;
        StatusTextBlock.Foreground = textBrush;

        try
        {
            ReaderTextBlock.FontFamily = new FontFamily(s.FontFamily);
            StatusTextBlock.FontFamily = new FontFamily(s.FontFamily);
        }
        catch
        {
            ReaderTextBlock.FontFamily = new FontFamily("Microsoft JhengHei UI");
        }

        ReaderTextBlock.FontSize = Math.Clamp(s.FontSize, 10, 36);
        ReaderTextBlock.LineHeight = Math.Max(12, Math.Round(ReaderTextBlock.FontSize * s.LineHeightMultiplier));
        ReaderTextBlock.Padding = new Thickness(s.HorizontalPadding, 6, s.HorizontalPadding, 14);

        if (FontSizeValueText != null) FontSizeValueText.Text = $"{s.FontSize:0}pt";
        if (LineHeightValueText != null) LineHeightValueText.Text = $"{s.LineHeightMultiplier:0.00}x";
        if (AutoScrollSpeedValueText != null) AutoScrollSpeedValueText.Text = $"{s.AutoScrollSpeed:0}px/s";
        if (BgOpacityValueText != null) BgOpacityValueText.Text = $"{Math.Round(s.BackgroundOpacity * 100)}%";
        if (TextOpacityValueText != null) TextOpacityValueText.Text = $"{Math.Round(s.TextOpacity * 100)}%";
    }

    private void UpdateTopBarStatusButtons()
    {
        var s = _storage.Settings;
        BtnAutoHideToggle.Content = s.AutoHideOnMouseLeave ? "自動隱形:開" : "自動隱形:關";
        BtnAutoScrollToggle.Content = _autoScrollTimer.IsEnabled ? "巡航:開" : "巡航:關";
        BtnGhostClickThrough.Content = _win32Stealth.IsClickThroughEnabled ? "穿透:開(Alt+G)" : "穿透";

        string camShort = s.CamouflageMode switch
        {
            "SystemLog" => "偽裝:Log",
            "CSharpCode" => "偽裝:C#",
            _ => "偽裝:無"
        };
        BtnCamouflageCycle.Content = camShort;
    }

    #region EPUB 載入、章節渲染（含偽裝外殼）與進度記憶

    public async Task ImportAndOpenBookAsync(string filePath, bool switchImmediately)
    {
        if (!File.Exists(filePath) || !filePath.EndsWith(".epub", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string fullSourcePath = Path.GetFullPath(filePath);
        string sourceHash = StorageService.ComputeFileSha256(fullSourcePath);

        // 以檔案內容 SHA-256 或解析後路徑去重
        var existing = _storage.Library.FirstOrDefault(b =>
            (!string.IsNullOrEmpty(sourceHash) && string.Equals(b.ContentHash, sourceHash, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(_storage.ResolveBookFilePath(b), fullSourcePath, StringComparison.OrdinalIgnoreCase));

        if (existing == null)
        {
            string bookId = Guid.NewGuid().ToString("N");
            // 先複製一份到受管理的本地/指定書庫目錄（以 {BookId}.epub 命名防曝光、防原始檔遺失）
            var (storedPath, resolvedFullPath, contentHash) = _storage.CopyBookToManagedStorage(fullSourcePath, bookId);
            var (title, author, totalChapters) = await _epubEngine.OpenBookAsync(resolvedFullPath);

            existing = new BookItem
            {
                Id = bookId,
                Title = title,
                Author = author,
                FilePath = storedPath,
                ContentHash = contentHash,
                TotalChapters = Math.Max(1, totalChapters),
                CurrentChapterIndex = 0,
                ScrollRatio = 0.0,
                VerticalOffset = 0.0,
                LastReadTime = DateTime.Now
            };
            _storage.Library.Insert(0, existing);
            _storage.SaveImmediately();
        }
        else if (!File.Exists(_storage.ResolveBookFilePath(existing)))
        {
            // 若書庫已有紀錄但託管檔案遺失，重新複製補齊
            var (storedPath, _, contentHash) = _storage.CopyBookToManagedStorage(fullSourcePath, existing.Id);
            existing.FilePath = storedPath;
            existing.ContentHash = contentHash;
            _storage.SaveImmediately();
        }

        if (switchImmediately)
        {
            await LoadBookAsync(existing);
        }
        else
        {
            RefreshLibraryList();
        }
    }

    private async Task LoadBookAsync(BookItem book)
    {
        try
        {
            SaveCurrentBookProgressImmediately();

            string resolvedPath = _storage.ResolveBookFilePath(book);
            var (title, author, totalChapters) = await _epubEngine.OpenBookAsync(resolvedPath);
            book.Title = title;
            book.Author = author;
            book.TotalChapters = Math.Max(1, totalChapters);
            book.LastReadTime = DateTime.Now;

            _currentBook = book;
            _storage.Settings.LastOpenedBookId = book.Id;

            await LoadChapterAsync(book.CurrentChapterIndex, book.ScrollRatio, book.VerticalOffset);
            _storage.SaveImmediately();
        }
        catch (Exception ex)
        {
            ReaderTextBlock.Text = $"無法開啟書籍：{ex.Message}\n請按 Ctrl+B 開啟書庫選擇其他書籍，或直接將 .epub 檔案拖曳至此視窗。";
        }
    }

    private async Task LoadChapterAsync(int chapterIndex, double targetScrollRatio = 0.0, double fallbackOffset = 0.0)
    {
        if (_currentBook == null || _epubEngine.TotalChapters == 0) return;

        _isPanicFakeScreenActive = false;
        chapterIndex = Math.Clamp(chapterIndex, 0, _epubEngine.TotalChapters - 1);
        _currentChapter = await _epubEngine.GetChapterAsync(chapterIndex);

        _isRestoringScroll = true;
        _currentBook.CurrentChapterIndex = _currentChapter.Index;
        _currentBook.CurrentChapterTitle = _currentChapter.Title;
        _currentBook.LastReadTime = DateTime.Now;

        ReaderTextBlock.Text = CamouflageService.FormatChapterContent(_currentChapter, _storage.Settings.CamouflageMode);
        ReaderScrollViewer.UpdateLayout();

        if (targetScrollRatio > 0.0 && ReaderScrollViewer.ScrollableHeight > 0)
        {
            ReaderScrollViewer.ScrollToVerticalOffset(targetScrollRatio * ReaderScrollViewer.ScrollableHeight);
        }
        else if (fallbackOffset > 0.0)
        {
            ReaderScrollViewer.ScrollToVerticalOffset(fallbackOffset);
        }
        else
        {
            ReaderScrollViewer.ScrollToTop();
        }

        _isRestoringScroll = false;
        UpdateReadingProgressAndStatus();
    }

    private void RefreshChapterCamouflageDisplay()
    {
        if (_currentChapter == null || _currentBook == null || _isPanicFakeScreenActive) return;

        double currentRatio = _currentBook.ScrollRatio;
        _isRestoringScroll = true;
        ReaderTextBlock.Text = CamouflageService.FormatChapterContent(_currentChapter, _storage.Settings.CamouflageMode);
        ReaderScrollViewer.UpdateLayout();

        if (currentRatio > 0.0 && ReaderScrollViewer.ScrollableHeight > 0)
        {
            ReaderScrollViewer.ScrollToVerticalOffset(currentRatio * ReaderScrollViewer.ScrollableHeight);
        }
        _isRestoringScroll = false;
        UpdateReadingProgressAndStatus();
    }

    private void UpdateReadingProgressAndStatus()
    {
        if (_currentBook == null || _isPanicFakeScreenActive) return;

        double scrollable = ReaderScrollViewer.ScrollableHeight;
        double offset = ReaderScrollViewer.VerticalOffset;
        double ratio = scrollable > 0.5 ? Math.Clamp(offset / scrollable, 0.0, 1.0) : 0.0;

        _currentBook.VerticalOffset = offset;
        _currentBook.ScrollRatio = ratio;
        _currentBook.LastReadTime = DateTime.Now;

        double chapterPct = Math.Round(ratio * 100.0, 0);
        string shieldTag = _storage.Settings.ExcludeFromCapture ? "[防截圖] " : "";
        string ghostTag = _win32Stealth.IsClickThroughEnabled ? "[穿透中:Alt+G/F8解除] " : "";
        StatusTextBlock.Text = $"{ghostTag}{shieldTag}{_currentBook.Title} · [{_currentBook.CurrentChapterIndex + 1}/{_currentBook.TotalChapters}] {_currentBook.CurrentChapterTitle} ({chapterPct}% | 全書 {_currentBook.OverallProgressPercent:0.0}%)";
    }

    private void SaveCurrentBookProgressImmediately()
    {
        if (_currentBook != null && !_isRestoringScroll && !_isPanicFakeScreenActive)
        {
            UpdateReadingProgressAndStatus();
            _storage.SaveImmediately();
        }
    }

    #endregion

    #region 自動滾動巡航 (Auto-Scroll)

    private void ToggleAutoScroll()
    {
        if (_autoScrollTimer.IsEnabled)
        {
            StopAutoScroll();
        }
        else
        {
            _autoScrollTimer.Start();
            UpdateTopBarStatusButtons();
        }
    }

    private void StopAutoScroll()
    {
        if (_autoScrollTimer.IsEnabled)
        {
            _autoScrollTimer.Stop();
            UpdateTopBarStatusButtons();
        }
    }

    private async void AutoScrollTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsVisible || _isPanicFakeScreenActive ||
            LibraryDrawer.Visibility == Visibility.Visible ||
            SettingsDrawer.Visibility == Visibility.Visible)
        {
            return;
        }

        // 每 50ms 推進 (AutoScrollSpeed / 20) 像素
        double step = Math.Max(0.25, _storage.Settings.AutoScrollSpeed * 0.05);
        if (ReaderScrollViewer.VerticalOffset >= ReaderScrollViewer.ScrollableHeight - 0.5)
        {
            if (_currentBook != null && _currentBook.CurrentChapterIndex < _epubEngine.TotalChapters - 1)
            {
                await LoadChapterAsync(_currentBook.CurrentChapterIndex + 1, 0.0);
            }
            else
            {
                StopAutoScroll();
            }
        }
        else
        {
            ReaderScrollViewer.ScrollToVerticalOffset(ReaderScrollViewer.VerticalOffset + step);
        }
    }

    #endregion

    #region 全視窗拖曳、滾輪、翻頁與快捷鍵控制

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInteractiveControl(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (e.ClickCount == 2 &&
            LibraryDrawer.Visibility != Visibility.Visible &&
            SettingsDrawer.Visibility != Visibility.Visible)
        {
            e.Handled = true;
            _storage.Settings.ShowMiniStatusBar = !_storage.Settings.ShowMiniStatusBar;
            TopMicroBar.Visibility = _storage.Settings.ShowMiniStatusBar ? Visibility.Visible : Visibility.Collapsed;
            _storage.ScheduleSave();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch
            {
                // Ignore if mouse released mid-drag
            }
        }
    }

    private static bool IsInteractiveControl(DependencyObject? source)
    {
        var current = source;
        while (current != null)
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase ||
                current is Slider ||
                current is System.Windows.Controls.Primitives.Thumb ||
                current is System.Windows.Controls.Primitives.ScrollBar ||
                current is System.Windows.Controls.Primitives.ResizeGrip ||
                current is System.Windows.Controls.ComboBox ||
                current is System.Windows.Controls.ComboBoxItem ||
                current is System.Windows.Controls.Primitives.TextBoxBase ||
                current is System.Windows.Controls.ListBoxItem)
            {
                return true;
            }
            current = GetParentObject(current);
        }
        return false;
    }

    private static DependencyObject? GetParentObject(DependencyObject? current)
    {
        if (current == null) return null;
        if (current is Visual || current is System.Windows.Media.Media3D.Visual3D)
        {
            return VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
        }
        return LogicalTreeHelper.GetParent(current);
    }

    private async Task ScrollStepOrNextChapterAsync(bool down)
    {
        double step = _storage.Settings.ScrollStepPixels;

        // 若處於假報錯 StackTrace 畫面，支援滑鼠滾輪與方向鍵正常上下滾動檢視日誌（不跨章、不覆寫書籍進度）
        if (_isPanicFakeScreenActive)
        {
            if (down)
            {
                ReaderScrollViewer.ScrollToVerticalOffset(ReaderScrollViewer.VerticalOffset + step);
            }
            else
            {
                ReaderScrollViewer.ScrollToVerticalOffset(Math.Max(0, ReaderScrollViewer.VerticalOffset - step));
            }
            return;
        }

        if (down)
        {
            if (ReaderScrollViewer.VerticalOffset >= ReaderScrollViewer.ScrollableHeight - 0.5 &&
                _currentBook != null &&
                _currentBook.CurrentChapterIndex < _epubEngine.TotalChapters - 1)
            {
                await LoadChapterAsync(_currentBook.CurrentChapterIndex + 1, 0.0);
            }
            else
            {
                ReaderScrollViewer.ScrollToVerticalOffset(ReaderScrollViewer.VerticalOffset + step);
            }
        }
        else
        {
            if (ReaderScrollViewer.VerticalOffset <= 0.5 &&
                _currentBook != null &&
                _currentBook.CurrentChapterIndex > 0)
            {
                await LoadChapterAsync(_currentBook.CurrentChapterIndex - 1, 1.0);
            }
            else
            {
                ReaderScrollViewer.ScrollToVerticalOffset(Math.Max(0, ReaderScrollViewer.VerticalOffset - step));
            }
        }
    }

    private async void ReaderScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var modifiers = Keyboard.Modifiers;

        if (modifiers == ModifierKeys.Control)
        {
            double delta = e.Delta > 0 ? 1.0 : -1.0;
            _storage.Settings.FontSize = Math.Clamp(_storage.Settings.FontSize + delta, 10, 36);
            if (!_isInitializing) FontSizeSlider.Value = _storage.Settings.FontSize;
            ApplyAppearanceToUI();
            _storage.ScheduleSave();
            return;
        }

        if (modifiers == ModifierKeys.Alt)
        {
            double delta = e.Delta > 0 ? 0.05 : -0.05;
            _storage.Settings.TextOpacity = Math.Round(Math.Clamp(_storage.Settings.TextOpacity + delta, 0.10, 1.0), 2);
            if (!_isInitializing) TextOpacitySlider.Value = _storage.Settings.TextOpacity;
            ApplyAppearanceToUI();
            _storage.ScheduleSave();
            return;
        }

        if (modifiers == ModifierKeys.Shift)
        {
            double delta = e.Delta > 0 ? 0.05 : -0.05;
            _storage.Settings.BackgroundOpacity = Math.Round(Math.Clamp(_storage.Settings.BackgroundOpacity + delta, 0.0, 1.0), 2);
            if (!_isInitializing) BgOpacitySlider.Value = _storage.Settings.BackgroundOpacity;
            ApplyAppearanceToUI();
            _storage.ScheduleSave();
            return;
        }

        await ScrollStepOrNextChapterAsync(down: e.Delta < 0);
    }

    private void ReaderScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isInitializing || _isRestoringScroll) return;

        if (_isPanicFakeScreenActive)
        {
            double maxScroll = Math.Max(1.0, ReaderScrollViewer.ScrollableHeight);
            double curOffset = ReaderScrollViewer.VerticalOffset;
            int approxLine = 1 + (int)Math.Round((curOffset / maxScroll) * 168);
            StatusTextBlock.Text = $"[DIAGNOSTIC-ERR] SqlClient Timeout (0x80131904) | Buffer Line {approxLine}/172 | Offset {(int)curOffset}px";
            return;
        }

        if (_currentBook == null) return;
        UpdateReadingProgressAndStatus();
        _storage.ScheduleSave();
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 攔截視窗內 Alt 系統組合鍵，避免 WPF 進入 Win32 系統選單模式 (SC_KEYMENU) 吃掉按鍵
        if (e.Key == Key.System)
        {
            if (e.SystemKey == Key.G)
            {
                e.Handled = true;
                _win32Stealth.TriggerGhostToggleDebounced();
                return;
            }
            if (e.SystemKey == Key.J)
            {
                e.Handled = true;
                await ScrollStepOrNextChapterAsync(down: true);
                return;
            }
            if (e.SystemKey == Key.K)
            {
                e.Handled = true;
                await ScrollStepOrNextChapterAsync(down: false);
                return;
            }
        }

        if (e.Key == Key.F8)
        {
            e.Handled = true;
            _win32Stealth.TriggerGhostToggleDebounced();
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.B)
        {
            e.Handled = true;
            ToggleLibraryDrawerTab(DrawerTab.Books);
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.T)
        {
            e.Handled = true;
            ToggleLibraryDrawerTab(DrawerTab.Chapters);
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
        {
            e.Handled = true;
            OpenLibraryDrawer(DrawerTab.Search);
            SearchBox.Focus();
            SearchBox.SelectAll();
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.D)
        {
            e.Handled = true;
            AddBookmarkAtCurrentPosition();
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.L)
        {
            e.Handled = true;
            CycleCamouflageMode();
            return;
        }

        if ((Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.OemComma) || e.Key == Key.F2)
        {
            e.Handled = true;
            if (SettingsDrawer.Visibility == Visibility.Visible)
                CloseDrawers();
            else
                OpenSettingsDrawer();
            return;
        }

        if (e.Key == Key.F5)
        {
            e.Handled = true;
            ToggleAutoScroll();
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (LibraryDrawer.Visibility == Visibility.Visible || SettingsDrawer.Visibility == Visibility.Visible)
            {
                CloseDrawers();
            }
            else
            {
                TriggerBossKeyAction();
            }
            return;
        }

        if (LibraryDrawer.Visibility == Visibility.Visible || SettingsDrawer.Visibility == Visibility.Visible)
        {
            return;
        }

        double step = _storage.Settings.ScrollStepPixels;
        double pageStep = Math.Max(step, ReaderScrollViewer.ViewportHeight * 0.85);

        switch (e.Key)
        {
            case Key.Down:
            case Key.J:
                e.Handled = true;
                await ScrollStepOrNextChapterAsync(down: true);
                break;

            case Key.Up:
            case Key.K:
                e.Handled = true;
                await ScrollStepOrNextChapterAsync(down: false);
                break;

            case Key.Right:
            case Key.PageDown:
            case Key.Space:
                e.Handled = true;
                if (!_isPanicFakeScreenActive && ReaderScrollViewer.VerticalOffset >= ReaderScrollViewer.ScrollableHeight - 1.0)
                {
                    if (_currentBook != null && _currentBook.CurrentChapterIndex < _epubEngine.TotalChapters - 1)
                    {
                        await LoadChapterAsync(_currentBook.CurrentChapterIndex + 1, 0.0);
                    }
                }
                else
                {
                    ReaderScrollViewer.ScrollToVerticalOffset(ReaderScrollViewer.VerticalOffset + pageStep);
                }
                break;

            case Key.Left:
            case Key.PageUp:
                e.Handled = true;
                if (!_isPanicFakeScreenActive && ReaderScrollViewer.VerticalOffset <= 1.0)
                {
                    if (_currentBook != null && _currentBook.CurrentChapterIndex > 0)
                    {
                        await LoadChapterAsync(_currentBook.CurrentChapterIndex - 1, 1.0);
                    }
                }
                else
                {
                    ReaderScrollViewer.ScrollToVerticalOffset(Math.Max(0, ReaderScrollViewer.VerticalOffset - pageStep));
                }
                break;

            case Key.OemOpenBrackets:
                e.Handled = true;
                if (_currentBook != null && _currentBook.CurrentChapterIndex > 0)
                {
                    await LoadChapterAsync(_currentBook.CurrentChapterIndex - 1, 0.0);
                }
                break;

            case Key.OemCloseBrackets:
                e.Handled = true;
                if (_currentBook != null && _currentBook.CurrentChapterIndex < _epubEngine.TotalChapters - 1)
                {
                    await LoadChapterAsync(_currentBook.CurrentChapterIndex + 1, 0.0);
                }
                break;
        }
    }

    #endregion

    #region 防抓包：老闆鍵 (瞬隱 vs 假報錯 StackTrace)、滑鼠穿透與自動隱形

    private void TriggerBossKeyAction()
    {
        StopAutoScroll();

        // 模式 A：切換為假報錯 StackTrace 畫面（視窗不消失，避免旁人起疑）
        if (string.Equals(_storage.Settings.BossKeyAction, "FakeException", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsVisible)
            {
                EnsureWindowVisibleAndInteractive();
            }

            if (!_isPanicFakeScreenActive)
            {
                SaveCurrentBookProgressImmediately();
                _savedPanicScrollOffset = ReaderScrollViewer.VerticalOffset;
                _isPanicFakeScreenActive = true;

                _isRestoringScroll = true;
                ReaderTextBlock.Text = CamouflageService.GenerateFakeExceptionScreen();
                StatusTextBlock.Text = "[DIAGNOSTIC-ERR] SqlClient Timeout (0x80131904) | WorkerPool: Degraded";
                ReaderScrollViewer.ScrollToTop();
                _isRestoringScroll = false;
            }
            else
            {
                // 恢復原本的小說內容與捲動位置
                _isPanicFakeScreenActive = false;
                if (_currentChapter != null)
                {
                    _isRestoringScroll = true;
                    ReaderTextBlock.Text = CamouflageService.FormatChapterContent(_currentChapter, _storage.Settings.CamouflageMode);
                    ReaderScrollViewer.UpdateLayout();
                    ReaderScrollViewer.ScrollToVerticalOffset(_savedPanicScrollOffset);
                    _isRestoringScroll = false;
                    UpdateReadingProgressAndStatus();
                }
            }
            return;
        }

        // 模式 B（預設）：瞬間隱藏或喚醒視窗
        if (IsVisible && Visibility == Visibility.Visible)
        {
            HideStealthWindowImmediately();
        }
        else
        {
            EnsureWindowVisibleAndInteractive();
        }
    }

    private void HideStealthWindowImmediately()
    {
        StopAutoScroll();
        SaveCurrentBookProgressImmediately();
        Hide();
    }

    private void EnsureWindowVisibleAndInteractive()
    {
        if (_win32Stealth.IsClickThroughEnabled)
        {
            _win32Stealth.SetClickThrough(false);
            UpdateTopBarStatusButtons();
        }
        Show();
        Activate();
        ApplyMouseStealthState(isMouseInside: true);
    }

    private void ToggleClickThroughGhostMode()
    {
        bool nextState = !_win32Stealth.IsClickThroughEnabled;
        if (nextState)
        {
            CloseDrawers();
            ApplyMouseStealthState(isMouseInside: true);
        }

        _win32Stealth.SetClickThrough(nextState);

        if (!nextState)
        {
            Show();
            Activate();
            ApplyMouseStealthState(isMouseInside: true);
        }

        UpdateTopBarStatusButtons();
        UpdateReadingProgressAndStatus();
    }

    private void CycleCamouflageMode()
    {
        string next = _storage.Settings.CamouflageMode switch
        {
            "None" => "SystemLog",
            "SystemLog" => "CSharpCode",
            _ => "None"
        };
        _storage.Settings.CamouflageMode = next;

        _isInitializing = true;
        SelectComboByTag(CamouflageModeCombo, next);
        _isInitializing = false;

        UpdateTopBarStatusButtons();
        RefreshChapterCamouflageDisplay();
        _storage.ScheduleSave();
    }

    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        ApplyMouseStealthState(isMouseInside: true);
    }

    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_storage.Settings.AutoHideOnMouseLeave &&
            !_win32Stealth.IsClickThroughEnabled &&
            LibraryDrawer.Visibility != Visibility.Visible &&
            SettingsDrawer.Visibility != Visibility.Visible)
        {
            ApplyMouseStealthState(isMouseInside: false);
        }
    }

    private void ApplyMouseStealthState(bool isMouseInside)
    {
        if (isMouseInside || !_storage.Settings.AutoHideOnMouseLeave)
        {
            MainContentGrid.Opacity = 1.0;
            ApplyAppearanceToUI();
        }
        else
        {
            MainContentGrid.Opacity = 0.0;
            RootBorder.Background = HitTestInvisibleBrush;
            RootBorder.BorderBrush = System.Windows.Media.Brushes.Transparent;
        }
    }

    private void TopMicroBar_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        TopMicroBar.Opacity = 0.95;
    }

    private void TopMicroBar_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        TopMicroBar.Opacity = 0.38;
    }

    private void BtnAutoScrollToggle_Click(object sender, RoutedEventArgs e)
    {
        ToggleAutoScroll();
    }

    private void BtnCamouflageCycle_Click(object sender, RoutedEventArgs e)
    {
        CycleCamouflageMode();
    }

    private void BtnGhostClickThrough_Click(object sender, RoutedEventArgs e)
    {
        ToggleClickThroughGhostMode();
    }

    private void BtnAutoHideToggle_Click(object sender, RoutedEventArgs e)
    {
        _storage.Settings.AutoHideOnMouseLeave = !_storage.Settings.AutoHideOnMouseLeave;
        AutoHideCheckBox.IsChecked = _storage.Settings.AutoHideOnMouseLeave;
        UpdateTopBarStatusButtons();
        _storage.ScheduleSave();
    }

    private void BtnBossHide_Click(object sender, RoutedEventArgs e)
    {
        TriggerBossKeyAction();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    #endregion

    #region 書籤與全書搜尋、圖書館與目錄抽屜

    private void AddBookmarkAtCurrentPosition()
    {
        if (_currentBook == null || _currentChapter == null) return;

        UpdateReadingProgressAndStatus();

        // 擷取對應比例附近的段落作為書籤文字摘要
        int pCount = _currentChapter.Paragraphs.Count;
        int approxIdx = pCount > 1
            ? Math.Clamp((int)Math.Round(_currentBook.ScrollRatio * (pCount - 1)), 0, pCount - 1)
            : 0;

        string snippet = pCount > 0 ? _currentChapter.Paragraphs[approxIdx] : _currentChapter.Title;
        if (snippet.Length > 48)
        {
            snippet = snippet[..48] + "...";
        }

        var bookmark = new BookmarkItem
        {
            Id = Guid.NewGuid().ToString("N"),
            ChapterIndex = _currentBook.CurrentChapterIndex,
            ChapterTitle = _currentBook.CurrentChapterTitle,
            ScrollRatio = _currentBook.ScrollRatio,
            Snippet = snippet,
            CreatedAt = DateTime.Now
        };

        _currentBook.Bookmarks.Insert(0, bookmark);
        _storage.SaveImmediately();

        StatusTextBlock.Text = $"🔖 已加入書籤：{bookmark.PositionDisplay} — {snippet}";
        if (LibraryDrawer.Visibility == Visibility.Visible && _activeDrawerTab == DrawerTab.Bookmarks)
        {
            RefreshBookmarksList();
        }
    }

    private void BtnAddBookmark_Click(object sender, RoutedEventArgs e)
    {
        AddBookmarkAtCurrentPosition();
    }

    private void BtnAddBookmark_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenLibraryDrawer(DrawerTab.Bookmarks);
    }

    private void BtnSearch_Click(object sender, RoutedEventArgs e)
    {
        ToggleLibraryDrawerTab(DrawerTab.Search);
        if (LibraryDrawer.Visibility == Visibility.Visible)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        }
    }

    private async void BtnPrevChapter_Click(object sender, RoutedEventArgs e)
    {
        if (_currentBook != null && _currentBook.CurrentChapterIndex > 0)
        {
            await LoadChapterAsync(_currentBook.CurrentChapterIndex - 1, 0.0);
        }
    }

    private async void BtnNextChapter_Click(object sender, RoutedEventArgs e)
    {
        if (_currentBook != null && _currentBook.CurrentChapterIndex < _epubEngine.TotalChapters - 1)
        {
            await LoadChapterAsync(_currentBook.CurrentChapterIndex + 1, 0.0);
        }
    }

    private void BtnLibrary_Click(object sender, RoutedEventArgs e)
    {
        ToggleLibraryDrawerTab(DrawerTab.Books);
    }

    private void BtnToc_Click(object sender, RoutedEventArgs e)
    {
        ToggleLibraryDrawerTab(DrawerTab.Chapters);
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsDrawer.Visibility == Visibility.Visible)
            CloseDrawers();
        else
            OpenSettingsDrawer();
    }

    private void ToggleLibraryDrawerTab(DrawerTab tab)
    {
        if (LibraryDrawer.Visibility == Visibility.Visible && _activeDrawerTab == tab)
        {
            CloseDrawers();
        }
        else
        {
            OpenLibraryDrawer(tab);
        }
    }

    private void ExpandWindowTemporarilyForDrawer()
    {
        StopAutoScroll();
        if (Height < 340)
        {
            _savedCompactHeight ??= Height;
            Height = 360;
        }
        if (Width < 560)
        {
            Width = 580;
        }
    }

    private void OpenLibraryDrawer(DrawerTab tab)
    {
        ExpandWindowTemporarilyForDrawer();
        SettingsDrawer.Visibility = Visibility.Collapsed;
        LibraryDrawer.Visibility = Visibility.Visible;

        if (tab != DrawerTab.Search)
        {
            SearchBox.Text = string.Empty;
        }
        SwitchLibraryTab(tab);
    }

    private void SwitchLibraryTab(DrawerTab tab)
    {
        _activeDrawerTab = tab;

        BooksListBox.Visibility = tab == DrawerTab.Books ? Visibility.Visible : Visibility.Collapsed;
        ChaptersListBox.Visibility = tab == DrawerTab.Chapters ? Visibility.Visible : Visibility.Collapsed;
        BookmarksListBox.Visibility = tab == DrawerTab.Bookmarks ? Visibility.Visible : Visibility.Collapsed;
        SearchResultsListBox.Visibility = tab == DrawerTab.Search ? Visibility.Visible : Visibility.Collapsed;

        BtnTabBooks.FontWeight = tab == DrawerTab.Books ? FontWeights.Bold : FontWeights.Normal;
        BtnTabChapters.FontWeight = tab == DrawerTab.Chapters ? FontWeights.Bold : FontWeights.Normal;
        BtnTabBookmarks.FontWeight = tab == DrawerTab.Bookmarks ? FontWeights.Bold : FontWeights.Normal;
        BtnTabSearch.FontWeight = tab == DrawerTab.Search ? FontWeights.Bold : FontWeights.Normal;

        BtnImportEpub.Visibility = tab == DrawerTab.Books ? Visibility.Visible : Visibility.Collapsed;
        BtnRemoveItem.Visibility = (tab == DrawerTab.Books || tab == DrawerTab.Bookmarks) ? Visibility.Visible : Visibility.Collapsed;
        BtnExecuteBookSearch.Visibility = tab == DrawerTab.Search ? Visibility.Visible : Visibility.Collapsed;

        SearchPromptText.Text = tab == DrawerTab.Search ? "全書關鍵字：" : "篩選：";

        switch (tab)
        {
            case DrawerTab.Books:
                RefreshLibraryList();
                break;
            case DrawerTab.Chapters:
                RefreshChaptersList();
                break;
            case DrawerTab.Bookmarks:
                RefreshBookmarksList();
                break;
            case DrawerTab.Search:
                DrawerFooterHint.Text = "輸入關鍵字後按 Enter 或點擊「搜尋全書」，雙擊結果即可直接跳轉至該段落。";
                break;
        }
    }

    private void RefreshLibraryList()
    {
        string keyword = SearchBox.Text.Trim();
        var items = _storage.Library
            .OrderByDescending(b => b.LastReadTime)
            .Where(b => string.IsNullOrEmpty(keyword) ||
                        b.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                        b.Author.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();

        BooksListBox.ItemsSource = items;
        if (_currentBook != null)
        {
            BooksListBox.SelectedItem = items.FirstOrDefault(b => b.Id == _currentBook.Id);
        }
    }

    private void RefreshChaptersList()
    {
        string keyword = SearchBox.Text.Trim();
        var items = _epubEngine.Chapters
            .Where(c => string.IsNullOrEmpty(keyword) ||
                        c.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();

        ChaptersListBox.ItemsSource = items;
        if (_currentBook != null)
        {
            var currentItem = items.FirstOrDefault(c => c.Index == _currentBook.CurrentChapterIndex);
            if (currentItem != null)
            {
                ChaptersListBox.SelectedItem = currentItem;
                ChaptersListBox.ScrollIntoView(currentItem);
            }
        }
    }

    private void RefreshBookmarksList()
    {
        if (_currentBook == null)
        {
            BookmarksListBox.ItemsSource = null;
            return;
        }

        string keyword = SearchBox.Text.Trim();
        var items = _currentBook.Bookmarks
            .Where(bm => string.IsNullOrEmpty(keyword) ||
                         bm.Snippet.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                         bm.ChapterTitle.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();

        BookmarksListBox.ItemsSource = items;
    }

    private async Task ExecuteFullBookSearchAsync()
    {
        string keyword = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(keyword)) return;

        DrawerFooterHint.Text = $"正在搜尋「{keyword}」...";
        var results = await _epubEngine.SearchInBookAsync(keyword);
        SearchResultsListBox.ItemsSource = results;
        DrawerFooterHint.Text = $"搜尋完成：共找到 {results.Count} 筆相符段落（雙擊項目即可跳轉）。";
    }

    private void OpenSettingsDrawer()
    {
        ExpandWindowTemporarilyForDrawer();
        LibraryDrawer.Visibility = Visibility.Collapsed;
        SettingsDrawer.Visibility = Visibility.Visible;
    }

    private void CloseDrawers()
    {
        LibraryDrawer.Visibility = Visibility.Collapsed;
        SettingsDrawer.Visibility = Visibility.Collapsed;

        if (_savedCompactHeight.HasValue)
        {
            Height = _savedCompactHeight.Value;
            _savedCompactHeight = null;
        }
    }

    private void BtnCloseDrawer_Click(object sender, RoutedEventArgs e)
    {
        CloseDrawers();
    }

    private void BtnTabBooks_Click(object sender, RoutedEventArgs e) => SwitchLibraryTab(DrawerTab.Books);
    private void BtnTabChapters_Click(object sender, RoutedEventArgs e) => SwitchLibraryTab(DrawerTab.Chapters);
    private void BtnTabBookmarks_Click(object sender, RoutedEventArgs e) => SwitchLibraryTab(DrawerTab.Bookmarks);
    private void BtnTabSearch_Click(object sender, RoutedEventArgs e)
    {
        SwitchLibraryTab(DrawerTab.Search);
        SearchBox.Focus();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        switch (_activeDrawerTab)
        {
            case DrawerTab.Books:
                RefreshLibraryList();
                break;
            case DrawerTab.Chapters:
                RefreshChaptersList();
                break;
            case DrawerTab.Bookmarks:
                RefreshBookmarksList();
                break;
        }
    }

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _activeDrawerTab == DrawerTab.Search)
        {
            e.Handled = true;
            await ExecuteFullBookSearchAsync();
        }
    }

    private async void BtnExecuteBookSearch_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteFullBookSearchAsync();
    }

    private async void BtnImportEpub_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "選擇要匯入圖書館的 EPUB 檔案",
            Filter = "EPUB 電子書 (*.epub)|*.epub|所有檔案 (*.*)|*.*",
            Multiselect = true
        };

        if (dlg.ShowDialog(this) == true)
        {
            for (int i = 0; i < dlg.FileNames.Length; i++)
            {
                bool isLast = i == dlg.FileNames.Length - 1;
                await ImportAndOpenBookAsync(dlg.FileNames[i], switchImmediately: isLast);
            }
            RefreshLibraryList();
        }
    }

    private void BtnRemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (_activeDrawerTab == DrawerTab.Books && BooksListBox.SelectedItem is BookItem selectedBook)
        {
            if (_currentBook?.Id == selectedBook.Id)
            {
                _epubEngine.DisposeCurrentBook();
            }
            _storage.DeleteManagedBookFile(selectedBook);
            _storage.Library.Remove(selectedBook);
            _storage.SaveImmediately();
            RefreshLibraryList();
        }
        else if (_activeDrawerTab == DrawerTab.Bookmarks &&
                 _currentBook != null &&
                 BookmarksListBox.SelectedItem is BookmarkItem selectedBookmark)
        {
            _currentBook.Bookmarks.Remove(selectedBookmark);
            _storage.SaveImmediately();
            RefreshBookmarksList();
        }
    }

    private async void DrawerListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        await ActivateSelectedDrawerItemAsync();
    }

    private async void DrawerListBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await ActivateSelectedDrawerItemAsync();
        }
    }

    private async void BtnConfirmSelection_Click(object sender, RoutedEventArgs e)
    {
        await ActivateSelectedDrawerItemAsync();
    }

    private async Task ActivateSelectedDrawerItemAsync()
    {
        switch (_activeDrawerTab)
        {
            case DrawerTab.Books when BooksListBox.SelectedItem is BookItem book:
                CloseDrawers();
                await LoadBookAsync(book);
                break;

            case DrawerTab.Chapters when ChaptersListBox.SelectedItem is ChapterItem chapter:
                CloseDrawers();
                await LoadChapterAsync(chapter.Index, 0.0);
                _storage.ScheduleSave();
                break;

            case DrawerTab.Bookmarks when BookmarksListBox.SelectedItem is BookmarkItem bm:
                CloseDrawers();
                await LoadChapterAsync(bm.ChapterIndex, bm.ScrollRatio);
                _storage.ScheduleSave();
                break;

            case DrawerTab.Search when SearchResultsListBox.SelectedItem is SearchResultItem sr:
                CloseDrawers();
                await LoadChapterAsync(sr.ChapterIndex, sr.ScrollRatio);
                _storage.ScheduleSave();
                break;
        }
    }

    #endregion

    #region 拖放匯入 (Drag & Drop .epub)

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;

        var epubFiles = files.Where(f => f.EndsWith(".epub", StringComparison.OrdinalIgnoreCase)).ToList();
        for (int i = 0; i < epubFiles.Count; i++)
        {
            bool isLast = i == epubFiles.Count - 1;
            await ImportAndOpenBookAsync(epubFiles[i], switchImmediately: isLast);
        }
    }

    #endregion

    #region 外觀、字型、雙層透明度與防抓包設定事件

    private void PresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ThemePreset preset }) return;

        _isInitializing = true;
        _storage.Settings.BackgroundHex = preset.BackgroundHex;
        _storage.Settings.TextHex = preset.TextHex;
        _storage.Settings.BackgroundOpacity = preset.BackgroundOpacity;
        _storage.Settings.TextOpacity = preset.TextOpacity;

        BgHexBox.Text = preset.BackgroundHex;
        TextHexBox.Text = preset.TextHex;
        BgOpacitySlider.Value = preset.BackgroundOpacity;
        TextOpacitySlider.Value = preset.TextOpacity;

        if (!string.IsNullOrEmpty(preset.RecommendedFont))
        {
            _storage.Settings.FontFamily = preset.RecommendedFont;
            FontFamilyCombo.SelectedItem = preset.RecommendedFont;
        }
        _isInitializing = false;

        ApplyAppearanceToUI();
        _storage.ScheduleSave();
    }

    private void FontFamilyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (FontFamilyCombo.SelectedItem is string fontName && !string.IsNullOrWhiteSpace(fontName))
        {
            _storage.Settings.FontFamily = fontName;
            ApplyAppearanceToUI();
            _storage.ScheduleSave();
        }
    }

    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        _storage.Settings.FontSize = Math.Round(e.NewValue);
        ApplyAppearanceToUI();
        _storage.ScheduleSave();
    }

    private void LineHeightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        _storage.Settings.LineHeightMultiplier = Math.Round(e.NewValue, 2);
        ApplyAppearanceToUI();
        _storage.ScheduleSave();
    }

    private void AutoScrollSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        _storage.Settings.AutoScrollSpeed = Math.Round(e.NewValue);
        ApplyAppearanceToUI();
        _storage.ScheduleSave();
    }

    private void BgOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        _storage.Settings.BackgroundOpacity = Math.Round(e.NewValue, 2);
        ApplyAppearanceToUI();
        _storage.ScheduleSave();
    }

    private void TextOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        _storage.Settings.TextOpacity = Math.Round(e.NewValue, 2);
        ApplyAppearanceToUI();
        _storage.ScheduleSave();
    }

    private void ColorHexBox_LostFocus(object sender, RoutedEventArgs e)
    {
        ApplyCustomHexInput();
    }

    private void ColorHexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            ApplyCustomHexInput();
        }
    }

    private void ApplyCustomHexInput()
    {
        if (_isInitializing) return;
        _storage.Settings.BackgroundHex = BgHexBox.Text.Trim();
        _storage.Settings.TextHex = TextHexBox.Text.Trim();
        ApplyAppearanceToUI();
        _storage.ScheduleSave();
    }

    private void QuickBgColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex })
        {
            BgHexBox.Text = hex;
            ApplyCustomHexInput();
        }
    }

    private void QuickTextColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex })
        {
            TextHexBox.Text = hex;
            ApplyCustomHexInput();
        }
    }

    private void CamouflageModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (CamouflageModeCombo.SelectedItem is ComboBoxItem { Tag: string mode })
        {
            _storage.Settings.CamouflageMode = mode;
            UpdateTopBarStatusButtons();
            RefreshChapterCamouflageDisplay();
            _storage.ScheduleSave();
        }
    }

    private void BossKeyActionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (BossKeyActionCombo.SelectedItem is ComboBoxItem { Tag: string action })
        {
            _storage.Settings.BossKeyAction = action;
            _storage.ScheduleSave();
        }
    }

    private void BossKeyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (BossKeyCombo.SelectedItem is string gesture)
        {
            _storage.Settings.BossKeyGesture = gesture;
            _win32Stealth.RegisterBossKey(gesture);
            _storage.ScheduleSave();
        }
    }

    private void StealthCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        _storage.Settings.ExcludeFromCapture = ExcludeCaptureCheckBox.IsChecked == true;
        _storage.Settings.AutoHideOnMouseLeave = AutoHideCheckBox.IsChecked == true;
        _storage.Settings.Topmost = TopmostCheckBox.IsChecked == true;
        _storage.Settings.HideFromAltTab = HideAltTabCheckBox.IsChecked == true;

        Topmost = _storage.Settings.Topmost;
        _win32Stealth.SetExcludeFromCapture(_storage.Settings.ExcludeFromCapture);
        _win32Stealth.SetHideFromAltTab(_storage.Settings.HideFromAltTab);
        UpdateTopBarStatusButtons();
        UpdateReadingProgressAndStatus();
        _storage.ScheduleSave();
    }

    private void CustomBooksDirBox_LostFocus(object sender, RoutedEventArgs e)
    {
        ApplyCustomBooksDirectoryInput();
    }

    private void CustomBooksDirBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            ApplyCustomBooksDirectoryInput();
        }
    }

    private void ApplyCustomBooksDirectoryInput()
    {
        if (_isInitializing) return;
        _storage.Settings.CustomBooksDirectory = CustomBooksDirBox.Text.Trim();
        _storage.GetActiveBooksDirectory();
        _storage.SaveImmediately();
    }

    private void BtnBrowseBooksDir_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "選擇書庫 EPUB 檔案自動複製存放的資料夾",
            SelectedPath = _storage.GetActiveBooksDirectory(),
            ShowNewFolderButton = true
        };

        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
        {
            CustomBooksDirBox.Text = dlg.SelectedPath;
            ApplyCustomBooksDirectoryInput();
        }
    }

    private void BtnResetBooksDir_Click(object sender, RoutedEventArgs e)
    {
        CustomBooksDirBox.Text = string.Empty;
        ApplyCustomBooksDirectoryInput();
    }

    #endregion

    #region 視窗位置尺寸記憶與關閉清理

    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        if (_isInitializing || WindowState != WindowState.Normal) return;
        _storage.Settings.WindowLeft = Left;
        _storage.Settings.WindowTop = Top;
        _storage.ScheduleSave();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isInitializing || WindowState != WindowState.Normal) return;
        if (_savedCompactHeight == null)
        {
            _storage.Settings.WindowWidth = Width;
            _storage.Settings.WindowHeight = Height;
            _storage.ScheduleSave();
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        StopAutoScroll();
        if (_savedCompactHeight.HasValue)
        {
            _storage.Settings.WindowHeight = _savedCompactHeight.Value;
        }
        SaveCurrentBookProgressImmediately();
        _win32Stealth.Dispose();
        _epubEngine.Dispose();
    }

    #endregion
}
