using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FolderNotifier;

namespace FolderNotifier.UiTests;

internal static class UpdateTests
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("MZ simulated installer - never executed");
    private static readonly string Hash = Convert.ToHexString(SHA256.HashData(Payload)).ToLowerInvariant();
    private static readonly string Future = $"v{AppUpdater.CurrentVersion.Major + 1}.0.0";

    internal static async Task Run(string previews, string root)
    {
        Check(AppUpdater.ParseVersion("v1.9.0") == AppUpdater.ParseVersion("1.9.0.0"),
            "Three- and four-component versions compare equally");
        foreach (var tag in new[] { "v1.9.0-beta", "1.9", "1.9.0;bad", "v999999999999999.0.0" })
            Reject(() => AppUpdater.ParseVersion(tag), "Invalid or prerelease version is rejected: " + tag);
        var service = new AppUpdater();
        var stable = Parse(Json());
        Reject(() => Parse(Json(draft: true)), "Draft updates are rejected");
        Reject(() => Parse(Json(prerelease: true)), "Prerelease updates are rejected");
        Reject(() => Parse(Json().Replace(AppUpdater.Repository, "https://example.com/repo")),
            "Foreign release and download URLs are rejected");
        Reject(() => Parse(Json().Replace("FolderNotifier-Setup.exe", "Other.exe")), "Missing installer is rejected");
        Reject(() => Parse(Json(digest: false, checksum: false)), "Unverifiable installers are rejected");
        var downloads = Path.Combine(root, "updates");
        using var handler = new FakeHandler();
        using var client = new HttpClient(handler);
        service = new AppUpdater(client);
        handler.Reply = (request, _) => Task.FromResult(request.RequestUri!.Host == "api.github.com"
            ? Text(Json()) : Bytes(Payload));
        var release = await service.CheckAsync(CancellationToken.None);
        Check(handler.HasHeaders, "Public release check sends its application identity and GitHub Accept header");
        var file = await service.DownloadAsync(release, new Progress<int>(), CancellationToken.None, downloads);
        Check(File.ReadAllBytes(file).SequenceEqual(Payload), "Verified installer bytes are saved intact");
        Delete(file);
        handler.Reply = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith(".sha256")
            ? Text(Hash + "  FolderNotifier-Setup.exe\n") : Bytes(Payload));
        var fallback = Parse(Json(digest: false));
        file = await service.DownloadAsync(fallback, new Progress<int>(), CancellationToken.None, downloads);
        Check(File.Exists(file), "Published SHA-256 file verifies releases without an API digest");
        Delete(file);
        handler.Reply = (_, _) => Task.FromResult(Bytes(Payload.Select(b => (byte)(b ^ 1)).ToArray()));
        await RejectAsync(() => service.DownloadAsync(stable, new Progress<int>(), CancellationToken.None, downloads),
            "Same-sized corrupted download is rejected");
        Check(!Directory.EnumerateFileSystemEntries(downloads).Any(), "Failed verification removes its temporary payload");
        handler.Reply = (_, _) => Task.FromResult(Bytes(Payload[..^1]));
        await RejectAsync(() => service.DownloadAsync(stable, new Progress<int>(), CancellationToken.None, downloads),
            "Truncated download is rejected");
        handler.Reply = (_, _) => Task.FromResult(Text(Hash + "  Different.exe"));
        await RejectAsync(() => service.DownloadAsync(fallback, new Progress<int>(), CancellationToken.None, downloads),
            "Checksum for another filename is rejected");
        using (var cancel = new CancellationTokenSource())
        {
            handler.Reply = (_, _) => Task.FromResult(Bytes(Payload));
            try
            {
                await service.DownloadAsync(stable, new ImmediateProgress(value => { if (value > 0) cancel.Cancel(); }),
                    cancel.Token, downloads);
                throw new Exception("Canceled download unexpectedly succeeded");
            }
            catch (OperationCanceledException) { }
            Check(!Directory.EnumerateFileSystemEntries(downloads).Any(), "Cancellation removes partial and completed temporary downloads");
        }
        var start = AppUpdater.InstallerStartInfo(@"C:\업데이트 경로\FolderNotifier-Setup.exe");
        Check(start.UseShellExecute && start.Arguments.StartsWith("/FROMAPP=1 /DIR=\"") && start.Arguments.EndsWith('"'),
            "Installer handoff waits for app shutdown and targets the current installation with quoted paths");

        string? launched = null;
        var window = new MainWindow(Path.Combine(root, "update-settings.json"), new NoStartup(), service,
            path => launched = path, downloads);
        window.Show();
        try
        {
            window.SettingsNavButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            handler.Reply = (request, _) => Task.FromResult(request.RequestUri!.Host == "api.github.com" ? Text(Json()) : Bytes(Payload));
            await window.CheckForUpdatesAsync();
            Check(window.CurrentVersionText.Text.Contains(AppUpdater.FormatVersion(AppUpdater.CurrentVersion))
                && window.InstallUpdateButton.IsVisible && window.ReleaseNotesText.Text.Contains("업데이트 변경 내용"),
                "Settings show current and newer versions, release notes and a visible install button");
            window.UpdatePanel.BringIntoView();
            await Task.Delay(100);
            window.UpdateLayout();
            Check(window.RootScroller.ScrollableHeight > 0
                && window.InstallUpdateButton.TransformToAncestor(window).Transform(new Point(0, window.InstallUpdateButton.ActualHeight)).Y < window.ActualHeight,
                "Settings scroll to the visible install action without clipping the new update section");
            Save(window, Path.Combine(previews, "ui-preview-updates.png"));
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            window.UpdatePanel.BringIntoView();
            await Task.Delay(100);
            window.RootScroller.ScrollToBottom();
            await Task.Delay(100);
            Check(window.InstallUpdateButton.TransformToAncestor(window).Transform(new Point(window.InstallUpdateButton.ActualWidth, window.InstallUpdateButton.ActualHeight)).X < window.ActualWidth
                && window.InstallUpdateButton.TransformToAncestor(window).Transform(new Point(0, window.InstallUpdateButton.ActualHeight)).Y < window.ActualHeight,
                "Update installation remains reachable at the minimum window size");
            Save(window, Path.Combine(previews, "ui-preview-updates-minimum.png"));
            window.FolderInput.Text = Path.Combine(root, "감시 폴더");
            _ = window.StartMonitoringAsync();
            for (var attempt = 0; !window.IsRealtimeMonitoring && attempt < 100; attempt++) await Task.Delay(100);
            await window.InstallAvailableUpdateAsync();
            Check(launched is not null && File.ReadAllBytes(launched).SequenceEqual(Payload) && window.IsRealtimeMonitoring,
                "Verified download reaches only the injected installer handoff while monitoring remains active");
            Delete(launched!);
            launched = null;
            handler.Reply = (request, _) => Task.FromResult(request.RequestUri!.Host == "api.github.com" ? Text(Json()) : Bytes(new byte[Payload.Length]));
            await window.CheckForUpdatesAsync();
            await window.InstallAvailableUpdateAsync();
            Check(launched is null && window.UpdateDetailText.Text.Contains("검증")
                && window.IsRealtimeMonitoring && window.InstallUpdateButton.IsEnabled,
                "Integrity failure never launches an installer, keeps monitoring and allows retry");
            foreach (var tag in new[] { "v" + AppUpdater.FormatVersion(AppUpdater.CurrentVersion), "v1.0.0" })
            {
                handler.Reply = (_, _) => Task.FromResult(Text(Json(tag: tag)));
                await window.CheckForUpdatesAsync();
                await window.InstallAvailableUpdateAsync();
                Check(window.InstallUpdateButton.Visibility == Visibility.Collapsed && launched is null,
                    "Current and older releases cannot be installed: " + tag);
            }
            handler.Reply = async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Text(Json()); };
            var requests = handler.Count;
            var pending = window.CheckForUpdatesAsync();
            await window.CheckForUpdatesAsync();
            Check(!window.CheckUpdateButton.IsEnabled && handler.Count == requests + 1,
                "Repeated checks do not overlap while a request is pending");
            window.CancelUpdateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await pending;
            Check(window.UpdateStatusText.Text.Contains("취소") && window.CheckUpdateButton.IsEnabled,
                "Cancel restores the update controls without stopping monitoring");
            handler.Reply = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            await window.CheckForUpdatesAsync();
            Check(window.UpdateDetailText.Text.Contains("연결") && window.InstallUpdateButton.Visibility == Visibility.Collapsed,
                "Rate limits and network failures explain retry and clear stale installers");
            handler.Reply = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            await window.CheckForUpdatesAsync();
            Check(window.UpdateDetailText.Text.Contains("공개된 정식 버전"), "An unpublished release has a distinct message");
            handler.Reply = (_, _) => Task.FromResult(Text("{"));
            await window.CheckForUpdatesAsync();
            Check(window.UpdateStatusText.Text.Contains("완료하지 못"), "Malformed release responses leave the UI usable");
            handler.Reply = (_, _) => Task.FromException<HttpResponseMessage>(new TaskCanceledException());
            await window.CheckForUpdatesAsync();
            Check(window.UpdateDetailText.Text.Contains("시간이 초과"), "Timeout is distinguished from user cancellation");
        }
        finally
        {
            await window.StopMonitoringAsync();
            window.ExitApp();
            await Task.Delay(100);
        }
    }

    private static UpdateRelease Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return AppUpdater.ParseRelease(document.RootElement);
    }
    private static string Json(string? tag = null, bool draft = false, bool prerelease = false,
        bool digest = true, bool checksum = true) => JsonSerializer.Serialize(new
        {
            tag_name = tag ?? Future, draft, prerelease,
            html_url = AppUpdater.Repository + "/releases/tag/" + (tag ?? Future),
            body = "업데이트 변경 내용\n• 버튼 디자인 개선\n• 새 버전 확인 및 설치",
            assets = (checksum ? new[] { "FolderNotifier-Setup.exe", "FolderNotifier-Setup.sha256" }
                : new[] { "FolderNotifier-Setup.exe" }).Select(name => new
                {
                    name, size = Payload.Length,
                    browser_download_url = AppUpdater.Repository + "/releases/download/" + (tag ?? Future) + "/" + name,
                    digest = digest ? "sha256:" + Hash : null
                })
        });
    private static HttpResponseMessage Text(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    private static HttpResponseMessage Bytes(byte[] value) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(value) };
    private static void Delete(string file) { File.Delete(file); Directory.Delete(Path.GetDirectoryName(file)!); }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (InvalidDataException) { Check(true, message); return; }
        throw new Exception(message);
    }
    private static async Task RejectAsync(Func<Task<string>> action, string message)
    {
        try { await action(); } catch (InvalidDataException) { Check(true, message); return; }
        throw new Exception(message);
    }
    private static void Check(bool passed, string message)
    {
        if (!passed) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
    private static void Save(Window window, string path)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); png.Save(file);
    }
    private sealed class ImmediateProgress(Action<int> report) : IProgress<int> { public void Report(int value) => report(value); }
    private sealed class NoStartup : IStartupRegistration
    {
        public bool IsEnabled => false;
        public void SetEnabled(bool enabled) { }
    }
    private sealed class FakeHandler : HttpMessageHandler
    {
        internal Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Reply = (_, _) => Task.FromResult(Text(Json()));
        internal int Count;
        internal bool HasHeaders;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            if (request.RequestUri!.Host == "api.github.com")
                HasHeaders = request.Headers.UserAgent.ToString().StartsWith("FolderNotifier/") && request.Headers.Accept.ToString().Contains("application/vnd.github+json");
            return Reply(request, cancellationToken);
        }
    }
}
