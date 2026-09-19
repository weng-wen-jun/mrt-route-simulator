using MrtRouteSimulator.Engine;

internal static class LegacyPortMigrationTests
{
    public static void ListsOnlyEdgesWithBothSidesMissing()
    {
        var source = StationLayoutTemplateService.Build(StationLayoutTemplateKind.IslandTwoTracks);
        var edge = source.Topology.Edges[0];
        var partial = source with
        {
            Topology = source.Topology with
            {
                Edges = source.Topology.Edges.Select(item => item.TrackEdgeId == edge.TrackEdgeId
                    ? item with { FromPortSide = null, ToPortSide = null }
                    : item.TrackEdgeId == source.Topology.Edges[1].TrackEdgeId
                        ? item with { FromPortSide = null }
                    : item).ToArray()
            }
        };

        var result = LegacyPortMigration.FindUnspecifiedEdges(partial);
        Equal(1, result.Count, "遷移盤點應只列出兩端皆未指定的 edge。 ");
        Equal(edge.TrackEdgeId, result[0].TrackEdgeId, "盤點應保留 edge 識別碼。 ");
    }

    public static void AppliesOnlyExplicitAssignmentsAndRevalidates()
    {
        var source = StationLayoutTemplateService.Build(StationLayoutTemplateKind.IslandTwoTracks);
        var edge = source.Topology.Edges[0];
        var missing = source with
        {
            Topology = source.Topology with
            {
                Edges = source.Topology.Edges
                    .Select(item => item with { FromPortSide = null, ToPortSide = null })
                    .ToArray()
            }
        };

        var assignments = source.Topology.Edges.ToDictionary(
            item => item.TrackEdgeId,
            item => (item.FromPortSide!.Value, item.ToPortSide!.Value),
            StringComparer.OrdinalIgnoreCase);
        var migrated = LegacyPortMigration.ApplyExplicitSides(
            missing,
            assignments);

        var restored = migrated.Topology.Edges.Single(item => item.TrackEdgeId == edge.TrackEdgeId);
        Equal(edge.FromPortSide, restored.FromPortSide, "遷移後起點側別必須來自明確 assignment。 ");
        Equal(edge.ToPortSide, restored.ToPortSide, "遷移後終點側別必須來自明確 assignment。 ");
        TopologyProjectFormat.Validate(migrated);
    }

    public static void NeverInfersMissingAssignment()
    {
        var source = StationLayoutTemplateService.Build(StationLayoutTemplateKind.IslandTwoTracks);
        var edge = source.Topology.Edges[0];
        var missing = source with
        {
            Topology = source.Topology with
            {
                Edges = source.Topology.Edges
                    .Select(item => item with { FromPortSide = null, ToPortSide = null })
                    .ToArray()
            }
        };

        Throws<SimulationValidationException>(
            () => LegacyPortMigration.ApplyExplicitSides(missing,
                new Dictionary<string, (TrackPortSide From, TrackPortSide To)>()),
            "尚未提供 assignment 時必須要求使用者明確確認。 ");
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}預期={expected}，實際={actual}。 ");
    }

    private static void Throws<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }
}
