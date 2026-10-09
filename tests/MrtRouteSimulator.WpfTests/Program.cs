using MrtRouteSimulator.App;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

internal static class Program
{
    private static string GetRoot(string[] args)
    {
        var root = args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal));
        return System.IO.Path.GetFullPath(root ?? ".");
    }

    private static void RunInputPageTests(string root)
    {
        Console.WriteLine("開始 WPF 工作區與模擬設定跨頁測試");
        ShellInputTests.Run(root);
        Console.WriteLine("開始 WPF 車站／軌道輸入頁測試");
        StationInputPageTests.Run(root);
        Console.WriteLine("開始 WPF 營運輸入頁測試");
        OperationInputPageTests.Run(root);
    }

    private static void RunValidationWarningTests()
    {
        var window = new MainWindow();
        try
        {
            var showValidation = typeof(MainWindow).GetMethod(
                "ShowValidation", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("找不到 ShowValidation 測試入口。");
            var border = (Border?)window.FindName("ValidationBorder")
                ?? throw new InvalidOperationException("找不到 ValidationBorder。");
            var text = (TextBlock?)window.FindName("ValidationTextBlock")
                ?? throw new InvalidOperationException("找不到 ValidationTextBlock。");
            var button = (Button?)window.FindName("HideValidationButton")
                ?? throw new InvalidOperationException("找不到關閉警告按鈕。");

            showValidation.Invoke(window, [new[] { "第一則警告", "第二則警告" }]);
            Require(border.Visibility == Visibility.Visible, "警告列顯示時必須可見。");
            Require(text.Text.Contains("第一則警告", StringComparison.Ordinal)
                && text.Text.Contains("第二則警告", StringComparison.Ordinal),
                "警告列必須保留多行訊息。");
            Require(text.TextWrapping == TextWrapping.Wrap, "多行警告文字必須維持換行。");
            Require(AutomationProperties.GetName(button) == "關閉警告", "關閉按鈕 automation name 必須是關閉警告。");

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(border.Visibility == Visibility.Collapsed, "點擊關閉警告後警告列必須收合。");
            Require(text.Text.Length == 0, "點擊關閉警告後文字必須清空。");

            showValidation.Invoke(window, [new[] { "新的警告", "第二個新的警告" }]);
            Require(border.Visibility == Visibility.Visible
                && text.Text.Contains("新的警告", StringComparison.Ordinal)
                && text.Text.Contains("第二個新的警告", StringComparison.Ordinal),
                "新的驗證警告必須能再次顯示。");
            Require(text.TextWrapping == TextWrapping.Wrap, "再次顯示的多行警告文字必須維持換行。");
            Console.WriteLine("[通過] ValidationBorder 關閉警告可收合、清空並再次顯示");
        }
        finally
        {
            WpfTestWait.Close(window);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        var app = new App();
        app.InitializeComponent();
        // The test harness owns application shutdown, not its first temporary window.
        app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        try
        {
            if (args.Contains("--mcp-only"))
            {
                McpBridgeTests.Run(GetRoot(args));
                Console.WriteLine("PASS WPF MCP bridge");
                return 0;
            }
            if (args.Contains("--native-retention-only"))
            {
                NativeRetentionTests.Run(GetRoot(args));
                Console.WriteLine("PASS WPF native retention reachability (test-only GC, no Show)");
                return 0;
            }

            if (args.Contains("--validation-warning-only"))
            {
                McpBridgeTests.Run(GetRoot(args));
            RunValidationWarningTests();
                Console.WriteLine("PASS WPF validation warning dismissal");
                return 0;
            }
            if (args.Contains("--track-theme-only"))
            {
                TrackDiagramThemeTests.Run(GetRoot(args));
                return 0;
            }
            if (args.Contains("--visual-rules-only"))
            {
                // 全範例配線圖版面規則；不含需要互動桌面的像素匯出測試。
                VisualRulesTests.Run();
                TrackDiagramThemeTests.Run(GetRoot(args));
                Console.WriteLine("PASS WPF visual rules only");
                return 0;
            }
            if (args.Contains("--synchronous-export-only"))
            {
                SynchronousDiagramExportTests.Run(GetRoot(args));
                Console.WriteLine("PASS WPF synchronous diagram export");
                return 0;
            }
            if (args.Contains("--pdf-pagination-only"))
            {
                PdfPaginationRegressionTests.Run(GetRoot(args));
                Console.WriteLine("PASS WPF PDF pagination");
                return 0;
            }
            PlaybackProfileTests.VerifyDiagnosticsContract();
            PlaybackProfileTests.VerifyWorkspaceSelectionEventBoundary();
            if (args.Contains("--interface-scale-only"))
            {
                InterfaceScalePreferenceTests.Run();
                InterfaceScaleTests.Run(GetRoot(args));
                return 0;
            }
            if (args.Contains("--diagram-compact-only"))
            {
                DiagramCompactLayoutTests.Run(GetRoot(args));
                return 0;
            }
            if (args.Contains("--outer-shell-only"))
            {
                OuterShellScrollTests.Run(GetRoot(args));
                Console.WriteLine("PASS WPF outer shell scroll");
                return 0;
            }
            if (args.Contains("--large-output-only"))
            {
                var root = args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal)) ?? ".";
                OutputTests.RunLarge(System.IO.Path.GetFullPath(root));
                Console.WriteLine("PASS WPF large outputs");
                return 0;
            }
            if (args.Contains("--workspace-only"))
            {
                var root = GetRoot(args);
                TopologyWorkspaceRoundTripTests.Run(root);
                RunInputPageTests(root);
                Console.WriteLine("PASS WPF topology workspace and input pages");
                return 0;
            }
            if (args.Contains("--input-pages-only"))
            {
                RunInputPageTests(GetRoot(args));
                Console.WriteLine("PASS WPF input pages");
                return 0;
            }
            if (args.Contains("--operations-input-only"))
            {
                OperationInputPageTests.Run(GetRoot(args));
                Console.WriteLine("PASS WPF operations input page");
                return 0;
            }
            if (args.Contains("--stations-input-only"))
            {
                StationInputPageTests.Run(GetRoot(args));
                Console.WriteLine("PASS WPF stations input page");
                return 0;
            }
            if (args.Contains("--input-snapshots"))
            {
                var outputIndex = Array.IndexOf(args, "--input-snapshots") + 1;
                if (outputIndex >= args.Length) throw new ArgumentException("請在 --input-snapshots 後指定輸出目錄。");
                InputWorkspaceSnapshots.Run(GetRoot(args), System.IO.Path.GetFullPath(args[outputIndex]));
                Console.WriteLine("PASS WPF input snapshots");
                return 0;
            }
            if (args.Contains("--native-final-only"))
            {
                var root = args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal)) ?? ".";
                DpiConfigurationTests.Run(System.IO.Path.GetFullPath(root));
                TimeDistanceStationLabelTests.Run(System.IO.Path.GetFullPath(root));
                TimeDistanceResizeTests.Run(System.IO.Path.GetFullPath(root));
                CompactRouteLayoutTests.Run(System.IO.Path.GetFullPath(root));
                OuterShellScrollTests.Run(System.IO.Path.GetFullPath(root));
                NativeAcceptanceInputTests.Run(System.IO.Path.GetFullPath(root));
                NativeAcceptanceTests.Run(System.IO.Path.GetFullPath(root));
                Console.WriteLine("PASS WPF final acceptance preparation (not native desktop acceptance)");
                return 0;
            }
            if (args.Contains("--native-acceptance-only"))
            {
                var root = args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal)) ?? ".";
                NativeAcceptanceTests.Run(System.IO.Path.GetFullPath(root));
                Console.WriteLine("PASS WPF native acceptance measurement");
                return 0;
            }
            if (args.Contains("--pacing-lazy-only") || args.Contains("--diagram-lazy-only"))
            {
                var root = args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal)) ?? ".";
                if (args.Contains("--pacing-lazy-only")) PlaybackPacingTests.Run(System.IO.Path.GetFullPath(root));
                NestedPlaybackTabTests.Run(System.IO.Path.GetFullPath(root));
                TimeDistanceCacheTests.Run();
                TimeDistanceVisualTests.Run(System.IO.Path.GetFullPath(root));
                TimeDistanceStickyAxisTests.Run(System.IO.Path.GetFullPath(root));
                Console.WriteLine("PASS WPF pacing/lazy/incremental");
                return 0;
            }
            if (args.Contains("--diagram-preparation-only"))
            {
                TimeDistancePreparationTests.Run(GetRoot(args));
                return 0;
            }
            if (args.Contains("--profile-full-timedistance-only"))
            {
                var sample = args.First(argument => !argument.StartsWith("--", StringComparison.Ordinal));
                TimeDistanceVisualTests.RunFullProfile(System.IO.Path.GetFullPath(sample));
                return 0;
            }
            if (args.Contains("--profile-interactive-timedistance-only"))
            {
                var sample = args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal))
                    ?? throw new ArgumentException("請提供 Schema 8 大型樣本路徑。");
                InteractiveTimeDistanceProfileTests.Run(System.IO.Path.GetFullPath(sample));
                return 0;
            }
            if (args.Contains("--large-playback-only"))
            {
                var sample = args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal))
                    ?? throw new ArgumentException("請提供 Schema 8 大型樣本路徑。");
                LargePlaybackDiagnostics.Run(System.IO.Path.GetFullPath(sample));
                Console.WriteLine("PASS WPF large playback diagnostics");
                return 0;
            }
            if (args.Contains("--profile-playback-only"))
            {
                var sample = args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal))
                    ?? throw new ArgumentException("請提供 Schema 8 大型樣本路徑。");
                var profile = args.FirstOrDefault(argument => argument.StartsWith("--profile-tab=", StringComparison.Ordinal))
                    ?.Substring("--profile-tab=".Length);
                PlaybackProfileTests.Run(System.IO.Path.GetFullPath(sample), profile);
                Console.WriteLine("PASS WPF playback profile diagnostics");
                return 0;
            }
            if (args.Contains("--output-only"))
            {
                var root = args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal)) ?? ".";
                OutputTests.Run(System.IO.Path.GetFullPath(root));
                Console.WriteLine("PASS WPF outputs");
                return 0;
            }
            if (args.Contains("--speed-only"))
            {
                var root = args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal)) ?? ".";
                SpeedJourneyTests.Run(System.IO.Path.GetFullPath(root));
                Console.WriteLine("PASS WPF speed journey");
                return 0;
            }
            if (args.Contains("--playback-only"))
            {
                var root = args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal)) ?? ".";
                PlaybackWorkerTests.Run(System.IO.Path.GetFullPath(root));
                Console.WriteLine("PASS WPF playback worker");
                return 0;
            }
            McpBridgeTests.Run(GetRoot(args));
            RunValidationWarningTests();
            SynchronousDiagramExportTests.Run(GetRoot(args));
            PdfPaginationRegressionTests.Run(GetRoot(args));
            Console.WriteLine("開始 WPF 視覺規則測試");
            InterfaceScalePreferenceTests.Run();
            InterfaceScaleTests.Run(GetRoot(args));
            var regressionRoot = System.IO.Path.GetFullPath(args.Length > 0 ? args[0] : ".");
            PlaybackPacingTests.Run(regressionRoot);
            NestedPlaybackTabTests.Run(regressionRoot);
            TimeDistanceCacheTests.Run();
            TimeDistanceStationLabelTests.Run(regressionRoot);
            TimeDistanceResizeTests.Run(regressionRoot);
            CompactRouteLayoutTests.Run(regressionRoot);
            OuterShellScrollTests.Run(regressionRoot);
            TimeDistanceVisualTests.Run(regressionRoot);
            TimeDistanceStickyAxisTests.Run(regressionRoot);
            DiagramCompactLayoutTests.Run(regressionRoot);
            TimeDistancePreparationTests.Run(regressionRoot);
            VisualRulesTests.Run();
            TrackDiagramThemeTests.Run(GetRoot(args));
            var projectRoot = GetRoot(args);
            Console.WriteLine("開始 WPF 專案載入測試");
            ProjectLoadTests.Run(projectRoot);
            Console.WriteLine("開始 WPF 大型拓撲工作區往返測試");
            TopologyWorkspaceRoundTripTests.Run(projectRoot);
            RunInputPageTests(projectRoot);
            Console.WriteLine("開始 WPF 播放 worker 測試");
            PlaybackWorkerTests.Run(projectRoot);
            Console.WriteLine("開始 WPF 原生量測入口測試");
            DpiConfigurationTests.Run(regressionRoot);
            NativeAcceptanceTests.Run(projectRoot);
            NativeAcceptanceInputTests.Run(regressionRoot);
            Console.WriteLine("開始 WPF 長行程／結果測試");
            SpeedJourneyTests.Run(projectRoot);
            Console.WriteLine("開始 WPF 輸出測試");
            OutputTests.Run(projectRoot);
            Console.WriteLine("PASS WPF visual rules");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally { app.Shutdown(); }
    }
}
