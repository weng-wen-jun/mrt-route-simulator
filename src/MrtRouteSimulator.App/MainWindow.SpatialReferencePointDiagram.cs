using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

public partial class MainWindow
{
    private void DrawSpatialReferencePointGeometry(
        double left,
        double trackWidth,
        double outboundY,
        double inboundY,
        double canvasWidth,
        double canvasHeight)
    {
        if (_route is null)
        {
            return;
        }

        IEnumerable<SpatialReferencePointInputRow> referencePoints = SpatialReferencePointRows.Count == 0
            ? GetCompleteSpatialReferencePointRows()
            : SpatialReferencePointRows;
        if (!referencePoints.Any())
        {
            return;
        }

        var explicitTailTrackStations = GetAfterStationTailTrackVisualLayouts()
            .Select(item => item.Station.StationId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var point in referencePoints)
        {
            var stationIndex = _route.Stations.ToList().FindIndex(station =>
                station.StationId.Equals(point.StationId, StringComparison.OrdinalIgnoreCase));
            if (stationIndex < 0)
            {
                continue;
            }

            var station = _route.Stations[stationIndex];
            var stationX = left + station.PositionMeters / _route.TotalLengthMeters * trackWidth;
            var interiorSign = stationIndex == _route.Stations.Count - 1 ? -1d : 1d;
            var exteriorSign = -interiorSign;
            var crossoverPixels = Math.Clamp(
                point.CrossoverLengthMeters / Math.Max(1, _route.TotalLengthMeters) * trackWidth,
                24,
                72);
            var color = point.Kind switch
            {
                "中間站" => Color.FromRgb(47, 107, 86),
                "銜接點" => Color.FromRgb(52, 125, 101),
                "中央避車線折返" => Color.FromRgb(121, 86, 173),
                "站後折返" => Color.FromRgb(188, 92, 52),
                _ => Color.FromRgb(42, 111, 162)
            };
            var tooltip = BuildSpatialReferencePointToolTip(point);

            if (point.Kind == "站後折返" && explicitTailTrackStations.Contains(point.StationId))
            {
                continue;
            }

            switch (point.Kind)
            {
                case "中間站":
                {
                    var marker = new Ellipse
                    {
                        Width = 14,
                        Height = 14,
                        Fill = Brushes.White,
                        Stroke = new SolidColorBrush(color),
                        StrokeThickness = 3,
                        ToolTip = tooltip
                    };
                    System.Windows.Controls.Canvas.SetLeft(marker, stationX - 7);
                    System.Windows.Controls.Canvas.SetTop(marker, (outboundY + inboundY) / 2 - 7);
                    RouteCanvas.Children.Add(marker);
                    AddReferenceLabel(ReferenceLabel(point.Name, "中間站"), Math.Clamp(stationX - 31, 0, canvasWidth - 70),
                        Math.Max(0, outboundY - 54), color, tooltip);
                    break;
                }
                case "銜接點":
                {
                    var sign = stationX < canvasWidth * 0.62 ? 1d : -1d;
                    var branchX = Math.Clamp(stationX + sign * Math.Max(48, crossoverPixels), left, left + trackWidth);
                    var branchY = Math.Max(20, outboundY - 54);
                    AddReferenceLine(stationX, outboundY, branchX, branchY, color, tooltip, 3);
                    AddReferenceLine(branchX, branchY, Math.Clamp(branchX + sign * 36, left, left + trackWidth), branchY,
                        color, tooltip, 3);
                    AddReferenceLabel($"{point.Name}\n銜接點", Math.Clamp(branchX - 32, 0, canvasWidth - 70),
                        Math.Max(0, branchY - 35), color, tooltip);
                    break;
                }
                case "站前折返":
                {
                    var centerX = Math.Clamp(stationX + interiorSign * Math.Max(28, crossoverPixels * 0.7), left, left + trackWidth);
                    DrawCrossover(centerX, crossoverPixels, outboundY, inboundY, color, tooltip);
                    AddReferenceLabel($"{point.Name}\n站前折返", Math.Clamp(centerX - 34, 0, canvasWidth - 74),
                        Math.Max(0, outboundY - 54), color, tooltip);
                    break;
                }
                case "站後折返":
                {
                    var tailEndX = Math.Clamp(stationX + exteriorSign * Math.Max(52, crossoverPixels), 10, canvasWidth - 10);
                    var midY = (outboundY + inboundY) / 2;
                    AddReferenceLine(stationX, outboundY, tailEndX, midY, color, tooltip, 2.5);
                    AddReferenceLine(stationX, inboundY, tailEndX, midY, color, tooltip, 2.5);
                    AddReferenceLine(tailEndX, midY, Math.Clamp(tailEndX + exteriorSign * 26, 8, canvasWidth - 8), midY,
                        color, tooltip, 4);
                    AddReferenceLabel($"{point.Name}\n站後折返", Math.Clamp(tailEndX - 35, 0, canvasWidth - 76),
                        Math.Min(canvasHeight - 34, midY + 8), color, tooltip);
                    break;
                }
                default:
                {
                    var midY = (outboundY + inboundY) / 2;
                    var pocketCenterX = Math.Clamp(stationX + interiorSign * Math.Max(38, crossoverPixels), left, left + trackWidth);
                    var pocketStartX = Math.Clamp(pocketCenterX - crossoverPixels * 0.55, left, left + trackWidth);
                    var pocketEndX = Math.Clamp(pocketCenterX + crossoverPixels * 0.55, left, left + trackWidth);
                    AddReferenceLine(pocketStartX, midY, pocketEndX, midY, color, tooltip, 5);
                    AddReferenceLine(pocketStartX, midY, pocketStartX - interiorSign * 18, outboundY, color, tooltip, 2.5);
                    AddReferenceLine(pocketEndX, midY, pocketEndX + interiorSign * 18, inboundY, color, tooltip, 2.5);
                    AddReferenceLabel($"{point.Name}\n中央避車線", Math.Clamp(pocketCenterX - 38, 0, canvasWidth - 82),
                        Math.Min(canvasHeight - 34, midY + 7), color, tooltip);
                    break;
                }
            }
        }

        void DrawCrossover(double centerX, double span, double upperY, double lowerY, Color color, string tooltip)
        {
            var half = span / 2;
            AddReferenceLine(centerX - half, upperY, centerX + half, lowerY, color, tooltip, 2.5);
            AddReferenceLine(centerX - half, lowerY, centerX + half, upperY, color, tooltip, 2.5);
        }

        void AddReferenceLine(double x1, double y1, double x2, double y2, Color color, string tooltip, double thickness)
        {
            RouteCanvas.Children.Add(new Line
            {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                Stroke = new SolidColorBrush(color), StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                ToolTip = tooltip
            });
        }

        void AddReferenceLabel(string text, double x, double y, Color color, string tooltip)
        {
            var label = new System.Windows.Controls.TextBlock
            {
                Text = text,
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(color),
                ToolTip = tooltip
            };
            System.Windows.Controls.Canvas.SetLeft(label, x);
            System.Windows.Controls.Canvas.SetTop(label, y);
            RouteCanvas.Children.Add(label);
        }

        static string ReferenceLabel(string name, string kind) =>
            name.Equals(kind, StringComparison.OrdinalIgnoreCase) ? kind : $"{name}\n{kind}";
    }

