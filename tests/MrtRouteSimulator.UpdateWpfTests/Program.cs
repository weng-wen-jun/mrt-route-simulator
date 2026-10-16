using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Automation;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;
using MrtRouteSimulator.UpdateCore;

internal static class Program
{
    private static int passed;
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "MrtUpdateWpfTests-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Project = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid.mrtsim.json"));
    [STAThread]
    private static int Main()
    {
        var app = new MrtRouteSimulator.App.App(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        Directory.CreateDirectory(Root);
        try
        {
            Test("construction has no network or update-state writes", () =>
            {
                using var f = new Fixture("initial"); var window = new UpdateCenterWindow(f.Service);
                try { Assert(f.Handler.Requests == 0 && !Directory.Exists(f.State) && !f.Service.CheckAtStartup, "Construction must remain offline with startup checking disabled."); }
                finally { window.Close(); }
            });
            Test("startup checkbox persists enabled and disabled preferences", () =>
            {
                using var f = new Fixture("preferences"); var window = new UpdateCenterWindow(f.Service);
                var checkbox = Descendants(window).OfType<CheckBox>().Single(); checkbox.IsChecked = true; checkbox.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                Assert(f.Service.CheckAtStartup && File.Exists(Path.Combine(f.State, "preferences.json")), "Preference did not persist."); window.Close();
                using var reloaded = new AppUpdateService(new HttpClient(new FakeHandler()), f.State, f.Install); var second = new UpdateCenterWindow(reloaded);
                try
                {
                    var value = Descendants(second).OfType<CheckBox>().Single(); Assert(value.IsChecked == true && reloaded.CheckAtStartup, "Preference did not reload.");
                    value.IsChecked = false; value.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                    using var final = new AppUpdateService(new HttpClient(new FakeHandler()), f.State, f.Install); Assert(!final.CheckAtStartup, "Disabled preference did not persist.");
                }
                finally { second.Close(); }
            });
            Test("versions and absent-receipt software restrictions", () =>
            {
                using var f = new Fixture("software-disabled"); f.Handler.Offers = [Package(UpdateKind.Application, "99.0.0", new() { [UpdateConstants.ApplicationExe] = [1, 2, 3] })];
                var window = new UpdateCenterWindow(f.Service);
                try
                {
                    string text = Field<TextBlock>(window, "versions").Text; Assert(text.Contains(f.Service.AppVersion.ToString(), StringComparison.Ordinal) && text.Contains("隨程式附帶", StringComparison.Ordinal), "Initial versions are incorrect.");
                    Click(window, "check"); WaitIdle(window); Assert(Field<TextBox>(window, "messages").Text.Contains("99.0.0", StringComparison.Ordinal), "Application offer was not displayed.");
                    Assert(!f.Service.CanInstallSoftware && !Field<Button>(window, "appDownload").IsEnabled && !Field<Button>(window, "appInstall").IsEnabled, "Absent receipt must disable application download and install.");
                    Throws<InvalidOperationException>(() => f.Service.PrepareSoftwareJob(new(Path.Combine(Root, "unused-stage"), f.Handler.Offers[0].Offer.Manifest)));
                    Assert(!Directory.Exists(Path.Combine(f.State, "Downloads")) && !Directory.Exists(Path.Combine(f.State, "Jobs")), "Checking must not create downloads or launch jobs.");
                }
                finally { window.Close(); }
            });
            Test("software update availability rejects installation and ancestor Git markers", () =>
            {
                using var f = new Fixture("software-git-policy");
                Directory.CreateDirectory(Path.Combine(f.Install, "updater"));
                File.WriteAllBytes(Path.Combine(f.Install, UpdateConstants.ApplicationExe), [1]);
                File.WriteAllBytes(Path.Combine(f.Install, "updater", "MrtRouteSimulator.Updater.dll"), [2]);
                File.WriteAllText(Path.Combine(f.Install, UpdateConstants.InstallationReceipt), JsonSerializer.Serialize(new InstalledFiles(1, UpdateConstants.Product, f.Service.AppVersion.ToString(), [UpdateConstants.ApplicationExe, "updater/MrtRouteSimulator.Updater.dll"]), UpdateJson.Options));
                f.Handler.Offers = [Package(UpdateKind.Application, "99.0.0", new() { [UpdateConstants.ApplicationExe] = [3] })];
                Assert(f.Service.CanInstallSoftware && f.Service.SoftwareUpdateUnavailableReason is null, "An isolated portable installation with its receipt and updater must support updates.");
                var available = new UpdateCenterWindow(f.Service);
                try { Click(available, "check"); WaitIdle(available); Assert(Field<Button>(available, "appDownload").IsEnabled, "Valid portable installation did not enable software download."); }
                finally { available.Close(); }
                string localGit = Path.Combine(f.Install, ".git"); Directory.CreateDirectory(localGit);
                CheckBlocked();
                Directory.Delete(localGit);
                Assert(f.Service.CanInstallSoftware, "Removing only the isolated Git marker must restore portable eligibility.");
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(f.Install)!, ".git"), "gitdir: isolated-test-marker");
                CheckBlocked();
                void CheckBlocked()
                {
                    Assert(!f.Service.CanInstallSoftware && f.Service.SoftwareUpdateUnavailableReason?.Contains("複製到專案外", StringComparison.Ordinal) == true, "Git workspaces must explain that the portable copy belongs outside the project.");
                    var blocked = new UpdateCenterWindow(f.Service);
                    try
                    {
                        Click(blocked, "check"); WaitIdle(blocked);
                        Assert(Field<TextBlock>(blocked, "versions").Text.Contains("複製到專案外", StringComparison.Ordinal), "The UI did not explain the Git workspace restriction.");
                        Assert(!Field<Button>(blocked, "appDownload").IsEnabled && !Field<Button>(blocked, "appInstall").IsEnabled, "Git workspaces must disable software download and installation.");
                        Throws<InvalidOperationException>(() => f.Service.PrepareSoftwareJob(new(Path.Combine(Root, "unused-git-stage"), f.Handler.Offers[0].Offer.Manifest)));
                    }
                    finally { blocked.Close(); }
                }
            });
            Test("software job carries complete isolated runner runtime", () =>
            {
                using var f = new Fixture("runner-runtime");
                string updater = Path.Combine(f.Install, "updater"); Directory.CreateDirectory(updater);
                foreach (string relative in new[] { "MrtRouteSimulator.Updater.dll", "MrtRouteSimulator.Updater.exe", "hostfxr.dll", "coreclr.dll", "MrtRouteSimulator.Updater.runtimeconfig.json", "runtimes/win-x64/native/native.dll", "zh-Hant/example.resources.dll" })
                {
                    string path = Path.Combine(updater, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, relative);
                }
                File.WriteAllText(Path.Combine(f.Install, UpdateConstants.InstallationReceipt), "{}");
                var package = Package(UpdateKind.Application, "99.0.0", new() { [UpdateConstants.ApplicationExe] = [1] });
                f.Handler.Offers = [package];
                var download = f.Service.DownloadAsync(package.Offer, new Progress<UpdateProgress>(), CancellationToken.None);
                PumpUntil(() => download.IsCompleted);
                var staged = download.GetAwaiter().GetResult();
                string job = f.Service.PrepareSoftwareJob(staged), runner = Path.Combine(Path.GetDirectoryName(job)!, "runner");
                foreach (string source in Directory.GetFiles(updater, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(updater, source);
                    Assert(File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(Path.Combine(runner, relative))), "Runner omitted or changed " + relative);
                }
                File.WriteAllText(Path.Combine(updater, "settings.json"), "must not copy");
                Throws<InvalidOperationException>(() => f.Service.PrepareSoftwareJob(staged));
            });
            Test("packaged sample library includes nested directories", () =>
            {
                using var f = new Fixture("recursive-library"); f.WritePackaged("b.mrtsim.json"); f.WritePackaged("小型/深層/a.mrtsim.json");
                var entries = f.Service.Library(); Assert(entries.Count == 2 && entries.Any(x => x.Name.Replace('\\', '/') == "小型/深層/a.mrtsim.json"), "Nested samples were omitted.");
                Assert(entries.All(x => x.Path.StartsWith(f.Install, StringComparison.OrdinalIgnoreCase)), "Library must remain in the isolated installation.");
            });
            Test("load selection returns exact sample path", () =>
            {
                using var f = new Fixture("selection"); string selected = f.WritePackaged("小型/選取.mrtsim.json"); var window = new UpdateCenterWindow(f.Service);
                Field<ListBox>(window, "library").SelectedIndex = 0; Click(window, "load"); Assert(window.SelectedSamplePath == selected && f.Handler.Requests == 0, "Load did not return the exact local path.");
            });
            Test("check reports no update and restores controls", () =>
            {
                using var f = new Fixture("no-update"); var window = new UpdateCenterWindow(f.Service);
                try
                {
                    Click(window, "check"); WaitIdle(window); string text = Field<TextBox>(window, "messages").Text;
                    Assert(text.Contains("程式：", StringComparison.Ordinal) && text.Contains("資料：", StringComparison.Ordinal) && text.Contains("尚未發佈", StringComparison.Ordinal), "No-update result is not readable.");
                    Assert(f.Handler.Requests == 2 && Field<Button>(window, "check").IsEnabled && !Field<Button>(window, "cancel").IsEnabled && !Directory.Exists(f.State), "Checking changed state or left the window busy.");
                }
                finally { window.Close(); }
            });
            Test("HTTP failure is displayed and retry recovers", () =>
            {
                using var f = new Fixture("check-error"); f.Handler.FailNetwork = true; var window = new UpdateCenterWindow(f.Service);
                try
                {
                    Click(window, "check"); WaitIdle(window); Assert(Field<TextBox>(window, "messages").Text.Contains("更新未完成", StringComparison.Ordinal), "HTTP failure was not displayed.");
                    Assert(Field<Button>(window, "check").IsEnabled && !Field<Button>(window, "cancel").IsEnabled, "HTTP failure left the UI busy.");
                    f.Handler.FailNetwork = false; Click(window, "check"); WaitIdle(window); Assert(Field<TextBox>(window, "messages").Text.Contains("尚未發佈", StringComparison.Ordinal), "Retry did not recover.");
                }
                finally { window.Close(); }
            });
            Test("valid data update runs Engine validation and refreshes library", () =>
            {
                using var f = new Fixture("data-success"); string original = f.WritePackaged("原始.mrtsim.json");
                string userSave = Path.Combine(f.Install, "使用者存檔.mrtsim.json"); File.WriteAllText(userSave, "keep-user-save");
                f.Handler.Offers = [Package(UpdateKind.Data, "1.0.0", new() { ["小型/更新範例.mrtsim.json"] = Project, ["README.md"] = Encoding.UTF8.GetBytes("maintenance examples") })]; var window = new UpdateCenterWindow(f.Service);
                try
                {
                    Click(window, "check"); WaitIdle(window); Assert(Field<Button>(window, "dataInstall").IsEnabled, "Data offer did not enable installation."); Click(window, "dataInstall"); WaitIdle(window);
                    Assert(f.Service.DataVersion == "1.0.0" && Field<TextBlock>(window, "versions").Text.Contains("1.0.0", StringComparison.Ordinal), "Data version did not activate.");
                    var item = f.Service.Library().Single(); Assert(item.Name.Replace('\\', '/') == "小型/更新範例.mrtsim.json" && item.Path.StartsWith(f.State, StringComparison.OrdinalIgnoreCase), "Library did not switch to managed data.");
                    Assert(Field<ListBox>(window, "library").Items.Count == 1 && Field<TextBox>(window, "messages").Text.Contains("範例資料庫已更新", StringComparison.Ordinal), "UI did not refresh after activation.");
                    Assert(File.Exists(original) && File.ReadAllText(userSave) == "keep-user-save", "Data update changed packaged samples or user saves.");
                    _ = TopologyProjectFormat.CreateRuntime(TopologyProjectFormat.Deserialize(File.ReadAllText(item.Path)));
                }
                finally { window.Close(); }
            });
            Test("invalid Engine document never replaces active data", () =>
            {
                using var f = new Fixture("data-invalid"); f.Handler.Offers = [Package(UpdateKind.Data, "1.0.0", new() { ["有效.mrtsim.json"] = Project })]; var window = new UpdateCenterWindow(f.Service);
                try
                {
                    Click(window, "check"); WaitIdle(window); Click(window, "dataInstall"); WaitIdle(window);
                    string directory = f.Service.DataDirectory; string pointer = Path.Combine(f.State, "Data", "active.json"); byte[] previous = File.ReadAllBytes(pointer);
                    // ZIP and file hashes are valid; only the real Engine schema/catalog validation rejects this document.
                    f.Handler.Offers = [Package(UpdateKind.Data, "1.0.1", new() { ["無效.mrtsim.json"] = Encoding.UTF8.GetBytes("{\"schemaVersion\":8}") })];
                    Click(window, "check"); WaitIdle(window); Click(window, "dataInstall"); WaitIdle(window);
                    Assert(Field<TextBox>(window, "messages").Text.Contains("更新未完成", StringComparison.Ordinal), "Engine failure was not shown.");
                    Assert(f.Service.DataVersion == "1.0.0" && f.Service.DataDirectory == directory && File.ReadAllBytes(pointer).SequenceEqual(previous), "Invalid data replaced the active pointer.");
                    Assert(!Directory.Exists(Path.Combine(f.State, "Data", "versions", "1.0.1")) && f.Service.Library().Single().Name == "有效.mrtsim.json" && Field<Button>(window, "check").IsEnabled, "Invalid version entered the library or left UI busy.");
                }
                finally { window.Close(); }
            });
            Test("corrupt managed-data pointer is caught by the main menu without changing workspace", () =>
            {
                using var f = new Fixture("corrupt-pointer"); Directory.CreateDirectory(Path.Combine(f.State, "Data"));
                var main = new MainWindow();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(MainWindow).GetField("_createUpdateService", flags)!.SetValue(main, (Func<AppUpdateService>)(() => new(new HttpClient(new FakeHandler()), f.State, f.Install)));
                string Capture() => JsonSerializer.Serialize((SimulationProjectDocument)typeof(MainWindow).GetMethod("CaptureProjectDocument", flags)!.Invoke(main, null)!);
                string before = Capture(); var active = typeof(MainWindow).GetField("_activeTopologyProjectDocument", flags)!.GetValue(main);
                try
                {
                    foreach (string corrupt in new[] { "{", "{\"version\":\"1.0.0\"}" })
                    {
                        File.WriteAllText(Path.Combine(f.State, "Data", "active.json"), corrupt);
                        var menu = Descendants(main).OfType<MenuItem>().Single(item => AutomationProperties.GetName(item) == "更新中心");
                        menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                        var warning = (TextBlock)main.FindName("ValidationTextBlock");
                        Assert(((Border)main.FindName("ValidationBorder")).Visibility == Visibility.Visible && warning.Text.Contains("更新中心", StringComparison.Ordinal), "Corrupt pointer was not converted into a validation message.");
                        Assert(Capture() == before && ReferenceEquals(active, typeof(MainWindow).GetField("_activeTopologyProjectDocument", flags)!.GetValue(main)), "Opening a broken update center changed the workspace.");
                        Assert(!app.Windows.OfType<UpdateCenterWindow>().Any(window => window.IsVisible), "Broken data state opened a modal update window.");
                        foreach (var failed in app.Windows.OfType<UpdateCenterWindow>().ToArray()) failed.Close();
                    }
                }
                finally
                {
                    if (typeof(MainWindow).GetMethod("StopCurrentPlaybackResourcesAsync", flags)!.Invoke(main, null) is Task stop)
                    { PumpUntil(() => stop.IsCompleted); stop.GetAwaiter().GetResult(); }
                    typeof(MainWindow).GetField("_closeAfterPlaybackShutdown", flags)!.SetValue(main, true); main.Close();
                }
            });
            Test("recovery button handles missing files, busy operations, and newest isolated backup", () =>
            {
                using var f = new Fixture("recovery"); var window = new UpdateCenterWindow(f.Service);
                var button = Descendants(window).OfType<Button>().Single(item => Equals(item.Content, "載入最近的更新前專案副本"));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(window.SelectedSamplePath is null && Field<TextBox>(window, "messages").Text.Contains("目前沒有", StringComparison.Ordinal), "Missing recovery file was not handled.");
                string folder = Path.Combine(f.State, "Recovery"); Directory.CreateDirectory(folder);
                string older = Path.Combine(folder, "old.mrtsim.json"), newer = Path.Combine(folder, "new.mrtsim.json");
                File.WriteAllBytes(older, Project); File.WriteAllBytes(newer, Project); File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-2)); File.SetLastWriteTimeUtc(newer, DateTime.UtcNow.AddHours(-1));
                Click(window, "check"); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(window.SelectedSamplePath is null, "Recovery selection bypassed the active-operation gate."); WaitIdle(window);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert(window.SelectedSamplePath == newer, "Recovery did not choose the latest isolated backup.");
            });
            Test("760x660 offscreen update center keeps primary controls in the viewport", () =>
            {
                using var f = new Fixture("snapshot");
                f.Handler.Offers = [Package(UpdateKind.Data, "1.0.0", new() { ["小型/10-三站基準範例.mrtsim.json"] = Project, ["中型/20-維護範例.mrtsim.json"] = Project, ["README.md"] = Encoding.UTF8.GetBytes("維護範例資料庫") })];
                var window = new UpdateCenterWindow(f.Service) { Width = 760, Height = 660, Left = -10000, Top = -10000, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
                try
                {
                    Click(window, "check"); WaitIdle(window); Click(window, "dataInstall"); WaitIdle(window);
                    window.Show(); window.UpdateLayout(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render); window.UpdateLayout();
                    var viewport = new Rect(0, 0, 760, 660);
                    foreach (string name in new[] { "check", "appDownload", "appInstall", "dataInstall", "cancel", "load" })
                    {
                        var control = Field<Button>(window, name); var bounds = control.TransformToAncestor(window).TransformBounds(new Rect(control.RenderSize));
                        Assert(bounds.Width > 0 && bounds.Height > 0 && viewport.Contains(bounds), "Button outside snapshot viewport: " + name);
                    }
                    var recovery = Descendants(window).OfType<Button>().Single(item => Equals(item.Content, "載入最近的更新前專案副本"));
                    Assert(viewport.Contains(recovery.TransformToAncestor(window).TransformBounds(new Rect(recovery.RenderSize))), "Recovery button is outside the snapshot viewport.");
                    Assert(Field<TextBlock>(window, "versions").ActualHeight > 0 && Field<ListBox>(window, "library").Items.Count == 2, "Version text or sample library was not laid out.");
                    var bitmap = new RenderTargetBitmap(760, 660, 96, 96, PixelFormats.Pbgra32);
                    // WPF renders client visuals without native chrome. Give the PNG's entire canvas an opaque background.
                    var background = new DrawingVisual(); using (var drawing = background.RenderOpen()) drawing.DrawRectangle(Brushes.White, null, viewport);
                    bitmap.Render(background); bitmap.Render(window);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                    string output = Path.GetFullPath(Path.Combine("artifacts", "update-wpf-tests", "update-center-760x660.png")); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                    using (var stream = File.Create(output)) png.Save(stream); Console.WriteLine("offscreenSnapshot=" + output);
                }
                finally { window.Close(); }
            });
            Console.WriteLine($"PASS {passed}/{passed} update WPF/service tests (STA, fake HTTP, isolated temporary state)"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { app.Shutdown(); Directory.Delete(Root, true); }
    }
    private static void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static T Field<T>(object instance, string name) => (T)(instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance) ?? throw new InvalidOperationException("Missing field " + name));
    private static void Click(UpdateCenterWindow window, string field) { var button = Field<Button>(window, field); Assert(button.IsEnabled, "Attempted disabled click: " + field); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
    private static void PumpUntil(Func<bool> ready)
    {
        if (ready()) return;
        var frame = new DispatcherFrame(); var started = DateTime.UtcNow;
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (ready() || DateTime.UtcNow - started > TimeSpan.FromSeconds(30)) frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop(); Assert(ready(), "Dispatcher operation timed out.");
    }
    private static void WaitIdle(UpdateCenterWindow window)
    {
        bool IsIdle() => typeof(UpdateCenterWindow).GetField("operation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window) is null;
        if (IsIdle()) return; var frame = new DispatcherFrame(); var started = DateTime.UtcNow;
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (IsIdle() || DateTime.UtcNow - started > TimeSpan.FromSeconds(30)) frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop(); Assert(IsIdle(), "Update operation timed out.");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(parent)) if (child is DependencyObject element) { yield return element; foreach (var nested in Descendants(element)) yield return nested; }
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static FixturePackage Package(UpdateKind kind, string version, Dictionary<string, byte[]> files)
    {
        using var memory = new MemoryStream(); using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true)) foreach (var file in files) { var entry = zip.CreateEntry(file.Key); using var output = entry.Open(); output.Write(file.Value); }
        byte[] bytes = memory.ToArray(); string name = kind == UpdateKind.Application ? "application.zip" : "data.zip"; string tag = (kind == UpdateKind.Application ? "v" : "data-v") + version;
        var manifest = new UpdateManifest(1, UpdateConstants.Product, kind, version, "0.0.0", TopologyProjectFormat.CurrentSchemaVersion, name, Hash(bytes), bytes.Length, files.Select(x => new UpdateFile(x.Key, Hash(x.Value), x.Value.Length)).ToArray());
        var offer = new UpdateOffer(manifest, new(name, new Uri($"https://github.com/{UpdateConstants.Repository}/releases/download/{tag}/{name}"), bytes.Length, "sha256:" + Hash(bytes)), "fixture release", new Uri($"https://github.com/{UpdateConstants.Repository}/releases/tag/{tag}")); return new(offer, bytes, tag);
    }
    private sealed record FixturePackage(UpdateOffer Offer, byte[] Bytes, string Tag);
    private sealed class Fixture : IDisposable
    {
        public string State { get; } public string Install { get; } public FakeHandler Handler { get; } = new(); public AppUpdateService Service { get; }
        public Fixture(string name) { string directory = Path.Combine(Root, name); State = Path.Combine(directory, "state"); Install = Path.Combine(directory, "install"); Directory.CreateDirectory(Install); Service = new(new HttpClient(Handler), State, Install); }
        public string WritePackaged(string relative) { string path = Path.Combine(Install, "samples", relative.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, Project); return path; }
        public void Dispose() => Service.Dispose();
    }
    private sealed class FakeHandler : HttpMessageHandler
    {
        public FixturePackage[] Offers { get; set; } = []; public int Requests { get; private set; } public bool FailNetwork { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++; await Task.Delay(1, token); if (FailNetwork) throw new HttpRequestException("Fixture network unavailable");
            if (request.RequestUri!.Host == "api.github.com")
            {
                object Asset(string name, string tag, byte[] bytes) => new { name, browser_download_url = $"https://github.com/{UpdateConstants.Repository}/releases/download/{tag}/{name}", size = bytes.Length, digest = "sha256:" + Hash(bytes) };
                byte[] metadata = JsonSerializer.SerializeToUtf8Bytes(Offers.Select(x => { byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(x.Offer.Manifest, UpdateJson.Options); string name = x.Offer.Manifest.Kind == UpdateKind.Application ? "mrt-application-update.json" : "mrt-data-update.json"; return new { draft = false, prerelease = false, tag_name = x.Tag, body = x.Offer.Notes, assets = new[] { Asset(name, x.Tag, manifest), Asset(x.Offer.Package.Name, x.Tag, x.Bytes) } }; }));
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(metadata) };
            }
            foreach (var offer in Offers) if (request.RequestUri.AbsolutePath.Contains("/" + offer.Tag + "/", StringComparison.Ordinal)) return new(HttpStatusCode.OK) { Content = new ByteArrayContent(request.RequestUri.AbsolutePath.EndsWith(".json", StringComparison.Ordinal) ? JsonSerializer.SerializeToUtf8Bytes(offer.Offer.Manifest, UpdateJson.Options) : offer.Bytes) };
            throw new InvalidOperationException("Unexpected fake request " + request.RequestUri);
        }
    }
}
