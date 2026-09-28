using MrtRouteSimulator.App;

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

    [STAThread]
    private static int Main(string[] args)
    {
        var app = new App();
        app.InitializeComponent();
        try
        {
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
            if (args.Contains("--large-playback-only"))
            {
                var sample = args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal))
                    ?? throw new ArgumentException("請提供 Schema 8 大型樣本路徑。");
                LargePlaybackDiagnostics.Run(System.IO.Path.GetFullPath(sample));
                Console.WriteLine("PASS WPF large playback diagnostics");
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
            Console.WriteLine("開始 WPF 視覺規則測試");
            VisualRulesTests.Run();
            var projectRoot = GetRoot(args);
            Console.WriteLine("開始 WPF 專案載入測試");
            ProjectLoadTests.Run(projectRoot);
            Console.WriteLine("開始 WPF 大型拓撲工作區往返測試");
            TopologyWorkspaceRoundTripTests.Run(projectRoot);
            RunInputPageTests(projectRoot);
            Console.WriteLine("開始 WPF 播放 worker 測試");
            PlaybackWorkerTests.Run(projectRoot);
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
