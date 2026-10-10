# 主視窗外殼骨架與共用控制項樣式（子專案 C1）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把主視窗改成「左側導覽列＋常駐播放列」骨架，並以 `UiTheme` 為唯一色源，重做全 App 共用控制項樣式。

**Architecture:** `WorkspaceTabControl` 改為 `ShellTabControl`（`TabControl` 子類別），以範本把頁籤畫成 64 px 導覽列，並以 `PageHeader`／`NavFooter` 依賴屬性承載 KPI 細條、驗證橫幅與「起稿」鈕；共用樣式集中於 `Themes/Controls.xaml`，以 `x:Static` 引用 public 的 `UiTheme`。元件名稱、事件、自動化名稱全部保留，因此切換頁面的程式、MCP `select_page` 與以名稱找元件的測試不需改寫。

**Tech Stack:** .NET 10、WPF（C# 14、XAML）、自製 WPF 測試 runner（`tests/MrtRouteSimulator.WpfTests`）。

**Spec:** `docs/superpowers/specs/2026-10-10-main-window-shell-design.md`

## Global Constraints

- 不修改 `src/MrtRouteSimulator.Engine/`、專案 Schema、版本號。
- 外層捲動結構名稱與契約不變：`ShellOverlayRoot`、`ShellScrollViewer`、`ShellContentGrid`（XAML `Height="720"`）、`ShellRouteScrollbarOverlay`、`ShellRouteHorizontalScrollBar`。
- 保留所有既有 `x:Name`、事件處理常式名稱與 `AutomationProperties.Name`；按鈕改成圖示鈕時，`AutomationProperties.Name` 使用原本的按鈕文字（例如 `▶ 播放`）。
- `WorkspaceTabControl` 仍必須是 `TabControl`（`ShellTabControl : TabControl`）；`TabItem.Header` 維持完整原名，短標籤一律用附加屬性 `ShellNav.ShortLabel` 顯示。
- 選單頂層標題固定為 `_檔案`、`_編輯`、`_顯示設定`、`_原生驗收量測`（`InterfaceScaleTests` 要求介面縮放位於標題含「顯示設定」的選單）。
- 顏色只能取自 `UiTheme`；XAML 以 `{x:Static app:UiTheme.XxxBrush}` 引用；不得新增寫死的 hex 色值（`App.xaml` 的相容資源鍵以 `Color="{x:Static app:UiTheme.Xxx}"` 定義）。
- App 專案 `TreatWarningsAsErrors=true`。
- 圖示字型 `Segoe Fluent Icons, Segoe MDL2 Assets`；不新增任何套件。
- 像素與視窗版面類 WPF 測試只能在互動桌面 session 執行；執行完整 runner 時不可操作跳出的測試視窗。執行前先 `query user` 確認 session 為 `Active`。
- Commit 遵循 Conventional Commits，結尾 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`；不 push；只 `git add` 列出的檔案。

## 規格的實作細化（Task 6 同步回寫規格）

1. `TimeAccent` 為 `#7EE0B5`（深色標題列上的模擬時鐘）；`App.xaml` 的 `SuccessBrush` 相容鍵改指向新增的 `Success`（`#16866B`）。
2. 子分頁 `TabItem.Header` 不改，膠囊顯示 `ShellNav.ShortLabel`。
3. 視角跟隨與左右縮放留在「配線圖」子分頁內（只對配線圖有效）；只有 `PlaybackStatusText` 移到膠囊列右側。
4. `ShellTabControl` 另有 `NavFooter` 依賴屬性，用來放導覽列底部的「起稿」鈕。
5. 抽屜以 1 px 分隔線表示邊界，不使用 `DropShadowEffect`（Effect 會讓抽屜內文字變成點陣模糊）。
6. `SetQuickBuilderState(locked, collapsed)` 不再自動開啟抽屜：`collapsed=true` 才關閉；開啟只發生在使用者操作（起稿鈕、選單「快速起稿」、前往 V2 設定）。
7. `VersionSummaryText` 保留為隱藏元素，作為 App 名稱的提示框內容來源。
8. 狀態圓點：播放狀態旗標 `_isV2PlaybackPlaying` 原本有 7 處直接賦值，改為一律經由新方法 `SetV2PlaybackPlaying(bool)`，由它同步刷新圓點；播放工作者發生錯誤（`_playbackWorker.Completion.IsFaulted`）與驗證警告同為橘色。旗標本身仍是欄位（既有測試以反射讀取）。

## Review Focus

1. **介面縮放 125%**：標題列的播放控制與選單不可被擠出視窗或互相重疊。由 Task 5 測試釘住。
2. **超長專案檔名**：`CurrentProjectFileTextBlock` 很長時只截斷文字，不推擠選單與播放列。由 Task 5 測試釘住。
3. **驗證橫幅訊息很多時**：橫幅最高 140 px 內捲動，配線圖可視區仍保有足夠高度。由 Task 5 測試釘住。
4. **抽屜開關與視窗縮放**：抽屜覆蓋在主體上、不改變配線圖寬度；縮放視窗不會重新打開已關閉的抽屜；Esc 只在焦點位於抽屜內時關閉。由 Task 4 測試釘住。
5. **導覽列鍵盤操作**：導覽項目可取得焦點且使用 `FocusRingVisual`；以滑鼠點選或程式切換都會改變 `SelectedItem`。由 Task 4 測試釘住。

---

## File Structure

| 檔案 | 動作 | 責任 |
|---|---|---|
| `src/MrtRouteSimulator.App/UiTheme.cs` | 修改 | 改為 public，新增外殼色票 |
| `src/MrtRouteSimulator.App/App.xaml` | 修改 | 相容資源鍵改取 `UiTheme`；合併 `Themes/Controls.xaml` |
| `src/MrtRouteSimulator.App/Themes/Controls.xaml` | 新增 | 全 App 共用控制項樣式與外殼範本 |
| `src/MrtRouteSimulator.App/ShellTabControl.cs` | 新增 | `TabControl` 子類別，`PageHeader`／`NavFooter` |
| `src/MrtRouteSimulator.App/ShellNav.cs` | 新增 | 附加屬性 `ShortLabel`、`Icon` |
| `src/MrtRouteSimulator.App/MainWindow.xaml` | 修改 | 外殼骨架重排 |
| `src/MrtRouteSimulator.App/MainWindow.WorkspaceNavigation.cs` | 修改 | 快速起稿抽屜邏輯；移除 `OpenResults_Click` |
| `src/MrtRouteSimulator.App/MainWindow.InputEditors.cs`、`MainWindow.xaml.cs` | 修改 | 「快速起稿／V2 設定」開啟抽屜；驗證橫幅與播放狀態更新狀態圓點 |
| `src/MrtRouteSimulator.App/MainWindow.Shell.cs` | 新增 | 狀態列圓點、`SetV2PlaybackPlaying` |
| `src/MrtRouteSimulator.App/MainWindow.V2.cs` | 修改 | 播放旗標賦值改經 `SetV2PlaybackPlaying` |
| `tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs` | 新增 | 本輪回歸測試、基準量測、截圖 |
| `tests/MrtRouteSimulator.WpfTests/Program.cs` | 修改 | `--shell-layout-baseline`、`--shell-layout-only`、`--shell-screenshots`，並納入完整 runner |
| `tests/MrtRouteSimulator.WpfTests/OuterShellScrollTests.cs` | 修改 | 左欄改抽屜、狀態列列號 3→2 |
| `tests/MrtRouteSimulator.WpfTests/LargePlaybackDiagnostics.cs` | 修改 | 配線圖撐滿檢查改用新結構 |
| `docs/superpowers/specs/2026-10-10-main-window-shell-design.md`、`CHANGELOG.md`、`QA_REPORT.md` | 修改 | Task 6 |

**共用指令**（worktree 根目錄，PowerShell）：

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --shell-layout-only
```

---

### Task 0: 同步 main、基準線與配線圖高度基準

**Files:**
- Create: `tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs`
- Modify: `tests/MrtRouteSimulator.WpfTests/Program.cs`

**Interfaces:**
- Produces: `ShellLayoutTests.Run(string root)`、`ShellLayoutTests.MeasureRouteViewportHeight(string root)`、`ShellLayoutTests.PumpLayout(Window)`、`ShellLayoutTests.Require(bool, string)`；runner 旗標 `--shell-layout-baseline`、`--shell-layout-only`。

- [ ] **Step 1: 同步最新 main**

以 `ToolSearch` 載入 `mcp__ccd_host__sync_with_base_branch` 後呼叫它，把 `main`（已含 `dd7b765` 的 MCP 匯出修正）合併進本分支。有衝突就依兩邊意圖解決後 commit。

- [ ] **Step 2: 還原、建置與 Engine 基準**

```powershell
dotnet restore .\MrtRouteSimulator.slnx --configfile .\src\MrtRouteSimulator.Mcp\NuGet.Config
dotnet build .\MrtRouteSimulator.slnx -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.Tests\MrtRouteSimulator.Tests.csproj -c Release --no-build --no-restore
```

Expected: 0 warning／0 error；Engine 全部通過，記下通過數。

- [ ] **Step 3: 完整 WPF runner 基準（互動桌面、不操作視窗）**

```powershell
query user
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- .
```

Expected: session 為 `Active`；最後一行 `PASS WPF visual rules`。若失敗，記錄訊息；之後不得把這些既有失敗算成本輪回歸。

- [ ] **Step 4: 建立量測工具並量基準**

建立 `tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs`：

```csharp
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

/// <summary>主視窗外殼（子專案 C1）的版面與樣式回歸測試。</summary>
internal static class ShellLayoutTests
{
    public static void Run(string root)
    {
        Console.WriteLine("PASS WPF shell layout");
    }

    // 1280×800、100% 介面縮放、範例 10、模擬頁「配線圖」子分頁的 RouteScrollViewer 高度。
    internal static double MeasureRouteViewportHeight(string root)
    {
        var previousScale = SetInterfaceScale(1);
        var window = new MainWindow { Width = 1280, Height = 800 };
        try
        {
            LoadSample(window, root, "10-小型-三站完整拓樸基準範例.mrtsim.json");
            window.Show();
            ((TabControl)window.FindName("WorkspaceTabControl")!).SelectedItem = window.FindName("SimulationTabItem");
            ((TabControl)window.FindName("SimulationViewTabControl")!).SelectedIndex = 0;
            PumpLayout(window);
            return ((ScrollViewer)window.FindName("RouteScrollViewer")!).ActualHeight;
        }
        finally
        {
            WpfTestWait.Close(window);
            SetInterfaceScale(previousScale);
        }
    }

