using System.IO;
using System.Reflection;
using System.Windows;
using MrtRouteSimulator.Engine;
using MrtRouteSimulator.UpdateCore;

namespace MrtRouteSimulator.App;

public partial class MainWindow
{
    private Func<AppUpdateService> _createUpdateService = static () => new();

    private async void OpenUpdateCenter_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var service = _createUpdateService();
            var window = new UpdateCenterWindow(service, () => PrepareUpdateRestart(service), Close) { Owner = this };
            window.ShowDialog();
            if (window.SelectedSamplePath is not { } path) return;
            if (MessageBox.Show(this, "載入專案會取代目前工作區。請先儲存需要保留的變更。\n是否繼續？", "載入專案", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            if (new FileInfo(path).Length > MaximumProjectFileBytes) throw new InvalidDataException("範例超過讀取上限。");
            var document = await Task.Run(() => TopologyProjectFormat.Deserialize(File.ReadAllText(path)));
            if (LegacyPortMigration.FindUnspecifiedEdges(document).Count != 0)
                throw new InvalidDataException("這份範例仍需接軌側別選擇，請使用「讀取存檔」完成遷移。");
            var prepared = await Task.Run(() => PrepareTopologyProjectForPlayback(document));
            await ApplyPreparedTopologyProjectForPlaybackAsync(prepared, lockLegacyInputs: true);
            SetCurrentProjectFile(null);
            StatusTextBlock.Text = $"已載入專案：{Path.GetFileName(path)}；請另存自己的專案。";
        }
        catch (Exception exception) { ShowValidation([$"更新中心：{exception.Message}"]); }
    }
    private bool PrepareUpdateRestart(AppUpdateService service)
    {
        if (MessageBox.Show(this, "將建立目前專案的復原副本，接著關閉程式、安裝更新並重新啟動。\n要繼續嗎？", "安裝程式更新", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return false;
        var document = _activeTopologyProjectDocument ?? TopologyProjectFactory.CreateLinearDraft(CaptureProjectDocument());
        var json = TopologyProjectFormat.Serialize(document);
        var folder = Path.Combine(service.StateRoot, "Recovery"); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.mrtsim.json");
        WriteProjectAtomically(path, json);
        StatusTextBlock.Text = $"更新前專案副本：{path}";
        return true;
    }

    private async Task CheckUpdatesAtStartupAsync()
    {
        if (Assembly.GetEntryAssembly() != typeof(App).Assembly) return;
        using var service = new AppUpdateService();
        if (!service.CheckAtStartup) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            var app = await service.CheckAsync(UpdateKind.Application, timeout.Token);
            var data = await service.CheckAsync(UpdateKind.Data, timeout.Token);
            if (app.Offer is not null || data.Offer is not null)
                StatusTextBlock.Text = "有可用更新；請開啟「檔案 → 更新中心」。";
        }
        catch (Exception) { }
    }
}
