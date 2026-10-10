# 拓樸編輯器外觀統一（子專案 E）— 設計規格

- 日期：2026-10-10
- 分支：`claude/ui-design-improvement-0a0ca4`（D 已合併至本機 `main` 的 `31ee644`）
- 範圍：全面 UI 翻新子專案 E：專案工作區（拓樸編輯器）視窗與它開出的對話框
- 前置：`docs/superpowers/specs/2026-10-09-ui-theme-and-track-diagram-design.md`（`UiTheme`、配線圖軌道配色）、`docs/superpowers/specs/2026-10-10-main-window-shell-design.md`（`Themes/Controls.xaml` 按鈕與分段標籤）、`docs/superpowers/specs/2026-10-10-result-pages-design.md`（結果頁標題、卡片、`StatusTones`）
- 狀態：設計已於對話中逐段確認，待使用者審閱本文件

## 1. 目標與範圍

使用者選擇「統一外觀」：保留編輯器的三欄結構（左導覽、中間頁面、右側選取與驗證）與所有功能，套用主視窗的色票與元件樣式；實作方式選擇「共用樣式＋小工具」。

| 畫面 | 程式 | 本輪 |
|---|---|---|
| 專案工作區外殼（頁首、導覽、右側面板、底部列） | `TopologyEditorWindow.cs` `BuildShell` | ✅ |
| 10 個導覽頁：總覽、車站與月台、軌道與設施、車型、服務與路徑、停站模式、班表與接續、模擬設定、進階資料、驗證訊息 | `TopologyEditorWindow.cs`、`.Stations.cs`、`.Operations.cs`、`.Settings.cs` | ✅ |
| 由其他入口開啟的頁面：快速起稿、路線示意圖（「開啟完整配線預覽」）、結果 | `ShowQuickBuilder`、`ShowSchematic`、`ShowResults` | ✅ |
| 路線示意圖軌道繪製 | `TopologyEditorWindow.cs` `DrawSchematic` | ✅ |
| 「新增／編輯」「選取」小對話框 | `TopologyEditorWindow.cs` `Ask`、`Choose` | ✅ |
| 舊檔接軌側別遷移對話框 | `LegacyPortMigrationDialog.cs` | ✅ |
| 讀檔進度視窗 | `ProjectLoadProgressWindow.cs` | ✅ |
| 系統訊息框（`MessageBox`） | Windows 繪製 | 不在本輪（無法自訂） |
| `TopologyJsonEditorWindow` | 已退役的轉接類別，沒有畫面 | 不需處理 |

成功標準：

1. 編輯器與對話框的顏色全部取自 `UiTheme`，程式中不再有寫死色值（目前約 35 處）。
2. 按鈕、分段標籤、卡片、清單、表格、頁面標題與主視窗／結果頁同一套樣式，滑鼠移上、停用、鍵盤焦點的效果一致。
3. 路線示意圖的軌道顏色與主視窗配線圖相同（下行橘、上行藍、側線淡色、中性灰）。
4. 功能、資料、驗證、存檔、頁面清單與順序不變；既有編輯器測試照常通過。

不在本輪：版面重排成主視窗外殼（深色標題列、圖示導覽列）、新增編輯功能、可編輯表格套用結果頁欄位規則。

## 2. `EditorChrome` 與編輯器樣式

新增 `src/MrtRouteSimulator.App/EditorChrome.cs`（`public static class`，與 `ChartPainter` 相同理由：測試專案沒有 `InternalsVisibleTo`），集中建立編輯器與對話框的共用元件：

| 成員 | 說明 |
|---|---|
| `void ApplyWindowChrome(Window)` | 套用 App 字型、`TextStrong` 前景與 `WindowBackground` 底色（WPF 不會把 `TargetType="Window"` 的隱含樣式套到子類別） |
| `StackPanel PageHeader(string title, string description)` | 頁面標題（結果頁標題樣式）＋灰色說明 |
| `TextBlock SectionTitle(string)` | 頁內小標題（卡片標題樣式，13 px 半粗） |
| `TextBlock Hint(string)` | 灰色說明文字（`TextMuted`，自動換行） |
| `Border Card(UIElement child, string? title = null)` | 白底圓角卡片（結果卡片樣式，內距 10）；有標題時標題在左上 |
| `Button Button(string label, RoutedEventHandler handler, bool primary)` | 套 `PrimaryButton`／`SecondaryButton` 樣式，不設任何顏色 |
| `TabControl Tabs()`、`TabItem Tab(string header, object content)` | 膠囊式分段標籤；`Tab` 同時設定 `Header` 與 `ShellNav.ShortLabel`（分段樣式從後者讀文字） |
| `ListBox List()` | 編輯器清單樣式 |
| `TextBox SearchBox(string placeholder)` | 放大鏡圖示＋灰色提示字的搜尋框 |
| `Ellipse StatusDot(Brush)` | 8 DIP 狀態色點 |

