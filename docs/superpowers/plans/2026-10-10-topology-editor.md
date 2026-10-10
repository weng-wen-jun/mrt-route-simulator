# 拓樸編輯器外觀統一（子專案 E）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 專案工作區（拓樸編輯器）與它開出的對話框改用主視窗的色票與元件樣式（按鈕、分段標籤、清單、卡片、搜尋框、驗證色點），路線示意圖改用配線圖的軌道配色；功能與資料不變。

**Architecture:** 新增 `EditorChrome`（共用元件與 `ApplyWindowChrome`），編輯器專用樣式以具名樣式放在 `Themes/Controls.xaml`；`ApplyWindowChrome` 把分段標籤、清單、群組框、進度條登記為「該視窗資源內的隱含樣式」，因此編輯器內所有 `TabControl`／`ListBox`／`GroupBox`／`ProgressBar` 自動套用，主視窗完全不受影響。按鈕集中由 `CreateButton` → `EditorChrome.Button` 套主要／次要樣式；其餘逐處替換寫死色值。路線示意圖改用 `TrackRailStyle`。

**Tech Stack:** .NET 10、WPF（C# 14、XAML）、自製 WPF 測試 runner（`tests/MrtRouteSimulator.WpfTests`）。

**Spec:** `docs/superpowers/specs/2026-10-10-topology-editor-design.md`

## Global Constraints

- 不修改 `src/MrtRouteSimulator.Engine/`、專案 Schema、版本號；編輯器資料流、驗證、「套用／取消」語意、`ProjectWorkspacePage` 列舉值不變。
- 既有元件 `Name`、`AutomationProperties.Name`、按鈕文字、分頁標題、導覽文字保留；可新增名稱，不得改名或移除。右側面板切換鈕文字維持「收合／展開」（`ShellInputTests` 依此尋找）。
- `Ask`／`Choose` 對話框結構不得改變：`Ask` 為 `ScrollViewer` → `StackPanel`（文字框為直接子元素、最後一個子元素是按鈕列 `StackPanel`，第一顆按鈕為確認）；`Choose` 為 `StackPanel`（直接含 `ComboBox`、最後是按鈕列）。`TopologyWorkspaceRoundTripTests` 依此操作。
- 顏色只能取自 `UiTheme`；`TopologyEditorWindow*.cs`（4 檔）、`LegacyPortMigrationDialog.cs`、`ProjectLoadProgressWindow.cs`、`EditorChrome.cs` 完成後不得出現 `Color.FromRgb`、`Color.FromArgb`、`Brushes.<色名>`、`Colors.<色名>`（`Transparent` 除外）。
- 主視窗不受影響：新增樣式一律具名；隱含樣式只登記在編輯器與其對話框的 `Window.Resources`。
- 主要（橘色）按鈕只限：底部「套用」、對話框確認「建立」「選取」「套用明確側別」、「建立格式版本 8 拓撲」「以此站型重新起稿」；其餘一律次要。
- App 專案 `TreatWarningsAsErrors=true`。
- 像素與視窗版面類 WPF 測試只能在互動桌面 session 執行：先 `query user` 確認 `Active`、工作區高度 ≥ 700；完整 runner 執行期間不操作測試視窗。
- 既有測試若因新版面失敗，只調整與新版面對應的數值並記錄 `Ruling`，不得刪除或放寬功能性檢查。
- Commit 遵循 Conventional Commits，結尾 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`；不 push；只 `git add` 列出的檔案。

## 規格的實作細化（Task 6 回寫規格）

1. 分段標籤、清單、群組框、進度條以「視窗範圍的隱含樣式」套用：`EditorChrome.ApplyWindowChrome` 在編輯器與其對話框的 `Resources` 登記 `EditorTabControl`、`EditorList`、`EditorGroupBox`、`ThinProgressBar`。因此不需要規格表列的 `EditorChrome.Tabs`／`Tab`／`List`；`EditorTabItem` 直接顯示 `Header`，不依賴 `ShellNav.ShortLabel`（規格第 9 節的風險因此消失）。
2. 右側面板切換鈕保留文字「收合／展開」（既有測試與「按鈕文字保留」規則），改套 `GhostButton`；不新增 `IconButton` 樣式。
3. 頁面說明用可換行的 12 px 灰字（`EditorChrome.Hint`），不用會截成單行的 `ResultPageDescription`；選取車站標題 20→15 px；頁內小標題 13 px 半粗。
4. `EditorChrome` 另加 `StyleOf`、`FieldLabel`、`ValidationSeverityBrush`、`ValidationSummaryBrush`；新增 `ValidationSeverityBrushConverter` 與 `ValidationMessageTemplate`（驗證清單色點）。
5. 「新增設施…」「查看設施」原以 `!readOnly` 決定主次（唯讀時變主要），一律改次要。
6. 表格內搜尋框寬 150→180，容納提示字「搜尋名稱、ID 或關聯」。
7. 遷移對話框的對照表原本沒有列定義，所有資料列疊在第一列；本輪補上每列列定義與表頭底色（既有錯誤修正）。
8. 頁首與底部列新增名稱 `EditorHeader`、`EditorFooter`，供測試定位。
9. 班表頁接續參照的提醒文字改 `WarningText`；示意圖連接線線寬沿用配線圖規則（依起點軌道種類 6／4）。

## Review Focus

1. **最小視窗 980×640 與 125% 介面縮放**：底部「驗證／套用／取消」、頁首搜尋與收合鈕不得被裁切。由 Task 2 測試釘住。
2. **很長的驗證訊息**：右側驗證清單要換行顯示，色點對齊第一行而不是置中。由 Task 2 測試釘住。
3. **鍵盤操作**：導覽、清單項目與分段標籤取得焦點時要有焦點框。由 Task 1 測試釘住。
4. **停用的按鈕**（例如沒有選取項目時）：要以半透明顯示，不能和可按的按鈕一樣。由 Task 1 測試釘住。
5. **多選參照清單**（月台允許車型／服務）：每一個已選項目都要顯示選取底色，而不只最後點的那一個。由 Task 1 測試釘住。

---

## File Structure

| 檔案 | 動作 | 責任 |
|---|---|---|
| `src/MrtRouteSimulator.App/EditorChrome.cs` | 新增 | 編輯器共用元件、`ApplyWindowChrome`、驗證色點對照與轉換器 |
| `src/MrtRouteSimulator.App/Themes/Controls.xaml` | 修改 | 編輯器具名樣式與驗證訊息範本 |
| `src/MrtRouteSimulator.App/TopologyEditorWindow.cs` | 修改 | 外殼、共用建立方法、總覽／快速起稿／驗證／舊頁、示意圖、`Ask`／`Choose` |
| `src/MrtRouteSimulator.App/TopologyEditorWindow.Stations.cs` | 修改 | 車站、軌道、進階資料頁 |
| `src/MrtRouteSimulator.App/TopologyEditorWindow.Operations.cs` | 修改 | 車型、服務、停站模式、班表頁 |
| `src/MrtRouteSimulator.App/TopologyEditorWindow.Settings.cs` | 修改 | 模擬設定頁 |
| `src/MrtRouteSimulator.App/LegacyPortMigrationDialog.cs` | 修改 | 遷移對話框 |
| `src/MrtRouteSimulator.App/ProjectLoadProgressWindow.cs` | 修改 | 讀檔進度視窗 |
| `tests/MrtRouteSimulator.WpfTests/EditorThemeTests.cs` | 新增 | E 的全部測試 |
| `tests/MrtRouteSimulator.WpfTests/Program.cs` | 修改 | `--editor-theme-only` 與完整 runner |
| `tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs` | 修改 | 截圖加拍編輯器各頁與對話框 |
| `README.md`、E 規格、`CHANGELOG.md`、`QA_REPORT.md` | 修改 | Task 6 |

**共用指令**（worktree 根目錄，PowerShell）：

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --editor-theme-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --workspace-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- .
```

**編輯腳本慣例**：各步驟的 Python 腳本以 Bash 在 worktree 根目錄執行（`python - <<'EOF' … EOF`），每個替換都先確認舊字串出現次數，不符就中止。腳本開頭的共用函式：

```python
from pathlib import Path
def rep(path, old, new, count=1):
    p = Path(path); s = p.read_text(encoding='utf-8')
    n = s.count(old)
    assert n == count, f'{path}: expected {count}, found {n}: {old[:70]!r}'
    p.write_text(s.replace(old, new), encoding='utf-8', newline='')
```

---

### Task 0: 基準線、`EditorThemeTests` 骨架與截圖擴充

**Files:**
- Create: `tests/MrtRouteSimulator.WpfTests/EditorThemeTests.cs`
- Modify: `tests/MrtRouteSimulator.WpfTests/Program.cs`
- Modify: `tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs`（`CaptureScreenshots`）

**Interfaces:**
- Produces: `EditorThemeTests.Run(string root)`、`Require(bool, string)`、`OpenEditor(string root, string page, double width = 1280, double height = 800)`、`Navigate(Window, string page)`、`Field(Window, string name)`、`Descendants(DependencyObject)`、`Sample(string root)`、`AppType(string name)`、`EditorType`、`PageType`；runner 旗標 `--editor-theme-only`；截圖多出 `editor-Project`、`editor-Stations`、`editor-DispatchPlanning`、`editor-Schematic`、`dialog-migration`、`dialog-progress`（共 25 張）。

- [ ] **Step 1: 確認分支包含最新 main**

```powershell
git merge-base --is-ancestor main HEAD; "main is ancestor: $LASTEXITCODE"
```

Expected: `main is ancestor: 0`（本分支 = main `31ee644` + 規格與計畫 commit）。

- [ ] **Step 2: 建置、Engine 與完整 WPF 基準**

```powershell
query user
Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
dotnet build .\MrtRouteSimulator.slnx -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.Tests\MrtRouteSimulator.Tests.csproj -c Release --no-build --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- .
```

Expected: session `Active`；工作區高度 ≥ 700；0 warning／0 error；Engine 198/198；WPF 最後一行 `PASS WPF visual rules`（D 結束時 138 項 `[通過]`）。把數字記入進度紀錄。

- [ ] **Step 3: 建立測試骨架、旗標與截圖擴充**

建立 `tests/MrtRouteSimulator.WpfTests/EditorThemeTests.cs`：

```csharp
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

/// <summary>拓樸編輯器外觀（子專案 E）：共用樣式、外殼、各頁、示意圖、對話框與寫死顏色掃描。</summary>
internal static class EditorThemeTests
{
    internal static readonly Type EditorType = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.TopologyEditorWindow")!;
    internal static readonly Type PageType = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.ProjectWorkspacePage")!;

    public static void Run(string root)
    {
        Console.WriteLine("PASS WPF editor theme");
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static Type AppType(string name) => typeof(MainWindow).Assembly.GetType($"MrtRouteSimulator.App.{name}")!;

    internal static TopologyProjectDocument Sample(string root) => TopologyProjectFormat.Deserialize(
        File.ReadAllText(System.IO.Path.Combine(root, "samples", "14-大型-二十八站完整營運範例.mrtsim.json")));

    /// <summary>在螢幕外開啟編輯器（範例 14），測試結束由呼叫端關閉。</summary>
    internal static Window OpenEditor(string root, string page, double width = 1280, double height = 800)
    {
        var window = (Window)Activator.CreateInstance(EditorType, Sample(root), Enum.Parse(PageType, page))!;
        window.Width = width;
        window.Height = height;
        window.Left = -10000;
        window.Top = -10000;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.ShowInTaskbar = false;
        window.Show();
        window.UpdateLayout();
        return window;
    }

    internal static void Navigate(Window window, string page)
    {
        EditorType.GetMethod("Navigate", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [Enum.Parse(PageType, page)]);
        window.UpdateLayout();
    }

    internal static object? Field(Window window, string name) =>
        EditorType.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window);

    internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}
```

`Program.cs`：在 `if (args.Contains("--chart-theme-only"))` 區塊之前加入：

```csharp
            if (args.Contains("--editor-theme-only"))
            {
                EditorThemeTests.Run(GetRoot(args));
                return 0;
            }
```

並在完整 runner 的 `ChartThemeTests.Run(GetRoot(args));`（第二次出現、位於 `ResultPageTests.Run` 之後那行）下一行加入：

```csharp
            EditorThemeTests.Run(GetRoot(args));
```

`ShellLayoutTests.cs` 的 `CaptureScreenshots`：把

```csharp
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
```

換成

```csharp
            foreach (var page in new[] { "Project", "Stations", "Tracks", "Services", "DispatchPlanning", "Schematic" })
            {
                // 路線示意圖不是導覽頁，先開軌道頁再導覽過去。
                var editor = (Window)Activator.CreateInstance(editorType, document, Enum.Parse(pageType, page == "Schematic" ? "Tracks" : page))!;
                editor.Width = 1280;
                editor.Height = 800;
                try
                {
                    editor.Show();
                    if (page == "Schematic")
                        editorType.GetMethod("Navigate", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, [Enum.Parse(pageType, "Schematic")]);
                    Save(editor, System.IO.Path.Combine(output, $"editor-{page}-1280x800.png"));
                }
                finally { editor.Close(); }
            }
            SaveEditorDialogs(output);
```

在 `SaveDiagramExports` 方法之後加入：