    private (double X, double Y) GetSpatialReferencePointTrainPosition(
        WorldTrainState state,
        double defaultX,
        double defaultY,
        double left,
        double trackWidth,
        double outboundY,
        double inboundY)
    {
        if (TryGetAfterStationTailTrackTrainPosition(
                state,
                left,
                trackWidth,
                outboundY,
                inboundY,
                out var tailTrackPosition))
        {
            return tailTrackPosition;
        }

        if (TryGetSpatialTurnbackTrainPosition(
                state,
                defaultX,
                defaultY,
                left,
                trackWidth,
                outboundY,
                inboundY,
                out var spatialTurnbackPosition))
        {
            return spatialTurnbackPosition;
        }

        if (_route is null || state.Phase != OperationalPhase.Turning)
        {
            return (defaultX, defaultY);
        }

        IEnumerable<SpatialReferencePointInputRow> referencePoints = SpatialReferencePointRows.Count == 0
            ? GetCompleteSpatialReferencePointRows()
            : SpatialReferencePointRows;
        var point = referencePoints.FirstOrDefault(row =>
            row.StationId.Equals(state.CurrentStationId, StringComparison.OrdinalIgnoreCase));
        var stationIndex = _route.Stations.ToList().FindIndex(station =>
            station.StationId.Equals(state.CurrentStationId, StringComparison.OrdinalIgnoreCase));
        if (point is null || stationIndex < 0 || point.Kind is "中間站" or "銜接點")
        {
            return (defaultX, defaultY);
        }

        var station = _route.Stations[stationIndex];
        var stationX = left + station.PositionMeters / _route.TotalLengthMeters * trackWidth;
        var interiorSign = stationIndex == _route.Stations.Count - 1 ? -1d : 1d;
        var sign = point.Kind == "站後折返" ? -interiorSign : interiorSign;
        var offset = point.Kind == "中央避車線折返" ? 42 : 34;
        return (Math.Clamp(stationX + sign * offset, left, left + trackWidth), (outboundY + inboundY) / 2);
    }

