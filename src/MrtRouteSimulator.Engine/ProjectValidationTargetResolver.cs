namespace MrtRouteSimulator.Engine;

internal static class ProjectValidationTargetResolver
{
    private sealed record OwnerDefinition(string Label, ProjectValidationTargetKind Kind, OwnerLookup Lookup = OwnerLookup.Direct);

    private enum OwnerLookup { Direct, TurnbackOperation, PassingOperation, StationOperation }

    private static readonly OwnerDefinition[] Owners =
    [
        new("折返設施到達", ProjectValidationTargetKind.TurnbackFacility),
        new("折返設施出發", ProjectValidationTargetKind.TurnbackFacility),
        new("折返設施停等", ProjectValidationTargetKind.TurnbackFacility),
        new("越行設施進入", ProjectValidationTargetKind.PassingFacility),
        new("越行設施離開", ProjectValidationTargetKind.PassingFacility),
        new("折返作業", ProjectValidationTargetKind.TurnbackFacility, OwnerLookup.TurnbackOperation),
        new("越行作業", ProjectValidationTargetKind.PassingFacility, OwnerLookup.PassingOperation),
        new("車站作業", ProjectValidationTargetKind.Station, OwnerLookup.StationOperation),
        new("軌道節點", ProjectValidationTargetKind.Node),
        new("軌道 edge", ProjectValidationTargetKind.Edge),
        new("車站", ProjectValidationTargetKind.Station),
        new("月台", ProjectValidationTargetKind.Platform),
        new("衝突資源", ProjectValidationTargetKind.Resource),
        new("軌道速限", ProjectValidationTargetKind.SpeedLimit),
        new("坡度區間", ProjectValidationTargetKind.Gradient),
        new("營運路線", ProjectValidationTargetKind.ServiceRoute),
        new("ServiceRoute", ProjectValidationTargetKind.ServiceRoute),
        new("停站模式", ProjectValidationTargetKind.StopPattern),
        new("車型", ProjectValidationTargetKind.VehicleType),
        new("服務類型", ProjectValidationTargetKind.ServiceType),
        new("折返設施", ProjectValidationTargetKind.TurnbackFacility),
        new("越行設施", ProjectValidationTargetKind.PassingFacility)
    ];

    public static ProjectValidationMessage CreateError(string error, TopologyProjectDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        ArgumentNullException.ThrowIfNull(document);

        // Dispatch rows have no stable ID in Schema 8. Their referenced catalog ID is never the owner.
        if (Has(error, "等間距派車") || Has(error, "手動班表"))
        {
            var dispatchKind = Has(error, "等間距派車")
                ? ProjectValidationTargetKind.HeadwayPlan
                : ProjectValidationTargetKind.ManualTimetable;
            return new ProjectValidationMessage(
                ProjectValidationSeverity.Error,
                error,
                null,
                dispatchKind,
                InferFieldName(error, dispatchKind));
        }

        var referenceIndex = error.IndexOf("引用", StringComparison.Ordinal);
        var owner = Owners
            .Select(definition => TryReadOwner(error, definition, out var id, out var index)
                ? (Definition: definition, Id: id, Index: index)
                : (Definition: (OwnerDefinition?)null, Id: string.Empty, Index: int.MaxValue))
            .Where(candidate => candidate.Definition is not null
                && (referenceIndex < 0 || candidate.Index < referenceIndex))
            .OrderBy(candidate => candidate.Index)
            .ThenByDescending(candidate => candidate.Definition!.Label.Length)
            .FirstOrDefault();

        if (owner.Definition is not null)
        {
            var targetId = ResolveTargetId(owner.Definition.Lookup, owner.Id, document);
            return new ProjectValidationMessage(
                ProjectValidationSeverity.Error,
                error,
                targetId,
                owner.Definition.Kind,
                InferFieldName(error, owner.Definition.Kind));
        }

        var kind = InferPageTargetKind(error);
        return new ProjectValidationMessage(
            ProjectValidationSeverity.Error,
            error,
            null,
            kind,
            InferFieldName(error, kind));
    }

