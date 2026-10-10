# 結果圖表主題統一（子專案 D）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 速度曲線、相鄰列車距離圖、時間－里程運行圖與 PNG／PDF 匯出改用同一套圖表主題（格線、軸線、刻度、圖例、標題），顏色全部取自 `UiTheme`，並讓「需要制動」等安全狀態在圖表、路線圖與表格同色。

**Architecture:** 新增 `ChartTheme`（圖表色票，只引用 `UiTheme` 既有凍結畫筆）與 `ChartPainter`（取整數值刻度、軸線、時間刻度、圖例、標題、空白提示的共用繪圖工具）。各圖保留原本的資料取樣、座標換算邏輯與快取，只把外觀相關的繪製改由 `ChartPainter` 產生；速度曲線與距離圖的 Y 軸改以刻度最大值換算。PDF 分頁時每頁以 `VisualBrush` 補畫 `Tag="ChartLegend"` 的圖例元件。

**Tech Stack:** .NET 10、WPF（C# 14）、自製 WPF 測試 runner（`tests/MrtRouteSimulator.WpfTests`）。

**Spec:** `docs/superpowers/specs/2026-10-10-result-charts-design.md`

## Global Constraints

- 不修改 `src/MrtRouteSimulator.Engine/`、專案 Schema、版本號；圖表只畫既有資料，不新增計算。
- 圖表顏色只能取自 `UiTheme`（經 `ChartTheme`）；規格第 1 節表列的檔案與方法不得出現 `Color.FromRgb`、`Color.FromArgb`、`Brushes.<色名>`、`Colors.<色名>`（`Transparent` 除外）。
- 字型一律為 App 字型（`Themes/Controls.xaml` 的 `AppFont`，`Microsoft JhengHei UI, Segoe UI`）。
- 既有 `Tag`（`SpeedLimitLabel`、`StopStationLabel`）、畫布 `x:Name`、`AutomationProperties.Name` 保留。
- 運行圖的快取、增量更新、分層重建、layout key 邏輯不變；運行圖互動（縮放、篩選、時間刻度、終點時間）、匯出檔格式、MCP 匯出流程不變。
- `MainWindow.TrainColors` 欄位保留且繼續被使用（`TrackDiagramThemeTests` 檢查它直接引用 `UiTheme.VehiclePalette`）。
- App 專案 `TreatWarningsAsErrors=true`。
- 像素與視窗版面類 WPF 測試只能在互動桌面 session 執行：先 `query user` 確認 `Active`，並確認 `[System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea` 高度 ≥ 720；完整 runner 執行期間不操作測試視窗。
- 既有測試的座標或數值若因新版面失敗，只調整與新刻度／新版面對應的數值並記錄 `Ruling`，不得刪除或放寬功能性檢查。
- Commit 遵循 Conventional Commits，結尾 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`；不 push；只 `git add` 列出的檔案。

## 規格的實作細化（Task 8 回寫規格）

1. `ChartPainter` 改為 `public static class`：測試專案沒有 `InternalsVisibleTo`，與 `UiTheme`、`StatusTones` 一致。
2. `ChartTheme` 另加：`NeutralSeries`（運行圖圖例「V2 實際／計畫／理論」線段樣本，`RailNeutralStrong`）、`MarkerOutline`（事件點與圖例色點的白框，`Surface`）、`LimitZone`（線性路線圖速限區塊底色，`TextSubtle` 加 0x2A 透明度，唯一的衍生畫筆）；虛線樣式常數 `LongDash`（5,3）、`ShortDot`（2,3）、`TailDash`（4,3）、`PlannedDash`（6,4）；線寬與字級常數。
3. `ChartPainter` 另加：`DrawAxes(Canvas, ChartArea, string verticalLabel, string horizontalLabel)`（運行圖沒有數值刻度，只畫兩條軸線與軸名；`DrawValueAxis` 內部也用它）、`DrawHeader`（標題＋圖例排成一列，畫布放不下時省略標題、保留圖例）、`Place`；`DrawLegend` 回傳 `StackPanel`（`FrameworkElement` 的子型別），讓呼叫端可在圖例後方接狀態文字。
4. 時間刻度文字維持 9 px：運行圖時間刻度有固定寬 70 的版面規則；速度曲線時間刻度下方還有兩列停站標籤。數值刻度與單位為 10 px。
5. 速度曲線標題為「{列車} 速度」；V1 標題「V1 理論速度」、圖例「理論速度」、時間刻度以秒標示（V1 沒有時鐘時間）。
6. 距離圖標題取配對選單文字第一個「｜」之前（例「FULL-O04 → FULL-O13」），未選配對時為「全部配對」；最低裕度標籤文字後加「｜{狀態}」；繪圖區上緣由 20 改 30 以容納標題列；原「時間」軸名改為四等分時間刻度。
7. 運行圖圖例不再顯示「（事件觸發點已隱藏）」：取消事件點時圖例只剩兩項；計畫線整理中／失敗的狀態文字接在圖例後方。
8. 空白提示統一放在 (18, 18)、12 px。
9. V1 路線圖的列車膠囊沿用 `TrainColors`（見 Global Constraints）；運行圖列車線改用 `UiTheme.VehicleBrush`（同色、共用凍結畫筆）。
10. 線性路線圖的車站格線用 `Hairline`；停站標籤與速限標籤用 `AxisLabel`；換向標記用 `Annotation`。
11. `SafetyStatusColor` 改為 `SafetyStatusBrush`（回傳 `StatusTones` 畫筆）。

## Review Focus

1. **窄畫布（約 420 px 寬）的距離圖或速度曲線**：標題列放不下時要省略標題、圖例完整留在畫布內，不可超出右緣。由 Task 2 測試釘住。
2. **最大值為 0、負數、NaN、無限大或極大值**（列車整段停在站上、距離全為 0）：刻度不得產生 NaN 或無限大座標。由 Task 2 測試釘住。
3. **未播放時的計畫預覽**：速度曲線圖例要寫「計畫速度」，線色仍為該列車的車輛色。由 Task 3 測試釘住。
4. **刻度取整後的繪圖區上緣**：速度、速限、淨距、安全距離等線不得畫到繪圖區上方（刻度最大值必須 ≥ 資料最大值）。由 Task 3、Task 4 測試釘住。
5. **取消「事件點」勾選**：兩條運行圖繪製路徑的圖例只剩「V2 實際／計畫／理論」，事件點全部隱藏。由 Task 5 測試釘住。

---

## File Structure

| 檔案 | 動作 | 責任 |
|---|---|---|
| `src/MrtRouteSimulator.App/ChartTheme.cs` | 新增 | 圖表色票、虛線樣式、線寬字級常數、App 字型 |
| `src/MrtRouteSimulator.App/ChartPainter.cs` | 新增 | 取整刻度、軸線、時間刻度、圖例、標題、空白提示 |
| `src/MrtRouteSimulator.App/MainWindow.V2.cs` | 修改 | V2 速度曲線、距離圖、完整運行圖、線性路線圖、安全狀態色 |
| `src/MrtRouteSimulator.App/MainWindow.xaml.cs` | 修改 | V1 速度曲線、V1 路線圖 |
| `src/MrtRouteSimulator.App/MainWindow.TimeDistance.cs` | 修改 | 互動運行圖、固定時間軸、圖例項目 |
| `src/MrtRouteSimulator.App/TimeDistanceStationLabelLayout.cs` | 修改 | 車站標籤字型與引線透明度 |
| `src/MrtRouteSimulator.App/DiagramExportService.cs` | 修改 | 匯出頁底色、補畫軸線、PDF 每頁圖例 |
| `src/MrtRouteSimulator.App/MainWindow.SpatialReferencePointDiagram.cs` | 修改 | 五類空間參考點與尾軌幾何顏色 |
| `tests/MrtRouteSimulator.WpfTests/ChartThemeTests.cs` | 新增 | D 的全部測試 |
| `tests/MrtRouteSimulator.WpfTests/Program.cs` | 修改 | `--chart-theme-only` 與完整 runner |
| `tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs` | 修改 | 截圖加拍 V1 圖表與匯出 |
| `tests/MrtRouteSimulator.WpfTests/TimeDistanceVisualTests.cs` | 修改 | `HasEventLegend` 改讀圖例元件 |
| `README.md`、D 規格、`CHANGELOG.md`、`QA_REPORT.md` | 修改 | Task 8 |

**共用指令**（worktree 根目錄，PowerShell）：

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --chart-theme-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- .
```

---

### Task 0: 基準線、`ChartThemeTests` 骨架與截圖擴充

**Files:**
- Create: `tests/MrtRouteSimulator.WpfTests/ChartThemeTests.cs`
- Modify: `tests/MrtRouteSimulator.WpfTests/Program.cs`
- Modify: `tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs`（`CaptureScreenshots`）

**Interfaces:**
- Produces: `ChartThemeTests.Run(string root)`、`ChartThemeTests.Require(bool, string)`、`ChartThemeTests.BuildV1Simulation(MainWindow)`；runner 旗標 `--chart-theme-only`；截圖多出 `v1-route-1280x800.png`、`v1-speed-1280x800.png`、`export-diagram.png`、`export-pdf-page1.png`（共 19 張）。

- [ ] **Step 1: 確認分支包含最新 main**

```powershell
git merge-base --is-ancestor main HEAD; "main is ancestor: $LASTEXITCODE"
```

Expected: `main is ancestor: 0`（本分支 = main `f341102` + 規格 commit）。

- [ ] **Step 2: 建置、Engine、完整 WPF 與效能基準**

```powershell
query user
Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
dotnet build .\MrtRouteSimulator.slnx -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.Tests\MrtRouteSimulator.Tests.csproj -c Release --no-build --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- .
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- .\samples\14-大型-二十八站完整營運範例.mrtsim.json --large-playback-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- .\samples\14-大型-二十八站完整營運範例.mrtsim.json --profile-interactive-timedistance-only > "$(git rev-parse --show-toplevel)\.superpowers\sdd\2026-10-10-result-charts\profile-before.txt"
```

Expected: session `Active`；工作區高度 ≥ 720；0 warning／0 error；Engine 198/198；WPF 最後一行 `PASS WPF visual rules`（C2 結束時 124 項 `[通過]`）；大型播放 `PASS WPF large playback diagnostics`（C2：58.5×、最大 UI 輸入間隔 70 ms）；`profile-before.txt` 含 `INTERACTIVE_TIME_DISTANCE_PROFILE_JSON=`。把數字記入進度紀錄。

- [ ] **Step 3: 建立測試骨架、旗標與截圖擴充**

建立 `tests/MrtRouteSimulator.WpfTests/ChartThemeTests.cs`：

```csharp
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

/// <summary>結果圖表主題（子專案 D）：色票、共用繪圖工具、各圖繪製、PDF 分頁與寫死顏色掃描。</summary>
internal static class ChartThemeTests
{
    public static void Run(string root)
    {
        Console.WriteLine("PASS WPF chart theme");
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    /// <summary>以 V1 基礎物理與預設輸入建立模擬；V1 圖表測試與截圖共用。</summary>
    internal static void BuildV1Simulation(MainWindow window)
    {
        ((ComboBox)window.FindName("EngineModeComboBox")!).SelectedIndex = 0; // V1 基礎物理
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
        try
        {
            var play = (Button)window.FindName("PlayButton")!;
            WpfTestWait.Invoke(window, "RunSimulation_Click", play, new RoutedEventArgs());
            for (var attempt = 0; attempt < 100 && !play.IsEnabled; attempt++) WpfTestWait.Wait(Task.Delay(50));
            Require(play.IsEnabled && WpfTestWait.Field(window, "_simulationEngine") is not null, "V1 模擬必須建立完成。");
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
    }
}
```

`Program.cs`：在 `if (args.Contains("--shell-layout-only"))` 區塊之前加入：

```csharp
            if (args.Contains("--chart-theme-only"))
            {
                ChartThemeTests.Run(GetRoot(args));
                return 0;
            }
```

並在完整 runner 的 `ResultPageTests.Run(GetRoot(args));` 下一行加入：

```csharp
            ChartThemeTests.Run(GetRoot(args));
```

`ShellLayoutTests.cs` 的 `CaptureScreenshots`：把

```csharp
                        foreach (var (index, file) in new[] { (1, "trains"), (2, "speed") })
                        {
                            views.SelectedIndex = index;
                            WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);
                            WpfTestWait.Wait(Task.Delay(200));
                            Save(window, System.IO.Path.Combine(output, $"{file}-1280x800.png"));
                        }
```

換成

```csharp
                        foreach (var (index, file) in new[] { (1, "trains"), (2, "speed") })
                        {
                            views.SelectedIndex = index;
                            WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);
                            WpfTestWait.Wait(Task.Delay(200));
                            Save(window, System.IO.Path.Combine(output, $"{file}-1280x800.png"));
                        }
                        tabs.SelectedItem = window.FindName("DiagramTabItem");
                        PumpLayout(window);
                        SaveDiagramExports(window, output);
```

在同一方法的 `var drawerWindow = new MainWindow { Width = 1280, Height = 800 };` 之前加入：

```csharp
            var v1Window = new MainWindow { Width = 1280, Height = 800 };
            try
            {
                v1Window.Show();
                PumpLayout(v1Window);
                ChartThemeTests.BuildV1Simulation(v1Window);
                ((TabControl)v1Window.FindName("WorkspaceTabControl")!).SelectedItem = v1Window.FindName("SimulationTabItem");
                var v1Views = (TabControl)v1Window.FindName("SimulationViewTabControl")!;
                foreach (var (index, file, draw) in new[] { (0, "v1-route", "DrawRoute"), (2, "v1-speed", "DrawSpeedProfile") })
                {
                    v1Views.SelectedIndex = index;
                    PumpLayout(v1Window);
                    WpfTestWait.Invoke(v1Window, draw);
                    Save(v1Window, System.IO.Path.Combine(output, $"{file}-1280x800.png"));
                }
            }
            finally { WpfTestWait.Close(v1Window); }
```

在 `Save` 方法之後加入：

```csharp
    // 匯出 PNG 與 PDF 第一頁（以分頁模式組頁），供改版前後對照匯出外觀。
    private static void SaveDiagramExports(MainWindow window, string output)
    {
        var exportType = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.DiagramExportService")!;
        var canvas = (Canvas)window.FindName("TimeDistanceCanvas")!;
        WpfTestWait.Invoke(window, "DrawTimeDistanceDiagramFull");
        PumpLayout(window);
        exportType.GetMethod("ExportPng")!.Invoke(null, [canvas, System.IO.Path.Combine(output, "export-diagram.png"), 1d]);
        var render = exportType.GetMethod("Render", BindingFlags.Static | BindingFlags.NonPublic, binder: null,
            types: [typeof(FrameworkElement), typeof(double), typeof(bool)], modifiers: null)!;
        var createPages = exportType.GetMethod("CreatePdfPages", BindingFlags.Static | BindingFlags.NonPublic)!;
        var bitmap = (RenderTargetBitmap)render.Invoke(null, [canvas, 1.6d, true])!;
        var pages = (IReadOnlyList<BitmapSource>)createPages.Invoke(null, [bitmap, 794, 547, true, canvas, 1.6d])!;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(pages[0]));
        using var stream = File.Create(System.IO.Path.Combine(output, "export-pdf-page1.png"));
        encoder.Save(stream);
    }
```

- [ ] **Step 4: 建置並執行骨架與改版前截圖**

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --chart-theme-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --shell-screenshots
New-Item -ItemType Directory .\artifacts\d-before -Force | Out-Null
Copy-Item .\artifacts\shell-screenshots\*.png .\artifacts\d-before -Force
(Get-ChildItem .\artifacts\d-before\*.png).Count
```

Expected: 建置 0 warning／0 error；`PASS WPF chart theme`；截圖輸出 `shellScreenshots=...`；`d-before` 內 19 張 PNG（含 `v1-route`、`v1-speed`、`export-diagram`、`export-pdf-page1`）。用 Read 工具看這 4 張新圖，確認 V1 路線圖有車站、速度曲線有線、PDF 第一頁有標題與圖例文字。

- [ ] **Step 5: Commit**

```powershell
git add tests/MrtRouteSimulator.WpfTests/ChartThemeTests.cs tests/MrtRouteSimulator.WpfTests/Program.cs tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs
git commit -m "test: add chart theme test skeleton and chart screenshots" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 1: `ChartTheme` 色票

**Files:**
- Create: `src/MrtRouteSimulator.App/ChartTheme.cs`
- Test: `tests/MrtRouteSimulator.WpfTests/ChartThemeTests.cs`

**Interfaces:**
- Produces: `public static class ChartTheme`，`SolidColorBrush` 欄位 `Background`、`Grid`、`Axis`、`AxisLabel`、`Title`、`LegendText`、`Message`、`Annotation`、`PrimarySeries`、`LimitSeries`、`ThresholdSeries`、`DangerSeries`、`NeutralSeries`、`EventStation`、`EventTerminal`、`EventSafety`、`MarkerOutline`、`TailTrack`、`ExportPage`、`LimitZone`；`DoubleCollection` 欄位 `LongDash`、`ShortDot`、`TailDash`、`PlannedDash`；常數 `GridThickness`（1）、`AxisThickness`（1.2）、`AxisLabelFontSize`（10）、`TimeTickFontSize`（9）、`TitleFontSize`（13）、`LegendFontSize`（11）、`MessageFontSize`（12）；屬性 `FontFamily Font`。

- [ ] **Step 1: 寫失敗測試**

`ChartThemeTests.Run` 改為：

```csharp
    public static void Run(string root)
    {
        VerifyThemeTokens();
        Console.WriteLine("PASS WPF chart theme");
    }
```

並加入：

