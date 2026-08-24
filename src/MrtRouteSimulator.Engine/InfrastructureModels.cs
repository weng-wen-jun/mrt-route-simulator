using System.Collections.ObjectModel;

namespace MrtRouteSimulator.Engine;

/// <summary>股道可服務的運行方向。</summary>
public enum TrackDirection
{
    Both = 0,
    Outbound = 1,
    Inbound = -1,
    Down = 1,
    Up = -1
}

/// <summary>股道的營運用途。</summary>
public enum TrackKind
{
    Mainline,
    Platform,
    Passing,
    Siding,
    Crossover,
    Turnback,
    TailTrack,
    Approach,
    Other
}

/// <summary>月台配置策略。</summary>
public enum PlatformAllocationStrategy
{
    Fixed,
    RoundRobin,
    EarliestAvailable,
    Scheduled,
    Automatic
}

/// <summary>端點折返形式。</summary>
public enum TurnbackKind
{
    LegacyAbstract,
    BeforeStation,
    AfterStation
}

/// <summary>
/// 站場月台的不可變定義。允許清單為空時表示不限制車型或服務類型。
/// </summary>
public sealed class PlatformDefinition
{
    public PlatformDefinition(
        string platformId,
        string stationId,
        string name,
        TrackDirection direction,
        double effectiveLengthMeters,
        double stoppingPositionMeters = 0,
        bool allowsPassengerService = true,
        IEnumerable<string>? allowedVehicleTypeIds = null,
        IEnumerable<string>? allowedServiceTypeIds = null,
        IEnumerable<string>? trackSegmentIds = null)
    {
        PlatformId = NormalizeRequired(platformId, "月台編號");
        StationId = NormalizeRequired(stationId, "月台所屬車站編號");
        Name = string.IsNullOrWhiteSpace(name) ? PlatformId : name.Trim();
        Direction = direction;
        EffectiveLengthMeters = effectiveLengthMeters;
        StoppingPositionMeters = stoppingPositionMeters;
        AllowsPassengerService = allowsPassengerService;
        AllowedVehicleTypeIds = ToFrozenIds(allowedVehicleTypeIds, "允許車型編號");
        AllowedServiceTypeIds = ToFrozenIds(allowedServiceTypeIds, "允許服務類型編號");
        TrackSegmentIds = ToFrozenIds(trackSegmentIds, "月台股道編號");
        Validate();
    }

    public PlatformDefinition(
        string platformId,
        string stationId,
        TrackDirection direction,
        double effectiveLengthMeters,
        IEnumerable<string>? allowedVehicleTypeIds = null,
        IEnumerable<string>? allowedServiceTypeIds = null)
        : this(platformId, stationId, platformId, direction, effectiveLengthMeters, 0, true,
            allowedVehicleTypeIds, allowedServiceTypeIds)
    {
    }

    public string PlatformId { get; }
    public string StationId { get; }
    public string Name { get; }
    public TrackDirection Direction { get; }
    public double EffectiveLengthMeters { get; }
    public double StoppingPositionMeters { get; }
    public bool AllowsPassengerService { get; }
    public IReadOnlySet<string> AllowedVehicleTypeIds { get; }
    public IReadOnlySet<string> AllowedServiceTypeIds { get; }
    public IReadOnlySet<string> TrackSegmentIds { get; }

    public bool IsCompatible(
        TrackDirection direction,
        double trainLengthMeters,
        string? vehicleTypeId = null,
        string? serviceTypeId = null,
        bool requirePassengerService = false)
    {
        if (requirePassengerService && !AllowsPassengerService)
        {
            return false;
        }

        if (!DirectionMatches(Direction, direction)
            || !double.IsFinite(trainLengthMeters)
            || trainLengthMeters <= 0
            || trainLengthMeters > EffectiveLengthMeters + 1e-9)
        {
            return false;
        }

        return IsAllowed(AllowedVehicleTypeIds, vehicleTypeId)
            && IsAllowed(AllowedServiceTypeIds, serviceTypeId);
    }

