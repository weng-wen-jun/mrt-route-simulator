using MrtRouteSimulator.Engine;

internal static class StationPresentationTests
{
    public static void Schema8RoundTripsStationPresentationFields()
    {
        var source = CreatePresentationDocument();
        var json = TopologyProjectFormat.Serialize(source);
        var restored = TopologyProjectFormat.Deserialize(json);

        var restoredNode = restored.Topology.Nodes.Single(item => item.NodeId == "DN0");
        Equal(123.4d, restoredNode.SchematicPosition ?? double.NaN,
            "Schema 8 應保存 nullable 節點示意水平位置。");
        Equal(1d, restoredNode.SchematicLane ?? double.NaN,
            "Schema 8 應保存 nullable 節點示意股道位置。");
        Equal(-2.5, restored.Topology.Edges.Single(item => item.TrackEdgeId == "D0").SchematicLane ?? double.NaN,
            "Schema 8 應保存 nullable 示意股道位置。");
        var platform = restored.Topology.Platforms.Single(item => item.PlatformId == "A:D");
        Equal("2A", platform.PlatformNumber, "Schema 8 應保存月台號碼。");
        Equal("BODY-S", platform.PlatformBodyId, "Schema 8 應保存共用站體編號。");
        Equal(PlatformSide.Above, platform.DisplaySide, "Schema 8 應保存月台呈現側別。");

        var auto = restored.Topology.Platforms.Single(item => item.PlatformId == "A:U");
        Equal(PlatformSide.Auto, auto.DisplaySide, "未指定側別應以 Auto 往返保存。");
    }

    public static void RejectsInvalidStationPresentationFields()
    {
        var source = CreatePresentationDocument();
        var edge = source.Topology.Edges.Single(item => item.TrackEdgeId == "D0");
        var platform = source.Topology.Platforms.Single(item => item.PlatformId == "A:D");

        Throws<SimulationValidationException>(
            () => TopologyProjectFormat.Validate(source with
            {
                Topology = source.Topology with
                {
                    Edges = source.Topology.Edges.Select(item => item.TrackEdgeId == edge.TrackEdgeId
                        ? item with { SchematicLane = double.NaN } : item).ToArray()
                }
            }), "示意股道必須是");
        Throws<SimulationValidationException>(
            () => TopologyProjectFormat.Validate(source with
            {
                Topology = source.Topology with
                {
                    Nodes = source.Topology.Nodes.Select(item => item.NodeId == "DN0"
                        ? item with { SchematicPosition = double.NaN } : item).ToArray()
                }
            }), "示意座標無效");
        Throws<SimulationValidationException>(
            () => TopologyProjectFormat.Validate(source with
            {
                Topology = source.Topology with
                {
                    Edges = source.Topology.Edges.Select(item => item.TrackEdgeId == edge.TrackEdgeId
                        ? item with { SchematicLane = 8.01 } : item).ToArray()
                }
            }), "示意股道必須是");
        Throws<SimulationValidationException>(
            () => TopologyProjectFormat.Validate(source with
            {
                Topology = source.Topology with
                {
                    Nodes = source.Topology.Nodes.Select(item => item.NodeId == "DN0"
                        ? item with { SchematicLane = 8.01 } : item).ToArray()
                }
            }), "示意座標無效");
        Throws<SimulationValidationException>(
            () => TopologyProjectFormat.Validate(source with
            {
                Topology = source.Topology with
                {
                    Platforms = source.Topology.Platforms.Select(item => item.PlatformId == platform.PlatformId
                        ? item with { DisplaySide = (PlatformSide)99 } : item).ToArray()
                }
            }), "呈現側別無效");
    }