```csharp
    private static void VerifyThemeTokens()
    {
        var tokens = new (string Name, SolidColorBrush Actual, SolidColorBrush Source)[]
        {
            ("Background", ChartTheme.Background, UiTheme.CanvasBackgroundBrush),
            ("Grid", ChartTheme.Grid, UiTheme.BorderBrush),
            ("Axis", ChartTheme.Axis, UiTheme.TextSubtleBrush),
            ("AxisLabel", ChartTheme.AxisLabel, UiTheme.TextMutedBrush),
            ("Title", ChartTheme.Title, UiTheme.TextStrongBrush),
            ("LegendText", ChartTheme.LegendText, UiTheme.TextMutedBrush),
            ("Message", ChartTheme.Message, UiTheme.TextMutedBrush),
            ("Annotation", ChartTheme.Annotation, UiTheme.TextSubtleBrush),
            ("PrimarySeries", ChartTheme.PrimarySeries, UiTheme.AccentBrush),
            ("LimitSeries", ChartTheme.LimitSeries, UiTheme.TextMutedBrush),
            ("ThresholdSeries", ChartTheme.ThresholdSeries, UiTheme.RailNeutralStrongBrush),
            ("DangerSeries", ChartTheme.DangerSeries, UiTheme.DangerBrush),
            ("NeutralSeries", ChartTheme.NeutralSeries, UiTheme.RailNeutralStrongBrush),
            ("EventStation", ChartTheme.EventStation, UiTheme.SuccessBrush),
            ("EventTerminal", ChartTheme.EventTerminal, UiTheme.RailNeutralStrongBrush),
            ("EventSafety", ChartTheme.EventSafety, UiTheme.DangerBrush),
            ("MarkerOutline", ChartTheme.MarkerOutline, UiTheme.SurfaceBrush),
            ("TailTrack", ChartTheme.TailTrack, UiTheme.RailDownBrush),
            ("ExportPage", ChartTheme.ExportPage, UiTheme.SurfaceBrush)
        };
        foreach (var (name, actual, source) in tokens)
            Require(ReferenceEquals(actual, source) && actual.IsFrozen, $"ChartTheme.{name} 必須直接引用對應的 UiTheme 凍結畫筆。");

        var zone = ChartTheme.LimitZone.Color;
        Require(ChartTheme.LimitZone.IsFrozen && zone.A == 0x2A
                && zone.R == UiTheme.TextSubtle.R && zone.G == UiTheme.TextSubtle.G && zone.B == UiTheme.TextSubtle.B,
            "LimitZone 必須是 TextSubtle 加 0x2A 透明度的凍結畫筆。");
        foreach (var (name, dash, expected) in new (string, DoubleCollection, double[])[]
                 {
                     ("LongDash", ChartTheme.LongDash, new double[] { 5, 3 }),
                     ("ShortDot", ChartTheme.ShortDot, new double[] { 2, 3 }),
                     ("TailDash", ChartTheme.TailDash, new double[] { 4, 3 }),
                     ("PlannedDash", ChartTheme.PlannedDash, new double[] { 6, 4 })
                 })
            Require(dash.IsFrozen && dash.SequenceEqual(expected), $"ChartTheme.{name} 應為凍結的 {string.Join(",", expected)}。");
        Require(ReferenceEquals(ChartTheme.Font, Application.Current.TryFindResource("AppFont")), "圖表字型必須是 App 字型（AppFont）。");
        Require(ChartTheme.GridThickness == 1 && ChartTheme.AxisThickness == 1.2 && ChartTheme.AxisLabelFontSize == 10
                && ChartTheme.TimeTickFontSize == 9 && ChartTheme.TitleFontSize == 13 && ChartTheme.LegendFontSize == 11
                && ChartTheme.MessageFontSize == 12, "圖表線寬與字級常數不符規格。");
        Console.WriteLine("[通過] ChartTheme 色票直接引用 UiTheme 且凍結");
    }
```

- [ ] **Step 2: 執行，確認失敗**

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
```

Expected: 建置失敗，錯誤 `CS0103: 名稱 'ChartTheme' 不存在於目前內容中`（或英文 `The name 'ChartTheme' does not exist`）。

- [ ] **Step 3: 實作**

建立 `src/MrtRouteSimulator.App/ChartTheme.cs`：

```csharp
using System.Windows;
using System.Windows.Media;

namespace MrtRouteSimulator.App;

/// <summary>
/// 結果圖表（速度曲線、相鄰列車距離圖、時間－里程運行圖、匯出頁）的色票、線型與字型。
/// 畫筆都直接引用 <see cref="UiTheme"/> 的凍結畫筆，不另定義色值；圖表程式只從這裡取色，
/// 讓畫面、PNG 與 PDF 一致。
/// </summary>
public static class ChartTheme
{
    public static readonly SolidColorBrush Background = UiTheme.CanvasBackgroundBrush;
    public static readonly SolidColorBrush Grid = UiTheme.BorderBrush;
    public static readonly SolidColorBrush Axis = UiTheme.TextSubtleBrush;
    public static readonly SolidColorBrush AxisLabel = UiTheme.TextMutedBrush;
    public static readonly SolidColorBrush Title = UiTheme.TextStrongBrush;
    public static readonly SolidColorBrush LegendText = UiTheme.TextMutedBrush;
    public static readonly SolidColorBrush Message = UiTheme.TextMutedBrush;
    public static readonly SolidColorBrush Annotation = UiTheme.TextSubtleBrush;
    public static readonly SolidColorBrush PrimarySeries = UiTheme.AccentBrush;
    public static readonly SolidColorBrush LimitSeries = UiTheme.TextMutedBrush;
    public static readonly SolidColorBrush ThresholdSeries = UiTheme.RailNeutralStrongBrush;
    public static readonly SolidColorBrush DangerSeries = UiTheme.DangerBrush;
    public static readonly SolidColorBrush NeutralSeries = UiTheme.RailNeutralStrongBrush;
    public static readonly SolidColorBrush EventStation = UiTheme.SuccessBrush;
    public static readonly SolidColorBrush EventTerminal = UiTheme.RailNeutralStrongBrush;
    public static readonly SolidColorBrush EventSafety = UiTheme.DangerBrush;
    public static readonly SolidColorBrush MarkerOutline = UiTheme.SurfaceBrush;
    public static readonly SolidColorBrush TailTrack = UiTheme.RailDownBrush;
    public static readonly SolidColorBrush ExportPage = UiTheme.SurfaceBrush;

    /// <summary>線性路線圖的速限區塊底色：TextSubtle 加透明度，是唯一的衍生畫筆。</summary>
    public static readonly SolidColorBrush LimitZone = Translucent(UiTheme.TextSubtle, 0x2A);

    /// <summary>速限、動態安全距離虛線。</summary>
    public static readonly DoubleCollection LongDash = FrozenDash(5, 3);

    /// <summary>障礙物煞車需求點線、換向標記。</summary>
    public static readonly DoubleCollection ShortDot = FrozenDash(2, 3);

    /// <summary>運行圖尾軌格線。</summary>
    public static readonly DoubleCollection TailDash = FrozenDash(4, 3);

    /// <summary>運行圖計畫／理論線。</summary>
    public static readonly DoubleCollection PlannedDash = FrozenDash(6, 4);

    public const double GridThickness = 1;
    public const double AxisThickness = 1.2;
    public const double AxisLabelFontSize = 10;
    public const double TimeTickFontSize = 9;
    public const double TitleFontSize = 13;
    public const double LegendFontSize = 11;
    public const double MessageFontSize = 12;

    private static readonly FontFamily FallbackFont = new("Microsoft JhengHei UI, Segoe UI");

    /// <summary>App 字型（Themes/Controls.xaml 的 AppFont）；匯出與尚未掛上視窗的畫布也用同一字型。</summary>
    public static FontFamily Font =>
        Application.Current?.TryFindResource("AppFont") as FontFamily ?? FallbackFont;

