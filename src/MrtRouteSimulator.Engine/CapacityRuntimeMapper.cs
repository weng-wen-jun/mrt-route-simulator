namespace MrtRouteSimulator.Engine;

public sealed record CapacityRuntimeMappingResult(TopologyProjectDocument Document, IReadOnlyList<string> Notes);

/// <summary>明確套用容量輸入到既有實體路線；不使用理論Ts節流，不移動既有拓樸。</summary>
public static class CapacityRuntimeMapper
{
    private const string Prefix = "CAP-RUNTIME:";
    public static CapacityRuntimeMappingResult ApplyStations(TopologyProjectDocument document, TrainDirection? onlyDirection = null)
    {
        TopologyProjectFormat.Validate(document);
        var settings = document.SpatialCapacity ?? throw new SimulationValidationException(["請先建立容量設定。"]);
        var points = settings.Points.Where(point => point.Kind == SpatialReferencePointKind.IntermediateStation).ToArray();
        if (points.Length == 0) throw new SimulationValidationException(["目前套用功能需要至少一個中間站容量參考點；折返幾何須由實體設施設定。"]);
        if (points.Select(point => point.StationId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != points.Length)
            throw new SimulationValidationException(["同一車站不可同時套用多組容量運行參數。"]);
        var train = settings.Train;
        var graph = new InfrastructureGraphV4(document.Topology);
        var gradients = document.Topology.Gradients.Where(item => !item.GradientId.StartsWith(Prefix, StringComparison.Ordinal)).ToList();
        var limits = document.Topology.SpeedLimits.Where(item => !item.SpeedLimitId.StartsWith(Prefix, StringComparison.Ordinal)).ToList();
        var signals = document.SignalBlocks.Where(item => !item.SignalId.StartsWith(Prefix, StringComparison.Ordinal)).ToList();
        var patterns = document.StopPatterns.Where(pattern => !pattern.Id.StartsWith(Prefix, StringComparison.Ordinal)).ToList();
        var patternMap = new Dictionary<(string, TrainDirection), string>();
        var occupiedEdges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var direction in Enum.GetValues<TrainDirection>().Where(direction => onlyDirection is null || direction == onlyDirection))
        {
            var reverse = direction == TrainDirection.Inbound;
            var route = document.ServiceRoutes.Single(route => route.ServiceRouteId == document.DirectionRouteBindings.Single(binding => binding.Direction == direction).ServiceRouteId);
            var routeLength = route.Traversals.Sum(traversal => graph.Edges[traversal.TrackEdgeId].LengthMeters);
            var stops = ResolvedStopResolver.Resolve(graph, route);
            var mapped = points.Select(point =>
            {
                var stop = stops.SingleOrDefault(stop => stop.StationId.Equals(point.StationId, StringComparison.OrdinalIgnoreCase))
                    ?? throw new SimulationValidationException([$"{point.Name}未對應{direction}路線停站。"]);
                var platform = graph.Platforms[stop.PlatformId];
                var front = stop.ChainageMeters + (platform.StopPositionReference == StopPositionReference.TrainCenter ? train.LengthMeters / 2 : 0);
                return (Point: point, Stop: stop, Front: front);
            }).OrderBy(item => item.Front).ToArray();
            foreach (var original in document.StopPatterns.Where(pattern => !pattern.Id.StartsWith(Prefix, StringComparison.Ordinal)))
            {
                var id = Prefix + direction + ":" + original.Id;
                patternMap[(original.Id, direction)] = id;
                patterns.Add(original with { Id = id, DisplayName = original.DisplayName + "（容量運行" + direction + "）", Instructions = original.Instructions.Select(instruction =>
                {
                    var point = points.SingleOrDefault(point => point.StationId.Equals(instruction.StationId, StringComparison.OrdinalIgnoreCase));
                    return point is null ? instruction : instruction with { DwellTimeSeconds = reverse ? point.StationReverseDwellSeconds : point.StationForwardDwellSeconds };
                }).ToArray() });
            }
            for (var pi = 0; pi < mapped.Length; pi++)
            {
                var item = mapped[pi]; var point = item.Point;
                var start = pi == 0 ? 0 : (mapped[pi - 1].Front + item.Front) / 2;
                var end = pi == mapped.Length - 1 ? routeLength : (mapped[pi + 1].Front + item.Front) / 2;
                var gradeIn = reverse ? point.StationReverseGradeInPermille : point.StationForwardGradeInPermille;
                var gradeOut = reverse ? point.StationReverseGradeOutPermille : point.StationForwardGradeOutPermille;
                var speedIn = reverse ? point.StationReverseLaterCruiseSpeedMetersPerSecond : point.StationForwardLaterCruiseSpeedMetersPerSecond;
                var speedOut = reverse ? point.StationReverseEarlierCruiseSpeedMetersPerSecond : point.StationForwardEarlierCruiseSpeedMetersPerSecond;
                AddProfile(start, item.Front, gradeIn, speedIn, "IN");
                AddProfile(item.Front, end, gradeOut, speedOut, "OUT");
                var distanceToSignal = reverse ? point.StationReverseDistanceToSignalMeters : point.StationForwardDistanceToSignalMeters;
                var overlap = reverse ? point.StationReverseOverlapMeters : point.StationForwardOverlapMeters;
                var safety = reverse ? point.StationReverseSafetyFactor : point.StationForwardSafetyFactor;
                // Synthetic entry boundary is the upstream end of the stopped train.
                // Release requires the preceding rear to clear the downstream signal plus overlap.
                var entry = item.Front - train.LengthMeters;
                var release = item.Front + distanceToSignal + overlap;
                if (entry < start || release > end) throw new SimulationValidationException([$"{point.Name}的閉塞／重疊距離超出可配置區域；請先延長或明確編輯實體路線。"]);
                var protectedSections = Sections(entry, release).Select(part => new TrackSignalSection(part.EdgeId, part.Start, part.End)).ToArray();
                var signalLocation = PositionAt(entry);
                signals.Add(new TrackSignalBlockDefinition(Prefix + direction + ":" + point.ReferencePointId,
                    signalLocation.Position, signalLocation.Direction, protectedSections, settings.Global.ReactionTimeSeconds, Math.Max(1, safety)));

                void AddProfile(double from, double to, double grade, double speed, string suffix)
                {
                    var count = 0;
                    foreach (var part in Sections(from, to))
                    {
                        if (!occupiedEdges.Add(direction + ":" + part.EdgeId) && route.Traversals.Count(traversal => traversal.TrackEdgeId == part.EdgeId) > 1)
                            throw new SimulationValidationException(["重複edge的路線須手動指定容量運行區段。"]);
                        // Shared physical edges must have one physical grade, not incompatible per-route values.
                        var physicalGrade = part.Direction == TraversalDirection.Forward ? grade : -grade;
                        if (gradients.Any(existing => existing.TrackEdgeId == part.EdgeId && existing.StartOffsetMeters < part.End && existing.EndOffsetMeters > part.Start
                            && Math.Abs(existing.GradePermille - physicalGrade) > 1e-7))
                            throw new SimulationValidationException([$"edge「{part.EdgeId}」既有實體坡度與套用值衝突，請先於拓樸編輯器核對。"]);
                        var id = Prefix + direction + ":" + point.ReferencePointId + ":" + suffix + ":" + count++;
                        gradients.Add(new(id, part.EdgeId, part.Start, part.End, physicalGrade));
                        limits.Add(new TrackSpeedLimitDefinition { SpeedLimitId = id, TrackEdgeId = part.EdgeId, StartOffsetMeters = part.Start, EndOffsetMeters = part.End, LimitMetersPerSecond = speed, Direction = part.Direction });
                    }
                }
            }
            IEnumerable<(string EdgeId, double Start, double End, TraversalDirection Direction)> Sections(double from, double to)
            {
                var distance = 0d;
                foreach (var traversal in route.Traversals)
                {
                    var edge = graph.Edges[traversal.TrackEdgeId];
                    var lo = Math.Max(0, from - distance); var hi = Math.Min(edge.LengthMeters, to - distance);
                    if (hi > lo + 1e-7) yield return (edge.TrackEdgeId, traversal.Direction == TraversalDirection.Forward ? lo : edge.LengthMeters - hi,
                        traversal.Direction == TraversalDirection.Forward ? hi : edge.LengthMeters - lo, traversal.Direction);
                    distance += edge.LengthMeters;
                }
            }
            (TrackPosition Position, TraversalDirection Direction) PositionAt(double distance)
            {
                foreach (var traversal in route.Traversals)
                {
                    var edge = graph.Edges[traversal.TrackEdgeId];
                    if (distance < edge.LengthMeters - 1e-7) return (new TrackPosition(edge.TrackEdgeId, traversal.Direction == TraversalDirection.Forward ? distance : edge.LengthMeters - distance), traversal.Direction);
                    distance -= edge.LengthMeters;
                }
                throw new SimulationValidationException(["號誌位置超出實體路線。"]);
            }
        }
        string? Pattern(string? id, string serviceId, TrainDirection direction)
        {
            if (id?.StartsWith(Prefix, StringComparison.Ordinal) == true) id = id[(id.IndexOf(':', Prefix.Length) + 1)..];
            id ??= document.ServiceTypes.Single(service => service.Id == serviceId).DefaultStopPatternId ?? document.StopPatterns.First().Id;
            return patternMap.TryGetValue((id, direction), out var mappedId) ? mappedId : id;
        }
        var vmax = points.SelectMany(point => new[] { point.StationForwardEarlierCruiseSpeedMetersPerSecond, point.StationForwardLaterCruiseSpeedMetersPerSecond,
            point.StationReverseEarlierCruiseSpeedMetersPerSecond, point.StationReverseLaterCruiseSpeedMetersPerSecond }).Max();
        var result = document with
        {
            Topology = document.Topology with { Gradients = gradients, SpeedLimits = limits }, SignalBlocks = signals,
            VehicleTypes = document.VehicleTypes.Select(vehicle => vehicle with { LengthMeters = train.LengthMeters, MaxSpeedMetersPerSecond = vmax,
                AccelerationMetersPerSecondSquared = train.AccelerationMetersPerSecondSquared,
                ServiceBrakeDecelerationMetersPerSecondSquared = train.DecelerationMetersPerSecondSquared * train.BrakeEffect,
                EmergencyBrakeDecelerationMetersPerSecondSquared = Math.Max(train.DecelerationMetersPerSecondSquared, train.DecelerationMetersPerSecondSquared * train.BrakeEffect) }).ToArray(),
            Operations = document.Operations with { ControlReactionTimeSeconds = settings.Global.ReactionTimeSeconds },
            StopPatterns = patterns.ToArray(), Dispatch = document.Dispatch with {
                ManualTimetableRows = document.Dispatch.ManualTimetableRows?.Select(row => row with { StopPatternId = Pattern(row.StopPatternId, row.ServiceTypeId, row.Direction) }).ToArray(),
                SimpleHeadwayPlans = document.Dispatch.SimpleHeadwayPlans?.Select(row => row with { StopPatternId = Pattern(row.StopPatternId, row.ServiceTypeId, row.Direction) }).ToArray()
            }
        };
        TopologyProjectFormat.Validate(result);
        return new(result, ["已套用實體坡度、方向別速限／停站、車型性能、營運煞車有效因子與反應時間。",
            "進站授權採合成入口（停站車尾位置）及出站號誌＋overlap的保護區；前車車尾淨空後經反應時間開放。入口位置與坡度區域邊界是明列建模假設，非現地號誌設計。",
            "煞車安全係數擴大紅燈停止包絡；PHF與營運餘裕保留於理論分析，不直接限制實際派車；既有物理道岔與折返幾何不搬動。"]);
    }
}
