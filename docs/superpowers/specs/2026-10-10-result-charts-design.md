# 結果圖表主題統一（子專案 D）— 設計規格

- 日期：2026-10-10
- 分支：`claude/ui-design-improvement-0a0ca4`（C2 已合併至本機 `main` 的 `f341102`）
- 範圍：全面 UI 翻新子專案 D：結果圖表繪圖與 PNG／PDF 匯出外觀
- 前置：`docs/superpowers/specs/2026-10-09-ui-theme-and-track-diagram-design.md`（`UiTheme`）、`docs/superpowers/specs/2026-10-10-result-pages-design.md`（`ResultPage`、`StatusTones`、`ChartCard`）
- 狀態：已實作（2026-10-10）；實作時的細化見第 8 節

## 1. 目標與範圍

使用者選擇「統一圖表主題」：各圖資料呈現方式與互動不變，外觀改用一套共用的圖表主題；實作方式選擇「主題＋共用繪圖工具」。

| 圖表 | 繪圖程式 | 本輪 |
|---|---|---|
| 完整行程速度曲線（V2） | `MainWindow.V2.cs` `DrawV2SpeedProfile`、`DrawSpeedLimitLabels`、`DrawSpeedStopLabels`、`DrawSpeedTimeAxisTicks` | ✅ |
| 速度曲線（V1） | `MainWindow.xaml.cs` `DrawSpeedProfile` | ✅ |
| 相鄰列車距離圖 | `MainWindow.V2.cs` `DrawSafetyDistanceChart` | ✅ |
| 時間－里程運行圖（畫面） | `MainWindow.TimeDistance.cs` `DrawInteractiveTimeDistanceDiagramCore`、`DrawFixedDiagramTimeAxis`、`AddDiagramTimeTickLabel` | ✅ |
| 時間－里程運行圖（匯出／參考） | `MainWindow.V2.cs` `DrawTimeDistanceDiagramFull` | ✅ |
| PNG／PDF 匯出頁面 | `DiagramExportService.cs` | ✅ |
| 線性路線圖（V1 與非拓樸 V2）與空間參考點幾何 | `MainWindow.xaml.cs` `DrawRoute`、`MainWindow.V2.cs` `DrawV2Route`、`MainWindow.SpatialReferencePointDiagram.cs` | ✅（只換色） |
| 拓樸配線圖其餘少數色值 | `DrawTopologyGraphRoute` 等 | 不在本輪（子專案 B 範圍） |
| 拓樸編輯器自繪軌道 | `TopologyEditorWindow*.cs` | 不在本輪（子專案 E） |

成功標準：

1. 四類圖的格線、軸線、刻度、圖例、標題外觀一致，顏色全部取自 `UiTheme`。
2. 速度曲線與距離圖有可讀的數值刻度與時間刻度。
3. 「需要制動」等安全狀態在圖表與表格同色（依 `StatusTones`）。
4. 匯出的 PNG／PDF 與畫面一致（PDF 分頁也含圖例）。
5. Engine、專案 Schema、版本號不變；大型範例播放不變慢；完整 WPF runner 在互動桌面 session 全部通過。

不在本輪：新增圖表互動（滑鼠讀值、點圖例切換系列）、改變資料取樣或計算、圖表類型變更。

## 2. `ChartTheme` 色票與樣式

新增 `src/MrtRouteSimulator.App/ChartTheme.cs`（`public static class`），只引用 `UiTheme` 既有畫筆：

| 名稱 | 取自 | 用途 |
|---|---|---|
| `Background` | `CanvasBackgroundBrush` | 圖表底色（與 `ChartCard` 一致） |
| `Grid` | `BorderBrush` | 橫向格線、時間格線、車站格線，線寬 1 |
| `Axis` | `TextSubtleBrush` | 軸線，線寬 1.2；匯出頁補畫的軸線 |
| `AxisLabel` | `TextMutedBrush` | 刻度文字、單位，10 px |
| `Title` | `TextStrongBrush` | 圖內標題，13 px 半粗體 |
| `LegendText` | `TextMutedBrush` | 圖例文字，11 px |
| `Message` | `TextMutedBrush` | 空白狀態提示，12 px |
| `Annotation` | `TextSubtleBrush` | 換向標記等輔助虛線與文字 |
| `PrimarySeries` | `AccentBrush` | 主要資料線（距離圖實際淨距），線寬 2.4 |
| `LimitSeries` | `TextMutedBrush` | 速限虛線（5,3），線寬 1.4 |
| `ThresholdSeries` | `RailNeutralStrongBrush` | 動態安全距離虛線（5,3），線寬 1.6 |
| `DangerSeries` | `DangerBrush` | 障礙物煞車需求點線（2,3），線寬 1.8 |
| `EventStation` | `SuccessBrush` | 運行圖站點事件點 |
| `EventTerminal` | `RailNeutralStrongBrush` | 運行圖端點事件點 |
| `EventSafety` | `DangerBrush` | 運行圖安全事件點 |
| `TailTrack` | `RailDownBrush` | 運行圖尾軌格線（虛線 4,3）與標籤 |
| `ExportPage` | `SurfaceBrush` | 匯出頁底色（白，便於列印） |

