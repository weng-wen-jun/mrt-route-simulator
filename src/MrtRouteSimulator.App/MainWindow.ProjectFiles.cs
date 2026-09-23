using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

public partial class MainWindow
{
    private const long MaximumProjectFileBytes = SimulationProjectFormat.MaximumJsonCharacters * 4L;
    private const long MaximumFixedTimetableArchiveBytes = FixedTimetableArchiveFormat.MaximumJsonCharacters * 4L;
    private string? _currentProjectFilePath;

    private void UpdateFixedTimetableArchiveExportState()
    {
        if (ExportFixedTimetableArchiveMenuItem is null)
        {
            return;
        }

        var topologyLoaded = _activeTopologyProjectDocument is not null;
        ExportFixedTimetableArchiveMenuItem.IsEnabled = !topologyLoaded;
        ExportFixedTimetableArchiveMenuItem.ToolTip = topologyLoaded
            ? "Schema 8 拓撲專案目前以拓撲結果與 Schema 8 存檔為準；固定時刻表封存僅支援 legacy Schema 7 動態模擬。"
            : "匯出已完成的 legacy Schema 7 動態模擬結果。";
    }

    private void SetCurrentProjectFile(string? filePath)
    {
        _currentProjectFilePath = string.IsNullOrWhiteSpace(filePath)
            ? null
            : Path.GetFullPath(filePath);
        var baseTitle = $"MRT 路線進出站時間模擬器 {ProductVersion.Current}";
        if (_currentProjectFilePath is null)
        {
            Title = baseTitle;
            CurrentProjectFileTextBlock.Text = "目前存檔：尚未讀取（示範資料）";
            CurrentProjectFileTextBlock.ToolTip = "目前使用示範資料，尚未從存檔讀取。";
            return;
        }

        var fileName = Path.GetFileName(_currentProjectFilePath);
        Title = $"{baseTitle} — {fileName}";
        CurrentProjectFileTextBlock.Text = $"目前存檔：{fileName}";
        CurrentProjectFileTextBlock.ToolTip = _currentProjectFilePath;
    }