```csharp
    // 遷移對話框與讀檔進度視窗的外觀截圖；遷移對話框以 3 段缺接軌側別的軌道建立。
    private static void SaveEditorDialogs(string output)
    {
        var source = StationLayoutTemplateService.Build(StationLayoutTemplateKind.IslandTwoTracks);
        var legacy = source with
        {
            Topology = source.Topology with
            {
                Edges = source.Topology.Edges.Select((edge, index) => index < 3 ? edge with { FromPortSide = null, ToPortSide = null } : edge).ToArray()
            }
        };
        var assembly = typeof(MainWindow).Assembly;
        var windows = new (Window Window, string File)[]
        {
            ((Window)Activator.CreateInstance(assembly.GetType("MrtRouteSimulator.App.LegacyPortMigrationDialog")!, legacy)!, "dialog-migration"),
            ((Window)Activator.CreateInstance(assembly.GetType("MrtRouteSimulator.App.ProjectLoadProgressWindow")!)!, "dialog-progress")
        };
        foreach (var (window, file) in windows)
        {
            try
            {
                window.Show();
                Save(window, System.IO.Path.Combine(output, $"{file}.png"));
            }
            finally { window.Close(); }
        }
    }
```

- [ ] **Step 4: 建置並執行骨架與改版前截圖**

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --editor-theme-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --shell-screenshots
New-Item -ItemType Directory .\artifacts\e-before -Force | Out-Null
Copy-Item .\artifacts\shell-screenshots\*.png .\artifacts\e-before -Force
(Get-ChildItem .\artifacts\e-before\*.png).Count
```

Expected: 0 warning／0 error；`PASS WPF editor theme`；`shellScreenshots=...`；`e-before` 內 25 張 PNG。用 Read 工具看 `editor-Project`、`editor-Schematic`、`dialog-migration`、`dialog-progress`，確認畫面有內容（遷移對話框目前資料列會疊在一起，這是 Task 5 要修的既有錯誤）。

- [ ] **Step 5: Commit**

```powershell
git add tests/MrtRouteSimulator.WpfTests/EditorThemeTests.cs tests/MrtRouteSimulator.WpfTests/Program.cs tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs
git commit -m "test: add editor theme test skeleton and editor screenshots" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 1: 編輯器樣式與 `EditorChrome`

**Files:**
- Create: `src/MrtRouteSimulator.App/EditorChrome.cs`
- Modify: `src/MrtRouteSimulator.App/Themes/Controls.xaml`（檔尾 `</ResourceDictionary>` 之前）
- Test: `tests/MrtRouteSimulator.WpfTests/EditorThemeTests.cs`

**Interfaces:**
- Produces（`namespace MrtRouteSimulator.App`）：
  - `public static class EditorChrome`：`const string ValidationMessageTemplateKey = "ValidationMessageTemplate"`；`Style StyleOf(string key)`；`void ApplyWindowChrome(Window)`；`StackPanel PageHeader(string title, string description)`；`TextBlock SectionTitle(string)`；`TextBlock Hint(string)`；`TextBlock FieldLabel(string)`；`Border Card(UIElement? child = null, string? title = null)`；`Button Button(string label, RoutedEventHandler handler, bool primary)`；`TextBox SearchBox(string placeholder, TextBox? box = null)`；`Ellipse StatusDot(Brush fill)`；`SolidColorBrush ValidationSeverityBrush(ProjectValidationSeverity)`；`SolidColorBrush ValidationSummaryBrush(bool validated, int errors, int warnings)`。
  - `public sealed class ValidationSeverityBrushConverter : IValueConverter`。
  - `Controls.xaml` 具名資源：`EditorTabItem`、`EditorTabControl`、`EditorListItem`、`EditorList`、`EditorNavItem`、`EditorNavList`、`SearchBox`、`EditorGroupBox`、`ThinProgressBar`、`ValidationSeverityBrush`（轉換器）、`ValidationMessageTemplate`。
  - 測試端：`EditorThemeTests.ScannedFiles`（掃描清單，後續 Task 逐步加入）、`VerifyNoHardCodedEditorColors(string root)`。

- [ ] **Step 1: 寫失敗測試**

`Run` 改為：

```csharp
    public static void Run(string root)
    {
        VerifyEditorStyles();
        VerifyNoHardCodedEditorColors(root);
        Console.WriteLine("PASS WPF editor theme");
    }
```

加入：

```csharp
    private static readonly string[] EditorStyleKeys =
        ["EditorTabItem", "EditorTabControl", "EditorListItem", "EditorList", "EditorNavItem", "EditorNavList", "SearchBox", "EditorGroupBox", "ThinProgressBar"];

    private static void VerifyEditorStyles()
    {
        foreach (var key in EditorStyleKeys)
            Require(Application.Current.TryFindResource(key) is Style, $"Themes/Controls.xaml 缺少樣式 {key}。");
        Require(Application.Current.TryFindResource(EditorChrome.ValidationMessageTemplateKey) is DataTemplate, "缺少驗證訊息範本。");
        Require(Application.Current.Resources[typeof(TabControl)] is null && Application.Current.Resources[typeof(ListBox)] is null,
            "編輯器樣式不得登記為 App 層級的隱含樣式（主視窗不受影響）。");

        var window = new Window
        {
            Width = 640, Height = 520, Left = -10000, Top = -10000, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual
        };
        EditorChrome.ApplyWindowChrome(window);
        Require(ReferenceEquals(window.FontFamily, Application.Current.FindResource("AppFont"))
                && ReferenceEquals(window.Foreground, UiTheme.TextStrongBrush)
                && ReferenceEquals(window.Background, UiTheme.WindowBackgroundBrush),
            "ApplyWindowChrome 必須套用 App 字型、深色前景與視窗底色。");
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "服務類型", Content = new TextBlock { Text = "A" } });
        tabs.Items.Add(new TabItem { Header = "服務路徑", Content = new TextBlock { Text = "B" } });
        var list = new ListBox { ItemsSource = new[] { "甲", "乙", "丙" }, SelectionMode = SelectionMode.Multiple, Height = 110 };
        var group = new GroupBox { Header = "常用設定", Content = new TextBlock { Text = "內容" } };
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Value = 40, Width = 200 };
        var disabled = EditorChrome.Button("停用", (_, _) => { }, primary: false);
        disabled.IsEnabled = false;
        var primary = EditorChrome.Button("套用", (_, _) => { }, primary: true);
        var search = EditorChrome.SearchBox("搜尋車站");
        var card = EditorChrome.Card(new TextBlock { Text = "卡片內容" }, "月台配置");
        var header = EditorChrome.PageHeader("車站與月台", "從車站開始編輯本站基本資料。");
        window.Content = new StackPanel { Children = { header, tabs, list, group, progress, disabled, primary, search, card } };
        try
        {
            window.Show();
            window.UpdateLayout();
            var tabItems = tabs.Items.OfType<TabItem>().ToArray();
            Require(ReferenceEquals(tabs.Style, EditorChrome.StyleOf("EditorTabControl"))
                    && tabItems.All(item => ReferenceEquals(item.Style, EditorChrome.StyleOf("EditorTabItem"))),
                "編輯器視窗內的分頁必須自動套用膠囊式分段樣式。");
            Require(tabItems.All(item => Descendants(item).OfType<TextBlock>().Any(text => text.Text == (string)item.Header)),
                "分段標籤必須顯示分頁標題文字。");
            Require(ReferenceEquals(list.Style, EditorChrome.StyleOf("EditorList"))
                    && ReferenceEquals(group.Style, EditorChrome.StyleOf("EditorGroupBox"))
                    && ReferenceEquals(progress.Style, EditorChrome.StyleOf("ThinProgressBar")),
                "清單、群組框、進度條必須自動套用編輯器樣式。");

            list.SelectedItems.Add("甲");
            list.SelectedItems.Add("丙");
            window.UpdateLayout();
            var containers = Enumerable.Range(0, 3).Select(index => (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(index)).ToArray();
            Border Chrome(ListBoxItem item) => (Border)item.Template.FindName("Chrome", item);
            Require(ReferenceEquals(Chrome(containers[0]).Background, UiTheme.NavSelectedBrush)
                    && ReferenceEquals(Chrome(containers[2]).Background, UiTheme.NavSelectedBrush)
                    && !ReferenceEquals(Chrome(containers[1]).Background, UiTheme.NavSelectedBrush)
                    && ReferenceEquals(containers[0].Foreground, UiTheme.AccentBrush),
                "多選清單的每個選取項目都必須顯示選取底色與藍字。");
            var focusRing = Application.Current.FindResource("FocusRingVisual");
            Require(ReferenceEquals(containers[0].FocusVisualStyle, focusRing) && ReferenceEquals(tabItems[0].FocusVisualStyle, focusRing),
                "清單項目與分段標籤必須有鍵盤焦點框。");
            Require(disabled.Opacity < 0.5 && ReferenceEquals(disabled.Style, EditorChrome.StyleOf("SecondaryButton")),
                "停用的次要按鈕必須以半透明顯示。");
            Require(ReferenceEquals(primary.Style, EditorChrome.StyleOf("PrimaryButton"))
                    && primary.ReadLocalValue(Control.BackgroundProperty) == DependencyProperty.UnsetValue,
                "主要按鈕必須套用 PrimaryButton 且不自行設定顏色。");

            var placeholder = (TextBlock)search.Template.FindName("Placeholder", search);
            Require(ReferenceEquals(search.Style, EditorChrome.StyleOf("SearchBox")) && placeholder.Text == "搜尋車站"
                    && placeholder.Visibility == Visibility.Visible, "空白搜尋框必須顯示提示字。");
            search.Text = "O20";
            window.UpdateLayout();
            Require(placeholder.Visibility == Visibility.Collapsed, "輸入文字後提示字必須隱藏。");
            search.Text = "";
            window.UpdateLayout();
            Require(placeholder.Visibility == Visibility.Visible, "清空後提示字必須重新出現。");

            Require(ReferenceEquals(card.Style, EditorChrome.StyleOf("ResultCard")) && card.Padding == new Thickness(10)
                    && Descendants(card).OfType<TextBlock>().Any(text => text.Text == "月台配置" && text.FontSize == 13
                        && text.FontWeight == FontWeights.SemiBold && ReferenceEquals(text.Foreground, UiTheme.TextStrongBrush)),
                "卡片必須是白底圓角卡片（內距 10），標題 13 px 半粗深色。");
            var headerTexts = header.Children.OfType<TextBlock>().ToArray();
            Require(headerTexts.Length == 2 && ReferenceEquals(headerTexts[0].Style, EditorChrome.StyleOf("ResultPageTitle"))
                    && ReferenceEquals(headerTexts[1].Foreground, UiTheme.TextMutedBrush) && headerTexts[1].TextWrapping == TextWrapping.Wrap,
                "頁首必須是頁面標題樣式加可換行的灰色說明。");
        }
        finally { window.Close(); }

        var converter = new ValidationSeverityBrushConverter();
        foreach (var (severity, brush) in new (ProjectValidationSeverity, SolidColorBrush)[]
                 {
                     (ProjectValidationSeverity.Error, UiTheme.DangerBrush), (ProjectValidationSeverity.Warning, UiTheme.CautionBrush),
                     (ProjectValidationSeverity.Info, UiTheme.TextSubtleBrush)
                 })
            Require(ReferenceEquals(EditorChrome.ValidationSeverityBrush(severity), brush)
                    && ReferenceEquals(converter.Convert(severity, typeof(Brush), null!, System.Globalization.CultureInfo.InvariantCulture), brush),
                $"驗證嚴重度 {severity} 的色點顏色不符。");
        Require(ReferenceEquals(EditorChrome.ValidationSummaryBrush(false, 0, 0), UiTheme.TextSubtleBrush)
                && ReferenceEquals(EditorChrome.ValidationSummaryBrush(true, 2, 1), UiTheme.DangerBrush)
                && ReferenceEquals(EditorChrome.ValidationSummaryBrush(true, 0, 1), UiTheme.CautionBrush)
                && ReferenceEquals(EditorChrome.ValidationSummaryBrush(true, 0, 0), UiTheme.SuccessBrush),
            "驗證摘要色點：未驗證灰、有錯誤紅、只有提醒黃、通過綠。");
        Console.WriteLine("[通過] 編輯器共用樣式與 EditorChrome 元件（含多選、焦點框、停用按鈕）");
    }

    // 掃描清單：整個檔案不得寫死顏色；後續 Task 逐步加入。
    private static readonly string[] ScannedFiles = ["EditorChrome.cs"];

    private static readonly Regex HardCodedColor = new(
        @"\bColor\.From(Rgb|Argb)\b|\bBrushes\.(?!Transparent\b)[A-Z]\w*|\bColors\.(?!Transparent\b)[A-Z]\w*",
        RegexOptions.Compiled);

    private static void VerifyNoHardCodedEditorColors(string root)
    {
        var findings = ScannedFiles.SelectMany(file => HardCodedColor
                .Matches(File.ReadAllText(System.IO.Path.Combine(root, "src", "MrtRouteSimulator.App", file)))
                .Select(match => $"{file}：{match.Value}"))
            .Distinct().ToArray();
        Require(findings.Length == 0, "編輯器程式不得寫死顏色：" + string.Join("、", findings));
        Console.WriteLine($"[通過] 編輯器程式無寫死顏色（{ScannedFiles.Length} 個檔案）");
    }
```