    internal static void LoadSample(MainWindow window, string root, string fileName)
    {
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(System.IO.Path.Combine(root, "samples", fileName)));
        WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(window, "ConfigureTopologyProjectForPlaybackAsync", document, true));
    }

    internal static double SetInterfaceScale(double scale)
    {
        var service = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.InterfaceScaleService")!;
        var previous = (double)service.GetProperty("CurrentScale")!.GetValue(null)!;
        service.GetMethod("SetScale")!.Invoke(null, [scale]);
        return previous;
    }

    internal static void PumpLayout(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
```

在 `Program.cs` 的 `--track-theme-only` 區塊之前加入：

```csharp
            if (args.Contains("--shell-layout-baseline"))
            {
                Console.WriteLine($"shellBaselineRouteViewportHeight={ShellLayoutTests.MeasureRouteViewportHeight(GetRoot(args)):0.0}");
                return 0;
            }
            if (args.Contains("--shell-layout-only"))
            {
                ShellLayoutTests.Run(GetRoot(args));
                return 0;
            }
```

並在完整 runner 的 `TrackDiagramThemeTests.Run(GetRoot(args));` 下一行加入 `ShellLayoutTests.Run(GetRoot(args));`。

建置後執行：

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --shell-layout-baseline
```

Expected: 印出 `shellBaselineRouteViewportHeight=<數值>`。把這個數值記入進度紀錄，Task 4 Step 1 會把它寫進測試常數。

- [ ] **Step 5: Commit**

```powershell
git add tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs tests/MrtRouteSimulator.WpfTests/Program.cs
git commit -m "test: add shell layout test harness and route viewport baseline probe" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 1: `UiTheme` 外殼色票與相容資源鍵

**Files:**
- Modify: `src/MrtRouteSimulator.App/UiTheme.cs`
- Modify: `src/MrtRouteSimulator.App/App.xaml`
- Test: `tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs`

**Interfaces:**
- Produces: `public static class UiTheme`；新 `Color` 欄位 `AppBar`、`AppBarRaised`、`AppBarText`、`AppBarMuted`、`WindowBackground`、`Surface`、`Border`、`Accent`、`NavSelected`、`NavHover`、`Primary`、`PrimaryHover`、`TimeAccent`、`Success`、`WarningSurface`、`WarningBorder`、`WarningText`、`StatusBar`、`TooltipBackground`、`TableHeader`、`GridLine`，以及對應凍結畫筆（名稱 + `Brush`）。

- [ ] **Step 1: 寫失敗測試**

在 `ShellLayoutTests.Run` 的 `Console.WriteLine` 前加入 `VerifyShellTokens();`，並加入：

```csharp
    private static readonly string[] ShellTokens =
    [
        "AppBar", "AppBarRaised", "AppBarText", "AppBarMuted", "WindowBackground", "Surface", "Border", "Accent",
        "NavSelected", "NavHover", "Primary", "PrimaryHover", "TimeAccent", "Success", "WarningSurface",
        "WarningBorder", "WarningText", "StatusBar", "TooltipBackground", "TableHeader", "GridLine"
    ];

    private static void VerifyShellTokens()
    {
        var theme = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.UiTheme")!;
        Require(theme.IsPublic, "UiTheme 必須是 public，XAML 才能以 x:Static 引用。");
        foreach (var token in ShellTokens)
        {
            var color = theme.GetField(token)?.GetValue(null);
            var brush = theme.GetField(token + "Brush")?.GetValue(null) as SolidColorBrush;
            Require(color is Color value && brush is not null && brush.IsFrozen && brush.Color == value,
                $"UiTheme 缺少外殼色票 {token} 或其凍結畫筆。");
        }
        var legacy = new (string Key, string Token)[]
        {
            ("InkBrush", "TextStrong"), ("MutedBrush", "TextMuted"), ("PrimaryBrush", "Primary"),
            ("PrimaryDarkBrush", "PrimaryHover"), ("SurfaceBrush", "Surface"), ("BackgroundBrush", "WindowBackground"),
            ("BorderBrush", "Border"), ("SuccessBrush", "Success")
        };
        foreach (var (key, token) in legacy)
        {
            Require(Application.Current.TryFindResource(key) is SolidColorBrush brush
                    && brush.Color == (Color)theme.GetField(token)!.GetValue(null)!,
                $"App.xaml 的 {key} 必須取自 UiTheme.{token}。");
        }
        Console.WriteLine("[通過] UiTheme 外殼色票與相容資源鍵");
    }
```

- [ ] **Step 2: 執行，確認失敗**

執行共用指令。Expected: `UiTheme 必須是 public，XAML 才能以 x:Static 引用。`

- [ ] **Step 3: 實作**

`UiTheme.cs`：把 `internal static class UiTheme` 改為 `public static class UiTheme`；在 `Danger` 色票下一行加入：

```csharp

    // 主視窗外殼（子專案 C1）。
    public static readonly Color AppBar = Color.FromRgb(0x1B, 0x25, 0x38);
    public static readonly Color AppBarRaised = Color.FromRgb(0x2C, 0x3A, 0x55);
    public static readonly Color AppBarText = Colors.White;
    public static readonly Color AppBarMuted = Color.FromRgb(0x9F, 0xB0, 0xC8);
    public static readonly Color WindowBackground = Color.FromRgb(0xF5, 0xF7, 0xFA);
    public static readonly Color Surface = Colors.White;
    public static readonly Color Border = Color.FromRgb(0xE2, 0xE8, 0xEF);
    public static readonly Color Accent = Color.FromRgb(0x2F, 0x7F, 0xC1);
    public static readonly Color NavSelected = Color.FromRgb(0xEA, 0xF2, 0xFB);
    public static readonly Color NavHover = Color.FromRgb(0xF1, 0xF5, 0xF9);
    public static readonly Color Primary = Color.FromRgb(0xE8, 0x6D, 0x2D);
    public static readonly Color PrimaryHover = Color.FromRgb(0xB8, 0x47, 0x19);
    public static readonly Color TimeAccent = Color.FromRgb(0x7E, 0xE0, 0xB5);
    public static readonly Color Success = Color.FromRgb(0x16, 0x86, 0x6B);
    public static readonly Color WarningSurface = Color.FromRgb(0xFF, 0xF4, 0xEC);
    public static readonly Color WarningBorder = Color.FromRgb(0xF4, 0xC3, 0xA0);
    public static readonly Color WarningText = Color.FromRgb(0x9A, 0x3A, 0x12);
    public static readonly Color StatusBar = Color.FromRgb(0xE9, 0xEE, 0xF4);
    public static readonly Color TooltipBackground = Color.FromRgb(0x1E, 0x29, 0x3B);
    public static readonly Color TableHeader = Color.FromRgb(0xF8, 0xFA, 0xFC);
    public static readonly Color GridLine = Color.FromRgb(0xF1, 0xF5, 0xF9);
```

在 `DangerBrush` 那一行之後加入：

```csharp

    public static readonly SolidColorBrush AppBarBrush = Frozen(AppBar);
    public static readonly SolidColorBrush AppBarRaisedBrush = Frozen(AppBarRaised);
    public static readonly SolidColorBrush AppBarTextBrush = Frozen(AppBarText);
    public static readonly SolidColorBrush AppBarMutedBrush = Frozen(AppBarMuted);
    public static readonly SolidColorBrush WindowBackgroundBrush = Frozen(WindowBackground);
    public static readonly SolidColorBrush SurfaceBrush = Frozen(Surface);
    public static readonly SolidColorBrush BorderBrush = Frozen(Border);
    public static readonly SolidColorBrush AccentBrush = Frozen(Accent);
    public static readonly SolidColorBrush NavSelectedBrush = Frozen(NavSelected);
    public static readonly SolidColorBrush NavHoverBrush = Frozen(NavHover);
    public static readonly SolidColorBrush PrimaryBrush = Frozen(Primary);
    public static readonly SolidColorBrush PrimaryHoverBrush = Frozen(PrimaryHover);
    public static readonly SolidColorBrush TimeAccentBrush = Frozen(TimeAccent);
    public static readonly SolidColorBrush SuccessBrush = Frozen(Success);
    public static readonly SolidColorBrush WarningSurfaceBrush = Frozen(WarningSurface);
    public static readonly SolidColorBrush WarningBorderBrush = Frozen(WarningBorder);
    public static readonly SolidColorBrush WarningTextBrush = Frozen(WarningText);
    public static readonly SolidColorBrush StatusBarBrush = Frozen(StatusBar);
    public static readonly SolidColorBrush TooltipBackgroundBrush = Frozen(TooltipBackground);
    public static readonly SolidColorBrush TableHeaderBrush = Frozen(TableHeader);
    public static readonly SolidColorBrush GridLineBrush = Frozen(GridLine);
```

`App.xaml`：在 `<Application ...>` 開始標籤加入 `xmlns:app="clr-namespace:MrtRouteSimulator.App"`，並把八行寫死 hex 的 `SolidColorBrush` 替換為：

```xml
        <SolidColorBrush x:Key="InkBrush" Color="{x:Static app:UiTheme.TextStrong}" />
        <SolidColorBrush x:Key="MutedBrush" Color="{x:Static app:UiTheme.TextMuted}" />
        <SolidColorBrush x:Key="PrimaryBrush" Color="{x:Static app:UiTheme.Primary}" />
        <SolidColorBrush x:Key="PrimaryDarkBrush" Color="{x:Static app:UiTheme.PrimaryHover}" />
        <SolidColorBrush x:Key="SurfaceBrush" Color="{x:Static app:UiTheme.Surface}" />
        <SolidColorBrush x:Key="BackgroundBrush" Color="{x:Static app:UiTheme.WindowBackground}" />
        <SolidColorBrush x:Key="BorderBrush" Color="{x:Static app:UiTheme.Border}" />
        <SolidColorBrush x:Key="SuccessBrush" Color="{x:Static app:UiTheme.Success}" />
```

- [ ] **Step 4: 執行，確認通過**

執行共用指令，並跑 `--track-theme-only`。
Expected: `[通過] UiTheme 外殼色票與相容資源鍵`、`PASS WPF shell layout`；`PASS WPF track diagram theme`；建置 0 warning。

- [ ] **Step 5: Commit**

```powershell
git add src/MrtRouteSimulator.App/UiTheme.cs src/MrtRouteSimulator.App/App.xaml tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs
git commit -m "feat: add shell color tokens to UiTheme and source legacy brushes from it" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: 共用控制項樣式 `Themes/Controls.xaml`

**Files:**
- Create: `src/MrtRouteSimulator.App/Themes/Controls.xaml`
- Modify: `src/MrtRouteSimulator.App/App.xaml`
- Test: `tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs`

**Interfaces:**
- Produces: 資源鍵 `AppFont`、`IconFont`、`MonoFont`、`FocusRingVisual`、`SectionTitle`、`FieldLabel`、`ButtonChrome`、`PrimaryButton`、`SecondaryButton`、`GhostButton`、`AppBarButton`、`AppBarPrimaryButton`、`SummaryCard`；隱含樣式 `Window`、`Button`、`TextBox`、`ComboBox`、`ComboBoxItem`、`ScrollBar`、`Menu`、`MenuItem`、`ContextMenu`、`ToolTip`、`Expander`、`DataGrid`、`DataGridColumnHeader`、`DataGridRow`、`DataGridCell`；`MenuItem.SeparatorStyleKey`。

- [ ] **Step 1: 寫失敗測試**

在 `Run` 加入 `VerifyImplicitStyles();`，並加入：

```csharp
    private static void VerifyImplicitStyles()
    {
        var resources = Application.Current.Resources;
        foreach (var type in new[] { typeof(Button), typeof(TextBox), typeof(ComboBox), typeof(ComboBoxItem), typeof(ScrollBar),
                     typeof(Menu), typeof(MenuItem), typeof(ContextMenu), typeof(ToolTip), typeof(Expander), typeof(DataGrid), typeof(DataGridRow) })
            Require(Application.Current.TryFindResource(type) is Style, $"缺少 {type.Name} 的隱含樣式。");
        foreach (var key in new[] { "PrimaryButton", "SecondaryButton", "GhostButton", "AppBarButton", "AppBarPrimaryButton",
                     "SectionTitle", "FieldLabel", "FocusRingVisual", "IconFont", "MonoFont", "AppFont" })
            Require(Application.Current.TryFindResource(key) is not null, $"缺少資源 {key}。");
        foreach (var type in new[] { typeof(Button), typeof(TextBox), typeof(ComboBox), typeof(DataGrid) })
            Require(ReferenceEquals(SetterValue((Style)Application.Current.FindResource(type), Control.BackgroundProperty), UiTheme.SurfaceBrush),
                $"{type.Name} 底色必須取自 UiTheme.SurfaceBrush。");
        Require(ReferenceEquals(SetterValue((Style)Application.Current.FindResource(typeof(ToolTip)), Control.ForegroundProperty), UiTheme.AppBarTextBrush),
            "提示框文字必須取自 UiTheme.AppBarTextBrush。");
        Require(SetterValue((Style)Application.Current.FindResource(typeof(MenuItem)), Control.TemplateProperty) is ControlTemplate
                && Application.Current.TryFindResource(MenuItem.SeparatorStyleKey) is Style,
            "MenuItem 必須使用自訂範本與分隔線樣式。");
        Require(ReferenceEquals(SetterValue((Style)Application.Current.FindResource("PrimaryButton"), Control.BackgroundProperty), UiTheme.PrimaryBrush),
            "主要按鈕必須使用 UiTheme.PrimaryBrush。");
        Require(ReferenceEquals(SetterValue((Style)Application.Current.FindResource(typeof(DataGrid)), DataGrid.HorizontalGridLinesBrushProperty), UiTheme.GridLineBrush),
            "DataGrid 格線必須取自 UiTheme.GridLineBrush。");

        var editable = new ComboBox { IsEditable = true, ItemsSource = new[] { "1", "2" } };
        var readOnly = new ComboBox { ItemsSource = new[] { "A", "B" }, SelectedIndex = 0 };
        var text = new TextBox { Text = "x" };
        var scroll = new ScrollViewer { Height = 60, VerticalScrollBarVisibility = ScrollBarVisibility.Visible, Content = new Border { Height = 200 } };
        var panel = new StackPanel();
        foreach (var child in new UIElement[] { editable, readOnly, text, scroll }) panel.Children.Add(child);
        var window = new Window { Width = 400, Height = 320, Content = panel };
        try
        {
            window.Show();
            PumpLayout(window);
            Require(editable.Template.FindName("PART_EditableTextBox", editable) is TextBox, "可編輯 ComboBox 範本必須提供 PART_EditableTextBox。");
            Require(readOnly.Template.FindName("PART_Popup", readOnly) is Popup, "ComboBox 範本必須提供 PART_Popup。");
            editable.Text = "15";
            PumpLayout(window);
            Require(((TextBox)editable.Template.FindName("PART_EditableTextBox", editable)).Text == "15"
                    && ((TextBox)editable.Template.FindName("PART_EditableTextBox", editable)).IsVisible,
                "可編輯 ComboBox 的文字必須顯示在可見的 PART_EditableTextBox。");
            readOnly.IsDropDownOpen = true;
            PumpLayout(window);
            Require(readOnly.IsDropDownOpen, "ComboBox 必須能展開下拉清單。");
            readOnly.IsDropDownOpen = false;
            Require(text.Template.FindName("PART_ContentHost", text) is ScrollViewer, "TextBox 範本必須提供 PART_ContentHost。");
            var bar = FindDescendant<ScrollBar>(scroll, item => item.Orientation == Orientation.Vertical);
            Require(bar is not null && bar.ActualWidth <= 10.5, $"垂直捲軸寬度應為 10，實際 {bar?.ActualWidth:0.0}。");
        }
        finally { window.Close(); }
        Console.WriteLine("[通過] 共用控制項隱含樣式");
    }

    private static object? SetterValue(Style style, DependencyProperty property)
    {
        for (var current = style; current is not null; current = current.BasedOn)
        {
            if (current.Setters.OfType<Setter>().FirstOrDefault(setter => setter.Property == property) is { } setter)
                return setter.Value;
        }
        return null;
    }

    internal static T? FindDescendant<T>(DependencyObject root, Func<T, bool>? predicate = null) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match && (predicate is null || predicate(match))) return match;
            if (FindDescendant(child, predicate) is { } nested) return nested;
        }
        return null;
    }
```

- [ ] **Step 2: 執行，確認失敗**

執行共用指令。Expected: `缺少 ComboBoxItem 的隱含樣式。`（或第一個尚未存在的樣式）。

- [ ] **Step 3: 實作**

建立 `src/MrtRouteSimulator.App/Themes/Controls.xaml`：

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:app="clr-namespace:MrtRouteSimulator.App">

    <!-- 字型 -->
    <FontFamily x:Key="AppFont">Microsoft JhengHei UI, Segoe UI</FontFamily>
    <FontFamily x:Key="IconFont">Segoe Fluent Icons, Segoe MDL2 Assets</FontFamily>
    <FontFamily x:Key="MonoFont">Cascadia Mono, Consolas</FontFamily>

    <Style TargetType="Window">
        <Setter Property="FontFamily" Value="{StaticResource AppFont}" />
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.TextStrongBrush}" />
        <Setter Property="Background" Value="{x:Static app:UiTheme.WindowBackgroundBrush}" />
    </Style>

    <!-- 鍵盤焦點：只在鍵盤操作時顯示 -->
    <Style x:Key="FocusRingVisual">
        <Setter Property="Control.Template">
            <Setter.Value>
                <ControlTemplate>
                    <Border Margin="-2" CornerRadius="8" BorderThickness="2" BorderBrush="{x:Static app:UiTheme.AccentBrush}" />
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key="SectionTitle" TargetType="TextBlock">
        <Setter Property="FontSize" Value="17" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.TextStrongBrush}" />
        <Setter Property="Margin" Value="0,4,0,12" />
    </Style>

    <Style x:Key="FieldLabel" TargetType="TextBlock">
        <Setter Property="FontSize" Value="12" />
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.TextMutedBrush}" />
        <Setter Property="Margin" Value="0,0,0,5" />
    </Style>

    <Style x:Key="SummaryCard" TargetType="Border">
        <Setter Property="Background" Value="{x:Static app:UiTheme.SurfaceBrush}" />
        <Setter Property="BorderBrush" Value="{x:Static app:UiTheme.BorderBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="8" />
        <Setter Property="Padding" Value="13,10" />
        <Setter Property="Margin" Value="5" />
    </Style>

    <!-- 按鈕 -->
    <Style x:Key="ButtonChrome" TargetType="Button">
        <Setter Property="FocusVisualStyle" Value="{StaticResource FocusRingVisual}" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="HorizontalContentAlignment" Value="Center" />
        <Setter Property="VerticalContentAlignment" Value="Center" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Grid SnapsToDevicePixels="True">
                        <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                                BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="6" />
                        <Border x:Name="Shade" Background="{x:Static app:UiTheme.TextStrongBrush}" CornerRadius="6" Opacity="0" />
                        <ContentPresenter Margin="{TemplateBinding Padding}" RecognizesAccessKey="True"
                                          HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                          VerticalAlignment="{TemplateBinding VerticalContentAlignment}" />
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Shade" Property="Opacity" Value="0.07" />
                        </Trigger>
                        <Trigger Property="IsPressed" Value="True">
                            <Setter TargetName="Shade" Property="Opacity" Value="0.14" />
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.45" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key="SecondaryButton" TargetType="Button" BasedOn="{StaticResource ButtonChrome}">
        <Setter Property="Background" Value="{x:Static app:UiTheme.SurfaceBrush}" />
        <Setter Property="BorderBrush" Value="{x:Static app:UiTheme.HairlineBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.TextStrongBrush}" />
        <Setter Property="Padding" Value="12,7" />
        <Setter Property="MinHeight" Value="34" />
    </Style>

    <Style x:Key="PrimaryButton" TargetType="Button" BasedOn="{StaticResource ButtonChrome}">
        <Setter Property="Background" Value="{x:Static app:UiTheme.PrimaryBrush}" />
        <Setter Property="BorderThickness" Value="0" />
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.AppBarTextBrush}" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Padding" Value="18,9" />
        <Setter Property="MinHeight" Value="38" />
    </Style>

    <Style x:Key="GhostButton" TargetType="Button" BasedOn="{StaticResource ButtonChrome}">
        <Setter Property="Background" Value="{x:Static app:UiTheme.NavHoverBrush}" />
        <Setter Property="BorderThickness" Value="0" />
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.TextStrongBrush}" />
        <Setter Property="Padding" Value="10,5" />
    </Style>

    <Style x:Key="AppBarButton" TargetType="Button" BasedOn="{StaticResource ButtonChrome}">
        <Setter Property="Background" Value="{x:Static app:UiTheme.AppBarRaisedBrush}" />
        <Setter Property="BorderThickness" Value="0" />
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.AppBarTextBrush}" />
        <Setter Property="FontFamily" Value="{StaticResource IconFont}" />
        <Setter Property="FontSize" Value="14" />
        <Setter Property="Width" Value="32" />
        <Setter Property="Height" Value="28" />
        <Setter Property="Padding" Value="0" />
        <Setter Property="Margin" Value="0,0,4,0" />
    </Style>

    <Style x:Key="AppBarPrimaryButton" TargetType="Button" BasedOn="{StaticResource AppBarButton}">
        <Setter Property="Background" Value="{x:Static app:UiTheme.PrimaryBrush}" />
    </Style>

    <Style TargetType="Button" BasedOn="{StaticResource SecondaryButton}" />

    <!-- 文字輸入 -->
    <Style TargetType="TextBox">
        <Setter Property="MinHeight" Value="32" />
        <Setter Property="Padding" Value="9,5" />
        <Setter Property="VerticalContentAlignment" Value="Center" />
        <Setter Property="Background" Value="{x:Static app:UiTheme.SurfaceBrush}" />
        <Setter Property="BorderBrush" Value="{x:Static app:UiTheme.HairlineBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.TextStrongBrush}" />
        <Setter Property="SelectionBrush" Value="{x:Static app:UiTheme.AccentBrush}" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="TextBox">
                    <Border x:Name="Frame" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="6" SnapsToDevicePixels="True">
                        <ScrollViewer x:Name="PART_ContentHost" Focusable="False"
                                      HorizontalScrollBarVisibility="{TemplateBinding ScrollViewer.HorizontalScrollBarVisibility}"
                                      VerticalScrollBarVisibility="{TemplateBinding ScrollViewer.VerticalScrollBarVisibility}" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Frame" Property="BorderBrush" Value="{x:Static app:UiTheme.TextSubtleBrush}" />
                        </Trigger>
                        <Trigger Property="IsKeyboardFocusWithin" Value="True">
                            <Setter TargetName="Frame" Property="BorderBrush" Value="{x:Static app:UiTheme.AccentBrush}" />
                            <Setter TargetName="Frame" Property="BorderThickness" Value="2" />
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.55" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- 下拉選單（支援可編輯與唯讀） -->
    <ControlTemplate x:Key="ComboBoxToggleTemplate" TargetType="ToggleButton">
        <Border x:Name="Frame" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="6" SnapsToDevicePixels="True">
            <Path HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,10,0" Data="M0,0 L4,4 L8,0"
                  Stroke="{x:Static app:UiTheme.TextMutedBrush}" StrokeThickness="1.5"
                  StrokeStartLineCap="Round" StrokeEndLineCap="Round" />
        </Border>
        <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Setter TargetName="Frame" Property="BorderBrush" Value="{x:Static app:UiTheme.TextSubtleBrush}" />
            </Trigger>
            <Trigger Property="IsChecked" Value="True">
                <Setter TargetName="Frame" Property="BorderBrush" Value="{x:Static app:UiTheme.AccentBrush}" />
            </Trigger>
        </ControlTemplate.Triggers>
    </ControlTemplate>

    <Style TargetType="ComboBoxItem">
        <Setter Property="Padding" Value="8,5" />
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.TextStrongBrush}" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ComboBoxItem">
                    <Border x:Name="Chrome" Background="Transparent" CornerRadius="4" Padding="{TemplateBinding Padding}">
                        <ContentPresenter />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsHighlighted" Value="True">
                            <Setter TargetName="Chrome" Property="Background" Value="{x:Static app:UiTheme.NavHoverBrush}" />
                        </Trigger>
                        <Trigger Property="IsSelected" Value="True">
                            <Setter TargetName="Chrome" Property="Background" Value="{x:Static app:UiTheme.NavSelectedBrush}" />
                            <Setter Property="Foreground" Value="{x:Static app:UiTheme.AccentBrush}" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style TargetType="ComboBox">
        <Setter Property="MinHeight" Value="32" />
        <Setter Property="Padding" Value="9,4,28,4" />
        <Setter Property="Background" Value="{x:Static app:UiTheme.SurfaceBrush}" />
        <Setter Property="BorderBrush" Value="{x:Static app:UiTheme.HairlineBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.TextStrongBrush}" />
        <Setter Property="VerticalContentAlignment" Value="Center" />
        <Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Auto" />
        <Setter Property="ScrollViewer.VerticalScrollBarVisibility" Value="Auto" />
        <Setter Property="ScrollViewer.CanContentScroll" Value="True" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ComboBox">
                    <Grid SnapsToDevicePixels="True">
                        <ToggleButton x:Name="ToggleButton" Template="{StaticResource ComboBoxToggleTemplate}" Focusable="False" ClickMode="Press"
                                      Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                                      BorderThickness="{TemplateBinding BorderThickness}"
                                      IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}" />
                        <ContentPresenter x:Name="ContentSite" IsHitTestVisible="False" Margin="{TemplateBinding Padding}"
                                          Content="{TemplateBinding SelectionBoxItem}"
                                          ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"
                                          ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}"
                                          ContentStringFormat="{TemplateBinding SelectionBoxItemStringFormat}"
                                          VerticalAlignment="{TemplateBinding VerticalContentAlignment}" HorizontalAlignment="Left" />
                        <TextBox x:Name="PART_EditableTextBox" Style="{x:Null}" Visibility="Hidden" Margin="{TemplateBinding Padding}"
                                 Background="Transparent" BorderThickness="0" Padding="0" MinHeight="0" VerticalAlignment="Center"
                                 Foreground="{TemplateBinding Foreground}" IsReadOnly="{TemplateBinding IsReadOnly}" Focusable="True" />
                        <Popup x:Name="PART_Popup" Placement="Bottom" IsOpen="{TemplateBinding IsDropDownOpen}"
                               AllowsTransparency="True" Focusable="False" PopupAnimation="Fade">
                            <Border x:Name="DropDownBorder" Margin="0,4,0,0" MinWidth="{TemplateBinding ActualWidth}"
                                    MaxHeight="{TemplateBinding MaxDropDownHeight}" Background="{x:Static app:UiTheme.SurfaceBrush}"
                                    BorderBrush="{x:Static app:UiTheme.BorderBrush}" BorderThickness="1" CornerRadius="8" Padding="4">
                                <ScrollViewer SnapsToDevicePixels="True">
                                    <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained" />
                                </ScrollViewer>
                            </Border>
                        </Popup>
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsEditable" Value="True">
                            <Setter Property="IsTabStop" Value="False" />
                            <Setter TargetName="PART_EditableTextBox" Property="Visibility" Value="Visible" />
                            <Setter TargetName="ContentSite" Property="Visibility" Value="Hidden" />
                        </Trigger>
                        <Trigger Property="HasItems" Value="False">
                            <Setter TargetName="DropDownBorder" Property="MinHeight" Value="32" />
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.55" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- 捲軸：10 px、圓角拇指、無箭頭 -->
    <Style x:Key="ScrollThumb" TargetType="Thumb">
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Thumb">
                    <Border x:Name="Bar" CornerRadius="3" Background="{x:Static app:UiTheme.HairlineBrush}" />
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Bar" Property="Background" Value="{x:Static app:UiTheme.TextSubtleBrush}" />
                        </Trigger>
                        <Trigger Property="IsDragging" Value="True">
                            <Setter TargetName="Bar" Property="Background" Value="{x:Static app:UiTheme.TextMutedBrush}" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key="ScrollPageButton" TargetType="RepeatButton">
        <Setter Property="Focusable" Value="False" />
        <Setter Property="IsTabStop" Value="False" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="RepeatButton">
                    <Border Background="Transparent" />
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style TargetType="ScrollBar">
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="Width" Value="10" />
        <Setter Property="MinWidth" Value="10" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ScrollBar">
                    <Border x:Name="Rail" Background="{TemplateBinding Background}" Padding="3,2">
                        <Track x:Name="PART_Track" IsDirectionReversed="True">
                            <Track.DecreaseRepeatButton>
                                <RepeatButton Style="{StaticResource ScrollPageButton}" Command="{x:Static ScrollBar.PageUpCommand}" />
                            </Track.DecreaseRepeatButton>
                            <Track.Thumb>
                                <Thumb Style="{StaticResource ScrollThumb}" />
                            </Track.Thumb>
                            <Track.IncreaseRepeatButton>
                                <RepeatButton Style="{StaticResource ScrollPageButton}" Command="{x:Static ScrollBar.PageDownCommand}" />
                            </Track.IncreaseRepeatButton>
                        </Track>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Rail" Property="Padding" Value="1,2" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
        <Style.Triggers>
            <Trigger Property="Orientation" Value="Horizontal">
                <Setter Property="Width" Value="Auto" />
                <Setter Property="MinWidth" Value="0" />
                <Setter Property="Height" Value="10" />
                <Setter Property="MinHeight" Value="10" />
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="ScrollBar">
                            <Border x:Name="Rail" Background="{TemplateBinding Background}" Padding="2,3">
                                <Track x:Name="PART_Track" IsDirectionReversed="False">
                                    <Track.DecreaseRepeatButton>
                                        <RepeatButton Style="{StaticResource ScrollPageButton}" Command="{x:Static ScrollBar.PageLeftCommand}" />
                                    </Track.DecreaseRepeatButton>
                                    <Track.Thumb>
                                        <Thumb Style="{StaticResource ScrollThumb}" />
                                    </Track.Thumb>
                                    <Track.IncreaseRepeatButton>
                                        <RepeatButton Style="{StaticResource ScrollPageButton}" Command="{x:Static ScrollBar.PageRightCommand}" />
                                    </Track.IncreaseRepeatButton>
                                </Track>
                            </Border>
                            <ControlTemplate.Triggers>
                                <Trigger Property="IsMouseOver" Value="True">
                                    <Setter TargetName="Rail" Property="Padding" Value="2,1" />
                                </Trigger>
                            </ControlTemplate.Triggers>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Trigger>
        </Style.Triggers>
    </Style>

    <!-- 選單：頂層顯示在深色標題列；下拉與右鍵選單為白色浮層 -->
    <Style TargetType="Menu">
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.AppBarTextBrush}" />
        <Setter Property="FontSize" Value="13" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Menu">
                    <Border Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}">
                        <StackPanel IsItemsHost="True" Orientation="Horizontal" KeyboardNavigation.DirectionalNavigation="Cycle" />
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key="{x:Static MenuItem.SeparatorStyleKey}" TargetType="Separator">
        <Setter Property="Margin" Value="6,4" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Separator">
                    <Border Height="1" Background="{x:Static app:UiTheme.BorderBrush}" />
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <ControlTemplate x:Key="MenuTopLevelHeader" TargetType="MenuItem">
        <Border x:Name="Chrome" CornerRadius="6" Padding="10,5" Background="Transparent">
            <Grid>
                <ContentPresenter ContentSource="Header" RecognizesAccessKey="True" VerticalAlignment="Center" />
                <Popup x:Name="PART_Popup" Placement="Bottom" VerticalOffset="6" AllowsTransparency="True" Focusable="False"
                       PopupAnimation="Fade" IsOpen="{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}">
                    <Border Background="{x:Static app:UiTheme.SurfaceBrush}" BorderBrush="{x:Static app:UiTheme.BorderBrush}"
                            BorderThickness="1" CornerRadius="8" Padding="4" MinWidth="200">
                        <StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle" Grid.IsSharedSizeScope="True" />
                    </Border>
                </Popup>
            </Grid>
        </Border>
        <ControlTemplate.Triggers>
            <Trigger Property="IsHighlighted" Value="True">
                <Setter TargetName="Chrome" Property="Background" Value="{x:Static app:UiTheme.AppBarRaisedBrush}" />
            </Trigger>
            <Trigger Property="IsSubmenuOpen" Value="True">
                <Setter TargetName="Chrome" Property="Background" Value="{x:Static app:UiTheme.AppBarRaisedBrush}" />
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
                <Setter Property="Opacity" Value="0.45" />
            </Trigger>
        </ControlTemplate.Triggers>
    </ControlTemplate>

    <ControlTemplate x:Key="MenuTopLevelItem" TargetType="MenuItem">
        <Border x:Name="Chrome" CornerRadius="6" Padding="10,5" Background="Transparent">
            <ContentPresenter ContentSource="Header" RecognizesAccessKey="True" VerticalAlignment="Center" />
        </Border>
        <ControlTemplate.Triggers>
            <Trigger Property="IsHighlighted" Value="True">
                <Setter TargetName="Chrome" Property="Background" Value="{x:Static app:UiTheme.AppBarRaisedBrush}" />
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
                <Setter Property="Opacity" Value="0.45" />
            </Trigger>
        </ControlTemplate.Triggers>
    </ControlTemplate>

    <ControlTemplate x:Key="MenuSubmenuItem" TargetType="MenuItem">
        <Border x:Name="Chrome" CornerRadius="4" Padding="8,0" MinHeight="28" Background="Transparent">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="20" />
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" SharedSizeGroup="MenuGesture" />
                </Grid.ColumnDefinitions>
                <TextBlock x:Name="Check" Text="&#xE73E;" FontFamily="{StaticResource IconFont}" FontSize="12"
                           Foreground="{x:Static app:UiTheme.AccentBrush}" VerticalAlignment="Center" Visibility="Collapsed" />
                <ContentPresenter Grid.Column="1" ContentSource="Header" RecognizesAccessKey="True" VerticalAlignment="Center" Margin="0,0,16,0" />
                <TextBlock Grid.Column="2" Text="{TemplateBinding InputGestureText}" Foreground="{x:Static app:UiTheme.TextMutedBrush}"
                           VerticalAlignment="Center" />
            </Grid>
        </Border>
        <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True">
                <Setter TargetName="Check" Property="Visibility" Value="Visible" />
            </Trigger>
            <Trigger Property="IsHighlighted" Value="True">
                <Setter TargetName="Chrome" Property="Background" Value="{x:Static app:UiTheme.NavSelectedBrush}" />
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
                <Setter Property="Opacity" Value="0.45" />
            </Trigger>
        </ControlTemplate.Triggers>
    </ControlTemplate>

    <ControlTemplate x:Key="MenuSubmenuHeader" TargetType="MenuItem">
        <Border x:Name="Chrome" CornerRadius="4" Padding="8,0" MinHeight="28" Background="Transparent">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="20" />
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <ContentPresenter Grid.Column="1" ContentSource="Header" RecognizesAccessKey="True" VerticalAlignment="Center" Margin="0,0,16,0" />
                <TextBlock Grid.Column="2" Text="&#xE76C;" FontFamily="{StaticResource IconFont}" FontSize="10"
                           Foreground="{x:Static app:UiTheme.TextMutedBrush}" VerticalAlignment="Center" />
                <Popup x:Name="PART_Popup" Placement="Right" HorizontalOffset="4" AllowsTransparency="True" Focusable="False"
                       PopupAnimation="Fade" IsOpen="{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}">
                    <Border Background="{x:Static app:UiTheme.SurfaceBrush}" BorderBrush="{x:Static app:UiTheme.BorderBrush}"
                            BorderThickness="1" CornerRadius="8" Padding="4" MinWidth="160">
                        <StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle" Grid.IsSharedSizeScope="True" />
                    </Border>
                </Popup>
            </Grid>
        </Border>
        <ControlTemplate.Triggers>
            <Trigger Property="IsHighlighted" Value="True">
                <Setter TargetName="Chrome" Property="Background" Value="{x:Static app:UiTheme.NavSelectedBrush}" />
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
                <Setter Property="Opacity" Value="0.45" />
            </Trigger>
        </ControlTemplate.Triggers>
    </ControlTemplate>

    <Style TargetType="MenuItem">
        <Setter Property="FocusVisualStyle" Value="{x:Null}" />
        <Setter Property="Template" Value="{StaticResource MenuSubmenuItem}" />
        <Style.Triggers>
            <Trigger Property="Role" Value="TopLevelHeader">
                <Setter Property="Template" Value="{StaticResource MenuTopLevelHeader}" />
            </Trigger>
            <Trigger Property="Role" Value="TopLevelItem">
                <Setter Property="Template" Value="{StaticResource MenuTopLevelItem}" />
            </Trigger>
            <Trigger Property="Role" Value="SubmenuHeader">
                <Setter Property="Template" Value="{StaticResource MenuSubmenuHeader}" />
                <Setter Property="Foreground" Value="{x:Static app:UiTheme.TextStrongBrush}" />
            </Trigger>
            <Trigger Property="Role" Value="SubmenuItem">
                <Setter Property="Foreground" Value="{x:Static app:UiTheme.TextStrongBrush}" />
            </Trigger>
        </Style.Triggers>
    </Style>

    <Style TargetType="ContextMenu">
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ContextMenu">
                    <Border Background="{x:Static app:UiTheme.SurfaceBrush}" BorderBrush="{x:Static app:UiTheme.BorderBrush}"
                            BorderThickness="1" CornerRadius="8" Padding="4" MinWidth="180">
                        <StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle" Grid.IsSharedSizeScope="True" />
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- 提示框 -->
    <Style TargetType="ToolTip">
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.AppBarTextBrush}" />
        <Setter Property="FontSize" Value="12" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ToolTip">
                    <Border Background="{x:Static app:UiTheme.TooltipBackgroundBrush}" CornerRadius="6" Padding="8,5" MaxWidth="420">
                        <ContentPresenter>
                            <ContentPresenter.Resources>
                                <Style TargetType="TextBlock">
                                    <Setter Property="TextWrapping" Value="Wrap" />
                                </Style>
                            </ContentPresenter.Resources>
                        </ContentPresenter>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- 一般 Expander -->
    <Style x:Key="ExpanderHeaderToggle" TargetType="ToggleButton">
        <Setter Property="FocusVisualStyle" Value="{StaticResource FocusRingVisual}" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ToggleButton">
                    <Border Background="Transparent" Padding="0,4">
                        <StackPanel Orientation="Horizontal">
                            <TextBlock x:Name="Chevron" Text="&#xE76C;" FontFamily="{StaticResource IconFont}" FontSize="10"
                                       Foreground="{x:Static app:UiTheme.TextMutedBrush}" VerticalAlignment="Center" Margin="0,0,8,0"
                                       RenderTransformOrigin="0.5,0.5" />
                            <ContentPresenter VerticalAlignment="Center" RecognizesAccessKey="True" />
                        </StackPanel>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="Chevron" Property="RenderTransform">
                                <Setter.Value>
                                    <RotateTransform Angle="90" />
                                </Setter.Value>
                            </Setter>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style TargetType="Expander">
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.TextStrongBrush}" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Expander">
                    <DockPanel>
                        <ToggleButton DockPanel.Dock="Top" Style="{StaticResource ExpanderHeaderToggle}" FontWeight="SemiBold"
                                      Content="{TemplateBinding Header}" ContentTemplate="{TemplateBinding HeaderTemplate}"
                                      IsChecked="{Binding IsExpanded, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}" />
                        <ContentPresenter x:Name="ExpandSite" Visibility="Collapsed" Margin="{TemplateBinding Padding}" />
                    </DockPanel>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsExpanded" Value="True">
                            <Setter TargetName="ExpandSite" Property="Visibility" Value="Visible" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- 表格 -->
    <Style TargetType="DataGrid">
        <Setter Property="Background" Value="{x:Static app:UiTheme.SurfaceBrush}" />
        <Setter Property="BorderBrush" Value="{x:Static app:UiTheme.BorderBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="GridLinesVisibility" Value="Horizontal" />
        <Setter Property="HorizontalGridLinesBrush" Value="{x:Static app:UiTheme.GridLineBrush}" />
        <Setter Property="VerticalGridLinesBrush" Value="Transparent" />
        <Setter Property="RowBackground" Value="{x:Static app:UiTheme.SurfaceBrush}" />
        <Setter Property="AlternatingRowBackground" Value="{x:Static app:UiTheme.SurfaceBrush}" />
        <Setter Property="HeadersVisibility" Value="Column" />
        <Setter Property="CanUserAddRows" Value="False" />
        <Setter Property="CanUserDeleteRows" Value="False" />
        <Setter Property="AutoGenerateColumns" Value="False" />
        <Setter Property="SelectionMode" Value="Single" />
        <Setter Property="MinRowHeight" Value="28" />
    </Style>

    <Style TargetType="DataGridColumnHeader">
        <Setter Property="Background" Value="{x:Static app:UiTheme.TableHeaderBrush}" />
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.RailNeutralStrongBrush}" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Padding" Value="8,6" />
        <Setter Property="BorderThickness" Value="0,0,0,1" />
        <Setter Property="BorderBrush" Value="{x:Static app:UiTheme.BorderBrush}" />
    </Style>

    <Style TargetType="DataGridRow">
        <Setter Property="BorderThickness" Value="3,0,0,0" />
        <Setter Property="BorderBrush" Value="Transparent" />
        <Style.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Background" Value="{x:Static app:UiTheme.NavHoverBrush}" />
            </Trigger>
            <Trigger Property="IsSelected" Value="True">
                <Setter Property="Background" Value="{x:Static app:UiTheme.NavSelectedBrush}" />
                <Setter Property="BorderBrush" Value="{x:Static app:UiTheme.AccentBrush}" />
            </Trigger>
        </Style.Triggers>
    </Style>

    <Style TargetType="DataGridCell">
        <Setter Property="BorderThickness" Value="0" />
        <Setter Property="FocusVisualStyle" Value="{x:Null}" />
        <Style.Triggers>
            <Trigger Property="IsSelected" Value="True">
                <Setter Property="Background" Value="Transparent" />
                <Setter Property="Foreground" Value="{x:Static app:UiTheme.TextStrongBrush}" />
                <Setter Property="BorderBrush" Value="Transparent" />
            </Trigger>
        </Style.Triggers>
    </Style>
