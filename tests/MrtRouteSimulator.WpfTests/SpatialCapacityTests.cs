using System.IO;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

internal static class SpatialCapacityTests
{
    public static void Run(string root)
    {
        var samplePath = Directory.EnumerateFiles(Path.Combine(root, "samples", "小型"), "*.mrtsim.json")
            .FirstOrDefault(path => Path.GetFileName(path).Contains("三站", StringComparison.Ordinal))
            ?? Directory.EnumerateFiles(Path.Combine(root, "samples", "小型"), "*.mrtsim.json").First();
        var sample = TopologyProjectFormat.Deserialize(File.ReadAllText(samplePath));

        VerifyUnconfiguredMessage(sample);
        VerifyEngineValues(sample);
        VerifyEditorTransactionAndSpeedUnits(sample);
        VerifyPointSelectionPreservesInput(sample);
        VerifyCompactEditorLayout(sample);
        VerifyScenarioAndSensitivity(sample);
        VerifyTopologyEditorPreservesCapacity(sample);
        VerifyHandbookFiles(root);
        Console.WriteLine("[通過] Schema 8 容量參考點結果、未配置提示、交易取消／套用、輸入保存與工作區保留");
    }

    private static void VerifyHandbookFiles(string root)
    {
        var paths=Directory.GetFiles(Path.Combine(root,"samples","handbook-capacity"),"*.mrtsim.json");
        Require(paths.Length==12,"手冊容量案例應有8例與4個對照／改善分支，共12檔。");
        var window=new MainWindow();
        try
        {
            foreach(var path in paths)
            {
                var document=TopologyProjectFormat.Deserialize(File.ReadAllText(path));
                SetDocument(window,document);
                Invoke(window,"RefreshSpatialCapacityResults");
                Require(window.SpatialCapacityRows.Count==document.SpatialCapacity!.Points.Sum(p=>p.Kind==SpatialReferencePointKind.IntermediateStation?2:1),"手冊存檔結果列完整："+path);
                if(Path.GetFileName(path).StartsWith("04-"))
                    Require(window.SpatialCapacityRows.Select(r=>r.LineCapacity).SequenceEqual(new[]{"37","33"}),"例四圖示40秒應保留37/33而非迎合發布答案。");
                if(Path.GetFileName(path).StartsWith("04b-"))
                    Require(window.SpatialCapacityRows.Select(r=>r.LineCapacity).SequenceEqual(new[]{"34","31"}),"例四45秒分支應重現34/31。");
                if(Path.GetFileName(path).StartsWith("07-"))
                {
                    Require(window.SpatialCapacityRows.Single(r=>r.ReferencePoint=="S7 中間站" && r.Direction=="順行").LineCapacity=="26","例七手冊方向S7應為26。");
                    Require(window.SpatialCapacityBottleneckRows.All(r=>r.ReferencePoint=="S14"),"例七兩方向瓶頸為S14。");
                }
                if(Path.GetFileName(path).StartsWith("07b-"))
                    Require(window.SpatialCapacityRows.Single(r=>r.ReferencePoint=="S7 中間站" && r.Direction=="順行").LineCapacity=="25","例七URCS相容S7應為25。");
                if(Path.GetFileName(path).StartsWith("08c-"))
                    Require(window.SpatialCapacityRows.Single(r=>r.ReferencePoint=="S14 站後折返").DesignHeadway=="148.843","33秒改善分支應滿足150秒。");
            }
        }
        finally {WpfTestWait.Close(window);}
        var type=typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.SpatialCapacitySensitivityWindow")!;
        var settings=TopologyProjectFormat.Deserialize(File.ReadAllText(paths.Single(p=>Path.GetFileName(p).StartsWith("06-")))).SpatialCapacity!;
        var dialog=(Window)Activator.CreateInstance(type,settings)!;
        try
        {
            var grid=(Grid)dialog.Content;
            Require(Grid.GetRow(grid.Children.OfType<ScrollViewer>().Single())==1,"敏感度選擇區必須位於獨立列，不可覆蓋說明。");
        }
        finally {dialog.Close();}
        Console.WriteLine("[通過] 12份手冊容量檔載入與模式、原圖差異、瓶頸及改善分支");
    }
    private static void VerifyUnconfiguredMessage(TopologyProjectDocument sample)
    {
        var window = new MainWindow();
        try
        {
            SetDocument(window, sample with { SpatialCapacity = null });
            Invoke(window, "RefreshSpatialCapacityResults");
            var summary = (TextBlock)window.FindName("SpatialCapacitySummaryText")!;
            var rows = (ICollection)typeof(MainWindow).GetProperty("SpatialCapacityRows")!.GetValue(window)!;
            Require(rows.Count == 0, "未配置容量設定時結果列必須為空。");
            var runtimeButton = window.FindName("ApplyCapacityRuntimeButton") as Button;
            Require(runtimeButton is not null && runtimeButton.Content.ToString()!.Contains("套用中間站"), "容量頁必須提供明確套用至運行入口。");
            Require(summary.Text.Contains("尚未配置", StringComparison.Ordinal), "未配置容量設定時必須顯示尚未配置提示。");
        }
        finally
        {
            WpfTestWait.Close(window);
        }
    }

