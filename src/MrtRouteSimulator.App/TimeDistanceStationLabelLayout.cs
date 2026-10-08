using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MrtRouteSimulator.App;

/// <summary>
/// Presentation-only input for a station label. <see cref="AnchorY"/> is the
/// actual station/grid coordinate and is never changed by the label layout.
/// </summary>
public sealed record TimeDistanceStationLabelSpec(
    string StationId,
    string Text,
    double AnchorY,
    Color Color,
    bool IsTail = false);

/// <summary>
/// A measured, clipped-to-canvas-safe label placement. The anchor remains the
/// source station coordinate; <see cref="CenterY"/> is allowed to move for
/// collision avoidance.
/// </summary>
public sealed record TimeDistanceStationLabelPlacement(
    string StationId,
    string Text,
    double AnchorY,
    double Left,
    double Top,
    double Width,
    double Height,
    double FontSize,
    Color Color,
    bool IsTail,
    double LeaderStartX,
    double LeaderEndX)
{
    public Rect Bounds => new(Left, Top, Width, Height);

    public double CenterY => Top + Height / 2;

    public bool IsDisplaced => Math.Abs(CenterY - AnchorY) > 0.25;
}

/// <summary>
/// Tags placed on time-distance label visuals. They make the station-to-label
/// relationship inspectable without deriving it from pixel proximity.
/// </summary>
public sealed record TimeDistanceStationLabelTag(string StationId, double AnchorY);

/// <summary>
/// Tag placed on the short leader line associated with a station label.
/// </summary>
public sealed record TimeDistanceStationLeaderTag(string StationId, double AnchorY);

/// <summary>
/// Generic measured label layout for the time-distance station sidebar.
///
/// The input station coordinates are authoritative. Labels are ordered by
/// their anchors, measured with WPF's actual TextBlock metrics, packed with a
/// small gap, and repacked from the relevant boundary when needed to remain
/// inside the canvas.
/// No station identifier is special-cased.
/// </summary>
public static class TimeDistanceStationLabelLayout
{
    public const double DefaultFontSize = 10;
    public const double DefaultMinimumFontSize = 7;
    public const double DefaultGap = 1.5;

    public static IReadOnlyList<TimeDistanceStationLabelPlacement> Arrange(
        IReadOnlyList<TimeDistanceStationLabelSpec> labels,
        double canvasWidth,
        double canvasHeight,
        double stationGridX,
        double topBoundary = 0,
        double bottomBoundary = double.PositiveInfinity,
        double preferredFontSize = DefaultFontSize,
        double minimumFontSize = DefaultMinimumFontSize,
        double gap = DefaultGap,
        double scaleFactor = 1)
    {
        ArgumentNullException.ThrowIfNull(labels);
        if (labels.Count == 0)
        {
            return [];
        }

        var safeWidth = Math.Max(1, canvasWidth);
        var safeHeight = Math.Max(1, canvasHeight);
        var top = Math.Clamp(topBoundary, 0, safeHeight);
        var minimumBottom = Math.Min(safeHeight, top + 1);
        var bottom = double.IsFinite(bottomBoundary)
            ? Math.Clamp(bottomBoundary, minimumBottom, safeHeight)
            : safeHeight;
        var preferred = Math.Clamp(preferredFontSize, 4, 72);
        var minimum = Math.Clamp(Math.Min(minimumFontSize, preferred), 4, preferred);
        var effectiveScale = Math.Clamp(scaleFactor, .75, 2.5);
        var effectiveGap = Math.Max(.25, gap) * effectiveScale;
        // Reserve a visible gutter between the measured label box and the
        // station grid so a displaced label gets a short, forward leader line
        // instead of a line that doubles back through its own text.
        var maximumLabelWidth = Math.Max(1, stationGridX - 16);

        var fontSize = FindFittingFontSize(
            labels,
            top,
            bottom,
            preferred,
            minimum,
            effectiveGap,
            maximumLabelWidth);
        var measurements = labels
            .Select(label => (Label: label, Size: Measure(label.Text, fontSize)))
            .ToArray();

        // Keep source order stable for equal anchors while packing in visual
        // order. This avoids labels jumping between frames at a shared y.
        var ordered = measurements
            .Select((item, index) => (item.Label, item.Size, Index: index))
            .OrderBy(item => item.Label.AnchorY)
            .ThenBy(item => item.Index)
            .ToArray();
        var tops = new double[ordered.Length];
        var cursor = top;
        for (var index = 0; index < ordered.Length; index++)
        {
            var item = ordered[index];
            var desiredTop = item.Label.AnchorY - item.Size.Height / 2;
            var placedTop = Math.Max(desiredTop, cursor);
            tops[index] = placedTop;
            cursor = placedTop + item.Size.Height + effectiveGap;
        }

        // Pack from the bottom when the forward pass reaches the boundary, then
        // repack from the top if necessary. The passes preserve a strict
        // monotonic gap; no independent per-label clamping can create duplicate
        // boxes at a canvas edge.
        if (tops[^1] + ordered[^1].Size.Height > bottom)
        {
            cursor = bottom;
            for (var index = ordered.Length - 1; index >= 0; index--)
            {
                var item = ordered[index];
                tops[index] = Math.Min(tops[index], cursor - item.Size.Height);
                cursor = tops[index] - effectiveGap;
            }
        }
        if (tops[0] < top)
        {
            cursor = top;
            for (var index = 0; index < ordered.Length; index++)
            {
                var item = ordered[index];
                tops[index] = Math.Max(tops[index], cursor);
                cursor = tops[index] + item.Size.Height + effectiveGap;
            }
        }

        var placements = new TimeDistanceStationLabelPlacement[ordered.Length];
        for (var index = 0; index < ordered.Length; index++)
        {
            var item = ordered[index];
            var width = Math.Max(1, item.Size.Width);
            var left = Math.Clamp(3d, 0, Math.Max(0, safeWidth - width));
            var labelTop = tops[index];
            var right = Math.Min(safeWidth, left + width);
            var leaderStartX = Math.Clamp(right + 1, 0, safeWidth);
            var leaderEndX = Math.Clamp(stationGridX - 4, 0, safeWidth);
            placements[item.Index] = new TimeDistanceStationLabelPlacement(
                item.Label.StationId,
                item.Label.Text,
                item.Label.AnchorY,
                left,
                labelTop,
                width,
                item.Size.Height,
                fontSize,
                item.Label.Color,
                item.Label.IsTail,
                leaderStartX,
                leaderEndX);
        }

        return placements;
    }

