# UI 設計語言基礎與軌道配線圖翻新 — 設計規格

- 日期：2026-10-09
- 分支：`claude/ui-design-improvement-0a0ca4`
- 範圍：全面 UI 翻新的第一輪（子專案 A + B）
- 狀態：設計已於對話中逐段確認，待使用者審閱本文件

## 1. 背景與目標

使用者回報主畫面「圖案太醜」，並選定「整個 App 全面翻新」。全面翻新拆成五個子專案，各自走「設計 → 規格 → 實作計畫 → 實作」：

| # | 子專案 | 本輪 |
|---|---|---|
| A | 設計語言基礎：`UiTheme` 色票／畫筆單一來源 | ✅ |
| B | 軌道配線圖（`RouteCanvas`） | ✅ |
| C | 主視窗外框：選單、分頁籤、按鈕、KPI 卡、狀態列、DataGrid | 下一輪 |
| D | 結果圖表：速度曲線、時間－里程運行圖、安全距離、區間統計、空間參考點圖、PNG/PDF 匯出 | 之後 |
| E | 拓樸編輯器與對話框 | 之後 |

本輪成功標準：

1. 配線圖採用已確認的合併風格（C 柔和雙色 + A 膠囊列車 + B 區段占用亮燈）。
2. 列車標記不再截斷車號（例如 `FULL-O04` 不再顯示成 `FULL-`）。
3. 站名不再重複顯示 ID。
4. 新增「顯示區段占用亮燈」開關，會記住設定，預設開啟。
5. Engine 不變；既有 Engine、WPF 視覺與播放測試全部通過；新增本輪的回歸測試。

## 2. 現況問題（依 2026-10-09 截圖與原始碼）

- 月台本體以與軌道相同的深藍 `(25,96,125)` 實心填滿，白底編號疊在上面，形成大量「[2]」方塊。
- 列車標記固定 24×16 px，`VehicleMarkerLabel` 只取前 5 字，`FULL-O04`、`SECTION-VEHICLE-01` 無法辨識。
- 站名標籤為 `{Id}\n{Name}\n{km}`，`O01 / O01 站` 重複。
- 上下行軌道同色，方向只靠小三角形。
- App 層約 170 處寫死的 `Color.FromRgb`／`Brushes.*`，每一幀都 `new SolidColorBrush`，沒有共用色票。

## 3. 子專案 A — 設計語言基礎

### 3.1 單一來源

新增 `src/MrtRouteSimulator.App/UiTheme.cs`（`internal static class UiTheme`）：

- 以 `Color` 常數定義色票，並提供已凍結（`Freeze()`）的 `SolidColorBrush` 靜態欄位。
- 這是 App 內顏色的**唯一來源**。XAML 需要時以 `{x:Static local:UiTheme.XxxBrush}` 引用，不在 `App.xaml` 重複定義同一個 hex 值。
- 既有 `App.xaml` 的 `InkBrush`、`PrimaryBrush` 等資源本輪維持不動；子專案 C 再統一收斂。
- 本輪只把配線圖相關程式（`MainWindow.V2.cs` 配線圖段落、`StationSchematicPresentation.cs`）以及 `TrainColors`／`LockedRouteColors` 改成引用 `UiTheme`；其他檔案的寫死顏色留給 C／D／E。

### 3.2 色票

| Token | 值 | 用途 |
|---|---|---|
| `CanvasBackground` | `#FBFCFE` | 配線圖底色 |
| `TextStrong` | `#1E293B` | 站名 |
| `TextMuted` | `#64748B` | 里程、說明文字 |
| `TextSubtle` | `#94A3B8` | 次要提示 |
| `Hairline` | `#CBD5E1` | 月台框、站名引線 |
| `RailUp` / `RailUpSoft` | `#2F7FC1` / `#8CB8E0` | 上行正線／上行側線 |
| `RailDown` / `RailDownSoft` | `#E8833A` / `#F2B488` | 下行正線／下行側線 |
| `RailNeutral` | `#94A3B8` | 渡線、尾軌、袋狀軌、無法判定方向的軌道 |
| `RailNeutralStrong` | `#475569` | 止衝擋 |
| `PlatformFill` | `#FFFFFF` | 月台本體 |
| `PlatformBidirectional` | `#64748B` | 雙向月台編號圓點 |
| `StationBadgeFill` | `#EEF2F7` | 站名徽章底色 |
| `OccupancyGlow` | `#FFC93C`，alpha 0x80 | 區段占用光帶 |
| `Danger` | `#C43030` | 緊急停車／碰撞列車 |

