# UI 設計語言基礎與軌道配線圖翻新 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立 `UiTheme` 色票／畫筆單一來源，並把主畫面軌道配線圖翻新為「C 柔和雙色軌道 + A 膠囊列車 + B 區段占用亮燈（可開關）」。

**Architecture:** 新增四個小型、單一責任的呈現檔（`UiTheme`、`TrainMarkerPresentation`、`TrackRailStyle`、`TrackOccupancySnapshot`），由既有的 `MainWindow.V2.cs` 與 `StationSchematicPresentation.cs` 呼叫。占用資料由播放 worker 從 `SimulationWorld.TopologyOccupancy` 讀出，放進 `PlaybackFrame`，UI 只負責繪製；Engine 完全不改。幾何（軌道、月台、列車座標）規則不動，只改外觀屬性。

**Tech Stack:** .NET 10、WPF（C# 14）、自製 WPF 測試 runner（`tests/MrtRouteSimulator.WpfTests`，以反射存取 internal 型別）。

**Spec:** `docs/superpowers/specs/2026-10-09-ui-theme-and-track-diagram-design.md`

## Global Constraints

- 不修改 `src/MrtRouteSimulator.Engine/` 任何檔案；不改專案 Schema；不改版本號（`Directory.Build.props`）。
- `MainWindow.DrawTopologyGraphRoute` 的 7 個參數簽章不變，**不可新增同名 overload**（測試以 `GetMethod("DrawTopologyGraphRoute", NonPublic|Instance)` 反射呼叫）。
- `AppDisplayPreferences.DisplaySettings` 的主建構子維持 `(bool ShowLockedRoutes, double RouteMapHorizontalZoom, double InterfaceScale)`；`InterfaceScalePreferenceTests` 以此為契約。
- 軌道 `Polyline` 的 `ToolTip` 以 `"{edgeId}\n"` 開頭，且每個 edge 恰好一條；光帶、圖例不得帶有含 `"已鎖定進路"` 的 `ToolTip`。
- 配線圖上所有 `Polyline` 折角必須小於 45°，且不得超出畫布寬度。
- 鎖定進路色盤每色都要符合 `R > B && G < 170`。
- `PlatformBodyAnchor`（`Rectangle`）、`PlatformNumberAnchor`（`TextBlock`）、`StationLabelAnchor`、列車 `Border.Tag = vehicleId` 字串的型別與欄位不變；停站列車中心與月台中心誤差 < 0.51 px。
- App 專案為 `TreatWarningsAsErrors=true`：不得留下未使用的區域變數或 nullable 警告。
- 新程式碼一律寫 `System.Windows.Shapes.Path`（避免與 `System.IO.Path` 衝突）。
- UI 文字固定為：選單「顯示區段占用亮燈」、圖例「區段占用」「已鎖定進路」。
- Commit 訊息遵循 Conventional Commits，結尾加上 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`；不 push。只 `git add` 列出的檔案，不要加入 `artifacts/` 輸出。

## Review Focus

1. **列車剛好壓在節點邊界**（footprint 在相鄰 edge 上只有零長度區間）→ 只點亮真正有重疊的區段，不可連隔壁區段一起亮。由 Task 6 測試釘住。
2. **非 ASCII 或格式怪異的車號**（中文分段、連續分隔符、結尾分隔符、超長無分隔符）→ 標籤 ≤ 10 字、可讀、不為空。由 Task 2 測試釘住。
3. **列車位於畫布邊緣** → 膠囊完整落在畫布內，不被裁切。由 Task 2 `Place` 測試釘住。
4. **設定檔內 `ShowTrackOccupancy` 的值格式錯誤**（字串、null、數字、陣列）→ 視為開啟，且其他偏好保留。由 Task 7 測試釘住。
5. **兩列車占用同一區段** → 只畫一條光帶，兩個車號都被記錄。由 Task 6（資料彙整）與 Task 8（每個區段一條光帶）測試釘住。

---

## File Structure

| 檔案 | 動作 | 責任 |
|---|---|---|
| `src/MrtRouteSimulator.App/UiTheme.cs` | 新增 | 色票、凍結畫筆、列車／鎖定進路色盤（單一來源） |
| `src/MrtRouteSimulator.App/TrainMarkerPresentation.cs` | 新增 | 列車短號規則、畫面方向判斷、膠囊建立與定位 |
| `src/MrtRouteSimulator.App/TrackRailStyle.cs` | 新增 | 軌道配色分類（下行／上行／淺色／中性）與粗細 |
| `src/MrtRouteSimulator.App/TrackOccupancySnapshot.cs` | 新增 | 將 footprint 區間彙整成「edgeId → 車號」 |
| `src/MrtRouteSimulator.App/MainWindow.xaml.cs` | 修改 | `TrainColors` 改指向 `UiTheme`；建構子設定畫布底色、載入亮燈偏好 |
| `src/MrtRouteSimulator.App/MainWindow.V2.cs` | 修改 | 配線圖軌道／接軌／止衝擋／列車／亮燈圖層／圖例；開關處理 |
| `src/MrtRouteSimulator.App/StationSchematicPresentation.cs` | 修改 | 月台、站名徽章、圖例、里程說明文字 |
| `src/MrtRouteSimulator.App/TopologyEditorWindow.cs` | 修改 | `DrawStationNames` 呼叫端改傳 `(Id, Name, ChainageMeters)` |
| `src/MrtRouteSimulator.App/SimulationPlaybackWorker.cs` | 修改 | `PlaybackFrame.TrackEdgeOccupants` 與 `PublishFrame` |
| `src/MrtRouteSimulator.App/AppDisplayPreferences.cs` | 修改 | `ShowTrackOccupancy` 偏好（預設開啟） |
| `src/MrtRouteSimulator.App/MainWindow.xaml` | 修改 | 新增「顯示區段占用亮燈」選單項目 |
| `tests/MrtRouteSimulator.WpfTests/TrackDiagramThemeTests.cs` | 新增 | 本輪全部回歸測試 |
| `tests/MrtRouteSimulator.WpfTests/Program.cs` | 修改 | `--track-theme-only` 旗標，並納入完整 runner |
| `CHANGELOG.md`、`QA_REPORT.md` | 修改 | Task 9 記錄變更與驗收結果 |

**共用指令**（在 worktree 根目錄以 PowerShell 執行）：

```powershell
# 建置 WPF 測試（會一併建置 App 與 Engine）
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
# 只跑本輪測試
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --track-theme-only
```

---

### Task 0: 基準線

**Files:** 無修改。

- [ ] **Step 1: 還原專案（App、WPF 測試、Engine 測試都沒有外部套件，可離線完成）**

```powershell
dotnet restore .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj
dotnet restore .\tests\MrtRouteSimulator.Tests\MrtRouteSimulator.Tests.csproj
```

Expected: `Restore complete`，沒有錯誤。若出現需要下載套件的錯誤，停下來回報，不要自行換 NuGet 來源。

- [ ] **Step 2: 建置並跑 Engine 測試**

```powershell
dotnet build .\tests\MrtRouteSimulator.Tests\MrtRouteSimulator.Tests.csproj -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.Tests\MrtRouteSimulator.Tests.csproj -c Release --no-build --no-restore
```

Expected: 全部通過，記下通過數。

- [ ] **Step 3: 建置並跑完整 WPF runner（耗時數分鐘，可背景執行）**

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- .
```

Expected: 最後一行 `PASS WPF visual rules`。若基準線就失敗，記錄失敗訊息並回報，後續任務不得把既有失敗算成新回歸，也不得把新回歸歸咎於基準線。

---

### Task 1: UiTheme 色票單一來源（子專案 A）

**Files:**
- Create: `src/MrtRouteSimulator.App/UiTheme.cs`
- Modify: `src/MrtRouteSimulator.App/MainWindow.xaml.cs:18-28`（`TrainColors`）
- Modify: `src/MrtRouteSimulator.App/MainWindow.V2.cs:17-24`（`LockedRouteColors`）
- Create: `tests/MrtRouteSimulator.WpfTests/TrackDiagramThemeTests.cs`
- Modify: `tests/MrtRouteSimulator.WpfTests/Program.cs`

**Interfaces:**
- Produces（後續任務都會用到）：
  - `internal static class UiTheme`
  - `const double OccupancyGlowThickness = 16`
  - `Color` 欄位：`CanvasBackground`、`TextStrong`、`TextMuted`、`TextSubtle`、`Hairline`、`RailUp`、`RailUpSoft`、`RailDown`、`RailDownSoft`、`RailNeutral`、`RailNeutralStrong`、`PlatformFill`、`PlatformBidirectional`、`StationBadgeFill`、`OccupancyGlow`、`Danger`
  - 對應的凍結 `SolidColorBrush` 欄位：上述名稱 + `Brush`（例：`RailDownBrush`）
  - `Color[] VehiclePalette`、`Color[] LockedRoutePalette`、`SolidColorBrush[] VehicleBrushes`、`SolidColorBrush[] LockedRouteBrushes`
  - `SolidColorBrush VehicleBrush(int vehicleIndex)`
  - 測試檔中的共用 helper：`AppType(string)`、`StaticValue(Type, string)`、`CallStatic(Type, string, params object?[])`、`Require(bool, string)`

- [ ] **Step 1: 寫失敗測試**

建立 `tests/MrtRouteSimulator.WpfTests/TrackDiagramThemeTests.cs`：

```csharp
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

/// <summary>介面翻新第一輪（UiTheme + 軌道配線圖）的回歸測試。</summary>
internal static class TrackDiagramThemeTests
{
    private static readonly Assembly AppAssembly = typeof(MainWindow).Assembly;

    public static void Run(string root)
    {
        VerifyThemeTokens();
        Console.WriteLine("PASS WPF track diagram theme");
    }

    private static void VerifyThemeTokens()
    {
        var theme = AppType("UiTheme");
        var brushes = theme.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(SolidColorBrush))
            .Select(field => (field.Name, Brush: (SolidColorBrush)field.GetValue(null)!))
            .ToArray();
        Require(brushes.Length >= 16, $"UiTheme 應提供至少 16 支具名畫筆，實際 {brushes.Length}。");
        Require(brushes.All(item => item.Brush.IsFrozen),
            $"UiTheme 畫筆必須全部凍結：{string.Join("、", brushes.Where(item => !item.Brush.IsFrozen).Select(item => item.Name))}");
        Require(((Color)StaticValue(theme, "OccupancyGlow")!).A == 0x80, "區段占用光帶必須是半透明（alpha 0x80）。");

        var vehicle = (Color[])StaticValue(theme, "VehiclePalette")!;
        Require(vehicle.Length == 8 && vehicle.Distinct().Count() == 8, "列車色盤必須是 8 個不重複顏色。");
        var avoid = new[] { "RailUp", "RailDown", "Danger" }.Select(name => Hsv((Color)StaticValue(theme, name)!).H).ToArray();
        foreach (var color in vehicle)
        {
            var (hue, saturation, _) = Hsv(color);
            Require(saturation < .35 || avoid.All(other => HueDistance(hue, other) >= 20),
                $"列車色 {color} 的色相太接近上下行軌道或危險紅。");
        }
        var vehicleBrushes = (SolidColorBrush[])StaticValue(theme, "VehicleBrushes")!;
        Require(vehicleBrushes.Length == vehicle.Length
            && vehicleBrushes.Select(brush => brush.Color).SequenceEqual(vehicle)
            && vehicleBrushes.All(brush => brush.IsFrozen), "VehicleBrushes 必須與 VehiclePalette 一一對應且凍結。");
        var negative = (SolidColorBrush)CallStatic(theme, "VehicleBrush", -3)!;
        Require(vehicleBrushes.Contains(negative), "VehicleBrush 對負索引也必須回傳色盤內的畫筆。");

        var locked = (Color[])StaticValue(theme, "LockedRoutePalette")!;
        Require(locked.Length == 5 && locked.All(color => color.R > color.B && color.G < 170),
            "鎖定進路色盤必須全部是 R > B 且 G < 170 的紅／洋紅系。");

        var trainColors = (Color[])typeof(MainWindow).GetField("TrainColors", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var lockColors = (Color[])typeof(MainWindow).GetField("LockedRouteColors", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Require(ReferenceEquals(trainColors, vehicle), "MainWindow.TrainColors 必須直接引用 UiTheme.VehiclePalette。");
        Require(ReferenceEquals(lockColors, locked), "MainWindow.LockedRouteColors 必須直接引用 UiTheme.LockedRoutePalette。");
        Console.WriteLine("[通過] UiTheme 色票、凍結畫筆與色盤規則");
    }

    private static (double H, double S, double V) Hsv(Color color)
    {
        double r = color.R / 255d, g = color.G / 255d, b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        var hue = delta == 0 ? 0
            : max == r ? 60 * (((g - b) / delta) % 6)
            : max == g ? 60 * ((b - r) / delta + 2)
            : 60 * ((r - g) / delta + 4);
        if (hue < 0) hue += 360;
        return (hue, max == 0 ? 0 : delta / max, max);
    }

    private static double HueDistance(double first, double second)
    {
        var distance = Math.Abs(first - second) % 360;
        return distance > 180 ? 360 - distance : distance;
    }

    private static Type AppType(string name) =>
        AppAssembly.GetType($"MrtRouteSimulator.App.{name}")
        ?? throw new InvalidOperationException($"找不到 App 型別 {name}。");

    private static object? StaticValue(Type type, string name)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        if (type.GetField(name, flags) is { } field) return field.GetValue(null);
        if (type.GetProperty(name, flags) is { } property) return property.GetValue(null);
        throw new InvalidOperationException($"{type.Name} 找不到靜態成員 {name}。");
    }

    private static object? CallStatic(Type type, string method, params object?[] args)
    {
        var target = type.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"{type.Name} 找不到靜態方法 {method}。");
        return target.Invoke(null, args);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
```

