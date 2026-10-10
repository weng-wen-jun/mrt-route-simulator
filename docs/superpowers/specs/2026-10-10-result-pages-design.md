# 結果分頁版面統一與 C1 遺留修正（子專案 C2）— 設計規格

- 日期：2026-10-10
- 分支：`claude/ui-design-improvement-0a0ca4`（C1 已合併至本機 `main` 的 `f48b41e`）
- 範圍：全面 UI 翻新子專案 C 的第二部分 C2，另含 C1 最終審查留下的 9 個小問題
- 前置：`docs/superpowers/specs/2026-10-09-ui-theme-and-track-diagram-design.md`（`UiTheme`、配線圖）、`docs/superpowers/specs/2026-10-10-main-window-shell-design.md`（主視窗外殼、`Themes/Controls.xaml`）
- 狀態：已實作（2026-10-10）；實作中確定的細節已回寫於各節並標示「實作細化」

## 1. 目標與範圍

使用者選擇「統一與整理」：7 個結果分頁的內部版面跟上 C1 外殼風格，各頁顯示的資訊與欄位不變。實作方式選擇自訂結果頁元件 `ResultPage`，一致性由元件保證。

| 範圍 | 內容 |
|---|---|
| 7 個結果分頁 | 進出站時刻表、區間物理明細、V1／V2 同條件比較、資源占用與觀測容量、移動閉塞與煞車、V2 區間統計、列車運行圖／匯出 |
| 模擬頁 2 個子分頁 | 即時列車狀態（只套表格規則）、完整行程速度曲線（工具列與圖卡） |
| C1 遺留修正 | 第 5 節的 9 項 |

成功標準：

1. 7 頁的頁首、篩選、動作、內容排列方式一致。
2. 閉塞、統計、運行圖的工具列分組清楚，篩選與動作不混在一起。
3. 數字與時間欄可上下對齊比較，狀態以色點一眼可辨。
4. 9 個小問題全部修正，既有功能、元件名稱、自動化名稱、MCP 行為不退步。
5. Engine、專案 Schema、版本號不變；完整 WPF runner 在互動桌面 session 全部通過。

不在本輪：圖表 Canvas 內的繪圖（運行圖、速度曲線、安全距離圖，屬子專案 D）、拓樸編輯器與對話框版面（子專案 E）、新增或移除任何表格欄位、空表格的提示畫面（摘要列已說明「建立並播放模擬後顯示」）。

## 2. `ResultPage` 骨架

由上到下的固定位置：

| 位置 | 內容 | 規則 |
|---|---|---|
| 頁首左 | `Title`（15 px 半粗體）＋ `Description`（12 px `TextMuted`，同一行） | 說明過長截斷，完整內容放提示框 |
| 頁首右 | `Actions`：頁面動作鈕（匯出、重新整理） | 水平排列，靠右 |
| 摘要列 | `Summary`：沿用各頁既有摘要 TextBlock，前加資訊圖示 | 未設定時隱藏；不新增任何計算 |
| 篩選卡 | `Filters`（左）＋ `FilterActions`（右，前有 1 px 分隔線） | 白底、`Border` 細框、圓角 8；兩者皆未設定時整張卡隱藏 |
| 內容 | `Content`：圖表、表格各自放在白色圓角卡（`ResultCard` 樣式） | 佔滿剩餘高度 |
| 附註 | `Footnote`：內容下方一行灰字 | 未設定時隱藏 |

- 拿掉各頁 `Grid Margin="14"`；外距只由外殼 `ShellTabControl` 的內距（12）提供。
- 圖表底色改用 `UiTheme.CanvasBackground`；`MainWindow.xaml` 不再出現 `#RRGGBB` 色碼。
- 篩選卡內的標籤＋控制項使用共用樣式 `FilterLabel`（12 px `TextMuted`、右邊距 5）；實作細化：每組標籤＋控制項包成水平 StackPanel，篩選列換行時不會把標籤留在上一行。

## 3. 各頁對應

標題一律用 TabItem 的完整頁名。「（新增標籤）」表示原本只有下拉、沒有文字標籤。

