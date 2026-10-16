namespace MrtRouteSimulator.Engine;

/// <summary>實體號誌的保護區段。位置均以edge-local offset表示，與畫面里程無關。</summary>
public sealed record TrackSignalSection(string TrackEdgeId, double StartOffsetMeters, double EndOffsetMeters);

/// <summary>閉塞號誌；前車車尾離開所有保護區段並經轉換時間後才開放。安全係數只擴大停止包絡。</summary>
public sealed record TrackSignalBlockDefinition(
    string SignalId,
    TrackPosition StopPosition,
    TraversalDirection Direction,
    IReadOnlyList<TrackSignalSection> ProtectedSections,
    double ClearanceDelaySeconds = 0,
    double BrakingSafetyFactor = 1,
    bool DelayOnlyOnRouteChange = false);

public static class TrackSignalBlockValidation
{
    public static void Validate(IReadOnlyList<TrackSignalBlockDefinition> signals, InfrastructureGraphV4 infrastructure)
    {
        var errors = new List<string>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var signal in signals)
        {
            if (signal is null) { errors.Add("閉塞號誌不可為null。"); continue; }
            if (string.IsNullOrWhiteSpace(signal.SignalId) || !ids.Add(signal.SignalId)) errors.Add("閉塞號誌編號空白或重複。");
            if (!Enum.IsDefined(signal.Direction)) errors.Add($"號誌「{signal.SignalId}」方向無效。");
            if (string.IsNullOrWhiteSpace(signal.StopPosition.TrackEdgeId) || !infrastructure.Edges.TryGetValue(signal.StopPosition.TrackEdgeId, out var edge)
                || !double.IsFinite(signal.StopPosition.OffsetMeters) || signal.StopPosition.OffsetMeters < 0
                || signal.StopPosition.OffsetMeters > edge.LengthMeters) errors.Add($"號誌「{signal.SignalId}」停車線無效。");
            if (!double.IsFinite(signal.ClearanceDelaySeconds) || signal.ClearanceDelaySeconds < 0
                || !double.IsFinite(signal.BrakingSafetyFactor) || signal.BrakingSafetyFactor < 1)
                errors.Add($"號誌「{signal.SignalId}」轉換時間或煞車安全係數無效。");
            if (signal.ProtectedSections is not { Count: > 0 }) errors.Add($"號誌「{signal.SignalId}」缺少保護區段。");
            foreach (var section in signal.ProtectedSections ?? [])
                if (section is null || string.IsNullOrWhiteSpace(section.TrackEdgeId) || !infrastructure.Edges.TryGetValue(section.TrackEdgeId, out var protectedEdge)
                    || !double.IsFinite(section.StartOffsetMeters) || !double.IsFinite(section.EndOffsetMeters)
                    || section.StartOffsetMeters < 0 || section.EndOffsetMeters <= section.StartOffsetMeters
                    || section.EndOffsetMeters > protectedEdge.LengthMeters)
                    errors.Add($"號誌「{signal.SignalId}」保護區段無效。");
            // The block must start at its physical stop line, not after an unprotected gap.
            if (!(signal.ProtectedSections ?? []).Any(section => section is not null
                && section.TrackEdgeId.Equals(signal.StopPosition.TrackEdgeId, StringComparison.OrdinalIgnoreCase)
                && signal.StopPosition.OffsetMeters >= section.StartOffsetMeters - 1e-7
                && signal.StopPosition.OffsetMeters <= section.EndOffsetMeters + 1e-7))
                errors.Add($"號誌「{signal.SignalId}」保護區段必須包含停車線。");
        }
        if (errors.Count > 0) throw new SimulationValidationException(errors);
    }
}
