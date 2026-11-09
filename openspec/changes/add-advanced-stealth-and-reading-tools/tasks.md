## 1. 模型與狀態擴充 (`Models`)

- [x] 1.1 擴充 `AppSettings`：新增 `ExcludeFromCapture`（防截圖/防螢幕分享，預設 `true`）、`CamouflageMode`（`None` / `SystemLog` / `CSharpCode`）、`BossKeyAction`（`HideWindow` / `FakeException`）、`AutoScrollSpeed`（自動滾動速度）。
- [x] 1.2 新增 `BookmarkItem` 與 `SearchResultItem` 模型，並於 `BookItem` 中加入 `Bookmarks` 清單以持久化儲存每本書的書籤。

## 2. 進階防抓包與偽裝服務實作 (`Services`)

- [x] 2.1 擴充 `Win32StealthService`：
  - 新增 `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` 防截圖/防螢幕分享 API。
  - 新增 `SetClickThrough(bool enable)` (`WS_EX_TRANSPARENT`) 滑鼠穿透切換 API。
  - 註冊全域輔助熱鍵：`Alt+G`（切換滑鼠穿透）、`Alt+J`（穿透時無焦點向下滾動）、`Alt+K`（穿透時無焦點向上滾動），並於系統匣選單加入「切換滑鼠穿透」選項。
- [x] 2.2 新增 `CamouflageService`：實作將章節段落包裝為「系統執行 Log 格式」與「C# 程式碼與註解格式」的轉換器，以及逼真的「C# / SQL Exception StackTrace 假報錯畫面」產生器。
- [x] 2.3 擴充 `EpubEngineService`：新增 `SearchInBookAsync(string keyword)` 支援跨章節關鍵字快速搜尋並回傳段落預覽與相對定位比例。

## 3. UI 整合與互動實作 (`MainWindow`)

- [x] 3.1 整合自動滾動巡航模式 (`DispatcherTimer`)：支援 `F5` 或頂部微工具列 `[巡航]` 按鈕一鍵啟停，並在設定面板提供自動滾動速度滑桿。
- [x] 3.2 整合偽裝外殼切換 (`Ctrl+L` 或頂部 `[偽裝]` 按鈕)、滑鼠穿透 (`Alt+G` 或頂部 `[穿透]` 按鈕) 與老闆鍵「假報錯畫面」模式。
- [x] 3.3 擴充圖書館抽屜面板：新增 **`🔖 書籤 (Ctrl+D)`** 與 **`🔍 全書搜尋 (Ctrl+F)`** 分頁，支援一鍵打書籤、跨章節搜尋關鍵字並精準跳轉至目標段落位置。
- [x] 3.4 執行 `dotnet build -c Release` 與 `openspec validate` 驗證零錯誤。