在 `tests/MrtRouteSimulator.WpfTests/Program.cs` 的 `--synchronous-export-only` 區塊之前加入：

```csharp
            if (args.Contains("--track-theme-only"))
            {
                TrackDiagramThemeTests.Run(GetRoot(args));
                return 0;
            }
```

並在完整 runner 的 `VisualRulesTests.Run();` 下一行加入：

```csharp
            TrackDiagramThemeTests.Run(GetRoot(args));
```

- [ ] **Step 2: 執行測試，確認失敗**

執行共用指令（建置 + `--track-theme-only`）。
Expected: 建置成功；執行時丟出 `找不到 App 型別 UiTheme。`，結束碼 1。

- [ ] **Step 3: 實作 UiTheme**

建立 `src/MrtRouteSimulator.App/UiTheme.cs`：

```csharp
using System.Windows.Media;

namespace MrtRouteSimulator.App;

/// <summary>
/// App 顏色與共用畫筆的單一來源。畫筆全部凍結，可在每一幀重用而不重建；
/// XAML 需要時由 code-behind 或 x:Static 引用，不在 App.xaml 重複定義同一色值。
/// </summary>
internal static class UiTheme
{
    public const double OccupancyGlowThickness = 16;

    public static readonly Color CanvasBackground = Color.FromRgb(0xFB, 0xFC, 0xFE);
    public static readonly Color TextStrong = Color.FromRgb(0x1E, 0x29, 0x3B);
    public static readonly Color TextMuted = Color.FromRgb(0x64, 0x74, 0x8B);
    public static readonly Color TextSubtle = Color.FromRgb(0x94, 0xA3, 0xB8);
    public static readonly Color Hairline = Color.FromRgb(0xCB, 0xD5, 0xE1);
    public static readonly Color RailUp = Color.FromRgb(0x2F, 0x7F, 0xC1);
    public static readonly Color RailUpSoft = Color.FromRgb(0x8C, 0xB8, 0xE0);
    public static readonly Color RailDown = Color.FromRgb(0xE8, 0x83, 0x3A);
    public static readonly Color RailDownSoft = Color.FromRgb(0xF2, 0xB4, 0x88);
    public static readonly Color RailNeutral = Color.FromRgb(0x94, 0xA3, 0xB8);
    public static readonly Color RailNeutralStrong = Color.FromRgb(0x47, 0x55, 0x69);
    public static readonly Color PlatformFill = Colors.White;
    public static readonly Color PlatformBidirectional = Color.FromRgb(0x64, 0x74, 0x8B);
    public static readonly Color StationBadgeFill = Color.FromRgb(0xEE, 0xF2, 0xF7);
    public static readonly Color OccupancyGlow = Color.FromArgb(0x80, 0xFF, 0xC9, 0x3C);
    public static readonly Color Danger = Color.FromRgb(0xC4, 0x30, 0x30);

    // 每台車一色，跨配線圖、運行圖與速度曲線共用；色相避開上下行軌道與危險紅。
    public static readonly Color[] VehiclePalette =
    [
        Color.FromRgb(0x7C, 0x5C, 0xD6),
        Color.FromRgb(0x0F, 0x9D, 0x8A),
        Color.FromRgb(0xC2, 0x41, 0x8F),
        Color.FromRgb(0x4B, 0x5B, 0x6E),
        Color.FromRgb(0x3F, 0x9C, 0x35),
        Color.FromRgb(0x54, 0x68, 0xD4),
        Color.FromRgb(0x9B, 0x4D, 0xCA),
        Color.FromRgb(0x6E, 0x73, 0x16)
    ];

    // 鎖定進路維持紅／洋紅系，與下行橘軌區隔。
    public static readonly Color[] LockedRoutePalette =
    [
        Color.FromRgb(0xD9, 0x3A, 0x4A),
        Color.FromRgb(0xC0, 0x26, 0x6D),
        Color.FromRgb(0xE0, 0x47, 0x5F),
        Color.FromRgb(0xA8, 0x32, 0x6E),
        Color.FromRgb(0xCC, 0x3D, 0x3D)
    ];

    public static readonly SolidColorBrush CanvasBackgroundBrush = Frozen(CanvasBackground);
    public static readonly SolidColorBrush TextStrongBrush = Frozen(TextStrong);
    public static readonly SolidColorBrush TextMutedBrush = Frozen(TextMuted);
    public static readonly SolidColorBrush TextSubtleBrush = Frozen(TextSubtle);
    public static readonly SolidColorBrush HairlineBrush = Frozen(Hairline);
    public static readonly SolidColorBrush RailUpBrush = Frozen(RailUp);
    public static readonly SolidColorBrush RailUpSoftBrush = Frozen(RailUpSoft);
    public static readonly SolidColorBrush RailDownBrush = Frozen(RailDown);
    public static readonly SolidColorBrush RailDownSoftBrush = Frozen(RailDownSoft);
    public static readonly SolidColorBrush RailNeutralBrush = Frozen(RailNeutral);
    public static readonly SolidColorBrush RailNeutralStrongBrush = Frozen(RailNeutralStrong);
    public static readonly SolidColorBrush PlatformFillBrush = Frozen(PlatformFill);
    public static readonly SolidColorBrush PlatformBidirectionalBrush = Frozen(PlatformBidirectional);
    public static readonly SolidColorBrush StationBadgeFillBrush = Frozen(StationBadgeFill);
    public static readonly SolidColorBrush OccupancyGlowBrush = Frozen(OccupancyGlow);
    public static readonly SolidColorBrush DangerBrush = Frozen(Danger);

    public static readonly SolidColorBrush[] VehicleBrushes = VehiclePalette.Select(Frozen).ToArray();
    public static readonly SolidColorBrush[] LockedRouteBrushes = LockedRoutePalette.Select(Frozen).ToArray();

    public static SolidColorBrush VehicleBrush(int vehicleIndex) =>
        VehicleBrushes[((vehicleIndex % VehicleBrushes.Length) + VehicleBrushes.Length) % VehicleBrushes.Length];

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
```

在 `MainWindow.xaml.cs` 把整段 `TrainColors` 陣列宣告替換為：

```csharp
    private static readonly Color[] TrainColors = UiTheme.VehiclePalette;
```

在 `MainWindow.V2.cs` 把整段 `LockedRouteColors` 陣列宣告替換為：

```csharp
    private static readonly Color[] LockedRouteColors = UiTheme.LockedRoutePalette;
```

- [ ] **Step 4: 執行測試，確認通過**

執行共用指令。
Expected: `[通過] UiTheme 色票、凍結畫筆與色盤規則` 與 `PASS WPF track diagram theme`，結束碼 0；建置 0 warning。

- [ ] **Step 5: Commit**