    private static void VerifyEngineValues(TopologyProjectDocument sample)
    {
        var points = new[]
        {
            (Point: Station("CAP-GRADE-10-0", 10, 0), Headway: "121.176", Line: "29"),
            (Point: Station("CAP-GRADE-0-10", 0, 10), Headway: "126.182", Line: "28"),
            (Point: Station("CAP-GRADE-10-MINUS-10", 10, -10), Headway: "119.983", Line: "30")
        };
        var settings = new SpatialCapacityProjectSettings(
            new SpatialCapacityGlobalParameters(),
            new SpatialCapacityTrainParameters(),
            points.Select(item => item.Point).ToArray(),
            SpatialCapacityCalculationMode.TaiwanHandbook);
        var window = new MainWindow();
        try
        {
            SetDocument(window, sample with { SpatialCapacity = settings });
            Invoke(window, "RefreshSpatialCapacityResults");
            var rows = (IList)typeof(MainWindow).GetProperty("SpatialCapacityRows")!.GetValue(window)!;
            Require(rows.Count == points.Length * 2, "中間站容量點必須各顯示順行與逆行兩列。");
            foreach (var expected in points)
            {
                var row = rows.Cast<object>().Single(item =>
                    (string)item.GetType().GetProperty("ReferencePoint")!.GetValue(item)! == expected.Point.Name
                    && (string)item.GetType().GetProperty("Direction")!.GetValue(item)! == "順行");
                Equal(expected.Headway, RowText(row, "DesignHeadway"), $"{expected.Point.Name} 的台灣手冊設計班距格式");
                Equal(expected.Line, RowText(row, "LineCapacity"), $"{expected.Point.Name} 的台灣手冊列／h格式");
                Equal("台灣手冊", RowText(row, "Mode"), $"{expected.Point.Name} 必須保留計算模式");
            }

            var turnbackSettings = settings with
            {
                Points = [new ProjectSpatialReferencePoint(
                    "CAP-TURNBACK", "手動位置-T", "站後折返", SpatialReferencePointKind.AfterStationTurnback)]
            };
            SetDocument(window, sample with { SpatialCapacity = turnbackSettings });
            Invoke(window, "RefreshSpatialCapacityResults");
            rows = (IList)typeof(MainWindow).GetProperty("SpatialCapacityRows")!.GetValue(window)!;
            Require(rows.Count == 1, "非中間站容量點只有一組設定方向，不能重複顯示順逆行。");
            Equal("設定方向", RowText(rows[0]!, "Direction"), "非中間站結果必須標示設定方向");
        }
        finally
        {
            WpfTestWait.Close(window);
        }
    }

