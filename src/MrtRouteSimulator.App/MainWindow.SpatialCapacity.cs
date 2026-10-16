using System.Collections.ObjectModel;
using System.Windows;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

public partial class MainWindow
{
    /// <summary>容量參考點結果只保存已計算的值；UI 不重做容量公式。</summary>
    public ObservableCollection<SpatialCapacityResultRow> SpatialCapacityRows { get; } = [];
    public ObservableCollection<SpatialCapacityBottleneckRow> SpatialCapacityBottleneckRows { get; } = [];

    private void ShowCapacityResults_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTopologyProjectDocument?.SpatialCapacity is not { Points.Length: > 0 })
        {
            ShowValidation(["請先配置至少一個容量參考點，再開啟容量圖表。"]);
            return;
        }
        try { new CapacityResultsWindow(_activeTopologyProjectDocument, () => _latestPlaybackFrame) { Owner = this }.ShowDialog(); }
        catch (SimulationValidationException exception) { ShowValidation(exception.Errors); }
    }
    private void EditSpatialCapacity_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTopologyProjectDocument is not { } document)
        {
            ShowValidation(["容量參考點設定只適用於 Schema 8 專案；請先載入或建立專案。"]);
            StatusTextBlock.Text = "目前專案不支援容量設定，未開啟編輯器。";
            return;
        }

        var editor = new SpatialCapacityEditorWindow(document) { Owner = this };
        if (editor.ShowDialog() != true || editor.Result is null)
        {
            StatusTextBlock.Text = "已取消容量設定；目前專案未變更。";
            return;
        }

        // 容量是獨立的分析設定；套用只更新 Schema 8 文件與結果頁，不重新模擬。
        _activeTopologyProjectDocument = editor.Result;
        RefreshSpatialCapacityResults();
        StatusTextBlock.Text = "容量設定已套用；未重新模擬，結果頁已依新設定更新。";
    }

    private void EditSpatialCapacitySensitivity_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTopologyProjectDocument?.SpatialCapacity is not { } settings || settings.Points.Length == 0)
        {
            ShowValidation(["請先配置至少一個容量參考點，再執行敏感度分析。"]);
            StatusTextBlock.Text = "尚未配置容量參考點，未開啟敏感度分析。";
            return;
        }

        var editor = new SpatialCapacitySensitivityWindow(settings) { Owner = this };
        if (editor.ShowDialog() != true || editor.Result is null)
        {
            StatusTextBlock.Text = "已取消容量敏感度分析。";
            return;
        }

        StatusTextBlock.Text = $"已完成 {editor.Result.Count} 筆容量敏感度分析結果。";
    }

    /// <summary>載入專案或套用容量設定後重新整理容量參考點結果。</summary>
    private void RefreshSpatialCapacityResults()
    {
        SpatialCapacityRows.Clear();
        SpatialCapacityBottleneckRows.Clear();
        var settings = _activeTopologyProjectDocument?.SpatialCapacity;
        if (settings is null)
        {
            SetSpatialCapacitySummary("尚未配置容量參考點；按「編輯容量設定…」建立手動分析設定。", configured: false, scenarioSummary: null);
            return;
        }

        try
        {
            SpatialCapacityProjectAnalysis.Validate(settings);
            foreach (var pointDto in settings.Points ?? [])
            {
                var point = SpatialCapacityProjectAnalysis.ToDefinition(pointDto);
                if (pointDto.Kind == SpatialReferencePointKind.IntermediateStation)
                {
                    AddSpatialCapacityResult(pointDto, point, settings, SpatialCapacityDirection.Forward, "順行");
                    AddSpatialCapacityResult(pointDto, point, settings, SpatialCapacityDirection.Reverse, "逆行");
                }
                else
                {
                    // These reference-point definitions have one geometry and one set of
                    // directional parameters.  Showing it twice would suggest that an
                    // independent reverse-direction model exists when it does not.
                    AddSpatialCapacityResult(pointDto, point, settings, SpatialCapacityDirection.Forward, "設定方向");
                }
            }

            string? scenarioSummary = null;
            if (settings.CompareAsSingleScenario && settings.Points is { Length: > 0 })
            {
                foreach (var bottleneck in SpatialCapacityScenarioAnalysis.Calculate(settings))
                {
                    SpatialCapacityBottleneckRows.Add(new SpatialCapacityBottleneckRow(
                        SpatialCapacityDirectionText(bottleneck.Direction),
                        bottleneck.ReferencePointId,
                        string.IsNullOrWhiteSpace(bottleneck.Name) ? bottleneck.ReferencePointId : bottleneck.Name,
                        FormatCapacityValue(bottleneck.Result.SafetyHeadwaySeconds),
                        FormatCapacityValue(bottleneck.Result.DesignHeadwaySeconds),
                        bottleneck.Result.LineCapacityPerHour.ToString("N0"),
                        SpatialCapacityModeText(settings.Mode)));
                }

                scenarioSummary = string.Join("；", SpatialCapacityBottleneckRows.Select(row =>
                    $"{row.Direction}瓶頸：{row.ReferencePoint} {row.DesignHeadway} 秒／{row.LineCapacity} 列／h"));
            }

            var localScopeText = settings.CompareAsSingleScenario && settings.Points is { Length: > 0 }
                ? "已啟用同一容量分析情境瓶頸摘要。"
                : "結果為各參考點局部分析，不代表整線最小容量。";
            SetSpatialCapacitySummary(
                $"已配置 {settings.Points?.Length ?? 0} 個手動參考點；結果依 {SpatialCapacityModeText(settings.Mode)} 計算。{localScopeText}",
                configured: true,
                scenarioSummary: scenarioSummary);
        }
        catch (SimulationValidationException exception)
        {
            SpatialCapacityRows.Clear();
            SpatialCapacityBottleneckRows.Clear();
            SetSpatialCapacitySummary($"容量設定驗證未通過：{string.Join("；", exception.Errors)}", configured: false, scenarioSummary: null);
        }
        catch (InvalidOperationException exception)
        {
            SpatialCapacityRows.Clear();
            SpatialCapacityBottleneckRows.Clear();
            SetSpatialCapacitySummary($"容量設定無法計算：{exception.Message}", configured: false, scenarioSummary: null);
        }
    }

    private void AddSpatialCapacityResult(
        ProjectSpatialReferencePoint pointDto,
        SpatialReferencePointDefinition point,
        SpatialCapacityProjectSettings settings,
        SpatialCapacityDirection direction,
        string directionText)
    {
        var result = SpatialCapacityAnalysis.Calculate(
            point,
            settings.Global,
            settings.Train,
            direction,
            settings.Mode);
        SpatialCapacityRows.Add(new SpatialCapacityResultRow(
            string.IsNullOrWhiteSpace(pointDto.Name) ? pointDto.ReferencePointId : pointDto.Name,
            SpatialCapacityKindText(pointDto.Kind),
            directionText,
            FormatCapacityValue(result.SafetyHeadwaySeconds),
            FormatCapacityValue(result.DesignHeadwaySeconds),
            result.LineCapacityPerHour.ToString("N0"),
            result.DesignPassengerCapacityPerHour.ToString("N0"),
            result.AchievablePassengerCapacityPerHour.ToString("N0"),
            SpatialCapacityModeText(settings.Mode)));
    }

    private void SetSpatialCapacitySummary(string text, bool configured, string? scenarioSummary)
    {
        if (SpatialCapacitySummaryText is not null)
            SpatialCapacitySummaryText.Text = text;
        if (SpatialCapacityBottleneckSummaryText is not null)
        {
            SpatialCapacityBottleneckSummaryText.Text = scenarioSummary ?? "";
            SpatialCapacityBottleneckSummaryText.Visibility = string.IsNullOrWhiteSpace(scenarioSummary)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
        if (SpatialCapacityBottleneckCard is not null)
            SpatialCapacityBottleneckCard.Visibility = string.IsNullOrWhiteSpace(scenarioSummary)
                ? Visibility.Collapsed
                : Visibility.Visible;
        if (SpatialCapacityDataGrid is not null)
            SpatialCapacityDataGrid.ToolTip = configured
                ? "容量參考點是手動分析假設，不是整線最大容量。"
                : text;
    }

    private static string FormatCapacityValue(double value) => value.ToString("0.###");

    internal static string SpatialCapacityDirectionText(SpatialCapacityDirection direction) => direction switch
    {
        SpatialCapacityDirection.Forward => "順行",
        SpatialCapacityDirection.Reverse => "逆行",
        _ => direction.ToString()
    };

    internal static string SpatialCapacityModeText(SpatialCapacityCalculationMode mode) => mode switch
    {
        SpatialCapacityCalculationMode.TaiwanHandbook => "台灣手冊",
        SpatialCapacityCalculationMode.UrcsCompatible => "URCS 相容",
        _ => mode.ToString()
    };

    internal static string SpatialCapacityKindText(SpatialReferencePointKind kind) => kind switch
    {
        SpatialReferencePointKind.IntermediateStation => "中間站",
        SpatialReferencePointKind.BeforeStationTurnback => "站前折返",
        SpatialReferencePointKind.AfterStationTurnback => "站後折返",
        SpatialReferencePointKind.CentralSidingTurnback => "中央避車線折返",
        SpatialReferencePointKind.Junction => "銜接點",
        _ => kind.ToString()
    };

    public sealed record SpatialCapacityResultRow(
        string ReferencePoint,
        string Kind,
        string Direction,
        string SafetyHeadway,
        string DesignHeadway,
        string LineCapacity,
        string DesignPassengerCapacity,
        string AchievablePassengerCapacity,
        string Mode);

    public sealed record SpatialCapacityBottleneckRow(
        string Direction,
        string ReferencePoint,
        string Name,
        string SafetyHeadway,
        string DesignHeadway,
        string LineCapacity,
        string Mode);
}