    private static bool TryReadOwner(string error, OwnerDefinition owner, out string id, out int index)
    {
        index = error.IndexOf(owner.Label, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            id = string.Empty;
            return false;
        }

        var valueStart = index + owner.Label.Length;
        if (error.Length >= valueStart + 2
            && error.AsSpan(valueStart, 2).SequenceEqual("編號")) valueStart += 2;
        if (valueStart >= error.Length || error[valueStart] != '「')
        {
            id = string.Empty;
            return false;
        }

        valueStart++;
        var valueEnd = error.IndexOf('」', valueStart);
        if (valueEnd <= valueStart)
        {
            id = string.Empty;
            return false;
        }

        id = error[valueStart..valueEnd];
        return true;
    }

    private static string? ResolveTargetId(OwnerLookup lookup, string ownerId, TopologyProjectDocument document) => lookup switch
    {
        OwnerLookup.Direct => ownerId,
        OwnerLookup.TurnbackOperation => document.Topology.TurnbackOperations
            .FirstOrDefault(item => Same(item.OperationId, ownerId))?.FacilityId,
        OwnerLookup.PassingOperation => document.Topology.PassingOperations
            .FirstOrDefault(item => Same(item.OperationId, ownerId))?.FacilityId,
        OwnerLookup.StationOperation => document.Topology.StationOperations
            .FirstOrDefault(item => Same(item.StationOperationId, ownerId))?.StationId,
        _ => null
    };

    private static ProjectValidationTargetKind InferPageTargetKind(string error)
    {
        if (Has(error, "等間距派車")) return ProjectValidationTargetKind.HeadwayPlan;
        if (Has(error, "手動班表")) return ProjectValidationTargetKind.ManualTimetable;
        if (Has(error, "折返設施") || Has(error, "折返作業")) return ProjectValidationTargetKind.TurnbackFacility;
        if (Has(error, "越行設施") || Has(error, "越行作業")) return ProjectValidationTargetKind.PassingFacility;
        if (Has(error, "軌道速限")) return ProjectValidationTargetKind.SpeedLimit;
        if (Has(error, "坡度區間")) return ProjectValidationTargetKind.Gradient;
        if (Has(error, "衝突資源")) return ProjectValidationTargetKind.Resource;
        if (Has(error, "月台")) return ProjectValidationTargetKind.Platform;
        if (Has(error, "車站")) return ProjectValidationTargetKind.Station;
        if (Has(error, "軌道 edge") || Has(error, "有向道岔轉向")) return ProjectValidationTargetKind.Edge;
        if (Has(error, "軌道節點")) return ProjectValidationTargetKind.Node;
        if (Has(error, "ServiceRoute") || Has(error, "營運路線")) return ProjectValidationTargetKind.ServiceRoute;
        if (Has(error, "停站模式")) return ProjectValidationTargetKind.StopPattern;
        if (Has(error, "車型")) return ProjectValidationTargetKind.VehicleType;
        if (Has(error, "服務類型")) return ProjectValidationTargetKind.ServiceType;
        if (Has(error, "模擬設定")) return ProjectValidationTargetKind.Simulation;
        if (Has(error, "專案") || Has(error, "存檔") || Has(error, "Schema 8")) return ProjectValidationTargetKind.Project;
        return ProjectValidationTargetKind.Unknown;
    }

