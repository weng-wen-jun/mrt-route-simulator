using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace MrtRouteSimulator.App;

/// <summary>Application presentation preference; never part of a simulation project.</summary>
internal static class InterfaceScaleService
{
    private static readonly ConditionalWeakTable<FrameworkElement, ScaleTransform> Transforms = new();
    private static bool _initialized;
    public static double CurrentScale { get; private set; } = 1;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        CurrentScale = AppDisplayPreferences.LoadInterfaceScale();
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, args) =>
            {
                if (sender is Window window && ReferenceEquals(args.OriginalSource, window))
                    ApplyToWindow(window);
            }));
    }

    public static void SetScale(double scale)
    {
        Initialize();
        CurrentScale = AppDisplayPreferences.NormalizeInterfaceScale(scale);
        if (Application.Current is not { } app) return;
        foreach (Window window in app.Windows) ApplyToWindow(window);
    }

    public static void ApplyToWindow(Window window)
    {
        Initialize();
        if (window.Content is not FrameworkElement root) return;
        var transform = Transforms.GetValue(root, element =>
        {
            var scale = new ScaleTransform(1, 1);
            if (element.LayoutTransform is { } existing && !existing.Value.IsIdentity)
                element.LayoutTransform = new TransformGroup { Children = { existing, scale } };
            else
                element.LayoutTransform = scale;
            return scale;
        });
        transform.ScaleX = CurrentScale;
        transform.ScaleY = CurrentScale;
    }
}
