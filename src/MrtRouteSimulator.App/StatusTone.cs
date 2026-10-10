using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MrtRouteSimulator.App;

public enum StatusTone
{
    Neutral,
    Active,
    Success,
    Caution,
    Danger
}

/// <summary>
/// 結果表狀態文字到色點的對照。只看文字，不重新計算任何數值；
/// 程式新增狀態文字時請補進對照表（ResultPageTests 會檢查）。
/// </summary>
public static class StatusTones
{
    private static readonly Dictionary<string, StatusTone> Map = new(StringComparer.Ordinal)
    {
        ["侵入安全距離"] = StatusTone.Danger,
        ["碰撞停止"] = StatusTone.Danger,
        ["障礙急停"] = StatusTone.Danger,
        ["接近警戒"] = StatusTone.Caution,
        ["需要制動"] = StatusTone.Caution,
        ["已抵達"] = StatusTone.Success,
        ["完成"] = StatusTone.Success,
        ["安全"] = StatusTone.Success,
        ["可比較"] = StatusTone.Success,
        ["已發車"] = StatusTone.Active,
        ["停站中"] = StatusTone.Active,
        ["運行中"] = StatusTone.Active,
        ["停站"] = StatusTone.Active,
        ["加速"] = StatusTone.Active,
        ["巡航"] = StatusTone.Active,
        ["惰行"] = StatusTone.Active,
        ["煞車"] = StatusTone.Active,
        ["進站平順煞車"] = StatusTone.Active,
        ["到站"] = StatusTone.Active,
        ["駛入尾軌"] = StatusTone.Active,
        ["尾軌返回"] = StatusTone.Active,
        ["駛入折返線"] = StatusTone.Active,
        ["折返線返回"] = StatusTone.Active,
        ["折返"] = StatusTone.Active,
        ["待發"] = StatusTone.Neutral,
        ["—"] = StatusTone.Neutral,
        ["V1 理論基準"] = StatusTone.Neutral,
        ["V2 尚未抵達"] = StatusTone.Neutral,
        ["跨站不比較"] = StatusTone.Neutral,
        ["折返節點不適用 V1"] = StatusTone.Neutral,
        ["退出營運"] = StatusTone.Neutral
    };

    public static IReadOnlyDictionary<string, StatusTone> KnownStatuses => Map;

    public static StatusTone Classify(string? status) =>
        status is not null && Map.TryGetValue(status.Trim(), out var tone) ? tone : StatusTone.Neutral;

    public static SolidColorBrush Brush(StatusTone tone) => tone switch
    {
        StatusTone.Danger => UiTheme.DangerBrush,
        StatusTone.Caution => UiTheme.CautionBrush,
        StatusTone.Success => UiTheme.SuccessBrush,
        StatusTone.Active => UiTheme.AccentBrush,
        _ => UiTheme.TextSubtleBrush
    };
}

/// <summary>狀態欄色點：把儲存格的狀態文字轉成色點畫筆。</summary>
public sealed class StatusToneBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        StatusTones.Brush(StatusTones.Classify(value as string));

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException("狀態色點只供顯示，不支援反向轉換。");
}