字型一律為 App 字型（`Microsoft JhengHei UI`），不再使用系統預設字型。

實作另加（見第 8 節）：`NeutralSeries`（`RailNeutralStrong`，運行圖圖例「V2 實際／計畫／理論」線段樣本）、`MarkerOutline`（`Surface`，事件點與圖例色點白框）、`LimitZone`（`TextSubtle` 加 0x2A 透明度，線性路線圖速限區塊底色）；虛線樣式 `LongDash`（5,3）、`ShortDot`（2,3）、`TailDash`（4,3）、`PlannedDash`（6,4）；線寬與字級常數（格線 1、軸線 1.2、刻度 10 px、時間刻度 9 px、標題 13 px、圖例 11 px、提示 12 px）。

**系列配色規則**：

- 速度曲線：線色為該列車的車輛色（`UiTheme.VehicleBrush(index)`，與配線圖膠囊、運行圖線同色）；V1 單列車使用 `VehicleBrush(0)`。速限為 `LimitSeries` 灰色虛線，速限標籤用 `AxisLabel` 色。
- 距離圖：實際淨距 `PrimarySeries`、動態安全距離 `ThresholdSeries`、障礙物煞車需求 `DangerSeries`。「最低裕度」改為色點標籤，色點顏色依 `StatusTones.Classify(SafetyStatusToChinese(status))`，文字用 `Title` 深色（黃色小字在白底上對比不足，2026-10-10 合併前改定）；`SafetyStatusColor` 移除，線性路線圖的列車間安全線也改用同一對照，所以「需要制動」在圖表、路線圖與表格都是 `Caution` 黃。
- 運行圖：列車線維持車輛色盤（計畫線為較淡虛線），只換格線、標籤、事件點與圖例。

## 3. `ChartPainter` 共用繪圖工具

新增 `src/MrtRouteSimulator.App/ChartPainter.cs`（`internal static class`）：

| 成員 | 說明 |
|---|---|
| `record struct ChartArea(double Left, double Top, double Width, double Height)` | 繪圖區 |
| `double[] NiceTicks(double maxValue, int targetCount = 5)` | 以 1／2／5×10ⁿ 為間距的取整刻度，從 0 開始、最後一個 ≥ `maxValue`；例：80.1 → 0、20、40、60、80、100 |
| `double DrawValueAxis(Canvas, ChartArea, double maxValue, string unit)` | 畫橫向格線、Y 刻度文字、兩條軸線與單位；回傳刻度最大值，資料以它換算座標，讓線與刻度對齊 |
| `void DrawTimeAxis(Canvas, ChartArea, IReadOnlyList<(double X, string Label)> ticks)` | 畫時間刻度短線與文字 |
| `FrameworkElement DrawLegend(Canvas, double left, double top, IReadOnlyList<ChartLegendItem> items)` | 以線段樣本或色點＋文字排成一列；容器 `Tag="ChartLegend"` |
| `record ChartLegendItem(string Label, Brush Brush, ChartLegendMarker Marker)`；`enum ChartLegendMarker { Line, Dash, Dot, Point }` | 圖例項目 |
| `TextBlock DrawTitle(Canvas, string text, double left, double top)` | 圖內標題，`Tag="ChartTitle"` |
| `void DrawMessage(Canvas, string text)` | 空白狀態提示 |
| `Polyline CreateSeries(Brush, double thickness, DoubleCollection? dash = null)` | 資料線 |
| `TextBlock CreateLabel(string text, Brush brush, double fontSize = 10)` | 圖內小標籤（速限、停站、換向） |

原 `DrawAxes`、`CreateChartLine`、圖表用 `AddCanvasText` 呼叫改由 `ChartPainter` 取代；既有 `Tag`（`SpeedLimitLabel`、`StopStationLabel`）保留。

實作另加：`DrawAxes(Canvas, ChartArea, string verticalLabel, string horizontalLabel)`（運行圖只畫軸線與軸名；`DrawValueAxis` 內部也用它）、`DrawHeader`（標題＋圖例排成一列，畫布放不下時省略標題、保留圖例）、`Place`。`ChartPainter` 為 `public`（測試專案沒有 `InternalsVisibleTo`），`DrawLegend` 回傳 `StackPanel`，讓呼叫端在圖例後方接狀態文字。

