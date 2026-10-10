# 結果分頁版面統一與 C1 遺留修正（子專案 C2）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 7 個結果分頁改用共用的 `ResultPage` 元件與結果表規則（數字靠右、文字截斷、狀態色點），並修正 C1 審查留下的 9 個小問題。

**Architecture:** 新增 `ResultPage : ContentControl`（標題、說明、摘要、篩選卡、動作、內容、附註各有固定位置，非 Content 的位置以邏輯子元素掛載），範本放在 `Themes/Controls.xaml`。結果表以附加屬性 `ResultTable.Enabled`／`ResultTable.Kind` 啟用，依欄位種類套用 `TextCell`／`NumericCell`／`StatusCell` 樣式；狀態色點由 `StatusTones` 對照表決定。所有元件名稱、事件與自動化名稱保留。

**Tech Stack:** .NET 10、WPF（C# 14、XAML）、自製 WPF 測試 runner（`tests/MrtRouteSimulator.WpfTests`）。

**Spec:** `docs/superpowers/specs/2026-10-10-result-pages-design.md`

## Global Constraints

- 不修改 `src/MrtRouteSimulator.Engine/`、專案 Schema、版本號。
- 7 頁內所有既有 `x:Name`、事件處理常式、`AutomationProperties.Name`、按鈕文字保留；可新增 `x:Name`，不得改名或移除。
- 結果頁只顯示 `SimulationWorld` 已有的輸出；色點只依狀態文字分類，不新增任何計算。
- 顏色只能取自 `UiTheme`；`MainWindow.xaml` 完成後不得含 `#RRGGBB`／`#AARRGGBB`。
- `ResultTable` 只作用於標了 `ResultTable.Enabled="True"` 的表格；拓樸編輯器與輸入表格不受影響。
- 運行圖頁的 `DiagramWorkspaceGrid`、`DiagramControlsExpander`、`DiagramViewportBorder`、`DiagramEventsExpander` 結構與名稱保留（`UpdateShellContentHeight` 依賴）。
- App 專案 `TreatWarningsAsErrors=true`。
- 像素與視窗版面類 WPF 測試只能在互動桌面 session 執行；先 `query user` 確認 `Active`，完整 runner 執行期間不操作測試視窗。
- 既有版面數值測試若因新版面失敗，只調整與新版面對應的數值並記錄 `Ruling`，不得刪除或放寬功能性檢查。
- Commit 遵循 Conventional Commits，結尾 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`；不 push；只 `git add` 列出的檔案。

## 規格的實作細化（Task 10 回寫規格）

1. 狀態字串以程式實際輸出為準：閉塞表為「接近警戒」「需要制動」「侵入安全距離」；比較表的「可比較」歸 Success，「V2 尚未抵達」「跨站不比較」「折返節點不適用 V1」歸 Neutral；「退出營運」歸 Neutral。規格列的「違規類」目前沒有對應的狀態字串（「停車超限」只出現在事件表的事件欄）。
2. 匯出與重新整理按鈕沿用原按鈕文字（沒有明確自動化名稱的按鈕，其自動化名稱就是按鈕文字）。
3. 表格卡：DataGrid 放進 `ResultCard`（Padding 4）並取消自身外框；事件表與即時列車狀態表維持原本有框的 DataGrid，避免影響運行圖高度計算與子分頁版面。
4. 狀態欄寬度各加 14 px 容納色點。
5. 新增 `x:Name`：`ComparisonDataGrid`、`ResourceDataGrid`、`JourneyStatisticsDataGrid`、`IntervalStatisticsDataGrid`。
6. 頁面標題一律用頁名；速度曲線子分頁移除與膠囊重複的「列車完整行程速度曲線」標題。
7. 畫面更新例外的停止流程集中到 `StopPlaybackAfterUiFailure(Exception)`。

## Review Focus

1. **800 DIP 窄視窗**：閉塞頁「障礙物急停」群組不可與篩選重疊或超出頁面；統計頁四顆動作鈕不可壓住標題。由 Task 5 測試釘住。
2. **125% 介面縮放**：統計頁頁首動作區與標題不重疊、不超出。由 Task 5 測試釘住。
3. **未知或新增的狀態文字**：顯示灰點而非錯誤，且現有程式輸出的狀態字串都在對照表內。由 Task 1 測試釘住。
4. **大型範例的上千列時刻表**：結果表必須維持列虛擬化，播放時 UI 不卡。由 Task 2 測試與 Task 10 大型播放診斷釘住。
5. **V1 引擎模式**：V1 結果（「V1 理論基準」）照常以灰點顯示；V1 播放時狀態圓點為綠。由 Task 1、Task 8 測試釘住。

---

## File Structure

| 檔案 | 動作 | 責任 |
|---|---|---|
| `src/MrtRouteSimulator.App/StatusTone.cs` | 新增 | 狀態文字 → 色點對照、轉換器 |
| `src/MrtRouteSimulator.App/ResultTable.cs` | 新增 | 結果表附加屬性與欄位樣式套用 |
| `src/MrtRouteSimulator.App/ResultPage.cs` | 新增 | 結果頁元件（各位置依賴屬性、邏輯子元素） |
| `src/MrtRouteSimulator.App/UiTheme.cs` | 修改 | `Caution` 色票 |
| `src/MrtRouteSimulator.App/Themes/Controls.xaml` | 修改 | 結果表／結果頁樣式；選單、下拉焦點框、停用提示 |
| `src/MrtRouteSimulator.App/MainWindow.xaml` | 修改 | 7 頁、模擬子分頁、KPI 細條、抽屜位置、主選單樣式 |
| `src/MrtRouteSimulator.App/MainWindow.Shell.cs` | 修改 | 狀態圓點、`StopPlaybackAfterUiFailure` |
| `src/MrtRouteSimulator.App/MainWindow.xaml.cs` | 修改 | V1 播放／暫停、重設、畫面更新例外刷新圓點 |
| `src/MrtRouteSimulator.App/MainWindow.V2.cs` | 修改 | 清除結果時刷新圓點與起稿鈕文字 |
| `src/MrtRouteSimulator.App/MainWindow.Topology.cs` | 修改 | 載入專案時更新起稿鈕文字 |
| `src/MrtRouteSimulator.App/MainWindow.WorkspaceNavigation.cs` | 修改 | 抽屜焦點、起稿鈕文字 |
| `src/MrtRouteSimulator.App/MainWindow.ShellScroll.cs` | 修改 | 固定捲軸避開抽屜 |
| `tests/MrtRouteSimulator.WpfTests/ResultPageTests.cs` | 新增 | C2 測試 |
| `tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs` | 修改 | C1 遺留修正測試、截圖擴充 |
| `tests/MrtRouteSimulator.WpfTests/Program.cs` | 修改 | `--result-pages-only` 與完整 runner |
| `README.md`、兩份規格、`CHANGELOG.md`、`QA_REPORT.md` | 修改 | Task 10 |

**共用指令**（worktree 根目錄，PowerShell）：

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --result-pages-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --shell-layout-only
```

---

### Task 0: 基準線、`ResultPageTests` 骨架與截圖擴充

**Files:**
- Create: `tests/MrtRouteSimulator.WpfTests/ResultPageTests.cs`
- Modify: `tests/MrtRouteSimulator.WpfTests/Program.cs`
- Modify: `tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs`（`CaptureScreenshots`）

**Interfaces:**
- Produces: `ResultPageTests.Run(string root)`、`ResultPageTests.Require(bool, string)`；runner 旗標 `--result-pages-only`；截圖多出 `segment`／`comparison`／`resource`／`safety`／`statistics`／`diagram`／`trains`／`speed` 八張 1280×800。

- [ ] **Step 1: 確認分支包含最新 main**

```powershell
git merge-base --is-ancestor main HEAD; "main is ancestor: $LASTEXITCODE"
```

Expected: `main is ancestor: 0`（C1 已快轉合併，本分支只多了規格 commit）。若非 0，以 `git merge main --no-edit` 合併本機 main。

- [ ] **Step 2: 建置、Engine 與完整 WPF 基準**

```powershell
query user
dotnet build .\MrtRouteSimulator.slnx -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.Tests\MrtRouteSimulator.Tests.csproj -c Release --no-build --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- .
```

Expected: session `Active`；0 warning／0 error；Engine 全過；WPF 最後一行 `PASS WPF visual rules`。把 Engine 通過數與 `[通過]` 行數記入進度紀錄（C1 結束時為 198/198、107）。

- [ ] **Step 3: 建立測試骨架、旗標與截圖擴充**

建立 `tests/MrtRouteSimulator.WpfTests/ResultPageTests.cs`：

```csharp
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using MrtRouteSimulator.App;

/// <summary>結果分頁（子專案 C2）的元件、版面與表格規則測試。</summary>
internal static class ResultPageTests
{
    public static void Run(string root)
    {
        Console.WriteLine("PASS WPF result pages");
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
```

`Program.cs`：在 `if (args.Contains("--shell-layout-only"))` 區塊之前加入：

```csharp
            if (args.Contains("--result-pages-only"))
            {
                ResultPageTests.Run(GetRoot(args));
                return 0;
            }
```

並在完整 runner 的 `ShellLayoutTests.Run(GetRoot(args));`（`TrackDiagramThemeTests.Run` 之後那一行）下一行加入 `ResultPageTests.Run(GetRoot(args));`。

`ShellLayoutTests.CaptureScreenshots`：把

```csharp
                    tabs.SelectedItem = window.FindName("ResultsTabItem");
                    Save(window, System.IO.Path.Combine(output, $"timetable-{width}x{height}.png"));
```

替換為：

```csharp
                    tabs.SelectedItem = window.FindName("ResultsTabItem");
                    WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);
                    Save(window, System.IO.Path.Combine(output, $"timetable-{width}x{height}.png"));
                    if (width == 1280)
                    {
                        foreach (var (tab, file) in new[] { ("SegmentTabItem", "segment"), ("ComparisonTabItem", "comparison"),
                                     ("ResourceTabItem", "resource"), ("SafetyTabItem", "safety"),
                                     ("IntervalStatisticsTabItem", "statistics"), ("DiagramTabItem", "diagram") })
                        {
                            tabs.SelectedItem = window.FindName(tab);
                            WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);
                            WpfTestWait.Wait(Task.Delay(tab == "DiagramTabItem" ? 1500 : 200));
                            Save(window, System.IO.Path.Combine(output, $"{file}-1280x800.png"));
                        }
                        tabs.SelectedItem = window.FindName("SimulationTabItem");
                        var views = (TabControl)window.FindName("SimulationViewTabControl")!;
                        foreach (var (index, file) in new[] { (1, "trains"), (2, "speed") })
                        {
                            views.SelectedIndex = index;
                            WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);
                            WpfTestWait.Wait(Task.Delay(200));
                            Save(window, System.IO.Path.Combine(output, $"{file}-1280x800.png"));
                        }
                    }
```

- [ ] **Step 4: 執行骨架與改版前截圖**

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --result-pages-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --shell-screenshots
if (Test-Path .\artifacts\c2-before) { Remove-Item .\artifacts\c2-before -Recurse -Force }
Move-Item .\artifacts\shell-screenshots .\artifacts\c2-before
```

Expected: `PASS WPF result pages`；截圖完成且 `artifacts\c2-before` 內含 15 張 PNG（改版前對照組，`artifacts/` 已在 `.gitignore`）。

- [ ] **Step 5: Commit**

```powershell
git add tests/MrtRouteSimulator.WpfTests/ResultPageTests.cs tests/MrtRouteSimulator.WpfTests/Program.cs tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs
git commit -m "test: add result pages test harness and capture every result page" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 1: `StatusTone` 對照與 `Caution` 色票

**Files:**
- Create: `src/MrtRouteSimulator.App/StatusTone.cs`
- Modify: `src/MrtRouteSimulator.App/UiTheme.cs`
- Test: `tests/MrtRouteSimulator.WpfTests/ResultPageTests.cs`

**Interfaces:**
- Produces: `public enum StatusTone { Neutral, Active, Success, Caution, Danger }`；`public static class StatusTones`：`IReadOnlyDictionary<string, StatusTone> KnownStatuses`、`StatusTone Classify(string?)`、`SolidColorBrush Brush(StatusTone)`；`public sealed class StatusToneBrushConverter : IValueConverter`；`UiTheme.Caution`（`#D9A400`）與 `UiTheme.CautionBrush`。

- [ ] **Step 1: 寫失敗測試**

在 `Run` 的 `Console.WriteLine` 前加入 `VerifyStatusTones(root);`，並加入：

```csharp
    private static readonly (string Status, StatusTone Tone)[] ExpectedTones =
    [
        ("侵入安全距離", StatusTone.Danger), ("碰撞停止", StatusTone.Danger), ("障礙急停", StatusTone.Danger),
        ("接近警戒", StatusTone.Caution), ("需要制動", StatusTone.Caution),
        ("已抵達", StatusTone.Success), ("完成", StatusTone.Success), ("安全", StatusTone.Success), ("可比較", StatusTone.Success),
        ("已發車", StatusTone.Active), ("停站中", StatusTone.Active), ("運行中", StatusTone.Active), ("停站", StatusTone.Active),
        ("加速", StatusTone.Active), ("巡航", StatusTone.Active), ("惰行", StatusTone.Active), ("煞車", StatusTone.Active),
        ("進站平順煞車", StatusTone.Active), ("到站", StatusTone.Active), ("駛入尾軌", StatusTone.Active),
        ("尾軌返回", StatusTone.Active), ("駛入折返線", StatusTone.Active), ("折返線返回", StatusTone.Active),
        ("折返", StatusTone.Active),
        ("待發", StatusTone.Neutral), ("—", StatusTone.Neutral), ("V1 理論基準", StatusTone.Neutral),
        ("V2 尚未抵達", StatusTone.Neutral), ("跨站不比較", StatusTone.Neutral), ("折返節點不適用 V1", StatusTone.Neutral),
        ("退出營運", StatusTone.Neutral)
    ];

    private static void VerifyStatusTones(string root)
    {
        foreach (var (status, tone) in ExpectedTones)
            Require(StatusTones.Classify(status) == tone, $"狀態「{status}」應為 {tone}，實際 {StatusTones.Classify(status)}。");
        Require(StatusTones.Classify(" 已抵達 ") == StatusTone.Success, "狀態文字前後空白不得影響分類。");
        foreach (var unknown in new string?[] { null, "", "未知狀態", "全新狀態文字" })
            Require(StatusTones.Classify(unknown) == StatusTone.Neutral, $"無法辨識的狀態「{unknown}」應為 Neutral。");
        Require(StatusTones.KnownStatuses.Count == ExpectedTones.Length,
            $"對照表應恰有 {ExpectedTones.Length} 筆，實際 {StatusTones.KnownStatuses.Count}。");

        // 程式實際輸出的狀態字串都必須在對照表內，避免新增狀態後默默變成灰點。
        string Source(params string[] path) => File.ReadAllText(System.IO.Path.Combine([root, "src", .. path]));
        string[] Literals(string text, string startPattern)
        {
            var match = Regex.Match(text, startPattern + @"\s*\{(?<body>.*?)\};", RegexOptions.Singleline);
            Require(match.Success, $"找不到 {startPattern} 的對照區塊。");
            return Regex.Matches(match.Groups["body"].Value, "\"([^\"]+)\"").Select(item => item.Groups[1].Value).ToArray();
        }
        var v2 = Source("MrtRouteSimulator.App", "MainWindow.V2.cs");
        var produced = Literals(v2, @"PhaseToChinese\(OperationalPhase phase\) => phase switch")
            .Concat(Literals(v2, @"SafetyStatusToChinese\(SafetyStatus status\) => status switch"))
            .Concat(Regex.Matches(Source("MrtRouteSimulator.App", "IncrementalV1V2Comparison.cs"), "const string \\w+Status = \"([^\"]+)\"")
                .Select(item => item.Groups[1].Value))
            .Concat(["已抵達", "停站中", "已發車", "待發", "完成", "運行中", "V1 理論基準", "—"]);
        foreach (var status in produced.Distinct())
            Require(StatusTones.KnownStatuses.ContainsKey(status), $"程式會輸出的狀態「{status}」不在 StatusTones 對照表內。");

        var expectedBrushes = new (StatusTone Tone, SolidColorBrush Brush)[]
        {
            (StatusTone.Danger, UiTheme.DangerBrush), (StatusTone.Caution, UiTheme.CautionBrush),
            (StatusTone.Success, UiTheme.SuccessBrush), (StatusTone.Active, UiTheme.AccentBrush),
            (StatusTone.Neutral, UiTheme.TextSubtleBrush)
        };
        foreach (var (tone, brush) in expectedBrushes)
            Require(ReferenceEquals(StatusTones.Brush(tone), brush), $"{tone} 色點必須取自 UiTheme。");
        Require(UiTheme.Caution == Color.FromRgb(0xD9, 0xA4, 0x00) && UiTheme.CautionBrush.IsFrozen,
            "Caution 色票應為 #D9A400 且畫筆凍結。");
        var converted = new StatusToneBrushConverter().Convert("需要制動", typeof(Brush), null!, System.Globalization.CultureInfo.InvariantCulture);
        Require(ReferenceEquals(converted, UiTheme.CautionBrush), "轉換器必須依狀態文字回傳色點畫筆。");
        Console.WriteLine("[通過] StatusTone 狀態色點對照");
    }
```

- [ ] **Step 2: 執行，確認失敗**

執行共用指令前兩行。Expected: 建置失敗，`CS0246: 找不到類型或命名空間名稱 'StatusTone'`。

- [ ] **Step 3: 實作**

`UiTheme.cs`：在 `GridLine` 色票那一行之後加入

```csharp
    public static readonly Color Caution = Color.FromRgb(0xD9, 0xA4, 0x00);
```

並在 `GridLineBrush` 那一行之後加入