    private static void VerifyEditorTransactionAndSpeedUnits(TopologyProjectDocument sample)
    {
        var point = new ProjectSpatialReferencePoint(
            "CAP-ROUNDTRIP", "手動位置-B", "速度往返", SpatialReferencePointKind.IntermediateStation,
            StationForwardEarlierCruiseSpeedMetersPerSecond: 72 / 3.6,
            StationForwardLaterCruiseSpeedMetersPerSecond: 66.123456789 / 3.6,
            StationForwardGradeInPermille: 0.123456789,
            StationForwardDistanceToSignalMeters: 15.123456789);
        var settings = new SpatialCapacityProjectSettings(
            new SpatialCapacityGlobalParameters(),
            new SpatialCapacityTrainParameters(),
            [point],
            SpatialCapacityCalculationMode.UrcsCompatible,
            CompareAsSingleScenario: true);
        var document = sample with { SpatialCapacity = settings };
        var editorType = EditorType();

        var cancel = (Window)Activator.CreateInstance(editorType, document)!;
        try
        {
            SelectFirstPoint(cancel, SpatialReferencePointKind.IntermediateStation);
            var nameEditor = PointEditor(cancel, "Name");
            nameEditor.Text = "取消後的草稿名稱";
            Require((bool)Invoke(cancel, "CommitActivePoint", false)!, "取消測試的 draft 編輯應可提交至交易草稿。");
            Require(((CheckBox)GetField(cancel, "compareAsScenarioCheckBox")).IsChecked == true,
                "取消編輯器必須載入同一容量分析情境旗標。");
            Require(editorType.GetProperty("Result")!.GetValue(cancel) is null, "取消前不應先產生 Result。");
            cancel.Close();
            Require(editorType.GetProperty("Result")!.GetValue(cancel) is null, "實際修改 draft 後取消仍不得產生 Result。");
            Equal("速度往返", point.Name, "取消不得改動原始文件中的容量參考點");
        }
        finally
        {
            cancel.Close();
        }

        var apply = (Window)Activator.CreateInstance(editorType, document)!;
        try
        {
            SelectFirstPoint(apply, SpatialReferencePointKind.IntermediateStation);
            var earlier = PointEditor(apply, "StationForwardEarlierCruiseSpeedKmh");
            var later = PointEditor(apply, "StationForwardLaterCruiseSpeedKmh");
            Equal("72", earlier.Text, "速度欄位應以 km/h 顯示");
            Equal("66.123456789", later.Text, "速度欄位應以 km/h 顯示");
            earlier.Text = "81";
            Require((bool)Invoke(apply, "TryApplyDraft")!, "有效容量草稿應可套用。");
            var result = (TopologyProjectDocument)editorType.GetProperty("Result")!.GetValue(apply)!;
            Require(result.SpatialCapacity is not null, "套用後 Result 必須帶容量設定。");
            Equal(SpatialCapacityCalculationMode.UrcsCompatible, result.SpatialCapacity!.Mode, "套用後必須保留 URCS 相容模式");
            Require(result.SpatialCapacity.CompareAsSingleScenario, "套用後必須保留同一容量分析情境旗標");
            NearlyEqual(81 / 3.6, result.SpatialCapacity.Points.Single().StationForwardEarlierCruiseSpeedMetersPerSecond,
                1e-9, "km/h 轉 m/s 往返");
                        NearlyEqual(0.123456789, result.SpatialCapacity.Points.Single().StationForwardGradeInPermille,
                1e-12, "編輯器未修改的坡度不可因顯示格式截斷");
            NearlyEqual(15.123456789, result.SpatialCapacity.Points.Single().StationForwardDistanceToSignalMeters,
                1e-12, "編輯器未修改的距離不可因顯示格式截斷");
            NearlyEqual(66.123456789 / 3.6, result.SpatialCapacity.Points.Single().StationForwardLaterCruiseSpeedMetersPerSecond,
                1e-12, "編輯器未修改的速度不可因顯示格式截斷");
            var roundTrip = TopologyProjectFormat.Deserialize(TopologyProjectFormat.Serialize(result));
            NearlyEqual(81 / 3.6, roundTrip.SpatialCapacity!.Points.Single().StationForwardEarlierCruiseSpeedMetersPerSecond,
                1e-9, "套用後 Schema 8 速度往返");
        }
        finally
        {
            apply.Close();
        }
    }

