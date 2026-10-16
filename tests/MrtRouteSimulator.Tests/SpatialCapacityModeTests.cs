using MrtRouteSimulator.Engine;

internal static class SpatialCapacityModeTests
{
    public static void ThreeGradeVectorsDifferByMode()
    {
        var vectors = new[]
        {
            (In: 10d, Out: 0d, Urcs: 126.182349206, Handbook: 121.175796126, UrcsLine: 28, HandbookLine: 29,
                UrcsDesign: 54208, HandbookDesign: 56144, UrcsAchievable: 43366, HandbookAchievable: 44915),
            (In: 0d, Out: 10d, Urcs: 121.175796126, Handbook: 126.182349206, UrcsLine: 29, HandbookLine: 28,
                UrcsDesign: 56144, HandbookDesign: 54208, UrcsAchievable: 44915, HandbookAchievable: 43366),
            (In: 10d, Out: -10d, Urcs: 130.468063492, Handbook: 119.983135576, UrcsLine: 27, HandbookLine: 30,
                UrcsDesign: 52272, HandbookDesign: 58080, UrcsAchievable: 41817, HandbookAchievable: 46464)
        };

        foreach (var vector in vectors)
        {
            var point = Station(vector.In, vector.Out);
            var urcs = SpatialCapacityAnalysis.Calculate(point, mode: SpatialCapacityCalculationMode.UrcsCompatible);
            var handbook = SpatialCapacityAnalysis.Calculate(point, mode: SpatialCapacityCalculationMode.TaiwanHandbook);
            NearlyEqual(vector.Urcs, urcs.DesignHeadwaySeconds, 1e-6);
            NearlyEqual(vector.Handbook, handbook.DesignHeadwaySeconds, 1e-6);
            Equal(vector.UrcsLine, urcs.LineCapacityPerHour);
            Equal(vector.HandbookLine, handbook.LineCapacityPerHour);
            Equal(vector.UrcsDesign, urcs.DesignPassengerCapacityPerHour);
            Equal(vector.HandbookDesign, handbook.DesignPassengerCapacityPerHour);
            Equal(vector.UrcsAchievable, urcs.AchievablePassengerCapacityPerHour);
            Equal(vector.HandbookAchievable, handbook.AchievablePassengerCapacityPerHour);
        }
    }

    public static void ReverseDirectionRemainsIndependentInBothModes()
    {
        var point = new SpatialReferencePointDefinition(
            "CAP-REVERSE", "MANUAL-STATION", "容量逆行測試", SpatialReferencePointKind.IntermediateStation,
            stationForwardDwellSeconds: 10,
            stationReverseDwellSeconds: 45,
            stationReverseGradeInPermille: 10,
            stationReverseGradeOutPermille: -10,
            stationReverseDistanceToSignalMeters: 30,
            stationReverseOverlapMeters: 200,
            stationReverseEarlierCruiseSpeedMetersPerSecond: 60 / 3.6,
            stationReverseLaterCruiseSpeedMetersPerSecond: 70 / 3.6,
            stationReverseSafetyFactor: 2);

        foreach (var mode in Enum.GetValues<SpatialCapacityCalculationMode>())
        {
            var forward = SpatialCapacityAnalysis.Calculate(point, direction: SpatialCapacityDirection.Forward, mode: mode);
            var reverse = SpatialCapacityAnalysis.Calculate(point, direction: SpatialCapacityDirection.Reverse, mode: mode);
            True(Math.Abs(forward.SafetyHeadwaySeconds - reverse.SafetyHeadwaySeconds) > 1,
                $"{mode} 不得共用順逆行參數。");
        }
    }

