using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FolderNotifier;
using Microsoft.Win32;

namespace FolderNotifier.UiTests;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ShellIdentity.Configure();
        Marshal.ThrowExceptionForHR(GetCurrentProcessExplicitAppUserModelID(out var appId));
        try { Check(Marshal.PtrToStringUni(appId) == ShellIdentity.AppUserModelId, "Windows receives the explicit taskbar app identity"); }
        finally { Marshal.FreeCoTaskMem(appId); }
        var parent = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
        var root = Path.Combine(parent, "FolderNotifier-tests-" + Guid.NewGuid().ToString("N"));
        var folder = Directory.CreateDirectory(Path.Combine(root, "감시 폴더")).FullName;
        var config = Path.Combine(root, "config", "settings.json");
        var app = new App();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var startup = new TestStartupRegistration();
        var window = new MainWindow(config, startup);
        Exception? failure = null;
        window.Loaded += async (_, _) =>
        {
            try
            {
                await Task.Delay(200);
                Check(window.Height <= SystemParameters.WorkArea.Height && window.Width <= SystemParameters.WorkArea.Width,
                    "Initial window fits the available desktop");
                SavePreview(window, Path.Combine(parent, "ui-preview-idle.png"));
                Check(window.MonitorNavButton.IsVisible && window.ActivityNavButton.IsVisible && window.SettingsNavButton.IsVisible
                    && window.FolderGraphic.Source is DrawingImage,
                    "The redesigned workspace includes navigation and a graphical folder panel");
                Check(ReferenceEquals(window.InboxGraphic.Source, NotificationArtwork.Bell)
                    && window.UnreadCountBadge.Visibility == Visibility.Collapsed && !window.NotificationInboxButton.IsEnabled,
                    "The dedicated notification card starts with a drawn bell and no unread badge");
                Check(window.AppVersionText.Text == "v" + typeof(MainWindow).Assembly.GetName().Version!.ToString(3),
                    "The visible version identifies the installed UI revision");
                window.FolderInput.Focus();
                window.TestNotificationButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(200);
                var popup = window.NotificationPopup!;
                Check(popup is { IsVisible: true } && popup.NotificationKind.Text == "알림 미리보기"
                    && popup.DisplayedFiles.Select(file => file.Name).SequenceEqual(new[] { "월간 보고서.pdf", "입고 목록.xlsx" }),
                    "Preview displays example filenames through the actual file notification layout");
                Check(popup.Topmost && !popup.ShowInTaskbar && !popup.ShowActivated && !popup.IsActive,
                    "Notification stays above other windows without stealing focus or adding a taskbar button");
                var workArea = SystemParameters.WorkArea;
                Check(popup.Left >= workArea.Left && popup.Top >= workArea.Top
                    && popup.Left + popup.ActualWidth <= workArea.Right && popup.Top + popup.ActualHeight <= workArea.Bottom,
                    "Notification is fully inside the desktop work area above the taskbar");
                Check(window.Activities.Any(row => row.Title == "알림 미리보기") && window.FileCount.Text == "0",
                    "Test is recorded in history without incrementing the new-file count");
                Check(window.UnreadNotificationCount == 1 && window.HasUnreadTrayIcon && window.TaskbarItemInfo.Overlay is not null
                    && window.TrayTooltip.Contains("미확인 1개") && window.MarkReadButton.Visibility == Visibility.Visible,
                    "Test notification sets the tray badge, taskbar overlay, unread count and tooltip");
                await WaitUntil(() => !popup.IsArriving);
                SavePreview(popup, Path.Combine(parent, "ui-preview-notification.png"));
                Check(popup.NotificationGraphic.Source is DrawingImage && popup.NotificationGraphic.ActualWidth >= 64
                    && popup.PopupCount.Text == "2" && popup.PopupCountBadge.IsVisible,
                    "The popup displays large vector bell artwork and a graphical count badge");
                Check(Images(popup.FileList).Any(image => ReferenceEquals(image.Source, popup.DisplayedFiles[0].Icon))
                    && Images(popup.FileList).Any(image => ReferenceEquals(image.Source, popup.DisplayedFiles[1].Icon))
                    && !ReferenceEquals(popup.DisplayedFiles[0].Icon, popup.DisplayedFiles[1].Icon),
                    "PDF and spreadsheet rows render distinct graphical file icons");
                Check(ReferenceEquals(window.InboxGraphic.Source, NotificationArtwork.UnreadBell)
                    && window.UnreadCountBadge.IsVisible && window.UnreadCountText.Text == "1"
                    && window.InboxSummary.Text.Contains("월간 보고서.pdf"),
                    "New notifications update the main card bell, red badge and filename summary");
                window.TestNotificationButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(ReferenceEquals(popup, window.NotificationPopup), "Repeated test reuses one popup");
                popup.DismissButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => !popup.IsVisible);
                Check(!popup.IsVisible, "Notification close button dismisses the popup");
                Check(window.UnreadNotificationCount == 2 && window.HasUnreadTrayIcon && window.TaskbarItemInfo.Overlay is not null,
                    "Dismissing a popup preserves the unread badge and accumulates notifications");
                window.NotificationInboxButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(popup.IsVisible && window.UnreadNotificationCount == 2 && window.UnreadCountText.Text == "2",
                    "The main notification card can reopen the latest alert without adding unread counts");
                SavePreview(window, Path.Combine(parent, "ui-preview-unread-card.png"));
                popup.Dismiss();
                await WaitUntil(() => !popup.IsVisible);
                await TestNotificationLifetime();
                await TestArrivalAnimation(parent);
                await TestDepartureAnimation(parent);
                await TestNotificationDuration(root);
                await TestFilenameLayout(parent);
                window.MarkReadButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(window.UnreadNotificationCount == 0 && !window.HasUnreadTrayIcon && window.TaskbarItemInfo.Overlay is null
                    && window.MarkReadButton.Visibility == Visibility.Collapsed && window.FileCount.Text == "0",
                    "Mark-read clears both badges without changing detected-file totals");
                Check(ReferenceEquals(window.InboxGraphic.Source, NotificationArtwork.Bell)
                    && window.UnreadCountBadge.Visibility == Visibility.Collapsed && window.NotificationInboxButton.IsEnabled,
                    "Acknowledgment resets the card graphic while retaining access to the latest alert");
                TestIconFrames();
                window.FolderInput.Text = folder;
                Check(window.MonitorFolderName.Text == "감시 폴더" && (string)window.MonitorFolderName.ToolTip == folder,
                    "The folder panel identifies the selected folder and retains its full path");
                window.RecursiveSwitch.IsChecked = true;
                window.LoginStartupSwitch.IsChecked = true;
                Check(startup.IsEnabled, "Login startup is registered immediately");
                var loginSettings = AppSettings.Load(out _, config);
                Check(loginSettings.Folder == folder && loginSettings.IncludeSubfolders,
                    "Login startup persists folder without pressing Start");
                window.LoginStartupSwitch.IsChecked = false;
                Check(!startup.IsEnabled, "Unchecking removes login startup");
                startup.FailWrites = true;
                window.LoginStartupSwitch.IsChecked = true;
                Check(window.LoginStartupSwitch.IsChecked == false && !startup.IsEnabled
                    && window.Activities.Any(row => row.Title == "자동 실행 설정 실패"), "Failed registration rolls back the switch and explains the error");
                startup.FailWrites = false;
                TestIsolatedRegistry();
                window.StartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => window.StatusText.Text == "감시 중");
                Check(window.IsRealtimeMonitoring && window.StatusDetail.Text.Contains("실시간 이벤트 감시"),
                    "Monitoring starts with filesystem events and accurately reports its mode");
                Check(ReferenceEquals(window.FolderGraphic.Source, NotificationArtwork.ConnectedFolder),
                    "The folder graphic reflects an active realtime connection");
                Check(!window.FolderInput.IsEnabled && !window.RecursiveSwitch.IsEnabled && !window.AutoStartSwitch.IsEnabled,
                    "Monitoring locks the folder and options");
                Check(window.StopButton.IsEnabled, "Stop enabled while monitoring");
                Check(window.LoginStartupSwitch.IsEnabled && window.NotificationDurationSelector.IsEnabled,
                    "Login startup and notification duration remain editable while monitoring");
                var child = Directory.CreateDirectory(Path.Combine(folder, "하위")).FullName;
                File.WriteAllText(Path.Combine(child, "테스트 파일.txt"), "UI integration");
                await WaitUntil(() => window.FileCount.Text == "1");
                Check(window.Activities.Any(row => row.Title == "테스트 파일.txt" && row.IsFile), "Async scan updates activity and count");
                Check(window.EmptyState.Visibility == Visibility.Collapsed, "Activity replaces the empty state");
                Check(popup.IsVisible && popup.NotificationTitle.Text == "새 파일 도착"
                    && popup.DisplayedFiles.Any(file => file.Name == "테스트 파일.txt"), "New-file detection uses the same visible notification path");
                Check(window.UnreadNotificationCount == 1 && window.HasUnreadTrayIcon, "Detected files mark the app as unread");
                File.WriteAllText(Path.Combine(folder, "업무자료.pdf"), "test fixture only");
                File.WriteAllText(Path.Combine(folder, "입고 목록.xlsx"), "test fixture only");
                await WaitUntil(() => window.FileCount.Text == "3");
                Check(popup.NotificationTitle.Text == "새 파일 2개 도착"
                    && popup.DisplayedFiles.Any(file => file.Name == "업무자료.pdf") && popup.DisplayedFiles.Any(file => file.Name == "입고 목록.xlsx"),
                    "Multiple detected files appear together in one popup");
                Check(window.UnreadNotificationCount == 3 && window.TrayTooltip.Contains("미확인 3개"),
                    "Batch notifications count every file and retain the unread tooltip across scans");
                popup.Dismiss();
                await WaitUntil(() => !popup.IsVisible);
                Check(window.UnreadNotificationCount == 3 && window.HasUnreadTrayIcon && window.TaskbarItemInfo.Overlay is not null
                    && window.TrayTooltip.Contains("미확인 3개"), "Dismissing the popup preserves the unread badge");
                SavePreview(window, Path.Combine(parent, "ui-preview.png"));
                window.Width = window.MinWidth;
                window.Height = window.MinHeight;
                await Task.Delay(200);
                window.UpdateLayout();
                Console.WriteLine($"Minimum layout: window={window.ActualWidth}x{window.ActualHeight}, client={window.RootScroller.ActualWidth}x{window.RootScroller.ActualHeight}, scroll={window.RootScroller.ScrollableHeight}");
                Check(window.ActivityList.ActualHeight > 100, "Minimum window retains usable history");
                Check(window.StartButton.TransformToAncestor(window).Transform(new Point(window.StartButton.ActualWidth, 0)).X < window.ActualWidth,
                    "Primary action remains inside the minimum window");
                Check(window.RecursiveSwitch.TransformToAncestor(window).Transform(new Point(window.RecursiveSwitch.ActualWidth, 0)).X < window.ActualWidth,
                    "Settings remain inside the minimum window");
                SavePreview(window, Path.Combine(parent, "ui-preview-minimum.png"));
                window.SettingsNavButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(100);
                Check(window.CurrentPage == WorkspacePage.Settings && window.PageTitle.Text == "환경 설정"
                    && !window.MonitorCard.IsVisible && !window.ActivityCard.IsVisible && window.SettingsCard.IsVisible
                    && window.SettingsCard.ActualWidth > 500,
                    "Settings navigation switches to a dedicated full-width settings screen");
                Check(((SolidColorBrush)window.SettingsNavButton.Background).Color != ((SolidColorBrush)window.MonitorNavButton.Background).Color,
                    "Sidebar navigation visibly distinguishes the selected section");
                SavePreview(window, Path.Combine(parent, "ui-preview-settings.png"));
                var recordedFileNames = window.Activities.Where(row => row.IsFile).Select(row => row.Title).ToArray();
                window.ActivityNavButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(100);
                Check(window.CurrentPage == WorkspacePage.Activity && window.PageTitle.Text == "파일 기록"
                    && window.ActivityCard.IsVisible && !window.MonitorCard.IsVisible && !window.RightPanel.IsVisible
                    && window.ActivityCard.ActualWidth > 500 && window.ActivityList.ActualHeight > 250,
                    "Activity navigation switches to a dedicated expanded file-history screen");
                Check(window.IsRealtimeMonitoring && window.FileCount.Text == "3"
                    && window.Activities.Where(row => row.IsFile).Select(row => row.Title).SequenceEqual(recordedFileNames),
                    "Changing screens preserves live monitoring, file totals and the existing history");
                SavePreview(window, Path.Combine(parent, "ui-preview-history.png"));
                window.MonitorNavButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(100);
                Check(window.CurrentPage == WorkspacePage.Monitor && window.MonitorCard.IsVisible && window.RightPanel.IsVisible
                    && window.RootScroller.VerticalOffset == 0,
                    "Monitoring navigation restores its dashboard and resets the outer scroll position");
                window.RootScroller.ScrollToBottom();
                await Task.Delay(200);
                Check(window.ExitButton.TransformToAncestor(window).Transform(new Point(0, window.ExitButton.ActualHeight)).Y < window.ActualHeight,
                    "Small-screen scrolling reaches exit controls");
                SavePreview(window, Path.Combine(parent, "ui-preview-minimum-bottom.png"));
                window.StopButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => window.StartButton.IsEnabled);
                Check(window.FolderInput.IsEnabled && window.RecursiveSwitch.IsEnabled, "Stop restores editing");
                Check(ReferenceEquals(window.FolderGraphic.Source, NotificationArtwork.Folder),
                    "Stopping monitoring resets the folder graphic");
                var settings = AppSettings.Load(out var error, config);
                Check(error is null && settings.Folder == folder && settings.IncludeSubfolders, "Settings preserve the existing format");
                File.WriteAllText(config, "invalid JSON");
                AppSettings.Load(out error, config);
                Check(error is not null, "Corrupt settings recover with an error");
                window.Close();
                Check(!window.IsVisible, "Close hides to tray");
                Check(popup.IsVisible, "Tray hide guidance is visible while the main window is hidden");
                Check(window.UnreadNotificationCount == 3, "Tray hide guidance does not create an extra unread notification");
                typeof(Window).GetMethod("OnActivated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(window, new object[] { EventArgs.Empty });
                Check(window.UnreadNotificationCount == 3 && window.HasUnreadTrayIcon,
                    "Activation delivered to a hidden tray window cannot acknowledge its unread badge");
                popup.OpenAppButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => !popup.IsVisible);
                Check(window.IsVisible && !popup.IsVisible, "Notification action restores the app and dismisses the popup");
                Check(window.CurrentPage == WorkspacePage.Activity && window.ActivityCard.IsVisible,
                    "The notification's file-list action opens the actual file-history page");
                Check(window.UnreadNotificationCount == 0 && !window.HasUnreadTrayIcon && window.TaskbarItemInfo.Overlay is null,
                    "Opening the app from the notification clears unread indicators");
                var recordedFiles = window.FileCount.Text;
                window.MonitorNavButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.TestNotificationButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.MarkReadButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(window.FileCount.Text == recordedFiles && window.Activities.Any(row => row.IsFile),
                    "Acknowledging notifications preserves file counts and history");
                window.TestNotificationButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.ClearHistoryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(window.UnreadNotificationCount == 0 && !window.HasUnreadTrayIcon && window.TaskbarItemInfo.Overlay is null
                    && window.Activities.Count == 0, "Clearing history also clears unread indicators");
                Check(!window.NotificationInboxButton.IsEnabled && window.UnreadCountBadge.Visibility == Visibility.Collapsed,
                    "Clearing history clears the latest-alert card as well");
                window.ExitApp();
                await Task.Delay(200);
                Check(!popup.IsVisible && !app.Windows.Cast<Window>().Contains(popup), "App exit closes the notification window");
                var background = new MainWindow(config, startup);
                try
                {
                    Check(background.CanStartInBackground, "Saved folder permits background startup");
                    background.StartInBackground();
                    await WaitUntil(() => background.StatusText.Text == "감시 중");
                    Check(!background.IsVisible && background.FileCount.Text == "0", "Login starts monitoring in the tray with a fresh baseline");
                    File.WriteAllText(Path.Combine(folder, "로그인 후 새 파일.txt"), "startup fixture");
                    await WaitUntil(() => background.FileCount.Text == "1");
                    await WaitUntil(() => background.NotificationPopup is { IsVisible: true }
                        && background.NotificationPopup.DisplayedFiles.Any(file => file.Name == "로그인 후 새 파일.txt"));
                    Check(background.Activities.Any(row => row.Title == "로그인 후 새 파일.txt"), "Background login monitor detects new files");
                    Console.WriteLine($"Background notification: mainVisible={background.IsVisible}, popupVisible={background.NotificationPopup?.IsVisible}, popupActive={background.NotificationPopup?.IsActive}, unread={background.UnreadNotificationCount}, message={background.NotificationPopup?.NotificationMessage.Text}");
                    Check(!background.IsVisible && background.NotificationPopup is { IsVisible: true, IsActive: false }
                        && background.NotificationPopup.DisplayedFiles.Any(file => file.Name == "로그인 후 새 파일.txt"),
                        "Tray-only monitoring displays new-file notifications without opening the main window");
                    Check(background.UnreadNotificationCount == 1 && background.HasUnreadTrayIcon,
                        "Tray-only monitoring retains a badge after a new file arrives");
                    await background.StopMonitoringAsync();
                    background.ShowWindow();
                    await Task.Delay(200);
                    Check(background.StatusText.Text == "중지됨", "Showing a stopped startup window does not restart monitoring");
                    Check(background.UnreadNotificationCount == 0 && !background.HasUnreadTrayIcon, "Restoring the main window acknowledges pending notifications");
                }
                finally { background.ExitApp(); await Task.Delay(200); }
                var unavailable = new MainWindow(Path.Combine(root, "unavailable.json"), startup);
                try
                {
                    unavailable.FolderInput.Text = Path.Combine(root, "없는 폴더");
                    var monitor = unavailable.StartMonitoringAsync();
                    await WaitUntil(() => unavailable.NotificationPopup is { IsVisible: true });
                    Check(unavailable.NotificationPopup!.NotificationTitle.Text == "폴더를 확인할 수 없습니다",
                        "Folder-access errors use the visible popup too");
                    unavailable.PrepareForSessionEnd();
                    Check(!unavailable.NotificationPopup.IsVisible, "Session ending immediately dismisses notifications");
                    await unavailable.StopMonitoringAsync();
                    await monitor;
                }
                finally { unavailable.ExitApp(); await Task.Delay(200); }
                var neverShown = new MainWindow(config, startup);
                var hiddenClosed = false;
                neverShown.Closed += (_, _) => hiddenClosed = true;
                neverShown.StartInBackground();
                await WaitUntil(() => neverShown.StatusText.Text == "감시 중");
                neverShown.ExitApp();
                await WaitUntil(() => hiddenClosed);
                Check(!neverShown.IsVisible, "Tray exit closes a startup window that was never shown");
                Console.WriteLine("All WPF UI checks passed.");
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                if (window.IsVisible) window.ExitApp();
                await Task.Delay(200);
                app.Shutdown();
            }
        };
        try
        {
            app.Run(window);
            if (failure is not null) throw new Exception("UI test failed", failure);
        }
        finally
        {
            if (!Path.GetFullPath(root).StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unsafe test cleanup path");
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task TestFilenameLayout(string parent)
    {
        var popup = new NotificationWindow(() => { });
        try
        {
            var longName = "2026년_10월_입고검수_" + new string('가', 190) + "_최종확인.xlsx";
            var files = new[] { longName, "월간 보고서.pdf", "배송 안내.txt", "추가 파일.csv", "README" };
            popup.ShowNotification("새 파일 5개 도착", "", files, @"D:\업무 자료\입고 검수\2026년 10월");
            await Task.Delay(200);
            popup.UpdateLayout();
            Check(popup.DisplayedFiles.Count == 3 && popup.DisplayedFiles[0].Name == longName
                && popup.DisplayedFiles[0].Name.EndsWith(".xlsx"), "Long filenames retain their full text and extension without global message truncation");
            Check(popup.FileList.IsVisible && popup.FileList.Items.Count == 3 && popup.NotificationMessage.Visibility == Visibility.Collapsed,
                "File notifications render distinct visible filename rows");
            Check(popup.OverflowText.Text.Contains("외 2개") && popup.OverflowText.IsVisible,
                "Batches disclose the exact number of files beyond the three preview rows");
            Check(popup.FolderContext.Text.EndsWith("2026년 10월") && popup.FolderContext.ToolTip is string,
                "Notification folder context retains its full path in a tooltip");
            var presenter = (ContentPresenter)popup.FileList.ItemContainerGenerator.ContainerFromIndex(0);
            Check(presenter.ActualHeight <= 150 && presenter.ActualHeight > 40,
                "Long filenames wrap within a bounded row instead of pushing actions off-screen");
            Check(popup.OpenAppButton.TransformToAncestor(popup).Transform(new Point(0, popup.OpenAppButton.ActualHeight)).Y <= popup.ActualHeight
                && popup.Top + popup.ActualHeight <= SystemParameters.WorkArea.Bottom,
                "Long batch notifications keep the open action inside the popup and desktop");
            SavePreview(popup, Path.Combine(parent, "ui-preview-notification-long.png"));
            popup.ShowNotification("새 파일 도착", "", new[] { "입고 검수 완료.xlsx" }, @"D:\업무 자료");
            await Task.Delay(100);
            Check(popup.DisplayedFiles.Count == 1 && popup.OverflowText.Visibility == Visibility.Collapsed,
                "Reusing a batch popup for a single file clears stale rows and overflow");
            SavePreview(popup, Path.Combine(parent, "ui-preview-notification-single.png"));
            popup.ShowNotification("연결 확인", "폴더 연결을 확인해 주세요.");
            Check(popup.FilePanel.Visibility == Visibility.Collapsed && popup.NotificationMessage.IsVisible
                && popup.DisplayedFiles.Count == 0, "Generic guidance replaces file rows without showing stale filenames");
        }
        finally { popup.ClosePermanently(); }
    }

    private static async Task TestArrivalAnimation(string parent)
    {
        var popup = new NotificationWindow(() => { }, TimeSpan.FromSeconds(2), animationsEnabled: true);
        try
        {
            popup.ShowNotification("새 파일 도착", "", new[] { "실시간 입고 자료.xlsx" }, @"D:\업무 자료");
            Check(popup.IsArriving && popup.ArrivalOffset >= 35 && popup.ArrivalSurface.Opacity < .1,
                "A hidden notification starts below its final position and fully faded");
            SavePreview(popup, Path.Combine(parent, "ui-preview-animation-start.png"));
            await Task.Delay(110);
            Check(popup.IsVisible && popup.IsArriving && popup.ArrivalOffset > 0 && popup.ArrivalOffset < 36
                && popup.ArrivalSurface.Opacity > 0 && popup.ArrivalSurface.Opacity < 1,
                "The notification visibly slides upward and fades in during its entrance");
            SavePreview(popup, Path.Combine(parent, "ui-preview-animation-middle.png"));
            await WaitUntil(() => !popup.IsArriving);
            Check(popup.ArrivalOffset == 0 && popup.ArrivalSurface.Opacity == 1 && !popup.IsActive,
                "Entrance settles at full opacity without taking keyboard focus");
            SavePreview(popup, Path.Combine(parent, "ui-preview-animation-finished.png"));
            popup.ShowNotification("추가 파일 도착", "", new[] { "두 번째 파일.pdf" });
            Check(!popup.IsArriving && popup.ArrivalOffset == 0 && popup.ArrivalSurface.Opacity == 1,
                "Updating an already visible popup does not replay the slide or flash");
            popup.Dismiss();
            await WaitUntil(() => !popup.IsVisible);
            popup.ShowNotification("닫기 테스트", "등장 도중 닫기");
            popup.Dismiss();
            await Task.Delay(400);
            Check(!popup.IsVisible && !popup.IsArriving && popup.ArrivalOffset == 0,
                "Dismissing during entrance cancels animation without later resurrecting the popup");
        }
        finally { popup.ClosePermanently(); }
        var reduced = new NotificationWindow(() => { }, animationsEnabled: false);
        try
        {
            reduced.ShowNotification("동작 줄이기", "애니메이션 설정을 따릅니다.");
            Check(!reduced.IsArriving && reduced.ArrivalOffset == 0 && reduced.ArrivalSurface.Opacity == 1,
                "Reduced-motion mode shows the popup immediately without animation");
        }
        finally { reduced.ClosePermanently(); }
        var shortLived = new NotificationWindow(() => { }, TimeSpan.FromMilliseconds(100), animationsEnabled: true);
        try
        {
            shortLived.ShowNotification("표시 시간", "등장이 끝난 뒤부터 표시 시간을 셉니다.");
            await Task.Delay(180);
            Check(shortLived.IsVisible && shortLived.IsArriving, "Auto-dismiss cannot expire before entrance finishes");
            shortLived.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
                { RoutedEvent = UIElement.MouseEnterEvent });
            await Task.Delay(400);
            Check(shortLived.IsVisible && !shortLived.IsArriving, "Hover during entrance keeps the completed notification visible");
            shortLived.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
                { RoutedEvent = UIElement.MouseLeaveEvent });
            await WaitUntil(() => !shortLived.IsVisible);
            Check(!shortLived.IsVisible, "Leaving the completed entrance starts the normal display lifetime");
        }
        finally { shortLived.ClosePermanently(); }
    }

    private static async Task TestDepartureAnimation(string parent)
    {
        var popup = new NotificationWindow(() => { }, TimeSpan.FromSeconds(2), animationsEnabled: true);
        try
        {
            popup.ShowNotification("퇴장 테스트", "", new[] { "입고 검수 완료.xlsx" });
            await WaitUntil(() => !popup.IsArriving);
            popup.DismissButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(popup.IsVisible && popup.IsDeparting, "Close starts a departure instead of hiding the popup immediately");
            popup.Dismiss();
            await Task.Delay(110);
            Check(popup.IsVisible && popup.IsDeparting && popup.ArrivalOffset > 0 && popup.ArrivalOffset < 36
                && popup.ArrivalSurface.Opacity > 0 && popup.ArrivalSurface.Opacity < 1,
                "The departing popup moves downward while fading out");
            SavePreview(popup, Path.Combine(parent, "ui-preview-departure-middle.png"));
            await WaitUntil(() => !popup.IsVisible);
            Check(!popup.IsDeparting && popup.ArrivalOffset == 0 && popup.ArrivalSurface.Opacity == 1,
                "Completed departure hides the window and resets motion for its next use");
            popup.ShowNotification("이전 알림", "퇴장 중 새 파일");
            await WaitUntil(() => !popup.IsArriving);
            popup.Dismiss();
            await Task.Delay(80);
            var previousOpacity = popup.ArrivalSurface.Opacity;
            popup.ShowNotification("새 파일 도착", "", new[] { "늦게 도착한 파일.pdf" });
            Check(popup.IsVisible && popup.IsArriving && !popup.IsDeparting
                && Math.Abs(popup.ArrivalSurface.Opacity - previousOpacity) < .05,
                "A new notification reverses the in-progress exit without flashing or losing the new content");
            await Task.Delay(250);
            Check(popup.IsVisible && popup.NotificationTitle.Text == "새 파일 도착",
                "An old departure callback cannot hide the replacement notification");
            await WaitUntil(() => !popup.IsArriving);
            popup.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(popup)!, 0, System.Windows.Input.Key.Escape) { RoutedEvent = UIElement.KeyDownEvent });
            Check(popup.IsDeparting && popup.IsVisible, "Escape uses the same smooth departure");
            await WaitUntil(() => !popup.IsVisible);
            popup.ShowNotification("즉시 정리", "앱 종료");
            popup.Dismiss();
            popup.ClosePermanently();
            await Task.Delay(400);
            Check(!popup.IsVisible && !Application.Current.Windows.Cast<Window>().Contains(popup),
                "Permanent app shutdown cancels pending motion and closes the notification immediately");
        }
        finally { if (Application.Current.Windows.Cast<Window>().Contains(popup)) popup.ClosePermanently(); }
        var reduced = new NotificationWindow(() => { }, animationsEnabled: false);
        try
        {
            reduced.ShowNotification("동작 줄이기", "즉시 닫기");
            reduced.Dismiss();
            Check(!reduced.IsVisible && !reduced.IsDeparting, "Reduced-motion mode also skips the exit animation");
        }
        finally { reduced.ClosePermanently(); }
    }

    private static async Task TestNotificationDuration(string root)
    {
        var config = Path.Combine(root, "duration-settings.json");
        File.WriteAllText(config, "{\"Folder\":\"\",\"StartOnLaunch\":false}");
        Check(AppSettings.Load(out _, config).NotificationDurationSeconds == 5,
            "Existing settings without a duration use the five-second default");
        File.WriteAllText(config, "{\"NotificationDurationSeconds\":99}");
        Check(AppSettings.Load(out _, config).NotificationDurationSeconds == 5,
            "An out-of-range saved duration is limited to five seconds");
        File.WriteAllText(config, "{\"NotificationDurationSeconds\":0}");
        Check(AppSettings.Load(out _, config).NotificationDurationSeconds == 1,
            "A nonpositive saved duration is limited to one second");
        var durationWindow = new MainWindow(config, new TestStartupRegistration());
        try
        {
            Check(durationWindow.NotificationDurationSelector.Items.Cast<int>().SequenceEqual(Enumerable.Range(1, 5))
                && (int)durationWindow.NotificationDurationSelector.SelectedItem == 1,
                "The notification duration selector offers exactly one through five seconds and loads the saved value");
            durationWindow.NotificationDurationSelector.SelectedItem = 3;
            Check(AppSettings.Load(out _, config).NotificationDurationSeconds == 3,
                "Changing the duration saves immediately without stopping monitoring or closing the app");
            var restored = new MainWindow(config, new TestStartupRegistration());
            try
            {
                Check((int)restored.NotificationDurationSelector.SelectedItem == 3,
                    "Reopening the app restores its chosen notification duration");
            }
            finally { restored.ExitApp(); }
            durationWindow.NotificationDurationSelector.SelectedItem = 1;
            durationWindow.TestNotificationButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var popup = durationWindow.NotificationPopup!;
            popup.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
                { RoutedEvent = UIElement.MouseLeaveEvent });
            await WaitUntil(() => !popup.IsArriving);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            await WaitUntil(() => !popup.IsVisible);
            Check(popup.DisplayDuration == TimeSpan.FromSeconds(1) && timer.Elapsed.TotalSeconds >= .9
                && timer.Elapsed.TotalSeconds < 2.5,
                "The actual notification uses the selected one-second lifetime and completes its smooth departure");
            durationWindow.NotificationDurationSelector.SelectedItem = 5;
            durationWindow.TestNotificationButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            popup.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
                { RoutedEvent = UIElement.MouseLeaveEvent });
            await WaitUntil(() => !popup.IsArriving);
            await Task.Delay(1400);
            Check(popup.IsVisible && !popup.IsDeparting && popup.DisplayDuration == TimeSpan.FromSeconds(5),
                "The reused notification applies five seconds instead of retaining its previous one-second lifetime");
            durationWindow.NotificationDurationSelector.SelectedItem = 1;
            Check(popup.IsVisible && !popup.IsArriving && popup.DisplayDuration == TimeSpan.FromSeconds(1),
                "Changing duration while a popup is visible resets its lifetime without replaying its entrance");
            await WaitUntil(() => !popup.IsVisible);
        }
        finally { durationWindow.ExitApp(); }
        var invalidConfig = Directory.CreateDirectory(Path.Combine(root, "settings-is-a-directory")).FullName;
        var failed = new MainWindow(invalidConfig, new TestStartupRegistration());
        try
        {
            failed.NotificationDurationSelector.SelectedItem = 2;
            Check((int)failed.NotificationDurationSelector.SelectedItem == 5
                && failed.Activities.Any(row => row.Title == "알림 시간 저장 실패"),
                "A duration save failure restores the previous selection and explains the failure");
        }
        finally { failed.ExitApp(); }
    }

    private static async Task TestNotificationLifetime()
    {
        var popup = new NotificationWindow(() => { }, TimeSpan.FromMilliseconds(500), animationsEnabled: false);
        try
        {
            popup.ShowNotification("자동 닫힘 테스트", "처음 알림");
            popup.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
                { RoutedEvent = UIElement.MouseLeaveEvent });
            await Task.Delay(300);
            popup.ShowNotification("새 알림", "표시 시간 연장");
            popup.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
                { RoutedEvent = UIElement.MouseLeaveEvent });
            await Task.Delay(300);
            Check(popup.IsVisible && popup.NotificationTitle.Text == "새 알림", "A new notification resets the auto-dismiss timer");
            await WaitUntil(() => !popup.IsVisible);
            Check(!popup.IsVisible, "Notification automatically dismisses after its lifetime");
            popup.ShowNotification("다시 표시", "닫힌 후 재사용");
            Check(popup.IsVisible, "Notification can be shown again after automatic dismissal");
            popup.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(popup)!, 0, System.Windows.Input.Key.Escape) { RoutedEvent = UIElement.KeyDownEvent });
            Check(!popup.IsVisible, "Escape dismisses the notification");
            popup.ShowNotification("창 닫기", "Alt+F4 후 재사용");
            popup.Close();
            Check(!popup.IsVisible, "System close dismisses the popup without destroying it");
            popup.ShowNotification("다음 알림", "다시 표시 가능");
            Check(popup.IsVisible, "Next notification still works after a system close");
            popup.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
                { RoutedEvent = UIElement.MouseEnterEvent });
            await Task.Delay(650);
            Check(popup.IsVisible, "Hover pauses automatic dismissal so notification actions remain usable");
            popup.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
                { RoutedEvent = UIElement.MouseLeaveEvent });
            await WaitUntil(() => !popup.IsVisible);
            Check(!popup.IsVisible, "Leaving the popup resumes automatic dismissal");
        }
        finally { popup.ClosePermanently(); }
    }

    private static void TestIconFrames()
    {
        foreach (var size in new[] { 16, 20, 24, 32, 40, 48 })
        {
            using var normal = AppIcon.Load(size: new System.Drawing.Size(size, size));
            using var unread = AppIcon.Load(unread: true, size: new System.Drawing.Size(size, size));
            Check(normal.Width == size && normal.Height == size && unread.Width == size && unread.Height == size,
                $"Normal and unread icons supply a native {size}px frame");
            using var bitmap = normal.ToBitmap();
            var left = size; var top = size; var right = 0; var bottom = 0;
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                    if (bitmap.GetPixel(x, y).A > 32)
                    { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
            Check(right - left + 1 >= size * .9 && bottom - top + 1 >= size * .84,
                $"Icon fills the {size}px canvas with limited transparent padding");
        }
    }

    private static System.Collections.Generic.IEnumerable<Image> Images(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is Image image) yield return image;
            foreach (var nested in Images(child)) yield return nested;
        }
    }

    private static void SavePreview(Window window, string path)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!condition()) await Task.Delay(100, timeout.Token);
    }

    private static void TestIsolatedRegistry()
    {
        const string testParent = @"Software\FolderNotifier.Tests";
        var testKey = testParent + "\\" + Guid.NewGuid().ToString("N");
        var registration = new StartupRegistration(@"C:\한글 경로\FolderNotifier.exe", testKey);
        try
        {
            registration.SetEnabled(true);
            using (var key = Registry.CurrentUser.OpenSubKey(testKey))
                Check((string?)key?.GetValue(StartupRegistration.ValueName) == "\"C:\\한글 경로\\FolderNotifier.exe\" --startup",
                    "Real isolated registry entry quotes Korean and spaced executable paths");
            Check(registration.IsEnabled, "Registered startup state reads back");
            registration.SetEnabled(false);
            Check(!registration.IsEnabled, "Real isolated registry entry is removed");
            registration.SetEnabled(false);
            Check(!registration.IsEnabled, "Repeated startup removal is safe");
        }
        finally
        {
            if (!testKey.StartsWith(testParent + "\\", StringComparison.Ordinal)) throw new InvalidOperationException("Invalid registry cleanup scope");
            Registry.CurrentUser.DeleteSubKeyTree(testKey, throwOnMissingSubKey: false);
        }
    }

    private sealed class TestStartupRegistration : IStartupRegistration
    {
        public bool IsEnabled { get; private set; }
        public bool FailWrites { get; set; }
        public void SetEnabled(bool enabled)
        {
            if (FailWrites) throw new UnauthorizedAccessException("Test: startup registration denied");
            IsEnabled = enabled;
        }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }

    [DllImport("shell32.dll")]
    private static extern int GetCurrentProcessExplicitAppUserModelID(out IntPtr appId);
}
