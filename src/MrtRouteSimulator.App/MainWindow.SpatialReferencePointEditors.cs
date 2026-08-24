using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

public partial class MainWindow
{
    private void EditSpatialReferencePoints_Click(object sender, RoutedEventArgs e)
    {
        var stations = DraftRows("中間站");
        var junctions = DraftRows("銜接點");
        var front = DraftRows("站前折返");
        var rear = DraftRows("站後折返");
        var pocket = DraftRows("中央避車線折返");
        var stationIds = StationRows.Select(row => row.StationId)
            .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var sequence = SpatialReferencePointRows.Count;

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

        var tabs = new TabControl();
        tabs.Items.Add(CreateEditableTab("中間站", stationGrid, stations,
            () => CreateRow("中間站", ++sequence, stationIds.FirstOrDefault())));
        tabs.Items.Add(CreateEditableTab("銜接點", junctionGrid, junctions,
            () => CreateRow("銜接點", ++sequence, stationIds.FirstOrDefault())));
        tabs.Items.Add(CreateEditableTab("站前折返", frontGrid, front,
            () => CreateRow("站前折返", ++sequence, stationIds.LastOrDefault())));
        tabs.Items.Add(CreateEditableTab("站後折返", rearGrid, rear,
            () => CreateRow("站後折返", ++sequence, stationIds.LastOrDefault())));
        tabs.Items.Add(CreateEditableTab("中央避車線折返", pocketGrid, pocket,
            () => CreateRow("中央避車線折返", ++sequence, stationIds.LastOrDefault())));

        var window = CreateEditorWindow("空間參考點／折返站型式", 1220, 650);
        window.Owner = this;
        var root = (DockPanel)window.Content;
        var note = new TextBlock
        {
            Text = "設定完成後會立即重繪路線圖。中間站可分別設定順行／逆行容量參數；站前折返的停站時間會取代該次折返的端點停站時間；站後與中央避車線的停等時間則用於站後折返。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12),
            Foreground = System.Windows.Media.Brushes.DimGray
        };
        DockPanel.SetDock(note, Dock.Top);
        root.Children.Add(note);
        root.Children.Add(tabs);
        AddOkCancel(window, () =>
        {
            CommitGrid(stationGrid); CommitGrid(junctionGrid); CommitGrid(frontGrid); CommitGrid(rearGrid); CommitGrid(pocketGrid);
            var combined = stations.Concat(junctions).Concat(front).Concat(rear).Concat(pocket).Select(Clone).ToArray();
            try
            {
                _ = BuildSpatialReferencePointDefinitions(combined);
                var duplicateStation = combined.GroupBy(row => row.StationId, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(group => group.Count() > 1);
                if (duplicateStation is not null)
                {
                    throw new SimulationValidationException([$"車站「{duplicateStation.Key}」只能指定一種空間參考點型式。　"]);
                }

                Replace(SpatialReferencePointRows, combined);
                CommitInfrastructurePreview("空間參考點已更新並立即呈現在路線圖；請重新建立模擬以套用行車與折返設定。", window);
            }
            catch (SimulationValidationException exception)
            {
                MessageBox.Show(window, string.Join(Environment.NewLine, exception.Errors), "空間參考點設定有誤",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        });
        window.ShowDialog();

        ObservableCollection<SpatialReferencePointInputRow> DraftRows(string kind) =>
            new(SpatialReferencePointRows.Where(row => row.Kind == kind).Select(Clone));
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
        window.DialogResult = true;
    }

    private static SpatialReferencePointInputRow CreateRow(string kind, int sequence, string? stationId)
    {
        var row = new SpatialReferencePointInputRow
        {
            ReferencePointId = $"REF-{sequence:000}",
            StationId = stationId ?? string.Empty,
            Name = kind,
            Kind = kind,
            MainlineApproachCruiseSpeedKmh = kind == "站前折返" ? 60 : 80
        };
        return row;
    }
}
