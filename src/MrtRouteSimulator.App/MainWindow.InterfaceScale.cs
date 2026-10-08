using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace MrtRouteSimulator.App;

public partial class MainWindow
{
    private bool _isCompactSummaryLayout;
    private void MainWindow_CompactSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCompactSummaryHeight();
    }

    private void UpdateCompactSummaryHeight()
    {
        if (RouteSummaryScrollViewer is null) return;
        var compact = ActualHeight / InterfaceScaleService.CurrentScale < 600;
        RouteSummaryScrollViewer.MaxHeight = compact ? 40 : 100;
        if (compact && !_isCompactSummaryLayout && RouteSummaryExpander is not null)
            RouteSummaryExpander.IsExpanded = false;
        _isCompactSummaryLayout = compact;
    }

    private void UpdateInterfaceScaleMenu()
    {
        foreach (var item in InterfaceScaleMenuItem.Items.OfType<MenuItem>())
            item.IsChecked = double.TryParse(item.Tag?.ToString(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var scale)
                && Math.Abs(scale - InterfaceScaleService.CurrentScale) < .001;
    }

    private void InterfaceScale_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || !double.TryParse(item.Tag?.ToString(),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var scale)) return;
        InterfaceScaleService.SetScale(scale);
        UpdateCompactSummaryHeight();
        UpdateInterfaceScaleMenu();
        try
        {
            AppDisplayPreferences.SaveInterfaceScale(InterfaceScaleService.CurrentScale);
            StatusTextBlock.Text = $"介面縮放已設為 {InterfaceScaleService.CurrentScale:P0}；跟隨軟體設定，不寫入路線存檔。";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusTextBlock.Text = $"介面縮放已套用，但無法保存軟體設定：{exception.Message}";
        }
    }

    private void SpeedScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (SpeedCanvas is null || sender is not ScrollViewer viewer) return;
        var height = viewer.ActualHeight;
        SpeedCanvas.Height = IsFiniteLayoutDimension(height) ? Math.Max(220, height) : 220;
    }
}
