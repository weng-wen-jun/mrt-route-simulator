using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

public partial class MainWindow
{
    private SimulationWorld? _v2World;
    private SimulationWorld? _plannedWorld;
    private bool _v2Enabled;

    public ObservableCollection<SpeedLimitInputRow> SpeedLimitRows { get; } = [];

    public ObservableCollection<SafetyRow> SafetyRows { get; } = [];

    public ObservableCollection<EventRow> EventRows { get; } = [];

    public IReadOnlyList<string> SpeedLimitDirectionOptions { get; } = ["雙向", "下行", "上行"];

    private void LoadSampleV2Data()
    {
        SpeedLimitRows.Clear();
        SpeedLimitRows.Add(new SpeedLimitInputRow
        {
            StartKm = 1.25,
            EndKm = 1.87,
            LimitKmh = 45,
            Direction = "雙向",
            Note = "示範彎道路段"
        });
        SpeedLimitRows.Add(new SpeedLimitInputRow
        {
            StartKm = 3.30,
            EndKm = 4.20,
            LimitKmh = 55,
            Direction = "下行",
            Note = "示範方向別速限"
        });
        JerkTextBox.Text = "0.65";
        CoastingRatioTextBox.Text = "0.15";
        ApproachDistanceTextBox.Text = "180";
        ApproachSpeedTextBox.Text = "30";
        TrainLengthTextBox.Text = "92";
        ReactionTimeTextBox.Text = "1.5";
        ServiceBrakeTextBox.Text = "0.9";
        EmergencyBrakeTextBox.Text = "1.3";
        SpeedLimitWarningText.Text = string.Empty;
    }

    private void ConfigureV2World(int trainCount, double? specifiedHeadwaySeconds)
    {
        _v2Enabled = OperationModeComboBox.SelectedItem is ComboBoxItem item
            && string.Equals(item.Tag?.ToString(), "Realistic", StringComparison.Ordinal);
        if (!_v2Enabled)
        {
            _v2World = null;
            _plannedWorld = null;
            ObstacleStopButton.IsEnabled = false;
            return;
        }

        if (_route is null || _parameters is null)
        {
            throw new InvalidOperationException("請先建立有效的 V1 基準路線與列車性能。");
        }

        SpeedLimitDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        SpeedLimitDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var operational = new OperationalParameters(
            ParsePositive(JerkTextBox, "Jerk"),
            ParseNonNegative(CoastingRatioTextBox, "惰行比例"),
            ParseNonNegative(ApproachDistanceTextBox, "進站控制距離"),
            ParseNonNegative(ApproachSpeedTextBox, "進站控制速度") / 3.6,
            tractionFadeRatio: 0.45,
            ParsePositive(TrainLengthTextBox, "車長"),
            ParsePositive(ServiceBrakeTextBox, "營運煞車減速度"),
            ParsePositive(EmergencyBrakeTextBox, "緊急煞車減速度"),
            ParseNonNegative(ReactionTimeTextBox, "控制反應時間"),
            brakeBuildUpTimeSeconds: 0.8,
            positioningErrorMeters: 3,
            safetyMarginMeters: 25,
            absoluteMinimumGapMeters: 15);
        var limits = SpeedLimitRows.Select((row, index) =>
        {
            if (!double.IsFinite(row.StartKm)
                || !double.IsFinite(row.EndKm)
                || !double.IsFinite(row.LimitKmh))
            {
                throw new InvalidOperationException($"速限第 {index + 1} 列必須使用有限數值。");
            }

            return new SpeedLimitSegment(
                row.StartKm * 1000,
                row.EndKm * 1000,
                row.LimitKmh / 3.6,
                ParseSpeedLimitDirection(row.Direction, index + 1),
                row.Note?.Trim() ?? string.Empty);
        }).ToArray();
        var movingBlockMode = ParseMovingBlockMode();
        _v2World = new SimulationWorld(
            _route,
            _parameters,
            operational,
            trainCount,
            specifiedHeadwaySeconds,
            limits,
            OperationProfileMode.RealisticOperations,
            movingBlockMode);
        _plannedWorld = new SimulationWorld(
            _route,
            _parameters,
            operational,
            trainCount,
            specifiedHeadwaySeconds,
            speedLimits: null,
            OperationProfileMode.BasicPhysics,
            MovingBlockMode.Independent);
        _playbackDurationSeconds = _v2World.BaselineCycleTimeSeconds * 1.5
            + (trainCount - 1) * _v2World.HeadwaySeconds;
        ObstacleStopButton.IsEnabled = true;
        SpeedLimitWarningText.Text = string.Join("　", _v2World.SpeedLimits.GetOverlapWarnings());
        PopulateFilterControls(trainCount);
    }

    private void PopulateV2Results()
    {
        if (!_v2Enabled || _v2World is null)
        {
            return;
        }

        HeadwaySummaryText.Text = $"{FormatDuration(_v2World.HeadwaySeconds)}（基準）";
        PlaybackStatusText.Text = "V2 已就緒；播放時每個 0.1 秒控制與碰撞子步進都會依序執行。";
        DrawV2Route();
        DrawV2SpeedProfile();
        DrawSafetyDistanceChart();
        DrawTimeDistanceDiagram();
    }