## 4. 各圖改動

### 4.1 速度曲線（V2／V1）

- 標題列：`DrawTitle`（例：「FULL-O13 速度」）＋圖例（實際或計畫速度＝車輛色實線、軌道速限＝灰色虛線）。
- Y 軸：`DrawValueAxis(..., maxSpeed, "km/h")`，資料依回傳的刻度最大值換算。
- 時間軸：沿用四等分時間刻度（`DrawSpeedTimeAxisTicks` 改用 `DrawTimeAxis`）。
- 停站標籤、速限標籤、換向標記保留位置邏輯，只換顏色與字型。

### 4.2 相鄰列車距離圖

- 標題列：所選列車對（例：「FULL-O04 → FULL-O13」）＋三項圖例＋最低裕度色點標籤。
- Y 軸：`DrawValueAxis(..., maxDistance, "m")`；新增四等分時間刻度（與速度曲線相同格式）。

### 4.3 時間－里程運行圖（畫面與匯出兩條路徑）

- 標題沿用原文字，改用 `DrawTitle`。
- 圖例：「V2 實際」（灰色實線）、「計畫／理論」（灰色虛線），開啟事件點時加「站點事件／端點事件／安全事件」三個色點；計畫線整理中／失敗的狀態文字接在圖例後方。
- 車站格線 `Grid`、時間格線 `Grid`、尾軌格線 `TailTrack` 虛線；車站標籤 `AxisLabel`、尾軌標籤 `TailTrack`；`TimeDistanceStationLabelLayout` 的字型改為 App 字型。
- 事件點依 `EventStation`／`EventTerminal`／`EventSafety`，維持白色外框。
- 固定時間軸（`DiagramTimeAxisCanvas`）：軸線 `Axis`、刻度短線 `Axis`、刻度文字 `AxisLabel`。
- 快取、增量更新、分層重建、layout key 邏輯不變。

### 4.4 PNG／PDF 匯出

- PNG：直接拍畫布，自動跟隨新外觀。
- PDF：頁面底色 `ExportPage`；補畫軸線改用 `Axis`；新增：每頁以與文字相同的方式（`VisualBrush`）補畫 `Tag="ChartLegend"` 的圖例元件，避免分頁後遺失線段樣本。

### 4.5 線性路線圖與空間參考點幾何（只換色）

- 軌道線：上行 `RailUp`、下行 `RailDown`、中性 `RailNeutralStrong`；車站點白底＋`RailNeutralStrong` 外框；文字 `TextStrong`／`TextMuted`。
- 列車間安全線：依 §2 的 `StatusTones` 對照。
- 五類空間參考點：中間站 `Success`、站前折返 `Accent`、站後折返 `RailDown`、中央避車線折返 `VehiclePalette[0]`（`#7C5CD6`）、銜接點 `RailNeutralStrong`；後折返尾軌幾何與標籤用 `RailDown`。

## 5. 相容性與不變條件

- Engine、專案 Schema、版本號不變；圖表只畫既有資料，新增刻度只是座標標示。
- 既有測試依賴的 `Tag`（`SpeedLimitLabel`、`StopStationLabel`）、畫布 `x:Name`、`AutomationProperties.Name` 保留。
- 運行圖互動（縮放、篩選、時間刻度設定、終點時間）、匯出檔案格式、MCP 匯出流程不變。
- 速度曲線與距離圖的座標換算改以刻度最大值為上限，曲線形狀不變、只是縮放比例對齊刻度。

## 6. 測試與驗證

新增 `tests/MrtRouteSimulator.WpfTests/ChartThemeTests.cs`（runner 旗標 `--chart-theme-only`，納入完整 runner）：

1. `ChartTheme` 每個畫筆與 `UiTheme` 對應畫筆為同一實例且已凍結。
2. `ChartPainter`：`NiceTicks` 向量（例 80.1 → 0…100、1767.7 → 0…2000、0 與負值的防護）；`DrawValueAxis` 的格線數量、刻度文字與回傳值；`DrawLegend` 的項目數、線型（實線／虛線／點線／色點）與 `Tag`；`DrawTitle`、`DrawMessage` 樣式。
3. 各圖實際繪製（範例資料推進後）：速度曲線主線畫筆等於該列車車輛色、含 Y 刻度與圖例；距離圖三條線畫筆與線型、最低裕度色點依 `StatusTones`（含「需要制動」→ `Caution`）；運行圖兩條繪製路徑的標題、圖例項目、事件點畫筆一致。
4. PDF 分頁頁面含圖例：組出的頁面點陣圖在圖例位置出現圖例線段的顏色。
5. 寫死顏色掃描：`MainWindow.TimeDistance.cs`、`DiagramExportService.cs`、`MainWindow.SpatialReferencePointDiagram.cs`、`TimeDistanceStationLabelLayout.cs` 全檔，以及第 1 節表列的方法本體，不得出現 `Color.FromRgb`、`Color.FromArgb`、`Brushes.<色名>`、`Colors.<色名>`（`Transparent` 除外）。
6. V1 模式：以 V1 建立模擬後，速度曲線與路線圖使用主題色。