    private static string? InferFieldName(string error, ProjectValidationTargetKind kind) => kind switch
    {
        ProjectValidationTargetKind.Project when Has(error, "專案編號") => "ProjectId",
        ProjectValidationTargetKind.Project when Has(error, "專案名稱") => "ProjectName",
        ProjectValidationTargetKind.Node when Has(error, "編號") => "Id",
        ProjectValidationTargetKind.Node when Has(error, "名稱") => "Name",
        ProjectValidationTargetKind.Node when Has(error, "種類") => "Kind",
        ProjectValidationTargetKind.Edge when Has(error, "編號") => "Id",
        ProjectValidationTargetKind.Edge when Has(error, "起點節點") => "From",
        ProjectValidationTargetKind.Edge when Has(error, "終點節點") => "To",
        ProjectValidationTargetKind.Edge when Has(error, "長度") => "LengthMeters",
        ProjectValidationTargetKind.Edge when Has(error, "預設速限") => "DefaultSpeedKmh",
        ProjectValidationTargetKind.Edge when Has(error, "方向設定") => "Directionality",
        ProjectValidationTargetKind.Edge when Has(error, "類型") => "Kind",
        ProjectValidationTargetKind.Station when Has(error, "編號") => "Id",
        ProjectValidationTargetKind.Station when Has(error, "名稱") => "Name",
        ProjectValidationTargetKind.Station when Has(error, "預設停站時間") => "DefaultDwellSeconds",
        ProjectValidationTargetKind.Station when Has(error, "月台") => "Platforms",
        ProjectValidationTargetKind.Platform when Has(error, "編號") => "Id",
        ProjectValidationTargetKind.Platform when Has(error, "名稱") => "Name",
        ProjectValidationTargetKind.Platform when Has(error, "引用不存在的車站") => "Station",
        ProjectValidationTargetKind.Platform when Has(error, "引用不存在的軌道 edge") => "TrackEdge",
        ProjectValidationTargetKind.Platform when Has(error, "有效長度") => "EffectiveLengthMeters",
        ProjectValidationTargetKind.Platform when Has(error, "允許方向") => "Direction",
        ProjectValidationTargetKind.Resource when Has(error, "編號") => "Id",
        ProjectValidationTargetKind.Resource when Has(error, "名稱") => "Name",
        ProjectValidationTargetKind.Resource when Has(error, "類型") => "Kind",
        ProjectValidationTargetKind.SpeedLimit when Has(error, "編號") => "Id",
        ProjectValidationTargetKind.SpeedLimit when Has(error, "引用不存在的軌道 edge") => "TrackEdge",
        ProjectValidationTargetKind.SpeedLimit when Has(error, "速限必須") => "LimitKmh",
        ProjectValidationTargetKind.SpeedLimit when Has(error, "方向") => "Direction",
        ProjectValidationTargetKind.Gradient when Has(error, "編號") => "Id",
        ProjectValidationTargetKind.Gradient when Has(error, "引用不存在的軌道 edge") => "TrackEdge",
        ProjectValidationTargetKind.Gradient when Has(error, "坡度必須") => "GradePermille",
        ProjectValidationTargetKind.ServiceRoute when Has(error, "編號") => "Id",
        ProjectValidationTargetKind.ServiceRoute when Has(error, "名稱") => "Name",
        ProjectValidationTargetKind.ServiceRoute when Has(error, "候選月台") => "CandidatePlatforms",
        ProjectValidationTargetKind.ServiceRoute when Has(error, "停靠引用不存在的車站") => "Station",
        ProjectValidationTargetKind.ServiceRoute when Has(error, "traversal") && Has(error, "edge") => "TrackEdge",
        ProjectValidationTargetKind.ServiceRoute when Has(error, "traversal") && Has(error, "方向") => "Direction",
        ProjectValidationTargetKind.StopPattern when Has(error, "編號") => "Id",
        ProjectValidationTargetKind.StopPattern when Has(error, "topology 車站") => "Station",
        ProjectValidationTargetKind.StopPattern when Has(error, "無效動作") => "Action",
        ProjectValidationTargetKind.VehicleType when Has(error, "編號") => "Id",
        ProjectValidationTargetKind.VehicleType when Has(error, "停站模式") => "DefaultStopPattern",
        ProjectValidationTargetKind.ServiceType when Has(error, "編號") => "Id",
        ProjectValidationTargetKind.ServiceType when Has(error, "引用不存在的車型") => "DefaultVehicleType",
        ProjectValidationTargetKind.ServiceType when Has(error, "引用不存在的停站模式") => "DefaultStopPattern",
        ProjectValidationTargetKind.ServiceType when Has(error, "偏好月台") => "PreferredPlatforms",
        ProjectValidationTargetKind.HeadwayPlan => DispatchField(error),
        ProjectValidationTargetKind.ManualTimetable => DispatchField(error),
        ProjectValidationTargetKind.TurnbackFacility when Has(error, "名稱") => "Name",
        ProjectValidationTargetKind.TurnbackFacility when Has(error, "類型") || Has(error, "種類") => "Kind",
        ProjectValidationTargetKind.PassingFacility when Has(error, "名稱") => "Name",
        _ => null
    };

    private static string? DispatchField(string error)
    {
        if (Has(error, "服務類型")) return "ServiceType";
        if (Has(error, "車型")) return "VehicleType";
        if (Has(error, "停站模式")) return "StopPattern";
        if (Has(error, "起始月台")) return "OriginPlatform";
        return null;
    }

    private static bool Has(string value, string text) =>
        value.Contains(text, StringComparison.OrdinalIgnoreCase);

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
