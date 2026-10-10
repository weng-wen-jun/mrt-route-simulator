using System.Windows.Media;

namespace MrtRouteSimulator.App;

/// <summary>
/// App 顏色與共用畫筆的單一來源。畫筆全部凍結，可在每一幀重用而不重建；
/// XAML 需要時由 code-behind 或 x:Static 引用，不在 App.xaml 重複定義同一色值。
/// </summary>
public static class UiTheme
{
    public const double OccupancyGlowThickness = 16;

    public static readonly Color CanvasBackground = Color.FromRgb(0xFB, 0xFC, 0xFE);
    public static readonly Color TextStrong = Color.FromRgb(0x1E, 0x29, 0x3B);
    public static readonly Color TextMuted = Color.FromRgb(0x64, 0x74, 0x8B);
    public static readonly Color TextSubtle = Color.FromRgb(0x94, 0xA3, 0xB8);
    public static readonly Color Hairline = Color.FromRgb(0xCB, 0xD5, 0xE1);
    public static readonly Color RailUp = Color.FromRgb(0x2F, 0x7F, 0xC1);
    public static readonly Color RailUpSoft = Color.FromRgb(0x8C, 0xB8, 0xE0);
    public static readonly Color RailDown = Color.FromRgb(0xE8, 0x83, 0x3A);
    public static readonly Color RailDownSoft = Color.FromRgb(0xF2, 0xB4, 0x88);
    public static readonly Color RailNeutral = Color.FromRgb(0x94, 0xA3, 0xB8);
    public static readonly Color RailNeutralStrong = Color.FromRgb(0x47, 0x55, 0x69);
    public static readonly Color PlatformFill = Colors.White;
    public static readonly Color PlatformBidirectional = Color.FromRgb(0x64, 0x74, 0x8B);
    public static readonly Color StationBadgeFill = Color.FromRgb(0xEE, 0xF2, 0xF7);
    public static readonly Color OccupancyGlow = Color.FromArgb(0x80, 0xFF, 0xC9, 0x3C);
    public static readonly Color Danger = Color.FromRgb(0xC4, 0x30, 0x30);

    // 主視窗外殼（子專案 C1）。
    public static readonly Color AppBar = Color.FromRgb(0x1B, 0x25, 0x38);
    public static readonly Color AppBarRaised = Color.FromRgb(0x2C, 0x3A, 0x55);
    public static readonly Color AppBarText = Colors.White;
    public static readonly Color AppBarMuted = Color.FromRgb(0x9F, 0xB0, 0xC8);
    public static readonly Color WindowBackground = Color.FromRgb(0xF5, 0xF7, 0xFA);
    public static readonly Color Surface = Colors.White;
    public static readonly Color Border = Color.FromRgb(0xE2, 0xE8, 0xEF);
    public static readonly Color Accent = Color.FromRgb(0x2F, 0x7F, 0xC1);
    public static readonly Color NavSelected = Color.FromRgb(0xEA, 0xF2, 0xFB);
    public static readonly Color NavHover = Color.FromRgb(0xF1, 0xF5, 0xF9);
    public static readonly Color Primary = Color.FromRgb(0xE8, 0x6D, 0x2D);
    public static readonly Color PrimaryHover = Color.FromRgb(0xB8, 0x47, 0x19);
    public static readonly Color TimeAccent = Color.FromRgb(0x7E, 0xE0, 0xB5);
    public static readonly Color Success = Color.FromRgb(0x16, 0x86, 0x6B);
    public static readonly Color WarningSurface = Color.FromRgb(0xFF, 0xF4, 0xEC);
    public static readonly Color WarningBorder = Color.FromRgb(0xF4, 0xC3, 0xA0);
    public static readonly Color WarningText = Color.FromRgb(0x9A, 0x3A, 0x12);
    public static readonly Color StatusBar = Color.FromRgb(0xE9, 0xEE, 0xF4);
    public static readonly Color TooltipBackground = Color.FromRgb(0x1E, 0x29, 0x3B);
    public static readonly Color TableHeader = Color.FromRgb(0xF8, 0xFA, 0xFC);
    public static readonly Color GridLine = Color.FromRgb(0xF1, 0xF5, 0xF9);
    public static readonly Color Caution = Color.FromRgb(0xD9, 0xA4, 0x00);

    // 每台車一色，跨配線圖、運行圖與速度曲線共用；色相避開上下行軌道與危險紅。
    public static readonly Color[] VehiclePalette =
    [
        Color.FromRgb(0x7C, 0x5C, 0xD6),
        Color.FromRgb(0x0F, 0x9D, 0x8A),
        Color.FromRgb(0xC2, 0x41, 0x8F),
        Color.FromRgb(0x6E, 0x1A, 0x3A),
        Color.FromRgb(0x3F, 0x9C, 0x35),
        Color.FromRgb(0x54, 0x68, 0xD4),
        Color.FromRgb(0x9B, 0x4D, 0xCA),
        Color.FromRgb(0x6E, 0x73, 0x16)
    ];

