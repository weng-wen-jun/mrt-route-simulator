using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

/// <summary>Consumes Engine capacity results and an immutable playback snapshot; never advances a world.</summary>
public sealed class CapacityResultsWindow : Window
{
    private readonly TopologyProjectDocument document;
    private readonly SpatialCapacityProjectSettings settings;
    private readonly Func<PlaybackFrame?> snapshotProvider;
    private PlaybackFrame? snapshot;
    private readonly ComboBox direction = new() { Width = 100, ItemsSource = new[] { "順行", "逆行" }, SelectedIndex = 0 };
    private readonly ComboBox precision = new() { Width = 180, ItemsSource = new[] { "整數截斷（手冊）", "未截斷（3 位小數）" }, SelectedIndex = 0, ToolTip = "切換理論列車容量欄、路線容量圖與瓶頸線；理論實測比較固定使用未截斷值。" };
    private readonly ComboBox eventKind = new() { Width = 100, ItemsSource = new[] { "離站事件", "到站事件" }, SelectedIndex = 0 };
    private readonly TextBox start = new() { Width = 65, Text = "0" };
    private readonly TextBox end = new() { Width = 65, Text = "0" };
    private readonly TextBlock summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock measurement = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TabControl tabs = new();
    private readonly DataGrid table = new() { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, RowHeaderWidth = 0 };
    private readonly Canvas chart = new() { Height = 440, Background = Brushes.White };
    private readonly Grid content = new();
    private readonly List<CapacityPointValue> values = [];
    private List<CapacityTableRow> visibleRows = [];
    private bool ready;
    public CapacityResultsWindow(TopologyProjectDocument document, Func<PlaybackFrame?>? snapshotProvider = null)
    {
        this.document = document;
        settings = document.SpatialCapacity ?? throw new InvalidOperationException("請先配置容量參考點。");
        SpatialCapacityProjectAnalysis.Validate(settings);
        this.snapshotProvider = snapshotProvider ?? (() => null);
        Title = "容量分析結果"; Width = 1120; Height = 760; MinWidth = 800; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        EditorChrome.ApplyWindowChrome(this);
        foreach (var point in settings.Points)
        foreach (var d in Enum.GetValues<SpatialCapacityDirection>())
            values.Add(new(point, d, SpatialCapacityAnalysis.Calculate(SpatialCapacityProjectAnalysis.ToDefinition(point), settings.Global, settings.Train, d, settings.Mode)));
        NameScope.SetNameScope(this, new NameScope());
        Content = Build();
        snapshot = this.snapshotProvider();
        SetDefaultWindow();
        ready = true;
        RefreshResults();
    }
    private FrameworkElement Build()
    {
        var root = new Grid { Margin = new Thickness(16) };
        foreach (var h in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto }) root.RowDefinitions.Add(new() { Height = h });
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        tools.Children.Add(new TextBlock { Text = "方向", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        NameElement(direction, "CapacityChartDirection"); tools.Children.Add(direction);
        tools.Children.Add(new TextBlock { Text = "路線容量顯示", Margin = new Thickness(12, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        NameElement(precision, "CapacityDisplayPrecision"); tools.Children.Add(precision);
        tools.Children.Add(Button("儲存…", (_, _) => Save())); tools.Children.Add(Button("複製", (_, _) => Copy())); tools.Children.Add(Button("關閉", (_, _) => Close()));
        root.Children.Add(tools);
        summary.Margin = new Thickness(0, 0, 0, 10); Grid.SetRow(summary, 1); root.Children.Add(summary);
        var measurementPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        var selectors = new WrapPanel();
        selectors.Children.Add(new TextBlock { Text = "實測窗口（模擬分鐘） 起", VerticalAlignment = VerticalAlignment.Center });
        NameElement(start, "CapacityWindowStart"); selectors.Children.Add(start);
        selectors.Children.Add(new TextBlock { Text = "至", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) });
        NameElement(end, "CapacityWindowEnd"); selectors.Children.Add(end);
        NameElement(eventKind, "CapacityEventKind"); eventKind.Margin = new Thickness(10, 0, 0, 0); selectors.Children.Add(eventKind);
        selectors.Children.Add(Button("更新實測快照", (_, _) => { snapshot = snapshotProvider(); RefreshResults(); }));
        selectors.Children.Add(Button("11 小時／取 10 小時", (_, _) => SetWindow(30, 630)));
        selectors.Children.Add(Button("12 小時／取 10 小時", (_, _) => SetWindow(90, 690)));
        measurementPanel.Children.Add(selectors); measurement.Margin = new Thickness(0, 5, 0, 0); measurementPanel.Children.Add(measurement);
        Grid.SetRow(measurementPanel, 2); root.Children.Add(measurementPanel);
        NameElement(tabs, "CapacityResultTabs");
        foreach (var label in new[] { "結果表", "路線容量", "設計容量", "可達成容量", "理論與實測" }) tabs.Items.Add(new TabItem { Header = label });
        var body = new Grid(); body.RowDefinitions.Add(new() { Height = GridLength.Auto }); body.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(tabs); Grid.SetRow(content, 1); body.Children.Add(content);
        NameElement(chart, "CapacityResultChart"); NameElement(table, "CapacityResultTable");
        foreach (var (header, binding, width) in new[] { ("參考點", "Point", 130d), ("型式", "Kind", 105d), ("設計班距 (秒)", "Headway", 110d), ("列／小時", "Line", 85d), ("設計乘位／小時", "Design", 115d), ("可達成人／小時", "Achievable", 115d), ("實測列／小時", "Observed", 110d), ("事件數", "Count", 70d), ("實測－理論列／小時", "Difference", 145d), ("資料狀態", "Status", 250d) })
            table.Columns.Add(new DataGridTextColumn { Header = header, Binding = new System.Windows.Data.Binding(binding), Width = width });
        content.Children.Add(table);
        var scroll = new ScrollViewer { Content = chart, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        content.Children.Add(scroll); Grid.SetRow(body, 3); root.Children.Add(body);
        var note = new TextBlock { Text = "非中間站沿用單組幾何。紅線只在已確認為同一容量情境時顯示；列車實測通過量不代表最大容量，旅客容量為理論值。實測與理論設定可能尚未等條件套用。", TextWrapping = TextWrapping.Wrap, Foreground = UiTheme.TextMutedBrush, Margin = new Thickness(0, 10, 0, 0) };
        Grid.SetRow(note, 4); root.Children.Add(note);
        start.TextChanged += (_, _) => { if (ready) RefreshResults(); };
        end.TextChanged += (_, _) => { if (ready) RefreshResults(); };
        precision.SelectionChanged += (_, _) => { if (ready) RefreshResults(); };
        direction.SelectionChanged += (_, _) => { if (ready) RefreshResults(); };
        eventKind.SelectionChanged += (_, _) => { if (ready) RefreshResults(); };
        tabs.SelectionChanged += (_, _) => { if (ready) Draw(); };
        tabs.SelectedIndex = 0;
        SizeChanged += (_, _) => { if (ready) Draw(); };
        return root;
    }
    private void NameElement(FrameworkElement element, string name) { element.Name = name; RegisterName(name, element); AutomationProperties.SetName(element, name); }
    private static Button Button(string label, RoutedEventHandler handler) { var b = new Button { Content = label, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 5, 10, 5) }; b.Click += handler; return b; }
    private void SetDefaultWindow()
    {
        var t = snapshot?.CurrentTimeSeconds ?? 0;
        start.Text = t >= 39600 ? "30" : "0";
        end.Text = ((t >= 39600 ? t - 1800 : t) / 60).ToString("0.########", CultureInfo.InvariantCulture);
    }
    private void SetWindow(double from, double to) { start.Text = from.ToString(CultureInfo.InvariantCulture); end.Text = to.ToString(CultureInfo.InvariantCulture); RefreshResults(); }
    private bool Untruncated => precision.SelectedIndex == 1;
    private double LineValue(SpatialCapacityResult result) => Untruncated ? 3600 / result.DesignHeadwaySeconds : result.LineCapacityPerHour;
    private string LineText(SpatialCapacityResult result) => LineValue(result).ToString(Untruncated ? "0.000" : "N0", CultureInfo.CurrentCulture);
    private SpatialCapacityDirection SelectedDirection => direction.SelectedIndex == 1 ? SpatialCapacityDirection.Reverse : SpatialCapacityDirection.Forward;
    private TrainDirection SelectedTrainDirection => direction.SelectedIndex == 1 ? TrainDirection.Inbound : TrainDirection.Outbound;
    private List<CapacityPointValue> SelectedValues => values.Where(v => v.Direction == SelectedDirection).ToList();
    public void RefreshResults()
    {
        var valid = double.TryParse(start.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var from) && double.IsFinite(from) && from >= 0;
        valid &= double.TryParse(end.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var to) && double.IsFinite(to) && to > from;
        from *= 60; to *= 60;
        var hasSnapshot = snapshot is not null;
        valid &= hasSnapshot && to <= snapshot!.CurrentTimeSeconds + 1e-7;
        measurement.Text = !hasSnapshot ? "尚無實際模擬快照；可先查看理論結果。" : !valid ? $"量測範圍須在 0 至 {snapshot!.CurrentTimeSeconds / 60:0.###} 分鐘內，且結束大於起點；不顯示不完整窗口。" : $"實測快照至 {snapshot!.CurrentTimeSeconds / 60:0.###} 分鐘；窗口 [{from / 60:0.###}, {to / 60:0.###}) 分鐘，共 {(to - from) / 3600:0.###} 小時。計數解析度 {3600 / (to - from):0.###} 列／小時。終點折返請選「到站事件」。";
        visibleRows = [];
        foreach (var value in SelectedValues)
        {
            double? rate = null; int? count = null;
            var status = !hasSnapshot ? "尚無模擬" : !valid ? "窗口未完成或無效" : "";
            if (valid)
            {
                if (value.Point.Kind == SpatialReferencePointKind.Junction) status = "銜接點需獨立通過事件，未以站事件替代";
                else if (!(snapshot!.Context.TopologyInfrastructure?.Stations.ContainsKey(value.Point.StationId) ?? false)) status = "未對應實際 StationId";
                else
                {
                    var observed = Observe(snapshot.Events, value.Point.StationId, SelectedTrainDirection, from, to, eventKind.SelectedIndex == 1);
                    rate = observed.CountPerHour; count = observed.Count; status = count == 0 ? "窗口內 0 次事件" : "目前模擬事件；未證實與理論等條件";
                    if (observed.IgnoredMissingServiceRunIdCount > 0) status += $"；忽略 {observed.IgnoredMissingServiceRunIdCount} 筆缺車次 ID";
                }
            }
            visibleRows.Add(new(value.Point.ReferencePointId + (string.IsNullOrWhiteSpace(value.Point.Name) ? "" : " " + value.Point.Name), MainWindow.SpatialCapacityKindText(value.Point.Kind), F(value.Result.DesignHeadwaySeconds), LineText(value.Result), value.Result.DesignPassengerCapacityPerHour.ToString("N0"), value.Result.AchievablePassengerCapacityPerHour.ToString("N0"), rate is null ? "—" : F(rate.Value), count?.ToString() ?? "—", rate is null ? "—" : F(rate.Value - 3600 / value.Result.DesignHeadwaySeconds), status, rate));
        }
        table.ItemsSource = visibleRows;
        var bottle = settings.CompareAsSingleScenario ? SpatialCapacityScenarioAnalysis.Calculate(settings).Single(b => b.Direction == SelectedDirection) : null;
        summary.Text = bottle is null ? $"{MainWindow.SpatialCapacityDirectionText(SelectedDirection)} · {MainWindow.SpatialCapacityModeText(settings.Mode)} · 各點獨立分析，未啟用整線瓶頸比較。" : $"{MainWindow.SpatialCapacityDirectionText(SelectedDirection)}瓶頸：{bottle.ReferencePointId}　設計班距 {bottle.Result.DesignHeadwaySeconds:0.###} 秒　路線容量 {LineText(bottle.Result)} 列／小時　設計容量 {bottle.Result.DesignPassengerCapacityPerHour:N0} 乘位／小時　可達成 {bottle.Result.AchievablePassengerCapacityPerHour:N0} 人／小時";
        Draw();
    }
    private static CapacityObservationResult Observe(IReadOnlyList<SimulationEvent> events, string station, TrainDirection d, double from, double to, bool arrivals) =>
        CapacityObservationAnalysis.Analyze(events, station, d, from, to, arrivals ? CapacityObservationEventKind.Arrival : CapacityObservationEventKind.Departure);
    private static string F(double n) => n.ToString("0.###", CultureInfo.CurrentCulture);
    private static Brush Color(SpatialReferencePointKind kind) => kind switch { SpatialReferencePointKind.IntermediateStation => Brushes.CornflowerBlue, SpatialReferencePointKind.Junction => Brushes.Goldenrod, SpatialReferencePointKind.BeforeStationTurnback => Brushes.DarkOrange, SpatialReferencePointKind.AfterStationTurnback => Brushes.MediumPurple, _ => Brushes.Teal };
    private void Draw()
    {
        if (!ready) return;
        table.Visibility = tabs.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        content.Children[1].Visibility = tabs.SelectedIndex == 0 ? Visibility.Collapsed : Visibility.Visible;
        chart.Children.Clear();
        var points = SelectedValues;
        chart.Width = Math.Max(Math.Max(740, ActualWidth - 80), points.Count * 125 + 130);
        chart.Height = 440;
        var mode = tabs.SelectedIndex;
        if (mode <= 0) return;
        var comparison = mode == 4;
        var title = mode switch { 1 => Untruncated ? "路線容量（理論，未截斷，顯示 3 位小數）" : "路線容量（理論，整數截斷）", 2 => "設計容量（理論乘位）", 3 => "可達成容量（理論，含尖峰小時因子）", _ => "理論與實測列車流率（理論採未截斷 3600/h）" };
        Label(title + " · " + MainWindow.SpatialCapacityDirectionText(SelectedDirection), 22, 10, Brushes.Black, 16);
        Label(comparison ? "實測依所選站點事件及窗口；無資料顯示 —，不代表零容量。" : "藍：中間站　橙：站前折返　紫：站後折返　綠：中央避車線　黃：銜接點", 22, 40, Brushes.DimGray, 12);
        var data = points.Select(p => mode switch { 1 => LineValue(p.Result), 2 => p.Result.DesignPassengerCapacityPerHour, 3 => p.Result.AchievablePassengerCapacityPerHour, _ => 3600 / p.Result.DesignHeadwaySeconds }).ToArray();
        var max = data.Concat(comparison ? visibleRows.Where(r => r.ObservedValue.HasValue).Select(r => r.ObservedValue!.Value) : []).DefaultIfEmpty(1).Max();
        var area = new ChartPainter.ChartArea(88, 100, chart.Width - 118, 245);
        var ceiling = ChartPainter.DrawValueAxis(chart, area, Math.Max(1, max * 1.15), "");
        Label(mode == 2 ? "乘位／小時" : mode == 3 ? "人／小時" : "列／小時", 15, 76, Brushes.DimGray, 12);
        ChartPainter.DrawAxes(chart, area, "", "參考點");
        var step = points.Count == 0 ? area.Width : area.Width / points.Count;
        for (var i = 0; i < points.Count; i++)
        {
            var x = area.Left + step * (i + .5); var barWidth = Math.Min(48, step * .55);
            Bar(x - (comparison ? 45 : barWidth / 2), data[i], comparison ? 35 : barWidth, Color(points[i].Point.Kind), area, ceiling);
            if (comparison && visibleRows[i].ObservedValue is { } actual) Bar(x + 10, actual, 35, Brushes.SeaGreen, area, ceiling);
            else if (comparison) Label("—", x + 8, area.Bottom - 20, Brushes.DimGray);
            Label(points[i].Point.ReferencePointId, x - 25, area.Bottom + 10, Brushes.Black);
        }
        if (settings.CompareAsSingleScenario && points.Count > 0 && !comparison)
        {
            var b = SpatialCapacityScenarioAnalysis.Calculate(settings).Single(b => b.Direction == SelectedDirection).Result;
            var threshold = mode == 1 ? LineValue(b) : mode == 2 ? b.DesignPassengerCapacityPerHour : b.AchievablePassengerCapacityPerHour;
            var y = area.Bottom - threshold / ceiling * area.Height;
            chart.Children.Add(new Line { X1 = area.Left, X2 = area.Right, Y1 = y, Y2 = y, Stroke = Brushes.Firebrick, StrokeThickness = 1.8, Tag = "CapacityBottleneckLine" });
            Label("情境瓶頸 " + threshold.ToString(mode == 1 && Untruncated ? "0.000" : "N0", CultureInfo.CurrentCulture), area.Left + 5, Math.Max(area.Top - 20, y - 20), Brushes.Firebrick);
        }
        Label(comparison ? "各型式色：理論　綠：實測；數值標籤為列／小時。" : settings.CompareAsSingleScenario ? "紅線為已確認同一情境的理論瓶頸。" : "各點獨立；未啟用同一情境，故不畫整線瓶頸線。", 22, 385, Brushes.DimGray, 12);
        Label(comparison ? measurement.Text : "理論設定與實際營運條件需分開驗證；旅客容量並非實測旅客流。", 22, 410, Brushes.DimGray, 11);
    }
    private void Bar(double x, double value, double width, Brush fill, ChartPainter.ChartArea area, double ceiling)
    {
        var height = value / ceiling * area.Height;
        chart.Children.Add(new Rectangle { Width = width, Height = Math.Max(0, height), Fill = fill, Stroke = Brushes.SlateGray, StrokeThickness = .7, Margin = new Thickness(0), Tag = "CapacityBar" });
        var bar = chart.Children[^1]; Canvas.SetLeft(bar, x); Canvas.SetTop(bar, area.Bottom - height);
        Label(value.ToString(tabs.SelectedIndex == 4 || tabs.SelectedIndex == 1 && Untruncated ? "0.000" : "0.#", CultureInfo.CurrentCulture), x - 4, area.Bottom - height - 22, Brushes.Black, 12);
    }
    private void Label(string text, double x, double y, Brush color, double size = 13) { var label = new TextBlock { Text = text, Foreground = color, FontSize = size, MaxWidth = Math.Max(100, chart.Width - x - 15), TextTrimming = TextTrimming.CharacterEllipsis }; chart.Children.Add(label); Canvas.SetLeft(label, x); Canvas.SetTop(label, y); }
    public void ExportTo(string path)
    {
        if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) File.WriteAllText(path, TableText(","), new UTF8Encoding(true));
        else if (tabs.SelectedIndex == 0) throw new InvalidOperationException("請先選擇圖表分頁，再匯出圖片或 PDF。");
        else if (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) DiagramExportService.ExportPdf(chart, path, PdfPageSize.A4, false);
        else DiagramExportService.ExportPng(chart, path, 1.6);
    }
    private string TableText(string delimiter)
    {
        string Row(params string[] items) => string.Join(delimiter, items.Select(s => "\"" + s.Replace("\"", "\"\"") + "\""));
        return "方向：" + MainWindow.SpatialCapacityDirectionText(SelectedDirection) + Environment.NewLine + "量測窗口（模擬分鐘）：[" + start.Text + ", " + end.Text + ")；" + eventKind.SelectedItem + Environment.NewLine + "快照時間（模擬秒）：" + (snapshot?.CurrentTimeSeconds.ToString("R", CultureInfo.InvariantCulture) ?? "無") + "；理論模式：" + MainWindow.SpatialCapacityModeText(settings.Mode) + "；列車容量顯示：" + precision.SelectedItem + Environment.NewLine + Row("參考點", "型式", "設計班距秒", "列每小時", "設計乘位每小時", "可達成人每小時", "實測列每小時", "事件數", "實測減未截斷理論", "資料狀態") + Environment.NewLine + string.Join(Environment.NewLine, visibleRows.Select(r => Row(r.Point, r.Kind, r.Headway, r.Line, r.Design, r.Achievable, r.Observed, r.Count, r.Difference, r.Status)));
    }
    private void Save()
    {
        var dialog = new SaveFileDialog { Filter = tabs.SelectedIndex == 0 ? "容量結果 CSV|*.csv" : "圖表 PNG|*.png|圖表 PDF|*.pdf|容量結果 CSV|*.csv", FileName = "容量分析-" + MainWindow.SpatialCapacityDirectionText(SelectedDirection) };
        if (dialog.ShowDialog(this) != true) return;
        try { ExportTo(dialog.FileName); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { MessageBox.Show(this, ex.Message, "儲存失敗"); }
    }
    private void Copy()
    {
        try
        {
            if (tabs.SelectedIndex == 0) Clipboard.SetText(TableText("\t"));
            else { chart.UpdateLayout(); var image = new RenderTargetBitmap((int)Math.Ceiling(chart.Width), (int)Math.Ceiling(chart.Height), 96, 96, PixelFormats.Pbgra32); image.Render(chart); Clipboard.SetImage(image); }
        }
        catch (System.Runtime.InteropServices.COMException ex) { MessageBox.Show(this, ex.Message, "複製失敗"); }
    }
    private sealed record CapacityPointValue(ProjectSpatialReferencePoint Point, SpatialCapacityDirection Direction, SpatialCapacityResult Result);
    public sealed record CapacityTableRow(string Point, string Kind, string Headway, string Line, string Design, string Achievable, string Observed, string Count, string Difference, string Status, double? ObservedValue);
}