    private void UpdateV2PlaybackView()
    {
        if (!_v2Enabled || _v2World is null || _plannedWorld is null)
        {
            return;
        }

        _v2World.AdvanceTo(_playbackTimeSeconds);
        _plannedWorld.AdvanceTo(_playbackTimeSeconds);
        var snapshot = _v2World.GetSnapshot();
        CurrentTrainRows.Clear();
        foreach (var state in snapshot.Trains)
        {
            CurrentTrainRows.Add(new CurrentTrainRow(
                state.VehicleId.Replace("Vehicle ", "V", StringComparison.Ordinal),
                state.IsActive ? DirectionToChinese(state.Direction) : "—",
                state.IsActive ? PhaseToChinese(state.Phase) : "待發",
                $"{state.FrontPositionMeters / 1000:0.00}",
                $"{state.SpeedMetersPerSecond * 3.6:0.#}",
                state.CurrentStationId,
                state.NextStationId ?? "—"));
        }

        SafetyRows.Clear();
        foreach (var observation in snapshot.SafetyObservations.Where(MatchesSafetyFilters))
        {
            SafetyRows.Add(new SafetyRow(
                $"{ShortVehicle(observation.FollowerVehicleId)} → {ShortVehicle(observation.LeaderVehicleId)}",
                observation.TrackId,
                $"{observation.FollowerFrontPositionMeters / 1000:0.00}",
                $"{observation.LeaderRearPositionMeters / 1000:0.00}",
                $"{observation.ActualGapMeters:0.0}",
                $"{observation.DynamicSafetyDistanceMeters:0.0}",
                $"{observation.ObstacleBrakingDemandMeters:0.0}",
                $"{observation.SafetyMarginMeters:0.0}",
                SafetyStatusToChinese(observation.Status)));
        }

        EventRows.Clear();
        foreach (var simulationEvent in _v2World.Events.TakeLast(300).Reverse())
        {
            EventRows.Add(new EventRow(
                TrajectoryAnalysis.FormatClock(_startClockSeconds + simulationEvent.SimulationTimeSeconds),
                EventTypeToChinese(simulationEvent.EventType),
                string.IsNullOrWhiteSpace(simulationEvent.VehicleId) ? "—" : ShortVehicle(simulationEvent.VehicleId),
                $"{simulationEvent.PositionMeters / 1000:0.00}",
                simulationEvent.Message));
        }

        RefreshPairFilter(snapshot.SafetyObservations);
        UpdateSafetySummary();
        SimulationClockText.Text = TrajectoryAnalysis.FormatClock(_startClockSeconds + _playbackTimeSeconds);
        DrawV2Route(snapshot);
        DrawV2SpeedProfile();
        DrawSafetyDistanceChart();
        DrawTimeDistanceDiagram();
        _v2World.AcknowledgeSnapshotEvents();
    }

    private void ResetV2Playback()
    {
        _v2World?.Reset();
        _plannedWorld?.Reset();
        SafetyRows.Clear();
        EventRows.Clear();
        SafetyPairComboBox.Items.Clear();
        SafetySummaryText.Text = "建立 V2 模擬後顯示安全摘要。";
        DrawSafetyDistanceChart();
        DrawTimeDistanceDiagram();
    }

    private void ClearV2Results()
    {
        _v2World = null;
        _plannedWorld = null;
        _v2Enabled = false;
        SafetyRows.Clear();
        EventRows.Clear();
        SafetyPairComboBox.Items.Clear();
        DiagramVehicleComboBox.Items.Clear();
        ObstacleTrainComboBox.Items.Clear();
        ObstacleStopButton.IsEnabled = false;
        SafetySummaryText.Text = "建立 V2 模擬後顯示安全摘要。";
        DrawSafetyDistanceChart();
        DrawTimeDistanceDiagram();
    }

    private void AddSpeedLimit_Click(object sender, RoutedEventArgs e)
    {
        SpeedLimitRows.Add(new SpeedLimitInputRow
        {
            StartKm = 0,
            EndKm = _route is null ? 0.10 : Math.Min(0.10, _route.TotalLengthMeters / 1000),
            LimitKmh = 45,
            Direction = "雙向"
        });
        SpeedLimitDataGrid.SelectedIndex = SpeedLimitRows.Count - 1;
        SpeedLimitDataGrid.ScrollIntoView(SpeedLimitRows[^1]);
    }

    private void RemoveSpeedLimit_Click(object sender, RoutedEventArgs e)
    {
        var index = SpeedLimitDataGrid.SelectedIndex;
        if (index < 0)
        {
            ShowValidation(["請先選取要刪除的速限列。"]);
            return;
        }

        SpeedLimitRows.RemoveAt(index);
        HideValidation();
    }