    private void Validate()
    {
        var errors = new List<string>();
        RequireDirection(Direction, "月台方向", errors);
        RequirePositive(EffectiveLengthMeters, "月台有效長度", errors);
        RequireNonNegative(StoppingPositionMeters, "月台停車點", errors);
        if (StoppingPositionMeters > EffectiveLengthMeters + 1e-9)
        {
            errors.Add("月台停車點不可超過月台有效長度。");
        }

        Throw(errors);
    }

    internal static bool IsAllowed(IReadOnlySet<string> allowed, string? value)
        => allowed.Count == 0 || (!string.IsNullOrWhiteSpace(value) && allowed.Contains(value.Trim()));

    internal static bool DirectionMatches(TrackDirection actual, TrackDirection requested)
        => actual == TrackDirection.Both || requested == TrackDirection.Both || actual == requested;

    internal static string NormalizeRequired(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new SimulationValidationException([$"{field}不可空白。"]);
        }

        return value.Trim();
    }

    internal static IReadOnlySet<string> ToFrozenIds(IEnumerable<string>? values, string field)
    {
        var errors = new List<string>();
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values ?? [])
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"{field}不可包含空白編號。");
            }
            else if (!result.Add(value.Trim()))
            {
                errors.Add($"{field}「{value.Trim()}」重複。");
            }
        }

        Throw(errors);
        return new ReadOnlySet<string>(result);
    }

    internal static void RequireDirection(TrackDirection value, string field, ICollection<string> errors)
    {
        if (value is not TrackDirection.Both and not TrackDirection.Outbound and not TrackDirection.Inbound)
        {
            errors.Add($"{field}不是有效的方向。");
        }
    }

    internal static void RequirePositive(double value, string field, ICollection<string> errors)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            errors.Add($"{field}必須是有限且大於 0 的數值。");
        }
    }

    internal static void RequireNonNegative(double value, string field, ICollection<string> errors)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            errors.Add($"{field}必須是有限的非負數。");
        }
    }

    internal static void Throw(IReadOnlyCollection<string> errors)
    {
        if (errors.Count > 0)
        {
            throw new SimulationValidationException(errors);
        }
    }

    private sealed class ReadOnlySet<T>(IEnumerable<T> values) : ReadOnlyCollection<T>(values.ToArray()), IReadOnlySet<T>
        where T : notnull
    {
        public new bool Contains(T item) => Items.Contains(item);
        public bool IsProperSubsetOf(IEnumerable<T> other) => Items.ToHashSet().IsProperSubsetOf(other);
        public bool IsProperSupersetOf(IEnumerable<T> other) => Items.ToHashSet().IsProperSupersetOf(other);
        public bool IsSubsetOf(IEnumerable<T> other) => Items.ToHashSet().IsSubsetOf(other);
        public bool IsSupersetOf(IEnumerable<T> other) => Items.ToHashSet().IsSupersetOf(other);
        public bool Overlaps(IEnumerable<T> other) => Items.ToHashSet().Overlaps(other);
        public bool SetEquals(IEnumerable<T> other) => Items.ToHashSet().SetEquals(other);
    }
}

/// <summary>站場股道區段的不可變定義。</summary>
public sealed class TrackSegmentDefinition
{
    public TrackSegmentDefinition(
        string trackId,
        string fromStationId,
        string toStationId,
        double startPositionMeters,
        double endPositionMeters,
        TrackDirection direction,
        TrackKind kind,
        double effectiveLengthMeters,
        double speedLimitMetersPerSecond,
        IEnumerable<string>? conflictResourceIds = null)
    {
        TrackId = PlatformDefinition.NormalizeRequired(trackId, "股道編號");
        FromStationId = PlatformDefinition.NormalizeRequired(fromStationId, "股道起點車站編號");
        ToStationId = PlatformDefinition.NormalizeRequired(toStationId, "股道終點車站編號");
        StartPositionMeters = startPositionMeters;
        EndPositionMeters = endPositionMeters;
        Direction = direction;
        Kind = kind;
        EffectiveLengthMeters = effectiveLengthMeters;
        SpeedLimitMetersPerSecond = speedLimitMetersPerSecond;
        ConflictResourceIds = PlatformDefinition.ToFrozenIds(conflictResourceIds, "股道衝突資源編號");
        Validate();
    }