```csharp
    public static readonly SolidColorBrush CautionBrush = Frozen(Caution);
```

建立 `src/MrtRouteSimulator.App/StatusTone.cs`：

```csharp
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MrtRouteSimulator.App;

public enum StatusTone
{
    Neutral,
    Active,
    Success,
    Caution,
    Danger
}

/// <summary>
/// 結果表狀態文字到色點的對照。只看文字，不重新計算任何數值；
/// 程式新增狀態文字時請補進對照表（ResultPageTests 會檢查）。
/// </summary>
public static class StatusTones
{
    private static readonly Dictionary<string, StatusTone> Map = new(StringComparer.Ordinal)
    {
        ["侵入安全距離"] = StatusTone.Danger,
        ["碰撞停止"] = StatusTone.Danger,
        ["障礙急停"] = StatusTone.Danger,
        ["接近警戒"] = StatusTone.Caution,
        ["需要制動"] = StatusTone.Caution,
        ["已抵達"] = StatusTone.Success,
        ["完成"] = StatusTone.Success,
        ["安全"] = StatusTone.Success,
        ["可比較"] = StatusTone.Success,
        ["已發車"] = StatusTone.Active,
        ["停站中"] = StatusTone.Active,
        ["運行中"] = StatusTone.Active,
        ["停站"] = StatusTone.Active,
        ["加速"] = StatusTone.Active,
        ["巡航"] = StatusTone.Active,
        ["惰行"] = StatusTone.Active,
        ["煞車"] = StatusTone.Active,
        ["進站平順煞車"] = StatusTone.Active,
        ["到站"] = StatusTone.Active,
        ["駛入尾軌"] = StatusTone.Active,
        ["尾軌返回"] = StatusTone.Active,
        ["駛入折返線"] = StatusTone.Active,
        ["折返線返回"] = StatusTone.Active,
        ["折返"] = StatusTone.Active,
        ["待發"] = StatusTone.Neutral,
        ["—"] = StatusTone.Neutral,
        ["V1 理論基準"] = StatusTone.Neutral,
        ["V2 尚未抵達"] = StatusTone.Neutral,
        ["跨站不比較"] = StatusTone.Neutral,
        ["折返節點不適用 V1"] = StatusTone.Neutral,
        ["退出營運"] = StatusTone.Neutral
    };

    public static IReadOnlyDictionary<string, StatusTone> KnownStatuses => Map;

    public static StatusTone Classify(string? status) =>
        status is not null && Map.TryGetValue(status.Trim(), out var tone) ? tone : StatusTone.Neutral;

    public static SolidColorBrush Brush(StatusTone tone) => tone switch
    {
        StatusTone.Danger => UiTheme.DangerBrush,
        StatusTone.Caution => UiTheme.CautionBrush,
        StatusTone.Success => UiTheme.SuccessBrush,
        StatusTone.Active => UiTheme.AccentBrush,
        _ => UiTheme.TextSubtleBrush
    };
}

/// <summary>狀態欄色點：把儲存格的狀態文字轉成色點畫筆。</summary>
public sealed class StatusToneBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        StatusTones.Brush(StatusTones.Classify(value as string));

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException("狀態色點只供顯示，不支援反向轉換。");
}
```

- [ ] **Step 4: 執行，確認通過**

執行共用指令前兩行。Expected: `[通過] StatusTone 狀態色點對照`、`PASS WPF result pages`；建置 0 warning。

- [ ] **Step 5: Commit**

```powershell
git add src/MrtRouteSimulator.App/StatusTone.cs src/MrtRouteSimulator.App/UiTheme.cs tests/MrtRouteSimulator.WpfTests/ResultPageTests.cs
git commit -m "feat: add status tone mapping and caution color" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: `ResultTable` 欄位規則與表格樣式

**Files:**
- Create: `src/MrtRouteSimulator.App/ResultTable.cs`
- Modify: `src/MrtRouteSimulator.App/Themes/Controls.xaml`
- Test: `tests/MrtRouteSimulator.WpfTests/ResultPageTests.cs`

**Interfaces:**
- Consumes: `StatusToneBrushConverter`（Task 1）。
- Produces: `public enum ResultColumnKind { Text, Numeric, Status }`；`public static class ResultTable`：附加屬性 `Enabled`（`bool`，掛在 DataGrid）、`Kind`（`ResultColumnKind`，掛在 DataGridColumn），`GetEnabled/SetEnabled/GetKind/SetKind`；資源鍵 `StatusToneBrush`、`TextCell`、`NumericCell`、`NumericHeader`、`StatusCell`。

- [ ] **Step 1: 寫失敗測試**

在 `Run` 加入 `VerifyResultTableRules();`，並加入：

```csharp
    private sealed record SampleRow(string Name, string Distance, string Status);

    private static void VerifyResultTableRules()
    {
        DataGrid CreateGrid(bool enabled)
        {
            var grid = new DataGrid
            {
                Width = 420,
                Height = 160,
                ItemsSource = new[]
                {
                    new SampleRow("很長很長很長很長很長很長的停站模式名稱", "12.345", "已抵達"),
                    new SampleRow("短", "1.2", "侵入安全距離")
                }
            };
            if (enabled) ResultTable.SetEnabled(grid, true);
            var name = new DataGridTextColumn { Header = "名稱", Binding = new Binding(nameof(SampleRow.Name)), Width = 90 };
            var distance = new DataGridTextColumn { Header = "距離 km", Binding = new Binding(nameof(SampleRow.Distance)), Width = 90 };
            var status = new DataGridTextColumn { Header = "狀態", Binding = new Binding(nameof(SampleRow.Status)), Width = 140 };
            ResultTable.SetKind(distance, ResultColumnKind.Numeric);
            ResultTable.SetKind(status, ResultColumnKind.Status);
            grid.Columns.Add(name);
            grid.Columns.Add(distance);
            grid.Columns.Add(status);
            return grid;
        }

        DataGridCell Cell(DataGrid grid, int row, int column)
        {
            var container = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(row);
            var presenter = ShellLayoutTests.FindDescendant<DataGridCellsPresenter>(container)!;
            return (DataGridCell)presenter.ItemContainerGenerator.ContainerFromIndex(column);
        }

        var enabledGrid = CreateGrid(true);
        var plainGrid = CreateGrid(false);
        var panel = new StackPanel();
        panel.Children.Add(enabledGrid);
        panel.Children.Add(plainGrid);
        var window = new Window { Width = 520, Height = 420, Content = panel };
        try
        {
            window.Show();
            ShellLayoutTests.PumpLayout(window);
            var longName = (TextBlock)Cell(enabledGrid, 0, 0).Content;
            Require(longName.TextTrimming == TextTrimming.CharacterEllipsis && Equals(longName.ToolTip, longName.Text),
                "結果表文字欄必須截斷顯示「…」並以提示框顯示完整文字。");
            var number = (TextBlock)Cell(enabledGrid, 0, 1).Content;
            Require(number.TextAlignment == TextAlignment.Right && number.FontFamily.Source.Contains("Consolas"),
                $"結果表數字欄必須靠右並用等寬字型；alignment={number.TextAlignment}, font={number.FontFamily}。");
            var header = ShellLayoutTests.FindDescendant<DataGridColumnHeader>(enabledGrid, item => Equals(item.Content, "距離 km"));
            Require(header?.HorizontalContentAlignment == HorizontalAlignment.Right, "數字欄的欄名必須靠右。");
            var dangerCell = Cell(enabledGrid, 1, 2);
            Require(ShellLayoutTests.FindDescendant<Ellipse>(dangerCell)?.Fill is var dangerFill && ReferenceEquals(dangerFill, UiTheme.DangerBrush),
                "狀態欄必須以色點標示，「侵入安全距離」為 Danger。");
            Require(ReferenceEquals(ShellLayoutTests.FindDescendant<Ellipse>(Cell(enabledGrid, 0, 2))?.Fill, UiTheme.SuccessBrush),
                "「已抵達」的色點必須為 Success。");
            enabledGrid.SelectedIndex = 1;
            ShellLayoutTests.PumpLayout(window);
            Require(dangerCell.IsSelected && ReferenceEquals(dangerCell.Background, UiTheme.NavSelectedBrush),
                "狀態欄的選取格仍需 NavSelected 底色（沿用 C1 儲存格樣式）。");
            var plainName = (TextBlock)Cell(plainGrid, 0, 0).Content;
            Require(plainName.TextTrimming == TextTrimming.None && ShellLayoutTests.FindDescendant<Ellipse>(Cell(plainGrid, 1, 2)) is null,
                "未啟用 ResultTable 的表格不得被改動。");
            Require(enabledGrid.EnableRowVirtualization, "結果表必須維持列虛擬化。");
        }
        finally { window.Close(); }
        Console.WriteLine("[通過] ResultTable 欄位規則：文字截斷、數字靠右、狀態色點");
    }
```

- [ ] **Step 2: 執行，確認失敗**

執行共用指令前兩行。Expected: 建置失敗，`CS0103: 名稱 'ResultTable' 不存在於目前的內容中`。

- [ ] **Step 3: 實作**

建立 `src/MrtRouteSimulator.App/ResultTable.cs`：

```csharp
using System.Windows;
using System.Windows.Controls;

namespace MrtRouteSimulator.App;

public enum ResultColumnKind
{
    Text,
    Numeric,
    Status
}

/// <summary>
/// 結果表的欄位呈現規則：文字欄截斷並提示完整內容、數字欄靠右等寬、狀態欄加色點。
/// 只作用於標了 ResultTable.Enabled 的 DataGrid；欄位種類以 ResultTable.Kind 標記，預設為 Text。
/// </summary>
public static class ResultTable
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(ResultTable), new PropertyMetadata(false, OnEnabledChanged));

    public static readonly DependencyProperty KindProperty = DependencyProperty.RegisterAttached(
        "Kind", typeof(ResultColumnKind), typeof(ResultTable), new PropertyMetadata(ResultColumnKind.Text));

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    public static ResultColumnKind GetKind(DependencyObject element) => (ResultColumnKind)element.GetValue(KindProperty);

    public static void SetKind(DependencyObject element, ResultColumnKind value) => element.SetValue(KindProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid || e.NewValue is not true) return;
        // XAML 先設定 DataGrid 屬性、再逐一加入欄位，所以欄位在加入時套用；Loaded 再補一次以防萬一。
        ApplyAll(grid);
        grid.Columns.CollectionChanged += (_, args) =>
        {
            if (args.NewItems is null) return;
            foreach (DataGridColumn column in args.NewItems) Apply(column);
        };
        grid.Loaded += (_, _) => ApplyAll(grid);
    }

    private static void ApplyAll(DataGrid grid)
    {
        foreach (var column in grid.Columns) Apply(column);
    }

    private static void Apply(DataGridColumn column)
    {
        if (column is not DataGridTextColumn text) return;
        var kind = GetKind(column);
        text.ElementStyle = FindStyle(kind == ResultColumnKind.Numeric ? "NumericCell" : "TextCell");
        if (kind == ResultColumnKind.Numeric) text.HeaderStyle = FindStyle("NumericHeader");
        if (kind == ResultColumnKind.Status) text.CellStyle = FindStyle("StatusCell");
    }

    private static Style FindStyle(string key) =>
        Application.Current?.TryFindResource(key) as Style
        ?? throw new InvalidOperationException($"找不到結果表樣式「{key}」；請確認 App.xaml 已合併 Themes/Controls.xaml。");
}
```

在 `Themes/Controls.xaml` 的 `</ResourceDictionary>` 之前加入：

```xml
    <!-- 結果表欄位（ResultTable） -->
    <app:StatusToneBrushConverter x:Key="StatusToneBrush" />

    <Style x:Key="TextCell" TargetType="TextBlock" BasedOn="{x:Static DataGridTextColumn.DefaultElementStyle}">
        <Setter Property="TextTrimming" Value="CharacterEllipsis" />
        <Setter Property="ToolTip" Value="{Binding Text, RelativeSource={RelativeSource Self}}" />
    </Style>

    <Style x:Key="NumericCell" TargetType="TextBlock" BasedOn="{StaticResource TextCell}">
        <Setter Property="TextAlignment" Value="Right" />
        <Setter Property="HorizontalAlignment" Value="Stretch" />
        <Setter Property="FontFamily" Value="{StaticResource MonoFont}" />
        <Setter Property="Margin" Value="2,0,8,0" />
    </Style>

    <Style x:Key="NumericHeader" TargetType="DataGridColumnHeader" BasedOn="{StaticResource {x:Type DataGridColumnHeader}}">
        <Setter Property="HorizontalContentAlignment" Value="Right" />
    </Style>

    <Style x:Key="StatusCell" TargetType="DataGridCell" BasedOn="{StaticResource {x:Type DataGridCell}}">
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="DataGridCell">
                    <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}" SnapsToDevicePixels="True">
                        <DockPanel>
                            <Ellipse DockPanel.Dock="Left" Width="8" Height="8" Margin="6,0,2,0" VerticalAlignment="Center"
                                     Fill="{Binding Content.Text, RelativeSource={RelativeSource TemplatedParent}, Converter={StaticResource StatusToneBrush}}" />
                            <ContentPresenter VerticalAlignment="Center" />
                        </DockPanel>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
```

- [ ] **Step 4: 執行，確認通過**

執行共用指令前兩行。Expected: `[通過] ResultTable 欄位規則：文字截斷、數字靠右、狀態色點`、`PASS WPF result pages`。

- [ ] **Step 5: Commit**

```powershell
git add src/MrtRouteSimulator.App/ResultTable.cs src/MrtRouteSimulator.App/Themes/Controls.xaml tests/MrtRouteSimulator.WpfTests/ResultPageTests.cs
git commit -m "feat: add result table column rules with status dots" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: `ResultPage` 元件與範本

**Files:**
- Create: `src/MrtRouteSimulator.App/ResultPage.cs`
- Modify: `src/MrtRouteSimulator.App/Themes/Controls.xaml`
- Test: `tests/MrtRouteSimulator.WpfTests/ResultPageTests.cs`

**Interfaces:**
- Produces: `public sealed class ResultPage : ContentControl`，依賴屬性 `Title`（`string`）、`Description`、`Summary`、`Filters`、`FilterActions`、`Actions`、`Footnote`（`object?`）；範本部件 `PART_Title`、`PART_Description`、`ActionsHost`、`SummaryRow`、`FilterCard`、`FilterActionsHost`、`FootnoteHost`、`PART_Content`；樣式鍵 `ResultPageTitle`、`ResultPageDescription`、`ResultPageSummary`、`ResultPageFootnote`、`FilterLabel`、`FilterGroupLabel`、`FilterCard`、`ResultCard`、`ChartCard`、`ResultCardTitle`。

- [ ] **Step 1: 寫失敗測試**

在 `Run` 加入 `VerifyResultPageControl();`，並加入：

```csharp
    private static void VerifyResultPageControl()
    {
        TextBlock Text(string value) => new() { Text = value };
        var description = Text("說明");
        var summary = Text("摘要");
        var filter = Text("篩選");
        var filterAction = new Button { Content = "篩選動作" };
        var action = new Button { Content = "匯出" };
        var footnote = Text("附註");
        var body = new Border { Height = 120 };
        var full = new ResultPage
        {
            Title = "完整頁", Description = description, Summary = summary, Filters = filter,
            FilterActions = filterAction, Actions = action, Footnote = footnote, Content = body
        };
        var minimal = new ResultPage { Title = "精簡頁", Content = new Border { Height = 80 } };
        var filtersOnly = new ResultPage { Title = "只有篩選", Filters = Text("篩選"), Content = new Border { Height = 80 } };
        var host = new UniformGrid { Columns = 3 };
        host.Children.Add(full);
        host.Children.Add(minimal);
        host.Children.Add(filtersOnly);
        var window = new Window { Width = 1200, Height = 520, Content = host };
        try
        {
            window.Show();
            ShellLayoutTests.PumpLayout(window);
            double Top(FrameworkElement element) => element.TranslatePoint(new Point(0, 0), full).Y;
            double Left(FrameworkElement element) => element.TranslatePoint(new Point(0, 0), full).X;
            FrameworkElement Part(ResultPage page, string name) => (FrameworkElement)page.Template.FindName(name, page);
            var title = Part(full, "PART_Title");
            Require(title is TextBlock { Text: "完整頁" }, "ResultPage 必須顯示標題。");
            Require(Top(title) < Top(summary) && Top(summary) < Top(filter) && Top(filter) < Top(body) && Top(body) < Top(footnote),
                "ResultPage 由上而下應為頁首、摘要、篩選卡、內容、附註。");
            Require(Left(action) > Left(description) && Math.Abs(Top(action) - Top(title)) < 12, "動作鈕必須在頁首右側。");
            Require(Left(filterAction) > Left(filter) && Math.Abs(Top(filterAction) - Top(filter)) < 12, "篩選卡的動作群組必須在篩選右側。");
            foreach (var slot in new FrameworkElement[] { description, summary, filter, filterAction, action, footnote, body })
                Require(LogicalTreeHelper.GetParent(slot) == full, $"{slot.GetType().Name} 必須是 ResultPage 的邏輯子元素。");
            foreach (var part in new[] { "PART_Description", "ActionsHost", "SummaryRow", "FilterCard", "FootnoteHost" })
                Require(Part(minimal, part).Visibility == Visibility.Collapsed, $"未設定內容時 {part} 必須隱藏。");
            Require(Part(filtersOnly, "FilterCard").Visibility == Visibility.Visible
                    && Part(filtersOnly, "FilterActionsHost").Visibility == Visibility.Collapsed,
                "只有篩選時顯示篩選卡、隱藏右側動作群組。");
            Require(!full.Focusable && !full.IsTabStop, "ResultPage 本身不應成為 Tab 停駐點。");
        }
        finally { window.Close(); }
        Console.WriteLine("[通過] ResultPage 元件骨架");
    }
```