`Themes/Controls.xaml` 新增具名樣式（只有指定時才套用，主視窗不受影響）：

| 樣式 | 用途 |
|---|---|
| `EditorNavList`、`EditorNavItem` | 左側導覽清單：白底卡片、項目高 32；選取為 `NavSelected` 底＋左側 3 DIP `Accent` 指示條＋`Accent` 字；滑鼠移上 `NavHover` |
| `EditorList`、`EditorListItem` | 頁內清單：白底、`Border` 圓角框、項目高 28；選取 `NavSelected` 底＋`Accent` 字；滑鼠移上 `NavHover` |
| `SearchBox` | 搜尋框：左側放大鏡圖示（`IconFont`），空白時顯示 `Tag` 提示字（`TextSubtle`） |
| `EditorGroupBox` | 群組框改為卡片外觀，標題放在卡片左上 |
| `ThinProgressBar` | 細長圓角進度條：底色 `Border`、填色 `Accent` |
| `IconButton` | 圖示按鈕（右側面板收合鈕），基於 `GhostButton` |

## 3. 視窗外殼

- 視窗：呼叫 `EditorChrome.ApplyWindowChrome`；最外層由白底改為 `WindowBackground`，內容放在白色卡片上。
- 頁首列：專案標題文字內容不變（例「14-大型-二十八站完整營運範例 · 格式版本 8 · 軌道與設施 · 草稿未套用」），字樣同主視窗標題；搜尋框改 `SearchBox`，提示字「搜尋頁面」；「收合」改為 `IconButton`，自動化名稱與提示依狀態為「收合右側面板」或「展開右側面板」。
- 左側導覽：`EditorNavList`；10 個項目文字與順序不變。
- 中間頁面：所有頁面的標題與說明由 `PageHeader` 建立。
- 右側面板：「選取項目」「驗證」各為一張卡片；驗證清單用 `EditorList`，每列前加嚴重度色點：錯誤 `Danger`、提醒 `Caution`、其他 `TextSubtle`。
- 底部列：驗證摘要前加狀態色點：「尚未驗證草稿」`TextSubtle`、「驗證通過」`Success`、有錯誤 `Danger`、只有提醒 `Caution`；「驗證」「取消」為次要按鈕，「套用」為主要按鈕（橘色，與主視窗「建立模擬」一致）。

## 4. 頁面內元件

- 頁面標題 15 px 半粗、說明 12 px 灰字（取代目前 22 px 標題）；頁內小標題（原 17 px，例「月台配置」「依參考圖建立站場」「選取項目」「驗證」）改 `SectionTitle`。
- 細框區塊（車站基本資料、月台細節、本站作業、折返／越行作業細節、班表細節、停站模式差異說明等）改 `Card`；「模擬設定」的 3 個群組框（常用設定、控制、安全）改 `EditorGroupBox`。
- 按鈕：`CreateButton`、`CreateNamedButton`（約 70 處）改由 `EditorChrome.Button` 建立。主要按鈕只限下列，其餘一律次要：
  - 底部「套用」
  - 對話框確認：「建立」「選取」（`Ask`／`Choose`）、「套用明確側別」（遷移對話框）
  - 會整份取代草稿的「建立格式版本 8 拓撲」「以此站型重新起稿」
- 分段標籤：編輯器內所有 `TabControl`（服務類型／服務路徑、班距計畫／手動班表與接續／展開預覽、進階資料各分頁等）改 `EditorChrome.Tabs`／`Tab`，分頁標題文字不變。
- 清單：車站清單、作業清單、參照多選清單、驗證清單改 `EditorList`。
- 表格：`CreateGrid` 與其他表格拿掉寫死的框色，讓 `Themes/Controls.xaml` 的表格樣式生效；可編輯表格不套 `ResultTable` 欄位規則（C2 已訂界線）。
- 搜尋框：頁首、車站搜尋、車型搜尋、軌道區段等頁內搜尋改 `SearchBox`，提示字依用途（「搜尋頁面」「搜尋車站」「搜尋車型」「搜尋名稱、ID 或關聯」）；原提示框文字保留。
- 文字顏色：說明與提示一律 `TextMuted`；摘要表（`SummaryGrid`）左欄標籤 `TextMuted`、右欄數值 `TextStrong` 半粗。

## 5. 路線示意圖

`DrawSchematic` 已與主視窗配線圖共用 `StationSchematicPresentation` 的月台、車站名、連接線與圖例，只有軌道仍用舊的藍綠色。改為：

