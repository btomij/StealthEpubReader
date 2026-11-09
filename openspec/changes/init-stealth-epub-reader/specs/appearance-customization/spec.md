## ADDED Requirements

### Requirement: 系統字型自由切換
The system SHALL 讀取 Windows 系統已安裝之所有字型清單（並優先置頂常用閱讀與等寬程式碼偽裝字型如 `Consolas`、`Cascadia Mono`、`Microsoft JhengHei`、`PMingLiU`），允許使用者即時切換閱讀器文字字型並持久化儲存。

#### Scenario: 切換閱讀字型為等寬程式碼字型
- **WHEN** 使用者在設定面板或字型選單中選擇 `Consolas`（或任意系統已安裝字型）
- **THEN** 閱讀器內文與狀態列立即套用該字型渲染，且不遺失當前閱讀捲動位置

### Requirement: 背景與文字雙層獨立透明度調整
The system SHALL 分別提供「背景透明度 (`BackgroundOpacity`, 0%~100%)」與「文字透明度 (`TextOpacity`, 10%~100%)」獨立控制滑桿與快捷鍵，使背景可設為全透明而不影響文字清晰度。

#### Scenario: 設定背景全透明並保留清晰文字
- **WHEN** 使用者將背景透明度調整為 `0%` 並將文字透明度設為 `75%`
- **THEN** 視窗背景完全透明（可直接看到底層 IDE 或 Excel 畫面，並在視窗範圍內支援滾輪操作），而文字保持 `75%` 不透明度清晰顯示

### Requirement: 自訂顏色與上班保護色預設集
The system SHALL 支援自訂背景色與文字色（HEX 色碼與調色盤），並內建一鍵切換的「上班保護色」預設主題（包含「IDE 深色註解風」、「Office 淺色報表風」、「幽靈全透明懸浮風」、「護眼羊皮紙風」）。

#### Scenario: 一鍵套用 IDE 深色註解風保護色
- **WHEN** 使用者點選「IDE 深色註解風」預設主題
- **THEN** 背景色立即切換為 `#1E1E1E`、文字色切換為程式碼註解綠 `#6A9955`（或暗灰），瞬間融入深色開發環境

### Requirement: 字體大小、行距與寬度邊距即時微調
The system SHALL 允許使用者即時調整字體大小（如 `10pt`~`36pt`）、行高倍數與內文邊距，並支援透過快捷鍵（如 `Ctrl + 滾輪` 調字級、`Alt + 滾輪` 調文字透明度）快速微調。

#### Scenario: 透過快捷鍵即時調整文字大小
- **WHEN** 使用者在閱讀視窗按住 `Ctrl` 並滾動滑鼠滾輪
- **THEN** 內文字體大小立即放大或縮小，並自動更新設定檔
