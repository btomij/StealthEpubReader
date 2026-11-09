# reading-progress Specification

## Purpose
TBD - created by archiving change init-stealth-epub-reader. Update Purpose after archive.
## Requirements
### Requirement: 書籍獨立閱讀進度精準記錄
The system SHALL 為書庫中的每一本書獨立記錄其當前章節索引 (`CurrentChapterIndex`)、章節內捲動偏移比例 (`ScrollProgressRatio` / `VerticalOffset`)、閱讀百分比 (`OverallPercentage`) 及最後閱讀時間。

#### Scenario: 在不同書籍間切換時保留各自進度
- **WHEN** 使用者在書籍 A 閱讀至第 5 章 40% 處，切換至書籍 B 閱讀，隨後再切回書籍 A
- **THEN** 書籍 A 精準恢復至第 5 章 40% 的捲動位置

### Requirement: 非同步防抖存檔與老闆鍵強制存檔
The system SHALL 在閱讀位置變更後透過防抖機制（Debounce 500ms）於背景非同步寫入本地 JSON，並在觸發老闆鍵隱藏、切換書籍或關閉程式時立即保存當前進度與視窗座標尺寸。

#### Scenario: 捲動後自動無感存檔
- **WHEN** 使用者滾動閱讀內容並停止操作達 500 毫秒
- **THEN** 系統在背景執行緒更新本地進度檔，且不影響 UI 流暢度

### Requirement: 啟動即自動續讀上次位置
The system SHALL 在應用程式啟動時自動載入上次閱讀的書籍、章節與捲動位置，並還原上次的視窗螢幕座標、尺寸、字型與外觀透明度設定。

#### Scenario: 重新啟動閱讀器直接續讀
- **WHEN** 使用者關閉閱讀器後重新啟動程式
- **THEN** 閱讀器直接以先前的視窗位置、大小、字型與配色開啟，並定位於上次離開時的那本書與那一行文字

