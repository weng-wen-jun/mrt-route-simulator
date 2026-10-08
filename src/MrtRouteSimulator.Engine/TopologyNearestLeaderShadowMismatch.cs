namespace MrtRouteSimulator.Engine;

public sealed record TopologyNearestLeaderShadowMismatch(
    double SimulationTimeSeconds,
    string FollowerVehicleId,
    string? OracleLeaderVehicleId,
    string? IndexedLeaderVehicleId,
    RuntimeTopologyCursor FollowerCursor,
    double? OracleHeadDistanceMeters,
    double? IndexedHeadDistanceMeters,
    double? OracleActualGapMeters,
    double? IndexedActualGapMeters,
    SafetyStatus? OracleSafetyStatus,
    SafetyStatus? IndexedSafetyStatus,
    double? OracleControlLimitMetersPerSecond,
    double? IndexedControlLimitMetersPerSecond,
    IReadOnlyList<string> CandidateVehicleIds,
    long PhysicalStateGeneration);