- [ ] **Step 2: 執行，確認失敗**

執行共用指令前兩行。Expected: 建置失敗，`CS0246: 找不到類型或命名空間名稱 'ResultPage'`。

- [ ] **Step 3: 實作**

建立 `src/MrtRouteSimulator.App/ResultPage.cs`：

```csharp
using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace MrtRouteSimulator.App;

/// <summary>
/// 結果分頁的共用骨架：頁首（標題、說明、動作）、摘要、篩選卡、內容與附註。
/// 除 Content 外的各位置以邏輯子元素掛載，讓 FindName、資源與字型繼承照舊。
/// </summary>
public sealed class ResultPage : ContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(ResultPage), new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty = RegisterSlot(nameof(Description));
    public static readonly DependencyProperty SummaryProperty = RegisterSlot(nameof(Summary));
    public static readonly DependencyProperty FiltersProperty = RegisterSlot(nameof(Filters));
    public static readonly DependencyProperty FilterActionsProperty = RegisterSlot(nameof(FilterActions));
    public static readonly DependencyProperty ActionsProperty = RegisterSlot(nameof(Actions));
    public static readonly DependencyProperty FootnoteProperty = RegisterSlot(nameof(Footnote));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public object? Summary
    {
        get => GetValue(SummaryProperty);
        set => SetValue(SummaryProperty, value);
    }

    public object? Filters
    {
        get => GetValue(FiltersProperty);
        set => SetValue(FiltersProperty, value);
    }

    public object? FilterActions
    {
        get => GetValue(FilterActionsProperty);
        set => SetValue(FilterActionsProperty, value);
    }

    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    public object? Footnote
    {
        get => GetValue(FootnoteProperty);
        set => SetValue(FootnoteProperty, value);
    }

    protected override IEnumerator LogicalChildren
    {
        get
        {
            var children = new List<object>();
            var baseChildren = base.LogicalChildren;
            while (baseChildren?.MoveNext() == true) children.Add(baseChildren.Current);
            foreach (var slot in new[] { Description, Summary, Filters, FilterActions, Actions, Footnote })
                if (slot is not null) children.Add(slot);
            return children.GetEnumerator();
        }
    }

    private static DependencyProperty RegisterSlot(string name) => DependencyProperty.Register(
        name, typeof(object), typeof(ResultPage), new FrameworkPropertyMetadata(null, OnSlotChanged));

    private static void OnSlotChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var page = (ResultPage)d;
        if (e.OldValue is not null) page.RemoveLogicalChild(e.OldValue);
        if (e.NewValue is not null) page.AddLogicalChild(e.NewValue);
    }
}
```

在 `Themes/Controls.xaml` 的 `</ResourceDictionary>` 之前加入：

```xml
    <!-- 結果頁（ResultPage） -->
    <Style x:Key="ResultPageTitle" TargetType="TextBlock">
        <Setter Property="FontSize" Value="15" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.TextStrongBrush}" />
        <Setter Property="VerticalAlignment" Value="Center" />
    </Style>

    <Style x:Key="ResultPageDescription" TargetType="TextBlock">
        <Setter Property="FontSize" Value="12" />
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.TextMutedBrush}" />
        <Setter Property="VerticalAlignment" Value="Center" />
        <Setter Property="TextTrimming" Value="CharacterEllipsis" />
        <Setter Property="ToolTip" Value="{Binding Text, RelativeSource={RelativeSource Self}}" />
    </Style>

    <Style x:Key="ResultPageSummary" TargetType="TextBlock">
        <Setter Property="FontSize" Value="12" />
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.TextMutedBrush}" />
        <Setter Property="TextWrapping" Value="Wrap" />
        <Setter Property="VerticalAlignment" Value="Center" />
    </Style>

    <Style x:Key="ResultPageFootnote" TargetType="TextBlock">
        <Setter Property="FontSize" Value="11" />
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.TextMutedBrush}" />
        <Setter Property="TextWrapping" Value="Wrap" />
    </Style>

    <Style x:Key="FilterLabel" TargetType="TextBlock">
        <Setter Property="FontSize" Value="12" />
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.TextMutedBrush}" />
        <Setter Property="VerticalAlignment" Value="Center" />
        <Setter Property="Margin" Value="0,0,5,0" />
    </Style>

    <Style x:Key="FilterGroupLabel" TargetType="TextBlock">
        <Setter Property="FontSize" Value="11" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.TextSubtleBrush}" />
        <Setter Property="VerticalAlignment" Value="Center" />
        <Setter Property="Margin" Value="0,0,10,0" />
        <Setter Property="MinWidth" Value="28" />
    </Style>

    <Style x:Key="FilterCard" TargetType="Border">
        <Setter Property="Background" Value="{x:Static app:UiTheme.SurfaceBrush}" />
        <Setter Property="BorderBrush" Value="{x:Static app:UiTheme.BorderBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="8" />
        <Setter Property="Padding" Value="10,4" />
    </Style>

    <Style x:Key="ResultCard" TargetType="Border">
        <Setter Property="Background" Value="{x:Static app:UiTheme.SurfaceBrush}" />
        <Setter Property="BorderBrush" Value="{x:Static app:UiTheme.BorderBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="8" />
        <Setter Property="Padding" Value="4" />
    </Style>

    <Style x:Key="ChartCard" TargetType="Border" BasedOn="{StaticResource ResultCard}">
        <Setter Property="Background" Value="{x:Static app:UiTheme.CanvasBackgroundBrush}" />
        <Setter Property="Padding" Value="6" />
    </Style>

    <Style x:Key="ResultCardTitle" TargetType="TextBlock">
        <Setter Property="FontSize" Value="13" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.TextStrongBrush}" />
        <Setter Property="Margin" Value="6,4,6,4" />
    </Style>

    <Style TargetType="app:ResultPage">
        <Setter Property="Focusable" Value="False" />
        <Setter Property="IsTabStop" Value="False" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="app:ResultPage">
                    <DockPanel>
                        <Grid DockPanel.Dock="Top" Margin="0,0,0,6">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <DockPanel VerticalAlignment="Center" Margin="0,0,12,0">
                                <TextBlock x:Name="PART_Title" DockPanel.Dock="Left" Text="{TemplateBinding Title}"
                                           Style="{StaticResource ResultPageTitle}" />
                                <ContentPresenter x:Name="PART_Description" Content="{TemplateBinding Description}"
                                                  Margin="10,0,0,0" VerticalAlignment="Center" />
                            </DockPanel>
                            <ContentPresenter x:Name="ActionsHost" Grid.Column="1" Content="{TemplateBinding Actions}"
                                              VerticalAlignment="Center" />
                        </Grid>
                        <DockPanel x:Name="SummaryRow" DockPanel.Dock="Top" Margin="0,0,0,8">
                            <TextBlock DockPanel.Dock="Left" Text="&#xE946;" FontFamily="{StaticResource IconFont}" FontSize="12"
                                       Foreground="{x:Static app:UiTheme.AccentBrush}" VerticalAlignment="Top" Margin="0,2,6,0" />
                            <ContentPresenter Content="{TemplateBinding Summary}" />
                        </DockPanel>
                        <Border x:Name="FilterCard" DockPanel.Dock="Top" Style="{StaticResource FilterCard}" Margin="0,0,0,8">
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="Auto" />
                                </Grid.ColumnDefinitions>
                                <ContentPresenter Content="{TemplateBinding Filters}" VerticalAlignment="Center" />
                                <Border x:Name="FilterActionsHost" Grid.Column="1" BorderBrush="{x:Static app:UiTheme.BorderBrush}"
                                        BorderThickness="1,0,0,0" Padding="10,0,0,0" Margin="10,0,0,0">
                                    <ContentPresenter Content="{TemplateBinding FilterActions}" VerticalAlignment="Center" />
                                </Border>
                            </Grid>
                        </Border>
                        <ContentPresenter x:Name="FootnoteHost" DockPanel.Dock="Bottom" Content="{TemplateBinding Footnote}" Margin="2,8,0,0" />
                        <ContentPresenter x:Name="PART_Content" Content="{TemplateBinding Content}" />
                    </DockPanel>
                    <ControlTemplate.Triggers>
                        <Trigger Property="Description" Value="{x:Null}">
                            <Setter TargetName="PART_Description" Property="Visibility" Value="Collapsed" />
                        </Trigger>
                        <Trigger Property="Actions" Value="{x:Null}">
                            <Setter TargetName="ActionsHost" Property="Visibility" Value="Collapsed" />
                        </Trigger>
                        <Trigger Property="Summary" Value="{x:Null}">
                            <Setter TargetName="SummaryRow" Property="Visibility" Value="Collapsed" />
                        </Trigger>
                        <Trigger Property="FilterActions" Value="{x:Null}">
                            <Setter TargetName="FilterActionsHost" Property="Visibility" Value="Collapsed" />
                        </Trigger>
                        <MultiTrigger>
                            <MultiTrigger.Conditions>
                                <Condition Property="Filters" Value="{x:Null}" />
                                <Condition Property="FilterActions" Value="{x:Null}" />
                            </MultiTrigger.Conditions>
                            <Setter TargetName="FilterCard" Property="Visibility" Value="Collapsed" />
                        </MultiTrigger>
                        <Trigger Property="Footnote" Value="{x:Null}">
                            <Setter TargetName="FootnoteHost" Property="Visibility" Value="Collapsed" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
```

- [ ] **Step 4: 執行，確認通過**

執行共用指令前兩行。Expected: `[通過] ResultPage 元件骨架`、`PASS WPF result pages`。

- [ ] **Step 5: Commit**

```powershell
git add src/MrtRouteSimulator.App/ResultPage.cs src/MrtRouteSimulator.App/Themes/Controls.xaml tests/MrtRouteSimulator.WpfTests/ResultPageTests.cs
git commit -m "feat: add result page control and template" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: 時刻表、區間、比較、容量四頁

**Files:**
- Modify: `src/MrtRouteSimulator.App/MainWindow.xaml`（`ResultsTabItem`、`SegmentTabItem`、`ComparisonTabItem`、`ResourceTabItem`）
- Test: `tests/MrtRouteSimulator.WpfTests/ResultPageTests.cs`

**Interfaces:**
- Consumes: `ResultPage`、`ResultTable`、樣式鍵（Task 2、3）。
- Produces: 新 `x:Name` `ComparisonDataGrid`、`ResourceDataGrid`；測試輔助 `Page(MainWindow, string)`、`InSlot(object?, DependencyObject)`、`RequireIn(MainWindow, object?, string, params string[])`、`FindLogical<T>(object?, Func<T, bool>)`、`VerifyTableColumns(MainWindow, string, string[], string[])`。

- [ ] **Step 1: 寫失敗測試**

在 `Run` 加入 `VerifyBasicPages();`，並加入：

```csharp
    internal static ResultPage Page(MainWindow window, string tabName)
    {
        var tab = (TabItem)window.FindName(tabName)!;
        Require(tab.Content is ResultPage, $"{tabName} 的內容必須是 ResultPage。");
        var page = (ResultPage)tab.Content;
        Require(page.Title == (string)tab.Header, $"{tabName} 的標題應為「{tab.Header}」，實際「{page.Title}」。");
        Require(page.Margin == new Thickness(0), $"{tabName} 不得再有頁面自己的外距。");
        return page;
    }

    internal static bool InSlot(object? slot, DependencyObject element)
    {
        for (DependencyObject? current = element; current is not null; current = LogicalTreeHelper.GetParent(current))
            if (ReferenceEquals(current, slot)) return true;
        return false;
    }

    internal static void RequireIn(MainWindow window, object? slot, string slotName, params string[] names)
    {
        foreach (var name in names)
            Require(window.FindName(name) is DependencyObject element && InSlot(slot, element), $"{name} 必須位於 {slotName}。");
    }

    internal static T? FindLogical<T>(object? root, Func<T, bool> predicate) where T : DependencyObject
    {
        if (root is not DependencyObject node) return null;
        if (node is T match && predicate(match)) return match;
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            if (FindLogical(child, predicate) is { } found) return found;
        return null;
    }

    internal static void VerifyTableColumns(MainWindow window, string gridName, string[] numeric, string[] status)
    {
        var grid = window.FindName(gridName) as DataGrid;
        Require(grid is not null && ResultTable.GetEnabled(grid), $"{gridName} 必須存在並啟用 ResultTable。");
        var headers = grid!.Columns.Select(column => column.Header?.ToString() ?? string.Empty).ToArray();
        foreach (var header in numeric.Concat(status))
            Require(headers.Contains(header), $"{gridName} 找不到欄位「{header}」。");
        foreach (var column in grid.Columns)
        {
            var header = column.Header?.ToString() ?? string.Empty;
            var expected = numeric.Contains(header) ? ResultColumnKind.Numeric
                : status.Contains(header) ? ResultColumnKind.Status : ResultColumnKind.Text;
            Require(ResultTable.GetKind(column) == expected, $"{gridName} 的「{header}」應為 {expected}，實際 {ResultTable.GetKind(column)}。");
            Require(ReferenceEquals(((DataGridTextColumn)column).ElementStyle,
                    Application.Current.FindResource(expected == ResultColumnKind.Numeric ? "NumericCell" : "TextCell")),
                $"{gridName} 的「{header}」未套用 {expected} 樣式。");
        }
    }

    private static void VerifyBasicPages()
    {
        var window = new MainWindow { Width = 1280, Height = 800 };
        try
        {
            window.Show();
            ShellLayoutTests.PumpLayout(window);
            var timetable = Page(window, "ResultsTabItem");
            RequireIn(window, timetable.Description, "Description", "TimetableSourceText");
            RequireIn(window, timetable.Content, "Content", "TimetableDataGrid");
            Require(timetable.Filters is null && timetable.Summary is null && timetable.Actions is null, "時刻表頁不應有篩選、摘要或動作。");
            VerifyTableColumns(window, "TimetableDataGrid",
                ["計畫到站", "計畫出站", "實際到站", "實際出站", "停站", "延誤", "累積 km"], ["狀態"]);

            var segment = Page(window, "SegmentTabItem");
            RequireIn(window, segment.Description, "Description", "SegmentSourceText");
            RequireIn(window, segment.Content, "Content", "SegmentDataGrid");
            Require(segment.Footnote is TextBlock { Text: var segmentNote } && segmentNote.Contains("ATP"), "區間頁附註必須保留安全認證聲明。");
            VerifyTableColumns(window, "SegmentDataGrid",
                ["距離 km", "峰值 km/h", "旅行時間", "加速", "巡航", "惰行", "減速"], ["狀態"]);

            var comparison = Page(window, "ComparisonTabItem");
            RequireIn(window, comparison.Content, "Content", "ComparisonDataGrid");
            Require(comparison.Footnote is TextBlock { Text: var compareNote } && compareNote.Contains("V1 不適用"),
                "比較頁附註必須保留跨站與折返說明。");
            VerifyTableColumns(window, "ComparisonDataGrid",
                ["V1 到站", "V1 出站", "V1 停站", "V2 到站", "V2 出站", "V2 停站", "到站差", "出站差", "出站差 %"], ["狀態"]);

            var resource = Page(window, "ResourceTabItem");
            RequireIn(window, resource.Summary, "Summary", "ResourceOccupancySummaryText");
            Require(FindLogical<Button>(resource.Actions, button => Equals(button.Content, "匯出資源占用 CSV")) is not null,
                "容量頁的匯出鈕必須位於 Actions。");
            RequireIn(window, resource.Content, "Content", "ResourceDataGrid");
            VerifyTableColumns(window, "ResourceDataGrid",
                ["占用時間", "使用率", "預約次數", "觀測每小時次數", "最短釋放間距"], []);
        }
        finally { WpfTestWait.Close(window); }
        Console.WriteLine("[通過] 時刻表、區間、比較、容量頁套用 ResultPage 與表格規則");
    }
```

- [ ] **Step 2: 執行，確認失敗**

執行共用指令前兩行。Expected: `ResultsTabItem 的內容必須是 ResultPage。`

- [ ] **Step 3: 實作**

在 `MainWindow.xaml` 以下列內容整段替換對應的 `<TabItem x:Name="…">…</TabItem>`（從開始標籤到對應的結束標籤，含兩端）。

時刻表：

```xml
                    <TabItem x:Name="ResultsTabItem" Header="進出站時刻表" app:ShellNav.ShortLabel="時刻表" app:ShellNav.Icon="&#xE787;">
                        <app:ResultPage Title="進出站時刻表">
                            <app:ResultPage.Description>
                                <TextBlock x:Name="TimetableSourceText" Text="V1 顯示解析基準；V2 寫實引擎顯示派車計畫與實際事件"
                                           Style="{StaticResource ResultPageDescription}" />
                            </app:ResultPage.Description>
                            <Border Style="{StaticResource ResultCard}">
                                <DataGrid x:Name="TimetableDataGrid" ItemsSource="{Binding TimetableRows}" IsReadOnly="True" RowHeaderWidth="0"
                                          FrozenColumnCount="2" BorderThickness="0" app:ResultTable.Enabled="True">
                                    <DataGrid.Columns>
                                        <DataGridTextColumn Header="車輛 ID" Binding="{Binding TrainId}" Width="100" />
                                        <DataGridTextColumn Header="車次 ID" Binding="{Binding ServiceRunId}" Width="125" />
                                        <DataGridTextColumn Header="方向" Binding="{Binding Direction}" Width="62" />
                                        <DataGridTextColumn Header="服務類型" Binding="{Binding ServiceType}" Width="90" />
                                        <DataGridTextColumn Header="停站模式" Binding="{Binding StopPattern}" Width="115" />
                                        <DataGridTextColumn Header="站號" Binding="{Binding StationId}" Width="68" />
                                        <DataGridTextColumn Header="車站" Binding="{Binding StationName}" Width="*" MinWidth="120" />
                                        <DataGridTextColumn Header="計畫到站" Binding="{Binding PlannedArrivalTime}" Width="95" app:ResultTable.Kind="Numeric" />
                                        <DataGridTextColumn Header="計畫出站" Binding="{Binding PlannedDepartureTime}" Width="95" app:ResultTable.Kind="Numeric" />
                                        <DataGridTextColumn Header="實際到站" Binding="{Binding ArrivalTime}" Width="95" app:ResultTable.Kind="Numeric" />
                                        <DataGridTextColumn Header="實際出站" Binding="{Binding DepartureTime}" Width="95" app:ResultTable.Kind="Numeric" />
                                        <DataGridTextColumn Header="停站" Binding="{Binding DwellTime}" Width="72" app:ResultTable.Kind="Numeric" />
                                        <DataGridTextColumn Header="延誤" Binding="{Binding Delay}" Width="72" app:ResultTable.Kind="Numeric" />
                                        <DataGridTextColumn Header="狀態" Binding="{Binding Status}" Width="96" app:ResultTable.Kind="Status" />
                                        <DataGridTextColumn Header="累積 km" Binding="{Binding PositionKm}" Width="82" app:ResultTable.Kind="Numeric" />
                                    </DataGrid.Columns>
                                </DataGrid>
                            </Border>
                        </app:ResultPage>
                    </TabItem>