    private static void VerifyPointSelectionPreservesInput(TopologyProjectDocument sample)
    {
        var points = new[]
        {
            new ProjectSpatialReferencePoint("CAP-SWITCH-1", "位置-1", "第一點", SpatialReferencePointKind.IntermediateStation),
            new ProjectSpatialReferencePoint("CAP-SWITCH-2", "位置-2", "第二點", SpatialReferencePointKind.IntermediateStation),
            new ProjectSpatialReferencePoint("CAP-SWITCH-T", "位置-T", "站前折返", SpatialReferencePointKind.BeforeStationTurnback)
        };
        var document = sample with
        {
            SpatialCapacity = new SpatialCapacityProjectSettings(
                new SpatialCapacityGlobalParameters(), new SpatialCapacityTrainParameters(), points)
        };
        var editor = (Window)Activator.CreateInstance(EditorType(), document)!;
        try
        {
            SelectFirstPoint(editor, SpatialReferencePointKind.IntermediateStation);
            var pointLists = (IDictionary)GetField(editor, "pointLists");
            var stationList = (ListBox)pointLists[SpatialReferencePointKind.IntermediateStation]!;
            PointEditor(editor, "Name").Text = "切換保存";
            stationList.SelectedIndex = 1;
            stationList.SelectedIndex = 0;
            Equal("切換保存", PointEditor(editor, "Name").Text, "切換容量參考點後應保留已輸入名稱");

            var tabs = (TabControl)GetField(editor, "pointTabs");
            tabs.SelectedIndex = 1;
            tabs.SelectedIndex = 0;
            Equal("切換保存", PointEditor(editor, "Name").Text, "切換容量分頁後應保留已輸入名稱");

            var invalid = PointEditor(editor, "StationForwardDwellSeconds");
            invalid.Text = "不是數字";
            stationList.SelectedIndex = 1;
            Equal(0, stationList.SelectedIndex, "非法輸入時不得切換容量參考點");
            Equal("不是數字", invalid.Text, "非法輸入時不得默默丟失原輸入");
        }
        finally
        {
            editor.Close();
        }
    }

    private static void VerifyTopologyEditorPreservesCapacity(TopologyProjectDocument sample)
    {
        var settings = new SpatialCapacityProjectSettings(
            new SpatialCapacityGlobalParameters(),
            new SpatialCapacityTrainParameters(),
            [new ProjectSpatialReferencePoint("CAP-PROJECT", "位置-P", "專案容量", SpatialReferencePointKind.IntermediateStation)]);
        var document = sample with { SpatialCapacity = settings };
        var editorType = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.TopologyEditorWindow")!;
        var pageType = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.ProjectWorkspacePage")!;
        var editor = (Window)Activator.CreateInstance(editorType, document, Enum.Parse(pageType, "Project"))!;
        editor.Left = -10000;
        editor.Top = -10000;
        editor.WindowStartupLocation = WindowStartupLocation.Manual;
        editor.ShowInTaskbar = false;
        editor.Show();
        try
        {
            CompleteTextDialog(editor, "編輯專案識別資料", boxes => boxes[1].Text = "容量保留專案");
            editorType.GetMethod("EditProjectIdentity", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null);
            var state = GetField(editor, "state");
            var draft = (TopologyProjectDocument)state.GetType().GetProperty("Draft")!.GetValue(state)!;
            Equal("容量保留專案", draft.ProjectName, "TopologyEditor 專案名稱應套用修改");
            Require(draft.SpatialCapacity is not null, "TopologyEditor 修改專案名稱後不得遺失 SpatialCapacity。");
            Equal("CAP-PROJECT", draft.SpatialCapacity!.Points.Single().ReferencePointId,
                "TopologyEditor 修改專案名稱後應保留容量參考點");
        }
        finally
        {
            editor.Close();
        }
    }

