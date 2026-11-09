## Context

本專案旨在打造一款專為辦公室隱蔽閱讀（上班偷看）設計的輕量級 C# EPUB 閱讀器 `StealthEpubReader`。傳統桌面端閱讀器（如 Calibre、Thorium 或基於 WebView2 的閱讀器）存在啟動慢、記憶體佔用高、視窗邊框明顯、縮放時白閃卡頓，以及調整視窗透明度時文字跟著變淡難以辨識等問題。
因此，我們選用 `.NET 10` 搭配 **WPF (Windows Presentation Foundation)** 原生渲染架構，結合 Win32 底層 API，實現極低資源佔用與毫秒級反應速度。

## Goals / Non-Goals

**Goals:**
- **極致輕量與零卡頓**：採用 WPF 原生排版與章節懶加載 + 鄰近章節預載快取，避免任何內嵌瀏覽器引擎。
- **高隱蔽性防抓包**：無邊框視窗、隱藏工作列圖示、低調系統匣控制、Win32 全域老闆鍵 (`Alt+Q`) 瞬隱、以及「滑鼠移開自動透明 (Auto-Stealth)」機制。
- **高自由度外觀偽裝**：背景透明度 (`0%~100%`) 與文字透明度 (`10%~100%`) 雙層獨立控制；支援讀取並切換所有 Windows 系統字型（含等寬程式碼字型偽裝）；內建多組上班保護色預設集。
- **無感進度記憶與極簡圖書館**：自動非同步防抖儲存每本書的精準捲動位置，並提供無封面的極簡圖書館快速切書。

**Non-Goals:**
- 不實作複雜的 EPUB3 互動式 JavaScript、音訊/視訊播放或滿版漫畫圖片渲染（預設過濾圖片以防穿幫）。
- 不實作雲端帳號同步或肥大的 SQL 資料庫（統一使用本地輕量 JSON 檔案）。

## Decisions

### 1. UI 框架選擇：WPF (.NET 10) + WinForms NotifyIcon 混合支援
- **決定**：使用 `.NET 10` (`net10.0-windows`) WPF 作為主框架，並啟用 `<UseWindowsForms>true</UseWindowsForms>` 以直接使用原生穩定的 `System.Windows.Forms.NotifyIcon` 作為系統匣圖示。
- **原因**：WPF 原生支援 `AllowsTransparency="True"` 與 `WindowStyle="None"`，可將視窗背景設為 `#01000000`（近乎完全透明但仍可接收滑鼠滾輪與懸停事件）並將文字獨立設為任意不透明度；同時內建 `System.Windows.Forms.NotifyIcon` 無需額外引入第三方 Tray 套件即可穩定支援系統匣。

### 2. EPUB 解析與章節快取策略：`VersOne.Epub` + `HtmlAgilityPack`
- **決定**：使用 `VersOne.Epub` 的 `EpubReader.OpenBookAsync(filePath)` 只讀取書籍結構索引，不一次性載入所有內容；當切換至第 `i` 章時，僅解析第 `i` 章 HTML 為純文字段落清單，並透過背景 `Task` 預載第 `i-1` 與 `i+1` 章至 `ConcurrentDictionary<int, ChapterContent>`。
- **原因**：即使面對 20MB+、上千章的網路小說 EPUB，啟動與切書依然能在數毫秒內完成，翻章節完全無感。

### 3. 閱讀器渲染控制項：`ScrollViewer` + `ItemsControl` / `TextBlock` 段落流
- **決定**：將章節 HTML 清理為結構化段落清單，使用帶平滑/可調步進的 `ScrollViewer` 呈現，並透過紀錄 `ScrollViewer.VerticalOffset` 與 `ScrollableHeight` 計算精準百分比 (`ScrollRatio`)。
- **原因**：相較於 `FlowDocumentPageViewer` 在極小視窗（如單行/雙行狀態列尺寸）下容易因分頁計算而裁切文字，基於 `ScrollViewer` 的流式渲染能完美適應從「高 35px 的單行字幕條」到「一般小視窗」的任何比例，且字型、字級、行距、顏色綁定皆能即時響應。

### 4. 全域老闆鍵與自動隱形機制：Win32 `RegisterHotKey` + 視窗 `MouseEnter`/`MouseLeave`
- **決定**：透過 `HwndSource` 攔截 `WM_HOTKEY` 訊息處理全域快捷鍵（預設 `Alt+Q`，亦支援一鍵切換為其他組合鍵），直接呼叫 `Window.Hide()` / `Window.Show()` 並搭配 `WS_EX_TOOLWINDOW` 擴充樣式避免出現在 `Alt+Tab`。
- **原因**：Win32 原生熱鍵即使在全螢幕 Excel、VS Code 或瀏覽器中也能 0 延遲觸發，且 `Hide()` 不經過最小化動畫，完全不會被察覺。

## Risks / Trade-offs

- **[Risk] 背景透明度設為 0% 時滑鼠點擊穿透問題** → **Mitigation**：WPF 中完全透明 (`#00000000`) 的像素會讓滑鼠事件穿透到底層視窗，導致無法在閱讀器上滾動滑鼠滾輪或觸發 `MouseEnter`。因此當使用者將背景透明度設為 `0%` 時，底層實際套用 `Alpha = 1` (`#01000000`)，肉眼 100% 不可見，但仍能正常攔截滑鼠滾輪與「滑鼠移入自動顯現」事件；另外提供「滑鼠穿透鎖定模式」供進階需求使用。
- **[Risk] EPUB 檔案搬移或刪除導致啟動失敗** → **Mitigation**：啟動與切書時檢查 `File.Exists(book.FilePath)`，若原始檔案不存在則優雅提示並保留圖書館清單供使用者重新選擇或開啟其他書籍。