- [ ] **Step 2: 執行，確認失敗**

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
```

Expected: 建置失敗，`EditorChrome`、`ValidationSeverityBrushConverter` 不存在。

- [ ] **Step 3: 實作樣式**

`Themes/Controls.xaml`：在檔尾 `</ResourceDictionary>` 之前加入：

```xml
    <!-- 拓樸編輯器（子專案 E）：具名樣式；EditorChrome.ApplyWindowChrome 只在編輯器與其對話框的視窗資源中
         把 TabControl／ListBox／GroupBox／ProgressBar 登記為隱含樣式，主視窗不受影響。 -->
    <Style x:Key="EditorTabItem" TargetType="TabItem">
        <Setter Property="FocusVisualStyle" Value="{StaticResource FocusRingVisual}" />
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.TextMutedBrush}" />
        <Setter Property="Cursor" Value="Hand" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="TabItem">
                    <Border x:Name="Chrome" CornerRadius="10" Background="Transparent" Padding="14,3" MinHeight="22">
                        <ContentPresenter ContentSource="Header" RecognizesAccessKey="True" VerticalAlignment="Center"
                                          TextElement.FontSize="12" />
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

    <Style x:Key="EditorTabControl" TargetType="TabControl" BasedOn="{StaticResource SegmentedTabControl}">
        <Setter Property="ItemContainerStyle" Value="{StaticResource EditorTabItem}" />
    </Style>

    <Style x:Key="EditorListItem" TargetType="ListBoxItem">
        <Setter Property="FocusVisualStyle" Value="{StaticResource FocusRingVisual}" />
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.TextStrongBrush}" />
        <Setter Property="Padding" Value="8,4" />
        <Setter Property="MinHeight" Value="28" />
        <Setter Property="HorizontalContentAlignment" Value="Stretch" />
        <Setter Property="VerticalContentAlignment" Value="Center" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ListBoxItem">
                    <Border x:Name="Chrome" Background="Transparent" CornerRadius="4" Padding="{TemplateBinding Padding}"
                            SnapsToDevicePixels="True">
                        <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                          VerticalAlignment="{TemplateBinding VerticalContentAlignment}" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Chrome" Property="Background" Value="{x:Static app:UiTheme.NavHoverBrush}" />
                        </Trigger>
                        <Trigger Property="IsSelected" Value="True">
                            <Setter TargetName="Chrome" Property="Background" Value="{x:Static app:UiTheme.NavSelectedBrush}" />
                            <Setter Property="Foreground" Value="{x:Static app:UiTheme.AccentBrush}" />
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter Property="Opacity" Value="0.55" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key="EditorList" TargetType="ListBox">
        <Setter Property="Background" Value="{x:Static app:UiTheme.SurfaceBrush}" />
        <Setter Property="BorderBrush" Value="{x:Static app:UiTheme.BorderBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="Padding" Value="3" />
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.TextStrongBrush}" />
        <Setter Property="ItemContainerStyle" Value="{StaticResource EditorListItem}" />
        <Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ListBox">
                    <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="8" SnapsToDevicePixels="True">
                        <ScrollViewer Padding="{TemplateBinding Padding}" Focusable="False"
                                      HorizontalScrollBarVisibility="{TemplateBinding ScrollViewer.HorizontalScrollBarVisibility}"
                                      VerticalScrollBarVisibility="{TemplateBinding ScrollViewer.VerticalScrollBarVisibility}">
                            <ItemsPresenter />
                        </ScrollViewer>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key="EditorNavItem" TargetType="ListBoxItem" BasedOn="{StaticResource EditorListItem}">
        <Setter Property="MinHeight" Value="32" />
        <Setter Property="Padding" Value="12,4" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ListBoxItem">
                    <Grid SnapsToDevicePixels="True">
                        <Border x:Name="Chrome" Background="Transparent" CornerRadius="6" Padding="{TemplateBinding Padding}">
                            <ContentPresenter VerticalAlignment="{TemplateBinding VerticalContentAlignment}" />
                        </Border>
                        <Border x:Name="Indicator" Width="3" Margin="0,6" CornerRadius="1.5" HorizontalAlignment="Left"
                                Background="{x:Static app:UiTheme.AccentBrush}" Visibility="Collapsed" />
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="Chrome" Property="Background" Value="{x:Static app:UiTheme.NavHoverBrush}" />
                        </Trigger>
                        <Trigger Property="IsSelected" Value="True">
                            <Setter TargetName="Chrome" Property="Background" Value="{x:Static app:UiTheme.NavSelectedBrush}" />
                            <Setter TargetName="Indicator" Property="Visibility" Value="Visible" />
                            <Setter Property="Foreground" Value="{x:Static app:UiTheme.AccentBrush}" />
                            <Setter Property="FontWeight" Value="SemiBold" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key="EditorNavList" TargetType="ListBox" BasedOn="{StaticResource EditorList}">
        <Setter Property="Padding" Value="6" />
        <Setter Property="ItemContainerStyle" Value="{StaticResource EditorNavItem}" />
    </Style>

    <Style x:Key="SearchBox" TargetType="TextBox" BasedOn="{StaticResource {x:Type TextBox}}">
        <Setter Property="Padding" Value="2,5,9,5" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="TextBox">
                    <Border x:Name="Frame" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="6" SnapsToDevicePixels="True">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="Auto" />
                                <ColumnDefinition Width="*" />
                            </Grid.ColumnDefinitions>
                            <TextBlock Text="&#xE721;" FontFamily="{StaticResource IconFont}" FontSize="12" Margin="9,0,2,0"
                                       VerticalAlignment="Center" Foreground="{x:Static app:UiTheme.TextSubtleBrush}" />
                            <ScrollViewer x:Name="PART_ContentHost" Grid.Column="1" Focusable="False" VerticalAlignment="Center"
                                          HorizontalScrollBarVisibility="Hidden" VerticalScrollBarVisibility="Hidden" />
                            <TextBlock x:Name="Placeholder" Grid.Column="1" Margin="4,0,9,0" VerticalAlignment="Center"
                                       IsHitTestVisible="False" Visibility="Collapsed" TextTrimming="CharacterEllipsis"
                                       Foreground="{x:Static app:UiTheme.TextSubtleBrush}"
                                       Text="{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}" />
                        </Grid>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="Text" Value="">
                            <Setter TargetName="Placeholder" Property="Visibility" Value="Visible" />
                        </Trigger>
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

    <Style x:Key="EditorGroupBox" TargetType="GroupBox">
        <Setter Property="Padding" Value="12,4,12,10" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="GroupBox">
                    <Border Background="{x:Static app:UiTheme.SurfaceBrush}" BorderBrush="{x:Static app:UiTheme.BorderBrush}"
                            BorderThickness="1" CornerRadius="8" SnapsToDevicePixels="True">
                        <DockPanel>
                            <ContentPresenter DockPanel.Dock="Top" ContentSource="Header" RecognizesAccessKey="True" Margin="12,10,12,4"
                                              TextElement.FontSize="13" TextElement.FontWeight="SemiBold"
                                              TextElement.Foreground="{x:Static app:UiTheme.TextStrongBrush}" />
                            <ContentPresenter Margin="{TemplateBinding Padding}" />
                        </DockPanel>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key="ThinProgressBar" TargetType="ProgressBar">
        <Setter Property="Height" Value="6" />
        <Setter Property="Foreground" Value="{x:Static app:UiTheme.AccentBrush}" />
        <Setter Property="Background" Value="{x:Static app:UiTheme.BorderBrush}" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ProgressBar">
                    <Grid SnapsToDevicePixels="True">
                        <Border x:Name="PART_Track" Background="{TemplateBinding Background}" CornerRadius="3" />
                        <Border x:Name="PART_Indicator" Background="{TemplateBinding Foreground}" CornerRadius="3"
                                HorizontalAlignment="Left" />
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <app:ValidationSeverityBrushConverter x:Key="ValidationSeverityBrush" />

    <!-- 驗證訊息：色點依嚴重度，對齊第一行；長訊息換行。 -->
    <DataTemplate x:Key="ValidationMessageTemplate">
        <DockPanel>
            <Ellipse DockPanel.Dock="Left" Width="8" Height="8" Margin="0,5,7,0" VerticalAlignment="Top"
                     Fill="{Binding Source.Severity, Converter={StaticResource ValidationSeverityBrush}}" />
            <TextBlock Text="{Binding DisplayMessage}" TextWrapping="Wrap" />
        </DockPanel>
    </DataTemplate>
```

- [ ] **Step 4: 實作 `EditorChrome`**

建立 `src/MrtRouteSimulator.App/EditorChrome.cs`：

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

/// <summary>
/// 拓樸編輯器與其對話框的共用元件。外觀全部來自 Themes/Controls.xaml 的樣式與 UiTheme 畫筆；
/// <see cref="ApplyWindowChrome"/> 只在該視窗的資源中把分段標籤、清單、群組框、進度條登記為隱含樣式，
/// 主視窗不受影響。
/// </summary>
public static class EditorChrome
{
    public const string ValidationMessageTemplateKey = "ValidationMessageTemplate";

    public static Style StyleOf(string key) => (Style)Application.Current.FindResource(key);

    public static void ApplyWindowChrome(Window window)
    {
        // WPF 不會把 TargetType="Window" 的隱含樣式套到 Window 子類別，因此直接設定字型與底色。
        window.FontFamily = (FontFamily)Application.Current.FindResource("AppFont");
        window.Foreground = UiTheme.TextStrongBrush;
        window.Background = UiTheme.WindowBackgroundBrush;
        window.Resources[typeof(TabControl)] = StyleOf("EditorTabControl");
        window.Resources[typeof(ListBox)] = StyleOf("EditorList");
        window.Resources[typeof(GroupBox)] = StyleOf("EditorGroupBox");
        window.Resources[typeof(ProgressBar)] = StyleOf("ThinProgressBar");
    }

    public static StackPanel PageHeader(string title, string description)
    {
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock { Text = title, Style = StyleOf("ResultPageTitle") });
        var hint = Hint(description);
        hint.Margin = new Thickness(0, 4, 0, 0);
        header.Children.Add(hint);
        return header;
    }

    public static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        FontSize = 13,
        FontWeight = FontWeights.SemiBold,
        Foreground = UiTheme.TextStrongBrush,
        Margin = new Thickness(0, 12, 0, 6)
    };

    public static TextBlock Hint(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = UiTheme.TextMutedBrush
    };

    public static TextBlock FieldLabel(string text) => new() { Text = text, Style = StyleOf("FieldLabel") };

    /// <summary>白底圓角卡片；有標題時標題在左上，內容填滿其餘空間。</summary>
    public static Border Card(UIElement? child = null, string? title = null)
    {
        var card = new Border { Style = StyleOf("ResultCard"), Padding = new Thickness(10) };
        if (title is null)
        {
            card.Child = child;
            return card;
        }

        var panel = new DockPanel();
        var heading = SectionTitle(title);
        heading.Margin = new Thickness(0, 0, 0, 6);
        DockPanel.SetDock(heading, Dock.Top);
        panel.Children.Add(heading);
        if (child is not null) panel.Children.Add(child);
        card.Child = panel;
        return card;
    }

    /// <summary>主要／次要按鈕；外觀與互動狀態全部由共用樣式決定。</summary>
    public static Button Button(string label, RoutedEventHandler handler, bool primary)
    {
        var button = new Button { Content = label, Style = StyleOf(primary ? "PrimaryButton" : "SecondaryButton") };
        button.Click += handler;
        return button;
    }

    /// <summary>放大鏡圖示＋灰色提示字的搜尋框；可傳入既有文字框（保留其名稱與事件）。</summary>
    public static TextBox SearchBox(string placeholder, TextBox? box = null)
    {
        box ??= new TextBox();
        box.Style = StyleOf("SearchBox");
        box.Tag = placeholder;
        return box;
    }

    public static Ellipse StatusDot(Brush fill) => new()
    {
        Width = 8,
        Height = 8,
        Fill = fill,
        Margin = new Thickness(0, 0, 6, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    public static SolidColorBrush ValidationSeverityBrush(ProjectValidationSeverity severity) => severity switch
    {
        ProjectValidationSeverity.Error => UiTheme.DangerBrush,
        ProjectValidationSeverity.Warning => UiTheme.CautionBrush,
        _ => UiTheme.TextSubtleBrush
    };

    /// <summary>底部驗證摘要色點：未驗證灰、有錯誤紅、只有提醒黃、通過綠。</summary>
    public static SolidColorBrush ValidationSummaryBrush(bool validated, int errors, int warnings) =>
        !validated ? UiTheme.TextSubtleBrush
        : errors > 0 ? UiTheme.DangerBrush
        : warnings > 0 ? UiTheme.CautionBrush
        : UiTheme.SuccessBrush;
}

/// <summary>驗證訊息嚴重度 → 色點畫筆（驗證清單範本使用）。</summary>
public sealed class ValidationSeverityBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ProjectValidationSeverity severity ? EditorChrome.ValidationSeverityBrush(severity) : UiTheme.TextSubtleBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException("驗證色點只供顯示，不支援反向轉換。");
}
```

- [ ] **Step 5: 執行，確認通過**

```powershell
dotnet build .\MrtRouteSimulator.slnx -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --editor-theme-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --shell-layout-only
```

Expected: 0／0；`[通過] 編輯器共用樣式與 EditorChrome 元件（含多選、焦點框、停用按鈕）`、`[通過] 編輯器程式無寫死顏色（1 個檔案）`、`PASS WPF editor theme`；`PASS WPF shell layout`（主視窗不受影響）。

- [ ] **Step 6: Commit**

```powershell
git add src/MrtRouteSimulator.App/EditorChrome.cs src/MrtRouteSimulator.App/Themes/Controls.xaml tests/MrtRouteSimulator.WpfTests/EditorThemeTests.cs
git commit -m "feat: add editor chrome helpers and editor styles" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: 編輯器外殼

**Files:**
- Modify: `src/MrtRouteSimulator.App/TopologyEditorWindow.cs`（欄位、建構子、`BuildShell`、`SetValidationMessages`、`CreateButton`）
- Test: `tests/MrtRouteSimulator.WpfTests/EditorThemeTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `EditorChrome.ApplyWindowChrome`、`StyleOf`、`SearchBox`、`Card`、`Button`、`StatusDot`、`ValidationSummaryBrush`、`ValidationMessageTemplateKey`。
- Produces: 欄位 `private readonly Ellipse validationSummaryDot`；頁首 `Grid` 名稱 `EditorHeader`、底部 `DockPanel` 名稱 `EditorFooter`；`CreateButton(label, handler, secondary)` 改由 `EditorChrome.Button(label, handler, primary: !secondary)` 建立（Task 3 依此調整主次）。