    private static SolidColorBrush Translucent(Color color, byte alpha)
    {
        color.A = alpha;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static DoubleCollection FrozenDash(params double[] values)
    {
        var dash = new DoubleCollection(values);
        dash.Freeze();
        return dash;
    }
}
```

- [ ] **Step 4: 執行，確認通過**

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --chart-theme-only
```

Expected: 0 warning／0 error；`[通過] ChartTheme 色票直接引用 UiTheme 且凍結`、`PASS WPF chart theme`。

- [ ] **Step 5: Commit**

```powershell
git add src/MrtRouteSimulator.App/ChartTheme.cs tests/MrtRouteSimulator.WpfTests/ChartThemeTests.cs
git commit -m "feat: add ChartTheme tokens for result charts" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: `ChartPainter` 共用繪圖工具與寫死顏色掃描

**Files:**
- Create: `src/MrtRouteSimulator.App/ChartPainter.cs`
- Test: `tests/MrtRouteSimulator.WpfTests/ChartThemeTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `ChartTheme`。
- Produces（`namespace MrtRouteSimulator.App`）：
  - `public enum ChartLegendMarker { Line, Dash, Dot, Point }`
  - `public sealed record ChartLegendItem(string Label, Brush Brush, ChartLegendMarker Marker)`
  - `public static class ChartPainter`：常數 `LegendTag = "ChartLegend"`、`TitleTag = "ChartTitle"`、`MessageTag = "ChartMessage"`、`GridTag = "ChartGrid"`、`ValueTickTag = "ChartTick"`、`TimeTickTag = "ChartTimeTick"`；`readonly record struct ChartArea(double Left, double Top, double Width, double Height)`（含 `Right`、`Bottom`）；`double[] NiceTicks(double maxValue, int targetCount = 5)`；`double DrawValueAxis(Canvas, ChartArea, double maxValue, string unit)`；`void DrawAxes(Canvas, ChartArea, string verticalLabel, string horizontalLabel)`；`void DrawTimeAxis(Canvas, ChartArea, IReadOnlyList<(double X, string Label)> ticks)`；`StackPanel DrawLegend(Canvas, double left, double top, IReadOnlyList<ChartLegendItem> items)`；`TextBlock DrawTitle(Canvas, string text, double left, double top)`；`(TextBlock? Title, StackPanel Legend) DrawHeader(Canvas, string title, IReadOnlyList<ChartLegendItem> items, double left, double top)`；`void DrawMessage(Canvas, string text)`；`Polyline CreateSeries(Brush, double thickness, DoubleCollection? dash = null)`；`TextBlock CreateLabel(string text, Brush brush, double fontSize = ChartTheme.AxisLabelFontSize)`；`void Place(Canvas, UIElement, double left, double top)`。
  - 測試端：`ChartThemeTests.ChartSources`（掃描清單，後續 Task 逐步加入）與 `VerifyNoHardCodedChartColors(string root)`。

- [ ] **Step 1: 寫失敗測試**

`Run` 改為：

```csharp
    public static void Run(string root)
    {
        VerifyThemeTokens();
        VerifyNiceTicks();
        VerifyPainterElements();
        VerifyHeaderFitsNarrowCanvas();
        VerifyNoHardCodedChartColors(root);
        Console.WriteLine("PASS WPF chart theme");
    }
```

加入：

```csharp
    private static void VerifyNiceTicks()
    {
        void Expect(double max, double[] expected)
        {
            var actual = ChartPainter.NiceTicks(max);
            Require(actual.Length == expected.Length && actual.Zip(expected).All(pair => Math.Abs(pair.First - pair.Second) < 1e-9),
                $"NiceTicks({max}) 應為 {string.Join("/", expected)}，實際 {string.Join("/", actual)}。");
        }
        Expect(80.1, [0, 20, 40, 60, 80, 100]);
        Expect(1767.7, [0, 500, 1000, 1500, 2000]);
        Expect(100, [0, 20, 40, 60, 80, 100]);
        Expect(50, [0, 10, 20, 30, 40, 50]);
        Expect(0.3, [0, 0.1, 0.2, 0.3]);
        foreach (var invalid in new[] { 0d, -5d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Expect(invalid, [0, 1]);
        var huge = ChartPainter.NiceTicks(double.MaxValue);
        Require(huge.All(double.IsFinite) && huge[^1] >= double.MaxValue * 0.999, "極大值的刻度不得溢位成無限大。");
        Console.WriteLine("[通過] ChartPainter 取整刻度（含 0、負值、NaN、無限大與極大值防護）");
    }

    private static void VerifyPainterElements()
    {
        var canvas = new Canvas { Width = 600, Height = 300 };
        var area = new ChartPainter.ChartArea(42, 26, 540, 200);
        var top = ChartPainter.DrawValueAxis(canvas, area, 80.1, "km/h");
        Require(top == 100, $"DrawValueAxis 應回傳刻度最大值 100，實際 {top}。");
        var grid = canvas.Children.OfType<Line>().Where(line => Equals(line.Tag, ChartPainter.GridTag)).ToArray();
        Require(grid.Length == 5 && grid.All(line => ReferenceEquals(line.Stroke, ChartTheme.Grid) && line.StrokeThickness == 1)
                && Math.Abs(grid.Min(line => line.Y1) - area.Top) < 1e-9,
            "80.1 的數值軸應有 5 條格線（0 以外），最上面一條在繪圖區頂端，且用 Grid 畫筆。");
        var ticks = canvas.Children.OfType<TextBlock>().Where(text => Equals(text.Tag, ChartPainter.ValueTickTag)).ToArray();
        Require(ticks.Select(text => text.Text).SequenceEqual(["0", "20", "40", "60", "80", "100"])
                && ticks.All(text => ReferenceEquals(text.Foreground, ChartTheme.AxisLabel) && text.FontSize == 10
                    && Canvas.GetLeft(text) + text.DesiredSize.Width <= area.Left),
            "數值刻度文字應為 0～100、AxisLabel 色、10 px，且位於 Y 軸左側。");
        var axes = canvas.Children.OfType<Line>().Where(line => ReferenceEquals(line.Stroke, ChartTheme.Axis)).ToArray();
        Require(axes.Length == 2 && axes.All(line => line.StrokeThickness == 1.2), "兩條軸線必須用 Axis 畫筆、線寬 1.2。");
        Require(canvas.Children.OfType<TextBlock>().Any(text => text.Text == "km/h" && ReferenceEquals(text.Foreground, ChartTheme.AxisLabel)),
            "單位文字必須用 AxisLabel 色。");

        ChartPainter.DrawTimeAxis(canvas, area, [(42d, "06:00:00"), (582d, "07:00:00")]);
        var timeLabels = canvas.Children.OfType<TextBlock>().Where(text => Equals(text.Tag, ChartPainter.TimeTickTag)).ToArray();
        Require(timeLabels.Length == 2 && timeLabels.All(text => text.FontSize == 9 && ReferenceEquals(text.Foreground, ChartTheme.AxisLabel)
                    && Canvas.GetLeft(text) >= area.Left - 2 && Canvas.GetLeft(text) + text.DesiredSize.Width <= area.Right + 0.01
                    && Math.Abs(Canvas.GetTop(text) - (area.Bottom + 5)) < 1e-9),
            "時間刻度文字必須 9 px、AxisLabel 色，置中於刻度但不超出繪圖區左右界。");

        var legend = ChartPainter.DrawLegend(canvas, 50, 4,
        [
            new ChartLegendItem("實線", ChartTheme.PrimarySeries, ChartLegendMarker.Line),
            new ChartLegendItem("虛線", ChartTheme.ThresholdSeries, ChartLegendMarker.Dash),
            new ChartLegendItem("點線", ChartTheme.DangerSeries, ChartLegendMarker.Dot),
            new ChartLegendItem("色點", ChartTheme.EventStation, ChartLegendMarker.Point)
        ]);
        Require(Equals(legend.Tag, ChartPainter.LegendTag) && Canvas.GetLeft(legend) == 50 && Canvas.GetTop(legend) == 4
                && legend.Orientation == Orientation.Horizontal, "圖例必須是 Tag=ChartLegend 的水平 StackPanel，放在指定位置。");
        Require(legend.Children.OfType<TextBlock>().Select(text => text.Text).SequenceEqual(["實線", "虛線", "點線", "色點"])
                && legend.Children.OfType<TextBlock>().All(text => text.FontSize == 11 && ReferenceEquals(text.Foreground, ChartTheme.LegendText)),
            "圖例文字必須依序、11 px、LegendText 色。");
        var markers = legend.Children.OfType<Shape>().ToArray();
        Require(markers.Length == 4
                && markers[0] is Line { StrokeDashArray: var solid } && (solid?.Count ?? 0) == 0
                && markers[1] is Line { StrokeDashArray: { } dash } && dash.SequenceEqual(new double[] { 3, 2 })
                && markers[2] is Line { StrokeDashArray: { } dot } && dot.SequenceEqual(new double[] { 1, 1.5 })
                && markers[3] is Ellipse { Fill: var pointFill, Stroke: var pointStroke }
                && ReferenceEquals(pointFill, ChartTheme.EventStation) && ReferenceEquals(pointStroke, ChartTheme.MarkerOutline)
                && ReferenceEquals(markers[0].Stroke, ChartTheme.PrimarySeries),
            "圖例樣本必須依線型為實線／虛線／點線／白框色點，顏色與項目一致。");

        var title = ChartPainter.DrawTitle(canvas, "測試標題", 42, 4);
        Require(Equals(title.Tag, ChartPainter.TitleTag) && title.FontSize == 13 && title.FontWeight == FontWeights.SemiBold
                && ReferenceEquals(title.Foreground, ChartTheme.Title), "標題必須 13 px 半粗體、Title 色、Tag=ChartTitle。");
        var messageCanvas = new Canvas();
        ChartPainter.DrawMessage(messageCanvas, "尚無資料");
        var message = messageCanvas.Children.OfType<TextBlock>().Single();
        Require(messageCanvas.Children.Count == 1 && message.Text == "尚無資料" && message.FontSize == 12
                && Equals(message.Tag, ChartPainter.MessageTag) && ReferenceEquals(message.Foreground, ChartTheme.Message)
                && Canvas.GetLeft(message) == 18 && Canvas.GetTop(message) == 18,
            "空白提示必須 12 px、Message 色、Tag=ChartMessage，放在 (18, 18)。");
        var series = ChartPainter.CreateSeries(ChartTheme.DangerSeries, 1.8, ChartTheme.ShortDot);
        Require(ReferenceEquals(series.Stroke, ChartTheme.DangerSeries) && series.StrokeThickness == 1.8
                && ReferenceEquals(series.StrokeDashArray, ChartTheme.ShortDot), "CreateSeries 必須套用畫筆、線寬與線型。");
        Require(canvas.Children.OfType<TextBlock>().Concat(legend.Children.OfType<TextBlock>())
                .All(text => ReferenceEquals(text.FontFamily, ChartTheme.Font)), "所有圖表文字必須使用 App 字型。");
        Console.WriteLine("[通過] ChartPainter 軸線、刻度、圖例、標題與空白提示樣式");
    }

    private static void VerifyHeaderFitsNarrowCanvas()
    {
        ChartLegendItem[] items =
        [
            new("實際淨距", ChartTheme.PrimarySeries, ChartLegendMarker.Line),
            new("動態安全距離", ChartTheme.ThresholdSeries, ChartLegendMarker.Dash),
            new("障礙物煞車需求", ChartTheme.DangerSeries, ChartLegendMarker.Dot)
        ];
        var wide = new Canvas { Width = 900, Height = 200 };
        var (wideTitle, wideLegend) = ChartPainter.DrawHeader(wide, "FULL-O04 → FULL-O13", items, 52, 4);
        Require(wideTitle is not null && Canvas.GetLeft(wideTitle) == 52
                && Canvas.GetLeft(wideLegend) >= 52 + wideTitle.DesiredSize.Width + 16 - 0.01,
            "寬畫布：標題在左、圖例接在標題右側。");
        var narrow = new Canvas { Width = 420, Height = 200 };
        var (narrowTitle, narrowLegend) = ChartPainter.DrawHeader(narrow, "FULL-O04 → FULL-O13", items, 52, 4);
        Require(narrowTitle is null && !narrow.Children.OfType<TextBlock>().Any(text => Equals(text.Tag, ChartPainter.TitleTag)),
            "窄畫布放不下標題＋圖例時必須省略標題。");
        Require(Canvas.GetLeft(narrowLegend) == 52 && 52 + narrowLegend.DesiredSize.Width <= 420 - 4,
            $"窄畫布的圖例必須完整留在畫布內（寬 {narrowLegend.DesiredSize.Width:0.0}）。");
        Console.WriteLine("[通過] 標題列在窄畫布省略標題、保留完整圖例");
    }

    // 掃描範圍：方法清單為空表示整個檔案，否則只掃描列出的方法本體。後續 Task 逐步加入。
    private static readonly (string File, string[] Methods)[] ChartSources =
    [
        ("ChartTheme.cs", []),
        ("ChartPainter.cs", [])
    ];

    private static readonly Regex HardCodedColor = new(
        @"\bColor\.From(Rgb|Argb)\b|\bBrushes\.(?!Transparent\b)[A-Z]\w*|\bColors\.(?!Transparent\b)[A-Z]\w*",
        RegexOptions.Compiled);

    private static void VerifyNoHardCodedChartColors(string root)
    {
        Require(HardCodedColor.IsMatch("Stroke = Brushes.SlateGray") && HardCodedColor.IsMatch("Color.FromRgb(1, 2, 3)")
                && HardCodedColor.IsMatch("Color.FromArgb(1, 2, 3, 4)") && HardCodedColor.IsMatch("Colors.White")
                && !HardCodedColor.IsMatch("Brushes.Transparent") && !HardCodedColor.IsMatch("UiTheme.VehicleBrushes[0]")
                && !HardCodedColor.IsMatch("SystemColors.ControlBrush"), "寫死顏色掃描規則自我檢查失敗。");
        var sample = "private void A()\n{\n    if (x) { y(); }\n}\nprivate void B() { z(); }";
        var body = MethodBody(sample, "A", "sample");
        Require(body.Contains("y();", StringComparison.Ordinal) && !body.Contains("z();", StringComparison.Ordinal),
            "方法本體擷取自我檢查失敗。");

        var findings = new List<string>();
        foreach (var (file, methods) in ChartSources)
        {
            var source = File.ReadAllText(System.IO.Path.Combine(root, "src", "MrtRouteSimulator.App", file));
            IEnumerable<(string Scope, string Text)> scopes = methods.Length == 0
                ? [(file, source)]
                : methods.Select(method => ($"{file} {method}", MethodBody(source, method, file)));
            foreach (var (scope, text) in scopes)
                findings.AddRange(HardCodedColor.Matches(text).Select(match => $"{scope}：{match.Value}"));
        }
        Require(findings.Count == 0, "圖表程式不得寫死顏色：" + string.Join("、", findings.Distinct()));
        Console.WriteLine($"[通過] 圖表程式無寫死顏色（{ChartSources.Length} 個來源檔）");
    }

    private static string MethodBody(string source, string method, string file)
    {
        var match = Regex.Match(source, @"(private|internal|public)[^;{=]*?\b" + Regex.Escape(method) + @"\s*\(");
        Require(match.Success, $"{file} 找不到方法 {method}。");
        var open = source.IndexOf('{', match.Index);
        var depth = 0;
        for (var index = open; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0) return source[open..(index + 1)];
        }
        throw new InvalidOperationException($"{file} 的 {method} 大括號不成對。");
    }
```

- [ ] **Step 2: 執行，確認失敗**

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
```

Expected: 建置失敗，`ChartPainter`、`ChartLegendItem`、`ChartLegendMarker` 不存在。

- [ ] **Step 3: 實作**

建立 `src/MrtRouteSimulator.App/ChartPainter.cs`：

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MrtRouteSimulator.App;

public enum ChartLegendMarker
{
    Line,
    Dash,
    Dot,
    Point
}

public sealed record ChartLegendItem(string Label, Brush Brush, ChartLegendMarker Marker);

/// <summary>
/// 結果圖表的共用繪圖工具：取整數值刻度、軸線、時間刻度、圖例、標題與空白提示。
/// 只負責外觀；資料換算由各圖依 <see cref="DrawValueAxis"/> 回傳的刻度最大值自行處理，
/// 讓線與刻度對齊。
/// </summary>
public static class ChartPainter
{
    public const string LegendTag = "ChartLegend";
    public const string TitleTag = "ChartTitle";
    public const string MessageTag = "ChartMessage";
    public const string GridTag = "ChartGrid";
    public const string ValueTickTag = "ChartTick";
    public const string TimeTickTag = "ChartTimeTick";

    private static readonly Size Unbounded = new(double.PositiveInfinity, double.PositiveInfinity);

    public readonly record struct ChartArea(double Left, double Top, double Width, double Height)
    {
        public double Right => Left + Width;
        public double Bottom => Top + Height;
    }

    /// <summary>
    /// 以 1／2／5×10ⁿ 為間距、從 0 開始且最後一格不小於 <paramref name="maxValue"/> 的刻度。
    /// 非有限值或非正值回傳 0、1，避免座標換算產生 NaN 或無限大。
    /// </summary>
    public static double[] NiceTicks(double maxValue, int targetCount = 5)
    {
        if (!double.IsFinite(maxValue) || maxValue <= 0) return [0, 1];
        var rough = maxValue / Math.Max(1, targetCount);
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var normalized = rough / magnitude;
        var step = (normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10) * magnitude;
        var count = Math.Max(1, (int)Math.Ceiling(maxValue / step - 1e-9));
        if (!double.IsFinite(step * count)) return [0, maxValue];
        return Enumerable.Range(0, count + 1).Select(index => index * step).ToArray();
    }

    /// <summary>畫橫向格線、Y 刻度文字、兩條軸線與單位；回傳刻度最大值，資料以它換算座標。</summary>
    public static double DrawValueAxis(Canvas canvas, ChartArea area, double maxValue, string unit)
    {
        var ticks = NiceTicks(maxValue);
        var top = ticks[^1];
        foreach (var value in ticks)
        {
            var y = area.Bottom - value / top * area.Height;
            if (value > 0)
            {
                canvas.Children.Add(new Line
                {
                    X1 = area.Left,
                    X2 = area.Right,
                    Y1 = y,
                    Y2 = y,
                    Stroke = ChartTheme.Grid,
                    StrokeThickness = ChartTheme.GridThickness,
                    Tag = GridTag
                });
            }

            var label = CreateLabel(value.ToString("0.###", CultureInfo.InvariantCulture), ChartTheme.AxisLabel);
            label.Tag = ValueTickTag;
            label.Measure(Unbounded);
            Place(canvas, label, Math.Max(0, area.Left - 4 - label.DesiredSize.Width), y - label.DesiredSize.Height / 2);
        }

        DrawAxes(canvas, area, unit, string.Empty);
        return top;
    }

    /// <summary>畫左側與下方兩條軸線；軸名為空字串時不加文字。</summary>
    public static void DrawAxes(Canvas canvas, ChartArea area, string verticalLabel, string horizontalLabel)
    {
        canvas.Children.Add(new Line
        {
            X1 = area.Left,
            X2 = area.Left,
            Y1 = area.Top,
            Y2 = area.Bottom,
            Stroke = ChartTheme.Axis,
            StrokeThickness = ChartTheme.AxisThickness
        });
        canvas.Children.Add(new Line
        {
            X1 = area.Left,
            X2 = area.Right,
            Y1 = area.Bottom,
            Y2 = area.Bottom,
            Stroke = ChartTheme.Axis,
            StrokeThickness = ChartTheme.AxisThickness
        });
        if (!string.IsNullOrEmpty(verticalLabel)) Place(canvas, CreateLabel(verticalLabel, ChartTheme.AxisLabel), 3, 2);
        if (!string.IsNullOrEmpty(horizontalLabel))
            Place(canvas, CreateLabel(horizontalLabel, ChartTheme.AxisLabel), area.Right - 48, area.Bottom + 12);
    }

    /// <summary>畫時間刻度短線與置中的刻度文字；文字不超出繪圖區左右界。</summary>
    public static void DrawTimeAxis(Canvas canvas, ChartArea area, IReadOnlyList<(double X, string Label)> ticks)
    {
        foreach (var (x, text) in ticks)
        {
            canvas.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = area.Bottom,
                Y2 = area.Bottom + 4,
                Stroke = ChartTheme.Axis,
                StrokeThickness = 1
            });
            var label = CreateLabel(text, ChartTheme.AxisLabel, ChartTheme.TimeTickFontSize);
            label.Tag = TimeTickTag;
            label.Measure(Unbounded);
            var width = label.DesiredSize.Width;
            Place(canvas, label,
                Math.Clamp(x - width / 2, area.Left - 2, Math.Max(area.Left - 2, area.Right - width)),
                area.Bottom + 5);
        }
    }

    public static StackPanel DrawLegend(Canvas canvas, double left, double top, IReadOnlyList<ChartLegendItem> items)
    {
        var legend = CreateLegend(items);
        Place(canvas, legend, left, top);
        return legend;
    }

    public static TextBlock DrawTitle(Canvas canvas, string text, double left, double top)
    {
        var title = CreateTitle(text);
        Place(canvas, title, left, top);
        return title;
    }

    /// <summary>
    /// 標題與圖例排成一列；畫布太窄放不下時省略標題（下拉選單已顯示同樣資訊），圖例一律保留。
    /// </summary>
    public static (TextBlock? Title, StackPanel Legend) DrawHeader(
        Canvas canvas, string title, IReadOnlyList<ChartLegendItem> items, double left, double top)
    {
        var titleBlock = CreateTitle(title);
        var legend = CreateLegend(items);
        titleBlock.Measure(Unbounded);
        legend.Measure(Unbounded);
        var canvasWidth = double.IsNaN(canvas.Width) ? canvas.ActualWidth : canvas.Width;
        var legendLeft = left + titleBlock.DesiredSize.Width + 16;
        var showTitle = canvasWidth <= 0 || legendLeft + legend.DesiredSize.Width <= canvasWidth - 4;
        if (showTitle) Place(canvas, titleBlock, left, top);
        else legendLeft = left;
        Place(canvas, legend, legendLeft,
            top + Math.Max(0, (titleBlock.DesiredSize.Height - legend.DesiredSize.Height) / 2));
        return (showTitle ? titleBlock : null, legend);
    }

    public static void DrawMessage(Canvas canvas, string text)
    {
        var message = CreateLabel(text, ChartTheme.Message, ChartTheme.MessageFontSize);
        message.Tag = MessageTag;
        Place(canvas, message, 18, 18);
    }

    public static Polyline CreateSeries(Brush brush, double thickness, DoubleCollection? dash = null) => new()
    {
        Stroke = brush,
        StrokeThickness = thickness,
        StrokeDashArray = dash,
        StrokeLineJoin = PenLineJoin.Round
    };

    public static TextBlock CreateLabel(string text, Brush brush, double fontSize = ChartTheme.AxisLabelFontSize) => new()
    {
        Text = text,
        Foreground = brush,
        FontSize = fontSize,
        FontFamily = ChartTheme.Font
    };

    public static void Place(Canvas canvas, UIElement element, double left, double top)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        canvas.Children.Add(element);
    }

    private static TextBlock CreateTitle(string text)
    {
        var title = CreateLabel(text, ChartTheme.Title, ChartTheme.TitleFontSize);
        title.FontWeight = FontWeights.SemiBold;
        title.Tag = TitleTag;
        return title;
    }

    private static StackPanel CreateLegend(IReadOnlyList<ChartLegendItem> items)
    {
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Tag = LegendTag };
        foreach (var item in items)
        {
            legend.Children.Add(CreateLegendMarker(item));
            var label = CreateLabel(item.Label, ChartTheme.LegendText, ChartTheme.LegendFontSize);
            label.Margin = new Thickness(0, 0, 12, 0);
            label.VerticalAlignment = VerticalAlignment.Center;
            legend.Children.Add(label);
        }

        return legend;
    }

    private static FrameworkElement CreateLegendMarker(ChartLegendItem item)
    {
        if (item.Marker == ChartLegendMarker.Point)
        {
            return new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = item.Brush,
                Stroke = ChartTheme.MarkerOutline,
                StrokeThickness = 1,
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        return new Line
        {
            X1 = 0,
            X2 = 18,
            Y1 = 5,
            Y2 = 5,
            Width = 18,
            Height = 10,
            Stroke = item.Brush,
            StrokeThickness = 2,
            StrokeDashArray = item.Marker switch
            {
                ChartLegendMarker.Dash => [3, 2],
                ChartLegendMarker.Dot => [1, 1.5],
                _ => null
            },
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
    }
}
```

- [ ] **Step 4: 執行，確認通過**

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --chart-theme-only
```

Expected: 0／0；四行新的 `[通過]`（取整刻度、繪圖工具樣式、窄畫布標題列、無寫死顏色 2 個來源檔）與 `PASS WPF chart theme`。

- [ ] **Step 5: Commit**

```powershell
git add src/MrtRouteSimulator.App/ChartPainter.cs tests/MrtRouteSimulator.WpfTests/ChartThemeTests.cs
git commit -m "feat: add ChartPainter shared chart drawing helpers" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: 速度曲線（V2 與 V1）

**Files:**
- Modify: `src/MrtRouteSimulator.App/MainWindow.V2.cs`（`DrawV2SpeedProfile`、`DrawSpeedLimitLabels`、`DrawSpeedStopLabels`、`DrawSpeedTimeAxisTicks`）
- Modify: `src/MrtRouteSimulator.App/MainWindow.xaml.cs`（`DrawSpeedProfile`）
- Test: `tests/MrtRouteSimulator.WpfTests/ChartThemeTests.cs`

**Interfaces:**
- Consumes: Task 1、2 的 `ChartTheme`、`ChartPainter`、`ChartLegendItem`。
- Produces: `private void DrawSpeedTimeAxisTicks(Canvas canvas, ChartPainter.ChartArea area, double minTime, double maxTime)`（Task 4 的距離圖使用）；測試端 `VerifyV2Charts(string root)`（Task 4、5、6 在其中加呼叫）與 `VerifyV1Charts()`（Task 7 擴充）。

- [ ] **Step 1: 寫失敗測試**

`Run` 改為：

```csharp
    public static void Run(string root)
    {
        VerifyThemeTokens();
        VerifyNiceTicks();
        VerifyPainterElements();
        VerifyHeaderFitsNarrowCanvas();
        VerifyV2Charts(root);
        VerifyV1Charts();
        VerifyNoHardCodedChartColors(root);
        Console.WriteLine("PASS WPF chart theme");
    }
```

`ChartSources` 改為：

```csharp
    private static readonly (string File, string[] Methods)[] ChartSources =
    [
        ("ChartTheme.cs", []),
        ("ChartPainter.cs", []),
        ("MainWindow.V2.cs", ["DrawV2SpeedProfile", "DrawSpeedLimitLabels", "DrawSpeedStopLabels", "DrawSpeedTimeAxisTicks"]),
        ("MainWindow.xaml.cs", ["DrawSpeedProfile"])
    ];
```

加入：

```csharp
    private static void VerifyV2Charts(string root)
    {
        var window = new MainWindow { Width = 1280, Height = 800 };
        try
        {
            ShellLayoutTests.LoadSample(window, root, "14-大型-二十八站完整營運範例.mrtsim.json");
            window.Show();
            WpfTestWait.WaitForPlannedTimeline(window);
            ((TabControl)window.FindName("WorkspaceTabControl")!).SelectedItem = window.FindName("SimulationTabItem");
            ((TabControl)window.FindName("SimulationViewTabControl")!).SelectedIndex = 2;
            ShellLayoutTests.PumpLayout(window);
            WpfTestWait.Invoke(window, "DrawV2SpeedProfile");
            VerifySpeedChart(window, "計畫速度");
            WpfTestWait.Advance(window, 600);
            Require(WpfTestWait.LatestFrame(window).SafetyHistory.Count > 0, "範例 14 推進 600 秒後必須已有相鄰列車安全觀測。");
            ((TabControl)window.FindName("WorkspaceTabControl")!).SelectedItem = window.FindName("SimulationTabItem");
            ((TabControl)window.FindName("SimulationViewTabControl")!).SelectedIndex = 2;
            ShellLayoutTests.PumpLayout(window);
            WpfTestWait.Invoke(window, "DrawV2SpeedProfile");
            VerifySpeedChart(window, "實際速度");
        }
        finally { WpfTestWait.Close(window); }
    }

    private static void VerifySpeedChart(MainWindow window, string seriesLabel)
    {
        var canvas = (Canvas)window.FindName("SpeedCanvas")!;
        var vehicle = ((ComboBox)window.FindName("SpeedProfileRunComboBox")!).SelectedItem?.ToString();
        Require(vehicle is not null && canvas.ActualWidth >= 100, "速度曲線必須已選定列車且畫布已配置。");
        var parseIndex = typeof(MainWindow).GetMethod("ParseVehicleIndex", BindingFlags.Static | BindingFlags.NonPublic)!;
        var vehicleBrush = UiTheme.VehicleBrush((int)parseIndex.Invoke(null, [vehicle])!);
        var lines = canvas.Children.OfType<Polyline>().ToArray();
        Require(lines.Length == 2, $"速度曲線應只有速限與速度兩條線，實際 {lines.Length}。");
        Require(ReferenceEquals(lines[1].Stroke, vehicleBrush) && lines[1].StrokeThickness == 2.4,
            $"{vehicle} 的速度線必須使用該列車的車輛色、線寬 2.4。");
        Require(ReferenceEquals(lines[0].Stroke, ChartTheme.LimitSeries) && ReferenceEquals(lines[0].StrokeDashArray, ChartTheme.LongDash),
            "速限線必須為 LimitSeries 灰色虛線（5,3）。");
        Require(lines.SelectMany(line => line.Points).All(point => point.Y >= 26 - 0.01),
            "速度與速限線不得高於繪圖區頂端。");
        var ticks = canvas.Children.OfType<TextBlock>().Where(text => Equals(text.Tag, ChartPainter.ValueTickTag)).ToArray();
        Require(ticks.Length >= 3 && ticks[0].Text == "0" && canvas.Children.OfType<TextBlock>().Any(text => text.Text == "km/h"),
            "速度曲線必須有從 0 開始的 km/h 數值刻度。");
        Require(canvas.Children.OfType<TextBlock>().Count(text => Equals(text.Tag, ChartPainter.TimeTickTag)) == 5,
            "速度曲線必須有五個時間刻度。");
        var legend = canvas.Children.OfType<StackPanel>().Single(panel => Equals(panel.Tag, ChartPainter.LegendTag));
        Require(legend.Children.OfType<TextBlock>().Select(text => text.Text).SequenceEqual([seriesLabel, "軌道速限"]),
            $"速度曲線圖例應為「{seriesLabel}／軌道速限」。");
        Require(ReferenceEquals(((Line)legend.Children[0]).Stroke, vehicleBrush), "圖例的速度線樣本必須與速度線同色。");
        var title = canvas.Children.OfType<TextBlock>().SingleOrDefault(text => Equals(text.Tag, ChartPainter.TitleTag));
        Require(title?.Text == $"{vehicle} 速度", $"速度曲線標題應為「{vehicle} 速度」，實際「{title?.Text}」。");
        Require(canvas.Children.OfType<TextBlock>().Where(text => text.Tag is "SpeedLimitLabel" or "StopStationLabel")
                .All(text => ReferenceEquals(text.Foreground, ChartTheme.AxisLabel) && ReferenceEquals(text.FontFamily, ChartTheme.Font)),
            "速限與停站標籤必須用 AxisLabel 色與 App 字型。");
        Console.WriteLine($"[通過] 速度曲線主題（{seriesLabel}：車輛色、刻度、圖例、標題）");
    }

    private static void VerifyV1Charts()
    {
        var window = new MainWindow { Width = 1280, Height = 800 };
        try
        {
            window.Show();
            ShellLayoutTests.PumpLayout(window);
            BuildV1Simulation(window);
            ((TabControl)window.FindName("WorkspaceTabControl")!).SelectedItem = window.FindName("SimulationTabItem");
            var views = (TabControl)window.FindName("SimulationViewTabControl")!;
            views.SelectedIndex = 2;
            ShellLayoutTests.PumpLayout(window);
            WpfTestWait.Invoke(window, "DrawSpeedProfile");
            var speed = (Canvas)window.FindName("SpeedCanvas")!;
            var line = speed.Children.OfType<Polyline>().Single();
            Require(ReferenceEquals(line.Stroke, UiTheme.VehicleBrush(0)), "V1 速度線必須使用第一台車的車輛色。");
            Require(line.Points.All(point => point.Y >= 26 - 0.01), "V1 速度線不得高於繪圖區頂端。");
            Require(speed.Children.OfType<TextBlock>().Any(text => Equals(text.Tag, ChartPainter.ValueTickTag) && text.Text == "0")
                    && speed.Children.OfType<TextBlock>().Count(text => Equals(text.Tag, ChartPainter.TimeTickTag)) == 5
                    && speed.Children.OfType<StackPanel>().Any(panel => Equals(panel.Tag, ChartPainter.LegendTag)),
                "V1 速度曲線必須有數值刻度、五個時間刻度與圖例。");
        }
        finally { WpfTestWait.Close(window); }
        Console.WriteLine("[通過] V1 速度曲線使用圖表主題");
    }
```

- [ ] **Step 2: 執行，確認失敗**

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --chart-theme-only
```

Expected: 建置成功；執行以 `速限線必須為 LimitSeries 灰色虛線（5,3）。` 或 `的速度線必須使用該列車的車輛色` 失敗（舊速度線為橘色 `new SolidColorBrush`）。

- [ ] **Step 3: 實作 V2 速度曲線**

`MainWindow.V2.cs`：把 `DrawV2SpeedProfile` 整個方法換成：

```csharp
    private void DrawV2SpeedProfile()
    {
        if (_updatingSpeedProfileSelection) return;
        var canvas = SpeedCanvas;
        canvas.Children.Clear();
        var width = canvas.ActualWidth;
        var height = canvas.ActualHeight;
        if (!double.IsFinite(width) || !double.IsFinite(height)
            || width < 100 || height < 100 || _latestPlaybackFrame is null || _parameters is null)
        {
            return;
        }

        var selectedVehicleId = SpeedProfileRunComboBox.SelectedItem?.ToString();
        _updatingSpeedProfileSelection = true;
        try
        {
            foreach (var vehicleId in _latestPlaybackFrame.GetSnapshot().Trains.Select(train => train.VehicleId)
                         .Distinct(StringComparer.Ordinal))
            {
                if (!SpeedProfileRunComboBox.Items.Contains(vehicleId)) SpeedProfileRunComboBox.Items.Add(vehicleId);
            }
            if (selectedVehicleId is null && SpeedProfileRunComboBox.Items.Count > 0)
            {
                SpeedProfileRunComboBox.SelectedIndex = 0;
                selectedVehicleId = SpeedProfileRunComboBox.SelectedItem?.ToString();
            }
        }
        finally { _updatingSpeedProfileSelection = false; }
        var actualSamples = _latestPlaybackFrame.Trajectory
            .Where(sample => sample.VehicleId == selectedVehicleId)
            .ToArray();
        var useActual = actualSamples.Length >= 2;
        var plannedArtifact = _plannedTimelineArtifact;
        var samples = useActual
            ? actualSamples
            : (plannedArtifact?.Trajectory ?? [])
                .Where(sample => sample.VehicleId == selectedVehicleId)
                .ToArray();
        if (samples.Length < 2)
        {
            SpeedProfileSourceText.Text = plannedArtifact is null
                ? "計畫時間軸計算中…"
                : selectedVehicleId is null ? "尚無列車" : $"{selectedVehicleId} · 尚無軌跡";
            ChartPainter.DrawMessage(canvas, plannedArtifact is null
                ? "實際模擬可先播放；計畫速度圖完成後會自動顯示。"
                : "播放後顯示所選列車的上下行、停站及折返軌跡。");
            return;
        }

        IReadOnlyList<SimulationEvent> sourceEvents = useActual
            ? _latestPlaybackFrame.Events
            : plannedArtifact!.Events;
        var complete = sourceEvents.Any(item => item.VehicleId == selectedVehicleId && item.EventType == SimulationEventType.ServiceEnded);
        SpeedProfileSourceText.Text = useActual
            ? complete ? "V2 實際" : "實際（截至目前）"
            : complete ? "計畫預覽" : "計畫預覽（尚未完成）";
        SpeedProfileSourceText.ToolTip = $"{selectedVehicleId} · 同一列車上下行及折返接續軌跡；"
            + (useActual ? "顯示截至目前的實際運行。" : "顯示計畫模擬時間範圍內的運行。");

        // 下方 62 DIP 留給時間刻度與兩列停站標籤。
        var area = new ChartPainter.ChartArea(42, 26, width - 42 - 15, height - 26 - 62);
        var minTime = samples[0].SimulationTimeSeconds;
        var maxTime = Math.Max(minTime + 1, samples[^1].SimulationTimeSeconds);
        var vehicleBrush = UiTheme.VehicleBrush(ParseVehicleIndex(selectedVehicleId!));
        ChartPainter.DrawHeader(canvas, $"{selectedVehicleId} 速度",
        [
            new ChartLegendItem(useActual ? "實際速度" : "計畫速度", vehicleBrush, ChartLegendMarker.Line),
            new ChartLegendItem("軌道速限", ChartTheme.LimitSeries, ChartLegendMarker.Dash)
        ], area.Left, 4);
        var maxSpeed = ChartPainter.DrawValueAxis(canvas, area, Math.Max(_parameters.MaxSpeedMetersPerSecond,
            samples.Max(sample => Math.Max(sample.SpeedMetersPerSecond, GetDisplaySpeedLimitMetersPerSecond(sample)))) * 3.6 * 1.1,
            "km/h");
        DrawSpeedTimeAxisTicks(canvas, area, minTime, maxTime);

        var speedLine = ChartPainter.CreateSeries(vehicleBrush, 2.4);
        var limitLine = ChartPainter.CreateSeries(ChartTheme.LimitSeries, 1.4, ChartTheme.LongDash);
        var displaySamples = TrajectoryAnalysis.DecimatePreservingCriticalPoints(samples, 450);
        foreach (var sample in displaySamples)
        {
            var x = area.Left + (sample.SimulationTimeSeconds - minTime) / (maxTime - minTime) * area.Width;
            speedLine.Points.Add(new Point(x, area.Bottom - sample.SpeedMetersPerSecond * 3.6 / maxSpeed * area.Height));
            var limit = GetDisplaySpeedLimitMetersPerSecond(sample) * 3.6;
            limitLine.Points.Add(new Point(x, area.Bottom - limit / maxSpeed * area.Height));
        }

        canvas.Children.Add(limitLine);
        canvas.Children.Add(speedLine);
        DrawSpeedLimitLabels(canvas, displaySamples, area.Left, area.Top, area.Width, area.Height, minTime, maxTime, maxSpeed);
        DrawSpeedStopLabels(canvas, displaySamples, area.Left, area.Bottom, area.Width, minTime, maxTime);
        var previousDirection = samples[0].Direction;
        var directionChangeIndex = 0;
        foreach (var sample in samples.Skip(1))
        {
            if (sample.Direction == previousDirection) continue;
            previousDirection = sample.Direction;
            var x = area.Left + (sample.SimulationTimeSeconds - minTime) / (maxTime - minTime) * area.Width;
            canvas.Children.Add(new Line
            {
                X1 = x, X2 = x, Y1 = area.Top + 16, Y2 = area.Bottom,
                Stroke = ChartTheme.Annotation, StrokeDashArray = ChartTheme.ShortDot,
                ToolTip = $"{TrajectoryAnalysis.FormatClock(_startClockSeconds + sample.SimulationTimeSeconds)} 換向為{DirectionToChinese(sample.Direction)} · {sample.ServiceRunId}"
            });
            ChartPainter.Place(canvas, ChartPainter.CreateLabel($"轉{DirectionToChinese(sample.Direction)}", ChartTheme.Annotation),
                Math.Clamp(x + 3, area.Left, Math.Max(area.Left, area.Right - 45)),
                area.Top + 18 + directionChangeIndex++ % 2 * 15);
        }
    }
```

`DrawSpeedLimitLabels`：把

```csharp
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(153, 92, 12)),
                Background = new SolidColorBrush(Color.FromArgb(225, 250, 251, 253)),
