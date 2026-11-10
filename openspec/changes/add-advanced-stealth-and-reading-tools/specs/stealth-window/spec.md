## ADDED Requirements

### Requirement: 防螢幕分享與防截圖曝光 (Screen Capture Exclusion)
The system SHALL 透過 Win32 `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` 提供「防截圖 / 防螢幕分享」開關（預設啟用），使閱讀器視窗在螢幕截圖、錄影與遠端會議桌面分享中完全隱形。

#### Scenario: 啟用防截圖時進行螢幕分享或截圖
- **WHEN** 使用者啟用「防截圖/防分享」功能，並使用截圖工具或會議軟體分享整個桌面
- **THEN** 使用者本機螢幕仍可正常閱讀文字，但截圖與遠端分享畫面中完全不會顯示閱讀器視窗

### Requirement: 老闆鍵假報錯畫面切換與可滾動檢視模式 (Panic Fake StackTrace)
The system SHALL 允許使用者設定老闆鍵觸發行為為「瞬間隱藏視窗」或「瞬間切換為假報錯/診斷畫面 (Panic Screen)」；當設為假報錯畫面時，觸發老闆鍵或緊急鍵 SHALL 立即將閱讀內文替換為百行以上的多頁逼真程式例外 StackTrace 與系統診斷日誌，並允許使用者透過滑鼠滾輪與方向鍵正常上下滾動檢視假報錯內容（不影響原書籍進度），再次觸發時無縫恢復原本的小說段落與捲動位置。

#### Scenario: 觸發老闆鍵切換為假報錯畫面並滾動檢視
- **WHEN** 使用者將老闆鍵模式設為「假報錯畫面」、按下老闆鍵 (`Alt+Q`) 並滾動滑鼠滾輪
- **THEN** 閱讀器內文與狀態列瞬間替換為長篇 C# / SQL / gRPC Exception StackTrace 除錯日誌且隨滾輪平滑上下滾動；再次按下 `Alt+Q` 時立即還原至先前的小說閱讀位置

### Requirement: 滑鼠點擊穿透幽靈模式與全域無焦點翻頁
The system SHALL 提供「滑鼠點擊穿透模式 (`WS_EX_TRANSPARENT`)」及多重防漏接全域切換熱鍵 (`Alt+G` 與 `F8`，結合底層鍵盤狀態偵測與 `SWP_FRAMECHANGED` 視窗快取刷新)；啟用穿透時，滑鼠點擊與滾輪事件 MUST 直接穿透至底層視窗，再次按下 `Alt+G`（包含按住 `Alt` 連續按 `G`）或 `F8` 時 MUST 立即解除穿透並恢復滑鼠互動，且穿透期間可透過全域熱鍵 (`Alt+J` 向下、`Alt+K` 向上) 無焦點控制閱讀器捲動。

#### Scenario: 開啟滑鼠穿透後再按 Alt+G 立即切換回來
- **WHEN** 使用者按下 `Alt+G`（或點擊穿透按鈕）進入滑鼠穿透模式，操作底層軟體後再次按下 `Alt+G`（或 `F8`）
- **THEN** 系統立即偵測到按鍵並解除 `WS_EX_TRANSPARENT` 穿透狀態，視窗立即恢復可點擊、可拖曳與可滾輪捲動狀態
