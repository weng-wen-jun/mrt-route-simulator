using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Controls;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

public partial class MainWindow
{
    private void WorkspaceTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, WorkspaceTabControl) || !_v2Enabled)
        {
            return;
        }

        UpdateV2PlaybackView(force: true);
    }

    private void UpdateV2PlaybackView(bool force = false)
    {
        if (!_v2Enabled || _playbackWorker is null)
        {
            return;
        }

        if (_playbackWorker.TryReadLatestFrame(out var latestFrame)
            && latestFrame is not null
            && latestFrame.GenerationId == _playbackWorker.GenerationId)
        {
            _latestPlaybackFrame = latestFrame;
        }

        var frame = _latestPlaybackFrame;
        if (frame is null)
        {
            return;
        }

        var frameChanged = frame.Sequence != _lastRenderedPlaybackFrameSequence;
        if (!frameChanged && !force)
        {
            return;
        }

        var renderTimer = Stopwatch.StartNew();
        SimulationResultDelta? delta = null;
        if (frameChanged)
        {
            delta = _resultAccumulator.Advance(frame);
            _lastAccumulatorMilliseconds = delta.AccumulatorMilliseconds;
            if (delta.WasReset)
            {
                EventRows.Clear();
                _lastRenderedEventCount = 0;
                SafetyRows.Clear();
                _timetableDirty = true;
                _segmentDetailsDirty = true;
                _comparisonDirty = true;
                _resourceOccupancyDirty = true;
                _intervalStatisticsDirty = true;
                _actualSummaryDirty = true;
            }

            if (delta.HasNewEvents)
            {
                if (delta.NewEvents.Any(IsTimetableEvent))
                {
                    _timetableDirty = true;
                    _comparisonDirty = true;
                }

                if (delta.NewEvents.Any(item => item.EventType is SimulationEventType.Arrival or SimulationEventType.Departure))
                {
                    _actualSummaryDirty = true;
                }

                if (delta.NewEvents.Any(item => item.EventType is SimulationEventType.RouteReserved or SimulationEventType.RouteReleased))
                {
                    _resourceOccupancyDirty = true;
                }
            }

            if (delta.HasNewTrajectory)
            {
                _segmentDetailsDirty = true;
                _intervalStatisticsDirty = true;
            }

            if (delta.HasNewSafety)
            {
                _safetyHistoryDirty = true;
            }

            _playbackTimeSeconds = frame.SimulationTimeSeconds;
        }

        var selectedTab = WorkspaceTabControl.SelectedItem;
        var now = Stopwatch.GetTimestamp();
        var refreshDynamicRows = frameChanged || force;
        if (refreshDynamicRows && RefreshDue(ref _lastTrainRenderTimestamp, 50, force))
        {
            var trainRows = frame.Trains
                .Where(state => state.Phase != OperationalPhase.OutOfService)
                .Select(state => new CurrentTrainRow(
                    state.VehicleId.Replace("Vehicle ", "V", StringComparison.Ordinal) + $"｜{state.ServiceClassId}",
                    state.IsActive ? DirectionToChinese(state.Direction) : "—",
                    state.IsActive ? PhaseToChinese(state.Phase) : "待發",
                    frame.GetTrainCenterPosition(state.VehicleId) is { } center
                        && _stationChainageProjection?.ToChainage(center) is { } centerChainage
                            ? $"{centerChainage / 1000:0.000}" : "—",
                    $"{state.SpeedMetersPerSecond * 3.6:0.#}",
                    GetV2CurrentLocation(state),
                    state.NextStationId ?? "—"))
                .ToArray();
            ApplyRowsByKey(CurrentTrainRows, trainRows, row => row.TrainId);
            SimulationClockText.Text = TrajectoryAnalysis.FormatClock(_startClockSeconds + _playbackTimeSeconds);
        }

        if (refreshDynamicRows && ReferenceEquals(selectedTab, SimulationTabItem))
        {
            if (RefreshDue(ref _lastRouteRenderTimestamp, 33, force))
            {
                var routeRender = Stopwatch.StartNew();
                DrawV2Route(frame.GetSnapshot());
                _lastRouteRenderMilliseconds = routeRender.Elapsed.TotalMilliseconds;
            }
            if (RefreshDue(ref _lastChartRenderTimestamp, 250, force))
            {
                DrawV2SpeedProfile();
            }
        }

        if (ReferenceEquals(selectedTab, SafetyTabItem))
        {
            if (refreshDynamicRows && RefreshDue(ref _lastSafetyRenderTimestamp, 100, force))
            {
                var safetyRows = frame.CurrentSafety
                    .Where(MatchesSafetyFilters)
                    .Select(observation => new SafetyRow(
                        $"{ShortVehicle(observation.FollowerVehicleId)} → {ShortVehicle(observation.LeaderVehicleId)}",
                        observation.TrackId,
                        $"{observation.FollowerFrontPositionMeters / 1000:0.00}",
                        $"{observation.LeaderRearPositionMeters / 1000:0.00}",
                        $"{observation.ActualGapMeters:0.0}",
                        $"{observation.DynamicSafetyDistanceMeters:0.0}",
                        $"{observation.ObstacleBrakingDemandMeters:0.0}",
                        $"{observation.SafetyMarginMeters:0.0}",
                        SafetyStatusToChinese(observation.Status)))
                    .ToArray();
                ApplyRowsByKey(SafetyRows, safetyRows,
                    row => $"{row.Pair}|{row.Track}");
            }

            if (frame.Events.Count != _lastRenderedEventCount || force)
            {
                AppendVisibleEventRows(frame);
            }

            if ((_safetyHistoryDirty || force) && RefreshDue(ref _lastSafetySummaryTimestamp, 1000, force))
            {
                RefreshPairFilter(frame.SafetyHistory.Where(MatchesSafetyFilters));
                UpdateSafetySummary();
                _safetyHistoryDirty = false;
            }

            if (refreshDynamicRows && RefreshDue(ref _lastChartRenderTimestamp, 250, force))
            {
                DrawSafetyDistanceChart();
            }
        }

        if (ReferenceEquals(selectedTab, ResultsTabItem) && (_timetableDirty || force))
        {
            PopulateV3Timetable();
            _timetableDirty = false;
        }

        if (ReferenceEquals(selectedTab, SegmentTabItem)
            && (_segmentDetailsDirty || force)
            && RefreshDue(ref _lastSegmentRefreshTimestamp, 1000, force))
        {
            PopulateV3SegmentDetails();
            _segmentDetailsDirty = false;
        }

        if (ReferenceEquals(selectedTab, ComparisonTabItem)
            && (_comparisonDirty || force)
            && (!_isV2PlaybackPlaying || frame.IsComplete || force || RefreshDue(ref _lastComparisonRefreshTimestamp, 1000, force)))
        {
            PopulateV1V2Comparison();
            _comparisonDirty = false;
            _lastComparisonRefreshTimestamp = now;
        }

        if (ReferenceEquals(selectedTab, ResourceTabItem)
            && (_resourceOccupancyDirty || force || RefreshDue(ref _lastResourceRefreshTimestamp, 1000, false)))
        {
            PopulateResourceOccupancy();
            _resourceOccupancyDirty = false;
            _lastResourceRefreshTimestamp = now;
        }

        if (ReferenceEquals(selectedTab, IntervalStatisticsTabItem)
            && (_intervalStatisticsDirty || force)
            && RefreshDue(ref _lastIntervalRefreshTimestamp, 1000, force))
        {
            PopulateIntervalStatistics();
            _intervalStatisticsDirty = false;
        }

        if (ReferenceEquals(selectedTab, DiagramTabItem)
            && refreshDynamicRows
            && RefreshDue(ref _lastChartRenderTimestamp, 250, force))
        {
            DrawTimeDistanceDiagram();
        }

        if (_actualSummaryDirty && (delta?.HasNewEvents == true || force || delta?.WasReset == true))
        {
            UpdateV2ActualSummary();
            _actualSummaryDirty = false;
        }

        _lastRenderedPlaybackFrameSequence = frame.Sequence;
        renderTimer.Stop();
        _lastUiRenderMilliseconds = renderTimer.Elapsed.TotalMilliseconds;
        PlaybackStatusText.ToolTip =
            $"模擬推進 {frame.Performance.SimulationAdvanceMilliseconds:0.0} ms；frame 建立 {frame.Performance.FrameBuildMilliseconds:0.0} ms；"
            + $"frame 發布 {frame.Performance.FramePublishMilliseconds:0.0} ms；結果累積 {_lastAccumulatorMilliseconds:0.0} ms；"
            + $"UI 刷新 {_lastUiRenderMilliseconds:0.0} ms；略過 {frame.Performance.FrameDropCount} 幀；"
            + $"路線圖 {_lastRouteRenderMilliseconds:0.0} ms／固定配線重建 {_routeStaticRebuildCount} 次；"
            + $"軌跡 {frame.Performance.TrajectoryCount:N0} 筆；安全觀測 {frame.Performance.SafetyHistoryCount:N0} 筆。";
    }

    private void AppendVisibleEventRows(PlaybackFrame frame)
    {
        if (frame.Events.Count < _lastRenderedEventCount)
        {
            EventRows.Clear();
            _lastRenderedEventCount = 0;
        }

        var firstIndex = Math.Max(_lastRenderedEventCount, frame.Events.Count - 300);
        for (var index = firstIndex; index < frame.Events.Count; index++)
        {
            var simulationEvent = frame.Events[index];
            EventRows.Insert(0, new EventRow(
                TrajectoryAnalysis.FormatClock(_startClockSeconds + simulationEvent.SimulationTimeSeconds),
                EventTypeToChinese(simulationEvent.EventType),
                string.IsNullOrWhiteSpace(simulationEvent.VehicleId) ? "—" : ShortVehicle(simulationEvent.VehicleId),
                $"{simulationEvent.PositionMeters / 1000:0.00}",
                simulationEvent.Message));
            if (EventRows.Count > 300)
            {
                EventRows.RemoveAt(EventRows.Count - 1);
            }
        }

        _lastRenderedEventCount = frame.Events.Count;
    }

    private static void ApplyRowsByKey<TRow, TKey>(
        ObservableCollection<TRow> rows,
        IReadOnlyList<TRow> desiredRows,
        Func<TRow, TKey> keySelector)
        where TKey : notnull
    {
        var keyComparer = EqualityComparer<TKey>.Default;
        var desiredKeys = desiredRows.Select(keySelector).ToHashSet(keyComparer);
        for (var index = rows.Count - 1; index >= 0; index--)
        {
            if (!desiredKeys.Contains(keySelector(rows[index])))
            {
                rows.RemoveAt(index);
            }
        }

        var rowComparer = EqualityComparer<TRow>.Default;
        for (var index = 0; index < desiredRows.Count; index++)
        {
            var desired = desiredRows[index];
            var key = keySelector(desired);
            var existingIndex = index < rows.Count && keyComparer.Equals(keySelector(rows[index]), key)
                ? index
                : -1;
            if (existingIndex < 0)
            {
                for (var candidate = index + 1; candidate < rows.Count; candidate++)
                {
                    if (keyComparer.Equals(keySelector(rows[candidate]), key))
                    {
                        existingIndex = candidate;
                        break;
                    }
                }
            }

            if (existingIndex < 0)
            {
                rows.Insert(index, desired);
                continue;
            }

            if (existingIndex != index)
            {
                rows.Move(existingIndex, index);
            }

            if (!rowComparer.Equals(rows[index], desired))
            {
                rows[index] = desired;
            }
        }

        while (rows.Count > desiredRows.Count)
        {
            rows.RemoveAt(rows.Count - 1);
        }
    }

    private static bool RefreshDue(ref long lastTimestamp, double minimumIntervalMilliseconds, bool force)
    {
        var now = Stopwatch.GetTimestamp();
        if (!force && lastTimestamp != 0
            && (now - lastTimestamp) < minimumIntervalMilliseconds * Stopwatch.Frequency / 1000)
        {
            return false;
        }

        lastTimestamp = now;
        return true;
    }

    private static bool IsTimetableEvent(SimulationEvent item) => item.EventType is
        SimulationEventType.Arrival or
        SimulationEventType.Departure or
        SimulationEventType.StationPassed or
        SimulationEventType.ServiceEnded;
}