```

區間：

```xml
                    <TabItem x:Name="SegmentTabItem" Header="區間物理明細" app:ShellNav.ShortLabel="區間" app:ShellNav.Icon="&#xEA37;">
                        <app:ResultPage Title="區間物理明細">
                            <app:ResultPage.Description>
                                <TextBlock x:Name="SegmentSourceText" Text="V1 自動判斷解析曲線；V2 改讀實際模擬世界軌跡與事件"
                                           Style="{StaticResource ResultPageDescription}" />
                            </app:ResultPage.Description>
                            <app:ResultPage.Footnote>
                                <TextBlock Text="基礎模式保留 V1.0 三角形／梯形解析結果；V2 是營運與號誌概念模擬，不代表真實 ATP／ATO／ATS 或鐵路安全認證。"
                                           Style="{StaticResource ResultPageFootnote}" />
                            </app:ResultPage.Footnote>
                            <Border Style="{StaticResource ResultCard}">
                                <DataGrid x:Name="SegmentDataGrid" ItemsSource="{Binding SegmentRows}" IsReadOnly="True" RowHeaderWidth="0"
                                          BorderThickness="0" app:ResultTable.Enabled="True">
                                    <DataGrid.Columns>
                                        <DataGridTextColumn Header="區間" Binding="{Binding Segment}" Width="*" MinWidth="160" />
                                        <DataGridTextColumn Header="車輛 ID" Binding="{Binding VehicleId}" Width="100" />
                                        <DataGridTextColumn Header="車次 ID" Binding="{Binding ServiceRunId}" Width="120" />
                                        <DataGridTextColumn Header="方向" Binding="{Binding Direction}" Width="58" />
                                        <DataGridTextColumn Header="狀態" Binding="{Binding Status}" Width="92" app:ResultTable.Kind="Status" />
                                        <DataGridTextColumn Header="距離 km" Binding="{Binding DistanceKm}" Width="84" app:ResultTable.Kind="Numeric" />
                                        <DataGridTextColumn Header="曲線" Binding="{Binding Profile}" Width="74" />
                                        <DataGridTextColumn Header="峰值 km/h" Binding="{Binding PeakSpeedKmh}" Width="94" app:ResultTable.Kind="Numeric" />
                                        <DataGridTextColumn Header="旅行時間" Binding="{Binding TravelTime}" Width="90" app:ResultTable.Kind="Numeric" />
                                        <DataGridTextColumn Header="加速" Binding="{Binding AccelerationTime}" Width="78" app:ResultTable.Kind="Numeric" />
                                        <DataGridTextColumn Header="巡航" Binding="{Binding CruisingTime}" Width="78" app:ResultTable.Kind="Numeric" />
                                        <DataGridTextColumn Header="惰行" Binding="{Binding CoastingTime}" Width="78" app:ResultTable.Kind="Numeric" />
                                        <DataGridTextColumn Header="減速" Binding="{Binding DecelerationTime}" Width="78" app:ResultTable.Kind="Numeric" />
                                        <DataGridTextColumn Header="控制事件" Binding="{Binding ControlEvents}" Width="140" />
                                    </DataGrid.Columns>
                                </DataGrid>
                            </Border>
                        </app:ResultPage>
                    </TabItem>
```

比較：

```xml
                    <TabItem x:Name="ComparisonTabItem" Header="V1／V2 同條件比較" app:ShellNav.ShortLabel="比較" app:ShellNav.Icon="&#xE8AB;">
                        <app:ResultPage Title="V1／V2 同條件比較">
                            <app:ResultPage.Description>
                                <TextBlock Text="同一車次、同一停站條件下的 V1 解析理論與 V2 寫實實際" Style="{StaticResource ResultPageDescription}" />
                            </app:ResultPage.Description>
                            <app:ResultPage.Footnote>
                                <TextBlock Text="跨站不套用站停差異；折返節點明確標記為 V1 不適用，避免以不同模型硬湊數字。"
                                           Style="{StaticResource ResultPageFootnote}" />
                            </app:ResultPage.Footnote>
                            <Border Style="{StaticResource ResultCard}">
                                <DataGrid x:Name="ComparisonDataGrid" ItemsSource="{Binding V1V2ComparisonRows}" IsReadOnly="True" RowHeaderWidth="0"
                                          FrozenColumnCount="2" BorderThickness="0" app:ResultTable.Enabled="True">
                                    <DataGrid.Columns>
                                        <DataGridTextColumn Header="車輛 ID" Binding="{Binding VehicleId}" Width="100" />
                                        <DataGridTextColumn Header="車次 ID" Binding="{Binding ServiceRunId}" Width="125" />
                                        <DataGridTextColumn Header="方向" Binding="{Binding Direction}" Width="58" />
                                        <DataGridTextColumn Header="車型" Binding="{Binding VehicleType}" Width="85" />
                                        <DataGridTextColumn Header="停站模式" Binding="{Binding StopPattern}" Width="110" />
                                        <DataGridTextColumn Header="站號" Binding="{Binding StationId}" Width="65" />
                                        <DataGridTextColumn Header="車站" Binding="{Binding StationName}" Width="*" MinWidth="110" />
                                        <DataGridTextColumn Header="V1 到站" Binding="{Binding TheoreticalArrival}" Width="88" app:ResultTable.Kind="Numeric" />
                                        <DataGridTextColumn Header="V1 出站" Binding="{Binding TheoreticalDeparture}" Width="88" app:ResultTable.Kind="Numeric" />
                                        <DataGridTextColumn Header="V1 停站" Binding="{Binding TheoreticalDwell}" Width="76" app:ResultTable.Kind="Numeric" />
                                        <DataGridTextColumn Header="V2 到站" Binding="{Binding ActualArrival}" Width="88" app:ResultTable.Kind="Numeric" />
                                        <DataGridTextColumn Header="V2 出站" Binding="{Binding ActualDeparture}" Width="88" app:ResultTable.Kind="Numeric" />
                                        <DataGridTextColumn Header="V2 停站" Binding="{Binding ActualDwell}" Width="76" app:ResultTable.Kind="Numeric" />
                                        <DataGridTextColumn Header="到站差" Binding="{Binding ArrivalDifference}" Width="75" app:ResultTable.Kind="Numeric" />
                                        <DataGridTextColumn Header="出站差" Binding="{Binding DepartureDifference}" Width="75" app:ResultTable.Kind="Numeric" />
                                        <DataGridTextColumn Header="出站差 %" Binding="{Binding DepartureDifferencePercent}" Width="82" app:ResultTable.Kind="Numeric" />
                                        <DataGridTextColumn Header="狀態" Binding="{Binding Status}" Width="139" app:ResultTable.Kind="Status" />
                                    </DataGrid.Columns>
                                </DataGrid>
                            </Border>
                        </app:ResultPage>
                    </TabItem>
```

容量：

```xml
                    <TabItem x:Name="ResourceTabItem" Header="資源占用與觀測容量" app:ShellNav.ShortLabel="容量" app:ShellNav.Icon="&#xE8A9;">
                        <app:ResultPage Title="資源占用與觀測容量">
                            <app:ResultPage.Description>
                                <TextBlock Text="月台／進路／衝突區／尾軌獨立占用時間軸" Style="{StaticResource ResultPageDescription}" />
                            </app:ResultPage.Description>
                            <app:ResultPage.Summary>
                                <TextBlock x:Name="ResourceOccupancySummaryText" Text="建立並播放 V2 模擬後顯示。" Style="{StaticResource ResultPageSummary}" />
                            </app:ResultPage.Summary>
                            <app:ResultPage.Actions>
                                <Button Content="匯出資源占用 CSV" Style="{StaticResource SecondaryButton}" Click="ExportResourceOccupancyCsv_Click" />
                            </app:ResultPage.Actions>
                            <Border Style="{StaticResource ResultCard}">
                                <DataGrid x:Name="ResourceDataGrid" ItemsSource="{Binding ResourceOccupancyRows}" IsReadOnly="True" RowHeaderWidth="0"
                                          FrozenColumnCount="1" BorderThickness="0" app:ResultTable.Enabled="True">
                                    <DataGrid.Columns>
                                        <DataGridTextColumn Header="資源 ID" Binding="{Binding ResourceId}" Width="*" MinWidth="180" />
                                        <DataGridTextColumn Header="占用時間" Binding="{Binding OccupiedTime}" Width="95" app:ResultTable.Kind="Numeric" />
                                        <DataGridTextColumn Header="使用率" Binding="{Binding Utilization}" Width="85" app:ResultTable.Kind="Numeric" />
                                        <DataGridTextColumn Header="預約次數" Binding="{Binding ReservationCount}" Width="85" app:ResultTable.Kind="Numeric" />
                                        <DataGridTextColumn Header="觀測每小時次數" Binding="{Binding ObservedReservationsPerHour}" Width="115" app:ResultTable.Kind="Numeric" />
                                        <DataGridTextColumn Header="最短釋放間距" Binding="{Binding MinimumReleaseHeadway}" Width="110" app:ResultTable.Kind="Numeric" />
                                    </DataGrid.Columns>
                                </DataGrid>
                            </Border>
                        </app:ResultPage>
                    </TabItem>
```

- [ ] **Step 4: 執行，確認通過；並跑相關既有子集**

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --result-pages-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --output-only
```

Expected: `[通過] 時刻表、區間、比較、容量頁…`、`PASS WPF result pages`；`--output-only`（時刻表、統計、CSV／PNG／PDF 輸出）通過；建置 0 warning。

- [ ] **Step 5: Commit**

```powershell
git add src/MrtRouteSimulator.App/MainWindow.xaml tests/MrtRouteSimulator.WpfTests/ResultPageTests.cs
git commit -m "feat: apply result page layout to timetable, segment, comparison and capacity pages" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: 閉塞、統計兩頁

**Files:**
- Modify: `src/MrtRouteSimulator.App/MainWindow.xaml`（`SafetyTabItem`、`IntervalStatisticsTabItem`）
- Test: `tests/MrtRouteSimulator.WpfTests/ResultPageTests.cs`

**Interfaces:**
- Consumes: Task 4 的測試輔助。
- Produces: 新 `x:Name` `JourneyStatisticsDataGrid`、`IntervalStatisticsDataGrid`。

- [ ] **Step 1: 寫失敗測試**

在 `Run` 加入 `VerifySafetyAndStatisticsPages();`、`VerifyCrowdedHeaders();`，並加入：

```csharp
    private static void VerifySafetyAndStatisticsPages()
    {
        var window = new MainWindow { Width = 1280, Height = 800 };
        try
        {
            window.Show();
            ShellLayoutTests.PumpLayout(window);
            var safety = Page(window, "SafetyTabItem");
            RequireIn(window, safety.Summary, "Summary", "SafetySummaryText");
            RequireIn(window, safety.Filters, "Filters", "BrakingModeComboBox", "SafetyPairComboBox",
                "SafetyDirectionComboBox", "SafetyStatusComboBox", "SafetyWindowComboBox");
            foreach (var label in new[] { "煞車估算", "列車對", "方向", "狀態", "時間" })
                Require(FindLogical<TextBlock>(safety.Filters, text => text.Text == label) is not null, $"閉塞頁篩選缺少標籤「{label}」。");
            RequireIn(window, safety.FilterActions, "FilterActions", "ObstacleTrainComboBox", "ObstacleDelayTextBox");
            Require(FindLogical<Button>(safety.FilterActions, button => Equals(button.Content, "觸發／排程急停")) is not null,
                "障礙物急停鈕必須位於篩選卡右側群組。");
            RequireIn(window, safety.Content, "Content", "SafetyDistanceCanvas", "SafetyDataGrid");
            VerifyTableColumns(window, "SafetyDataGrid",
                ["後車頭 km", "前車尾 km", "淨距 m", "安全距離 m", "煞車需求 m", "安全裕度 m"], ["狀態"]);

            var statistics = Page(window, "IntervalStatisticsTabItem");
            RequireIn(window, statistics.Summary, "Summary", "IntervalSummaryText");
            RequireIn(window, statistics.Filters, "Filters", "IntervalDirectionComboBox", "IntervalVehicleComboBox",
                "IntervalServiceRunComboBox", "IntervalVehicleTypeComboBox", "IntervalServiceTypeComboBox",
                "IntervalStopPatternComboBox", "IntervalStartSecondTextBox", "IntervalEndSecondTextBox", "IncludeInProgressCheckBox");
            foreach (var text in new[] { "重新整理", "匯出區間 CSV", "匯出彙總 CSV", "匯出全程平均 CSV" })
                Require(FindLogical<Button>(statistics.Actions, button => Equals(button.Content, text)) is not null,
                    $"統計頁「{text}」必須位於 Actions。");
            RequireIn(window, statistics.Content, "Content", "JourneyStatisticsDataGrid", "IntervalStatisticsDataGrid");
            VerifyTableColumns(window, "JourneyStatisticsDataGrid", ["起站離站", "終站抵達", "全程秒", "平均 km/h"], ["狀態"]);
            VerifyTableColumns(window, "IntervalStatisticsDataGrid",
                ["出發", "抵達", "旅行秒", "平均 km/h", "峰值 km/h", "閉塞受限 s"], ["狀態"]);
        }
        finally { WpfTestWait.Close(window); }
        Console.WriteLine("[通過] 閉塞、統計頁套用 ResultPage 與表格規則");
    }

    // 審查重點：窄視窗與 125% 縮放時，頁首動作區與篩選卡右側群組不可壓住標題或篩選、不可超出頁面。
    private static void VerifyCrowdedHeaders()
    {
        foreach (var (width, height, scale, tab) in new[]
                 {
                     (800d, 700d, 1d, "SafetyTabItem"), (800d, 700d, 1d, "IntervalStatisticsTabItem"),
                     (1280d, 800d, 1.25d, "IntervalStatisticsTabItem"), (1280d, 800d, 1.25d, "SafetyTabItem")
                 })
        {
            var previousScale = ShellLayoutTests.SetInterfaceScale(scale);
            var window = new MainWindow { Width = width, Height = height };
            try
            {
                window.Show();
                ((TabControl)window.FindName("WorkspaceTabControl")!).SelectedItem = window.FindName(tab);
                ShellLayoutTests.PumpLayout(window);
                var page = (ResultPage)((TabItem)window.FindName(tab)!).Content;
                Rect Bounds(FrameworkElement element) =>
                    new(element.TranslatePoint(new Point(0, 0), page), new Size(element.ActualWidth, element.ActualHeight));
                var context = $"{tab} {width}×{height}@{scale:0.##}";
                var title = Bounds((FrameworkElement)page.Template.FindName("PART_Title", page));
                if (page.Actions is FrameworkElement actionsElement)
                {
                    var actions = Bounds(actionsElement);
                    Require(!title.IntersectsWith(actions) && actions.Right <= page.ActualWidth + .5,
                        $"{context}：標題與動作區不可重疊或超出；title={title}, actions={actions}, page={page.ActualWidth:0.0}。");
                }
                if (page.Filters is FrameworkElement filtersElement && page.FilterActions is FrameworkElement filterActionsElement)
                {
                    var filters = Bounds(filtersElement);
                    var filterActions = Bounds(filterActionsElement);
                    Require(!filters.IntersectsWith(filterActions) && filterActions.Right <= page.ActualWidth + .5,
                        $"{context}：篩選與右側群組不可重疊或超出；filters={filters}, group={filterActions}, page={page.ActualWidth:0.0}。");
                }
            }
            finally
            {
                WpfTestWait.Close(window);
                ShellLayoutTests.SetInterfaceScale(previousScale);
            }
        }
        Console.WriteLine("[通過] 窄視窗與 125% 縮放下頁首動作區與篩選卡不重疊");
    }
