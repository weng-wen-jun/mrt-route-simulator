using System.Windows;
using System.Windows.Input;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

public partial class MainWindow
{
    private const double QuickBuilderExpandedWidth = 450;
    private const double QuickBuilderMinimumWidth = 320;
    private const double NavRailWidth = 64;

    private void OpenInfrastructureWorkspace_Click(object sender, RoutedEventArgs e) =>
        OpenTopologyWorkspace(ProjectWorkspacePage.Tracks);

    private void OpenOperationsWorkspace_Click(object sender, RoutedEventArgs e) =>
        OpenTopologyWorkspace(ProjectWorkspacePage.Services);

    private void OpenSimulationWorkspace_Click(object sender, RoutedEventArgs e) =>
        OpenTopologyWorkspace(ProjectWorkspacePage.Simulation);

    private async void OpenTopologyWorkspace(ProjectWorkspacePage initialPage)
    {
        HideValidation();
        try
        {
            var document = _activeTopologyProjectDocument
                ?? TopologyProjectFactory.CreateLinearDraft(CaptureProjectDocument());
            var editor = new TopologyEditorWindow(document, initialPage) { Owner = this };
            if (editor.ShowDialog() != true || editor.Result is null)
            {
                StatusTextBlock.Text = "已取消專案工作區變更；目前專案未變更。";
                return;
            }

            await ConfigureTopologyProjectForPlaybackAsync(editor.Result);
            StatusTextBlock.Text = "專案工作區變更已套用；可直接播放或前往分析結果。";
        }
        catch (SimulationValidationException exception)
        {
            ShowValidation(exception.Errors);
            StatusTextBlock.Text = "專案工作區未能建立或套用；目前專案未變更。";
        }
        catch (InvalidOperationException exception)
        {
            ShowValidation([exception.Message]);
            StatusTextBlock.Text = "專案工作區未能建立或套用；目前專案未變更。";
        }
    }

    // locked：停用快速起稿輸入；collapsed：關閉抽屜。抽屜只在使用者明確要求時開啟
    // （起稿鈕、選單「快速起稿」、前往 V2 設定），避免啟動或清除結果時自動彈出。
    private void SetQuickBuilderState(bool locked, bool collapsed)
    {
        ConfigurationScrollViewer.IsEnabled = true;
        QuickBuilderInputPanel.IsEnabled = !locked;
        if (collapsed) SetQuickBuilderDrawerOpen(false);
    }

    private void SetQuickBuilderDrawerOpen(bool open)
    {
        QuickBuilderSidebar.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        UpdateQuickBuilderWidth();
    }

    private void QuickBuilderToggle_Click(object sender, RoutedEventArgs e)
    {
        if (QuickBuilderSidebar.Visibility == Visibility.Visible) SetQuickBuilderDrawerOpen(false);
        else FocusRouteInput_Click(sender, e);
    }

    private void QuickBuilderClose_Click(object sender, RoutedEventArgs e) => SetQuickBuilderDrawerOpen(false);

    private void QuickBuilderSidebar_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        SetQuickBuilderDrawerOpen(false);
        QuickBuilderToggleButton.Focus();
        e.Handled = true;
    }

    private void UpdateQuickBuilderWidth()
    {
        if (QuickBuilderSidebar is null || QuickBuilderSidebar.Visibility == Visibility.Collapsed) return;
        // Use the measured, interface-scaled content width, not physical pixels.
        var width = ShellContentGrid.ActualWidth - NavRailWidth;
        if (!IsFiniteLayoutDimension(width)) return;
        QuickBuilderSidebar.Width = Math.Clamp(width * .38, QuickBuilderMinimumWidth, QuickBuilderExpandedWidth);
    }
}
