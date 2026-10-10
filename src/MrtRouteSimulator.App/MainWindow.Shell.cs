using System.Diagnostics;
using System.Windows;

namespace MrtRouteSimulator.App;

public partial class MainWindow
{
    // 畫面更新失敗而停止播放後為 true；重設或清除結果時清除。
    private bool _playbackStoppedByError;

    // 播放旗標的唯一寫入點：同步刷新狀態列圓點。
    private void SetV2PlaybackPlaying(bool playing)
    {
        _isV2PlaybackPlaying = playing;
        UpdateStatusIndicator();
    }

    // 狀態列圓點：驗證警告、因錯誤停止或播放工作者錯誤為橘；V2 播放中或 V1 計時器運作中為綠；其他為灰。
    private void UpdateStatusIndicator()
    {
        if (StatusIndicatorDot is null || ValidationBorder is null) return;
        var hasProblem = ValidationBorder.Visibility == Visibility.Visible
            || _playbackStoppedByError
            || _playbackWorker?.Completion.IsFaulted == true;
        var playing = _isV2PlaybackPlaying
            || (!_v2Enabled && _simulationEngine is not null && _playbackTimer?.IsEnabled == true);
        StatusIndicatorDot.Fill = hasProblem
            ? UiTheme.PrimaryBrush
            : playing ? UiTheme.SuccessBrush : UiTheme.TextSubtleBrush;
    }

    // 畫面更新失敗而停止播放：保留專案資料，狀態圓點標示為錯誤（橘）。
    private void StopPlaybackAfterUiFailure(Exception exception)
    {
        NativeAcceptanceAbortForLifecycle("ui-update-failed");
        PausePlayback();
        _playbackStoppedByError = true;
        Trace.WriteLine($"Playback stopped after an unexpected UI update failure: {exception}");
        PlaybackStatusText.Text = $"播放已停止：{exception.Message}";
        StatusTextBlock.Text = "播放更新失敗；模擬已暫停，專案資料仍保留。";
        UpdateStatusIndicator();
    }
}
