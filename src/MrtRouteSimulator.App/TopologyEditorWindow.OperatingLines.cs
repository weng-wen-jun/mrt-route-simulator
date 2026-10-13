using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

internal sealed partial class TopologyEditorWindow
{
    private readonly ObservableCollection<OperatingLineEditorViewModel> operatingLineRows = [];

    private void ShowOperatingLinesPage()
    {
        CommitOperationsPageEditors();
        LoadOperatingLineRows();

        var panel = NewPage(
            "營運路線網路",
            "一次規劃 1 至 8 條主線／支線。每條線可選單線或雙線；支線以父線和接軌里程接入共用幹線。本線區段長度與本線站數只描述新建股道區段，不含父線共享幹線與共享站。這些欄位是可保存的建模 metadata，實際執行仍使用建立後的 Schema 8 topology 與服務路徑。");
        var content = new StackPanel();

        var countPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        countPanel.Children.Add(EditorChrome.FieldLabel("營運路線數量"));
        var count = new ComboBox
        {
            ItemsSource = Enumerable.Range(1, 8).ToArray(),
            SelectedItem = Math.Clamp(operatingLineRows.Count, 1, 8),
            Width = 72,
            Margin = new Thickness(8, 0, 0, 0)
        };
        AutomationProperties.SetName(count, "營運路線數量");
        count.SelectionChanged += (_, _) =>
        {
            if (count.SelectedItem is int requested) ResizeOperatingLineRows(requested);
        };
        countPanel.Children.Add(count);
        content.Children.Add(countPanel);

        var grid = new DataGrid
        {
            ItemsSource = operatingLineRows,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            MinHeight = 310,
            RowHeaderWidth = 0
        };
        AutomationProperties.SetName(grid, "營運路線規劃表");
        grid.Columns.Add(OperatingLineColumn("路線 ID", nameof(OperatingLineEditorViewModel.LineId), 105));
        grid.Columns.Add(OperatingLineColumn("名稱", nameof(OperatingLineEditorViewModel.Name), 130));
        grid.Columns.Add(new DataGridComboBoxColumn
        {
            Header = "軌道數",
            ItemsSource = new[] { 1, 2 },
            SelectedItemBinding = new Binding(nameof(OperatingLineEditorViewModel.TrackCount))
                { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = 72
        });
        grid.Columns.Add(OperatingLineColumn("父線 ID", nameof(OperatingLineEditorViewModel.ParentLineId), 115));
        grid.Columns.Add(OperatingLineColumn("接軌 km", nameof(OperatingLineEditorViewModel.JunctionChainageKm), 90));
        grid.Columns.Add(OperatingLineColumn("本線區段長度 km", nameof(OperatingLineEditorViewModel.LengthKm), 130));
        grid.Columns.Add(OperatingLineColumn("本線站數", nameof(OperatingLineEditorViewModel.StationCount), 90));
        content.Children.Add(EditorChrome.Card(grid, "路線結構"));

        var hint = EditorChrome.Hint("第一列應為不填父線的主線；支線只能引用前面已列出的父線，接軌里程由父線起點計算。支線的本線區段長度與本線站數不含父線共享部分；建立後的營運路徑會再串接共享幹線。單線會建立同股道的雙向互斥資源。按下方按鈕前只會修改這個表格，不會重建草稿。");
        hint.Margin = new Thickness(0, 8, 0, 8);
        content.Children.Add(hint);
        content.Children.Add(CreateNamedButton("檢視並建立網路起稿…", "依營運路線建立網路起稿", (_, _) =>
        {
            grid.CommitEdit(DataGridEditingUnit.Cell, true);
            grid.CommitEdit(DataGridEditingUnit.Row, true);
            BuildOperatingNetworkDraft();
        }, false));

        panel.Children.Add(content);
        ShowWorkspacePage(WrapOperationsPage(panel));
        RegisterPageCommitHook("operations.operating-lines", () => { });
    }

    private void LoadOperatingLineRows()
    {
        operatingLineRows.Clear();
        foreach (var line in state.Draft.OperatingLines ?? [])
            operatingLineRows.Add(new OperatingLineEditorViewModel(line));
        if (operatingLineRows.Count == 0)
            operatingLineRows.Add(CreateOperatingLineRow(0));
    }

    private void ResizeOperatingLineRows(int requested)
    {
        while (operatingLineRows.Count < requested)
            operatingLineRows.Add(CreateOperatingLineRow(operatingLineRows.Count));
        while (operatingLineRows.Count > requested)
            operatingLineRows.RemoveAt(operatingLineRows.Count - 1);
    }

    private OperatingLineEditorViewModel CreateOperatingLineRow(int index)
    {
        var number = index + 1;
        var id = number == 1 ? "MAIN" : $"BRANCH-{number - 1}";
        return new OperatingLineEditorViewModel(new OperatingLineDefinition
        {
            LineId = id,
            Name = number == 1 ? "主線" : $"支線 {number - 1}",
            TrackCount = 2,
            ParentLineId = number == 1 ? null : operatingLineRows.FirstOrDefault()?.LineId ?? "MAIN",
            JunctionChainageMeters = number == 1 ? 0 : 1000,
            LengthMeters = 2000,
            StationCount = 3,
            OutboundServiceRouteId = $"ROUTE:{id}:OUTBOUND",
            InboundServiceRouteId = $"ROUTE:{id}:INBOUND"
        });
    }

    private void BuildOperatingNetworkDraft()
    {
        var definitions = operatingLineRows.Select(row => row.ToDomain()).ToArray();
        var summary = string.Join(Environment.NewLine, definitions.Select((line, index) =>
            $"{index + 1}. {line.Name}（{line.LineId}）：{line.TrackCount} 軌、" +
            (line.ParentLineId is null ? "主線" : $"父線 {line.ParentLineId}／接軌 {line.JunctionChainageMeters / 1000:0.###} km") +
            $"、本線區段長 {line.LengthMeters / 1000:0.###} km、本線 {line.StationCount} 站"));
        var answer = MessageBox.Show(
            this,
            $"將依下列 {definitions.Length} 條營運路線重建編輯器草稿：\n\n{summary}\n\n" +
            "重建範圍包含軌道、車站、服務路徑、停站模式與班表。主畫面目前專案只有在工作區按「套用」後才會變更。",
            "確認重建營運網路草稿",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.OK) return;

        try
        {
            state.Replace(OperatingNetworkBuilder.Build(state.Draft, definitions));
            ReloadViewModels();
            RefreshValidation();
            ShowOperatingLinesPage();
        }
        catch (SimulationValidationException exception)
        {
            ShowErrors(exception.Errors);
        }
        catch (InvalidOperationException exception)
        {
            ShowErrors([exception.Message]);
        }
    }

    private static DataGridTextColumn OperatingLineColumn(string header, string property, double width) => new()
    {
        Header = header,
        Binding = new Binding(property) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
        Width = width
    };
}

internal sealed class OperatingLineEditorViewModel(OperatingLineDefinition source)
{
    public string LineId { get; set; } = source.LineId;
    public string Name { get; set; } = source.Name;
    public int TrackCount { get; set; } = source.TrackCount;
    public string ParentLineId { get; set; } = source.ParentLineId ?? "";
    public double JunctionChainageKm { get; set; } = source.JunctionChainageMeters / 1000;
    public double LengthKm { get; set; } = source.LengthMeters / 1000;
    public int StationCount { get; set; } = source.StationCount;
    public string OutboundServiceRouteId { get; set; } = source.OutboundServiceRouteId;
    public string InboundServiceRouteId { get; set; } = source.InboundServiceRouteId;

    public OperatingLineDefinition ToDomain() => new()
    {
        LineId = LineId.Trim(),
        Name = Name.Trim(),
        TrackCount = TrackCount,
        ParentLineId = EditorValue.EmptyToNull(ParentLineId),
        JunctionChainageMeters = JunctionChainageKm * 1000,
        LengthMeters = LengthKm * 1000,
        StationCount = StationCount,
        OutboundServiceRouteId = string.IsNullOrWhiteSpace(OutboundServiceRouteId)
            ? $"ROUTE:{LineId.Trim()}:OUTBOUND"
            : OutboundServiceRouteId.Trim(),
        InboundServiceRouteId = string.IsNullOrWhiteSpace(InboundServiceRouteId)
            ? $"ROUTE:{LineId.Trim()}:INBOUND"
            : InboundServiceRouteId.Trim()
    };
}
