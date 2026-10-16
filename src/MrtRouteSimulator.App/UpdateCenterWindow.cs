using System.IO;
using System.Windows;
using System.Windows.Controls;
using MrtRouteSimulator.UpdateCore;

namespace MrtRouteSimulator.App;

public sealed class UpdateCenterWindow : Window
{
    private readonly AppUpdateService service;
    private readonly Func<bool> prepareRestart;
    private readonly Action requestRestart;
    private readonly TextBlock versions = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox messages = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 100 };
    private readonly ProgressBar progress = new() { Height = 16, Minimum = 0, Maximum = 100 };
    private readonly ListBox library = new() { MinHeight = 130, DisplayMemberPath = nameof(UpdateLibraryEntry.Name) };
    private readonly Button check = new() { Content = "檢查更新" };
    private readonly Button appDownload = new() { Content = "下載程式更新", IsEnabled = false };
    private readonly Button appInstall = new() { Content = "安裝並重新啟動", IsEnabled = false };
    private readonly Button dataInstall = new() { Content = "更新範例資料庫", IsEnabled = false };
    private readonly Button cancel = new() { Content = "取消下載", IsEnabled = false };
    private readonly Button load = new() { Content = "載入選取範例" };
    private CancellationTokenSource? operation;
    private UpdateOffer? appOffer, dataOffer;
    private StagedUpdate? stagedApplication;
    public string? SelectedSamplePath { get; private set; }

    public UpdateCenterWindow(AppUpdateService service, Func<bool>? prepareRestart = null, Action? requestRestart = null)
    {
        this.service = service;
        this.prepareRestart = prepareRestart ?? (() => false);
        this.requestRestart = requestRestart ?? (() => { });
        Title = "更新中心";
        Width = 760; Height = 660; MinWidth = 520; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        InterfaceScaleService.ApplyToWindow(this);
        var panel = new StackPanel { Margin = new Thickness(18) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "程式與範例資料更新", FontSize = 20, FontWeight = FontWeights.SemiBold });
        versions.Margin = new Thickness(0, 10, 0, 10); panel.Children.Add(versions);
        var startup = new CheckBox { Content = "啟動時檢查更新（只通知，不自動下載或安裝）", IsChecked = service.CheckAtStartup, Margin = new Thickness(0, 0, 0, 10) };
        startup.Click += (_, _) => { try { service.SetCheckAtStartup(startup.IsChecked == true); } catch (Exception e) { messages.Text = e.Message; startup.IsChecked = service.CheckAtStartup; } };
        panel.Children.Add(startup);
        var actions = new WrapPanel();
        foreach (var button in new[] { check, appDownload, appInstall, dataInstall, cancel })
        { button.Margin = new Thickness(0, 0, 8, 8); button.Padding = new Thickness(10, 5, 10, 5); actions.Children.Add(button); }
        panel.Children.Add(actions); panel.Children.Add(progress);
        messages.Margin = new Thickness(0, 10, 0, 12); panel.Children.Add(messages);
        panel.Children.Add(new TextBlock { Text = "專案維護範例", FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "資料更新經驗證後切換整份資料庫；你的存檔與目前模擬不會被更動。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) });
        panel.Children.Add(library);
        load.Margin = new Thickness(0, 8, 0, 0); load.HorizontalAlignment = HorizontalAlignment.Left; panel.Children.Add(load);
        check.Click += async (_, _) => await RunAsync(CheckUpdatesAsync);
        appDownload.Click += async (_, _) => await RunAsync(async token =>
        {
            if (appOffer is null) return;
            stagedApplication = await service.DownloadAsync(appOffer, DownloadProgress(), token);
            messages.Text = "程式套件已下載並驗證。按「安裝並重新啟動」套用；關閉程式前會建立專案復原副本。";
        });
        dataInstall.Click += async (_, _) => await RunAsync(async token =>
        {
            if (dataOffer is null) return;
            var staged = await service.DownloadAsync(dataOffer, DownloadProgress(), token);
            await service.InstallDataAsync(staged, token); dataOffer = null;
            messages.Text = "範例資料庫已更新；舊版仍保留。"; Refresh();
        });
        appInstall.Click += (_, _) => InstallApplication();
        cancel.Click += (_, _) => operation?.Cancel();
        var recovery = new Button { Content = "載入最近的更新前專案副本", Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        recovery.Click += (_, _) =>
        {
            if (operation is not null) return;
            try
            {
                var folder = Path.Combine(service.StateRoot, "Recovery"); UpdatePathPolicy.RejectReparsePoints(folder);
                var file = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.mrtsim.json").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
                if (file is null) { messages.Text = "目前沒有更新前專案副本。"; return; }
                UpdatePathPolicy.RejectReparsePoints(file); SelectedSamplePath = file; Close();
            }
            catch (Exception e) { messages.Text = $"無法載入復原副本：{e.Message}"; }
        };
        panel.Children.Add(recovery);
        load.Click += (_, _) => { if (operation is null && library.SelectedItem is UpdateLibraryEntry item) { SelectedSamplePath = item.Path; Close(); } };
        Closing += (_, e) => { if (operation is not null) { operation.Cancel(); e.Cancel = true; messages.Text = "正在取消，完成後即可關閉。"; } };
        Refresh();
        messages.Text = service.LastResult() ?? "按「檢查更新」取得官方穩定版本。";
    }

    private void Refresh()
    {
        versions.Text = $"程式版本：{service.AppVersion}　範例資料版本：{(service.DataVersion == "0.0.0" ? "隨程式附帶" : service.DataVersion)}\n" +
            (service.SoftwareUpdateUnavailableReason ?? "此安裝支援程式內更新。");
        library.ItemsSource = service.Library();
        var idle = operation is null;
        check.IsEnabled = idle; load.IsEnabled = idle; cancel.IsEnabled = !idle;
        appDownload.IsEnabled = idle && appOffer is not null && service.CanInstallSoftware;
        dataInstall.IsEnabled = idle && dataOffer is not null;
        appInstall.IsEnabled = idle && stagedApplication is not null && service.CanInstallSoftware;
    }
    private IProgress<UpdateProgress> DownloadProgress() => new Progress<UpdateProgress>(p =>
    {
        progress.IsIndeterminate = !p.TotalBytes.HasValue;
        if (p.TotalBytes > 0) progress.Value = 100d * p.ReceivedBytes / p.TotalBytes.Value;
        messages.Text = p.Message;
    });
    private async Task CheckUpdatesAsync(CancellationToken token)
    {
        var app = await service.CheckAsync(UpdateKind.Application, token);
        var data = await service.CheckAsync(UpdateKind.Data, token);
        appOffer = app.Offer; dataOffer = data.Offer;
        messages.Text = $"程式：{app.Message}\n資料：{data.Message}" +
            (app.Offer is null ? "" : $"\n\n程式更新說明：\n{app.Offer.Notes}") +
            (data.Offer is null ? "" : $"\n\n資料更新說明：\n{data.Offer.Notes}");
    }
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (operation is not null) return;
        operation = new CancellationTokenSource(); Refresh(); progress.IsIndeterminate = true;
        try { await action(operation.Token); }
        catch (OperationCanceledException) { messages.Text = "已取消；目前程式與資料未被替換。"; }
        catch (Exception e) { messages.Text = $"更新未完成：{e.Message}"; }
        finally { operation.Dispose(); operation = null; progress.IsIndeterminate = false; Refresh(); }
    }
    private void InstallApplication()
    {
        try
        {
            if (stagedApplication is null || !prepareRestart()) return;
            var job = service.PrepareSoftwareJob(stagedApplication);
            service.StartSoftwareJob(job);
            Close(); requestRestart();
        }
        catch (Exception e) { messages.Text = $"無法開始安裝：{e.Message}"; }
    }
}