    public TrackSegmentDefinition(
        string trackId,
        string fromStationId,
        string toStationId,
        double startPositionMeters,
        double endPositionMeters,
        TrackDirection direction,
        TrackKind kind,
        double speedLimitMetersPerSecond,
        IEnumerable<string>? conflictResourceIds = null)
        : this(trackId, fromStationId, toStationId, startPositionMeters, endPositionMeters, direction, kind,
            Math.Abs(endPositionMeters - startPositionMeters), speedLimitMetersPerSecond, conflictResourceIds)
    {
    }

    public string TrackId { get; }
    public string FromStationId { get; }
    public string ToStationId { get; }
    public double StartPositionMeters { get; }
    public double EndPositionMeters { get; }
    public TrackDirection Direction { get; }
    public TrackKind Kind { get; }
    public double EffectiveLengthMeters { get; }
    public double SpeedLimitMetersPerSecond { get; }
    public IReadOnlySet<string> ConflictResourceIds { get; }

    private void Validate()
    {
        var errors = new List<string>();
        PlatformDefinition.RequireDirection(Direction, "股道方向", errors);
        PlatformDefinition.RequirePositive(EffectiveLengthMeters, "股道有效長度", errors);
        PlatformDefinition.RequirePositive(SpeedLimitMetersPerSecond, "股道速限", errors);
        if (!double.IsFinite(StartPositionMeters) || !double.IsFinite(EndPositionMeters))
        {
            errors.Add("股道起訖里程必須是有限數值。");
        }

        if (FromStationId.Equals(ToStationId, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("股道起點與終點車站不可相同。");
        }

        PlatformDefinition.Throw(errors);
    }
}

/// <summary>兩個月台間可供列車使用的進路。</summary>
public sealed class RoutePathDefinition
{
    public RoutePathDefinition(
        string pathId,
        string fromPlatformId,
        string toPlatformId,
        TrackDirection direction,
        IEnumerable<string> trackSegmentIds,
        IEnumerable<string>? resourceIds = null)
    {
        PathId = PlatformDefinition.NormalizeRequired(pathId, "進路編號");
        FromPlatformId = PlatformDefinition.NormalizeRequired(fromPlatformId, "進路起點月台編號");
        ToPlatformId = PlatformDefinition.NormalizeRequired(toPlatformId, "進路終點月台編號");
        Direction = direction;
        TrackSegmentIds = PlatformDefinition.ToFrozenIds(trackSegmentIds, "進路股道編號");
        ResourceIds = PlatformDefinition.ToFrozenIds(resourceIds, "進路資源編號");
        Validate();
    }

    public string PathId { get; }
    public string FromPlatformId { get; }
    public string ToPlatformId { get; }
    public string OriginPlatformId => FromPlatformId;
    public string DestinationPlatformId => ToPlatformId;
    public TrackDirection Direction { get; }
    public IReadOnlySet<string> TrackSegmentIds { get; }
    public IReadOnlySet<string> ResourceIds { get; }

    private void Validate()
    {
        var errors = new List<string>();
        PlatformDefinition.RequireDirection(Direction, "進路方向", errors);
        if (FromPlatformId.Equals(ToPlatformId, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("進路起點與終點月台不可相同。");
        }

        if (TrackSegmentIds.Count == 0)
        {
            errors.Add("進路至少需要一個股道區段。");
        }

        if (ResourceIds.Count == 0)
        {
            errors.Add("進路至少需要一個衝突資源。");
        }

        PlatformDefinition.Throw(errors);
    }
}

/// <summary>端點站的折返設定。</summary>
public sealed class TurnbackPlanDefinition
{
    public TurnbackPlanDefinition(
        string turnbackId,
        string name,
        string stationId,
        TurnbackKind kind,
        string? arrivalPlatformId = null,
        string? departurePlatformId = null,
        IEnumerable<string>? trackSegmentIds = null,
        IEnumerable<string>? resourceIds = null,
        double turnbackTimeSeconds = 0)
    {
        TurnbackId = PlatformDefinition.NormalizeRequired(turnbackId, "折返設定編號");
        Name = string.IsNullOrWhiteSpace(name) ? TurnbackId : name.Trim();
        StationId = PlatformDefinition.NormalizeRequired(stationId, "折返車站編號");
        Kind = kind;
        ArrivalPlatformId = string.IsNullOrWhiteSpace(arrivalPlatformId) ? null : arrivalPlatformId.Trim();
        DeparturePlatformId = string.IsNullOrWhiteSpace(departurePlatformId) ? null : departurePlatformId.Trim();
        TrackSegmentIds = PlatformDefinition.ToFrozenIds(trackSegmentIds, "折返股道編號");
        ResourceIds = PlatformDefinition.ToFrozenIds(resourceIds, "折返資源編號");
        TurnbackTimeSeconds = turnbackTimeSeconds;
        Validate();
    }

