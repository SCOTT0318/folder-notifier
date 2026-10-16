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
                window.FolderInput.Focus();
                window.TestNotificationButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(200);
                var popup = window.NotificationPopup!;
                Check(popup is { IsVisible: true } && popup.NotificationTitle.Text == "폴더 알림 테스트"
                    && popup.NotificationMessage.Text.Contains("새 파일"), "Test button displays the actual notification window and message");
                Check(popup.Topmost && !popup.ShowInTaskbar && !popup.ShowActivated && !popup.IsActive,
                    "Notification stays above other windows without stealing focus or adding a taskbar button");
                var workArea = SystemParameters.WorkArea;
                Check(popup.Left >= workArea.Left && popup.Top >= workArea.Top
                    && popup.Left + popup.ActualWidth <= workArea.Right && popup.Top + popup.ActualHeight <= workArea.Bottom,
                    "Notification is fully inside the desktop work area above the taskbar");
                Check(window.Activities.Any(row => row.Title == "알림 테스트") && window.FileCount.Text == "0",
                    "Test is recorded in history without incrementing the new-file count");
                Check(window.UnreadNotificationCount == 1 && window.HasUnreadTrayIcon && window.TaskbarItemInfo.Overlay is not null
                    && window.TrayTooltip.Contains("미확인 1개") && window.MarkReadButton.Visibility == Visibility.Visible,
                    "Test notification sets the tray badge, taskbar overlay, unread count and tooltip");
                SavePreview(popup, Path.Combine(parent, "ui-preview-notification.png"));
                window.TestNotificationButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(ReferenceEquals(popup, window.NotificationPopup), "Repeated test reuses one popup");
                popup.DismissButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!popup.IsVisible, "Notification close button dismisses the popup");
                Check(window.UnreadNotificationCount == 2 && window.HasUnreadTrayIcon && window.TaskbarItemInfo.Overlay is not null,
                    "Dismissing a popup preserves the unread badge and accumulates notifications");
                await TestNotificationLifetime();
                window.MarkReadButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(window.UnreadNotificationCount == 0 && !window.HasUnreadTrayIcon && window.TaskbarItemInfo.Overlay is null
                    && window.MarkReadButton.Visibility == Visibility.Collapsed && window.FileCount.Text == "0",
                    "Mark-read clears both badges without changing detected-file totals");
                TestIconFrames();
                window.FolderInput.Text = folder;
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
                Check(!window.FolderInput.IsEnabled && !window.RecursiveSwitch.IsEnabled && !window.AutoStartSwitch.IsEnabled,
                    "Monitoring locks the folder and options");
                Check(window.StopButton.IsEnabled, "Stop enabled while monitoring");
                Check(window.LoginStartupSwitch.IsEnabled, "Login startup remains editable while monitoring");
                var child = Directory.CreateDirectory(Path.Combine(folder, "하위")).FullName;
                File.WriteAllText(Path.Combine(child, "테스트 파일.txt"), "UI integration");
                await WaitUntil(() => window.FileCount.Text == "1");
                Check(window.Activities.Any(row => row.Title == "테스트 파일.txt" && row.IsFile), "Async scan updates activity and count");
                Check(window.EmptyState.Visibility == Visibility.Collapsed, "Activity replaces the empty state");
                Check(popup.IsVisible && popup.NotificationTitle.Text == "새 파일이 추가되었습니다"
                    && popup.NotificationMessage.Text.Contains("테스트 파일.txt"), "New-file detection uses the same visible notification path");
                Check(window.UnreadNotificationCount == 1 && window.HasUnreadTrayIcon, "Detected files mark the app as unread");
                File.WriteAllText(Path.Combine(folder, "업무자료.pdf"), "test fixture only");
                File.WriteAllText(Path.Combine(folder, "입고 목록.xlsx"), "test fixture only");
                await WaitUntil(() => window.FileCount.Text == "3");
                Check(popup.NotificationTitle.Text == "새 파일 2개가 추가되었습니다"
                    && popup.NotificationMessage.Text.Contains("업무자료.pdf") && popup.NotificationMessage.Text.Contains("입고 목록.xlsx"),
                    "Multiple detected files appear together in one popup");
                Check(window.UnreadNotificationCount == 3 && window.TrayTooltip.Contains("미확인 3개"),
                    "Batch notifications count every file and retain the unread tooltip across scans");
                popup.Dismiss();
                await Task.Delay(2200);
                Check(window.UnreadNotificationCount == 3 && window.HasUnreadTrayIcon && window.TaskbarItemInfo.Overlay is not null
                    && window.TrayTooltip.Contains("미확인 3개"), "Periodic monitoring and popup expiry preserve the unread badge");
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
                window.RootScroller.ScrollToBottom();
                await Task.Delay(200);
                Check(window.ExitButton.TransformToAncestor(window).Transform(new Point(0, window.ExitButton.ActualHeight)).Y < window.ActualHeight,
                    "Small-screen scrolling reaches exit controls");
                SavePreview(window, Path.Combine(parent, "ui-preview-minimum-bottom.png"));
                window.StopButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => window.StartButton.IsEnabled);
                Check(window.FolderInput.IsEnabled && window.RecursiveSwitch.IsEnabled, "Stop restores editing");
                var settings = AppSettings.Load(out var error, config);
                Check(error is null && settings.Folder == folder && settings.IncludeSubfolders, "Settings preserve the existing format");
                File.WriteAllText(config, "invalid JSON");
                AppSettings.Load(out error, config);
                Check(error is not null, "Corrupt settings recover with an error");
                window.Close();
                Check(!window.IsVisible, "Close hides to tray");
                Check(popup.IsVisible, "Tray hide guidance is visible while the main window is hidden");
                Check(window.UnreadNotificationCount == 3, "Tray hide guidance does not create an extra unread notification");
                popup.OpenAppButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(window.IsVisible && !popup.IsVisible, "Notification action restores the app and dismisses the popup");
                Check(window.UnreadNotificationCount == 0 && !window.HasUnreadTrayIcon && window.TaskbarItemInfo.Overlay is null,
                    "Opening the app from the notification clears unread indicators");
                var recordedFiles = window.FileCount.Text;
                window.TestNotificationButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.MarkReadButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(window.FileCount.Text == recordedFiles && window.Activities.Any(row => row.IsFile),
                    "Acknowledging notifications preserves file counts and history");
                window.TestNotificationButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.ClearHistoryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(window.UnreadNotificationCount == 0 && !window.HasUnreadTrayIcon && window.TaskbarItemInfo.Overlay is null
                    && window.Activities.Count == 0, "Clearing history also clears unread indicators");
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
                        && background.NotificationPopup.NotificationMessage.Text.Contains("로그인 후 새 파일.txt"));
                    Check(background.Activities.Any(row => row.Title == "로그인 후 새 파일.txt"), "Background login monitor detects new files");
                    Console.WriteLine($"Background notification: mainVisible={background.IsVisible}, popupVisible={background.NotificationPopup?.IsVisible}, popupActive={background.NotificationPopup?.IsActive}, unread={background.UnreadNotificationCount}, message={background.NotificationPopup?.NotificationMessage.Text}");
                    Check(!background.IsVisible && background.NotificationPopup is { IsVisible: true, IsActive: false }
                        && background.NotificationPopup.NotificationMessage.Text.Contains("로그인 후 새 파일.txt"),
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

    private static async Task TestNotificationLifetime()
    {
        var popup = new NotificationWindow(() => { }, TimeSpan.FromMilliseconds(500));
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
