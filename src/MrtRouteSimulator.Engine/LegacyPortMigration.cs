namespace MrtRouteSimulator.Engine;

/// <summary>需要由使用者明確補齊實體接軌側別的 legacy edge。</summary>
public sealed record LegacyPortMigrationItem(
    string TrackEdgeId,
    string FromNodeId,
    string ToNodeId);

/// <summary>
/// Schema 8 舊檔接軌側別遷移的唯讀盤點與明確套用入口。
/// 不從示意位置、畫面左右或 chainage 推測任何 A/B 值。
/// </summary>
public static class LegacyPortMigration
{
    public static IReadOnlyList<LegacyPortMigrationItem> FindUnspecifiedEdges(TopologyProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Topology?.Edges
            .Where(edge => edge.FromPortSide is null && edge.ToPortSide is null)
            .Select(edge => new LegacyPortMigrationItem(edge.TrackEdgeId, edge.FromNodeId, edge.ToNodeId))
            .ToArray() ?? [];
    }

    /// <summary>
    /// 將使用者逐 edge 提供的兩端側別套用到文件。所有未指定 edge 都必須有完整
    /// assignment；完成後仍以原有 Schema 8 validator 重新驗證，不放寬任何規則。
    /// </summary>
    public static TopologyProjectDocument ApplyExplicitSides(
        TopologyProjectDocument document,
        IReadOnlyDictionary<string, (TrackPortSide From, TrackPortSide To)> assignments)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(assignments);
        TopologyProjectFormat.Validate(document);

        var pending = FindUnspecifiedEdges(document);
        var pendingIds = pending.Select(item => item.TrackEdgeId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = assignments.Keys
            .Where(key => !pendingIds.Contains(key))
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unknown.Length > 0)
            throw new SimulationValidationException([
                $"接軌側別遷移包含不需要遷移或不存在的 edge：{string.Join("、", unknown)}。"
            ]);

        var missing = pending
            .Where(item => !assignments.ContainsKey(item.TrackEdgeId))
            .Select(item => item.TrackEdgeId)
            .ToArray();
        if (missing.Length > 0)
            throw new SimulationValidationException([
                $"接軌側別遷移尚未完成；請逐一確認 edge：{string.Join("、", missing)}。"
            ]);

        var edges = document.Topology.Edges.Select(edge =>
        {
            if (!pendingIds.Contains(edge.TrackEdgeId)) return edge;
            var (from, to) = assignments[edge.TrackEdgeId];
            if (!Enum.IsDefined(from) || !Enum.IsDefined(to))
                throw new SimulationValidationException([
                    $"edge「{edge.TrackEdgeId}」的實體接軌側別只能是 A 或 B。"
                ]);
            return edge with { FromPortSide = from, ToPortSide = to };
        }).ToArray();

        var migrated = document with { Topology = document.Topology with { Edges = edges } };
        TopologyProjectFormat.Validate(migrated);
        return migrated;
    }
}
