using System.Windows;
using System.Windows.Controls;

namespace MrtRouteSimulator.App;

public enum ResultColumnKind
{
    Text,
    Numeric,
    Status
}

/// <summary>
/// 結果表的欄位呈現規則：文字欄截斷並提示完整內容、數字欄靠右等寬、狀態欄加色點。
/// 只作用於標了 ResultTable.Enabled 的 DataGrid；欄位種類以 ResultTable.Kind 標記，預設為 Text。
/// </summary>
public static class ResultTable
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(ResultTable), new PropertyMetadata(false, OnEnabledChanged));

    public static readonly DependencyProperty KindProperty = DependencyProperty.RegisterAttached(
        "Kind", typeof(ResultColumnKind), typeof(ResultTable), new PropertyMetadata(ResultColumnKind.Text));

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    public static ResultColumnKind GetKind(DependencyObject element) => (ResultColumnKind)element.GetValue(KindProperty);

    public static void SetKind(DependencyObject element, ResultColumnKind value) => element.SetValue(KindProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid || e.NewValue is not true) return;
        // XAML 先設定 DataGrid 屬性、再逐一加入欄位，所以欄位在加入時套用；Loaded 再補一次以防萬一。
        ApplyAll(grid);
        grid.Columns.CollectionChanged += (_, args) =>
        {
            if (args.NewItems is null) return;
            foreach (DataGridColumn column in args.NewItems) Apply(column);
        };
        grid.Loaded += (_, _) => ApplyAll(grid);
    }

    private static void ApplyAll(DataGrid grid)
    {
        foreach (var column in grid.Columns) Apply(column);
    }

    private static void Apply(DataGridColumn column)
    {
        if (column is not DataGridTextColumn text) return;
        var kind = GetKind(column);
        text.ElementStyle = FindStyle(kind == ResultColumnKind.Numeric ? "NumericCell" : "TextCell");
        if (kind == ResultColumnKind.Numeric) text.HeaderStyle = FindStyle("NumericHeader");
        if (kind == ResultColumnKind.Status) text.CellStyle = FindStyle("StatusCell");
    }

    private static Style FindStyle(string key) =>
        Application.Current?.TryFindResource(key) as Style
        ?? throw new InvalidOperationException($"找不到結果表樣式「{key}」；請確認 App.xaml 已合併 Themes/Controls.xaml。");
}