```

- [ ] **Step 2: 執行，確認失敗**

執行共用指令前兩行。Expected: `SafetyTabItem 的內容必須是 ResultPage。`

- [ ] **Step 3: 實作**

閉塞（整段替換 `SafetyTabItem`）：

```xml
                    <TabItem x:Name="SafetyTabItem" Header="移動閉塞與煞車" app:ShellNav.ShortLabel="閉塞" app:ShellNav.Icon="&#xEA18;">
                        <app:ResultPage Title="移動閉塞與煞車">
                            <app:ResultPage.Description>
                                <TextBlock Text="相鄰列車距離、安全裕度與煞車需求" Style="{StaticResource ResultPageDescription}" />
                            </app:ResultPage.Description>
                            <app:ResultPage.Summary>
                                <TextBlock x:Name="SafetySummaryText" Text="建立 V2 模擬後顯示安全摘要。" Style="{StaticResource ResultPageSummary}" />
                            </app:ResultPage.Summary>
                            <app:ResultPage.Filters>
                                <WrapPanel>
                                    <TextBlock Text="煞車估算" Style="{StaticResource FilterLabel}" />
                                    <ComboBox x:Name="BrakingModeComboBox" Width="120" SelectedIndex="0" SelectionChanged="BrakingMode_SelectionChanged" Margin="0,3,12,3">
                                        <ComboBoxItem Content="營運煞車" Tag="Service" />
                                        <ComboBoxItem Content="緊急煞車" Tag="Emergency" />
                                    </ComboBox>
                                    <TextBlock Text="列車對" Style="{StaticResource FilterLabel}" />
                                    <ComboBox x:Name="SafetyPairComboBox" Width="180" SelectionChanged="SafetyPair_SelectionChanged" Margin="0,3,12,3" />
                                    <TextBlock Text="方向" Style="{StaticResource FilterLabel}" />
                                    <ComboBox x:Name="SafetyDirectionComboBox" Width="82" SelectedIndex="0" SelectionChanged="SafetyFilter_Changed" Margin="0,3,12,3">
                                        <ComboBoxItem Content="全方向" Tag="All" />
                                        <ComboBoxItem Content="下行" Tag="Outbound" />
                                        <ComboBoxItem Content="上行" Tag="Inbound" />
                                    </ComboBox>
                                    <TextBlock Text="狀態" Style="{StaticResource FilterLabel}" />
                                    <ComboBox x:Name="SafetyStatusComboBox" Width="110" SelectedIndex="0" SelectionChanged="SafetyFilter_Changed" Margin="0,3,12,3">
                                        <ComboBoxItem Content="全狀態" Tag="All" />
                                        <ComboBoxItem Content="安全" Tag="Safe" />
                                        <ComboBoxItem Content="警戒" Tag="Caution" />
                                        <ComboBoxItem Content="需制動" Tag="BrakingRequired" />
                                        <ComboBoxItem Content="侵入" Tag="EnvelopeIntrusion" />
                                    </ComboBox>
                                    <TextBlock Text="時間" Style="{StaticResource FilterLabel}" />
                                    <ComboBox x:Name="SafetyWindowComboBox" Width="100" SelectedIndex="1" SelectionChanged="SafetyFilter_Changed" Margin="0,3,12,3">
                                        <ComboBoxItem Content="最近 60 秒" Tag="60" />
                                        <ComboBoxItem Content="最近 300 秒" Tag="300" />
                                        <ComboBoxItem Content="全部時間" Tag="All" />
                                    </ComboBox>
                                </WrapPanel>
                            </app:ResultPage.Filters>
                            <app:ResultPage.FilterActions>
                                <WrapPanel>
                                    <TextBlock Text="障礙物急停" Style="{StaticResource FilterGroupLabel}" />
                                    <TextBlock Text="障礙車" Style="{StaticResource FilterLabel}" />
                                    <ComboBox x:Name="ObstacleTrainComboBox" Width="105" Margin="0,3,10,3" />
                                    <TextBlock Text="延遲秒數" Style="{StaticResource FilterLabel}" />
                                    <TextBox x:Name="ObstacleDelayTextBox" Width="52" Text="0" Margin="0,3,10,3" />
                                    <Button Content="觸發／排程急停" Style="{StaticResource SecondaryButton}" Click="ObstacleStop_Click" Margin="0,3,0,3" />
                                </WrapPanel>
                            </app:ResultPage.FilterActions>
                            <Grid>
                                <Grid.RowDefinitions>
                                    <RowDefinition Height="250" />
                                    <RowDefinition Height="*" />
                                </Grid.RowDefinitions>
                                <Border Style="{StaticResource ChartCard}">
                                    <Canvas x:Name="SafetyDistanceCanvas" SizeChanged="SafetyDistanceCanvas_SizeChanged" AutomationProperties.Name="相鄰列車距離時間圖" />
                                </Border>
                                <Border Grid.Row="1" Style="{StaticResource ResultCard}" Margin="0,8,0,0">
                                    <DataGrid x:Name="SafetyDataGrid" ItemsSource="{Binding SafetyRows}" IsReadOnly="True" RowHeaderWidth="0"
                                              BorderThickness="0" app:ResultTable.Enabled="True">
                                        <DataGrid.Columns>
                                            <DataGridTextColumn Header="後車 → 前車" Binding="{Binding Pair}" Width="130" />
                                            <DataGridTextColumn Header="軌道" Binding="{Binding Track}" Width="55" />
                                            <DataGridTextColumn Header="後車頭 km" Binding="{Binding FollowerKm}" Width="82" app:ResultTable.Kind="Numeric" />
                                            <DataGridTextColumn Header="前車尾 km" Binding="{Binding LeaderRearKm}" Width="82" app:ResultTable.Kind="Numeric" />
                                            <DataGridTextColumn Header="淨距 m" Binding="{Binding GapMeters}" Width="72" app:ResultTable.Kind="Numeric" />
                                            <DataGridTextColumn Header="安全距離 m" Binding="{Binding SafetyMeters}" Width="86" app:ResultTable.Kind="Numeric" />
                                            <DataGridTextColumn Header="煞車需求 m" Binding="{Binding BrakeDemandMeters}" Width="86" app:ResultTable.Kind="Numeric" />
                                            <DataGridTextColumn Header="安全裕度 m" Binding="{Binding MarginMeters}" Width="82" app:ResultTable.Kind="Numeric" />
                                            <DataGridTextColumn Header="狀態" Binding="{Binding Status}" Width="*" app:ResultTable.Kind="Status" />
                                        </DataGrid.Columns>
                                    </DataGrid>
                                </Border>
                            </Grid>
                        </app:ResultPage>
                    </TabItem>
```

統計（整段替換 `IntervalStatisticsTabItem`）：

```xml
                    <TabItem x:Name="IntervalStatisticsTabItem" Header="V2 區間統計" app:ShellNav.ShortLabel="統計" app:ShellNav.Icon="&#xE8EF;">
                        <app:ResultPage Title="V2 區間統計">
                            <app:ResultPage.Description>
                                <TextBlock Text="完成與運行中區間的旅行時間與速率" Style="{StaticResource ResultPageDescription}" />
                            </app:ResultPage.Description>
                            <app:ResultPage.Summary>
                                <TextBlock x:Name="IntervalSummaryText" Text="建立並播放 V2 模擬後顯示。" Style="{StaticResource ResultPageSummary}" />
                            </app:ResultPage.Summary>
                            <app:ResultPage.Actions>
                                <StackPanel Orientation="Horizontal">
                                    <Button Content="重新整理" Style="{StaticResource SecondaryButton}" Click="RefreshIntervalStatistics_Click" Margin="0,0,6,0" />
                                    <Button Content="匯出區間 CSV" Style="{StaticResource SecondaryButton}" Click="ExportIntervalCsv_Click" Margin="0,0,6,0" />
                                    <Button Content="匯出彙總 CSV" Style="{StaticResource SecondaryButton}" Click="ExportIntervalSummaryCsv_Click" Margin="0,0,6,0" />
                                    <Button Content="匯出全程平均 CSV" Style="{StaticResource SecondaryButton}" Click="ExportJourneyCsv_Click" />
                                </StackPanel>
                            </app:ResultPage.Actions>
                            <app:ResultPage.Filters>
                                <WrapPanel>
                                    <TextBlock Text="方向" Style="{StaticResource FilterLabel}" />
                                    <ComboBox x:Name="IntervalDirectionComboBox" Width="92" SelectedIndex="0" SelectionChanged="IntervalFilter_Changed" Margin="0,3,12,3">
                                        <ComboBoxItem Content="全部" Tag="All" />
                                        <ComboBoxItem Content="下行" Tag="Outbound" />
                                        <ComboBoxItem Content="上行" Tag="Inbound" />
                                    </ComboBox>
                                    <TextBlock Text="車輛" Style="{StaticResource FilterLabel}" />
                                    <ComboBox x:Name="IntervalVehicleComboBox" Width="112" DisplayMemberPath="DisplayName" SelectedValuePath="Id"
                                              SelectionChanged="IntervalFilter_Changed" Margin="0,3,12,3" />
                                    <TextBlock Text="車次" Style="{StaticResource FilterLabel}" />
                                    <ComboBox x:Name="IntervalServiceRunComboBox" Width="120" DisplayMemberPath="DisplayName" SelectedValuePath="Id"
                                              SelectionChanged="IntervalFilter_Changed" Margin="0,3,12,3" />
                                    <TextBlock Text="車型" Style="{StaticResource FilterLabel}" />
                                    <ComboBox x:Name="IntervalVehicleTypeComboBox" Width="120" DisplayMemberPath="DisplayName" SelectedValuePath="Id"
                                              SelectionChanged="IntervalFilter_Changed" Margin="0,3,12,3" />
                                    <TextBlock Text="服務" Style="{StaticResource FilterLabel}" />
                                    <ComboBox x:Name="IntervalServiceTypeComboBox" Width="120" DisplayMemberPath="DisplayName" SelectedValuePath="Id"
                                              SelectionChanged="IntervalFilter_Changed" Margin="0,3,12,3" />
                                    <TextBlock Text="停站模式" Style="{StaticResource FilterLabel}" />
                                    <ComboBox x:Name="IntervalStopPatternComboBox" Width="130" DisplayMemberPath="DisplayName" SelectedValuePath="Id"
                                              SelectionChanged="IntervalFilter_Changed" Margin="0,3,12,3" />
                                    <TextBlock Text="模擬秒起" Style="{StaticResource FilterLabel}" />
                                    <TextBox x:Name="IntervalStartSecondTextBox" Width="72" LostFocus="IntervalFilter_Changed" Margin="0,3,8,3" />
                                    <TextBlock Text="迄" Style="{StaticResource FilterLabel}" />
                                    <TextBox x:Name="IntervalEndSecondTextBox" Width="72" LostFocus="IntervalFilter_Changed" Margin="0,3,12,3" />
                                    <CheckBox x:Name="IncludeInProgressCheckBox" Content="包含運行中區間" IsChecked="True"
                                              Checked="IntervalFilter_Changed" Unchecked="IntervalFilter_Changed" Margin="0,3,0,3" VerticalAlignment="Center" />
                                </WrapPanel>
                            </app:ResultPage.Filters>
                            <Grid>
                                <Grid.RowDefinitions>
                                    <RowDefinition Height="Auto" />
                                    <RowDefinition Height="*" />
                                </Grid.RowDefinitions>
                                <Border Style="{StaticResource ResultCard}">
                                    <StackPanel>
                                        <TextBlock Text="每車次起終站平均速率" Style="{StaticResource ResultCardTitle}" />
                                        <TextBlock Text="以起站實際發車至終站實際抵達計算，包含中間站停站時間。" Style="{StaticResource ResultPageFootnote}" Margin="6,0,6,4" />
                                        <DataGrid x:Name="JourneyStatisticsDataGrid" ItemsSource="{Binding JourneyStatisticRows}" IsReadOnly="True" RowHeaderWidth="0"
                                                  Height="155" BorderThickness="0" app:ResultTable.Enabled="True">
                                            <DataGrid.Columns>
                                                <DataGridTextColumn Header="車輛" Binding="{Binding Vehicle}" Width="95" />
                                                <DataGridTextColumn Header="車次" Binding="{Binding ServiceRun}" Width="105" />
                                                <DataGridTextColumn Header="方向" Binding="{Binding Direction}" Width="55" />
                                                <DataGridTextColumn Header="起站 → 終站" Binding="{Binding Route}" Width="135" />
                                                <DataGridTextColumn Header="狀態" Binding="{Binding Status}" Width="74" app:ResultTable.Kind="Status" />
                                                <DataGridTextColumn Header="起站離站" Binding="{Binding Departure}" Width="88" app:ResultTable.Kind="Numeric" />
                                                <DataGridTextColumn Header="終站抵達" Binding="{Binding Arrival}" Width="88" app:ResultTable.Kind="Numeric" />
                                                <DataGridTextColumn Header="全程秒" Binding="{Binding TravelTime}" Width="72" app:ResultTable.Kind="Numeric" />
                                                <DataGridTextColumn Header="平均 km/h" Binding="{Binding AverageSpeed}" Width="85" app:ResultTable.Kind="Numeric" />
                                            </DataGrid.Columns>
                                        </DataGrid>
                                    </StackPanel>
                                </Border>
                                <Border Grid.Row="1" Style="{StaticResource ResultCard}" Margin="0,8,0,0">
                                    <DockPanel>
                                        <TextBlock DockPanel.Dock="Top" Text="區間明細" Style="{StaticResource ResultCardTitle}" />
                                        <DataGrid x:Name="IntervalStatisticsDataGrid" ItemsSource="{Binding IntervalStatisticRows}" IsReadOnly="True" RowHeaderWidth="0"
                                                  BorderThickness="0" app:ResultTable.Enabled="True">
                                            <DataGrid.Columns>
                                                <DataGridTextColumn Header="車輛" Binding="{Binding Vehicle}" Width="95" />
                                                <DataGridTextColumn Header="車次" Binding="{Binding ServiceRun}" Width="105" />
                                                <DataGridTextColumn Header="方向" Binding="{Binding Direction}" Width="55" />
                                                <DataGridTextColumn Header="區間" Binding="{Binding Interval}" Width="135" />
                                                <DataGridTextColumn Header="狀態" Binding="{Binding Status}" Width="74" app:ResultTable.Kind="Status" />
                                                <DataGridTextColumn Header="出發" Binding="{Binding Departure}" Width="88" app:ResultTable.Kind="Numeric" />
                                                <DataGridTextColumn Header="抵達" Binding="{Binding Arrival}" Width="88" app:ResultTable.Kind="Numeric" />
                                                <DataGridTextColumn Header="旅行秒" Binding="{Binding TravelTime}" Width="72" app:ResultTable.Kind="Numeric" />
                                                <DataGridTextColumn Header="平均 km/h" Binding="{Binding AverageSpeed}" Width="85" app:ResultTable.Kind="Numeric" />
                                                <DataGridTextColumn Header="峰值 km/h" Binding="{Binding PeakSpeed}" Width="85" app:ResultTable.Kind="Numeric" />
                                                <DataGridTextColumn Header="閉塞受限 s" Binding="{Binding ControlLimitedTime}" Width="85" app:ResultTable.Kind="Numeric" />
                                                <DataGridTextColumn Header="控制事件" Binding="{Binding ControlEvents}" Width="*" />
                                            </DataGrid.Columns>
                                        </DataGrid>
                                    </DockPanel>
                                </Border>
                            </Grid>
                        </app:ResultPage>
                    </TabItem>
```

- [ ] **Step 4: 執行，確認通過；並跑相關既有子集**

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --result-pages-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --output-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --speed-only
```

Expected: 兩個新 `[通過]` 與 `PASS WPF result pages`；`--output-only`、`--speed-only`（含安全摘要文字檢查）通過。若 `VerifyCrowdedHeaders` 在 800 DIP 失敗，先縮小閉塞頁 `SafetyPairComboBox` 或 `ObstacleTrainComboBox` 寬度、或縮小按鈕左右內距，不得移除控制項。

- [ ] **Step 5: Commit**

```powershell
git add src/MrtRouteSimulator.App/MainWindow.xaml tests/MrtRouteSimulator.WpfTests/ResultPageTests.cs
git commit -m "feat: apply result page layout to safety and interval statistics pages" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: 運行圖頁、模擬子分頁與色碼掃描

**Files:**
- Modify: `src/MrtRouteSimulator.App/MainWindow.xaml`（`DiagramTabItem`、`SimulationTabItem` 的三個子分頁）
- Test: `tests/MrtRouteSimulator.WpfTests/ResultPageTests.cs`

**Interfaces:**
- Consumes: Task 4 的測試輔助。
- Produces: 新 `x:Name` `SpeedProfileToolbar`。

- [ ] **Step 1: 寫失敗測試**

在 `Run` 加入 `VerifyDiagramAndSimulationPages();`、`VerifyNoHardCodedColors(root);`，並加入：

```csharp
    private static void VerifyDiagramAndSimulationPages()
    {
        var window = new MainWindow { Width = 1280, Height = 800 };
        try
        {
            window.Show();
            ShellLayoutTests.PumpLayout(window);
            var diagram = Page(window, "DiagramTabItem");
            RequireIn(window, diagram.Content, "Content", "DiagramWorkspaceGrid", "DiagramControlsExpander",
                "DiagramViewportBorder", "DiagramEventsExpander", "EventDataGrid");
            var groups = new (string Name, string[] Controls, string[] Buttons)[]
            {
                ("篩選", ["DiagramDirectionComboBox", "DiagramVehicleComboBox", "ShowPlannedCheckBox", "ShowActualCheckBox", "ShowEventsCheckBox"], []),
                ("檢視", ["DiagramZoomSlider", "DiagramVerticalZoomSlider", "DiagramStartMinuteTextBox", "DiagramEndMinuteTextBox",
                    "DiagramTimeTickComboBox", "DiagramShowEndTimeCheckBox"], ["套用刻度"]),
                ("匯出", ["HighResolutionCheckBox", "PdfPageSizeComboBox", "PdfSplitPagesCheckBox"], ["匯出 PNG", "匯出 PDF", "匯出實際全量 CSV"])
            };
            foreach (var (name, controls, buttons) in groups)
            {
                var row = FindLogical<DockPanel>(diagram.Content, panel => panel.Children.OfType<TextBlock>().FirstOrDefault()?.Text == name);
                Require(row is not null, $"運行圖控制面板缺少「{name}」列。");
                RequireIn(window, row, name, controls);
                foreach (var text in buttons)
                    Require(FindLogical<Button>(row, button => Equals(button.Content, text)) is not null, $"「{text}」必須位於「{name}」列。");
            }
            Require(window.FindName("DiagramViewportBorder") is Border { Style: var viewportStyle }
                    && ReferenceEquals(viewportStyle, Application.Current.FindResource("ChartCard")),
                "運行圖外框必須使用 ChartCard。");
            VerifyTableColumns(window, "EventDataGrid", ["時間", "里程 km"], []);

            VerifyTableColumns(window, "CurrentTrainDataGrid", ["車體中心 km", "速度 km/h"], ["狀態"]);
            var toolbar = (DockPanel)window.FindName("SpeedProfileToolbar")!;
            Require(toolbar is not null && toolbar.Children.OfType<TextBlock>().First().Text == "列車"
                    && toolbar.Children.Contains((UIElement)window.FindName("SpeedProfileRunComboBox")!)
                    && toolbar.Children.Contains((UIElement)window.FindName("SpeedProfileSourceText")!),
                "速度曲線子分頁必須以「列車」標籤＋下拉＋來源文字組成精簡工具列。");
            var speedViewer = (FrameworkElement)window.FindName("SpeedScrollViewer")!;
            Require(speedViewer.Parent is Border { Style: var speedStyle } && ReferenceEquals(speedStyle, Application.Current.FindResource("ChartCard")),
                "速度曲線必須放在 ChartCard 內。");
        }
        finally { WpfTestWait.Close(window); }
        Console.WriteLine("[通過] 運行圖頁三列控制面板、模擬子分頁工具列與表格規則");
    }

    private static void VerifyNoHardCodedColors(string root)
    {
        var xaml = File.ReadAllText(System.IO.Path.Combine(root, "src", "MrtRouteSimulator.App", "MainWindow.xaml"));
        var colors = Regex.Matches(xaml, "\"#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?\"").Select(match => match.Value).Distinct().ToArray();
        Require(colors.Length == 0, $"MainWindow.xaml 不得含寫死色碼：{string.Join("、", colors)}");
        Console.WriteLine("[通過] MainWindow.xaml 無寫死色碼");
    }