### 3.3 列車色盤（每台車一色）

延續現有「每台車一色」的語意（`ParseVehicleIndex(vehicleId) % N`），配線圖、時間－里程運行圖、速度曲線、即時狀態都改用同一份 `UiTheme.VehiclePalette`，跨圖表配色維持一致。

`#7C5CD6`（紫）、`#0F9D8A`（青綠）、`#C2418F`（洋紅）、`#6E1A3A`（酒紅；原 `#4B5B6E` 石板灰，2026-10-10 子專案 D 因與圖表灰色線混淆而更換）、`#3F9C35`（綠）、`#5468D4`（靛）、`#9B4DCA`（紫羅蘭）、`#6E7316`（橄欖）

規則：每個顏色的 HSV 色相需和 `RailUp`、`RailDown`、`Danger` 相差至少 20°，否則必須是低彩度中性色（飽和度 < 0.35）；白字在其上要可讀。

### 3.4 已鎖定進路色盤

移除與下行橘軌相近的橘 `(232,95,27)`、琥珀 `(215,145,24)`。新色盤全部為紅／洋紅系，且仍符合既有測試的 `R > B && G < 170`：

`#D93A4A`、`#C0266D`、`#E0475F`、`#A8326E`、`#CC3D3D`

## 4. 子專案 B — 軌道配線圖元件規格

### 4.1 軌道

- **配色**（每條 edge 只有一個顏色）：
  1. 屬於下行服務路徑（`outboundEdgeIds`）且不屬於上行 → `RailDown`。
  2. 屬於上行服務路徑（`inboundEdgeIds`）且不屬於下行 → `RailUp`。
  3. `PassingTrack`／`Siding` 不在任何路徑內時，先跟隨兩端都共用的並行路徑 edge；找不到單一方向時，才跟隨共用任一端點的路徑 edge，使用對應的 `*Soft` 色。兩個方向都有或都沒有時用 `RailNeutral`。（車站節點可能同時被上下行正線共用，所以要先看並行 edge。）
  4. 其他（`Crossover`、`TailTrack`、`Turnback`、`PocketTrack`、`DepotLead`、`Approach`，以及無法判定的正線）→ `RailNeutral`。
- **粗細**：正線 6 px；側線與設施軌 4 px。大型路線（≥ 32 edges）側線保留白色襯底，避免和正線混在一起。
- **方向**：沿用既有位置（edge 比例 0.38）與單向判斷，改成畫在軌道上的白色小三角形（`Polygon`，半寬約 3.5 px）。雙向 edge 不畫。
- **接軌**：`DrawConnection` 的筆刷與粗細取自來源 edge；來源與目標顏色不同時改用 `RailNeutral`。
- **止衝擋**：維持 `Line`，改用 `RailNeutralStrong`、4 px、圓頭。

### 4.2 月台

- 本體維持 `Rectangle` + `PlatformBodyAnchor`，位置與尺寸規則不變（高 14 px、最小寬 24 px、以物理中心展開）。
- 改成 `PlatformFill` 填色、`Hairline` 1 px 外框、圓角 3。
- 通過正線的標記用本體維持 `Opacity = 0`。
- **編號**：維持 `TextBlock` + `PlatformNumberAnchor`，白字、透明底、10 px 半粗體，置中於月台條。後面新增直徑 13 px 的 `Ellipse` 圓點：
  - 下行月台（`Outbound`）用 `RailDown`，上行（`Inbound`）用 `RailUp`，其他用 `PlatformBidirectional`。
  - 非載客的通過標記改成空心圓（白底、彩色外框、彩色字）。
- 重疊避讓邏輯不變；圓點跟著編號的最終位置走。

### 4.3 站名

- `DrawStationNames` 改收 `(Id, Name, Detail)`。主畫面與編輯器兩個呼叫端一起更新。
- 標籤元素改為 `Border`（`StationBadgeFill`、圓角 8、內距 6,3），`Tag = StationLabelAnchor`，子元素為置中、自動換行的 `TextBlock`：
  - 第 1 行：主要名稱，12 px 半粗體 `TextStrong`。
  - 第 2 行：次要資訊，11 px `TextMuted`。
