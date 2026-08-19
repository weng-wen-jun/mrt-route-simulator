using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MrtRouteSimulator.Engine;
using EngineRoute = MrtRouteSimulator.Engine.Route;

namespace MrtRouteSimulator.App;

public partial class MainWindow : Window
{
    private static readonly Color[] TrainColors =
    [
        Color.FromRgb(232, 109, 45),
        Color.FromRgb(34, 126, 173),
        Color.FromRgb(22, 134, 107),
        Color.FromRgb(126, 87, 194),
        Color.FromRgb(205, 75, 112),
        Color.FromRgb(56, 163, 165),
        Color.FromRgb(231, 165, 48),
        Color.FromRgb(82, 102, 159)
    ];

    private readonly DispatcherTimer _playbackTimer;
    private EngineRoute? _route;
    private TrainParameters? _parameters;
    private CycleTimeResult? _cycle;
    private MultipleTrainResult? _multipleTrainResult;
    private SimulationEngine? _simulationEngine;
    private double _playbackTimeSeconds;
    private double _playbackDurationSeconds;
    private double _startClockSeconds;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _playbackTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _playbackTimer.Tick += PlaybackTimer_Tick;
        LoadSampleData();
        Loaded += (_, _) =>
        {
            DrawRoute();
            DrawSpeedProfile();
        };
    }

    public ObservableCollection<StationInputRow> StationRows { get; } = [];

    public ObservableCollection<TimetableRow> TimetableRows { get; } = [];

    public ObservableCollection<SegmentRow> SegmentRows { get; } = [];

    public ObservableCollection<CurrentTrainRow> CurrentTrainRows { get; } = [];

    private void LoadSample_Click(object sender, RoutedEventArgs e) => LoadSampleData();

    private void LoadSampleData()
    {
        PausePlayback();
        RouteIdTextBox.Text = "O";
        RouteNameTextBox.Text = "橘色示範線";
        MaxSpeedTextBox.Text = "80";
        AccelerationTextBox.Text = "1.0";
        DecelerationTextBox.Text = "1.0";
        DefaultDwellTextBox.Text = "30";
        OriginTurnaroundTextBox.Text = "3";
        TerminalTurnaroundTextBox.Text = "6";
        TrainCountTextBox.Text = "6";
        HeadwayTextBox.Text = string.Empty;
        StartTimeTextBox.Text = "06:00:00";

        StationRows.Clear();
        StationRows.Add(new StationInputRow { StationId = "O01", StationName = "海風站", DistanceFromPreviousKm = 0, DwellTimeSeconds = 0 });
        StationRows.Add(new StationInputRow { StationId = "O02", StationName = "港灣站", DistanceFromPreviousKm = 1.2, DwellTimeSeconds = 30 });
        StationRows.Add(new StationInputRow { StationId = "O03", StationName = "新城站", DistanceFromPreviousKm = 0.8, DwellTimeSeconds = 30 });
        StationRows.Add(new StationInputRow { StationId = "O04", StationName = "中央站", DistanceFromPreviousKm = 1.65, DwellTimeSeconds = 35 });
        StationRows.Add(new StationInputRow { StationId = "O05", StationName = "科園站", DistanceFromPreviousKm = 1.1, DwellTimeSeconds = 30 });
        StationRows.Add(new StationInputRow { StationId = "O06", StationName = "山景站", DistanceFromPreviousKm = 1.45, DwellTimeSeconds = 0 });
        LoadSampleV2Data();

        ClearResults();
        HideValidation();
        StatusTextBlock.Text = "已載入六站示範路線；可直接建立模擬或修改參數。";
    }

    private void AddStation_Click(object sender, RoutedEventArgs e)
    {
        StationDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        StationDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var defaultDwell = TryParseFlexible(DefaultDwellTextBox.Text, out var parsedDwell) ? parsedDwell : 30;
        var routePrefix = string.IsNullOrWhiteSpace(RouteIdTextBox.Text) ? "S" : RouteIdTextBox.Text.Trim().ToUpperInvariant();
        StationRows.Add(new StationInputRow
        {
            StationId = $"{routePrefix}{StationRows.Count + 1:00}",
            StationName = $"新車站 {StationRows.Count + 1}",
            DistanceFromPreviousKm = StationRows.Count == 0 ? 0 : 1,
            DwellTimeSeconds = defaultDwell
        });
        StationDataGrid.SelectedIndex = StationRows.Count - 1;
        StationDataGrid.ScrollIntoView(StationRows[^1]);
    }

    private void RemoveStation_Click(object sender, RoutedEventArgs e)
    {
        if (StationRows.Count <= 2)
        {
            ShowValidation(["路線至少需要 2 個車站，無法再刪除。"]);
            return;
        }

        var index = StationDataGrid.SelectedIndex;
        if (index < 0)
        {
            ShowValidation(["請先選取要刪除的車站。"]);
            return;
        }

        StationRows.RemoveAt(index);
        StationRows[0].DistanceFromPreviousKm = 0;
        StationDataGrid.Items.Refresh();
        HideValidation();
    }

    private void MoveStationUp_Click(object sender, RoutedEventArgs e)
    {
        var index = StationDataGrid.SelectedIndex;
        if (index <= 0)
        {
            return;
        }

        SwapStationIdentity(index, index - 1);
        StationDataGrid.SelectedIndex = index - 1;
    }

    private void MoveStationDown_Click(object sender, RoutedEventArgs e)
    {
        var index = StationDataGrid.SelectedIndex;
        if (index < 0 || index >= StationRows.Count - 1)
        {
            return;
        }

        SwapStationIdentity(index, index + 1);
        StationDataGrid.SelectedIndex = index + 1;
    }

    private void SwapStationIdentity(int firstIndex, int secondIndex)
    {
        var first = StationRows[firstIndex];
        var second = StationRows[secondIndex];
        (first.StationId, second.StationId) = (second.StationId, first.StationId);
        (first.StationName, second.StationName) = (second.StationName, first.StationName);
        (first.DwellTimeSeconds, second.DwellTimeSeconds) = (second.DwellTimeSeconds, first.DwellTimeSeconds);
        StationDataGrid.Items.Refresh();
    }

    private void RunSimulation_Click(object sender, RoutedEventArgs e)
    {
        PausePlayback();
        HideValidation();
        StationDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        StationDataGrid.CommitEdit(DataGridEditingUnit.Row, true);

        try
        {
            var defaultDwellSeconds = ParseNonNegative(DefaultDwellTextBox, "預設停站時間");
            var stationInputs = StationRows.Select((row, index) => new StationInput(
                row.StationId,
                row.StationName,
                row.DistanceFromPreviousKm * 1000,
                row.DwellTimeSeconds ?? defaultDwellSeconds)).ToArray();

            _route = RouteFactory.FromSegmentDistances(
                RouteIdTextBox.Text,
                RouteNameTextBox.Text,
                stationInputs,
                defaultDwellSeconds);

            var maxSpeedMetersPerSecond = ParsePositive(MaxSpeedTextBox, "最高速度") / 3.6;
            _parameters = new TrainParameters(
                maxSpeedMetersPerSecond,
                ParsePositive(AccelerationTextBox, "加速度"),
                ParsePositive(DecelerationTextBox, "減速度"),
                defaultDwellSeconds,
                ParseNonNegative(OriginTurnaroundTextBox, "起點折返時間") * 60,
                ParseNonNegative(TerminalTurnaroundTextBox, "終點折返時間") * 60);

            var trainCount = ParsePositiveInteger(TrainCountTextBox, "列車數量");
            double? specifiedHeadwaySeconds = string.IsNullOrWhiteSpace(HeadwayTextBox.Text)
                ? null
                : ParsePositive(HeadwayTextBox, "指定班距") * 60;
            _startClockSeconds = ParseClock(StartTimeTextBox.Text);

            _cycle = TripSimulator.CalculateCycleTime(_route, _parameters, _startClockSeconds);
            _multipleTrainResult = TripSimulator.SimulateMultipleTrains(
                _route,
                _parameters,
                trainCount,
                specifiedHeadwaySeconds,
                _startClockSeconds);
            _simulationEngine = new SimulationEngine(
                _route,
                _parameters,
                trainCount,
                specifiedHeadwaySeconds,
                0.1);
            ConfigureV2World(trainCount, specifiedHeadwaySeconds);

            if (!_v2Enabled)
            {
                _playbackDurationSeconds = _simulationEngine.CycleTimeSeconds
                    + (_simulationEngine.TrainCount - 1) * _simulationEngine.HeadwaySeconds;
            }
            _playbackTimeSeconds = 0;
            PopulateResults();
            UpdatePlaybackView();
            PlayButton.IsEnabled = true;
            StatusTextBlock.Text = $"{(_v2Enabled ? "V2 實際營運" : "V1 基礎物理")}模擬建立完成：{trainCount} 列車、{_route.Stations.Count} 站、固定 Tick 0.1 秒。";
            PlaybackStatusText.Text = "模擬已就緒，按「播放」查看列車運行。";
        }
        catch (SimulationValidationException exception)
        {
            ShowValidation(exception.Errors);
            StatusTextBlock.Text = "資料驗證未通過；請依左側訊息修正。";
        }
        catch (InvalidOperationException exception)
        {
            ShowValidation([exception.Message]);
            StatusTextBlock.Text = "資料驗證未通過；請依左側訊息修正。";
        }
        catch (Exception exception)
        {
            ShowValidation([$"無法建立模擬：{exception.Message}"]);
            StatusTextBlock.Text = "建立模擬時發生錯誤。";
        }
    }

    private void PopulateResults()
    {
        if (_route is null || _cycle is null || _multipleTrainResult is null || _parameters is null)
        {
            return;
        }

        RouteSummaryText.Text = $"{_route.Stations.Count} 站 · {_route.TotalLengthMeters / 1000:0.###} km";
        OneWaySummaryText.Text = FormatDuration(_cycle.OutboundTrip.TotalRunTimeSeconds);
        CycleSummaryText.Text = FormatDuration(_cycle.CycleTimeSeconds);
        HeadwaySummaryText.Text = FormatDuration(_multipleTrainResult.HeadwaySeconds);
        var actualPeak = _cycle.OutboundTrip.Segments.Max(segment => segment.Motion.PeakSpeedMetersPerSecond) * 3.6;
        SpeedSummaryText.Text = $"{_parameters.MaxSpeedMetersPerSecond * 3.6:0.#} / {actualPeak:0.#} km/h";

        TimetableRows.Clear();
        foreach (var train in _multipleTrainResult.Trains)
        {
            for (var index = 0; index < train.OutboundTrip.StationEvents.Count; index++)
            {
                var stationEvent = train.OutboundTrip.StationEvents[index];
                var isOrigin = index == 0;
                var isTerminal = index == train.OutboundTrip.StationEvents.Count - 1;
                TimetableRows.Add(new TimetableRow(
                    train.TrainId,
                    "下行",
                    stationEvent.StationId,
                    stationEvent.StationName,
                    isOrigin ? "—" : FormatClock(stationEvent.ArrivalTimeSeconds),
                    isTerminal ? "—" : FormatClock(stationEvent.DepartureTimeSeconds),
                    $"{stationEvent.DwellTimeSeconds:0.##} s",
                    $"{stationEvent.CumulativePositionMeters / 1000:0.###}"));
            }
        }

        SegmentRows.Clear();
        foreach (var segment in _cycle.OutboundTrip.Segments)
        {
            SegmentRows.Add(new SegmentRow(
                $"{segment.FromStation.StationId} → {segment.ToStation.StationId}",
                $"{segment.Motion.DistanceMeters / 1000:0.###}",
                ProfileToChinese(segment.Motion.ProfileType),
                $"{segment.Motion.PeakSpeedMetersPerSecond * 3.6:0.##}",
                FormatSeconds(segment.Motion.TravelTimeSeconds),
                FormatSeconds(segment.Motion.AccelerationTimeSeconds),
                FormatSeconds(segment.Motion.CruisingTimeSeconds),
                FormatSeconds(segment.Motion.DecelerationTimeSeconds)));
        }

        DrawRoute();
        DrawSpeedProfile();
        PopulateV2Results();
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_simulationEngine is null)
        {
            return;
        }

        if (_playbackTimeSeconds >= _playbackDurationSeconds)
        {
            _playbackTimeSeconds = 0;
        }

        _playbackTimer.Start();
        PlaybackStatusText.Text = "播放中；倍率只影響畫面，不改變物理結果。";
        StatusTextBlock.Text = "正在播放模擬。";
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        PausePlayback();
        if (_simulationEngine is not null)
        {
            PlaybackStatusText.Text = "已暫停；可繼續播放或重設。";
            StatusTextBlock.Text = "模擬已暫停。";
        }
    }

    private void ResetPlayback_Click(object sender, RoutedEventArgs e)
    {
        PausePlayback();
        _playbackTimeSeconds = 0;
        if (_simulationEngine is not null)
        {
            _simulationEngine.Reset();
            ResetV2Playback();
            UpdatePlaybackView();
            PlaybackStatusText.Text = "已回到首班列車發車時刻。";
            StatusTextBlock.Text = "播放進度已重設。";
        }
    }

    private void PlaybackTimer_Tick(object? sender, EventArgs e)
    {
        if (_simulationEngine is null)
        {
            PausePlayback();
            return;
        }

        _playbackTimeSeconds += _playbackTimer.Interval.TotalSeconds * GetPlaybackSpeed();
        if (_playbackTimeSeconds >= _playbackDurationSeconds)
        {
            _playbackTimeSeconds = _playbackDurationSeconds;
            PausePlayback();
            PlaybackStatusText.Text = "所有列車均已完成一個循環。";
            StatusTextBlock.Text = "模擬播放完成。";
        }

        UpdatePlaybackView();
    }

    private void UpdatePlaybackView()
    {
        if (_simulationEngine is null)
        {
            return;
        }

        if (_v2Enabled)
        {
            UpdateV2PlaybackView();
            return;
        }

        _simulationEngine.SetCurrentTime(_playbackTimeSeconds);
        var states = _simulationEngine.GetTrainStates();
        CurrentTrainRows.Clear();
        for (var index = 0; index < states.Count; index++)
        {
            var state = states[index];
            var hasDeparted = _playbackTimeSeconds + 1e-8 >= _simulationEngine.InitialDepartureOffsetsSeconds[index];
            CurrentTrainRows.Add(new CurrentTrainRow(
                state.TrainId,
                hasDeparted ? (state.Direction == TrainDirection.Outbound ? "下行" : "上行") : "—",
                hasDeparted ? StateToChinese(state.State) : "待發",
                $"{state.PositionMeters / 1000:0.###}",
                $"{state.SpeedMetersPerSecond * 3.6:0.#}",
                state.CurrentStationId,
                state.NextStationId ?? "—"));
        }

        SimulationClockText.Text = FormatClock(_startClockSeconds + _playbackTimeSeconds);
        DrawRoute(states);
    }

    private void DrawRoute(IReadOnlyList<TrainState>? states = null)
    {
        if (_v2Enabled)
        {
            DrawV2Route();
            return;
        }

        RouteCanvas.Children.Clear();
        var width = RouteCanvas.ActualWidth;
        var height = RouteCanvas.ActualHeight;
        if (width < 100 || height < 100)
        {
            return;
        }

        if (_route is null)
        {
            AddCanvasText(RouteCanvas, "建立模擬後，這裡會顯示多列車往返動畫。", 26, 28, 14, Color.FromRgb(102, 112, 133));
            return;
        }

        var left = 58d;
        var right = 42d;
        var trackWidth = Math.Max(1, width - left - right);
        var trackY = height * 0.53;

        RouteCanvas.Children.Add(new Line
        {
            X1 = left,
            X2 = left + trackWidth,
            Y1 = trackY,
            Y2 = trackY,
            Stroke = new SolidColorBrush(Color.FromRgb(70, 83, 105)),
            StrokeThickness = 5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });

        foreach (var station in _route.Stations)
        {
            var x = left + station.PositionMeters / _route.TotalLengthMeters * trackWidth;
            var marker = new Ellipse
            {
                Width = 16,
                Height = 16,
                Fill = Brushes.White,
                Stroke = new SolidColorBrush(Color.FromRgb(232, 109, 45)),
                StrokeThickness = 4,
                ToolTip = $"{station.StationId} {station.StationName}\n{station.PositionMeters / 1000:0.###} km"
            };
            Canvas.SetLeft(marker, x - 8);
            Canvas.SetTop(marker, trackY - 8);
            RouteCanvas.Children.Add(marker);

            var label = new TextBlock
            {
                Text = $"{station.StationId}\n{station.StationName}",
                TextAlignment = TextAlignment.Center,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(42, 52, 70)),
                Width = 82
            };
            Canvas.SetLeft(label, Math.Clamp(x - 41, 0, width - 82));
            Canvas.SetTop(label, trackY + 14);
            RouteCanvas.Children.Add(label);
        }

        AddCanvasText(RouteCanvas, "下行 →", left, 24, 12, Color.FromRgb(102, 112, 133));
        AddCanvasText(RouteCanvas, "← 上行", left, height - 34, 12, Color.FromRgb(102, 112, 133));

        states ??= _simulationEngine?.GetTrainStates(_playbackTimeSeconds);
        if (states is null)
        {
            return;
        }

        for (var index = 0; index < states.Count; index++)
        {
            if (_simulationEngine is not null
                && _playbackTimeSeconds + 1e-8 < _simulationEngine.InitialDepartureOffsetsSeconds[index])
            {
                continue;
            }

            var state = states[index];
            var x = left + state.PositionMeters / _route.TotalLengthMeters * trackWidth;
            var laneOffset = (index % 3) * 23;
            var y = state.Direction == TrainDirection.Outbound
                ? trackY - 55 - laneOffset
                : trackY + 55 + laneOffset;
            var color = TrainColors[index % TrainColors.Length];
            var train = new Border
            {
                Width = 42,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(color),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(2),
                Child = new TextBlock
                {
                    Text = $"{index + 1:00}",
                    Foreground = Brushes.White,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                ToolTip = $"{state.TrainId}\n{StateToChinese(state.State)}\n位置 {state.PositionMeters / 1000:0.###} km\n速度 {state.SpeedMetersPerSecond * 3.6:0.#} km/h"
            };
            Canvas.SetLeft(train, Math.Clamp(x - 21, 0, width - 42));
            Canvas.SetTop(train, Math.Clamp(y - 11, 4, height - 26));
            RouteCanvas.Children.Add(train);
        }
    }

    private void DrawSpeedProfile()
    {
        if (_v2Enabled)
        {
            DrawV2SpeedProfile();
            return;
        }

        SpeedCanvas.Children.Clear();
        var width = SpeedCanvas.ActualWidth;
        var height = SpeedCanvas.ActualHeight;
        if (width < 100 || height < 100)
        {
            return;
        }

        if (_cycle is null || _parameters is null)
        {
            AddCanvasText(SpeedCanvas, "建立模擬後顯示速度－時間曲線。", 22, 24, 12, Color.FromRgb(102, 112, 133));
            return;
        }

        var left = 42d;
        var right = 16d;
        var top = 16d;
        var bottom = 32d;
        var plotWidth = width - left - right;
        var plotHeight = height - top - bottom;
        var totalTime = _cycle.OutboundTrip.TotalRunTimeSeconds;
        var maxSpeed = _parameters.MaxSpeedMetersPerSecond * 1.08;

        for (var index = 0; index <= 4; index++)
        {
            var y = top + plotHeight * index / 4;
            SpeedCanvas.Children.Add(new Line
            {
                X1 = left,
                X2 = left + plotWidth,
                Y1 = y,
                Y2 = y,
                Stroke = new SolidColorBrush(Color.FromRgb(226, 230, 237)),
                StrokeThickness = 1
            });
        }

        SpeedCanvas.Children.Add(new Line { X1 = left, X2 = left, Y1 = top, Y2 = top + plotHeight, Stroke = Brushes.SlateGray, StrokeThickness = 1.2 });
        SpeedCanvas.Children.Add(new Line { X1 = left, X2 = left + plotWidth, Y1 = top + plotHeight, Y2 = top + plotHeight, Stroke = Brushes.SlateGray, StrokeThickness = 1.2 });
        AddCanvasText(SpeedCanvas, "km/h", 3, 2, 10, Color.FromRgb(102, 112, 133));
        AddCanvasText(SpeedCanvas, "時間", width - 42, height - 22, 10, Color.FromRgb(102, 112, 133));

        var polyline = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromRgb(232, 109, 45)),
            StrokeThickness = 2.5,
            StrokeLineJoin = PenLineJoin.Round
        };

        void AddPoint(double time, double speed)
        {
            var x = left + Math.Clamp(time / totalTime, 0, 1) * plotWidth;
            var y = top + plotHeight - Math.Clamp(speed / maxSpeed, 0, 1) * plotHeight;
            polyline.Points.Add(new Point(x, y));
        }

        AddPoint(0, 0);
        foreach (var segment in _cycle.OutboundTrip.Segments)
        {
            var departure = segment.DepartureTimeSeconds - _cycle.OutboundTrip.DepartureFromOriginSeconds;
            var accelerationEnd = departure + segment.Motion.AccelerationTimeSeconds;
            var cruiseEnd = accelerationEnd + segment.Motion.CruisingTimeSeconds;
            var arrival = segment.ArrivalTimeSeconds - _cycle.OutboundTrip.DepartureFromOriginSeconds;
            AddPoint(departure, 0);
            AddPoint(accelerationEnd, segment.Motion.PeakSpeedMetersPerSecond);
            if (segment.Motion.CruisingTimeSeconds > 0)
            {
                AddPoint(cruiseEnd, segment.Motion.PeakSpeedMetersPerSecond);
            }

            AddPoint(arrival, 0);
            var destinationEvent = _cycle.OutboundTrip.StationEvents.First(item => item.StationId == segment.ToStation.StationId);
            AddPoint(destinationEvent.DepartureTimeSeconds - _cycle.OutboundTrip.DepartureFromOriginSeconds, 0);
        }

        SpeedCanvas.Children.Add(polyline);
    }

    private void RouteCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawRoute();

    private void SpeedCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawSpeedProfile();

    private void ClearResults()
    {
        _route = null;
        _parameters = null;
        _cycle = null;
        _multipleTrainResult = null;
        _simulationEngine = null;
        ClearV2Results();
        _playbackTimeSeconds = 0;
        _playbackDurationSeconds = 0;
        TimetableRows.Clear();
        SegmentRows.Clear();
        CurrentTrainRows.Clear();
        RouteSummaryText.Text = "—";
        OneWaySummaryText.Text = "—";
        CycleSummaryText.Text = "—";
        HeadwaySummaryText.Text = "—";
        SpeedSummaryText.Text = "—";
        SimulationClockText.Text = "--:--:--";
        PlaybackStatusText.Text = "請先建立模擬";
        PlayButton.IsEnabled = false;
        DrawRoute();
        DrawSpeedProfile();
    }

    private void PausePlayback() => _playbackTimer.Stop();

    private double GetPlaybackSpeed()
    {
        if (PlaybackSpeedComboBox.SelectedItem is ComboBoxItem item
            && double.TryParse(item.Tag?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var speed))
        {
            return speed;
        }

        return 1;
    }

    private static double ParseClock(string value)
    {
        if (!TimeSpan.TryParse(value, CultureInfo.CurrentCulture, out var parsed)
            && !TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out parsed))
        {
            throw new InvalidOperationException("首班發車時間請使用 HH:mm:ss，例如 06:00:00。");
        }

        if (parsed < TimeSpan.Zero || parsed >= TimeSpan.FromDays(1))
        {
            throw new InvalidOperationException("首班發車時間必須介於 00:00:00 與 23:59:59。");
        }

        return parsed.TotalSeconds;
    }

    private static int ParsePositiveInteger(TextBox textBox, string name)
    {
        if (!int.TryParse(textBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) || value <= 0)
        {
            throw new InvalidOperationException($"{name}必須是大於 0 的整數。");
        }

        return value;
    }

    private static double ParsePositive(TextBox textBox, string name)
    {
        if (!TryParseFlexible(textBox.Text, out var value) || !double.IsFinite(value) || value <= 0)
        {
            throw new InvalidOperationException($"{name}必須是有限且大於 0 的數值。");
        }

        return value;
    }

    private static double ParseNonNegative(TextBox textBox, string name)
    {
        if (!TryParseFlexible(textBox.Text, out var value) || !double.IsFinite(value) || value < 0)
        {
            throw new InvalidOperationException($"{name}必須是有限的非負數。");
        }

        return value;
    }

    private static bool TryParseFlexible(string value, out double result)
    {
        return double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out result)
            || double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private void ShowValidation(IEnumerable<string> messages)
    {
        ValidationTextBlock.Text = string.Join(Environment.NewLine, messages.Select(message => $"• {message}"));
        ValidationBorder.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => ValidationBorder.BringIntoView()));
    }

    private void HideValidation()
    {
        ValidationTextBlock.Text = string.Empty;
        ValidationBorder.Visibility = Visibility.Collapsed;
    }

    private static string ProfileToChinese(SpeedProfileType profile) => profile switch
    {
        SpeedProfileType.Trapezoidal => "梯形",
        SpeedProfileType.Triangular => "三角",
        _ => "瞬時"
    };

    private static string StateToChinese(TrainMotionState state) => state switch
    {
        TrainMotionState.Dwelling => "停站",
        TrainMotionState.Accelerating => "加速",
        TrainMotionState.Cruising => "巡航",
        TrainMotionState.Decelerating => "減速",
        TrainMotionState.Arriving => "到站",
        TrainMotionState.Turning => "折返",
        _ => state.ToString()
    };

    private static string FormatSeconds(double seconds) => $"{seconds:0.00} s";

    private static string FormatDuration(double totalSeconds)
    {
        var rounded = (long)Math.Round(totalSeconds);
        var hours = rounded / 3600;
        var minutes = rounded % 3600 / 60;
        var seconds = rounded % 60;
        return hours > 0 ? $"{hours}時 {minutes:00}分 {seconds:00}秒" : $"{minutes}分 {seconds:00}秒";
    }

    private static string FormatClock(double totalSeconds)
    {
        var rounded = (long)Math.Round(totalSeconds);
        var days = rounded / 86400;
        var remainder = rounded % 86400;
        var hours = remainder / 3600;
        var minutes = remainder % 3600 / 60;
        var seconds = remainder % 60;
        return days > 0
            ? $"+{days}日 {hours:00}:{minutes:00}:{seconds:00}"
            : $"{hours:00}:{minutes:00}:{seconds:00}";
    }

    private static void AddCanvasText(Canvas canvas, string text, double left, double top, double fontSize, Color color)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            Foreground = new SolidColorBrush(color)
        };
        Canvas.SetLeft(textBlock, left);
        Canvas.SetTop(textBlock, top);
        canvas.Children.Add(textBlock);
    }
}