```

- [ ] **Step 2: 執行，確認失敗**

執行共用指令前兩行。Expected: `DiagramTabItem 的內容必須是 ResultPage。`

- [ ] **Step 3: 實作**

運行圖（整段替換 `DiagramTabItem`）：

```xml
                    <TabItem x:Name="DiagramTabItem" Header="列車運行圖／匯出" app:ShellNav.ShortLabel="運行圖" app:ShellNav.Icon="&#xE9D2;">
                        <app:ResultPage Title="列車運行圖／匯出">
                            <app:ResultPage.Description>
                                <TextBlock Text="時間－里程運行圖；篩選同時套用到 PNG／PDF" Style="{StaticResource ResultPageDescription}" />
                            </app:ResultPage.Description>
                            <Grid x:Name="DiagramWorkspaceGrid">
                                <Grid.RowDefinitions>
                                    <RowDefinition Height="Auto" />
                                    <RowDefinition Height="*" />
                                    <RowDefinition Height="Auto" />
                                </Grid.RowDefinitions>
                                <Expander x:Name="DiagramControlsExpander"
                                          Header="運行圖篩選、縮放與匯出（可收合）"
                                          IsExpanded="True"
                                          Expanded="DiagramLayoutExpander_Changed"
                                          Collapsed="DiagramLayoutExpander_Changed"
                                          Margin="0,0,0,8">
                                    <Border Style="{StaticResource FilterCard}" Margin="0,4,0,0">
                                        <StackPanel>
                                            <DockPanel>
                                                <TextBlock Text="篩選" DockPanel.Dock="Left" Style="{StaticResource FilterGroupLabel}" VerticalAlignment="Top" Margin="0,11,10,0" />
                                                <WrapPanel>
                                                    <TextBlock Text="方向" Style="{StaticResource FilterLabel}" />
                                                    <ComboBox x:Name="DiagramDirectionComboBox" Width="108" SelectedIndex="0" SelectionChanged="DiagramFilter_Changed" ToolTip="選擇顯示上下行、僅下行或僅上行；計畫線、實際線與事件觸發點一起篩選，PNG／PDF 亦跟隨。" Margin="0,3,12,3">
                                                        <ComboBoxItem Content="上下行皆顯示" Tag="All" />
                                                        <ComboBoxItem Content="僅下行" Tag="Outbound" />
                                                        <ComboBoxItem Content="僅上行" Tag="Inbound" />
                                                    </ComboBox>
                                                    <TextBlock Text="車輛" Style="{StaticResource FilterLabel}" />
                                                    <ComboBox x:Name="DiagramVehicleComboBox" Width="125" SelectionChanged="DiagramFilter_Changed" Margin="0,3,12,3" />
                                                    <CheckBox x:Name="ShowPlannedCheckBox" Content="計畫／理論" IsChecked="True" Checked="DiagramFilter_Changed" Unchecked="DiagramFilter_Changed" Margin="0,3,12,3" VerticalAlignment="Center" />
                                                    <CheckBox x:Name="ShowActualCheckBox" Content="模擬實際" IsChecked="True" Checked="DiagramFilter_Changed" Unchecked="DiagramFilter_Changed" Margin="0,3,12,3" VerticalAlignment="Center" />
                                                    <CheckBox x:Name="ShowEventsCheckBox" Content="顯示事件觸發點" IsChecked="True" Checked="DiagramFilter_Changed" Unchecked="DiagramFilter_Changed" ToolTip="顯示或隱藏運行圖的事件圓點；不影響列車軌跡、事件表及實際全量 CSV。" Margin="0,3,12,3" VerticalAlignment="Center" />
                                                </WrapPanel>
                                            </DockPanel>
                                            <DockPanel Margin="0,2,0,0">
                                                <TextBlock Text="檢視" DockPanel.Dock="Left" Style="{StaticResource FilterGroupLabel}" VerticalAlignment="Top" Margin="0,11,10,0" />
                                                <WrapPanel>
                                                    <TextBlock Text="左右縮放" Style="{StaticResource FilterLabel}" />
                                                    <Slider x:Name="DiagramZoomSlider" Minimum="1" Maximum="4" Value="1" Width="100" ValueChanged="DiagramZoom_Changed" Margin="0,3,12,3" VerticalAlignment="Center" />
                                                    <TextBlock Text="上下縮放" Style="{StaticResource FilterLabel}" />
                                                    <Slider x:Name="DiagramVerticalZoomSlider" Minimum="1" Maximum="4" Value="1" Width="100" ValueChanged="DiagramZoom_Changed" ToolTip="只調整里程軸高度，不改變時間軸寬度。" Margin="0,3,12,3" VerticalAlignment="Center" />
                                                    <TextBlock Text="起始分鐘" Style="{StaticResource FilterLabel}" />
                                                    <TextBox x:Name="DiagramStartMinuteTextBox" Width="48" Text="0" TextChanged="DiagramTimeFilter_TextChanged" Margin="0,3,8,3" />
                                                    <TextBlock Text="結束分鐘" Style="{StaticResource FilterLabel}" />
                                                    <TextBox x:Name="DiagramEndMinuteTextBox" Width="55" Text="" TextChanged="DiagramTimeFilter_TextChanged" Margin="0,3,12,3" />
                                                    <TextBlock Text="時間刻度（分）" Style="{StaticResource FilterLabel}" />
                                                    <ComboBox x:Name="DiagramTimeTickComboBox" Width="80" IsEditable="True" SelectedIndex="0" ToolTip="選擇或輸入每格分鐘數，再按套用刻度。自動為六等分；可輸入1～1440分鐘，最多2000格，過密時保留原設定。" Margin="0,3,6,3">
                                                        <ComboBoxItem Content="自動" />
                                                        <ComboBoxItem Content="1" />
                                                        <ComboBoxItem Content="2" />
                                                        <ComboBoxItem Content="5" />
                                                        <ComboBoxItem Content="10" />
                                                        <ComboBoxItem Content="15" />
                                                        <ComboBoxItem Content="30" />
                                                        <ComboBoxItem Content="60" />
                                                    </ComboBox>
                                                    <Button Content="套用刻度" Style="{StaticResource SecondaryButton}" Click="DiagramTimeTicksApply_Click" Margin="0,3,12,3" />
                                                    <CheckBox x:Name="DiagramShowEndTimeCheckBox" Content="顯示終點時間" IsChecked="True" Checked="DiagramFilter_Changed" Unchecked="DiagramFilter_Changed" ToolTip="在固定間隔刻度之外補上目前圖表右端時間；有設定結束分鐘時顯示該篩選終點，並非尚未完成模擬的實際完成時間。PNG／PDF亦跟隨。" Margin="0,3,0,3" VerticalAlignment="Center" />
                                                </WrapPanel>
                                            </DockPanel>
                                            <DockPanel Margin="0,2,0,0">
                                                <TextBlock Text="匯出" DockPanel.Dock="Left" Style="{StaticResource FilterGroupLabel}" VerticalAlignment="Top" Margin="0,11,10,0" />
                                                <WrapPanel>
                                                    <CheckBox x:Name="HighResolutionCheckBox" Content="高解析 PNG" Margin="0,3,12,3" VerticalAlignment="Center" />
                                                    <TextBlock Text="PDF 紙張" Style="{StaticResource FilterLabel}" />
                                                    <ComboBox x:Name="PdfPageSizeComboBox" Width="68" SelectedIndex="0" Margin="0,3,8,3">
                                                        <ComboBoxItem Content="A4" Tag="A4" />
                                                        <ComboBoxItem Content="A3" Tag="A3" />
                                                    </ComboBox>
                                                    <CheckBox x:Name="PdfSplitPagesCheckBox" Content="PDF 分頁" IsChecked="True" Margin="0,3,12,3" VerticalAlignment="Center" />
                                                    <Button Content="匯出 PNG" Style="{StaticResource SecondaryButton}" Click="ExportPng_Click" Margin="0,3,6,3" />
                                                    <Button Content="匯出 PDF" Style="{StaticResource SecondaryButton}" Click="ExportPdf_Click" Margin="0,3,6,3" />
                                                    <Button Content="匯出實際全量 CSV" ToolTip="匯出全部 V2 實際軌跡；不受運行圖方向、車輛、時間範圍及計畫／實際勾選影響。" Style="{StaticResource SecondaryButton}" Click="ExportCsv_Click" Margin="0,3,0,3" />
                                                </WrapPanel>
                                            </DockPanel>
                                        </StackPanel>
                                    </Border>
                                </Expander>
                                <Border Grid.Row="1" x:Name="DiagramViewportBorder" MinHeight="344" Style="{StaticResource ChartCard}">
                                    <Grid>
                                        <Grid.RowDefinitions>
                                            <RowDefinition Height="*" />
                                            <RowDefinition Height="32" />
                                        </Grid.RowDefinitions>
                                        <ScrollViewer x:Name="DiagramScrollViewer" MinHeight="300" HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Auto"
                                                      SizeChanged="DiagramScrollViewer_SizeChanged" ScrollChanged="DiagramScrollViewer_ScrollChanged">
                                            <Canvas x:Name="TimeDistanceCanvas" MinWidth="760" MinHeight="380" SizeChanged="TimeDistanceCanvas_SizeChanged" AutomationProperties.Name="時間里程列車運行圖" />
                                        </ScrollViewer>
                                        <Border x:Name="DiagramTimeAxisViewport" Grid.Row="1" Height="32" HorizontalAlignment="Left" ClipToBounds="True"
                                                Background="{x:Static app:UiTheme.CanvasBackgroundBrush}">
                                            <Canvas x:Name="DiagramTimeAxisCanvas" Height="32" AutomationProperties.Name="固定時間橫軸" IsHitTestVisible="False" />
                                        </Border>
                                    </Grid>
                                </Border>
                                <Expander Grid.Row="2" x:Name="DiagramEventsExpander" Header="事件列表（可收折）" IsExpanded="True"
                                          Expanded="DiagramLayoutExpander_Changed" Collapsed="DiagramLayoutExpander_Changed"
                                          Margin="0,8,0,0">
                                    <DataGrid x:Name="EventDataGrid" Height="140" ItemsSource="{Binding EventRows}" IsReadOnly="True" RowHeaderWidth="0" Margin="0,6,0,0"
                                              app:ResultTable.Enabled="True">
                                        <DataGrid.Columns>
                                            <DataGridTextColumn Header="時間" Binding="{Binding Time}" Width="95" app:ResultTable.Kind="Numeric" />
                                            <DataGridTextColumn Header="事件" Binding="{Binding Type}" Width="110" />
                                            <DataGridTextColumn Header="車輛／車次" Binding="{Binding Vehicle}" Width="130" />
                                            <DataGridTextColumn Header="里程 km" Binding="{Binding PositionKm}" Width="80" app:ResultTable.Kind="Numeric" />
                                            <DataGridTextColumn Header="說明" Binding="{Binding Message}" Width="*" />
                                        </DataGrid.Columns>
                                    </DataGrid>
                                </Expander>
                            </Grid>
                        </app:ResultPage>
                    </TabItem>
```

模擬頁子分頁：

1. 「軌道配線與列車位置」子分頁的外框開始標籤

```xml
                                    <Border Background="#FAFBFD" BorderBrush="{StaticResource BorderBrush}" BorderThickness="1" CornerRadius="9" Padding="8">
```

改為

```xml
                                    <Border Background="{x:Static app:UiTheme.CanvasBackgroundBrush}" BorderBrush="{StaticResource BorderBrush}" BorderThickness="1" CornerRadius="9" Padding="8">
```

2. 「即時列車狀態」子分頁的 `CurrentTrainDataGrid` 整段替換為：

```xml
                                    <DataGrid x:Name="CurrentTrainDataGrid" Margin="0,6,0,0"
                                              ItemsSource="{Binding CurrentTrainRows}" IsReadOnly="True" RowHeaderWidth="0" app:ResultTable.Enabled="True">
                                        <DataGrid.Columns>
                                            <DataGridTextColumn Header="列車" Binding="{Binding TrainId}" Width="100" />
                                            <DataGridTextColumn Header="方向" Binding="{Binding Direction}" Width="80" />
                                            <DataGridTextColumn Header="狀態" Binding="{Binding State}" Width="124" app:ResultTable.Kind="Status" />
                                            <DataGridTextColumn Header="車體中心 km" Binding="{Binding PositionKm}" Width="120" app:ResultTable.Kind="Numeric" />
                                            <DataGridTextColumn Header="速度 km/h" Binding="{Binding SpeedKmh}" Width="110" app:ResultTable.Kind="Numeric" />
                                            <DataGridTextColumn Header="下一站" Binding="{Binding NextStation}" Width="*" />
                                        </DataGrid.Columns>
                                    </DataGrid>
```

3. 「完整行程速度曲線」子分頁 `SpeedProfileTabItem` 的內容（`<Grid Margin="0,6,0,0">` 起到對應 `</Grid>`）整段替換為：

```xml
                                    <Grid>
                                        <Grid.RowDefinitions>
                                            <RowDefinition Height="Auto" />
                                            <RowDefinition Height="*" />
                                        </Grid.RowDefinitions>
                                        <DockPanel x:Name="SpeedProfileToolbar" Margin="0,0,0,6" LastChildFill="True">
                                            <TextBlock Text="列車" DockPanel.Dock="Left" Style="{StaticResource FilterLabel}" />
                                            <ComboBox x:Name="SpeedProfileRunComboBox" DockPanel.Dock="Left" Width="190"
                                                      SelectionChanged="SpeedProfileRun_SelectionChanged"
                                                      AutomationProperties.Name="完整行程列車" />
                                            <TextBlock x:Name="SpeedProfileSourceText" Text="V2 實際／計畫預覽"
                                                       Style="{StaticResource ResultPageDescription}" Margin="10,0,0,0" />
                                        </DockPanel>
                                        <Border Grid.Row="1" Style="{StaticResource ChartCard}">
                                            <ScrollViewer x:Name="SpeedScrollViewer"
                                                          VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled"
                                                          SizeChanged="SpeedScrollViewer_SizeChanged">
                                            <Canvas x:Name="SpeedCanvas" ClipToBounds="True" MinHeight="220" Height="220"
                                                    SizeChanged="SpeedCanvas_SizeChanged"
                                                    AutomationProperties.Name="列車完整行程速度時間曲線" />
                                            </ScrollViewer>
                                        </Border>
                                    </Grid>
```

- [ ] **Step 4: 執行，確認通過；並跑運行圖與模擬頁相關既有子集**

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --result-pages-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --diagram-compact-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --diagram-preparation-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --interface-scale-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --native-final-only
```

Expected: 兩個新 `[通過]` 與 `PASS WPF result pages`；運行圖精簡版面、運行圖準備、介面縮放、`--native-final-only`（含 `TimeDistance*`、`CompactRouteLayoutTests`、`OuterShellScrollTests`）通過。運行圖版面數值測試若因頁首高度失敗，依 Global Constraints 只調整數值並記錄 `Ruling`。

- [ ] **Step 5: Commit**

