using System.IO;
using System.Reflection;
using MrtRouteSimulator.App;

internal static class InterfaceScalePreferenceTests
{
    private static readonly double[] SupportedScales = [0.8, 0.9, 1, 1.1, 1.25];

    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mrt-interface-scale-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "display-settings.json");
        var api = new PreferenceApi();

        try
        {
            var missing = api.Load(path);
            Require(!GetBool(missing, "ShowLockedRoutes")
                && GetDouble(missing, "RouteMapHorizontalZoom") == 1
                && GetDouble(missing, "InterfaceScale") == 1,
                "遺失偏好檔必須回傳所有預設值。");

            File.WriteAllText(path, "{\"ShowLockedRoutes\":true,\"RouteMapHorizontalZoom\":1.5}");
            var oldSettings = api.Load(path);
            Require(GetBool(oldSettings, "ShowLockedRoutes")
                && GetDouble(oldSettings, "RouteMapHorizontalZoom") == 1.5
                && GetDouble(oldSettings, "InterfaceScale") == 1,
                "舊版缺少 InterfaceScale 欄位時，必須回傳 100% 並保留既有偏好。");

            foreach (var scale in SupportedScales)
            {
                api.Save(path, showLockedRoutes: true, routeMapHorizontalZoom: 1.5, interfaceScale: scale);
                var reloaded = api.Load(path);
                Require(GetBool(reloaded, "ShowLockedRoutes")
                    && GetDouble(reloaded, "RouteMapHorizontalZoom") == 1.5
                    && GetDouble(reloaded, "InterfaceScale") == scale,
                    $"介面縮放 {scale:0.##} 儲存後重讀必須保留五個合法選項與既有偏好。");
            }

            api.Save(path, showLockedRoutes: true, routeMapHorizontalZoom: 1.5, interfaceScale: 1);
            var restored100 = api.Load(path);
            Require(GetBool(restored100, "ShowLockedRoutes")
                && GetDouble(restored100, "RouteMapHorizontalZoom") == 1.5
                && GetDouble(restored100, "InterfaceScale") == 1,
                "恢復 100% 不得改寫 ShowLockedRoutes 或 RouteMapHorizontalZoom。");

            VerifyInvalidJsonValues(api, path);
            VerifyNormalization(api);
            Console.WriteLine("PASS WPF interface scale preferences: isolated path, legacy defaults, round-trip, malformed values, normalization");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static void VerifyInvalidJsonValues(PreferenceApi api, string path)
    {
        var invalidInterfaceValues = new[]
        {
            "{\"InterfaceScale\":\"1.1\",\"ShowLockedRoutes\":true,\"RouteMapHorizontalZoom\":1.5}",
            "{\"InterfaceScale\":null,\"ShowLockedRoutes\":true,\"RouteMapHorizontalZoom\":1.5}",
            "{\"InterfaceScale\":[],\"ShowLockedRoutes\":true,\"RouteMapHorizontalZoom\":1.5}"
        };

        foreach (var json in invalidInterfaceValues)
        {
            File.WriteAllText(path, json);
            var settings = api.Load(path);
            Require(GetDouble(settings, "InterfaceScale") == 1
                && GetBool(settings, "ShowLockedRoutes")
                && GetDouble(settings, "RouteMapHorizontalZoom") == 1.5,
                "字串、null 或 array 形式的 InterfaceScale 必須回到 100%，且保留合法欄位。");
        }

        var invalidZoomValues = new[]
        {
            "{\"InterfaceScale\":1.1,\"ShowLockedRoutes\":true,\"RouteMapHorizontalZoom\":\"1.5\"}",
            "{\"InterfaceScale\":1.1,\"ShowLockedRoutes\":true,\"RouteMapHorizontalZoom\":null}",
            "{\"InterfaceScale\":1.1,\"ShowLockedRoutes\":true,\"RouteMapHorizontalZoom\":[]}"
        };

        foreach (var json in invalidZoomValues)
        {
            File.WriteAllText(path, json);
            var settings = api.Load(path);
            Require(GetDouble(settings, "InterfaceScale") == 1.1
                && GetBool(settings, "ShowLockedRoutes")
                && GetDouble(settings, "RouteMapHorizontalZoom") == 1,
                "非 number 的 RouteMapHorizontalZoom 必須安全回到預設值，不得讓 TryGetDouble 拋例外。");
        }

        foreach (var json in new[] { "not-json", "[]" })
        {
            File.WriteAllText(path, json);
            var settings = api.Load(path);
            Require(!GetBool(settings, "ShowLockedRoutes")
                && GetDouble(settings, "RouteMapHorizontalZoom") == 1
                && GetDouble(settings, "InterfaceScale") == 1,
                "malformed 或非 object JSON 必須安全回傳完整預設值。");
        }
    }

    private static void VerifyNormalization(PreferenceApi api)
    {
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Require(api.Normalize(value) == 1, "NaN／Infinity 的介面縮放必須正規化為 100%。");
        }

        var cases = new (double Value, double Expected)[]
        {
            (0.79, 0.8),
            (0.84, 0.8),
            (0.95, 0.9),
            (1.06, 1.1),
            (1.2, 1.25),
            (2, 1.25)
        };
        foreach (var (value, expected) in cases)
        {
            Require(api.Normalize(value) == expected,
                $"有限值 {value:0.##} 必須正規化至最近合法縮放 {expected:0.##}。");
        }
    }

    private static bool GetBool(object settings, string propertyName) =>
        (bool)(settings.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)!
            .GetValue(settings)!);

    private static double GetDouble(object settings, string propertyName) =>
        (double)(settings.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)!
            .GetValue(settings)!);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class PreferenceApi
    {
        private readonly Type _preferencesType = typeof(MainWindow).Assembly
            .GetType("MrtRouteSimulator.App.AppDisplayPreferences", throwOnError: true)!;
        private readonly Type _settingsType;
        private readonly ConstructorInfo _settingsConstructor;
        private readonly MethodInfo _load;
        private readonly MethodInfo _save;
        private readonly MethodInfo _normalize;

        public PreferenceApi()
        {
            _settingsType = _preferencesType.GetNestedType("DisplaySettings", BindingFlags.NonPublic)!
                ?? throw new InvalidOperationException("找不到 AppDisplayPreferences.DisplaySettings。");
            _settingsConstructor = _settingsType.GetConstructors(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Single(constructor => HasParameters(constructor, typeof(bool), typeof(double), typeof(double)));
            _load = FindMethod("Load", typeof(string));
            _save = FindMethod("Save", typeof(string), _settingsType);
            _normalize = FindMethod("NormalizeInterfaceScale", typeof(double));
        }

        public object Load(string path) => _load.Invoke(null, new object?[] { path })
            ?? throw new InvalidOperationException("Load returned null.");

        public void Save(string path, bool showLockedRoutes, double routeMapHorizontalZoom, double interfaceScale)
        {
            var settings = _settingsConstructor.Invoke(new object?[]
            {
                showLockedRoutes,
                routeMapHorizontalZoom,
                interfaceScale
            });
            _save.Invoke(null, new[] { (object?)path, settings });
        }

        public double Normalize(double value) => (double)(_normalize.Invoke(null, new object?[] { value })
            ?? throw new InvalidOperationException("NormalizeInterfaceScale returned null."));

        private MethodInfo FindMethod(string name, params Type[] parameterTypes) =>
            _preferencesType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Single(method => method.Name == name
                    && method.GetParameters().Select(parameter => parameter.ParameterType)
                        .SequenceEqual(parameterTypes));

        private static bool HasParameters(ConstructorInfo constructor, params Type[] parameterTypes) =>
            constructor.GetParameters().Select(parameter => parameter.ParameterType)
                .SequenceEqual(parameterTypes);
    }
}