```

換成

```csharp
                FontSize = 9,
                FontFamily = ChartTheme.Font,
                Foreground = ChartTheme.AxisLabel,
                Background = ChartTheme.Background,
```

`DrawSpeedStopLabels`：把

```csharp
                Stroke = Brushes.SteelBlue, StrokeThickness = 1,
```

換成

```csharp
                Stroke = ChartTheme.AxisLabel, StrokeThickness = 1,
```

再把

```csharp
                FontSize = 9,
                Foreground = Brushes.SteelBlue,
```

換成

```csharp
                FontSize = 9,
                FontFamily = ChartTheme.Font,
                Foreground = ChartTheme.AxisLabel,
```

把 `DrawSpeedTimeAxisTicks` 整個方法換成：

```csharp
    // 速度曲線與距離圖共用：四等分時間刻度，以時鐘時間標示。
    private void DrawSpeedTimeAxisTicks(Canvas canvas, ChartPainter.ChartArea area, double minTime, double maxTime)
    {
        const int tickCount = 4;
        var ticks = new List<(double X, string Label)>(tickCount + 1);
        for (var index = 0; index <= tickCount; index++)
        {
            var ratio = index / (double)tickCount;
            ticks.Add((area.Left + ratio * area.Width,
                TrajectoryAnalysis.FormatClock(_startClockSeconds + minTime + ratio * (maxTime - minTime))));
        }
        ChartPainter.DrawTimeAxis(canvas, area, ticks);
    }
```

- [ ] **Step 4: 實作 V1 速度曲線**

`MainWindow.xaml.cs`：把 `DrawSpeedProfile` 從 `SpeedCanvas.Children.Clear();` 到 `SpeedCanvas.Children.Add(polyline);` 的方法本體換成（`if (_v2Enabled)` 開頭保留）：

```csharp
    private void DrawSpeedProfile()
    {
        if (_v2Enabled)
        {
            DrawV2SpeedProfile();
            return;
        }

        SpeedCanvas.Children.Clear();
        var width = SpeedCanvas.ActualWidth;
        var height = SpeedCanvas.ActualHeight;
        if (width < 100 || height < 100)
        {
            return;
        }

        if (_cycle is null || _parameters is null)
        {
            ChartPainter.DrawMessage(SpeedCanvas, "建立模擬後顯示速度－時間曲線。");
            return;
        }

        var area = new ChartPainter.ChartArea(42, 26, width - 42 - 16, height - 26 - 32);
        var totalTime = _cycle.OutboundTrip.TotalRunTimeSeconds;
        var vehicleBrush = UiTheme.VehicleBrush(0);
        ChartPainter.DrawHeader(SpeedCanvas, "V1 理論速度",
            [new ChartLegendItem("理論速度", vehicleBrush, ChartLegendMarker.Line)], area.Left, 4);
        var maxSpeed = ChartPainter.DrawValueAxis(SpeedCanvas, area, _parameters.MaxSpeedMetersPerSecond * 3.6 * 1.08, "km/h");
        ChartPainter.DrawTimeAxis(SpeedCanvas, area, Enumerable.Range(0, 5)
            .Select(index => (area.Left + area.Width * index / 4, $"{totalTime * index / 4:0} s"))
            .ToArray());
        var polyline = ChartPainter.CreateSeries(vehicleBrush, 2.4);

        void AddPoint(double time, double speed)
        {
            var x = area.Left + Math.Clamp(time / totalTime, 0, 1) * area.Width;
            var y = area.Bottom - Math.Clamp(speed * 3.6 / maxSpeed, 0, 1) * area.Height;
            polyline.Points.Add(new Point(x, y));
        }

        AddPoint(0, 0);
        foreach (var segment in _cycle.OutboundTrip.Segments)
        {
            var departure = segment.DepartureTimeSeconds - _cycle.OutboundTrip.DepartureFromOriginSeconds;
            var accelerationEnd = departure + segment.Motion.AccelerationTimeSeconds;
            var cruiseEnd = accelerationEnd + segment.Motion.CruisingTimeSeconds;
            var arrival = segment.ArrivalTimeSeconds - _cycle.OutboundTrip.DepartureFromOriginSeconds;
            AddPoint(departure, 0);
            AddPoint(accelerationEnd, segment.Motion.PeakSpeedMetersPerSecond);
            if (segment.Motion.CruisingTimeSeconds > 0)
            {
                AddPoint(cruiseEnd, segment.Motion.PeakSpeedMetersPerSecond);
            }

            AddPoint(arrival, 0);
            var destinationEvent = _cycle.OutboundTrip.StationEvents.First(item => item.StationId == segment.ToStation.StationId);
            AddPoint(destinationEvent.DepartureTimeSeconds - _cycle.OutboundTrip.DepartureFromOriginSeconds, 0);
        }

        SpeedCanvas.Children.Add(polyline);
    }
```

- [ ] **Step 5: 執行，確認通過**

```powershell
dotnet build .\MrtRouteSimulator.slnx -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --chart-theme-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --speed-only
```

Expected: 0／0；`[通過] 速度曲線主題（計畫速度…）`、`[通過] 速度曲線主題（實際速度…）`、`[通過] V1 速度曲線使用圖表主題`、`[通過] 圖表程式無寫死顏色（4 個來源檔）`、`PASS WPF chart theme`；`PASS WPF speed journey`。若 `SpeedJourneyTests` 因座標數值失敗，依 Global Constraints 只調整對應數值並記錄 `Ruling`。

- [ ] **Step 6: 完整 WPF runner**

```powershell
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- .
```

Expected: `PASS WPF visual rules`。

- [ ] **Step 7: Commit**

```powershell
git add src/MrtRouteSimulator.App/MainWindow.V2.cs src/MrtRouteSimulator.App/MainWindow.xaml.cs tests/MrtRouteSimulator.WpfTests/ChartThemeTests.cs
git commit -m "feat: theme speed profile charts with value ticks and legend" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: 相鄰列車距離圖與安全狀態色

**Files:**
- Modify: `src/MrtRouteSimulator.App/MainWindow.V2.cs`（`DrawSafetyDistanceChart`、新增 `AddSafetyMarginChip`、`SafetyStatusBrush`；移除 `SafetyStatusColor`、`CreateChartLine`；`DrawV2Route` 的列車間安全線）
- Test: `tests/MrtRouteSimulator.WpfTests/ChartThemeTests.cs`