    private static void VerifyCompactEditorLayout(TopologyProjectDocument sample)
    {
        var settings = new SpatialCapacityProjectSettings(
            new SpatialCapacityGlobalParameters(), new SpatialCapacityTrainParameters(),
            [new ProjectSpatialReferencePoint("CAP-LAYOUT", "位置-L", "布局測試", SpatialReferencePointKind.IntermediateStation)]);
        var editor = (Window)Activator.CreateInstance(EditorType(), sample with { SpatialCapacity = settings })!;
        try
        {
            var root = (Grid)editor.Content;
            var modelScroll = root.Children.OfType<ScrollViewer>().Single(item => Grid.GetRow(item) == 1);
            var pointTabs = root.Children.OfType<TabControl>().Single(item => Grid.GetRow(item) == 2);
            var actions = root.Children.OfType<StackPanel>().Single(item => Grid.GetRow(item) == 3);
            Require(modelScroll.MaxHeight <= 280, "容量設定上方參數區必須有高度上限，避免壓縮參考點區域。");
            foreach (var size in new[] { new Size(900, 620), new Size(900 / 1.25, 620 / 1.25) })
            {
                root.Measure(size);
                root.Arrange(new Rect(new Point(), size));
                Require(pointTabs.DesiredSize.Height > 0 && pointTabs.DesiredSize.Width > 0,
                    $"容量參考點分頁在 {size.Width:0}x{size.Height:0} 可視尺寸下必須存在。");
                Require(actions.DesiredSize.Height > 0, "容量設定套用／取消列在緊湊尺寸下仍必須存在。");
            }
        }
        finally
        {
            editor.Close();
        }
    }

    private static void VerifyScenarioAndSensitivity(TopologyProjectDocument sample)
    {
        var points = new[]
        {
            new ProjectSpatialReferencePoint("S11", "S11", "S11", SpatialReferencePointKind.IntermediateStation,
                StationForwardDwellSeconds: 30, StationReverseDwellSeconds: 30),
            new ProjectSpatialReferencePoint("S12", "S12", "S12", SpatialReferencePointKind.IntermediateStation,
                StationForwardDwellSeconds: 35, StationReverseDwellSeconds: 35),
            new ProjectSpatialReferencePoint("S14", "S14", "S14", SpatialReferencePointKind.IntermediateStation,
                StationForwardDwellSeconds: 90, StationReverseDwellSeconds: 90),
            new ProjectSpatialReferencePoint("S16", "S16", "S16", SpatialReferencePointKind.IntermediateStation,
                StationForwardDwellSeconds: 30, StationReverseDwellSeconds: 30)
        };
        var scenarioSettings = new SpatialCapacityProjectSettings(
            new SpatialCapacityGlobalParameters(), new SpatialCapacityTrainParameters(), points,
            SpatialCapacityCalculationMode.TaiwanHandbook, CompareAsSingleScenario: true);
        var main = new MainWindow();
        try
        {
            SetDocument(main, sample with { SpatialCapacity = scenarioSettings });
            Invoke(main, "RefreshSpatialCapacityResults");
            var bottlenecks = (IList)typeof(MainWindow)
                .GetProperty("SpatialCapacityBottleneckRows")!.GetValue(main)!;
            Require(bottlenecks.Count >= 1, "啟用同一容量分析情境後必須顯示瓶頸結果。");
            var summary = (TextBlock)main.FindName("SpatialCapacityBottleneckSummaryText")!;
            Require(summary.Visibility == Visibility.Visible && summary.Text.Contains("S14", StringComparison.Ordinal),
                "四個手動參考點的範例應顯示 S14 瓶頸。");
            Require(RowText(bottlenecks[0]!, "LineCapacity").Length > 0, "瓶頸結果必須顯示列／h。");
        }
        finally
        {
            WpfTestWait.Close(main);
        }

        var sensitivityType = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.SpatialCapacitySensitivityWindow")
            ?? throw new InvalidOperationException("找不到容量敏感度分析視窗。");
        var sensitivitySettings = scenarioSettings with
        {
            Points =
            [
                new ProjectSpatialReferencePoint("J1", "J1", "銜接", SpatialReferencePointKind.Junction),
                new ProjectSpatialReferencePoint("TURN", "TURN", "站後", SpatialReferencePointKind.AfterStationTurnback)
            ]
        };
        var sensitivity = (Window)Activator.CreateInstance(sensitivityType, sensitivitySettings)!;
        try
        {
            var parameterCombo = (ComboBox)GetField(sensitivity, "parameterComboBox");
            var pointCombo = (ComboBox)GetField(sensitivity, "pointComboBox");
            var start = (TextBox)GetField(sensitivity, "startTextBox");
            var end = (TextBox)GetField(sensitivity, "endTextBox");
            var step = (TextBox)GetField(sensitivity, "stepTextBox");
            var summary = (TextBlock)GetField(sensitivity, "summaryText");

            parameterCombo.SelectedValue = SpatialCapacitySensitivityParameter.MainlineTrafficRatio;
            Require(pointCombo.Items.Count == 1 && pointCombo.SelectedValue as string == "J1",
                "主線連續通過比例只允許銜接點。");
            parameterCombo.SelectedValue = SpatialCapacitySensitivityParameter.TurnbackDwellSeconds;
            Require(pointCombo.Items.Count == 1 && pointCombo.SelectedValue as string == "TURN",
                "折返／尾軌停靠時間只允許三類折返點。");

            start.Text = "35";
            end.Text = "35";
            step.Text = "5";
            Require((bool)Invoke(sensitivity, "TryRunAnalysis")!, "折返停靠時間 35 秒的手冊敏感度分析應成功。");
            var result = (IList)GetProperty(sensitivityType, sensitivity, "Result")!;
            Require(result.Count == 1, "dwell=35 的敏感度分析應有一筆結果。");
            Equal("35", RowText(result[0]!, "Value"), "敏感度結果必須保留 dwell=35 參數值");
            Equal("160.757", RowText(result[0]!, "DesignHeadway"), "dwell=35 台灣手冊目標班距格式");

            start.Text = "NaN";
            Require(!(bool)Invoke(sensitivity, "TryRunAnalysis")!, "非有限敏感度起點必須拒絕。");
            Require(summary.Text.Contains("有限數值", StringComparison.Ordinal), "非有限敏感度起點必須顯示錯誤摘要。");
            start.Text = "20";
            end.Text = "10";
            Require(!(bool)Invoke(sensitivity, "TryRunAnalysis")!, "反向敏感度範圍必須拒絕。");
        }
        finally
        {
            sensitivity.Close();
        }
    }

