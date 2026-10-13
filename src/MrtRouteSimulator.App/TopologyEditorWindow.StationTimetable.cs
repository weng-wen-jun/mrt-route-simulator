using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

internal sealed partial class TopologyEditorWindow
{
    private void EditSelectedStationTimetable()
    {
        if (selectedDispatchRow is null)
        {
            ShowErrors(["請先選取一筆手動班表車次。"]);
            return;
        }
        CommitDispatchDetail();
        var route = ResolveDispatchRoute(selectedDispatchRow);
        if (route is null)
        {
            ShowErrors(["所選車次沒有有效的營運路徑，請先選擇營運路徑。"]);
            return;
        }

        var eligible = GetEligibleTimetableStations(selectedDispatchRow, route).ToArray();
        var eligibleIds = eligible.Select(item => item.StationId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = new ObservableCollection<StationTimetableEntryEditorViewModel>(
            selectedDispatchRow.StationTimetableEntries.Select(item =>
                new StationTimetableEntryEditorViewModel(item.ToDomain())));

        var dialog = new Window
        {
            Title = $"逐站時刻 · {selectedDispatchRow.ServiceRunId}",
            Owner = this,
            Width = 700,
            Height = 520,
            MinWidth = 560,
            MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        EditorChrome.ApplyWindowChrome(dialog);
        AutomationProperties.SetName(dialog, "逐站發車時刻與最少停站時間編輯器");
        var root = new DockPanel { Margin = new Thickness(14) };
        dialog.Content = root;
        var note = EditorChrome.Hint(
            "表定發車時刻使用與該車次相同的班表時鐘（HH:mm:ss）。只可設定此營運路徑上實際停靠且不是終點的車站；通過站與終點不適用。起站沒有到站事件，因此最少停站秒數固定為 0。列車須同時到達表定發車時刻並停滿最少秒數才可離站。取消不會修改班表。");
        note.Margin = new Thickness(0, 0, 0, 10);
        DockPanel.SetDock(note, Dock.Top);
        root.Children.Add(note);

        DataGrid? grid = null;
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        actions.Children.Add(CreateNamedButton("新增停靠站", "新增逐站發車時刻", (_, _) =>
        {
            var available = eligible.Where(item => rows.All(row => !IdEquals(row.StationId, item.StationId))).ToArray();
            if (available.Length == 0)
            {
                ShowErrors(["此車次所有可設定的停靠站都已列入。"]);
                return;
            }
            var labels = available.Select(item => $"{item.Name}（{item.StationId}）").ToArray();
            var selected = Choose("新增逐站時刻", "停靠站", labels, labels[0]);
            if (selected is null) return;
            var station = available[Array.IndexOf(labels, selected)];
            var isOrigin = IdEquals(station.StationId, route.Stops.First().StationId);
            var defaultDwell = state.Draft.Topology.Stations
                .FirstOrDefault(item => IdEquals(item.StationId, station.StationId))?.DefaultDwellTimeSeconds ?? 0;
            rows.Add(new StationTimetableEntryEditorViewModel(new ProjectStationTimetableEntry(
                station.StationId,
                selectedDispatchRow.DepartureSeconds,
                isOrigin ? 0 : defaultDwell)));
        }, true));
        actions.Children.Add(CreateNamedButton("刪除選取", "刪除逐站發車時刻", (_, _) =>
        {
            if (grid!.SelectedItem is StationTimetableEntryEditorViewModel row) rows.Remove(row);
        }, true));
        DockPanel.SetDock(actions, Dock.Top);
        root.Children.Add(actions);

        grid = new DataGrid
        {
            ItemsSource = rows,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            RowHeaderWidth = 0
        };
        AutomationProperties.SetName(grid, "逐站時刻表");
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "車站",
            Binding = new Binding(nameof(StationTimetableEntryEditorViewModel.StationId))
                { Converter = new StationNameConverter(state.Draft) },
            IsReadOnly = true,
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "表定發車時刻",
            Binding = new Binding(nameof(StationTimetableEntryEditorViewModel.PlannedDepartureClock))
                { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = 145
        });
        grid.Columns.Add(StationTimetableColumn("最少停站秒數", nameof(StationTimetableEntryEditorViewModel.MinimumDwellSeconds), 135));
        var timetableCard = EditorChrome.Card(grid, "停靠站時刻");

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        footer.Children.Add(CreateNamedButton("套用", "套用逐站發車時刻", (_, _) =>
        {
            if (!grid.CommitEdit(DataGridEditingUnit.Cell, true)
                || !grid.CommitEdit(DataGridEditingUnit.Row, true))
            {
                ShowErrors(["逐站時刻包含無法解析的數值，請修正後再套用。"]);
                return;
            }
            var errors = ValidateStationTimetableRows(rows, eligibleIds, route);
            if (errors.Count > 0)
            {
                ShowErrors(errors);
                return;
            }
            selectedDispatchRow.StationTimetableEntries.Clear();
            foreach (var row in rows.OrderBy(item => RouteStopIndex(route, item.StationId)))
                selectedDispatchRow.StationTimetableEntries.Add(new StationTimetableEntryEditorViewModel(row.ToDomain()));
            CommitTableDrafts();
            dialog.DialogResult = true;
        }, false));
        var cancel = CreateNamedButton("取消", "取消逐站發車時刻", (_, _) => dialog.DialogResult = false, true);
        cancel.Margin = new Thickness(0);
        footer.Children.Add(cancel);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        // DockPanel 的最後一個 child 會填滿剩餘空間；表格卡片必須最後加入，
        // 否則 footer 會被當成 fill child 而在小視窗下掉出可視區。
        root.Children.Add(timetableCard);

        dialog.ShowDialog();
        if (dialog.DialogResult == true) ShowDispatchPlanningPage();
    }

    private ServiceRouteDefinition? ResolveDispatchRoute(ManualTimetableEditorViewModel row)
    {
        var routeId = EditorValue.EmptyToNull(row.ServiceRouteId)
            ?? state.Draft.DirectionRouteBindings.FirstOrDefault(item => item.Direction == row.Direction)?.ServiceRouteId;
        return state.Draft.ServiceRoutes.FirstOrDefault(item => IdEquals(item.ServiceRouteId, routeId));
    }

    private IEnumerable<(string StationId, string Name)> GetEligibleTimetableStations(
        ManualTimetableEditorViewModel row,
        ServiceRouteDefinition route)
    {
        var patternId = EditorValue.EmptyToNull(row.StopPattern)
            ?? state.Draft.ServiceTypes.FirstOrDefault(item => IdEquals(item.Id, row.ServiceType))?.DefaultStopPatternId
            ?? state.Draft.VehicleTypes.FirstOrDefault(item => IdEquals(item.Id, row.VehicleType))?.DefaultStopPatternId;
        var pattern = state.Draft.StopPatterns.FirstOrDefault(item => IdEquals(item.Id, patternId));
        foreach (var stop in route.Stops.Take(Math.Max(0, route.Stops.Count - 1)))
        {
            var instruction = pattern?.Instructions.FirstOrDefault(item => IdEquals(item.StationId, stop.StationId));
            if (instruction?.Action is StopPatternAction.Pass or StopPatternAction.Turnback) continue;
            var station = state.Draft.Topology.Stations.FirstOrDefault(item => IdEquals(item.StationId, stop.StationId));
            yield return (stop.StationId, station?.Name ?? stop.StationId);
        }
    }

    private static List<string> ValidateStationTimetableRows(
        IEnumerable<StationTimetableEntryEditorViewModel> rows,
        IReadOnlySet<string> eligibleIds,
        ServiceRouteDefinition route)
    {
        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var originId = route.Stops.FirstOrDefault()?.StationId;
        foreach (var row in rows)
        {
            if (!eligibleIds.Contains(row.StationId)) errors.Add($"車站「{row.StationId}」不是此車次可設定的停靠站，或為通過站／終點。");
            if (!seen.Add(row.StationId)) errors.Add($"車站「{row.StationId}」重複設定。");
            if (!row.HasValidPlannedDepartureClock)
                errors.Add($"車站「{row.StationId}」的表定發車時刻必須使用 HH:mm:ss 格式。");
            else if (!double.IsFinite(row.PlannedDepartureTimeSeconds) || row.PlannedDepartureTimeSeconds < 0)
                errors.Add($"車站「{row.StationId}」的表定發車時刻無效。");
            if (!double.IsFinite(row.MinimumDwellSeconds) || row.MinimumDwellSeconds < 0)
                errors.Add($"車站「{row.StationId}」的最少停站秒數必須是非負有限值。");
            if (IdEquals(row.StationId, originId) && Math.Abs(row.MinimumDwellSeconds) > 1e-9)
                errors.Add($"起站「{row.StationId}」沒有到站事件，最少停站秒數必須為 0。");
        }
        return errors;
    }

    private static int RouteStopIndex(ServiceRouteDefinition route, string stationId)
    {
        for (var index = 0; index < route.Stops.Count; index++)
            if (IdEquals(route.Stops[index].StationId, stationId)) return index;
        return int.MaxValue;
    }

    private static DataGridTextColumn StationTimetableColumn(string header, string property, double width) => new()
    {
        Header = header,
        Binding = new Binding(property) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged, StringFormat = "0.###" },
        Width = width
    };

    private sealed class StationNameConverter(TopologyProjectDocument document) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var id = value?.ToString() ?? "";
            var station = document.Topology.Stations.FirstOrDefault(item => IdEquals(item.StationId, id));
            return $"{station?.Name ?? id}（{id}）";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
