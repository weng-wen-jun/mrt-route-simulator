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
        VerifyAppBarInputClassification();
        VerifyDrawerKeepsValidationVisible();
        VerifyDataGridCellSelectionVisible();
        VerifyDisabledAppBarTooltips();
        VerifyMenuStyles();
        VerifyComboFocusFrame();
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
        foreach (var (width, height, scale) in new[] { (800d, 520d, 1d), (800d, 520d, 1.25d), (1280d, 800d, 1.25d) })
        {
            var previousScale = SetInterfaceScale(scale);
            var window = new MainWindow { Width = width, Height = height };
            try
            {
                window.Show();
                PumpLayout(window);
                ((TextBlock)window.FindName("CurrentProjectFileTextBlock")!).Text =
                    "目前存檔：" + string.Concat(Enumerable.Repeat("很長很長的專案檔名", 12)) + ".mrtsim.json";
                // 播放中的時鐘格式比初始的 --:--:-- 寬，以最寬狀態量測。
                ((TextBlock)window.FindName("SimulationClockText")!).Text = "00:05:00.0";
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
                // 以外層捲動區的可視寬度為界（直向捲軸出現時不含捲軸）。
                var viewport = ((ScrollViewer)window.FindName("ShellScrollViewer")!).ViewportWidth;
                Require(bar.Right <= viewport + .5, $"{width}×{height}@{scale:0.##}：播放列不可超出可視寬度；bar={bar}, viewport={viewport:0.0}。");
            }
            finally
            {
                WpfTestWait.Close(window);
                SetInterfaceScale(previousScale);
            }
        }
        Console.WriteLine("[通過] 標題列在 800 DIP（100%／125%）與 1280 DIP（125%）下不重疊、不超出");
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

    // 原生驗收以按鈕文字分類點擊；標題列改為圖示鈕後必須改讀自動化名稱。
    private static void VerifyAppBarInputClassification()
    {
        var window = new MainWindow { Width = 1280, Height = 800 };
        try
        {
            window.Show();
            PumpLayout(window);
            var bar = (Panel)window.FindName("PlaybackControlBar")!;
            string Kind(string automationName)
            {
                var button = bar.Children.OfType<Button>().Single(item => AutomationProperties.GetName(item) == automationName);
                var result = WpfTestWait.Invoke(window, "NativeAcceptanceClassifyInputSource", button)!;
                return (string)result.GetType().GetField("Item1")!.GetValue(result)!;
            }
            Require(Kind("▶ 播放") is "play" or "resume", $"播放鈕應分類為 play，實際 {Kind("▶ 播放")}。");
            Require(Kind("Ⅱ 暫停") == "pause", $"暫停鈕應分類為 pause，實際 {Kind("Ⅱ 暫停")}。");
            Require(Kind("↺ 重設") == "reset", $"重設鈕應分類為 reset，實際 {Kind("↺ 重設")}。");
            Require(Kind("建立模擬") == "otherClick", $"建立模擬應分類為 otherClick，實際 {Kind("建立模擬")}。");
        }
        finally { WpfTestWait.Close(window); }
        Console.WriteLine("[通過] 標題列圖示鈕的原生驗收點擊分類沿用自動化名稱");
    }

    // 在抽屜內操作產生的驗證訊息（例如未選車站就按刪除）不得被抽屜蓋住。
    private static void VerifyDrawerKeepsValidationVisible()
    {
        var previousScale = SetInterfaceScale(1);
        var window = new MainWindow { Width = 1280, Height = 800 };
        try
        {
            window.Show();
            PumpLayout(window);
            ((Button)window.FindName("QuickBuilderToggleButton")!).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            WpfTestWait.Invoke(window, "ShowValidation", (object)new[] { "請先選取要刪除的車站。" });
            PumpLayout(window);
            var content = (FrameworkElement)window.Content;
            Rect Bounds(string name)
            {
                var element = (FrameworkElement)window.FindName(name)!;
                return new Rect(element.TranslatePoint(new Point(0, 0), content), new Size(element.ActualWidth, element.ActualHeight));
            }
            var drawer = Bounds("QuickBuilderSidebar");
            var message = Bounds("ValidationTextBlock");
            Require(((FrameworkElement)window.FindName("QuickBuilderSidebar")!).IsVisible, "抽屜應保持開啟。");
            Require(!drawer.IntersectsWith(message), $"抽屜開啟時驗證訊息不得被抽屜蓋住；drawer={drawer}, message={message}。");
            Require(drawer.Height >= 300, $"抽屜仍需保有可操作高度，實際 {drawer.Height:0.0}。");
            WpfTestWait.Invoke(window, "HideValidation");
            PumpLayout(window);
            Require(Bounds("QuickBuilderSidebar").Top < drawer.Top, "關閉警告後抽屜應回到頁首下緣。");
        }
        finally
        {
            WpfTestWait.Close(window);
            SetInterfaceScale(previousScale);
        }
        Console.WriteLine("[通過] 抽屜從頁首下緣開始，驗證訊息不被抽屜蓋住");
    }

    // 儲存格選取模式（例如停站模式矩陣）必須看得出選了哪一格，鍵盤焦點格要有框。
    private static void VerifyDataGridCellSelectionVisible()
    {
        var grid = new DataGrid { SelectionUnit = DataGridSelectionUnit.Cell, Width = 300, Height = 120,
            ItemsSource = new[] { new { A = "1", B = "2" }, new { A = "3", B = "4" } } };
        grid.Columns.Add(new DataGridTextColumn { Header = "A", Binding = new System.Windows.Data.Binding("A") });
        grid.Columns.Add(new DataGridTextColumn { Header = "B", Binding = new System.Windows.Data.Binding("B") });
        var window = new Window { Width = 400, Height = 240, Content = grid };
        try
        {
            window.Show();
            window.Activate();
            PumpLayout(window);
            var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
            var presenter = FindDescendant<DataGridCellsPresenter>(row)!;
            var cell = (DataGridCell)presenter.ItemContainerGenerator.ContainerFromIndex(1);
            cell.IsSelected = true;
            cell.Focus();
            PumpLayout(window);
            Require(ReferenceEquals(cell.Background, UiTheme.NavSelectedBrush),
                $"選取的儲存格必須以 NavSelected 底色標示，實際 {cell.Background}。");
            Require(cell.IsKeyboardFocusWithin && ReferenceEquals(cell.BorderBrush, UiTheme.AccentBrush) && cell.BorderThickness.Left >= 1,
                $"鍵盤焦點所在的儲存格必須有 Accent 框；focus={cell.IsKeyboardFocusWithin}, border={cell.BorderBrush}, thickness={cell.BorderThickness}。");
        }
        finally { window.Close(); }
        Console.WriteLine("[通過] 儲存格選取模式的 DataGrid 標示選取格與焦點格");
    }

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
                button.IsEnabled = false; // 新視窗的急停鈕可能已啟用；固定前提為「停用中」。
                Require(!button.IsEnabled && ToolTipService.GetShowOnDisabled(button) && button.ToolTip is not null,
                    $"{name} 停用時也必須顯示提示框；enabled={button.IsEnabled}, showOnDisabled={ToolTipService.GetShowOnDisabled(button)}, toolTip={button.ToolTip}。");
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
}
