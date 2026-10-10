using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Automation;

/// <summary>
/// Regression: large-project PNG at horizontal zoom 1.5, then `load` a small
/// project and export PDF through the named pipe. The window is deliberately
/// not shown, like McpBridgeTests: its diagram ScrollViewer is never measured,
/// which used to let the canvas width compound from its own ActualWidth and
/// made the PDF's two renders differ (CroppedBitmap "值不在預期的範圍內").
/// </summary>
internal static class McpProjectSwitchExportTests
{
    public static void Run(string root)
    {
        var output = Path.Combine(root, "output", "mcp-switch-export-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        // Bridge failures only return exception.Message; surface the full logged exception.
        var listener = new TextWriterTraceListener(Console.Error);
        Trace.Listeners.Add(listener);
        var window = new MainWindow { Width = 1100, Height = 800 };
        try
        {
            window.Measure(new Size(1100, 800));
            window.Arrange(new Rect(0, 0, 1100, 800));
            window.UpdateLayout();
            window.EnableMcpBridge(root);
            var canvas = (Canvas)window.FindName("TimeDistanceCanvas");
            var pngScale = ((CheckBox)window.FindName("HighResolutionCheckBox")).IsChecked == true ? 2 : 1;

            Call("zoom", new { horizontal = 1.5, vertical = 1.25 });
            Call("load", new { path = "samples/14-大型-二十八站完整營運範例.mrtsim.json" });
            WpfTestWait.WaitForPlannedTimeline(window);
            Call("advance", new { targetSeconds = 60 });
            Call("export", new { path = Path.Combine(output, "large.csv"), format = "csv" });
            var png = Path.Combine(output, "large.png");
            Call("export", new { path = png, format = "png" });
            var exportedWidth = canvas.Width;
            var pngWidth = PngPixelWidth(png);
            Require(pngWidth == (int)Math.Ceiling(exportedWidth * pngScale),
                $"PNG 必須以實際畫布寬度輸出，不可是未配置的 1 px：png={pngWidth}，canvas={exportedWidth}。");

            Call("load", new { path = "samples/10-小型-三站完整拓樸基準範例.mrtsim.json" });
            WpfTestWait.WaitForPlannedTimeline(window);
            Call("advance", new { targetSeconds = 10 });
            var pdf = Path.Combine(output, "small.pdf");
            Call("export", new { path = pdf, format = "pdf" });
            Require(canvas.Width == exportedWidth,
                $"運行圖寬度只取決於視窗與縮放，不得沿用前一次輸出累乘：前={exportedWidth}，後={canvas.Width}。");
            var minimumImageHeight = (int)Math.Ceiling(canvas.ActualHeight * 1.6);
            var images = PdfImageSizes(pdf);
            Require(images.Count > 0 && images.All(size => size.Width > 1 && size.Height >= minimumImageHeight),
                $"PDF 頁面影像須為完整運行圖：{string.Join(", ", images)}，最小高度 {minimumImageHeight}。");
            Console.WriteLine($"[通過] MCP named pipe：大型專案 PNG 後切換三站專案仍可匯出 PDF（{images.Count} 頁，畫布 {canvas.Width:0.##}）");
        }
        finally
        {
            WpfTestWait.Close(window);
            Trace.Listeners.Remove(listener);
            listener.Flush();
            // Only remove this test's freshly created output directory.
            Directory.Delete(output, recursive: true);
        }

        JsonElement Call(string command, object arguments)
        {
            var json = JsonSerializer.SerializeToNode(arguments)!.AsObject();
            json["workspaceRoot"] = root;
            var request = new DesktopRequest(command, JsonSerializer.SerializeToElement(json, AutomationJson.Options));
            var task = Task.Run(() => DesktopBridge.CallAsync(Environment.ProcessId, request, CancellationToken.None));
            WpfTestWait.Wait(task);
            return task.GetAwaiter().GetResult();
        }
    }

    private static int PngPixelWidth(string path)
    {
        using var stream = File.OpenRead(path);
        return BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0].PixelWidth;
    }

    private static List<(int Width, int Height)> PdfImageSizes(string path) =>
        Regex.Matches(Encoding.Latin1.GetString(File.ReadAllBytes(path)), @"/Subtype /Image /Width (\d+) /Height (\d+)")
            .Select(match => (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value)))
            .ToList();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
