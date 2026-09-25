using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MrtRouteSimulator.App;

/// <summary>讀取候選專案時顯示階段與進度，不提供取消以保護目前已載入的工作階段。</summary>
internal sealed class ProjectLoadProgressWindow : Window
{
    private readonly TextBlock _message;
    private readonly ProgressBar _progressBar;

    public ProjectLoadProgressWindow()
    {
        Title = "正在讀取存檔";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _message = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _progressBar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 18, Margin = new Thickness(0, 14, 0, 0) };
        Content = new Border
        {
            Padding = new Thickness(20),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "正在讀取存檔，請稍候", FontWeight = FontWeights.SemiBold, FontSize = 16 },
                    _message,
                    _progressBar
                }
            }
        };
        UpdateProgress(0, "準備讀取檔案…");
    }

    public void UpdateProgress(double percentage, string message)
    {
        _progressBar.Value = Math.Clamp(percentage, _progressBar.Minimum, _progressBar.Maximum);
        _message.Text = message;
    }
}
