using System.Reflection;
using System.IO;
using Path = System.IO.Path;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using MrtRouteSimulator.App;

internal static class SynchronousDiagramExportTests
{
    public static void Run(string root)
    {
        var output = Path.Combine(root, "tmp", "synchronous-diagram-export");
        Directory.CreateDirectory(output);

        var exportType = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.DiagramExportService")!
            ?? throw new InvalidOperationException("找不到 DiagramExportService。");
        var exportPng = exportType.GetMethod("ExportPng")
            ?? throw new InvalidOperationException("找不到同步 PNG 匯出入口。");
        var exportPdf = exportType.GetMethod("ExportPdf")
            ?? throw new InvalidOperationException("找不到同步 PDF 匯出入口。");
        var render = exportType.GetMethod(
            "Render",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(FrameworkElement), typeof(double), typeof(bool)],
            modifiers: null)
            ?? throw new InvalidOperationException("找不到含文字開關的 Render 入口。");
        var pageSize = exportType.Assembly.GetType("MrtRouteSimulator.App.PdfPageSize")!
            ?? throw new InvalidOperationException("找不到 PDF 頁面尺寸列舉。");
        var a4 = Enum.Parse(pageSize, "A4");
        var createPdfDialog = typeof(MainWindow).GetMethod(
            "CreatePdfSaveFileDialog",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: [pageSize, typeof(string)],
            modifiers: null)
            ?? throw new InvalidOperationException("找不到 PDF 儲存對話框建立入口。");

        foreach (var (pageSizeName, expectedTitle) in new[]
        {
            ("A4", "匯出列車運行圖 PDF（A4 橫向）"),
            ("A3", "匯出列車運行圖 PDF（A3 橫向）")
        })
        {
            var dialog = (SaveFileDialog)createPdfDialog.Invoke(
                null, [Enum.Parse(pageSize, pageSizeName), "測試路線"])!;
            Require(dialog.Title == expectedTitle,
                $"PDF {pageSizeName} 對話框標題必須反映實際紙張：{dialog.Title}");
            Require(dialog.Filter == "PDF 文件 (*.pdf)|*.pdf",
                $"PDF {pageSizeName} 對話框 filter 不得改變既有 contract。");
            // SaveFileDialog normalizes the assigned ".pdf" extension when read back.
            Require(dialog.DefaultExt == "pdf" && dialog.AddExtension && dialog.OverwritePrompt,
                $"PDF {pageSizeName} 對話框副檔名／覆寫 contract 不得改變：DefaultExt='{dialog.DefaultExt}', AddExtension={dialog.AddExtension}, OverwritePrompt={dialog.OverwritePrompt}。");
            Require(dialog.FileName == "測試路線_列車運行圖.pdf",
                $"PDF {pageSizeName} 對話框預設檔名不符既有 contract。");
        }