</ResourceDictionary>
```

把 `App.xaml` 改成（相容資源鍵沿用 Task 1 的寫法；原本的所有 `Style` 搬進 `Controls.xaml` 後自 `App.xaml` 移除）：

```xml
<Application x:Class="MrtRouteSimulator.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:app="clr-namespace:MrtRouteSimulator.App"
             StartupUri="MainWindow.xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Themes/Controls.xaml" />
            </ResourceDictionary.MergedDictionaries>
            <!-- 既有資源鍵保留給各視窗引用；色值一律取自 UiTheme。 -->
            <SolidColorBrush x:Key="InkBrush" Color="{x:Static app:UiTheme.TextStrong}" />
            <SolidColorBrush x:Key="MutedBrush" Color="{x:Static app:UiTheme.TextMuted}" />
            <SolidColorBrush x:Key="PrimaryBrush" Color="{x:Static app:UiTheme.Primary}" />
            <SolidColorBrush x:Key="PrimaryDarkBrush" Color="{x:Static app:UiTheme.PrimaryHover}" />
            <SolidColorBrush x:Key="SurfaceBrush" Color="{x:Static app:UiTheme.Surface}" />
            <SolidColorBrush x:Key="BackgroundBrush" Color="{x:Static app:UiTheme.WindowBackground}" />
            <SolidColorBrush x:Key="BorderBrush" Color="{x:Static app:UiTheme.Border}" />
            <SolidColorBrush x:Key="SuccessBrush" Color="{x:Static app:UiTheme.Success}" />
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

