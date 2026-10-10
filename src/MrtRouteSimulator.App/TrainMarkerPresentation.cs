using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

/// <summary>配線圖列車膠囊的純呈現規則；位置與方向一律由 Engine 提供的位置推得。</summary>
internal static class TrainMarkerPresentation
{
    public const double MarkerHeight = 18;
    public const double MinimumWidth = 34;
    private const int MaximumLabelLength = 10;
    private static readonly char[] Separators = ['-', '_', ' '];
    private static readonly Geometry RightChevron = Frozen("M0,0 L3.5,3.5 L0,7");
    private static readonly Geometry LeftChevron = Frozen("M3.5,0 L0,3.5 L3.5,7");
    // 寬度只由標籤與固定字型決定；快取後每幀不再重新量測（僅在 UI 執行緒使用）。
    private static readonly Dictionary<string, double> LabelWidths = new(StringComparer.Ordinal);

    public static string Label(string vehicleId)
    {
        if (string.IsNullOrWhiteSpace(vehicleId)) return "?";
        if (vehicleId.StartsWith("Vehicle ", StringComparison.Ordinal)
            && int.TryParse(vehicleId.AsSpan(8), out var legacyNumber))
        {
            return $"V{legacyNumber:00}";
        }

        if (vehicleId.StartsWith("AUTO-", StringComparison.OrdinalIgnoreCase))
        {
            return "A" + vehicleId[5..].TrimStart('0').PadLeft(2, '0');
        }

        if (vehicleId.Length <= MaximumLabelLength) return vehicleId;
        var tokens = vehicleId.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        var label = tokens.Length <= 1
            ? vehicleId
            : string.Concat(tokens[..^1].Select(token => StringInfo.GetNextTextElement(token, 0).ToUpperInvariant()))
                + "-" + tokens[^1];
        return label.Length <= MaximumLabelLength ? label : TruncateTextElements(label, MaximumLabelLength - 1) + "…";
    }

    // 依完整字元（text element）截斷，不把 emoji 等補充平面字元切成半個。
    private static string TruncateTextElements(string text, int maximumLength)
    {
        var builder = new StringBuilder();
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            if (builder.Length + element.Length > maximumLength) break;
            builder.Append(element);
        }
        return builder.ToString();
    }

    public static int Heading(Point? front, Point center, TrainDirection direction)
    {
        if (front is { } head && Math.Abs(head.X - center.X) >= .5) return head.X > center.X ? 1 : -1;
        return direction == TrainDirection.Outbound ? 1 : -1;
    }

    public static Border Create(string vehicleId, Brush fill, int heading, string toolTip)
    {
        var label = Label(vehicleId);
        var text = new TextBlock
        {
            Text = label,
            Foreground = Brushes.White,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        // 明確指定 App 字型，讓量測與實際繪製使用同一套字型。
        if (Application.Current?.TryFindResource("AppFont") is FontFamily appFont) text.FontFamily = appFont;
        if (!LabelWidths.TryGetValue(label, out var textWidth))
        {
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            textWidth = Math.Ceiling(text.DesiredSize.Width);
            LabelWidths[label] = textWidth;
        }
        var chevron = new System.Windows.Shapes.Path
        {
            Data = heading > 0 ? RightChevron : LeftChevron,
            Stroke = Brushes.White,
            StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = heading > 0 ? new Thickness(3, 0, 0, 0) : new Thickness(0, 0, 3, 0)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = heading > 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto
        });
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = heading > 0 ? GridLength.Auto : new GridLength(1, GridUnitType.Star)
        });
        Grid.SetColumn(text, heading > 0 ? 0 : 1);
        Grid.SetColumn(chevron, heading > 0 ? 1 : 0);
        grid.Children.Add(text);
        grid.Children.Add(chevron);
        return new Border
        {
            Width = Math.Max(MinimumWidth, textWidth + 24),
            Height = MarkerHeight,
            CornerRadius = new CornerRadius(MarkerHeight / 2),
            Background = fill,
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(6, 0, 6, 0),
            Child = grid,
            ToolTip = toolTip
        };
    }

    // 以列車中心置中；只在畫布邊界 clamp，不改變停站時與月台中心的對位。
    public static void Place(FrameworkElement marker, Point center, double canvasWidth, double canvasHeight)
    {
        Canvas.SetLeft(marker, Math.Clamp(center.X - marker.Width / 2, 0, Math.Max(0, canvasWidth - marker.Width)));
        Canvas.SetTop(marker, Math.Clamp(center.Y - marker.Height / 2, 0, Math.Max(0, canvasHeight - marker.Height)));
        Panel.SetZIndex(marker, 5);
    }

    private static Geometry Frozen(string data)
    {
        var geometry = Geometry.Parse(data);
        if (!geometry.IsFrozen) geometry.Freeze();
        return geometry;
    }
}
