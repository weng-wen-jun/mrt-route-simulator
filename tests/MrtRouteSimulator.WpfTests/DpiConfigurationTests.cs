using System.IO;
using System.Text;
using System.Xml.Linq;

/// <summary>Manifest/build-artifact checks, not native OS DPI acceptance.</summary>
internal static class DpiConfigurationTests
{
    public static void Run(string root)
    {
        var projectDirectory = Path.Combine(root, "src", "MrtRouteSimulator.App");
        var project = XDocument.Load(Path.Combine(projectDirectory, "MrtRouteSimulator.App.csproj"));
        Require(project.Descendants("ApplicationManifest").Single().Value == "app.manifest",
            "App must explicitly embed its DPI manifest.");
        var manifest = XDocument.Load(Path.Combine(projectDirectory, "app.manifest"));
        XNamespace modern = "http://schemas.microsoft.com/SMI/2016/WindowsSettings";
        XNamespace legacy = "http://schemas.microsoft.com/SMI/2005/WindowsSettings";
        XNamespace assemblyV3 = "urn:schemas-microsoft-com:asm.v3";
        Require(manifest.Descendants(modern + "dpiAwareness").Single().Value == "PerMonitorV2, PerMonitor",
            "App must prefer per-monitor V2 with per-monitor fallback.");
        Require(manifest.Descendants(legacy + "dpiAware").Single().Value == "true/pm",
            "Legacy DPI declaration must remain per-monitor aware.");
        var execution = manifest.Descendants(assemblyV3 + "requestedExecutionLevel").Single();
        Require((string?)execution.Attribute("level") == "asInvoker"
                && (string?)execution.Attribute("uiAccess") == "false",
            "DPI configuration must not request elevation or UI access.");
        var executable = Path.Combine(projectDirectory, "bin", "Release", "net10.0-windows",
            "MRT路線進出站時間模擬器.exe");
        var appHost = Encoding.UTF8.GetString(File.ReadAllBytes(executable));
        Require(appHost.Contains("PerMonitorV2, PerMonitor", StringComparison.Ordinal)
                && appHost.Contains("true/pm", StringComparison.Ordinal),
            "Release apphost must contain the embedded DPI declarations.");
        Console.WriteLine("[通過] App DPI manifest/build artifact (not native DPI acceptance)");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
