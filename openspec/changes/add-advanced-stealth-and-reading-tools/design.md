## Context

在完成第一版輕量防抓包 EPUB 閱讀器後，使用者希望進一步強化四大進階隱蔽能力（防截圖/螢幕分享、Log/程式碼文字偽裝外殼、老闆鍵假報錯畫面、滑鼠穿透幽靈模式）與兩大閱讀輔助工具（自動滾動巡航、關鍵字搜尋與書籤）。所有新增功能皆須維持純原生、零外部肥大依賴與毫秒級反應。

## Goals / Non-Goals

**Goals:**
- **硬體/OS 級防截圖與防螢幕分享**：透過 Win32 `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)` 實現對自己可見、對螢幕擷取/分享完全隱形。
- **文字級視覺偽裝**：提供即時切換的「系統 Log 偽裝外殼」與「C# 程式碼註解偽裝外殼」，以及觸發老闆鍵時瞬間切換的「逼真假報錯 (Panic Fake StackTrace)」。
- **無焦點穿透閱讀**：透過 `WS_EX_TRANSPARENT | WS_EX_LAYERED` 讓滑鼠操作穿透到底層辦公軟體，同時透過全域快捷鍵 (`Alt+G` 切換穿透、`Alt+J`/`Alt+K` 上下滾動) 控制閱讀。
- **免動手自動滾動與快速檢索**：內建平滑計時器自動滾動 (Auto-Scroll) 與全書關鍵字搜尋 (`Ctrl+F`)、書籤紀錄 (`Ctrl+D`)。

**Non-Goals:**
- 不使用會拖慢效能的 OCR 或高耗能 Hook。
- 全書搜尋採用記憶體非同步掃描，不建立肥大的外部全文檢索資料庫。

## Decisions

### 1. 防螢幕分享與截圖：`user32.dll!SetWindowDisplayAffinity`
- **決定**：使用 Windows 10 2004+ / Windows 11 支援之 `WDA_EXCLUDEFROMCAPTURE (0x00000011)`，預設開啟並可在設定面板與頂部工具列切換。
- **原因**：此為作業系統 DWM (Desktop Window Manager) 原生合成層級功能，效能開銷為 0，且能確保 Teams、Zoom、OBS、Windows 截圖工具 (`Win+Shift+S`) 完全拍不到閱讀器。

### 2. 滑鼠點擊穿透與無焦點控制：`WS_EX_TRANSPARENT` + 全域熱鍵
- **決定**：啟用滑鼠穿透時，為視窗加上 `WS_EX_TRANSPARENT` 樣式；為避免開啟穿透後無法用滑鼠點回閱讀器，註冊全域熱鍵 `Alt+G`（一鍵開關滑鼠穿透）以及全域無焦點滾動熱鍵 `Alt+J`（往下滾動）/ `Alt+K`（往上滾動），同時系統匣選單亦可隨時關閉穿透。
- **原因**：讓使用者雙手在 Excel 或 IDE 打字時，視窗即使浮在正中間也完全不擋滑鼠點擊。

### 3. 程式碼 / Log 偽裝外殼與假報錯畫面：`CamouflageService`
- **決定**：在不修改原始 EPUB 檔案與進度計算的前提下，於渲染層透過 `CamouflageService` 將章節段落即時格式化為：
  - `None`：原始純文字段落
  - `SystemLog`：`[2026-11-09 17:12:04.182 INF] [Thread-08] DataPipeline: <小說段落>`
  - `CSharpCode`：穿插逼真的 C# 命名空間、非同步方法簽章與 `// <小說段落>` 註解
  - 老闆鍵可選「直接隱藏視窗」或「切換為假報錯 StackTrace 畫面 (`PanicFakeScreen`)」。

### 4. 自動滾動 (Auto-Scroll) 與全書搜尋/書籤
- **決定**：使用 `DispatcherTimer`（間隔 50ms，可調整每秒像素速度 `AutoScrollSpeed`）進行平滑微幅滾動；遇到操作老闆鍵或打開抽屜時自動暫停。書籤與搜尋整合至抽屜面板（`Ctrl+F` 開啟搜尋、`Ctrl+D` 快速加入當前章節與捲動百分比書籤）。

## Risks / Trade-offs

- **[Risk] 開啟滑鼠穿透 (`WS_EX_TRANSPARENT`) 後使用者忘記如何點回視窗** → **Mitigation**：提供全域熱鍵 `Alt+G` 隨時解除穿透、按下老闆鍵喚醒時自動解除穿透，並在系統匣右鍵選單提供「解除滑鼠穿透」選項。
