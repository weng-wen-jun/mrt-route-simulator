using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
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
        var bitmap = Render(element, 1.6);
        var pageWidth = pageSize == PdfPageSize.A3 ? 1191 : 842;
        var pageHeight = pageSize == PdfPageSize.A3 ? 842 : 595;
        const int margin = 24;
        var availableWidth = pageWidth - margin * 2;
        var availableHeight = pageHeight - margin * 2;
        var pages = CreatePdfPages(bitmap, availableWidth, availableHeight, splitPages);

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
        bool splitPages)
    {
        var pageAspect = (double)availableWidth / availableHeight;
        if (!splitPages || (double)bitmap.PixelWidth / bitmap.PixelHeight <= pageAspect * 1.08)
        {
            return [bitmap];
        }

        var axisWidth = Math.Clamp((int)Math.Round(92 * bitmap.DpiX / 96), 1, bitmap.PixelWidth / 3);
        var sliceCapacity = Math.Max(120, (int)Math.Floor(bitmap.PixelHeight * pageAspect) - axisWidth);
        var step = Math.Max(1, (int)Math.Floor(sliceCapacity * 0.92));
        var pages = new List<BitmapSource>();
        for (var start = axisWidth; start < bitmap.PixelWidth; start += step)
        {
            var sliceWidth = Math.Min(sliceCapacity, bitmap.PixelWidth - start);
            var outputWidth = axisWidth + sliceWidth;
            var stride = outputWidth * 4;
            var pixels = new byte[stride * bitmap.PixelHeight];
            bitmap.CopyPixels(new Int32Rect(0, 0, axisWidth, bitmap.PixelHeight), pixels, stride, 0);
            bitmap.CopyPixels(new Int32Rect(start, 0, sliceWidth, bitmap.PixelHeight), pixels, stride, axisWidth * 4);
            var page = BitmapSource.Create(
                outputWidth,
                bitmap.PixelHeight,
                bitmap.DpiX,
                bitmap.DpiY,
                PixelFormats.Pbgra32,
                null,
                pixels,
                stride);
            page.Freeze();
            pages.Add(page);
            if (start + sliceWidth >= bitmap.PixelWidth)
            {
                break;
            }
        }

        return pages;
    }

    private static byte[] EncodeJpeg(BitmapSource bitmap)
    {
        var jpeg = new JpegBitmapEncoder { QualityLevel = 92 };
        jpeg.Frames.Add(BitmapFrame.Create(bitmap));
        using var imageStream = new MemoryStream();
        jpeg.Save(imageStream);
        return imageStream.ToArray();
    }

    private static RenderTargetBitmap Render(FrameworkElement element, double scale)
    {
        var width = Math.Max(1, element.ActualWidth);
        var height = Math.Max(1, element.ActualHeight);
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(width * scale));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(height * scale));
        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        return bitmap;
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