    private IReadOnlyList<AfterStationTailTrackVisualLayout> GetAfterStationTailTrackVisualLayouts()
    {
        if (_route is null || _latestPlaybackFrame?.Infrastructure is not { } infrastructure)
        {
            return [];
        }

        var result = new List<AfterStationTailTrackVisualLayout>();
        foreach (var plan in infrastructure.TurnbackPlans.Where(item => item.Kind == TurnbackKind.AfterStation))
        {
            var layout = infrastructure.FindAfterStationTailTrackLayout(plan);
            var stationIndex = _route.Stations.ToList().FindIndex(station =>
                station.StationId.Equals(plan.StationId, StringComparison.OrdinalIgnoreCase));
            if (layout is not null && stationIndex >= 0)
            {
                result.Add(new AfterStationTailTrackVisualLayout(plan, layout, _route.Stations[stationIndex], stationIndex));
            }
        }

        return result;
    }

    private void DrawAfterStationTailTrackGeometry(
        IReadOnlyList<AfterStationTailTrackVisualLayout> tailTrackLayouts,
        double left,
        double trackWidth,
        double outboundY,
        double inboundY,
        double canvasWidth,
        double canvasHeight)
    {
        var color = Color.FromRgb(188, 92, 52);
        var middleY = (outboundY + inboundY) / 2;
        foreach (var tailTrack in tailTrackLayouts)
        {
            var stationX = left + tailTrack.Station.PositionMeters / _route!.TotalLengthMeters * trackWidth;
            var nodeX = GetAfterStationTailTrackNodeX(tailTrack, left, trackWidth, canvasWidth);
            var tooltip = $"{tailTrack.Plan.Name}｜站後折返尾軌\n"
                + $"虛擬節點 {tailTrack.Layout.VirtualNodeId}｜{tailTrack.Layout.VirtualNodePositionMeters / 1000:0.00} km\n"
                + $"下行 {tailTrack.Layout.OutboundTrack.TrackId}｜上行 {tailTrack.Layout.InboundTrack.TrackId}";
            AddTailLine(stationX, outboundY, nodeX, middleY, tooltip);
            AddTailLine(stationX, inboundY, nodeX, middleY, tooltip);
            var node = new Ellipse
            {
                Width = 16,
                Height = 16,
                Fill = Brushes.White,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 3,
                ToolTip = tooltip
            };
            System.Windows.Controls.Canvas.SetLeft(node, nodeX - 8);
            System.Windows.Controls.Canvas.SetTop(node, middleY - 8);
            RouteCanvas.Children.Add(node);
            AddCanvasText(
                RouteCanvas,
                $"{tailTrack.Layout.VirtualNodeId}\n尾軌折返",
                Math.Clamp(nodeX - 38, 0, canvasWidth - 88),
                Math.Min(canvasHeight - 34, middleY + 12),
                9,
                color);

            void AddTailLine(double x1, double y1, double x2, double y2, string lineTooltip)
            {
                RouteCanvas.Children.Add(new Line
                {
                    X1 = x1,
                    Y1 = y1,
                    X2 = x2,
                    Y2 = y2,
                    Stroke = new SolidColorBrush(color),
                    StrokeThickness = 3,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    ToolTip = lineTooltip
                });
            }
        }
    }

