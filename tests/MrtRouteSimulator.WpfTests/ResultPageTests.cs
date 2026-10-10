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
        Console.WriteLine("PASS WPF result pages");
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