```powershell
git add src/MrtRouteSimulator.App/MainWindow.xaml tests/MrtRouteSimulator.WpfTests/ResultPageTests.cs
git commit -m "feat: apply result page layout to diagram page and simulation sub-tabs" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: 主題層修正（停用提示、選單白字、下拉焦點框）

**Files:**
- Modify: `src/MrtRouteSimulator.App/Themes/Controls.xaml`
- Modify: `src/MrtRouteSimulator.App/MainWindow.xaml`（`MainMenu`）
- Test: `tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs`

**Interfaces:**
- Produces: 樣式鍵 `AppBarMenu`；資源鍵 `MenuTopLevelHighlightBrush`。

- [ ] **Step 1: 寫失敗測試**

在 `ShellLayoutTests.Run` 的 `Console.WriteLine` 前加入 `VerifyDisabledAppBarTooltips();`、`VerifyMenuStyles();`、`VerifyComboFocusFrame();`，並加入：

```csharp
    private static void VerifyDisabledAppBarTooltips()
    {
        var window = new MainWindow { Width = 1280, Height = 800 };
        try
        {
            window.Show();
            PumpLayout(window);
            foreach (var name in new[] { "PlayButton", "ObstacleStopButton" })
            {
                var button = (Button)window.FindName(name)!;
                Require(!button.IsEnabled && ToolTipService.GetShowOnDisabled(button) && button.ToolTip is not null,
                    $"{name} 停用時也必須顯示提示框。");
            }
        }
        finally { WpfTestWait.Close(window); }
        Console.WriteLine("[通過] 停用中的標題列圖示鈕仍顯示提示框");
    }

    private static void VerifyMenuStyles()
    {
        var plainMenu = new Menu();
        var plainItem = new MenuItem { Header = "一般選單" };
        plainItem.Items.Add(new MenuItem { Header = "子項" });
        plainMenu.Items.Add(plainItem);
        var light = new Window { Width = 300, Height = 120, Content = plainMenu };
        var window = new MainWindow { Width = 1280, Height = 800 };
        try
        {
            light.Show();
            window.Show();
            PumpLayout(light);
            PumpLayout(window);
            Require(ReferenceEquals(plainMenu.Foreground, UiTheme.TextStrongBrush), "一般選單必須是深色字，白字只屬於標題列選單。");
            Require(ReferenceEquals(plainItem.TryFindResource("MenuTopLevelHighlightBrush"), UiTheme.NavHoverBrush),
                "一般選單的頂層滑過底色應為 NavHover。");
            var mainMenu = (Menu)window.FindName("MainMenu")!;
            Require(ReferenceEquals(mainMenu.Style, Application.Current.FindResource("AppBarMenu"))
                    && ReferenceEquals(mainMenu.Foreground, UiTheme.AppBarTextBrush),
                "主選單必須套用 AppBarMenu 並為白字。");
            var topItem = mainMenu.Items.OfType<MenuItem>().First();
            Require(ReferenceEquals(topItem.TryFindResource("MenuTopLevelHighlightBrush"), UiTheme.AppBarRaisedBrush),
                "標題列選單的頂層滑過底色應為 AppBarRaised。");
        }
        finally
        {
            light.Close();
            WpfTestWait.Close(window);
        }
        Console.WriteLine("[通過] 選單白字只套用在標題列選單");
    }

    private static void VerifyComboFocusFrame()
    {
        var readOnly = new ComboBox { ItemsSource = new[] { "A", "B" }, SelectedIndex = 0, Width = 120 };
        var editable = new ComboBox { IsEditable = true, ItemsSource = new[] { "1", "2" }, Width = 120 };
        var panel = new StackPanel();
        panel.Children.Add(readOnly);
        panel.Children.Add(editable);
        var window = new Window { Width = 300, Height = 200, Content = panel };
        try
        {
            window.Show();
            window.Activate();
            PumpLayout(window);
            foreach (var (combo, target) in new (ComboBox Combo, IInputElement Target)[]
                     {
                         (readOnly, readOnly),
                         (editable, (IInputElement)editable.Template.FindName("PART_EditableTextBox", editable))
                     })
            {
                Keyboard.Focus(target);
                PumpLayout(window);
                var toggle = (ToggleButton)combo.Template.FindName("ToggleButton", combo);
                Require(combo.IsKeyboardFocusWithin && ReferenceEquals(toggle.BorderBrush, UiTheme.AccentBrush) && toggle.BorderThickness.Left >= 2,
                    $"{(combo.IsEditable ? "可編輯" : "唯讀")}下拉取得焦點時必須顯示 Accent 2 px 框；focus={combo.IsKeyboardFocusWithin}, border={toggle.BorderBrush}, thickness={toggle.BorderThickness}。");
            }
        }
        finally { window.Close(); }
        Console.WriteLine("[通過] 下拉選單取得焦點時顯示 Accent 框");
    }
```

- [ ] **Step 2: 執行，確認失敗**

執行共用指令前一行與第三行。Expected: `PlayButton 停用時也必須顯示提示框。`

- [ ] **Step 3: 實作**

`Themes/Controls.xaml`：

1. `AppBarButton` 樣式加入一行 setter：

```xml
        <Setter Property="ToolTipService.ShowOnDisabled" Value="True" />
```

2. 隱含 `Menu` 樣式的

```xml
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.AppBarTextBrush}" />
```

改為

```xml
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.TextStrongBrush}" />
```

並在該 `Menu` 樣式的 `</Style>` 之後加入：

```xml

    <!-- 頂層選單項目的滑過底色：一般淺色視窗用 NavHover，標題列（AppBarMenu）覆寫為 AppBarRaised。 -->
    <x:Static x:Key="MenuTopLevelHighlightBrush" Member="app:UiTheme.NavHoverBrush" />

    <Style x:Key="AppBarMenu" TargetType="Menu" BasedOn="{StaticResource {x:Type Menu}}">
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.AppBarTextBrush}" />
        <Style.Resources>
            <x:Static x:Key="MenuTopLevelHighlightBrush" Member="app:UiTheme.AppBarRaisedBrush" />
        </Style.Resources>
    </Style>
```

3. `MenuTopLevelHeader` 範本（`IsHighlighted`、`IsSubmenuOpen` 兩個觸發）與 `MenuTopLevelItem` 範本（`IsHighlighted` 觸發）中的

```xml
<Setter TargetName="Chrome" Property="Background" Value="{x:Static app:UiTheme.AppBarRaisedBrush}" />
```

共 3 處，全部改為

```xml
<Setter TargetName="Chrome" Property="Background" Value="{DynamicResource MenuTopLevelHighlightBrush}" />
```

4. `ComboBox` 範本的 `<ControlTemplate.Triggers>` 內（`IsEditable` 觸發之前）加入：

```xml
                        <Trigger Property="IsKeyboardFocusWithin" Value="True">
                            <Setter TargetName="ToggleButton" Property="BorderBrush" Value="{x:Static app:UiTheme.AccentBrush}" />
                            <Setter TargetName="ToggleButton" Property="BorderThickness" Value="2" />
                        </Trigger>
```

`MainWindow.xaml`：`<Menu x:Name="MainMenu" ...` 開始標籤加入屬性 `Style="{StaticResource AppBarMenu}"`。

- [ ] **Step 4: 執行，確認通過**

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --shell-layout-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --interface-scale-only
```

Expected: 三個新 `[通過]` 與 `PASS WPF shell layout`；介面縮放子集通過。

- [ ] **Step 5: Commit**

```powershell
git add src/MrtRouteSimulator.App/Themes/Controls.xaml src/MrtRouteSimulator.App/MainWindow.xaml tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs
git commit -m "fix: scope white menu text to the app bar and add focus and disabled tooltips" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: 狀態圓點修正

**Files:**
- Modify: `src/MrtRouteSimulator.App/MainWindow.Shell.cs`
- Modify: `src/MrtRouteSimulator.App/MainWindow.xaml.cs`
- Modify: `src/MrtRouteSimulator.App/MainWindow.V2.cs`
- Test: `tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs`

**Interfaces:**
- Produces: 欄位 `_playbackStoppedByError`；方法 `StopPlaybackAfterUiFailure(Exception)`；`UpdateStatusIndicator()` 新規則（驗證警告、因錯誤停止或工作者錯誤 → 橘；V2 播放中或 V1 計時器運作中 → 綠；其他 → 灰）。

- [ ] **Step 1: 寫失敗測試**

在 `ShellLayoutTests.Run` 加入 `VerifyStatusIndicatorStates();`，並加入：

```csharp
    private static void VerifyStatusIndicatorStates()
    {
        var window = new MainWindow { Width = 1280, Height = 800 };
        var previousContext = SynchronizationContext.Current;
        try
        {
            window.Show();
            PumpLayout(window);
            var dot = (System.Windows.Shapes.Ellipse)window.FindName("StatusIndicatorDot")!;
            ((ComboBox)window.FindName("EngineModeComboBox")!).SelectedIndex = 0; // V1 基礎物理
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
            var play = (Button)window.FindName("PlayButton")!;
            WpfTestWait.Invoke(window, "RunSimulation_Click", play, new RoutedEventArgs());
            for (var attempt = 0; attempt < 100 && !play.IsEnabled; attempt++) WpfTestWait.Wait(Task.Delay(50));
            Require(play.IsEnabled && WpfTestWait.Field(window, "_simulationEngine") is not null, "V1 模擬必須建立完成。");
            WpfTestWait.Invoke(window, "Play_Click", play, new RoutedEventArgs());
            WpfTestWait.Wait(Task.Delay(50));
            Require(ReferenceEquals(dot.Fill, UiTheme.SuccessBrush), "V1 播放中狀態圓點必須為綠色。");
            WpfTestWait.Invoke(window, "Pause_Click", play, new RoutedEventArgs());
            WpfTestWait.Wait(Task.Delay(50));
            Require(ReferenceEquals(dot.Fill, UiTheme.TextSubtleBrush), "V1 暫停後狀態圓點必須為灰色。");
            WpfTestWait.Invoke(window, "StopPlaybackAfterUiFailure", new InvalidOperationException("測試用畫面更新失敗"));
            Require(ReferenceEquals(dot.Fill, UiTheme.PrimaryBrush)
                    && ((TextBlock)window.FindName("PlaybackStatusText")!).Text.Contains("測試用畫面更新失敗"),
                "畫面更新失敗而停止時狀態圓點必須為橘色。");
            WpfTestWait.Invoke(window, "ClearResults");
            Require(ReferenceEquals(dot.Fill, UiTheme.TextSubtleBrush), "清除結果後狀態圓點必須回到灰色。");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
            WpfTestWait.Close(window);
        }
        Console.WriteLine("[通過] 狀態圓點：V1 播放、畫面更新失敗與清除結果");
    }
```

- [ ] **Step 2: 執行，確認失敗**

執行共用指令前一行與第三行。Expected: `V1 播放中狀態圓點必須為綠色。`

- [ ] **Step 3: 實作**

`MainWindow.Shell.cs` 整個檔案替換為：

```csharp
using System.Diagnostics;
using System.Windows;

namespace MrtRouteSimulator.App;

public partial class MainWindow
{
    // 畫面更新失敗而停止播放後為 true；重設或清除結果時清除。
    private bool _playbackStoppedByError;

    // 播放旗標的唯一寫入點：同步刷新狀態列圓點。
    private void SetV2PlaybackPlaying(bool playing)
    {
        _isV2PlaybackPlaying = playing;
        UpdateStatusIndicator();
    }

    // 狀態列圓點：驗證警告、因錯誤停止或播放工作者錯誤為橘；V2 播放中或 V1 計時器運作中為綠；其他為灰。
    private void UpdateStatusIndicator()
    {
        if (StatusIndicatorDot is null || ValidationBorder is null) return;
        var hasProblem = ValidationBorder.Visibility == Visibility.Visible
            || _playbackStoppedByError
            || _playbackWorker?.Completion.IsFaulted == true;
        var playing = _isV2PlaybackPlaying
            || (!_v2Enabled && _simulationEngine is not null && _playbackTimer?.IsEnabled == true);
        StatusIndicatorDot.Fill = hasProblem
            ? UiTheme.PrimaryBrush
            : playing ? UiTheme.SuccessBrush : UiTheme.TextSubtleBrush;
    }

    // 畫面更新失敗而停止播放：保留專案資料，狀態圓點標示為錯誤（橘）。
    private void StopPlaybackAfterUiFailure(Exception exception)
    {
        NativeAcceptanceAbortForLifecycle("ui-update-failed");
        PausePlayback();
        _playbackStoppedByError = true;
        Trace.WriteLine($"Playback stopped after an unexpected UI update failure: {exception}");
        PlaybackStatusText.Text = $"播放已停止：{exception.Message}";
        StatusTextBlock.Text = "播放更新失敗；模擬已暫停，專案資料仍保留。";
        UpdateStatusIndicator();
    }
}
```

`MainWindow.xaml.cs`：

1. `PlaybackTimer_Tick` 的 `catch` 區塊

```csharp
        catch (Exception exception)
        {
            NativeAcceptanceAbortForLifecycle("ui-update-failed");
            PausePlayback();
            Trace.WriteLine($"Playback stopped after an unexpected UI update failure: {exception}");
            PlaybackStatusText.Text = $"播放已停止：{exception.Message}";
            StatusTextBlock.Text = "播放更新失敗；模擬已暫停，專案資料仍保留。";
        }
```

替換為

```csharp
        catch (Exception exception)
        {
            StopPlaybackAfterUiFailure(exception);
        }
```

2. V1 播放開始處

```csharp
            _playbackTimer.Start();
            PlaybackStatusText.Text = "播放中；倍率只影響畫面，不改變物理結果。";
```

改為

```csharp
            _playbackTimer.Start();
            UpdateStatusIndicator();
            PlaybackStatusText.Text = "播放中；倍率只影響畫面，不改變物理結果。";
```

3. `PausePlaybackAsync` 結尾的

```csharp
            await PauseWorkerSafelyAsync(worker, inputToken);
            return;
        }

        _playbackTimer.Stop();
    }
```

改為

```csharp
            await PauseWorkerSafelyAsync(worker, inputToken);
            return;
        }

        _playbackTimer.Stop();
        UpdateStatusIndicator();
    }
