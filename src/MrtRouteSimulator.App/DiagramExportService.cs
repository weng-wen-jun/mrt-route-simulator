using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
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
        if (!splitPages || (double)bitmap.PixelWidth / bitmap.PixelHeight <= pageAspect * 1.08)
        {
            return [bitmap];
        }

        // Render the graph without TextBlocks before slicing.  Every page then gets a
        // complete, page-local text layer below instead of a bitmap fragment that can
        // cut a title, tick label, or train marker in half.
        var graphBitmap = Render(element, renderScale, includeText: false);
        var textBlocks = FindTextBlocks(element);
        var logicalScale = bitmap.DpiX / 96d;
        var axisWidth = Math.Clamp((int)Math.Round(92 * logicalScale), 1, bitmap.PixelWidth / 3);
        var left = Math.Clamp((int)Math.Round(82 * logicalScale), 1, axisWidth);
        var plotTop = Math.Clamp((int)Math.Round(48 * logicalScale), 0, bitmap.PixelHeight);
        var plotBottom = Math.Clamp((int)Math.Round(42 * logicalScale), 0, bitmap.PixelHeight - plotTop);
        var plotHeight = Math.Max(1, bitmap.PixelHeight - plotTop - plotBottom);
        var sliceCapacity = Math.Max(120, (int)Math.Floor(bitmap.PixelHeight * pageAspect) - axisWidth);
        var step = Math.Max(1, (int)Math.Floor(sliceCapacity * 0.92));
        var pages = new List<BitmapSource>();
        for (var start = left; start < bitmap.PixelWidth; start += step)
        {
            var sliceWidth = Math.Min(sliceCapacity, bitmap.PixelWidth - start);
            var outputWidth = axisWidth + sliceWidth;
            pages.Add(ComposePdfPage(
                graphBitmap,
                textBlocks,
                start,
                sliceWidth,
                outputWidth,
                axisWidth,
                left,
                plotTop,
                plotHeight,
                logicalScale));
            if (start + sliceWidth >= bitmap.PixelWidth)
            {
                break;
            }
        }

        return pages;
    }

    private static BitmapSource ComposePdfPage(
        RenderTargetBitmap graphBitmap,
        IReadOnlyList<TextBlock> textBlocks,
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
                            logicalPlotBottom + 24,
                            width,
                            height);
                    }
                    else if (center >= logicalStart - 0.5 && center <= logicalStart + logicalSliceWidth + 0.5)
                    {
                        var destinationX = logicalAxisWidth + center - logicalStart - width / 2;
                        destinationX = Math.Clamp(destinationX, logicalAxisWidth, Math.Max(logicalAxisWidth, logicalWidth - width));
                        DrawTextBlock(context, textBlock, destinationX, y, width, height);
                    }

                    continue;
                }

                if (x < logicalLeft - 0.5)
                {
                    // Station/vertical-axis labels belong to every page's left axis.
                    DrawTextBlock(context, textBlock, x, y, width, height);
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
            new VisualBrush(source) { Stretch = Stretch.Fill },
            null,
            new Rect(x, y, width, height));
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
