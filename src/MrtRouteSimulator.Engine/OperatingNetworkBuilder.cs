namespace MrtRouteSimulator.Engine;

/// <summary>
/// 可保存於 Schema 8 專案的營運路線規劃 metadata。它只描述建模意圖；實際執行仍以
/// <see cref="TopologyInfrastructureDefinition"/>、<see cref="ServiceRouteDefinition"/> 與
/// <see cref="SimulationWorld"/> 為權威。
/// </summary>
public sealed record OperatingLineDefinition
{
    public required string LineId { get; init; }
    public required string Name { get; init; }
    public int TrackCount { get; init; } = 2;
    public string? ParentLineId { get; init; }
    public double JunctionChainageMeters { get; init; }
    public double LengthMeters { get; init; } = 2_000;
    public int StationCount { get; init; } = 3;
    public required string OutboundServiceRouteId { get; init; }
    public required string InboundServiceRouteId { get; init; }
}

/// <summary>
/// 將營運路線規劃轉成既有 Schema 8 domain topology。支線可重用父線與祖先路線的共用
/// 幹線，但不建立第二套 runtime 或以畫面座標猜測實體接軌。
/// </summary>
public static class OperatingNetworkBuilder
{
    private const double DefaultSpeedLimitMetersPerSecond = 80d / 3.6d;
    private const double DefaultPlatformLengthMeters = 220d;
    private const double PositionToleranceMeters = 1e-6;

    /// <summary>驗證 document 上的營運路線 metadata 以及它和 ServiceRoute 的對應。</summary>
    public static void ValidateMetadata(TopologyProjectDocument document, List<string> errors)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(errors);
        if (document.OperatingLines is null)
            return;

        ValidateMetadata(document.OperatingLines, errors);
        var routes = document.ServiceRoutes
            .Where(route => !string.IsNullOrWhiteSpace(route.ServiceRouteId))
            .GroupBy(route => route.ServiceRouteId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var line in document.OperatingLines)
        {
            ValidateRoute(line, line.OutboundServiceRouteId, TrainDirection.Outbound);
            ValidateRoute(line, line.InboundServiceRouteId, TrainDirection.Inbound);
        }

