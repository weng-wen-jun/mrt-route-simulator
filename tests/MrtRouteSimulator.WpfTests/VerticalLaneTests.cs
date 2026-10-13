using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

internal static class VerticalLaneTests
{
    private static readonly Assembly AppAssembly = typeof(MainWindow).Assembly;
    public static void Run(string root)
    {
        VerifyGeometry(4, false);
        VerifyGeometry(32, true);
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(Path.Combine(root,
            "samples", "14-大型-二十八站完整營運範例.mrtsim.json")));
        var layoutType = AppAssembly.GetType("MrtRouteSimulator.App.SchematicVerticalLayout")!;
        var defaults = (TopologyProjectDocument)layoutType.GetMethod("FillDefaults")!.Invoke(null, [document, true])!;
        TopologyProjectFormat.CreateRuntime(defaults);
        Require(defaults.Topology.Edges.Single(edge => edge.TrackEdgeId == "EDGE:DOWN:O12:O13:B-001").SchematicLane == 1,
            "未列入預設全停路徑的快速通過正線必須由有向接續繼承 +1。");
        foreach (var binding in defaults.DirectionRouteBindings)
        {
            var route = defaults.ServiceRoutes.Single(r => r.ServiceRouteId == binding.ServiceRouteId);
            foreach (var id in route.Traversals.Select(t => t.TrackEdgeId))
            {
                var edge = defaults.Topology.Edges.Single(e => e.TrackEdgeId == id);
                if (edge.Kind == TrackEdgeKind.Mainline)
                    Require(edge.SchematicLane == (binding.Direction == TrainDirection.Outbound ? 1 : -1), "主線預設必須是 +1/−1。");
            }
        }
        Require(defaults.Topology.Edges.Where(e => e.Kind is TrackEdgeKind.PassingTrack or TrackEdgeKind.Siding)
            .All(e => e.SchematicLane >= 2), "支線預設必須向 +2 以上分配。");
        Require(defaults.Topology.DirectedConnections.SequenceEqual(document.Topology.DirectedConnections)
            && defaults.ServiceRoutes.SequenceEqual(document.ServiceRoutes), "配置不得修改實體接續或服務路徑。");
        VerifyEditor(defaults);
        Console.WriteLine("PASS WPF vertical units, dense lanes, endpoint continuity and editable project round-trip");
    }

    private static void VerifyGeometry(int count, bool compact)
    {
        TrackEdgeDefinition Edge(string id, TrackEdgeKind kind, double lane, string from, string to) => new()
        {
            TrackEdgeId = id, Kind = kind, SchematicLane = lane,
            FromNodeId = from, ToNodeId = to, LengthMeters = 100, DefaultSpeedLimitMetersPerSecond = 15
        };
        var edges = new List<TrackEdgeDefinition>
        {
            Edge("SIDE2", TrackEdgeKind.Siding, 2, "A", "B"),
            Edge("SIDE3", TrackEdgeKind.PassingTrack, 3, "A", "B"),
            Edge("DOWN", TrackEdgeKind.Mainline, 1, "A", "B"),
            Edge("UP", TrackEdgeKind.Mainline, -1, "C", "D")
        };
        while (edges.Count < count) edges.Add(Edge("FILL" + edges.Count, TrackEdgeKind.Mainline, 1, "E", "F"));
        var geometryType = AppAssembly.GetType("MrtRouteSimulator.App.TopologySchematicGeometry")!;
        var original = geometryType.GetMethod("Build")!.Invoke(null,
            [edges.Select(e => (e.TrackEdgeId, new Point(0, 210), new Point(100, 210)))])!;
        var presentation = AppAssembly.GetType("MrtRouteSimulator.App.StationSchematicPresentation")!;
        var connections = new[]
        {
            new DirectedTrackConnectionDefinition("DOWN", TraversalDirection.Forward, "SIDE2", TraversalDirection.Forward),
            new DirectedTrackConnectionDefinition("DOWN", TraversalDirection.Forward, "SIDE3", TraversalDirection.Forward)
        };
        var result = (IDictionary)presentation.GetMethod("ApplyLanes")!.Invoke(null,
            [original, edges, 210d, 23d, null, connections, compact, null, false])!;
        Point At(string id, double ratio)
        {
            var geometry = result[id]!;
            return (Point)geometry.GetType().GetMethod("PointAt")!.Invoke(geometry, [ratio])!;
        }
        Require(Math.Abs(At("DOWN", .5).Y - 233) < .001 && Math.Abs(At("UP", .5).Y - 187) < .001, "主線每側一單位。");
        Require(Math.Abs(At("SIDE2", .5).Y - 256) < .001 && Math.Abs(At("SIDE3", .5).Y - 279) < .001,
            "大型路線或 compact 模式不可壓縮 +2/+3 單位。");
        Require(Math.Abs(At("SIDE2", 0).Y - At("DOWN", 0).Y) < .001
            && Math.Abs(At("SIDE2", 1).Y - At("DOWN", 1).Y) < .001, "支線端點必須接回已配置的主線。");
    }

    private static void VerifyEditor(TopologyProjectDocument document)
    {
        var editorType = AppAssembly.GetType("MrtRouteSimulator.App.TopologyEditorWindow")!;
        var pageType = AppAssembly.GetType("MrtRouteSimulator.App.ProjectWorkspacePage")!;
        var editor = (Window)Activator.CreateInstance(editorType, document, Enum.Parse(pageType, "Tracks"))!;
        editor.ShowInTaskbar = false; editor.Left = -10000; editor.Top = -10000;
        editor.WindowStartupLocation = WindowStartupLocation.Manual;
        editor.Show();
        try
        {
            editor.UpdateLayout();
            var grid = Descendants<DataGrid>(editor).Single(g => g.Name == "VerticalLaneGrid");
            var laneColumn = (DataGridTextColumn)grid.Columns[2];
            Require(laneColumn.Binding is Binding { Path.Path: "SchematicLane", Mode: BindingMode.TwoWay },
                "可調欄位必須繫結原 editor VM。");
            var row = grid.Items.Cast<object>().First(r => (TrackEdgeKind)r.GetType().GetProperty("Kind")!.GetValue(r)! is TrackEdgeKind.PassingTrack or TrackEdgeKind.Siding);
            grid.SelectedItem = row; grid.CurrentCell = new DataGridCellInfo(row, laneColumn);
            grid.ScrollIntoView(row, laneColumn); grid.UpdateLayout();
            Require(grid.BeginEdit(), "垂直位置欄位必須能編輯。");
            grid.UpdateLayout();
            var text = (TextBox)laneColumn.GetCellContent(row);
            text.Text = "3.5";
            Require(grid.CommitEdit(DataGridEditingUnit.Cell, true) && grid.CommitEdit(DataGridEditingUnit.Row, true), "小數單位編輯須提交。");
            editorType.GetMethod("CommitTableDrafts", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null);
            var state = editorType.GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
            var draft = (TopologyProjectDocument)state.GetType().GetProperty("Draft")!.GetValue(state)!;
            var id = (string)row.GetType().GetProperty("Id")!.GetValue(row)!;
            Require(draft.Topology.Edges.Single(e => e.TrackEdgeId == id).SchematicLane == 3.5, "UI 調整必須寫入草稿。");
            var roundTrip = TopologyProjectFormat.Deserialize(TopologyProjectFormat.Serialize(draft));
            Require(roundTrip.Topology.Edges.Single(e => e.TrackEdgeId == id).SchematicLane == 3.5, "單位調整必須隨專案保存。");
            Require(document.Topology.Edges.Single(e => e.TrackEdgeId == id).SchematicLane != 3.5, "草稿不得污染原專案。");
            TopologyProjectFormat.CreateRuntime(roundTrip);
            var button = Descendants<Button>(editor).Single(b => b.Name == "DefaultVerticalLanesButton");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            editor.UpdateLayout();
            Require(Descendants<DataGrid>(editor).Any(g => g.Name == "VerticalLaneGrid"), "預設配置後仍須提供調整介面。");
        }
        finally { editor.Close(); }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants<T>(System.Windows.Media.VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}