using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    private readonly StorageService _storage = new();
    private readonly EpubEngineService _epubEngine = new();
    private readonly Win32StealthService _win32Stealth = new();

    private BookItem? _currentBook;
    private bool _isInitializing = true;
    private bool _isRestoringScroll = false;
    private double? _savedCompactHeight;

    private static readonly SolidColorBrush HitTestInvisibleBrush = CreateFrozenBrush(Color.FromArgb(2, 0, 0, 0));
    private static readonly SolidColorBrush SubtleBorderBrush = CreateFrozenBrush(Color.FromArgb(36, 136, 136, 136));

    public MainWindow()
    {
        InitializeComponent();
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

        // 還原視窗位置與大小
        RestoreWindowBounds();

        // 初始化 Win32 全域老闆鍵與低調系統匣
        _win32Stealth.BossKeyTriggered += ToggleBossStealthVisibility;
        _win32Stealth.TrayToggleVisibilityRequested += () => Dispatcher.Invoke(ToggleBossStealthVisibility);
        _win32Stealth.TrayOpenLibraryRequested += () => Dispatcher.Invoke(() =>
        {
            EnsureWindowVisible();
            OpenLibraryDrawer(showChaptersTab: false);
        });
        _win32Stealth.TrayOpenSettingsRequested += () => Dispatcher.Invoke(() =>
        {
            EnsureWindowVisible();
            OpenSettingsDrawer();
        });
        _win32Stealth.TrayExitRequested += () => Dispatcher.Invoke(Close);

        _win32Stealth.Initialize(this, _storage.Settings.BossKeyGesture, _storage.Settings.HideFromAltTab);

        // 初始化設定面板控制項與保護色按鈕
        InitializeSettingsControls();
        ApplyAppearanceToUI();

        _isInitializing = false;

        // 若書庫為空，自動產生內建範例 EPUB 書籍供立即體驗
        if (_storage.Library.Count == 0)
        {
            string samplePath = SampleEpubGenerator.EnsureSampleEpubCreated(_storage.DataDirectory);
            await ImportAndOpenBookAsync(samplePath, switchImmediately: true);
        }
        else
        {
            // 自動續讀上次開啟的書籍
            var targetBook = _storage.Library.FirstOrDefault(b => b.Id == _storage.Settings.LastOpenedBookId)
                             ?? _storage.Library.OrderByDescending(b => b.LastReadTime).First();

            if (File.Exists(targetBook.FilePath))
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
        ScrollStepSlider.Value = Math.Clamp(s.ScrollStepPixels, 16, 160);
        BgOpacitySlider.Value = Math.Clamp(s.BackgroundOpacity, 0.0, 1.0);
        TextOpacitySlider.Value = Math.Clamp(s.TextOpacity, 0.10, 1.0);

        BgHexBox.Text = s.BackgroundHex;
        TextHexBox.Text = s.TextHex;

        // 4. 載入防抓包選項
        BossKeyCombo.ItemsSource = Win32StealthService.SupportedBossKeys;
        BossKeyCombo.SelectedItem = Win32StealthService.SupportedBossKeys.Contains(s.BossKeyGesture)
            ? s.BossKeyGesture
            : "Alt+Q";

        AutoHideCheckBox.IsChecked = s.AutoHideOnMouseLeave;
        TopmostCheckBox.IsChecked = s.Topmost;
        HideAltTabCheckBox.IsChecked = s.HideFromAltTab;

        UpdateAutoHideButtonText();
    }

    private void ApplyAppearanceToUI()
    {
        var s = _storage.Settings;

        // 背景與文字雙層獨立透明度筆刷
        RootBorder.Background = ThemeAndFontService.CreateBackgroundBrush(s.BackgroundHex, s.BackgroundOpacity);
        RootBorder.BorderBrush = s.BackgroundOpacity <= 0.02
            ? System.Windows.Media.Brushes.Transparent
            : SubtleBorderBrush;

        var textBrush = ThemeAndFontService.CreateTextBrush(s.TextHex, s.TextOpacity);
        ReaderTextBlock.Foreground = textBrush;
        StatusTextBlock.Foreground = textBrush;

        // 套用字型、字體大小與行高
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

        // 更新設定面板數值標籤
        if (FontSizeValueText != null) FontSizeValueText.Text = $"{s.FontSize:0}pt";
        if (LineHeightValueText != null) LineHeightValueText.Text = $"{s.LineHeightMultiplier:0.00}x";
        if (ScrollStepValueText != null) ScrollStepValueText.Text = $"{s.ScrollStepPixels:0}px";
        if (BgOpacityValueText != null) BgOpacityValueText.Text = $"{Math.Round(s.BackgroundOpacity * 100)}%";
        if (TextOpacityValueText != null) TextOpacityValueText.Text = $"{Math.Round(s.TextOpacity * 100)}%";
    }

    #region EPUB 載入、章節切換與進度記憶

    public async Task ImportAndOpenBookAsync(string filePath, bool switchImmediately)
    {
        if (!File.Exists(filePath) || !filePath.EndsWith(".epub", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string fullPath = Path.GetFullPath(filePath);
        var existing = _storage.Library.FirstOrDefault(b =>
            string.Equals(Path.GetFullPath(b.FilePath), fullPath, StringComparison.OrdinalIgnoreCase));

        if (existing == null)
        {
            var (title, author, totalChapters) = await _epubEngine.OpenBookAsync(fullPath);
            existing = new BookItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = title,
                Author = author,
                FilePath = fullPath,
                TotalChapters = Math.Max(1, totalChapters),
                CurrentChapterIndex = 0,
                ScrollRatio = 0.0,
                VerticalOffset = 0.0,
                LastReadTime = DateTime.Now
            };
            _storage.Library.Insert(0, existing);
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

            var (title, author, totalChapters) = await _epubEngine.OpenBookAsync(book.FilePath);
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

        chapterIndex = Math.Clamp(chapterIndex, 0, _epubEngine.TotalChapters - 1);
        var chapter = await _epubEngine.GetChapterAsync(chapterIndex);

        _isRestoringScroll = true;
        _currentBook.CurrentChapterIndex = chapter.Index;
        _currentBook.CurrentChapterTitle = chapter.Title;
        _currentBook.LastReadTime = DateTime.Now;

        ReaderTextBlock.Text = chapter.FullText;
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

    private void UpdateReadingProgressAndStatus()
    {
        if (_currentBook == null) return;

        double scrollable = ReaderScrollViewer.ScrollableHeight;
        double offset = ReaderScrollViewer.VerticalOffset;
        double ratio = scrollable > 0.5 ? Math.Clamp(offset / scrollable, 0.0, 1.0) : 0.0;

        _currentBook.VerticalOffset = offset;
        _currentBook.ScrollRatio = ratio;
        _currentBook.LastReadTime = DateTime.Now;

        double chapterPct = Math.Round(ratio * 100.0, 0);
        StatusTextBlock.Text = $"{_currentBook.Title} · [{_currentBook.CurrentChapterIndex + 1}/{_currentBook.TotalChapters}] {_currentBook.CurrentChapterTitle} ({chapterPct}% | 全書 {_currentBook.OverallProgressPercent:0.0}%)";
    }

    private void SaveCurrentBookProgressImmediately()
    {
        if (_currentBook != null && !_isRestoringScroll)
        {
            UpdateReadingProgressAndStatus();
            _storage.SaveImmediately();
        }
    }

    #endregion

    #region 滾輪、翻頁、快捷鍵與視窗拖曳控制

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInteractiveControl(e.OriginalSource as DependencyObject))
        {
            return;
        }

        // 雙擊閱讀區可快速切換隱藏/顯示頂部微型工具列，達到極致純文字偽裝
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

        // 點住視窗任一非互動控制項位置（含閱讀內文區、狀態列、空白處）皆可直接拖曳移動視窗
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

    private void ReaderScrollViewer_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Handled in Window_PreviewMouseLeftButtonDown (ClickCount == 2)
    }

    private async void ReaderScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var modifiers = Keyboard.Modifiers;

        // 1. Ctrl + 滾輪：即時縮放字體大小
        if (modifiers == ModifierKeys.Control)
        {
            double delta = e.Delta > 0 ? 1.0 : -1.0;
            _storage.Settings.FontSize = Math.Clamp(_storage.Settings.FontSize + delta, 10, 36);
            if (!_isInitializing) FontSizeSlider.Value = _storage.Settings.FontSize;
            ApplyAppearanceToUI();
            _storage.ScheduleSave();
            return;
        }

        // 2. Alt + 滾輪：即時調整文字透明度
        if (modifiers == ModifierKeys.Alt)
        {
            double delta = e.Delta > 0 ? 0.05 : -0.05;
            _storage.Settings.TextOpacity = Math.Round(Math.Clamp(_storage.Settings.TextOpacity + delta, 0.10, 1.0), 2);
            if (!_isInitializing) TextOpacitySlider.Value = _storage.Settings.TextOpacity;
            ApplyAppearanceToUI();
            _storage.ScheduleSave();
            return;
        }

        // 3. Shift + 滾輪：即時調整背景透明度
        if (modifiers == ModifierKeys.Shift)
        {
            double delta = e.Delta > 0 ? 0.05 : -0.05;
            _storage.Settings.BackgroundOpacity = Math.Round(Math.Clamp(_storage.Settings.BackgroundOpacity + delta, 0.0, 1.0), 2);
            if (!_isInitializing) BgOpacitySlider.Value = _storage.Settings.BackgroundOpacity;
            ApplyAppearanceToUI();
            _storage.ScheduleSave();
            return;
        }

        // 4. 一般滾輪：依設定步進平滑捲動，若在章節底端/頂端繼續滾動則自動跨章節
        double step = _storage.Settings.ScrollStepPixels;
        if (e.Delta < 0)
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
        else if (e.Delta > 0)
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

    private void ReaderScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isInitializing || _isRestoringScroll || _currentBook == null) return;
        UpdateReadingProgressAndStatus();
        _storage.ScheduleSave();
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+B：切換極簡圖書館
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.B)
        {
            e.Handled = true;
            if (LibraryDrawer.Visibility == Visibility.Visible)
                CloseDrawers();
            else
                OpenLibraryDrawer(showChaptersTab: false);
            return;
        }

        // Ctrl+T：切換章節目錄
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.T)
        {
            e.Handled = true;
            if (LibraryDrawer.Visibility == Visibility.Visible && ChaptersListBox.Visibility == Visibility.Visible)
                CloseDrawers();
            else
                OpenLibraryDrawer(showChaptersTab: true);
            return;
        }

        // Ctrl+, 或 F2：切換字型與外觀設定面板
        if ((Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.OemComma) || e.Key == Key.F2)
        {
            e.Handled = true;
            if (SettingsDrawer.Visibility == Visibility.Visible)
                CloseDrawers();
            else
                OpenSettingsDrawer();
            return;
        }

        // Esc：若抽屜開啟則關閉抽屜，否則直接觸發老闆鍵隱藏視窗！
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (LibraryDrawer.Visibility == Visibility.Visible || SettingsDrawer.Visibility == Visibility.Visible)
            {
                CloseDrawers();
            }
            else
            {
                HideStealthWindowImmediately();
            }
            return;
        }

        // 若抽屜開啟中，不攔截一般方向鍵與輸入框按鍵
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
                if (ReaderScrollViewer.VerticalOffset >= ReaderScrollViewer.ScrollableHeight - 0.5 &&
                    _currentBook != null && _currentBook.CurrentChapterIndex < _epubEngine.TotalChapters - 1)
                {
                    await LoadChapterAsync(_currentBook.CurrentChapterIndex + 1, 0.0);
                }
                else
                {
                    ReaderScrollViewer.ScrollToVerticalOffset(ReaderScrollViewer.VerticalOffset + step);
                }
                break;

            case Key.Up:
            case Key.K:
                e.Handled = true;
                if (ReaderScrollViewer.VerticalOffset <= 0.5 &&
                    _currentBook != null && _currentBook.CurrentChapterIndex > 0)
                {
                    await LoadChapterAsync(_currentBook.CurrentChapterIndex - 1, 1.0);
                }
                else
                {
                    ReaderScrollViewer.ScrollToVerticalOffset(Math.Max(0, ReaderScrollViewer.VerticalOffset - step));
                }
                break;

            case Key.Right:
            case Key.PageDown:
            case Key.Space:
                e.Handled = true;
                if (ReaderScrollViewer.VerticalOffset >= ReaderScrollViewer.ScrollableHeight - 1.0)
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
                if (ReaderScrollViewer.VerticalOffset <= 1.0)
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

            case Key.OemOpenBrackets: // '[' 上一章
                e.Handled = true;
                if (_currentBook != null && _currentBook.CurrentChapterIndex > 0)
                {
                    await LoadChapterAsync(_currentBook.CurrentChapterIndex - 1, 0.0);
                }
                break;

            case Key.OemCloseBrackets: // ']' 下一章
                e.Handled = true;
                if (_currentBook != null && _currentBook.CurrentChapterIndex < _epubEngine.TotalChapters - 1)
                {
                    await LoadChapterAsync(_currentBook.CurrentChapterIndex + 1, 0.0);
                }
                break;
        }
    }

    #endregion

    #region 防抓包：全域老闆鍵瞬隱與滑鼠移開自動隱形 (Auto-Stealth)

    private void ToggleBossStealthVisibility()
    {
        if (IsVisible && Visibility == Visibility.Visible)
        {
            HideStealthWindowImmediately();
        }
        else
        {
            EnsureWindowVisible();
        }
    }

    private void HideStealthWindowImmediately()
    {
        SaveCurrentBookProgressImmediately();
        Hide();
    }

    private void EnsureWindowVisible()
    {
        Show();
        Activate();
        ApplyMouseStealthState(isMouseInside: true);
    }

    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        ApplyMouseStealthState(isMouseInside: true);
    }

    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_storage.Settings.AutoHideOnMouseLeave &&
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
            // 滑鼠移開時：將文字與邊框設為 0% 完全透明，僅保留 Alpha=2 的透明背景以維持滑鼠移回偵測
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

    private void BtnAutoHideToggle_Click(object sender, RoutedEventArgs e)
    {
        _storage.Settings.AutoHideOnMouseLeave = !_storage.Settings.AutoHideOnMouseLeave;
        AutoHideCheckBox.IsChecked = _storage.Settings.AutoHideOnMouseLeave;
        UpdateAutoHideButtonText();
        _storage.ScheduleSave();
    }

    private void UpdateAutoHideButtonText()
    {
        BtnAutoHideToggle.Content = _storage.Settings.AutoHideOnMouseLeave ? "自動隱形:開" : "自動隱形:關";
    }

    private void BtnBossHide_Click(object sender, RoutedEventArgs e)
    {
        HideStealthWindowImmediately();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    #endregion

    #region 頂部按鈕與抽屜面板 (圖書館、目錄、外觀字型設定)

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
        if (LibraryDrawer.Visibility == Visibility.Visible && BooksListBox.Visibility == Visibility.Visible)
            CloseDrawers();
        else
            OpenLibraryDrawer(showChaptersTab: false);
    }

    private void BtnToc_Click(object sender, RoutedEventArgs e)
    {
        if (LibraryDrawer.Visibility == Visibility.Visible && ChaptersListBox.Visibility == Visibility.Visible)
            CloseDrawers();
        else
            OpenLibraryDrawer(showChaptersTab: true);
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsDrawer.Visibility == Visibility.Visible)
            CloseDrawers();
        else
            OpenSettingsDrawer();
    }

    private void ExpandWindowTemporarilyForDrawer()
    {
        if (Height < 330)
        {
            _savedCompactHeight ??= Height;
            Height = 350;
        }
        if (Width < 520)
        {
            Width = 540;
        }
    }

    private void OpenLibraryDrawer(bool showChaptersTab)
    {
        ExpandWindowTemporarilyForDrawer();
        SettingsDrawer.Visibility = Visibility.Collapsed;
        LibraryDrawer.Visibility = Visibility.Visible;

        SearchBox.Text = string.Empty;
        SwitchLibraryTab(showChaptersTab);
    }

    private void SwitchLibraryTab(bool showChapters)
    {
        if (showChapters)
        {
            BooksListBox.Visibility = Visibility.Collapsed;
            ChaptersListBox.Visibility = Visibility.Visible;
            BtnTabBooks.FontWeight = FontWeights.Normal;
            BtnTabChapters.FontWeight = FontWeights.Bold;
            RefreshChaptersList();
        }
        else
        {
            ChaptersListBox.Visibility = Visibility.Collapsed;
            BooksListBox.Visibility = Visibility.Visible;
            BtnTabBooks.FontWeight = FontWeights.Bold;
            BtnTabChapters.FontWeight = FontWeights.Normal;
            RefreshLibraryList();
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

    private void BtnTabBooks_Click(object sender, RoutedEventArgs e)
    {
        SwitchLibraryTab(showChapters: false);
    }

    private void BtnTabChapters_Click(object sender, RoutedEventArgs e)
    {
        SwitchLibraryTab(showChapters: true);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (BooksListBox.Visibility == Visibility.Visible)
            RefreshLibraryList();
        else
            RefreshChaptersList();
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

    private void BtnRemoveBook_Click(object sender, RoutedEventArgs e)
    {
        if (BooksListBox.SelectedItem is BookItem selected)
        {
            _storage.Library.Remove(selected);
            _storage.SaveImmediately();
            RefreshLibraryList();
        }
    }

    private async void BooksListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        await ActivateSelectedBookOrChapterAsync();
    }

    private async void BooksListBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await ActivateSelectedBookOrChapterAsync();
        }
    }

    private async void ChaptersListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        await ActivateSelectedBookOrChapterAsync();
    }

    private async void ChaptersListBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await ActivateSelectedBookOrChapterAsync();
        }
    }

    private async void BtnConfirmSelection_Click(object sender, RoutedEventArgs e)
    {
        await ActivateSelectedBookOrChapterAsync();
    }

    private async Task ActivateSelectedBookOrChapterAsync()
    {
        if (BooksListBox.Visibility == Visibility.Visible && BooksListBox.SelectedItem is BookItem book)
        {
            CloseDrawers();
            await LoadBookAsync(book);
        }
        else if (ChaptersListBox.Visibility == Visibility.Visible && ChaptersListBox.SelectedItem is ChapterItem chapter)
        {
            CloseDrawers();
            await LoadChapterAsync(chapter.Index, 0.0);
            _storage.ScheduleSave();
        }
    }

    #endregion

    #region 拖放匯入 (Drag & Drop .epub)

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
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

    #region 外觀、字型、雙層透明度設定事件

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

    private void ScrollStepSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        _storage.Settings.ScrollStepPixels = Math.Round(e.NewValue);
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
        _storage.Settings.AutoHideOnMouseLeave = AutoHideCheckBox.IsChecked == true;
        _storage.Settings.Topmost = TopmostCheckBox.IsChecked == true;
        _storage.Settings.HideFromAltTab = HideAltTabCheckBox.IsChecked == true;

        Topmost = _storage.Settings.Topmost;
        _win32Stealth.SetHideFromAltTab(_storage.Settings.HideFromAltTab);
        UpdateAutoHideButtonText();
        _storage.ScheduleSave();
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
        // 若正在暫時展開抽屜，不覆寫緊湊高度紀錄
        if (_savedCompactHeight == null)
        {
            _storage.Settings.WindowWidth = Width;
            _storage.Settings.WindowHeight = Height;
            _storage.ScheduleSave();
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
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
