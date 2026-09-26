using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

internal static class TopologyWorkspaceRoundTripTests
{
    public static void Run(string root)
    {
        var sample = TopologyProjectFormat.Deserialize(File.ReadAllText(Path.Combine(
            root, "samples", "14-大型-二十八站完整營運範例.mrtsim.json")));
        var assembly = typeof(MainWindow).Assembly;
        var editorType = assembly.GetType("MrtRouteSimulator.App.TopologyEditorWindow")!;
        var pageType = assembly.GetType("MrtRouteSimulator.App.ProjectWorkspacePage")!;
        var editor = (Window)Activator.CreateInstance(editorType, sample, Enum.Parse(pageType, "Infrastructure"))!;
        editor.Left = -10000;
        editor.Top = -10000;
        editor.WindowStartupLocation = WindowStartupLocation.Manual;
        editor.ShowInTaskbar = false;
        editor.Show();
        try
        {
            var state = editorType.GetField("state", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
            var draftProperty = state.GetType().GetProperty("Draft")!;
            editorType.GetMethod("CommitTableDrafts", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, null);
            var draft = (TopologyProjectDocument)draftProperty.GetValue(state)!;
            if (draft.Topology.TurnbackOperations.Count != sample.Topology.TurnbackOperations.Count
                || draft.Topology.PassingOperations.Count != sample.Topology.PassingOperations.Count
                || draft.Topology.StationOperations.Count != sample.Topology.StationOperations.Count
                || draft.Topology.DirectedConnections.Count != sample.Topology.DirectedConnections.Count)
                throw new InvalidOperationException("大型樣本進入基礎設施頁後不得遺失折返、待避、車站作業或有向接續。");
            if (!draft.Topology.PassingOperations.Select(item => (item.OperationId, item.FacilityId, item.ServiceRouteId, item.ExpressServiceTypeId))
                .SequenceEqual(sample.Topology.PassingOperations.Select(item => (item.OperationId, item.FacilityId, item.ServiceRouteId, item.ExpressServiceTypeId))))
                throw new InvalidOperationException("大型樣本待避作業未修改往返後內容漂移。");
            var serviceNetwork = editorType.GetField("serviceNetwork", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
            var editorPatterns = (IEnumerable)serviceNetwork.GetType().GetProperty("StopPatterns")!.GetValue(serviceNetwork)!;
            var o13Pattern = editorPatterns.Cast<object>().Single(item =>
                (string)item.GetType().GetProperty("Id")!.GetValue(item)! == "FULL-LINE-O13-HOLD");
            var editorInstructions = (IEnumerable)o13Pattern.GetType().GetProperty("Instructions")!.GetValue(o13Pattern)!;
            var o13Instruction = editorInstructions.Cast<object>().Single(item =>
                (string)item.GetType().GetProperty("Station")!.GetValue(item)! == "O13");
            var waitProperty = o13Instruction.GetType().GetProperty("WaitForOvertakeServiceRunId")!;
            if ((string)waitProperty.GetValue(o13Instruction)! != "EXPRESS-DOWN-01")
                throw new InvalidOperationException("工作區未顯示大型樣本的指定待避車次。");
            waitProperty.SetValue(o13Instruction, "");
            editorType.GetMethod("CommitTableDrafts", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, null);
            draft = (TopologyProjectDocument)draftProperty.GetValue(state)!;
            if (draft.StopPatterns.Single(item => item.Id == "FULL-LINE-O13-HOLD")
                    .Instructions.Single(item => item.StationId == "O13").WaitForOvertakeServiceRunId is not null)
                throw new InvalidOperationException("工作區清空指定待避車次後未回復一般停站行為。");
            waitProperty.SetValue(o13Instruction, "EXPRESS-DOWN-01");
            editorType.GetMethod("CommitTableDrafts", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, null);
            draft = (TopologyProjectDocument)draftProperty.GetValue(state)!;
            if (draft.StopPatterns.Single(item => item.Id == "FULL-LINE-O13-HOLD")
                    .Instructions.Single(item => item.StationId == "O13").WaitForOvertakeServiceRunId != "EXPRESS-DOWN-01")
                throw new InvalidOperationException("工作區指定待避車次編輯後未寫入草稿。");
            TopologyProjectFormat.CreateRuntime(draft);

            var rows = (IList)editorType.GetField("stationOperations", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
            var first = rows[0]!;
            var property = first.GetType().GetProperty("DefaultDwellSeconds")!;
            var original = (double)property.GetValue(first)!;
            property.SetValue(first, original + 1);
            editorType.GetMethod("CommitTableDrafts", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, null);
            draft = (TopologyProjectDocument)draftProperty.GetValue(state)!;
            if (draft.Topology.StationOperations[0].DefaultDwellTimeSeconds != original + 1)
                throw new InvalidOperationException("車站作業表格修改未寫入 Schema 8 草稿。");
            TopologyProjectFormat.CreateRuntime(draft);
            Console.WriteLine("[通過] 大型樣本作業與有向接續經工作區往返保留，車站作業可修改並建立 runtime");

            CompleteTextDialog(editor, "編輯專案識別資料", boxes =>
            {
                boxes[0].Text = "LARGE-UI-REPRO";
                boxes[1].Text = "大型 UI 重現測試";
            });
            editorType.GetMethod("EditProjectIdentity", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, null);
            CompleteTextDialog(editor, "營運與安全參數", boxes =>
            {
                boxes[4].Text = "0.15";
                boxes[9].Text = "0.3";
                boxes[10].Text = "2";
            });
            editorType.GetMethod("EditOperationalSettings", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, null);
            draft = (TopologyProjectDocument)draftProperty.GetValue(state)!;
            if (draft.ProjectId != "LARGE-UI-REPRO" || draft.ProjectName != "大型 UI 重現測試"
                || draft.Operations.TractionFadeRatio != 0.15
                || draft.Operations.BrakeBuildUpTimeSeconds != 0.3
                || draft.Operations.PositioningErrorMeters != 2)
                throw new InvalidOperationException("專案識別與營運設定對話框未套用至大型 Schema 8 草稿。");
            TopologyProjectFormat.CreateRuntime(draft);
            Console.WriteLine("[通過] 大型樣本專案識別與三項原缺入口營運參數可經對話框修改");

            CompleteChoiceDialog(editor, "設定發車模式", "班距計畫", () =>
                CompleteChoiceDialog(editor, "設定車輛分配", "全部指定"));
            editorType.GetMethod("EditDispatchModes", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, null);
            draft = (TopologyProjectDocument)draftProperty.GetValue(state)!;
            if (draft.Dispatch.ActiveMode != DispatchPlanningMode.SimpleHeadway
                || draft.Dispatch.VehicleAssignmentMode != VehicleAssignmentMode.ExplicitOnly)
                throw new InvalidOperationException("發車模式與車輛分配對話框未寫入 Schema 8 草稿。");
            CompleteChoiceDialog(editor, "設定發車模式", "手動班表", () =>
                CompleteChoiceDialog(editor, "設定車輛分配", "自動分配"));
            editorType.GetMethod("EditDispatchModes", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, null);
            draft = (TopologyProjectDocument)draftProperty.GetValue(state)!;
            TopologyProjectFormat.CreateRuntime(draft);
            Console.WriteLine("[通過] 大型樣本發車模式與車輛分配可經對話框切換並恢復 runtime");

            var manualCount = draft.Dispatch.ManualTimetableRows?.Length ?? 0;
            CompleteChoiceDialog(editor, "選擇班次方向", "上行");
            editorType.GetMethod("AddManualTimetableRow", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, null);
            draft = (TopologyProjectDocument)draftProperty.GetValue(state)!;
            if (draft.Dispatch.ManualTimetableRows?.Length != manualCount + 1
                || draft.Dispatch.ManualTimetableRows[^1].Direction != TrainDirection.Inbound)
                throw new InvalidOperationException("手動班表新增時選取上行，卻未寫入上行班次。");
            TopologyProjectFormat.CreateRuntime(draft);
            Console.WriteLine("[通過] 大型樣本可由 UI 新增指定上行方向的手動班次");

            CompleteTextDialog(editor, "模擬參數", boxes =>
            {
                boxes[2].Text = "9";
                boxes[3].Text = "300";
            }, () => CompleteChoiceDialog(editor, "設定營運模式", "寫實營運", () =>
                CompleteChoiceDialog(editor, "設定移動閉塞", "控制", () =>
                    CompleteChoiceDialog(editor, "設定煞車估算", "營運煞車"))));
            editorType.GetMethod("EditSimulationSettings", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, null);
            draft = (TopologyProjectDocument)draftProperty.GetValue(state)!;
            if (draft.Simulation.TrainCount != 9 || draft.Simulation.HeadwaySeconds != 300)
                throw new InvalidOperationException("模擬設定對話框未保存列車數摘要或指定班距。");
            TopologyProjectFormat.CreateRuntime(draft);
            Console.WriteLine("[通過] 大型樣本可由 UI 設定列車數摘要與指定班距");

            var reloaded = TopologyProjectFormat.Deserialize(TopologyProjectFormat.Serialize(draft));
            if (reloaded.ProjectId != draft.ProjectId
                || reloaded.Operations.TractionFadeRatio != draft.Operations.TractionFadeRatio
                || reloaded.Dispatch.ManualTimetableRows?.Length != draft.Dispatch.ManualTimetableRows?.Length
                || reloaded.Topology.PassingOperations.Count != draft.Topology.PassingOperations.Count
                || reloaded.Topology.DirectedConnections.Count != draft.Topology.DirectedConnections.Count)
                throw new InvalidOperationException("大型工作區修改後的 Schema 8 序列化／重讀遺失重要設定。");
            TopologyProjectFormat.CreateRuntime(reloaded);
            Console.WriteLine("[通過] 大型樣本修改後可序列化、重讀並建立 topology-native runtime");

            var applyEditor = (Window)Activator.CreateInstance(editorType, reloaded, Enum.Parse(pageType, "Project"))!;
            applyEditor.Left = -10000;
            applyEditor.Top = -10000;
            applyEditor.WindowStartupLocation = WindowStartupLocation.Manual;
            applyEditor.ShowInTaskbar = false;
            applyEditor.Dispatcher.BeginInvoke(new Action(() =>
                editorType.GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(applyEditor, null)));
            if (applyEditor.ShowDialog() != true
                || editorType.GetProperty("Result")!.GetValue(applyEditor) is not TopologyProjectDocument applied
                || applied.ProjectId != reloaded.ProjectId)
                throw new InvalidOperationException("大型工作區套用未產生已驗證的 Schema 8 專案。");

            var cancelEditor = (Window)Activator.CreateInstance(editorType, sample, Enum.Parse(pageType, "Project"))!;
            cancelEditor.Left = -10000;
            cancelEditor.Top = -10000;
            cancelEditor.WindowStartupLocation = WindowStartupLocation.Manual;
            cancelEditor.ShowInTaskbar = false;
            cancelEditor.Dispatcher.BeginInvoke(new Action(() => cancelEditor.DialogResult = false));
            if (cancelEditor.ShowDialog() != false || editorType.GetProperty("Result")!.GetValue(cancelEditor) is not null)
                throw new InvalidOperationException("取消工作區不得產生已套用的專案。");
            Console.WriteLine("[通過] 大型工作區套用經 runtime 驗證，取消不產生替換專案");
        }
        finally { editor.Close(); }
    }

    private static void CompleteTextDialog(Window owner, string title, Action<TextBox[]> fill, Action? afterClose = null)
    {
        owner.Dispatcher.BeginInvoke(new Action(() =>
        {
            var dialog = Application.Current.Windows.OfType<Window>()
                .Single(window => window.Owner == owner && window.Title == title);
            var panel = (StackPanel)((ScrollViewer)dialog.Content).Content;
            fill(panel.Children.OfType<TextBox>().ToArray());
            if (afterClose is not null) dialog.Closed += (_, _) => afterClose();
            var buttons = panel.Children.OfType<StackPanel>().Last();
            var accept = buttons.Children.OfType<Button>().First();
            accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
    }

    private static void CompleteChoiceDialog(Window owner, string title, string choice, Action? afterClose = null)
    {
        owner.Dispatcher.BeginInvoke(new Action(() =>
        {
            var dialog = Application.Current.Windows.OfType<Window>()
                .Single(window => window.Owner == owner && window.Title == title);
            var panel = (StackPanel)dialog.Content;
            var combo = panel.Children.OfType<ComboBox>().Single();
            combo.SelectedItem = choice;
            if (!Equals(combo.SelectedItem, choice))
                throw new InvalidOperationException($"對話框「{title}」缺少選項「{choice}」。");
            if (afterClose is not null) dialog.Closed += (_, _) => afterClose();
            var buttons = panel.Children.OfType<StackPanel>().Last();
            buttons.Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
    }
}