    public static void FlatVectorsPreserveAllFiveKindsAndAlternateVectors()
    {
        var points = new[]
        {
            (Point: new SpatialReferencePointDefinition("CAP-S", "MANUAL-S", "中間站", SpatialReferencePointKind.IntermediateStation), Headway: 124.753777778, Line: 28, Design: 54208, Achievable: 43366),
            (Point: new SpatialReferencePointDefinition("CAP-F", "MANUAL-F", "站前", SpatialReferencePointKind.BeforeStationTurnback,
                mainlineApproachCruiseSpeedMetersPerSecond: 60 / 3.6), Headway: 158.217222222, Line: 22, Design: 42592, Achievable: 34073),
            (Point: new SpatialReferencePointDefinition("CAP-R", "MANUAL-R", "站後", SpatialReferencePointKind.AfterStationTurnback), Headway: 154.256668133, Line: 23, Design: 44528, Achievable: 35622),
            (Point: new SpatialReferencePointDefinition("CAP-P", "MANUAL-P", "中央避車線", SpatialReferencePointKind.CentralSidingTurnback), Headway: 165.698112577, Line: 21, Design: 40656, Achievable: 32524),
            (Point: new SpatialReferencePointDefinition("CAP-J", "MANUAL-J", "銜接", SpatialReferencePointKind.Junction), Headway: 86.335888889, Line: 41, Design: 79376, Achievable: 63500)
        };

        foreach (var expected in points)
        {
            var urcs = SpatialCapacityAnalysis.Calculate(expected.Point, mode: SpatialCapacityCalculationMode.UrcsCompatible);
            var handbook = SpatialCapacityAnalysis.Calculate(expected.Point, mode: SpatialCapacityCalculationMode.TaiwanHandbook);
            NearlyEqual(expected.Headway, urcs.DesignHeadwaySeconds, 1e-6);
            NearlyEqual(expected.Headway, handbook.DesignHeadwaySeconds, 1e-6);
            Equal(expected.Line, urcs.LineCapacityPerHour);
            Equal(expected.Line, handbook.LineCapacityPerHour);
            Equal(expected.Design, urcs.DesignPassengerCapacityPerHour);
            Equal(expected.Design, handbook.DesignPassengerCapacityPerHour);
            Equal(expected.Achievable, urcs.AchievablePassengerCapacityPerHour);
            Equal(expected.Achievable, handbook.AchievablePassengerCapacityPerHour);
        }

        var alternateFront = new[]
        {
            (Dwell: 30d, Headway: 113.832445912, Line: 31, Design: 60016, Achievable: 48012),
            (Dwell: 180d, Headway: 176.608611111, Line: 20, Design: 38720, Achievable: 30976)
        };
        foreach (var expected in alternateFront)
        {
            var point = new SpatialReferencePointDefinition(
                $"CAP-A-F-{expected.Dwell}", "MANUAL-A", "交替月台", SpatialReferencePointKind.BeforeStationTurnback,
                alternateBerthing: true, turnbackDwellSeconds: expected.Dwell,
                mainlineApproachCruiseSpeedMetersPerSecond: 60 / 3.6);
            var result = SpatialCapacityAnalysis.Calculate(point);
            NearlyEqual(expected.Headway, result.DesignHeadwaySeconds, 1e-6);
            Equal(expected.Line, result.LineCapacityPerHour);
            Equal(expected.Design, result.DesignPassengerCapacityPerHour);
            Equal(expected.Achievable, result.AchievablePassengerCapacityPerHour);
        }

        var alternateRear = new[]
        {
            (Dwell: 30d, Headway: 109.871891822, Line: 32, Design: 61952, Achievable: 49561),
            (Dwell: 180d, Headway: 174.628334067, Line: 20, Design: 38720, Achievable: 30976)
        };
        foreach (var expected in alternateRear)
        {
            var point = new SpatialReferencePointDefinition(
                $"CAP-A-R-{expected.Dwell}", "MANUAL-A", "交替月台", SpatialReferencePointKind.AfterStationTurnback,
                alternateBerthing: true, turnbackDwellSeconds: expected.Dwell);
            var result = SpatialCapacityAnalysis.Calculate(point);
            NearlyEqual(expected.Headway, result.DesignHeadwaySeconds, 1e-6);
            Equal(expected.Line, result.LineCapacityPerHour);
            Equal(expected.Design, result.DesignPassengerCapacityPerHour);
            Equal(expected.Achievable, result.AchievablePassengerCapacityPerHour);
        }
    }