**Interfaces:**
- Consumes: Task 3 的 `DrawSpeedTimeAxisTicks(Canvas, ChartPainter.ChartArea, double, double)`；`StatusTones.Classify(string?)`、`StatusTones.Brush(StatusTone)`。
- Produces: `private static SolidColorBrush SafetyStatusBrush(SafetyStatus status)`；`private static void AddSafetyMarginChip(Canvas canvas, SafetyObservation minimum, ChartPainter.ChartArea area)`（Border `Tag="SafetyMarginChip"`）。

- [ ] **Step 1: 寫失敗測試**

`Run` 在 `VerifyV2Charts(root);` 之前加入 `VerifySafetyStatusBrush();`。`VerifyV2Charts` 在最後一行 `VerifySpeedChart(window, "實際速度");` 之後加入：

```csharp
            VerifySafetyChart(window);
```

`ChartSources` 的 `MainWindow.V2.cs` 列改為：

```csharp
        ("MainWindow.V2.cs", ["DrawV2SpeedProfile", "DrawSpeedLimitLabels", "DrawSpeedStopLabels", "DrawSpeedTimeAxisTicks",
            "DrawSafetyDistanceChart", "AddSafetyMarginChip"]),
```

加入：

```csharp
    private static void VerifySafetyStatusBrush()
    {
        var method = typeof(MainWindow).GetMethod("SafetyStatusBrush", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 SafetyStatusBrush。");
        foreach (var (status, brush) in new (SafetyStatus, SolidColorBrush)[]
                 {
                     (SafetyStatus.Safe, UiTheme.SuccessBrush),
                     (SafetyStatus.Caution, UiTheme.CautionBrush),
                     (SafetyStatus.BrakingRequired, UiTheme.CautionBrush),
                     (SafetyStatus.EnvelopeIntrusion, UiTheme.DangerBrush)
                 })
            Require(ReferenceEquals(method.Invoke(null, [status]), brush), $"安全狀態 {status} 的顏色必須與表格色點一致。");
        Console.WriteLine("[通過] 安全狀態顏色與表格色點一致（需要制動＝注意黃）");
    }

    private static void VerifySafetyChart(MainWindow window)
    {
        ((TabControl)window.FindName("WorkspaceTabControl")!).SelectedItem = window.FindName("SafetyTabItem");
        ((ComboBox)window.FindName("SafetyWindowComboBox")!).SelectedIndex = 2; // 全部時間
        WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);
        ShellLayoutTests.PumpLayout(window);
        WpfTestWait.Invoke(window, "DrawSafetyDistanceChart");
        var canvas = (Canvas)window.FindName("SafetyDistanceCanvas")!;
        var lines = canvas.Children.OfType<Polyline>().ToArray();
        Require(lines.Length == 3, $"距離圖應有三條線，實際 {lines.Length}。");
        Require(ReferenceEquals(lines[0].Stroke, ChartTheme.PrimarySeries) && lines[0].StrokeThickness == 2.4
                && (lines[0].StrokeDashArray?.Count ?? 0) == 0, "實際淨距必須為 PrimarySeries 實線、線寬 2.4。");
        Require(ReferenceEquals(lines[1].Stroke, ChartTheme.ThresholdSeries) && lines[1].StrokeThickness == 1.6
                && ReferenceEquals(lines[1].StrokeDashArray, ChartTheme.LongDash), "動態安全距離必須為 ThresholdSeries 虛線（5,3）、線寬 1.6。");
        Require(ReferenceEquals(lines[2].Stroke, ChartTheme.DangerSeries) && lines[2].StrokeThickness == 1.8
                && ReferenceEquals(lines[2].StrokeDashArray, ChartTheme.ShortDot), "障礙物煞車需求必須為 DangerSeries 點線（2,3）、線寬 1.8。");
        Require(lines.SelectMany(line => line.Points).All(point => point.Y >= 30 - 0.01), "距離圖的線不得高於繪圖區頂端。");
        Require(canvas.Children.OfType<TextBlock>().Any(text => Equals(text.Tag, ChartPainter.ValueTickTag) && text.Text == "0")
                && canvas.Children.OfType<TextBlock>().Any(text => text.Text == "m"), "距離圖必須有從 0 開始的 m 數值刻度。");
        Require(canvas.Children.OfType<TextBlock>().Count(text => Equals(text.Tag, ChartPainter.TimeTickTag)) == 5,
            "距離圖必須有五個時間刻度。");
        var legend = canvas.Children.OfType<StackPanel>().Single(panel => Equals(panel.Tag, ChartPainter.LegendTag));
        Require(legend.Children.OfType<TextBlock>().Select(text => text.Text).SequenceEqual(["實際淨距", "動態安全距離", "障礙物煞車需求"]),
            "距離圖圖例應為實際淨距、動態安全距離、障礙物煞車需求。");
        var chip = canvas.Children.OfType<Border>().Single(border => Equals(border.Tag, "SafetyMarginChip"));
        var chipPanel = (StackPanel)chip.Child;
        var dot = chipPanel.Children.OfType<Ellipse>().Single();
        var label = chipPanel.Children.OfType<TextBlock>().Single();
        var status = label.Text[(label.Text.LastIndexOf('｜') + 1)..];
        Require(label.Text.StartsWith("最低裕度", StringComparison.Ordinal)
                && ReferenceEquals(dot.Fill, StatusTones.Brush(StatusTones.Classify(status)))
                && ReferenceEquals(label.Foreground, dot.Fill),
            $"最低裕度標籤「{label.Text}」的色點與文字必須依狀態「{status}」取 StatusTones 色。");
        Console.WriteLine("[通過] 相鄰列車距離圖主題、刻度與最低裕度色點");
    }
```

- [ ] **Step 2: 執行，確認失敗**

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --chart-theme-only
```

Expected: 以 `找不到 SafetyStatusBrush。` 失敗。

- [ ] **Step 3: 實作**

`MainWindow.V2.cs`：把 `DrawSafetyDistanceChart` 整個方法換成：

```csharp
    private void DrawSafetyDistanceChart()
    {
        SafetyDistanceCanvas.Children.Clear();
        var width = SafetyDistanceCanvas.ActualWidth;
        var height = SafetyDistanceCanvas.ActualHeight;
        if (!double.IsFinite(width) || !double.IsFinite(height)
            || width < 120 || height < 100 || _latestPlaybackFrame is null || _latestPlaybackFrame.SafetyHistory.Count == 0)
        {
            if (width >= 120 && height >= 100)
            {
                ChartPainter.DrawMessage(SafetyDistanceCanvas, "播放多列車 V2 模擬後顯示實際淨距、安全距離與障礙物煞車需求。");
            }

            return;
        }

        var selected = SafetyPairComboBox.SelectedItem?.ToString();
        var windowTag = GetSelectedTag(SafetyWindowComboBox);
        var windowSeconds = double.TryParse(windowTag, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedWindow)
            ? parsedWindow
            : double.PositiveInfinity;
        var earliestTime = double.IsFinite(windowSeconds)
            ? Math.Max(0, _latestPlaybackFrame.CurrentTimeSeconds - windowSeconds)
            : 0;
        var history = _latestPlaybackFrame.SafetyHistory
            .Where(item => (selected is null || PairKey(item) == selected)
                && item.SimulationTimeSeconds >= earliestTime
                && MatchesSafetyFilters(item))
            .ToArray();
        if (history.Length == 0)
        {
            ChartPainter.DrawMessage(SafetyDistanceCanvas, "所選配對在此時間範圍沒有資料；可切換上下行或選擇「全部時間」。");
            return;
        }

        // 上方 30 DIP 給標題列（配對、圖例），下方 34 DIP 給時間刻度。
        var area = new ChartPainter.ChartArea(52, 30, width - 52 - 18, height - 30 - 34);
        var minTime = history[0].SimulationTimeSeconds;
        var maxTime = Math.Max(minTime + 1, history[^1].SimulationTimeSeconds);
        ChartPainter.DrawHeader(SafetyDistanceCanvas, selected is null ? "全部配對" : selected.Split('｜')[0],
        [
            new ChartLegendItem("實際淨距", ChartTheme.PrimarySeries, ChartLegendMarker.Line),
            new ChartLegendItem("動態安全距離", ChartTheme.ThresholdSeries, ChartLegendMarker.Dash),
            new ChartLegendItem("障礙物煞車需求", ChartTheme.DangerSeries, ChartLegendMarker.Dot)
        ], area.Left, 4);
        var maxDistance = ChartPainter.DrawValueAxis(SafetyDistanceCanvas, area, Math.Max(50, history.Max(item => Math.Max(
            Math.Max(item.ActualGapMeters, item.DynamicSafetyDistanceMeters),
            item.ObstacleBrakingDemandMeters)) * 1.12), "m");
        DrawSpeedTimeAxisTicks(SafetyDistanceCanvas, area, minTime, maxTime);
        var gapLine = ChartPainter.CreateSeries(ChartTheme.PrimarySeries, 2.4);
        var safetyLine = ChartPainter.CreateSeries(ChartTheme.ThresholdSeries, 1.6, ChartTheme.LongDash);
        var obstacleLine = ChartPainter.CreateSeries(ChartTheme.DangerSeries, 1.8, ChartTheme.ShortDot);
        foreach (var item in history)
        {
            var x = area.Left + (item.SimulationTimeSeconds - minTime) / (maxTime - minTime) * area.Width;
            gapLine.Points.Add(new Point(x, ToY(item.ActualGapMeters)));
            safetyLine.Points.Add(new Point(x, ToY(item.DynamicSafetyDistanceMeters)));
            obstacleLine.Points.Add(new Point(x, ToY(item.ObstacleBrakingDemandMeters)));
        }

        SafetyDistanceCanvas.Children.Add(gapLine);
        SafetyDistanceCanvas.Children.Add(safetyLine);
        SafetyDistanceCanvas.Children.Add(obstacleLine);
        AddSafetyMarginChip(SafetyDistanceCanvas, history.MinBy(item => item.SafetyMarginMeters)!, area);

        double ToY(double value) => area.Bottom - Math.Clamp(value / maxDistance, 0, 1) * area.Height;
    }

    // 最低安全裕度色點標籤；顏色與閉塞表狀態色點相同（StatusTones）。
    private static void AddSafetyMarginChip(Canvas canvas, SafetyObservation minimum, ChartPainter.ChartArea area)
    {
        var brush = SafetyStatusBrush(minimum.Status);
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = brush,
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        content.Children.Add(ChartPainter.CreateLabel(
            $"最低裕度 {minimum.SafetyMarginMeters:0.0} m @ {minimum.SimulationTimeSeconds:0.0} s｜{SafetyStatusToChinese(minimum.Status)}",
            brush, ChartTheme.LegendFontSize));
        ChartPainter.Place(canvas, new Border
        {
            Child = content,
            Background = UiTheme.SurfaceBrush,
            BorderBrush = ChartTheme.Grid,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(7, 2, 8, 2),
            Tag = "SafetyMarginChip"
        }, area.Left + 8, area.Top + 6);
    }
```

把 `SafetyStatusColor` 方法整段：

```csharp
    private static Color SafetyStatusColor(SafetyStatus status) => status switch
    {
        SafetyStatus.Safe => Color.FromRgb(22, 134, 107),
        SafetyStatus.Caution => Color.FromRgb(218, 166, 35),
        SafetyStatus.BrakingRequired => Color.FromRgb(232, 109, 45),
        _ => Color.FromRgb(196, 48, 48)
    };
```

換成

```csharp
    // 安全狀態在距離圖、路線圖與閉塞表同色：依中文狀態文字查 StatusTones。
    private static SolidColorBrush SafetyStatusBrush(SafetyStatus status) =>
        StatusTones.Brush(StatusTones.Classify(SafetyStatusToChinese(status)));
```

刪除 `CreateChartLine` 方法整段：

```csharp
    private static Polyline CreateChartLine(Color color, double thickness, DoubleCollection? dash = null) => new()
    {
        Stroke = new SolidColorBrush(color),
        StrokeThickness = thickness,
        StrokeDashArray = dash
    };

