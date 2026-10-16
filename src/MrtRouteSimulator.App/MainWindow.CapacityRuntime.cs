using System.Windows;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;
public partial class MainWindow
{
    private async void ApplyCapacityRuntime_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTopologyProjectDocument is not { } document) { ShowValidation(["請先載入Schema 8專案。"]); return; }
        try
        {
            var result = CapacityRuntimeMapper.ApplyStations(document);
            await ConfigureTopologyProjectForPlaybackAsync(result.Document);
            StatusTextBlock.Text = string.Join(" ", result.Notes);
        }
        catch (SimulationValidationException exception) { ShowValidation(exception.Errors); }
        catch (Exception exception) { ShowValidation(["容量運行參數套用失敗：" + exception.Message]); }
    }
}