驗收：

- 整個 solution Release build 0 warning／0 error；Engine runner 全過；完整 WPF runner 通過。
- 效能：28 站大型播放診斷、播放節奏測試（`--pacing-lazy-only`）與運行圖相關效能測試通過。
- 截圖：`--shell-screenshots` 加拍 V1 路線圖、匯出的 PNG 與 PDF 第一頁；與改版前逐張對照並交付使用者。

## 7. 風險

| 風險 | 緩解 |
|---|---|
| 運行圖改動影響增量更新或效能 | 只換畫筆與圖例元件，不動快取與 layout key；以大型播放診斷與運行圖效能測試把關 |
| 速度曲線、距離圖改以刻度最大值換算後，既有測試的座標或標籤位置數值改變 | 只調整與新刻度對應的數值並記錄理由，不放寬功能性檢查 |
| PDF 分頁圖例補畫位置與裁切區重疊 | 圖例位於繪圖區上方標題列；以像素測試確認每頁都有圖例 |
| 寫死顏色掃描誤傷非圖表程式 | 掃描範圍限定於第 1 節表列的檔案與方法 |

## 8. 實作細化（2026-10-10）

1. 時間刻度文字維持 9 px：運行圖時間刻度有固定寬 70 的版面規則；速度曲線時間刻度下方還有兩列停站標籤。數值刻度與單位為 10 px。
2. 速度曲線標題為「{列車} 速度」；V1 標題「V1 理論速度」、圖例「理論速度」、時間刻度以秒標示（V1 沒有時鐘時間）。
3. 距離圖標題取配對選單文字第一個「｜」之前（例「FULL-O04 → FULL-O13」），未選配對時為「全部配對」；最低裕度標籤文字後加「｜{狀態}」；繪圖區上緣由 20 改 30 以容納標題列；原「時間」軸名改為四等分時間刻度。
4. 運行圖圖例不再顯示「（事件觸發點已隱藏）」：取消事件點時圖例只剩兩項；計畫線整理中／失敗的狀態文字接在圖例後方。
5. 空白提示統一放在 (18, 18)、12 px。
6. V1 路線圖的列車膠囊沿用 `MainWindow.TrainColors`（既有測試檢查它直接引用 `UiTheme.VehiclePalette`）；運行圖列車線改用 `UiTheme.VehicleBrush`（同色、共用凍結畫筆）。
7. 線性路線圖：車站格線 `Hairline`、車站文字 `TextStrong`、軌道說明文字 `TextMuted`；速度曲線的停站與速限標籤用 `AxisLabel`；換向標記用 `Annotation`。
8. `SafetyStatusColor` 改為 `SafetyStatusBrush`，回傳 `StatusTones` 畫筆。
9. 已知限制：運行圖最上方的車次標籤可能與圖例同列重疊；改版前的文字圖例已有同樣情形，本輪未調整標籤位置。
10. 距離圖的最低裕度色點標籤放在繪圖區左上角（繪圖區頂端下方 6 DIP、白底圓角框），不放在標題列：窄畫布時標題列連標題都放不下，標籤會被擠出。資料最高點離繪圖區頂端至少保留約 11% 高度，標籤可能遮住左側少量曲線峰值。
11. 合併前的整理（2026-10-10，依最終審查）：
    - 車輛色盤第 4 色由 `#4B5B6E`（石板灰）改為 `#6E1A3A`（酒紅）：原色與灰色速限線、運行圖圖例線段、端點事件點幾乎同色；新增「車輛色彩度（RGB 最大與最小通道差）≥ 64」的測試。
    - `NiceTicks` 對極小（次正規）值或刻度過多時退回 0 與最大值兩格，不再拋例外。
    - 圖例虛線與點線樣本改為 `ChartTheme.LegendDash`（3,2）、`LegendDot`（1,1.5）；新增 `ChartPainter.CreateDot`，圖例事件點與最低裕度標籤共用。
    - `ChartTheme.Font` 第一次取得 App 字型後快取。
    - 改名：`DrawSpeedTimeAxisTicks` → `DrawClockTimeAxis`（速度曲線與距離圖共用）、`DiagramExportService.DrawTextBlock` → `DrawElementSnapshot`（文字與圖例元件共用）。
    - PDF 分頁回歸測試的固定圖例改用 `ChartPainter.DrawLegend`，並涵蓋圖例比頁面寬時的等比縮小。