        foreach (var (scale, label) in new[] { (1d, "normal"), (2d, "high-resolution") })
        {
            var pngFixture = CreateFixture();
            var png = Path.Combine(output, $"{label}.png");
            var pdf = Path.Combine(output, $"{label}.pdf");

            // Intentionally do not call UpdateLayout after clearing and rebuilding the
            // already-arranged canvas. This is the native export regression boundary.
            exportPng.Invoke(null, [pngFixture.Canvas, png, scale]);
            var pngFrame = DecodePng(png);
            VerifyBitmap(pngFrame, (int)Math.Ceiling(pngFixture.Canvas.Width * scale),
                (int)Math.Ceiling(pngFixture.Canvas.Height * scale), $"PNG {label}", expectText: true);

            var pdfFixture = CreateFixture();
            exportPdf.Invoke(null, [pdfFixture.Canvas, pdf, a4, false]);
            var pdfBytes = File.ReadAllBytes(pdf);
            Require(pdfBytes.Length > 100
                && Encoding.ASCII.GetString(pdfBytes, 0, 8) == "%PDF-1.4"
                && Encoding.ASCII.GetString(pdfBytes).Contains("/Subtype /Image", StringComparison.Ordinal),
                $"PDF {label} 匯出必須包含有效圖片物件。");

            var pdfRenderFixture = CreateFixture();
            var pdfBitmap = (RenderTargetBitmap)render.Invoke(null,
                [pdfRenderFixture.Canvas, 1.6d, true])!;
            VerifyBitmap(pdfBitmap, (int)Math.Ceiling(pdfRenderFixture.Canvas.Width * 1.6),
                (int)Math.Ceiling(pdfRenderFixture.Canvas.Height * 1.6), $"Render PDF {label}", expectText: true);

            var hiddenFixture = CreateFixture();
            var beforeVisibility = hiddenFixture.Text.Visibility;
            var hiddenTextBitmap = (RenderTargetBitmap)render.Invoke(null, [hiddenFixture.Canvas, scale, false])!;
            Require(hiddenFixture.Text.Visibility == beforeVisibility,
                $"Render(includeText:false) 後必須恢復文字 visibility：{label}。");
            VerifyBitmap(hiddenTextBitmap, (int)Math.Ceiling(hiddenFixture.Canvas.Width * scale),
                (int)Math.Ceiling(hiddenFixture.Canvas.Height * scale), $"Render hidden text {label}", expectText: false);
        }

