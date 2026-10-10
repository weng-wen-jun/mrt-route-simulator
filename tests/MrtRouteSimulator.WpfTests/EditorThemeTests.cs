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
        VerifyEditorStyles();
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
