using System.Windows;

namespace MrtRouteSimulator.App;

public partial class MainWindow
{
    // 播放旗標的唯一寫入點：同步刷新狀態列圓點。
    private void SetV2PlaybackPlaying(bool playing)
    {
        _isV2PlaybackPlaying = playing;
        UpdateStatusIndicator();
    }

    // 狀態列圓點：驗證警告或播放工作者錯誤為橘、播放中為綠、其他為灰。
    private void UpdateStatusIndicator()
    {
        if (StatusIndicatorDot is null || ValidationBorder is null) return;
        var hasProblem = ValidationBorder.Visibility == Visibility.Visible
            || _playbackWorker?.Completion.IsFaulted == true;
        StatusIndicatorDot.Fill = hasProblem
            ? UiTheme.PrimaryBrush
            : _isV2PlaybackPlaying ? UiTheme.SuccessBrush : UiTheme.TextSubtleBrush;
    }
}