        Console.WriteLine("[通過] no-show 同步 PNG／PDF 匯出保留重建後紅色圖形與尺寸");
        VerifyPngTrainLabels(exportType, exportPng, output);
    }

    private static void VerifyPngTrainLabels(Type exportType, MethodInfo exportPng, string output)
    {
        var placeTrain = exportType.GetMethod("PlacePdfTrainLabel", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到共用匯出文字配置。");
        var render = exportType.GetMethod("Render", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var scale in new[] { 1d, 2d })
        {
            var canvas = new Canvas { Width = 480, Height = 260, Background = Brushes.White };
            var labels = new List<TextBlock>();
            var lines = new List<Polyline>();
            foreach (var (name, color) in new[] { ("FULL-O13", Brushes.Blue), ("FULL-O04", Brushes.Magenta) })
            {
                var line = new Polyline { Stroke = Brushes.Gray, StrokeThickness = 1,
                    Points = new PointCollection { new Point(100, 210), new Point(300, 100) } };
                var label = new TextBlock { Text = name, FontSize = 9, Foreground = color };
                Canvas.SetLeft(label, 103);
                Canvas.SetTop(label, 195);
                canvas.Children.Add(line);
                canvas.Children.Add(label);
                labels.Add(label);
                lines.Add(line);
            }
            canvas.Measure(new Size(480, 260));
            canvas.Arrange(new Rect(0, 0, 480, 260));
            canvas.UpdateLayout();
            var occupied = new List<Rect>();
            var bounds = new Rect(86, 50, 390, 166);
            var placements = labels.Select(label => (Rect)placeTrain.Invoke(null,
                [new Rect(103, 195, label.ActualWidth, label.ActualHeight), bounds, occupied])!).ToArray();
            Require(!placements[0].IntersectsWith(placements[1]),
                "同起點PNG車次標籤配置必須分列。");
            var rawBitmap = (BitmapSource)render.Invoke(null, [canvas, scale, true])!;
            var rawBgra = new FormatConvertedBitmap(rawBitmap, PixelFormats.Bgra32, null, 0);
            Require(CountLabelPixels(rawBgra, placements[1], scale, magenta: true) <= 10,
                "fixture須證明舊直接Render在第二文字列缺字，否則不是有效的重疊回歸。");
            var path = Path.Combine(output, $"train-labels-scale-{scale:0}.png");
            exportPng.Invoke(null, [canvas, path, scale]);
            var bitmap = DecodePng(path);
            Require(bitmap.PixelWidth == (int)(480 * scale) && bitmap.PixelHeight == (int)(260 * scale),
                "不需溢出annotation列時，PNG尺寸與解析度不得改變。");
            Require(CountLabelPixels(bitmap, placements[0], scale, magenta: false) > 10
                && CountLabelPixels(bitmap, placements[1], scale, magenta: true) > 10,
                $"PNG {scale}x 必須在分開的文字列保留兩個車次名，不能只測檔案存在。");
            Require(labels.All(label => Canvas.GetLeft(label) == 103 && Canvas.GetTop(label) == 195
                    && label.Visibility == Visibility.Visible)
                && lines.All(line => line.Points.SequenceEqual(new[] { new Point(100, 210), new Point(300, 100) })),
                "PNG文字overlay不得改變來源標籤座標／visibility或軌跡點。");
        }
        Console.WriteLine("[通過] PNG 1x／2x 同起點車次標籤分列、像素保留與來源不變");
    }

    private static int CountLabelPixels(BitmapSource bitmap, Rect bounds, double scale, bool magenta)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        var count = 0;
        for (var y = Math.Max(0, (int)Math.Floor(bounds.Top * scale));
             y < Math.Min(bitmap.PixelHeight, (int)Math.Ceiling(bounds.Bottom * scale)); y++)
        for (var x = Math.Max(0, (int)Math.Floor(bounds.Left * scale));
             x < Math.Min(bitmap.PixelWidth, (int)Math.Ceiling(bounds.Right * scale)); x++)
        {
            var offset = y * stride + x * 4;
            if (pixels[offset] > 160 && pixels[offset + 1] < 120
                && (magenta ? pixels[offset + 2] > 160 : pixels[offset + 2] < 120)) count++;
        }
        return count;
    }

    private static (Canvas Canvas, TextBlock Text) CreateFixture()
    {
        var host = new Canvas { Width = 720, Height = 420 };
        var canvas = new Canvas { Width = 480, Height = 260, Background = Brushes.White };
        Canvas.SetLeft(canvas, 37);
        Canvas.SetTop(canvas, 23);
        host.Children.Add(canvas);
        host.Measure(new Size(host.Width, host.Height));
        host.Arrange(new Rect(0, 0, host.Width, host.Height));

        canvas.Children.Clear();
        var rectangle = new Rectangle { Width = 132, Height = 76, Fill = Brushes.Red };
        Canvas.SetLeft(rectangle, 86);
        Canvas.SetTop(rectangle, 78);
        var text = new TextBlock { Text = "同步匯出", Foreground = Brushes.Black, FontSize = 18 };
        Canvas.SetLeft(text, 24);
        Canvas.SetTop(text, 18);
        canvas.Children.Add(rectangle);
        canvas.Children.Add(text);
        return (canvas, text);
    }

    private static BitmapSource DecodePng(string path)
    {
        using var stream = File.OpenRead(path);
        var frame = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad)
            .Frames.Single();
        return new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
    }

    private static void VerifyBitmap(
        BitmapSource bitmap,
        int expectedWidth,
        int expectedHeight,
        string label,
        bool expectText)
    {
        Require(bitmap.PixelWidth == expectedWidth && bitmap.PixelHeight == expectedHeight,
            $"{label} 尺寸不符：實際={bitmap.PixelWidth}x{bitmap.PixelHeight}，預期={expectedWidth}x{expectedHeight}。");

        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        var redPixels = 0;
        var nonWhitePixels = 0;
        var darkPixels = 0;
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            var blue = pixels[offset];
            var green = pixels[offset + 1];
            var red = pixels[offset + 2];
            var alpha = pixels[offset + 3];
            if (alpha > 0 && red > 180 && green < 100 && blue < 100)
                redPixels++;
            if (alpha > 0 && (red < 248 || green < 248 || blue < 248))
                nonWhitePixels++;
            if (alpha > 0 && red < 96 && green < 96 && blue < 96)
                darkPixels++;
        }

        Require(redPixels > 100, $"{label} 必須包含可辨識的紅色 Rectangle 像素。");
        Require(nonWhitePixels > 0, $"{label} 必須包含非白像素。");
        if (expectText)
            Require(darkPixels > 0, $"{label} 必須包含 TextBlock 深色像素。");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
