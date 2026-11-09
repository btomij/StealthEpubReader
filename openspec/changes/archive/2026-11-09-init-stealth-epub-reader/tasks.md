## 1. 專案初始化與基礎架構

- [x] 1.1 建立 `.NET 10` WPF 專案 `src/StealthEpubReader/StealthEpubReader.csproj`，啟用 `UseWPF` 與 `UseWindowsForms`（支援原生系統匣 `NotifyIcon`），並安裝 `VersOne.Epub` 與 `HtmlAgilityPack` 套件。
- [x] 1.2 建立資料模型：`BookItem`（書籍資訊與章節進度）、`ChapterContent`（解析後的輕量章節段落）、`AppSettings`（字型、字級、行距、背景與文字獨立透明度、顏色、保護色主題、視窗座標尺寸、老闆鍵與滑鼠自動透明設定）。

## 2. 核心服務層實作 (`Services` & `Core`)

- [x] 2.1 實作 `StorageService`：負責 `settings.json` 與 `library.json` 的非同步載入、500ms 防抖 (Debounce) 自動儲存與強制同步落盤。
- [x] 2.2 實作 `EpubEngineService`：封裝 `VersOne.Epub` 按需載入、XHTML 轉純文字段落（自動過濾 `<img>`/`<svg>` 圖片防穿幫）、TOC 目錄提取，以及上一章/下一章背景預載快取 (`ConcurrentDictionary`)。
- [x] 2.3 實作 `Win32StealthService`：封裝 Win32 `RegisterHotKey` 全域老闆鍵（預設 `Alt+Q`）、`WS_EX_TOOLWINDOW`（隱藏 `Alt+Tab` 圖示）與系統匣 (`NotifyIcon`) 低調控制選單。
- [x] 2.4 實作 `ThemeAndFontService`：讀取 Windows 系統字型清單（置頂等寬程式碼字型如 `Consolas`、`Cascadia Mono` 與常用中文字型），管理上班保護色預設集（IDE 註解風、Office 報表風、幽靈全透明風、護眼深色風）與雙層獨立透明度計算。

## 3. 防抓包主視窗與互動面板實作 (`Views`)

- [x] 3.1 實作無邊框偽裝主視窗 `MainWindow.xaml`：支援任意尺寸拖曳縮放（含極窄單行狀態列模式）、滑鼠移開自動透明 (Auto-Stealth)、檔案拖放 (Drag & Drop `.epub`) 匯入。
- [x] 3.2 實作多元閱讀與翻頁控制：同時支援「滾輪/上下鍵平滑捲動」、「左右鍵/PageUp/PageDown 翻頁與跨章節無縫切換」及「Ctrl+滾輪調字級 / Alt+滾輪調文字透明度」。
- [x] 3.3 實作極簡無封面「圖書館與目錄抽屜面板」：支援快速切書（顯示進度 `%` 與最後閱讀時間）、關鍵字搜尋、匯入/移除書籍與章節跳轉。
- [x] 3.4 實作「外觀與偽裝設定面板」：支援系統字型下拉切換、字體大小/行距調整、背景透明度 (`0%~100%`) 與文字透明度 (`10%~100%`) 獨立滑桿、自訂色碼與一鍵套用上班保護色主題。

## 4. 驗證與測試樣本建立

- [x] 4.1 內建或自動產生一本測試用輕量範例 EPUB 書籍（讓使用者首次開啟即可立即體驗換字型、調透明度、切章節與老闆鍵功能）。
- [x] 4.2 執行專案編譯 (`dotnet build`) 與 OpenSpec 驗證 (`openspec validate`)，確認零錯誤並更新任務完成狀態。
