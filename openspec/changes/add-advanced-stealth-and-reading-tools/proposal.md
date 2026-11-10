## Why

在辦公室隱蔽閱讀情境中，除了基礎的無邊框與雙層透明度外，使用者仍面臨四大進階風險與痛點：(1) 臨時被要求 Teams/Zoom 分享整個螢幕或截圖時視窗曝光；(2) 同事走近細看時純文章段落仍可能被識破；(3) 老闆已經走到背後時「視窗突然消失」反而引人起疑；(4) 需要一邊在底層軟體打字操作、一邊自動或穿透閱讀，以及在長篇小說中快速搜尋角色/關鍵字與標記書籤。

## What Changes

- **新增防截圖與防螢幕分享曝光 (`SetWindowDisplayAffinity`)**：呼叫 Windows 原生 `WDA_EXCLUDEFROMCAPTURE` API，開啟時讓閱讀器在截圖工具、錄影與 Teams/Zoom 螢幕分享畫面中 100% 自動隱形。
- **新增程式碼 / 系統 Log 偽裝外殼模式 (`camouflage-formatting`)**：支援將章節段落即時包裝成「系統執行 Log (`[INF] WorkerService...`)」或「C# 程式碼與註解結構」，就算旁人近看也像在看程式或除錯日誌。
- **新增老闆鍵「假報錯畫面 (Panic Fake StackTrace)」模式**：老闆鍵除了「瞬間隱藏視窗」外，支援切換為「瞬間替換成逼真的 C# / SQL Exception StackTrace 與系統診斷輸出」，避免視窗突然消失引起懷疑。
- **新增滑鼠點擊穿透幽靈模式 (`WS_EX_TRANSPARENT`) 與全域翻頁熱鍵**：開啟穿透後滑鼠點擊與滾輪直接穿透至底層 VS Code / Excel，並支援透過全域熱鍵（`Alt+J` / `Alt+K`）無焦點翻頁及 `Alt+G` 切換穿透狀態。
- **新增自動滾動巡航模式 (Auto-Scroll)**：支援設定每秒平滑自動捲動速度（快捷鍵 `F5` 或按鈕一鍵啟停），免動手滾動滑鼠。
- **新增書內關鍵字搜尋與書籤系統 (`search-and-bookmarks`)**：支援 `Ctrl+F` 跨章節/本章關鍵字搜尋並直接跳轉，以及 `Ctrl+D` 新增/管理閱讀書籤。
- **強化圖書館匯入管理 (`library-management`)**：匯入 `.epub` 時自動將檔案複製至本地書庫目錄（預設 `data/books/`，或使用者於設定中指定的自訂資料夾），並以低調 GUID 檔名 (`{BookId}.epub`) 存放，避免原始檔案被刪除後失效並防止系統檔名搜尋曝光；移除書籍時同步清理書庫副本。

## Capabilities

### New Capabilities
- `camouflage-formatting`: 程式碼註解偽裝外殼、系統 Log 日誌偽裝外殼與老闆鍵逼真假報錯畫面 (Panic StackTrace) 產生器。
- `search-and-bookmarks`: 全書/章節關鍵字檢索定位 (`Ctrl+F`) 與多書籤標記管理 (`Ctrl+D`)。

### Modified Capabilities
- `stealth-window`: 新增防截圖/防螢幕分享 (`WDA_EXCLUDEFROMCAPTURE`)、滑鼠點擊穿透模式 (`WS_EX_TRANSPARENT` + 全域熱鍵 `Alt+G`/`Alt+J`/`Alt+K`)，以及老闆鍵行為模式切換（隱藏視窗 vs. 假報錯畫面）。
- `epub-engine`: 新增可調速之自動滾動巡航模式 (Auto-Scroll)。
- `library-management`: 匯入書籍時自動複製檔案至本地或指定書庫路徑並偽裝檔名，移除書籍時同步刪除託管副本。

## Impact

- 擴充 `Win32StealthService`：新增 `SetWindowDisplayAffinity`、`WS_EX_TRANSPARENT` 穿透切換，以及全域穿透切換/無焦點翻頁熱鍵 (`Alt+G`, `Alt+J`, `Alt+K`)。
- 新增 `CamouflageService`：提供「無偽裝」、「系統 Log 模式」、「C# 程式碼註解模式」段落轉換與「假報錯 StackTrace」產生。
- 擴充 `EpubEngineService`、`BookItem`、`AppSettings` 與 `MainWindow`：支援全書關鍵字搜尋、書籤列表 (`Bookmarks`) 與 `DispatcherTimer` 自動滾動巡航。