    private bool TryGetAfterStationTailTrackTrainPosition(
        WorldTrainState state,
        double left,
        double trackWidth,
        double outboundY,
        double inboundY,
        out (double X, double Y) position)
    {
        position = default;
        var tailTrack = GetAfterStationTailTrackVisualLayouts().FirstOrDefault(item =>
            item.Layout.OutboundTrack.TrackId.Equals(state.TrackId, StringComparison.OrdinalIgnoreCase)
            || item.Layout.InboundTrack.TrackId.Equals(state.TrackId, StringComparison.OrdinalIgnoreCase));
        if (tailTrack is null)
        {
            return false;
        }

        var stationX = left + tailTrack.Station.PositionMeters / _route!.TotalLengthMeters * trackWidth;
        var nodeX = GetAfterStationTailTrackNodeX(tailTrack, left, trackWidth, RouteCanvas.ActualWidth);
        var length = Math.Abs(tailTrack.Layout.VirtualNodePositionMeters - tailTrack.Station.PositionMeters);
        var progress = length <= 1e-7
            ? 1
            : Math.Clamp(Math.Abs(state.FrontPositionMeters - tailTrack.Station.PositionMeters) / length, 0, 1);
        var middleY = (outboundY + inboundY) / 2;
        var usesOutboundTrack = tailTrack.Layout.OutboundTrack.TrackId.Equals(state.TrackId, StringComparison.OrdinalIgnoreCase);
        var y = usesOutboundTrack
            ? outboundY + (middleY - outboundY) * progress
            : inboundY + (middleY - inboundY) * progress;
        position = (stationX + (nodeX - stationX) * progress, y);
        return true;
    }

