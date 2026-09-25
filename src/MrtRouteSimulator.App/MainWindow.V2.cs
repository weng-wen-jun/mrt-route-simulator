using System.Collections.ObjectModel;
using System.Diagnostics;
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
    private SimulationSession? _v2Session;
    private SimulationWorld? _v2World => _v2Session?.ActualWorld;
    private SimulationWorld? _plannedWorld => _v2Session?.PlannedWorld;
    private ResolvedDispatchPlan? _v2DispatchPlan;
    private SimulationProjectDocument? _activeSimulationProjectDocument;
    private IReadOnlyList<SimulationEvent> _plannedTimetableEvents = [];
    private bool _v2Enabled;
    private bool _updatingSpeedProfileSelection;
    private double? _v2PlannedMinimumIntervalSeconds;
    private long _lastExpensivePlaybackRefreshTimestamp;

    private static readonly long ExpensivePlaybackRefreshIntervalTicks =
        (long)(Stopwatch.Frequency * 0.25);

    public ObservableCollection<ServicePatternInputRow> ServicePatternRows { get; } = [];

    public ObservableCollection<SafetyRow> SafetyRows { get; } = [];

    public ObservableCollection<EventRow> EventRows { get; } = [];

    private void LoadSampleV2Data()
    {
        ServicePatternRows.Clear();
        LoadSampleInputCatalogs();
        CoastingRatioTextBox.Text = "0.15";
        ApproachDistanceTextBox.Text = "65";
        ApproachSpeedTextBox.Text = "0";
        ReactionTimeTextBox.Text = "1.5";
        OperationModeComboBox.SelectedIndex = 1;
        MovingBlockModeComboBox.SelectedIndex = 2;
        BrakingModeComboBox.SelectedIndex = 0;
    }

    private void ConfigureV2World()
    {
        _v2Enabled = EngineModeComboBox.SelectedItem is ComboBoxItem item
            && string.Equals(item.Tag?.ToString(), "V2RealisticOperations", StringComparison.Ordinal);
        if (!_v2Enabled)
        {
            _v2Session = null;
            _v2DispatchPlan = null;
            _activeSimulationProjectDocument = null;
            _plannedTimetableEvents = [];
            _v2PlannedMinimumIntervalSeconds = null;
            ObstacleStopButton.IsEnabled = false;
            return;
        }

        // 主畫面的線性欄位只作為一次性的 quick builder；V2 runtime 一律建立 Schema 8
        // physical graph。建立後，唯一可編輯資料來源是 Schema 8 專案工作區。
        ConfigureTopologyProjectForPlayback(
            TopologyProjectFactory.CreateLinearDraft(CaptureProjectDocument()),
            lockLegacyInputs: true);
    }

    private void PopulateV2Results()
    {
        if (!_v2Enabled || _v2World is null)
        {
            return;
        }

        HeadwaySummaryText.Text = _v2PlannedMinimumIntervalSeconds is { } interval
            ? $"{FormatDuration(interval)}（計畫最短）"
            : "單一／同時發車";
        PlaybackStatusText.Text = "V2 已就緒；播放時每個 0.1 秒控制與碰撞子步進都會依序執行。";
        DrawV2Route();
        DrawV2SpeedProfile();
        DrawSafetyDistanceChart();
        DrawTimeDistanceDiagram();
        PopulateIntervalStatistics();
        PopulateV3Timetable();
        PopulateV3SegmentDetails();
        PopulateV1V2Comparison();
        PopulateResourceOccupancy();
        UpdateV2ActualSummary();
    }

    private void UpdateV2PlaybackView()
    {
        if (!_v2Enabled || _v2Session is null)
        {
            return;
        }

        var session = _v2Session;
        var snapshot = session.AdvanceActualTo(_playbackTimeSeconds);
        CurrentTrainRows.Clear();
        foreach (var state in snapshot.Trains.Where(state => state.Phase != OperationalPhase.OutOfService))
        {
            CurrentTrainRows.Add(new CurrentTrainRow(
                state.VehicleId.Replace("Vehicle ", "V", StringComparison.Ordinal) + $"｜{state.ServiceClassId}",
                state.IsActive ? DirectionToChinese(state.Direction) : "—",
                state.IsActive ? PhaseToChinese(state.Phase) : "待發",
                session.ActualWorld.GetTrainCenterPosition(state.VehicleId) is { } center
                    && _stationChainageProjection?.ToChainage(center) is { } centerChainage
                        ? $"{centerChainage / 1000:0.000}" : "—",
                $"{state.SpeedMetersPerSecond * 3.6:0.#}",
                GetV2CurrentLocation(state),
                state.NextStationId ?? "—"));
        }

        var refreshExpensiveViews = !_playbackTimer.IsEnabled || ShouldRefreshExpensivePlaybackViews();
        if (refreshExpensiveViews)
        {
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
            foreach (var simulationEvent in session.ActualWorld.Events.TakeLast(300).Reverse())
            {
                EventRows.Add(new EventRow(
                    TrajectoryAnalysis.FormatClock(_startClockSeconds + simulationEvent.SimulationTimeSeconds),
                    EventTypeToChinese(simulationEvent.EventType),
                    string.IsNullOrWhiteSpace(simulationEvent.VehicleId) ? "—" : ShortVehicle(simulationEvent.VehicleId),
                    $"{simulationEvent.PositionMeters / 1000:0.00}",
                    simulationEvent.Message));
            }

            RefreshPairFilter(session.ActualWorld.SafetyHistory.Where(MatchesSafetyFilters));
            UpdateSafetySummary();
        }

        SimulationClockText.Text = TrajectoryAnalysis.FormatClock(_startClockSeconds + _playbackTimeSeconds);
        DrawV2Route(snapshot);
        if (refreshExpensiveViews)
        {
            DrawV2SpeedProfile();
            DrawSafetyDistanceChart();
            DrawTimeDistanceDiagram();
            PopulateIntervalStatistics(throttled: true);
            PopulateV3Timetable();
            PopulateV3SegmentDetails();
            PopulateV1V2Comparison();
            PopulateResourceOccupancy();
            UpdateV2ActualSummary();
        }
        session.ActualWorld.AcknowledgeSnapshotEvents();
    }

    private bool ShouldRefreshExpensivePlaybackViews()
    {
        var now = Stopwatch.GetTimestamp();
        if (_lastExpensivePlaybackRefreshTimestamp != 0
            && now - _lastExpensivePlaybackRefreshTimestamp < ExpensivePlaybackRefreshIntervalTicks)
        {
            return false;
        }

        _lastExpensivePlaybackRefreshTimestamp = now;
        return true;
    }

    private void ResetV2Playback()
    {
        _v2Session?.Reset();
        SafetyRows.Clear();
        EventRows.Clear();
        IntervalStatisticRows.Clear();
        JourneyStatisticRows.Clear();
        V1V2ComparisonRows.Clear();
        ResourceOccupancyRows.Clear();
        _lastIntervalRefreshSecond = -1;
        _lastExpensivePlaybackRefreshTimestamp = 0;
        SafetyPairComboBox.Items.Clear();
        SafetySummaryText.Text = "建立 V2 模擬後顯示安全摘要。";
        DrawSafetyDistanceChart();
        DrawTimeDistanceDiagram();
    }

    private void ClearV2Results()
    {
        // 結束 topology 執行狀態後，使用者必須能重新編輯表單或切換引擎。
        SetQuickBuilderState(locked: false, collapsed: false);
        _v2Session = null;
        _v2DispatchPlan = null;
        _activeSimulationProjectDocument = null;
        _activeTopologyProjectDocument = null;
        _plannedTimetableEvents = [];
        _v2Enabled = false;
        _v2PlannedMinimumIntervalSeconds = null;
        SafetyRows.Clear();
        EventRows.Clear();
        IntervalStatisticRows.Clear();
        JourneyStatisticRows.Clear();
        _lastIntervalRefreshSecond = -1;
        _lastExpensivePlaybackRefreshTimestamp = 0;
        SafetyPairComboBox.Items.Clear();
        DiagramVehicleComboBox.Items.Clear();
        ObstacleTrainComboBox.Items.Clear();
        SpeedProfileRunComboBox.Items.Clear();

        ObstacleStopButton.IsEnabled = false;
        SafetySummaryText.Text = "建立 V2 模擬後顯示安全摘要。";
        IntervalSummaryText.Text = "目前不是 V2 模擬。";
        DrawSafetyDistanceChart();
        DrawTimeDistanceDiagram();
    }

    private ServicePattern[] BuildServicePatterns() => BuildStopPatternDefinitions()
        .Select(pattern => new ServicePattern(
            pattern.Id,
            pattern.DisplayName,
            pattern.Instructions.Select(item => new StationServiceInstruction(
                item.StationId,
                item.Action switch
                {
                    StopPatternAction.Stop => StationServiceMode.Stop,
                    StopPatternAction.Pass => StationServiceMode.Pass,
                    StopPatternAction.Turnback => StationServiceMode.Turnback,
                    _ => throw new SimulationValidationException(["停站模式動作無效。"])
                },
                item.PassingSpeedLimitMetersPerSecond,
                item.DwellTimeSeconds)).ToArray()))
        .ToArray();

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
        if (string.IsNullOrWhiteSpace(selectedVehicle))
        {
            ShowValidation(["請先選擇要觸發障礙物急停的實際車輛 ID。　"]);
            return;
        }

        var target = snapshot.Trains.FirstOrDefault(train =>
            train.VehicleId.Equals(selectedVehicle, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            ShowValidation([$"找不到車輛「{selectedVehicle}」；請重新建立模擬以更新車輛清單。　"]);
            return;
        }

        if (target.Phase == OperationalPhase.OutOfService)
        {
            ShowValidation([$"車輛「{selectedVehicle}」已退出營運，無法再觸發障礙物急停。　"]);
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
            FileName = $"{GetActiveRouteDisplayName()}_列車運行圖.png"
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
            FileName = $"{GetActiveRouteDisplayName()}_列車運行圖.pdf"
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
        if (_v2World is null || _v2World.Trajectory.Count == 0)
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
            FileName = $"{GetActiveRouteDisplayName()}_軌跡事件.csv"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var csv = TrajectoryAnalysis.BuildCsv(_v2World.Trajectory, _v2World.Events, _startClockSeconds);
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
        var width = PrepareRouteCanvasWidth();
        var height = RouteCanvas.ActualHeight;
        if (width < 100 || height < 100)
        {
            return;
        }

        if (_v2World is { } topologyWorld)
        {
            DrawTopologyGraphRoute(
                topologyWorld.TopologyInfrastructure,
                _activeTopologyProjectDocument,
                snapshot ?? topologyWorld.GetSnapshot(),
                width,
                height,
                topologyWorld);
            return;
        }

        if (_route is null)
        {
            return;
        }

        var tailTrackLayouts = GetAfterStationTailTrackVisualLayouts();
        var hasNorthTailTrack = tailTrackLayouts.Any(item => item.StationIndex == 0);
        var hasSouthTailTrack = tailTrackLayouts.Any(item => item.StationIndex == _route.Stations.Count - 1);
        var left = hasNorthTailTrack ? 132d : 60d;
        var right = hasSouthTailTrack ? 112d : 38d;
        var trackWidth = Math.Max(1, width - left - right);
        var outboundY = height * 0.39;
        var inboundY = height * 0.63;
        DrawTrackLine(outboundY, "下行 DOWN →");
        DrawTrackLine(inboundY, "← 上行 UP");
        DrawSpatialReferencePointGeometry(left, trackWidth, outboundY, inboundY, width, height);
        DrawAfterStationTailTrackGeometry(tailTrackLayouts, left, trackWidth, outboundY, inboundY, width, height);

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
            (x, y) = GetSpatialReferencePointTrainPosition(state, x, y, left, trackWidth, outboundY, inboundY);
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
                    Text = VehicleMarkerLabel(state.VehicleId),
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                ToolTip = $"{state.VehicleId}｜{state.ServiceRunId}｜{state.ServiceClassId}｜{state.ServicePatternId}\n"
                    + $"{DirectionToChinese(state.Direction)} {state.TrackId}\n"
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

    /// <summary>
    /// V4 路線圖直接根據 node/edge/platform 繪製。主線依 Outbound ServiceRoute 的 traversal
    /// 次序排為長直線，其他支線、渡線及尾軌再由主線岔出；座標只屬於視覺 layout，
    /// 不改寫 Engine 的 edge-local offset。
    /// </summary>
    private void DrawTopologyGraphRoute(
        InfrastructureGraphV4 infrastructure,
        TopologyProjectDocument? topologyProject,
        SimulationSnapshot snapshot,
        double width,
        double height,
        SimulationWorld world)
    {
        var stationChainage = topologyProject is null ? null : StationChainageProjection.TryCreate(topologyProject);
        var nodes = infrastructure.Nodes.Values
            .OrderBy(node => node.NodeId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (nodes.Length == 0)
        {
            AddCanvasText(RouteCanvas, "此拓撲尚未定義軌道節點。", 24, 24, 14, Color.FromRgb(102, 112, 133));
            return;
        }

        ServiceRouteDefinition? outboundServiceRoute = null;
        ServiceRouteDefinition? inboundServiceRoute = null;
        if (topologyProject is not null)
        {
            foreach (var binding in topologyProject.DirectionRouteBindings)
            {
                var serviceRoute = topologyProject.ServiceRoutes.FirstOrDefault(route =>
                    route.ServiceRouteId.Equals(binding.ServiceRouteId, StringComparison.OrdinalIgnoreCase));
                if (serviceRoute is null)
                {
                    continue;
                }

                if (binding.Direction == TrainDirection.Outbound)
                {
                    outboundServiceRoute = serviceRoute;
                }
                else if (binding.Direction == TrainDirection.Inbound)
                {
                    inboundServiceRoute = serviceRoute;
                }
            }

            outboundServiceRoute ??= topologyProject.ServiceRoutes.FirstOrDefault();
        }

        var mainlineNodeDistances = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var mainlineLengthMeters = 0d;
        if (outboundServiceRoute is not null)
        {
            foreach (var traversal in outboundServiceRoute.Traversals)
            {
                if (!infrastructure.TryGetEdge(traversal.TrackEdgeId, out var edge))
                {
                    continue;
                }

                var (startNodeId, endNodeId) = GetTraversalEndpoints(edge, traversal.Direction);
                mainlineNodeDistances.TryAdd(startNodeId, mainlineLengthMeters);
                mainlineLengthMeters += edge.LengthMeters;
                mainlineNodeDistances.TryAdd(endNodeId, mainlineLengthMeters);
            }
        }

        var left = 108d;
        var right = 108d;
        var top = 30d;
        var bottom = 28d;
        var usableWidth = Math.Max(1, width - left - right);
        var mainlineY = height * 0.52;
        var trackSpacing = inboundServiceRoute is null ? 0 : Math.Clamp(height * 0.18, 30, 46);
        var outboundTrackY = mainlineY + trackSpacing / 2;
        var inboundTrackY = mainlineY - trackSpacing / 2;
        var nodePoints = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);

        if (mainlineNodeDistances.Count > 0 && mainlineLengthMeters > 0)
        {
            foreach (var (nodeId, distanceMeters) in mainlineNodeDistances)
            {
                nodePoints[nodeId] = new Point(
                    left + distanceMeters / mainlineLengthMeters * usableWidth,
                    mainlineY);
            }
        }
        else
        {
            // 不完整 topology 仍採水平保底排列，不能再把未排序的 nodeId 畫成圓形散點。
            for (var index = 0; index < nodes.Length; index++)
            {
                var ratio = nodes.Length == 1 ? 0.5 : (double)index / (nodes.Length - 1);
                nodePoints[nodes[index].NodeId] = new Point(left + ratio * usableWidth, mainlineY);
            }
        }

        var unplacedNodes = new HashSet<string>(
            nodes.Select(node => node.NodeId).Where(nodeId => !nodePoints.ContainsKey(nodeId)),
            StringComparer.OrdinalIgnoreCase);
        var branchIndex = 0;
        while (unplacedNodes.Count > 0)
        {
            var placedAny = false;
            foreach (var edge in infrastructure.Edges.Values
                         .OrderBy(item => item.TrackEdgeId, StringComparer.OrdinalIgnoreCase))
            {
                var fromPlaced = nodePoints.TryGetValue(edge.FromNodeId, out var from);
                var toPlaced = nodePoints.TryGetValue(edge.ToNodeId, out var to);
                if (fromPlaced == toPlaced)
                {
                    continue;
                }

                var known = fromPlaced ? from : to;
                var unknownNodeId = fromPlaced ? edge.ToNodeId : edge.FromNodeId;
                if (!unplacedNodes.Contains(unknownNodeId))
                {
                    continue;
                }

                var isTail = edge.Kind is TrackEdgeKind.TailTrack or TrackEdgeKind.Turnback;
                var isVerticalBranch = edge.Kind is TrackEdgeKind.PassingTrack
                    or TrackEdgeKind.PocketTrack
                    or TrackEdgeKind.Crossover
                    or TrackEdgeKind.Siding
                    or TrackEdgeKind.DepotLead
                    or TrackEdgeKind.Approach;
                var direction = known.X <= width * 0.5 ? -1d : 1d;
                var branchDirection = branchIndex++ % 2 == 0 ? -1d : 1d;
                var x = isTail
                    ? known.X + direction * Math.Min(96, usableWidth * 0.14)
                    : known.X + (isVerticalBranch ? 0 : direction * Math.Min(90, usableWidth * 0.12));
                var y = isTail
                    ? known.Y
                    : known.Y + branchDirection * Math.Min(72, Math.Max(42, height * 0.18));
                nodePoints[unknownNodeId] = new Point(
                    Math.Clamp(x, 22, width - 22),
                    Math.Clamp(y, top, height - bottom));
                unplacedNodes.Remove(unknownNodeId);
                placedAny = true;
            }

            if (placedAny)
            {
                continue;
            }

            // 與主線不連通的獨立子圖也以整齊的水平列呈現，並明確保留其孤立性。
            var remaining = unplacedNodes.OrderBy(nodeId => nodeId, StringComparer.OrdinalIgnoreCase).ToArray();
            for (var index = 0; index < remaining.Length; index++)
            {
                var ratio = (double)(index + 1) / (remaining.Length + 1);
                nodePoints[remaining[index]] = new Point(left + ratio * usableWidth, top + 24);
                unplacedNodes.Remove(remaining[index]);
            }
        }

        var outboundEdgeIds = outboundServiceRoute?.Traversals
            .Select(traversal => traversal.TrackEdgeId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inboundEdgeIds = inboundServiceRoute?.Traversals
            .Select(traversal => traversal.TrackEdgeId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var rawEdgePoints = new Dictionary<string, (Point From, Point To)>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in infrastructure.Edges.Values)
        {
            var from = nodePoints[edge.FromNodeId];
            var to = nodePoints[edge.ToNodeId];
            if (outboundEdgeIds.Contains(edge.TrackEdgeId))
            {
                from.Y = outboundTrackY;
                to.Y = outboundTrackY;
            }
            else if (inboundEdgeIds.Contains(edge.TrackEdgeId))
            {
                from.Y = inboundTrackY;
                to.Y = inboundTrackY;
            }
            rawEdgePoints[edge.TrackEdgeId] = (from, to);
        }

        // 尾軌沿「抵達股道」的主線方向直線延伸。若回程要切到另一股道，僅由
        // DirectedConnection 畫出真正的橫渡線，不能把尾軌置中後畫成 Y 字。
        if (topologyProject is not null)
        {
            foreach (var facility in topologyProject.Topology.TurnbackFacilities
                         .Where(item => item.Kind == TurnbackFacilityKind.TailTrack))
            {
                if (!infrastructure.TryGetEdge(facility.ArrivalTrackEdgeId, out var arrivalEdge)
                    || !rawEdgePoints.TryGetValue(arrivalEdge.TrackEdgeId, out var arrivalPoints))
                {
                    continue;
                }

                foreach (var traversal in facility.Traversals)
                {
                    if (!infrastructure.TryGetEdge(traversal.TrackEdgeId, out var tailEdge)
                        || tailEdge.Kind != TrackEdgeKind.TailTrack
                        || !rawEdgePoints.TryGetValue(tailEdge.TrackEdgeId, out var tailPoints))
                    {
                        continue;
                    }

                    var sharedNodeId = tailEdge.FromNodeId.Equals(arrivalEdge.FromNodeId, StringComparison.OrdinalIgnoreCase)
                        || tailEdge.ToNodeId.Equals(arrivalEdge.FromNodeId, StringComparison.OrdinalIgnoreCase)
                            ? arrivalEdge.FromNodeId
                            : arrivalEdge.ToNodeId;
                    var arrivalY = sharedNodeId.Equals(arrivalEdge.FromNodeId, StringComparison.OrdinalIgnoreCase)
                        ? arrivalPoints.From.Y
                        : arrivalPoints.To.Y;
                    rawEdgePoints[tailEdge.TrackEdgeId] = (
                        new Point(tailPoints.From.X, arrivalY),
                        new Point(tailPoints.To.X, arrivalY));
                }
            }
        }

        if (topologyProject is not null)
            StationSchematicPresentation.LayoutLinearTurnbacks(rawEdgePoints, infrastructure.Edges.Values,
                topologyProject.Topology.TurnbackFacilities, width);
        StationSchematicPresentation.ApplyNodeLayout(rawEdgePoints, infrastructure.Nodes.Values,
            infrastructure.Edges.Values, mainlineY, Math.Max(23, trackSpacing / 2), 60, width - 120);
        var edgeGeometries = TopologySchematicGeometry.Build(
            infrastructure.Edges.Values.Select(edge =>
            {
                var points = rawEdgePoints[edge.TrackEdgeId];
                return (edge.TrackEdgeId, points.From, points.To);
            }));

        edgeGeometries = StationSchematicPresentation.ApplyLanes(edgeGeometries,
            infrastructure.Edges.Values, mainlineY, Math.Max(23, trackSpacing / 2), infrastructure.Platforms.Values,
            infrastructure.DirectedConnections,
            StationSchematicPresentation.UsesCompactLaneTransitions(topologyProject, width),
            topologyProject?.Topology.PassingFacilities, width >= 2000);
        edgeGeometries = StationSchematicPresentation.ApplyChainage(edgeGeometries, topologyProject, width);
        var railColor = Color.FromRgb(25, 96, 125);
        StationSchematicPresentation.DrawLegend(RouteCanvas);
        var platformVisuals = new List<(
            PlatformDefinitionV4 Platform,
            TrackEdgeDefinition Edge,
            TopologySchematicEdgeGeometry Geometry,
            Point Start,
            Point End,
            Point Stop)>();
        foreach (var platform in infrastructure.Platforms.Values)
        {
            if (!edgeGeometries.TryGetValue(platform.TrackEdgeId, out var geometry)
                || !infrastructure.TryGetEdge(platform.TrackEdgeId, out var edge))
            {
                continue;
            }

            var startRatio = edge.LengthMeters <= 0 ? 0 : platform.PlatformStartOffsetMeters / edge.LengthMeters;
            var endRatio = edge.LengthMeters <= 0 ? 0 : platform.PlatformEndOffsetMeters / edge.LengthMeters;
            var stopRatio = edge.LengthMeters <= 0 ? 0 : platform.StopPositionOffsetMeters / edge.LengthMeters;
            platformVisuals.Add((
                platform,
                edge,
                geometry,
                geometry.PointAt(startRatio),
                geometry.PointAt(endRatio),
                geometry.PointAt(stopRatio)));
        }

        var stationCenters = StationSchematicPresentation.DrawPlatforms(RouteCanvas, infrastructure.Platforms.Values,
            infrastructure.Edges.Values, edgeGeometries, mainlineY, topologyProject?.Topology.PassingFacilities);
        var stationVisuals = infrastructure.Stations.Values
            .Select(station =>
            {
                var platforms = platformVisuals
                    .Where(item => item.Platform.StationId.Equals(station.StationId, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(item => item.Platform.PlatformId, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var anchorX = stationCenters.GetValueOrDefault(station.StationId, double.NaN);
                var minY = platforms.Length == 0 ? outboundTrackY : platforms.Min(item => item.Geometry.Points.Min(p => p.Y));
                var maxY = platforms.Length == 0 ? inboundTrackY : platforms.Max(item => item.Geometry.Points.Max(p => p.Y));
                return (Station: station, Platforms: platforms, AnchorX: anchorX, MinY: minY, MaxY: maxY);
            })
            .Where(item => double.IsFinite(item.AnchorX))
            .OrderBy(item => item.AnchorX)
            .ToArray();


        // 主線分成上下行兩條水平軌後，facility edge 仍以 topology node 為端點。
        // 把 directed connection 畫成實線接軌，避免尾軌／袋狀軌看起來懸空；這些
        // connector 只表示已允許的轉向，不會把幾何相交誤當成 graph adjacency。
        foreach (var connection in infrastructure.DirectedConnections)
        {
            if (!edgeGeometries.TryGetValue(connection.FromTrackEdgeId, out var fromGeometry)
                || !edgeGeometries.TryGetValue(connection.ToTrackEdgeId, out var toGeometry))
            {
                continue;
            }

            var from = connection.FromDirection == TraversalDirection.Forward
                ? fromGeometry.To
                : fromGeometry.From;
            var to = connection.ToDirection == TraversalDirection.Forward
                ? toGeometry.From
                : toGeometry.To;


            if (connection.FromTrackEdgeId == connection.ToTrackEdgeId) continue;
            var incoming = connection.FromDirection == TraversalDirection.Forward
                ? fromGeometry.To - fromGeometry.PointAt(.99) : fromGeometry.From - fromGeometry.PointAt(.01);
            var outgoing = connection.ToDirection == TraversalDirection.Forward
                ? toGeometry.PointAt(.01) - toGeometry.From : toGeometry.PointAt(.99) - toGeometry.To;
            var fromEdge = infrastructure.Edges.GetValueOrDefault(connection.FromTrackEdgeId);
            var toEdge = infrastructure.Edges.GetValueOrDefault(connection.ToTrackEdgeId);
            var allowLaneTurn = fromEdge is not null && toEdge is not null
                && (fromEdge.SchematicLane.HasValue || toEdge.SchematicLane.HasValue
                    || fromEdge.Kind != TrackEdgeKind.Mainline || toEdge.Kind != TrackEdgeKind.Mainline);
            StationSchematicPresentation.DrawConnection(RouteCanvas, from, to, incoming, outgoing, new SolidColorBrush(railColor),
                $"合法轉向：{connection.FromTrackEdgeId} ({connection.FromDirection}) → {connection.ToTrackEdgeId} ({connection.ToDirection})",
                allowLaneTurn);
        }

        foreach (var edge in infrastructure.Edges.Values.OrderBy(item => item.TrackEdgeId, StringComparer.OrdinalIgnoreCase))
        {
            var geometry = edgeGeometries[edge.TrackEdgeId];
            var emphasizeSideTrack = edgeGeometries.Count >= 32
                && edge.Kind is TrackEdgeKind.PassingTrack or TrackEdgeKind.Siding;
            if (emphasizeSideTrack)
            {
                // The large-route overview intentionally keeps short passing-track
                // transitions shallow.  Reserve a light outline so the parallel
                // side track remains visible instead of blending into the mainline.
                RouteCanvas.Children.Add(new Polyline
                {
                    Points = new PointCollection(geometry.Points),
                    Stroke = Brushes.White,
                    StrokeThickness = 7,
                    StrokeLineJoin = PenLineJoin.Round,
                    IsHitTestVisible = false
                });
            }
            RouteCanvas.Children.Add(new Polyline
            {
                Points = new PointCollection(geometry.Points),
                Stroke = new SolidColorBrush(emphasizeSideTrack ? Color.FromRgb(8, 123, 150) : railColor),
                StrokeThickness = emphasizeSideTrack ? 3.6 : 5,
                StrokeLineJoin = PenLineJoin.Round,
                ToolTip = $"{edge.TrackEdgeId}\n{UiDisplayText.Enum(edge.Kind)} · {edge.LengthMeters:0.#} m · 預設 {edge.DefaultSpeedLimitMetersPerSecond * 3.6:0.#} km/h"
            });
            if (edge.Directionality != TrackDirectionality.Bidirectional)
            {
                var arrowCenter = geometry.PointAt(0.38);
                var tangent = geometry.PointAt(0.40) - geometry.PointAt(0.36);
                if (edge.Directionality == TrackDirectionality.ReverseOnly) tangent = -tangent;
                if (tangent.Length > 0.01)
                {
                    tangent.Normalize();
                    var normal = new Vector(-tangent.Y, tangent.X);
                    RouteCanvas.Children.Add(new Polygon
                    {
                        Points = new PointCollection { arrowCenter + tangent * 7, arrowCenter - tangent * 5 + normal * 5, arrowCenter - tangent * 5 - normal * 5 },
                        Fill = new SolidColorBrush(railColor),
                        IsHitTestVisible = false
                    });
                }
            }

        }

        foreach (var node in nodes.Where(node => node.Kind == TrackNodeKind.BufferStop))
        {
            var edge = infrastructure.Edges.Values.FirstOrDefault(item =>
                item.FromNodeId.Equals(node.NodeId, StringComparison.OrdinalIgnoreCase)
                || item.ToNodeId.Equals(node.NodeId, StringComparison.OrdinalIgnoreCase));
            if (edge is null || !edgeGeometries.TryGetValue(edge.TrackEdgeId, out var geometry))
            {
                continue;
            }

            var atStart = edge.FromNodeId.Equals(node.NodeId, StringComparison.OrdinalIgnoreCase);
            var point = atStart ? geometry.Points[0] : geometry.Points[^1];
            var inner = atStart ? geometry.Points[Math.Min(1, geometry.Points.Count - 1)] : geometry.Points[Math.Max(0, geometry.Points.Count - 2)];
            var tangent = point - inner;
            if (tangent.Length <= double.Epsilon)
            {
                continue;
            }

            tangent.Normalize();
            var normal = new Vector(-tangent.Y, tangent.X) * 8;
            RouteCanvas.Children.Add(new Line
            {
                X1 = point.X - normal.X,
                Y1 = point.Y - normal.Y,
                X2 = point.X + normal.X,
                Y2 = point.Y + normal.Y,
                Stroke = new SolidColorBrush(railColor),
                StrokeThickness = 5,
                ToolTip = $"{node.Name} · 止衝\n中心基準里程 {stationChainage?.ToChainage(new(edge.TrackEdgeId, atStart ? 0 : edge.LengthMeters)) / 1000:0.000}K"
            });
        }

        StationSchematicPresentation.DrawStationNames(RouteCanvas, stationVisuals.Select(s => (s.Station.StationId,
            s.Station.Name + (stationChainage?.StationCenters.TryGetValue(s.Station.StationId, out var km) == true ? $"\n{km / 1000:0.000}K" : ""))), width);
        StationSchematicPresentation.DrawLayoutWarnings(RouteCanvas);

        foreach (var state in snapshot.Trains.Where(train => train.IsActive))
        {
            var centerPosition = world.GetTrainCenterPosition(state.VehicleId);
            if (centerPosition is not { } center || !edgeGeometries.TryGetValue(center.TrackEdgeId, out var geometry)
                || !infrastructure.TryGetEdge(center.TrackEdgeId, out var edge))
            {
                continue;
            }
            var offset = center.OffsetMeters;
            var ratio = Math.Clamp(offset / edge.LengthMeters, 0, 1);
            var point = geometry.PointAt(ratio);
            var index = ParseVehicleIndex(state.VehicleId);
            var marker = new Border
            {
                Width = 24,
                Height = 16,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(state.Phase is OperationalPhase.Collided or OperationalPhase.EmergencyStopped
                    ? Color.FromRgb(196, 48, 48)
                    : TrainColors[index % TrainColors.Length]),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(2),
                Child = new TextBlock
                {
                    Text = VehicleMarkerLabel(state.VehicleId),
                    Foreground = Brushes.White,
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                ToolTip = $"{state.VehicleId}｜{state.ServiceRunId}\n車體中心 {stationChainage?.ToChainage(center) / 1000:0.000}K\n車頭 {state.TrackEdgeId}，偏移 {state.OffsetMeters:0.#} m\n{PhaseToChinese(state.Phase)}｜{state.SpeedMetersPerSecond * 3.6:0.#} km/h"
            };
            Canvas.SetLeft(marker, Math.Clamp(point.X - 12, 0, width - 24));
            Canvas.SetTop(marker, Math.Clamp(point.Y - 8, 0, height - 16));
            RouteCanvas.Children.Add(marker);
        }

        StationSchematicPresentation.DrawChainageReference(RouteCanvas, stationChainage, height - 38);
        AddCanvasText(RouteCanvas, "軌道配線圖 · 將滑鼠移到軌道、月台或列車可查看詳細資料", 12, height - 21, 9, Color.FromRgb(108, 119, 132));

        static (string StartNodeId, string EndNodeId) GetTraversalEndpoints(
            TrackEdgeDefinition edge,
            TraversalDirection direction) => direction == TraversalDirection.Forward
                ? (edge.FromNodeId, edge.ToNodeId)
                : (edge.ToNodeId, edge.FromNodeId);

    }

    private void DrawV2SpeedProfile()
    {
        if (_updatingSpeedProfileSelection) return;
        var canvas = SpeedCanvas;
        canvas.Children.Clear();
        var width = canvas.ActualWidth;
        var height = canvas.ActualHeight;
        if (width < 100 || height < 100 || _v2World is null || _parameters is null)
        {
            return;
        }

        var selectedVehicleId = SpeedProfileRunComboBox.SelectedItem?.ToString();
        _updatingSpeedProfileSelection = true;
        try
        {
            foreach (var vehicleId in _v2World.GetSnapshot().Trains.Select(train => train.VehicleId)
                         .Distinct(StringComparer.Ordinal))
            {
                if (!SpeedProfileRunComboBox.Items.Contains(vehicleId)) SpeedProfileRunComboBox.Items.Add(vehicleId);
            }
            if (selectedVehicleId is null && SpeedProfileRunComboBox.Items.Count > 0)
            {
                SpeedProfileRunComboBox.SelectedIndex = 0;
                selectedVehicleId = SpeedProfileRunComboBox.SelectedItem?.ToString();
            }
        }
        finally { _updatingSpeedProfileSelection = false; }
        var actualSamples = _v2World.Trajectory
            .Where(sample => sample.VehicleId == selectedVehicleId)
            .ToArray();
        var useActual = actualSamples.Length >= 2;
        var samples = useActual
            ? actualSamples
            : (_v2Session?.PlannedTrajectory ?? [])
                .Where(sample => sample.VehicleId == selectedVehicleId)
                .ToArray();
        if (samples.Length < 2)
        {
            SpeedProfileSourceText.Text = selectedVehicleId is null ? "尚無列車" : $"{selectedVehicleId} · 尚無軌跡";
            AddCanvasText(canvas, "播放後顯示所選列車的上下行、停站及折返軌跡。", 16, 18, 12, Color.FromRgb(102, 112, 133));
            return;
        }

        var sourceEvents = useActual ? _v2World.Events : _v2Session!.PlannedEvents;
        var complete = sourceEvents.Any(item => item.VehicleId == selectedVehicleId && item.EventType == SimulationEventType.ServiceEnded);
        SpeedProfileSourceText.Text = useActual
            ? complete ? "V2 實際" : "實際（截至目前）"
            : complete ? "計畫預覽" : "計畫預覽（尚未完成）";
        SpeedProfileSourceText.ToolTip = $"{selectedVehicleId} · 同一列車上下行及折返接續軌跡；"
            + (useActual ? "顯示截至目前的實際運行。" : "顯示計畫模擬時間範圍內的運行。");

        var left = 42d;
        var top = 14d;
        var right = 15d;
        var bottom = 38d;
        var plotWidth = width - left - right;
        var plotHeight = height - top - bottom;
        var minTime = samples[0].SimulationTimeSeconds;
        var maxTime = Math.Max(minTime + 1, samples[^1].SimulationTimeSeconds);
        var maxSpeed = Math.Max(_parameters.MaxSpeedMetersPerSecond,
            samples.Max(sample => Math.Max(sample.SpeedMetersPerSecond, GetDisplaySpeedLimitMetersPerSecond(sample)))) * 3.6 * 1.1;
        DrawAxes(canvas, left, top, plotWidth, plotHeight, "km/h", string.Empty);
        DrawSpeedTimeAxisTicks(canvas, left, top, plotWidth, plotHeight, minTime, maxTime);

        var speedLine = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromRgb(232, 109, 45)),
            StrokeThickness = 2.4
        };
        var limitLine = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromRgb(205, 126, 24)),
            StrokeThickness = 1.4,
            StrokeDashArray = [4, 3]
        };
        foreach (var sample in TrajectoryAnalysis.DecimatePreservingCriticalPoints(samples, 450))
        {
            var x = left + (sample.SimulationTimeSeconds - minTime) / (maxTime - minTime) * plotWidth;
            var y = top + plotHeight - sample.SpeedMetersPerSecond * 3.6 / maxSpeed * plotHeight;
            speedLine.Points.Add(new Point(x, y));
            var limit = GetDisplaySpeedLimitMetersPerSecond(sample) * 3.6;
            limitLine.Points.Add(new Point(x, top + plotHeight - limit / maxSpeed * plotHeight));
        }

        canvas.Children.Add(limitLine);
        canvas.Children.Add(speedLine);
        var previousDirection = samples[0].Direction;
        var directionChangeIndex = 0;
        foreach (var sample in samples.Skip(1))
        {
            if (sample.Direction == previousDirection) continue;
            previousDirection = sample.Direction;
            var x = left + (sample.SimulationTimeSeconds - minTime) / (maxTime - minTime) * plotWidth;
            canvas.Children.Add(new Line
            {
                X1 = x, X2 = x, Y1 = top + 16, Y2 = top + plotHeight,
                Stroke = Brushes.SlateGray, StrokeDashArray = [2, 3],
                ToolTip = $"{TrajectoryAnalysis.FormatClock(_startClockSeconds + sample.SimulationTimeSeconds)} 換向為{DirectionToChinese(sample.Direction)} · {sample.ServiceRunId}"
            });
            AddCanvasText(canvas, $"轉{DirectionToChinese(sample.Direction)}", Math.Clamp(x + 3, left, left + plotWidth - 45),
                top + 18 + directionChangeIndex++ % 2 * 15, 10, Color.FromRgb(85, 94, 112));
        }
        AddCanvasText(canvas, useActual ? "— 實際速度　- - 軌道速限" : "— 計畫速度　- - 軌道速限", left + 8, top + 2, 10, Color.FromRgb(85, 94, 112));
    }

    private void DrawSpeedTimeAxisTicks(
        Canvas canvas,
        double left,
        double top,
        double width,
        double height,
        double minTime,
        double maxTime)
    {
        const int tickCount = 4;
        for (var index = 0; index <= tickCount; index++)
        {
            var ratio = index / (double)tickCount;
            var x = left + ratio * width;
            var time = minTime + ratio * (maxTime - minTime);
            canvas.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = top + height,
                Y2 = top + height + 4,
                Stroke = Brushes.SlateGray,
                StrokeThickness = 1
            });
            AddCanvasText(
                canvas,
                TrajectoryAnalysis.FormatClock(_startClockSeconds + time),
                Math.Clamp(x - 24, left - 2, left + width - 48),
                top + height + 5,
                9,
                Color.FromRgb(102, 112, 133));
        }
    }

    private static double? GetMinimumPlannedIntervalSeconds(ResolvedDispatchPlan dispatchPlan)
    {
        var starts = dispatchPlan.Runs
            .Select(run => RelativeDispatchSeconds(run.PlannedDepartureTime, dispatchPlan.ScheduleAnchorTime))
            .Distinct().OrderBy(value => value).ToArray();
        if (starts.Length < 2) return null;
        return starts.Zip(starts.Skip(1), (first, second) => second - first)
            .Where(interval => interval > 1e-7).DefaultIfEmpty().Min() is var minimum && minimum > 1e-7
                ? minimum : null;
    }

    private double GetDisplaySpeedLimitMetersPerSecond(TrajectorySample sample)
    {
        if (sample.TrackSpeedLimitMetersPerSecond is { } limit) return limit;
        if (_activeTopologyProjectDocument is not null
            && sample.TrackEdgeId is { Length: > 0 } edgeId
            && _v2World!.TopologyInfrastructure.Edges.TryGetValue(edgeId, out var edge))
        {
            return Math.Min(edge.DefaultSpeedLimitMetersPerSecond, _parameters!.MaxSpeedMetersPerSecond);
        }

        return _v2World!.SpeedLimits.GetCurrentLimitMetersPerSecond(
            sample.PositionMeters,
            sample.Direction,
            _parameters!.MaxSpeedMetersPerSecond);
    }

    private static double RelativeDispatchSeconds(TimeSpan value, TimeSpan anchor)
    {
        var result = value.TotalSeconds - anchor.TotalSeconds;
        return result < 0 ? result + TimeSpan.FromDays(1).TotalSeconds : result;
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
            AddCanvasText(SafetyDistanceCanvas, "所選配對在此時間範圍沒有資料；可切換上下行或選擇「全部時間」。", 18, 18, 12, Color.FromRgb(102, 112, 133));
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
        var width = targetWidth;
        var height = Math.Max(380, TimeDistanceCanvas.ActualHeight);
        if ((_route is null && _activeTopologyProjectDocument is null) || _v2World is null || _plannedWorld is null)
        {
            AddCanvasText(TimeDistanceCanvas, "建立並播放 V2 模擬後顯示時間－里程運行圖。", 22, 22, 13, Color.FromRgb(102, 112, 133));
            return;
        }

        (string StationId, string StationName, double PositionMeters)[] displayStations = _activeTopologyProjectDocument is null
            ? _route!.Stations.Select(station => (station.StationId, station.StationName, station.PositionMeters)).ToArray()
            : _v2World.GetTopologyResultContext().GetStops(TrainDirection.Outbound)
                .Select(stop => (stop.StationId, stop.StationName, stop.ProjectedChainageMeters)).ToArray();
        var displayRouteName = _activeTopologyProjectDocument?.ProjectName ?? _route!.RouteName;

        var actual = ShowActualCheckBox?.IsChecked == true ? _v2World.Trajectory : [];
        var planned = ShowPlannedCheckBox?.IsChecked == true ? _plannedWorld.Trajectory : [];
        if (actual.Count == 0 && planned.Count == 0)
        {
            AddCanvasText(TimeDistanceCanvas, "播放後即時建立運行圖；空圖不會啟動零列車模擬引擎。", 22, 22, 13, Color.FromRgb(102, 112, 133));
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
        var tailTrackLayouts = _activeTopologyProjectDocument is null
            ? GetAfterStationTailTrackVisualLayouts()
            : [];
        var positions = displayStations.Select(station => station.PositionMeters)
            .Concat(actual.Select(sample => sample.PositionMeters))
            .Concat(planned.Select(sample => sample.PositionMeters))
            .Concat(tailTrackLayouts.Select(item => item.Layout.VirtualNodePositionMeters))
            .ToArray();
        var minimumPosition = positions.Min();
        var maximumPosition = positions.Max();
        var positionSpan = Math.Max(1, maximumPosition - minimumPosition);
        double ToDiagramY(double position) => top + plotHeight
            - (position - minimumPosition) / positionSpan * plotHeight;
        DrawAxes(
            TimeDistanceCanvas,
            left,
            top,
            plotWidth,
            plotHeight,
            tailTrackLayouts.Count == 0 ? "累積里程" : "累積里程（含尾軌）",
            "時間");
        AddCanvasText(
            TimeDistanceCanvas,
            $"{displayRouteName}｜計畫／理論與 V2 模擬實際運行圖｜{UiDisplayText.Enum(_v2World.MovingBlockMode)}｜"
                + $"{(_activeTopologyProjectDocument is null ? $"速限 {_v2World.SpeedLimits.Limits.Count} 段" : "拓撲軌道區段速限")}｜固定時間步進 0.1 秒",
            left,
            8,
            14,
            Color.FromRgb(34, 43, 60));

        foreach (var station in displayStations)
        {
            var y = ToDiagramY(station.PositionMeters);
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

        foreach (var tailTrack in tailTrackLayouts)
        {
            var y = ToDiagramY(tailTrack.Layout.VirtualNodePositionMeters);
            TimeDistanceCanvas.Children.Add(new Line
            {
                X1 = left,
                X2 = left + plotWidth,
                Y1 = y,
                Y2 = y,
                Stroke = new SolidColorBrush(Color.FromRgb(188, 92, 52)),
                StrokeThickness = 1,
                StrokeDashArray = [4, 3]
            });
            AddCanvasText(
                TimeDistanceCanvas,
                $"{tailTrack.Layout.VirtualNodeId}  {tailTrack.Layout.VirtualNodePositionMeters / 1000:0.00} km",
                3,
                y - 8,
                10,
                Color.FromRgb(188, 92, 52));
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
                         SimulationEventType.Departure
                         or SimulationEventType.Arrival
                         or SimulationEventType.StationPassed
                         or SimulationEventType.TurnaroundStarted
                         or SimulationEventType.TailTrackReached
                         or SimulationEventType.TailTrackReturnStarted
                         or SimulationEventType.DirectionChanged
                         or SimulationEventType.ServiceEnded
                         or SimulationEventType.DepartureDelayed
                         or SimulationEventType.WaitingForResource
                         or SimulationEventType.ObstacleEmergencyStop
                         or SimulationEventType.PredictedCollision
                         or SimulationEventType.Collision
                         or SimulationEventType.SafetyStatusChanged))
        {
            var x = left + (simulationEvent.SimulationTimeSeconds - startTime) / visibleDuration * plotWidth;
            var y = ToDiagramY(simulationEvent.PositionMeters);
            var isSafetyEvent = simulationEvent.EventType is
                SimulationEventType.ObstacleEmergencyStop
                or SimulationEventType.PredictedCollision
                or SimulationEventType.Collision
                or SimulationEventType.SafetyStatusChanged;
            var isTerminalEvent = simulationEvent.EventType is
                SimulationEventType.TurnaroundStarted
                or SimulationEventType.TailTrackReached
                or SimulationEventType.TailTrackReturnStarted
                or SimulationEventType.DirectionChanged
                or SimulationEventType.ServiceEnded;
            var markerColor = isSafetyEvent
                ? Color.FromRgb(196, 48, 48)
                : isTerminalEvent
                    ? Color.FromRgb(126, 87, 194)
                    : Color.FromRgb(22, 134, 107);
            var marker = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = new SolidColorBrush(markerColor),
                Stroke = Brushes.White,
                StrokeThickness = 1,
                ToolTip = $"{TrajectoryAnalysis.FormatClock(_startClockSeconds + simulationEvent.SimulationTimeSeconds)}"
                    + $"｜{EventTypeToChinese(simulationEvent.EventType)}\n{simulationEvent.Message}"
            };
            Canvas.SetLeft(marker, x - 4);
            Canvas.SetTop(marker, y - 4);
            TimeDistanceCanvas.Children.Add(marker);
        }

        AddCanvasText(
            TimeDistanceCanvas,
            "實線：V2 模擬實際　虛線：無干擾計畫／理論　綠點：車站　紫點：折返／尾軌／退出　紅點：安全／障礙",
            left,
            28,
            10,
            Color.FromRgb(82, 93, 111));

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
                        ToDiagramY(sample.PositionMeters)));
                }

                TimeDistanceCanvas.Children.Add(line);
                if (points.Count > 0)
                {
                    var first = points[0];
                    AddCanvasText(
                        TimeDistanceCanvas,
                        ShortVehicle(first.VehicleId),
                        left + (first.SimulationTimeSeconds - startTime) / visibleDuration * plotWidth + 3,
                        ToDiagramY(first.PositionMeters) - 15,
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
            if (_v2World is null)
            {
                SafetySummaryText.Text = "請先建立 V2 寫實引擎模擬。";
            }
            else if (_v2World.MovingBlockMode == MovingBlockMode.Independent)
            {
                SafetySummaryText.Text = "目前為獨立運行模式，不建立移動閉塞相鄰配對；請切換為監視或控制。";
            }
            else if (_v2World.CurrentTimeSeconds <= 0.001)
            {
                SafetySummaryText.Text = "模擬尚未播放；第一個 0.1 秒時間步進後才會建立相鄰配對。";
            }
            else
            {
                var active = _v2World.GetSnapshot().Trains.Count(train => train.IsActive);
                SafetySummaryText.Text = active < 2
                    ? "目前營運中列車少於兩列，沒有可形成的相鄰配對。"
                    : "目前沒有同方向、同股道的相鄰列車配對；這是正常條件式空白。";
            }
            return;
        }

        var filteredHistory = _v2World.SafetyHistory.Where(MatchesSafetyFilters).ToArray();
        if (filteredHistory.Length == 0)
        {
            SafetySummaryText.Text = "已有安全觀測，但目前的方向／狀態篩選沒有符合資料。";
            return;
        }

        var minimum = filteredHistory.MinBy(item => item.SafetyMarginMeters)!;
        SafetySummaryText.Text = $"所選方向／狀態全程最低安全裕度 {minimum.SafetyMarginMeters:0.0} m，"
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

    private void PopulateFilterControls(ResolvedDispatchPlan dispatchPlan)
    {
        DiagramVehicleComboBox.Items.Clear();
        ObstacleTrainComboBox.Items.Clear();
        DiagramVehicleComboBox.Items.Add("全部");
        foreach (var vehicleId in dispatchPlan.Runs.Select(run => run.VehicleId)
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Select(value => value!)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            DiagramVehicleComboBox.Items.Add(vehicleId);
            ObstacleTrainComboBox.Items.Add(vehicleId);
        }

        DiagramVehicleComboBox.SelectedIndex = 0;
        ObstacleTrainComboBox.SelectedIndex = 0;
        _updatingSpeedProfileSelection = true;
        try
        {
            SpeedProfileRunComboBox.Items.Clear();
            foreach (var vehicleId in dispatchPlan.Runs.Select(run => run.VehicleId)
                         .Concat((_v2Session?.PlannedTrajectory ?? []).Select(sample => sample.VehicleId))
                         .Where(id => !string.IsNullOrWhiteSpace(id))
                         .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                SpeedProfileRunComboBox.Items.Add(vehicleId);
            }
            SpeedProfileRunComboBox.SelectedIndex = SpeedProfileRunComboBox.Items.Count > 0 ? 0 : -1;
        }
        finally { _updatingSpeedProfileSelection = false; }
        ObstacleDelayTextBox.Text = "0";
    }

    private void SpeedProfileRun_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            DrawV2SpeedProfile();
        }
    }

    private void UpdateV2ActualSummary()
    {
        if (_v2World is null || _v2DispatchPlan is null || _parameters is null)
        {
            return;
        }

        var events = _v2World.Events;
        var topologyResults = _activeTopologyProjectDocument is null
            ? null
            : _v2World.GetTopologyResultContext();
        var completedTrips = new List<double>();
        foreach (var run in _v2DispatchPlan.Runs)
        {
            var routeStops = topologyResults?.GetDisplayStations(run.Direction);
            var originPosition = routeStops is { Count: > 0 }
                ? routeStops[0].PositionMeters
                : run.Direction == TrainDirection.Outbound ? 0 : _route!.TotalLengthMeters;
            var terminalPosition = routeStops is { Count: > 0 }
                ? routeStops[^1].PositionMeters
                : run.Direction == TrainDirection.Outbound ? _route!.TotalLengthMeters : 0;
            var departure = events.Where(item => item.EventType == SimulationEventType.Departure
                    && item.ServiceRunId.Equals(run.ServiceRunId, StringComparison.OrdinalIgnoreCase)
                    && item.Direction == run.Direction
                    && MatchesRunVehicle(item)
                    && MatchesStation(item, routeStops?.FirstOrDefault()?.StationId, originPosition))
                .OrderBy(item => item.SimulationTimeSeconds)
                .FirstOrDefault();
            var arrival = events.Where(item => item.EventType == SimulationEventType.Arrival
                    && item.ServiceRunId.Equals(run.ServiceRunId, StringComparison.OrdinalIgnoreCase)
                    && item.Direction == run.Direction
                    && MatchesRunVehicle(item)
                    && MatchesStation(item, routeStops?.LastOrDefault()?.StationId, terminalPosition))
                .OrderBy(item => item.SimulationTimeSeconds)
                .FirstOrDefault();
            if (departure is not null && arrival is not null && arrival.SimulationTimeSeconds >= departure.SimulationTimeSeconds)
            {
                completedTrips.Add(arrival.SimulationTimeSeconds - departure.SimulationTimeSeconds);
            }

            bool MatchesRunVehicle(SimulationEvent item) => string.IsNullOrWhiteSpace(run.VehicleId)
                || string.Equals(item.VehicleId, run.VehicleId, StringComparison.OrdinalIgnoreCase);
        }

        bool MatchesStation(SimulationEvent item, string? stationId, double legacyPosition)
        {
            if (topologyResults is not null)
                return stationId is not null && topologyResults.MatchesStationEvent(item.Direction, stationId, item);
            return Math.Abs(item.PositionMeters - legacyPosition) <= .6;
        }

        OneWaySummaryText.Text = completedTrips.Count > 0
            ? $"{FormatDuration(completedTrips.Average())}（V2 實際平均）"
            : _cycle is not null
                ? $"{FormatDuration(_cycle.OutboundTrip.TotalRunTimeSeconds)}（無干擾基準）"
                : "尚無完成行程";

        var originDepartures = events.Where(item => item.EventType == SimulationEventType.Departure
            && (topologyResults is null
                ? Math.Abs(item.PositionMeters - (item.Direction == TrainDirection.Outbound ? 0 : _route!.TotalLengthMeters)) <= .6
                : topologyResults.MatchesStationEvent(item.Direction,
                    topologyResults.GetStops(item.Direction)[0].StationId, item)));
        var intervals = originDepartures.GroupBy(item => item.Direction).SelectMany(group =>
        {
            var times = group.Select(item => item.SimulationTimeSeconds).Distinct().Order().ToArray();
            return times.Zip(times.Skip(1), (first, second) => second - first);
        }).Where(value => value > 1e-7)
            .ToArray();
        HeadwaySummaryText.Text = intervals.Length > 0
            ? $"{FormatDuration(intervals.Min())}（V2 實際最短）"
            : _v2PlannedMinimumIntervalSeconds is { } planned
                ? $"{FormatDuration(planned)}（計畫最短）"
                : "單一／同時發車";

        var peakSpeed = _v2World.Trajectory.Count > 0
            ? _v2World.Trajectory.Max(sample => sample.SpeedMetersPerSecond) * 3.6
            : 0;
        SpeedSummaryText.Text = peakSpeed > 0
            ? $"{_parameters.MaxSpeedMetersPerSecond * 3.6:0.#} / {peakSpeed:0.#} km/h（V2 實際）"
            : $"{_parameters.MaxSpeedMetersPerSecond * 3.6:0.#} km/h（尚未播放）";

        var outcomeTimes = events.Where(item => item.EventType == SimulationEventType.ServiceEnded)
            .GroupBy(item => item.VehicleId)
            .Select(group =>
            {
                var departure = events.Where(item => item.EventType == SimulationEventType.Departure && item.VehicleId == group.Key)
                    .MinBy(item => item.SimulationTimeSeconds);
                return departure is null ? (double?)null : group.Max(item => item.SimulationTimeSeconds) - departure.SimulationTimeSeconds;
            })
            .Where(value => value.HasValue).Select(value => value!.Value)
            .ToArray();
        CycleSummaryText.Text = outcomeTimes.Length > 0
            ? $"{FormatDuration(outcomeTimes.Average())}（已完成列車平均）"
            : _cycle is not null
                ? $"{FormatDuration(_cycle.CycleTimeSeconds)}（無干擾基準）"
                : "尚無完成全程";
    }

    private string GetActiveRouteDisplayName() =>
        _activeTopologyProjectDocument?.ProjectName
        ?? _route?.RouteName
        ?? "拓撲路線";

    private bool HasPendingV2TerminalOutcomes()
    {
        if (_v2World is null || _v2DispatchPlan is null)
        {
            return false;
        }

        foreach (var run in _v2DispatchPlan.Runs)
        {
            var plannedStart = RelativeDispatchSeconds(run.PlannedDepartureTime, _v2DispatchPlan.ScheduleAnchorTime);
            if (_v2World.CurrentTimeSeconds + 1e-7 < plannedStart)
            {
                return true;
            }

            if (!run.ContinueAfterTerminal)
            {
                if (!_v2World.Events.Any(item => item.EventType == SimulationEventType.ServiceEnded
                    && item.ServiceRunId.Equals(run.ServiceRunId, StringComparison.OrdinalIgnoreCase)
                    && (string.IsNullOrWhiteSpace(run.VehicleId)
                        || item.VehicleId.Equals(run.VehicleId, StringComparison.OrdinalIgnoreCase))))
                {
                    return true;
                }

                continue;
            }

            if (!_v2World.Events.Any(item => item.EventType == SimulationEventType.DirectionChanged
                && (string.IsNullOrWhiteSpace(run.VehicleId)
                    || item.VehicleId.Equals(run.VehicleId, StringComparison.OrdinalIgnoreCase))
                && item.SimulationTimeSeconds + 1e-7 >= plannedStart))
            {
                return true;
            }
        }

        return false;
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

    private static string GetSelectedTag(ComboBox comboBox) =>
        comboBox.SelectedItem is ComboBoxItem item ? item.Tag?.ToString() ?? string.Empty : string.Empty;

    private static double ParseDiagramMinute(TextBox textBox, double fallback)
    {
        return TryParseFlexible(textBox.Text, out var value) && double.IsFinite(value) && value >= 0
            ? value
            : fallback;
    }

    private static string PairKey(SafetyObservation item) =>
        $"{ShortVehicle(item.FollowerVehicleId)} → {ShortVehicle(item.LeaderVehicleId)}｜{DirectionToChinese(item.Direction)}｜{item.TrackId}";

    private static string ShortVehicle(string vehicleId) => vehicleId.Replace("Vehicle ", "V", StringComparison.Ordinal);

    private static int ParseVehicleIndex(string vehicleId)
    {
        if (int.TryParse(vehicleId.AsSpan(vehicleId.LastIndexOf(' ') + 1), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var value))
        {
            return Math.Max(0, value - 1);
        }

        unchecked
        {
            var hash = 2166136261u;
            foreach (var character in vehicleId)
            {
                hash = (hash ^ char.ToUpperInvariant(character)) * 16777619u;
            }
            return (int)(hash & 0x7FFFFFFF);
        }
    }

    private static string VehicleMarkerLabel(string vehicleId)
    {
        if (vehicleId.StartsWith("Vehicle ", StringComparison.Ordinal)
            && int.TryParse(vehicleId.AsSpan(8), out var legacyNumber))
        {
            return $"V{legacyNumber:00}";
        }

        if (vehicleId.StartsWith("AUTO-", StringComparison.OrdinalIgnoreCase))
        {
            return "A" + vehicleId[5..].TrimStart('0').PadLeft(2, '0');
        }

        return vehicleId.Length <= 5 ? vehicleId : vehicleId[..5];
    }

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
        OperationalPhase.TailTrackOutbound => "駛入尾軌",
        OperationalPhase.TailTrackReturn => "尾軌返回",
        OperationalPhase.SpatialTurnbackOutbound => "駛入折返線",
        OperationalPhase.SpatialTurnbackReturn => "折返線返回",
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
        SimulationEventType.StationPassed => "跨站通過",
        SimulationEventType.StationStopViolation => "停車超限",
        SimulationEventType.BrakingModeChanged => "煞車模式切換",
        SimulationEventType.DepartureDelayed => "延遲發車",
        SimulationEventType.WaitingForResource => "等待進路",
        SimulationEventType.PlatformAssigned => "月台配置",
        SimulationEventType.RouteReserved => "進路鎖定",
        SimulationEventType.RouteReleased => "進路釋放",
        SimulationEventType.TailTrackReached => "抵達尾軌節點",
        SimulationEventType.TailTrackReturnStarted => "尾軌返回",
        SimulationEventType.OvertakeRequested => "待避要求",
        SimulationEventType.OvertakeCompleted => "待避完成",
        SimulationEventType.OvertakeCancelled => "待避取消",
        SimulationEventType.ServiceEnded => "退出營運",
        _ => "其他事件"
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