| 頁面 | Description | Summary | Filters | FilterActions | Actions | Content／Footnote |
|---|---|---|---|---|---|---|
| 進出站時刻表 | `TimetableSourceText`（程式會切換文字） | — | — | — | — | `TimetableDataGrid` |
| 區間物理明細 | `SegmentSourceText` | — | — | — | — | `SegmentDataGrid`；附註「基礎模式保留 V1.0…不代表真實 ATP／ATO／ATS 或鐵路安全認證。」 |
| V1／V2 同條件比較 | 「同一車次、同一停站條件下的 V1 理論與 V2 實際」 | — | — | — | — | 比較表；附註「跨站不套用站停差異；折返節點明確標記為 V1 不適用。」 |
| 資源占用與觀測容量 | 「月台／進路／衝突區／尾軌占用時間軸」 | `ResourceOccupancySummaryText` | — | — | 匯出 CSV | 資源表 |
| 移動閉塞與煞車 | 「相鄰列車距離、安全裕度與煞車需求」 | `SafetySummaryText` | 煞車、列車對（新增標籤）、方向（新增標籤）、狀態（新增標籤）、時間（新增標籤） | 「障礙物急停」：障礙車、延遲秒數、觸發／排程急停 | — | 距離圖卡（高 250）＋ `SafetyDataGrid` |
| V2 區間統計 | 「完成與運行中區間的旅行時間與速率」 | `IntervalSummaryText` | 方向、車輛、車次、車型、服務、停站模式、模擬秒起訖、包含運行中 | — | 重新整理｜匯出：區間 CSV、彙總 CSV、全程平均 CSV | 卡 1「每車次起終站平均速率」（說明＋全程表）；卡 2「區間明細」（區間表） |
| 列車運行圖／匯出 | 「時間－里程運行圖；篩選同時套用到 PNG／PDF」 | — | — | — | — | 見下方 |

實作細化：

- 匯出與重新整理按鈕沿用原按鈕文字（「匯出區間 CSV」「匯出彙總 CSV」「匯出全程平均 CSV」「匯出資源占用 CSV」），因為沒有明確自動化名稱的按鈕，其自動化名稱就是按鈕文字。
- 表格放進 `ResultCard`（Padding 4）並取消 DataGrid 自身外框；事件表與即時列車狀態表維持原本有框的 DataGrid。
- 新增 `x:Name`：`ComparisonDataGrid`、`ResourceDataGrid`、`JourneyStatisticsDataGrid`、`IntervalStatisticsDataGrid`、`SpeedProfileToolbar`。
- 速度曲線子分頁移除與膠囊重複的「列車完整行程速度曲線」標題。

**運行圖頁**：篩選留在既有可收合面板 `DiagramControlsExpander` 內，作為 `Content`（`DiagramWorkspaceGrid`）的第一列，因為 `UpdateShellContentHeight`／`GetDiagramWorkspaceDesiredHeight` 與運行圖測試依賴它。面板內約 20 個控制項分成三行，每行開頭有小組名：

- 篩選：方向、車輛、計畫／理論、模擬實際、顯示事件觸發點
- 檢視：左右縮放、上下縮放、起始／結束分鐘、時間刻度＋套用刻度、顯示終點時間
- 匯出：高解析 PNG、PDF 紙張、PDF 分頁、匯出 PNG、匯出 PDF、匯出實際全量 CSV

面板外觀改為篩選卡樣式；運行圖卡與事件列表（`DiagramEventsExpander`）結構不變。

**模擬頁子分頁**（不套 `ResultPage`）：

- 即時列車狀態：只套第 4 節表格規則。
- 完整行程速度曲線：上方改成精簡工具列（「列車」標籤＋`SpeedProfileRunComboBox`＋`SpeedProfileSourceText` 灰字），圖放進 `ResultCard`。

匯出鈕維持獨立按鈕，不收進下拉選單，避免改變點擊路徑與原生驗收的點擊分類。

## 4. 表格規則

適用於結果表：7 頁的表格、全程平均表、事件表、即時列車狀態表。以 DataGrid 上的附加屬性 `ResultTable.Enabled="True"` 啟用，拓樸編輯器等其他表格不受影響。

1. **欄位種類**：附加屬性 `ResultTable.Kind` 標在欄位上，預設 `Text`。
   - `Numeric`：時刻、km、km/h、秒、m、%、次數。內容與欄名靠右，內容用等寬字型（實作細化：用 `Consolas, Cascadia Mono`，Cascadia Mono 在 12～13 px 筆畫偏粗）。
   - 實作細化：結果表儲存格文字一律垂直置中，狀態欄與一般欄同高。
   - `Text`：長文字截斷顯示「…」，提示框顯示完整文字（例如停站模式欄）。
   - `Status`：狀態文字前加 8 px 色點，文字不變。
