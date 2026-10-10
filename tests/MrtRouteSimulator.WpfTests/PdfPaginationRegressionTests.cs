using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MrtRouteSimulator.App;

/// <summary>
/// Regression fixture for the A4 split-page time-distance export.
///
/// The fixture deliberately uses the native sample-14 output dimensions
/// (1406 x 380 DIP at the 1.6 PDF render scale) and places one station label
/// below the plot bottom.  It is intentionally kept separate from production
/// code so the pagination contract can be called from the existing WPF runner.
/// </summary>
internal static class PdfPaginationRegressionTests
{
    public static void Run(string root)
    {
        var exportType = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.DiagramExportService")
            ?? throw new InvalidOperationException("找不到 DiagramExportService。");
        var render = exportType.GetMethod(
            "Render",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(FrameworkElement), typeof(double), typeof(bool)],
            modifiers: null)
            ?? throw new InvalidOperationException("找不到含文字開關的 Render 入口。");
        var createPages = exportType.GetMethod(
            "CreatePdfPages",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types:
            [
                typeof(RenderTargetBitmap), typeof(int), typeof(int), typeof(bool),
                typeof(FrameworkElement), typeof(double)
            ],
            modifiers: null)
            ?? throw new InvalidOperationException("找不到 PDF 分頁入口。");

        var fixture = CreateFixture();
        var bitmap = (RenderTargetBitmap)render.Invoke(null, [fixture, 1.6d, true])!;
        var pages = (IReadOnlyList<BitmapSource>)createPages.Invoke(
            null,
            [bitmap, 794, 547, true, fixture, 1.6d])!;

        // The old 92% step (735 capacity / 676 step) creates starts at
        // 131, 807, 1483, 2159 and leaves a 91-pixel fourth page.  A valid
        // pagination algorithm may overlap page slices, but it must derive the
        // count from the covered span and never emit this sliver.
        Require(pages.Count == 3,
            $"A4 split must produce 3 usable pages for 1406x380 DIP; actual={pages.Count}。");
        Require(pages.All(page => page.PixelWidth >= 600),
            "A4 split must not emit a narrow tail page; every page must retain a usable graph slice。");

        // O03 is intentionally below the 338-DIP plot bottom while remaining on
        // the left station axis.  It must survive page-local text reconstruction
        // on every page, just like O04 above the plot-bottom boundary.
        foreach (var (page, index) in pages.Select((page, index) => (page, index + 1)))
        {
            Require(HasDarkPixels(page, 0, Math.Min(page.PixelWidth, 145), 555, page.PixelHeight),
                $"第 {index} 頁必須保留 plotBottom 以下的左側站名標籤。");
            Require(HasRedPixels(page, 90, 130, 540, 590),
                $"第 {index} 頁必須保留移位站名到原始里程刻度的引導線。");
        }

        // The first tick on page 2 is deliberately close to the page boundary.
        // Check both halves of the label so a crop at the left edge cannot pass
        // merely because a few glyph pixels remain visible.
        var boundaryTickPages = pages.Where(page =>
            HasDarkPixels(page, 145, 205, 545, page.PixelHeight)
            && HasDarkPixels(page, 205, 300, 545, page.PixelHeight)).ToArray();
        Require(boundaryTickPages.Length > 0,
            "跨頁邊界的時間刻度必須在某一頁完整保留，不能只留下半個標籤。");