```

`DrawV2Route` 的列車間安全線：把

```csharp
            var color = SafetyStatusColor(observation.Status);
            RouteCanvas.Children.Add(new Line
            {
                X1 = x1,
                X2 = x2,
                Y1 = y,
                Y2 = y,
                Stroke = new SolidColorBrush(color),
```

換成

```csharp
            RouteCanvas.Children.Add(new Line
            {
                X1 = x1,
                X2 = x2,
                Y1 = y,
                Y2 = y,
                Stroke = SafetyStatusBrush(observation.Status),
```

- [ ] **Step 4: 執行，確認通過**

```powershell
dotnet build .\MrtRouteSimulator.slnx -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --chart-theme-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --speed-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --result-pages-only
```

Expected: 0／0；`[通過] 安全狀態顏色與表格色點一致（需要制動＝注意黃）`、`[通過] 相鄰列車距離圖主題、刻度與最低裕度色點`、無寫死顏色（4 個來源檔）、`PASS WPF chart theme`；`PASS WPF speed journey`；`PASS WPF result pages`。

- [ ] **Step 5: 完整 WPF runner**

```powershell
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- .
```

Expected: `PASS WPF visual rules`。

- [ ] **Step 6: Commit**

```powershell
git add src/MrtRouteSimulator.App/MainWindow.V2.cs tests/MrtRouteSimulator.WpfTests/ChartThemeTests.cs
git commit -m "feat: theme safety distance chart and unify safety status colors" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: 時間－里程運行圖（畫面與匯出兩條路徑）

**Files:**
- Modify: `src/MrtRouteSimulator.App/MainWindow.TimeDistance.cs`
- Modify: `src/MrtRouteSimulator.App/MainWindow.V2.cs`（`DrawTimeDistanceDiagramFull`；移除 `DrawAxes`）
- Modify: `src/MrtRouteSimulator.App/TimeDistanceStationLabelLayout.cs`
- Modify: `tests/MrtRouteSimulator.WpfTests/TimeDistanceVisualTests.cs`（`HasEventLegend`）
- Test: `tests/MrtRouteSimulator.WpfTests/ChartThemeTests.cs`

**Interfaces:**
- Consumes: Task 2 的 `ChartPainter.DrawAxes`、`DrawTitle`、`DrawLegend`、`DrawMessage`、`CreateLabel`、`Place`。
- Produces: `private static IReadOnlyList<ChartLegendItem> GetDiagramLegendItems(bool showEvents)`（取代 `GetDiagramLegend(bool)`）；運行圖畫布上有一個 `Tag="ChartLegend"` 的 `StackPanel`（Task 6 的 PDF 依此補畫）。

- [ ] **Step 1: 寫失敗測試**

`VerifyV2Charts` 在 `VerifySafetyChart(window);` 之後加入：

```csharp
            VerifyTimeDistanceCharts(window);
```

`ChartSources` 改為：

```csharp
    private static readonly (string File, string[] Methods)[] ChartSources =
    [
        ("ChartTheme.cs", []),
        ("ChartPainter.cs", []),
        ("MainWindow.V2.cs", ["DrawV2SpeedProfile", "DrawSpeedLimitLabels", "DrawSpeedStopLabels", "DrawSpeedTimeAxisTicks",
            "DrawSafetyDistanceChart", "AddSafetyMarginChip", "DrawTimeDistanceDiagramFull"]),
        ("MainWindow.xaml.cs", ["DrawSpeedProfile"]),
        ("MainWindow.TimeDistance.cs", []),
        ("TimeDistanceStationLabelLayout.cs", [])
    ];
```

加入：

```csharp
    private static void VerifyTimeDistanceCharts(MainWindow window)
    {
        ((TabControl)window.FindName("WorkspaceTabControl")!).SelectedItem = window.FindName("DiagramTabItem");
        ShellLayoutTests.PumpLayout(window);
        var canvas = (Canvas)window.FindName("TimeDistanceCanvas")!;
        var showEvents = (CheckBox)window.FindName("ShowEventsCheckBox")!;
        try
        {
            foreach (var renderer in new[] { "DrawInteractiveTimeDistanceDiagram", "DrawTimeDistanceDiagramFull" })
            {
                foreach (var events in new[] { true, false })
                {
                    showEvents.IsChecked = events;
                    WpfTestWait.Invoke(window, renderer);
                    var context = $"{renderer}（事件點{(events ? "開" : "關")}）";
                    var title = canvas.Children.OfType<TextBlock>().Single(text => Equals(text.Tag, ChartPainter.TitleTag));
                    Require(title.Text.Contains("計畫／理論與 V2 模擬實際運行圖", StringComparison.Ordinal)
                            && ReferenceEquals(title.Foreground, ChartTheme.Title) && title.FontSize == 13 && Canvas.GetTop(title) == 8,
                        $"{context} 標題必須沿用原文字、圖表標題樣式、位於第 8 DIP。");
                    var legend = canvas.Children.OfType<StackPanel>().Single(panel => Equals(panel.Tag, ChartPainter.LegendTag));
                    string[] expected = events ? ["V2 實際", "計畫／理論", "站點事件", "端點事件", "安全事件"] : ["V2 實際", "計畫／理論"];
                    var legendTexts = legend.Children.OfType<TextBlock>().Select(text => text.Text).ToArray();
                    Require(legendTexts.Take(expected.Length).SequenceEqual(expected)
                            && legendTexts.Skip(expected.Length).All(text => text.StartsWith('（')),
                        $"{context} 圖例應為 {string.Join("／", expected)}，實際 {string.Join("／", legendTexts)}。");
                    var markers = canvas.Children.OfType<Ellipse>().Where(marker => marker.Visibility == Visibility.Visible).ToArray();
                    Require(events ? markers.Length > 0 : markers.Length == 0, $"{context} 事件點顯示必須跟隨勾選。");
                    Require(markers.All(marker => ReferenceEquals(marker.Stroke, ChartTheme.MarkerOutline)
                            && (ReferenceEquals(marker.Fill, ChartTheme.EventStation) || ReferenceEquals(marker.Fill, ChartTheme.EventTerminal)
                                || ReferenceEquals(marker.Fill, ChartTheme.EventSafety))),
                        $"{context} 事件點必須用 ChartTheme 事件色與白色外框。");
                    var stationLabels = canvas.Children.OfType<TextBlock>().Where(text => text.Tag is TimeDistanceStationLabelTag).ToArray();
                    Require(stationLabels.Length > 0 && stationLabels.All(text => ReferenceEquals(text.FontFamily, ChartTheme.Font)
                            && ((SolidColorBrush)text.Foreground).Color == ChartTheme.AxisLabel.Color),
                        $"{context} 車站標籤必須用 App 字型與 AxisLabel 色。");
                    var gridLines = canvas.Children.OfType<Line>().Where(line => Math.Abs(line.Y1 - line.Y2) < .01 && line.X2 > line.X1 + 100
                        && !ReferenceEquals(line.Stroke, ChartTheme.Axis)).ToArray();
                    Require(gridLines.Length > 0 && gridLines.All(line => ReferenceEquals(line.Stroke, ChartTheme.Grid)),
                        $"{context} 車站格線必須用 Grid 畫筆。");
                    var series = canvas.Children.OfType<Polyline>().Where(line => line.Points.Count > 0).ToArray();
                    Require(series.Length > 0 && series.All(line => UiTheme.VehicleBrushes.Any(brush => ReferenceEquals(brush, line.Stroke))),
                        $"{context} 列車線必須使用車輛色盤的共用畫筆。");
                }
            }

            typeof(MainWindow).GetField("_diagramLayoutKey", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, null);
            WpfTestWait.Invoke(window, "DrawInteractiveTimeDistanceDiagram");
            var axis = (Canvas)window.FindName("DiagramTimeAxisCanvas")!;
            Require(axis.Children.OfType<Line>().Any() && axis.Children.OfType<Line>().All(line => ReferenceEquals(line.Stroke, ChartTheme.Axis)),
                "固定時間軸的軸線與刻度短線必須用 Axis 畫筆。");
            Require(axis.Children.OfType<TextBlock>().All(text => ReferenceEquals(text.Foreground, ChartTheme.AxisLabel)
                    && ReferenceEquals(text.FontFamily, ChartTheme.Font)),
                "固定時間軸文字必須用 AxisLabel 色與 App 字型。");
        }
        finally { showEvents.IsChecked = true; }
        Console.WriteLine("[通過] 運行圖兩條繪製路徑的標題、圖例、事件點、格線與標籤主題一致");
    }
```

`TimeDistanceVisualTests.cs`：把

```csharp
    private static bool HasEventLegend(Canvas canvas) =>
        canvas.Children
            .OfType<TextBlock>()
            .Any(text => text.Text.Contains("綠點", StringComparison.Ordinal)
                || text.Text.Contains("紫點", StringComparison.Ordinal)
                || text.Text.Contains("紅點", StringComparison.Ordinal)
                || text.Text.Contains("事件標記", StringComparison.Ordinal));
```

換成

```csharp
    private static bool HasEventLegend(Canvas canvas) =>
        canvas.Children
            .OfType<StackPanel>()
            .Where(panel => Equals(panel.Tag, ChartPainter.LegendTag))
            .SelectMany(panel => panel.Children.OfType<TextBlock>())
            .Any(text => text.Text is "站點事件" or "端點事件" or "安全事件");
```

- [ ] **Step 2: 執行，確認失敗**

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --chart-theme-only
```

Expected: 建置成功；執行以 `InvalidOperationException` 失敗（`Single` 找不到 `Tag=ChartTitle` 的標題；舊運行圖標題是沒有 Tag 的文字）。

- [ ] **Step 3: 實作互動運行圖（`MainWindow.TimeDistance.cs`）**

刪除三個靜態畫筆：

```csharp
    private static readonly Brush DiagramSafetyBrush = new SolidColorBrush(Color.FromRgb(196, 48, 48));
    private static readonly Brush DiagramTerminalBrush = new SolidColorBrush(Color.FromRgb(126, 87, 194));
    private static readonly Brush DiagramStationBrush = new SolidColorBrush(Color.FromRgb(22, 134, 107));
```

`AddDiagramTimeTickLabel`：把

```csharp
            TextAlignment = TextAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(82, 93, 111))
```

換成

```csharp
            TextAlignment = TextAlignment.Center,
            FontFamily = ChartTheme.Font,
            Foreground = ChartTheme.AxisLabel
```

`DrawFixedDiagramTimeAxis`：把

```csharp
        AddCanvasText(DiagramTimeAxisCanvas, "時間", 8, 8, 10, Color.FromRgb(82, 93, 111));
        DiagramTimeAxisCanvas.Children.Add(new Line
        {
            X1 = left, X2 = left + plotWidth, Y1 = 0, Y2 = 0,
            Stroke = Brushes.Gainsboro
        });
```

換成

```csharp
        ChartPainter.Place(DiagramTimeAxisCanvas, ChartPainter.CreateLabel("時間", ChartTheme.AxisLabel), 8, 8);
        DiagramTimeAxisCanvas.Children.Add(new Line
        {
            X1 = left, X2 = left + plotWidth, Y1 = 0, Y2 = 0,
            Stroke = ChartTheme.Axis, StrokeThickness = ChartTheme.AxisThickness
        });
```

再把

```csharp
                X1 = x, X2 = x, Y1 = 0, Y2 = 5, Stroke = Brushes.Gray
```

換成

```csharp
                X1 = x, X2 = x, Y1 = 0, Y2 = 5, Stroke = ChartTheme.Axis
```

`DrawInteractiveTimeDistanceDiagramCore`：把

```csharp
                AddCanvasText(TimeDistanceCanvas, "建立並播放 V2 模擬後顯示時間－里程運行圖。", 22, 22, 13, Color.FromRgb(102, 112, 133));
```

換成

```csharp
                ChartPainter.DrawMessage(TimeDistanceCanvas, "建立並播放 V2 模擬後顯示時間－里程運行圖。");
```

把

```csharp
                AddCanvasText(TimeDistanceCanvas, message, 22, 22, 13, Color.FromRgb(102, 112, 133));
```

換成

```csharp
                ChartPainter.DrawMessage(TimeDistanceCanvas, message);
```

把靜態圖層開頭（從 `DrawAxes(TimeDistanceCanvas, left, top, pw, ph, ...` 到 `labelSpecs` 的 `.ToArray();`）：

```csharp
                DrawAxes(TimeDistanceCanvas, left, top, pw, ph, tails.Count == 0 ? "累積里程" : "累積里程（含尾軌）", "");
                DrawFixedDiagramTimeAxis(width, left, pw, start, duration);
                AddCanvasText(TimeDistanceCanvas, $"{_activeTopologyProjectDocument?.ProjectName ?? _route!.RouteName}｜計畫／理論與 V2 模擬實際運行圖｜{UiDisplayText.Enum(frame.MovingBlockMode)}｜固定時間步進 0.1 秒", left, 8, 14, Color.FromRgb(34, 43, 60));
                var cacheStatus = plannedCachePending
                    ? "　（計畫／理論線背景整理中）"
                    : plannedCacheFailed
                        ? "　（計畫／理論線整理失敗）"
                        : string.Empty;
                AddCanvasText(TimeDistanceCanvas, GetDiagramLegend(showEvents) + cacheStatus, left, 28, 10, Color.FromRgb(82, 93, 111));
                var labelSpecs = stations
                    .Select(s => new TimeDistanceStationLabelSpec(
                        s.Id,
                        $"{s.Id}  {s.Position / 1000:0.00} km",
                        Y(s.Position),
                        Color.FromRgb(82, 93, 111)))
                    .Concat(tails.Select(t => new TimeDistanceStationLabelSpec(
                        t.Layout.VirtualNodeId,
                        $"{t.Layout.VirtualNodeId}  {t.Layout.VirtualNodePositionMeters / 1000:0.00} km",
                        Y(t.Layout.VirtualNodePositionMeters),
                        Color.FromRgb(188, 92, 52),
                        IsTail: true)))
                    .ToArray();
```

換成

```csharp
                ChartPainter.DrawAxes(TimeDistanceCanvas, new ChartPainter.ChartArea(left, top, pw, ph),
                    tails.Count == 0 ? "累積里程" : "累積里程（含尾軌）", string.Empty);
                DrawFixedDiagramTimeAxis(width, left, pw, start, duration);
                ChartPainter.DrawTitle(TimeDistanceCanvas, $"{_activeTopologyProjectDocument?.ProjectName ?? _route!.RouteName}｜計畫／理論與 V2 模擬實際運行圖｜{UiDisplayText.Enum(frame.MovingBlockMode)}｜固定時間步進 0.1 秒", left, 8);
                var legend = ChartPainter.DrawLegend(TimeDistanceCanvas, left, 28, GetDiagramLegendItems(showEvents));
                if (plannedCachePending || plannedCacheFailed)
                {
                    legend.Children.Add(ChartPainter.CreateLabel(
                        plannedCachePending ? "（計畫／理論線背景整理中）" : "（計畫／理論線整理失敗）",
                        ChartTheme.LegendText, ChartTheme.LegendFontSize));
                }
                var labelSpecs = stations
                    .Select(s => new TimeDistanceStationLabelSpec(
                        s.Id,
                        $"{s.Id}  {s.Position / 1000:0.00} km",
                        Y(s.Position),
                        ChartTheme.AxisLabel.Color))
                    .Concat(tails.Select(t => new TimeDistanceStationLabelSpec(
                        t.Layout.VirtualNodeId,
                        $"{t.Layout.VirtualNodeId}  {t.Layout.VirtualNodePositionMeters / 1000:0.00} km",
                        Y(t.Layout.VirtualNodePositionMeters),
                        ChartTheme.TailTrack.Color,
                        IsTail: true)))
                    .ToArray();
```

把

```csharp
                        Stroke = placement.IsTail ? Brushes.Sienna : Brushes.Gainsboro,
                        StrokeDashArray = placement.IsTail ? [4, 3] : null
```

換成

```csharp
                        Stroke = placement.IsTail ? ChartTheme.TailTrack : ChartTheme.Grid,
                        StrokeThickness = ChartTheme.GridThickness,
                        StrokeDashArray = placement.IsTail ? ChartTheme.TailDash : null
```

把

```csharp
                    TimeDistanceCanvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = top, Y2 = top + ph, Stroke = Brushes.Gainsboro });
```

換成

```csharp
                    TimeDistanceCanvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = top, Y2 = top + ph, Stroke = ChartTheme.Grid, StrokeThickness = ChartTheme.GridThickness });
```

把

```csharp
                    var marker = new Ellipse { Width = 8, Height = 8, Stroke = Brushes.White, StrokeThickness = 1 };
```

換成

```csharp
                    var marker = new Ellipse { Width = 8, Height = 8, Stroke = ChartTheme.MarkerOutline, StrokeThickness = 1 };
```

把

```csharp
                m.Fill = IsDiagramSafetyEvent(ev.EventType) ? DiagramSafetyBrush : IsDiagramTerminalEvent(ev.EventType) ? DiagramTerminalBrush : DiagramStationBrush;
```

換成

```csharp
                m.Fill = IsDiagramSafetyEvent(ev.EventType) ? ChartTheme.EventSafety : IsDiagramTerminalEvent(ev.EventType) ? ChartTheme.EventTerminal : ChartTheme.EventStation;
```

把

```csharp
                    var color = TrainColors[ParseVehicleIndex(s.VehicleId) % TrainColors.Length];
                    var line = new Polyline { Stroke = new SolidColorBrush(color), StrokeThickness = vehicle != "全部" ? 3.1 : planned ? 1.4 : 2.2, StrokeDashArray = planned ? [6, 4] : null, Opacity = planned ? .55 : .95, ToolTip = $"{s.VehicleId}｜{s.ServiceRunId}｜{DirectionToChinese(s.Direction)}" };
                    var label = new TextBlock { Text = ShortVehicle(s.VehicleId), FontSize = 9, Foreground = new SolidColorBrush(color) };
```

換成

```csharp
                    var brush = UiTheme.VehicleBrush(ParseVehicleIndex(s.VehicleId));
                    var line = new Polyline { Stroke = brush, StrokeThickness = vehicle != "全部" ? 3.1 : planned ? 1.4 : 2.2, StrokeDashArray = planned ? ChartTheme.PlannedDash : null, Opacity = planned ? .55 : .95, ToolTip = $"{s.VehicleId}｜{s.ServiceRunId}｜{DirectionToChinese(s.Direction)}" };
                    var label = ChartPainter.CreateLabel(ShortVehicle(s.VehicleId), brush, 9);
```

把 `GetDiagramLegend` 方法：

```csharp
    private static string GetDiagramLegend(bool showEvents) =>
        "實線：V2 模擬實際　虛線：無干擾計畫／理論" + (showEvents
            ? "　綠點：車站　紫點：折返／尾軌／退出　紅點：安全／障礙"
            : "　（事件觸發點已隱藏）");
```

換成

```csharp
    // 運行圖圖例：兩種線型；開啟事件點時再加三種事件色點。
    private static IReadOnlyList<ChartLegendItem> GetDiagramLegendItems(bool showEvents)
    {
        List<ChartLegendItem> items =
        [
            new("V2 實際", ChartTheme.NeutralSeries, ChartLegendMarker.Line),
            new("計畫／理論", ChartTheme.NeutralSeries, ChartLegendMarker.Dash)
        ];
        if (showEvents)
        {
            items.Add(new ChartLegendItem("站點事件", ChartTheme.EventStation, ChartLegendMarker.Point));
            items.Add(new ChartLegendItem("端點事件", ChartTheme.EventTerminal, ChartLegendMarker.Point));
            items.Add(new ChartLegendItem("安全事件", ChartTheme.EventSafety, ChartLegendMarker.Point));
        }

        return items;
    }
```

- [ ] **Step 4: 實作完整運行圖（`MainWindow.V2.cs` 的 `DrawTimeDistanceDiagramFull`）**

把

```csharp
            AddCanvasText(TimeDistanceCanvas, "建立並播放 V2 模擬後顯示時間－里程運行圖。", 22, 22, 13, Color.FromRgb(102, 112, 133));
```

換成

```csharp
            ChartPainter.DrawMessage(TimeDistanceCanvas, "建立並播放 V2 模擬後顯示時間－里程運行圖。");
```

把

```csharp
            AddCanvasText(TimeDistanceCanvas, "播放後即時建立運行圖；空圖不會啟動零列車模擬引擎。", 22, 22, 13, Color.FromRgb(102, 112, 133));
```

換成

```csharp
            ChartPainter.DrawMessage(TimeDistanceCanvas, "播放後即時建立運行圖；空圖不會啟動零列車模擬引擎。");
```

把

```csharp
        DrawAxes(
            TimeDistanceCanvas,
            left,
            top,
            plotWidth,
            plotHeight,
            tailTrackLayouts.Count == 0 ? "累積里程" : "累積里程（含尾軌）",
            "時間");
        AddCanvasText(
            TimeDistanceCanvas,
            $"{displayRouteName}｜計畫／理論與 V2 模擬實際運行圖｜{UiDisplayText.Enum(_latestPlaybackFrame.MovingBlockMode)}｜"
                + $"{(_activeTopologyProjectDocument is null ? $"速限 {_latestPlaybackFrame.SpeedLimits.Limits.Count} 段" : "拓撲軌道區段速限")}｜固定時間步進 0.1 秒",
            left,
            8,
            14,
            Color.FromRgb(34, 43, 60));
```

換成

```csharp
        ChartPainter.DrawAxes(
            TimeDistanceCanvas,
            new ChartPainter.ChartArea(left, top, plotWidth, plotHeight),
            tailTrackLayouts.Count == 0 ? "累積里程" : "累積里程（含尾軌）",
            "時間");
        ChartPainter.DrawTitle(
            TimeDistanceCanvas,
            $"{displayRouteName}｜計畫／理論與 V2 模擬實際運行圖｜{UiDisplayText.Enum(_latestPlaybackFrame.MovingBlockMode)}｜"
                + $"{(_activeTopologyProjectDocument is null ? $"速限 {_latestPlaybackFrame.SpeedLimits.Limits.Count} 段" : "拓撲軌道區段速限")}｜固定時間步進 0.1 秒",
            left,
            8);
        ChartPainter.DrawLegend(TimeDistanceCanvas, left, 28, GetDiagramLegendItems(ShowEventsCheckBox?.IsChecked != false));
```

車站格線：把

```csharp
                Stroke = new SolidColorBrush(Color.FromRgb(222, 227, 235)),
                StrokeThickness = 1
```

換成

```csharp
                Stroke = ChartTheme.Grid,
                StrokeThickness = ChartTheme.GridThickness
```

尾軌格線：把

```csharp
                Stroke = new SolidColorBrush(Color.FromRgb(188, 92, 52)),
                StrokeThickness = 1,
                StrokeDashArray = [4, 3]
```

換成

```csharp
                Stroke = ChartTheme.TailTrack,
                StrokeThickness = ChartTheme.GridThickness,
                StrokeDashArray = ChartTheme.TailDash
```

標籤顏色：把

```csharp
                ToDiagramY(station.PositionMeters),
                Color.FromRgb(82, 93, 111)))
```

換成

```csharp
                ToDiagramY(station.PositionMeters),
                ChartTheme.AxisLabel.Color))
```

把

```csharp
                ToDiagramY(tail.Layout.VirtualNodePositionMeters),
                Color.FromRgb(188, 92, 52),
```

換成

```csharp
                ToDiagramY(tail.Layout.VirtualNodePositionMeters),
                ChartTheme.TailTrack.Color,
```

時間格線：把

```csharp
                Stroke = new SolidColorBrush(Color.FromRgb(232, 235, 241)),
                StrokeThickness = 1
```

換成

```csharp
                Stroke = ChartTheme.Grid,
                StrokeThickness = ChartTheme.GridThickness
```

事件點：把

```csharp
            var markerColor = isSafetyEvent
                ? Color.FromRgb(196, 48, 48)
                : isTerminalEvent
                    ? Color.FromRgb(126, 87, 194)
                    : Color.FromRgb(22, 134, 107);
            var marker = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = new SolidColorBrush(markerColor),
                Stroke = Brushes.White,
```

換成

```csharp
            var marker = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = isSafetyEvent ? ChartTheme.EventSafety : isTerminalEvent ? ChartTheme.EventTerminal : ChartTheme.EventStation,
                Stroke = ChartTheme.MarkerOutline,
```

刪除事件之後的舊圖例文字（保留前一行的 `RecordTiming`）：

```csharp
        AddCanvasText(
            TimeDistanceCanvas,
            GetDiagramLegend(ShowEventsCheckBox?.IsChecked != false),
            left,
            28,
            10,
            Color.FromRgb(82, 93, 111));