    private bool TryGetSpatialTurnbackTrainPosition(
        WorldTrainState state,
        double defaultX,
        double defaultY,
        double left,
        double trackWidth,
        double outboundY,
        double inboundY,
        out (double X, double Y) position)
    {
        position = default;
        const string prefix = "TURNBACK:";
        if (_route is null || !state.TrackId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        IEnumerable<SpatialReferencePointInputRow> referencePoints = SpatialReferencePointRows.Count == 0
            ? GetCompleteSpatialReferencePointRows()
            : SpatialReferencePointRows;
        var point = referencePoints.FirstOrDefault(item =>
            state.TrackId.StartsWith($"{prefix}{item.ReferencePointId}:", StringComparison.OrdinalIgnoreCase));
        if (point is null || point.Kind is not ("站前折返" or "中央避車線折返"))
        {
            return false;
        }

        var station = _route.Stations.FirstOrDefault(item =>
            item.StationId.Equals(point.StationId, StringComparison.OrdinalIgnoreCase));
        if (station is null)
        {
            return false;
        }

        var oneWayDistance = point.DistanceFromStopToCrossoverMeters + point.CrossoverLengthMeters;
        if (point.Kind == "中央避車線折返")
        {
            oneWayDistance += point.DistanceFromCrossoverToTurnbackStopMeters;
        }

        var progress = oneWayDistance <= 1e-7
            ? 1
            : Math.Clamp(Math.Abs(state.FrontPositionMeters - station.PositionMeters) / oneWayDistance, 0, 1);
        var isOutboundLeg = state.TrackId.EndsWith(":OUT", StringComparison.OrdinalIgnoreCase);
        var directionSign = isOutboundLeg ? (int)state.Direction : -(int)state.Direction;
        var stationX = left + station.PositionMeters / _route.TotalLengthMeters * trackWidth;
        var pixelDistance = Math.Clamp(
            oneWayDistance / Math.Max(1, _route.TotalLengthMeters) * trackWidth,
            point.Kind == "中央避車線折返" ? 38 : 28,
            point.Kind == "中央避車線折返" ? 82 : 64);
        var targetX = Math.Clamp(stationX + directionSign * pixelDistance, 10, RouteCanvas.ActualWidth - 10);
        var middleY = (outboundY + inboundY) / 2;
        var targetY = point.Kind == "中央避車線折返" ? middleY : (outboundY + inboundY) / 2;
        position = (
            defaultX + (targetX - defaultX) * progress,
            defaultY + (targetY - defaultY) * progress);
        return true;
    }

    private double GetAfterStationTailTrackNodeX(
        AfterStationTailTrackVisualLayout tailTrack,
        double left,
        double trackWidth,
        double canvasWidth)
    {
        var stationX = left + tailTrack.Station.PositionMeters / _route!.TotalLengthMeters * trackWidth;
        var exteriorSign = tailTrack.StationIndex == _route.Stations.Count - 1 ? 1d : -1d;
        var lengthPixels = Math.Clamp(
            Math.Abs(tailTrack.Layout.VirtualNodePositionMeters - tailTrack.Station.PositionMeters)
                / Math.Max(1, _route.TotalLengthMeters) * trackWidth,
            48,
            88);
        return Math.Clamp(stationX + exteriorSign * lengthPixels, 12, canvasWidth - 12);
    }

    private string GetV2CurrentLocation(WorldTrainState state)
    {
        var tailTrack = GetAfterStationTailTrackVisualLayouts().FirstOrDefault(item =>
            item.Layout.OutboundTrack.TrackId.Equals(state.TrackId, StringComparison.OrdinalIgnoreCase)
            || item.Layout.InboundTrack.TrackId.Equals(state.TrackId, StringComparison.OrdinalIgnoreCase));
        if (tailTrack is not null)
        {
            return $"{tailTrack.Layout.VirtualNodeId}（尾軌）";
        }

        const string prefix = "TURNBACK:";
        if (state.TrackId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            IEnumerable<SpatialReferencePointInputRow> referencePoints = SpatialReferencePointRows.Count == 0
                ? GetCompleteSpatialReferencePointRows()
                : SpatialReferencePointRows;
            var point = referencePoints
                .FirstOrDefault(item => state.TrackId.StartsWith(
                    $"{prefix}{item.ReferencePointId}:", StringComparison.OrdinalIgnoreCase));
            if (point is not null)
            {
                return $"{point.Name}（折返線）";
            }
        }

        return state.CurrentStationId;
    }

    private sealed record AfterStationTailTrackVisualLayout(
        TurnbackPlanDefinition Plan,
        AfterStationTailTrackLayout Layout,
        Station Station,
        int StationIndex);

    private static string BuildSpatialReferencePointToolTip(SpatialReferencePointInputRow point)
    {
        var details = point.Kind switch
        {
        "中間站" => $"{point.ReferencePointId}｜{point.Name}｜中間站\n"
            + $"順行坡度 {point.StationForwardGradeInPermille:0.##}／{point.StationForwardGradeOutPermille:0.##} ‰｜離開點 {point.StationForwardDistanceToSignalMeters:0.##} m｜重疊 {point.StationForwardOverlapMeters:0.##} m\n"
            + $"順行停站 {point.StationForwardDwellSeconds:0.##} s｜巡航 {point.StationForwardEarlierCruiseSpeedKmh:0.##}／{point.StationForwardLaterCruiseSpeedKmh:0.##} km/h｜安全係數 {point.StationForwardSafetyFactor:0.##}\n"
            + $"逆行坡度 {point.StationReverseGradeInPermille:0.##}／{point.StationReverseGradeOutPermille:0.##} ‰｜離開點 {point.StationReverseDistanceToSignalMeters:0.##} m｜重疊 {point.StationReverseOverlapMeters:0.##} m\n"
            + $"逆行停站 {point.StationReverseDwellSeconds:0.##} s｜巡航 {point.StationReverseEarlierCruiseSpeedKmh:0.##}／{point.StationReverseLaterCruiseSpeedKmh:0.##} km/h｜安全係數 {point.StationReverseSafetyFactor:0.##}",
        "銜接點" => $"{point.ReferencePointId}｜{point.Name}｜銜接點\n"
            + $"主線／側線坡度 {point.MainlineGradePermille:0.##}／{point.BranchlineGradePermille:0.##} ‰\n"
            + $"橫渡線 {point.CrossoverLengthMeters:0.##} m｜道岔 {point.SwitchSpeedLimitKmh:0.##} km/h\n"
            + $"巡航 {point.MainlineApproachCruiseSpeedKmh:0.##}／{point.BranchlineApproachCruiseSpeedKmh:0.##} km/h｜安全係數 {point.MainlineSafetyFactor:0.##}／{point.BranchlineSafetyFactor:0.##}｜主線比例 {point.MainlineTrafficRatio:0.##}",
        "站前折返" => $"{point.ReferencePointId}｜{point.Name}｜站前折返\n"
            + $"坡度 {point.MainlineGradePermille:0.##} ‰｜停車點至橫渡線 {point.DistanceFromStopToCrossoverMeters:0.##} m｜橫渡線 {point.CrossoverLengthMeters:0.##} m\n"
            + $"停站 {point.TurnbackDwellSeconds:0.##} s｜道岔 {point.SwitchSpeedLimitKmh:0.##} km/h｜進站前巡航 {point.MainlineApproachCruiseSpeedKmh:0.##} km/h｜安全係數 {point.MainlineSafetyFactor:0.##}"
            + (point.AlternateBerthing ? "\n站內交替停靠不同股道" : string.Empty),
        "站後折返" => $"{point.ReferencePointId}｜{point.Name}｜站後折返\n"
            + $"坡度 {point.MainlineGradePermille:0.##} ‰｜停車點至橫渡線 {point.DistanceFromStopToCrossoverMeters:0.##} m｜橫渡線 {point.CrossoverLengthMeters:0.##} m\n"
            + $"橫渡線至尾軌 {point.DistanceFromCrossoverToTurnbackStopMeters:0.##} m｜尾軌停等 {point.TurnbackDwellSeconds:0.##} s｜道岔 {point.SwitchSpeedLimitKmh:0.##} km/h"
            + (point.AlternateBerthing ? "\n尾軌交替停靠不同股道" : string.Empty),
        _ => $"{point.ReferencePointId}｜{point.Name}｜中央避車線折返\n"
            + $"坡度 {point.MainlineGradePermille:0.##} ‰｜停車點至橫渡線 {point.DistanceFromStopToCrossoverMeters:0.##} m｜橫渡線 {point.CrossoverLengthMeters:0.##} m\n"
            + $"橫渡線至避車線停車區 {point.DistanceFromCrossoverToTurnbackStopMeters:0.##} m｜折返停等 {point.TurnbackDwellSeconds:0.##} s｜道岔 {point.SwitchSpeedLimitKmh:0.##} km/h"
        };

        try
        {
            var definition = BuildSpatialReferencePointDefinitions([point]).Single();
            var forward = SpatialCapacityAnalysis.Calculate(definition);
            if (definition.Kind == SpatialReferencePointKind.IntermediateStation)
            {
                var reverse = SpatialCapacityAnalysis.Calculate(
                    definition,
                    direction: SpatialCapacityDirection.Reverse);
                return details
                    + $"\nURCS 相容設計班距：順行 {forward.DesignHeadwaySeconds:0.0} s／逆行 {reverse.DesignHeadwaySeconds:0.0} s"
                    + $"｜容量 {forward.LineCapacityPerHour}／{reverse.LineCapacityPerHour} 列／h";
            }

            return details
                + $"\nURCS 相容設計班距 {forward.DesignHeadwaySeconds:0.0} s｜容量 {forward.LineCapacityPerHour} 列／h";
        }
        catch (SimulationValidationException)
        {
            return details + "\nURCS 容量：參數組合尚未通過物理驗證";
        }
    }
}
