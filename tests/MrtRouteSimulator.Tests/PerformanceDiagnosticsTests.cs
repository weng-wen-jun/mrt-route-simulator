using System.Text.Json;
using MrtRouteSimulator.Engine;

internal static class PerformanceDiagnosticsTests
{
    public static void DiagnosticsAreOptInAndPreserveSimulationOutput()
    {
        var control = CreateWorld();
        var instrumented = CreateWorld();
        instrumented.PerformanceDiagnosticsEnabled = true;
        instrumented.NearestLeaderShadowModeEnabled = true;
        instrumented.ResetPerformanceDiagnostics();

        control.AdvanceTo(120);
        instrumented.AdvanceTo(120);

        var disabled = control.GetPerformanceDiagnostics();
        Equal(false, disabled.Enabled, "正常 world 的 diagnostics 必須預設關閉。");
        Equal(0L, disabled.TickCount, "關閉 diagnostics 時不得累計 tick。");
        Equal(0L, disabled.LeaderLookupCount, "關閉 diagnostics 時不得累計 leader lookup。");
        Equal(0L, disabled.OccupancyRefreshCount, "關閉 diagnostics 時不得累計 occupancy refresh。");

        var diagnostics = instrumented.GetPerformanceDiagnostics();
        Equal(true, diagnostics.Enabled, "benchmark world 必須能明確開啟 diagnostics。");
        Equal(1_200L, diagnostics.TickCount, "120 秒必須完整量測 1,200 個固定 tick。");
        Equal(3_600L, diagnostics.OccupancyRefreshCount, "正常 TickCore 每 tick 應 refresh occupancy 三次。");
        True(diagnostics.ActiveTrainCountSum > 0, "diagnostics 必須累計 active topology train samples。");
        True(diagnostics.PeakActiveTrainCount >= 2, "此情境必須涵蓋至少兩列 active topology trains。");
        True(diagnostics.LeaderLookupCount > 0, "控制模式必須量測 leader lookup。");
        True(diagnostics.CandidatePairCount > 0, "控制模式必須量測 full-scan candidate pairs。");
        True(diagnostics.TopologySafetyMetricCallCount >= diagnostics.CandidatePairCount,
            "metrics 次數不得少於 full-scan candidate pair 次數。");
        True(diagnostics.GraphDistanceCallCount > 0, "此情境必須實際量測 graph-distance calls。");
        True(diagnostics.TickCoreTimestampTicks > 0, "啟用 diagnostics 時必須量測 TickCore 時間。");
        True(diagnostics.ShadowLookupCount > 0, "shadow mode 必須比對 indexed lookup。");
        True(diagnostics.IndexedSuccessCount > 0, "candidate index 必須成功提供候選。");
        Equal(0L, diagnostics.ShadowMismatchCount, "candidate index 不得與 full-scan oracle 不一致。");
        Equal(0, instrumented.NearestLeaderShadowMismatches.Count, "mismatch detail 必須保持空白。");
        True(diagnostics.AverageIndexedCandidateCount <= diagnostics.AverageOracleCandidateCount,
            "broad phase 平均候選數不得高於 oracle full-scan candidate 數。");

        Equal(JsonSerializer.Serialize(control.Events), JsonSerializer.Serialize(instrumented.Events),
            "diagnostics 不得改變 event sequence。");
        Equal(JsonSerializer.Serialize(control.Trajectory), JsonSerializer.Serialize(instrumented.Trajectory),
            "diagnostics 不得改變 trajectory。");
        Equal(JsonSerializer.Serialize(control.SafetyHistory), JsonSerializer.Serialize(instrumented.SafetyHistory),
            "diagnostics 不得改變 safety history。");
    }

    private static SimulationWorld CreateWorld()
    {
        var samplePath = Path.Combine(FindRepositoryRoot(), "samples", "11-小型-三站完整拓樸運行範例.mrtsim.json");
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(samplePath));
        var runtime = TopologyProjectFormat.CreateRuntime(document);
        return new SimulationWorldOptions(
            Route: null,
            TrainParameters: runtime.TrainParameters,
            OperationalParameters: runtime.OperationalParameters,
            TrainCount: runtime.DispatchPlan.Runs.Count,
            InitialDepartureIntervalSeconds: document.Simulation.HeadwaySeconds,
            ProfileMode: document.Simulation.ProfileMode,
            MovingBlockMode: document.Simulation.MovingBlockMode,
            ServicePatterns: runtime.ServicePatterns,
            DispatchPlan: runtime.DispatchPlan,
            VehicleTypes: runtime.VehicleTypes,
            ServiceTypes: runtime.ServiceTypes,
            Topology: runtime.Topology).CreateWorld();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MrtRouteSimulator.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("找不到 repository root。");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} expected={expected}; actual={actual}");
        }
    }
}