    private void MovingBlockMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_v2World is null)
        {
            return;
        }

        _v2World.SetMovingBlockMode(ParseMovingBlockMode());
        UpdateV2PlaybackView();
    }

    private void BrakingMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_v2World is null || BrakingModeComboBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var mode = string.Equals(item.Tag?.ToString(), "Emergency", StringComparison.Ordinal)
            ? BrakingEstimationMode.Emergency
            : BrakingEstimationMode.Service;
        _v2World.SetBrakingEstimationMode(mode);
        UpdateV2PlaybackView();
    }

    private void ObstacleStop_Click(object sender, RoutedEventArgs e)
    {
        if (_v2World is null)
        {
            return;
        }

        var selectedVehicle = ObstacleTrainComboBox.SelectedItem?.ToString();
        var snapshot = _v2World.GetSnapshot();
        var target = snapshot.Trains.FirstOrDefault(train => train.VehicleId == selectedVehicle)
            ?? snapshot.Trains
                .Where(train => train.IsActive && train.Phase is not OperationalPhase.Collided and not OperationalPhase.EmergencyStopped)
                .OrderByDescending(train => train.Direction == TrainDirection.Outbound
                    ? train.FrontPositionMeters
                    : _route!.TotalLengthMeters - train.FrontPositionMeters)
                .FirstOrDefault();
        if (target is null)
        {
            ShowValidation(["目前沒有可排程障礙物急停的列車。"]);
            return;
        }

        var delay = ParseNonNegative(ObstacleDelayTextBox, "障礙物急停延遲時間");
        if (delay > 0)
        {
            _v2World.ScheduleObstacleEmergencyStop(target.VehicleId, _v2World.CurrentTimeSeconds + delay);
            PlaybackStatusText.Text = $"已排程 {target.VehicleId} 於 {delay:0.0} 秒後觸發障礙物急停。";
        }
        else
        {
            if (!target.IsActive)
            {
                ShowValidation([$"{target.VehicleId} 尚未發車；請輸入大於 0 的延遲秒數，或選擇營運中列車。"]);
                return;
            }

            _v2World.TriggerObstacleEmergencyStop(target.VehicleId);
            PlaybackStatusText.Text = $"已觸發 {target.VehicleId} 障礙物急停；這是保守例外事件。";
        }

        UpdateV2PlaybackView();
    }

    private void SafetyPair_SelectionChanged(object sender, SelectionChangedEventArgs e) => DrawSafetyDistanceChart();

    private void SafetyFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_v2World is not null)
        {
            UpdateV2PlaybackView();
        }
    }

    private void SafetyDistanceCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawSafetyDistanceChart();

    private void TimeDistanceCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawTimeDistanceDiagram();

    private void DiagramFilter_Changed(object sender, RoutedEventArgs e) => DrawTimeDistanceDiagram();

    private void DiagramFilter_Changed(object sender, SelectionChangedEventArgs e) => DrawTimeDistanceDiagram();

    private void DiagramZoom_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => DrawTimeDistanceDiagram();

    private void DiagramTimeFilter_TextChanged(object sender, TextChangedEventArgs e) => DrawTimeDistanceDiagram();

    private void ExportPng_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureDiagramAvailable())
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "匯出列車運行圖 PNG",
            Filter = "PNG 圖片 (*.png)|*.png",
            DefaultExt = ".png",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"{_route!.RouteName}_列車運行圖.png"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            DiagramExportService.ExportPng(TimeDistanceCanvas, dialog.FileName, HighResolutionCheckBox.IsChecked == true ? 2 : 1);
            StatusTextBlock.Text = $"PNG 已匯出：{dialog.FileName}";
        }
        catch (Exception exception)
        {
            ShowValidation([$"PNG 匯出失敗：{exception.Message}"]);
        }
    }

    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureDiagramAvailable())
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "匯出列車運行圖 PDF（A4 橫向）",
            Filter = "PDF 文件 (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"{_route!.RouteName}_列車運行圖.pdf"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var pageSize = GetSelectedTag(PdfPageSizeComboBox) == "A3" ? PdfPageSize.A3 : PdfPageSize.A4;
            DiagramExportService.ExportPdf(
                TimeDistanceCanvas,
                dialog.FileName,
                pageSize,
                PdfSplitPagesCheckBox.IsChecked == true);
            StatusTextBlock.Text = $"PDF 已匯出：{dialog.FileName}";
        }
        catch (Exception exception)
        {
            ShowValidation([$"PDF 匯出失敗：{exception.Message}"]);
        }
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_v2World is null || _route is null || _v2World.Trajectory.Count == 0)
        {
            ShowValidation(["請先播放 V2 模擬，產生軌跡後再匯出 CSV。"]);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "匯出軌跡與事件 CSV",
            Filter = "CSV 資料 (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"{_route.RouteName}_軌跡事件.csv"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var csv = TrajectoryAnalysis.BuildCsv(_route, _v2World.Trajectory, _v2World.Events, _startClockSeconds);
            File.WriteAllText(dialog.FileName, csv, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            StatusTextBlock.Text = $"CSV 已匯出：{dialog.FileName}";
        }
        catch (Exception exception)
        {
            ShowValidation([$"CSV 匯出失敗：{exception.Message}"]);
        }
    }

    private void DrawV2Route(SimulationSnapshot? snapshot = null)
    {
        RouteCanvas.Children.Clear();
        var width = RouteCanvas.ActualWidth;
        var height = RouteCanvas.ActualHeight;
        if (width < 100 || height < 100 || _route is null)
        {
            return;
        }

        var left = 60d;
        var right = 38d;
        var trackWidth = Math.Max(1, width - left - right);
        var outboundY = height * 0.39;
        var inboundY = height * 0.63;
        DrawTrackLine(outboundY, "下行 DOWN →");
        DrawTrackLine(inboundY, "← 上行 UP");

        if (_v2World is not null)
        {
            foreach (var limit in _v2World.SpeedLimits.Limits)
            {
                var x1 = left + limit.StartPositionMeters / _route.TotalLengthMeters * trackWidth;
                var x2 = left + limit.EndPositionMeters / _route.TotalLengthMeters * trackWidth;
                var top = limit.Direction == SpeedLimitDirection.Inbound ? inboundY - 13 : outboundY - 13;
                var zoneHeight = limit.Direction == SpeedLimitDirection.Both ? inboundY - outboundY + 26 : 26;
                var rectangle = new Rectangle
                {
                    Width = Math.Max(2, x2 - x1),
                    Height = zoneHeight,
                    Fill = new SolidColorBrush(Color.FromArgb(42, 231, 165, 48)),
                    Stroke = new SolidColorBrush(Color.FromRgb(205, 126, 24)),
                    StrokeDashArray = [3, 2],
                    ToolTip = $"速限 {limit.StartPositionMeters / 1000:0.00}～{limit.EndPositionMeters / 1000:0.00} km\n{limit.LimitMetersPerSecond * 3.6:0.#} km/h · {SpeedLimitDirectionToChinese(limit.Direction)}\n{limit.Note}"
                };
                Canvas.SetLeft(rectangle, x1);
                Canvas.SetTop(rectangle, top);
                RouteCanvas.Children.Add(rectangle);
                AddCanvasText(RouteCanvas, $"{limit.LimitMetersPerSecond * 3.6:0.#}", x1 + 2, top - 17, 10, Color.FromRgb(166, 90, 21));
            }
        }

        foreach (var station in _route.Stations)
        {
            var x = left + station.PositionMeters / _route.TotalLengthMeters * trackWidth;
            RouteCanvas.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = outboundY - 18,
                Y2 = inboundY + 18,
                Stroke = new SolidColorBrush(Color.FromRgb(174, 183, 199)),
                StrokeThickness = 1
            });
            AddCanvasText(RouteCanvas, $"{station.StationId}\n{station.PositionMeters / 1000:0.00} km", Math.Clamp(x - 28, 0, width - 58), inboundY + 25, 10, Color.FromRgb(55, 66, 86));
        }

        snapshot ??= _v2World?.GetSnapshot();
        if (snapshot is null)
        {
            return;
        }

        foreach (var observation in snapshot.SafetyObservations)
        {
            var x1 = left + observation.FollowerFrontPositionMeters / _route.TotalLengthMeters * trackWidth;
            var x2 = left + observation.LeaderRearPositionMeters / _route.TotalLengthMeters * trackWidth;
            var y = observation.Direction == TrainDirection.Outbound ? outboundY - 32 : inboundY + 32;
            var color = SafetyStatusColor(observation.Status);
            RouteCanvas.Children.Add(new Line
            {
                X1 = x1,
                X2 = x2,
                Y1 = y,
                Y2 = y,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 3,
                ToolTip = $"{ShortVehicle(observation.FollowerVehicleId)} → {ShortVehicle(observation.LeaderVehicleId)}\n"
                    + $"淨距 {observation.ActualGapMeters:0.0} m｜安全 {observation.DynamicSafetyDistanceMeters:0.0} m\n"
                    + $"後車頭 {observation.FollowerFrontPositionMeters / 1000:0.00} km｜前車尾 {observation.LeaderRearPositionMeters / 1000:0.00} km"
            });
        }

        foreach (var state in snapshot.Trains.Where(train => train.IsActive))
        {
            var x = left + state.FrontPositionMeters / _route.TotalLengthMeters * trackWidth;
            var y = state.Direction == TrainDirection.Outbound ? outboundY : inboundY;
            var index = ParseVehicleIndex(state.VehicleId);
            var train = new Border
            {
                Width = 43,
                Height = 23,
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(state.Phase is OperationalPhase.Collided or OperationalPhase.EmergencyStopped
                    ? Color.FromRgb(196, 48, 48)
                    : TrainColors[index % TrainColors.Length]),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(2),
                Child = new TextBlock
                {
                    Text = $"V{index + 1:00}",
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                ToolTip = $"{state.VehicleId}｜{state.ServiceRunId}\n{DirectionToChinese(state.Direction)} {state.TrackId}\n"
                    + $"車頭 {state.FrontPositionMeters / 1000:0.00} km｜車尾 {state.RearPositionMeters / 1000:0.00} km\n"
                    + $"{PhaseToChinese(state.Phase)}｜{state.SpeedMetersPerSecond * 3.6:0.#} km/h"
            };
            Canvas.SetLeft(train, Math.Clamp(x - 21.5, 0, width - 43));
            Canvas.SetTop(train, y - 11.5);
            RouteCanvas.Children.Add(train);
        }

        void DrawTrackLine(double y, string label)
        {
            RouteCanvas.Children.Add(new Line
            {
                X1 = left,
                X2 = left + trackWidth,
                Y1 = y,
                Y2 = y,
                Stroke = new SolidColorBrush(Color.FromRgb(70, 83, 105)),
                StrokeThickness = 4
            });
            AddCanvasText(RouteCanvas, label, left, y - 29, 11, Color.FromRgb(92, 103, 123));
        }
    }

    private void DrawV2SpeedProfile()
    {
        SpeedCanvas.Children.Clear();
        var width = SpeedCanvas.ActualWidth;
        var height = SpeedCanvas.ActualHeight;
        if (width < 100 || height < 100 || _v2World is null || _parameters is null)
        {
            return;
        }

        var samples = _v2World.Trajectory
            .Where(sample => sample.VehicleId == "Vehicle 01" && sample.Direction == TrainDirection.Outbound)
            .ToArray();
        if (samples.Length < 2)
        {
            AddCanvasText(SpeedCanvas, "播放後顯示 V2 速度、相位與即時速限。", 16, 18, 12, Color.FromRgb(102, 112, 133));
            return;
        }

        var left = 42d;
        var top = 17d;
        var plotWidth = width - left - 15;
        var plotHeight = height - top - 31;
        var maxTime = Math.Max(1, samples[^1].SimulationTimeSeconds);
        var maxSpeed = _parameters.MaxSpeedMetersPerSecond * 3.6 * 1.1;
        DrawAxes(SpeedCanvas, left, top, plotWidth, plotHeight, "km/h", "模擬時間");

        var speedLine = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(232, 109, 45)), StrokeThickness = 2.4 };
        var limitLine = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(205, 126, 24)), StrokeThickness = 1.4, StrokeDashArray = [4, 3] };
        foreach (var sample in TrajectoryAnalysis.DecimatePreservingCriticalPoints(samples, 450))
        {
            var x = left + sample.SimulationTimeSeconds / maxTime * plotWidth;
            var y = top + plotHeight - sample.SpeedMetersPerSecond * 3.6 / maxSpeed * plotHeight;
            speedLine.Points.Add(new Point(x, y));
            var limit = _v2World.SpeedLimits.GetCurrentLimitMetersPerSecond(
                sample.PositionMeters,
                sample.Direction,
                _parameters.MaxSpeedMetersPerSecond) * 3.6;
            limitLine.Points.Add(new Point(x, top + plotHeight - limit / maxSpeed * plotHeight));
        }

        SpeedCanvas.Children.Add(limitLine);
        SpeedCanvas.Children.Add(speedLine);
        AddCanvasText(SpeedCanvas, "— 實際速度　- - 里程速限", left + 8, top + 3, 10, Color.FromRgb(85, 94, 112));
    }

    private void DrawSafetyDistanceChart()
    {
        SafetyDistanceCanvas.Children.Clear();
        var width = SafetyDistanceCanvas.ActualWidth;
        var height = SafetyDistanceCanvas.ActualHeight;
        if (width < 120 || height < 100 || _v2World is null || _v2World.SafetyHistory.Count == 0)
        {
            if (width >= 120 && height >= 100)
            {
                AddCanvasText(SafetyDistanceCanvas, "播放多列車 V2 模擬後顯示實際淨距、安全距離與障礙物煞車需求。", 18, 18, 12, Color.FromRgb(102, 112, 133));
            }

            return;
        }

        var selected = SafetyPairComboBox.SelectedItem?.ToString();
        var windowTag = GetSelectedTag(SafetyWindowComboBox);
        var windowSeconds = double.TryParse(windowTag, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedWindow)
            ? parsedWindow
            : double.PositiveInfinity;
        var earliestTime = double.IsFinite(windowSeconds)
            ? Math.Max(0, _v2World.CurrentTimeSeconds - windowSeconds)
            : 0;
        var history = _v2World.SafetyHistory
            .Where(item => (selected is null || PairKey(item) == selected)
                && item.SimulationTimeSeconds >= earliestTime
                && MatchesSafetyFilters(item))
            .ToArray();
        if (history.Length == 0)
        {
            return;
        }

        var left = 52d;
        var top = 20d;
        var plotWidth = width - left - 18;
        var plotHeight = height - top - 34;
        var minTime = history[0].SimulationTimeSeconds;
        var maxTime = Math.Max(minTime + 1, history[^1].SimulationTimeSeconds);
        var maxDistance = Math.Max(50, history.Max(item => Math.Max(
            Math.Max(item.ActualGapMeters, item.DynamicSafetyDistanceMeters),
            item.ObstacleBrakingDemandMeters)) * 1.12);
        DrawAxes(SafetyDistanceCanvas, left, top, plotWidth, plotHeight, "m", "時間");
        var gapLine = CreateChartLine(Color.FromRgb(34, 126, 173), 2.4);
        var safetyLine = CreateChartLine(Color.FromRgb(232, 138, 35), 2.1, [5, 3]);
        var obstacleLine = CreateChartLine(Color.FromRgb(196, 48, 48), 2.1, [2, 3]);
        foreach (var item in history)
        {
            var x = left + (item.SimulationTimeSeconds - minTime) / (maxTime - minTime) * plotWidth;
            gapLine.Points.Add(new Point(x, ToY(item.ActualGapMeters)));
            safetyLine.Points.Add(new Point(x, ToY(item.DynamicSafetyDistanceMeters)));
            obstacleLine.Points.Add(new Point(x, ToY(item.ObstacleBrakingDemandMeters)));
        }

        SafetyDistanceCanvas.Children.Add(gapLine);
        SafetyDistanceCanvas.Children.Add(safetyLine);
        SafetyDistanceCanvas.Children.Add(obstacleLine);
        AddCanvasText(SafetyDistanceCanvas, "— 實際淨距　- - 動態安全距離　··· 障礙物煞車需求", left + 7, top + 2, 10, Color.FromRgb(72, 82, 101));
        var minimum = history.MinBy(item => item.SafetyMarginMeters)!;
        AddCanvasText(
            SafetyDistanceCanvas,
            $"最低裕度 {minimum.SafetyMarginMeters:0.0} m @ {minimum.SimulationTimeSeconds:0.0} s",
            left + 7,
            top + 18,
            10,
            SafetyStatusColor(minimum.Status));

        double ToY(double value) => top + plotHeight - Math.Clamp(value / maxDistance, 0, 1) * plotHeight;
    }

    private void DrawTimeDistanceDiagram()
    {
        if (TimeDistanceCanvas is null || DiagramScrollViewer is null)
        {
            return;
        }

        var zoom = DiagramZoomSlider?.Value ?? 1;
        var targetWidth = Math.Max(760, Math.Max(1, DiagramScrollViewer.ViewportWidth - 15) * zoom);
        if (Math.Abs(TimeDistanceCanvas.Width - targetWidth) > 1)
        {
            TimeDistanceCanvas.Width = targetWidth;
        }

        TimeDistanceCanvas.Children.Clear();
        var width = Math.Max(760, TimeDistanceCanvas.ActualWidth);
        var height = Math.Max(380, TimeDistanceCanvas.ActualHeight);
        if (_route is null || _v2World is null || _plannedWorld is null)
        {
            AddCanvasText(TimeDistanceCanvas, "建立並播放 V2 模擬後顯示時間－里程運行圖。", 22, 22, 13, Color.FromRgb(102, 112, 133));
            return;
        }

        var actual = ShowActualCheckBox?.IsChecked == true ? _v2World.Trajectory : [];
        var planned = ShowPlannedCheckBox?.IsChecked == true ? _plannedWorld.Trajectory : [];
        if (actual.Count == 0 && planned.Count == 0)
        {
            AddCanvasText(TimeDistanceCanvas, "播放後即時建立運行圖；空圖不會啟動零列車 Engine。", 22, 22, 13, Color.FromRgb(102, 112, 133));
            return;
        }

        var left = 82d;
        var right = 22d;
        var top = 48d;
        var bottom = 42d;
        var plotWidth = width - left - right;
        var plotHeight = height - top - bottom;
        var availableMaxTime = Math.Max(1, Math.Max(
            actual.Count == 0 ? 0 : actual.Max(item => item.SimulationTimeSeconds),
            planned.Count == 0 ? 0 : planned.Max(item => item.SimulationTimeSeconds)));
        var startTime = ParseDiagramMinute(DiagramStartMinuteTextBox, 0) * 60;
        var endTime = string.IsNullOrWhiteSpace(DiagramEndMinuteTextBox.Text)
            ? availableMaxTime
            : ParseDiagramMinute(DiagramEndMinuteTextBox, availableMaxTime / 60) * 60;
        startTime = Math.Clamp(startTime, 0, availableMaxTime);
        endTime = Math.Clamp(endTime, startTime + 0.1, Math.Max(startTime + 0.1, availableMaxTime));
        var visibleDuration = Math.Max(0.1, endTime - startTime);
        DrawAxes(TimeDistanceCanvas, left, top, plotWidth, plotHeight, "累積里程", "時間");
        AddCanvasText(
            TimeDistanceCanvas,
            $"{_route.RouteName}｜計畫／理論與 V2 模擬實際運行圖｜{_v2World.MovingBlockMode}｜速限 {_v2World.SpeedLimits.Limits.Count} 段｜固定 Tick 0.1 s",
            left,
            8,
            14,
            Color.FromRgb(34, 43, 60));

        foreach (var station in _route.Stations)
        {
            var y = top + plotHeight - station.PositionMeters / _route.TotalLengthMeters * plotHeight;
            TimeDistanceCanvas.Children.Add(new Line
            {
                X1 = left,
                X2 = left + plotWidth,
                Y1 = y,
                Y2 = y,
                Stroke = new SolidColorBrush(Color.FromRgb(222, 227, 235)),
                StrokeThickness = 1
            });
            AddCanvasText(TimeDistanceCanvas, $"{station.StationId}  {station.PositionMeters / 1000:0.00} km", 3, y - 8, 10, Color.FromRgb(82, 93, 111));
        }

        for (var tick = 0; tick <= 6; tick++)
        {
            var time = startTime + visibleDuration * tick / 6;
            var x = left + plotWidth * tick / 6;
            TimeDistanceCanvas.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = top,
                Y2 = top + plotHeight,
                Stroke = new SolidColorBrush(Color.FromRgb(232, 235, 241)),
                StrokeThickness = 1
            });
            AddCanvasText(TimeDistanceCanvas, TrajectoryAnalysis.FormatClock(_startClockSeconds + time), x - 34, top + plotHeight + 8, 9, Color.FromRgb(82, 93, 111));
        }

        DrawSeries(planned, isPlanned: true);
        DrawSeries(actual, isPlanned: false);

        foreach (var simulationEvent in _v2World.Events.Where(item =>
                     item.SimulationTimeSeconds >= startTime
                     && item.SimulationTimeSeconds <= endTime
                     && item.EventType is
                         SimulationEventType.ObstacleEmergencyStop
                         or SimulationEventType.PredictedCollision
                         or SimulationEventType.Collision
                         or SimulationEventType.SafetyStatusChanged))
        {
            var x = left + (simulationEvent.SimulationTimeSeconds - startTime) / visibleDuration * plotWidth;
            var y = top + plotHeight - simulationEvent.PositionMeters / _route.TotalLengthMeters * plotHeight;
            var marker = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = new SolidColorBrush(Color.FromRgb(196, 48, 48)),
                Stroke = Brushes.White,
                StrokeThickness = 1,
                ToolTip = $"{TrajectoryAnalysis.FormatClock(_startClockSeconds + simulationEvent.SimulationTimeSeconds)}\n{simulationEvent.Message}"
            };
            Canvas.SetLeft(marker, x - 4);
            Canvas.SetTop(marker, y - 4);
            TimeDistanceCanvas.Children.Add(marker);
        }

        AddCanvasText(TimeDistanceCanvas, "實線：V2 模擬實際　虛線：無干擾計畫／理論　紅點：安全或障礙事件", left, 28, 10, Color.FromRgb(82, 93, 111));

        void DrawSeries(IReadOnlyList<TrajectorySample> source, bool isPlanned)
        {
            var directionFilter = GetSelectedTag(DiagramDirectionComboBox);
            var vehicleFilter = DiagramVehicleComboBox.SelectedItem?.ToString();
            var filtered = source.Where(sample =>
                sample.SimulationTimeSeconds >= startTime
                && sample.SimulationTimeSeconds <= endTime
                &&
                (directionFilter == "All"
                    || directionFilter == "Outbound" && sample.Direction == TrainDirection.Outbound
                    || directionFilter == "Inbound" && sample.Direction == TrainDirection.Inbound)
                && (string.IsNullOrWhiteSpace(vehicleFilter)
                    || vehicleFilter == "全部"
                    || sample.VehicleId == vehicleFilter)).ToArray();
            foreach (var group in filtered.GroupBy(sample => (sample.VehicleId, sample.ServiceRunId, sample.Direction)))
            {
                var selectedVehicle = vehicleFilter is not null and not "全部";
                var index = ParseVehicleIndex(group.Key.VehicleId);
                var line = new Polyline
                {
                    Stroke = new SolidColorBrush(TrainColors[index % TrainColors.Length]),
                    StrokeThickness = selectedVehicle ? 3.1 : isPlanned ? 1.4 : 2.2,
                    StrokeDashArray = isPlanned ? [6, 4] : null,
                    Opacity = selectedVehicle || vehicleFilter is null or "全部" ? (isPlanned ? 0.55 : 0.95) : 0.22,
                    ToolTip = $"{group.Key.VehicleId}｜{group.Key.ServiceRunId}｜{DirectionToChinese(group.Key.Direction)}"
                };
                var points = TrajectoryAnalysis.DecimatePreservingCriticalPoints(group.ToArray(), 600);
                foreach (var sample in points)
                {
                    line.Points.Add(new Point(
                        left + (sample.SimulationTimeSeconds - startTime) / visibleDuration * plotWidth,
                        top + plotHeight - sample.PositionMeters / _route.TotalLengthMeters * plotHeight));
                }

                TimeDistanceCanvas.Children.Add(line);
                if (points.Count > 0)
                {
                    var first = points[0];
                    AddCanvasText(
                        TimeDistanceCanvas,
                        ShortVehicle(first.VehicleId),
                        left + (first.SimulationTimeSeconds - startTime) / visibleDuration * plotWidth + 3,
                        top + plotHeight - first.PositionMeters / _route.TotalLengthMeters * plotHeight - 15,
                        9,
                        TrainColors[index % TrainColors.Length]);
                }
            }
        }
    }

    private void UpdateSafetySummary()
    {
        if (_v2World is null || _v2World.SafetyHistory.Count == 0)
        {
            SafetySummaryText.Text = "目前沒有相鄰列車配對。";
            return;
        }

        var minimum = _v2World.SafetyHistory.MinBy(item => item.SafetyMarginMeters)!;
        SafetySummaryText.Text = $"全程最低安全裕度 {minimum.SafetyMarginMeters:0.0} m，"
            + $"{ShortVehicle(minimum.FollowerVehicleId)} → {ShortVehicle(minimum.LeaderVehicleId)}，"
            + $"發生於 {minimum.SimulationTimeSeconds:0.0} s；目前估算採用"
            + $"{(_v2World.BrakingEstimationMode == BrakingEstimationMode.Service ? "營運" : "緊急")}煞車。";
    }

    private void RefreshPairFilter(IEnumerable<SafetyObservation> observations)
    {
        var selected = SafetyPairComboBox.SelectedItem?.ToString();
        var keys = observations.Select(PairKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (keys.SequenceEqual(SafetyPairComboBox.Items.Cast<string>(), StringComparer.Ordinal))
        {
            return;
        }

        SafetyPairComboBox.Items.Clear();
        foreach (var key in keys)
        {
            SafetyPairComboBox.Items.Add(key);
        }

        SafetyPairComboBox.SelectedItem = selected is not null && keys.Contains(selected, StringComparer.Ordinal)
            ? selected
            : keys.FirstOrDefault();
    }

    private void PopulateFilterControls(int trainCount)
    {
        DiagramVehicleComboBox.Items.Clear();
        ObstacleTrainComboBox.Items.Clear();
        DiagramVehicleComboBox.Items.Add("全部");
        for (var index = 0; index < trainCount; index++)
        {
            var vehicleId = $"Vehicle {index + 1:00}";
            DiagramVehicleComboBox.Items.Add(vehicleId);
            ObstacleTrainComboBox.Items.Add(vehicleId);
        }

        DiagramVehicleComboBox.SelectedIndex = 0;
        ObstacleTrainComboBox.SelectedIndex = 0;
        ObstacleDelayTextBox.Text = "0";
    }

    private bool EnsureDiagramAvailable()
    {
        if (_v2World is not null && _v2World.Trajectory.Count > 0)
        {
            DrawTimeDistanceDiagram();
            return true;
        }

        ShowValidation(["請先播放 V2 模擬，產生軌跡後再匯出運行圖。"]);
        return false;
    }

    private MovingBlockMode ParseMovingBlockMode()
    {
        var tag = GetSelectedTag(MovingBlockModeComboBox);
        return tag switch
        {
            "Independent" => MovingBlockMode.Independent,
            "Control" => MovingBlockMode.Control,
            _ => MovingBlockMode.Monitoring
        };
    }

    private bool MatchesSafetyFilters(SafetyObservation observation)
    {
        var direction = GetSelectedTag(SafetyDirectionComboBox);
        if (direction == "Outbound" && observation.Direction != TrainDirection.Outbound
            || direction == "Inbound" && observation.Direction != TrainDirection.Inbound)
        {
            return false;
        }

        var status = GetSelectedTag(SafetyStatusComboBox);
        return status == "All"
            || string.Equals(status, observation.Status.ToString(), StringComparison.Ordinal);
    }

    private static SpeedLimitDirection ParseSpeedLimitDirection(string value, int rowNumber) => value switch
    {
        "雙向" => SpeedLimitDirection.Both,
        "下行" => SpeedLimitDirection.Outbound,
        "上行" => SpeedLimitDirection.Inbound,
        _ => throw new InvalidOperationException($"速限第 {rowNumber} 列方向無效，請選擇雙向、下行或上行。")
    };

    private static string GetSelectedTag(ComboBox comboBox) =>
        comboBox.SelectedItem is ComboBoxItem item ? item.Tag?.ToString() ?? string.Empty : string.Empty;

    private static double ParseDiagramMinute(TextBox textBox, double fallback)
    {
        return TryParseFlexible(textBox.Text, out var value) && double.IsFinite(value) && value >= 0
            ? value
            : fallback;
    }

    private static string PairKey(SafetyObservation item) =>
        $"{ShortVehicle(item.FollowerVehicleId)} → {ShortVehicle(item.LeaderVehicleId)}｜{item.TrackId}";

    private static string ShortVehicle(string vehicleId) => vehicleId.Replace("Vehicle ", "V", StringComparison.Ordinal);

    private static int ParseVehicleIndex(string vehicleId) =>
        int.TryParse(vehicleId.AsSpan(vehicleId.LastIndexOf(' ') + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Max(0, value - 1)
            : 0;

    private static string DirectionToChinese(TrainDirection direction) =>
        direction == TrainDirection.Outbound ? "下行" : "上行";

    private static string SpeedLimitDirectionToChinese(SpeedLimitDirection direction) => direction switch
    {
        SpeedLimitDirection.Outbound => "下行",
        SpeedLimitDirection.Inbound => "上行",
        _ => "雙向"
    };

    private static string PhaseToChinese(OperationalPhase phase) => phase switch
    {
        OperationalPhase.Pending => "待發",
        OperationalPhase.Dwelling => "停站",
        OperationalPhase.Accelerating => "加速",
        OperationalPhase.Cruising => "巡航",
        OperationalPhase.Coasting => "惰行",
        OperationalPhase.Braking => "煞車",
        OperationalPhase.ApproachBraking => "進站平順煞車",
        OperationalPhase.Arriving => "到站",
        OperationalPhase.Turning => "折返",
        OperationalPhase.EmergencyStopped => "障礙急停",
        OperationalPhase.Collided => "碰撞停止",
        _ => "退出營運"
    };

    private static string SafetyStatusToChinese(SafetyStatus status) => status switch
    {
        SafetyStatus.Safe => "安全",
        SafetyStatus.Caution => "接近警戒",
        SafetyStatus.BrakingRequired => "需要制動",
        _ => "侵入安全距離"
    };

    private static Color SafetyStatusColor(SafetyStatus status) => status switch
    {
        SafetyStatus.Safe => Color.FromRgb(22, 134, 107),
        SafetyStatus.Caution => Color.FromRgb(218, 166, 35),
        SafetyStatus.BrakingRequired => Color.FromRgb(232, 109, 45),
        _ => Color.FromRgb(196, 48, 48)
    };

    private static string EventTypeToChinese(SimulationEventType type) => type switch
    {
        SimulationEventType.Departure => "發車",
        SimulationEventType.Arrival => "抵達",
        SimulationEventType.DwellStarted => "停站",
        SimulationEventType.TurnaroundStarted => "折返開始",
        SimulationEventType.DirectionChanged => "折返完成",
        SimulationEventType.SafetyStatusChanged => "安全狀態",
        SimulationEventType.ControlBraking => "控制制動",
        SimulationEventType.ObstacleEmergencyStop => "障礙物急停",
        SimulationEventType.PredictedCollision => "預測碰撞",
        SimulationEventType.Collision => "實際碰撞",
        _ => "煞車模式切換"
    };

    private static Polyline CreateChartLine(Color color, double thickness, DoubleCollection? dash = null) => new()
    {
        Stroke = new SolidColorBrush(color),
        StrokeThickness = thickness,
        StrokeDashArray = dash
    };

    private static void DrawAxes(
        Canvas canvas,
        double left,
        double top,
        double width,
        double height,
        string verticalLabel,
        string horizontalLabel)
    {
        canvas.Children.Add(new Line
        {
            X1 = left,
            X2 = left,
            Y1 = top,
            Y2 = top + height,
            Stroke = Brushes.SlateGray,
            StrokeThickness = 1.1
        });
        canvas.Children.Add(new Line
        {
            X1 = left,
            X2 = left + width,
            Y1 = top + height,
            Y2 = top + height,
            Stroke = Brushes.SlateGray,
            StrokeThickness = 1.1
        });
        AddCanvasText(canvas, verticalLabel, 3, 2, 10, Color.FromRgb(102, 112, 133));
        AddCanvasText(canvas, horizontalLabel, left + width - 48, top + height + 12, 10, Color.FromRgb(102, 112, 133));
    }
}