    public string TurnbackId { get; }
    public string Name { get; }
    public string StationId { get; }
    public TurnbackKind Kind { get; }
    public string? ArrivalPlatformId { get; }
    public string? DeparturePlatformId { get; }
    public IReadOnlySet<string> TrackSegmentIds { get; }
    public IReadOnlySet<string> ResourceIds { get; }
    public double TurnbackTimeSeconds { get; }

    private void Validate()
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(Kind))
        {
            errors.Add("折返形式不是有效的設定。");
        }

        PlatformDefinition.RequireNonNegative(TurnbackTimeSeconds, "折返時間", errors);
        if (Kind != TurnbackKind.LegacyAbstract &&
            string.IsNullOrWhiteSpace(ArrivalPlatformId) && string.IsNullOrWhiteSpace(DeparturePlatformId))
        {
            errors.Add("站前或站後折返至少需要指定一個月台。");
        }

        PlatformDefinition.Throw(errors);
    }
}

/// <summary>單一車站的月台、股道、進路及折返配置。</summary>
public sealed class StationYardDefinition
{
    public StationYardDefinition(
        string stationId,
        string name,
        IEnumerable<PlatformDefinition>? platforms = null,
        IEnumerable<string>? trackSegmentIds = null,
        IEnumerable<string>? routePathIds = null,
        IEnumerable<TurnbackPlanDefinition>? turnbackPlans = null,
        PlatformAllocationStrategy platformAllocationStrategy = PlatformAllocationStrategy.Automatic)
    {
        StationId = PlatformDefinition.NormalizeRequired(stationId, "站場車站編號");
        Name = string.IsNullOrWhiteSpace(name) ? StationId : name.Trim();
        Platforms = Copy(platforms);
        TrackSegmentIds = PlatformDefinition.ToFrozenIds(trackSegmentIds, "站場股道編號");
        RoutePathIds = PlatformDefinition.ToFrozenIds(routePathIds, "站場進路編號");
        TurnbackPlans = Copy(turnbackPlans);
        PlatformAllocationStrategy = platformAllocationStrategy;
        Validate();
    }

    public StationYardDefinition(
        string stationId,
        IEnumerable<PlatformDefinition>? platforms = null,
        IEnumerable<string>? trackSegmentIds = null,
        IEnumerable<string>? routePathIds = null,
        IEnumerable<TurnbackPlanDefinition>? turnbackPlans = null,
        PlatformAllocationStrategy platformAllocationStrategy = PlatformAllocationStrategy.Automatic)
        : this(stationId, stationId, platforms, trackSegmentIds, routePathIds, turnbackPlans, platformAllocationStrategy)
    {
    }

    public string StationId { get; }
    public string Name { get; }
    public IReadOnlyList<PlatformDefinition> Platforms { get; }
    public IReadOnlySet<string> TrackSegmentIds { get; }
    public IReadOnlySet<string> RoutePathIds { get; }
    public IReadOnlyList<TurnbackPlanDefinition> TurnbackPlans { get; }
    public PlatformAllocationStrategy PlatformAllocationStrategy { get; }

    private void Validate()
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(PlatformAllocationStrategy))
        {
            errors.Add("月台分配策略不是有效的設定。");
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var platform in Platforms)
        {
            if (!platform.StationId.Equals(StationId, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"月台「{platform.PlatformId}」不屬於站場車站「{StationId}」。");
            }

            if (!ids.Add(platform.PlatformId))
            {
                errors.Add($"站場月台編號「{platform.PlatformId}」重複。");
            }
        }

        PlatformDefinition.Throw(errors);
    }

    private static IReadOnlyList<T> Copy<T>(IEnumerable<T>? values)
        => new ReadOnlyCollection<T>((values ?? []).ToArray());
}