    public static void ProjectSettingsValidateAndRoundTrip()
    {
        var point = new ProjectSpatialReferencePoint(
            "CAP-PROJECT", "MANUAL-STATION", "手動參考點", SpatialReferencePointKind.IntermediateStation,
            StationForwardGradeInPermille: 10,
            StationForwardGradeOutPermille: -10);
        var settings = new SpatialCapacityProjectSettings(
            new SpatialCapacityGlobalParameters(),
            new SpatialCapacityTrainParameters(),
            [point],
            CompareAsSingleScenario: true);
        SpatialCapacityProjectAnalysis.Validate(settings);

        var document = SyntheticLongRouteFullScenarioBuilder.BuildFull() with { SpatialCapacity = settings };
        var roundTrip = TopologyProjectFormat.Deserialize(TopologyProjectFormat.Serialize(document));
        True(roundTrip.SpatialCapacity is not null, "Schema 8 往返應保留容量設定。");
        Equal(SpatialCapacityCalculationMode.TaiwanHandbook, roundTrip.SpatialCapacity!.Mode);
        True(roundTrip.SpatialCapacity.CompareAsSingleScenario, "Schema 8 往返應保留單一情境明確 opt-in 旗標。");
        Equal("CAP-PROJECT", roundTrip.SpatialCapacity.Points[0].ReferencePointId);
    }

    public static void MissingCapacitySettingsRemainCompatible()
    {
        var document = SyntheticLongRouteFullScenarioBuilder.BuildFull();
        var roundTrip = TopologyProjectFormat.Deserialize(TopologyProjectFormat.Serialize(document));
        True(roundTrip.SpatialCapacity is null, "未設定容量時 Schema 8 應保持 null。");
    }

    public static void InvalidModeAndEffectiveAccelerationAreRejected()
    {
        var point = new ProjectSpatialReferencePoint(
            "CAP-BAD", "MANUAL-STATION", "無效模式", SpatialReferencePointKind.IntermediateStation);
        var invalidMode = new SpatialCapacityProjectSettings(
            new SpatialCapacityGlobalParameters(),
            new SpatialCapacityTrainParameters(),
            [point],
            (SpatialCapacityCalculationMode)999);
        Throws<SimulationValidationException>(() => SpatialCapacityProjectAnalysis.Validate(invalidMode), "容量計算模式");

        var invalidTrain = new SpatialCapacityProjectSettings(
            new SpatialCapacityGlobalParameters(),
            new SpatialCapacityTrainParameters(AccelerationMetersPerSecondSquared: 0.1),
            [point with { StationForwardGradeOutPermille = 30 }]);
        Throws<SimulationValidationException>(() => SpatialCapacityProjectAnalysis.Validate(invalidTrain), "有效加速度");

        var invalidGlobalWithNoPoints = new SpatialCapacityProjectSettings(
            new SpatialCapacityGlobalParameters(InterchangeTimeSeconds: 61),
            new SpatialCapacityTrainParameters(),
            []);
        Throws<SimulationValidationException>(() => SpatialCapacityProjectAnalysis.Validate(invalidGlobalWithNoPoints), "號誌");

        var duplicateId = new SpatialCapacityProjectSettings(
            new SpatialCapacityGlobalParameters(),
            new SpatialCapacityTrainParameters(),
            [point, point with { StationId = "MANUAL-OTHER" }]);
        Throws<SimulationValidationException>(() => SpatialCapacityProjectAnalysis.Validate(duplicateId), "重複");
    }

