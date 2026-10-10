# 主視窗外殼骨架與共用控制項樣式（子專案 C1）— 設計規格

- 日期：2026-10-10
- 分支：`claude/ui-design-improvement-0a0ca4`（第一輪 A+B 已合併至 `main` 的 `c103ab3`）
- 範圍：全面 UI 翻新子專案 C 的第一部分 C1
- 前置：`docs/superpowers/specs/2026-10-09-ui-theme-and-track-diagram-design.md`（`UiTheme` 與配線圖）
- 狀態：已實作（2026-10-10）；實作中確定的細節已回寫於各節並標示「實作細化」

## 1. 目標與範圍

使用者選擇「重新設計版面配置」，且主要用途是**觀看模擬動畫（配線圖播放）**。因此新版面以配線圖為主角：給它最大的可視面積，播放控制在任何頁面都伸手可及。

子專案 C 拆成：

| # | 內容 | 本輪 |
|---|---|---|
| C1 | 外殼骨架（標題列、導覽列、KPI 細條、子分頁、狀態列、快速起稿抽屜）與共用控制項樣式 | ✅ |
| C2 起 | 各分頁內部版面逐頁重排（時刻表、區間、比較、容量、閉塞、統計、運行圖） | 之後 |

成功標準：

1. 主視窗改為「左側導覽列＋常駐播放列」配置（第 2 節）。
2. 同樣 1280×800 視窗下，配線圖可視高度比改動前多至少 120 px。
3. 播放控制（建立、播放、暫停、重設、障礙物急停、倍率、時鐘）在 8 個頁面都可見。
4. 所有既有功能、元件名稱、自動化名稱、MCP `select_page` 與鍵盤操作維持可用。
5. 共用控制項（按鈕、輸入框、下拉、DataGrid、捲軸、選單、Expander、提示框）套用新樣式，顏色只取自 `UiTheme`。
6. Engine 不變；完整 WPF runner 在互動桌面 session 全部通過。

不在本輪：各分頁內部版面（C2 起）、結果圖表繪圖（子專案 D）、拓樸編輯器版面與自繪軌道（子專案 E）。

## 2. 版面骨架

`ShellOverlayRoot` → `ShellScrollViewer` → `ShellContentGrid`（固定 720 DIP 高）與 `ShellRouteScrollbarOverlay`／`ShellRouteHorizontalScrollBar`（配線圖固定水平捲軸）的外層結構與名稱**不變**。只改 `ShellContentGrid` 內部：

| 列 | 高度 | 內容 |
|---|---|---|
| 0 標題列 | 48 | 左：App 名稱（13 px 半粗體）與目前專案檔名（11 px）；中：選單；右：播放控制群組 |
| 1 主體 | * | 左 64 px 導覽列；右：頁首（KPI 細條＋驗證橫幅）與目前頁面內容；快速起稿抽屜覆蓋在主體上 |
| 2 狀態列 | 24 | 左：狀態圓點＋`StatusTextBlock`；右：「完全離線 · 核心單位 m·s·m/s·m/s²」 |

改動前為 78／34（選單）／*／34，標題列與狀態列合計減少 74 px，KPI 由約 100 px 的卡片改為 26 px 細條。

### 2.1 標題列

- 背景 `AppBar`（深藍），文字白色／`AppBarMuted`。原本的漸層與「完全離線」膠囊移除，「完全離線」移到狀態列右側。
- `VersionSummaryText` 移到 App 名稱的提示框（ToolTip）；`CurrentProjectFileTextBlock` 保留名稱與自動化名稱，顯示為第二行小字並截斷。
- **選單**放進標題列，淺色字、深色列背景，下拉為淺色浮層。重新分組為四個頂層選單，所有既有子項目的 `x:Name`、`Header`、`Click`、`Tag`、`AutomationProperties.Name` 保留：
  - `_檔案`：存檔、讀取存檔、匯出完成後固定時刻表…（不變）。
  - `_編輯`（新增群組）：快速起稿（改為開啟抽屜）、軌道與設施…、服務與路徑…、模擬設定…（原頂層項目移入）。
  - `_顯示設定`：介面縮放、顯示列車已鎖定的前方進路、顯示區段占用亮燈（不變；測試要求介面縮放位於標題含「顯示設定」的選單）。
  - `_原生驗收量測`：不變。
  - 原頂層「分析結果」移除，由導覽列取代；`OpenResults_Click` 一併移除。