/// <summary>站場拓樸及其查詢、驗證入口。</summary>
public sealed class InfrastructureGraph
{
    public InfrastructureGraph(
        Route route,
        IEnumerable<StationYardDefinition> stationYards,
        IEnumerable<TrackSegmentDefinition> trackSegments,
        IEnumerable<RoutePathDefinition> paths,
        IEnumerable<TurnbackPlanDefinition>? turnbackPlans = null,
        IEnumerable<SpatialReferencePointDefinition>? spatialReferencePoints = null)
    {
        Route = route ?? throw new ArgumentNullException(nameof(route));
        StationYards = Copy(stationYards, "站場");
        TrackSegments = Copy(trackSegments, "股道");
        Paths = Copy(paths, "進路");
        TurnbackPlans = Copy(turnbackPlans ?? StationYards.SelectMany(yard => yard.TurnbackPlans), "折返設定");
        SpatialReferencePoints = Copy(spatialReferencePoints ?? [], "空間參考點");
        Validate();
    }

    public Route Route { get; }
    public IReadOnlyList<StationYardDefinition> StationYards { get; }
    public IReadOnlyList<TrackSegmentDefinition> TrackSegments { get; }
    public IReadOnlyList<RoutePathDefinition> Paths { get; }
    public IReadOnlyList<RoutePathDefinition> RoutePaths => Paths;
    public IReadOnlyList<TurnbackPlanDefinition> TurnbackPlans { get; }
    public IReadOnlyList<SpatialReferencePointDefinition> SpatialReferencePoints { get; }
    public IReadOnlyList<PlatformDefinition> Platforms => StationYards.SelectMany(yard => yard.Platforms).ToArray();

    public SpatialReferencePointDefinition? FindSpatialReferencePoint(string stationId) =>
        SpatialReferencePoints.FirstOrDefault(item =>
            item.StationId.Equals(stationId, StringComparison.OrdinalIgnoreCase));

    /// <summary>把舊版線性路線轉成兩條獨立的 DOWN／UP 全線股道。</summary>
    public static InfrastructureGraph CreateLegacy(Route route)
    {
        ArgumentNullException.ThrowIfNull(route);
        var first = route.Stations[0];
        var last = route.Stations[^1];
        var length = route.TotalLengthMeters;

        var tracks = new[]
        {
            new TrackSegmentDefinition("DOWN", first.StationId, last.StationId, 0, length,
                TrackDirection.Outbound, TrackKind.Mainline, length, 27.7777777778,
                ["TRACK:DOWN"]),
            new TrackSegmentDefinition("UP", last.StationId, first.StationId, length, 0,
                TrackDirection.Inbound, TrackKind.Mainline, length, 27.7777777778,
                ["TRACK:UP"])
        };

        var platforms = new List<PlatformDefinition>();
        foreach (var station in route.Stations)
        {
            platforms.Add(new PlatformDefinition($"{station.StationId}:DOWN", station.StationId,
                $"{station.StationName} 下行月台", TrackDirection.Outbound, 220, 0, true,
                trackSegmentIds: ["DOWN"]));
            platforms.Add(new PlatformDefinition($"{station.StationId}:UP", station.StationId,
                $"{station.StationName} 上行月台", TrackDirection.Inbound, 220, 0, true,
                trackSegmentIds: ["UP"]));
        }

        var paths = new List<RoutePathDefinition>();
        for (var index = 0; index < route.Stations.Count - 1; index++)
        {
            var from = route.Stations[index];
            var to = route.Stations[index + 1];
            paths.Add(new RoutePathDefinition($"LEGACY:DOWN:{index + 1}", $"{from.StationId}:DOWN", $"{to.StationId}:DOWN",
                TrackDirection.Outbound, ["DOWN"], ["TRACK:DOWN"]));
            paths.Add(new RoutePathDefinition($"LEGACY:UP:{index + 1}", $"{to.StationId}:UP", $"{from.StationId}:UP",
                TrackDirection.Inbound, ["UP"], ["TRACK:UP"]));
        }

        var turnbacks = new[]
        {
            new TurnbackPlanDefinition($"LEGACY:{first.StationId}", $"{first.StationName} 抽象折返", first.StationId,
                TurnbackKind.LegacyAbstract, $"{first.StationId}:UP", $"{first.StationId}:DOWN", ["UP", "DOWN"],
                ["TURNBACK:" + first.StationId], 0),
            new TurnbackPlanDefinition($"LEGACY:{last.StationId}", $"{last.StationName} 抽象折返", last.StationId,
                TurnbackKind.LegacyAbstract, $"{last.StationId}:DOWN", $"{last.StationId}:UP", ["DOWN", "UP"],
                ["TURNBACK:" + last.StationId], 0)
        };

        var yards = route.Stations.Select(station =>
        {
            var stationPlatforms = platforms.Where(platform => platform.StationId.Equals(station.StationId, StringComparison.OrdinalIgnoreCase));
            var stationTurnbacks = turnbacks.Where(plan => plan.StationId.Equals(station.StationId, StringComparison.OrdinalIgnoreCase));
            return new StationYardDefinition(station.StationId, station.StationName, stationPlatforms,
                ["DOWN", "UP"], paths.Where(path => path.FromPlatformId.StartsWith(station.StationId + ":", StringComparison.OrdinalIgnoreCase)
                    || path.ToPlatformId.StartsWith(station.StationId + ":", StringComparison.OrdinalIgnoreCase)).Select(path => path.PathId),
                stationTurnbacks);
        }).ToArray();

        return new InfrastructureGraph(route, yards, tracks, paths, turnbacks);
    }

