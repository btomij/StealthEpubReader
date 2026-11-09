## ADDED Requirements

### Requirement: 防螢幕分享與防截圖曝光 (Screen Capture Exclusion)
The system SHALL 透過 Win32 `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` 提供「防截圖 / 防螢幕分享」開關（預設啟用），使閱讀器視窗在螢幕截圖、錄影與遠端會議桌面分享中完全隱形。

#### Scenario: 啟用防截圖時進行螢幕分享或截圖
- **WHEN** 使用者啟用「防截圖/防分享」功能，並使用截圖工具或會議軟體分享整個桌面
- **THEN** 使用者本機螢幕仍可正常閱讀文字，但截圖與遠端分享畫面中完全不會顯示閱讀器視窗

### Requirement: 老闆鍵假報錯畫面切換模式 (Panic Fake StackTrace)
The system SHALL 允許使用者設定老闆鍵觸發行為為「瞬間隱藏視窗」或「瞬間切換為假報錯/診斷畫面 (Panic Screen)」；當設為假報錯畫面時，觸發老闆鍵或緊急鍵 SHALL 立即將閱讀內文替換為逼真的程式例外 StackTrace 與系統日誌，再次觸發時無縫恢復原本的小說段落與捲動位置。

#### Scenario: 觸發老闆鍵切換為假報錯畫面
- **WHEN** 使用者將老闆鍵模式設為「假報錯畫面」並按下老闆鍵 (`Alt+Q`)
- **THEN** 閱讀器視窗保持顯示，但內文與狀態列瞬間替換為 C# / SQL Exception StackTrace 除錯資訊；再次按下 `Alt+Q` 時立即還原至先前閱讀位置

### Requirement: 滑鼠點擊穿透幽靈模式與全域無焦點翻頁
The system SHALL 提供「滑鼠點擊穿透模式 (`WS_EX_TRANSPARENT`)」及全域切換熱鍵 (`Alt+G`)；啟用穿透時，滑鼠點擊與滾輪事件 MUST 直接穿透至底層視窗，且使用者可透過全域熱鍵 (`Alt+J` 向下、`Alt+K` 向上) 在不切換視窗焦點的情況下控制閱讀器捲動。

#### Scenario: 開啟滑鼠穿透並操作底層軟體同時翻頁
- **WHEN** 使用者按下 `Alt+G`（或點擊穿透按鈕）開啟滑鼠穿透模式
- **THEN** 滑鼠在閱讀器範圍內的點擊直接作用於底層的 Excel 或 IDE，且按下 `Alt+J` / `Alt+K` 可直接控制懸浮的閱讀器上下捲動