- 名稱規則：
  - `Name` 已包含 `Id`（不分大小寫）→ 主要名稱 = `Name`，次要資訊 = 里程（例：`O03 站` / `2.197K`）。
  - 否則 → 主要名稱 = `Name`，次要資訊 = `Id · 里程`（例：`台北車站` / `BL12 · 1.234K`）。
  - 沒有里程時省略里程段。
- 寬度、置中、上下交錯與避讓規則不變（`PlaceStationLabel` 與 `ValidateStationLabels` 已支援 `Border` 包 `TextBlock`）。
- 引線改用 `Hairline` 1 px。

### 4.4 列車標記

- 維持 `Border`，`Tag = vehicleId`、`ToolTip` 字串格式不變（測試以 `"{vehicleId}｜"` 開頭比對），點擊與右鍵跟隨行為不變。
- **外觀**：高 18 px、圓角 9、填色取 `VehiclePalette`（`Collided`／`EmergencyStopped` 用 `Danger`）、白色 1.5 px 外框。子元素為 `Grid`，包含車號 `TextBlock`（白字、10.5 px 半粗體）與領先端的白色 V 形箭頭（`Path`）。
- **寬度**：依文字量測，`max(34, 文字寬 + 24)`；以列車中心點水平置中（`Left = x − width/2`，畫布邊界內 clamp）。停站時仍須與月台中心對齊（誤差 < 0.51 px）。
- **車號規則**（`VehicleMarkerLabel`）：
  1. `Vehicle N` → `VNN`、`AUTO-xxx` → `Axx`（保留舊規則）。
  2. 長度 ≤ 10 → 原樣顯示（`FULL-O04`、`FULL-UP-01`、`EXPRESS-01`）。
  3. 否則以 `-`、`_`、空白切段，除最後一段外取首字母大寫，再接 `-` 與最後一段（`SECTION-VEHICLE-01` → `SV-01`、`FULL-SECTION-O04` → `FS-O04`）；結果仍超過 10 字時截成 9 字加 `…`。
- **方向**：用 `WorldTrainState.TrackEdgeId`／`OffsetMeters`（車頭）與 `TrainCenterPositions`（車體中心）在畫面幾何上的 X 差判斷箭頭朝左或朝右；`|dx| < 0.5 px` 時改依 `Direction`（`Outbound` 向右）。

### 4.5 區段占用亮燈

- **資料來源**：`SimulationPlaybackWorker.PublishFrame` 從 `_world.TopologyOccupancy` 的每個 footprint `OccupiedIntervals` 彙整出 `ImmutableDictionary<string, ImmutableArray<string>> TrackEdgeOccupants`（edgeId → vehicleIds），作為 `PlaybackFrame` 的新欄位。Engine 不修改。
- **傳遞**：`DrawTopologyGraphRoute` 的 7 個參數簽章**不變**（WPF 測試以反射、固定參數呼叫，加 overload 也會讓反射找不到唯一方法）。改由 `DrawV2Route` 在呼叫前把最新 frame 的占用資料寫入欄位 `_routeTrackOccupants`；沒有 frame 時為空。
- **繪製**：屬於動態圖層，靜態快取命中與完整重建兩條路徑都要畫，順序為「亮燈 → 已鎖定進路 → 列車」。每個被占用的 edge 畫一條 `Polyline`：
  - 沿完整 edge 幾何，`OccupancyGlow`、16 px、圓角接合。
  - `Panel.ZIndex = -1`，壓在軌道底下；不改變 `Children` 索引，以免破壞快取與 `Children[0]` 重用測試。
  - `IsHitTestVisible = false`、不設 `ToolTip`、`Tag = "TrackOccupancy:{edgeId}"`。
  - 各 edge 的 `PointCollection` 在靜態快取中凍結後共用。
- **開關**：
  - 「顯示設定」選單新增 `ShowTrackOccupancyMenuItem`「顯示區段占用亮燈」（可勾選），位置在「顯示列車已鎖定的前方進路」旁。
  - `AppDisplayPreferences` 新增 `ShowTrackOccupancy`：舊設定檔沒有這個欄位時視為 `true`；讀寫失敗時沿用既有的容錯行為。
  - 切換任何一個顯示開關時清除 `_topologyRouteVisualCache` 並重畫，讓圖例同步。

### 4.6 圖例與說明文字

- `DrawLegend(canvas, showOccupancy, showLockedRoutes)`：「← 上行」（`RailUp`）、「下行 →」（`RailDown`），開關啟用時再加上光帶色塊「區段占用」、紅線「已鎖定進路」。編輯器呼叫時兩者皆為 `false`。
- 「起始站中心…配線里程…」與「軌道配線圖 · 將滑鼠移到…」改用 `TextMuted`／`TextSubtle`，位置不變。
- `RouteCanvas.Background` 設為 `UiTheme.CanvasBackgroundBrush`。

