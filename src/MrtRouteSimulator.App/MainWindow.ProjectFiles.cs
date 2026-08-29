using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

public partial class MainWindow
{
    private const long MaximumProjectFileBytes = SimulationProjectFormat.MaximumJsonCharacters * 4L;
    private const long MaximumFixedTimetableArchiveBytes = FixedTimetableArchiveFormat.MaximumJsonCharacters * 4L;

    private void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        HideValidation();
        try
        {
            var document = CaptureProjectDocument();
            var json = SimulationProjectFormat.Serialize(document);
            var dialog = new SaveFileDialog
            {
                Title = "儲存 MRT 模擬專案",
                Filter = "MRT 模擬專案 (*.mrtsim.json)|*.mrtsim.json|JSON 檔案 (*.json)|*.json",
                DefaultExt = ".mrtsim.json",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = $"{SanitizeFileName(document.RouteName)}.mrtsim.json"
            };
            if (dialog.ShowDialog(this) != true)
            {
                StatusTextBlock.Text = "已取消儲存專案；原檔案未變更。";
                return;
            }

            WriteProjectAtomically(dialog.FileName, json);
            StatusTextBlock.Text = $"專案已儲存：{dialog.FileName}";
        }
        catch (SimulationValidationException exception)
        {
            ShowValidation(exception.Errors);
            StatusTextBlock.Text = "專案資料驗證未通過，未寫入檔案。";
        }
        catch (InvalidOperationException exception)
        {
            ShowValidation([exception.Message]);
            StatusTextBlock.Text = "專案資料驗證未通過，未寫入檔案。";
        }
        catch (IOException exception)
        {
            ShowValidation([$"無法儲存專案：{exception.Message}"]);
            StatusTextBlock.Text = "儲存專案失敗。";
        }
        catch (UnauthorizedAccessException exception)
        {
            ShowValidation([$"沒有權限儲存專案：{exception.Message}"]);
            StatusTextBlock.Text = "儲存專案失敗。";
        }
    }

    private void LoadProject_Click(object sender, RoutedEventArgs e)
    {
        HideValidation();
        var dialog = new OpenFileDialog
        {
            Title = "讀取 MRT 模擬專案或固定時刻表",
            Filter = "MRT 模擬專案 (*.mrtsim.json)|*.mrtsim.json|固定時刻表封存 (*.mrttimetable.json)|*.mrttimetable.json|JSON 檔案 (*.json)|*.json",
            DefaultExt = ".mrtsim.json",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            StatusTextBlock.Text = "已取消讀取專案；目前設定未變更。";
            return;
        }

        try
        {
            var fileInfo = new FileInfo(dialog.FileName);
            if (fileInfo.Length > MaximumFixedTimetableArchiveBytes)
            {
                throw new SimulationValidationException([$"存檔超過 {MaximumFixedTimetableArchiveBytes / 1_000_000} MB 讀取上限。"]);
            }

            var json = File.ReadAllText(dialog.FileName, Encoding.UTF8);
            var archive = FixedTimetableArchiveFormat.IsFixedTimetableArchive(json)
                ? FixedTimetableArchiveFormat.Deserialize(json)
                : null;
            var document = archive?.Project ?? SimulationProjectFormat.Deserialize(json);
            PausePlayback();
            ClearResults();
            ApplyProjectDocument(document);
            if (archive is not null)
            {
                DisplayFixedTimetableArchive(archive);
            }
            HideValidation();
            StatusTextBlock.Text = archive is null
                ? $"專案已讀取：{dialog.FileName}；請按「計算並建立模擬」。"
                : $"固定時刻表已讀取：{dialog.FileName}；可直接查看完成模擬的凍結結果。";
        }
        catch (SimulationValidationException exception)
        {
            ShowValidation(exception.Errors);
            StatusTextBlock.Text = "存檔驗證未通過；目前設定未變更。";
        }
        catch (IOException exception)
        {
            ShowValidation([$"無法讀取專案：{exception.Message}"]);
            StatusTextBlock.Text = "讀取專案失敗；目前設定未變更。";
        }
        catch (UnauthorizedAccessException exception)
        {
            ShowValidation([$"沒有權限讀取專案：{exception.Message}"]);
            StatusTextBlock.Text = "讀取專案失敗；目前設定未變更。";
        }
    }

    private void ExportFixedTimetableArchive_Click(object sender, RoutedEventArgs e)
    {
        HideValidation();
        try
        {
            if (!_v2Enabled || _route is null || _v2World is null || _v2DispatchPlan is null
                || _activeSimulationProjectDocument is null)
            {
                throw new InvalidOperationException("請先建立 V2 寫實營運模擬。固定時刻表只會封存 SimulationWorld 的實際結果。");
            }

            if (!_v2World.IsComplete)
            {
                throw new InvalidOperationException("請先讓所有列車完成營運循環，再匯出固定時刻表。未完成的動態結果不可封存為固定時刻表。");
            }

            var entries = OperationsTimetable.Build(
                _route,
                _v2DispatchPlan,
                _plannedTimetableEvents,
                _v2World.Events);
            var archive = FixedTimetableArchiveFormat.Create(_activeSimulationProjectDocument, entries);
            var json = FixedTimetableArchiveFormat.Serialize(archive);
            var dialog = new SaveFileDialog
            {
                Title = "匯出完成後固定時刻表",
                Filter = "固定時刻表封存 (*.mrttimetable.json)|*.mrttimetable.json|JSON 檔案 (*.json)|*.json",
                DefaultExt = ".mrttimetable.json",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = $"{SanitizeFileName(archive.Project.RouteName)}-固定時刻表.mrttimetable.json"
            };
            if (dialog.ShowDialog(this) != true)
            {
                StatusTextBlock.Text = "已取消匯出固定時刻表；原檔案未變更。";
                return;
            }

            WriteProjectAtomically(dialog.FileName, json);
            StatusTextBlock.Text = $"固定時刻表已匯出：{dialog.FileName}";
        }
        catch (SimulationValidationException exception)
        {
            ShowValidation(exception.Errors);
            StatusTextBlock.Text = "固定時刻表驗證未通過，未寫入檔案。";
        }
        catch (InvalidOperationException exception)
        {
            ShowValidation([exception.Message]);
            StatusTextBlock.Text = "無法匯出固定時刻表。";
        }
        catch (IOException exception)
        {
            ShowValidation([$"無法匯出固定時刻表：{exception.Message}"]);
            StatusTextBlock.Text = "固定時刻表匯出失敗。";
        }
        catch (UnauthorizedAccessException exception)
        {
            ShowValidation([$"沒有權限匯出固定時刻表：{exception.Message}"]);
            StatusTextBlock.Text = "固定時刻表匯出失敗。";
        }
    }

    private SimulationProjectDocument CaptureProjectDocument()
    {
        StationDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        StationDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
        SpeedLimitDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        SpeedLimitDataGrid.CommitEdit(DataGridEditingUnit.Row, true);

        var defaultDwell = ParseNonNegative(DefaultDwellTextBox, "預設停站時間");
        var stations = StationRows.Select(row => new ProjectStation(
            row.StationId.Trim(),
            row.StationName.Trim(),
            row.DistanceFromPreviousKm * 1000,
            row.DwellTimeSeconds)).ToArray();
        var engineKind = GetSelectedTag(EngineModeComboBox) == "V1BasicPhysics"
            ? SimulationEngineKind.V1BasicPhysics
            : SimulationEngineKind.V2RealisticOperations;
        var resolvedDispatch = engineKind == SimulationEngineKind.V2RealisticOperations
            ? BuildResolvedDispatchPlan()
            : null;
        var baselineVehicle = resolvedDispatch is null ? null : ResolveBaselineVehicle(resolvedDispatch);
        var train = new ProjectTrainSettings(
            baselineVehicle?.MaxSpeedMetersPerSecond ?? ParsePositive(MaxSpeedTextBox, "最高速度") / 3.6,
            baselineVehicle?.AccelerationMetersPerSecondSquared ?? ParsePositive(AccelerationTextBox, "加速度"),
            baselineVehicle?.ServiceBrakeDecelerationMetersPerSecondSquared ?? ParsePositive(DecelerationTextBox, "減速度"),
            defaultDwell,
            ParseNonNegative(OriginTurnaroundTextBox, "起點折返時間") * 60,
            ParseNonNegative(TerminalTurnaroundTextBox, "終點折返時間") * 60);
        var operations = new ProjectOperationalSettings(
            baselineVehicle?.JerkMetersPerSecondCubed ?? ParsePositive(JerkTextBox, "Jerk"),
            ParseNonNegative(CoastingRatioTextBox, "惰行比例"),
            ParseNonNegative(ApproachDistanceTextBox, "進站控制距離"),
            ParseNonNegative(ApproachSpeedTextBox, "進站控制速度") / 3.6,
            0.45,
            baselineVehicle?.LengthMeters ?? ParsePositive(TrainLengthTextBox, "車長"),
            baselineVehicle?.ServiceBrakeDecelerationMetersPerSecondSquared ?? ParsePositive(ServiceBrakeTextBox, "營運煞車減速度"),
            baselineVehicle?.EmergencyBrakeDecelerationMetersPerSecondSquared ?? ParsePositive(EmergencyBrakeTextBox, "緊急煞車減速度"),
            ParseNonNegative(ReactionTimeTextBox, "控制反應時間"),
            0.8,
            3,
            25,
            15);
        var speedLimits = SpeedLimitRows.Select((row, index) => new ProjectSpeedLimit(
            row.StartKm * 1000,
            row.EndKm * 1000,
            row.LimitKmh / 3.6,
            ParseSpeedLimitDirection(row.Direction, index + 1),
            row.Note.Trim())).ToArray();
        var profileMode = GetSelectedTag(OperationModeComboBox) == "Basic"
            ? OperationProfileMode.BasicPhysics
            : OperationProfileMode.RealisticOperations;
        double? headway = resolvedDispatch is not null
            ? GetMinimumPlannedIntervalSeconds(resolvedDispatch)
            : string.IsNullOrWhiteSpace(HeadwayTextBox.Text)
                ? null
                : ParsePositive(HeadwayTextBox, "指定班距") * 60;
        var simulation = new ProjectRunSettings(
            resolvedDispatch?.Runs.Count ?? ParsePositiveInteger(TrainCountTextBox, "列車數量"),
            headway,
            resolvedDispatch?.ScheduleAnchorTime.TotalSeconds ?? ParseClock(StartTimeTextBox.Text),
            GetPlaybackSpeed(),
            profileMode,
            ParseMovingBlockMode(),
            ParseBrakingEstimationMode(),
            engineKind);
        var vehicleTypes = VehicleTypeRows.Select(row => new ProjectVehicleType(
            row.Id, row.Name, row.LengthMeters, row.MaxSpeedKmh / 3.6, row.Acceleration,
            row.ServiceBrake, row.EmergencyBrake, row.Jerk, row.TractionDecay, row.CoastingDeceleration,
            EmptyToNull(row.DefaultStopPatternId))).ToArray();
        var serviceTypes = ServiceTypeRows.Select(row => new ProjectServiceType(
            row.Id, row.Name, row.ColorHex, row.RunPrefix, EmptyToNull(row.DefaultStopPatternId),
            EmptyToNull(row.DefaultVehicleTypeId), row.Priority, row.CanRequestOvertake,
            SplitIds(row.PreferredPlatformIds))).ToArray();
        var stopPatterns = BuildStopPatternDefinitions().Select(pattern => new ProjectStopPattern(
            pattern.Id,
            pattern.DisplayName,
            pattern.Instructions.Select(instruction => new ProjectStopPatternInstruction(
                instruction.StationId, instruction.Action, instruction.DwellTimeSeconds,
                instruction.PassingSpeedLimitMetersPerSecond)).ToArray())).ToArray();
        var dispatch = new ProjectDispatchPlan(
            _dispatchPlanningMode == "手動班表" ? DispatchPlanningMode.ManualTimetable : DispatchPlanningMode.SimpleHeadway,
            _vehicleAssignmentMode == "全部指定" ? VehicleAssignmentMode.ExplicitOnly : VehicleAssignmentMode.Automatic,
            HeadwayPlanRows.Select(row => new ProjectHeadwayPlan(
                ParseDirection(row.Direction), ParseTime(row.FirstDeparture, "首班時間").TotalSeconds,
                TimeSpan.FromMinutes(row.HeadwayMinutes).TotalSeconds, row.RunCount, row.ServiceTypeId,
                EmptyToNull(row.VehicleTypeId), EmptyToNull(row.StopPatternId), EmptyToNull(row.OriginPlatformId),
                EmptyToNull(row.VehicleId), row.ContinueAfterTerminal)).ToArray(),
            ManualTimetableRows.Select(row => new ProjectManualTimetableRow(
                ParseTime(row.PlannedDeparture, "發車時間").TotalSeconds, ParseDirection(row.Direction), row.ServiceTypeId,
                EmptyToNull(row.VehicleTypeId), EmptyToNull(row.StopPatternId), EmptyToNull(row.OriginPlatformId),
                EmptyToNull(row.VehicleId), EmptyToNull(row.ServiceRunId), row.ContinueAfterTerminal,
                EmptyToNull(row.ContinuationServiceRunId))).ToArray());
        var infrastructure = CaptureProjectInfrastructure(stations);

        return new SimulationProjectDocument(
            SimulationProjectFormat.CurrentSchemaVersion,
            RouteIdTextBox.Text.Trim(),
            RouteNameTextBox.Text.Trim(),
            stations,
            train,
            operations,
            speedLimits,
            simulation,
            vehicleTypes,
            serviceTypes,
            stopPatterns,
            dispatch,
            infrastructure);
    }

    private void ApplyProjectDocument(SimulationProjectDocument document)
    {
        RouteIdTextBox.Text = document.RouteId;
        RouteNameTextBox.Text = document.RouteName;
        StationRows.Clear();
        foreach (var station in document.Stations)
        {
            StationRows.Add(new StationInputRow
            {
                StationId = station.StationId,
                StationName = station.StationName,
                DistanceFromPreviousKm = station.DistanceFromPreviousMeters / 1000,
                DwellTimeSeconds = station.DwellTimeSeconds
            });
        }

        MaxSpeedTextBox.Text = FormatProjectNumber(document.Train.MaxSpeedMetersPerSecond * 3.6);
        AccelerationTextBox.Text = FormatProjectNumber(document.Train.AccelerationMetersPerSecondSquared);
        DecelerationTextBox.Text = FormatProjectNumber(document.Train.DecelerationMetersPerSecondSquared);
        DefaultDwellTextBox.Text = FormatProjectNumber(document.Train.DefaultDwellTimeSeconds);
        OriginTurnaroundTextBox.Text = FormatProjectNumber(document.Train.OriginTurnaroundTimeSeconds / 60);
        TerminalTurnaroundTextBox.Text = FormatProjectNumber(document.Train.TerminalTurnaroundTimeSeconds / 60);

        JerkTextBox.Text = FormatProjectNumber(document.Operations.JerkMetersPerSecondCubed);
        CoastingRatioTextBox.Text = FormatProjectNumber(document.Operations.CoastingRatio);
        ApproachDistanceTextBox.Text = FormatProjectNumber(document.Operations.ApproachDistanceMeters);
        ApproachSpeedTextBox.Text = FormatProjectNumber(document.Operations.ApproachSpeedMetersPerSecond * 3.6);
        TrainLengthTextBox.Text = FormatProjectNumber(document.Operations.TrainLengthMeters);
        ReactionTimeTextBox.Text = FormatProjectNumber(document.Operations.ControlReactionTimeSeconds);
        ServiceBrakeTextBox.Text = FormatProjectNumber(document.Operations.ServiceBrakingMetersPerSecondSquared);
        EmergencyBrakeTextBox.Text = FormatProjectNumber(document.Operations.EmergencyBrakingMetersPerSecondSquared);

        SpeedLimitRows.Clear();
        foreach (var limit in document.SpeedLimits)
        {
            SpeedLimitRows.Add(new SpeedLimitInputRow
            {
                StartKm = limit.StartPositionMeters / 1000,
                EndKm = limit.EndPositionMeters / 1000,
                LimitKmh = limit.LimitMetersPerSecond * 3.6,
                Direction = SpeedLimitDirectionToChinese(limit.Direction),
                Note = limit.Note
            });
        }

        ServicePatternRows.Clear();

        TrainCountTextBox.Text = document.Simulation.TrainCount.ToString(CultureInfo.InvariantCulture);
        HeadwayTextBox.Text = document.Simulation.HeadwaySeconds is { } headway
            ? FormatProjectNumber(headway / 60)
            : string.Empty;
        StartTimeTextBox.Text = TimeSpan.FromSeconds(document.Simulation.StartClockSeconds)
            .ToString("hh\\:mm\\:ss", CultureInfo.InvariantCulture);
        SelectComboBoxTag(
            OperationModeComboBox,
            document.Simulation.ProfileMode == OperationProfileMode.BasicPhysics ? "Basic" : "Realistic");
        SelectComboBoxTag(EngineModeComboBox, document.Simulation.EngineKind.ToString());
        SelectComboBoxTag(MovingBlockModeComboBox, document.Simulation.MovingBlockMode.ToString());
        SelectComboBoxTag(BrakingModeComboBox, document.Simulation.BrakingEstimationMode.ToString());
        if (!SelectComboBoxTag(PlaybackSpeedComboBox, FormatProjectNumber(document.Simulation.PlaybackSpeed)))
        {
            PlaybackSpeedComboBox.SelectedIndex = 0;
        }

        SpeedLimitWarningText.Text = string.Empty;
        ApplyExtendedProjectInputs(document);
        DrawRoute();
        DrawSpeedProfile();
    }

    private BrakingEstimationMode ParseBrakingEstimationMode() =>
        GetSelectedTag(BrakingModeComboBox) == "Emergency"
            ? BrakingEstimationMode.Emergency
            : BrakingEstimationMode.Service;

    private static bool SelectComboBoxTag(ComboBox comboBox, string tag)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return true;
            }
        }

        return false;
    }

    private static string FormatProjectNumber(double value) =>
        value.ToString("0.########", CultureInfo.InvariantCulture);

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "MRT模擬專案" : cleaned;
    }

    private static void WriteProjectAtomically(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new IOException("儲存位置無效。");
        }

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