```powershell
git add src/MrtRouteSimulator.App/UiTheme.cs src/MrtRouteSimulator.App/MainWindow.xaml.cs src/MrtRouteSimulator.App/MainWindow.V2.cs tests/MrtRouteSimulator.WpfTests/TrackDiagramThemeTests.cs tests/MrtRouteSimulator.WpfTests/Program.cs
git commit -m "feat: add UiTheme as single source for app colors" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: 膠囊列車標記

**Files:**
- Create: `src/MrtRouteSimulator.App/TrainMarkerPresentation.cs`
- Modify: `src/MrtRouteSimulator.App/MainWindow.V2.cs`（`DrawTopologyTrainMarkers` 約 1362-1412 行；`VehicleMarkerLabel` 約 2443-2457 行）
- Test: `tests/MrtRouteSimulator.WpfTests/TrackDiagramThemeTests.cs`

**Interfaces:**
- Consumes: `UiTheme.VehicleBrush(int)`、`UiTheme.DangerBrush`
- Produces:
  - `internal static class TrainMarkerPresentation`
  - `const double MarkerHeight = 18`、`const double MinimumWidth = 34`
  - `string Label(string vehicleId)`
  - `int Heading(Point? front, Point center, TrainDirection direction)`（+1 = 畫面向右，-1 = 向左）
  - `Border Create(string vehicleId, Brush fill, int heading, string toolTip)`
  - `void Place(FrameworkElement marker, Point center, double canvasWidth, double canvasHeight)`（同時把 `ZIndex` 設為 5）
  - 測試 helper：`DrawSample(MainWindow main, string root, string fileName, double width, double seconds)` → `(Canvas Canvas, TopologyProjectDocument Sample)`

- [ ] **Step 1: 寫失敗測試**

在 `TrackDiagramThemeTests.Run` 的 `VerifyThemeTokens();` 之後加入 `VerifyTrainMarkers(root);`，並加入下列方法：

```csharp
    private static void VerifyTrainMarkers(string root)
    {
        var presentation = AppType("TrainMarkerPresentation");
        string Label(string id) => (string)CallStatic(presentation, "Label", id)!;
        var labelCases = new (string Input, string Expected)[]
        {
            ("Vehicle 3", "V03"),
            ("AUTO-007", "A07"),
            ("FULL-O04", "FULL-O04"),
            ("EXPRESS-01", "EXPRESS-01"),
            ("FULL-UP-01", "FULL-UP-01"),
            ("SECTION-VEHICLE-01", "SV-01"),
            ("FULL-SECTION-O04", "FS-O04"),
            ("普通車-區間快速-0001", "普區-0001"),
            ("LONG-VEHICLE-NAME-", "LV-NAME"),
            ("A--B-C-0123456789", "ABC-01234…"),
            ("ABCDEFGHIJKLMN", "ABCDEFGHI…")
        };
        var mainLabel = typeof(MainWindow).GetMethod("VehicleMarkerLabel", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var (input, expected) in labelCases)
        {
            var actual = Label(input);
            Require(actual == expected, $"車號 {input} 應顯示 {expected}，實際 {actual}。");
            Require(actual.Length is > 0 and <= 10, $"車號 {input} 的標籤長度必須介於 1～10。");
            Require((string)mainLabel.Invoke(null, [input])! == expected, $"MainWindow.VehicleMarkerLabel 必須委派給 TrainMarkerPresentation（{input}）。");
        }

        int Heading(Point? front, Point center, TrainDirection direction) =>
            (int)CallStatic(presentation, "Heading", front, center, direction)!;
        Require(Heading(new Point(110, 0), new Point(100, 0), TrainDirection.Inbound) == 1, "車頭在右側時箭頭必須朝右。");
        Require(Heading(new Point(90, 0), new Point(100, 0), TrainDirection.Outbound) == -1, "車頭在左側時箭頭必須朝左（例如折返後）。");
        Require(Heading(new Point(100.3, 0), new Point(100, 0), TrainDirection.Inbound) == -1, "車頭與車體中心幾乎重合時改依行車方向。");
        Require(Heading(null, new Point(100, 0), TrainDirection.Outbound) == 1, "缺少車頭位置時下行朝右。");

        var right = (Border)CallStatic(presentation, "Create", "SECTION-VEHICLE-01", Brushes.Teal, 1, "tip")!;
        var rightGrid = (Grid)right.Child;
        var rightText = rightGrid.Children.OfType<TextBlock>().Single();
        var rightChevron = rightGrid.Children.OfType<System.Windows.Shapes.Path>().Single();
        Require(right.Height == 18 && right.Width >= 34 && right.CornerRadius.TopLeft == 9, "列車膠囊應為高 18、圓角 9、最小寬 34。");
        Require(rightText.Text == "SV-01" && Grid.GetColumn(rightText) == 0 && Grid.GetColumn(rightChevron) == 1,
            "朝右膠囊的箭頭必須在右側（領先端）。");
        Require(Equals(right.ToolTip, "tip"), "膠囊必須保留呼叫端提供的提示文字。");
        var left = (Border)CallStatic(presentation, "Create", "E1", Brushes.Teal, -1, "tip")!;
        Require(Grid.GetColumn(((Grid)left.Child).Children.OfType<System.Windows.Shapes.Path>().Single()) == 0,
            "朝左膠囊的箭頭必須在左側（領先端）。");

        CallStatic(presentation, "Place", right, new Point(5, 5), 300d, 200d);
        Require(Canvas.GetLeft(right) == 0 && Canvas.GetTop(right) == 0 && Panel.GetZIndex(right) == 5,
            "左上角列車必須完整留在畫布內，且位於月台之上（ZIndex 5）。");
        CallStatic(presentation, "Place", right, new Point(298, 199), 300d, 200d);
        Require(Math.Abs(Canvas.GetLeft(right) + right.Width - 300) < 1e-9 && Math.Abs(Canvas.GetTop(right) - 182) < 1e-9,
            "右下角列車必須完整留在畫布內。");
        CallStatic(presentation, "Place", right, new Point(150, 100), 300d, 200d);
        Require(Math.Abs(Canvas.GetLeft(right) + right.Width / 2 - 150) < 1e-9 && Math.Abs(Canvas.GetTop(right) - 91) < 1e-9,
            "一般位置的列車必須以中心點置中。");

        var vehicleBrushes = (SolidColorBrush[])StaticValue(AppType("UiTheme"), "VehicleBrushes")!;
        var danger = StaticValue(AppType("UiTheme"), "DangerBrush");
        var main = new MainWindow();
        try
        {
            // 範例 11 的 LOCAL-01 於 0 秒停靠 P-W-D（VisualRulesTests 已驗證），可穩定取得列車標記。
            var (canvas, _) = DrawSample(main, root, "11-小型-三站完整拓樸運行範例.mrtsim.json", 1200, 0);
            var trains = canvas.Children.OfType<Border>().Where(border => border.Tag is string).ToArray();
            Require(trains.Length > 0, "範例 11 在 0 秒應至少顯示一列車。");
            foreach (var train in trains)
            {
                var text = ((Grid)train.Child).Children.OfType<TextBlock>().Single();
                Require(train.Height == 18 && Panel.GetZIndex(train) == 5, $"{train.Tag}：列車膠囊尺寸或圖層錯誤。");
                Require(vehicleBrushes.Any(brush => ReferenceEquals(brush, train.Background)) || ReferenceEquals(train.Background, danger),
                    $"{train.Tag}：列車顏色必須取自 UiTheme。");
                Require(text.Text == Label((string)train.Tag), $"{train.Tag}：列車標籤必須套用短號規則。");
                Require(Canvas.GetLeft(train) >= 0 && Canvas.GetLeft(train) + train.Width <= canvas.Width + 1e-9,
                    $"{train.Tag}：列車膠囊不可超出畫布。");
            }
        }
        finally { main.Close(); }
        Console.WriteLine("[通過] 膠囊列車標記：短號、方向、定位與配色");
    }

    private static (Canvas Canvas, TopologyProjectDocument Sample) DrawSample(
        MainWindow main, string root, string fileName, double width, double seconds)
    {
        var sample = TopologyProjectFormat.Deserialize(File.ReadAllText(System.IO.Path.Combine(root, "samples", fileName)));
        var runtime = TopologyProjectFormat.CreateRuntime(sample);
        var world = new SimulationWorldOptions(null, runtime.TrainParameters, runtime.OperationalParameters,
            runtime.DispatchPlan.Runs.Count, sample.Simulation.HeadwaySeconds,
            ProfileMode: sample.Simulation.ProfileMode, MovingBlockMode: sample.Simulation.MovingBlockMode,
            ServicePatterns: runtime.ServicePatterns, DispatchPlan: runtime.DispatchPlan, VehicleTypes: runtime.VehicleTypes,
            ServiceTypes: runtime.ServiceTypes, Topology: runtime.Topology).CreateWorld();
        if (seconds > 0) world.AdvanceTo(seconds);
        ((MenuItem)main.FindName("ShowLockedRoutesMenuItem")).IsChecked = false;
        var canvas = (Canvas)main.FindName("RouteCanvas");
        canvas.Children.Clear();
        canvas.Width = width;
        canvas.Height = 400;
        var snapshot = world.GetSnapshot();
        var centers = snapshot.Trains
            .Select(state => (state.VehicleId, Center: world.GetTrainCenterPosition(state.VehicleId)))
            .Where(item => item.Center is not null)
            .ToDictionary(item => item.VehicleId, item => item.Center!.Value, StringComparer.OrdinalIgnoreCase);
        typeof(MainWindow).GetMethod("DrawTopologyGraphRoute", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(main, [runtime.Topology.Infrastructure, sample, snapshot, width, 400d, centers, world.GetActiveRouteLocks()]);
        return (canvas, sample);
    }
```

- [ ] **Step 2: 執行測試，確認失敗**

執行共用指令。
Expected: 丟出 `找不到 App 型別 TrainMarkerPresentation。`。

- [ ] **Step 3: 實作 TrainMarkerPresentation**

建立 `src/MrtRouteSimulator.App/TrainMarkerPresentation.cs`：

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

/// <summary>配線圖列車膠囊的純呈現規則；位置與方向一律由 Engine 提供的位置推得。</summary>
internal static class TrainMarkerPresentation
{
    public const double MarkerHeight = 18;
    public const double MinimumWidth = 34;
    private const int MaximumLabelLength = 10;
    private static readonly char[] Separators = ['-', '_', ' '];
    private static readonly Geometry RightChevron = Frozen("M0,0 L3.5,3.5 L0,7");
    private static readonly Geometry LeftChevron = Frozen("M3.5,0 L0,3.5 L3.5,7");

    public static string Label(string vehicleId)
    {
        if (vehicleId.StartsWith("Vehicle ", StringComparison.Ordinal)
            && int.TryParse(vehicleId.AsSpan(8), out var legacyNumber))
        {
            return $"V{legacyNumber:00}";
        }

        if (vehicleId.StartsWith("AUTO-", StringComparison.OrdinalIgnoreCase))
        {
            return "A" + vehicleId[5..].TrimStart('0').PadLeft(2, '0');
        }

        if (vehicleId.Length <= MaximumLabelLength) return vehicleId;
        var tokens = vehicleId.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        var label = tokens.Length <= 1
            ? vehicleId
            : string.Concat(tokens[..^1].Select(token => char.ToUpperInvariant(token[0]))) + "-" + tokens[^1];
        return label.Length <= MaximumLabelLength ? label : label[..(MaximumLabelLength - 1)] + "…";
    }

    public static int Heading(Point? front, Point center, TrainDirection direction)
    {
        if (front is { } head && Math.Abs(head.X - center.X) >= .5) return head.X > center.X ? 1 : -1;
        return direction == TrainDirection.Outbound ? 1 : -1;
    }

    public static Border Create(string vehicleId, Brush fill, int heading, string toolTip)
    {
        var text = new TextBlock
        {
            Text = Label(vehicleId),
            Foreground = Brushes.White,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var chevron = new System.Windows.Shapes.Path
        {
            Data = heading > 0 ? RightChevron : LeftChevron,
            Stroke = Brushes.White,
            StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = heading > 0 ? new Thickness(3, 0, 0, 0) : new Thickness(0, 0, 3, 0)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = heading > 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto
        });
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = heading > 0 ? GridLength.Auto : new GridLength(1, GridUnitType.Star)
        });
        Grid.SetColumn(text, heading > 0 ? 0 : 1);
        Grid.SetColumn(chevron, heading > 0 ? 1 : 0);
        grid.Children.Add(text);
        grid.Children.Add(chevron);
        return new Border
        {
            Width = Math.Max(MinimumWidth, Math.Ceiling(text.DesiredSize.Width) + 24),
            Height = MarkerHeight,
            CornerRadius = new CornerRadius(MarkerHeight / 2),
            Background = fill,
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(6, 0, 6, 0),
            Child = grid,
            ToolTip = toolTip
        };
    }

    // 以列車中心置中；只在畫布邊界 clamp，不改變停站時與月台中心的對位。
    public static void Place(FrameworkElement marker, Point center, double canvasWidth, double canvasHeight)
    {
        Canvas.SetLeft(marker, Math.Clamp(center.X - marker.Width / 2, 0, Math.Max(0, canvasWidth - marker.Width)));
        Canvas.SetTop(marker, Math.Clamp(center.Y - marker.Height / 2, 0, Math.Max(0, canvasHeight - marker.Height)));
        Panel.SetZIndex(marker, 5);
    }

    private static Geometry Frozen(string data)
    {
        var geometry = Geometry.Parse(data);
        if (!geometry.IsFrozen) geometry.Freeze();
        return geometry;
    }
}
```

在 `MainWindow.V2.cs` 把 `VehicleMarkerLabel` 整個方法替換為：

```csharp
    private static string VehicleMarkerLabel(string vehicleId) => TrainMarkerPresentation.Label(vehicleId);
```

在 `DrawTopologyTrainMarkers` 中，從 `var point = geometry.PointAt(ratio);` 之後到 `KeepFollowedRouteVehicleInView(...)` 之前（也就是原本 `var index = ...`、`var marker = new Border {...}`、`AttachTrainMarkerNavigation`、兩個 `Canvas.Set*`、`RouteCanvas.Children.Add(marker)`）整段替換為：

```csharp
            Point? front = null;
            if (state.TrackEdgeId is { } frontEdgeId && state.OffsetMeters is { } frontOffset
                && edgeGeometries.TryGetValue(frontEdgeId, out var frontGeometry)
                && infrastructure.TryGetEdge(frontEdgeId, out var frontEdge) && frontEdge.LengthMeters > 0)
            {
                front = frontGeometry.PointAt(Math.Clamp(frontOffset / frontEdge.LengthMeters, 0, 1));
            }

            var fill = state.Phase is OperationalPhase.Collided or OperationalPhase.EmergencyStopped
                ? UiTheme.DangerBrush
                : UiTheme.VehicleBrush(ParseVehicleIndex(state.VehicleId));
            var marker = TrainMarkerPresentation.Create(
                state.VehicleId,
                fill,
                TrainMarkerPresentation.Heading(front, point, state.Direction),
                $"{state.VehicleId}｜{state.ServiceRunId}\n車體中心 {stationChainage?.ToChainage(center) / 1000:0.000}K\n車頭 {state.TrackEdgeId}，偏移 {state.OffsetMeters:0.#} m\n{PhaseToChinese(state.Phase)}｜{state.SpeedMetersPerSecond * 3.6:0.#} km/h\n點選查看完整行程速度曲線");
            AttachTrainMarkerNavigation(marker, state.VehicleId);
            TrainMarkerPresentation.Place(marker, point, width, height);
            RouteCanvas.Children.Add(marker);
```

（提示文字與原本完全相同；`ratio`、`offset` 等原本的變數保留不動。）

- [ ] **Step 4: 執行測試，確認通過**

執行共用指令。
Expected: `[通過] 膠囊列車標記：短號、方向、定位與配色`，結束碼 0；建置 0 warning。

- [ ] **Step 5: Commit**

```powershell
git add src/MrtRouteSimulator.App/TrainMarkerPresentation.cs src/MrtRouteSimulator.App/MainWindow.V2.cs tests/MrtRouteSimulator.WpfTests/TrackDiagramThemeTests.cs
git commit -m "feat: draw capsule train markers with readable labels and heading" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: 雙色軌道、白色方向箭頭、接軌與止衝擋

**Files:**
- Create: `src/MrtRouteSimulator.App/TrackRailStyle.cs`
- Modify: `src/MrtRouteSimulator.App/MainWindow.V2.cs`（`DrawTopologyGraphRoute`：`railColor` 宣告、接軌迴圈、軌道迴圈、止衝擋迴圈）
- Modify: `src/MrtRouteSimulator.App/MainWindow.xaml.cs`（建構子設定畫布底色）
- Test: `tests/MrtRouteSimulator.WpfTests/TrackDiagramThemeTests.cs`

**Interfaces:**
- Consumes: `UiTheme.Rail*Brush`、`UiTheme.CanvasBackgroundBrush`
- Produces:
  - `internal enum RailTone { Neutral, Down, Up, DownSoft, UpSoft }`
  - `TrackRailStyle.MainlineThickness = 6`、`TrackRailStyle.SideThickness = 4`
  - `IReadOnlyDictionary<string, RailTone> TrackRailStyle.Classify(IEnumerable<TrackEdgeDefinition>, IReadOnlySet<string> outboundEdgeIds, IReadOnlySet<string> inboundEdgeIds)`
  - `SolidColorBrush TrackRailStyle.Brush(RailTone)`
  - `double TrackRailStyle.Thickness(TrackEdgeDefinition)`
  - 軌道迴圈中每條 edge 的 `PointCollection points`（已凍結；Task 8 會存進快取）

- [ ] **Step 1: 寫失敗測試**

在 `Run` 加入 `VerifyRailStyles(root);`，並加入：

```csharp
    private static void VerifyRailStyles(string root)
    {
        var theme = AppType("UiTheme");
        Color ThemeColor(string name) => (Color)StaticValue(theme, name)!;
        var main = new MainWindow();
        try
        {
            var routeCanvas = (Canvas)main.FindName("RouteCanvas");
            Require(ReferenceEquals(routeCanvas.Background, StaticValue(theme, "CanvasBackgroundBrush")),
                "配線圖底色必須取自 UiTheme.CanvasBackgroundBrush。");
            var (canvas, _) = DrawSample(main, root, "10-小型-三站完整拓樸基準範例.mrtsim.json", 1200, 0);
            var rails = canvas.Children.OfType<Polyline>()
                .Where(line => line.ToolTip is string tip && tip.Contains('\n'))
                .ToDictionary(line => ((string)line.ToolTip!).Split('\n', 2)[0], StringComparer.OrdinalIgnoreCase);
            var expected = new (string EdgeId, string Color, double Thickness)[]
            {
                ("DOWN-W-M", "RailDown", 6), ("DOWN-M-LOCAL", "RailDown", 6), ("DOWN-M-E", "RailDown", 6),
                ("UP-E-M", "RailUp", 6), ("UP-M-W", "RailUp", 6),
                ("PASS-LOOP-M", "RailDownSoft", 4),
                ("TAIL-OUT", "RailNeutral", 4), ("TAIL-W-OUT", "RailNeutral", 4), ("POCKET-OUT", "RailNeutral", 4),
                ("TAIL-OUT:ENTRY", "RailNeutral", 4), ("POCKET-OUT:EXIT", "RailNeutral", 4)
            };
            foreach (var (edgeId, colorName, thickness) in expected)
            {
                Require(rails.TryGetValue(edgeId, out var rail), $"缺少軌道 {edgeId}。");
                Require(rail!.Stroke is SolidColorBrush brush && brush.Color == ThemeColor(colorName) && rail.StrokeThickness == thickness,
                    $"{edgeId} 應為 {colorName}／{thickness}px，實際 {(rail.Stroke as SolidColorBrush)?.Color}／{rail.StrokeThickness}px。");
                Require(rail.Points.IsFrozen, $"{edgeId}：軌道點集合必須凍結以便亮燈圖層共用。");
            }

            var chevrons = canvas.Children.OfType<Polygon>().ToArray();
            Require(chevrons.Length > 0 && chevrons.All(arrow => arrow.Fill is SolidColorBrush fill && fill.Color == Colors.White),
                "單向軌道的方向箭頭必須是畫在軌道上的白色三角形。");
            var bufferStops = canvas.Children.OfType<Line>().Where(line => line.ToolTip is string tip && tip.Contains("止衝")).ToArray();
            Require(bufferStops.Length > 0 && bufferStops.All(line => line.Stroke is SolidColorBrush stroke
                    && stroke.Color == ThemeColor("RailNeutralStrong") && line.StrokeThickness == 4),
                "止衝擋必須使用 RailNeutralStrong、4px。");
            var allowed = new[] { "RailDown", "RailUp", "RailDownSoft", "RailUpSoft", "RailNeutral" }.Select(ThemeColor).ToArray();
            var connectors = canvas.Children.OfType<Polyline>()
                .Where(line => line.ToolTip is string tip && tip.StartsWith("合法轉向", StringComparison.Ordinal)).ToArray();
            Require(connectors.All(line => line.Stroke is SolidColorBrush stroke && allowed.Contains(stroke.Color)),
                "接軌曲線顏色必須取自軌道色票。");
        }
        finally { main.Close(); }
        Console.WriteLine("[通過] 雙色軌道、白色方向箭頭、接軌與止衝擋");
    }
```

- [ ] **Step 2: 執行測試，確認失敗**

執行共用指令。
Expected: 失敗於 `配線圖底色必須取自 UiTheme.CanvasBackgroundBrush。`。

- [ ] **Step 3: 實作**

建立 `src/MrtRouteSimulator.App/TrackRailStyle.cs`：

```csharp
using System.Windows.Media;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

internal enum RailTone
{
    Neutral,
    Down,
    Up,
    DownSoft,
    UpSoft
}

/// <summary>配線圖軌道配色與粗細規則；只依服務路徑與 edge 種類決定外觀，不影響幾何。</summary>
internal static class TrackRailStyle
{
    public const double MainlineThickness = 6;
    public const double SideThickness = 4;

    public static IReadOnlyDictionary<string, RailTone> Classify(
        IEnumerable<TrackEdgeDefinition> edges,
        IReadOnlySet<string> outboundEdgeIds,
        IReadOnlySet<string> inboundEdgeIds)
    {
        var all = edges.ToArray();
        RailTone RouteTone(string edgeId)
        {
            var outbound = outboundEdgeIds.Contains(edgeId);
            var inbound = inboundEdgeIds.Contains(edgeId);
            return outbound && !inbound ? RailTone.Down : inbound && !outbound ? RailTone.Up : RailTone.Neutral;
        }

        static bool SharesNode(TrackEdgeDefinition first, TrackEdgeDefinition second) =>
            first.FromNodeId.Equals(second.FromNodeId, StringComparison.OrdinalIgnoreCase)
            || first.FromNodeId.Equals(second.ToNodeId, StringComparison.OrdinalIgnoreCase)
            || first.ToNodeId.Equals(second.FromNodeId, StringComparison.OrdinalIgnoreCase)
            || first.ToNodeId.Equals(second.ToNodeId, StringComparison.OrdinalIgnoreCase);

        var result = new Dictionary<string, RailTone>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in all)
        {
            var tone = RouteTone(edge.TrackEdgeId);
            if (tone == RailTone.Neutral && edge.Kind is TrackEdgeKind.PassingTrack or TrackEdgeKind.Siding)
            {
                // 側線不在任何方向路徑內時，跟隨共用端點的單一方向正線，以淺色呈現。
                var neighbourTones = all
                    .Where(other => !other.TrackEdgeId.Equals(edge.TrackEdgeId, StringComparison.OrdinalIgnoreCase)
                        && SharesNode(edge, other))
                    .Select(other => RouteTone(other.TrackEdgeId))
                    .Where(other => other != RailTone.Neutral)
                    .Distinct()
                    .ToArray();
                if (neighbourTones.Length == 1)
                {
                    tone = neighbourTones[0] == RailTone.Down ? RailTone.DownSoft : RailTone.UpSoft;
                }
            }

            result[edge.TrackEdgeId] = tone;
        }

        return result;
    }

    public static SolidColorBrush Brush(RailTone tone) => tone switch
    {
        RailTone.Down => UiTheme.RailDownBrush,
        RailTone.Up => UiTheme.RailUpBrush,
        RailTone.DownSoft => UiTheme.RailDownSoftBrush,
        RailTone.UpSoft => UiTheme.RailUpSoftBrush,
        _ => UiTheme.RailNeutralBrush
    };

    public static double Thickness(TrackEdgeDefinition edge) =>
        edge.Kind == TrackEdgeKind.Mainline ? MainlineThickness : SideThickness;
}
```

在 `MainWindow.xaml.cs` 建構子中 `InitializeComponent();` 的下一行加入：

```csharp
        RouteCanvas.Background = UiTheme.CanvasBackgroundBrush;