- [ ] **Step 1: 寫失敗測試**

`Run` 改為：

```csharp
    public static void Run(string root)
    {
        VerifyEditorStyles();
        VerifyShell(root);
        VerifyMinimumSizeAndScale(root);
        VerifyNoHardCodedEditorColors(root);
        Console.WriteLine("PASS WPF editor theme");
    }
```

加入：

```csharp
    internal static readonly string[] NavigationPages =
        ["Project", "Stations", "Tracks", "Vehicle", "Services", "StopPatterns", "DispatchPlanning", "Simulation", "AdvancedData", "Validation"];

    private static FrameworkElement Named(DependencyObject root, string name) =>
        Descendants(root).OfType<FrameworkElement>().Single(element => element.Name == name);

    private static void VerifyShell(string root)
    {
        var window = OpenEditor(root, "Tracks");
        try
        {
            Require(ReferenceEquals(window.FontFamily, Application.Current.FindResource("AppFont"))
                    && ReferenceEquals(window.Background, UiTheme.WindowBackgroundBrush),
                "編輯器視窗必須套用 App 字型與視窗底色。");
            var navigation = (ListBox)Field(window, "navigation")!;
            Require(ReferenceEquals(navigation.Style, EditorChrome.StyleOf("EditorNavList"))
                    && navigation.ReadLocalValue(Control.BackgroundProperty) == DependencyProperty.UnsetValue,
                "左側導覽必須使用 EditorNavList，且不自行設定底色。");
            var selected = (ListBoxItem)navigation.ItemContainerGenerator.ContainerFromItem(navigation.SelectedItem);
            Require(ReferenceEquals(((Border)selected.Template.FindName("Chrome", selected)).Background, UiTheme.NavSelectedBrush)
                    && ((Border)selected.Template.FindName("Indicator", selected)).Visibility == Visibility.Visible
                    && ReferenceEquals(selected.Foreground, UiTheme.AccentBrush),
                "選取的導覽項目必須是淺藍底、左側藍色指示條與藍字。");
            var search = (TextBox)Field(window, "workspaceSearch")!;
            Require(ReferenceEquals(search.Style, EditorChrome.StyleOf("SearchBox")) && Equals(search.Tag, "搜尋頁面")
                    && AutomationProperties.GetName(search) == "搜尋工作區頁面",
                "頁首搜尋框必須使用 SearchBox、提示字「搜尋頁面」，並保留自動化名稱。");
            Require(ReferenceEquals(((TextBlock)Field(window, "workspaceSummary")!).Style, EditorChrome.StyleOf("ResultPageTitle")),
                "頁首標題必須使用頁面標題樣式。");
            var headerButtons = Descendants(Named(window, "EditorHeader")).OfType<Button>().Where(b => b.TemplatedParent is null).ToArray();
            Require(headerButtons.Length == 1 && Equals(headerButtons[0].Content, "收合")
                    && ReferenceEquals(headerButtons[0].Style, EditorChrome.StyleOf("GhostButton")),
                "右側面板切換鈕維持文字「收合」，改用淡色按鈕樣式。");
            var footerButtons = Descendants(Named(window, "EditorFooter")).OfType<Button>().Where(b => b.TemplatedParent is null)
                .ToDictionary(button => (string)button.Content);
            Require(ReferenceEquals(footerButtons["套用"].Style, EditorChrome.StyleOf("PrimaryButton"))
                    && ReferenceEquals(footerButtons["驗證"].Style, EditorChrome.StyleOf("SecondaryButton"))
                    && ReferenceEquals(footerButtons["取消"].Style, EditorChrome.StyleOf("SecondaryButton")),
                "底部「套用」為主要按鈕，「驗證」「取消」為次要按鈕。");
            var rightPanel = (Grid)Field(window, "rightPanel")!;
            Require(rightPanel.Children.OfType<Border>().Count(border => ReferenceEquals(border.Style, EditorChrome.StyleOf("ResultCard"))) == 2,
                "右側面板必須是「選取項目」「驗證」兩張卡片。");

            var dot = (Ellipse)Field(window, "validationSummaryDot")!;
            Require(ReferenceEquals(dot.Fill, UiTheme.TextSubtleBrush), "尚未驗證時摘要色點為灰色。");
            var setMessages = EditorType.GetMethod("SetValidationMessages", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var longText = string.Concat(Enumerable.Repeat("很長的驗證訊息需要換行顯示，", 8));
            setMessages.Invoke(window, [new ProjectValidationMessage[]
            {
                new(ProjectValidationSeverity.Error, longText),
                new(ProjectValidationSeverity.Warning, "提醒訊息"),
                new(ProjectValidationSeverity.Info, "資訊訊息")
            }]);
            window.UpdateLayout();
            Require(ReferenceEquals(dot.Fill, UiTheme.DangerBrush), "有錯誤時摘要色點為紅色。");
            var list = (ListBox)Field(window, "validationList")!;
            var expected = new[] { UiTheme.DangerBrush, UiTheme.CautionBrush, UiTheme.TextSubtleBrush };
            for (var index = 0; index < expected.Length; index++)
            {
                var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(index);
                var ellipse = Descendants(container).OfType<Ellipse>().Single();
                Require(ReferenceEquals(ellipse.Fill, expected[index]), $"驗證清單第 {index + 1} 列的色點顏色不符。");
            }
            var first = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            var firstText = Descendants(first).OfType<TextBlock>().Single(text => text.Text.Length > 40);
            var firstDot = Descendants(first).OfType<Ellipse>().Single();
            Require(firstText.ActualHeight > firstDot.ActualHeight * 3 && firstDot.VerticalAlignment == VerticalAlignment.Top,
                "很長的驗證訊息必須換行，色點對齊第一行。");
            setMessages.Invoke(window, [new[] { new ProjectValidationMessage(ProjectValidationSeverity.Warning, "只有提醒") }]);
            Require(ReferenceEquals(dot.Fill, UiTheme.CautionBrush), "只有提醒時摘要色點為黃色。");
            setMessages.Invoke(window, [Array.Empty<ProjectValidationMessage>()]);
            Require(ReferenceEquals(dot.Fill, UiTheme.SuccessBrush), "驗證通過時摘要色點為綠色。");
        }
        finally { window.Close(); }
        Console.WriteLine("[通過] 編輯器外殼：導覽、頁首、右側卡片、驗證色點與底部按鈕");
    }

    // 守門測試：最小視窗與 125% 縮放下，頁首與底部操作不可被裁切（可能在改動前就通過）。
    private static void VerifyMinimumSizeAndScale(string root)
    {
        foreach (var scale in new[] { 1d, 1.25d })
        {
            var previous = ShellLayoutTests.SetInterfaceScale(scale);
            var window = OpenEditor(root, "Project", 980, 640);
            try
            {
                foreach (var page in NavigationPages)
                {
                    Navigate(window, page);
                    var content = (FrameworkElement)window.Content;
                    var bounds = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
                    foreach (var area in new[] { "EditorHeader", "EditorFooter" })
                    foreach (var element in Descendants(Named(window, area)).OfType<Control>()
                                 .Where(control => control.TemplatedParent is null && control is Button or TextBox or CheckBox))
                    {
                        var rect = element.TransformToAncestor(content).TransformBounds(new Rect(element.RenderSize));
                        Require(bounds.Contains(rect),
                            $"{scale:P0}、980×640 的「{page}」頁，{area} 的「{(element as ContentControl)?.Content ?? AutomationProperties.GetName(element)}」被裁切。");
                    }
                }
            }
            finally
            {
                window.Close();
                ShellLayoutTests.SetInterfaceScale(previous);
            }
        }
        Console.WriteLine("[通過] 編輯器在 980×640 與 125% 縮放下頁首與底部操作完整可見");
    }
```

- [ ] **Step 2: 執行，確認失敗**

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --editor-theme-only
```

Expected: 建置成功；執行以 `編輯器視窗必須套用 App 字型與視窗底色。` 失敗。

- [ ] **Step 3: 實作**

以 Bash 執行（worktree 根目錄）：

```bash
python - <<'EOF'
from pathlib import Path
def rep(path, old, new, count=1):
    p = Path(path); s = p.read_text(encoding='utf-8')
    n = s.count(old)
    assert n == count, f'{path}: expected {count}, found {n}: {old[:70]!r}'
    p.write_text(s.replace(old, new), encoding='utf-8', newline='')

f = 'src/MrtRouteSimulator.App/TopologyEditorWindow.cs'
rep(f, '''    private readonly TextBlock validationSummary = new();
''', '''    private readonly TextBlock validationSummary = new();
    private readonly Ellipse validationSummaryDot = EditorChrome.StatusDot(UiTheme.TextSubtleBrush);
''')
rep(f, '''        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildShell();''', '''        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        EditorChrome.ApplyWindowChrome(this);
        Content = BuildShell();''')
rep(f, '''        var root = new Grid { Background = Brushes.White };''', '''        var root = new Grid();''')
rep(f, '''        var header = new Grid { Margin = new Thickness(14, 10, 14, 4) };''',
       '''        var header = new Grid { Name = "EditorHeader", Margin = new Thickness(14, 10, 14, 4) };''')
rep(f, '''        workspaceSummary.FontSize = 16;
        workspaceSummary.FontWeight = FontWeights.SemiBold;
''', '''        workspaceSummary.Style = EditorChrome.StyleOf("ResultPageTitle");
''')
rep(f, '''        workspaceSearch.Width = 220;
        workspaceSearch.Height = 28;
''', '''        EditorChrome.SearchBox("搜尋頁面", workspaceSearch);
        workspaceSearch.Width = 220;
''')
rep(f, '''        rightPanelToggle = CreateButton("收合", (_, _) => ToggleRightPanel(), true);
''', '''        rightPanelToggle = CreateButton("收合", (_, _) => ToggleRightPanel(), true);
        rightPanelToggle.Style = EditorChrome.StyleOf("GhostButton");
''')
rep(f, '''        navigation.Background = new SolidColorBrush(Color.FromRgb(247, 249, 252));
''', '''        navigation.Style = EditorChrome.StyleOf("EditorNavList");
''')
rep(f, '''        navigationText.SetValue(TextBlock.MarginProperty, new Thickness(4, 5, 4, 5));''',
       '''        navigationText.SetValue(TextBlock.MarginProperty, new Thickness(0, 2, 0, 2));''')
rep(f, '''        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(230) });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var propertyHeader = new DockPanel();
        propertyHeader.Children.Add(new TextBlock { Text = "選取項目", FontSize = 17, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        right.Children.Add(propertyHeader);
        selectionDetails.Margin = new Thickness(0, 8, 0, 12);
        selectionDetails.Foreground = new SolidColorBrush(Color.FromRgb(65, 75, 95));
        var selectionScroll = new ScrollViewer
        {
            Content = selectionDetails,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(selectionScroll, 1);
        right.Children.Add(selectionScroll);
        var validationTitle = new TextBlock { Text = "驗證", FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        Grid.SetRow(validationTitle, 2);
        right.Children.Add(validationTitle);
        validationList.BorderBrush = new SolidColorBrush(Color.FromRgb(215, 221, 232));
        validationList.BorderThickness = new Thickness(1);
        validationList.ItemsSource = validationMessages;
        AttachValidationActivation(validationList);
        Grid.SetRow(validationList, 3);
        validationList.DisplayMemberPath = nameof(ProjectValidationMessageViewModel.DisplayMessage);
        right.Children.Add(validationList);
''', '''        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(270) });
        selectionDetails.Margin = new Thickness(0, 0, 0, 4);
        selectionDetails.Foreground = UiTheme.TextStrongBrush;
        var selectionScroll = new ScrollViewer
        {
            Content = selectionDetails,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        right.Children.Add(EditorChrome.Card(selectionScroll, "選取項目"));
        // 驗證清單放在卡片內，不再有自己的框線；每列前的色點依嚴重度。
        validationList.BorderThickness = new Thickness(0);
        validationList.Padding = new Thickness(0);
        validationList.ItemsSource = validationMessages;
        validationList.ItemTemplate = (DataTemplate)Application.Current.FindResource(EditorChrome.ValidationMessageTemplateKey);
        AttachValidationActivation(validationList);
        var validationCard = EditorChrome.Card(validationList, "驗證");
        validationCard.Margin = new Thickness(0, 10, 0, 0);
        Grid.SetRow(validationCard, 1);
        right.Children.Add(validationCard);
''')
rep(f, '''        var footer = new DockPanel { Margin = new Thickness(14, 4, 14, 10), LastChildFill = false };
        validationSummary.Text = "尚未驗證草稿";
        validationSummary.Foreground = new SolidColorBrush(Color.FromRgb(65, 75, 95));
        validationSummary.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(validationSummary, Dock.Left);
        footer.Children.Add(validationSummary);
''', '''        var footer = new DockPanel { Name = "EditorFooter", Margin = new Thickness(14, 4, 14, 10), LastChildFill = false };
        validationSummary.Text = "尚未驗證草稿";
        validationSummary.Foreground = UiTheme.TextMutedBrush;
        validationSummary.VerticalAlignment = VerticalAlignment.Center;
        var summaryRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        summaryRow.Children.Add(validationSummaryDot);
        summaryRow.Children.Add(validationSummary);
        DockPanel.SetDock(summaryRow, Dock.Left);
        footer.Children.Add(summaryRow);
''')
rep(f, '''            validationSummary.Text = errors == 0 && warnings == 0
                ? "驗證通過 · 草稿可套用"
                : $"驗證摘要：{errors} 個錯誤、{warnings} 個提醒 · 點選訊息可定位";
''', '''            validationSummary.Text = errors == 0 && warnings == 0
                ? "驗證通過 · 草稿可套用"
                : $"驗證摘要：{errors} 個錯誤、{warnings} 個提醒 · 點選訊息可定位";
            validationSummaryDot.Fill = EditorChrome.ValidationSummaryBrush(validated: true, errors, warnings);
''')
rep(f, '''    private static Button CreateButton(string label, RoutedEventHandler handler, bool secondary)
    {
        var button = new Button
        {
            Content = label, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 7, 0),
            Background = secondary ? Brushes.White : new SolidColorBrush(Color.FromRgb(34, 92, 175)),
            Foreground = secondary ? new SolidColorBrush(Color.FromRgb(38, 51, 73)) : Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(164, 178, 202))
        };
        button.Click += handler;
        return button;
    }
''', '''    // 主次外觀由共用樣式決定；secondary=false 只保留給規格列出的主要動作。
    private static Button CreateButton(string label, RoutedEventHandler handler, bool secondary)
    {
        var button = EditorChrome.Button(label, handler, primary: !secondary);
        button.Margin = new Thickness(0, 0, 7, 0);
        return button;
    }
''')
EOF
```

- [ ] **Step 4: 執行，確認通過**

```powershell
dotnet build .\MrtRouteSimulator.slnx -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --editor-theme-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --workspace-only
```

Expected: 0／0；`[通過] 編輯器外殼：導覽、頁首、右側卡片、驗證色點與底部按鈕`、`[通過] 編輯器在 980×640 與 125% 縮放下頁首與底部操作完整可見`、`PASS WPF editor theme`；`PASS WPF topology workspace and input pages`。

- [ ] **Step 5: Commit**

```powershell
git add src/MrtRouteSimulator.App/TopologyEditorWindow.cs tests/MrtRouteSimulator.WpfTests/EditorThemeTests.cs
git commit -m "feat: theme topology editor shell, navigation and validation panel" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: 編輯器各頁

