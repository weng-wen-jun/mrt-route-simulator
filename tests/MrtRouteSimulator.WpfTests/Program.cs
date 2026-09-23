using MrtRouteSimulator.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var app = new App();
        app.InitializeComponent();
        try
        {
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
            Console.WriteLine("開始 WPF 專案載入測試");
            ProjectLoadTests.Run(System.IO.Path.GetFullPath(args.Length > 0 ? args[0] : "."));
            Console.WriteLine("開始 WPF 播放 worker 測試");
            PlaybackWorkerTests.Run(System.IO.Path.GetFullPath(args.Length > 0 ? args[0] : "."));
            Console.WriteLine("開始 WPF 長行程／結果測試");
            SpeedJourneyTests.Run(System.IO.Path.GetFullPath(args.Length > 0 ? args[0] : "."));
            Console.WriteLine("開始 WPF 輸出測試");
            OutputTests.Run(System.IO.Path.GetFullPath(args.Length > 0 ? args[0] : "."));
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