```

在 `MainWindow.V2.cs` 的 `DrawTopologyGraphRoute`：

1. 把 `var railColor = Color.FromRgb(25, 96, 125);` 這一行替換為（`railColor` 之後不再使用，必須刪除，否則會觸發 warning-as-error）：

```csharp
        var railTones = TrackRailStyle.Classify(infrastructure.Edges.Values, outboundEdgeIds, inboundEdgeIds);
```

2. 在接軌迴圈中，把 `StationSchematicPresentation.DrawConnection(RouteCanvas, from, to, incoming, outgoing, new SolidColorBrush(railColor), ...` 這個呼叫替換為：

```csharp
            var fromTone = railTones.GetValueOrDefault(connection.FromTrackEdgeId, RailTone.Neutral);
            var toTone = railTones.GetValueOrDefault(connection.ToTrackEdgeId, RailTone.Neutral);
            StationSchematicPresentation.DrawConnection(RouteCanvas, from, to, incoming, outgoing,
                TrackRailStyle.Brush(fromTone == toTone ? fromTone : RailTone.Neutral),
                $"合法轉向：{connection.FromTrackEdgeId} ({connection.FromDirection}) → {connection.ToTrackEdgeId} ({connection.ToDirection})",
                allowLaneTurn,
                fromEdge is null ? TrackRailStyle.SideThickness : TrackRailStyle.Thickness(fromEdge));
```

3. 把軌道迴圈 `foreach (var edge in infrastructure.Edges.Values.OrderBy(...))` 的整個迴圈主體替換為：

```csharp
        {
            var geometry = edgeGeometries[edge.TrackEdgeId];
            var points = new PointCollection(geometry.Points);
            points.Freeze();
            var thickness = TrackRailStyle.Thickness(edge);
            var emphasizeSideTrack = edgeGeometries.Count >= 32
                && edge.Kind is TrackEdgeKind.PassingTrack or TrackEdgeKind.Siding;
            if (emphasizeSideTrack)
            {
                // 大型總覽的短側線轉折刻意壓平；保留白色襯底讓它與正線分得開。
                RouteCanvas.Children.Add(new Polyline
                {
                    Points = points,
                    Stroke = Brushes.White,
                    StrokeThickness = thickness + 3,
                    StrokeLineJoin = PenLineJoin.Round,
                    IsHitTestVisible = false
                });
            }
            RouteCanvas.Children.Add(new Polyline
            {
                Points = points,
                Stroke = TrackRailStyle.Brush(railTones.GetValueOrDefault(edge.TrackEdgeId, RailTone.Neutral)),
                StrokeThickness = thickness,
                StrokeLineJoin = PenLineJoin.Round,
                ToolTip = $"{edge.TrackEdgeId}\n{UiDisplayText.Enum(edge.Kind)} · {edge.LengthMeters:0.#} m · 預設 {edge.DefaultSpeedLimitMetersPerSecond * 3.6:0.#} km/h"
            });
            if (edge.Directionality != TrackDirectionality.Bidirectional)
            {
                var arrowCenter = geometry.PointAt(0.38);
                var tangent = geometry.PointAt(0.40) - geometry.PointAt(0.36);
                if (edge.Directionality == TrackDirectionality.ReverseOnly) tangent = -tangent;
                if (tangent.Length > 0.01)
                {
                    tangent.Normalize();
                    var normal = new Vector(-tangent.Y, tangent.X);
                    var size = thickness * .55;
                    RouteCanvas.Children.Add(new Polygon
                    {
                        Points = new PointCollection
                        {
                            arrowCenter + tangent * size * 1.2,
                            arrowCenter - tangent * size * .8 + normal * size,
                            arrowCenter - tangent * size * .8 - normal * size
                        },
                        Fill = Brushes.White,
                        IsHitTestVisible = false
                    });
                }
            }
        }
```

4. 在止衝擋迴圈的 `new Line { ... }` 中，把 `Stroke = new SolidColorBrush(railColor), StrokeThickness = 5,` 替換為：

```csharp
                Stroke = UiTheme.RailNeutralStrongBrush,
                StrokeThickness = 4,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
```

（`ToolTip` 等其他屬性保留。）

- [ ] **Step 4: 執行測試，確認通過**

執行共用指令。
Expected: `[通過] 雙色軌道、白色方向箭頭、接軌與止衝擋`，結束碼 0；建置 0 warning。
若 `PASS-LOOP-M` 的顏色不是 `RailDownSoft`：先讀 `samples/10-小型-三站完整拓樸基準範例.mrtsim.json` 中 `PASS-LOOP-M` 的端點節點，確認它與哪些路徑 edge 共用節點，再判斷是 `Classify` 寫錯還是測試預期錯；不可直接改測試去配合輸出。

- [ ] **Step 5: Commit**

```powershell
git add src/MrtRouteSimulator.App/TrackRailStyle.cs src/MrtRouteSimulator.App/MainWindow.V2.cs src/MrtRouteSimulator.App/MainWindow.xaml.cs tests/MrtRouteSimulator.WpfTests/TrackDiagramThemeTests.cs
git commit -m "feat: color track diagram rails by running direction" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: 白色月台與彩色編號圓點

**Files:**
- Modify: `src/MrtRouteSimulator.App/StationSchematicPresentation.cs`（`DrawPlatforms` 約 977-1013 行）
- Test: `tests/MrtRouteSimulator.WpfTests/TrackDiagramThemeTests.cs`

**Interfaces:**
- Consumes: `UiTheme.PlatformFillBrush`、`UiTheme.HairlineBrush`、`UiTheme.RailDownBrush`、`UiTheme.RailUpBrush`、`UiTheme.PlatformBidirectionalBrush`
- Produces: 月台編號 `TextBlock`（`PlatformNumberAnchor`，ZIndex 3）置中在一個 `Ellipse` 圓點（ZIndex 2）之上；`DrawPlatforms` 的簽章與回傳值不變。

- [ ] **Step 1: 寫失敗測試**

在 `Run` 加入 `VerifyPlatforms(root);`，並加入：

```csharp
    private static void VerifyPlatforms(string root)
    {
        var theme = AppType("UiTheme");
        var platformFill = StaticValue(theme, "PlatformFillBrush");
        var main = new MainWindow();
        try
        {
            foreach (var (file, width) in new[]
            {
                ("10-小型-三站完整拓樸基準範例.mrtsim.json", 1200d),
                ("14-大型-二十八站完整營運範例.mrtsim.json", 1370d)
            })
            {
                var (canvas, sample) = DrawSample(main, root, file, width, 0);
                var bodies = canvas.Children.OfType<Rectangle>().Where(item => item.Tag?.GetType().Name == "PlatformBodyAnchor").ToArray();
                Require(bodies.Length > 0 && bodies.All(body => ReferenceEquals(body.Fill, platformFill)
                        && ReferenceEquals(body.Stroke, StaticValue(theme, "HairlineBrush")) && body.RadiusX == 3),
                    $"{file}：月台本體必須是白底、細框、圓角 3。");
                var badges = canvas.Children.OfType<Ellipse>().ToArray();
                var directions = sample.Topology.Platforms.ToDictionary(item => item.PlatformId, item => item.AllowedDirection,
                    StringComparer.OrdinalIgnoreCase);
                foreach (var number in canvas.Children.OfType<TextBlock>().Where(item => item.Tag?.GetType().Name == "PlatformNumberAnchor"))
                {
                    var platformId = (string)number.Tag.GetType().GetProperty("PlatformId")!.GetValue(number.Tag)!;
                    number.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    var textBox = new Rect(Canvas.GetLeft(number), Canvas.GetTop(number), number.DesiredSize.Width, number.DesiredSize.Height);
                    var textCenter = new Point(textBox.Left + textBox.Width / 2, textBox.Top + textBox.Height / 2);
                    var badge = badges.SingleOrDefault(item =>
                        new Rect(Canvas.GetLeft(item), Canvas.GetTop(item), item.Width, item.Height).Contains(textCenter));
                    Require(badge is not null, $"{file}/{platformId}：月台編號缺少對應的圓點。");
                    var toneName = directions[platformId] switch
                    {
                        TrackDirection.Outbound => "RailDownBrush",
                        TrackDirection.Inbound => "RailUpBrush",
                        _ => "PlatformBidirectionalBrush"
                    };
                    var tone = StaticValue(theme, toneName);
                    var hollow = ReferenceEquals(badge!.Fill, platformFill);
                    Require(number.Background is null, $"{file}/{platformId}：編號不可再有白底方塊。");
                    Require(hollow
                            ? ReferenceEquals(badge.Stroke, tone) && ReferenceEquals(number.Foreground, tone)
                            : ReferenceEquals(badge.Fill, tone) && number.Foreground is SolidColorBrush white && white.Color == Colors.White,
                        $"{file}/{platformId}：編號圓點顏色必須對應行車方向（{toneName}）。");
                    var badgeBox = new Rect(Canvas.GetLeft(badge), Canvas.GetTop(badge), badge.Width, badge.Height);
                    Require(badgeBox.Contains(textBox), $"{file}/{platformId}：編號文字必須完整落在圓點內。");
                    Require(Panel.GetZIndex(badge) == 2 && Panel.GetZIndex(number) == 3, $"{file}/{platformId}：圓點與編號圖層順序錯誤。");
                }
                var warnings = (IReadOnlyList<string>)CallStatic(AppType("StationSchematicPresentation"), "ValidateStationLabels", canvas)!;
                Require(warnings.Count == 0, $"{file}：{string.Join("；", warnings)}");
            }
        }
        finally { main.Close(); }
        Console.WriteLine("[通過] 白色月台與方向色編號圓點");
    }
```

- [ ] **Step 2: 執行測試，確認失敗**

執行共用指令。
Expected: 失敗於 `月台本體必須是白底、細框、圓角 3。`。

- [ ] **Step 3: 實作**

在 `StationSchematicPresentation.DrawPlatforms` 中，把從 `var markerOnly = group.All(...)` 到「編號 `foreach (var face in group)` 迴圈結束」的整段替換為：

```csharp
            var markerOnly = group.All(f => throughMarkerIds.Contains(f.Platform.PlatformId));
            var body = new Rectangle { Width = rect.Width, Height = rect.Height,
                Tag = new PlatformBodyAnchor(group.Key.StationId, group.Key.Body),
                Fill = UiTheme.PlatformFillBrush,
                Stroke = UiTheme.HairlineBrush,
                StrokeThickness = 1,
                RadiusX = 3,
                RadiusY = 3,
                Opacity = markerOnly ? 0 : 1,
                IsHitTestVisible = !markerOnly,
                ToolTip = string.Join("\n", group.Select(f => $"{f.Platform.Name} · {f.Platform.TrackEdgeId}")) };
            Panel.SetZIndex(body, 1);
            Canvas.SetLeft(body, rect.Left); Canvas.SetTop(body, rect.Top); canvas.Children.Add(body);
            foreach (var face in group)
            {
                var number = string.IsNullOrWhiteSpace(face.Platform.PlatformNumber)
                    ? face.Platform.AllowedDirection == TrackDirection.Outbound ? "1" : face.Platform.AllowedDirection == TrackDirection.Inbound ? "2" : "•"
                    : face.Platform.PlatformNumber;
                var tone = PlatformNumberBrush(face.Platform.AllowedDirection);
                // 非載客的通過正線只是營運標記，以空心圓點與載客月台區分。
                var hollow = throughMarkerIds.Contains(face.Platform.PlatformId);
                var label = new TextBlock { Text = number, FontSize = 10, FontWeight = FontWeights.SemiBold,
                    Foreground = hollow ? tone : Brushes.White,
                    Tag = new PlatformNumberAnchor(face.Platform.StationId, face.Platform.PlatformId),
                    IsHitTestVisible = false };
                Panel.SetZIndex(label, 3);
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var diameter = Math.Max(Math.Max(13, Math.Ceiling(label.DesiredSize.Height)), Math.Ceiling(label.DesiredSize.Width) + 4);
                var centerX = face.Box.Left + face.Box.Width / 2;
                var centerY = face.Box.Top + face.Box.Height / 2;
                var step = diameter + 2;
                var direction = centerY <= middle ? -1 : 1;
                var badgeBounds = new Rect(centerX - diameter / 2, centerY - diameter / 2, diameter, diameter);
                // 密集總覽中相鄰月台中心可能只差幾個像素；保留每個編號，但錯開圓點。
                for (var attempt = 0; placedPlatformNumberBounds.Any(previous => previous.IntersectsWith(badgeBounds)); attempt++)
                {
                    var offset = (attempt / 2 + 1) * step;
                    badgeBounds.Y = centerY - diameter / 2 + (attempt % 2 == 0 ? direction : -direction) * offset;
                }
                placedPlatformNumberBounds.Add(badgeBounds);
                var badge = new Ellipse { Width = diameter, Height = diameter,
                    Fill = hollow ? UiTheme.PlatformFillBrush : tone,
                    Stroke = hollow ? tone : null,
                    StrokeThickness = hollow ? 1.2 : 0,
                    IsHitTestVisible = false };
                Panel.SetZIndex(badge, 2);
                Canvas.SetLeft(badge, badgeBounds.Left); Canvas.SetTop(badge, badgeBounds.Top); canvas.Children.Add(badge);
                Canvas.SetLeft(label, badgeBounds.Left + (diameter - label.DesiredSize.Width) / 2);
                Canvas.SetTop(label, badgeBounds.Top + (diameter - label.DesiredSize.Height) / 2);
                canvas.Children.Add(label);
            }
```

並在 `DrawPlatforms` 方法之後（類別內）加入：

```csharp
    private static SolidColorBrush PlatformNumberBrush(TrackDirection direction) => direction switch
    {
        TrackDirection.Outbound => UiTheme.RailDownBrush,
        TrackDirection.Inbound => UiTheme.RailUpBrush,
        _ => UiTheme.PlatformBidirectionalBrush
    };
```

- [ ] **Step 4: 執行測試，確認通過**

執行共用指令。
Expected: `[通過] 白色月台與方向色編號圓點`，結束碼 0；建置 0 warning。

- [ ] **Step 5: Commit**

```powershell
git add src/MrtRouteSimulator.App/StationSchematicPresentation.cs tests/MrtRouteSimulator.WpfTests/TrackDiagramThemeTests.cs
git commit -m "feat: restyle platforms as white bars with direction-colored numbers" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: 站名徽章與 ID 去重

**Files:**
- Modify: `src/MrtRouteSimulator.App/StationSchematicPresentation.cs`（`DrawStationNames` 約 475-526 行；新增 `StationLabelLines`、`CreateStationLabel`）
- Modify: `src/MrtRouteSimulator.App/MainWindow.V2.cs`（`DrawStationNames` 呼叫端，約 1254 行）
- Modify: `src/MrtRouteSimulator.App/TopologyEditorWindow.cs`（`DrawStationNames` 呼叫端，約 2211 行）
- Test: `tests/MrtRouteSimulator.WpfTests/TrackDiagramThemeTests.cs`

**Interfaces:**
- Consumes: `UiTheme.StationBadgeFillBrush`、`UiTheme.TextStrongBrush`、`UiTheme.TextMutedBrush`、`UiTheme.HairlineBrush`
- Produces:
  - `public static (string Primary, string Secondary) StationSchematicPresentation.StationLabelLines(string id, string name, double? chainageMeters)`
  - `public static void DrawStationNames(Canvas canvas, IEnumerable<(string Id, string Name, double? ChainageMeters)> stations, double width)`（取代舊的 `(string Id, string Name)` 版本）
  - 站名元素為 `Border`（`Tag = StationLabelAnchor`，`Child` 為 `TextBlock`）

- [ ] **Step 1: 寫失敗測試**

在 `Run` 加入 `VerifyStationLabels(root);`，並加入：

```csharp
    private static void VerifyStationLabels(string root)
    {
        var presentation = AppType("StationSchematicPresentation");
        (string Primary, string Secondary) Lines(string id, string name, double? meters) =>
            ((string, string))CallStatic(presentation, "StationLabelLines", id, name, meters)!;
        Require(Lines("O03", "O03 站", 2197) == ("O03 站", "2.197K"), "站名已含 ID 時不可重複顯示 ID。");
        Require(Lines("BL12", "台北車站", 1234) == ("台北車站", "BL12 · 1.234K"), "站名不含 ID 時次要資訊必須帶 ID 與里程。");
        Require(Lines("W", "西站", null) == ("西站", "W"), "沒有里程時次要資訊只有 ID。");
        Require(Lines("X", "  ", null) == ("X", ""), "空白站名以 ID 為主要名稱。");
        Require(Lines("o03", "O03 站", null) == ("O03 站", ""), "ID 比對不分大小寫。");

        var badgeFill = StaticValue(AppType("UiTheme"), "StationBadgeFillBrush");
        var main = new MainWindow();
        try
        {
            var (small, _) = DrawSample(main, root, "10-小型-三站完整拓樸基準範例.mrtsim.json", 1200, 0);
            var labels = small.Children.OfType<FrameworkElement>().Where(item => item.Tag?.GetType().Name == "StationLabelAnchor").ToArray();
            Require(labels.Length == 3 && labels.All(item => item is Border border
                    && ReferenceEquals(border.Background, badgeFill) && border.Child is TextBlock),
                "範例 10 的三個站名都必須是淺灰圓角徽章。");
            var west = labels.Cast<Border>().Select(border => ((TextBlock)border.Child).Text).Single(text => text.Contains("西站"));
            Require(west.Contains("W · "), $"西站徽章的次要資訊必須帶 ID 與里程，實際：{west}");

            var (large, _) = DrawSample(main, root, "14-大型-二十八站完整營運範例.mrtsim.json", 1370, 0);
            foreach (var label in large.Children.OfType<Border>().Where(item => item.Tag?.GetType().Name == "StationLabelAnchor"))
            {
                var stationId = (string)label.Tag.GetType().GetProperty("StationId")!.GetValue(label.Tag)!;
                var text = ((TextBlock)label.Child).Text;
                Require(text.Split(stationId).Length - 1 == 1, $"{stationId}：站名徽章不可重複顯示 ID（{text}）。");
            }
            var warnings = (IReadOnlyList<string>)CallStatic(presentation, "ValidateStationLabels", large)!;
            Require(warnings.Count == 0, $"範例 14：{string.Join("；", warnings)}");
        }
        finally { main.Close(); }
        Console.WriteLine("[通過] 站名徽章與 ID 去重");
    }
```

- [ ] **Step 2: 執行測試，確認失敗**

執行共用指令。
Expected: 丟出 `StationSchematicPresentation 找不到靜態方法 StationLabelLines。`。

- [ ] **Step 3: 實作**

在 `StationSchematicPresentation.cs` 把整個 `DrawStationNames` 方法替換為：

```csharp
    public static void DrawStationNames(Canvas canvas,
        IEnumerable<(string Id, string Name, double? ChainageMeters)> stations, double width)
    {
        var bodies = canvas.Children.OfType<Rectangle>().Where(r => r.Tag is PlatformBodyAnchor).ToArray();
        if (bodies.Length == 0) return;
        var upper = Math.Max(canvas.Height < 380 ? 66 : 36, bodies.Min(r => Canvas.GetTop(r)) - 60);
        var lower = bodies.Max(r => Canvas.GetTop(r) + r.Height) + 32;
        var occupied = new List<Rect>();
        var stationIndex = 0;
        foreach (var station in stations)
        {
            var own = bodies.Where(r => ((PlatformBodyAnchor)r.Tag).StationId == station.Id).ToArray();
            if (own.Length == 0) continue;

            // 一座車站只畫一個主標籤。大型越行站的停靠股與通過股在圖上
            // 可能有不同的 X 座標；若各自畫完整站名，會把同一站重複標成兩次。
            // 月臺號碼仍留在每條股道，站名則錨定於全部月臺本體的視覺中心。
            var stationBounds = new Rect(Canvas.GetLeft(own[0]), Canvas.GetTop(own[0]), own[0].Width, own[0].Height);
            foreach (var body in own.Skip(1))
            {
                stationBounds.Union(new Rect(Canvas.GetLeft(body), Canvas.GetTop(body), body.Width, body.Height));
            }

            var center = stationBounds.Left + stationBounds.Width / 2;
            // 在總覽圖中交錯上下放置，可讓相鄰站名保有可讀間距；若真的衝突，
            // 仍沿同側垂直避讓，且不會改寫月臺／軌道位置。
            var above = stationIndex++ % 2 == 0;
            var label = CreateStationLabel(station.Id, station.Name, station.ChainageMeters);
            PlaceStationLabel(label, station.Id, center, above ? upper : lower, width);
            label.Width = Math.Min(label.Width, 110);
            Canvas.SetLeft(label, center - label.Width / 2);
            label.Tag = new StationLabelAnchor(station.Id, center);
            label.Measure(new Size(label.Width, double.PositiveInfinity));
            var top = Canvas.GetTop(label);
            var box = new Rect(Canvas.GetLeft(label), top, label.Width, label.DesiredSize.Height);
            while (occupied.Any(r => r.IntersectsWith(box)))
            {
                top += (above ? -1 : 1) * (label.DesiredSize.Height + 8);
                if (top < 36) { above = false; top = lower; }
                box.Y = top;
            }
            Canvas.SetTop(label, top); occupied.Add(box);
            canvas.Children.Add(new Line { X1 = center, X2 = center,
                Y1 = above ? box.Bottom + 2 : box.Top - 2,
                Y2 = above ? stationBounds.Top - 3 : stationBounds.Bottom + 3,
                Stroke = UiTheme.HairlineBrush, StrokeThickness = 1, IsHitTestVisible = false });
            canvas.Children.Add(label);
            canvas.Height = Math.Max(canvas.Height, box.Bottom + 28);
        }
    }

    // 站名已含 ID（例如「O03 站」）時不再重複 ID；否則把 ID 移到次要資訊。
    public static (string Primary, string Secondary) StationLabelLines(string id, string name, double? chainageMeters)
    {
        var primary = string.IsNullOrWhiteSpace(name) ? id : name.Trim();
        var parts = new List<string>();
        if (!primary.Contains(id, StringComparison.OrdinalIgnoreCase)) parts.Add(id);
        if (chainageMeters is { } meters && double.IsFinite(meters)) parts.Add($"{meters / 1000:0.000}K");
        return (primary, string.Join(" · ", parts));
    }

    private static Border CreateStationLabel(string id, string name, double? chainageMeters)
    {
        var (primary, secondary) = StationLabelLines(id, name, chainageMeters);
        var text = new TextBlock { TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
            Foreground = UiTheme.TextStrongBrush };
        text.Inlines.Add(new System.Windows.Documents.Run(primary) { FontSize = 12, FontWeight = FontWeights.SemiBold });
        if (secondary.Length > 0)
        {
            text.Inlines.Add(new System.Windows.Documents.LineBreak());
            text.Inlines.Add(new System.Windows.Documents.Run(secondary) { FontSize = 11, Foreground = UiTheme.TextMutedBrush });
        }
        return new Border { Background = UiTheme.StationBadgeFillBrush, CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 3, 6, 3), Child = text, ToolTip = $"{id} · {name}" };
    }