**Files:**
- Modify: `src/MrtRouteSimulator.App/TopologyEditorWindow.cs`（`ShowProjectHome`、`ShowQuickBuilder`、`BuildServiceRouteEditor`、`ShowDispatch`、`ShowSimulation`、`ShowValidation`、`EditorTab`、`StationAndPlatformTab`、`NewPage`、`CreateGrid`、`SummaryGrid`）
- Modify: `src/MrtRouteSimulator.App/TopologyEditorWindow.Stations.cs`
- Modify: `src/MrtRouteSimulator.App/TopologyEditorWindow.Operations.cs`
- Modify: `src/MrtRouteSimulator.App/TopologyEditorWindow.Settings.cs`
- Test: `tests/MrtRouteSimulator.WpfTests/EditorThemeTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `EditorChrome.PageHeader`、`SectionTitle`、`Hint`、`FieldLabel`、`Card`、`SearchBox`、`ValidationMessageTemplateKey`；Task 2 的 `CreateButton`、`NavigationPages`、`Named`。
- Produces: `EditorThemeTests.InspectElements(DependencyObject root, string context)`（Task 5 對話框檢查共用）、`AllowedPrimary`。

- [ ] **Step 1: 寫失敗測試**

`Run` 在 `VerifyMinimumSizeAndScale(root);` 之前加入 `VerifyPages(root);`。`ScannedFiles` 改為：

```csharp
    private static readonly string[] ScannedFiles =
        ["EditorChrome.cs", "TopologyEditorWindow.Stations.cs", "TopologyEditorWindow.Operations.cs", "TopologyEditorWindow.Settings.cs"];
