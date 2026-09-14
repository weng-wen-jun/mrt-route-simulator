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
                OutputTests.Run(System.IO.Path.GetFullPath(args[0]));
                Console.WriteLine("PASS WPF outputs");
                return 0;
            }
            if (args.Contains("--speed-only"))
            {
                SpeedJourneyTests.Run(System.IO.Path.GetFullPath(args[0]));
                Console.WriteLine("PASS WPF speed journey");
                return 0;
            }
            VisualRulesTests.Run();
            ProjectLoadTests.Run(System.IO.Path.GetFullPath(args.Length > 0 ? args[0] : "."));
            SpeedJourneyTests.Run(System.IO.Path.GetFullPath(args.Length > 0 ? args[0] : "."));
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