    public static void ScenarioBottleneckRequiresOptInAndKeepsDirectionsSeparate()
    {
        var points = new[]
        {
            new ProjectSpatialReferencePoint(
                "S1", "MANUAL-S1", "順行瓶頸", SpatialReferencePointKind.IntermediateStation,
                StationForwardDwellSeconds: 120, StationReverseDwellSeconds: 0),
            new ProjectSpatialReferencePoint(
                "S7", "MANUAL-S7", "逆行瓶頸", SpatialReferencePointKind.IntermediateStation,
                StationForwardDwellSeconds: 0, StationReverseDwellSeconds: 120),
            new ProjectSpatialReferencePoint("S10", "MANUAL-S10", "銜接", SpatialReferencePointKind.Junction),
            new ProjectSpatialReferencePoint("S14", "MANUAL-S14", "站前", SpatialReferencePointKind.BeforeStationTurnback)
        };
        var independent = new SpatialCapacityProjectSettings(
            new SpatialCapacityGlobalParameters(),
            new SpatialCapacityTrainParameters(),
            points);
        Throws<SimulationValidationException>(
            () => SpatialCapacityScenarioAnalysis.Calculate(independent),
            "同一分析情境");

        var settings = independent with { CompareAsSingleScenario = true };
        var results = SpatialCapacityScenarioAnalysis.Calculate(settings);
        Equal(2, results.Count);
        Equal(SpatialCapacityDirection.Forward, results[0].Direction);
        Equal(SpatialCapacityDirection.Reverse, results[1].Direction);
        Equal("S1", results[0].ReferencePointId);
        Equal("S7", results[1].ReferencePointId);

        foreach (var direction in Enum.GetValues<SpatialCapacityDirection>())
        {
            var expected = points
                .Select(point => (Point: point, Result: SpatialCapacityAnalysis.Calculate(
                    SpatialCapacityProjectAnalysis.ToDefinition(point),
                    settings.Global,
                    settings.Train,
                    direction,
                    settings.Mode)))
                .OrderByDescending(item => item.Result.DesignHeadwaySeconds)
                .ThenBy(item => item.Point.ReferencePointId, StringComparer.OrdinalIgnoreCase)
                .First();
            var actual = results.Single(item => item.Direction == direction);
            Equal(expected.Point.ReferencePointId, actual.ReferencePointId);
            NearlyEqual(expected.Result.DesignHeadwaySeconds, actual.Result.DesignHeadwaySeconds, 1e-9);
        }
    }

    public static void ScenarioBottleneckTieBreaksByStableId()
    {
        var settings = new SpatialCapacityProjectSettings(
            new SpatialCapacityGlobalParameters(),
            new SpatialCapacityTrainParameters(),
            [
                new ProjectSpatialReferencePoint("Z9", "MANUAL-Z9", "Z", SpatialReferencePointKind.Junction),
                new ProjectSpatialReferencePoint("A1", "MANUAL-A1", "A", SpatialReferencePointKind.Junction)
            ],
            CompareAsSingleScenario: true);
        var results = SpatialCapacityScenarioAnalysis.Calculate(settings);
        Equal(2, results.Count);
        Equal("A1", results[0].ReferencePointId);
        Equal("A1", results[1].ReferencePointId);
    }

    public static void SensitivityScansUseFixedValuesAndApplicableKinds()
    {
        var junction = new ProjectSpatialReferencePoint("J1", "MANUAL-J1", "銜接", SpatialReferencePointKind.Junction);
        var turnback = new ProjectSpatialReferencePoint(
            "S14", "MANUAL-S14", "站前", SpatialReferencePointKind.BeforeStationTurnback,
            AlternateBerthing: true);
        var settings = new SpatialCapacityProjectSettings(
            new SpatialCapacityGlobalParameters(),
            new SpatialCapacityTrainParameters(),
            [junction, turnback]);

        var ratio = SpatialCapacityScenarioAnalysis.AnalyzeSensitivity(
            settings, "J1", SpatialCapacitySensitivityParameter.MainlineTrafficRatio, 0, 1, 0.25);
        Equal(5, ratio.Count);
        NearlyEqual(0, ratio[0].Value, 1e-12);
        NearlyEqual(1, ratio[^1].Value, 1e-12);
        True(ratio[0].Result.DesignHeadwaySeconds > ratio[^1].Result.DesignHeadwaySeconds,
            "主線通過比例提高時，銜接點瓶頸應不增加。");

        var nonDivisible = SpatialCapacityScenarioAnalysis.AnalyzeSensitivity(
            settings, "J1", SpatialCapacitySensitivityParameter.MainlineTrafficRatio, 0, 1, 0.3);
        Equal(4, nonDivisible.Count);
        NearlyEqual(0.9, nonDivisible[^1].Value, 1e-12);

        var dwell = SpatialCapacityScenarioAnalysis.AnalyzeSensitivity(
            settings, "S14", SpatialCapacitySensitivityParameter.TurnbackDwellSeconds, 10, 120, 10);
        Equal(12, dwell.Count);
        NearlyEqual(10, dwell[0].Value, 1e-12);
        NearlyEqual(120, dwell[^1].Value, 1e-12);
        True(dwell[^1].Result.DesignHeadwaySeconds > dwell[0].Result.DesignHeadwaySeconds,
            "站前折返停等增加時，瓶頸設計班距應增加。");
    }