    public IReadOnlyList<PlatformDefinition> FindCompatiblePlatforms(
        string stationId,
        TrackDirection direction,
        double trainLengthMeters,
        string? vehicleTypeId = null,
        string? serviceTypeId = null,
        bool requirePassengerService = false)
    {
        var normalizedStationId = PlatformDefinition.NormalizeRequired(stationId, "查詢車站編號");
        return Platforms
            .Where(platform => platform.StationId.Equals(normalizedStationId, StringComparison.OrdinalIgnoreCase))
            .Where(platform => platform.IsCompatible(direction, trainLengthMeters, vehicleTypeId, serviceTypeId, requirePassengerService))
            .OrderBy(platform => platform.PlatformId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<PlatformDefinition> FindCompatiblePlatforms(
        string stationId,
        TrainDirection direction,
        double trainLengthMeters,
        string? vehicleTypeId = null,
        string? serviceTypeId = null,
        bool requirePassengerService = false)
        => FindCompatiblePlatforms(stationId, (TrackDirection)direction, trainLengthMeters, vehicleTypeId, serviceTypeId, requirePassengerService);

    public IReadOnlyList<RoutePathDefinition> FindPaths(
        string fromPlatformId,
        string toPlatformId,
        TrackDirection direction,
        string? vehicleTypeId = null,
        string? serviceTypeId = null,
        double? trainLengthMeters = null)
    {
        var from = PlatformDefinition.NormalizeRequired(fromPlatformId, "查詢進路起點月台編號");
        var to = PlatformDefinition.NormalizeRequired(toPlatformId, "查詢進路終點月台編號");
        return Paths
            .Where(path => path.FromPlatformId.Equals(from, StringComparison.OrdinalIgnoreCase)
                && path.ToPlatformId.Equals(to, StringComparison.OrdinalIgnoreCase)
                && PlatformDefinition.DirectionMatches(path.Direction, direction))
            .Where(path => path.TrackSegmentIds.All(trackId => TrackSegments.Any(track => track.TrackId.Equals(trackId, StringComparison.OrdinalIgnoreCase)
                && PlatformDefinition.DirectionMatches(track.Direction, direction))))
            .Where(path =>
            {
                var platform = Platforms.FirstOrDefault(item => item.PlatformId.Equals(from, StringComparison.OrdinalIgnoreCase));
                return platform is null || !trainLengthMeters.HasValue
                    || platform.IsCompatible(direction, trainLengthMeters.Value, vehicleTypeId, serviceTypeId);
            })
            .OrderBy(path => path.PathId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<RoutePathDefinition> FindPaths(
        string fromPlatformId,
        string toPlatformId,
        TrainDirection direction,
        string? vehicleTypeId = null,
        string? serviceTypeId = null,
        double? trainLengthMeters = null)
        => FindPaths(fromPlatformId, toPlatformId, (TrackDirection)direction, vehicleTypeId, serviceTypeId, trainLengthMeters);

    /// <summary>重新檢查所有跨物件參照；失敗時以中文一次列出所有原因。</summary>
    public void Validate()
    {
        var errors = new List<string>();
        var stationIds = Route.Stations.Select(station => station.StationId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var yardIds = Unique(StationYards.Select(yard => yard.StationId), "站場車站編號", errors);
        foreach (var yard in StationYards)
        {
            if (!stationIds.Contains(yard.StationId))
            {
                errors.Add($"站場「{yard.StationId}」不存在於路線。");
            }
        }

        var trackIds = Unique(TrackSegments.Select(track => track.TrackId), "股道編號", errors);
        var platformIds = Unique(Platforms.Select(platform => platform.PlatformId), "月台編號", errors);
        var pathIds = Unique(Paths.Select(path => path.PathId), "進路編號", errors);
        var turnbackIds = Unique(TurnbackPlans.Select(plan => plan.TurnbackId), "折返設定編號", errors);
        var referencePointIds = Unique(SpatialReferencePoints.Select(point => point.ReferencePointId), "空間參考點編號", errors);
        _ = Unique(SpatialReferencePoints.Select(point => point.StationId), "空間參考點所屬車站", errors);

        foreach (var track in TrackSegments)
        {
            if (!stationIds.Contains(track.FromStationId) || !stationIds.Contains(track.ToStationId))
            {
                errors.Add($"股道「{track.TrackId}」引用不存在的起訖車站。");
            }
        }

        foreach (var platform in Platforms)
        {
            if (!stationIds.Contains(platform.StationId))
            {
                errors.Add($"月台「{platform.PlatformId}」引用不存在的車站。");
            }

            foreach (var trackId in platform.TrackSegmentIds)
            {
                if (!trackIds.Contains(trackId))
                {
                    errors.Add($"月台「{platform.PlatformId}」引用不存在的股道「{trackId}」。");
                }
            }
        }

        foreach (var path in Paths)
        {
            if (!platformIds.Contains(path.FromPlatformId) || !platformIds.Contains(path.ToPlatformId))
            {
                errors.Add($"進路「{path.PathId}」引用不存在的起訖月台。");
            }

            foreach (var trackId in path.TrackSegmentIds)
            {
                if (!trackIds.Contains(trackId))
                {
                    errors.Add($"進路「{path.PathId}」引用不存在的股道「{trackId}」。");
                }
            }
        }

        foreach (var plan in TurnbackPlans)
        {
            if (!stationIds.Contains(plan.StationId))
            {
                errors.Add($"折返設定「{plan.TurnbackId}」引用不存在的車站。");
            }

            if (plan.ArrivalPlatformId is not null && !platformIds.Contains(plan.ArrivalPlatformId))
            {
                errors.Add($"折返設定「{plan.TurnbackId}」引用不存在的抵達月台。");
            }

            if (plan.DeparturePlatformId is not null && !platformIds.Contains(plan.DeparturePlatformId))
            {
                errors.Add($"折返設定「{plan.TurnbackId}」引用不存在的發車月台。");
            }

            foreach (var trackId in plan.TrackSegmentIds)
            {
                if (!trackIds.Contains(trackId))
                {
                    errors.Add($"折返設定「{plan.TurnbackId}」引用不存在的股道「{trackId}」。");
                }
            }
        }

        foreach (var yard in StationYards)
        {
            foreach (var trackId in yard.TrackSegmentIds)
            {
                if (!trackIds.Contains(trackId))
                {
                    errors.Add($"站場「{yard.StationId}」引用不存在的股道「{trackId}」。");
                }
            }

            foreach (var pathId in yard.RoutePathIds)
            {
                if (!pathIds.Contains(pathId))
                {
                    errors.Add($"站場「{yard.StationId}」引用不存在的進路「{pathId}」。");
                }
            }
        }

        foreach (var point in SpatialReferencePoints)
        {
            if (!stationIds.Contains(point.StationId))
            {
                errors.Add($"空間參考點「{point.ReferencePointId}」引用不存在的車站「{point.StationId}」。");
            }
        }

        _ = yardIds;
        _ = pathIds;
        _ = turnbackIds;
        _ = referencePointIds;
        PlatformDefinition.Throw(errors);
    }

    private static IReadOnlySet<string> Unique(IEnumerable<string> values, string field, ICollection<string> errors)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (!set.Add(value))
            {
                errors.Add($"{field}「{value}」重複。");
            }
        }

        return set;
    }

    private static IReadOnlyList<T> Copy<T>(IEnumerable<T>? values, string field)
    {
        if (values is null)
        {
            throw new SimulationValidationException([$"{field}資料不可為空。"]);
        }

        return new ReadOnlyCollection<T>(values.ToArray());
    }
}

/// <summary>
/// 以資源 ID 原子預約進路。每次預約會先排序並去重資源，因此結果與輸入列舉順序無關。
/// </summary>
public sealed class RouteResourceReservationManager
{
    private readonly object sync = new();
    private readonly Dictionary<string, string> ownersByResource = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<string>> resourcesByOwner = new(StringComparer.OrdinalIgnoreCase);

    public bool Reserve(string reservationId, IEnumerable<string> resourceIds)
    {
        var owner = PlatformDefinition.NormalizeRequired(reservationId, "預約識別碼");
        var resources = NormalizeResources(resourceIds);
        if (resources.Count == 0)
        {
            throw new SimulationValidationException(["預約至少需要一個資源編號。"]);
        }

        lock (sync)
        {
            if (resourcesByOwner.TryGetValue(owner, out var current)
                && current.SequenceEqual(resources, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            if (resources.Any(resource => ownersByResource.TryGetValue(resource, out var existing)
                && !existing.Equals(owner, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            if (resourcesByOwner.TryGetValue(owner, out current))
            {
                foreach (var resource in current)
                {
                    ownersByResource.Remove(resource);
                }
            }

            foreach (var resource in resources)
            {
                ownersByResource[resource] = owner;
            }

            resourcesByOwner[owner] = resources;
            return true;
        }
    }

    public bool Reserve(string reservationId, RoutePathDefinition path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Reserve(reservationId, path.ResourceIds);
    }

    public bool IsAvailable(IEnumerable<string> resourceIds, string? reservationId = null)
    {
        var resources = NormalizeResources(resourceIds);
        var owner = string.IsNullOrWhiteSpace(reservationId) ? null : reservationId.Trim();
        lock (sync)
        {
            return resources.All(resource => !ownersByResource.TryGetValue(resource, out var existing)
                || (owner is not null && existing.Equals(owner, StringComparison.OrdinalIgnoreCase)));
        }
    }

    public bool IsAvailable(string reservationId, IEnumerable<string> resourceIds)
        => IsAvailable(resourceIds, reservationId);

    public bool Release(string reservationId)
    {
        var owner = PlatformDefinition.NormalizeRequired(reservationId, "預約識別碼");
        lock (sync)
        {
            if (!resourcesByOwner.Remove(owner, out var resources))
            {
                return false;
            }

            foreach (var resource in resources)
            {
                if (ownersByResource.TryGetValue(resource, out var current)
                    && current.Equals(owner, StringComparison.OrdinalIgnoreCase))
                {
                    ownersByResource.Remove(resource);
                }
            }

            return true;
        }
    }

    public IReadOnlyDictionary<string, string> Reservations
    {
        get
        {
            lock (sync)
            {
                return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(ownersByResource, StringComparer.OrdinalIgnoreCase));
            }
        }
    }

    private static IReadOnlyList<string> NormalizeResources(IEnumerable<string>? resourceIds)
    {
        if (resourceIds is null)
        {
            throw new SimulationValidationException(["預約資源清單不可為空。"]);
        }

        var result = resourceIds
            .Where(resource => !string.IsNullOrWhiteSpace(resource))
            .Select(resource => resource.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(resource => resource, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return result;
    }
}
