## Why

一般桌面端 EPUB 閱讀器多採用內嵌瀏覽器（如 WebView2）且介面顯眼，不僅記憶體佔用高、視窗縮放與切換時容易白閃卡頓，在辦公室環境下極易曝光。因此需要建立一款以 C# WPF 原生渲染為核心、具備「背景與文字雙層獨立透明度」、「自由切換系統字型」、「毫秒級全域老闆鍵與滑鼠離開自動隱形」、「極速無卡頓章節預載」與「極簡圖書館切換」的輕量級防抓包 EPUB 閱讀器。

## What Changes

- 建立 C# (.NET 10 WPF) 輕量級 EPUB 閱讀器專案 `StealthEpubReader`。
- 新增無邊框偽裝視窗系統，支援全域熱鍵（預設 `Alt+Q`）毫秒級隱藏/喚醒、低調系統匣 (System Tray) 圖示、隱藏工作列圖示，以及「滑鼠移開自動透明」開關。
- 新增外觀與字型自訂系統，支援讀取 Windows 系統字型（含等寬程式碼字型偽裝）、背景與文字獨立透明度、自訂配色與「上班保護色」預設主題（IDE 註解風、Office 報表風、幽靈全透明風）。
- 新增無卡頓 EPUB 閱讀引擎（基於 `VersOne.Epub` 與 `HtmlAgilityPack`），採用章節按需載入與鄰近章節背景預載快取，預設過濾圖片防穿幫，並同時支援「滾輪/上下鍵捲動」與「左右鍵翻頁/切換章節」。
- 新增無感閱讀進度記憶系統，自動非同步防抖儲存每本書的章節與捲動偏移量，啟動即自動續讀上次位置。
- 新增極簡圖書館管理系統，支援拖放匯入 `.epub`、顯示閱讀進度百分比與快速切書。

## Capabilities

### New Capabilities
- `stealth-window`: 無邊框透明偽裝視窗、全域老闆鍵 (`Alt+Q`)、系統匣控制、工作列隱藏與滑鼠移開自動隱形機制。
- `appearance-customization`: 系統字型自由切換、背景與文字雙層獨立透明度、自訂色彩、保護色預設集、字級與行距即時調整。
- `epub-engine`: 極速無卡頓 EPUB 解析、章節懶加載與預載快取、純文字防穿幫排版、滾輪/方向鍵多元翻頁操作。
- `reading-progress`: 書籍獨立閱讀進度（章節與捲動位置）與視窗狀態記憶、非同步防抖存檔、啟動直接續讀。
- `library-management`: 極簡無封面圖書館面板、拖放與檔案匯入、進度百分比顯示與毫秒級切書。

### Modified Capabilities

## Impact

- 新增 `src/StealthEpubReader` WPF 應用程式與相關服務層 (`Core`, `Models`, `Services`, `Views`)。
- 引入輕量級開源套件：`VersOne.Epub`（EPUB 解析）與 `HtmlAgilityPack`（XHTML 輕量提取）。
- 本地狀態儲存於應用程式目錄或使用者 AppData 下的 `library.json` 與 `settings.json`。