## 5. 相容性與不變條件

- Engine（`src/MrtRouteSimulator.Engine`）不修改；不在 UI 推算占用，只消費 world 輸出。
- 幾何權威不變：`ApplyLanes`／`ApplyChainage`／月台與列車定位公式不動，只改外觀屬性。
- 既有測試的錨點契約保留：
  - 軌道 `Polyline` 的 `ToolTip` 以 `"{edgeId}\n"` 開頭，且每個 edge 只有一條。
  - 所有 `Polyline` 折角小於 45°、不超出畫布。
  - `PlatformBodyAnchor`、`PlatformNumberAnchor`、`StationLabelAnchor` 型別與欄位不變。
  - 列車 `Border.Tag` 為 vehicleId 字串；鎖定進路的 `ToolTip` 含 `"已鎖定進路\n軌道"`。
  - 靜態快取重用時 `Children[0]` 為同一物件。
- 共用 helper（`DrawPlatforms`、`DrawStationNames`、`DrawLegend`、`DrawConnection`）的外觀變更會同時套用到拓樸編輯器；編輯器自繪的軌道顏色留給子專案 E。過渡期兩者風格部分不一致，屬預期。
- 運行圖等圖表會因 `VehiclePalette` 更換而改色，但「每台車一色」的語意不變。
- 不改專案 Schema、不改版本號。

## 6. 測試與驗證

### 6.1 必須持續通過

- `dotnet build .\MrtRouteSimulator.slnx -c Release --no-restore`
- Engine runner（`tests/MrtRouteSimulator.Tests`）全部通過。
- WPF runner（`tests/MrtRouteSimulator.WpfTests`）全部通過，特別是 `VisualRulesTests`、`CompactRouteLayoutTests`、`LargePlaybackDiagnostics`、`PlaybackWorkerTests`。

### 6.2 新增回歸測試（WPF runner）

1. **車號規則**：`Vehicle 3` → `V03`、`AUTO-007` → `A07`、`FULL-O04` 原樣、`SECTION-VEHICLE-01` → `SV-01`、超長字串截斷含 `…`。
2. **方向箭頭**：同一列車在前進與折返後，箭頭方向符合車頭相對車體中心的畫面方向。
3. **占用亮燈**：用 sample 推進到有列車運行時，開關開啟則每個被占用的 edge 恰有一條 `TrackOccupancy:` 光帶、`ZIndex = -1`；關閉則沒有光帶。設定可存取；缺少欄位的舊設定檔載入為開啟。
4. **軌道配色**：雙股島式範例的下行正線為 `RailDown`、上行為 `RailUp`、渡線／尾軌為 `RailNeutral`。
5. **站名去重**：`Name` 含 `Id` 時，標籤文字不出現重複的 `Id`；不含時次要資訊含 `Id`。
6. **主題**：`UiTheme` 所有公開筆刷 `IsFrozen`；`VehiclePalette` 符合 3.3 的色相／彩度規則；`LockedRoutePalette` 符合 `R > B && G < 170`。

### 6.3 人工驗收

- 以 `samples/14-大型-二十八站完整營運範例.mrtsim.json` 建立模擬並播放，輸出截圖，確認月台、站名、列車與亮燈在大型路線、水平縮放 1×／2× 下都清楚可讀。
- 以 `samples/05`（四股雙島待避）與 `samples/07`（尾軌袋狀軌）確認側線與設施配色。

## 7. 風險

| 風險 | 緩解 |
|---|---|
| 列車標記變寬，在密集路段互相遮蔽 | 最小寬 34 px；車號 ≤ 10 字；完整資訊仍在提示框。不做自動避讓（YAGNI），人工驗收若有問題再處理 |
| 光帶 16 px 在小型範例顯得過粗 | 寬度集中由 `UiTheme` 常數控制，人工驗收時調整 |
| 每幀新增光帶 `Polyline` 的效能 | 只畫被占用的 edge（數量 ≈ 列車數），共用凍結的 `PointCollection` 與筆刷；以 `LargePlaybackDiagnostics` 觀察 |
| 共用 helper 讓編輯器外觀半新半舊 | 已列為預期過渡，子專案 E 收斂 |