```

4. `ResetPlaybackAsync` 方法本體的第一行之前（`private async Task ResetPlaybackAsync()` 下一行的 `{` 之後）加入：

```csharp
        _playbackStoppedByError = false;
        UpdateStatusIndicator();
```

`MainWindow.V2.cs` 的 `ClearV2Results`：把

```csharp
        var worker = _playbackWorker;
        _playbackWorker = null;
        if (worker is not null)
        {
            _ = DisposePlaybackWorkerSafelyAsync(worker);
        }
```

改為

```csharp
        var worker = _playbackWorker;
        _playbackWorker = null;
        if (worker is not null)
        {
            _ = DisposePlaybackWorkerSafelyAsync(worker);
        }

        _playbackStoppedByError = false;
        UpdateStatusIndicator();
```

`MainWindow.xaml.cs` 的 `using System.Diagnostics;` 保持不動（未使用的 using 只是資訊提示，不會因 `TreatWarningsAsErrors` 失敗）。

- [ ] **Step 4: 執行，確認通過**

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --shell-layout-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --playback-only
```

Expected: `[通過] 狀態圓點：V1 播放、畫面更新失敗與清除結果`、`PASS WPF shell layout`；`--playback-only` 通過；建置 0 warning。

- [ ] **Step 5: Commit**

```powershell
git add src/MrtRouteSimulator.App/MainWindow.Shell.cs src/MrtRouteSimulator.App/MainWindow.xaml.cs src/MrtRouteSimulator.App/MainWindow.V2.cs tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs
git commit -m "fix: keep the status dot in sync for V1 playback, UI failures and cleared results" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: 外殼互動修正（固定捲軸、KPI 細條、抽屜焦點、起稿鈕文字）

**Files:**
- Modify: `src/MrtRouteSimulator.App/MainWindow.xaml`（KPI 細條、抽屜位置）
- Modify: `src/MrtRouteSimulator.App/MainWindow.ShellScroll.cs`
- Modify: `src/MrtRouteSimulator.App/MainWindow.WorkspaceNavigation.cs`
- Modify: `src/MrtRouteSimulator.App/MainWindow.Topology.cs`
- Modify: `src/MrtRouteSimulator.App/MainWindow.V2.cs`
- Test: `tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs`

**Interfaces:**
- Produces: 方法 `UpdateQuickBuilderToggleText()`。

- [ ] **Step 1: 寫失敗測試**

在 `ShellLayoutTests.Run` 加入 `VerifyRouteScrollbarAvoidsDrawer(root);`、`VerifyKpiStripTruncates();`、`VerifyDrawerFocusReturn();`、`VerifyQuickBuilderToggleText(root);`，並加入：

```csharp
    private static void VerifyRouteScrollbarAvoidsDrawer(string root)
    {
        var previousScale = SetInterfaceScale(1);
        var window = new MainWindow { Width = 1280, Height = 800 };
        try
        {
            LoadSample(window, root, "10-小型-三站完整拓樸基準範例.mrtsim.json");
            window.Show();
            WpfTestWait.Wait(Task.Delay(200));
            ((TabControl)window.FindName("WorkspaceTabControl")!).SelectedItem = window.FindName("SimulationTabItem");
            ((TabControl)window.FindName("SimulationViewTabControl")!).SelectedIndex = 0;
            PumpLayout(window);
            var viewer = (ScrollViewer)window.FindName("RouteScrollViewer")!;
            var canvas = (Canvas)window.FindName("RouteCanvas")!;
            canvas.MinWidth = viewer.ViewportWidth + 600;
            PumpLayout(window);
            WpfTestWait.Invoke(window, "SetQuickBuilderDrawerOpen", true);
            PumpLayout(window);
            WpfTestWait.Invoke(window, "UpdateShellRouteScrollbar");
            var proxy = (ScrollBar)window.FindName("ShellRouteHorizontalScrollBar")!;
            var overlay = (FrameworkElement)window.FindName("ShellOverlayRoot")!;
            var drawer = (FrameworkElement)window.FindName("QuickBuilderSidebar")!;
            var drawerRight = drawer.TransformToAncestor(overlay)
                .TransformBounds(new Rect(0, 0, drawer.ActualWidth, drawer.ActualHeight)).Right;
            Require(proxy.Visibility == Visibility.Visible && Canvas.GetLeft(proxy) >= drawerRight - .5,
                $"抽屜開啟時固定水平捲軸必須從抽屜右緣開始；proxyLeft={Canvas.GetLeft(proxy):0.0}, drawerRight={drawerRight:0.0}。");
            WpfTestWait.Invoke(window, "SetQuickBuilderDrawerOpen", false);
            PumpLayout(window);
            WpfTestWait.Invoke(window, "UpdateShellRouteScrollbar");
            Require(proxy.Visibility == Visibility.Visible && Canvas.GetLeft(proxy) < drawerRight - 100,
                $"抽屜關閉後捲軸應回到配線圖左緣；proxyLeft={Canvas.GetLeft(proxy):0.0}。");
        }
        finally
        {
            WpfTestWait.Close(window);
            SetInterfaceScale(previousScale);
        }
        Console.WriteLine("[通過] 抽屜開啟時固定水平捲軸避開抽屜");
    }

    private static void VerifyKpiStripTruncates()
    {
        var previousScale = SetInterfaceScale(1);
        var window = new MainWindow { Width = 800, Height = 700 };
        try
        {
            window.Show();
            PumpLayout(window);
            var names = new[] { "RouteSummaryText", "OneWaySummaryText", "CycleSummaryText", "HeadwaySummaryText", "SpeedSummaryText" };
            foreach (var name in names)
                ((TextBlock)window.FindName(name)!).Text = "36 個節點 · 65 個軌道區段（超長測試文字）";
            PumpLayout(window);
            var strip = (FrameworkElement)window.FindName("RouteSummaryExpander")!;
            var stripRight = strip.TranslatePoint(new Point(strip.ActualWidth, 0), window).X;
            foreach (var name in names)
            {
                var value = (TextBlock)window.FindName(name)!;
                var right = value.TranslatePoint(new Point(value.ActualWidth, 0), window).X;
                Require(value.IsVisible && right <= stripRight + .5 && value.TextTrimming == TextTrimming.CharacterEllipsis
                        && Equals(value.ToolTip, value.Text),
                    $"{name} 在 800 DIP 應截斷於細條內並有提示框；right={right:0.0}, strip={stripRight:0.0}。");
            }
        }
        finally
        {
            WpfTestWait.Close(window);
            SetInterfaceScale(previousScale);
        }
        Console.WriteLine("[通過] 窄視窗 KPI 細條五項皆截斷於細條內");
    }

    private static void VerifyDrawerFocusReturn()
    {
        var window = new MainWindow { Width = 1280, Height = 800 };
        try
        {
            window.Show();
            window.Activate();
            PumpLayout(window);
            var toggle = (Button)window.FindName("QuickBuilderToggleButton")!;
            var close = (Button)window.FindName("QuickBuilderCloseButton")!;
            var input = (TextBox)window.FindName("RouteIdTextBox")!;
            foreach (var closer in new[] { close, toggle })
            {
                toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                PumpLayout(window);
                Keyboard.Focus(input);
                Require(input.IsKeyboardFocused, "測試前置：焦點應在抽屜內的輸入框。");
                closer.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                PumpLayout(window);
                Require(toggle.IsKeyboardFocused, $"以{(ReferenceEquals(closer, close) ? "關閉鈕" : "起稿鈕")}關閉抽屜後，焦點必須回到「起稿」鈕。");
            }
            var drawer = (FrameworkElement)window.FindName("QuickBuilderSidebar")!;
            var tabs = (UIElement)window.FindName("WorkspaceTabControl")!;
            var body = (Panel)drawer.Parent;
            Require(body.Children.IndexOf(drawer) > body.Children.IndexOf(tabs),
                "抽屜在 XAML 中必須排在導覽區之後，Tab 鍵才會先經過導覽列。");
            Require(Panel.GetZIndex(drawer) > Panel.GetZIndex(tabs), "抽屜仍須顯示在最上層。");
        }
        finally { WpfTestWait.Close(window); }
        Console.WriteLine("[通過] 關閉抽屜後焦點回到起稿鈕，Tab 順序先經過導覽列");
    }

    private static void VerifyQuickBuilderToggleText(string root)
    {
        var window = new MainWindow { Width = 1280, Height = 800 };
        try
        {
            window.Show();
            PumpLayout(window);
            var toggle = (Button)window.FindName("QuickBuilderToggleButton")!;
            Require(AutomationProperties.GetName(toggle) == "快速起稿抽屜開關" && Equals(toggle.ToolTip, "快速建立線性路線（一次性起稿）"),
                "未讀入專案時起稿鈕應描述快速起稿抽屜。");
            LoadSample(window, root, "10-小型-三站完整拓樸基準範例.mrtsim.json");
            Require(AutomationProperties.GetName(toggle) == "開啟專案工作區（快速起稿）" && Equals(toggle.ToolTip, "開啟專案工作區（快速起稿）"),
                "已讀入專案時起稿鈕應描述為開啟專案工作區。");
            WpfTestWait.Invoke(window, "ClearResults");
            Require(AutomationProperties.GetName(toggle) == "快速起稿抽屜開關", "清除專案後起稿鈕應恢復為快速起稿抽屜。");
        }
        finally { WpfTestWait.Close(window); }
        Console.WriteLine("[通過] 起稿鈕文字跟隨是否已讀入專案");
    }
```

- [ ] **Step 2: 執行，確認失敗**

執行共用指令前一行與第三行。Expected: `抽屜開啟時固定水平捲軸必須從抽屜右緣開始；…`

- [ ] **Step 3: 實作**

`MainWindow.ShellScroll.cs` 的 `UpdateShellRouteScrollbar`：在

```csharp
                var intersection = Rect.Intersect(routeBounds, viewportBounds);
```

之後加入：

```csharp
                // 快速起稿抽屜開著時，捲軸從抽屜右緣開始，不蓋住抽屜也不攔截抽屜的點擊。
                if (!intersection.IsEmpty && QuickBuilderSidebar is { IsVisible: true } drawer)
                {
                    var drawerRight = drawer.TransformToAncestor(ShellOverlayRoot)
                        .TransformBounds(new Rect(0, 0, drawer.ActualWidth, drawer.ActualHeight)).Right;
                    if (drawerRight > intersection.Left)
                        intersection = intersection.Right > drawerRight
                            ? new Rect(drawerRight, intersection.Top, intersection.Right - drawerRight, intersection.Height)
                            : Rect.Empty;
                }
```

`MainWindow.WorkspaceNavigation.cs`：

1. 檔頭加入 `using System.Windows.Automation;`。
2. `SetQuickBuilderDrawerOpen` 整個方法替換為：

```csharp
    private void SetQuickBuilderDrawerOpen(bool open)
    {
        // 關閉時若焦點在抽屜內，把焦點還給「起稿」鈕，避免停在已隱藏的元件上。
        var returnFocus = !open && QuickBuilderSidebar.IsKeyboardFocusWithin;
        QuickBuilderSidebar.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        UpdateQuickBuilderWidth();
        UpdateQuickBuilderDrawerTop();
        UpdateShellRouteScrollbar();
        if (returnFocus) QuickBuilderToggleButton.Focus();
    }
```

3. `QuickBuilderSidebar_KeyDown` 移除已由上式處理的 `QuickBuilderToggleButton.Focus();` 一行。
4. 新增方法：

```csharp
    // 已讀入拓樸專案時「起稿」會開啟專案工作區的快速起稿頁；提示與自動化名稱跟著行為走。
    private void UpdateQuickBuilderToggleText()
    {
        if (QuickBuilderToggleButton is null) return;
        var opensWorkspace = _activeTopologyProjectDocument is not null;
        var name = opensWorkspace ? "開啟專案工作區（快速起稿）" : "快速起稿抽屜開關";
        QuickBuilderToggleButton.ToolTip = opensWorkspace ? name : "快速建立線性路線（一次性起稿）";
        AutomationProperties.SetName(QuickBuilderToggleButton, name);
    }
```

`MainWindow.Topology.cs`：在 `        _activeTopologyProjectDocument = document;` 下一行加入 `        UpdateQuickBuilderToggleText();`。

`MainWindow.V2.cs`（`ClearV2Results` 內）：在 `        _activeTopologyProjectDocument = null;` 下一行加入 `        UpdateQuickBuilderToggleText();`。

`MainWindow.xaml`：

1. KPI 細條：把 `RouteSummaryScrollViewer` 整段（開始標籤到 `</ScrollViewer>`）替換為：

```xml
                            <ScrollViewer x:Name="RouteSummaryScrollViewer" MaxHeight="100"
                                          VerticalScrollBarVisibility="Disabled" HorizontalScrollBarVisibility="Disabled">
                                <UniformGrid Rows="1" Columns="5">
                                    <DockPanel>
                                        <TextBlock Text="車站" Style="{StaticResource KpiLabel}" />
                                        <TextBlock x:Name="RouteSummaryText" Text="—" Style="{StaticResource KpiValue}" />
                                    </DockPanel>
                                    <DockPanel>
                                        <TextBlock Text="單程" Style="{StaticResource KpiLabel}" />
                                        <TextBlock x:Name="OneWaySummaryText" Text="—" Style="{StaticResource KpiValue}" />
                                    </DockPanel>
                                    <DockPanel>
                                        <TextBlock Text="全程" Style="{StaticResource KpiLabel}" />
                                        <TextBlock x:Name="CycleSummaryText" Text="—" Style="{StaticResource KpiValue}" />
                                    </DockPanel>
                                    <DockPanel>
                                        <TextBlock Text="班距" Style="{StaticResource KpiLabel}" />
                                        <TextBlock x:Name="HeadwaySummaryText" Text="—" Style="{StaticResource KpiValue}" />
                                    </DockPanel>
                                    <DockPanel>
                                        <TextBlock Text="峰值" Style="{StaticResource KpiLabel}" />
                                        <TextBlock x:Name="SpeedSummaryText" Text="—" Style="{StaticResource KpiValue}" />
                                    </DockPanel>
                                </UniformGrid>
                            </ScrollViewer>
```

2. 抽屜移到導覽區之後（元素內容不變），以腳本搬移：

```powershell
@'
p = 'src/MrtRouteSimulator.App/MainWindow.xaml'
t = open(p, encoding='utf-8').read()
start = t.index('            <Border x:Name="QuickBuilderSidebar"')
end = t.index('            <app:ShellTabControl x:Name="WorkspaceTabControl"')
drawer = t[start:end].rstrip('\n') + '\n'
t = t[:start] + t[end:]
anchor = '                </app:ShellTabControl>\n'
i = t.index(anchor) + len(anchor)
t = t[:i] + '\n' + drawer + t[i:]
open(p, 'w', encoding='utf-8', newline='\n').write(t)
'@ | python -
git diff --stat src/MrtRouteSimulator.App/MainWindow.xaml
```

Expected: 只有搬移（新增與刪除行數相同），抽屜位於 `</app:ShellTabControl>` 與其後的 `</Grid>` 之間。

- [ ] **Step 4: 執行，確認通過**

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --shell-layout-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --outer-shell-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --native-final-only
```

Expected: 四個新 `[通過]` 與 `PASS WPF shell layout`；`--outer-shell-only`、`--native-final-only`（含 `CompactRouteLayoutTests` 的摘要收合）通過。

- [ ] **Step 5: Commit**

```powershell
git add src/MrtRouteSimulator.App/MainWindow.xaml src/MrtRouteSimulator.App/MainWindow.ShellScroll.cs src/MrtRouteSimulator.App/MainWindow.WorkspaceNavigation.cs src/MrtRouteSimulator.App/MainWindow.Topology.cs src/MrtRouteSimulator.App/MainWindow.V2.cs tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs
git commit -m "fix: keep route scrollbar, KPI strip, drawer focus and quick builder label consistent" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: 完整驗證、截圖與文件

**Files:**
- Modify: `README.md`、`docs/superpowers/specs/2026-10-10-main-window-shell-design.md`、`docs/superpowers/specs/2026-10-10-result-pages-design.md`、`CHANGELOG.md`、`QA_REPORT.md`

- [ ] **Step 1: 完整驗證（互動桌面、不操作視窗）**

```powershell
query user
dotnet build .\MrtRouteSimulator.slnx -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.Tests\MrtRouteSimulator.Tests.csproj -c Release --no-build --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- .
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- .\samples\14-大型-二十八站完整營運範例.mrtsim.json --large-playback-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --shell-screenshots
```

Expected: session `Active`；整個 solution 0 warning／0 error；Engine 通過數與 Task 0 相同；完整 WPF runner `PASS WPF visual rules` 且含 `PASS WPF result pages`、`PASS WPF shell layout`；大型播放診斷 PASS；`artifacts\shell-screenshots` 內 15 張 PNG。

- [ ] **Step 2: 人工外觀檢查**

用 Read 工具開啟 `artifacts\shell-screenshots\` 每張 PNG，並與 `artifacts\c2-before\` 同名圖對照，逐項確認：7 頁頁首（標題、灰字說明、右側動作）、摘要列圖示與文字、篩選卡（標籤與控制項對齊、閉塞頁右側群組有分隔線）、表格卡圓角與表頭、數字欄靠右對齊、狀態色點顏色、運行圖三列控制面板、速度曲線工具列、KPI 細條五格；沒有文字被硬切或元件重疊。發現問題只改樣式或版面值，修正後重跑 Step 1 對應的測試。把新舊截圖傳給使用者看。

- [ ] **Step 3: 文件**

1. `README.md`：
   - 把「5. 在「模擬動畫」播放、暫停或重設；其他分頁可查看時刻表、區間物理、移動閉塞與列車運行圖。」改為「5. 以標題列右側的播放控制播放、暫停或重設（任何分頁都可操作）；左側導覽列可切換到時刻表、區間物理、移動閉塞與列車運行圖等結果頁。」
   - 把「主選單提供「快速起稿」、「路網編輯」、「營運設定」、「模擬設定」與「分析結果」入口；路網、營運及模擬設定會直接開啟專案工作區的對應頁。」改為「標題列「編輯」選單提供「快速起稿」、「軌道與設施」、「服務與路徑」與「模擬設定」；後三者直接開啟專案工作區的對應頁。結果頁由左側導覽列切換。」
   - 把「建立或讀取 topology 專案後，快速起稿側欄會收合，讓模擬與結果使用完整寬度；後續由專案工作區編輯，取消時不套用草稿。」改為「快速起稿是按需開啟的抽屜（導覽列底部「起稿」鈕或「編輯 → 快速起稿」）；建立或讀取 topology 專案後抽屜會關閉，「起稿」改為開啟專案工作區的快速起稿頁，取消時不套用草稿。」
   - 把「播放倍率位於模擬動畫工具列，收合側欄後仍可調整；輸入或讀檔錯誤顯示在主內容區。」改為「播放控制（建立、播放、暫停、重設、障礙物急停、倍率、時鐘）固定在標題列；輸入或讀檔錯誤顯示在頁首的警告橫幅。」
2. C1 規格 `2026-10-10-main-window-shell-design.md` §4.2：鍵名樣式清單刪除「`SummaryCard`、」。
3. C2 規格 `2026-10-10-result-pages-design.md`：依本計畫「規格的實作細化」第 1～7 點更新第 3、4 節（狀態字串、按鈕文字、表格卡、狀態欄寬、新增名稱、標題）與第 5 節修正 1（`StopPlaybackAfterUiFailure`），並把「狀態」改為「已實作（2026-10-10）」。
4. `CHANGELOG.md` 的 `## V4.1.0` 下、C1 那一節之前新增：

```markdown
### 2026-10-10 介面翻新子專案 C2：結果分頁版面統一

- 7 個結果分頁改用共用的 `ResultPage` 元件：頁首（標題、說明、動作）、摘要、篩選卡（篩選與相關動作分組）、內容卡與附註位置一致；閉塞、統計、運行圖的工具列依「篩選／檢視／匯出」分組。
- 結果表數字與時間欄靠右並用等寬字型，長文字截斷並以提示框顯示完整內容，狀態欄加色點（異常紅、注意黃、完成綠、進行中藍、其他灰）。
- 修正 C1 遺留 9 項：狀態圓點（V1 播放、畫面更新失敗、清除結果）、固定水平捲軸避開抽屜、窄視窗 KPI 細條截斷、停用圖示鈕提示、抽屜焦點與 Tab 順序、起稿鈕文字隨專案狀態、選單白字只限標題列、下拉選單焦點框、README 與規格同步。
- `MainWindow.xaml` 不再含寫死色碼；設計見 `docs/superpowers/specs/2026-10-10-result-pages-design.md`。
```

5. `QA_REPORT.md` 第一個 `## ` 之前新增一節，數字一律填 Step 1 的實際輸出：

```markdown
## 介面翻新子專案 C2：結果分頁版面統一（2026-10-10）

- Release build：<實際 warning／error 數>。
- Engine runner：<通過數>/<總數>。
- WPF runner（互動桌面）：<PASS WPF visual rules／失敗訊息>，<[通過] 行數> 項 `[通過]`；新增 `ResultPageTests`（狀態色點對照與來源字串守門、結果表欄位規則、`ResultPage` 骨架、7 頁與模擬子分頁結構、窄視窗與 125% 頁首不重疊、無寫死色碼）與 C1 遺留修正 8 項回歸測試全部通過。
- 大型 28 站播放診斷：<PASS／失敗訊息>。
- 人工截圖檢查（範例 14 推進 5 分鐘，7 頁與 2 個子分頁，1280×800，與改版前對照）：<結果>。
- 未涵蓋：圖表 Canvas 內的繪圖（子專案 D）、拓樸編輯器版面（子專案 E）；原生桌面 DPI 與實體滑鼠操作未重跑。
```

- [ ] **Step 4: Commit**

```powershell
git add README.md docs/superpowers/specs/2026-10-10-main-window-shell-design.md docs/superpowers/specs/2026-10-10-result-pages-design.md CHANGELOG.md QA_REPORT.md
git commit -m "docs: record result pages verification and refresh user docs" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
