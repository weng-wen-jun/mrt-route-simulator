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
            Title = "讀取 MRT 模擬專案",
            Filter = "MRT 模擬專案 (*.mrtsim.json)|*.mrtsim.json|JSON 檔案 (*.json)|*.json",
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
            if (fileInfo.Length > MaximumProjectFileBytes)
            {
                throw new SimulationValidationException([$"存檔超過 {MaximumProjectFileBytes / 1_000_000} MB 讀取上限。"]);
            }

            var json = File.ReadAllText(dialog.FileName, Encoding.UTF8);
            var document = SimulationProjectFormat.Deserialize(json);
            PausePlayback();
            ClearResults();
            ApplyProjectDocument(document);
            HideValidation();
            StatusTextBlock.Text = $"專案已讀取：{dialog.FileName}；請按「計算並建立模擬」。";
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

    private SimulationProjectDocument CaptureProjectDocument()
    {
        StationDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        StationDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
        SpeedLimitDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        SpeedLimitDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
        ServicePatternDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        ServicePatternDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
        ServiceRunDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        ServiceRunDataGrid.CommitEdit(DataGridEditingUnit.Row, true);

        var defaultDwell = ParseNonNegative(DefaultDwellTextBox, "預設停站時間");
        var stations = StationRows.Select(row => new ProjectStation(
            row.StationId.Trim(),
            row.StationName.Trim(),
            row.DistanceFromPreviousKm * 1000,
            row.DwellTimeSeconds)).ToArray();
        var train = new ProjectTrainSettings(
            ParsePositive(MaxSpeedTextBox, "最高速度") / 3.6,
            ParsePositive(AccelerationTextBox, "加速度"),
            ParsePositive(DecelerationTextBox, "減速度"),
            defaultDwell,
            ParseNonNegative(OriginTurnaroundTextBox, "起點折返時間") * 60,
            ParseNonNegative(TerminalTurnaroundTextBox, "終點折返時間") * 60);
        var operations = new ProjectOperationalSettings(
            ParsePositive(JerkTextBox, "Jerk"),
            ParseNonNegative(CoastingRatioTextBox, "惰行比例"),
            ParseNonNegative(ApproachDistanceTextBox, "進站控制距離"),
            ParseNonNegative(ApproachSpeedTextBox, "進站控制速度") / 3.6,
            0.45,
            ParsePositive(TrainLengthTextBox, "車長"),
            ParsePositive(ServiceBrakeTextBox, "營運煞車減速度"),
            ParsePositive(EmergencyBrakeTextBox, "緊急煞車減速度"),
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
        double? headway = string.IsNullOrWhiteSpace(HeadwayTextBox.Text)
            ? null
            : ParsePositive(HeadwayTextBox, "指定班距") * 60;
        var profileMode = GetSelectedTag(OperationModeComboBox) == "Basic"
            ? OperationProfileMode.BasicPhysics
            : OperationProfileMode.RealisticOperations;
        var simulation = new ProjectRunSettings(
            ParsePositiveInteger(TrainCountTextBox, "列車數量"),
            headway,
            ParseClock(StartTimeTextBox.Text),
            GetPlaybackSpeed(),
            profileMode,
            ParseMovingBlockMode(),
            ParseBrakingEstimationMode());
        var servicePatterns = BuildServicePatterns()
            .Select(pattern => new ProjectServicePattern(
                pattern.PatternId,
                pattern.PatternName,
                pattern.Instructions.Select(instruction => new ProjectStationServiceInstruction(
                    instruction.StationId,
                    instruction.Mode,
                    instruction.SpeedLimitMetersPerSecond)).ToArray()))
            .ToArray();
        var serviceRuns = BuildServiceRunPlans()
            .Select(plan => new ProjectServiceRunPlan(
                plan.VehicleId,
                plan.ServiceNumber,
                plan.Direction,
                plan.ServiceClassId,
                plan.PatternId))
            .ToArray();

        return new SimulationProjectDocument(
            SimulationProjectFormat.CurrentSchemaVersion,
            RouteIdTextBox.Text.Trim(),
            RouteNameTextBox.Text.Trim(),
            stations,
            train,
            operations,
            speedLimits,
            simulation,
            servicePatterns,
            serviceRuns);
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
        foreach (var pattern in document.ServicePatterns ?? [])
        {
            foreach (var instruction in pattern.Instructions)
            {
                ServicePatternRows.Add(new ServicePatternInputRow
                {
                    PatternId = pattern.PatternId,
                    PatternName = pattern.PatternName,
                    StationId = instruction.StationId,
                    Mode = instruction.Mode == StationServiceMode.Pass ? "跨站" : "停站",
                    SpeedLimitKmh = instruction.SpeedLimitMetersPerSecond * 3.6
                });
            }
        }

        ServiceRunRows.Clear();
        foreach (var plan in document.ServiceRuns ?? [])
        {
            ServiceRunRows.Add(new ServiceRunInputRow
            {
                VehicleId = plan.VehicleId,
                ServiceNumber = plan.ServiceNumber,
                Direction = plan.Direction == TrainDirection.Outbound ? "下行" : "上行",
                ServiceClassId = plan.ServiceClassId,
                PatternId = plan.PatternId
            });
        }

        TrainCountTextBox.Text = document.Simulation.TrainCount.ToString(CultureInfo.InvariantCulture);
        HeadwayTextBox.Text = document.Simulation.HeadwaySeconds is { } headway
            ? FormatProjectNumber(headway / 60)
            : string.Empty;
        StartTimeTextBox.Text = TimeSpan.FromSeconds(document.Simulation.StartClockSeconds)
            .ToString("hh\\:mm\\:ss", CultureInfo.InvariantCulture);
        SelectComboBoxTag(
            OperationModeComboBox,
            document.Simulation.ProfileMode == OperationProfileMode.BasicPhysics ? "Basic" : "Realistic");
        SelectComboBoxTag(MovingBlockModeComboBox, document.Simulation.MovingBlockMode.ToString());
        SelectComboBoxTag(BrakingModeComboBox, document.Simulation.BrakingEstimationMode.ToString());
        if (!SelectComboBoxTag(PlaybackSpeedComboBox, FormatProjectNumber(document.Simulation.PlaybackSpeed)))
        {
            PlaybackSpeedComboBox.SelectedIndex = 0;
        }

        SpeedLimitWarningText.Text = string.Empty;
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
