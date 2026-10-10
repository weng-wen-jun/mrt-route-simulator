using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MrtRouteSimulator.App;

public enum ChartLegendMarker
{
    Line,
    Dash,
    Dot,
    Point
}

public sealed record ChartLegendItem(string Label, Brush Brush, ChartLegendMarker Marker);

/// <summary>
/// 結果圖表的共用繪圖工具：取整數值刻度、軸線、時間刻度、圖例、標題與空白提示。
/// 只負責外觀；資料換算由各圖依 <see cref="DrawValueAxis"/> 回傳的刻度最大值自行處理，
/// 讓線與刻度對齊。
/// </summary>
public static class ChartPainter
{
    public const string LegendTag = "ChartLegend";
    public const string TitleTag = "ChartTitle";
    public const string MessageTag = "ChartMessage";
    public const string GridTag = "ChartGrid";
    public const string ValueTickTag = "ChartTick";
    public const string TimeTickTag = "ChartTimeTick";

    private static readonly Size Unbounded = new(double.PositiveInfinity, double.PositiveInfinity);

    public readonly record struct ChartArea(double Left, double Top, double Width, double Height)
    {
        public double Right => Left + Width;
        public double Bottom => Top + Height;
    }

    /// <summary>
    /// 以 1／2／5×10ⁿ 為間距、從 0 開始且最後一格不小於 <paramref name="maxValue"/> 的刻度。
    /// 非有限值或非正值回傳 0、1，避免座標換算產生 NaN 或無限大。
    /// </summary>
    public static double[] NiceTicks(double maxValue, int targetCount = 5)
    {
        if (!double.IsFinite(maxValue) || maxValue <= 0) return [0, 1];
        var rough = maxValue / Math.Max(1, targetCount);
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var normalized = rough / magnitude;
        var step = (normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10) * magnitude;
        // 極小（次正規）值會讓間距下溢為 0，極大值會讓最後一格溢位；兩者都退回 0 與最大值兩格。
        if (!(step > 0) || !double.IsFinite(step)) return [0, maxValue];
        var rawCount = Math.Ceiling(maxValue / step - 1e-9);
        if (!(rawCount <= 1000) || !double.IsFinite(step * rawCount)) return [0, maxValue];
        var count = Math.Max(1, (int)rawCount);
        return Enumerable.Range(0, count + 1).Select(index => index * step).ToArray();
    }

    /// <summary>畫橫向格線、Y 刻度文字、兩條軸線與單位；回傳刻度最大值，資料以它換算座標。</summary>
    public static double DrawValueAxis(Canvas canvas, ChartArea area, double maxValue, string unit)
    {
        var ticks = NiceTicks(maxValue);
        var top = ticks[^1];
        foreach (var value in ticks)
        {
            var y = area.Bottom - value / top * area.Height;
            if (value > 0)
            {
                canvas.Children.Add(new Line
                {
                    X1 = area.Left,
                    X2 = area.Right,
                    Y1 = y,
                    Y2 = y,
                    Stroke = ChartTheme.Grid,
                    StrokeThickness = ChartTheme.GridThickness,
                    Tag = GridTag
                });
            }

            var label = CreateLabel(value.ToString("0.###", CultureInfo.InvariantCulture), ChartTheme.AxisLabel);
            label.Tag = ValueTickTag;
            label.Measure(Unbounded);
            Place(canvas, label, Math.Max(0, area.Left - 4 - label.DesiredSize.Width), y - label.DesiredSize.Height / 2);
        }

        DrawAxes(canvas, area, unit, string.Empty);
        return top;
    }

    /// <summary>畫左側與下方兩條軸線；軸名為空字串時不加文字。</summary>
    public static void DrawAxes(Canvas canvas, ChartArea area, string verticalLabel, string horizontalLabel)
    {
        canvas.Children.Add(new Line
        {
            X1 = area.Left,
            X2 = area.Left,
            Y1 = area.Top,
            Y2 = area.Bottom,
            Stroke = ChartTheme.Axis,
            StrokeThickness = ChartTheme.AxisThickness
        });
        canvas.Children.Add(new Line
        {
            X1 = area.Left,
            X2 = area.Right,
            Y1 = area.Bottom,
            Y2 = area.Bottom,
            Stroke = ChartTheme.Axis,
            StrokeThickness = ChartTheme.AxisThickness
        });
        if (!string.IsNullOrEmpty(verticalLabel)) Place(canvas, CreateLabel(verticalLabel, ChartTheme.AxisLabel), 3, 2);
        if (!string.IsNullOrEmpty(horizontalLabel))
            Place(canvas, CreateLabel(horizontalLabel, ChartTheme.AxisLabel), area.Right - 48, area.Bottom + 12);
    }