- 軌道顏色依 `TrackRailStyle.Classify(edges, outboundEdgeIds, inboundEdgeIds)`（下行 `RailDown`、上行 `RailUp`、側線 `RailDownSoft`／`RailUpSoft`、其他 `RailNeutral`），以 `TrackRailStyle.Brush` 取畫筆、`TrackRailStyle.Thickness` 取線寬（正線 6、側線 4）；`DrawConnection` 的顏色依兩端軌道色調（相同時用該色，不同時用中性色），與配線圖一致。
- 方向箭頭填色跟隨所在軌道色；白色描邊改 `UiTheme.SurfaceBrush`；止衝擋線用中性色。
- 畫布底色 `CanvasBackground`；設施清單前的小方塊改 `RailNeutralStrong`；「自動排版」為次要按鈕。
- 排版、座標與自動排版邏輯不變。

## 6. 對話框

- `Ask`／`Choose`：建立對話框的程式抽成 `CreateAskDialog`、`CreateChooseDialog`（回傳 `Window` 與欄位，不顯示），`Ask`／`Choose` 只負責 `ShowDialog` 與取值；套用 `ApplyWindowChrome`、白底卡片、內距 20；欄位名稱用 `FieldLabel` 樣式；確認鈕主要、取消次要，靠右。
- `LegacyPortMigrationDialog`：套用 `ApplyWindowChrome`；說明文字 `TextMuted`；對照表表頭列 `TableHeader` 底色；按鈕改 `EditorChrome.Button`（「套用明確側別」主要，「保留相容讀取」「取消」次要）。
- `ProjectLoadProgressWindow`：套用 `ApplyWindowChrome`；標題 `TextStrong` 半粗、訊息 `TextMuted`；進度條 `ThinProgressBar`。

## 7. 相容性與不變條件

- Engine、專案 Schema、版本號不變；編輯器資料流、驗證、「套用／取消」語意、`ProjectWorkspacePage` 列舉值不變。
- 既有元件 `Name`、`AutomationProperties.Name`、按鈕文字、分頁標題、導覽文字保留（既有測試依此尋找元件）；可新增名稱，不得改名或移除。
- 介面縮放照常作用在編輯器與對話框。
- 主視窗不受影響：新增樣式皆為具名樣式。

## 8. 測試與驗證

新增 `tests/MrtRouteSimulator.WpfTests/EditorThemeTests.cs`（runner 旗標 `--editor-theme-only`，納入完整 runner）：

1. 外殼：字型與底色、導覽清單樣式、右側兩張卡片、底部按鈕主次、驗證摘要與清單色點對照。
2. 逐頁巡檢：依序開啟 10 個導覽頁與快速起稿、路線示意圖、結果頁，每頁檢查：按鈕都套共用樣式且沒有本地顏色；主要按鈕只出現在第 4 節清單；分頁一律膠囊式且每個標籤都有文字；清單用編輯器清單樣式；表格沒有本地框色；文字前景都來自 `UiTheme` 畫筆。
3. 路線示意圖：以 28 站範例繪製，軌道畫筆只能是 `TrackRailStyle.Brush` 的結果，且上行、下行都有出現。
4. 對話框：`CreateAskDialog`、`CreateChooseDialog`、`LegacyPortMigrationDialog`、`ProjectLoadProgressWindow` 直接建立後檢查樣式（不顯示對話框）。
5. 寫死顏色掃描：`TopologyEditorWindow*.cs`（4 檔）、`LegacyPortMigrationDialog.cs`、`ProjectLoadProgressWindow.cs`、`EditorChrome.cs` 全檔，不得出現 `Color.FromRgb`、`Color.FromArgb`、`Brushes.<色名>`、`Colors.<色名>`（`Transparent` 除外）。
6. 最小尺寸與縮放：980×640 與 980×640＋125% 介面縮放下，頁首、導覽、底部按鈕不被裁切。

驗收：

- 整個 solution Release build 0 warning／0 error；Engine runner 全過；完整 WPF runner 通過，含既有車站頁、營運頁、工作區往返、示意圖版面規則與輸入頁截圖測試。
- 截圖：`--shell-screenshots` 加拍編輯器總覽、車站與月台、軌道與設施、服務與路徑、班表與接續、路線示意圖，以及新增對話框、遷移對話框、讀檔進度視窗；與改版前逐張對照並交付使用者。

## 9. 風險

| 風險 | 緩解 |
|---|---|
| 分段標籤樣式從 `ShellNav.ShortLabel` 讀文字，編輯器分頁沒設會變空白 | `EditorChrome.Tab` 同時設定；逐頁巡檢檢查每個標籤都有文字 |
| 約 70 處按鈕與 20 多個頁面一起改，漏改或主次判斷不一 | 集中由 `EditorChrome` 建立；逐頁巡檢與寫死顏色掃描把關 |
| 卡片與內距加大後，窄視窗內容被擠出 | 最小尺寸與 125% 縮放測試；必要時只調整內距 |
| 既有編輯器測試依賴元件型別或結構（例如找 `TabControl`） | 只換樣式不換元件型別；名稱與文字保留；完整 runner 把關 |