- **播放控制群組**（右側，依序）：建立模擬、▶ 播放（`PlayButton`）、Ⅱ 暫停、↺ 重設、⚠ 障礙物急停（`ObstacleStopButton`）、倍率（`PlaybackSpeedComboBox`）、時鐘（`SimulationClockText`，等寬字型、`TimeAccent` 綠；實作細化：深色標題列上改用亮綠 `#7EE0B5` 以保對比）。元件從「模擬動畫」頁內容搬到標題列，名稱、事件、`IsEnabled` 初始值與自動化名稱不變。按鈕為 32×28 的圖示鈕並保留文字於 ToolTip 與自動化名稱；播放鈕使用主要（橘色）樣式。
- `PlaybackStatusText` 留在「模擬動畫」頁的子分頁列右側（第 2.4 節），不放進標題列。
- `VersionSummaryText` 保留為隱藏元素，作為 App 名稱提示框的內容來源（實作細化）。

### 2.2 左側導覽列

- `WorkspaceTabControl` 改為 `ShellTabControl`（`TabControl` 的子類別，第 4.3 節），以 `NavRailTabControl` 範本將頁籤畫成 64 px 寬的直排導覽：
  - 每個項目 50×40，圖示（`Segoe Fluent Icons, Segoe MDL2 Assets`，16 px）加 11 px 短標籤。
  - 狀態：一般（`TextMuted`）、滑過（`NavHover` 底）、選中（`NavSelected` 底、`Accent` 藍字與左側 3 px 色條）、鍵盤焦點（`Accent` 焦點框）。
  - `TabItem.Header` 保留完整原名（例如「進出站時刻表」），範本以附加屬性 `ShellNav.ShortLabel`／`ShellNav.Icon` 顯示短標籤與圖示；完整名稱同時作為 ToolTip 與自動化名稱。
- 8 個頁面（`x:Name` 不變）與短標籤：

| TabItem | 短標籤 | 圖示（Segoe MDL2 字碼） |
|---|---|---|
| `SimulationTabItem` | 模擬 | `` Play |
| `ResultsTabItem` | 時刻表 | `` Calendar |
| `SegmentTabItem` | 區間 | `` List |
| `ComparisonTabItem` | 比較 | `` Switch |
| `ResourceTabItem` | 容量 | `` ViewAll |
| `SafetyTabItem` | 閉塞 | `` Shield |
| `IntervalStatisticsTabItem` | 統計 | `` Calculator |
| `DiagramTabItem` | 運行圖 | `` AreaChart |

- 導覽列底部（分隔線下）放「起稿」按鈕（`QuickBuilderToggleButton`，圖示 `` Edit），切換快速起稿抽屜；它不是 TabItem。

### 2.3 頁首：KPI 細條與驗證橫幅

- 由 `ShellTabControl.PageHeader` 呈現在內容區上方，所有頁面共用。
- `RouteSummaryExpander`（名稱不變、預設展開）改為一行 26 px 細條：五組「灰色標籤＋粗體數值」水平排列（車站、單程、全程、班距、峰值），右端為收合箭頭。五個數值 TextBlock（`RouteSummaryText`、`OneWaySummaryText`、`CycleSummaryText`、`HeadwaySummaryText`、`SpeedSummaryText`）名稱與內容來源不變，改為不換行並截斷，完整內容放 ToolTip。
- `ValidationBorder`（名稱、`ValidationTextBlock`、`HideValidationButton` 與行為不變）改為細條下方的警告橫幅：淺橘底、左側 4 px 橘色條、文字 `#9A3A12`、右側「關閉 ✕」。

### 2.4 「模擬動畫」頁

- 原本頁內的播放按鈕列移除（已搬到標題列）。
- `SimulationViewTabControl` 套用 `SegmentedTabControl` 範本：膠囊切換（灰底軌、白色選中塊）三項「配線圖／列車狀態／速度曲線」。實作細化：`TabItem.Header` 保留完整原名（作為 ToolTip 與自動化名稱），膠囊顯示附加屬性 `ShellNav.ShortLabel`。
- 同一列右側：`PlaybackStatusText`。實作細化：視角跟隨與左右縮放（`StopRouteFollowButton` 等，名稱不變）只對配線圖有效，因此留在「配線圖」子分頁內的工具列。
- 配線圖區（`RouteScrollViewer`、`RouteCanvas`、`RouteViewportHost`）結構不變，佔滿剩餘高度。

### 2.5 快速起稿抽屜