- [ ] **Step 4: 執行，確認通過；並跑 visual-rules 與 interface-scale 子集**

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --shell-layout-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --visual-rules-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --interface-scale-only
```

Expected: `[通過] 共用控制項隱含樣式`；`PASS WPF visual rules only`；介面縮放兩行 PASS。若 visual-rules 的編輯器檢查因隱含樣式改變尺寸而失敗，先讀失敗訊息判斷是否為樣式造成的版面數值變化；只能調整樣式（例如按鈕 `MinHeight`），不得放寬測試。

- [ ] **Step 5: Commit**

```powershell
git add src/MrtRouteSimulator.App/Themes/Controls.xaml src/MrtRouteSimulator.App/App.xaml tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs
git commit -m "feat: add shared control styles sourced from UiTheme" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: `ShellTabControl`、`ShellNav` 與外殼範本

**Files:**
- Create: `src/MrtRouteSimulator.App/ShellTabControl.cs`
- Create: `src/MrtRouteSimulator.App/ShellNav.cs`
- Modify: `src/MrtRouteSimulator.App/Themes/Controls.xaml`
- Test: `tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class ShellTabControl : TabControl`，依賴屬性 `PageHeader`、`NavFooter`（`object?`，為邏輯子元素）。
  - `public static class ShellNav`，附加屬性 `ShortLabel`、`Icon`（`string`）與 `Get/Set` 方法。
  - 樣式鍵 `NavRailItem`、`NavRailTabControl`（`TargetType=app:ShellTabControl`）、`NavRailFooterButton`、`SegmentedItem`、`SegmentedTabControl`、`KpiStripToggle`、`KpiStripExpander`、`KpiLabel`、`KpiValue`。

- [ ] **Step 1: 寫失敗測試**

在 `Run` 加入 `VerifyShellControls();`，並加入：

```csharp
    private static void VerifyShellControls()
    {
        var tabs = new ShellTabControl { Style = (Style)Application.Current.FindResource("NavRailTabControl") };
        var header = new TextBlock { Text = "頁首" };
        var footer = new Button { Content = "起稿", Style = (Style)Application.Current.FindResource("NavRailFooterButton") };
        tabs.PageHeader = header;
        tabs.NavFooter = footer;
        for (var index = 0; index < 3; index++)
        {
            var item = new TabItem { Header = $"完整頁名{index}", Content = new TextBlock { Text = $"內容{index}" } };
            ShellNav.SetShortLabel(item, $"頁{index}");
            ShellNav.SetIcon(item, "");
            tabs.Items.Add(item);
        }
        var segmented = new TabControl { Style = (Style)Application.Current.FindResource("SegmentedTabControl") };
        foreach (var label in new[] { "配線圖", "列車狀態", "速度曲線" })
        {
            var item = new TabItem { Header = label + "完整名", Content = new TextBlock { Text = label } };
            ShellNav.SetShortLabel(item, label);
            segmented.Items.Add(item);
        }
        var strip = new Expander { Style = (Style)Application.Current.FindResource("KpiStripExpander"), IsExpanded = true,
            Content = new TextBlock { Text = "摘要" } };
        var host = new DockPanel();
        DockPanel.SetDock(strip, Dock.Top);
        DockPanel.SetDock(segmented, Dock.Top);
        host.Children.Add(strip);
        host.Children.Add(segmented);
        host.Children.Add(tabs);
        var window = new Window { Width = 700, Height = 520, Content = host };
        try
        {
            window.Show();
            window.Activate(); // TabItem 以滑鼠選取時需要取得焦點。
            PumpLayout(window);
            var items = tabs.Items.OfType<TabItem>().ToArray();
            var positions = items.Select(item => item.TranslatePoint(new Point(0, 0), tabs)).ToArray();
            Require(positions.All(point => point.X < 64) && positions.Zip(positions.Skip(1)).All(pair => pair.Second.Y > pair.First.Y),
                "導覽列項目必須在左側 64 px 內由上而下排列。");
            Require(header.TranslatePoint(new Point(0, 0), tabs).X >= 64
                    && header.TranslatePoint(new Point(0, 0), tabs).Y < ((FrameworkElement)items[0].Content).TranslatePoint(new Point(0, 0), tabs).Y,
                "PageHeader 必須顯示在內容區上方。");
            Require(footer.TranslatePoint(new Point(0, 0), tabs).X < 64
                    && footer.TranslatePoint(new Point(0, 0), tabs).Y > positions[^1].Y, "NavFooter 必須在導覽列底部。");
            Require(LogicalTreeHelper.GetParent(header) == tabs && LogicalTreeHelper.GetParent(footer) == tabs,
                "PageHeader／NavFooter 必須是 ShellTabControl 的邏輯子元素。");
            Require(items.All(item => Equals(item.ToolTip, item.Header) && AutomationProperties.GetName(item) == (string)item.Header),
                "導覽項目的提示框與自動化名稱必須是完整頁名。");
            items[2].RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
            PumpLayout(window);
            Require(tabs.SelectedIndex == 2, "點選導覽列項目必須切換頁面。");
            Require(items.All(item => item.Focusable && ReferenceEquals(item.FocusVisualStyle, Application.Current.FindResource("FocusRingVisual"))),
                "導覽項目必須可取得鍵盤焦點並使用 FocusRingVisual。");
            var pills = segmented.Items.OfType<TabItem>().Select(item => item.TranslatePoint(new Point(0, 0), segmented)).ToArray();
            Require(pills.Select(point => point.Y).Distinct().Count() == 1 && pills.Zip(pills.Skip(1)).All(pair => pair.Second.X > pair.First.X),
                "膠囊切換鈕必須水平排列。");
            Require(Math.Abs(strip.ActualHeight - 26) < 1, $"KPI 細條展開時高 26，實際 {strip.ActualHeight:0.0}。");
            strip.IsExpanded = false;
            PumpLayout(window);
            Require(strip.ActualHeight <= 12.5, $"KPI 細條收合時最多 12，實際 {strip.ActualHeight:0.0}。");
        }
        finally { window.Close(); }
        Console.WriteLine("[通過] ShellTabControl、導覽列、膠囊切換與 KPI 細條範本");
    }
```

- [ ] **Step 2: 執行，確認失敗**

執行共用指令。Expected: 建置失敗，`CS0246: The type or namespace name 'ShellTabControl' could not be found`。

- [ ] **Step 3: 實作**

建立 `src/MrtRouteSimulator.App/ShellTabControl.cs`：

```csharp
using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace MrtRouteSimulator.App;

/// <summary>
/// 主視窗外殼的頁面切換控制項。仍是 TabControl（既有切換程式、MCP 與測試照用），
/// 另以 PageHeader 承載所有頁面共用的頁首、以 NavFooter 承載導覽列底部按鈕。
/// </summary>
public sealed class ShellTabControl : TabControl
{
    public static readonly DependencyProperty PageHeaderProperty = DependencyProperty.Register(
        nameof(PageHeader), typeof(object), typeof(ShellTabControl),
        new FrameworkPropertyMetadata(null, OnLogicalContentChanged));

    public static readonly DependencyProperty NavFooterProperty = DependencyProperty.Register(
        nameof(NavFooter), typeof(object), typeof(ShellTabControl),
        new FrameworkPropertyMetadata(null, OnLogicalContentChanged));

    public object? PageHeader
    {
        get => GetValue(PageHeaderProperty);
        set => SetValue(PageHeaderProperty, value);
    }

    public object? NavFooter
    {
        get => GetValue(NavFooterProperty);
        set => SetValue(NavFooterProperty, value);
    }

    // 讓頁首與導覽列底部元件成為邏輯子元素，以繼承字型、資源與 DataContext。
    protected override IEnumerator LogicalChildren
    {
        get
        {
            var children = new List<object>();
            var baseChildren = base.LogicalChildren;
            while (baseChildren?.MoveNext() == true) children.Add(baseChildren.Current);
            if (PageHeader is not null) children.Add(PageHeader);
            if (NavFooter is not null) children.Add(NavFooter);
            return children.GetEnumerator();
        }
    }

    private static void OnLogicalContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ShellTabControl)d;
        if (e.OldValue is not null) control.RemoveLogicalChild(e.OldValue);
        if (e.NewValue is not null) control.AddLogicalChild(e.NewValue);
    }
}
```

建立 `src/MrtRouteSimulator.App/ShellNav.cs`：

```csharp
using System.Windows;

namespace MrtRouteSimulator.App;

/// <summary>導覽列與膠囊切換鈕顯示用的短標籤與圖示；TabItem.Header 仍保留完整頁名。</summary>
public static class ShellNav
{
    public static readonly DependencyProperty ShortLabelProperty = DependencyProperty.RegisterAttached(
        "ShortLabel", typeof(string), typeof(ShellNav), new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(string), typeof(ShellNav), new FrameworkPropertyMetadata(string.Empty));

    public static string GetShortLabel(DependencyObject element) => (string)element.GetValue(ShortLabelProperty);

    public static void SetShortLabel(DependencyObject element, string value) => element.SetValue(ShortLabelProperty, value);

    public static string GetIcon(DependencyObject element) => (string)element.GetValue(IconProperty);

    public static void SetIcon(DependencyObject element, string value) => element.SetValue(IconProperty, value);
}
```

在 `Controls.xaml` 的 `</ResourceDictionary>` 之前加入：