```

加入：

```csharp
    internal static readonly HashSet<string> AllowedPrimary = new(StringComparer.Ordinal)
        { "套用", "建立格式版本 8 拓撲", "以此站型重新起稿", "建立", "選取", "套用明確側別" };

    private static readonly HashSet<Brush> ThemeBrushes = typeof(UiTheme).GetFields(BindingFlags.Public | BindingFlags.Static)
        .SelectMany(field => field.GetValue(null) switch
        {
            SolidColorBrush brush => new[] { brush },
            SolidColorBrush[] brushes => brushes,
            _ => Array.Empty<SolidColorBrush>()
        })
        .Cast<Brush>().ToHashSet();

    private static bool LocalBrushOk(DependencyObject element, DependencyProperty property) =>
        element.ReadLocalValue(property) is var value
        && (value == DependencyProperty.UnsetValue || value is Brush brush && ThemeBrushes.Contains(brush));

    private static bool InsideCanvas(DependencyObject element)
    {
        for (var parent = VisualTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is Canvas) return true;
        return false;
    }

    private static void VerifyPages(string root)
    {
        var window = OpenEditor(root, "Project");
        try
        {
            foreach (var page in NavigationPages.Concat(["QuickBuilder", "Schematic", "Results"]))
            {
                Navigate(window, page);
                InspectTree(window, (DependencyObject)window.Content, page, []);
            }
            // 由設施建立等操作回到的舊頁仍可開啟，一併巡檢。
            foreach (var legacy in new[] { "ShowInfrastructure", "ShowOperations", "ShowDispatch", "ShowSimulation" })
            {
                EditorType.GetMethod(legacy, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
                window.UpdateLayout();
                InspectTree(window, (DependencyObject)window.Content, legacy, []);
            }
        }
        finally { window.Close(); }
        Console.WriteLine("[通過] 編輯器各頁按鈕、分段標籤、清單、表格與文字顏色一致");
    }

    // 逐一切換每個分頁，讓未顯示的分頁內容也被檢查。
    private static void InspectTree(Window window, DependencyObject root, string context, HashSet<TabControl> visited)
    {
        InspectElements(root, context);
        foreach (var tabs in Descendants(root).OfType<TabControl>().Where(item => item.TemplatedParent is null && visited.Add(item)).ToArray())
        {
            Require(ReferenceEquals(tabs.Style, EditorChrome.StyleOf("EditorTabControl")), $"{context}：分頁必須使用膠囊式分段樣式。");
            var original = tabs.SelectedIndex;
            for (var index = 0; index < tabs.Items.Count; index++)
            {
                var item = (TabItem)tabs.Items[index]!;
                Require(ReferenceEquals(item.Style, EditorChrome.StyleOf("EditorTabItem")) && item.Header is string { Length: > 0 },
                    $"{context}：分頁標籤「{item.Header}」必須使用分段標籤樣式且有文字。");
                tabs.SelectedIndex = index;
                window.UpdateLayout();
                if (item.Content is DependencyObject content) InspectTree(window, content, $"{context}／{item.Header}", visited);
            }
            tabs.SelectedIndex = original;
            window.UpdateLayout();
        }
    }

    internal static void InspectElements(DependencyObject root, string context)
    {
        var primary = EditorChrome.StyleOf("PrimaryButton");
        var secondary = EditorChrome.StyleOf("SecondaryButton");
        var ghost = EditorChrome.StyleOf("GhostButton");
        foreach (var element in Descendants(root).OfType<FrameworkElement>().Where(item => item.TemplatedParent is null))
        {
            if (InsideCanvas(element)) continue; // 路線示意圖由 Task 4 另外檢查
            switch (element)
            {
                case Button button:
                    Require(ReferenceEquals(button.Style, primary) || ReferenceEquals(button.Style, secondary) || ReferenceEquals(button.Style, ghost),
                        $"{context}：按鈕「{button.Content}」必須使用共用按鈕樣式。");
                    Require(button.ReadLocalValue(Control.BackgroundProperty) == DependencyProperty.UnsetValue
                            && button.ReadLocalValue(Control.ForegroundProperty) == DependencyProperty.UnsetValue
                            && button.ReadLocalValue(Control.BorderBrushProperty) == DependencyProperty.UnsetValue,
                        $"{context}：按鈕「{button.Content}」不得自己設定顏色。");
                    if (ReferenceEquals(button.Style, primary))
                        Require(AllowedPrimary.Contains(button.Content as string ?? ""), $"{context}：「{button.Content}」不應是主要按鈕。");
                    break;
                case ListBox list:
                    Require(ReferenceEquals(list.Style, EditorChrome.StyleOf("EditorList")) || ReferenceEquals(list.Style, EditorChrome.StyleOf("EditorNavList")),
                        $"{context}：清單「{AutomationProperties.GetName(list)}」必須使用編輯器清單樣式。");
                    Require(LocalBrushOk(list, Control.BorderBrushProperty) && LocalBrushOk(list, Control.BackgroundProperty),
                        $"{context}：清單「{AutomationProperties.GetName(list)}」不得自己設定框色或底色。");
                    break;
                case GroupBox group:
                    Require(ReferenceEquals(group.Style, EditorChrome.StyleOf("EditorGroupBox")), $"{context}：群組框「{group.Header}」必須使用卡片樣式。");
                    break;
                case DataGrid grid:
                    Require(grid.ReadLocalValue(Control.BorderBrushProperty) == DependencyProperty.UnsetValue,
                        $"{context}：表格「{AutomationProperties.GetName(grid)}」不得自己設定框色。");
                    break;
                case TextBlock text:
                    Require(LocalBrushOk(text, TextBlock.ForegroundProperty),
                        $"{context}：文字「{(text.Text.Length > 20 ? text.Text[..20] : text.Text)}」的顏色必須取自 UiTheme。");
                    break;
                case Border border:
                    Require(LocalBrushOk(border, Border.BorderBrushProperty) && LocalBrushOk(border, Border.BackgroundProperty),
                        $"{context}：區塊外框與底色必須取自 UiTheme。");
                    break;
                case Control control:
                    Require(LocalBrushOk(control, Control.BackgroundProperty) && LocalBrushOk(control, Control.ForegroundProperty)
                            && LocalBrushOk(control, Control.BorderBrushProperty),
                        $"{context}：控制項「{AutomationProperties.GetName(control)}」的顏色必須取自 UiTheme。");
                    break;
            }
        }
    }
```

- [ ] **Step 2: 執行，確認失敗**

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --editor-theme-only
```

Expected: 建置成功；執行在 `Project` 頁失敗（說明文字仍是寫死顏色，或「編輯專案編號與名稱…」不應是主要按鈕）。

- [ ] **Step 3: 實作 `TopologyEditorWindow.cs` 各頁與共用方法**

以 Bash 執行：

```bash
python - <<'EOF'
from pathlib import Path
def rep(path, old, new, count=1):
    p = Path(path); s = p.read_text(encoding='utf-8')
    n = s.count(old)
    assert n == count, f'{path}: expected {count}, found {n}: {old[:70]!r}'
    p.write_text(s.replace(old, new), encoding='utf-8', newline='')

f = 'src/MrtRouteSimulator.App/TopologyEditorWindow.cs'
rep(f, '''            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(75, 86, 106)),
            Margin = new Thickness(0, 12, 0, 8)''', '''            TextWrapping = TextWrapping.Wrap,
            Foreground = UiTheme.TextMutedBrush,
            Margin = new Thickness(0, 12, 0, 8)''')
rep(f, '''CreateButton("編輯專案編號與名稱…", (_, _) => EditProjectIdentity(), false)''',
       '''CreateButton("編輯專案編號與名稱…", (_, _) => EditProjectIdentity(), true)''')
rep(f, '''        panel.Children.Add(new TextBlock { Text = "依參考圖建立站場", FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 20, 0, 6) });''',
       '''        panel.Children.Add(EditorChrome.SectionTitle("依參考圖建立站場"));''')
rep(f, '''        detail.Children.Add(new TextBlock { Text = "依序通過的軌道區段／路線停靠站", FontSize = 15, FontWeight = FontWeights.SemiBold });''',
       '''        detail.Children.Add(EditorChrome.SectionTitle("依序通過的軌道區段／路線停靠站"));''')
rep(f, '''CreateButton("自動建立路徑", (_, _) => BuildRoutePath(), false)''', '''CreateButton("自動建立路徑", (_, _) => BuildRoutePath(), true)''')
rep(f, '''CreateButton("設定啟用模式與車輛分配…", (_, _) => EditDispatchModes(), false)''', '''CreateButton("設定啟用模式與車輛分配…", (_, _) => EditDispatchModes(), true)''')
rep(f, '''CreateButton("調整列車基準參數…", (_, _) => EditTrainSettings(), false)''', '''CreateButton("調整列車基準參數…", (_, _) => EditTrainSettings(), true)''')
rep(f, '''CreateButton("調整營運與安全參數…", (_, _) => EditOperationalSettings(), false)''', '''CreateButton("調整營運與安全參數…", (_, _) => EditOperationalSettings(), true)''')
rep(f, '''CreateButton("調整模擬參數…", (_, _) => EditSimulationSettings(), false)''', '''CreateButton("調整模擬參數…", (_, _) => EditSimulationSettings(), true)''')
rep(f, '''CreateButton("新增車站到軌道…", (_, _) => AddStationOnTracks(), false)''', '''CreateButton("新增車站到軌道…", (_, _) => AddStationOnTracks(), true)''')
rep(f, '''CreateButton(readOnly ? "新增設施…" : "新增", (_, _) => add(), !readOnly)''', '''CreateButton(readOnly ? "新增設施…" : "新增", (_, _) => add(), true)''')
rep(f, '''CreateButton(readOnly ? "查看設施" : "新增", (_, _) => add(), !readOnly)''', '''CreateButton(readOnly ? "查看設施" : "新增", (_, _) => add(), true)''')
rep(f, '''        var list = new ListBox { ItemsSource = validationMessages, Height = 480, DisplayMemberPath = nameof(ProjectValidationMessageViewModel.DisplayMessage) };''',
       '''        var list = new ListBox { ItemsSource = validationMessages, Height = 480, ItemTemplate = (DataTemplate)Application.Current.FindResource(EditorChrome.ValidationMessageTemplateKey) };''')
rep(f, '''        var search = new TextBox { Width = 150, Margin = new Thickness(6, 0, 0, 0), ToolTip = "依名稱、ID、類型或關聯搜尋" };''',
       '''        var search = EditorChrome.SearchBox("搜尋名稱、ID 或關聯", new TextBox { Width = 180, Margin = new Thickness(6, 0, 0, 0), ToolTip = "依名稱、ID、類型或關聯搜尋" });''')
rep(f, '''        panel.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(75, 86, 106)), Margin = new Thickness(0, 6, 0, 14) });
''', '''        panel.Children.Add(EditorChrome.PageHeader(title, description));
''')
rep(f, '''            IsReadOnly = readOnly, RowHeaderWidth = 0, Margin = new Thickness(0),
            BorderBrush = new SolidColorBrush(Color.FromRgb(215, 221, 232)), BorderThickness = new Thickness(1)
''', '''            IsReadOnly = readOnly, RowHeaderWidth = 0, Margin = new Thickness(0)
''')
rep(f, '''            var label = new TextBlock { Text = values[index].Label, Margin = new Thickness(0, 5, 12, 5), Foreground = new SolidColorBrush(Color.FromRgb(90, 101, 122)) };
            var value = new TextBlock { Text = values[index].Value, Margin = new Thickness(0, 5, 0, 5), FontWeight = FontWeights.SemiBold };''',
       '''            var label = new TextBlock { Text = values[index].Label, Margin = new Thickness(0, 5, 12, 5), Foreground = UiTheme.TextMutedBrush };
            var value = new TextBlock { Text = values[index].Value, Margin = new Thickness(0, 5, 0, 5), FontWeight = FontWeights.SemiBold, Foreground = UiTheme.TextStrongBrush };''')
EOF
```

- [ ] **Step 4: 實作車站、營運、模擬設定頁**

以 Bash 執行：

```bash
python - <<'EOF'
from pathlib import Path
def rep(path, old, new, count=1):
    p = Path(path); s = p.read_text(encoding='utf-8')
    n = s.count(old)
    assert n == count, f'{path}: expected {count}, found {n}: {old[:70]!r}'
    p.write_text(s.replace(old, new), encoding='utf-8', newline='')

st = 'src/MrtRouteSimulator.App/TopologyEditorWindow.Stations.cs'
rep(st, '''        searchPanel.Children.Add(new TextBlock { Text = "搜尋車站", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        stationPageSearchTextBox = new TextBox { Name = "StationSearchTextBox", Height = 28, ToolTip = "輸入車站編號或名稱" };''',
        '''        searchPanel.Children.Add(EditorChrome.FieldLabel("搜尋車站"));
        stationPageSearchTextBox = EditorChrome.SearchBox("搜尋車站", new TextBox { Name = "StationSearchTextBox", ToolTip = "輸入車站編號或名稱" });''')
rep(st, '''            MinHeight = 120, Margin = new Thickness(0, 8, 0, 0), BorderBrush = new SolidColorBrush(Color.FromRgb(215, 221, 232)), BorderThickness = new Thickness(1),
''', '''            MinHeight = 120, Margin = new Thickness(0, 8, 0, 0),
''')
rep(st, '''        var schematicBox = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(215, 221, 232)), BorderThickness = new Thickness(1), Padding = new Thickness(10), Margin = new Thickness(0, 10, 0, 0)
        };
        var schematicPanel = new StackPanel();
        schematicPanel.Children.Add(new TextBlock { Text = "圖面配置", FontSize = 16, FontWeight = FontWeights.SemiBold });
        schematicPanel.Children.Add(new TextBlock { Text = "完整配線預覽沿用既有 StationSchematicPresentation；X/Y 與 lane 僅供呈現，實體 edge-local position、port-side 與 directed connection 仍由上方資料表維持。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 8) });
''', '''        var schematicBox = EditorChrome.Card();
        schematicBox.Margin = new Thickness(0, 10, 0, 0);
        var schematicPanel = new StackPanel();
        var schematicTitle = EditorChrome.SectionTitle("圖面配置");
        schematicTitle.Margin = new Thickness(0, 0, 0, 4);
        schematicPanel.Children.Add(schematicTitle);
        var schematicHint = EditorChrome.Hint("完整配線預覽沿用既有 StationSchematicPresentation；X/Y 與 lane 僅供呈現，實體 edge-local position、port-side 與 directed connection 仍由上方資料表維持。");
        schematicHint.Margin = new Thickness(0, 0, 0, 8);
        schematicPanel.Children.Add(schematicHint);
''')
rep(st, '''        Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 4), ToolTip = description''',
        '''        Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = UiTheme.TextStrongBrush, Margin = new Thickness(0, 14, 0, 4), ToolTip = description''')
rep(st, '''FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8), Name = "SelectedStationHeading" };''',
        '''FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = UiTheme.TextStrongBrush, Margin = new Thickness(0, 0, 0, 8), Name = "SelectedStationHeading" };''')
rep(st, '''TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(75, 86, 106)), Margin = new Thickness(0, 5, 0, 0) });''',
        '''TextWrapping = TextWrapping.Wrap, Foreground = UiTheme.TextMutedBrush, Margin = new Thickness(0, 5, 0, 0) });''')
rep(st, '''        var box = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(215, 221, 232)), BorderThickness = new Thickness(1), Padding = new Thickness(10) };''',
        '''        var box = EditorChrome.Card();''')
rep(st, '''        var box = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(225, 230, 238)), BorderThickness = new Thickness(1), Padding = new Thickness(10) };''',
        '''        var box = EditorChrome.Card();''', count=4)
rep(st, '''Foreground = new SolidColorBrush(Color.FromRgb(90, 101, 122)) });''', '''Foreground = UiTheme.TextMutedBrush });''', count=2)
rep(st, ''', Height = 100, BorderBrush = new SolidColorBrush(Color.FromRgb(215, 221, 232)), BorderThickness = new Thickness(1) };''',
        ''', Height = 100 };''')

op = 'src/MrtRouteSimulator.App/TopologyEditorWindow.Operations.cs'
rep(op, '''        var search = new TextBox { Width = 220, Margin = new Thickness(0, 0, 0, 8), ToolTip = "依車型名稱、ID 或相容服務搜尋" };''',
        '''        var search = EditorChrome.SearchBox("搜尋車型", new TextBox { Width = 220, Margin = new Thickness(0, 0, 0, 8), ToolTip = "依車型名稱、ID 或相容服務搜尋" });''')
rep(op, '''            Foreground = new SolidColorBrush(Color.FromRgb(75, 86, 106))
''', '''            Foreground = UiTheme.TextMutedBrush
''')
rep(op, ''',
            BorderBrush = new SolidColorBrush(Color.FromRgb(215, 221, 232)), BorderThickness = new Thickness(1)
''', '''
''', count=2)
rep(op, '''Foreground = new SolidColorBrush(Color.FromRgb(65, 75, 95)) };''', '''Foreground = UiTheme.TextMutedBrush };''')
rep(op, '''CreateNamedButton("套用班表設定", "套用班表啟用模式與車輛分配", (_, _) => CommitDispatchModes(), false)''',
        '''CreateNamedButton("套用班表設定", "套用班表啟用模式與車輛分配", (_, _) => CommitDispatchModes(), true)''')
rep(op, '''Foreground = new SolidColorBrush(Color.FromRgb(154, 59, 49)) };''', '''Foreground = UiTheme.WarningTextBrush };''')

se = 'src/MrtRouteSimulator.App/TopologyEditorWindow.Settings.cs'
rep(se, '''            Foreground = new SolidColorBrush(Color.FromRgb(75, 86, 106)),
''', '''            Foreground = UiTheme.TextMutedBrush,
''')
EOF
```

（第 4 步中 `Operations.cs` 的第 3 個替換會移除兩個表格的 `BorderBrush`／`BorderThickness`：停站模式矩陣與手動班表表格，物件初始化子保留結尾逗號是合法語法。）

- [ ] **Step 5: 執行，確認通過**

```powershell
dotnet build .\MrtRouteSimulator.slnx -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --editor-theme-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --workspace-only
```

Expected: 0／0；`[通過] 編輯器各頁按鈕、分段標籤、清單、表格與文字顏色一致`、`[通過] 編輯器程式無寫死顏色（4 個檔案）`、`PASS WPF editor theme`；`PASS WPF topology workspace and input pages`。巡檢若在某頁找到上面沒列到的寫死顏色或主次錯誤，依同一規則修正並記錄 `Ruling`。

- [ ] **Step 6: Commit**

```powershell
git add src/MrtRouteSimulator.App/TopologyEditorWindow.cs src/MrtRouteSimulator.App/TopologyEditorWindow.Stations.cs src/MrtRouteSimulator.App/TopologyEditorWindow.Operations.cs src/MrtRouteSimulator.App/TopologyEditorWindow.Settings.cs tests/MrtRouteSimulator.WpfTests/EditorThemeTests.cs
git commit -m "feat: theme topology editor pages with shared cards, buttons and lists" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: 路線示意圖

**Files:**
- Modify: `src/MrtRouteSimulator.App/TopologyEditorWindow.cs`（`ShowSchematic`、`DrawSchematic`）
- Test: `tests/MrtRouteSimulator.WpfTests/EditorThemeTests.cs`

**Interfaces:**
- Consumes: `TrackRailStyle.Classify(IEnumerable<TrackEdgeDefinition>, IReadOnlySet<string>, IReadOnlySet<string>)`、`TrackRailStyle.Brush(RailTone)`、`TrackRailStyle.Thickness(TrackEdgeDefinition)`、`TrackRailStyle.SideThickness`。
- Produces: 無新介面。

- [ ] **Step 1: 寫失敗測試**

`Run` 在 `VerifyMinimumSizeAndScale(root);` 之前加入 `VerifySchematic(root);`。`ScannedFiles` 加入 `"TopologyEditorWindow.cs"`：

```csharp
    private static readonly string[] ScannedFiles =
        ["EditorChrome.cs", "TopologyEditorWindow.cs", "TopologyEditorWindow.Stations.cs", "TopologyEditorWindow.Operations.cs", "TopologyEditorWindow.Settings.cs"];
```

加入：

```csharp
    private static void VerifySchematic(string root)
    {
        var document = Sample(root);
        var window = (Window)Activator.CreateInstance(EditorType, document, Enum.Parse(PageType, "Tracks"))!;
        try
        {
            var canvas = new Canvas { Width = 1370, Height = 520 };
            canvas.Measure(new Size(1370, 520));
            canvas.Arrange(new Rect(0, 0, 1370, 520));
            EditorType.GetMethod("DrawSchematic", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [canvas]);
            var allowed = new Brush[] { UiTheme.RailUpBrush, UiTheme.RailDownBrush, UiTheme.RailUpSoftBrush, UiTheme.RailDownSoftBrush, UiTheme.RailNeutralBrush };
            var rails = canvas.Children.OfType<Polyline>()
                .Where(line => line.ToolTip is string tip && document.Topology.Edges.Any(edge => tip.StartsWith(edge.TrackEdgeId + "\n", StringComparison.Ordinal)))
                .ToArray();
            Require(rails.Length > 0 && rails.All(line => allowed.Contains(line.Stroke) && line.StrokeThickness is 6d or 4d),
                "示意圖軌道只能使用配線圖的軌道畫筆與線寬（正線 6、側線 4）。");
            Require(rails.Any(line => ReferenceEquals(line.Stroke, UiTheme.RailUpBrush)) && rails.Any(line => ReferenceEquals(line.Stroke, UiTheme.RailDownBrush)),
                "示意圖必須同時出現上行藍與下行橘。");
            var arrows = canvas.Children.OfType<Polygon>().ToArray();
            Require(arrows.Length > 0 && arrows.All(arrow => allowed.Contains(arrow.Fill) && ReferenceEquals(arrow.Stroke, UiTheme.SurfaceBrush)),
                "方向箭頭必須跟隨軌道色並有白色描邊。");
            var markers = canvas.Children.OfType<Rectangle>().Where(item => item.Width == 9 && item.Height == 9).ToArray();
            Require(markers.Length > 0 && markers.All(item => ReferenceEquals(item.Fill, UiTheme.RailNeutralStrongBrush)),
                "設施清單前的小方塊必須是深灰色。");
        }
        finally { window.Close(); }
        Console.WriteLine("[通過] 路線示意圖使用配線圖的軌道配色與線寬");
    }
```

- [ ] **Step 2: 執行，確認失敗**

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --editor-theme-only
```

Expected: 以 `示意圖軌道只能使用配線圖的軌道畫筆與線寬（正線 6、側線 4）。` 失敗。

- [ ] **Step 3: 實作**

以 Bash 執行：

```bash
python - <<'EOF'
from pathlib import Path
def rep(path, old, new, count=1):
    p = Path(path); s = p.read_text(encoding='utf-8')
    n = s.count(old)
    assert n == count, f'{path}: expected {count}, found {n}: {old[:70]!r}'
    p.write_text(s.replace(old, new), encoding='utf-8', newline='')

f = 'src/MrtRouteSimulator.App/TopologyEditorWindow.cs'
rep(f, '''        var canvas = new Canvas { Background = new SolidColorBrush(Color.FromRgb(248, 250, 253)), Height = 520, ClipToBounds = true };''',
       '''        var canvas = new Canvas { Background = UiTheme.CanvasBackgroundBrush, Height = 520, ClipToBounds = true };''')
rep(f, '''        var railColor = Color.FromRgb(25, 96, 125);
''', '')
rep(f, '''        var inboundEdgeIds = state.Draft.ServiceRoutes
            .FirstOrDefault(route => route.ServiceRouteId.Equals(inboundRouteId, StringComparison.OrdinalIgnoreCase))?
            .Traversals.Select(traversal => traversal.TrackEdgeId).ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
''', '''        var inboundEdgeIds = state.Draft.ServiceRoutes
            .FirstOrDefault(route => route.ServiceRouteId.Equals(inboundRouteId, StringComparison.OrdinalIgnoreCase))?
            .Traversals.Select(traversal => traversal.TrackEdgeId).ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // 與主視窗配線圖相同的軌道配色：下行橘、上行藍、側線淡色、其他中性灰。
        var railTones = TrackRailStyle.Classify(state.Draft.Topology.Edges, outboundEdgeIds, inboundEdgeIds);
        RailTone ToneOf(string? edgeId) => edgeId is not null && railTones.TryGetValue(edgeId, out var tone) ? tone : RailTone.Neutral;
''')
rep(f, '''            StationSchematicPresentation.DrawConnection(canvas, from, to, incoming, outgoing, new SolidColorBrush(railColor),
                $"合法轉向：{connection.FromTrackEdgeId} ({connection.FromDirection}) → {connection.ToTrackEdgeId} ({connection.ToDirection})",
                allowLaneTurn);''', '''            var fromTone = ToneOf(connection.FromTrackEdgeId);
            StationSchematicPresentation.DrawConnection(canvas, from, to, incoming, outgoing,
                TrackRailStyle.Brush(fromTone == ToneOf(connection.ToTrackEdgeId) ? fromTone : RailTone.Neutral),
                $"合法轉向：{connection.FromTrackEdgeId} ({connection.FromDirection}) → {connection.ToTrackEdgeId} ({connection.ToDirection})",
                allowLaneTurn, fromEdge is null ? TrackRailStyle.SideThickness : TrackRailStyle.Thickness(fromEdge));''')
rep(f, '''                    Stroke = Brushes.White,
                    StrokeThickness = 7,''', '''                    Stroke = UiTheme.SurfaceBrush,
                    StrokeThickness = 7,''')
rep(f, '''                Stroke = new SolidColorBrush(emphasizeSideTrack ? Color.FromRgb(8, 123, 150) : railColor),
                StrokeThickness = emphasizeSideTrack ? 3.6 : 5,''', '''                Stroke = TrackRailStyle.Brush(ToneOf(edge.TrackEdgeId)),
                StrokeThickness = TrackRailStyle.Thickness(edge),''')
rep(f, '''                Stroke = new SolidColorBrush(railColor),
                StrokeThickness = 5,
                ToolTip = $"{node.NodeId} · {node.Name} · 止衝"''', '''                Stroke = TrackRailStyle.Brush(ToneOf(edge.TrackEdgeId)),
                StrokeThickness = 5,
                ToolTip = $"{node.NodeId} · {node.Name} · 止衝"''')
rep(f, '''Fill = new SolidColorBrush(Color.FromRgb(244, 173, 70)), ToolTip = $"{kind}：{name}" };''',
       '''Fill = UiTheme.RailNeutralStrongBrush, ToolTip = $"{kind}：{name}" };''')
rep(f, '''FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(153, 75, 17)) };''', '''FontSize = 10, Foreground = UiTheme.TextMutedBrush };''')
rep(f, '''                Fill = new SolidColorBrush(railColor),
                Stroke = Brushes.White,''', '''                Fill = TrackRailStyle.Brush(ToneOf(edgeId)),
                Stroke = UiTheme.SurfaceBrush,''')
EOF
```

- [ ] **Step 4: 執行，確認通過**

```powershell
dotnet build .\MrtRouteSimulator.slnx -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --editor-theme-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --visual-rules-only
```

Expected: 0／0；`[通過] 路線示意圖使用配線圖的軌道配色與線寬`、`[通過] 編輯器程式無寫死顏色（5 個檔案）`、`PASS WPF editor theme`；`PASS WPF visual rules only`（全範例示意圖版面規則不受配色影響）。

- [ ] **Step 5: Commit**

```powershell
git add src/MrtRouteSimulator.App/TopologyEditorWindow.cs tests/MrtRouteSimulator.WpfTests/EditorThemeTests.cs
git commit -m "feat: draw editor schematic with track diagram rail colors" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: 對話框

**Files:**
- Modify: `src/MrtRouteSimulator.App/TopologyEditorWindow.cs`（`Ask`、`Choose`，新增 `CreateAskDialog`、`CreateChooseDialog`、`CreateDialogWindow`、`DialogButtons`）
- Modify: `src/MrtRouteSimulator.App/LegacyPortMigrationDialog.cs`
- Modify: `src/MrtRouteSimulator.App/ProjectLoadProgressWindow.cs`
- Modify: `tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs`（`SaveEditorDialogs` 加拍新增對話框）
- Test: `tests/MrtRouteSimulator.WpfTests/EditorThemeTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `EditorChrome.ApplyWindowChrome`、`FieldLabel`、`Button`、`StyleOf`；Task 3 的 `InspectElements`。
- Produces: `private (Window Dialog, Dictionary<string, TextBox> Inputs) CreateAskDialog(string title, (string Label, string Initial)[] fields)`、`private (Window Dialog, ComboBox Choices) CreateChooseDialog(string title, string label, IReadOnlyList<string> values, string? current)`。

- [ ] **Step 1: 寫失敗測試**

`Run` 在 `VerifyMinimumSizeAndScale(root);` 之前加入 `VerifyDialogs(root);`。`ScannedFiles` 改為：

```csharp
    private static readonly string[] ScannedFiles =
    [
        "EditorChrome.cs", "TopologyEditorWindow.cs", "TopologyEditorWindow.Stations.cs", "TopologyEditorWindow.Operations.cs",
        "TopologyEditorWindow.Settings.cs", "LegacyPortMigrationDialog.cs", "ProjectLoadProgressWindow.cs"
    ];
```

加入：

```csharp
    private static void ShowOffscreen(Window window)
    {
        window.Left = -10000;
        window.Top = -10000;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.ShowInTaskbar = false;
        window.Show();
        window.UpdateLayout();
    }

    private static void RequireDialogChrome(Window dialog, string context, string confirm)
    {
        Require(ReferenceEquals(dialog.FontFamily, Application.Current.FindResource("AppFont")) && ReferenceEquals(dialog.Background, UiTheme.SurfaceBrush),
            $"{context}必須套用 App 字型與白底。");
        var buttons = Descendants(dialog).OfType<Button>().Where(button => button.TemplatedParent is null).ToArray();
        Require(buttons.Where(button => ReferenceEquals(button.Style, EditorChrome.StyleOf("PrimaryButton"))).Select(button => button.Content as string).SequenceEqual([confirm])
                && buttons.Where(button => !Equals(button.Content, confirm)).All(button => ReferenceEquals(button.Style, EditorChrome.StyleOf("SecondaryButton"))),
            $"{context}：只有「{confirm}」是主要按鈕，其餘為次要按鈕。");
        InspectElements(dialog, context);
    }

    private static Window Item1(object tuple) => (Window)tuple.GetType().GetField("Item1")!.GetValue(tuple)!;

    private static void VerifyDialogs(string root)
    {
        var editor = OpenEditor(root, "Project");
        try
        {
            var askMethod = EditorType.GetMethod("CreateAskDialog", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("找不到 CreateAskDialog。");
            var ask = Item1(askMethod.Invoke(editor, ["編輯專案識別資料", new (string, string)[] { ("專案編號", "P1"), ("專案名稱", "測試") }])!);
            try
            {
                ShowOffscreen(ask);
                RequireDialogChrome(ask, "新增／編輯對話框", "建立");
                var panel = (StackPanel)((ScrollViewer)ask.Content).Content;
                Require(panel.Margin == new Thickness(20) && panel.Children.OfType<TextBox>().Count() == 2
                        && panel.Children.OfType<TextBlock>().All(label => ReferenceEquals(label.Style, EditorChrome.StyleOf("FieldLabel")))
                        && panel.Children[^1] is StackPanel,
                    "新增／編輯對話框需保留結構（文字框為直接子元素、最後一列按鈕），欄位名稱用欄位標籤樣式、內距 20。");
            }
            finally { ask.Close(); }
            var chooseMethod = EditorType.GetMethod("CreateChooseDialog", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("找不到 CreateChooseDialog。");
            var choose = Item1(chooseMethod.Invoke(editor, ["選擇方向", "方向", new[] { "下行", "上行" }, "上行"])!);
            try
            {
                ShowOffscreen(choose);
                RequireDialogChrome(choose, "選取對話框", "選取");
                var panel = (StackPanel)choose.Content;
                Require(panel.Children.OfType<ComboBox>().Single().SelectedItem as string == "上行" && panel.Children[^1] is StackPanel,
                    "選取對話框需保留結構並預選目前值。");
            }
            finally { choose.Close(); }
        }
        finally { editor.Close(); }

        var source = StationLayoutTemplateService.Build(StationLayoutTemplateKind.IslandTwoTracks);
        var legacy = source with
        {
            Topology = source.Topology with
            {
                Edges = source.Topology.Edges.Select((edge, index) => index < 3 ? edge with { FromPortSide = null, ToPortSide = null } : edge).ToArray()
            }
        };
        var migration = (Window)Activator.CreateInstance(AppType("LegacyPortMigrationDialog"), legacy)!;
        try
        {
            ShowOffscreen(migration);
            RequireDialogChrome(migration, "遷移對話框", "套用明確側別");
            var table = Descendants(migration).OfType<Grid>().Single(grid => grid.ColumnDefinitions.Count == 4);
            Require(table.RowDefinitions.Count == 4, $"遷移對照表必須每列一個列定義（表頭＋3 段軌道），實際 {table.RowDefinitions.Count}。");
            var rows = legacy.Topology.Edges.Take(3)
                .Select(edge => Grid.GetRow(table.Children.OfType<TextBlock>().Single(text => text.Text == edge.TrackEdgeId))).ToArray();
            Require(rows.SequenceEqual([1, 2, 3]), "每段軌道必須在自己的列，不能疊在一起。");
            Require(table.Children.OfType<Border>().Any(border => ReferenceEquals(border.Background, UiTheme.TableHeaderBrush)
                    && Grid.GetRow(border) == 0 && Grid.GetColumnSpan(border) == 4), "對照表表頭列必須有淺色底。");
            Require(ReferenceEquals(Descendants(migration).OfType<TextBlock>()
                    .Single(text => text.Text.StartsWith("此 Schema 8 舊檔", StringComparison.Ordinal)).Foreground, UiTheme.TextMutedBrush),
                "遷移說明文字必須是灰色。");
        }
        finally { migration.Close(); }

        var progress = (Window)Activator.CreateInstance(AppType("ProjectLoadProgressWindow"))!;
        try
        {
            ShowOffscreen(progress);
            Require(ReferenceEquals(progress.FontFamily, Application.Current.FindResource("AppFont")) && ReferenceEquals(progress.Background, UiTheme.SurfaceBrush),
                "讀檔進度視窗必須套用 App 字型與白底。");
            var bar = Descendants(progress).OfType<ProgressBar>().Single();
            Require(ReferenceEquals(bar.Style, EditorChrome.StyleOf("ThinProgressBar")) && bar.ActualHeight <= 6.5, "讀檔進度條必須是細長樣式。");
            Require(Descendants(progress).OfType<TextBlock>().Any(text => text.Text == "正在讀取存檔，請稍候"
                    && ReferenceEquals(text.Style, EditorChrome.StyleOf("ResultPageTitle"))), "讀檔進度標題必須使用頁面標題樣式。");
            InspectElements(progress, "讀檔進度視窗");
        }
        finally { progress.Close(); }
        Console.WriteLine("[通過] 新增／選取、遷移與讀檔進度對話框套用編輯器樣式");
    }
```

- [ ] **Step 2: 執行，確認失敗**

```powershell
dotnet build .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --editor-theme-only
```

Expected: 以 `找不到 CreateAskDialog。` 失敗。

- [ ] **Step 3: 實作**

以 Bash 執行：

```bash
python - <<'EOF'
from pathlib import Path
def rep(path, old, new, count=1):
    p = Path(path); s = p.read_text(encoding='utf-8')
    n = s.count(old)
    assert n == count, f'{path}: expected {count}, found {n}: {old[:70]!r}'
    p.write_text(s.replace(old, new), encoding='utf-8', newline='')

f = 'src/MrtRouteSimulator.App/TopologyEditorWindow.cs'
rep(f, '''    private Dictionary<string, string>? Ask(string title, params (string Label, string Initial)[] fields)
    {
        var dialog = new Window { Title = title, Owner = this, Width = 500, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        var panel = new StackPanel { Margin = new Thickness(16) };
        var inputs = new Dictionary<string, TextBox>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            panel.Children.Add(new TextBlock { Text = field.Label, Margin = new Thickness(0, 5, 0, 3) });
            var input = new TextBox { Text = field.Initial }; inputs.Add(field.Label, input); panel.Children.Add(input);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(CreateButton("建立", (_, _) => dialog.DialogResult = true, false));
        buttons.Children.Add(CreateButton("取消", (_, _) => dialog.DialogResult = false, true));
        panel.Children.Add(buttons);
        dialog.Content = new ScrollViewer { Content = panel, MaxHeight = 660, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        return dialog.ShowDialog() == true ? inputs.ToDictionary(item => item.Key, item => item.Value.Text, StringComparer.Ordinal) : null;
    }

    private string? Choose(string title, string label, IReadOnlyList<string> values, string? current)
    {
        var dialog = new Window { Title = title, Owner = this, Width = 440, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
        var choices = new ComboBox { ItemsSource = values, SelectedItem = values.FirstOrDefault(value => value.Equals(current, StringComparison.OrdinalIgnoreCase)) ?? values.FirstOrDefault() };
        panel.Children.Add(choices);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(CreateButton("選取", (_, _) => dialog.DialogResult = true, false));
        buttons.Children.Add(CreateButton("取消", (_, _) => dialog.DialogResult = false, true));
        panel.Children.Add(buttons); dialog.Content = panel;
        return dialog.ShowDialog() == true ? choices.SelectedItem as string : null;
    }
''', '''    private Dictionary<string, string>? Ask(string title, params (string Label, string Initial)[] fields)
    {
        var (dialog, inputs) = CreateAskDialog(title, fields);
        return dialog.ShowDialog() == true ? inputs.ToDictionary(item => item.Key, item => item.Value.Text, StringComparer.Ordinal) : null;
    }

    // 建立「新增／編輯」對話框但不顯示，讓測試能直接檢查外觀。結構（ScrollViewer → StackPanel，
    // 文字框為直接子元素、最後一個子元素是按鈕列）是既有自動化測試的依據，不得改變。
    private (Window Dialog, Dictionary<string, TextBox> Inputs) CreateAskDialog(string title, (string Label, string Initial)[] fields)
    {
        var dialog = CreateDialogWindow(title, 500);
        var panel = new StackPanel { Margin = new Thickness(20) };
        var inputs = new Dictionary<string, TextBox>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            var label = EditorChrome.FieldLabel(field.Label);
            label.Margin = new Thickness(0, 8, 0, 4);
            panel.Children.Add(label);
            var input = new TextBox { Text = field.Initial }; inputs.Add(field.Label, input); panel.Children.Add(input);
        }
        panel.Children.Add(DialogButtons(dialog, "建立"));
        dialog.Content = new ScrollViewer { Content = panel, MaxHeight = 660, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        return (dialog, inputs);
    }

    private string? Choose(string title, string label, IReadOnlyList<string> values, string? current)
    {
        var (dialog, choices) = CreateChooseDialog(title, label, values, current);
        return dialog.ShowDialog() == true ? choices.SelectedItem as string : null;
    }

    // 結構（StackPanel 直接含下拉選單、最後一個子元素是按鈕列）是既有自動化測試的依據，不得改變。
    private (Window Dialog, ComboBox Choices) CreateChooseDialog(string title, string label, IReadOnlyList<string> values, string? current)
    {
        var dialog = CreateDialogWindow(title, 440);
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(EditorChrome.FieldLabel(label));
        var choices = new ComboBox { ItemsSource = values, SelectedItem = values.FirstOrDefault(value => value.Equals(current, StringComparison.OrdinalIgnoreCase)) ?? values.FirstOrDefault() };
        panel.Children.Add(choices);
        panel.Children.Add(DialogButtons(dialog, "選取"));
        dialog.Content = panel;
        return (dialog, choices);
    }

    private Window CreateDialogWindow(string title, double width)
    {
        var dialog = new Window { Title = title, Owner = this, Width = width, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        EditorChrome.ApplyWindowChrome(dialog);
        dialog.Background = UiTheme.SurfaceBrush;
        return dialog;
    }

    // 確認鈕（主要）在前、取消（次要）在後，靠右排列。
    private static StackPanel DialogButtons(Window dialog, string confirm)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(CreateButton(confirm, (_, _) => dialog.DialogResult = true, false));
        var cancel = CreateButton("取消", (_, _) => dialog.DialogResult = false, true);
        cancel.Margin = new Thickness(0);
        buttons.Children.Add(cancel);
        return buttons;
    }
''')

m = 'src/MrtRouteSimulator.App/LegacyPortMigrationDialog.cs'
rep(m, '''        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent();''', '''        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        EditorChrome.ApplyWindowChrome(this);
        Background = UiTheme.SurfaceBrush;
        Content = BuildContent();''')
rep(m, '''        var root = new Grid { Margin = new Thickness(18), Background = Brushes.White };''', '''        var root = new Grid { Margin = new Thickness(20) };''')
rep(m, '''            Foreground = new SolidColorBrush(Color.FromRgb(60, 70, 90)),''', '''            Foreground = UiTheme.TextMutedBrush,''')
rep(m, '''        AddCell(table, "軌道區段", 0, 0, true);''', '''        // 每列都要有自己的列定義；原本缺少列定義，所有資料列都疊在第一列。
        for (var row = 0; row <= items.Count; row++) table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var headerBand = new Border
        {
            Background = UiTheme.TableHeaderBrush, BorderBrush = UiTheme.BorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1), CornerRadius = new CornerRadius(6, 6, 0, 0)
        };
        Grid.SetColumnSpan(headerBand, 4);
        table.Children.Add(headerBand);
        AddCell(table, "軌道區段", 0, 0, true);''')
rep(m, '''        var button = new Button
        {
            Content = text,
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(6, 0, 0, 0),
            MinWidth = 118
        };
        if (secondary) button.Background = new SolidColorBrush(Color.FromRgb(238, 242, 248));
        button.Click += handler;
        return button;''', '''        var button = EditorChrome.Button(text, handler, primary: !secondary);
        button.Margin = new Thickness(6, 0, 0, 0);
        button.MinWidth = 118;
        return button;''')

p = 'src/MrtRouteSimulator.App/ProjectLoadProgressWindow.cs'
rep(p, '''        ShowInTaskbar = false;

        _message = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _progressBar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 18, Margin = new Thickness(0, 14, 0, 0) };''',
       '''        ShowInTaskbar = false;
        EditorChrome.ApplyWindowChrome(this);
        Background = UiTheme.SurfaceBrush;

        _message = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = UiTheme.TextMutedBrush, Margin = new Thickness(0, 6, 0, 0) };
        _progressBar = new ProgressBar { Minimum = 0, Maximum = 100, Margin = new Thickness(0, 14, 0, 0) };''')
rep(p, '''                    new TextBlock { Text = "正在讀取存檔，請稍候", FontWeight = FontWeights.SemiBold, FontSize = 16 },''',
       '''                    new TextBlock { Text = "正在讀取存檔，請稍候", Style = EditorChrome.StyleOf("ResultPageTitle") },''')

t = 'tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs'
rep(t, '''        foreach (var (window, file) in windows)
        {
            try
            {
                window.Show();
                Save(window, System.IO.Path.Combine(output, $"{file}.png"));
            }
            finally { window.Close(); }
        }
''', '''        foreach (var (window, file) in windows)
        {
            try
            {
                window.Show();
                Save(window, System.IO.Path.Combine(output, $"{file}.png"));
            }
            finally { window.Close(); }
        }
        var editorType = assembly.GetType("MrtRouteSimulator.App.TopologyEditorWindow")!;
        var editor = (Window)Activator.CreateInstance(editorType, source, Enum.Parse(assembly.GetType("MrtRouteSimulator.App.ProjectWorkspacePage")!, "Project"))!;
        try
        {
            editor.Show();
            var created = editorType.GetMethod("CreateAskDialog", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(editor, ["編輯專案識別資料", new (string, string)[] { ("專案編號", source.ProjectId), ("專案名稱", source.ProjectName) }])!;
            var ask = (Window)created.GetType().GetField("Item1")!.GetValue(created)!;
            try
            {
                ask.Show();
                Save(ask, System.IO.Path.Combine(output, "dialog-ask.png"));
            }
            finally { ask.Close(); }
        }
        finally { editor.Close(); }
''')
EOF
```

- [ ] **Step 4: 執行，確認通過**

```powershell
dotnet build .\MrtRouteSimulator.slnx -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --editor-theme-only
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --workspace-only
```

Expected: 0／0；`[通過] 新增／選取、遷移與讀檔進度對話框套用編輯器樣式`、`[通過] 編輯器程式無寫死顏色（7 個檔案）`、`PASS WPF editor theme`；`PASS WPF topology workspace and input pages`（往返測試仍能操作新增與選取對話框）。

- [ ] **Step 5: 完整 WPF runner**

```powershell
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- .
```

Expected: `PASS WPF visual rules`。

- [ ] **Step 6: Commit**

```powershell
git add src/MrtRouteSimulator.App/TopologyEditorWindow.cs src/MrtRouteSimulator.App/LegacyPortMigrationDialog.cs src/MrtRouteSimulator.App/ProjectLoadProgressWindow.cs tests/MrtRouteSimulator.WpfTests/ShellLayoutTests.cs tests/MrtRouteSimulator.WpfTests/EditorThemeTests.cs
git commit -m "feat: theme editor dialogs and fix migration table rows" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: 完整驗證、截圖與文件

**Files:**
- Modify: `README.md`、`docs/superpowers/specs/2026-10-10-topology-editor-design.md`、`CHANGELOG.md`、`QA_REPORT.md`

- [ ] **Step 1: 完整驗證（互動桌面、不操作視窗）**

```powershell
query user
Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
dotnet build .\MrtRouteSimulator.slnx -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.Tests\MrtRouteSimulator.Tests.csproj -c Release --no-build --no-restore
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- .
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- . --shell-screenshots
```

Expected: session `Active`；0 warning／0 error；Engine 198/198；完整 WPF runner `PASS WPF visual rules` 且含 `PASS WPF editor theme`、`PASS WPF chart theme`、`PASS WPF result pages`、`PASS WPF shell layout`；`artifacts\shell-screenshots` 內 26 張 PNG（多出 `dialog-ask`）。

- [ ] **Step 2: 人工外觀檢查**

用 Read 工具開啟 `artifacts\shell-screenshots\` 的 `editor-*`、`dialog-*` 共 9 張，與 `artifacts\e-before\` 同名圖對照，逐項確認：頁首標題與搜尋框、導覽選取樣式、頁面標題與說明、卡片、分段標籤、清單、表格、主次按鈕（每頁最多一個橘色主要按鈕）、右側兩張卡片與驗證色點、底部按鈕、示意圖軌道顏色、對話框按鈕與表格；沒有文字被切掉或元件重疊。發現問題只改樣式或版面值，先以測試重現再修正，修正後重跑 Step 1 對應的測試。把新舊截圖傳給使用者看。

- [ ] **Step 3: 文件**

1. `README.md`：在「- 版本化 `.mrtsim.json` 專案檔；讀取前會完整驗證，儲存時採同目錄暫存檔後原子取代，避免半成品覆蓋原檔。」之前加入一行：「- 專案工作區（拓樸編輯器）與主視窗採同一套外觀：膠囊式分頁、卡片、主要／次要按鈕、驗證訊息色點（錯誤紅、提醒黃）；路線示意圖的軌道配色與配線圖相同。」
2. E 規格 `2026-10-10-topology-editor-design.md`：
   - 「狀態」改為「已實作（2026-10-10）；實作時的細化見第 10 節」。
   - 文末新增「## 10. 實作細化（2026-10-10）」，內容為本計畫「規格的實作細化」第 1～9 點。
3. `CHANGELOG.md` 的 `## V4.1.0` 下、D 那一節之前新增：

```markdown
### 2026-10-10 介面翻新子專案 E：拓樸編輯器外觀統一

- 專案工作區（拓樸編輯器）與它開出的對話框改用主視窗的色票與元件樣式：導覽選取樣式、頁面標題與說明、白底卡片、膠囊式分頁、主要／次要按鈕、搜尋框提示字、清單選取樣式；驗證清單與底部摘要加色點（錯誤紅、提醒黃、通過綠）。
- 每個畫面只保留一個主要（橘色）按鈕：底部「套用」、對話框確認與會整份取代草稿的起稿動作；其餘改為次要按鈕。
- 路線示意圖改用配線圖的軌道配色與線寬（下行橘、上行藍、側線淡色）。
- 修正舊檔接軌側別遷移對話框的對照表所有資料列疊在第一列的問題。
- 新增 `EditorChrome` 與編輯器具名樣式；編輯器程式不再寫死顏色。設計見 `docs/superpowers/specs/2026-10-10-topology-editor-design.md`。
```

4. `QA_REPORT.md` 第一個 `## ` 之前新增一節，數字一律填 Step 1 的實際輸出：

```markdown
## 介面翻新子專案 E：拓樸編輯器外觀統一（2026-10-10）

- Release build：<實際 warning／error 數>。
- Engine runner：<通過數>/<總數>。
- WPF runner（互動桌面）：<PASS WPF visual rules／失敗訊息>，<[通過] 行數> 項 `[通過]`；新增 `EditorThemeTests`（共用樣式與 EditorChrome、外殼與驗證色點、13 頁與 4 個舊頁巡檢含所有分頁、路線示意圖配色、三種對話框、980×640 與 125% 縮放、寫死顏色掃描 7 個檔案）全部通過；既有工作區往返、車站頁、營運頁與示意圖版面規則測試照常通過。
- 人工截圖檢查（範例 14，編輯器 6 頁與 3 種對話框，1280×800，與改版前對照）：<結果>。
- 未涵蓋：系統訊息框（Windows 繪製）；原生桌面 DPI 與實體滑鼠操作未重跑。
```

- [ ] **Step 4: Commit**

```powershell
git add README.md docs/superpowers/specs/2026-10-10-topology-editor-design.md CHANGELOG.md QA_REPORT.md
git commit -m "docs: record topology editor verification and refresh user docs" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