        void ValidateRoute(OperatingLineDefinition line, string routeId, TrainDirection direction)
        {
            if (!routes.TryGetValue(routeId?.Trim() ?? string.Empty, out var route))
            {
                errors.Add($"營運路線「{line.LineId}」引用不存在的 {direction} ServiceRoute「{routeId}」。");
                return;
            }

            if (!string.Equals(route.OperatingLineId, line.LineId, StringComparison.OrdinalIgnoreCase)
                || route.Direction != direction)
            {
                errors.Add($"ServiceRoute「{routeId}」的營運路線或方向與「{line.LineId}」不一致。");
            }
        }
    }

    /// <summary>驗證 parent-before-child、接軌里程與 ID 唯一性等純 metadata 契約。</summary>
    public static void ValidateMetadata(IReadOnlyList<OperatingLineDefinition> lines, List<string> errors)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(errors);
        if (lines.Count == 0)
        {
            errors.Add("至少需要一條營運路線。");
            return;
        }

        var lineIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var routeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var lineId = line.LineId?.Trim() ?? string.Empty;
            if (lineId.Length == 0)
                errors.Add($"第 {index + 1} 條營運路線缺少 LineId。");
            else if (!lineIndexes.TryAdd(lineId, index))
                errors.Add($"營運路線 ID「{lineId}」重複。");
            if (string.IsNullOrWhiteSpace(line.Name))
                errors.Add($"營運路線「{lineId}」缺少名稱。");
            if (line.TrackCount is not (1 or 2))
                errors.Add($"營運路線「{lineId}」的軌道數只能是 1 或 2。");
            if (!double.IsFinite(line.LengthMeters) || line.LengthMeters <= 0)
                errors.Add($"營運路線「{lineId}」的長度必須是有限正數。");
            if (line.StationCount < 3)
                errors.Add($"營運路線「{lineId}」至少需要 3 站。");
            AddRouteId(line.OutboundServiceRouteId, "下行");
            AddRouteId(line.InboundServiceRouteId, "上行");

            if (string.IsNullOrWhiteSpace(line.ParentLineId))
            {
                if (Math.Abs(line.JunctionChainageMeters) > PositionToleranceMeters)
                    errors.Add($"獨立營運路線「{lineId}」的銜接里程必須是 0。");
            }
            else if (!double.IsFinite(line.JunctionChainageMeters))
            {
                errors.Add($"支線「{lineId}」的銜接里程必須是有限數值。");
            }

            void AddRouteId(string value, string label)
            {
                var routeId = value?.Trim() ?? string.Empty;
                if (routeId.Length == 0)
                    errors.Add($"營運路線「{lineId}」缺少{label} ServiceRoute ID。");
                else if (!routeIds.Add(routeId))
                    errors.Add($"ServiceRoute ID「{routeId}」重複。");
            }
        }

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line.ParentLineId))
                continue;
            var parentId = line.ParentLineId.Trim();
            if (!lineIndexes.TryGetValue(parentId, out var parentIndex))
            {
                errors.Add($"支線「{line.LineId}」引用不存在的父線「{parentId}」。");
                continue;
            }
            if (parentIndex >= index)
                errors.Add($"支線「{line.LineId}」的父線「{parentId}」必須排在支線之前，且不可形成循環。");
            var parent = lines[parentIndex];
            if (double.IsFinite(line.JunctionChainageMeters)
                && (line.JunctionChainageMeters < 0
                    || line.JunctionChainageMeters > parent.LengthMeters))
            {
                errors.Add($"支線「{line.LineId}」的銜接里程必須位於父線「{parentId}」的 0 至 {parent.LengthMeters:0.###} m 範圍內。");
            }
        }
    }

    /// <summary>
    /// 由營運路線 metadata 建立一份新 topology 起稿。每條線各產生上下行 ServiceRoute 與
    /// 手動班表車次；車型的物理性能沿用 template。
    /// </summary>
    public static TopologyProjectDocument Build(
        TopologyProjectDocument template,
        IReadOnlyList<OperatingLineDefinition> lines)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(lines);
        var errors = new List<string>();
        ValidateMetadata(lines, errors);
        RouteValidator.ThrowIfAny(errors);

        var normalizedLines = lines.Select(Normalize).ToArray();
        var nodes = new List<TrackNodeDefinition>();
        var edges = new List<TrackEdgeDefinition>();
        var stations = new List<StationDefinitionV4>();
        var platforms = new List<PlatformDefinitionV4>();
        var resources = new List<ConflictResourceDefinition>();
        var routes = new List<ServiceRouteDefinition>();
        var geometries = new Dictionary<string, LineGeometry>(StringComparer.OrdinalIgnoreCase);
        var junctionResourceIds = normalizedLines
            .Where(line => line.ParentLineId is not null)
            .ToDictionary(line => line.LineId, line => $"RESOURCE:SWITCH:{line.LineId}", StringComparer.OrdinalIgnoreCase);

        foreach (var line in normalizedLines.Where(line => line.TrackCount == 1))
        {
            resources.Add(new ConflictResourceDefinition(
                SingleTrackResourceId(line.LineId), $"{line.Name} 單線閉塞", ConflictResourceKind.Block));
        }
        foreach (var line in normalizedLines.Where(line => line.ParentLineId is not null))
        {
            resources.Add(new ConflictResourceDefinition(
                junctionResourceIds[line.LineId], $"{line.Name} 銜接道岔", ConflictResourceKind.Switch));
        }

        foreach (var line in normalizedLines)
        {
            var geometry = BuildLine(line);
            geometries.Add(line.LineId, geometry);
            var outboundRoute = CreateRoute(geometry, TrainDirection.Outbound);
            var inboundRoute = CreateRoute(geometry, TrainDirection.Inbound);
            routes.Add(outboundRoute);
            routes.Add(inboundRoute);

            // 每一階段都以目前已有的線路驗證，避免後續支線掩蓋較早的 topology 問題。
            var stagedTopology = CreateTopology(routes);
            InfrastructureValidator.ValidateAndThrow(stagedTopology, routes);
        }

        var topology = CreateTopology(routes);
        var stopPatterns = normalizedLines.Select(line =>
        {
            var geometry = geometries[line.LineId];
            var stationIds = geometry.OutboundPrefixStops
                .Concat(geometry.Stations.Select(station => station.OutboundStop))
                .Select(stop => stop.StationId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return new ProjectStopPattern(
                StopPatternId(line.LineId),
                $"{line.Name} 全停",
                stationIds.Select(stationId => new ProjectStopPatternInstruction(
                    stationId, StopPatternAction.Stop, template.Train.DefaultDwellTimeSeconds)).ToArray());
        }).ToArray();
        var defaultVehicleId = template.VehicleTypes.First().Id;
        var vehicleTypes = template.VehicleTypes.Select(vehicle =>
            vehicle with { DefaultStopPatternId = stopPatterns[0].Id }).ToArray();
        var serviceTypes = normalizedLines.Select((line, index) => new ProjectServiceType(
            ServiceTypeId(line.LineId),
            line.Name,
            ColorFor(index),
            $"{line.LineId}-",
            StopPatternId(line.LineId),
            defaultVehicleId)).ToArray();
        var manualRows = normalizedLines.SelectMany((line, index) =>
        {
            var geometry = geometries[line.LineId];
            var firstOutboundPlatform = geometry.OutboundPrefixStops
                .Concat(geometry.Stations.Select(station => station.OutboundStop))
                .First().CandidatePlatformIds[0];
            var firstInboundPlatform = geometry.Stations[^1].InboundStop.CandidatePlatformIds[0];
            return new[]
            {
                new ProjectManualTimetableRow(
                    PlannedDepartureTimeSeconds: index * 120d,
                    Direction: TrainDirection.Outbound,
                    ServiceTypeId: ServiceTypeId(line.LineId),
                    VehicleTypeId: defaultVehicleId,
                    StopPatternId: StopPatternId(line.LineId),
                    OriginPlatformId: firstOutboundPlatform,
                    ServiceRunId: $"RUN:{line.LineId}:DOWN:1",
                    ServiceRouteId: line.OutboundServiceRouteId),
                new ProjectManualTimetableRow(
                    PlannedDepartureTimeSeconds: index * 120d + 60d,
                    Direction: TrainDirection.Inbound,
                    ServiceTypeId: ServiceTypeId(line.LineId),
                    VehicleTypeId: defaultVehicleId,
                    StopPatternId: StopPatternId(line.LineId),
                    OriginPlatformId: firstInboundPlatform,
                    ServiceRunId: $"RUN:{line.LineId}:UP:1",
                    ServiceRouteId: line.InboundServiceRouteId)
            };
        }).ToArray();
        var primary = normalizedLines[0];
        var result = template with
        {
            VehicleTypes = vehicleTypes,
            ServiceTypes = serviceTypes,
            StopPatterns = stopPatterns,
            Dispatch = new ProjectDispatchPlan(
                DispatchPlanningMode.ManualTimetable,
                VehicleAssignmentMode.Automatic,
                ManualTimetableRows: manualRows),
            Simulation = template.Simulation with { TrainCount = manualRows.Length },
            Topology = topology,
            ServiceRoutes = routes.ToArray(),
            DirectionRouteBindings =
            [
                new TopologyDirectionRouteBinding(TrainDirection.Outbound, primary.OutboundServiceRouteId),
                new TopologyDirectionRouteBinding(TrainDirection.Inbound, primary.InboundServiceRouteId)
            ],
            OperatingLines = normalizedLines
        };
        errors.Clear();
        ValidateMetadata(result, errors);
        RouteValidator.ThrowIfAny(errors);
        TopologyProjectFormat.Validate(result);
        return result;

        LineGeometry BuildLine(OperatingLineDefinition line)
        {
            LineGeometry? parent = null;
            string startNodeId;
            IReadOnlyList<DirectedTrackTraversal> outboundPrefixTraversals;
            IReadOnlyList<DirectedTrackTraversal> inboundSuffixTraversals;
            IReadOnlyList<ServiceRouteStop> outboundPrefixStops;
            IReadOnlyList<ServiceRouteStop> inboundSuffixStops;
            if (line.ParentLineId is null)
            {
                startNodeId = NodeId(line.LineId, 0);
                outboundPrefixTraversals = [];
                inboundSuffixTraversals = [];
                outboundPrefixStops = [];
                inboundSuffixStops = [];
            }
            else
            {
                parent = geometries[line.ParentLineId];
                var junction = Canonical(line.JunctionChainageMeters);
                startNodeId = parent.NodeIdsByPosition[junction];
                outboundPrefixTraversals = OutboundPathTo(parent, junction);
                inboundSuffixTraversals = InboundPathToRoot(parent, junction);
                outboundPrefixStops = OutboundStopsTo(parent, junction);
                inboundSuffixStops = InboundStopsToRoot(parent, junction);
            }

            var stationPositions = Enumerable.Range(0, line.StationCount)
                .Select(index => Canonical(line.LengthMeters * index / (line.StationCount - 1d)))
                .ToArray();
            var childJunctions = normalizedLines
                .Where(candidate => string.Equals(candidate.ParentLineId, line.LineId, StringComparison.OrdinalIgnoreCase))
                .Select(candidate => Canonical(candidate.JunctionChainageMeters));
            var breakpoints = stationPositions.Concat(childJunctions).Append(0).Append(Canonical(line.LengthMeters))
                .Distinct().OrderBy(value => value).ToArray();
            var childJunctionSet = childJunctions.ToHashSet();
            var nodeIds = new Dictionary<double, string>();
            for (var index = 0; index < breakpoints.Length; index++)
            {
                var position = breakpoints[index];
                if (position == 0 && parent is not null)
                {
                    nodeIds[position] = startNodeId;
                    continue;
                }
                var nodeId = NodeId(line.LineId, index);
                nodeIds[position] = nodeId;
                nodes.Add(new TrackNodeDefinition(
                    nodeId,
                    $"{line.Name} {position:0.###} m",
                    childJunctionSet.Contains(position) ? TrackNodeKind.Junction : TrackNodeKind.Ordinary));
            }

            var segments = new List<LineSegment>(breakpoints.Length - 1);
            for (var index = 0; index < breakpoints.Length - 1; index++)
            {
                var start = breakpoints[index];
                var end = breakpoints[index + 1];
                var segmentResourceIds = ResourceIdsForSegment(line, start, end);
                if (line.TrackCount == 1)
                {
                    var edgeId = $"EDGE:{line.LineId}:SINGLE:{index + 1}";
                    edges.Add(CreateEdge(edgeId, nodeIds[start], nodeIds[end], end - start,
                        TrackDirectionality.Bidirectional, TrackPortSide.A, TrackPortSide.B,
                        segmentResourceIds, 0));
                    segments.Add(new LineSegment(
                        start, end,
                        new DirectedTrackTraversal(edgeId, TraversalDirection.Forward),
                        new DirectedTrackTraversal(edgeId, TraversalDirection.Reverse),
                        edgeId, edgeId));
                }
                else
                {
                    var downEdgeId = $"EDGE:{line.LineId}:DOWN:{index + 1}";
                    var upEdgeId = $"EDGE:{line.LineId}:UP:{index + 1}";
                    edges.Add(CreateEdge(downEdgeId, nodeIds[start], nodeIds[end], end - start,
                        TrackDirectionality.ForwardOnly, TrackPortSide.A, TrackPortSide.B,
                        segmentResourceIds, -1));
                    // 上行 edge 的實體方向由較大里程指向較小里程；使用 B→A port 契約，
                    // 可和單線 A→B 及雙線下行在 junction 明確相容。
                    edges.Add(CreateEdge(upEdgeId, nodeIds[end], nodeIds[start], end - start,
                        TrackDirectionality.ForwardOnly, TrackPortSide.B, TrackPortSide.A,
                        segmentResourceIds, 1));
                    segments.Add(new LineSegment(
                        start, end,
                        new DirectedTrackTraversal(downEdgeId, TraversalDirection.Forward),
                        new DirectedTrackTraversal(upEdgeId, TraversalDirection.Forward),
                        downEdgeId, upEdgeId));
                }
            }

            var stationRefs = new List<LineStation>(stationPositions.Length);
            for (var index = 0; index < stationPositions.Length; index++)
            {
                var position = stationPositions[index];
                var stationId = $"STATION:{line.LineId}:{index + 1}";
                var stationName = $"{line.Name} {index + 1}站";
                var downPlacement = ResolvePlatformPlacement(segments, position, TrainDirection.Outbound, line.TrackCount);
                var upPlacement = ResolvePlatformPlacement(segments, position, TrainDirection.Inbound, line.TrackCount);
                var downPlatformId = $"PLATFORM:{line.LineId}:{index + 1}:DOWN";
                var upPlatformId = $"PLATFORM:{line.LineId}:{index + 1}:UP";
                var downPlatform = CreatePlatform(downPlatformId, stationId, $"{stationName} 下行月台",
                    downPlacement.EdgeId, downPlacement.OffsetMeters, TrackDirection.Outbound);
                var upPlatform = CreatePlatform(upPlatformId, stationId, $"{stationName} 上行月台",
                    upPlacement.EdgeId, upPlacement.OffsetMeters, TrackDirection.Inbound);
                platforms.Add(downPlatform);
                platforms.Add(upPlatform);
                stations.Add(new StationDefinitionV4
                {
                    StationId = stationId,
                    Name = stationName,
                    DefaultDwellTimeSeconds = template.Train.DefaultDwellTimeSeconds,
                    PlatformIds = [downPlatformId, upPlatformId]
                });
                stationRefs.Add(new LineStation(
                    position,
                    new ServiceRouteStop { StationId = stationId, CandidatePlatformIds = [downPlatformId] },
                    new ServiceRouteStop { StationId = stationId, CandidatePlatformIds = [upPlatformId] }));
            }

            return new LineGeometry(
                line,
                nodeIds,
                segments,
                stationRefs,
                outboundPrefixTraversals,
                inboundSuffixTraversals,
                outboundPrefixStops,
                inboundSuffixStops);
        }

        TopologyInfrastructureDefinition CreateTopology(IReadOnlyList<ServiceRouteDefinition> currentRoutes)
        {
            var withoutConnections = new TopologyInfrastructureDefinition
            {
                Nodes = nodes.ToArray(),
                Edges = edges.ToArray(),
                Stations = stations.ToArray(),
                Platforms = platforms.ToArray(),
                Resources = resources.ToArray(),
                DirectedConnections = []
            };
            return withoutConnections with
            {
                DirectedConnections = DirectedTrackConnectionRules.Build(withoutConnections, currentRoutes)
            };
        }

        IReadOnlySet<string> ResourceIdsForSegment(OperatingLineDefinition line, double start, double end)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (line.TrackCount == 1)
                ids.Add(SingleTrackResourceId(line.LineId));
            if (line.ParentLineId is not null && Math.Abs(start) <= PositionToleranceMeters)
                ids.Add(junctionResourceIds[line.LineId]);
            foreach (var child in normalizedLines.Where(candidate =>
                         string.Equals(candidate.ParentLineId, line.LineId, StringComparison.OrdinalIgnoreCase)))
            {
                var junction = Canonical(child.JunctionChainageMeters);
                if (Math.Abs(start - junction) <= PositionToleranceMeters
                    || Math.Abs(end - junction) <= PositionToleranceMeters)
                    ids.Add(junctionResourceIds[child.LineId]);
            }
            return ids;
        }
    }

    /// <summary>
    /// 建立可重現的小型主支線逐站班表範例。所有里程、性能與時刻都是 synthetic test
    /// values；此 helper 是 sample 的原始來源，避免直接手寫 topology JSON。
    /// </summary>
    public static TopologyProjectDocument CreateExample(TopologyProjectDocument template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var main = new OperatingLineDefinition
        {
            LineId = "DEMO-MAIN",
            Name = "示範主線",
            TrackCount = 2,
            LengthMeters = 3_000,
            StationCount = 4,
            OutboundServiceRouteId = "DEMO-MAIN:DOWN",
            InboundServiceRouteId = "DEMO-MAIN:UP"
        };
        var branch = new OperatingLineDefinition
        {
            LineId = "DEMO-BRANCH",
            Name = "示範支線",
            TrackCount = 1,
            ParentLineId = main.LineId,
            JunctionChainageMeters = 750,
            LengthMeters = 1_500,
            StationCount = 4,
            OutboundServiceRouteId = "DEMO-BRANCH:DOWN",
            InboundServiceRouteId = "DEMO-BRANCH:UP"
        };
        var built = Build(template, [main, branch]);
        var routes = built.ServiceRoutes.ToDictionary(
            route => route.ServiceRouteId,
            StringComparer.OrdinalIgnoreCase);
        var rows = built.Dispatch.ManualTimetableRows!.Select(row =>
        {
            var route = routes[row.ServiceRouteId!];
            var intermediateStops = route.Stops.Skip(1).SkipLast(1).ToArray();
            var minimumDwellSeconds = route.OperatingLineId == branch.LineId ? 25d : 20d;
            return row with
            {
                StationTimetableEntries = intermediateStops.Select((stop, index) =>
                    new ProjectStationTimetableEntry(
                        stop.StationId,
                        row.PlannedDepartureTimeSeconds + (index + 1) * 180d,
                        minimumDwellSeconds)).ToArray()
            };
        }).ToArray();
        var example = built with
        {
            ProjectId = "SAMPLE-MAIN-BRANCH-TIMETABLE",
            ProjectName = "小型主支線逐站班表範例",
            Dispatch = built.Dispatch with { ManualTimetableRows = rows }
        };
        TopologyProjectFormat.Validate(example);
        return example;
    }

    private static ServiceRouteDefinition CreateRoute(LineGeometry geometry, TrainDirection direction)
    {
        var outbound = direction == TrainDirection.Outbound;
        return new ServiceRouteDefinition
        {
            ServiceRouteId = outbound
                ? geometry.Definition.OutboundServiceRouteId
                : geometry.Definition.InboundServiceRouteId,
            Name = $"{geometry.Definition.Name} {(outbound ? "下行" : "上行")}",
            OperatingLineId = geometry.Definition.LineId,
            Direction = direction,
            Traversals = outbound
                ? geometry.OutboundPrefixTraversals
                    .Concat(geometry.Segments.Select(segment => segment.OutboundTraversal)).ToArray()
                : geometry.Segments.AsEnumerable().Reverse()
                    .Select(segment => segment.InboundTraversal)
                    .Concat(geometry.InboundSuffixTraversals).ToArray(),
            Stops = outbound
                ? geometry.OutboundPrefixStops
                    .Concat(geometry.Stations.Select(station => station.OutboundStop)).ToArray()
                : geometry.Stations.AsEnumerable().Reverse()
                    .Select(station => station.InboundStop)
                    .Concat(geometry.InboundSuffixStops).ToArray()
        };
    }

    private static IReadOnlyList<DirectedTrackTraversal> OutboundPathTo(LineGeometry geometry, double position) =>
        geometry.OutboundPrefixTraversals
            .Concat(geometry.Segments.Where(segment => segment.EndMeters <= position + PositionToleranceMeters)
                .Select(segment => segment.OutboundTraversal))
            .ToArray();

    private static IReadOnlyList<DirectedTrackTraversal> InboundPathToRoot(LineGeometry geometry, double position) =>
        geometry.Segments.Where(segment => segment.EndMeters <= position + PositionToleranceMeters)
            .Reverse()
            .Select(segment => segment.InboundTraversal)
            .Concat(geometry.InboundSuffixTraversals)
            .ToArray();

    private static IReadOnlyList<ServiceRouteStop> OutboundStopsTo(LineGeometry geometry, double position) =>
        geometry.OutboundPrefixStops
            // 接軌點若正好落在父線車站，該站月台通常附著在分歧後的父線 edge；
            // 支線不行經該 edge，因此由支線自己的 0 m 車站表示同一接軌位置。
            .Concat(geometry.Stations.Where(station => station.PositionMeters < position - PositionToleranceMeters)
                .Select(station => station.OutboundStop))
            .ToArray();

    private static IReadOnlyList<ServiceRouteStop> InboundStopsToRoot(LineGeometry geometry, double position) =>
        geometry.Stations.Where(station => station.PositionMeters < position - PositionToleranceMeters)
            .Reverse()
            .Select(station => station.InboundStop)
            .Concat(geometry.InboundSuffixStops)
            .ToArray();

    private static PlatformPlacement ResolvePlatformPlacement(
        IReadOnlyList<LineSegment> segments,
        double position,
        TrainDirection direction,
        int trackCount)
    {
        var atTerminal = Math.Abs(position - segments[^1].EndMeters) <= PositionToleranceMeters;
        var segment = atTerminal
            ? segments[^1]
            : segments.First(item => Math.Abs(item.StartMeters - position) <= PositionToleranceMeters);
        if (direction == TrainDirection.Outbound)
            return new PlatformPlacement(segment.OutboundEdgeId, atTerminal ? segment.LengthMeters : 0);
        if (trackCount == 1)
            return new PlatformPlacement(segment.InboundEdgeId, atTerminal ? segment.LengthMeters : 0);
        return new PlatformPlacement(segment.InboundEdgeId, atTerminal ? 0 : segment.LengthMeters);
    }

    private static TrackEdgeDefinition CreateEdge(
        string edgeId,
        string fromNodeId,
        string toNodeId,
        double lengthMeters,
        TrackDirectionality directionality,
        TrackPortSide fromPortSide,
        TrackPortSide toPortSide,
        IReadOnlySet<string> conflictResourceIds,
        double schematicLane) => new()
        {
            TrackEdgeId = edgeId,
            FromNodeId = fromNodeId,
            ToNodeId = toNodeId,
            FromPortSide = fromPortSide,
            ToPortSide = toPortSide,
            LengthMeters = lengthMeters,
            Directionality = directionality,
            Kind = TrackEdgeKind.Mainline,
            DefaultSpeedLimitMetersPerSecond = DefaultSpeedLimitMetersPerSecond,
            ConflictResourceIds = conflictResourceIds,
            SchematicLane = schematicLane
        };

    private static PlatformDefinitionV4 CreatePlatform(
        string platformId,
        string stationId,
        string name,
        string edgeId,
        double offsetMeters,
        TrackDirection direction) => new()
        {
            PlatformId = platformId,
            StationId = stationId,
            Name = name,
            TrackEdgeId = edgeId,
            PlatformStartOffsetMeters = offsetMeters,
            PlatformEndOffsetMeters = offsetMeters,
            StopPositionOffsetMeters = offsetMeters,
            AllowedDirection = direction,
            EffectiveLengthMeters = DefaultPlatformLengthMeters,
            AllowsPassengerService = true
        };

    private static OperatingLineDefinition Normalize(OperatingLineDefinition line) => line with
    {
        LineId = line.LineId.Trim(),
        Name = line.Name.Trim(),
        ParentLineId = string.IsNullOrWhiteSpace(line.ParentLineId) ? null : line.ParentLineId.Trim(),
        JunctionChainageMeters = Canonical(line.JunctionChainageMeters),
        LengthMeters = Canonical(line.LengthMeters),
        OutboundServiceRouteId = line.OutboundServiceRouteId.Trim(),
        InboundServiceRouteId = line.InboundServiceRouteId.Trim()
    };

    private static double Canonical(double value) => Math.Round(value, 6, MidpointRounding.AwayFromZero);
    private static string NodeId(string lineId, int index) => $"NODE:{lineId}:{index + 1}";
    private static string SingleTrackResourceId(string lineId) => $"RESOURCE:BLOCK:{lineId}:SINGLE";
    private static string StopPatternId(string lineId) => $"STOP:{lineId}:ALL";
    private static string ServiceTypeId(string lineId) => $"SERVICE:{lineId}";
    private static string ColorFor(int index)
    {
        string[] colors = ["#1976D2", "#D32F2F", "#388E3C", "#7B1FA2", "#F57C00", "#00796B"];
        return colors[index % colors.Length];
    }

    private sealed record LineGeometry(
        OperatingLineDefinition Definition,
        IReadOnlyDictionary<double, string> NodeIdsByPosition,
        IReadOnlyList<LineSegment> Segments,
        IReadOnlyList<LineStation> Stations,
        IReadOnlyList<DirectedTrackTraversal> OutboundPrefixTraversals,
        IReadOnlyList<DirectedTrackTraversal> InboundSuffixTraversals,
        IReadOnlyList<ServiceRouteStop> OutboundPrefixStops,
        IReadOnlyList<ServiceRouteStop> InboundSuffixStops);

    private sealed record LineSegment(
        double StartMeters,
        double EndMeters,
        DirectedTrackTraversal OutboundTraversal,
        DirectedTrackTraversal InboundTraversal,
        string OutboundEdgeId,
        string InboundEdgeId)
    {
        public double LengthMeters => EndMeters - StartMeters;
    }

    private sealed record LineStation(
        double PositionMeters,
        ServiceRouteStop OutboundStop,
        ServiceRouteStop InboundStop);

    private readonly record struct PlatformPlacement(string EdgeId, double OffsetMeters);
}