```

列車線：把

```csharp
                var index = ParseVehicleIndex(group.Key.VehicleId);
                var line = new Polyline
                {
                    Stroke = new SolidColorBrush(TrainColors[index % TrainColors.Length]),
                    StrokeThickness = selectedVehicle ? 3.1 : isPlanned ? 1.4 : 2.2,
                    StrokeDashArray = isPlanned ? [6, 4] : null,
```

換成

```csharp
                var brush = UiTheme.VehicleBrush(ParseVehicleIndex(group.Key.VehicleId));
                var line = new Polyline
                {
                    Stroke = brush,
                    StrokeThickness = selectedVehicle ? 3.1 : isPlanned ? 1.4 : 2.2,
                    StrokeDashArray = isPlanned ? ChartTheme.PlannedDash : null,
```

把

```csharp
                    var first = points[0];
                    AddCanvasText(
                        TimeDistanceCanvas,
                        ShortVehicle(first.VehicleId),
                        left + (first.SimulationTimeSeconds - startTime) / visibleDuration * plotWidth + 3,
                        ToDiagramY(first.PositionMeters) - 15,
                        9,
                        TrainColors[index % TrainColors.Length]);
```

換成（車次標籤必須緊接在列車線之後加入，PDF 依此配對）

```csharp
                    var first = points[0];
                    ChartPainter.Place(
                        TimeDistanceCanvas,
                        ChartPainter.CreateLabel(ShortVehicle(first.VehicleId), brush, 9),
                        left + (first.SimulationTimeSeconds - startTime) / visibleDuration * plotWidth + 3,
                        ToDiagramY(first.PositionMeters) - 15);
```

刪除 `MainWindow.V2.cs` 末端已無人使用的 `DrawAxes` 方法整段（`private static void DrawAxes(` 到它的結尾大括號，包含兩行 `AddCanvasText(canvas, verticalLabel, ...)`、`AddCanvasText(canvas, horizontalLabel, ...)`）。

- [ ] **Step 5: 車站標籤字型（`TimeDistanceStationLabelLayout.cs`）**

`CreateTextBlock` 與 `Measure` 兩處的

```csharp
            FontFamily = SystemFonts.MessageFontFamily,
```

都換成

```csharp
            FontFamily = ChartTheme.Font,
```

`CreateLeaderLine`：把

```csharp
        var lineColor = Color.FromArgb(150, placement.Color.R, placement.Color.G, placement.Color.B);
```

換成

```csharp
        var lineColor = placement.Color;
        lineColor.A = 150;
```

- [ ] **Step 6: 執行，確認通過**

```powershell
dotnet build .\MrtRouteSimulator.slnx -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --chart-theme-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --pacing-lazy-only
```

Expected: 0／0（若出現 `TrainColors` 未使用的警告，代表 V1 路線圖的使用被誤刪，還原它）；`[通過] 運行圖兩條繪製路徑的標題、圖例、事件點、格線與標籤主題一致`、無寫死顏色（6 個來源檔）、`PASS WPF chart theme`；`PASS WPF pacing/lazy/incremental`。

- [ ] **Step 7: 完整 WPF runner**

```powershell
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- .
```

Expected: `PASS WPF visual rules`（含 `TimeDistanceStationLabelTests`、`TimeDistanceResizeTests`、`TimeDistanceStickyAxisTests`、`DiagramCompactLayoutTests`）。

- [ ] **Step 8: Commit**

```powershell
git add src/MrtRouteSimulator.App/MainWindow.TimeDistance.cs src/MrtRouteSimulator.App/MainWindow.V2.cs src/MrtRouteSimulator.App/TimeDistanceStationLabelLayout.cs tests/MrtRouteSimulator.WpfTests/TimeDistanceVisualTests.cs tests/MrtRouteSimulator.WpfTests/ChartThemeTests.cs
git commit -m "feat: theme time-distance diagram titles, legend, grid and events" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: PNG／PDF 匯出

**Files:**
- Modify: `src/MrtRouteSimulator.App/DiagramExportService.cs`
- Test: `tests/MrtRouteSimulator.WpfTests/ChartThemeTests.cs`

**Interfaces:**
- Consumes: Task 5 的 `Tag="ChartLegend"` 圖例元件；`ChartPainter.LegendTag`；`ChartTheme.ExportPage`、`Axis`、`AxisThickness`、`NeutralSeries`。
- Produces: `DiagramExportService.DrawTextBlock(DrawingContext, FrameworkElement, double, double, double, double)`（參數型別由 `TextBlock` 放寬）、`FindChartLegends(DependencyObject)`；`ComposePdfPage` 新增 `IReadOnlyList<FrameworkElement> legends` 參數（只在本檔使用）。

- [ ] **Step 1: 寫失敗測試**

`VerifyV2Charts` 在 `VerifyTimeDistanceCharts(window);` 之後加入：

```csharp
            VerifyPdfPagesKeepLegend(window);
```

`ChartSources` 改為：

```csharp
    private static readonly (string File, string[] Methods)[] ChartSources =
    [
        ("ChartTheme.cs", []),
        ("ChartPainter.cs", []),
        ("MainWindow.V2.cs", ["DrawV2SpeedProfile", "DrawSpeedLimitLabels", "DrawSpeedStopLabels", "DrawSpeedTimeAxisTicks",
            "DrawSafetyDistanceChart", "AddSafetyMarginChip", "DrawTimeDistanceDiagramFull"]),
        ("MainWindow.xaml.cs", ["DrawSpeedProfile"]),
        ("MainWindow.TimeDistance.cs", []),
        ("TimeDistanceStationLabelLayout.cs", []),
        ("DiagramExportService.cs", [])
    ];
```

加入：

```csharp
    private static void VerifyPdfPagesKeepLegend(MainWindow window)
    {
        ((TabControl)window.FindName("WorkspaceTabControl")!).SelectedItem = window.FindName("DiagramTabItem");
        var zoom = (Slider)window.FindName("DiagramZoomSlider")!;
        var previousZoom = zoom.Value;
        try
        {
            zoom.Value = 3;
            ShellLayoutTests.PumpLayout(window);
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagramFull");
            var canvas = (Canvas)window.FindName("TimeDistanceCanvas")!;
            canvas.UpdateLayout();
            var legend = canvas.Children.OfType<StackPanel>().Single(panel => Equals(panel.Tag, ChartPainter.LegendTag));
            var exportType = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.DiagramExportService")!;
            var render = exportType.GetMethod("Render", BindingFlags.Static | BindingFlags.NonPublic, binder: null,
                types: [typeof(FrameworkElement), typeof(double), typeof(bool)], modifiers: null)!;
            var createPages = exportType.GetMethod("CreatePdfPages", BindingFlags.Static | BindingFlags.NonPublic)!;
            var bitmap = (RenderTargetBitmap)render.Invoke(null, [canvas, 1.6d, true])!;
            var pages = (IReadOnlyList<BitmapSource>)createPages.Invoke(null, [bitmap, 794, 547, true, canvas, 1.6d])!;
            Require(pages.Count >= 2, $"放大 3 倍的運行圖應分成多頁，實際 {pages.Count} 頁。");
            var legendBounds = new Rect(Canvas.GetLeft(legend), Canvas.GetTop(legend), legend.ActualWidth, legend.ActualHeight);
            for (var index = 0; index < pages.Count; index++)
            {
                var pixels = CountPixels(pages[index], legendBounds, 1.6, ChartTheme.NeutralSeries.Color);
                Require(pixels > 10, $"PDF 第 {index + 1} 頁缺少圖例線段（圖例範圍內只有 {pixels} 個圖例色像素）。");
            }
            Console.WriteLine($"[通過] PDF 分頁每頁保留圖例（{pages.Count} 頁）");
        }
        finally
        {
            zoom.Value = previousZoom;
            typeof(MainWindow).GetField("_diagramLayoutKey", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, null);
        }
    }

    // 在頁面點陣圖的指定邏輯範圍內，計算與 color 相差不超過 4 的像素數。
    private static int CountPixels(BitmapSource page, Rect logical, double scale, Color color)
    {
        var bgra = new FormatConvertedBitmap(page, PixelFormats.Bgra32, null, 0);
        var x0 = Math.Max(0, (int)Math.Floor(logical.Left * scale));
        var y0 = Math.Max(0, (int)Math.Floor(logical.Top * scale));
        var x1 = Math.Min(bgra.PixelWidth, (int)Math.Ceiling(logical.Right * scale));
        var y1 = Math.Min(bgra.PixelHeight, (int)Math.Ceiling(logical.Bottom * scale));
        if (x1 <= x0 || y1 <= y0) return 0;
        var stride = (x1 - x0) * 4;
        var pixels = new byte[stride * (y1 - y0)];
        bgra.CopyPixels(new Int32Rect(x0, y0, x1 - x0, y1 - y0), pixels, stride, 0);
        var count = 0;
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            if (Math.Abs(pixels[offset] - color.B) <= 4 && Math.Abs(pixels[offset + 1] - color.G) <= 4
                && Math.Abs(pixels[offset + 2] - color.R) <= 4)
                count++;
        }
        return count;
    }
```

- [ ] **Step 2: 執行，確認失敗**

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --chart-theme-only
```

Expected: 以 `PDF 第 1 頁缺少圖例線段（圖例範圍內只有 0 個圖例色像素）。` 失敗（Task 5 之後圖例是元件，分頁裁切後遺失）。

- [ ] **Step 3: 實作**

`DiagramExportService.cs`：

1. `CreatePdfPages`：把

```csharp
        var stationLeaders = FindStationLeaders(element);
```

換成

```csharp
        var stationLeaders = FindStationLeaders(element);
        var legends = FindChartLegends(element);
```

並把呼叫 `ComposePdfPage(` 的引數

```csharp
                graphBitmap,
                textBlocks,
                stationLeaders,
                trainAnchors.Where(pair => Array.FindIndex(sliceStarts, sliceStart =>
```

換成

```csharp
                graphBitmap,
                textBlocks,
                stationLeaders,
                legends,
                trainAnchors.Where(pair => Array.FindIndex(sliceStarts, sliceStart =>
```

2. `ComposePdfSinglePage`：把

```csharp
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, logicalWidth, logicalHeight));
            context.DrawImage(graphBitmap, new Rect(0, 0, logicalWidth, originalLogicalHeight));
```

換成

```csharp
            context.DrawRectangle(ChartTheme.ExportPage, null, new Rect(0, 0, logicalWidth, logicalHeight));
            context.DrawImage(graphBitmap, new Rect(0, 0, logicalWidth, originalLogicalHeight));
```

3. `ComposePdfPage` 簽章：把

```csharp
        IReadOnlyList<TextBlock> textBlocks,
        IReadOnlyList<Line> stationLeaders,
        IReadOnlyDictionary<TextBlock, Point> pageTrainAnchors,
```

換成

```csharp
        IReadOnlyList<TextBlock> textBlocks,
        IReadOnlyList<Line> stationLeaders,
        IReadOnlyList<FrameworkElement> legends,
        IReadOnlyDictionary<TextBlock, Point> pageTrainAnchors,
```

把

```csharp
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, logicalWidth, logicalHeight));
            var source = new CroppedBitmap(
```

換成

```csharp
            context.DrawRectangle(ChartTheme.ExportPage, null, new Rect(0, 0, logicalWidth, logicalHeight));
            var source = new CroppedBitmap(
```

把

```csharp
            var pen = new Pen(Brushes.SlateGray, 1.1);
```

換成

```csharp
            var pen = new Pen(ChartTheme.Axis, ChartTheme.AxisThickness);
```

在文字迴圈結束處，把

```csharp
                        destinationX = Math.Clamp(destinationX, logicalAxisWidth, Math.Max(logicalAxisWidth, logicalWidth - width));
                        DrawTextBlock(context, textBlock, destinationX, y, width, height);
                    }
                }
            }
```

換成

```csharp
                        destinationX = Math.Clamp(destinationX, logicalAxisWidth, Math.Max(logicalAxisWidth, logicalWidth - width));
                        DrawTextBlock(context, textBlock, destinationX, y, width, height);
                    }
                }
            }

            // 圖例含線段樣本與色點，不在裁切的繪圖區內；與標題一樣在每頁原位補畫，頁面太窄時等比縮小。
            foreach (var legend in legends)
            {
                var legendX = Canvas.GetLeft(legend);
                var legendY = Canvas.GetTop(legend);
                var legendWidth = Math.Max(1, legend.ActualWidth);
                var legendHeight = Math.Max(1, legend.ActualHeight);
                var fit = Math.Min(1, Math.Max(1, logicalWidth - legendX - 4) / legendWidth);
                DrawTextBlock(context, legend, legendX, legendY, legendWidth * fit, legendHeight * fit);
            }
```

4. `DrawTextBlock`：把參數

```csharp
        DrawingContext context,
        TextBlock source,
```

換成

```csharp
        DrawingContext context,
        FrameworkElement source,
```

5. 在 `FindStationLeaders` 方法之後加入：

```csharp
    private static IReadOnlyList<FrameworkElement> FindChartLegends(DependencyObject root)
    {
        var result = new List<FrameworkElement>();
        Visit(root);
        return result;

        void Visit(DependencyObject node)
        {
            if (node is FrameworkElement { Tag: ChartPainter.LegendTag, Visibility: Visibility.Visible } legend
                && !double.IsNaN(Canvas.GetLeft(legend)) && !double.IsNaN(Canvas.GetTop(legend)))
            {
                result.Add(legend);
                return;
            }

            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
                Visit(VisualTreeHelper.GetChild(node, index));
        }
    }
```

6. `Render`：把

```csharp
                context.DrawRectangle(Brushes.White, null, bounds);
```

換成

```csharp
                context.DrawRectangle(ChartTheme.ExportPage, null, bounds);
```

- [ ] **Step 4: 執行，確認通過**

```powershell
dotnet build .\MrtRouteSimulator.slnx -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --chart-theme-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --pdf-pagination-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --synchronous-export-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --output-only
```

Expected: 0／0；`[通過] PDF 分頁每頁保留圖例（N 頁）`、無寫死顏色（7 個來源檔）、`PASS WPF chart theme`；`PASS WPF PDF pagination`；`PASS WPF synchronous diagram export`；`PASS WPF outputs`。

- [ ] **Step 5: 完整 WPF runner**

```powershell
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- .
```

Expected: `PASS WPF visual rules`（含 `McpProjectSwitchExportTests`）。

- [ ] **Step 6: Commit**

```powershell
git add src/MrtRouteSimulator.App/DiagramExportService.cs tests/MrtRouteSimulator.WpfTests/ChartThemeTests.cs
git commit -m "feat: keep chart legend on every PDF page and theme export pages" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: 線性路線圖、V1 路線圖與空間參考點幾何（只換色）

**Files:**
- Modify: `src/MrtRouteSimulator.App/MainWindow.V2.cs`（`DrawV2Route` 線性路徑）
- Modify: `src/MrtRouteSimulator.App/MainWindow.xaml.cs`（`DrawRoute`）
- Modify: `src/MrtRouteSimulator.App/MainWindow.SpatialReferencePointDiagram.cs`
- Test: `tests/MrtRouteSimulator.WpfTests/ChartThemeTests.cs`

**Interfaces:**
- Consumes: `ChartTheme.LimitZone`、`LimitSeries`、`AxisLabel`；`ChartPainter.DrawMessage`；Task 3 的 `VerifyV1Charts`。
- Produces: `private static Color SpatialReferencePointColor(string kind)`。

- [ ] **Step 1: 寫失敗測試**

`Run` 在 `VerifyNoHardCodedChartColors(root);` 之前加入 `VerifySpatialReferencePointColors();`。

`ChartSources` 改為：

```csharp
    private static readonly (string File, string[] Methods)[] ChartSources =
    [
        ("ChartTheme.cs", []),
        ("ChartPainter.cs", []),
        ("MainWindow.V2.cs", ["DrawV2SpeedProfile", "DrawSpeedLimitLabels", "DrawSpeedStopLabels", "DrawSpeedTimeAxisTicks",
            "DrawSafetyDistanceChart", "AddSafetyMarginChip", "DrawTimeDistanceDiagramFull", "DrawV2Route"]),
        ("MainWindow.xaml.cs", ["DrawSpeedProfile", "DrawRoute"]),
        ("MainWindow.TimeDistance.cs", []),
        ("TimeDistanceStationLabelLayout.cs", []),
        ("DiagramExportService.cs", []),
        ("MainWindow.SpatialReferencePointDiagram.cs", [])
    ];
```

把 `VerifyV1Charts` 整個方法換成：

