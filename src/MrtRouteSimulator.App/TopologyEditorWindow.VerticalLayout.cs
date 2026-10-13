using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

internal sealed partial class TopologyEditorWindow
{
    // 此列只轉接原 VM；顯示預設值不寫回草稿，確保未編輯往返不漂移。
    private sealed class VerticalLaneEditorRow(TrackEdgeEditorViewModel source, double? baseline, double? automatic)
    {
        public string Id => source.Id;
        public TrackEdgeKind Kind => source.Kind;
        public bool CanAdjust => !baseline.HasValue;
        public double? SchematicLane
        {
            get => baseline ?? source.SchematicLane ?? automatic;
            set { if (CanAdjust) source.SchematicLane = value; }
        }
    }

    private UIElement BuildVerticalLaneEditor()
    {
        var mainlineIds = state.Draft.DirectionRouteBindings
            .SelectMany(binding => state.Draft.ServiceRoutes
                .Where(route => route.ServiceRouteId == binding.ServiceRouteId)
                .SelectMany(route => route.Traversals.Select(traversal => traversal.TrackEdgeId)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var suggested = SchematicVerticalLayout.FillDefaults(state.Draft).Topology.Edges
            .ToDictionary(edge => edge.TrackEdgeId, StringComparer.OrdinalIgnoreCase);
        foreach (var edge in suggested.Values.Where(edge => edge.Kind == TrackEdgeKind.Mainline && edge.SchematicLane is 1 or -1))
            mainlineIds.Add(edge.TrackEdgeId);
        var rows = infrastructure.Edges.Select(row => new VerticalLaneEditorRow(row,
            row.Kind == TrackEdgeKind.Mainline && mainlineIds.Contains(row.Id) ? suggested[row.Id].SchematicLane : null,
            suggested[row.Id].SchematicLane)).ToArray();
        var content = new StackPanel();
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var defaults = CreateButton("套用預設垂直配置", (_, _) =>
        {
            RunEdit(() => SchematicVerticalLayout.FillDefaults(state.Draft, reset: true));
            ShowTracksPage();
        }, true);
        defaults.Name = "DefaultVerticalLanesButton";
        actions.Children.Add(defaults);
        var preview = CreateButton("預覽配線", (_, _) => { CommitTableDrafts(); ShowSchematic(); }, true);
        preview.Name = "VerticalLanePreviewButton";
        actions.Children.Add(preview);
        content.Children.Add(actions);
        var grid = new DataGrid
        {
            Name = "VerticalLaneGrid", ItemsSource = rows,
            AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
            MinHeight = 160, MaxHeight = 320, RowHeaderWidth = 0
        };
        AutomationProperties.SetName(grid, "軌道垂直位置調整表");
        grid.Columns.Add(new DataGridTextColumn { Header = "軌道區段", Binding = new Binding("Id"), IsReadOnly = true });
        grid.Columns.Add(new DataGridTextColumn { Header = "種類",
            Binding = new Binding("Kind") { Converter = new TraditionalChineseEnumConverter() }, IsReadOnly = true });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "垂直位置（單位）", Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            Binding = new Binding("SchematicLane")
            {
                Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
                ValidatesOnExceptions = true, NotifyOnValidationError = true
            }
        });
        grid.BeginningEdit += (_, args) =>
        {
            if (args.Row.Item is VerticalLaneEditorRow { CanAdjust: false })
                args.Cancel = true;
        };
        content.Children.Add(grid);
        return TrackPageGroup("垂直配置",
            "中線為 0，上行 −1、下行 +1；每單位 23 DIP，主線相隔 2 單位（46 DIP）。支線預設往 +2、+3 遞增，重疊區段分配不同單位。主線基準固定，支線正值向下，可輸入 −8 至 +8 的負值或小數；留空採自動配置。預設按鈕會重設主線與支線的垂直位置。按「套用」後隨專案儲存。",
            content);
    }
}