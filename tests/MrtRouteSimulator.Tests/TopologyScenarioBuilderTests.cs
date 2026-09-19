using MrtRouteSimulator.Engine;

internal static class TopologyScenarioBuilderTests
{
    public static void MinimalBaselineAndStagesUseThreeLayerGate()
    {
        var source = StationLayoutTemplateService.Build(StationLayoutTemplateKind.IslandTwoTracks);
        var builder = TopologyScenarioBuilder.FromMinimalBaseline(source);

        var result = builder
            .AddStage(
                TopologyScenarioStage.FullStationChain,
                document => document with { ProjectName = "大型 sample station chain" },
                smokeDurationSeconds: 2)
            .AddStage(
                TopologyScenarioStage.ServicePatterns,
                document => document,
                smokeDurationSeconds: 2)
            .Build();

        Equal(3, result.StageValidations.Count, "baseline 與兩個階段都必須各自留下驗證結果。 ");
        True(result.IsValid, "所有階段都通過 Structural／Operational 後才可完成 build。 ");
        True(result.StageValidations.All(item => item.StructuralPassed && item.OperationalPassed),
            "每一階段都必須通過 Structural 與 Operational。 ");
        Equal("大型 sample station chain", result.Document.ProjectName, "stage transform 必須保留到 build 結果。 ");
    }

    public static void QuickBuilderCreatesValidatedMinimalBaseline()
    {
        var template = StationLayoutTemplateService.Build(StationLayoutTemplateKind.IslandTwoTracks);
        var builder = TopologyScenarioBuilder.CreateMinimalBaseline(
            template,
            [
                new QuickLinearStationInput("A", "甲站", 0, 0),
                new QuickLinearStationInput("B", "乙站", 800, 20),
                new QuickLinearStationInput("C", "丙站", 900, 20)
            ]);

        var result = builder.Build();
        Equal(1, result.StageValidations.Count, "quick builder 必須先留下 minimal baseline 驗證結果。 ");
        True(result.IsValid, "quick builder 產生的 minimal baseline 必須可建立 topology runtime。 ");
        True(result.Document.Topology.Edges.Count >= 4, "minimal baseline 必須包含上下行實體 edge。 ");
        TopologyProjectFormat.Validate(result.Document);
    }

    public static void InvalidStageDoesNotReplaceCurrentDocument()
    {
        var source = StationLayoutTemplateService.Build(StationLayoutTemplateKind.IslandTwoTracks);
        var builder = TopologyScenarioBuilder.FromMinimalBaseline(source);
        var original = builder.Document;

        try
        {
            builder.AddStage(
                TopologyScenarioStage.FullStationChain,
                document => document with { ProjectId = "" },
                smokeDurationSeconds: 2);
            throw new InvalidOperationException("不合法階段未被 Structural gate 拒絕。 ");
        }
        catch (SimulationValidationException exception)
        {
            True(exception.Errors.Any(error => error.Contains("專案編號", StringComparison.Ordinal)),
                "失敗階段應回報 Schema 8 欄位驗證錯誤。 ");
        }

        True(ReferenceEquals(original, builder.Document), "失敗階段不可取代目前有效文件。 ");
        Equal(2, builder.StageValidations.Count, "失敗階段仍應保留可追蹤的驗證結果。 ");
        True(!builder.StageValidations[^1].IsValid, "失敗階段結果必須標示為未通過。 ");
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}預期={expected}，實際={actual}。 ");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