        Require(HasBluePixels(pages[0], 147, pages[0].PixelWidth, 545, pages[0].PixelHeight),
            "首個時間刻度必須保留在第一頁圖表時間軸內。");
        Require(!HasBluePixels(pages[0], 0, 147, 545, pages[0].PixelHeight),
            "首個時間刻度不能被誤畫到站名軸上。");
        Require(pages.Skip(1).All(page => !HasBluePixels(page, 0, page.PixelWidth, 545, page.PixelHeight)),
            "首個時間刻度不能誤當左軸站名而重複在後續頁。");
        Require(HasMagentaPixels(pages[0], 147, pages[0].PixelWidth, 576, pages[0].PixelHeight),
            "頁界內縮造成碰撞時，完整鄰近刻度必須保留在新增文字列。");
        Require(pages[0].PixelHeight > bitmap.PixelHeight,
            "新增刻度文字列時，頁面高度必須擴充而非裁掉文字。");
        var legend = fixture.Children.OfType<StackPanel>().Single(panel => Equals(panel.Tag, ChartPainter.LegendTag));
        Require(82 + legend.ActualWidth > pages.Min(page => page.PixelWidth) / 1.6,
            $"fixture 的圖例寬 {legend.ActualWidth:0} DIP 必須超過分頁寬，才能驗證等比縮小。");
        foreach (var (page, index) in pages.Select((page, index) => (page, index + 1)))
        {
            var legendTop = (int)(28 * 1.6);
            var legendBottom = (int)Math.Ceiling((28 + legend.ActualHeight) * 1.6);
            Require(FindColorBounds(page, 0, page.PixelWidth, legendTop, legendBottom,
                    (r, g, b) => r is >= 100 and <= 160 && g <= 40 && b is >= 100 and <= 160) is not null,
                $"第 {index} 頁必須補畫圖例線段。");
            Require(FindColorBounds(page, 0, page.PixelWidth, legendTop, legendBottom,
                    (r, g, b) => r >= 220 && g is >= 180 and <= 230 && b <= 60) is not null,
                $"第 {index} 頁的圖例比頁面寬時必須等比縮小，最後一項不能被切掉。");
        }
        Require(!HasCyanPixels(pages[0], 147, 500, 48, 75),
            "靠近圖表頂端的車次文字必須移離圖例區域。");
        Require(HasCyanPixels(pages[0], 147, pages[0].PixelWidth, 80, 300)
            && pages.Skip(1).All(page => !HasCyanPixels(page, 0, page.PixelWidth, 0, page.PixelHeight)),
            "車次文字以原折線首點歸屬單一頁，不得因文字中心越界而在下一頁孤立重複。");
        Require(HasTrainColorPixels(pages[0], 147, 500, 95, 150, false),
            "第二個同起點車次名必須獨立保留，不能只因第一個名字存在而通過。");
        Require(HasTrainColorPixels(pages[0], 680, pages[0].PixelWidth, 195, 217, true)
            && pages.Skip(1).All(page => !HasTrainColorPixels(page, 0, page.PixelWidth, 0, page.PixelHeight, true)),
            "頁界車次名本身必須完整保留在owner page，不能僅保留折線而漏字或跨頁重複。");
        var placeTrain = exportType.GetMethod("PlacePdfTrainLabel", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到PDF車次文字防碰撞配置。");
        var trainOccupied = new List<Rect> { new Rect(82, 28, 400, 14) };
        var trainBounds = new Rect(96, 48, 450, 290);
        var trainFirst = (Rect)placeTrain.Invoke(null, [new Rect(100, 33, 100, 12), trainBounds, trainOccupied])!;
        var trainSecond = (Rect)placeTrain.Invoke(null, [new Rect(100, 33, 100, 12), trainBounds, trainOccupied])!;
        Require(trainBounds.Contains(trainFirst) && trainBounds.Contains(trainSecond)
            && !trainFirst.IntersectsWith(trainSecond) && trainFirst.Y >= 48,
            "車次文字必須避開圖例、同起點文字及頁界，原軌跡不移動。");
        var denseOccupied = new List<Rect> { trainBounds };
        var overflowTrain = (Rect)placeTrain.Invoke(null, [new Rect(100, 33, 100, 12), trainBounds, denseOccupied])!;
        Require(overflowTrain.Top > trainBounds.Bottom && !overflowTrain.IntersectsWith(trainBounds),
            "極密車次名需延伸annotation列，不能裁掉或覆盖已有文字。");

        var placeLabel = exportType.GetMethod("PlacePdfTimeLabel", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到頁內時間刻度防重疊配置。");
        var occupied = new List<Rect>();
        var first = (Rect)placeLabel.Invoke(null, [96d, 346d, 70d, 12d, occupied])!;
        var near = (Rect)placeLabel.Invoke(null, [130d, 346d, 70d, 12d, occupied])!;
        var separated = (Rect)placeLabel.Invoke(null, [220d, 346d, 70d, 12d, occupied])!;
        Require(!first.IntersectsWith(near) && near.Y > first.Y,
            "被頁界內縮的相鄰刻度必須另起一列，不能互相覆蓋。");
        Require(separated.Y == first.Y, "沒有碰撞的刻度不得任意移列。");
        var narrowGapOccupied = new List<Rect>();
        var gapFirst = (Rect)placeLabel.Invoke(null, [96d, 346d, 70d, 12d, narrowGapOccupied])!;
        var gapNext = (Rect)placeLabel.Invoke(null, [168d, 346d, 70d, 12d, narrowGapOccupied])!;
        Require(gapNext.Y > gapFirst.Y,
            "相鄰文字框雖不重疊，但間距小於4DIP仍須分列保留可讀空隙。");

        var drawText = exportType.GetMethod("DrawElementSnapshot", BindingFlags.Static | BindingFlags.NonPublic)!;
        var textCanvas = new Canvas { Width = 300, Height = 30, Background = Brushes.White };
        var paddedText = AddText(textCanvas, "00:00:00.0", 20, 4, 9);
        paddedText.Width = 160;
        paddedText.Foreground = Brushes.Blue;
        textCanvas.Measure(new Size(300, 30));
        textCanvas.Arrange(new Rect(0, 0, 300, 30));
        var original = (RenderTargetBitmap)render.Invoke(null, [textCanvas, 1d, true])!;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
            drawText.Invoke(null, [context, paddedText, 0d, 0d, 160d, paddedText.ActualHeight]);
        var reconstructed = new RenderTargetBitmap(300, 30, 96, 96, PixelFormats.Pbgra32);
        reconstructed.Render(visual);
        Require(Math.Abs(BlueInkWidth(original) - BlueInkWidth(reconstructed)) <= 2,
            "頁內重建不得把字形拉伸成整個留白文字框寬度。");

        // Keep the existing no-text graph contract in this same fixture: text
        // reconstruction must not be caused by a text-bearing graph bitmap.
        var graphOnly = (RenderTargetBitmap)render.Invoke(null, [fixture, 1.6d, false])!;
        Require(!HasBlackPixels(graphOnly, 0, graphOnly.PixelWidth, 0, graphOnly.PixelHeight),
            "Render(includeText:false) 不得把標題／站名／刻度混入 graph bitmap。");
        Require(!HasBluePixels(graphOnly, 0, graphOnly.PixelWidth, 0, graphOnly.PixelHeight),
            "Render(includeText:false) 不得把首個時間刻度混入 graph bitmap。");
        Require(!HasMagentaPixels(graphOnly, 0, graphOnly.PixelWidth, 0, graphOnly.PixelHeight),
            "Render(includeText:false) 不得把鄰近時間刻度混入 graph bitmap。");

        RunSinglePageTrainOverlay(exportType, render, createPages);
        RunSinglePageCaptionOverlay(exportType, render, createPages);

        Console.WriteLine("[通過] A4 PDF pagination preserves station leaders, complete ticks and unstretched text without narrow tail pages");
    }

    private static void RunSinglePageTrainOverlay(Type exportType, MethodInfo render, MethodInfo createPages)
    {
        var fixture = CreateFixture();
        var bitmap = (RenderTargetBitmap)render.Invoke(null, [fixture, 1.6d, true])!;
        var trainBlocks = fixture.Children.OfType<TextBlock>()
            .Where(block => block.Text is "TRAIN-A" or "TRAIN-B" or "BOUNDARY-TRAIN")
            .ToArray();

        foreach (var splitPages in new[] { false, true })
        {
            // A wide available page exercises the split=true aspect early return;
            // split=false exercises the explicit single-page path.
            var pages = (IReadOnlyList<BitmapSource>)createPages.Invoke(
                null,
                [bitmap, 2200, 547, splitPages, fixture, 1.6d])!;
            Require(pages.Count == 1,
                $"單頁 train overlay ({splitPages}) 不得意外產生分頁。");
            Require(!HasCyanPixels(pages[0], 147, 500, 48, 75)
                && HasCyanPixels(pages[0], 147, 500, 80, 100),
                $"單頁 train overlay ({splitPages}) 必須把頂部 Cyan 車次名移離圖例。");
            Require(HasTrainColorPixels(pages[0], 147, 500, 100, 135, false),
                $"單頁 train overlay ({splitPages}) 必須保留第二個 Lime 車次名的避讓列。");
            Require(HasTrainColorPixels(pages[0], 820, pages[0].PixelWidth, 180, 220, true),
                $"單頁 train overlay ({splitPages}) 必須保留 boundary Orange 車次名。");
            Require(trainBlocks.All(block => block.Visibility == Visibility.Visible),
                $"單頁 train overlay ({splitPages}) 結束後必須還原 source Visibility。");
        }

        var nonChart = new Canvas { Width = 300, Height = 100, Background = Brushes.White };
        nonChart.Children.Add(new Line
        {
            X1 = 20, X2 = 280, Y1 = 20, Y2 = 80,
            Stroke = Brushes.DarkGreen, StrokeThickness = 1
        });
        AddText(nonChart, "非運行圖文字", 12, 8, 10);
        nonChart.Measure(new Size(300, 100));
        nonChart.Arrange(new Rect(0, 0, 300, 100));
        var nonChartBitmap = (RenderTargetBitmap)render.Invoke(null, [nonChart, 1.6d, true])!;
        var nonChartPages = (IReadOnlyList<BitmapSource>)createPages.Invoke(
            null,
            [nonChartBitmap, 2200, 547, true, nonChart, 1.6d])!;
        Require(nonChartPages.Count == 1 && ReferenceEquals(nonChartPages[0], nonChartBitmap),
            "沒有 train labels 的非運行圖 canvas 必須保留原始單頁 bitmap。");
    }

    private static void RunSinglePageCaptionOverlay(Type exportType, MethodInfo render, MethodInfo createPages)
    {
        var collisionFixture = CreateCaptionFixture(dense: true);
        var collisionBitmap = (RenderTargetBitmap)render.Invoke(null, [collisionFixture, 1.6d, true])!;
        var collisionCaption = RequireColorBounds(
            FindColorBounds(collisionBitmap, 0, collisionBitmap.PixelWidth, 0, collisionBitmap.PixelHeight,
                static (r, g, b) => r > 180 && g < 80 && b < 80),
            "fixture 必須包含原始紅色時間 caption。 ");
        var collisionTick = RequireColorBounds(
            FindColorBounds(collisionBitmap, 0, collisionBitmap.PixelWidth, 0, collisionBitmap.PixelHeight,
                static (r, g, b) => r < 80 && g < 80 && b > 180),
            "fixture 必須包含原始終點時間刻度。 ");
        var collisionBlocks = collisionFixture.Children.OfType<TextBlock>()
            .Where(block => block.Text is "TERMINAL-A" or "TERMINAL-B" or "時間")
            .ToArray();

        foreach (var splitPages in new[] { false, true })
        {
            var pages = (IReadOnlyList<BitmapSource>)createPages.Invoke(
                null,
                [collisionBitmap, 2200, 547, splitPages, collisionFixture, 1.6d])!;
            Require(pages.Count == 1,
                $"單頁 caption collision ({splitPages}) 不得意外產生分頁。 ");
            var page = pages[0];
            var finalCaption = RequireColorBounds(
                FindColorBounds(page, 0, page.PixelWidth, 0, page.PixelHeight,
                    static (r, g, b) => r > 180 && g < 80 && b < 80),
                $"單頁 caption collision ({splitPages}) 必須保留時間 caption。 ");
            var finalTrainA = RequireColorBounds(
                FindColorBounds(page, 0, page.PixelWidth, 0, page.PixelHeight,
                    static (r, g, b) => r > 180 && g > 70 && g < 180 && b < 80 && g - b > 50),
                $"單頁 caption collision ({splitPages}) 必須保留終點車次文字。 ");
            var finalTrainB = RequireColorBounds(
                FindColorBounds(page, 0, page.PixelWidth, 0, page.PixelHeight,
                    static (r, g, b) => r < 80 && g > 180 && b < 80),
                $"單頁 caption collision ({splitPages}) 必須保留密集終點車次文字。 ");
            var finalTick = RequireColorBounds(
                FindColorBounds(page, 0, page.PixelWidth, 0, page.PixelHeight,
                    static (r, g, b) => r < 80 && g < 80 && b > 180),
                $"單頁 caption collision ({splitPages}) 必須保留終點時間刻度。 ");
            Require(finalCaption.Top > collisionCaption.Top + 8,
                $"單頁 caption collision ({splitPages}) 發生碰撞時 caption 必須向下分列。 ");
            Require(!finalCaption.IntersectsWith(finalTrainA)
                && !finalCaption.IntersectsWith(finalTrainB)
                && !finalCaption.IntersectsWith(finalTick),
                $"單頁 caption collision ({splitPages}) caption 不得覆蓋終點車次／刻度。 ");
            Require(Math.Abs(finalTick.Top - collisionTick.Top) <= 1
                && Math.Abs(finalTick.Left - collisionTick.Left) <= 1,
                $"單頁 caption collision ({splitPages}) 終點時間刻度不得被移動。 ");
            Require(page.PixelHeight > collisionBitmap.PixelHeight,
                $"單頁 caption collision ({splitPages}) 密集 annotation 必須擴高輸出白底。 ");
            Require(collisionBlocks.All(block => block.Visibility == Visibility.Visible),
                $"單頁 caption collision ({splitPages}) 結束後必須還原 source Visibility。 ");
        }

        var safeFixture = CreateCaptionFixture(dense: false);
        var safeBitmap = (RenderTargetBitmap)render.Invoke(null, [safeFixture, 1.6d, true])!;
        var safeCaption = RequireColorBounds(
            FindColorBounds(safeBitmap, 0, safeBitmap.PixelWidth, 0, safeBitmap.PixelHeight,
                static (r, g, b) => r > 180 && g < 80 && b < 80),
            "noncollision fixture 必須包含原始紅色時間 caption。 ");
        var safePages = (IReadOnlyList<BitmapSource>)createPages.Invoke(
            null,
            [safeBitmap, 2200, 547, false, safeFixture, 1.6d])!;
        var safeFinalCaption = RequireColorBounds(
            FindColorBounds(safePages[0], 0, safePages[0].PixelWidth, 0, safePages[0].PixelHeight,
                static (r, g, b) => r > 180 && g < 80 && b < 80),
            "noncollision 單頁必須保留時間 caption。 ");
        Require(Math.Abs(safeFinalCaption.Top - safeCaption.Top) <= 1,
            "沒有碰撞時單頁時間 caption 必須保留原始 Y 座標。 ");
    }

    private static Canvas CreateCaptionFixture(bool dense)
    {
        const double width = 700;
        const double height = 180;
        var canvas = new Canvas { Width = width, Height = height, Background = Brushes.White };
        canvas.Children.Add(new Line
        {
            X1 = 82, X2 = width - 22, Y1 = 48, Y2 = height - 42,
            Stroke = Brushes.LightGray, StrokeThickness = 1
        });
        AddTrainLabel(canvas, dense ? 540 : 200, 135, "TERMINAL-A", Brushes.DarkOrange);
        if (dense)
            AddTrainLabel(canvas, 540, 135, "TERMINAL-B", Brushes.LimeGreen);
        var caption = AddText(canvas, "時間", width - 48, 120, 10);
        caption.Foreground = Brushes.Red;
        if (dense)
        {
            var annotation = AddText(canvas, "dense annotation", 86, 50, 9);
            annotation.Width = 600;
            annotation.Height = 88;
            annotation.Foreground = Brushes.Gray;
            var terminalTick = AddText(canvas, "01:59:32.5", 600, 120, 9);
            terminalTick.Width = 100;
            terminalTick.Foreground = Brushes.Blue;
        }

        canvas.Measure(new Size(width, height));
        canvas.Arrange(new Rect(0, 0, width, height));
        return canvas;

        void AddTrainLabel(Canvas target, double x, double y, string text, Brush color)
        {
            target.Children.Add(new Polyline
            {
                Points = [new Point(x, y), new Point(x + 1, y + 1)],
                Stroke = Brushes.DarkGreen, StrokeThickness = 1
            });
            var label = AddText(target, text, x + 3, y - 15, 9);
            label.Foreground = color;
            label.Width = 120;
        }
    }

    private static Canvas CreateFixture()
    {
        const double width = 1406;
        const double height = 380;
        var canvas = new Canvas { Width = width, Height = height, Background = Brushes.White };

        // Non-text graph content makes the hidden-text assertion meaningful.
        canvas.Children.Add(new Line
        {
            X1 = 82,
            X2 = width - 22,
            Y1 = 48,
            Y2 = height - 42,
            Stroke = Brushes.LightGray,
            StrokeThickness = 1
        });
        canvas.Children.Add(new Polyline
        {
            Stroke = Brushes.DarkGreen,
            StrokeThickness = 2,
            Points = [new Point(82, 338), new Point(420, 250), new Point(width - 22, 48)]
        });

        AddText(canvas, "14-大型-二十八站完整營運範例｜計畫／理論與 V2 模擬實際運行圖", 82, 8, 14);
        // 與運行圖相同的圖例元件；項目多到比 A4 分頁寬，驗證每頁補畫且等比縮小而不被切掉。
        ChartPainter.DrawLegend(canvas, 82, 28,
        [
            new ChartLegendItem("V2 實際", Brushes.Purple, ChartLegendMarker.Line),
            new ChartLegendItem("計畫／理論", Brushes.Purple, ChartLegendMarker.Dash),
            .. Enumerable.Range(1, 8).Select(index => new ChartLegendItem($"項目{index}", Brushes.LightGray, ChartLegendMarker.Point)),
            new ChartLegendItem("最後一項", Brushes.Gold, ChartLegendMarker.Point)
        ]);
        AddText(canvas, "O04  4.28 km", 3, 320, 10);
        AddText(canvas, "O03  3.00 km", 3, 355, 10);
        AddTrainLabel(90, 48, "TRAIN-A", Brushes.Cyan);
        AddTrainLabel(90, 48, "TRAIN-B", Brushes.Lime);
        AddTrainLabel(510, 140, "BOUNDARY-TRAIN", Brushes.DarkOrange);
        canvas.Children.Add(new Line
        {
            X1 = 60, Y1 = 361, X2 = 78, Y2 = 338,
            Stroke = Brushes.Red, StrokeThickness = 1,
            Tag = new TimeDistanceStationLeaderTag("O03", 338)
        });

        // A label whose center is just inside a subsequent slice boundary.  A
        // page-local implementation must keep its entire text box on that page.
        var boundaryTick = AddText(canvas, "00:40:00.0", 530, 346, 9);
        boundaryTick.Width = 70;
        boundaryTick.TextAlignment = TextAlignment.Center;
        var firstTick = AddText(canvas, "00:00:00.0", 47, 346, 9);
        firstTick.Width = 70;
        firstTick.TextAlignment = TextAlignment.Center;
        firstTick.Foreground = Brushes.Blue;
        var nearbyTick = AddText(canvas, "00:00:12.0", 75, 346, 9);
        nearbyTick.Width = 70;
        nearbyTick.TextAlignment = TextAlignment.Center;
        nearbyTick.Foreground = Brushes.Magenta;
        AddText(canvas, "時間", width - 48, 354, 10);

        canvas.Measure(new Size(width, height));
        canvas.Arrange(new Rect(0, 0, width, height));
        return canvas;

        void AddTrainLabel(double x, double y, string text, Brush color)
        {
            canvas.Children.Add(new Polyline
            {
                Points = [new Point(x, y), new Point(x + 1, y + 1)],
                Stroke = Brushes.DarkGreen, StrokeThickness = 1
            });
            var label = AddText(canvas, text, x + 3, y - 15, 9);
            label.Foreground = color;
            label.Width = 120;
        }
    }

    private static TextBlock AddText(Canvas canvas, string text, double left, double top, double fontSize)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            Foreground = Brushes.Black
        };
        Canvas.SetLeft(block, left);
        Canvas.SetTop(block, top);
        canvas.Children.Add(block);
        return block;
    }

    private static bool HasTrainColorPixels(BitmapSource bitmap, int left, int right, int top, int bottom, bool orange)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        for (var y = top; y < Math.Min(bottom, converted.PixelHeight); y++)
            for (var x = left; x < Math.Min(right, converted.PixelWidth); x++)
            {
                var offset = y * stride + x * 4;
                var matches = orange
                    ? pixels[offset + 2] > 180 && pixels[offset + 1] > 70 && pixels[offset + 1] < 180
                        && pixels[offset] < 80 && pixels[offset + 1] - pixels[offset] > 50
                    : pixels[offset + 2] < 80 && pixels[offset + 1] > 180 && pixels[offset] < 80;
                if (pixels[offset + 3] > 0 && matches) return true;
            }
        return false;
    }

    private static Rect? FindColorBounds(
        BitmapSource bitmap,
        int left,
        int right,
        int top,
        int bottom,
        Func<byte, byte, byte, bool> matches)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        left = Math.Clamp(left, 0, converted.PixelWidth);
        right = Math.Clamp(right, left, converted.PixelWidth);
        top = Math.Clamp(top, 0, converted.PixelHeight);
        bottom = Math.Clamp(bottom, top, converted.PixelHeight);
        var minX = right;
        var minY = bottom;
        var maxX = -1;
        var maxY = -1;
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var offset = y * stride + x * 4;
                if (pixels[offset + 3] == 0
                    || !matches(pixels[offset + 2], pixels[offset + 1], pixels[offset]))
                    continue;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return maxX < 0 ? null : new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static Rect RequireColorBounds(Rect? bounds, string message) =>
        bounds ?? throw new InvalidOperationException(message);

    private static bool HasCyanPixels(BitmapSource bitmap, int left, int right, int top, int bottom)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        for (var y = top; y < Math.Min(bottom, converted.PixelHeight); y++)
            for (var x = left; x < Math.Min(right, converted.PixelWidth); x++)
            {
                var offset = y * stride + x * 4;
                if (pixels[offset + 3] > 0 && pixels[offset] > 180
                    && pixels[offset + 1] > 180 && pixels[offset + 2] < 80) return true;
            }
        return false;
    }

    private static bool HasRedPixels(BitmapSource bitmap, int left, int right, int top, int bottom)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        for (var y = top; y < Math.Min(bottom, converted.PixelHeight); y++)
            for (var x = left; x < Math.Min(right, converted.PixelWidth); x++)
            {
                var offset = y * stride + x * 4;
                if (pixels[offset + 3] > 0 && pixels[offset + 2] > 180
                    && pixels[offset + 1] < 80 && pixels[offset] < 80) return true;
            }
        return false;
    }

    private static bool HasDarkPixels(BitmapSource bitmap, int left, int right, int top, int bottom)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        left = Math.Clamp(left, 0, converted.PixelWidth);
        right = Math.Clamp(right, left, converted.PixelWidth);
        top = Math.Clamp(top, 0, converted.PixelHeight);
        bottom = Math.Clamp(bottom, top, converted.PixelHeight);
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var offset = y * stride + x * 4;
                var alpha = pixels[offset + 3];
                if (alpha > 0
                    && pixels[offset] < 100
                    && pixels[offset + 1] < 100
                    && pixels[offset + 2] < 100)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasBlackPixels(BitmapSource bitmap, int left, int right, int top, int bottom)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        left = Math.Clamp(left, 0, converted.PixelWidth);
        right = Math.Clamp(right, left, converted.PixelWidth);
        top = Math.Clamp(top, 0, converted.PixelHeight);
        bottom = Math.Clamp(bottom, top, converted.PixelHeight);
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var offset = y * stride + x * 4;
                if (pixels[offset + 3] > 0
                    && pixels[offset] < 32
                    && pixels[offset + 1] < 32
                    && pixels[offset + 2] < 32)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasBluePixels(BitmapSource bitmap, int left, int right, int top, int bottom)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        for (var y = top; y < bottom; y++)
            for (var x = left; x < right; x++)
            {
                var offset = y * stride + x * 4;
                if (pixels[offset + 3] > 0 && pixels[offset] > 180
                    && pixels[offset + 1] < 80 && pixels[offset + 2] < 80)
                    return true;
            }
        return false;
    }

    private static bool HasMagentaPixels(BitmapSource bitmap, int left, int right, int top, int bottom)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        for (var y = top; y < bottom; y++)
            for (var x = left; x < right; x++)
            {
                var offset = y * stride + x * 4;
                if (pixels[offset + 3] > 0 && pixels[offset] > 180
                    && pixels[offset + 1] < 80 && pixels[offset + 2] > 180)
                    return true;
            }
        return false;
    }

    private static int BlueInkWidth(BitmapSource bitmap)
    {
        var columns = Enumerable.Range(0, bitmap.PixelWidth)
            .Where(x => HasBluePixels(bitmap, x, x + 1, 0, bitmap.PixelHeight)).ToArray();
        return columns.Length == 0 ? 0 : columns[^1] - columns[0] + 1;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
