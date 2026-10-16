using System;
using System.IO;
using System.Linq;
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
                File.WriteAllText(Path.Combine(folder, "업무자료.pdf"), "test fixture only");
                File.WriteAllText(Path.Combine(folder, "입고 목록.xlsx"), "test fixture only");
                await WaitUntil(() => window.FileCount.Text == "3");
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
                window.ShowWindow();
                Check(window.IsVisible, "Tray restore shows the window");
                window.ExitApp();
                await Task.Delay(200);
                var background = new MainWindow(config, startup);
                try
                {
                    Check(background.CanStartInBackground, "Saved folder permits background startup");
                    background.StartInBackground();
                    await WaitUntil(() => background.StatusText.Text == "감시 중");
                    Check(!background.IsVisible && background.FileCount.Text == "0", "Login starts monitoring in the tray with a fresh baseline");
                    File.WriteAllText(Path.Combine(folder, "로그인 후 새 파일.txt"), "startup fixture");
                    await WaitUntil(() => background.FileCount.Text == "1");
                    Check(background.Activities.Any(row => row.Title == "로그인 후 새 파일.txt"), "Background login monitor detects new files");
                    await background.StopMonitoringAsync();
                    background.ShowWindow();
                    await Task.Delay(200);
                    Check(background.StatusText.Text == "중지됨", "Showing a stopped startup window does not restart monitoring");
                }
                finally { background.ExitApp(); await Task.Delay(200); }
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

    private static void SavePreview(MainWindow window, string path)
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
}
