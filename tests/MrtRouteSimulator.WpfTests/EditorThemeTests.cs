using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
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
        VerifyEditorStyles();
        VerifyShell(root);
        VerifyInitialValidationSummary(root);
        VerifyPages(root);
        VerifySchematic(root);
        VerifyDialogs(root);
        VerifyMinimumSizeAndScale(root);
        VerifyStationPageFillsWorkspace(root);
        VerifyWorkspaceReachable(root);
        VerifyLongProjectName(root);
        VerifyEditorListsShowOptions(root);
        VerifyHintsAndSearchLabels(root);
        VerifyNoHardCodedEditorColors(root);
        Console.WriteLine("PASS WPF editor theme");
    }

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
    private static readonly string[] ScannedFiles =
    [
        "EditorChrome.cs", "TopologyEditorWindow.cs", "TopologyEditorWindow.Stations.cs", "TopologyEditorWindow.Operations.cs",
        "TopologyEditorWindow.Settings.cs", "LegacyPortMigrationDialog.cs", "ProjectLoadProgressWindow.cs"
    ];

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

    // 開啟時就驗證的頁面（總覽）：底部摘要文字與色點必須一致，不能文字寫未驗證、色點卻是結果色。
    private static void VerifyInitialValidationSummary(string root)
    {
        foreach (var page in new[] { "Project", "Tracks" })
        {
            var window = OpenEditor(root, page);
            try
            {
                var text = ((TextBlock)Field(window, "validationSummary")!).Text;
                var dot = (Ellipse)Field(window, "validationSummaryDot")!;
                var unvalidated = text.StartsWith("尚未驗證", StringComparison.Ordinal);
                Require(unvalidated == ReferenceEquals(dot.Fill, UiTheme.TextSubtleBrush),
                    $"「{page}」開啟時底部摘要「{text}」與色點顏色不一致。");
            }
            finally { window.Close(); }
        }
        Console.WriteLine("[通過] 開啟編輯器時底部驗證摘要文字與色點一致");
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

    // 回歸測試：車站與月台頁的清單與詳細區要填滿頁首以下的工作區，改變視窗大小後高度也要穩定。
    private static void VerifyStationPageFillsWorkspace(string root)
    {
        var window = OpenEditor(root, "Project");
        try
        {
            Navigate(window, "Stations");
            double Measure(string phase)
            {
                PumpDispatcher(window.Dispatcher);
                window.UpdateLayout();
                var workspace = (ContentControl)Field(window, "workspace")!;
                var header = (FrameworkElement)((StackPanel)((ScrollViewer)workspace.Content).Content).Children[0];
                var body = (FrameworkElement)Named(window, "StationDetailScrollViewer").Parent;
                var expected = workspace.ActualHeight - header.ActualHeight - header.Margin.Bottom - 40;
                Require(body.ActualHeight >= expected,
                    $"{phase}：車站與月台頁內容高 {body.ActualHeight:0}，應填滿工作區（至少 {expected:0}）。");
                return body.ActualHeight;
            }

            var first = Measure("從總覽切換到車站與月台");
            window.Height = 700;
            Measure("視窗縮小到 1280×700");
            window.Height = 800;
            var again = Measure("視窗回到 1280×800");
            Require(Math.Abs(first - again) < 1, $"視窗大小還原後車站頁高度由 {first:0} 變成 {again:0}，必須穩定。");
        }
        finally { window.Close(); }
        Console.WriteLine("[通過] 車站與月台頁填滿工作區且改變視窗大小後高度穩定");
    }

    // 回歸測試：980×640 與 125% 縮放下，每頁的按鈕、輸入框、下拉選單、表格與清單都看得到，或能捲動到。
    private static void VerifyWorkspaceReachable(string root)
    {
        foreach (var scale in new[] { 1d, 1.25d })
        {
            var previous = ShellLayoutTests.SetInterfaceScale(scale);
            var window = OpenEditor(root, "Project", 980, 640);
            try
            {
                var workspace = (ContentControl)Field(window, "workspace")!;
                foreach (var page in NavigationPages)
                {
                    Navigate(window, page);
                    PumpDispatcher(window.Dispatcher);
                    window.UpdateLayout();
                    var controls = Descendants(workspace).OfType<Control>()
                        .Where(control => control.TemplatedParent is null && control.IsVisible
                                          && control is Button or TextBox or ComboBox or CheckBox or DataGrid or ListBox)
                        .ToArray();
                    foreach (var control in controls)
                    {
                        Rect Bounds() => control.TransformToAncestor(workspace).TransformBounds(new Rect(control.RenderSize));
                        if (Bounds().Bottom <= workspace.ActualHeight + 0.5) continue;
                        control.BringIntoView();
                        window.UpdateLayout();
                        var rect = Bounds();
                        Require(rect.Top >= -0.5 && rect.Top < workspace.ActualHeight - 8,
                            $"{scale:P0}、980×640 的「{page}」頁，「{(control as ContentControl)?.Content ?? AutomationProperties.GetName(control)}」（{control.GetType().Name}）在工作區下方且捲動不到。");
                    }
                }
            }
            finally
            {
                window.Close();
                ShellLayoutTests.SetInterfaceScale(previous);
            }
        }
        Console.WriteLine("[通過] 980×640 與 125% 縮放下各頁控制項都看得到或捲動得到");
    }

    // 回歸測試：專案名稱過長時以「…」截斷並提示全名；格式版本、頁名與草稿狀態完整顯示並緊接在名稱後。
    private static void VerifyLongProjectName(string root)
    {
        var longName = string.Concat(Enumerable.Repeat("超長專案名稱示範", 12));
        foreach (var (name, width) in new[] { (longName, 980d), ("短名", 1280d) })
        {
            var window = (Window)Activator.CreateInstance(EditorType, Sample(root) with { ProjectName = name }, Enum.Parse(PageType, "Stations"))!;
            window.Width = width;
            window.Height = 640;
            ShowOffscreen(window);
            try
            {
                Require(EditorType.GetField("workspaceSummaryDetail", BindingFlags.NonPublic | BindingFlags.Instance) is not null
                        && ((TextBlock)Field(window, "workspaceSummary")!).Text == name,
                    "頁首必須把專案名稱與格式版本、頁名、草稿狀態分開顯示。");
                var title = (TextBlock)Field(window, "workspaceSummary")!;
                var detail = (TextBlock)Field(window, "workspaceSummaryDetail")!;
                var header = Named(window, "EditorHeader");
                Rect Bounds(FrameworkElement element) => element.TransformToAncestor(header).TransformBounds(new Rect(element.RenderSize));
                var titleRect = Bounds(title);
                var detailRect = Bounds(detail);
                var searchRect = Bounds((FrameworkElement)Field(window, "workspaceSearch")!);
                Require(title.TextTrimming == TextTrimming.CharacterEllipsis && Equals(title.ToolTip, name),
                    "頁首專案名稱過長時必須以「…」截斷，並以工具提示顯示全名。");
                Require(detail.Text.Contains("格式版本") && detail.Text.Contains("車站與月台") && detail.Text.Contains("草稿未套用")
                        && detail.ActualWidth >= NaturalWidth(detail) - 0.5 && detailRect.Right <= searchRect.Left + 0.5,
                    $"「{name[..2]}…」：頁首的格式版本、頁名與草稿狀態必須完整顯示。");
                Require(detailRect.Left >= titleRect.Right - 0.5 && detailRect.Left - titleRect.Right < 2,
                    $"「{name[..2]}…」：格式版本等資訊必須緊接在專案名稱後面。");
                if (name == longName)
                    Require(title.ActualWidth < NaturalWidth(title) - 1, "長專案名稱必須被截斷而不是撐開頁首。");
            }
            finally { window.Close(); }
        }
        Console.WriteLine("[通過] 頁首長專案名稱以「…」截斷並提示全名，其餘資訊完整顯示");
    }

    private static double NaturalWidth(TextBlock text)
    {
        var probe = new TextBlock { Text = text.Text, FontFamily = text.FontFamily, FontSize = text.FontSize, FontWeight = text.FontWeight };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return probe.DesiredSize.Width;
    }

    // 回歸測試：多選參照清單在 5 個以內的選項不需捲動就看得到每一項及選取底色；作業清單至少看得到 4 列。
    private static void VerifyEditorListsShowOptions(string root)
    {
        var editor = OpenEditor(root, "Stations");
        var host = new Window { Width = 420, Height = 760, FontSize = editor.FontSize };
        EditorChrome.ApplyWindowChrome(host);
        var panel = new StackPanel();
        host.Content = panel;
        try
        {
            var references = new List<ListBox>();
            for (var count = 1; count <= 5; count++)
            {
                var options = Enumerable.Range(1, count).Select(index => ($"P{index}", $"P{index} · 第 {index} 月台")).ToArray();
                var list = (ListBox)EditorType.GetMethod("ReferenceMultiSelect", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(editor, [$"Reference{count}", $"參照 {count}", (IEnumerable<(string Id, string Label)>)options,
                        string.Join(", ", options.Select(option => option.Item1)), (Action<string[]>)(_ => { })])!;
                panel.Children.Add(list);
                references.Add(list);
            }

            var longOption = new[] { ("PLATFORM:LONG", "PLATFORM:LONG · 這是一個非常長的月台名稱，用來確認清單在窄視窗下仍可左右捲動看到完整文字") };
            var longList = (ListBox)EditorType.GetMethod("ReferenceMultiSelect", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(editor, ["ReferenceLong", "長名稱參照", (IEnumerable<(string Id, string Label)>)longOption, "", (Action<string[]>)(_ => { })])!;
            panel.Children.Add(longList);

            var operations = CollectionViewSource.GetDefaultView(Enumerable.Range(1, 6).Select(index => new { Id = $"OP{index}" }).ToList());
            var operationList = (ListBox)EditorType.GetMethod("CreateOperationList", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(editor, [operations, "OperationList", "作業清單", (Action<object?>)(_ => { })])!;
            panel.Children.Add(operationList);
            ShowOffscreen(host);
            PumpDispatcher(host.Dispatcher);

            foreach (var list in references)
            {
                var viewer = Descendants(list).OfType<ScrollViewer>().First();
                Require(viewer.ExtentHeight <= viewer.ViewportHeight + 0.5,
                    $"{list.Items.Count} 個選項的參照清單需要捲動（捲動範圍 {viewer.ExtentHeight:0.#}、可視 {viewer.ViewportHeight:0.#}）。");
                foreach (var item in list.Items)
                {
                    var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(item);
                    var chrome = (Border)container.Template.FindName("Chrome", container);
                    Require(ReferenceEquals(chrome.Background, UiTheme.NavSelectedBrush),
                        $"{list.Items.Count} 個選項的參照清單中，已選項目沒有顯示選取底色。");
                }
            }

            Require(Descendants(longList).OfType<ScrollViewer>().First().ScrollableWidth > 0,
                "參照清單的長名稱被截斷且無法左右捲動。");
            var validationList = (ListBox)Field(editor, "validationList")!;
            Require(ScrollViewer.GetHorizontalScrollBarVisibility(validationList) == ScrollBarVisibility.Disabled,
                "驗證清單必須停用左右捲動，長訊息才會換行。");

            // 清單以項目為捲動單位，改量捲動內容區的像素高度。
            var rows = Descendants(operationList).OfType<ListBoxItem>().First().ActualHeight;
            var visible = Descendants(operationList).OfType<ScrollContentPresenter>().First().ActualHeight;
            Require(visible >= 4 * rows - 0.5, $"作業清單只看得到 {visible / rows:0.0} 列，至少要 4 列。");
        }
        finally
        {
            host.Close();
            editor.Close();
        }
        Console.WriteLine("[通過] 參照清單不需捲動即可看到所有選項，作業清單至少 4 列");
    }

    // 提示文字用次要色；車站搜尋框只用框內提示字，不再重複一個同名標籤。
    private static void VerifyHintsAndSearchLabels(string root)
    {
        var window = OpenEditor(root, "Stations");
        try
        {
            foreach (var builder in new[] { "BuildTurnbackOperationDetail", "BuildPassingOperationDetail" })
            {
                var detail = (Panel)EditorType.GetMethod(builder, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [null])!;
                var hint = detail.Children.OfType<TextBlock>().Single();
                Require(ReferenceEquals(hint.Foreground, UiTheme.TextMutedBrush), $"「{hint.Text}」是提示文字，必須用次要色。");
            }

            Require(!Descendants(window).OfType<TextBlock>().Any(text => text.TemplatedParent is null && text.Text == "搜尋車站"),
                "車站搜尋框已有框內提示字，不應再加同名標籤。");

            Navigate(window, "QuickBuilder");
            var description = Descendants(window).OfType<TextBlock>().Single(text => text.Text.StartsWith("選擇站型後重新建立", StringComparison.Ordinal));
            Require(ReferenceEquals(description.Foreground, UiTheme.TextMutedBrush), "「依參考圖建立站場」的說明是提示文字，必須用次要色。");
        }
        finally { window.Close(); }
        Console.WriteLine("[通過] 提示文字使用次要色，車站搜尋不重複標籤");
    }

    private static void PumpDispatcher(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

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
                    Require(group.ReadLocalValue(Control.PaddingProperty) == DependencyProperty.UnsetValue,
                        $"{context}：群組框「{group.Header}」不得自訂內距，須與其他群組框一致。");
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
            var bufferStops = canvas.Children.OfType<Line>().Where(line => line.ToolTip is string tip && tip.EndsWith("· 止衝", StringComparison.Ordinal)).ToArray();
            Require(bufferStops.Length > 0 && bufferStops.All(line => ReferenceEquals(line.Stroke, UiTheme.RailNeutralStrongBrush)
                    && line.StrokeThickness == 4 && line.StrokeStartLineCap == PenLineCap.Round && line.StrokeEndLineCap == PenLineCap.Round),
                "止衝擋線必須和配線圖一樣用深灰色、線寬 4、圓角端點。");
        }
        finally { window.Close(); }
        Console.WriteLine("[通過] 路線示意圖使用配線圖的軌道配色與線寬");
    }

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