```xml
    <!-- 外殼：左側導覽列 -->
    <Style x:Key="NavRailItem" TargetType="TabItem">
        <Setter Property="FocusVisualStyle" Value="{StaticResource FocusRingVisual}" />
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.TextMutedBrush}" />
        <Setter Property="Width" Value="50" />
        <Setter Property="Height" Value="40" />
        <Setter Property="Margin" Value="0,0,0,4" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="ToolTip" Value="{Binding Header, RelativeSource={RelativeSource Self}}" />
        <Setter Property="AutomationProperties.Name" Value="{Binding Header, RelativeSource={RelativeSource Self}}" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="TabItem">
                    <Grid Background="Transparent">
                        <Border x:Name="Chrome" CornerRadius="8" Background="Transparent" />
                        <Border x:Name="Indicator" Width="3" HorizontalAlignment="Left" Margin="0,7" CornerRadius="1.5"
                                Background="{x:Static app:UiTheme.AccentBrush}" Visibility="Collapsed" />
                        <StackPanel VerticalAlignment="Center" HorizontalAlignment="Center">
                            <TextBlock Text="{Binding (app:ShellNav.Icon), RelativeSource={RelativeSource TemplatedParent}}"
                                       FontFamily="{StaticResource IconFont}" FontSize="16" HorizontalAlignment="Center" />
                            <TextBlock Text="{Binding (app:ShellNav.ShortLabel), RelativeSource={RelativeSource TemplatedParent}}"
                                       FontSize="11" HorizontalAlignment="Center" Margin="0,3,0,0" />
                        </StackPanel>
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Chrome" Property="Background" Value="{x:Static app:UiTheme.NavHoverBrush}" />
                        </Trigger>
                        <Trigger Property="IsSelected" Value="True">
                            <Setter TargetName="Chrome" Property="Background" Value="{x:Static app:UiTheme.NavSelectedBrush}" />
                            <Setter TargetName="Indicator" Property="Visibility" Value="Visible" />
                            <Setter Property="Foreground" Value="{x:Static app:UiTheme.AccentBrush}" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key="NavRailTabControl" TargetType="app:ShellTabControl">
        <Setter Property="TabStripPlacement" Value="Left" />
        <Setter Property="Padding" Value="12,8,12,10" />
        <Setter Property="ItemContainerStyle" Value="{StaticResource NavRailItem}" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="app:ShellTabControl">
                    <Grid KeyboardNavigation.TabNavigation="Local">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="64" />
                            <ColumnDefinition Width="*" />
                        </Grid.ColumnDefinitions>
                        <Border Background="{x:Static app:UiTheme.SurfaceBrush}" BorderBrush="{x:Static app:UiTheme.BorderBrush}" BorderThickness="0,0,1,0">
                            <DockPanel LastChildFill="False">
                                <ContentPresenter DockPanel.Dock="Bottom" Content="{TemplateBinding NavFooter}" Margin="7,0,7,8" />
                                <TabPanel x:Name="HeaderPanel" DockPanel.Dock="Top" IsItemsHost="True" Margin="7,8,7,0"
                                          KeyboardNavigation.TabIndex="1" />
                            </DockPanel>
                        </Border>
                        <DockPanel Grid.Column="1" Background="{x:Static app:UiTheme.WindowBackgroundBrush}">
                            <ContentPresenter DockPanel.Dock="Top" Content="{TemplateBinding PageHeader}" Margin="12,8,12,0" />
                            <ContentPresenter x:Name="PART_SelectedContentHost" ContentSource="SelectedContent" Margin="{TemplateBinding Padding}" />
                        </DockPanel>
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key="NavRailFooterButton" TargetType="Button" BasedOn="{StaticResource ButtonChrome}">
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="BorderThickness" Value="0" />
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.TextMutedBrush}" />
        <Setter Property="Width" Value="50" />
        <Setter Property="Height" Value="40" />
        <Setter Property="Padding" Value="0" />
        <Setter Property="ContentTemplate">
            <Setter.Value>
                <DataTemplate>
                    <StackPanel>
                        <TextBlock Text="&#xE70F;" FontFamily="{StaticResource IconFont}" FontSize="16" HorizontalAlignment="Center" />
                        <TextBlock Text="{Binding}" FontSize="11" HorizontalAlignment="Center" Margin="0,3,0,0" />
                    </StackPanel>
                </DataTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- 外殼：膠囊切換鈕（子分頁） -->
    <Style x:Key="SegmentedItem" TargetType="TabItem">
        <Setter Property="FocusVisualStyle" Value="{StaticResource FocusRingVisual}" />
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.TextMutedBrush}" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="ToolTip" Value="{Binding Header, RelativeSource={RelativeSource Self}}" />
        <Setter Property="AutomationProperties.Name" Value="{Binding Header, RelativeSource={RelativeSource Self}}" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="TabItem">
                    <Border x:Name="Chrome" CornerRadius="10" Background="Transparent" Padding="14,3" MinHeight="22">
                        <TextBlock Text="{Binding (app:ShellNav.ShortLabel), RelativeSource={RelativeSource TemplatedParent}}"
                                   FontSize="12" VerticalAlignment="Center" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter Property="Foreground" Value="{x:Static app:UiTheme.TextStrongBrush}" />
                        </Trigger>
                        <Trigger Property="IsSelected" Value="True">
                            <Setter TargetName="Chrome" Property="Background" Value="{x:Static app:UiTheme.SurfaceBrush}" />
                            <Setter Property="Foreground" Value="{x:Static app:UiTheme.TextStrongBrush}" />
                            <Setter Property="FontWeight" Value="SemiBold" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key="SegmentedTabControl" TargetType="TabControl">
        <Setter Property="ItemContainerStyle" Value="{StaticResource SegmentedItem}" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="TabControl">
                    <DockPanel KeyboardNavigation.TabNavigation="Local">
                        <Border DockPanel.Dock="Top" HorizontalAlignment="Left" Background="{x:Static app:UiTheme.BorderBrush}"
                                CornerRadius="12" Padding="2" Margin="0,0,0,8">
                            <TabPanel x:Name="HeaderPanel" IsItemsHost="True" KeyboardNavigation.TabIndex="1" />
                        </Border>
                        <ContentPresenter x:Name="PART_SelectedContentHost" ContentSource="SelectedContent" />
                    </DockPanel>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- 外殼：KPI 細條 -->
    <Style x:Key="KpiStripToggle" TargetType="ToggleButton">
        <Setter Property="FocusVisualStyle" Value="{StaticResource FocusRingVisual}" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ToggleButton">
                    <Border Background="Transparent" Width="24">
                        <TextBlock x:Name="Glyph" Text="&#xE70E;" FontFamily="{StaticResource IconFont}" FontSize="10"
                                   Foreground="{x:Static app:UiTheme.TextMutedBrush}" HorizontalAlignment="Center" VerticalAlignment="Center" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsChecked" Value="False">
                            <Setter TargetName="Glyph" Property="Text" Value="&#xE70D;" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key="KpiStripExpander" TargetType="Expander">
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Expander">
                    <Border x:Name="Strip" Background="{x:Static app:UiTheme.SurfaceBrush}" BorderBrush="{x:Static app:UiTheme.BorderBrush}"
                            BorderThickness="1" CornerRadius="6" Height="26">
                        <DockPanel>
                            <ToggleButton DockPanel.Dock="Right" Style="{StaticResource KpiStripToggle}"
                                          IsChecked="{Binding IsExpanded, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"
                                          ToolTip="收合或展開路線摘要" AutomationProperties.Name="收合或展開路線摘要" />
                            <ContentPresenter x:Name="ExpandSite" Margin="12,0,0,0" VerticalAlignment="Center" />
                        </DockPanel>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsExpanded" Value="False">
                            <Setter TargetName="ExpandSite" Property="Visibility" Value="Collapsed" />
                            <Setter TargetName="Strip" Property="Height" Value="12" />
                            <Setter TargetName="Strip" Property="BorderThickness" Value="0" />
                            <Setter TargetName="Strip" Property="Background" Value="Transparent" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key="KpiLabel" TargetType="TextBlock">
        <Setter Property="FontSize" Value="11" />
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.TextMutedBrush}" />
        <Setter Property="VerticalAlignment" Value="Center" />
        <Setter Property="Margin" Value="0,0,6,0" />
    </Style>

    <Style x:Key="KpiValue" TargetType="TextBlock">
        <Setter Property="FontSize" Value="12" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.TextStrongBrush}" />
        <Setter Property="VerticalAlignment" Value="Center" />
        <Setter Property="Margin" Value="0,0,18,0" />
        <Setter Property="MaxWidth" Value="220" />
        <Setter Property="TextTrimming" Value="CharacterEllipsis" />
        <Setter Property="ToolTip" Value="{Binding Text, RelativeSource={RelativeSource Self}}" />
    </Style>
```

- [ ] **Step 4: 執行，確認通過**

執行共用指令。Expected: `[通過] ShellTabControl、導覽列、膠囊切換與 KPI 細條範本`；建置 0 warning。

- [ ] **Step 5: Commit**

```powershell
git add src/MrtRouteSimulator.App/ShellTabControl.cs src/MrtRouteSimulator.App/ShellNav.cs src/MrtRouteSimulator.App/Themes/Controls.xaml tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs
git commit -m "feat: add shell tab control, nav rail and segmented templates" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: 主視窗外殼重排與快速起稿抽屜

**Files:**
- Modify: `src/MrtRouteSimulator.App/MainWindow.xaml`
- Modify: `src/MrtRouteSimulator.App/MainWindow.WorkspaceNavigation.cs`
- Modify: `src/MrtRouteSimulator.App/MainWindow.InputEditors.cs`（`FocusRouteInput_Click`）
- Modify: `src/MrtRouteSimulator.App/MainWindow.xaml.cs`（`FocusV2Settings_Click`）
- Modify: `src/MrtRouteSimulator.App/Themes/Controls.xaml`（移除已無引用的 `SummaryCard`）
- Modify: `tests/MrtRouteSimulator.WpfTests/OuterShellScrollTests.cs`
- Test: `tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs`

**Interfaces:**
- Consumes: Task 1～3 的樣式鍵、`ShellTabControl`、`ShellNav`。
- Produces: 新元件名稱 `AppBar`、`AppTitlePanel`、`MainMenu`、`PlaybackControlBar`、`QuickBuilderToggleButton`、`QuickBuilderCloseButton`、`StatusIndicatorDot`；方法 `SetQuickBuilderDrawerOpen(bool)`、`QuickBuilderToggle_Click`、`QuickBuilderClose_Click`、`QuickBuilderSidebar_KeyDown`；常數 `ShellLayoutTests.BaselineRouteViewportHeight`。

- [ ] **Step 1: 寫失敗測試**

在 `ShellLayoutTests` 類別開頭加入常數，數值替換為 Task 0 Step 4 印出的基準值（只填數字）：

```csharp
    // 改版前（c103ab3 + main 同步後）在 1280×800、100% 介面縮放下量得的配線圖可視高度。
    private const double BaselineRouteViewportHeight = 0;
```

在 `Run` 加入 `VerifyShellSkeleton(root);` 與 `VerifyQuickBuilderDrawer();`，並加入：

```csharp
    private static readonly (string Name, string Label)[] NavPages =
    [
        ("SimulationTabItem", "模擬"), ("ResultsTabItem", "時刻表"), ("SegmentTabItem", "區間"), ("ComparisonTabItem", "比較"),
        ("ResourceTabItem", "容量"), ("SafetyTabItem", "閉塞"), ("IntervalStatisticsTabItem", "統計"), ("DiagramTabItem", "運行圖")
    ];

    private static void VerifyShellSkeleton(string root)
    {
        var routeHeight = MeasureRouteViewportHeight(root);
        Require(BaselineRouteViewportHeight > 0 && routeHeight >= BaselineRouteViewportHeight + 120,
            $"配線圖可視高度應比改版前（{BaselineRouteViewportHeight:0.0}）多 120 px 以上，實際 {routeHeight:0.0}。");

        var previousScale = SetInterfaceScale(1);
        var window = new MainWindow { Width = 1280, Height = 800 };
        try
        {
            LoadSample(window, root, "10-小型-三站完整拓樸基準範例.mrtsim.json");
            window.Show();
            window.Activate(); // TabItem 以滑鼠選取時需要取得焦點。
            PumpLayout(window);
            var shellGrid = (Grid)window.FindName("ShellContentGrid")!;
            Require(shellGrid.RowDefinitions.Count == 3
                    && Math.Abs(shellGrid.RowDefinitions[0].ActualHeight - 48) < .5
                    && Math.Abs(shellGrid.RowDefinitions[2].ActualHeight - 24) < .5,
                "外殼必須是標題列 48、主體、狀態列 24 三列。");

            var tabs = (TabControl)window.FindName("WorkspaceTabControl")!;
            Require(tabs is ShellTabControl, "WorkspaceTabControl 必須是 ShellTabControl。");
            var items = tabs.Items.OfType<TabItem>().ToArray();
            Require(items.Select(item => item.Name).SequenceEqual(NavPages.Select(page => page.Name)), "導覽列必須依序有 8 個頁面。");
            var appBar = (Visual)window.FindName("AppBar")!;
            var play = (Button)window.FindName("PlayButton")!;
            foreach (var (item, page) in items.Zip(NavPages))
            {
                Require(ShellNav.GetShortLabel(item) == page.Label && ShellNav.GetIcon(item).Length == 1, $"{page.Name} 缺少導覽短標籤或圖示。");
                Require(item.TranslatePoint(new Point(0, 0), window).X < 64, $"{page.Name} 必須位於左側導覽列。");
                item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                PumpLayout(window);
                Require(ReferenceEquals(tabs.SelectedItem, item), $"點選導覽列 {page.Name} 必須切換頁面。");
                Require(play.IsVisible && play.IsDescendantOf(appBar), $"切到 {page.Name} 時播放控制必須仍在標題列中可見。");
            }

            tabs.SelectedItem = window.FindName("SimulationTabItem");
            var viewTabs = (TabControl)window.FindName("SimulationViewTabControl")!;
            viewTabs.SelectedIndex = 0;
            PumpLayout(window);
            Require(viewTabs.Items.OfType<TabItem>().Select(ShellNav.GetShortLabel).SequenceEqual(["配線圖", "列車狀態", "速度曲線"]),
                "子分頁膠囊必須顯示配線圖／列車狀態／速度曲線。");
            var summary = (Expander)window.FindName("RouteSummaryExpander")!;
            Require(summary.IsExpanded && Math.Abs(summary.ActualHeight - 26) < 1, $"KPI 細條預設展開且高 26，實際 {summary.ActualHeight:0.0}。");
            var rows = new[] { "RouteSummaryText", "OneWaySummaryText", "CycleSummaryText", "HeadwaySummaryText", "SpeedSummaryText" }
                .Select(name => ((FrameworkElement)window.FindName(name)!).TranslatePoint(new Point(0, 0), window).Y).ToArray();
            Require(rows.Max() - rows.Min() < 1, "五項摘要必須排在同一行。");

            var menu = (Menu)window.FindName("MainMenu")!;
            Require(menu.IsDescendantOf(appBar), "主選單必須在標題列中。");
            var headers = menu.Items.OfType<MenuItem>().Select(item => (string)item.Header).ToArray();
            Require(headers.SequenceEqual(["_檔案", "_編輯", "_顯示設定", "_原生驗收量測"]),
                $"頂層選單應為檔案／編輯／顯示設定／原生驗收量測，實際：{string.Join("、", headers)}");
            var edit = menu.Items.OfType<MenuItem>().Single(item => (string)item.Header == "_編輯");
            Require(edit.Items.OfType<MenuItem>().Select(AutomationProperties.GetName).SequenceEqual(["快速起稿", "軌道與設施工作區", "服務與路徑工作區", "模擬設定"]),
                "編輯選單必須依序有快速起稿、軌道與設施、服務與路徑、模擬設定。");
            foreach (var name in new[] { "ExportFixedTimetableArchiveMenuItem", "InterfaceScaleMenuItem", "ShowLockedRoutesMenuItem",
                         "ShowTrackOccupancyMenuItem", "StartNativeAcceptanceMeasurementMenuItem", "StopNativeAcceptanceMeasurementMenuItem" })
                Require(window.FindName(name) is MenuItem, $"選單項目 {name} 必須保留。");
            Require(!menu.Items.OfType<MenuItem>().Any(item => AutomationProperties.GetName(item) == "分析結果"), "頂層「分析結果」應已移除。");
        }
        finally
        {
            WpfTestWait.Close(window);
            SetInterfaceScale(previousScale);
        }
        Console.WriteLine("[通過] 外殼骨架：標題列、導覽列、播放列、KPI 細條、選單與配線圖高度");
    }

    private static void VerifyQuickBuilderDrawer()
    {
        var previousScale = SetInterfaceScale(1);
        var window = new MainWindow { Width = 1280, Height = 800 };
        try
        {
            window.Show();
            PumpLayout(window);
            var drawer = (Border)window.FindName("QuickBuilderSidebar")!;
            var toggle = (Button)window.FindName("QuickBuilderToggleButton")!;
            var close = (Button)window.FindName("QuickBuilderCloseButton")!;
            var input = (FrameworkElement)window.FindName("QuickBuilderInputPanel")!;
            Require(drawer.Visibility == Visibility.Collapsed, "快速起稿抽屜預設必須收起。");
            WpfTestWait.Invoke(window, "FocusRouteInput_Click", window, new RoutedEventArgs());
            PumpLayout(window);
            Require(drawer.Visibility == Visibility.Visible, "選單「快速起稿」必須開啟抽屜。");
            close.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            PumpLayout(window);
            Require(drawer.Visibility == Visibility.Collapsed, "關閉鈕必須關閉抽屜。");
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            PumpLayout(window);
            Require(drawer.Visibility == Visibility.Visible, "「起稿」鈕必須開啟抽屜。");
            drawer.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(drawer)!, 0, Key.Escape)
                { RoutedEvent = Keyboard.KeyDownEvent });
            PumpLayout(window);
            Require(drawer.Visibility == Visibility.Collapsed, "抽屜內按 Esc 必須關閉抽屜。");
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            PumpLayout(window);
            Require(drawer.Visibility == Visibility.Collapsed, "再按一次「起稿」必須關閉抽屜。");
            WpfTestWait.Invoke(window, "SetQuickBuilderState", true, false);
            Require(!input.IsEnabled && drawer.Visibility == Visibility.Collapsed, "locked 只停用輸入，不得自動開啟抽屜。");
            WpfTestWait.Invoke(window, "SetQuickBuilderState", false, false);
            Require(input.IsEnabled && drawer.Visibility == Visibility.Collapsed, "解鎖不得自動開啟抽屜。");
        }
        finally
        {
            WpfTestWait.Close(window);
            SetInterfaceScale(previousScale);
        }
        Console.WriteLine("[通過] 快速起稿抽屜：預設收起、選單／起稿鈕開啟、關閉鈕／Esc／再按關閉");
    }