2. **色點**：`StatusTone` 依狀態文字分類，顏色取自 `UiTheme`；不重新計算任何數值。

   | Tone | 色票 | 代表的狀態文字 |
   |---|---|---|
   | Danger | `Danger` | 侵入安全距離、碰撞停止、障礙急停 |
   | Caution | 新增 `Caution`（`#D9A400`） | 接近警戒、需要制動 |
   | Success | `Success` | 已抵達、完成、安全、可比較 |
   | Active | `Accent` | 已發車、停站中、運行中、停站、加速、巡航、惰行、煞車、進站平順煞車、到站、駛入尾軌、尾軌返回、駛入折返線、折返線返回、折返 |
   | Neutral | `TextSubtle` | 待發、—、V1 理論基準、V2 尚未抵達、跨站不比較、折返節點不適用 V1、退出營運、無法辨識的文字 |

   實作細化：上表為程式實際輸出的狀態字串（測試會讀原始碼確認全部在對照表內）；原設計列的「違規類」目前沒有對應的狀態字串，「停車超限」只出現在事件表的事件欄。

   對應以「完整字串對照表」為主，實作時逐一列出各表實際會出現的狀態字串並以單元測試釘住；新的狀態文字只需補這張表。
3. **欄寬**：沿用現有寬度與 `FrozenColumnCount`；只修會被硬切的欄位。欄位類型（`DataGridTextColumn`）不變，排序、自動化名稱與以 TextBlock 讀取儲存格的既有測試照舊。

## 5. C1 遺留修正

| # | 問題 | 修法 |
|---|---|---|
| 1 | 狀態圓點：V1 播放時不變綠；工作者出錯後清除結果仍為橘色；畫面更新例外停止後顯示灰色 | V1 播放開始／暫停／重設時刷新圓點，播放中為綠；`ClearV2Results` 在清掉 `_playbackWorker` 後再刷新；畫面更新例外停止時設「因錯誤停止」旗標（橘色），重設或建立新模擬時清除；實作細化：停止流程集中於 `StopPlaybackAfterUiFailure(Exception)` |
| 2 | 配線圖固定水平捲軸在抽屜開著時蓋在抽屜上 | 抽屜開啟時捲軸改從抽屜右緣開始，仍可拖曳 |
| 3 | 窄視窗 KPI 細條直接被切掉 | `RouteSummaryScrollViewer` 水平捲動由 Hidden 改為 Disabled，內容改成五格等寬，數值截斷顯示「…」並有提示框；名稱與 `UpdateCompactSummaryHeight` 邏輯不變 |
| 4 | 停用中的圖示鈕沒有提示 | `AppBarButton` 樣式加 `ToolTipService.ShowOnDisabled="True"` |
| 5 | 抽屜鍵盤焦點 | 以關閉鈕或「起稿」鈕關閉時，若焦點在抽屜內則還給「起稿」鈕；抽屜在 XAML 中移到 `ShellTabControl` 之後，Tab 順序先經過導覽列（`Panel.ZIndex` 仍讓抽屜在最上層） |
| 6 | 已讀入專案時「起稿」鈕文字與行為不符 | 已讀入專案時提示與自動化名稱改為「開啟專案工作區（快速起稿）」，未讀入時為「快速起稿抽屜開關」；於 `_activeTopologyProjectDocument` 變更處更新 |
| 7 | 選單隱含樣式全 App 強制白字 | 白字移到具名樣式 `AppBarMenu`，`MainMenu` 改用它；頂層項目的滑過底色改讀資源鍵 `MenuTopLevelHighlightBrush`（預設 `NavHover`，`AppBarMenu` 內覆寫為 `AppBarRaised`） |
| 8 | 下拉選單取得焦點時沒有藍色框 | `ComboBox` 範本加 `IsKeyboardFocusWithin` 觸發：外框 `Accent` 2 px（可編輯與唯讀皆適用） |
| 9 | 文件過時 | README 改寫「分析結果」選單、快速起稿側欄、播放倍率位置為導覽列、抽屜、標題列；C1 規格 §4.2 刪除 `SummaryCard` |

## 6. 技術做法

### 6.1 新增檔案

- `src/MrtRouteSimulator.App/ResultPage.cs`：`public sealed class ResultPage : ContentControl`，依賴屬性 `Title`（string）、`Description`、`Summary`、`Filters`、`FilterActions`、`Actions`、`Footnote`（皆 object）。除 `Content` 外的物件屬性與 `ShellTabControl` 相同，以 `AddLogicalChild`／`LogicalChildren` 掛成邏輯子元素，讓 `FindName`、資源與字型繼承照舊。
- `src/MrtRouteSimulator.App/ResultTable.cs`：附加屬性 `Enabled`（DataGrid）與 `Kind`（DataGridColumn，列舉 `ResultColumnKind { Text, Numeric, Status }`）；啟用時依欄位種類套用 `Themes/Controls.xaml` 的 `NumericCell`／`NumericHeader`／`TextCell`／`StatusCell` 樣式。
- `src/MrtRouteSimulator.App/StatusTone.cs`：`StatusTone` 列舉、`StatusTones.Classify(string?)` 與色點畫筆轉換器。
- `tests/MrtRouteSimulator.WpfTests/ResultPageTests.cs`：第 8 節測試，runner 旗標 `--result-pages-only`，並納入完整 runner。

