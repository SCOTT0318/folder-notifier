using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Windows;

namespace FolderNotifier;

internal partial class MainWindow
{
    private readonly AppUpdater updater;
    private readonly Action<string> installUpdate;
    private readonly string? updateDownloadRoot;
    private CancellationTokenSource? updateCancellation;
    private UpdateRelease? availableRelease;

    private async void OpenUpdatesClicked(object sender, RoutedEventArgs e)
    {
        NavigateTo(WorkspacePage.Settings);
        UpdatePanel.BringIntoView();
        await CheckForUpdatesAsync();
        UpdatePanel.BringIntoView();
    }

    private async void CheckUpdateClicked(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync();
    private void CancelUpdateClicked(object sender, RoutedEventArgs e) => updateCancellation?.Cancel();

    internal async Task CheckForUpdatesAsync()
    {
        if (updateCancellation is not null || closing) return;
        availableRelease = null;
        ReleasePageButton.Visibility = ReleaseNotesScroller.Visibility = InstallUpdateButton.Visibility = Visibility.Collapsed;
        SetUpdateBusy(true, true);
        UpdateStatusText.Text = "최신 버전을 확인하고 있어요";
        UpdateDetailText.Text = "공식 GitHub 배포 정보를 조회합니다…";
        using var operation = new CancellationTokenSource();
        updateCancellation = operation;
        try
        {
            var release = await updater.CheckAsync(operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            availableRelease = release;
            ReleasePageButton.Visibility = Visibility.Visible;
            ReleaseNotesText.Text = string.IsNullOrWhiteSpace(release.Notes) ? "등록된 변경 내용이 없습니다." : release.Notes;
            ReleaseNotesScroller.Visibility = Visibility.Visible;
            var newer = release.Version > AppUpdater.CurrentVersion;
            UpdateStatusText.Text = newer ? $"새 버전 v{release.DisplayVersion}을 설치할 수 있어요"
                : release.Version == AppUpdater.CurrentVersion ? "최신 버전을 사용하고 있어요" : "최신 공개 버전보다 새로운 버전을 사용하고 있어요";
            UpdateDetailText.Text = $"현재 v{AppUpdater.FormatVersion(AppUpdater.CurrentVersion)} · 최신 공개 v{release.DisplayVersion} · 확인 {DateTime.Now:MM/dd HH:mm}";
            InstallUpdateButton.Visibility = newer ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) when (IsUpdateError(ex)) { ShowUpdateError(ex, operation.Token); }
        finally { updateCancellation = null; SetUpdateBusy(false); }
    }

    private async void InstallUpdateClicked(object sender, RoutedEventArgs e)
    {
        if (availableRelease is not { } release || updateCancellation is not null) return;
        var answer = MessageBox.Show(this,
            $"v{release.DisplayVersion}을 다운로드하고 설치하시겠습니까?\n\n설치기가 열리면 앱과 감시가 종료됩니다. 설치 중 도착한 파일은 알림을 받지 못합니다.\n감시 폴더와 설정은 유지됩니다.",
            "폴더 알림 업데이트", MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes) await InstallAvailableUpdateAsync();
    }

    internal async Task InstallAvailableUpdateAsync()
    {
        if (availableRelease is not { } release || release.Version <= AppUpdater.CurrentVersion ||
            updateCancellation is not null || closing) return;
        SetUpdateBusy(true);
        UpdateStatusText.Text = $"v{release.DisplayVersion} 다운로드 중";
        UpdateDetailText.Text = "다운로드 중에는 감시가 계속됩니다. 완료 후 파일을 검증합니다.";
        UpdateProgress.Value = 0;
        using var operation = new CancellationTokenSource();
        updateCancellation = operation;
        string? downloadedPath = null;
        try
        {
            downloadedPath = await updater.DownloadAsync(release, new Progress<int>(value =>
            {
                if (updateCancellation != operation || operation.IsCancellationRequested) return;
                UpdateProgress.Value = value;
                UpdateDetailText.Text = $"다운로드 {value}% · {release.Size / 1048576d:0.0} MB · 완료 후 SHA-256 검증";
            }), operation.Token, updateDownloadRoot);
            operation.Token.ThrowIfCancellationRequested();
            UpdateStatusText.Text = "검증 완료 · 설치 프로그램 시작";
            installUpdate(downloadedPath);
            downloadedPath = null; // The installer now owns this temporary payload.
        }
        catch (Exception ex) when (IsUpdateError(ex)) { ShowUpdateError(ex, operation.Token); }
        finally
        {
            if (downloadedPath is not null)
            {
                try { File.Delete(downloadedPath); Directory.Delete(Path.GetDirectoryName(downloadedPath)!); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            updateCancellation = null;
            SetUpdateBusy(false);
        }
    }

    private void SetUpdateBusy(bool busy, bool checking = false)
    {
        CheckUpdateButton.IsEnabled = InstallUpdateButton.IsEnabled = !busy;
        CancelUpdateButton.Visibility = UpdateProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgress.IsIndeterminate = busy && checking;
    }

    private static bool IsUpdateError(Exception ex) => ex is HttpRequestException or IOException or InvalidDataException or
        UnauthorizedAccessException or OperationCanceledException or JsonException or KeyNotFoundException or
        InvalidOperationException or FormatException or Win32Exception;

    private void ShowUpdateError(Exception error, CancellationToken token)
    {
        UpdateStatusText.Text = error is OperationCanceledException && token.IsCancellationRequested
            ? "업데이트 작업을 취소했어요" : "업데이트를 완료하지 못했어요";
        UpdateDetailText.Text = error switch
        {
            OperationCanceledException when token.IsCancellationRequested => "필요할 때 다시 시도할 수 있습니다.",
            OperationCanceledException => "응답 시간이 초과되었습니다. 인터넷 연결을 확인하고 다시 시도해 주세요.",
            HttpRequestException => "배포 서버에 연결할 수 없습니다. 인터넷 연결을 확인하고 잠시 후 다시 시도해 주세요.",
            InvalidDataException => error.Message,
            _ => "업데이트 정보를 읽거나 설치 파일을 준비하지 못했습니다. 다시 시도해 주세요."
        };
    }

    private void ReleasePageClicked(object sender, RoutedEventArgs e)
    {
        if (availableRelease is null) return;
        try { Process.Start(new ProcessStartInfo(availableRelease.Page.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            UpdateDetailText.Text = "배포 페이지를 열지 못했습니다. 기본 브라우저를 확인해 주세요.";
        }
    }
}