```

`OuterShellScrollTests.cs`：

1. `VerifyProxyDoesNotOverlapStatusRow` 中把 `Grid.GetRow(child) != 3` 改為 `Grid.GetRow(child) != 2`（狀態列改在第 2 列）。
2. 把 `VerifyUnbuiltStartupLayout` 的 `try` 區塊內容替換為：

```csharp
            window.Show();
            PumpLayout(window);
            var drawer = (Border)window.FindName("QuickBuilderSidebar")!;
            var toggle = (Button)window.FindName("QuickBuilderToggleButton")!;
            var shellGrid = (Grid)window.FindName("ShellContentGrid")!;
            var route = (ScrollViewer)window.FindName("RouteScrollViewer")!;
            var proxy = (ScrollBar)window.FindName("ShellRouteHorizontalScrollBar")!;
            var calculate = typeof(MainWindow).GetMethod("CalculateRouteCanvasWidth",
                BindingFlags.Static | BindingFlags.NonPublic, null,
                [typeof(double), typeof(int), typeof(double)], null)!;
            Require((double)calculate.Invoke(null, [400d, 0, 2d])! == 400d,
                "未建立路線的空白畫布不得套用 200% 路線縮放。");
            Require(drawer.Visibility == Visibility.Collapsed, "快速起稿抽屜預設必須收起。");
            var routeWidthClosed = route.ActualWidth;
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            PumpLayout(window);
            Require(drawer.Visibility == Visibility.Visible && drawer.ActualWidth >= 319.5 && drawer.ActualWidth <= 330,
                $"800 DIP 抽屜寬度應為 320；visible={drawer.Visibility}, width={drawer.ActualWidth}, total={shellGrid.ActualWidth}。");
            Require(Math.Abs(route.ActualWidth - routeWidthClosed) < 1, "抽屜覆蓋在主體上，不得改變配線圖寬度。");
            var stationGrid = (DataGrid)window.FindName("StationDataGrid")!;
            Require(stationGrid.Columns[1].ActualWidth >= 80,
                "小視窗車站表的站名欄不得被壓成不可讀的窄欄。");
            stationGrid.ApplyTemplate();
            var stationScroll = (ScrollViewer)stationGrid.Template.FindName("DG_ScrollViewer", stationGrid)!;
            Require(stationScroll.ScrollableWidth > 1
                && stationScroll.ComputedHorizontalScrollBarVisibility == Visibility.Visible,
                $"小視窗車站表須有自己的水平滑桿；scrollable={stationScroll.ScrollableWidth}, visible={stationScroll.ComputedHorizontalScrollBarVisibility}, setting={stationScroll.HorizontalScrollBarVisibility}, grid={stationGrid.ActualWidth}, extent={stationScroll.ExtentWidth}, viewport={stationScroll.ViewportWidth}。");
            stationScroll.ScrollToRightEnd();
            PumpLayout(window);
            Require(stationScroll.HorizontalOffset > 1, "車站表水平滑桿須能實際到達右側欄位。");
            typeof(MainWindow).GetField("_routeMapHorizontalZoom", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(window, 2d);
            typeof(MainWindow).GetMethod("DrawRoute", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, [null]);
            PumpLayout(window);
            Require(route.ScrollableWidth < 1 && proxy.Visibility == Visibility.Collapsed,
                $"未讀檔／未建立時不應有空白水平捲軸；overflow={route.ScrollableWidth}, proxy={proxy.Visibility}。");
            window.Width = 1400;
            PumpLayout(window);
            Require(Math.Abs(drawer.ActualWidth - 450) < 1,
                $"寬視窗抽屜應保留 450 上限；actual={drawer.ActualWidth}。");
            window.Width = 800;
            PumpLayout(window);
            Require(route.ScrollableWidth < 1 && proxy.Visibility == Visibility.Collapsed,
                $"寬窗縮回小窗後空白畫布不得殘留溢位；overflow={route.ScrollableWidth}, proxy={proxy.Visibility}。");
            typeof(MainWindow).GetMethod("SetQuickBuilderState", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, [true, true]);
            PumpLayout(window);
            Require(drawer.Visibility == Visibility.Collapsed, "SetQuickBuilderState(collapsed) 必須關閉抽屜。");
            window.Width = 820;
            PumpLayout(window);
            Require(drawer.Visibility == Visibility.Collapsed, "視窗縮放不得重新打開已收合的抽屜。");
```

（`OuterShellScrollTests.cs` 檔頭若缺少 `using System.Windows.Controls.Primitives;`，一併加入以使用 `ButtonBase`。）

- [ ] **Step 2: 執行，確認失敗**

執行共用指令。Expected: 失敗於 `外殼必須是標題列 48、主體、狀態列 24 三列。`（或 `WorkspaceTabControl 必須是 ShellTabControl。`）。

- [ ] **Step 3: 重排 `MainWindow.xaml`**

1. 根元素 `<Window ...>` 加入 `xmlns:app="clr-namespace:MrtRouteSimulator.App"`。

2. 把自 `    <Grid x:Name="ShellContentGrid" Height="720">` 起、到 `        </Menu>`（含）為止的整段，替換為：

```xml
    <Grid x:Name="ShellContentGrid" Height="720">
        <Grid.RowDefinitions>
            <RowDefinition Height="48" />
            <RowDefinition Height="*" />
            <RowDefinition Height="24" />
        </Grid.RowDefinitions>

        <Border x:Name="AppBar" Grid.Row="0" Background="{x:Static app:UiTheme.AppBarBrush}">
            <Grid Margin="14,0,10,0">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <StackPanel x:Name="AppTitlePanel" VerticalAlignment="Center" Margin="0,0,12,0"
                            ToolTip="{Binding Text, ElementName=VersionSummaryText}">
                    <TextBlock Text="MRT 路線進出站時間模擬器" Foreground="{x:Static app:UiTheme.AppBarTextBrush}"
                               FontSize="13" FontWeight="SemiBold" TextTrimming="CharacterEllipsis" />
                    <TextBlock x:Name="CurrentProjectFileTextBlock" Text="目前存檔：尚未讀取（示範資料）"
                               Foreground="{x:Static app:UiTheme.AppBarMutedBrush}" FontSize="11" Margin="0,1,0,0"
                               TextTrimming="CharacterEllipsis" AutomationProperties.Name="目前存檔" />
                    <TextBlock x:Name="VersionSummaryText" Visibility="Collapsed" />
                </StackPanel>
                <Menu x:Name="MainMenu" Grid.Column="1" VerticalAlignment="Center" Margin="0,0,12,0"
                      KeyboardNavigation.TabNavigation="Continue" AutomationProperties.Name="主選單">
                    <MenuItem Header="_檔案" AutomationProperties.Name="檔案選單">
                        <MenuItem Header="存檔" Click="SaveProject_Click" AutomationProperties.Name="儲存模擬專案" />
                        <MenuItem Header="讀取存檔" Click="LoadProject_Click" AutomationProperties.Name="讀取模擬專案" />
                        <Separator />
                        <MenuItem x:Name="ExportFixedTimetableArchiveMenuItem" Header="匯出完成後固定時刻表…" Click="ExportFixedTimetableArchive_Click" AutomationProperties.Name="匯出完成後固定時刻表" />
                    </MenuItem>
                    <MenuItem Header="_編輯" AutomationProperties.Name="編輯選單">
                        <MenuItem Header="快速起稿" Click="FocusRouteInput_Click" AutomationProperties.Name="快速起稿" />
                        <Separator />
                        <MenuItem Header="軌道與設施…" Click="OpenInfrastructureWorkspace_Click" AutomationProperties.Name="軌道與設施工作區" />
                        <MenuItem Header="服務與路徑…" Click="OpenOperationsWorkspace_Click" AutomationProperties.Name="服務與路徑工作區" />
                        <MenuItem Header="模擬設定…" Click="OpenSimulationWorkspace_Click" AutomationProperties.Name="模擬設定" />
                    </MenuItem>
                    <MenuItem Header="_顯示設定" AutomationProperties.Name="顯示設定">
                        <MenuItem x:Name="InterfaceScaleMenuItem" Header="介面縮放（軟體設定）"
                                  AutomationProperties.Name="介面縮放"
                                  ToolTip="同步調整字體、按鈕與間距；下次啟動沿用，不影響路線存檔或模擬數據。">
                            <MenuItem Header="80%（精簡）" Tag="0.8" IsCheckable="True" Click="InterfaceScale_Click" />
                            <MenuItem Header="90%" Tag="0.9" IsCheckable="True" Click="InterfaceScale_Click" />
                            <MenuItem Header="100%（恢復預設）" Tag="1" IsCheckable="True" Click="InterfaceScale_Click" />
                            <MenuItem Header="110%" Tag="1.1" IsCheckable="True" Click="InterfaceScale_Click" />
                            <MenuItem Header="125%" Tag="1.25" IsCheckable="True" Click="InterfaceScale_Click" />
                        </MenuItem>
                        <Separator />
                        <MenuItem x:Name="ShowLockedRoutesMenuItem"
                                  Header="顯示列車已鎖定的前方進路"
                                  IsCheckable="True"
                                  Click="RouteLockVisibility_Changed"
                                  ToolTip="僅標示模擬中已成功預約的實體進路；未取得進路的待避列車不標示。"
                                  AutomationProperties.Name="顯示已鎖定進路" />
                        <MenuItem x:Name="ShowTrackOccupancyMenuItem"
                                  Header="顯示區段占用亮燈"
                                  IsCheckable="True"
                                  Click="TrackOccupancyVisibility_Changed"
                                  ToolTip="以黃色光帶標示列車目前占用的整條軌道區段；資料直接取自模擬核心。"
                                  AutomationProperties.Name="顯示區段占用亮燈" />
                    </MenuItem>
                    <MenuItem Header="_原生驗收量測" AutomationProperties.Name="原生驗收量測">
                        <MenuItem Header="播放／繼續播放" Tag="play" Click="Play_Click" AutomationProperties.Name="驗收正常播放／繼續播放" />
                        <MenuItem Header="暫停" Tag="pause" Click="Pause_Click" AutomationProperties.Name="驗收正常暫停" />
                        <MenuItem Header="重設播放" Tag="reset" Click="ResetPlayback_Click" AutomationProperties.Name="驗收正常重設播放" />
                        <Separator />
                        <MenuItem x:Name="StartNativeAcceptanceMeasurementMenuItem"
                                  Header="開始原生驗收量測"
                                  Click="NativeAcceptanceMeasurement_Start_Click"
                                  AutomationProperties.Name="開始原生驗收量測"
                                  ToolTip="只建立量測 session；下一次正常播放才開始採集。" />
                        <MenuItem x:Name="StopNativeAcceptanceMeasurementMenuItem"
                                  Header="停止原生驗收量測"
                                  Click="NativeAcceptanceMeasurement_Stop_Click"
                                  AutomationProperties.Name="停止原生驗收量測"
                                  IsEnabled="False"
                                  ToolTip="停止量測並 flush JSONL，不會改變播放狀態。" />
                    </MenuItem>
                </Menu>
                <StackPanel x:Name="PlaybackControlBar" Grid.Column="2" Orientation="Horizontal" VerticalAlignment="Center"
                            AutomationProperties.Name="播放控制">
                    <Button Style="{StaticResource AppBarButton}" Content="&#xE710;" Click="RunSimulation_Click"
                            ToolTip="建立模擬" AutomationProperties.Name="建立模擬" />
                    <Button x:Name="PlayButton" Style="{StaticResource AppBarPrimaryButton}" Content="&#xE768;" Click="Play_Click"
                            IsEnabled="False" ToolTip="播放" AutomationProperties.Name="▶ 播放" />
                    <Button Style="{StaticResource AppBarButton}" Content="&#xE769;" Click="Pause_Click"
                            ToolTip="暫停" AutomationProperties.Name="Ⅱ 暫停" />
                    <Button Style="{StaticResource AppBarButton}" Content="&#xE72C;" Click="ResetPlayback_Click"
                            ToolTip="重設" AutomationProperties.Name="↺ 重設" />
                    <Button x:Name="ObstacleStopButton" Style="{StaticResource AppBarButton}" Content="&#xE7BA;" Click="ObstacleStop_Click"
                            IsEnabled="False" ToolTip="障礙物急停" AutomationProperties.Name="⚠ 障礙物急停"
                            Foreground="{x:Static app:UiTheme.RailDownSoftBrush}" />
                    <ComboBox x:Name="PlaybackSpeedComboBox" Width="70" Height="28" MinHeight="28" Margin="4,0,8,0" SelectedIndex="2"
                              SelectionChanged="PlaybackSpeed_SelectionChanged" ToolTip="播放倍率" AutomationProperties.Name="播放倍率">
                        <ComboBoxItem Content="1×" Tag="1" />
                        <ComboBoxItem Content="10×" Tag="10" />
                        <ComboBoxItem Content="30×" Tag="30" />
                        <ComboBoxItem Content="60×" Tag="60" />
                    </ComboBox>
                    <TextBlock x:Name="SimulationClockText" Text="--:--:--" Foreground="{x:Static app:UiTheme.TimeAccentBrush}"
                               FontFamily="{StaticResource MonoFont}" FontSize="14" FontWeight="SemiBold" VerticalAlignment="Center"
                               MinWidth="72" TextAlignment="Right" AutomationProperties.Name="模擬時鐘" />
                </StackPanel>
            </Grid>
        </Border>
```

3. 把下列片段：

```xml
        <Grid Grid.Row="2">
            <Grid.ColumnDefinitions>
                <ColumnDefinition x:Name="QuickBuilderColumn" Width="300" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>

            <Border x:Name="QuickBuilderSidebar" Grid.Column="0" Background="#F8F9FB" BorderBrush="{StaticResource BorderBrush}" BorderThickness="0,0,1,0">
                <ScrollViewer x:Name="ConfigurationScrollViewer" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
                    <StackPanel x:Name="QuickBuilderInputPanel" Margin="18,16,18,22">
```

替換為：

```xml
        <Grid Grid.Row="1">
            <Border x:Name="QuickBuilderSidebar" Panel.ZIndex="20" HorizontalAlignment="Left" Width="360" Margin="64,0,0,0"
                    Visibility="Collapsed" Background="{x:Static app:UiTheme.SurfaceBrush}"
                    BorderBrush="{x:Static app:UiTheme.HairlineBrush}" BorderThickness="0,0,1,0"
                    KeyDown="QuickBuilderSidebar_KeyDown" AutomationProperties.Name="快速起稿抽屜">
                <DockPanel>
                    <Grid DockPanel.Dock="Top" Height="40" Margin="16,0,8,0">
                        <TextBlock Text="快速建立線性路線" FontSize="13" FontWeight="SemiBold" VerticalAlignment="Center"
                                   Foreground="{x:Static app:UiTheme.TextStrongBrush}" />
                        <Button x:Name="QuickBuilderCloseButton" Style="{StaticResource GhostButton}" HorizontalAlignment="Right"
                                Width="28" Height="28" MinHeight="0" Padding="0" FontFamily="{StaticResource IconFont}" FontSize="12"
                                Content="&#xE711;" Click="QuickBuilderClose_Click" ToolTip="關閉快速起稿" AutomationProperties.Name="關閉快速起稿" />
                    </Grid>
                <ScrollViewer x:Name="ConfigurationScrollViewer" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
                    <StackPanel x:Name="QuickBuilderInputPanel" Margin="18,4,18,22">
```

4. 快速起稿內容結尾的：

```xml
                    </StackPanel>
                </ScrollViewer>
            </Border>

            <Grid Grid.Column="1" Margin="12">
```

起，到 `<TabControl x:Name="WorkspaceTabControl" ...>`（含）為止的整段（包含 `RouteSummaryExpander`、`ValidationBorder`），替換為：

```xml
                    </StackPanel>
                </ScrollViewer>
                </DockPanel>
            </Border>

            <app:ShellTabControl x:Name="WorkspaceTabControl" Style="{StaticResource NavRailTabControl}"
                                 SelectionChanged="WorkspaceTabControl_SelectionChanged" AutomationProperties.Name="工作區頁面">
                <app:ShellTabControl.NavFooter>
                    <StackPanel>
                        <Border Height="1" Background="{x:Static app:UiTheme.BorderBrush}" Margin="4,0,4,6" />
                        <Button x:Name="QuickBuilderToggleButton" Style="{StaticResource NavRailFooterButton}" Content="起稿"
                                Click="QuickBuilderToggle_Click" ToolTip="快速建立線性路線（一次性起稿）"
                                AutomationProperties.Name="快速起稿抽屜開關" />
                    </StackPanel>
                </app:ShellTabControl.NavFooter>
                <app:ShellTabControl.PageHeader>
                    <StackPanel>
                        <Expander x:Name="RouteSummaryExpander" Style="{StaticResource KpiStripExpander}" Header="路線摘要（可收合）" IsExpanded="True">
                            <ScrollViewer x:Name="RouteSummaryScrollViewer" MaxHeight="100"
                                          VerticalScrollBarVisibility="Disabled" HorizontalScrollBarVisibility="Hidden">
                                <StackPanel Orientation="Horizontal">
                                    <TextBlock Text="車站" Style="{StaticResource KpiLabel}" />
                                    <TextBlock x:Name="RouteSummaryText" Text="—" Style="{StaticResource KpiValue}" />
                                    <TextBlock Text="單程" Style="{StaticResource KpiLabel}" />
                                    <TextBlock x:Name="OneWaySummaryText" Text="—" Style="{StaticResource KpiValue}" />
                                    <TextBlock Text="全程" Style="{StaticResource KpiLabel}" />
                                    <TextBlock x:Name="CycleSummaryText" Text="—" Style="{StaticResource KpiValue}" />
                                    <TextBlock Text="班距" Style="{StaticResource KpiLabel}" />
                                    <TextBlock x:Name="HeadwaySummaryText" Text="—" Style="{StaticResource KpiValue}" />
                                    <TextBlock Text="峰值" Style="{StaticResource KpiLabel}" />
                                    <TextBlock x:Name="SpeedSummaryText" Text="—" Style="{StaticResource KpiValue}" />
                                </StackPanel>
                            </ScrollViewer>
                        </Expander>
                        <Border x:Name="ValidationBorder" Visibility="Collapsed" Margin="0,8,0,0"
                                Background="{x:Static app:UiTheme.WarningSurfaceBrush}" BorderBrush="{x:Static app:UiTheme.WarningBorderBrush}"
                                BorderThickness="1" CornerRadius="6">
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="4" />
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="Auto" />
                                </Grid.ColumnDefinitions>
                                <Border Background="{x:Static app:UiTheme.PrimaryBrush}" CornerRadius="6,0,0,6" />
                                <ScrollViewer Grid.Column="1" MaxHeight="140" VerticalScrollBarVisibility="Auto"
                                              HorizontalScrollBarVisibility="Disabled" Margin="10,7,8,7">
                                    <TextBlock x:Name="ValidationTextBlock" Foreground="{x:Static app:UiTheme.WarningTextBrush}"
                                               TextWrapping="Wrap" FontSize="12" />
                                </ScrollViewer>
                                <Button x:Name="HideValidationButton" Grid.Column="2" Content="關閉警告" Style="{StaticResource GhostButton}"
                                        Background="Transparent" Foreground="{x:Static app:UiTheme.WarningTextBrush}"
                                        Click="HideValidation_Click" AutomationProperties.Name="關閉警告"
                                        VerticalAlignment="Top" Margin="0,4,6,4" />
                            </Grid>
                        </Border>
                    </StackPanel>
                </app:ShellTabControl.PageHeader>
```

5. 「模擬動畫」頁：把自 `<TabItem x:Name="SimulationTabItem" Header="模擬動畫">` 起、到 `SimulationViewTabControl` 開始標籤（含 `AutomationProperties.Name="模擬即時檢視">`）為止的整段，替換為：

```xml
                    <TabItem x:Name="SimulationTabItem" Header="模擬動畫" app:ShellNav.ShortLabel="模擬" app:ShellNav.Icon="&#xE768;">
                        <Grid>
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto" />
                                <RowDefinition Height="*" />
                            </Grid.RowDefinitions>
                            <TextBlock x:Name="PlaybackStatusText" Grid.Row="1" Text="請先建立模擬" Panel.ZIndex="1"
                                       Foreground="{x:Static app:UiTheme.TextMutedBrush}" FontSize="12"
                                       HorizontalAlignment="Right" VerticalAlignment="Top" Margin="300,4,2,0"
                                       TextTrimming="CharacterEllipsis" ToolTip="{Binding Text, RelativeSource={RelativeSource Self}}" />
                            <TabControl x:Name="SimulationViewTabControl" Grid.Row="1" Style="{StaticResource SegmentedTabControl}"
                                        SelectionChanged="SimulationViewTabControl_SelectionChanged"
                                        AutomationProperties.Name="模擬即時檢視">
```

並修改三個子分頁的開始標籤：

```xml
                                <TabItem Header="軌道配線與列車位置" app:ShellNav.ShortLabel="配線圖">
                                <TabItem Header="即時列車狀態" app:ShellNav.ShortLabel="列車狀態">
                                <TabItem x:Name="SpeedProfileTabItem" Header="完整行程速度曲線" app:ShellNav.ShortLabel="速度曲線">
```

6. 其餘七個主頁面的開始標籤加上短標籤與圖示：

```xml
                    <TabItem x:Name="ResultsTabItem" Header="進出站時刻表" app:ShellNav.ShortLabel="時刻表" app:ShellNav.Icon="&#xE787;">
                    <TabItem x:Name="SegmentTabItem" Header="區間物理明細" app:ShellNav.ShortLabel="區間" app:ShellNav.Icon="&#xEA37;">
                    <TabItem x:Name="ComparisonTabItem" Header="V1／V2 同條件比較" app:ShellNav.ShortLabel="比較" app:ShellNav.Icon="&#xE8AB;">
                    <TabItem x:Name="ResourceTabItem" Header="資源占用與觀測容量" app:ShellNav.ShortLabel="容量" app:ShellNav.Icon="&#xE8A9;">
                    <TabItem x:Name="SafetyTabItem" Header="移動閉塞與煞車" app:ShellNav.ShortLabel="閉塞" app:ShellNav.Icon="&#xEA18;">
                    <TabItem x:Name="IntervalStatisticsTabItem" Header="V2 區間統計" app:ShellNav.ShortLabel="統計" app:ShellNav.Icon="&#xE8EF;">
                    <TabItem x:Name="DiagramTabItem" Header="列車運行圖／匯出" app:ShellNav.ShortLabel="運行圖" app:ShellNav.Icon="&#xE9D2;">
```

7. 把主分頁結尾的

```xml
                </TabControl>
            </Grid>
        </Grid>
```

（`DiagramTabItem` 之後、狀態列之前）替換為：

```xml
                </app:ShellTabControl>
        </Grid>
```

8. 把整個狀態列 `<Border Grid.Row="3" Background="#202C43">…</Border>` 替換為：

```xml
        <Border Grid.Row="2" Background="{x:Static app:UiTheme.StatusBarBrush}" BorderBrush="{x:Static app:UiTheme.BorderBrush}" BorderThickness="0,1,0,0">
            <Grid Margin="12,0">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <Ellipse x:Name="StatusIndicatorDot" Width="8" Height="8" Margin="0,0,8,0" VerticalAlignment="Center"
                         Fill="{x:Static app:UiTheme.TextSubtleBrush}" />
                <TextBlock x:Name="StatusTextBlock" Grid.Column="1" Text="就緒。請調整參數後建立模擬。"
                           Foreground="{x:Static app:UiTheme.RailNeutralStrongBrush}" VerticalAlignment="Center" FontSize="12"
                           TextTrimming="CharacterEllipsis" ToolTip="{Binding Text, RelativeSource={RelativeSource Self}}" />
                <TextBlock Grid.Column="2" Text="完全離線 · 核心單位 m · s · m/s · m/s²" Foreground="{x:Static app:UiTheme.TextMutedBrush}"
                           VerticalAlignment="Center" FontSize="11" Margin="12,0,0,0" />
            </Grid>
        </Border>
```

9. `Controls.xaml` 移除已無引用的 `SummaryCard` 樣式。

- [ ] **Step 4: 改寫抽屜邏輯**

把 `MainWindow.WorkspaceNavigation.cs` 整個檔案替換為：

```csharp
using System.Windows;
using System.Windows.Input;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

public partial class MainWindow
{
    private const double QuickBuilderExpandedWidth = 450;
    private const double QuickBuilderMinimumWidth = 320;
    private const double NavRailWidth = 64;

    private void OpenInfrastructureWorkspace_Click(object sender, RoutedEventArgs e) =>
        OpenTopologyWorkspace(ProjectWorkspacePage.Tracks);

    private void OpenOperationsWorkspace_Click(object sender, RoutedEventArgs e) =>
        OpenTopologyWorkspace(ProjectWorkspacePage.Services);

    private void OpenSimulationWorkspace_Click(object sender, RoutedEventArgs e) =>
        OpenTopologyWorkspace(ProjectWorkspacePage.Simulation);

    private async void OpenTopologyWorkspace(ProjectWorkspacePage initialPage)
    {
        HideValidation();
        try
        {
            var document = _activeTopologyProjectDocument
                ?? TopologyProjectFactory.CreateLinearDraft(CaptureProjectDocument());
            var editor = new TopologyEditorWindow(document, initialPage) { Owner = this };
            if (editor.ShowDialog() != true || editor.Result is null)
            {
                StatusTextBlock.Text = "已取消專案工作區變更；目前專案未變更。";
                return;
            }

            await ConfigureTopologyProjectForPlaybackAsync(editor.Result);
            StatusTextBlock.Text = "專案工作區變更已套用；可直接播放或前往分析結果。";
        }
        catch (SimulationValidationException exception)
        {
            ShowValidation(exception.Errors);
            StatusTextBlock.Text = "專案工作區未能建立或套用；目前專案未變更。";
        }
        catch (InvalidOperationException exception)
        {
            ShowValidation([exception.Message]);
            StatusTextBlock.Text = "專案工作區未能建立或套用；目前專案未變更。";
        }
    }

    // locked：停用快速起稿輸入；collapsed：關閉抽屜。抽屜只在使用者明確要求時開啟
    // （起稿鈕、選單「快速起稿」、前往 V2 設定），避免啟動或清除結果時自動彈出。
    private void SetQuickBuilderState(bool locked, bool collapsed)
    {
        ConfigurationScrollViewer.IsEnabled = true;
        QuickBuilderInputPanel.IsEnabled = !locked;
        if (collapsed) SetQuickBuilderDrawerOpen(false);
    }

    private void SetQuickBuilderDrawerOpen(bool open)
    {
        QuickBuilderSidebar.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        UpdateQuickBuilderWidth();
    }

    private void QuickBuilderToggle_Click(object sender, RoutedEventArgs e)
    {
        if (QuickBuilderSidebar.Visibility == Visibility.Visible) SetQuickBuilderDrawerOpen(false);
        else FocusRouteInput_Click(sender, e);
    }

    private void QuickBuilderClose_Click(object sender, RoutedEventArgs e) => SetQuickBuilderDrawerOpen(false);

    private void QuickBuilderSidebar_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        SetQuickBuilderDrawerOpen(false);
        QuickBuilderToggleButton.Focus();
        e.Handled = true;
    }

    private void UpdateQuickBuilderWidth()
    {
        if (QuickBuilderSidebar is null || QuickBuilderSidebar.Visibility == Visibility.Collapsed) return;
        // Use the measured, interface-scaled content width, not physical pixels.
        var width = ShellContentGrid.ActualWidth - NavRailWidth;
        if (!IsFiniteLayoutDimension(width)) return;
        QuickBuilderSidebar.Width = Math.Clamp(width * .38, QuickBuilderMinimumWidth, QuickBuilderExpandedWidth);
    }
}
```

`MainWindow.InputEditors.cs` 的 `FocusRouteInput_Click`、`MainWindow.xaml.cs` 的 `FocusV2Settings_Click`：在各自的 `SetQuickBuilderState(locked: false, collapsed: false);` 下一行加入：

```csharp
        SetQuickBuilderDrawerOpen(true);