    public static void StationPresentationFieldsDoNotChangeRuntimeStopTrajectory()
    {
        var source = CreatePresentationDocument();
        var presentationVariant = source with
        {
            Topology = source.Topology with
            {
                Edges = source.Topology.Edges.Select(item => item with { SchematicLane = item.SchematicLane is null ? 7.5 : -7.5 }).ToArray(),
                Platforms = source.Topology.Platforms.Select(item => item with
                {
                    PlatformNumber = item.PlatformId == "A:D" ? "99" : item.PlatformId,
                    PlatformBodyId = "BODY-OTHER",
                    DisplaySide = item.DisplaySide == PlatformSide.Above ? PlatformSide.Below : PlatformSide.Above
                }).ToArray()
            }
        };

        var sourceRuntime = TopologyProjectFormat.CreateRuntime(source);
        var variantRuntime = TopologyProjectFormat.CreateRuntime(presentationVariant);
        var sourceWorld = CreateWorld(sourceRuntime);
        var variantWorld = CreateWorld(variantRuntime);
        sourceWorld.AdvanceTo(180);
        variantWorld.AdvanceTo(180);

        Equal(sourceWorld.Trajectory.Count, variantWorld.Trajectory.Count, "呈現欄位不應改變軌跡樣本數。");
        for (var index = 0; index < sourceWorld.Trajectory.Count; index++)
        {
            var expected = sourceWorld.Trajectory[index];
            var actual = variantWorld.Trajectory[index];
            Equal(expected.TrackEdgeId, actual.TrackEdgeId, "呈現欄位不應改變實體 edge 軌跡。");
            Equal(expected.ServiceRouteTraversalIndex, actual.ServiceRouteTraversalIndex, "呈現欄位不應改變 traversal 順序。");
            NearlyEqual(expected.OffsetMeters ?? double.NaN, actual.OffsetMeters ?? double.NaN);
            NearlyEqual(expected.SimulationTimeSeconds, actual.SimulationTimeSeconds);
        }

        var expectedStops = sourceWorld.Events.Where(item => item.EventType == SimulationEventType.Arrival
            || item.EventType == SimulationEventType.Departure).Select(item => (item.EventType, item.TrackEdgeId, item.PlatformId)).ToArray();
        var actualStops = variantWorld.Events.Where(item => item.EventType == SimulationEventType.Arrival
            || item.EventType == SimulationEventType.Departure).Select(item => (item.EventType, item.TrackEdgeId, item.PlatformId)).ToArray();
        Equal(expectedStops.Length, actualStops.Length, "呈現欄位不應改變實際停站事件數。");
        for (var index = 0; index < expectedStops.Length; index++)
        {
            Equal(expectedStops[index], actualStops[index], "呈現欄位不應改變實際停站軌跡。");
        }
    }

    private static SimulationWorld CreateWorld(TopologyProjectRuntime runtime) => new SimulationWorldOptions(
        Route: null,
        TrainParameters: runtime.TrainParameters,
        OperationalParameters: runtime.OperationalParameters,
        TrainCount: 1,
        InitialDepartureIntervalSeconds: 1,
        MovingBlockMode: MovingBlockMode.Independent,
        ServicePatterns: runtime.ServicePatterns,
        DispatchPlan: runtime.DispatchPlan,
        VehicleTypes: runtime.VehicleTypes,
        ServiceTypes: runtime.ServiceTypes,
        Topology: runtime.Topology).CreateWorld();

    private static TopologyProjectDocument CreatePresentationDocument()
    {
        var document = StationLayoutTemplateService.Build(StationLayoutTemplateKind.IslandTwoTracks);
        var topology = document.Topology with
        {
            Nodes = document.Topology.Nodes.Select(item => item.NodeId == "DN0"
                ? item with { SchematicPosition = 123.4, SchematicLane = 1 } : item).ToArray(),
            Edges = document.Topology.Edges.Select(item => item.TrackEdgeId == "D0"
                ? item with { SchematicLane = -2.5 } : item).ToArray(),
            Platforms = document.Topology.Platforms.Select(item => item.PlatformId == "A:D"
                ? item with { PlatformNumber = "2A", PlatformBodyId = "BODY-S", DisplaySide = PlatformSide.Above }
                : item.PlatformId == "A:U"
                ? item with { DisplaySide = PlatformSide.Auto }
                : item).ToArray()
        };
        return document with { Topology = topology };
    }

    private static void NearlyEqual(double expected, double actual, double tolerance = 0.000001)
    {
        if (!double.IsFinite(expected) || !double.IsFinite(actual) || Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"預期 {expected}，實際 {actual}。");
    }

    private static void Equal<T>(T expected, T actual, string message = "")
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} 預期 {expected}，實際 {actual}。");
    }

    private static void Throws<TException>(Action action, string expectedMessage)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception) when (exception.Message.Contains(expectedMessage, StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException($"預期拋出包含「{expectedMessage}」的 {typeof(TException).Name}。");
    }
}
