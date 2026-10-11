using System.Windows;
using System.Windows.Media;

namespace MrtRouteSimulator.App;

/// <summary>
/// 結果圖表（速度曲線、相鄰列車距離圖、時間－里程運行圖、匯出頁）的色票、線型與字型。
/// 畫筆都直接引用 <see cref="UiTheme"/> 的凍結畫筆，不另定義色值；圖表程式只從這裡取色，
/// 讓畫面、PNG 與 PDF 一致。
/// </summary>
public static class ChartTheme
{
    public static readonly SolidColorBrush Background = UiTheme.CanvasBackgroundBrush;
    public static readonly SolidColorBrush Grid = UiTheme.BorderBrush;
    public static readonly SolidColorBrush Axis = UiTheme.TextSubtleBrush;
    public static readonly SolidColorBrush AxisLabel = UiTheme.TextMutedBrush;
    public static readonly SolidColorBrush Title = UiTheme.TextStrongBrush;
    public static readonly SolidColorBrush LegendText = UiTheme.TextMutedBrush;
    public static readonly SolidColorBrush Message = UiTheme.TextMutedBrush;
    public static readonly SolidColorBrush Annotation = UiTheme.TextSubtleBrush;
    public static readonly SolidColorBrush PrimarySeries = UiTheme.AccentBrush;
    public static readonly SolidColorBrush LimitSeries = UiTheme.TextMutedBrush;
    public static readonly SolidColorBrush ThresholdSeries = UiTheme.RailNeutralStrongBrush;
    public static readonly SolidColorBrush DangerSeries = UiTheme.DangerBrush;
    public static readonly SolidColorBrush NeutralSeries = UiTheme.RailNeutralStrongBrush;
    public static readonly SolidColorBrush EventStation = UiTheme.SuccessBrush;
    public static readonly SolidColorBrush EventTerminal = UiTheme.RailNeutralStrongBrush;
    public static readonly SolidColorBrush EventSafety = UiTheme.DangerBrush;
    public static readonly SolidColorBrush MarkerOutline = UiTheme.SurfaceBrush;
    public static readonly SolidColorBrush TailTrack = UiTheme.RailDownBrush;
    public static readonly SolidColorBrush ExportPage = UiTheme.SurfaceBrush;

    /// <summary>線性路線圖的速限區塊底色：TextSubtle 加透明度，是唯一的衍生畫筆。</summary>
    public static readonly SolidColorBrush LimitZone = Translucent(UiTheme.TextSubtle, 0x2A);

    /// <summary>速限、動態安全距離虛線。</summary>
    public static readonly DoubleCollection LongDash = FrozenDash(5, 3);

    /// <summary>障礙物煞車需求點線、換向標記。</summary>
    public static readonly DoubleCollection ShortDot = FrozenDash(2, 3);

    /// <summary>運行圖尾軌格線。</summary>
    public static readonly DoubleCollection TailDash = FrozenDash(4, 3);

    /// <summary>運行圖計畫／理論線。</summary>
    public static readonly DoubleCollection PlannedDash = FrozenDash(6, 4);

    /// <summary>圖例的虛線樣本（線寬 2，短樣本上要看得出虛線）。</summary>
    public static readonly DoubleCollection LegendDash = FrozenDash(3, 2);

    /// <summary>圖例的點線樣本。</summary>
    public static readonly DoubleCollection LegendDot = FrozenDash(1, 1.5);

    public const double GridThickness = 1;
    public const double AxisThickness = 1.2;
    public const double AxisLabelFontSize = 10;
    public const double TimeTickFontSize = 9;
    public const double TitleFontSize = 13;
    public const double LegendFontSize = 11;
    public const double MessageFontSize = 12;

    private static readonly FontFamily FallbackFont = new("Microsoft JhengHei UI, Segoe UI");
    private static FontFamily? _appFont;

    /// <summary>
    /// App 字型（Themes/Controls.xaml 的 AppFont）；匯出與尚未掛上視窗的畫布也用同一字型。
    /// 第一次取得資源後快取，車站標籤量測會反覆呼叫；尚無 Application 時回傳備用字型且不快取。
    /// </summary>
    public static FontFamily Font
    {
        get
        {
            if (_appFont is not null) return _appFont;
            if (Application.Current?.TryFindResource("AppFont") is FontFamily appFont) return _appFont = appFont;
            return FallbackFont;
        }
    }

    private static SolidColorBrush Translucent(Color color, byte alpha)
    {
        color.A = alpha;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static DoubleCollection FrozenDash(params double[] values)
    {
        var dash = new DoubleCollection(values);
        dash.Freeze();
        return dash;
    }
}