- `QuickBuilderSidebar`（名稱不變）改為覆蓋在主體左側、導覽列右緣起的抽屜：寬 `clamp(主體寬 × .38, 320, 450)`，白底、右側 1 px 分隔線（實作細化：不用 `DropShadowEffect`，避免抽屜內文字點陣模糊），最上方標題列含「快速建立線性路線」與關閉鈕（`QuickBuilderCloseButton`）。內容（`ConfigurationScrollViewer`、`QuickBuilderInputPanel` 等）不變。
- 預設收起。開啟方式：導覽列「✎ 起稿」、選單「編輯 → 快速起稿」（`FocusRouteInput_Click` 開啟並聚焦第一個輸入框）。關閉：關閉鈕、再按一次「✎ 起稿」、或 Esc。
- `SetQuickBuilderState(locked, collapsed)`：`locked` 控制輸入停用；`collapsed=true` 關閉抽屜。實作細化：它不再自動開啟抽屜，開啟只發生在使用者操作（起稿鈕、選單「快速起稿」、前往 V2 設定），避免啟動或清除結果時抽屜自行彈出；`QuickBuilderColumn` 移除，寬度改由抽屜自身計算（`UpdateQuickBuilderWidth` 改為設定抽屜寬）。

### 2.6 狀態列

`StatusTextBlock` 名稱不變；24 px 高、`StatusBar` 淺灰底、深灰字。左側 8 px 圓點顯示播放狀態（播放中綠、暫停灰、錯誤橘）。實作細化：播放旗標 `_isV2PlaybackPlaying` 一律經由 `SetV2PlaybackPlaying(bool)` 設定並同步刷新圓點；驗證警告顯示中或播放工作者錯誤（`Completion.IsFaulted`）為橘色。

## 3. 共用控制項樣式

顏色全部取自 `UiTheme`。新增外殼色票：

| Token | 值 | 用途 |
|---|---|---|
| `AppBar` / `AppBarRaised` | `#1B2538` / `#2C3A55` | 標題列底與其上按鈕 |
| `AppBarText` / `AppBarMuted` | `#FFFFFF` / `#9FB0C8` | 標題列文字 |
| `WindowBackground` | `#F5F7FA` | 視窗底色 |
| `Surface` | `#FFFFFF` | 卡片、表格、輸入框底 |
| `Border` | `#E2E8EF` | 一般細框（`Hairline` 為較深框 `#CBD5E1`） |
| `Accent` | `#2F7FC1` | 選取、焦點、連結（與上行軌道同色） |
| `NavSelected` / `NavHover` | `#EAF2FB` / `#F1F5F9` | 導覽列與清單的選中／滑過底 |
| `Primary` / `PrimaryHover` | `#E86D2D` / `#B84719` | 主要動作（播放、建立） |
| `TimeAccent` | `#7EE0B5` | 模擬時鐘（深色標題列上；實作細化） |
| `Success` | `#16866B` | 播放中狀態圓點、`App.xaml` 的 `SuccessBrush` 相容鍵 |
| `TableHeader` / `GridLine` | `#F8FAFC` / `#F1F5F9` | DataGrid 表頭與橫向格線 |
| `WarningSurface` / `WarningBorder` / `WarningText` | `#FFF4EC` / `#F4C3A0` / `#9A3A12` | 驗證橫幅 |
| `StatusBar` | `#E9EEF4` | 狀態列 |
| `TooltipBackground` | `#1E293B` | 提示框 |

元件規則：

- **字型**：Microsoft JhengHei UI；內文 13、表格 12、小字 11；時間與數值欄可用等寬字型（`Cascadia Mono, Consolas`）。
- **圓角**：控制項 6、卡片／面板／浮層 8、膠囊 12。
- **按鈕**：`PrimaryButton`（橘底白字）、`SecondaryButton`（白底 `Hairline` 框）、新增 `GhostButton`（`NavHover` 底、無框）、`AppBarButton`（`AppBarRaised` 底）；滑過加深、按下再加深、停用透明度 .45、鍵盤焦點 `Accent` 2 px 框。新增隱含 `Button` 樣式等同 `SecondaryButton`，讓未指定樣式的按鈕一致。
- **輸入框／下拉**：白底、`Hairline` 1 px、圓角 6、高 32；焦點時 `Accent` 2 px 框；下拉箭頭為細 V 形；下拉清單為白色圓角浮層、選中項 `NavSelected`。
- **選單**：標題列上為淺色字；下拉與 `ContextMenu` 為白色圓角浮層、項目高 28、勾選以藍色勾號、子選單箭頭 ▸、分隔線 `Border` 色。
- **DataGrid**：白底、外框 `Border`、只畫橫向細格線（`#F1F5F9`）、表頭 `#F8FAFC` 粗體灰字、列高 28、滑過 `NavHover`、選取 `NavSelected` 底與左側 3 px `Accent` 條、無列頭。
- **捲軸**：寬 10（滑過時拇指變粗）、圓角拇指 `Hairline`、無上下箭頭。`ShellRouteHorizontalScrollBar` 套用相同樣式。
- **Expander**：一般 Expander 為細箭頭加標題；`RouteSummaryExpander` 使用 KPI 細條專用範本。
- **提示框**：深色 `TooltipBackground`、白字、圓角 6。
- 隱含樣式套用至整個 App（含拓樸編輯器與對話框）；這些視窗的版面不變。