### 6.2 修改

- `UiTheme.cs`：新增 `Caution` 色票與畫筆。
- `Themes/Controls.xaml`：`ResultPage` 隱含樣式與範本、`ResultCard`、`FilterLabel`、表格欄位樣式、`AppBarMenu`、選單頂層滑過資源鍵、`ComboBox` 焦點框、`AppBarButton` 停用提示。
- `MainWindow.xaml`：7 頁改用 `app:ResultPage`、結果表標 `ResultTable`、速度曲線工具列、KPI 細條五格、抽屜移位、`MainMenu` 套 `AppBarMenu`。
- `MainWindow.Shell.cs`、`MainWindow.WorkspaceNavigation.cs`、`MainWindow.ShellScroll.cs`、`MainWindow.xaml.cs`、`MainWindow.V2.cs`、`MainWindow.Topology.cs`：第 5 節修正 1、2、5、6。
- `README.md`、C1 規格：修正 9。

## 7. 相容性與不變條件

- 7 頁內所有 `x:Name`、事件、`AutomationProperties.Name` 保留，包括 `TimetableDataGrid`、`SegmentDataGrid`、`SafetyDistanceCanvas`、`SafetyDataGrid`、`BrakingModeComboBox`、`SafetyPairComboBox`、`ObstacleTrainComboBox`、`ObstacleDelayTextBox`、各 `Interval*ComboBox`／`TextBox`、`IncludeInProgressCheckBox`、`DiagramWorkspaceGrid`、`DiagramControlsExpander`、`DiagramViewportBorder`、`DiagramScrollViewer`、`TimeDistanceCanvas`、`DiagramTimeAxisViewport`、`DiagramEventsExpander`、`EventDataGrid`、各摘要與來源 TextBlock。
- 運行圖頁的高度計算（`UpdateShellContentHeight`）在頁首加高後須仍正確；受影響的版面數值測試只調整數值並記錄理由，不放寬功能性檢查。
- MCP `select_page`、原生驗收的自動化名稱、Engine、專案 Schema、版本號不變。
- `ResultTable` 只作用於標了 `Enabled` 的表格；拓樸編輯器與輸入表格不受影響。

## 8. 測試與驗證

`ResultPageTests` 新增：

1. `ResultPage` 元件：各位置上下順序正確；`Summary`、篩選卡（`Filters` 與 `FilterActions` 皆空）、`Footnote` 未設定時隱藏；各位置內容為邏輯子元素。
2. 7 頁結構：內容皆為 `ResultPage`，`Title` 等於頁名；指定控制項位於正確位置（例：`BrakingModeComboBox` 在 `Filters`、`ObstacleTrainComboBox` 在 `FilterActions`、統計頁三個匯出鈕在 `Actions`）；頁面根元素不再有 14 px 外距。
3. 寫死色碼掃描：`MainWindow.xaml` 不得含 `#RRGGBB`／`#AARRGGBB`。
4. 表格規則：每張結果表已啟用 `ResultTable`；指定的數字欄靠右並用等寬字型；文字欄截斷並有提示；狀態欄色點畫筆與 `StatusTone` 一致。
5. `StatusTones.Classify`：對照表內每個字串對應正確，未知字串為 Neutral。
6. 第 5 節修正 1～8 各一個回歸測試，先確認失敗再修正。

驗收：

- 整個 solution Release build 0 warning／0 error；Engine runner 全過；完整 WPF runner 與 28 站大型播放診斷在互動桌面 session 通過。
- 截圖工具 `--shell-screenshots` 擴充為拍下 7 頁與 2 個子分頁（範例 14 推進 5 分鐘，1280×800），逐張人工檢查並交付使用者。

## 9. 風險

| 風險 | 緩解 |
|---|---|
| 運行圖頁高度計算與既有運行圖測試受頁首影響 | 篩選留在原面板、`DiagramWorkspaceGrid` 結構不變；先跑運行圖相關子集再跑完整 runner |
| 狀態欄改用自訂儲存格範本後選取／焦點外觀消失 | `StatusCell` 以 C1 的 `DataGridCell` 隱含樣式為 BasedOn，範本使用 TemplateBinding 的 Background／BorderBrush；以第 8 節第 4 項與 C1 既有儲存格選取測試把關 |
| `ResultTable.Kind` 在欄位產生前未套用 | 於 DataGrid `Initialized` 與 `Columns.CollectionChanged` 套用；測試直接檢查產生後的儲存格 |
| 狀態字串對照不完整，新狀態顯示灰色 | 灰色是安全的預設；單元測試列出現有全部字串 |
| 大型時刻表每格都有提示框造成負擔 | 提示框只在文字欄、且以繫結延後產生；DataGrid 仍為虛擬化 |
