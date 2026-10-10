using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using MrtRouteSimulator.App;

/// <summary>結果分頁（子專案 C2）的元件、版面與表格規則測試。</summary>
internal static class ResultPageTests
{
    public static void Run(string root)
    {
        VerifyStatusTones(root);
        Console.WriteLine("PASS WPF result pages");
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static readonly (string Status, StatusTone Tone)[] ExpectedTones =
    [
        ("侵入安全距離", StatusTone.Danger), ("碰撞停止", StatusTone.Danger), ("障礙急停", StatusTone.Danger),
        ("接近警戒", StatusTone.Caution), ("需要制動", StatusTone.Caution),
        ("已抵達", StatusTone.Success), ("完成", StatusTone.Success), ("安全", StatusTone.Success), ("可比較", StatusTone.Success),
        ("已發車", StatusTone.Active), ("停站中", StatusTone.Active), ("運行中", StatusTone.Active), ("停站", StatusTone.Active),
        ("加速", StatusTone.Active), ("巡航", StatusTone.Active), ("惰行", StatusTone.Active), ("煞車", StatusTone.Active),
        ("進站平順煞車", StatusTone.Active), ("到站", StatusTone.Active), ("駛入尾軌", StatusTone.Active),
        ("尾軌返回", StatusTone.Active), ("駛入折返線", StatusTone.Active), ("折返線返回", StatusTone.Active),
        ("折返", StatusTone.Active),
        ("待發", StatusTone.Neutral), ("—", StatusTone.Neutral), ("V1 理論基準", StatusTone.Neutral),
        ("V2 尚未抵達", StatusTone.Neutral), ("跨站不比較", StatusTone.Neutral), ("折返節點不適用 V1", StatusTone.Neutral),
        ("退出營運", StatusTone.Neutral)
    ];

    private static void VerifyStatusTones(string root)
    {
        foreach (var (status, tone) in ExpectedTones)
            Require(StatusTones.Classify(status) == tone, $"狀態「{status}」應為 {tone}，實際 {StatusTones.Classify(status)}。");
        Require(StatusTones.Classify(" 已抵達 ") == StatusTone.Success, "狀態文字前後空白不得影響分類。");
        foreach (var unknown in new string?[] { null, "", "未知狀態", "全新狀態文字" })
            Require(StatusTones.Classify(unknown) == StatusTone.Neutral, $"無法辨識的狀態「{unknown}」應為 Neutral。");
        Require(StatusTones.KnownStatuses.Count == ExpectedTones.Length,
            $"對照表應恰有 {ExpectedTones.Length} 筆，實際 {StatusTones.KnownStatuses.Count}。");

        // 程式實際輸出的狀態字串都必須在對照表內，避免新增狀態後默默變成灰點。
        string Source(params string[] path) => File.ReadAllText(System.IO.Path.Combine([root, "src", .. path]));
        string[] Literals(string text, string startPattern)
        {
            var match = Regex.Match(text, startPattern + @"\s*\{(?<body>.*?)\};", RegexOptions.Singleline);
            Require(match.Success, $"找不到 {startPattern} 的對照區塊。");
            return Regex.Matches(match.Groups["body"].Value, "\"([^\"]+)\"").Select(item => item.Groups[1].Value).ToArray();
        }
        var v2 = Source("MrtRouteSimulator.App", "MainWindow.V2.cs");
        var produced = Literals(v2, @"PhaseToChinese\(OperationalPhase phase\) => phase switch")
            .Concat(Literals(v2, @"SafetyStatusToChinese\(SafetyStatus status\) => status switch"))
            .Concat(Regex.Matches(Source("MrtRouteSimulator.App", "IncrementalV1V2Comparison.cs"), "const string \\w+Status = \"([^\"]+)\"")
                .Select(item => item.Groups[1].Value))
            .Concat(["已抵達", "停站中", "已發車", "待發", "完成", "運行中", "V1 理論基準", "—"]);
        foreach (var status in produced.Distinct())
            Require(StatusTones.KnownStatuses.ContainsKey(status), $"程式會輸出的狀態「{status}」不在 StatusTones 對照表內。");

        var expectedBrushes = new (StatusTone Tone, SolidColorBrush Brush)[]
        {
            (StatusTone.Danger, UiTheme.DangerBrush), (StatusTone.Caution, UiTheme.CautionBrush),
            (StatusTone.Success, UiTheme.SuccessBrush), (StatusTone.Active, UiTheme.AccentBrush),
            (StatusTone.Neutral, UiTheme.TextSubtleBrush)
        };
        foreach (var (tone, brush) in expectedBrushes)
            Require(ReferenceEquals(StatusTones.Brush(tone), brush), $"{tone} 色點必須取自 UiTheme。");
        Require(UiTheme.Caution == Color.FromRgb(0xD9, 0xA4, 0x00) && UiTheme.CautionBrush.IsFrozen,
            "Caution 色票應為 #D9A400 且畫筆凍結。");
        var converted = new StatusToneBrushConverter().Convert("需要制動", typeof(Brush), null!, System.Globalization.CultureInfo.InvariantCulture);
        Require(ReferenceEquals(converted, UiTheme.CautionBrush), "轉換器必須依狀態文字回傳色點畫筆。");
        Console.WriteLine("[通過] StatusTone 狀態色點對照");
    }
}