```

在 `MainWindow.V2.cs` 把 `StationSchematicPresentation.DrawStationNames(RouteCanvas, ...)` 的整個呼叫（兩行）替換為：

```csharp
        StationSchematicPresentation.DrawStationNames(RouteCanvas, stationVisuals.Select(s => (s.Station.StationId, s.Station.Name,
            stationChainage?.StationCenters.TryGetValue(s.Station.StationId, out var km) == true ? km : (double?)null)), width);
```

在 `TopologyEditorWindow.cs` 把 `StationSchematicPresentation.DrawStationNames(canvas, ...)` 的整個呼叫（兩行）替換為：

```csharp
        StationSchematicPresentation.DrawStationNames(canvas, stationVisuals.Select(s => (s.Station.StationId, s.Station.Name,
            stationChainage?.StationCenters.TryGetValue(s.Station.StationId, out var km) == true ? km : (double?)null)), width);
```

- [ ] **Step 4: 執行測試，確認通過**

執行共用指令。
Expected: `[通過] 站名徽章與 ID 去重`，結束碼 0；建置 0 warning。

- [ ] **Step 5: Commit**

```powershell
git add src/MrtRouteSimulator.App/StationSchematicPresentation.cs src/MrtRouteSimulator.App/MainWindow.V2.cs src/MrtRouteSimulator.App/TopologyEditorWindow.cs tests/MrtRouteSimulator.WpfTests/TrackDiagramThemeTests.cs
git commit -m "feat: show station names as badges without duplicated ids" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: 區段占用資料進入 PlaybackFrame

