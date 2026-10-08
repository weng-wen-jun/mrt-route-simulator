using System.Windows;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

public partial class MainWindow
{
    private const double QuickBuilderExpandedWidth = 450;

    private void OpenInfrastructureWorkspace_Click(object sender, RoutedEventArgs e) =>
        OpenTopologyWorkspace(ProjectWorkspacePage.Tracks);

    private void OpenOperationsWorkspace_Click(object sender, RoutedEventArgs e) =>
        OpenTopologyWorkspace(ProjectWorkspacePage.Services);

    private void OpenSimulationWorkspace_Click(object sender, RoutedEventArgs e) =>
        OpenTopologyWorkspace(ProjectWorkspacePage.Simulation);

    private void OpenResults_Click(object sender, RoutedEventArgs e)
    {
        WorkspaceTabControl.SelectedItem = ResultsTabItem;
        ResultsTabItem.BringIntoView();
        StatusTextBlock.Text = "已切換至分析結果。";
    }

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

    private void SetQuickBuilderState(bool locked, bool collapsed)
    {
        ConfigurationScrollViewer.IsEnabled = true;
        QuickBuilderInputPanel.IsEnabled = !locked;
        QuickBuilderSidebar.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        QuickBuilderColumn.Width = collapsed
            ? new GridLength(0)
            : new GridLength(QuickBuilderExpandedWidth);
        UpdateQuickBuilderWidth();
    }

    private void UpdateQuickBuilderWidth()
    {
        if (QuickBuilderColumn is null || QuickBuilderSidebar is null
            || QuickBuilderSidebar.Visibility == Visibility.Collapsed) return;
        // Use the measured, interface-scaled content width, not physical pixels.
        // Keep the editor usable while reserving most of a compact window for results.
        var width = ShellContentGrid.ActualWidth;
        if (!IsFiniteLayoutDimension(width)) return;
        QuickBuilderColumn.Width = new GridLength(Math.Clamp(width * .38, 280, QuickBuilderExpandedWidth));
    }
}
