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
    // 改版前（c103ab3 + main 同步後）在 1280×800、100% 介面縮放下量得的配線圖可視高度。
    private const double BaselineRouteViewportHeight = 163.8;

    public static void Run(string root)
    {
        VerifyShellTokens();
        VerifyImplicitStyles();
        VerifyShellControls();
        VerifyShellSkeleton(root);
        VerifyQuickBuilderDrawer();
        VerifyStatusIndicator(root);
        VerifyAppBarFits();
        VerifyValidationBanner(root);
        Console.WriteLine("PASS WPF shell layout");
    }

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

    private static void VerifyImplicitStyles()
    {
        var resources = Application.Current.Resources;
        foreach (var type in new[] { typeof(Button), typeof(TextBox), typeof(ComboBox), typeof(ComboBoxItem), typeof(ScrollBar),
                     typeof(Menu), typeof(MenuItem), typeof(ContextMenu), typeof(ToolTip), typeof(Expander), typeof(DataGrid), typeof(DataGridRow) })
            Require(AppDefines(type), $"缺少 {type.Name} 的隱含樣式。");
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

    // 只認 App 自己的資源字典；TryFindResource(typeof(X)) 會退回 WPF 系統佈景樣式，無法證明樣式存在。
    private static bool AppDefines(object key)
    {
        var resources = Application.Current.Resources;
        return resources.Contains(key) || resources.MergedDictionaries.Any(dictionary => dictionary.Contains(key));
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
}
