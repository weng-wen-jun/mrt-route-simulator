using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

internal static class InputWorkspaceSnapshots
{
    public static void Run(string root, string outputDirectory)
    {
        var sample = TopologyProjectFormat.Deserialize(File.ReadAllText(Path.Combine(
            root, "samples", "14-大型-二十八站完整營運範例.mrtsim.json")));
        var editorType = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.TopologyEditorWindow")!;
        var pageType = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.ProjectWorkspacePage")!;
        Directory.CreateDirectory(outputDirectory);

        foreach (var (width, height) in new[] { (1360, 860), (980, 640) })
        foreach (var (page, label) in new[]
        {
            ("Stations", "stations-o20"), ("StopPatterns", "stop-patterns"),
            ("DispatchPlanning", "dispatch"), ("Simulation", "simulation")
        })
        {
            var window = (Window)Activator.CreateInstance(editorType, sample, Enum.Parse(pageType, page))!;
            window.Width = width;
            window.Height = height;
            window.Left = -10000;
            window.Top = -10000;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.ShowInTaskbar = false;
            window.Show();
            try
            {
                window.UpdateLayout();
                if (page == "Stations")
                {
                    var list = (ListBox)editorType.GetField("stationPageStationList", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
                    list.SelectedItem = ((IEnumerable)list.ItemsSource).Cast<object>()
                        .Single(item => (string)item.GetType().GetProperty("Id")!.GetValue(item)! == "O20");
                }
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                window.UpdateLayout();
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                var path = Path.Combine(outputDirectory, $"{label}-{width}x{height}.png");
                using (var output = File.Create(path)) encoder.Save(output);
                Console.WriteLine($"inputSnapshot={path}");
            }
            finally { window.Close(); }
        }
    }
}
