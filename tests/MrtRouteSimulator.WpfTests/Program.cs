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
            VisualRulesTests.Run();
            ProjectLoadTests.Run(System.IO.Path.GetFullPath(args.Length > 0 ? args[0] : "."));
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