    // 鎖定進路維持紅／洋紅系，與下行橘軌區隔。
    public static readonly Color[] LockedRoutePalette =
    [
        Color.FromRgb(0xD9, 0x3A, 0x4A),
        Color.FromRgb(0xC0, 0x26, 0x6D),
        Color.FromRgb(0xE0, 0x47, 0x5F),
        Color.FromRgb(0xA8, 0x32, 0x6E),
        Color.FromRgb(0xCC, 0x3D, 0x3D)
    ];

    public static readonly SolidColorBrush CanvasBackgroundBrush = Frozen(CanvasBackground);
    public static readonly SolidColorBrush TextStrongBrush = Frozen(TextStrong);
    public static readonly SolidColorBrush TextMutedBrush = Frozen(TextMuted);
    public static readonly SolidColorBrush TextSubtleBrush = Frozen(TextSubtle);
    public static readonly SolidColorBrush HairlineBrush = Frozen(Hairline);
    public static readonly SolidColorBrush RailUpBrush = Frozen(RailUp);
    public static readonly SolidColorBrush RailUpSoftBrush = Frozen(RailUpSoft);
    public static readonly SolidColorBrush RailDownBrush = Frozen(RailDown);
    public static readonly SolidColorBrush RailDownSoftBrush = Frozen(RailDownSoft);
    public static readonly SolidColorBrush RailNeutralBrush = Frozen(RailNeutral);
    public static readonly SolidColorBrush RailNeutralStrongBrush = Frozen(RailNeutralStrong);
    public static readonly SolidColorBrush PlatformFillBrush = Frozen(PlatformFill);
    public static readonly SolidColorBrush PlatformBidirectionalBrush = Frozen(PlatformBidirectional);
    public static readonly SolidColorBrush StationBadgeFillBrush = Frozen(StationBadgeFill);
    public static readonly SolidColorBrush OccupancyGlowBrush = Frozen(OccupancyGlow);
    public static readonly SolidColorBrush DangerBrush = Frozen(Danger);

    public static readonly SolidColorBrush AppBarBrush = Frozen(AppBar);
    public static readonly SolidColorBrush AppBarRaisedBrush = Frozen(AppBarRaised);
    public static readonly SolidColorBrush AppBarTextBrush = Frozen(AppBarText);
    public static readonly SolidColorBrush AppBarMutedBrush = Frozen(AppBarMuted);
    public static readonly SolidColorBrush WindowBackgroundBrush = Frozen(WindowBackground);
    public static readonly SolidColorBrush SurfaceBrush = Frozen(Surface);
    public static readonly SolidColorBrush BorderBrush = Frozen(Border);
    public static readonly SolidColorBrush AccentBrush = Frozen(Accent);
    public static readonly SolidColorBrush NavSelectedBrush = Frozen(NavSelected);
    public static readonly SolidColorBrush NavHoverBrush = Frozen(NavHover);
    public static readonly SolidColorBrush PrimaryBrush = Frozen(Primary);
    public static readonly SolidColorBrush PrimaryHoverBrush = Frozen(PrimaryHover);
    public static readonly SolidColorBrush TimeAccentBrush = Frozen(TimeAccent);
    public static readonly SolidColorBrush SuccessBrush = Frozen(Success);
    public static readonly SolidColorBrush WarningSurfaceBrush = Frozen(WarningSurface);
    public static readonly SolidColorBrush WarningBorderBrush = Frozen(WarningBorder);
    public static readonly SolidColorBrush WarningTextBrush = Frozen(WarningText);
    public static readonly SolidColorBrush StatusBarBrush = Frozen(StatusBar);
    public static readonly SolidColorBrush TooltipBackgroundBrush = Frozen(TooltipBackground);
    public static readonly SolidColorBrush TableHeaderBrush = Frozen(TableHeader);
    public static readonly SolidColorBrush GridLineBrush = Frozen(GridLine);
    public static readonly SolidColorBrush CautionBrush = Frozen(Caution);

    public static readonly SolidColorBrush[] VehicleBrushes = VehiclePalette.Select(Frozen).ToArray();
    public static readonly SolidColorBrush[] LockedRouteBrushes = LockedRoutePalette.Select(Frozen).ToArray();

    public static SolidColorBrush VehicleBrush(int vehicleIndex) =>
        VehicleBrushes[((vehicleIndex % VehicleBrushes.Length) + VehicleBrushes.Length) % VehicleBrushes.Length];

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