## 4. 技術做法

### 4.1 色票單一來源

- `UiTheme` 改為 `public static class`（App 為 exe，無外部使用者），新增第 3 節色票與對應凍結畫筆，供 XAML 以 `{x:Static app:UiTheme.XxxBrush}` 引用。
- `App.xaml` 現有的 `InkBrush`、`MutedBrush`、`PrimaryBrush`、`PrimaryDarkBrush`、`SurfaceBrush`、`BackgroundBrush`、`BorderBrush`、`SuccessBrush` 資源鍵保留（避免改動所有引用處），值改為 `x:Static` 指向 `UiTheme`，不再寫死 hex。

### 4.2 共用樣式檔

新增 `src/MrtRouteSimulator.App/Themes/Controls.xaml`（`ResourceDictionary`），在 `App.xaml` 以 `MergedDictionaries` 合併。內容：第 3 節所有隱含樣式與鍵名樣式（`PrimaryButton`、`SecondaryButton`、`GhostButton`、`AppBarButton`、`SummaryCard`、`SectionTitle`、`FieldLabel`、`NavRailTabControl`、`SegmentedTabControl`、`KpiStripExpander`）。既有鍵名維持，避免其他視窗的 XAML 修改。

### 4.3 `ShellTabControl` 與導覽附加屬性

- 新增 `src/MrtRouteSimulator.App/ShellTabControl.cs`：`public sealed class ShellTabControl : TabControl`，新增依賴屬性 `PageHeader`（`object`），範本在內容區上方以 `ContentPresenter` 呈現；實作細化：另有 `NavFooter`（導覽列底部的「起稿」鈕），兩者皆為邏輯子元素以繼承資源與字型。因為仍是 `TabControl`，`WorkspaceTabControl.SelectedItem = ...`、MCP `select_page`、`(TabControl)FindName("WorkspaceTabControl")` 等既有程式與測試不需改動。
- 新增 `src/MrtRouteSimulator.App/ShellNav.cs`：附加屬性 `ShortLabel`（string）與 `Icon`（string 字形），供 `NavRailTabControl` 與 `SegmentedTabControl` 範本使用。

### 4.4 主視窗 XAML 與程式

- `MainWindow.xaml` 依第 2 節重排；名稱、事件、自動化名稱如第 2 節所列保留。
- 移除 `QuickBuilderColumn` 與頂層「分析結果」選單；`MainWindow.WorkspaceNavigation.cs` 改寫 `SetQuickBuilderState`、`UpdateQuickBuilderWidth`，新增抽屜開關處理（`QuickBuilderToggleButton`、`QuickBuilderCloseButton`、Esc）。
- 狀態列圓點由 `MainWindow.Shell.cs` 的 `SetV2PlaybackPlaying`／`UpdateStatusIndicator` 設定（實作細化，見第 2.6 節）。
- `InterfaceScaleService` 對視窗內容的縮放機制不變。

## 5. 相容性與不變條件

- Engine、專案 Schema、版本號不變。
- 外層捲動結構（`ShellOverlayRoot`、`ShellScrollViewer`、`ShellContentGrid` 高 720、`ShellRouteScrollbarOverlay`、`ShellRouteHorizontalScrollBar`）與其測試契約不變。
- 測試以 `FindName` 取得的元件名稱全部保留，包括 `WorkspaceTabControl`、`SimulationViewTabControl`、`SimulationTabItem`、`SpeedProfileTabItem`、`DiagramTabItem`、`SafetyTabItem`、`RouteCanvas`、`RouteScrollViewer`、`PlayButton`、`PlaybackSpeedComboBox`、`SimulationClockText`、`PlaybackStatusText`、`StopRouteFollowButton`、`ShowLockedRoutesMenuItem`、`ShowTrackOccupancyMenuItem`、`ValidationBorder`、`ValidationTextBlock`、各 Summary TextBlock，以及結果頁內的控制項。
- 原生驗收（`docs/NATIVE_DESKTOP_ACCEPTANCE.md`）依賴的自動化名稱保留；按鈕改為圖示鈕時，`AutomationProperties.Name` 仍為原中文名稱。
- 配線圖、運行圖等 Canvas 內容與 `UiTheme` 既有色票不變。