    /// <summary>畫時間刻度短線與置中的刻度文字；文字不超出繪圖區左右界。</summary>
    public static void DrawTimeAxis(Canvas canvas, ChartArea area, IReadOnlyList<(double X, string Label)> ticks)
    {
        foreach (var (x, text) in ticks)
        {
            canvas.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = area.Bottom,
                Y2 = area.Bottom + 4,
                Stroke = ChartTheme.Axis,
                StrokeThickness = 1
            });
            var label = CreateLabel(text, ChartTheme.AxisLabel, ChartTheme.TimeTickFontSize);
            label.Tag = TimeTickTag;
            label.Measure(Unbounded);
            var width = label.DesiredSize.Width;
            Place(canvas, label,
                Math.Clamp(x - width / 2, area.Left - 2, Math.Max(area.Left - 2, area.Right - width)),
                area.Bottom + 5);
        }
    }

    public static StackPanel DrawLegend(Canvas canvas, double left, double top, IReadOnlyList<ChartLegendItem> items)
    {
        var legend = CreateLegend(items);
        Place(canvas, legend, left, top);
        return legend;
    }

    public static TextBlock DrawTitle(Canvas canvas, string text, double left, double top)
    {
        var title = CreateTitle(text);
        Place(canvas, title, left, top);
        return title;
    }

    /// <summary>
    /// 標題與圖例排成一列；畫布太窄放不下時省略標題（下拉選單已顯示同樣資訊），圖例一律保留。
    /// </summary>
    public static (TextBlock? Title, StackPanel Legend) DrawHeader(
        Canvas canvas, string title, IReadOnlyList<ChartLegendItem> items, double left, double top)
    {
        var titleBlock = CreateTitle(title);
        var legend = CreateLegend(items);
        titleBlock.Measure(Unbounded);
        legend.Measure(Unbounded);
        var canvasWidth = double.IsNaN(canvas.Width) ? canvas.ActualWidth : canvas.Width;
        var legendLeft = left + titleBlock.DesiredSize.Width + 16;
        var showTitle = canvasWidth <= 0 || legendLeft + legend.DesiredSize.Width <= canvasWidth - 4;
        if (showTitle) Place(canvas, titleBlock, left, top);
        else legendLeft = left;
        Place(canvas, legend, legendLeft,
            top + Math.Max(0, (titleBlock.DesiredSize.Height - legend.DesiredSize.Height) / 2));
        return (showTitle ? titleBlock : null, legend);
    }

    public static void DrawMessage(Canvas canvas, string text)
    {
        var message = CreateLabel(text, ChartTheme.Message, ChartTheme.MessageFontSize);
        message.Tag = MessageTag;
        Place(canvas, message, 18, 18);
    }

    public static Polyline CreateSeries(Brush brush, double thickness, DoubleCollection? dash = null) => new()
    {
        Stroke = brush,
        StrokeThickness = thickness,
        StrokeDashArray = dash,
        StrokeLineJoin = PenLineJoin.Round
    };

    public static TextBlock CreateLabel(string text, Brush brush, double fontSize = ChartTheme.AxisLabelFontSize) => new()
    {
        Text = text,
        Foreground = brush,
        FontSize = fontSize,
        FontFamily = ChartTheme.Font
    };

    public static void Place(Canvas canvas, UIElement element, double left, double top)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        canvas.Children.Add(element);
    }

    private static TextBlock CreateTitle(string text)
    {
        var title = CreateLabel(text, ChartTheme.Title, ChartTheme.TitleFontSize);
        title.FontWeight = FontWeights.SemiBold;
        title.Tag = TitleTag;
        return title;
    }

    private static StackPanel CreateLegend(IReadOnlyList<ChartLegendItem> items)
    {
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Tag = LegendTag };
        foreach (var item in items)
        {
            legend.Children.Add(CreateLegendMarker(item));
            var label = CreateLabel(item.Label, ChartTheme.LegendText, ChartTheme.LegendFontSize);
            label.Margin = new Thickness(0, 0, 12, 0);
            label.VerticalAlignment = VerticalAlignment.Center;
            legend.Children.Add(label);
        }

        return legend;
    }

    private static FrameworkElement CreateLegendMarker(ChartLegendItem item)
    {
        if (item.Marker == ChartLegendMarker.Point)
        {
            return new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = item.Brush,
                Stroke = ChartTheme.MarkerOutline,
                StrokeThickness = 1,
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        return new Line
        {
            X1 = 0,
            X2 = 18,
            Y1 = 5,
            Y2 = 5,
            Width = 18,
            Height = 10,
            Stroke = item.Brush,
            StrokeThickness = 2,
            StrokeDashArray = item.Marker switch
            {
                ChartLegendMarker.Dash => [3, 2],
                ChartLegendMarker.Dot => [1, 1.5],
                _ => null
            },
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
    }
}