```csharp
    private static void VerifyV1Charts()
    {
        var window = new MainWindow { Width = 1280, Height = 800 };
        try
        {
            window.Show();
            ShellLayoutTests.PumpLayout(window);
            BuildV1Simulation(window);
            ((TabControl)window.FindName("WorkspaceTabControl")!).SelectedItem = window.FindName("SimulationTabItem");
            var views = (TabControl)window.FindName("SimulationViewTabControl")!;
            views.SelectedIndex = 2;
            ShellLayoutTests.PumpLayout(window);
            WpfTestWait.Invoke(window, "DrawSpeedProfile");
            var speed = (Canvas)window.FindName("SpeedCanvas")!;
            var line = speed.Children.OfType<Polyline>().Single();
            Require(ReferenceEquals(line.Stroke, UiTheme.VehicleBrush(0)), "V1 速度線必須使用第一台車的車輛色。");
            Require(line.Points.All(point => point.Y >= 26 - 0.01), "V1 速度線不得高於繪圖區頂端。");
            Require(speed.Children.OfType<TextBlock>().Any(text => Equals(text.Tag, ChartPainter.ValueTickTag) && text.Text == "0")
                    && speed.Children.OfType<TextBlock>().Count(text => Equals(text.Tag, ChartPainter.TimeTickTag)) == 5
                    && speed.Children.OfType<StackPanel>().Any(panel => Equals(panel.Tag, ChartPainter.LegendTag)),
                "V1 速度曲線必須有數值刻度、五個時間刻度與圖例。");

            views.SelectedIndex = 0;
            ShellLayoutTests.PumpLayout(window);
            WpfTestWait.Invoke(window, "DrawRoute");
            var route = (Canvas)window.FindName("RouteCanvas")!;
            Require(ReferenceEquals(route.Children.OfType<Line>().First().Stroke, UiTheme.RailNeutralStrongBrush),
                "V1 路線軌道必須用 RailNeutralStrong。");
            var stations = route.Children.OfType<Ellipse>().ToArray();
            Require(stations.Length > 0 && stations.All(marker => ReferenceEquals(marker.Fill, UiTheme.SurfaceBrush)
                    && ReferenceEquals(marker.Stroke, UiTheme.RailNeutralStrongBrush)),
                "V1 車站點必須白底＋RailNeutralStrong 外框。");
            Require(route.Children.OfType<TextBlock>().Where(text => text.Text.Contains('\n'))
                    .All(text => ReferenceEquals(text.Foreground, UiTheme.TextStrongBrush)),
                "V1 車站名稱必須用 TextStrong。");

            // 非拓樸的線性 V2 路線圖只在沒有播放 frame 時出現；以 V1 建立的路線直接繪製。
            Require(WpfTestWait.Field(window, "_latestPlaybackFrame") is null, "V1 模式不得有 V2 播放 frame。");
            var v2Enabled = typeof(MainWindow).GetField("_v2Enabled", BindingFlags.Instance | BindingFlags.NonPublic)!;
            v2Enabled.SetValue(window, true);
            try { WpfTestWait.Invoke(window, "DrawV2Route"); }
            finally { v2Enabled.SetValue(window, false); }
            var tracks = route.Children.OfType<Line>()
                .Where(item => item.StrokeThickness == 4 && Math.Abs(item.Y1 - item.Y2) < .01 && item.X2 - item.X1 > 100).ToArray();
            Require(tracks.Length == 2 && ReferenceEquals(tracks[0].Stroke, UiTheme.RailDownBrush)
                    && ReferenceEquals(tracks[1].Stroke, UiTheme.RailUpBrush),
                "線性路線圖的下行軌道必須用 RailDown、上行用 RailUp。");
        }
        finally { WpfTestWait.Close(window); }
        Console.WriteLine("[通過] V1 速度曲線、V1 路線圖與線性 V2 路線圖使用主題色");
    }
```

加入：

```csharp
    private static void VerifySpatialReferencePointColors()
    {
        var method = typeof(MainWindow).GetMethod("SpatialReferencePointColor", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 SpatialReferencePointColor。");
        foreach (var (kind, color) in new[]
                 {
                     ("中間站", UiTheme.Success), ("站前折返", UiTheme.Accent), ("站後折返", UiTheme.RailDown),
                     ("中央避車線折返", UiTheme.VehiclePalette[0]), ("銜接點", UiTheme.RailNeutralStrong)
                 })
            Require((Color)method.Invoke(null, [kind])! == color, $"空間參考點「{kind}」必須用 {color}。");
        Console.WriteLine("[通過] 五類空間參考點幾何改用主題色");
    }
```

- [ ] **Step 2: 執行，確認失敗**

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --chart-theme-only
```

Expected: 以 `V1 路線軌道必須用 RailNeutralStrong。` 失敗。

- [ ] **Step 3: 實作 V1 路線圖（`MainWindow.xaml.cs` 的 `DrawRoute`）**

把

```csharp
            AddCanvasText(RouteCanvas, "建立模擬後，這裡會顯示多列車往返動畫。", 26, 28, 14, Color.FromRgb(102, 112, 133));
```

換成

```csharp
            ChartPainter.DrawMessage(RouteCanvas, "建立模擬後，這裡會顯示多列車往返動畫。");
```

把

```csharp
            Stroke = new SolidColorBrush(Color.FromRgb(70, 83, 105)),
            StrokeThickness = 5,
```

換成

```csharp
            Stroke = UiTheme.RailNeutralStrongBrush,
            StrokeThickness = 5,
```

把

```csharp
                Fill = Brushes.White,
                Stroke = new SolidColorBrush(Color.FromRgb(232, 109, 45)),
                StrokeThickness = 4,
```

換成

```csharp
                Fill = UiTheme.SurfaceBrush,
                Stroke = UiTheme.RailNeutralStrongBrush,
                StrokeThickness = 4,
```

把

```csharp
                Foreground = new SolidColorBrush(Color.FromRgb(42, 52, 70)),
```

換成

```csharp
                Foreground = UiTheme.TextStrongBrush,
```

把

```csharp
        AddCanvasText(RouteCanvas, "下行 →", left, 24, 12, Color.FromRgb(102, 112, 133));
        AddCanvasText(RouteCanvas, "← 上行", left, height - 34, 12, Color.FromRgb(102, 112, 133));
```

換成

```csharp
        AddCanvasText(RouteCanvas, "下行 →", left, 24, 12, UiTheme.TextMuted);
        AddCanvasText(RouteCanvas, "← 上行", left, height - 34, 12, UiTheme.TextMuted);
```

把（列車膠囊底色沿用 `TrainColors`，見 Global Constraints）

```csharp
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(2),
                Child = new TextBlock
                {
                    Text = $"{index + 1:00}",
                    Foreground = Brushes.White,
```

換成

```csharp
                BorderBrush = UiTheme.SurfaceBrush,
                BorderThickness = new Thickness(2),
                Child = new TextBlock
                {
                    Text = $"{index + 1:00}",
                    Foreground = UiTheme.SurfaceBrush,
```

- [ ] **Step 4: 實作線性 V2 路線圖（`MainWindow.V2.cs` 的 `DrawV2Route`）**

把

```csharp
        DrawTrackLine(outboundY, "下行 DOWN →");
        DrawTrackLine(inboundY, "← 上行 UP");
```

換成

```csharp
        DrawTrackLine(outboundY, "下行 DOWN →", UiTheme.RailDownBrush);
        DrawTrackLine(inboundY, "← 上行 UP", UiTheme.RailUpBrush);
```

把

```csharp
                    Fill = new SolidColorBrush(Color.FromArgb(42, 231, 165, 48)),
                    Stroke = new SolidColorBrush(Color.FromRgb(205, 126, 24)),
```

換成

```csharp
                    Fill = ChartTheme.LimitZone,
                    Stroke = ChartTheme.LimitSeries,
```

把

```csharp
                AddCanvasText(RouteCanvas, $"{limit.LimitMetersPerSecond * 3.6:0.#}", x1 + 2, top - 17, 10, Color.FromRgb(166, 90, 21));
```

換成

```csharp
                AddCanvasText(RouteCanvas, $"{limit.LimitMetersPerSecond * 3.6:0.#}", x1 + 2, top - 17, 10, ChartTheme.AxisLabel.Color);
```

把

```csharp
                Stroke = new SolidColorBrush(Color.FromRgb(174, 183, 199)),
                StrokeThickness = 1
            });
            AddCanvasText(RouteCanvas, $"{station.StationId}\n{station.PositionMeters / 1000:0.00} km", Math.Clamp(x - 28, 0, width - 58), inboundY + 25, 10, Color.FromRgb(55, 66, 86));
```

換成

```csharp
                Stroke = UiTheme.HairlineBrush,
                StrokeThickness = 1
            });
            AddCanvasText(RouteCanvas, $"{station.StationId}\n{station.PositionMeters / 1000:0.00} km", Math.Clamp(x - 28, 0, width - 58), inboundY + 25, 10, UiTheme.TextStrong);
```

把區域函式

```csharp
        void DrawTrackLine(double y, string label)
        {
            RouteCanvas.Children.Add(new Line
            {
                X1 = left,
                X2 = left + trackWidth,
                Y1 = y,
                Y2 = y,
                Stroke = new SolidColorBrush(Color.FromRgb(70, 83, 105)),
                StrokeThickness = 4
            });
            AddCanvasText(RouteCanvas, label, left, y - 29, 11, Color.FromRgb(92, 103, 123));
        }
```

換成

```csharp
        void DrawTrackLine(double y, string label, Brush stroke)
        {
            RouteCanvas.Children.Add(new Line
            {
                X1 = left,
                X2 = left + trackWidth,
                Y1 = y,
                Y2 = y,
                Stroke = stroke,
                StrokeThickness = 4
            });
            AddCanvasText(RouteCanvas, label, left, y - 29, 11, UiTheme.TextMuted);
        }
```

- [ ] **Step 5: 實作空間參考點幾何（`MainWindow.SpatialReferencePointDiagram.cs`）**

把

```csharp
            var color = point.Kind switch
            {
                "中間站" => Color.FromRgb(47, 107, 86),
                "銜接點" => Color.FromRgb(52, 125, 101),
                "中央避車線折返" => Color.FromRgb(121, 86, 173),
                "站後折返" => Color.FromRgb(188, 92, 52),
                _ => Color.FromRgb(42, 111, 162)
            };
```

換成

```csharp
            var color = SpatialReferencePointColor(point.Kind);
```

檔內兩處 `Fill = Brushes.White,` 都換成 `Fill = UiTheme.SurfaceBrush,`。

把

```csharp
        var color = Color.FromRgb(188, 92, 52);
```

換成

```csharp
        var color = UiTheme.RailDown;
```

在 `private void DrawAfterStationTailTrackGeometry(` 之前加入：

```csharp
    // 五類空間參考點的幾何顏色；與運行圖、配線圖共用主題色票。
    private static Color SpatialReferencePointColor(string kind) => kind switch
    {
        "中間站" => UiTheme.Success,
        "銜接點" => UiTheme.RailNeutralStrong,
        "中央避車線折返" => UiTheme.VehiclePalette[0],
        "站後折返" => UiTheme.RailDown,
        _ => UiTheme.Accent
    };

```

- [ ] **Step 6: 執行，確認通過**

```powershell
dotnet build .\MrtRouteSimulator.slnx -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --chart-theme-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --shell-layout-only
```

Expected: 0／0；`[通過] V1 速度曲線、V1 路線圖與線性 V2 路線圖使用主題色`、`[通過] 五類空間參考點幾何改用主題色`、無寫死顏色（8 個來源檔）、`PASS WPF chart theme`；`PASS WPF shell layout`。

- [ ] **Step 7: 完整 WPF runner**

```powershell
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- .
```

Expected: `PASS WPF visual rules`（含 `TrackDiagramThemeTests` 的 `TrainColors` 檢查）。

- [ ] **Step 8: Commit**

```powershell
git add src/MrtRouteSimulator.App/MainWindow.V2.cs src/MrtRouteSimulator.App/MainWindow.xaml.cs src/MrtRouteSimulator.App/MainWindow.SpatialReferencePointDiagram.cs tests/MrtRouteSimulator.WpfTests/ChartThemeTests.cs
git commit -m "feat: recolor linear route and spatial reference geometry with theme colors" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: 完整驗證、截圖與文件

**Files:**
- Modify: `README.md`、`docs/superpowers/specs/2026-10-10-result-charts-design.md`、`CHANGELOG.md`、`QA_REPORT.md`

- [ ] **Step 1: 完整驗證（互動桌面、不操作視窗）**

```powershell
query user
Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
dotnet build .\MrtRouteSimulator.slnx -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.Tests\MrtRouteSimulator.Tests.csproj -c Release --no-build --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- .
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- .\samples\14-大型-二十八站完整營運範例.mrtsim.json --large-playback-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --pacing-lazy-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- .\samples\14-大型-二十八站完整營運範例.mrtsim.json --profile-interactive-timedistance-only > "$(git rev-parse --show-toplevel)\.superpowers\sdd\2026-10-10-result-charts\profile-after.txt"
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --shell-screenshots
```

Expected: session `Active`、工作區高度 ≥ 720；整個 solution 0 warning／0 error；Engine 198/198；完整 WPF runner `PASS WPF visual rules` 且含 `PASS WPF chart theme`、`PASS WPF result pages`、`PASS WPF shell layout`；大型播放 PASS，倍率與最大 UI 輸入間隔不比 Task 0 基準差超過 20%；`PASS WPF pacing/lazy/incremental`；比較 `profile-before.txt` 與 `profile-after.txt` 的 `Warm` 各次 `TimeDistance.StaticVisuals`、`TimeDistance.SeriesVisuals` 耗時，增加不超過 1.5 倍（超過即視為回歸，先查原因）；`artifacts\shell-screenshots` 內 19 張 PNG。

- [ ] **Step 2: 人工外觀檢查**

用 Read 工具開啟 `artifacts\shell-screenshots\` 的 `speed`、`safety`、`diagram`、`v1-route`、`v1-speed`、`export-diagram`、`export-pdf-page1` 等圖，並與 `artifacts\d-before\` 同名圖對照，逐項確認：速度曲線與距離圖有標題列、圖例、Y 軸數值刻度與時間刻度，曲線沒有超出繪圖區；速度線為車輛色；距離圖最低裕度色點顏色合理；運行圖標題、圖例色點、事件點與格線顏色；PDF 第一頁有標題與圖例線段；V1 路線圖軌道與車站點；沒有文字重疊或被裁切。發現問題只改外觀數值，先以測試重現再修正，修正後重跑 Step 1 對應的測試。把新舊截圖傳給使用者看。

- [ ] **Step 3: 文件**

1. `README.md`：
   - 把「- 速度曲線以實體列車 `VehicleId` 選取完整行程，串接上下行、停站及折返車次，並標示換向位置。」改為「- 速度曲線以實體列車 `VehicleId` 選取完整行程，串接上下行、停站及折返車次，並標示換向位置；圖上有 km/h 數值刻度、時間刻度與圖例，線色與該列車在配線圖、運行圖上的顏色相同。」
   - 把「- 移動閉塞仍可分上下行檢核，方向篩選同時作用於歷史配對、圖表與安全摘要；列車退出後仍可選取歷史配對，以「全部時間」查看較早資料。」改為「- 移動閉塞仍可分上下行檢核，方向篩選同時作用於歷史配對、圖表與安全摘要；列車退出後仍可選取歷史配對，以「全部時間」查看較早資料。相鄰列車距離圖標出最低安全裕度，顏色與閉塞表狀態色點相同（需要制動為黃色）。」
   - 把「- 時間－里程列車運行圖：計畫／理論與模擬實際軌跡、方向／車輛／時間篩選、縮放、事件標記及滑鼠提示。」改為「- 時間－里程列車運行圖：計畫／理論與模擬實際軌跡、方向／車輛／時間篩選、縮放、事件標記（綠：站點、灰：端點、紅：安全）及滑鼠提示。」
   - 把「- PNG（一般／高解析度）、PDF（A4／A3、橫向、可分頁）及 CSV 軌跡／事件匯出。」改為「- PNG（一般／高解析度）、PDF（A4／A3、橫向、可分頁，每頁保留標題與圖例）及 CSV 軌跡／事件匯出。」
2. D 規格 `2026-10-10-result-charts-design.md`：
   - 「狀態」改為「已實作（2026-10-10）」。
   - 第 2 節表格後加入 `NeutralSeries`、`MarkerOutline`、`LimitZone` 三列與虛線樣式、線寬字級常數說明；第 3 節表格加入 `DrawAxes`、`DrawHeader`、`Place`，並註明 `ChartPainter` 為 `public`、`DrawLegend` 回傳 `StackPanel`。
   - 第 4 節依本計畫「規格的實作細化」第 4～11 點補充（時間刻度 9 px、標題文字、距離圖版面與狀態文字、運行圖圖例變更、空白提示位置、`TrainColors` 保留、路線圖各元素顏色、`SafetyStatusBrush`）。
3. `CHANGELOG.md` 的 `## V4.1.0` 下、C2 那一節之前新增：

```markdown
### 2026-10-10 介面翻新子專案 D：結果圖表主題統一

- 新增 `ChartTheme`（圖表色票，全部引用 `UiTheme`）與 `ChartPainter`（取整數值刻度、軸線、時間刻度、圖例、標題、空白提示）；速度曲線、相鄰列車距離圖、時間－里程運行圖與 PNG／PDF 匯出改用同一套外觀與 App 字型。
- 速度曲線與距離圖新增 Y 軸數值刻度、時間刻度與圖例；速度線改為該列車的車輛色（與配線圖、運行圖一致）。
- 「需要制動」等安全狀態在距離圖、路線圖與閉塞表同色（依 `StatusTones`）；運行圖事件點改為站點綠、端點灰、安全紅，並以色點圖例說明。
- PDF 分頁時每頁補畫圖例；匯出頁底色與補畫軸線改用主題色。線性路線圖、V1 路線圖與五類空間參考點幾何改用主題色。
- 設計見 `docs/superpowers/specs/2026-10-10-result-charts-design.md`。
```

4. `QA_REPORT.md` 第一個 `## ` 之前新增一節，數字一律填 Step 1 的實際輸出：

```markdown
## 介面翻新子專案 D：結果圖表主題統一（2026-10-10）

- Release build：<實際 warning／error 數>。
- Engine runner：<通過數>/<總數>。
- WPF runner（互動桌面）：<PASS WPF visual rules／失敗訊息>，<[通過] 行數> 項 `[通過]`；新增 `ChartThemeTests`（色票引用、取整刻度與防護、繪圖工具樣式、窄畫布標題列、V2／V1 速度曲線、距離圖與安全狀態色、運行圖兩條繪製路徑、PDF 每頁圖例、線性與 V1 路線圖、空間參考點、寫死顏色掃描）全部通過。
- 大型 28 站播放診斷：<PASS／倍率／最大 UI 輸入間隔>；播放節奏測試：<PASS>；互動運行圖效能：<StaticVisuals／SeriesVisuals 改動前後>。
- 人工截圖檢查（範例 14 推進 5 分鐘與 V1 預設輸入，1280×800，含匯出 PNG 與 PDF 第一頁，與改版前對照）：<結果>。
- 未涵蓋：拓樸配線圖其餘色值（子專案 B 範圍）、拓樸編輯器（子專案 E）；原生桌面 DPI 與實體滑鼠操作未重跑。
```

- [ ] **Step 4: Commit**

```powershell
git add README.md docs/superpowers/specs/2026-10-10-result-charts-design.md CHANGELOG.md QA_REPORT.md
git commit -m "docs: record result charts verification and refresh user docs" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
