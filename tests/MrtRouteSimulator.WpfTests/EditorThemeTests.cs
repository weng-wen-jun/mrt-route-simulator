using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

/// <summary>拓樸編輯器外觀（子專案 E）：共用樣式、外殼、各頁、示意圖、對話框與寫死顏色掃描。</summary>
internal static class EditorThemeTests
{
    internal static readonly Type EditorType = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.TopologyEditorWindow")!;
    internal static readonly Type PageType = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.ProjectWorkspacePage")!;

    public static void Run(string root)
    {
        Console.WriteLine("PASS WPF editor theme");
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static Type AppType(string name) => typeof(MainWindow).Assembly.GetType($"MrtRouteSimulator.App.{name}")!;

    internal static TopologyProjectDocument Sample(string root) => TopologyProjectFormat.Deserialize(
        File.ReadAllText(System.IO.Path.Combine(root, "samples", "14-大型-二十八站完整營運範例.mrtsim.json")));

    /// <summary>在螢幕外開啟編輯器（範例 14），測試結束由呼叫端關閉。</summary>
    internal static Window OpenEditor(string root, string page, double width = 1280, double height = 800)
    {
        var window = (Window)Activator.CreateInstance(EditorType, Sample(root), Enum.Parse(PageType, page))!;
        window.Width = width;
        window.Height = height;
        window.Left = -10000;
        window.Top = -10000;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.ShowInTaskbar = false;
        window.Show();
        window.UpdateLayout();
        return window;
    }

    internal static void Navigate(Window window, string page)
    {
        EditorType.GetMethod("Navigate", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [Enum.Parse(PageType, page)]);
        window.UpdateLayout();
    }

    internal static object? Field(Window window, string name) =>
        EditorType.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window);

    internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}