    public static void SensitivityRejectsInvalidRangeKindAndPointInputs()
    {
        var station = new ProjectSpatialReferencePoint("S1", "MANUAL-S1", "站", SpatialReferencePointKind.IntermediateStation);
        var settings = new SpatialCapacityProjectSettings(
            new SpatialCapacityGlobalParameters(),
            new SpatialCapacityTrainParameters(),
            [station]);
        Throws<SimulationValidationException>(() => SpatialCapacityScenarioAnalysis.AnalyzeSensitivity(
            settings, "S1", SpatialCapacitySensitivityParameter.MainlineTrafficRatio, 0, 1, 0.1), "不適用");
        Throws<SimulationValidationException>(() => SpatialCapacityScenarioAnalysis.AnalyzeSensitivity(
            settings, "UNKNOWN", SpatialCapacitySensitivityParameter.TurnbackDwellSeconds, 0, 1, 1), "找不到");

        var junction = settings with
        {
            Points = [new ProjectSpatialReferencePoint("J1", "MANUAL-J1", "銜接", SpatialReferencePointKind.Junction)]
        };
        Throws<SimulationValidationException>(() => SpatialCapacityScenarioAnalysis.AnalyzeSensitivity(
            junction, "J1", SpatialCapacitySensitivityParameter.MainlineTrafficRatio, double.NaN, 1, 1), "有限");
        Throws<SimulationValidationException>(() => SpatialCapacityScenarioAnalysis.AnalyzeSensitivity(
            junction, "J1", SpatialCapacitySensitivityParameter.MainlineTrafficRatio, 0, 1, 0), "步長");
        Throws<SimulationValidationException>(() => SpatialCapacityScenarioAnalysis.AnalyzeSensitivity(
            junction, "J1", SpatialCapacitySensitivityParameter.MainlineTrafficRatio, 0, 1, 0.004), "201");
        Throws<SimulationValidationException>(() => SpatialCapacityScenarioAnalysis.AnalyzeSensitivity(
            junction, "J1", SpatialCapacitySensitivityParameter.MainlineTrafficRatio, -0.1, 1, 0.1), "介於");
        Throws<SimulationValidationException>(() => SpatialCapacityScenarioAnalysis.AnalyzeSensitivity(
            junction, "J1", (SpatialCapacitySensitivityParameter)999, 0, 1, 0.1), "敏感度參數");
    }

    private static SpatialReferencePointDefinition Station(double gradeIn, double gradeOut) =>
        new("CAP-STATION", "MANUAL-STATION", "中間站", SpatialReferencePointKind.IntermediateStation,
            stationForwardGradeInPermille: gradeIn,
            stationForwardGradeOutPermille: gradeOut);

    private static void NearlyEqual(double expected, double actual, double tolerance)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"預期 {expected}，實際 {actual}。");
    }

    private static void Equal<T>(T expected, T actual)
        where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"預期 {expected}，實際 {actual}。");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
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
