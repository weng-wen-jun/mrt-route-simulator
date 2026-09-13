using MrtRouteSimulator.Engine;

internal static class StationConstructionRuleTests
{
    public static void RejectsFalsePlatformCapacity()
    {
        var source = StationLayoutTemplateService.Build(StationLayoutTemplateKind.CentralPocket);
        Check(source with { Topology = source.Topology with {
            Platforms = source.Topology.Platforms.Select(p => p.PlatformId == "B:P"
                ? p with { EffectiveLengthMeters = 261 } : p).ToArray() } }, "STATION-002");
        foreach (var offset in new[] { 79d, 221d })
        {
            var invalid = source with { Topology = source.Topology with {
                Platforms = source.Topology.Platforms.Select(p => p.PlatformId == "B:P"
                    ? p with { StopPositionOffsetMeters = offset } : p).ToArray() } };
            try { TopologyProjectFormat.CreateRuntime(invalid); }
            catch (SimulationValidationException ex) when (ex.Message.Contains("STATION-003")) { continue; }
            throw new InvalidOperationException("中心停點距月台端僅59公尺，不能讓120公尺列車完整停靠。");
        }
        TopologyProjectFormat.CreateRuntime(source);
    }

    public static void RejectsInconsistentStationConstruction()
    {
        var source = StationLayoutTemplateService.Build(StationLayoutTemplateKind.FrontTurnback);
        Check(source with { Topology = source.Topology with {
            TurnbackFacilities = source.Topology.TurnbackFacilities.Select((f, i) => i == 0
                ? f with { DepartureStartOffsetMeters = f.DepartureStartOffsetMeters + 1 } : f).ToArray()
        } }, "TURN-001");
        var facility = source.Topology.TurnbackFacilities[0];
        Check(source with { Topology = source.Topology with {
            Platforms = source.Topology.Platforms.Select(p => p.TrackEdgeId == facility.ArrivalTrackEdgeId
                ? p with { StopPositionOffsetMeters = p.StopPositionOffsetMeters + 1 } : p).ToArray()
        } }, "TURN-002");
        var operation = source.Topology.TurnbackOperations[0];
        Check(source with { ServiceRoutes = source.ServiceRoutes.Select(r => r.ServiceRouteId == operation.DepartureServiceRouteId
            ? r with { Stops = r.Stops.Skip(1).ToArray() } : r).ToArray() }, "TURN-003");
        Check(source with { Topology = source.Topology with {
            Platforms = source.Topology.Platforms.Select(p => p.StationId == "A"
                ? p with { PlatformNumber = "1" } : p).ToArray()
        } }, "STATION-001");
        TopologyProjectFormat.Validate(source);
    }

    private static void Check(TopologyProjectDocument document, string code)
    {
        var result = InfrastructureValidator.Validate(document.Topology, document.ServiceRoutes);
        if (!result.Errors.Any(e => e.Contains(code)))
            throw new InvalidOperationException($"建置檢核未回報 {code}: {string.Join(";", result.Errors)}");
        try { TopologyProjectFormat.CreateRuntime(document); }
        catch (SimulationValidationException) { return; }
        throw new InvalidOperationException($"runtime 入口未攔截 {code}");
    }
}