**Files:**
- Create: `src/MrtRouteSimulator.App/TrackOccupancySnapshot.cs`
- Modify: `src/MrtRouteSimulator.App/SimulationPlaybackWorker.cs`（`PlaybackFrame` 約 77-93 行；`PublishFrame` 約 595-658 行）
- Test: `tests/MrtRouteSimulator.WpfTests/TrackDiagramThemeTests.cs`

**Interfaces:**
- Consumes: `SimulationWorld.TopologyOccupancy`（`IReadOnlyDictionary<string, TopologyMovementFootprint>`，key = VehicleId）、`TopologyMovementFootprint.OccupiedIntervals`（`IReadOnlyList<TrackOccupancyInterval>`）、`TrackPosition.DefaultToleranceMeters`
- Produces:
  - `internal static class TrackOccupancySnapshot`
  - `static readonly ImmutableDictionary<string, ImmutableArray<string>> Empty`
  - `ImmutableDictionary<string, ImmutableArray<string>> Build(IEnumerable<KeyValuePair<string, IReadOnlyList<TrackOccupancyInterval>>> intervalsByVehicle)`（key 不分大小寫；車號排序）
  - `PlaybackFrame.TrackEdgeOccupants`（新的位置參數，位於 `ActiveRouteLocks` 之後）

- [ ] **Step 1: 寫失敗測試**

在 `Run` 加入 `VerifyTrackOccupancySnapshot(root);`，並加入：

```csharp
    private static void VerifyTrackOccupancySnapshot(string root)
    {
        var snapshotType = AppType("TrackOccupancySnapshot");
        var input = new Dictionary<string, IReadOnlyList<TrackOccupancyInterval>>(StringComparer.OrdinalIgnoreCase)
        {
            ["T2"] = [new TrackOccupancyInterval("E1", 300, 420)],
            ["T1"] = [new TrackOccupancyInterval("E1", 10, 150), new TrackOccupancyInterval("E2", 0, 0)],
            ["T3"] = [new TrackOccupancyInterval("E3", 5, 95)]
        };
        var result = (ImmutableDictionary<string, ImmutableArray<string>>)CallStatic(snapshotType, "Build", input)!;
        Require(result.Count == 2 && result.ContainsKey("E1") && result.ContainsKey("e3"),
            "占用彙整必須以 edge 為 key，且不分大小寫。");
        Require(!result.ContainsKey("E2"), "只在節點邊界接觸（零長度區間）的區段不可點亮。");
        Require(result["E1"].SequenceEqual(["T1", "T2"]), "同一區段的兩列車必須合併成一筆並依車號排序。");
        Require(((ImmutableDictionary<string, ImmutableArray<string>>)StaticValue(snapshotType, "Empty")!).IsEmpty,
            "Empty 必須是空字典。");

        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(
            System.IO.Path.Combine(root, "samples", "10-小型-三站完整拓樸基準範例.mrtsim.json")));
        var window = new MainWindow();
        try
        {
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(window, "ConfigureTopologyProjectForPlaybackAsync", document, true));
            var worker = (SimulationPlaybackWorker)WpfTestWait.Field(window, "_playbackWorker")!;
            WpfTestWait.Wait(worker.Ready);
            ((DispatcherTimer)WpfTestWait.Field(window, "_playbackTimer")!).Stop();
            WpfTestWait.Wait(worker.AdvanceToSimulationTimeAsync(60));
            WpfTestWait.Invoke(window, "UpdateV2PlaybackView");
            var frame = WpfTestWait.LatestFrame(window);
            Require(frame.TrackEdgeOccupants.Count > 0, "運行中的 frame 必須帶有區段占用資料。");
            foreach (var train in frame.Trains.Where(item => item.IsActive
                         && item.Phase is not (OperationalPhase.Pending or OperationalPhase.OutOfService)
                         && frame.TrainCenterPositions.ContainsKey(item.VehicleId)))
            {
                var centerEdge = frame.TrainCenterPositions[train.VehicleId].TrackEdgeId;
                Require(frame.TrackEdgeOccupants.TryGetValue(centerEdge, out var occupants)
                        && occupants.Contains(train.VehicleId, StringComparer.OrdinalIgnoreCase),
                    $"{train.VehicleId}：車體中心所在區段 {centerEdge} 必須列為占用。");
            }
        }
        finally { WpfTestWait.Close(window); }
        Console.WriteLine("[通過] 區段占用彙整與 PlaybackFrame 傳遞");
    }
```

- [ ] **Step 2: 執行測試，確認失敗**

執行共用指令。
Expected: 建置失敗，錯誤為 `CS1061: 'PlaybackFrame' does not contain a definition for 'TrackEdgeOccupants'`。

- [ ] **Step 3: 實作**

建立 `src/MrtRouteSimulator.App/TrackOccupancySnapshot.cs`：

```csharp
using System.Collections.Immutable;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

/// <summary>
/// 把 Engine footprint 的占用區間整理成「edgeId → 車號」，供配線圖區段亮燈使用。
/// 只做彙整，不推算任何位置；只在節點邊界接觸的零長度區間不算占用。
/// </summary>
internal static class TrackOccupancySnapshot
{
    public static readonly ImmutableDictionary<string, ImmutableArray<string>> Empty =
        ImmutableDictionary.Create<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase);

    public static ImmutableDictionary<string, ImmutableArray<string>> Build(
        IEnumerable<KeyValuePair<string, IReadOnlyList<TrackOccupancyInterval>>> intervalsByVehicle)
    {
        var occupants = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (vehicleId, intervals) in intervalsByVehicle)
        {
            foreach (var interval in intervals)
            {
                if (interval.EndOffsetMeters - interval.StartOffsetMeters <= TrackPosition.DefaultToleranceMeters) continue;
                if (!occupants.TryGetValue(interval.TrackEdgeId, out var vehicles))
                {
                    vehicles = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                    occupants[interval.TrackEdgeId] = vehicles;
                }
                vehicles.Add(vehicleId);
            }
        }

        return occupants.ToImmutableDictionary(
            pair => pair.Key,
            pair => pair.Value.ToImmutableArray(),
            StringComparer.OrdinalIgnoreCase);
    }
}
```

在 `SimulationPlaybackWorker.cs` 的 `PlaybackFrame` record 中，`ImmutableArray<LockedRouteSegment> ActiveRouteLocks,` 之後加入一個參數：

```csharp
    ImmutableDictionary<string, ImmutableArray<string>> TrackEdgeOccupants,
```

在 `PublishFrame` 中，`var centers = ...` 那段 `foreach` 結束之後加入：

```csharp
        var trackEdgeOccupants = TrackOccupancySnapshot.Build(_world.TopologyOccupancy.Select(pair =>
            KeyValuePair.Create(pair.Key, pair.Value.OccupiedIntervals)));
```

並在 `new PlaybackFrame(...)` 的參數列中，`_world.GetActiveRouteLocks().ToImmutableArray(),` 之後加入：

```csharp
            trackEdgeOccupants,
```

- [ ] **Step 4: 執行測試，確認通過**

執行共用指令。
Expected: `[通過] 區段占用彙整與 PlaybackFrame 傳遞`，結束碼 0；建置 0 warning。
若 `frame.TrackEdgeOccupants.Count == 0`：先確認範例 10 在 60 秒時是否有運行中列車（檢查 `frame.Trains` 的 `Phase`），再決定是否改用較晚的時間點；不可移除這條檢查。

- [ ] **Step 5: Commit**

