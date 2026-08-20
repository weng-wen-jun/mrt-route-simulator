using System.Reflection;

namespace MrtRouteSimulator.Engine;

public static class ProductVersion
{
    public static string Current { get; } = ResolveCurrent();

    private static string ResolveCurrent()
    {
        var version = typeof(ProductVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return string.IsNullOrWhiteSpace(version) ? "V0.0.0" : version;
    }
}