    public static TextBlock CreateTextBlock(TimeDistanceStationLabelPlacement placement)
    {
        var textBlock = new TextBlock
        {
            Text = placement.Text,
            FontSize = placement.FontSize,
            FontFamily = SystemFonts.MessageFontFamily,
            FontWeight = FontWeights.Normal,
            Foreground = new SolidColorBrush(placement.Color),
            TextWrapping = TextWrapping.NoWrap,
            Tag = new TimeDistanceStationLabelTag(placement.StationId, placement.AnchorY),
            SnapsToDevicePixels = true
        };
        Canvas.SetLeft(textBlock, placement.Left);
        Canvas.SetTop(textBlock, placement.Top);
        return textBlock;
    }

    public static Line CreateLeaderLine(TimeDistanceStationLabelPlacement placement)
    {
        var lineColor = Color.FromArgb(150, placement.Color.R, placement.Color.G, placement.Color.B);
        return new Line
        {
            X1 = placement.LeaderStartX,
            Y1 = placement.CenterY,
            X2 = placement.LeaderEndX,
            Y2 = placement.AnchorY,
            Stroke = new SolidColorBrush(lineColor),
            StrokeThickness = .75,
            Tag = new TimeDistanceStationLeaderTag(placement.StationId, placement.AnchorY),
            SnapsToDevicePixels = true
        };
    }

    private static double FindFittingFontSize(
        IReadOnlyList<TimeDistanceStationLabelSpec> labels,
        double top,
        double bottom,
        double preferred,
        double minimum,
        double gap,
        double maximumWidth)
    {
        var available = Math.Max(1, bottom - top);
        var size = preferred;
        while (size > minimum
            && (RequiredHeight(labels, size, gap) > available
                || RequiredWidth(labels, size) > maximumWidth))
        {
            size = Math.Max(minimum, size - .25);
        }

        // Extremely dense or unusually tall labels still get a deterministic
        // fit rather than clipping. This fallback is data-driven and applies
        // uniformly to every station.
        var requiredHeight = RequiredHeight(labels, size, gap);
        var requiredWidth = RequiredWidth(labels, size);
        if (requiredHeight > available || requiredWidth > maximumWidth)
        {
            // Find the smallest readable WPF font that satisfies both bounds;
            // unlike a one-shot ratio this remains safe for unusually dense
            // station sets and long localized names.
            var low = 2d;
            var high = size;
            for (var iteration = 0; iteration < 18; iteration++)
            {
                var candidate = (low + high) / 2;
                if (RequiredHeight(labels, candidate, gap) <= available
                    && RequiredWidth(labels, candidate) <= maximumWidth)
                {
                    // Candidate fits: keep searching upward for the largest
                    // readable size that still satisfies both bounds.
                    low = candidate;
                }
                else
                {
                    high = candidate;
                }
            }
            size = low;
        }
        return size;
    }

    private static double RequiredHeight(
        IReadOnlyList<TimeDistanceStationLabelSpec> labels,
        double fontSize,
        double gap)
    {
        var total = labels.Sum(label => Measure(label.Text, fontSize).Height);
        return total + Math.Max(0, labels.Count - 1) * gap;
    }

    private static double RequiredWidth(
        IReadOnlyList<TimeDistanceStationLabelSpec> labels,
        double fontSize) => labels.Max(label => Measure(label.Text, fontSize).Width);

    private static Size Measure(string text, double fontSize)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontFamily = SystemFonts.MessageFontFamily,
            FontWeight = FontWeights.Normal,
            TextWrapping = TextWrapping.NoWrap
        };
        textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return new Size(
            Math.Max(1, textBlock.DesiredSize.Width),
            Math.Max(1, textBlock.DesiredSize.Height));
    }
}