```powershell
git add src/MrtRouteSimulator.App/TrackOccupancySnapshot.cs src/MrtRouteSimulator.App/SimulationPlaybackWorker.cs tests/MrtRouteSimulator.WpfTests/TrackDiagramThemeTests.cs
git commit -m "feat: publish track section occupancy in playback frames" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: 「顯示區段占用亮燈」偏好與選單

**Files:**
- Modify: `src/MrtRouteSimulator.App/AppDisplayPreferences.cs`
- Modify: `src/MrtRouteSimulator.App/MainWindow.xaml`（約 76-81 行，`ShowLockedRoutesMenuItem` 之後）
- Modify: `src/MrtRouteSimulator.App/MainWindow.xaml.cs`（建構子）
- Modify: `src/MrtRouteSimulator.App/MainWindow.V2.cs`（新增 `TrackOccupancyVisibility_Changed`）
- Test: `tests/MrtRouteSimulator.WpfTests/TrackDiagramThemeTests.cs`

**Interfaces:**
- Produces:
  - `DisplaySettings.ShowTrackOccupancy { get; init; } = true`（不放進主建構子）
  - `AppDisplayPreferences.LoadShowTrackOccupancy()`、`AppDisplayPreferences.SaveShowTrackOccupancy(bool)`
  - `MenuItem ShowTrackOccupancyMenuItem`（XAML 具名元素，Task 8 讀取 `IsChecked`）
  - `MainWindow.TrackOccupancyVisibility_Changed(object, RoutedEventArgs)`

- [ ] **Step 1: 寫失敗測試**

在 `Run` 加入 `VerifyTrackOccupancyPreference();`，並加入：

```csharp
    private static void VerifyTrackOccupancyPreference()
    {
        var preferences = AppType("AppDisplayPreferences");
        var settingsType = preferences.GetNestedType("DisplaySettings", BindingFlags.NonPublic)!;
        var load = preferences.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method => method.Name == "Load" && method.GetParameters().Length == 1
                && method.GetParameters()[0].ParameterType == typeof(string));
        var save = preferences.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method => method.Name == "Save" && method.GetParameters().Length == 2);
        var constructor = settingsType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(item => item.GetParameters().Select(parameter => parameter.ParameterType)
                .SequenceEqual([typeof(bool), typeof(double), typeof(double)]));
        var occupancy = settingsType.GetProperty("ShowTrackOccupancy")!;
        object Load(string path) => load.Invoke(null, [path])!;
        bool Occupancy(object settings) => (bool)occupancy.GetValue(settings)!;
        bool Locks(object settings) => (bool)settingsType.GetProperty("ShowLockedRoutes")!.GetValue(settings)!;

        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mrt-track-occupancy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "display-settings.json");
        try
        {
            Require(Occupancy(Load(path)), "沒有偏好檔時區段占用亮燈預設開啟。");
            File.WriteAllText(path, "{\"ShowLockedRoutes\":true,\"RouteMapHorizontalZoom\":1.5}");
            var legacy = Load(path);
            Require(Occupancy(legacy) && Locks(legacy), "舊偏好檔缺少欄位時視為開啟，且保留其他偏好。");
            File.WriteAllText(path, "{\"ShowTrackOccupancy\":false,\"ShowLockedRoutes\":true}");
            var off = Load(path);
            Require(!Occupancy(off) && Locks(off), "明確關閉時必須讀成關閉。");
            foreach (var malformed in new[] { "\"false\"", "null", "0", "[]" })
            {
                File.WriteAllText(path, $"{{\"ShowTrackOccupancy\":{malformed},\"ShowLockedRoutes\":true}}");
                var settings = Load(path);
                Require(Occupancy(settings) && Locks(settings), $"格式錯誤的 ShowTrackOccupancy（{malformed}）必須視為開啟且保留其他偏好。");
            }
            var saved = constructor.Invoke([true, 1.5, 1.1]);
            occupancy.SetValue(saved, false);
            save.Invoke(null, [path, saved]);
            var reloaded = Load(path);
            Require(!Occupancy(reloaded) && Locks(reloaded), "關閉後存檔再讀必須維持關閉。");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }

        var main = new MainWindow();
        try
        {
            var item = main.FindName("ShowTrackOccupancyMenuItem") as MenuItem;
            var locks = (MenuItem)main.FindName("ShowLockedRoutesMenuItem");
            Require(item is not null && item.IsCheckable && Equals(item.Header, "顯示區段占用亮燈")
                    && System.Windows.Automation.AutomationProperties.GetName(item) == "顯示區段占用亮燈",
                "顯示設定選單必須提供可勾選的「顯示區段占用亮燈」。");
            var parent = (ItemsControl)locks.Parent;
            Require(parent.Items.IndexOf(item) == parent.Items.IndexOf(locks) + 1, "亮燈開關必須緊接在鎖定進路開關之後。");
        }
        finally { main.Close(); }
        Console.WriteLine("[通過] 區段占用亮燈偏好與選單");
    }
```

- [ ] **Step 2: 執行測試，確認失敗**

執行共用指令。
Expected: 失敗，`settingsType.GetProperty("ShowTrackOccupancy")` 為 null，丟出 `NullReferenceException`（或 `TargetInvocationException` 內含它）。

- [ ] **Step 3: 實作**

在 `AppDisplayPreferences.cs`：

1. 在 `LoadShowLockedRoutes` 旁加入：

```csharp
    public static bool LoadShowTrackOccupancy() => Load().ShowTrackOccupancy;

    public static void SaveShowTrackOccupancy(bool show)
    {
        var settings = Load();
        Save(settings with { ShowTrackOccupancy = show });
    }
```

2. 在 `Load(string path)` 中，把 `return new DisplaySettings(showLockedRoutes, routeMapHorizontalZoom, interfaceScale);` 替換為：

```csharp
            // 只有明確的 false 才關閉；缺欄位或格式錯誤一律維持預設開啟。
            var showTrackOccupancy = !document.RootElement.TryGetProperty("ShowTrackOccupancy", out var occupancyValue)
                || occupancyValue.ValueKind != JsonValueKind.False;
            return new DisplaySettings(showLockedRoutes, routeMapHorizontalZoom, interfaceScale)
            {
                ShowTrackOccupancy = showTrackOccupancy
            };
```

3. 把 `DisplaySettings` record 替換為：

```csharp
    internal sealed record DisplaySettings(
        bool ShowLockedRoutes = false,
        double RouteMapHorizontalZoom = DefaultRouteMapHorizontalZoom,
        double InterfaceScale = DefaultInterfaceScale)
    {
        // 不放進主建構子：既有偏好測試以 (bool, double, double) 建構子為契約。
        public bool ShowTrackOccupancy { get; init; } = true;
    }
```

在 `MainWindow.xaml` 的 `ShowLockedRoutesMenuItem` 元素（結尾 `/>`）之後加入：

```xml
                <MenuItem x:Name="ShowTrackOccupancyMenuItem"
                          Header="顯示區段占用亮燈"
                          IsCheckable="True"
                          Click="TrackOccupancyVisibility_Changed"
                          ToolTip="以黃色光帶標示列車目前占用的整條軌道區段；資料直接取自模擬核心。"
                          AutomationProperties.Name="顯示區段占用亮燈" />
```

在 `MainWindow.xaml.cs` 建構子的 `ShowLockedRoutesMenuItem.IsChecked = AppDisplayPreferences.LoadShowLockedRoutes();` 下一行加入：

```csharp
        ShowTrackOccupancyMenuItem.IsChecked = AppDisplayPreferences.LoadShowTrackOccupancy();
```

在 `MainWindow.V2.cs` 的 `RouteLockVisibility_Changed` 方法之後加入：

```csharp
    private void TrackOccupancyVisibility_Changed(object sender, RoutedEventArgs e)
    {
        try
        {
            AppDisplayPreferences.SaveShowTrackOccupancy(ShowTrackOccupancyMenuItem.IsChecked);
        }
        catch (IOException)
        {
            StatusTextBlock.Text = "已切換區段占用亮燈，但無法保存本機畫面設定。";
        }
        catch (UnauthorizedAccessException)
        {
            StatusTextBlock.Text = "已切換區段占用亮燈，但無法保存本機畫面設定。";
        }
        // 圖例屬於靜態快取圖層，切換後必須整張重建。
        _topologyRouteVisualCache = null;
        if (IsLoaded) DrawRoute();
    }
```

- [ ] **Step 4: 執行測試，確認通過**

執行共用指令；另外跑既有偏好測試，確認建構子契約沒被破壞：

```powershell
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --interface-scale-only
```

Expected: `[通過] 區段占用亮燈偏好與選單`；`--interface-scale-only` 印出 `PASS WPF interface scale preferences ...`；兩者結束碼 0。

- [ ] **Step 5: Commit**

```powershell
git add src/MrtRouteSimulator.App/AppDisplayPreferences.cs src/MrtRouteSimulator.App/MainWindow.xaml src/MrtRouteSimulator.App/MainWindow.xaml.cs src/MrtRouteSimulator.App/MainWindow.V2.cs tests/MrtRouteSimulator.WpfTests/TrackDiagramThemeTests.cs
git commit -m "feat: add persisted toggle for track section occupancy lights" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: 亮燈圖層、圖例與說明文字

**Files:**
- Modify: `src/MrtRouteSimulator.App/MainWindow.V2.cs`（`TopologyRouteVisualCache` record、`RouteLockVisibility_Changed`、`DrawV2Route`、`DrawTopologyGraphRoute`、新增 `DrawTopologyTrackOccupancy`、新增欄位）
- Modify: `src/MrtRouteSimulator.App/StationSchematicPresentation.cs`（`DrawLegend`、`DrawChainageReference`）
- Test: `tests/MrtRouteSimulator.WpfTests/TrackDiagramThemeTests.cs`

**Interfaces:**
- Consumes: `PlaybackFrame.TrackEdgeOccupants`、`TrackOccupancySnapshot.Empty`、`ShowTrackOccupancyMenuItem`、Task 3 的凍結 `points`、`UiTheme.OccupancyGlowBrush`、`UiTheme.OccupancyGlowThickness`、`UiTheme.LockedRouteBrushes`
- Produces:
  - 欄位 `ImmutableDictionary<string, ImmutableArray<string>> _routeTrackOccupants`
  - `TopologyRouteVisualCache.RailPoints`（`IReadOnlyDictionary<string, PointCollection>`）
  - 光帶 `Polyline`：`Tag = "TrackOccupancy:{edgeId}"`、`ZIndex = -1`、無 `ToolTip`、`IsHitTestVisible = false`、`Points` 與軌道共用同一個凍結 `PointCollection`
  - `StationSchematicPresentation.DrawLegend(Canvas canvas, bool showOccupancy = false, bool showLockedRoutes = false)`，圖例為 `StackPanel`（`Tag = "RouteLegend"`）

- [ ] **Step 1: 寫失敗測試**

在 `Run` 加入 `VerifyOccupancyGlowAndLegend(root);`，並加入：

```csharp
    private static void VerifyOccupancyGlowAndLegend(string root)
    {
        var glowBrush = StaticValue(AppType("UiTheme"), "OccupancyGlowBrush");
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(
            System.IO.Path.Combine(root, "samples", "10-小型-三站完整拓樸基準範例.mrtsim.json")));
        var window = new MainWindow();
        try
        {
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(window, "ConfigureTopologyProjectForPlaybackAsync", document, true));
            var worker = (SimulationPlaybackWorker)WpfTestWait.Field(window, "_playbackWorker")!;
            WpfTestWait.Wait(worker.Ready);
            ((DispatcherTimer)WpfTestWait.Field(window, "_playbackTimer")!).Stop();
            WpfTestWait.Wait(worker.AdvanceToSimulationTimeAsync(60));
            WpfTestWait.Invoke(window, "UpdateV2PlaybackView");
            var frame = WpfTestWait.LatestFrame(window);
            Require(frame.TrackEdgeOccupants.Count > 0, "測試前提：60 秒時必須有占用區段。");

            var occupancyItem = (MenuItem)window.FindName("ShowTrackOccupancyMenuItem");
            var locksItem = (MenuItem)window.FindName("ShowLockedRoutesMenuItem");
            var cacheField = typeof(MainWindow).GetField("_topologyRouteVisualCache", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var canvas = (Canvas)window.FindName("RouteCanvas");
            void Redraw(bool occupancy, bool locks)
            {
                occupancyItem.IsChecked = occupancy;
                locksItem.IsChecked = locks;
                cacheField.SetValue(window, null);
                canvas.Width = 1200;
                canvas.Height = 400;
                canvas.Measure(new Size(1200, 400));
                canvas.Arrange(new Rect(0, 0, 1200, 400));
                WpfTestWait.Invoke(window, "DrawV2Route");
            }
            Polyline[] Glows() => canvas.Children.OfType<Polyline>()
                .Where(line => line.Tag is string tag && tag.StartsWith("TrackOccupancy:", StringComparison.Ordinal)).ToArray();
            string[] LegendTexts() => canvas.Children.OfType<StackPanel>().Single(panel => Equals(panel.Tag, "RouteLegend"))
                .Children.OfType<TextBlock>().Select(text => text.Text).ToArray();

            Redraw(occupancy: true, locks: true);
            var glows = Glows();
            var glowEdges = glows.Select(line => ((string)line.Tag).Split(':', 2)[1]).ToArray();
            Require(glowEdges.Length == glowEdges.Distinct(StringComparer.OrdinalIgnoreCase).Count(), "每個占用區段只能有一條光帶。");
            Require(glowEdges.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(frame.TrackEdgeOccupants.Keys),
                "光帶必須恰好對應 frame 中的占用區段。");
            var rails = canvas.Children.OfType<Polyline>().Where(line => line.ToolTip is string tip && tip.Contains('\n'))
                .ToDictionary(line => ((string)line.ToolTip!).Split('\n', 2)[0], StringComparer.OrdinalIgnoreCase);
            foreach (var glow in glows)
            {
                var edgeId = ((string)glow.Tag).Split(':', 2)[1];
                Require(Panel.GetZIndex(glow) == -1 && !glow.IsHitTestVisible && glow.ToolTip is null
                        && ReferenceEquals(glow.Stroke, glowBrush) && glow.StrokeThickness == 16,
                    $"{edgeId}：光帶必須壓在軌道下、不可互動、無提示文字、使用 UiTheme 光帶色與 16px。");
                Require(ReferenceEquals(glow.Points, rails[edgeId].Points), $"{edgeId}：光帶必須沿用軌道同一組凍結座標。");
            }
            var legend = LegendTexts();
            Require(legend.Contains("區段占用") && legend.Contains("已鎖定進路") && legend.Contains("← 上行") && legend.Contains("下行 →"),
                "兩個開關都開啟時圖例必須含上下行、區段占用與已鎖定進路。");

            WpfTestWait.Invoke(window, "DrawV2Route");
            Require(Glows().Length == glows.Length, "靜態快取命中時光帶不可重複疊加。");

            Redraw(occupancy: false, locks: false);
            Require(Glows().Length == 0, "關閉亮燈後不可再有光帶。");
            legend = LegendTexts();
            Require(!legend.Contains("區段占用") && !legend.Contains("已鎖定進路"), "關閉開關後圖例不可再列出對應項目。");
        }
        finally { WpfTestWait.Close(window); }
        Console.WriteLine("[通過] 區段占用光帶、快取重用與圖例");
    }
```