## 6. 測試與驗證

### 6.1 因設計刻意改變而更新的既有測試

- `OuterShellScrollTests`：「800 DIP 預設頁左欄應自適應縮小」改為「快速起稿抽屜預設收起、導覽列固定 64 px、主體不因抽屜開關改變寬度」；720 DIP 內容高度與固定捲軸契約不變。
- `LargePlaybackDiagnostics`：「路線圖分頁撐滿剩餘高度」改為以新結構判定（`SimulationViewTabControl` 佔滿模擬頁內容區、`RouteScrollViewer` 佔滿配線圖子分頁）。
- 其他受版面尺寸影響的斷言（例如 `CompactRouteLayoutTests`、`InterfaceScaleTests` 的位置數值）若失敗，只調整與新骨架對應的數值，不得放寬功能性檢查；每一處調整在實作紀錄中說明。

### 6.2 新增 `ShellLayoutTests`

1. 標題列 48、狀態列 24；`ShellContentGrid` 仍為 720。
2. 導覽列恰有 8 個頁面項目，順序與第 2.2 節一致，每項有短標籤、圖示與完整名稱 ToolTip／自動化名稱；以滑鼠事件點選後 `WorkspaceTabControl.SelectedItem` 切換。
3. 播放控制群組（`PlayButton` 等）位於標題列，切換到每個頁面時都可見且可點。
4. 快速起稿抽屜預設收起；「✎ 起稿」、選單「編輯 → 快速起稿」可開啟，關閉鈕與 Esc 可關閉；`SetQuickBuilderState` 的 locked／collapsed 語意不變。
5. `RouteSummaryExpander` 預設展開、高 26、五項數值同列。
6. 四個頂層選單與其子項目存在，所有既有 `x:Name` 選單項目可找到；「分析結果」頂層項目不存在。
7. 1280×800 視窗下，「模擬動畫 → 配線圖」的 `RouteScrollViewer` 可視高度比改動前（以 `c103ab3` 量得的基準值寫入測試）多至少 120 px。
8. 隱含樣式存在且顏色取自 `UiTheme`：`Button`、`TextBox`、`ComboBox`、`DataGrid`、`ScrollBar`、`MenuItem`、`ToolTip`。

### 6.3 驗收

- Release build（整個 solution）0 warning／0 error；Engine runner 全過。
- 完整 WPF runner 在互動桌面 session 通過（執行期間不操作測試視窗）。
- 啟動 App 載入範例 14 播放，截圖 1280×800 與 1920×1080 兩種尺寸，人工確認：導覽列、播放列、KPI 細條、子分頁、抽屜、狀態列、各結果頁的 DataGrid 與捲軸外觀；拓樸編輯器與對話框控制項套用新樣式後無破版。

## 7. 風險

| 風險 | 緩解 |
|---|---|
| 隱含樣式（尤其捲軸、ComboBox）改變其他視窗的可用寬度或行為 | 只改外觀與尺寸，不改互動；以完整 WPF runner 與編輯器截圖檢查 |
| 播放控制搬到標題列後，窄視窗放不下 | 標題列採圖示鈕；寬度不足時先截斷專案檔名與 App 名稱（`TextTrimming`），播放群組與選單不縮；`ShellLayoutTests` 在 800 DIP 寬、100% 介面縮放下檢查三者不重疊 |
| `x:Static` 引用 `UiTheme` 需 public | 已列入 4.1；App 為 exe，無外部 API 承諾 |
| 與背景任務（匯出相關）衝突 | 它們修改 `DiagramExportService`、`MainWindow.Mcp.cs`、`McpBridgeTests`，與本輪檔案幾乎不重疊；實作前同步最新 `main` |
| 版面測試大量失敗難以判斷 | 先在互動桌面跑改動前基準，鎖定受影響測試清單；session 鎖定時的失敗依記憶中的環境注意事項判讀 |