    private void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        HideValidation();
        try
        {
            // Schema 8 是唯一 UI editable / persistence format。尚未建立 topology 時，
            // 舊線性欄位只會被快速轉成一次性的 Schema 8 draft，絕不再輸出 Schema 7。
            var topologyDocument = _activeTopologyProjectDocument
                ?? TopologyProjectFactory.CreateLinearDraft(CaptureProjectDocument());
            var json = TopologyProjectFormat.Serialize(topologyDocument);
            var projectName = topologyDocument.ProjectName;
            var dialog = new SaveFileDialog
            {
                Title = "儲存 MRT 模擬專案",
                Filter = "MRT 模擬專案 (*.mrtsim.json)|*.mrtsim.json|JSON 檔案 (*.json)|*.json",
                DefaultExt = ".mrtsim.json",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = $"{SanitizeFileName(projectName)}.mrtsim.json"
            };
            if (dialog.ShowDialog(this) != true)
            {
                StatusTextBlock.Text = "已取消儲存專案；原檔案未變更。";
                return;
            }

            WriteProjectAtomically(dialog.FileName, json);
            SetCurrentProjectFile(dialog.FileName);
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

    private async void LoadProject_Click(object sender, RoutedEventArgs e)
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

        ProjectLoadProgressWindow? progressWindow = null;
        try
        {
            var fileInfo = new FileInfo(dialog.FileName);
            var isFixedTimetableArchive = fileInfo.Name.EndsWith(
                ".mrttimetable.json",
                StringComparison.OrdinalIgnoreCase);
            var maximumBytes = isFixedTimetableArchive
                ? MaximumFixedTimetableArchiveBytes
                : MaximumProjectFileBytes;
            if (fileInfo.Length > maximumBytes)
            {
                var formatLabel = isFixedTimetableArchive ? "固定時刻表封存" : "模擬專案";
                throw new SimulationValidationException([
                    $"{formatLabel}存檔超過 {maximumBytes / 1_000_000} MB 讀取上限。"]);
            }

            progressWindow = new ProjectLoadProgressWindow { Owner = this };
            progressWindow.Show();
            IsEnabled = false;
            IProgress<(double Percentage, string Message)> progress = new Progress<(double Percentage, string Message)>(update =>
                progressWindow.UpdateProgress(update.Percentage, update.Message));
            var json = await ReadProjectFileAsync(dialog.FileName, fileInfo.Length, progress);
            progress.Report((35, "正在辨識存檔格式…"));
            if (await Task.Run(() => IsSchema8TopologyProject(json)))
            {
                progress.Report((45, "正在驗證拓撲專案…"));
                var topologyDocument = await Task.Run(() => TopologyProjectFormat.Deserialize(json));
                var legacyPortItems = await Task.Run(() => LegacyPortMigration.FindUnspecifiedEdges(topologyDocument));
                var migratedLegacyPorts = false;
                var keptLegacyCompatibility = false;
                if (legacyPortItems.Count > 0)
                {
                    progressWindow.Hide();
                    IsEnabled = true;
                    var migration = new LegacyPortMigrationDialog(topologyDocument) { Owner = this };
                    if (migration.ShowDialog() != true || migration.Result is null)
                    {
                        StatusTextBlock.Text = "已取消接軌側別遷移；目前設定未變更。";
                        return;
                    }

                    topologyDocument = migration.Result;
                    migratedLegacyPorts = !migration.KeptCompatibility;
                    keptLegacyCompatibility = migration.KeptCompatibility;
                    IsEnabled = false;
                    progressWindow.Show();
                }
                progress.Report((60, "正在建立模擬與計畫時間軸…"));
                var prepared = await Task.Run(() => PrepareTopologyProjectForPlayback(topologyDocument, progress));
                progress.Report((92, "正在套用讀取結果…"));
                ApplyPreparedTopologyProjectForPlayback(prepared, lockLegacyInputs: true);
                HideValidation();
                SetCurrentProjectFile(dialog.FileName);
                var migrationStatus = migratedLegacyPorts
                    ? "；已套用使用者確認的實體接軌側別"
                    : keptLegacyCompatibility
                        ? "；保留未指定側別的相容讀取，尚未宣稱方向已確認"
                        : "";
                StatusTextBlock.Text = $"拓撲專案已讀取：{dialog.FileName}{migrationStatus}；可直接播放、查看路線圖並原樣存檔。快速起稿欄已收合。";
                return;
            }

            progress.Report((45, "正在驗證存檔內容…"));
            var archive = await Task.Run(() => FixedTimetableArchiveFormat.IsFixedTimetableArchive(json)
                ? FixedTimetableArchiveFormat.Deserialize(json)
                : null);
            var document = archive?.Project ?? await Task.Run(() => SimulationProjectFormat.Deserialize(json));
            if (archive is null)
            {
                progress.Report((60, "正在將舊格式轉為拓撲並建立模擬…"));
                var prepared = await Task.Run(() => PrepareTopologyProjectForPlayback(
                    TopologyProjectFactory.CreateLinearDraft(document),
                    progress));
                progress.Report((92, "正在套用讀取結果…"));
                ApplyPreparedTopologyProjectForPlayback(prepared, lockLegacyInputs: true);
                HideValidation();
                SetCurrentProjectFile(dialog.FileName);
                StatusTextBlock.Text = $"已將舊格式版本 {document.SchemaVersion} 專案轉為拓撲專案；請由專案工作區繼續編輯並另存。";
                return;
            }
            progress.Report((75, "正在建立固定時刻表結果…"));
            var archiveRows = await PrepareFixedTimetableArchiveRowsAsync(archive, progress);
            PausePlayback();
            ClearResults();
            ApplyProjectDocument(document);
            ApplyFixedTimetableArchive(archive, archiveRows);
            HideValidation();
            SetCurrentProjectFile(dialog.FileName);
            StatusTextBlock.Text = $"固定時刻表已讀取：{dialog.FileName}；已切換至進出站時刻表結果頁。";
        }
        catch (SimulationValidationException exception)
        {
            ShowValidation(exception.Errors);
            StatusTextBlock.Text = "存檔驗證未通過；目前設定未變更。";
        }
        catch (JsonException exception)
        {
            ShowValidation([$"專案 JSON 格式無效：{exception.Message}"]);
            StatusTextBlock.Text = "讀取專案失敗；目前設定未變更。";
        }
        catch (InvalidOperationException exception)
        {
            ShowValidation([$"專案模擬準備失敗：{exception.Message}"]);
            StatusTextBlock.Text = "專案未能建立模擬；目前設定未變更。";
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
        finally
        {
            IsEnabled = true;
            progressWindow?.Close();
        }
    }

    private static async Task<string> ReadProjectFileAsync(
        string filePath,
        long fileLength,
        IProgress<(double Percentage, string Message)> progress)
    {
        const int bufferSize = 80 * 1024;
        await using var file = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var bytes = new MemoryStream(fileLength > int.MaxValue ? 0 : (int)fileLength);
        var buffer = new byte[bufferSize];
        long totalRead = 0;
        int bytesRead;
        while ((bytesRead = await file.ReadAsync(buffer)) > 0)
        {
            await bytes.WriteAsync(buffer.AsMemory(0, bytesRead));
            totalRead += bytesRead;
            var percentage = fileLength == 0 ? 30 : totalRead * 30d / fileLength;
            progress.Report((percentage, $"正在讀取檔案… {percentage:0}%"));
        }

        bytes.Position = 0;
        using var reader = new StreamReader(bytes, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync();
    }

    private static bool IsSchema8TopologyProject(string json)
    {
        using var root = JsonDocument.Parse(json);
        return root.RootElement.ValueKind == JsonValueKind.Object
            && root.RootElement.TryGetProperty("schemaVersion", out var version)
            && version.ValueKind == JsonValueKind.Number
            && version.TryGetInt32(out var schemaVersion)
            && schemaVersion == TopologyProjectFormat.CurrentSchemaVersion;
    }

    private void ExportFixedTimetableArchive_Click(object sender, RoutedEventArgs e)
    {
        HideValidation();
        try
        {
            if (_activeTopologyProjectDocument is not null)
            {
                throw new InvalidOperationException(
                    "目前是 Schema 8 拓撲專案；固定時刻表封存僅支援 legacy Schema 7 動態模擬，請使用 Schema 8 存檔或結果頁匯出。");
            }

            if (!_v2Enabled || _route is null || _v2World is null || _v2DispatchPlan is null
                || _activeSimulationProjectDocument is null)
            {
                throw new InvalidOperationException("請先建立 V2 寫實營運模擬。固定時刻表只會封存模擬世界的實際結果。");
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
            baselineVehicle?.JerkMetersPerSecondCubed ?? 0.65,
            ParseNonNegative(CoastingRatioTextBox, "惰行比例"),
            ParseNonNegative(ApproachDistanceTextBox, "進站控制距離"),
            ParseNonNegative(ApproachSpeedTextBox, "進站控制速度") / 3.6,
            0.45,
            baselineVehicle?.LengthMeters ?? 92,
            baselineVehicle?.ServiceBrakeDecelerationMetersPerSecondSquared ?? 0.9,
            baselineVehicle?.EmergencyBrakeDecelerationMetersPerSecondSquared ?? 1.3,
            ParseNonNegative(ReactionTimeTextBox, "控制反應時間"),
            0.8,
            3,
            25,
            15);
        // Schema 8 quick builder 的線性表只提供一次性的 station-distance 起稿；里程
        // speed-limit rows 不再是可保存的 physical authority。建立 topology 後，速限
        // 一律由工作區以 TrackEdgeId + edge-local offset 編輯。
        var speedLimits = Array.Empty<ProjectSpeedLimit>();
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
        SetQuickBuilderState(locked: false, collapsed: false);
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

        CoastingRatioTextBox.Text = FormatProjectNumber(document.Operations.CoastingRatio);
        ApproachDistanceTextBox.Text = FormatProjectNumber(document.Operations.ApproachDistanceMeters);
        ApproachSpeedTextBox.Text = FormatProjectNumber(document.Operations.ApproachSpeedMetersPerSecond * 3.6);
        ReactionTimeTextBox.Text = FormatProjectNumber(document.Operations.ControlReactionTimeSeconds);

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
        _suppressEngineModeSelectionChanged = true;
        try
        {
            SelectComboBoxTag(EngineModeComboBox, document.Simulation.EngineKind.ToString());
        }
        finally
        {
            _suppressEngineModeSelectionChanged = false;
        }
        ApplyEngineModeUiState();
        SelectComboBoxTag(MovingBlockModeComboBox, document.Simulation.MovingBlockMode.ToString());
        SelectComboBoxTag(BrakingModeComboBox, document.Simulation.BrakingEstimationMode.ToString());
        if (!SelectComboBoxTag(PlaybackSpeedComboBox, FormatProjectNumber(document.Simulation.PlaybackSpeed)))
        {
            PlaybackSpeedComboBox.SelectedIndex = 0;
        }

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