- [ ] **Step 2: 執行測試，確認失敗**

執行共用指令。
Expected: 失敗於 `光帶必須恰好對應 frame 中的占用區段。`（目前沒有任何光帶）。

- [ ] **Step 3: 實作**

在 `MainWindow.V2.cs` 檔頭的 using 區加入 `using System.Collections.Immutable;`，並在類別欄位區（`_topologyRouteVisualCache` 旁）加入：

```csharp
    private ImmutableDictionary<string, ImmutableArray<string>> _routeTrackOccupants = TrackOccupancySnapshot.Empty;
```

把 `TopologyRouteVisualCache` record 替換為：

```csharp
    private sealed record TopologyRouteVisualCache(
        InfrastructureGraphV4 Infrastructure,
        TopologyProjectDocument? Project,
        double Width,
        double Height,
        IReadOnlyDictionary<string, TopologySchematicEdgeGeometry> EdgeGeometries,
        StationChainageProjection? StationChainage,
        IReadOnlyDictionary<string, PointCollection> RailPoints,
        int StaticChildCount);
```

在 `RouteLockVisibility_Changed` 中，`if (IsLoaded) DrawRoute();` 的前一行加入：

```csharp
        // 圖例屬於靜態快取圖層，切換後必須整張重建。
        _topologyRouteVisualCache = null;
```

在 `DrawV2Route` 中，把 `if (_latestPlaybackFrame is { } topologyWorld)` 區塊替換為（並在區塊之後、`RouteCanvas.Children.Clear();` 之前加入清空占用的那一行）：

```csharp
        if (_latestPlaybackFrame is { } topologyWorld)
        {
            _routeTrackOccupants = topologyWorld.TrackEdgeOccupants;
            DrawTopologyGraphRoute(
                topologyWorld.TopologyInfrastructure!,
                _activeTopologyProjectDocument,
                snapshot ?? topologyWorld.GetSnapshot(),
                width,
                height,
                topologyWorld.TrainCenterPositions,
                topologyWorld.ActiveRouteLocks);
            return;
        }

        _routeTrackOccupants = TrackOccupancySnapshot.Empty;
```

在 `DrawTopologyGraphRoute` 中：

1. 快取命中區塊裡，`DrawTopologyRouteLocks(infrastructure, activeRouteLocks, cached.EdgeGeometries);` 的前一行加入：

```csharp
            DrawTopologyTrackOccupancy(cached.RailPoints);
```

2. 把 `StationSchematicPresentation.DrawLegend(RouteCanvas);` 替換為：

```csharp
        StationSchematicPresentation.DrawLegend(RouteCanvas, ShowTrackOccupancyMenuItem.IsChecked, ShowLockedRoutesMenuItem.IsChecked);
```

3. 在軌道迴圈 `foreach (var edge in infrastructure.Edges.Values.OrderBy(...))` 之前加入：

```csharp
        var railPoints = new Dictionary<string, PointCollection>(StringComparer.OrdinalIgnoreCase);
```

並在迴圈主體 `points.Freeze();` 的下一行加入：

```csharp
            railPoints[edge.TrackEdgeId] = points;
```

4. 把 `AddCanvasText(RouteCanvas, "軌道配線圖 · 將滑鼠移到軌道、月台或列車可查看詳細資料", 12, 54, 9, Color.FromRgb(108, 119, 132));` 替換為：

```csharp
        AddCanvasText(RouteCanvas, "軌道配線圖 · 將滑鼠移到軌道、月台或列車可查看詳細資料", 12, 54, 9, UiTheme.TextSubtle);
```

5. 把建立快取的 `new TopologyRouteVisualCache(...)` 呼叫替換為：

```csharp
        _topologyRouteVisualCache = new TopologyRouteVisualCache(
            infrastructure, topologyProject, width, RouteCanvas.Height, edgeGeometries, stationChainage,
            railPoints, RouteCanvas.Children.Count);
        DrawTopologyTrackOccupancy(railPoints);
```

（緊接著原本的 `DrawTopologyRouteLocks(...)` 與 `DrawTopologyTrainMarkers(...)` 保留，順序為：亮燈 → 鎖定進路 → 列車。）

6. 在 `DrawTopologyRouteLocks` 方法之前加入：

```csharp
    private void DrawTopologyTrackOccupancy(IReadOnlyDictionary<string, PointCollection> railPoints)
    {
        if (!ShowTrackOccupancyMenuItem.IsChecked) return;

        foreach (var (edgeId, vehicles) in _routeTrackOccupants)
        {
            if (vehicles.IsDefaultOrEmpty || !railPoints.TryGetValue(edgeId, out var points)) continue;

            // 整條區段亮燈（號誌盤風格）；ZIndex -1 讓光帶壓在軌道下，不改變 Children 索引與快取。
            var glow = new Polyline
            {
                Points = points,
                Stroke = UiTheme.OccupancyGlowBrush,
                StrokeThickness = UiTheme.OccupancyGlowThickness,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false,
                Tag = "TrackOccupancy:" + edgeId
            };
            Panel.SetZIndex(glow, -1);
            RouteCanvas.Children.Add(glow);
        }
    }
```

在 `StationSchematicPresentation.cs`：

1. 把 `DrawLegend` 整個方法替換為：

```csharp
    public static void DrawLegend(Canvas canvas, bool showOccupancy = false, bool showLockedRoutes = false)
    {
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Tag = "RouteLegend" };
        void AddText(string text, Brush brush, double leftMargin) => legend.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = brush,
            Margin = new Thickness(leftMargin, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        void AddSwatch(Brush brush, double height) => legend.Children.Add(new Border
        {
            Width = 18,
            Height = height,
            CornerRadius = new CornerRadius(height / 2),
            Background = brush,
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });

        AddText("← 上行", UiTheme.RailUpBrush, 0);
        AddText("下行 →", UiTheme.RailDownBrush, 12);
        AddText("靠右行駛・里程向右增加", UiTheme.TextSubtleBrush, 12);
        if (showOccupancy)
        {
            AddSwatch(UiTheme.OccupancyGlowBrush, 10);
            AddText("區段占用", UiTheme.TextMutedBrush, 5);
        }
        if (showLockedRoutes)
        {
            AddSwatch(UiTheme.LockedRouteBrushes[0], 3);
            AddText("已鎖定進路", UiTheme.TextMutedBrush, 5);
        }
        Canvas.SetLeft(legend, 18); Canvas.SetTop(legend, 12); canvas.Children.Add(legend);
    }
```

2. 在 `DrawChainageReference` 中把 `Foreground = Brushes.SlateGray` 替換為 `Foreground = UiTheme.TextMutedBrush`。

- [ ] **Step 4: 執行測試，確認通過**

執行共用指令。
Expected: `[通過] 區段占用光帶、快取重用與圖例`，以及前面各項 `[通過]`，結束碼 0；建置 0 warning。

- [ ] **Step 5: Commit**

```powershell
git add src/MrtRouteSimulator.App/MainWindow.V2.cs src/MrtRouteSimulator.App/StationSchematicPresentation.cs tests/MrtRouteSimulator.WpfTests/TrackDiagramThemeTests.cs
git commit -m "feat: light occupied track sections and refresh diagram legend" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: 完整回歸、人工驗收與文件

**Files:**
- Modify: `CHANGELOG.md`（`## V4.1.0` 區段開頭）
- Modify: `QA_REPORT.md`（檔案最上方新增一節）

- [ ] **Step 1: 完整 solution 還原與 Release 建置**

```powershell
dotnet restore .\MrtRouteSimulator.slnx --configfile .\src\MrtRouteSimulator.Mcp\NuGet.Config
dotnet build .\MrtRouteSimulator.slnx -c Release --no-restore
```

Expected: 0 Warning(s)、0 Error(s)。
注意：這一步可能從 nuget.org 下載 MCP 專案的套件（`ModelContextProtocol`、`Microsoft.Extensions.Hosting`）。依使用者的全域規則，執行前要先詢問使用者；若使用者不同意，改為只建置 App、Engine 測試、WPF 測試三個專案，並在 QA 紀錄註明未建置 MCP 專案。

- [ ] **Step 2: Engine 與完整 WPF runner**

```powershell
dotnet run --project .\tests\MrtRouteSimulator.Tests\MrtRouteSimulator.Tests.csproj -c Release --no-build --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- .
```

Expected: Engine 通過數與 Task 0 基準相同；WPF 最後一行 `PASS WPF visual rules`，且輸出中出現 `PASS WPF track diagram theme`。任何失敗都要先判斷是不是本輪造成的回歸，再修正；不可為了通過而降低既有測試門檻。

- [ ] **Step 3: 大型路線播放診斷與截圖**

```powershell
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- .\samples\14-大型-二十八站完整營運範例.mrtsim.json --large-playback-only
```

Expected: `PASS WPF large playback diagnostics`，並印出 `routeImage=` 與 `windowImage=` 路徑。用 Read 工具開啟這兩張圖，再加上 `artifacts\station-rules-visual\` 內以下截圖，逐項檢查：
- `14-大型-二十八站完整營運範例.mrtsim.json-main-1370-t0.png`、`...-main-2512-t0.png`
- `05-小型-三站四股雙島待避範例.mrtsim.json-main-1200-t0.png`
- `07-小型-三站尾軌袋狀軌折返範例.mrtsim.json-main-1200-t0.png`
- `05-...-editor-1200.png`（編輯器的過渡外觀）

人工檢查清單：上下行分別為藍、橘；側線為淺色；設施軌為灰；月台是白條加彩色圓點；站名徽章沒有重複 ID；列車膠囊車號完整、箭頭朝向行進方向；光帶在軌道下且只覆蓋占用區段；圖例完整。若光帶在小範例顯得過粗，只調整 `UiTheme.OccupancyGlowThickness`，並同步更新 Task 8 測試中的 `16`。

- [ ] **Step 4: 更新 CHANGELOG 與 QA_REPORT**

在 `CHANGELOG.md` 的 `## V4.1.0 - 2026-10-07（本地更新，尚未發布）` 標題下一行加入：

```markdown
### 2026-10-09 介面翻新第一輪：設計語言基礎與軌道配線圖

- 新增 `UiTheme` 作為 App 色票與凍結畫筆的單一來源；列車、鎖定進路色盤改由此提供，跨配線圖、運行圖與速度曲線維持「每台車一色」。
- 軌道配線圖改為上行藍／下行橘雙色軌道、白色方向箭頭、白色月台搭配方向色編號圓點、站名徽章（不再重複 ID）與可完整辨識車號的膠囊列車（箭頭依車頭位置判斷）。
- 新增「顯示設定 → 顯示區段占用亮燈」（預設開啟、會記住設定），以黃色光帶標示列車占用的整條軌道區段；資料取自 `SimulationWorld.TopologyOccupancy`，Engine 未修改。
- 拓樸編輯器共用新的月台、站名與圖例樣式；編輯器自繪軌道留待後續子專案。設計見 `docs/superpowers/specs/2026-10-09-ui-theme-and-track-diagram-design.md`。
```

在 `QA_REPORT.md` 第一個 `## ` 標題之前加入一節，數字一律填入 Step 1～3 的實際輸出（不可沿用舊數字）：

```markdown
## 介面翻新第一輪（2026-10-09）

- Release build：<Step 1 實際的 warning／error 數；若未建置 MCP 專案要註明>
- Engine runner：<實際通過數>/<總數>
- WPF runner：<PASS WPF visual rules／失敗訊息>；新增 `TrackDiagramThemeTests`（UiTheme、膠囊列車、雙色軌道、月台圓點、站名徽章、占用彙整、亮燈開關與圖例）通過。
- 大型 28 站播放診斷：<PASS／失敗訊息>；人工檢查截圖：<檢查結果與任何已知視覺問題>。
- 未涵蓋：主視窗外框、結果圖表、拓樸編輯器軌道顏色（子專案 C／D／E）；原生桌面 DPI 與滑鼠操作未重跑。
```

- [ ] **Step 5: Commit**

```powershell
git add CHANGELOG.md QA_REPORT.md
git commit -m "docs: record UI refresh round one changes and verification" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