```

- [ ] **Step 5: 執行，確認通過**

執行共用指令，並跑：

```powershell
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --outer-shell-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --native-final-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --validation-warning-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --interface-scale-only
```

（`--validation-warning-only` 在既有 runner 中會先跑完整 MCP bridge 測試，約 200 秒，因此不另外跑 `--mcp-only`。）

Expected: `[通過] 外殼骨架…`、`[通過] 快速起稿抽屜…`、`PASS WPF shell layout`；`PASS WPF outer shell scroll`；`--native-final-only`（含 `CompactRouteLayoutTests`）通過；`PASS WPF validation warning dismissal`（含 MCP bridge）；interface-scale 通過；建置 0 warning。若既有版面測試因新骨架的數值改變而失敗，只調整與新骨架對應的數值並在進度紀錄寫明 `Ruling`；不得刪除或放寬功能性檢查。

- [ ] **Step 6: Commit**

```powershell
git add src/MrtRouteSimulator.App/MainWindow.xaml src/MrtRouteSimulator.App/MainWindow.WorkspaceNavigation.cs src/MrtRouteSimulator.App/MainWindow.InputEditors.cs src/MrtRouteSimulator.App/MainWindow.xaml.cs src/MrtRouteSimulator.App/Themes/Controls.xaml tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs tests/MrtRouteSimulator.WpfTests/OuterShellScrollTests.cs
git commit -m "feat: rebuild main window shell with nav rail, app bar playback and quick builder drawer" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: 狀態圓點、窄視窗與縮放、驗證橫幅與大型路線診斷

**Files:**
- Create: `src/MrtRouteSimulator.App/MainWindow.Shell.cs`
- Modify: `src/MrtRouteSimulator.App/MainWindow.xaml.cs`（`ShowValidation`、`HideValidation`、4 處播放旗標賦值）
- Modify: `src/MrtRouteSimulator.App/MainWindow.V2.cs`（3 處播放旗標賦值）
- Modify: `tests/MrtRouteSimulator.WpfTests/LargePlaybackDiagnostics.cs`
- Test: `tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs`

**Interfaces:**
- Consumes: Task 4 的 `StatusIndicatorDot`、`AppTitlePanel`、`MainMenu`、`PlaybackControlBar`、`CurrentProjectFileTextBlock`。
- Produces: `MainWindow.SetV2PlaybackPlaying(bool)`、`MainWindow.UpdateStatusIndicator()`。

- [ ] **Step 1: 寫失敗測試**

在 `Run` 加入 `VerifyStatusIndicator(root);`、`VerifyAppBarFits();`、`VerifyValidationBanner(root);`，並加入：

