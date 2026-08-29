using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

public partial class MainWindow
{
    private void EditSpatialReferencePoints_Click(object sender, RoutedEventArgs e)
    {
        if (TryActivateEditor("SpatialReferencePoints")) return;
        var initialRows = GetCompleteSpatialReferencePointRows();
        var stations = DraftRows("中間站");
        var junctions = DraftRows("銜接點");
        var front = DraftRows("站前折返");
        var rear = DraftRows("站後折返");
        var pocket = DraftRows("中央避車線折返");
        IEnumerable<SpatialReferencePointInputRow> templateSource = SpatialReferencePointTemplateRows.Count == 0
            ? CreateDefaultSpatialReferencePointTemplateRows()
            : SpatialReferencePointTemplateRows;
        var templates = new ObservableCollection<SpatialReferencePointInputRow>(templateSource.Select(Clone));
        var stationIds = StationRows.Select(row => row.StationId)
            .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var sequence = initialRows.Length;

        var stationGrid = CreateGrid(stations,
            TextColumn("參考點 ID", nameof(SpatialReferencePointInputRow.ReferencePointId), 105),
            ComboColumn("車站", nameof(SpatialReferencePointInputRow.StationId), stationIds, 85),
            TextColumn("名稱", nameof(SpatialReferencePointInputRow.Name), 120),
            TextColumn("順行進站坡度 ‰", nameof(SpatialReferencePointInputRow.StationForwardGradeInPermille), 115),
            TextColumn("順行離站坡度 ‰", nameof(SpatialReferencePointInputRow.StationForwardGradeOutPermille), 115),
            TextColumn("順行離開點距離 m", nameof(SpatialReferencePointInputRow.StationForwardDistanceToSignalMeters), 125),
            TextColumn("順行重疊區 m", nameof(SpatialReferencePointInputRow.StationForwardOverlapMeters), 105),
            TextColumn("順行停站 s", nameof(SpatialReferencePointInputRow.StationForwardDwellSeconds), 90),
            TextColumn("順行進站前 km/h", nameof(SpatialReferencePointInputRow.StationForwardEarlierCruiseSpeedKmh), 120),
            TextColumn("順行離站後 km/h", nameof(SpatialReferencePointInputRow.StationForwardLaterCruiseSpeedKmh), 120),
            TextColumn("順行安全係數", nameof(SpatialReferencePointInputRow.StationForwardSafetyFactor), 105),
            TextColumn("逆行進站坡度 ‰", nameof(SpatialReferencePointInputRow.StationReverseGradeInPermille), 115),
            TextColumn("逆行離站坡度 ‰", nameof(SpatialReferencePointInputRow.StationReverseGradeOutPermille), 115),
            TextColumn("逆行離開點距離 m", nameof(SpatialReferencePointInputRow.StationReverseDistanceToSignalMeters), 125),
            TextColumn("逆行重疊區 m", nameof(SpatialReferencePointInputRow.StationReverseOverlapMeters), 105),
            TextColumn("逆行停站 s", nameof(SpatialReferencePointInputRow.StationReverseDwellSeconds), 90),
            TextColumn("逆行進站前 km/h", nameof(SpatialReferencePointInputRow.StationReverseEarlierCruiseSpeedKmh), 120),
            TextColumn("逆行離站後 km/h", nameof(SpatialReferencePointInputRow.StationReverseLaterCruiseSpeedKmh), 120),
            TextColumn("逆行安全係數", nameof(SpatialReferencePointInputRow.StationReverseSafetyFactor), 105));

        var junctionGrid = CreateGrid(junctions,
            TextColumn("參考點 ID", nameof(SpatialReferencePointInputRow.ReferencePointId), 105),
            ComboColumn("車站", nameof(SpatialReferencePointInputRow.StationId), stationIds, 85),
            TextColumn("名稱", nameof(SpatialReferencePointInputRow.Name), 120),
            TextColumn("主線往銜接點坡度 ‰", nameof(SpatialReferencePointInputRow.MainlineGradePermille), 135),
            TextColumn("側線往銜接點坡度 ‰", nameof(SpatialReferencePointInputRow.BranchlineGradePermille), 135),
            TextColumn("橫渡線長度 m", nameof(SpatialReferencePointInputRow.CrossoverLengthMeters), 105),
            TextColumn("道岔限速 km/h", nameof(SpatialReferencePointInputRow.SwitchSpeedLimitKmh), 105),
            TextColumn("主線接近巡航 km/h", nameof(SpatialReferencePointInputRow.MainlineApproachCruiseSpeedKmh), 125),
            TextColumn("側線接近巡航 km/h", nameof(SpatialReferencePointInputRow.BranchlineApproachCruiseSpeedKmh), 125),
            TextColumn("主線安全係數", nameof(SpatialReferencePointInputRow.MainlineSafetyFactor), 105),
            TextColumn("側線安全係數", nameof(SpatialReferencePointInputRow.BranchlineSafetyFactor), 105),
            TextColumn("主線通過比例", nameof(SpatialReferencePointInputRow.MainlineTrafficRatio), 105));

        var frontGrid = CreateGrid(front,
            TextColumn("參考點 ID", nameof(SpatialReferencePointInputRow.ReferencePointId), 105),
            ComboColumn("車站", nameof(SpatialReferencePointInputRow.StationId), stationIds, 85),
            TextColumn("名稱", nameof(SpatialReferencePointInputRow.Name), 120),
            CheckColumn("站內交替停靠不同股道", nameof(SpatialReferencePointInputRow.AlternateBerthing), 145),
            TextColumn("站前進站坡度 ‰", nameof(SpatialReferencePointInputRow.MainlineGradePermille), 115),
            TextColumn("停車點至橫渡線 m", nameof(SpatialReferencePointInputRow.DistanceFromStopToCrossoverMeters), 125),
            TextColumn("橫渡線長度 m", nameof(SpatialReferencePointInputRow.CrossoverLengthMeters), 105),
            TextColumn("停站時間 s", nameof(SpatialReferencePointInputRow.TurnbackDwellSeconds), 95),
            TextColumn("道岔限速 km/h", nameof(SpatialReferencePointInputRow.SwitchSpeedLimitKmh), 105),
            TextColumn("進站前巡航 km/h", nameof(SpatialReferencePointInputRow.MainlineApproachCruiseSpeedKmh), 120),
            TextColumn("橫渡線安全係數", nameof(SpatialReferencePointInputRow.MainlineSafetyFactor), 115));

        var rearGrid = CreateGrid(rear,
            TextColumn("參考點 ID", nameof(SpatialReferencePointInputRow.ReferencePointId), 105),
            ComboColumn("車站", nameof(SpatialReferencePointInputRow.StationId), stationIds, 85),
            TextColumn("名稱", nameof(SpatialReferencePointInputRow.Name), 120),
            CheckColumn("尾軌交替停靠不同股道", nameof(SpatialReferencePointInputRow.AlternateBerthing), 145),
            TextColumn("站後離站坡度 ‰", nameof(SpatialReferencePointInputRow.MainlineGradePermille), 115),
            TextColumn("停車點至橫渡線 m", nameof(SpatialReferencePointInputRow.DistanceFromStopToCrossoverMeters), 125),
            TextColumn("橫渡線長度 m", nameof(SpatialReferencePointInputRow.CrossoverLengthMeters), 105),
            TextColumn("橫渡線至尾軌停車區 m", nameof(SpatialReferencePointInputRow.DistanceFromCrossoverToTurnbackStopMeters), 145),
            TextColumn("尾軌停等時間 s", nameof(SpatialReferencePointInputRow.TurnbackDwellSeconds), 110),
            TextColumn("道岔限速 km/h", nameof(SpatialReferencePointInputRow.SwitchSpeedLimitKmh), 105));

        var pocketGrid = CreateGrid(pocket,
            TextColumn("參考點 ID", nameof(SpatialReferencePointInputRow.ReferencePointId), 105),
            ComboColumn("車站", nameof(SpatialReferencePointInputRow.StationId), stationIds, 85),
            TextColumn("名稱", nameof(SpatialReferencePointInputRow.Name), 120),
            TextColumn("進中央避車線坡度 ‰", nameof(SpatialReferencePointInputRow.MainlineGradePermille), 135),
            TextColumn("停車點至橫渡線 m", nameof(SpatialReferencePointInputRow.DistanceFromStopToCrossoverMeters), 125),
            TextColumn("橫渡線長度 m", nameof(SpatialReferencePointInputRow.CrossoverLengthMeters), 105),
            TextColumn("橫渡線至避車線停車區 m", nameof(SpatialReferencePointInputRow.DistanceFromCrossoverToTurnbackStopMeters), 155),
            TextColumn("避車線折返停等 s", nameof(SpatialReferencePointInputRow.TurnbackDwellSeconds), 125),
            TextColumn("道岔限速 km/h", nameof(SpatialReferencePointInputRow.SwitchSpeedLimitKmh), 105));

        var templateGrid = CreateGrid(templates,
            TextColumn("站場型式", nameof(SpatialReferencePointInputRow.Kind), 110),
            CheckColumn("交替停靠", nameof(SpatialReferencePointInputRow.AlternateBerthing), 85),
            TextColumn("主線坡度 ‰", nameof(SpatialReferencePointInputRow.MainlineGradePermille), 90),
            TextColumn("側線坡度 ‰", nameof(SpatialReferencePointInputRow.BranchlineGradePermille), 90),
            TextColumn("停車點至橫渡線 m", nameof(SpatialReferencePointInputRow.DistanceFromStopToCrossoverMeters), 125),
            TextColumn("橫渡線長度 m", nameof(SpatialReferencePointInputRow.CrossoverLengthMeters), 110),
            TextColumn("橫渡線至停車區 m", nameof(SpatialReferencePointInputRow.DistanceFromCrossoverToTurnbackStopMeters), 130),
            TextColumn("折返停等 s", nameof(SpatialReferencePointInputRow.TurnbackDwellSeconds), 90),
            TextColumn("道岔 km/h", nameof(SpatialReferencePointInputRow.SwitchSpeedLimitKmh), 90),
            TextColumn("主線 km/h", nameof(SpatialReferencePointInputRow.MainlineApproachCruiseSpeedKmh), 90),
            TextColumn("側線 km/h", nameof(SpatialReferencePointInputRow.BranchlineApproachCruiseSpeedKmh), 90),
            TextColumn("主線安全", nameof(SpatialReferencePointInputRow.MainlineSafetyFactor), 85),
            TextColumn("側線安全", nameof(SpatialReferencePointInputRow.BranchlineSafetyFactor), 85),
            TextColumn("主線比例", nameof(SpatialReferencePointInputRow.MainlineTrafficRatio), 85),
            TextColumn("順進坡度 ‰", nameof(SpatialReferencePointInputRow.StationForwardGradeInPermille), 90),
            TextColumn("順出坡度 ‰", nameof(SpatialReferencePointInputRow.StationForwardGradeOutPermille), 90),
            TextColumn("順離開 m", nameof(SpatialReferencePointInputRow.StationForwardDistanceToSignalMeters), 90),
            TextColumn("順重疊 m", nameof(SpatialReferencePointInputRow.StationForwardOverlapMeters), 90),
            TextColumn("順停站 s", nameof(SpatialReferencePointInputRow.StationForwardDwellSeconds), 85),
            TextColumn("順進站 km/h", nameof(SpatialReferencePointInputRow.StationForwardEarlierCruiseSpeedKmh), 100),
            TextColumn("順離站 km/h", nameof(SpatialReferencePointInputRow.StationForwardLaterCruiseSpeedKmh), 100),
            TextColumn("順安全", nameof(SpatialReferencePointInputRow.StationForwardSafetyFactor), 80),
            TextColumn("逆進坡度 ‰", nameof(SpatialReferencePointInputRow.StationReverseGradeInPermille), 90),
            TextColumn("逆出坡度 ‰", nameof(SpatialReferencePointInputRow.StationReverseGradeOutPermille), 90),
            TextColumn("逆離開 m", nameof(SpatialReferencePointInputRow.StationReverseDistanceToSignalMeters), 90),
            TextColumn("逆重疊 m", nameof(SpatialReferencePointInputRow.StationReverseOverlapMeters), 90),
            TextColumn("逆停站 s", nameof(SpatialReferencePointInputRow.StationReverseDwellSeconds), 85),
            TextColumn("逆進站 km/h", nameof(SpatialReferencePointInputRow.StationReverseEarlierCruiseSpeedKmh), 100),
            TextColumn("逆離站 km/h", nameof(SpatialReferencePointInputRow.StationReverseLaterCruiseSpeedKmh), 100),
            TextColumn("逆安全", nameof(SpatialReferencePointInputRow.StationReverseSafetyFactor), 80));
        templateGrid.Columns[0].IsReadOnly = true;

        var tabs = new TabControl();
        tabs.Items.Add(CreateEditableTab("中間站", stationGrid, stations,
            () => CreateRow("中間站", ++sequence, stationIds.FirstOrDefault(), templates)));
        tabs.Items.Add(CreateEditableTab("銜接點", junctionGrid, junctions,
            () => CreateRow("銜接點", ++sequence, stationIds.FirstOrDefault(), templates)));
        tabs.Items.Add(CreateEditableTab("站前折返", frontGrid, front,
            () => CreateRow("站前折返", ++sequence, stationIds.LastOrDefault(), templates)));
        tabs.Items.Add(CreateEditableTab("站後折返", rearGrid, rear,
            () => CreateRow("站後折返", ++sequence, stationIds.LastOrDefault(), templates)));
        tabs.Items.Add(CreateEditableTab("中央避車線折返", pocketGrid, pocket,
            () => CreateRow("中央避車線折返", ++sequence, stationIds.LastOrDefault(), templates)));
        tabs.Items.Add(new TabItem
        {
            Header = "新增站預設範本",
            Content = new DockPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = "這五筆範本只會在按「新增」建立實體站場時複製；變更範本不會回寫現有站場的個別覆寫。",
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(10, 10, 10, 0)
                    },
                    templateGrid
                }
            }
        });

        var window = CreateEditorWindow("空間參考點／折返站型式", 1220, 650);
        RegisterEditorWindow("SpatialReferencePoints", window);
        window.Owner = this;
        var root = (DockPanel)window.Content;
        var note = new TextBlock
        {
            Text = "每一實體站只能選一種型式。新增站場會複製「新增站預設範本」的當前值；中間站可分別設定順行／逆行容量參數；站前折返的停站時間會取代該次折返的端點停站時間。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12),
            Foreground = System.Windows.Media.Brushes.DimGray
        };
        DockPanel.SetDock(note, Dock.Top);
        root.Children.Add(note);
        root.Children.Add(tabs);
        AddOkCancel(window, () =>
        {
            CommitGrid(stationGrid); CommitGrid(junctionGrid); CommitGrid(frontGrid); CommitGrid(rearGrid); CommitGrid(pocketGrid); CommitGrid(templateGrid);
            var combined = stations.Concat(junctions).Concat(front).Concat(rear).Concat(pocket).Select(Clone).ToArray();
            try
            {
                _ = BuildSpatialReferencePointDefinitions(combined);
                _ = BuildSpatialReferencePointTemplateDefinitions(templates);
                var duplicateStation = combined.GroupBy(row => row.StationId, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(group => group.Count() > 1);
                if (duplicateStation is not null)
                {
                    throw new SimulationValidationException([$"車站「{duplicateStation.Key}」只能指定一種空間參考點型式。　"]);
                }
                var missingStations = stationIds.Except(combined.Select(row => row.StationId), StringComparer.OrdinalIgnoreCase).ToArray();
                if (missingStations.Length > 0)
                {
                    throw new SimulationValidationException([$"每一實體站都必須指定一種空間參考點型式；尚未分類：{string.Join("、", missingStations)}。"]);
                }

                Replace(SpatialReferencePointRows, combined);
                Replace(SpatialReferencePointTemplateRows, templates.Select(Clone));
                CommitInfrastructurePreview("空間參考點已更新並立即呈現在路線圖；請重新建立模擬以套用行車與折返設定。", window);
            }
            catch (SimulationValidationException exception)
            {
                MessageBox.Show(window, string.Join(Environment.NewLine, exception.Errors), "空間參考點設定有誤",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        });
        window.Show();

        ObservableCollection<SpatialReferencePointInputRow> DraftRows(string kind) =>
            new(initialRows.Where(row => row.Kind == kind).Select(Clone));
    }

    private void CommitInfrastructurePreview(string status, Window window)
    {
        PausePlayback();
        ClearResults();
        try
        {
            _route = BuildRoutePreview();
            _v2Enabled = true;
            RouteSummaryText.Text = $"{_route.Stations.Count} 站 · {_route.TotalLengthMeters / 1000:0.###} km · 站場配置預覽";
            PlaybackStatusText.Text = "空間參考點預覽已更新；建立模擬後列車會依設定顯示。";
            DrawV2Route();
        }
        catch (SimulationValidationException)
        {
            // 車站主表仍在編輯時保留已提交設定；建立模擬會顯示完整驗證原因。
        }
        StatusTextBlock.Text = status;
        window.Close();
    }

    private static SpatialReferencePointInputRow CreateRow(
        string kind,
        int sequence,
        string? stationId,
        IEnumerable<SpatialReferencePointInputRow> templates)
    {
        var template = templates.Single(item => item.Kind.Equals(kind, StringComparison.Ordinal));
        var row = Clone(template);
        row.ReferencePointId = $"REF-{sequence:000}";
        row.StationId = stationId ?? string.Empty;
        row.Name = kind;
        row.Kind = kind;
        return row;
    }
}