    private static Type EditorType() => typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.SpatialCapacityEditorWindow")
        ?? throw new InvalidOperationException("找不到容量編輯器。");

    private static void SelectFirstPoint(Window editor, SpatialReferencePointKind kind)
    {
        var lists = (IDictionary)GetField(editor, "pointLists");
        ((ListBox)lists[kind]!).SelectedIndex = 0;
    }

    private static TextBox PointEditor(Window editor, string key)
    {
        var editors = (IDictionary)GetField(editor, "activePointEditors");
        return (TextBox)editors[key]!;
    }

    private static object GetField(object instance, string name) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;

    private static object? GetProperty(Type type, object instance, string name) =>
        type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)!.GetValue(instance);

    private static object? Invoke(object instance, string name, params object?[] args) =>
        instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, args);

    private static string RowText(object row, string property) =>
        (string)row.GetType().GetProperty(property)!.GetValue(row)!;

    private static void CompleteTextDialog(Window owner, string title, Action<TextBox[]> fill)
    {
        owner.Dispatcher.BeginInvoke(new Action(() =>
        {
            var dialog = Application.Current.Windows.OfType<Window>()
                .Single(window => window.Owner == owner && window.Title == title);
            var panel = (StackPanel)((ScrollViewer)dialog.Content).Content;
            fill(panel.Children.OfType<TextBox>().ToArray());
            var buttons = panel.Children.OfType<StackPanel>().Last();
            buttons.Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
    }

    private static ProjectSpatialReferencePoint Station(string id, double gradeIn, double gradeOut) =>
        new(id, $"手動位置-{id}", id, SpatialReferencePointKind.IntermediateStation,
            StationForwardGradeInPermille: gradeIn, StationForwardGradeOutPermille: gradeOut);

    private static void SetDocument(MainWindow window, TopologyProjectDocument document) =>
        typeof(MainWindow).GetField("_activeTopologyProjectDocument", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(window, document);

    private static object? Invoke(MainWindow window, string name, params object?[] args) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);

    private static void Equal<T>(T expected, T actual, string message)
        where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}：預期 {expected}，實際 {actual}。");
    }

    private static void NearlyEqual(double expected, double actual, double tolerance, string message)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{message}：預期 {expected.ToString(CultureInfo.InvariantCulture)}，實際 {actual.ToString(CultureInfo.InvariantCulture)}。");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
