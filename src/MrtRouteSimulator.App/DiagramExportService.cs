using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace MrtRouteSimulator.App;

internal enum PdfPageSize
{
    A4,
    A3
}

internal static class DiagramExportService
{
    public static void ExportPng(FrameworkElement element, string path, double scale)
    {
        var bitmap = Render(element, scale);
        var trainAnchors = FindTrainLabelAnchors(element);
        // PNG must retain the same non-overlapping train annotations as a
        // single-page PDF. Reuse the export overlay; do not move source visuals
        // or trajectories, and keep ordinary non-diagram PNGs unchanged.
        var output = trainAnchors.Count == 0
            ? bitmap
            : ComposePdfSinglePage(bitmap, element, trainAnchors, scale);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(output));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    public static void ExportPdf(FrameworkElement element, string path, PdfPageSize pageSize, bool splitPages)
    {
        const double renderScale = 1.6;
        var bitmap = Render(element, renderScale);
        var pageWidth = pageSize == PdfPageSize.A3 ? 1191 : 842;
        var pageHeight = pageSize == PdfPageSize.A3 ? 842 : 595;
        const int margin = 24;
        var availableWidth = pageWidth - margin * 2;
        var availableHeight = pageHeight - margin * 2;
        var pages = CreatePdfPages(bitmap, availableWidth, availableHeight, splitPages, element, renderScale);

        using var pdf = new MemoryStream();
        var offsets = new List<long> { 0 };
        WriteAscii(pdf, "%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");
        WriteObject(pdf, offsets, 1, "<< /Type /Catalog /Pages 2 0 R >>");
        var kids = string.Join(' ', Enumerable.Range(0, pages.Count).Select(index => $"{3 + index * 3} 0 R"));
        WriteObject(pdf, offsets, 2, $"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>");

        for (var index = 0; index < pages.Count; index++)
        {
            var page = pages[index];
            var pageObject = 3 + index * 3;
            var imageObject = pageObject + 1;
            var contentObject = pageObject + 2;
            var imageName = $"Im{index}";
            var ratio = Math.Min((double)availableWidth / page.PixelWidth, (double)availableHeight / page.PixelHeight);
            var drawWidth = page.PixelWidth * ratio;
            var drawHeight = page.PixelHeight * ratio;
            var drawX = (pageWidth - drawWidth) / 2;
            var drawY = (pageHeight - drawHeight) / 2;
            var content = string.Create(
                CultureInfo.InvariantCulture,
                $"q {drawWidth:0.###} 0 0 {drawHeight:0.###} {drawX:0.###} {drawY:0.###} cm /{imageName} Do Q");
            var contentBytes = Encoding.ASCII.GetBytes(content);
            var image = EncodeJpeg(page);

            WriteObject(
                pdf,
                offsets,
                pageObject,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageWidth} {pageHeight}] /Resources << /XObject << /{imageName} {imageObject} 0 R >> >> /Contents {contentObject} 0 R >>");
            offsets.Add(pdf.Position);
            WriteAscii(
                pdf,
                $"{imageObject} 0 obj\n<< /Type /XObject /Subtype /Image /Width {page.PixelWidth} /Height {page.PixelHeight} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {image.Length} >>\nstream\n");
            pdf.Write(image);
            WriteAscii(pdf, "\nendstream\nendobj\n");
            offsets.Add(pdf.Position);
            WriteAscii(pdf, $"{contentObject} 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
            pdf.Write(contentBytes);
            WriteAscii(pdf, "\nendstream\nendobj\n");
        }

        var xrefPosition = pdf.Position;
        WriteAscii(pdf, $"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        for (var index = 1; index < offsets.Count; index++)
        {
            WriteAscii(pdf, $"{offsets[index]:0000000000} 00000 n \n");
        }

        WriteAscii(pdf, $"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xrefPosition}\n%%EOF\n");
        File.WriteAllBytes(path, pdf.ToArray());
    }

    private static IReadOnlyList<BitmapSource> CreatePdfPages(
        RenderTargetBitmap bitmap,
        int availableWidth,
        int availableHeight,
        bool splitPages,
        FrameworkElement element,
        double renderScale)
    {
        var pageAspect = (double)availableWidth / availableHeight;
        var trainAnchors = FindTrainLabelAnchors(element);
        if (!splitPages || (double)bitmap.PixelWidth / bitmap.PixelHeight <= pageAspect * 1.08)
        {
            if (trainAnchors.Count == 0)
            {
                return [bitmap];
            }

            return [ComposePdfSinglePage(bitmap, element, trainAnchors, renderScale)];
        }

        // Render the graph without TextBlocks before slicing.  Every page then gets a
        // complete, page-local text layer below instead of a bitmap fragment that can
        // cut a title, tick label, or train marker in half.
        var graphBitmap = Render(element, renderScale, includeText: false);
        var textBlocks = FindTextBlocks(element);
        var stationLeaders = FindStationLeaders(element);
        var logicalScale = bitmap.DpiX / 96d;
        var axisWidth = Math.Clamp((int)Math.Round(92 * logicalScale), 1, bitmap.PixelWidth / 3);
        var left = Math.Clamp((int)Math.Round(82 * logicalScale), 1, axisWidth);
        var plotTop = Math.Clamp((int)Math.Round(48 * logicalScale), 0, bitmap.PixelHeight);
        var plotBottom = Math.Clamp((int)Math.Round(42 * logicalScale), 0, bitmap.PixelHeight - plotTop);
        var plotHeight = Math.Max(1, bitmap.PixelHeight - plotTop - plotBottom);
        var sliceCapacity = Math.Max(120, (int)Math.Floor(bitmap.PixelHeight * pageAspect) - axisWidth);
        var span = bitmap.PixelWidth - left;
        var pageCount = Math.Max(1, (int)Math.Ceiling((double)span / sliceCapacity));
        var finalStart = Math.Max(left, bitmap.PixelWidth - sliceCapacity);
        var pages = new List<BitmapSource>();
        var sliceStarts = Enumerable.Range(0, pageCount).Select(index => pageCount == 1 ? left
            : left + (int)Math.Round((double)(finalStart - left) * index / (pageCount - 1))).ToArray();
        for (var index = 0; index < pageCount; index++)
        {
            // Distribute the minimum number of full-width slices over the span.
            // A fixed overlap step can otherwise create a nearly empty tail page.
            var start = sliceStarts[index];
            var sliceWidth = Math.Min(sliceCapacity, bitmap.PixelWidth - start);
            var outputWidth = axisWidth + sliceWidth;
            pages.Add(ComposePdfPage(
                graphBitmap,
                textBlocks,
                stationLeaders,
                trainAnchors.Where(pair => Array.FindIndex(sliceStarts, sliceStart =>
                    pair.Value.X * logicalScale >= sliceStart - .5
                    && pair.Value.X * logicalScale <= Math.Min(bitmap.PixelWidth, sliceStart + sliceCapacity) + .5) == index)
                    .ToDictionary(pair => pair.Key, pair => pair.Value),
                trainAnchors,
                start,
                sliceWidth,
                outputWidth,
                axisWidth,
                left,
                plotTop,
                plotHeight,
                logicalScale));
        }

        return pages;
    }

    private static BitmapSource ComposePdfSinglePage(
        RenderTargetBitmap bitmap,
        FrameworkElement element,
        IReadOnlyDictionary<TextBlock, Point> trainAnchors,
        double renderScale)
    {
        var textBlocks = FindTextBlocks(element);
        var captionBlocks = textBlocks
            .Where(block => block.Visibility == Visibility.Visible && block.Text == "時間")
            .ToArray();
        var hiddenTextBlocks = trainAnchors.Keys
            .Concat(captionBlocks)
            .Distinct()
            .Where(block => block.Visibility != Visibility.Hidden)
            .Select(block => (block, visibility: block.Visibility))
            .ToArray();
        RenderTargetBitmap graphBitmap;
        try
        {
            foreach (var (block, _) in hiddenTextBlocks)
            {
                block.Visibility = Visibility.Hidden;
            }

            // Keep the graph and other text at their original single-page size
            // and position. Train labels and the time caption use export overlays.
            graphBitmap = Render(element, renderScale, includeText: true);
        }
        finally
        {
            foreach (var (block, visibility) in hiddenTextBlocks)
            {
                block.Visibility = visibility;
            }
        }

        var logicalScale = bitmap.DpiX / 96d;
        var logicalWidth = bitmap.PixelWidth / logicalScale;
        var originalLogicalHeight = bitmap.PixelHeight / logicalScale;
        var logicalPlotTop = Math.Min(48, Math.Max(0, originalLogicalHeight));
        var logicalPlotBottom = Math.Max(logicalPlotTop + 1, originalLogicalHeight - 42);
        var trainBounds = new Rect(
            82 + 4,
            logicalPlotTop + 2,
            Math.Max(1, logicalWidth - 82 - 8),
            Math.Max(1, logicalPlotBottom - logicalPlotTop - 4));
        var occupied = new List<Rect>();
        foreach (var block in textBlocks)
        {
            if (trainAnchors.ContainsKey(block) || captionBlocks.Contains(block)
                || block.Visibility != Visibility.Visible)
                continue;
            var x = Canvas.GetLeft(block);
            var y = Canvas.GetTop(block);
            if (double.IsNaN(x) || double.IsNaN(y)) continue;
            occupied.Add(new Rect(x, y, Math.Max(1, block.ActualWidth), Math.Max(1, block.ActualHeight)));
        }

        var trainLabels = new Dictionary<TextBlock, (Rect Bounds, Point Anchor)>();
        var logicalHeight = originalLogicalHeight;
        foreach (var (block, anchor) in trainAnchors.OrderBy(pair => pair.Value.X).ThenBy(pair => pair.Value.Y))
        {
            var width = Math.Min(Math.Max(1, block.ActualWidth), Math.Max(1, trainBounds.Width));
            var height = Math.Max(1, block.ActualHeight);
            var placement = PlacePdfTrainLabel(
                new Rect(Canvas.GetLeft(block), Canvas.GetTop(block), width, height),
                trainBounds,
                occupied);
            trainLabels[block] = (placement, anchor);
            logicalHeight = Math.Max(logicalHeight, placement.Bottom + 4);
        }

        // The single-page base graph keeps every non-train visual at its source
        // coordinates, but the final time caption must be laid out after ticks
        // and train annotations are known.  Keep its original Y when possible;
        // only move it into a new row when it collides with visible annotations.
        var captionLabels = new Dictionary<TextBlock, Rect>();
        foreach (var block in captionBlocks)
        {
            var x = Canvas.GetLeft(block);
            var y = Canvas.GetTop(block);
            if (double.IsNaN(x) || double.IsNaN(y)) continue;
            var placement = PlacePdfTimeLabel(
                x,
                y,
                Math.Max(1, block.ActualWidth),
                Math.Max(1, block.ActualHeight),
                occupied);
            captionLabels[block] = placement;
            logicalHeight = Math.Max(logicalHeight, placement.Bottom + 4);
        }

        var outputHeight = (int)Math.Ceiling(logicalHeight * logicalScale);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, logicalWidth, logicalHeight));
            context.DrawImage(graphBitmap, new Rect(0, 0, logicalWidth, originalLogicalHeight));
            foreach (var (block, train) in trainLabels)
            {
                var connection = new Point(
                    Math.Clamp(train.Anchor.X, train.Bounds.Left, train.Bounds.Right),
                    Math.Clamp(train.Anchor.Y, train.Bounds.Top, train.Bounds.Bottom));
                context.DrawLine(new Pen(block.Foreground, .6), train.Anchor, connection);
                DrawTextBlock(context, block, train.Bounds.X, train.Bounds.Y,
                    train.Bounds.Width, train.Bounds.Height);
            }
            foreach (var (block, bounds) in captionLabels)
            {
                DrawTextBlock(context, block, bounds.X, bounds.Y, bounds.Width, bounds.Height);
            }
        }

        var page = new RenderTargetBitmap(
            bitmap.PixelWidth,
            outputHeight,
            bitmap.DpiX,
            bitmap.DpiY,
            PixelFormats.Pbgra32);
        page.Render(visual);
        page.Freeze();
        return page;
    }

    private static BitmapSource ComposePdfPage(
        RenderTargetBitmap graphBitmap,
        IReadOnlyList<TextBlock> textBlocks,
        IReadOnlyList<Line> stationLeaders,
        IReadOnlyDictionary<TextBlock, Point> pageTrainAnchors,
        IReadOnlyDictionary<TextBlock, Point> allTrainAnchors,
        int start,
        int sliceWidth,
        int outputWidth,
        int axisWidth,
        int left,
        int plotTop,
        int plotHeight,
        double logicalScale)
    {
        var outputHeight = graphBitmap.PixelHeight;
        var logicalWidth = outputWidth / logicalScale;
        var logicalHeight = outputHeight / logicalScale;
        var logicalAxisWidth = axisWidth / logicalScale;
        var logicalLeft = left / logicalScale;
        var logicalStart = start / logicalScale;
        var logicalSliceWidth = sliceWidth / logicalScale;
        var logicalPlotTop = plotTop / logicalScale;
        var logicalPlotBottom = logicalHeight - (outputHeight - plotTop - plotHeight) / logicalScale;

        // Moving boundary ticks inside a page can make neighboring labels overlap.
        // Keep their full text and place colliding labels on additional rows; only
        // the export's text area grows, never the underlying trajectory geometry.
        var timeLabels = new Dictionary<TextBlock, Rect>();
        var occupiedTimeLabels = new List<Rect>();
        foreach (var block in textBlocks.OrderBy(Canvas.GetLeft))
        {
            var x = Canvas.GetLeft(block);
            var y = Canvas.GetTop(block);
            var width = Math.Max(1, block.ActualWidth);
            var center = x + width / 2;
            if (double.IsNaN(x) || double.IsNaN(y) || y <= logicalPlotBottom + 0.5
                || center < logicalLeft - 0.5 || block.Text == "時間"
                || center < logicalStart - 0.5 || center > logicalStart + logicalSliceWidth + 0.5)
                continue;
            var destinationX = Math.Clamp(logicalAxisWidth + center - logicalStart - width / 2,
                logicalAxisWidth + 4, Math.Max(logicalAxisWidth + 4, logicalWidth - width - 4));
            timeLabels[block] = PlacePdfTimeLabel(destinationX, y, width,
                Math.Max(1, block.ActualHeight), occupiedTimeLabels);
        }
        var captionY = Math.Max(logicalPlotBottom + 24,
            occupiedTimeLabels.Count == 0 ? 0 : occupiedTimeLabels.Max(rect => rect.Bottom) + 4);
        var captionHeight = textBlocks.Where(block => block.Text == "時間")
            .Select(block => block.ActualHeight).DefaultIfEmpty(0).Max();
        logicalHeight = Math.Max(logicalHeight, captionY + captionHeight + 4);
        var occupiedTrainLabels = new List<Rect>(occupiedTimeLabels);
        occupiedTrainLabels.Add(new Rect(logicalAxisWidth, captionY,
            Math.Max(1, logicalWidth - logicalAxisWidth), Math.Max(1, captionHeight)));
        var trainLabels = new Dictionary<TextBlock, (Rect Bounds, Point Anchor)>();
        foreach (var (block, anchor) in pageTrainAnchors.OrderBy(pair => pair.Value.X).ThenBy(pair => pair.Value.Y))
        {
            var width = Math.Min(Math.Max(1, block.ActualWidth), Math.Max(1, logicalWidth - logicalAxisWidth - 8));
            var height = Math.Max(1, block.ActualHeight);
            var bounds = new Rect(logicalAxisWidth + 4, logicalPlotTop + 2,
                Math.Max(width, logicalWidth - logicalAxisWidth - 8), Math.Max(height, logicalPlotBottom - logicalPlotTop - 4));
            var placement = PlacePdfTrainLabel(new Rect(logicalAxisWidth + Canvas.GetLeft(block) - logicalStart,
                Canvas.GetTop(block), width, height), bounds, occupiedTrainLabels);
            trainLabels[block] = (placement, new Point(logicalAxisWidth + anchor.X - logicalStart, anchor.Y));
            logicalHeight = Math.Max(logicalHeight, placement.Bottom + 4);
        }
        outputHeight = (int)Math.Ceiling(logicalHeight * logicalScale);

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            // Keep the established white page background and draw only the graph
            // area.  Axis/header text is deliberately drawn separately below.
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, logicalWidth, logicalHeight));
            var source = new CroppedBitmap(
                graphBitmap,
                new Int32Rect(start, plotTop, sliceWidth, plotHeight));
            context.DrawImage(
                source,
                new Rect(logicalAxisWidth, logicalPlotTop, logicalSliceWidth, plotHeight / logicalScale));

            var pen = new Pen(Brushes.SlateGray, 1.1);
            context.DrawLine(
                pen,
                new Point(logicalAxisWidth, logicalPlotTop),
                new Point(logicalAxisWidth, logicalPlotBottom));
            context.DrawLine(
                pen,
                new Point(logicalAxisWidth, logicalPlotBottom),
                new Point(logicalWidth, logicalPlotBottom));

            // The graph crop excludes the station axis. Preserve its original
            // leader geometry on every page so displaced labels still identify
            // their actual mileage grid line, rather than implying a new anchor.
            foreach (var leader in stationLeaders)
            {
                context.DrawLine(new Pen(leader.Stroke, leader.StrokeThickness),
                    new Point(leader.X1, leader.Y1), new Point(leader.X2, leader.Y2));
            }

            foreach (var textBlock in textBlocks)
            {
                var x = Canvas.GetLeft(textBlock);
                var y = Canvas.GetTop(textBlock);
                if (double.IsNaN(x) || double.IsNaN(y))
                {
                    continue;
                }

                var width = Math.Max(1, textBlock.ActualWidth);
                var height = Math.Max(1, textBlock.ActualHeight);
                if (allTrainAnchors.ContainsKey(textBlock))
                {
                    if (trainLabels.TryGetValue(textBlock, out var train))
                    {
                        var connection = new Point(Math.Clamp(train.Anchor.X, train.Bounds.Left, train.Bounds.Right),
                            Math.Clamp(train.Anchor.Y, train.Bounds.Top, train.Bounds.Bottom));
                        context.DrawLine(new Pen(textBlock.Foreground, .6), train.Anchor, connection);
                        DrawTextBlock(context, textBlock, train.Bounds.X, train.Bounds.Y, train.Bounds.Width, train.Bounds.Height);
                    }
                    continue;
                }
                // The chart title and legend occupy the fixed 8/28 DIP header
                // rows.  A train label can legitimately sit just below the plot
                // top, so do not classify every text block above the plot as header.
                if (x >= logicalLeft - 0.5 && y <= 30)
                {
                    // The chart title and legend are page-local.  Shrink them as
                    // needed so a long route name is never clipped at page right.
                    var fit = Math.Min(1, Math.Max(1, logicalWidth - x - 4) / width);
                    DrawTextBlock(context, textBlock, x, y, width * fit, height * fit);
                    continue;
                }

                if (x + width / 2 < logicalLeft - 0.5)
                {
                    // Label spacing may place a station below the plot bottom;
                    // its left-axis identity takes precedence over the time row.
                    // A centered first time tick starts left of the axis, but its
                    // center is on the axis and must not repeat on every page.
                    DrawTextBlock(context, textBlock, x, y, width, height);
                    continue;
                }

                if (y > logicalPlotBottom + 0.5)
                {
                    // Recreate x-axis tick labels from their original positions, but
                    // only when their tick is in this page's slice.  Their destination
                    // is clamped so the text itself cannot cross a page edge.
                    var center = x + width / 2;
                    if (textBlock.Text.Equals("時間", StringComparison.Ordinal))
                    {
                        // Leave the final time tick readable; the original canvas
                        // places this axis caption on the same baseline as the last
                        // tick, which is too tight after a page is narrowed.
                        DrawTextBlock(
                            context,
                            textBlock,
                            Math.Max(logicalAxisWidth, logicalWidth - 48),
                            captionY,
                            width,
                            height);
                    }
                    else if (timeLabels.TryGetValue(textBlock, out var placement))
                    {
                        DrawTextBlock(context, textBlock, placement.X, placement.Y, placement.Width, placement.Height);
                    }

                    continue;
                }

                if (y >= logicalPlotTop - 20 && y <= logicalPlotBottom + 0.5)
                {
                    // Train labels are redrawn only on the page containing their
                    // anchor. This prevents a label at a page boundary being split.
                    var center = x + width / 2;
                    if (center >= logicalStart - 0.5 && center <= logicalStart + logicalSliceWidth + 0.5)
                    {
                        var destinationX = logicalAxisWidth + x - logicalStart;
                        destinationX = Math.Clamp(destinationX, logicalAxisWidth, Math.Max(logicalAxisWidth, logicalWidth - width));
                        DrawTextBlock(context, textBlock, destinationX, y, width, height);
                    }
                }
            }
        }

        var page = new RenderTargetBitmap(
            outputWidth,
            outputHeight,
            graphBitmap.DpiX,
            graphBitmap.DpiY,
            PixelFormats.Pbgra32);
        page.Render(visual);
        page.Freeze();
        return page;
    }

    private static Rect PlacePdfTrainLabel(Rect desired, Rect bounds, List<Rect> occupied)
    {
        var x = Math.Clamp(desired.X, bounds.Left, Math.Max(bounds.Left, bounds.Right - desired.Width));
        var y = Math.Clamp(desired.Y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - desired.Height));
        var step = desired.Height + 3;
        for (var row = 0; row <= (int)Math.Ceiling(bounds.Height / step); row++)
        {
            foreach (var candidateY in row == 0 ? new[] { y } : new[] { y - row * step, y + row * step })
            {
                var candidate = new Rect(x, candidateY, desired.Width, desired.Height);
                if (!bounds.Contains(candidate) || occupied.Any(rect =>
                    { rect.Inflate(2, 2); return rect.IntersectsWith(candidate); })) continue;
                occupied.Add(candidate);
                return candidate;
            }
        }
        // Unusually dense charts retain every name in an extended annotation row.
        // The leader still points to the original graph anchor; no data is moved.
        var overflow = new Rect(x, Math.Max(bounds.Bottom, occupied.Select(rect => rect.Bottom).DefaultIfEmpty(0).Max()) + 3,
            desired.Width, desired.Height);
        occupied.Add(overflow);
        return overflow;
    }

    private static IReadOnlyDictionary<TextBlock, Point> FindTrainLabelAnchors(DependencyObject root)
    {
        var result = new Dictionary<TextBlock, Point>();
        Visit(root);
        return result;

        void Visit(DependencyObject node)
        {
            if (node is Canvas canvas)
            {
                // Full-history output emits each trajectory Polyline immediately
                // followed by its label. Use that actual point, not label offsets
                // or text-center guesses, to determine single-page ownership.
                for (var index = 1; index < canvas.Children.Count; index++)
                    if (canvas.Children[index - 1] is Polyline { Points.Count: > 0 } line
                        && canvas.Children[index] is TextBlock block
                        && block.Visibility == Visibility.Visible
                        && Canvas.GetTop(block) > 30)
                        result[block] = line.Points[0];
            }
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
                Visit(VisualTreeHelper.GetChild(node, index));
        }
    }

    private static Rect PlacePdfTimeLabel(double x, double y, double width, double height, List<Rect> occupied)
    {
        var result = new Rect(x, y, width, height);
        while (true)
        {
            var collisions = occupied.Where(rect =>
            {
                rect.Inflate(4, 0);
                return rect.IntersectsWith(result);
            }).ToArray();
            if (collisions.Length == 0) break;
            result.Y = collisions.Max(rect => rect.Bottom) + 2;
        }
        occupied.Add(result);
        return result;
    }

    private static void DrawTextBlock(
        DrawingContext context,
        TextBlock source,
        double x,
        double y,
        double width,
        double height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        context.DrawRectangle(
            new VisualBrush(source)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect((Point)VisualTreeHelper.GetOffset(source),
                    new Size(source.ActualWidth, source.ActualHeight)),
                Stretch = Stretch.Fill
            },
            null,
            new Rect(x, y, width, height));
    }

    private static IReadOnlyList<Line> FindStationLeaders(DependencyObject root)
    {
        var result = new List<Line>();
        Visit(root);
        return result;

        void Visit(DependencyObject node)
        {
            if (node is Line { Tag: TimeDistanceStationLeaderTag, Visibility: Visibility.Visible } line)
                result.Add(line);
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
                Visit(VisualTreeHelper.GetChild(node, index));
        }
    }

    private static IReadOnlyList<TextBlock> FindTextBlocks(DependencyObject root)
    {
        var result = new List<TextBlock>();
        Visit(root);
        return result;

        void Visit(DependencyObject node)
        {
            if (node is TextBlock textBlock)
            {
                result.Add(textBlock);
            }

            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
            {
                Visit(VisualTreeHelper.GetChild(node, index));
            }
        }
    }

    private static byte[] EncodeJpeg(BitmapSource bitmap)
    {
        var jpeg = new JpegBitmapEncoder { QualityLevel = 92 };
        jpeg.Frames.Add(BitmapFrame.Create(bitmap));
        using var imageStream = new MemoryStream();
        jpeg.Save(imageStream);
        return imageStream.ToArray();
    }

    private static RenderTargetBitmap Render(FrameworkElement element, double scale, bool includeText = true)
    {
        // Native export rebuilds the full-history visual tree synchronously. Its
        // new children must be arranged before VisualBrush captures the tree;
        // otherwise a valid PNG/PDF can contain only the white background.
        element.UpdateLayout();
        var width = Math.Max(1, element.ActualWidth);
        var height = Math.Max(1, element.ActualHeight);
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(width * scale));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(height * scale));
        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var bounds = new Rect(0, 0, width, height);
        var visual = new DrawingVisual();
        var hiddenTextBlocks = includeText
            ? Array.Empty<(TextBlock TextBlock, Visibility Visibility)>()
            : FindTextBlocks(element)
                .Where(textBlock => textBlock.Visibility != Visibility.Hidden)
                .Select(textBlock => (textBlock, textBlock.Visibility))
                .ToArray();
        try
        {
            foreach (var (textBlock, _) in hiddenTextBlocks)
            {
                textBlock.Visibility = Visibility.Hidden;
            }

            using (var context = visual.RenderOpen())
            {
                // Canvas 的透明背景在 JPEG/PDF 會變成黑底；父容器的配置偏移也不屬於輸出。
                context.DrawRectangle(Brushes.White, null, bounds);
                context.DrawRectangle(new VisualBrush(element)
                {
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect((Point)VisualTreeHelper.GetOffset(element), bounds.Size),
                    Stretch = Stretch.Fill
                }, null, bounds);
            }

            bitmap.Render(visual);
            return bitmap;
        }
        finally
        {
            foreach (var (textBlock, visibility) in hiddenTextBlocks)
            {
                textBlock.Visibility = visibility;
            }
        }
    }

    private static void WriteObject(Stream stream, ICollection<long> offsets, int number, string content)
    {
        offsets.Add(stream.Position);
        WriteAscii(stream, $"{number} 0 obj\n{content}\nendobj\n");
    }

    private static void WriteAscii(Stream stream, string value)
    {
        var bytes = Encoding.Latin1.GetBytes(value);
        stream.Write(bytes);
    }
}