```csharp
    private static void VerifyStatusIndicator(string root)
    {
        var window = new MainWindow { Width = 1280, Height = 800 };
        try
        {
            window.Show();
            PumpLayout(window);
            var dot = (System.Windows.Shapes.Ellipse)window.FindName("StatusIndicatorDot")!;
            // (object) 讓 string[] 以單一參數傳入，而不是被展開成 params 陣列。
            WpfTestWait.Invoke(window, "ShowValidation", (object)new[] { "測試警告" });
            Require(ReferenceEquals(dot.Fill, UiTheme.PrimaryBrush), "有驗證警告時狀態圓點必須為橘色。");
            WpfTestWait.Invoke(window, "HideValidation");
            Require(ReferenceEquals(dot.Fill, UiTheme.TextSubtleBrush), "未播放且無警告時狀態圓點必須為灰色。");
            WpfTestWait.Invoke(window, "SetV2PlaybackPlaying", true);
            Require(WpfTestWait.Field(window, "_isV2PlaybackPlaying") is true && ReferenceEquals(dot.Fill, UiTheme.SuccessBrush),
                "播放中狀態圓點必須為綠色。");
            WpfTestWait.Invoke(window, "SetV2PlaybackPlaying", false);
            Require(WpfTestWait.Field(window, "_isV2PlaybackPlaying") is false && ReferenceEquals(dot.Fill, UiTheme.TextSubtleBrush),
                "暫停後狀態圓點必須回到灰色。");
            var source = string.Join('\n', new[] { "MainWindow.xaml.cs", "MainWindow.V2.cs" }
                .Select(file => File.ReadAllText(System.IO.Path.Combine(root, "src", "MrtRouteSimulator.App", file))));
            Require(!source.Contains("_isV2PlaybackPlaying = "), "播放旗標必須一律經由 SetV2PlaybackPlaying 設定，圓點才會同步。");
        }
        finally { WpfTestWait.Close(window); }
        Console.WriteLine("[通過] 狀態列圓點");
    }

    private static void VerifyAppBarFits()
    {
        foreach (var (width, height, scale) in new[] { (800d, 520d, 1d), (1280d, 800d, 1.25d) })
        {
            var previousScale = SetInterfaceScale(scale);
            var window = new MainWindow { Width = width, Height = height };
            try
            {
                window.Show();
                PumpLayout(window);
                ((TextBlock)window.FindName("CurrentProjectFileTextBlock")!).Text =
                    "目前存檔：" + string.Concat(Enumerable.Repeat("很長很長的專案檔名", 12)) + ".mrtsim.json";
                PumpLayout(window);
                var content = (FrameworkElement)window.Content;
                Rect Bounds(string name)
                {
                    var element = (FrameworkElement)window.FindName(name)!;
                    return new Rect(element.TranslatePoint(new Point(0, 0), content), new Size(element.ActualWidth, element.ActualHeight));
                }
                var title = Bounds("AppTitlePanel");
                var menu = Bounds("MainMenu");
                var bar = Bounds("PlaybackControlBar");
                Require(title.Right <= menu.Left + .5 && menu.Right <= bar.Left + .5,
                    $"{width}×{height}@{scale:0.##}：標題、選單、播放列不可重疊；title={title}, menu={menu}, bar={bar}。");
                Require(bar.Right <= content.ActualWidth + .5, $"{width}×{height}@{scale:0.##}：播放列不可超出視窗；bar={bar}, content={content.ActualWidth:0.0}。");
            }
            finally
            {
                WpfTestWait.Close(window);
                SetInterfaceScale(previousScale);
            }
        }
        Console.WriteLine("[通過] 標題列在 800 DIP 與 125% 介面縮放下不重疊、不超出");
    }

    private static void VerifyValidationBanner(string root)
    {
        var previousScale = SetInterfaceScale(1);
        var window = new MainWindow { Width = 1280, Height = 800 };
        try
        {
            LoadSample(window, root, "10-小型-三站完整拓樸基準範例.mrtsim.json");
            window.Show();
            ((TabControl)window.FindName("WorkspaceTabControl")!).SelectedItem = window.FindName("SimulationTabItem");
            ((TabControl)window.FindName("SimulationViewTabControl")!).SelectedIndex = 0;
            WpfTestWait.Invoke(window, "ShowValidation", (object)Enumerable.Range(1, 30).Select(index => $"第 {index} 則驗證訊息").ToArray());
            PumpLayout(window);
            var banner = (Border)window.FindName("ValidationBorder")!;
            var route = (ScrollViewer)window.FindName("RouteScrollViewer")!;
            Require(banner.ActualHeight <= 160, $"驗證橫幅應在 140 px 內捲動，實際高 {banner.ActualHeight:0.0}。");
            Require(route.ActualHeight >= 200, $"大量驗證訊息時配線圖仍需保有可視高度，實際 {route.ActualHeight:0.0}。");
        }
        finally
        {
            WpfTestWait.Close(window);
            SetInterfaceScale(previousScale);
        }
        Console.WriteLine("[通過] 大量驗證訊息時橫幅自行捲動，配線圖保有高度");
    }
```

- [ ] **Step 2: 執行，確認失敗**

執行共用指令。Expected: `有驗證警告時狀態圓點必須為橘色。`（狀態圓點仍是 XAML 的固定灰色）。

- [ ] **Step 3: 實作**

建立 `src/MrtRouteSimulator.App/MainWindow.Shell.cs`：

```csharp
using System.Windows;

namespace MrtRouteSimulator.App;

public partial class MainWindow
{
    // 播放旗標的唯一寫入點：同步刷新狀態列圓點。
    private void SetV2PlaybackPlaying(bool playing)
    {
        _isV2PlaybackPlaying = playing;
        UpdateStatusIndicator();
    }

    // 狀態列圓點：驗證警告或播放工作者錯誤為橘、播放中為綠、其他為灰。
    private void UpdateStatusIndicator()
    {
        if (StatusIndicatorDot is null || ValidationBorder is null) return;
        var hasProblem = ValidationBorder.Visibility == Visibility.Visible
            || _playbackWorker?.Completion.IsFaulted == true;
        StatusIndicatorDot.Fill = hasProblem
            ? UiTheme.PrimaryBrush
            : _isV2PlaybackPlaying ? UiTheme.SuccessBrush : UiTheme.TextSubtleBrush;
    }
}
```

`MainWindow.xaml.cs`：在 `ShowValidation` 的 `ValidationBorder.Visibility = Visibility.Visible;` 與 `HideValidation` 的 `ValidationBorder.Visibility = Visibility.Collapsed;` 之後，各加入一行 `UpdateStatusIndicator();`。

把 `MainWindow.xaml.cs`（4 處）與 `MainWindow.V2.cs`（3 處）的播放旗標賦值全部改經新方法（`MainWindow.Shell.cs` 內的那一處不動）：

```powershell
foreach ($file in 'src/MrtRouteSimulator.App/MainWindow.xaml.cs', 'src/MrtRouteSimulator.App/MainWindow.V2.cs') {
    $text = [IO.File]::ReadAllText($file)
    $text = $text.Replace('_isV2PlaybackPlaying = true;', 'SetV2PlaybackPlaying(true);').Replace('_isV2PlaybackPlaying = false;', 'SetV2PlaybackPlaying(false);')
    [IO.File]::WriteAllText($file, $text, [Text.UTF8Encoding]::new($false))
}
git diff --stat
```

Expected: 兩檔原本皆為無 BOM 的 UTF-8，寫回後編碼與換行不變；`git diff` 只有這 7 行與上一步的 2 行 `UpdateStatusIndicator();`。

`LargePlaybackDiagnostics.cs`：把

```csharp
            if (Math.Abs(viewTabs.ActualHeight - simulationGrid.RowDefinitions[1].ActualHeight) > 5
                || routeViewport.ActualHeight < viewTabs.ActualHeight - 65)
                throw new InvalidOperationException("路線圖分頁未撐滿模擬頁面剩餘高度。");
```

替換為：

```csharp
            // 新骨架中配線圖下方保留固定捲軸（18）與外框內距；底緣距子分頁底緣不超過 30 px 即視為撐滿。
            var viewTabsBottom = viewTabs.TranslatePoint(new Point(0, viewTabs.ActualHeight), window).Y;
            var routeBottom = routeViewport.TranslatePoint(new Point(0, routeViewport.ActualHeight), window).Y;
            if (Math.Abs(viewTabs.ActualHeight - simulationGrid.RowDefinitions[1].ActualHeight) > 5
                || viewTabsBottom - routeBottom > 30)
                throw new InvalidOperationException("路線圖分頁未撐滿模擬頁面剩餘高度。");
```

- [ ] **Step 4: 執行，確認通過**

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --shell-layout-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- .\samples\14-大型-二十八站完整營運範例.mrtsim.json --large-playback-only
```

Expected: 三個新 `[通過]` 與 `PASS WPF shell layout`；`PASS WPF large playback diagnostics`。若 `VerifyAppBarFits` 在 125% 失敗，先縮小 `PlaybackSpeedComboBox` 寬度或 `AppBarButton` 間距，不得移除元件。

- [ ] **Step 5: Commit**

```powershell
git add src/MrtRouteSimulator.App/MainWindow.Shell.cs src/MrtRouteSimulator.App/MainWindow.V2.cs src/MrtRouteSimulator.App/MainWindow.xaml.cs tests/MrtRouteSimulator.WpfTests/LargePlaybackDiagnostics.cs tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs
git commit -m "feat: add status indicator and harden app bar and banner layout" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: 完整驗證、截圖與文件

**Files:**
- Modify: `tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs`、`tests/MrtRouteSimulator.WpfTests/Program.cs`（截圖工具）
- Modify: `docs/superpowers/specs/2026-10-10-main-window-shell-design.md`、`CHANGELOG.md`、`QA_REPORT.md`

- [ ] **Step 1: 截圖工具**

在 `ShellLayoutTests` 加入：

```csharp
    internal static void CaptureScreenshots(string root)
    {
        var output = System.IO.Path.Combine(root, "artifacts", "shell-screenshots");
        Directory.CreateDirectory(output);
        var previousScale = SetInterfaceScale(1);
        try
        {
            foreach (var (width, height) in new[] { (1280d, 800d), (1920d, 1080d) })
            {
                var window = new MainWindow { Width = width, Height = height };
                try
                {
                    LoadSample(window, root, "14-大型-二十八站完整營運範例.mrtsim.json");
                    window.Show();
                    WpfTestWait.Wait(Task.Delay(200));
                    WpfTestWait.Advance(window, 300); // 推進到 5 分鐘並刷新畫面，讓配線圖上有列車。
                    var tabs = (TabControl)window.FindName("WorkspaceTabControl")!;
                    tabs.SelectedItem = window.FindName("SimulationTabItem");
                    ((TabControl)window.FindName("SimulationViewTabControl")!).SelectedIndex = 0;
                    Save(window, System.IO.Path.Combine(output, $"main-{width}x{height}.png"));
                    tabs.SelectedItem = window.FindName("ResultsTabItem");
                    Save(window, System.IO.Path.Combine(output, $"timetable-{width}x{height}.png"));
                }
                finally { WpfTestWait.Close(window); }
            }
            var drawerWindow = new MainWindow { Width = 1280, Height = 800 };
            try
            {
                drawerWindow.Show();
                ((Button)drawerWindow.FindName("QuickBuilderToggleButton")!).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Save(drawerWindow, System.IO.Path.Combine(output, "drawer-1280x800.png"));
            }
            finally { WpfTestWait.Close(drawerWindow); }
            var editorType = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.TopologyEditorWindow")!;
            var pageType = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.ProjectWorkspacePage")!;
            var document = TopologyProjectFormat.Deserialize(File.ReadAllText(
                System.IO.Path.Combine(root, "samples", "14-大型-二十八站完整營運範例.mrtsim.json")));
            foreach (var page in new[] { "Tracks", "Services" })
            {
                var editor = (Window)Activator.CreateInstance(editorType, document, Enum.Parse(pageType, page))!;
                editor.Width = 1280;
                editor.Height = 800;
                try
                {
                    editor.Show();
                    Save(editor, System.IO.Path.Combine(output, $"editor-{page}-1280x800.png"));
                }
                finally { editor.Close(); }
            }
            Console.WriteLine($"shellScreenshots={output}");
        }
        finally { SetInterfaceScale(previousScale); }
    }

    private static void Save(Window window, string path)
    {
        PumpLayout(window);
        // 只拍視窗內容（不含系統標題列）；內容已套用介面縮放。
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
```

在 `Program.cs` 的 `--shell-layout-only` 區塊之前加入：

```csharp
            if (args.Contains("--shell-screenshots"))
            {
                ShellLayoutTests.CaptureScreenshots(GetRoot(args));
                return 0;
            }
```

- [ ] **Step 2: 完整驗證（互動桌面、不操作視窗）**

```powershell
query user
dotnet build .\MrtRouteSimulator.slnx -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.Tests\MrtRouteSimulator.Tests.csproj -c Release --no-build --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- .
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- .\samples\14-大型-二十八站完整營運範例.mrtsim.json --large-playback-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --shell-screenshots
```

Expected: session `Active`；整個 solution 0 warning／0 error；Engine 通過數與 Task 0 相同；完整 WPF runner `PASS WPF visual rules` 且含 `PASS WPF shell layout`；大型播放診斷 PASS；印出 `shellScreenshots=` 路徑。

- [ ] **Step 3: 人工外觀檢查**

用 Read 工具開啟 `artifacts\shell-screenshots\` 內全部 PNG，逐項確認：深藍標題列（名稱、檔名、四個選單、播放列與綠色時鐘）；左側導覽列 8 項加「起稿」；選中項的藍底與色條；KPI 細條一行；膠囊切換；配線圖佔滿；狀態列圓點與文字；抽屜白底與關閉鈕；時刻表 DataGrid 的表頭、格線、選取列；編輯器的按鈕、輸入框、表格套用新樣式後沒有破版。發現問題時只改樣式或版面值，修正後重跑 Step 2 對應的測試。把截圖也傳給使用者看。

- [ ] **Step 4: 文件**

1. 規格 `docs/superpowers/specs/2026-10-10-main-window-shell-design.md`：依本計畫「規格的實作細化」第 1～7 點更新對應段落（第 2.1 時鐘色、第 2.4 子分頁標題與縮放跟隨位置、第 2.5 抽屜邊界與 `SetQuickBuilderState` 語意、第 3 節 `TimeAccent`／`Success`、第 4.3 `NavFooter`）。
2. `CHANGELOG.md` 的 `## V4.1.0` 下新增：

```markdown
### 2026-10-10 介面翻新子專案 C1：主視窗外殼

- 主視窗改為左側導覽列＋常駐播放列：深藍標題列整合選單（檔案／編輯／顯示設定／原生驗收量測）與播放控制，8 個結果頁改為左側圖示導覽，KPI 摘要縮為一行細條，模擬子分頁改為膠囊切換，快速起稿改為按需開啟的抽屜，狀態列加入播放狀態圓點。
- 新增 `Themes/Controls.xaml`，按鈕、輸入框、下拉、DataGrid、捲軸、選單、Expander、提示框全 App 統一樣式，顏色只取自 `UiTheme`。
- 元件名稱、自動化名稱與 MCP `select_page` 行為不變；設計見 `docs/superpowers/specs/2026-10-10-main-window-shell-design.md`。
```

3. `QA_REPORT.md` 第一個 `## ` 之前新增一節，數字一律填 Step 2 的實際輸出：

```markdown
## 介面翻新子專案 C1：主視窗外殼（2026-10-10）

- Release build：<實際 warning／error 數>。
- Engine runner：<通過數>/<總數>。
- WPF runner（互動桌面）：<PASS WPF visual rules／失敗訊息>；新增 `ShellLayoutTests`（外殼色票、共用樣式、外殼範本、骨架、抽屜、狀態圓點、窄視窗與 125% 縮放、驗證橫幅）全部通過；配線圖可視高度 <改版後> px（改版前 <Task 0 基準> px）。
- 大型 28 站播放診斷：<PASS／失敗訊息>；人工截圖檢查：<結果>。
- 未涵蓋：各分頁內部版面（C2 起）、結果圖表（D）、拓樸編輯器版面與自繪軌道（E）。
```

- [ ] **Step 5: Commit**

```powershell
git add tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs tests/MrtRouteSimulator.WpfTests/Program.cs docs/superpowers/specs/2026-10-10-main-window-shell-design.md CHANGELOG.md QA_REPORT.md
git commit -m "docs: record shell redesign verification and screenshots tooling" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
