namespace MrtRouteSimulator.Engine;

/// <summary>將明列的轉換時間／安全係數套到實體渡線區，不以容量公式代替資源占用。</summary>
public static class TurnbackSignalMapper
{
    public static TopologyProjectDocument Apply(TopologyProjectDocument document, double interchangeSeconds, double safetyFactor)
    {
        var crossovers = document.Topology.Edges.Where(edge => edge.Kind == TrackEdgeKind.Crossover).ToArray();
        if (crossovers.Length == 0) throw new SimulationValidationException(["找不到實體渡線，不建立虛擬號誌。"]);
        var sections = crossovers.Select(edge => new TrackSignalSection(edge.TrackEdgeId, 0, edge.LengthMeters)).ToArray();
        var signals = crossovers.SelectMany(edge => new[] { TraversalDirection.Forward, TraversalDirection.Reverse }
            .Where(direction => edge.Directionality == TrackDirectionality.Bidirectional
                || (direction == TraversalDirection.Forward ? edge.Directionality == TrackDirectionality.ForwardOnly : edge.Directionality == TrackDirectionality.ReverseOnly))
            .Select(direction => new TrackSignalBlockDefinition("CAP-CROSSOVER:" + edge.TrackEdgeId + ":" + direction,
                new TrackPosition(edge.TrackEdgeId, direction == TraversalDirection.Forward ? 0 : edge.LengthMeters), direction, sections,
                interchangeSeconds, Math.Max(1, safetyFactor)))).ToArray();
        var result = document with { SignalBlocks = document.SignalBlocks.Where(signal => !signal.SignalId.StartsWith("CAP-CROSSOVER:", StringComparison.Ordinal)).Concat(signals).ToArray() };
        TopologyProjectFormat.Validate(result);
        return result;
    }
}
