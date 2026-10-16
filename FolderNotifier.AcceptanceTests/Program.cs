using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

// Run the exact supplied publish/installed assembly with isolated settings and actual filesystem scans.
// Do not reference the application project: that would test a freshly built copy.
internal static class Program
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly List<string> passed = new();

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Usage: <FolderNotifier.dll path> <evidence directory>");
        var assemblyPath = Path.GetFullPath(args[0]);
        var evidence = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(evidence);
        var fixture = Directory.CreateDirectory(Path.Combine(evidence, "fixture-" + Guid.NewGuid().ToString("N"))).FullName;
        var folder = Directory.CreateDirectory(Path.Combine(fixture, "watched")).FullName;
        File.WriteAllText(Path.Combine(folder, "baseline.txt"), "Existing files must not alert.");
        var config = Path.Combine(fixture, "settings.json");
        File.WriteAllText(config, JsonSerializer.Serialize(new { Folder = folder, IncludeSubfolders = true, StartOnLaunch = false }));
        var assemblyDirectory = Path.GetDirectoryName(assemblyPath)!;
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            var path = Path.Combine(assemblyDirectory, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        var assemblyHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyPath)));
        var corePath = Path.Combine(assemblyDirectory, "FolderNotifier.Core.dll");
        var coreHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(corePath)));
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
        var app = (Application)Activator.CreateInstance(assembly.GetType("FolderNotifier.App", true)!, nonPublic: true)!;
        Invoke(app, "InitializeComponent");
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var startup = Activator.CreateInstance(assembly.GetType("FolderNotifier.StartupRegistration", true)!,
            new object?[] { Path.ChangeExtension(assemblyPath, ".exe"), @"Software\FolderNotifier.AcceptanceTests\" + Guid.NewGuid().ToString("N") });
        var window = (Window)Activator.CreateInstance(assembly.GetType("FolderNotifier.MainWindow", true)!, new[] { config, startup })!;
        window.Title = "폴더 알림 · 실시간 트레이 검증";
        Exception? failure = null;
        window.Loaded += async (_, _) =>
        {
            Task? monitoring = null;
            try
            {
                monitoring = (Task)Invoke(window, "StartMonitoringAsync")!;
                await WaitUntil(() => Control<TextBlock>(window, "StatusText").Text == "감시 중");
                Check(Control<TextBlock>(window, "FileCount").Text == "0" && Count(window) == 0, "Baseline files do not create unread notifications");
                Check((bool)Get(window, "IsRealtimeMonitoring")!, "The supplied assembly arms realtime filesystem events");
                Check(Control<TextBlock>(window, "AppVersionText").Text == "v" + assembly.GetName().Version!.ToString(3),
                    "The installed screen identifies its exact UI version");
                Click(window, "HideButton");
                await WaitUntil(() => !window.IsVisible);
                var mainHandle = new WindowInteropHelper(window).Handle;
                Check(!IsWindowVisible(mainHandle), "The actual Windows main window is hidden to the tray");
                var hint = Get(window, "NotificationPopup");
                if (hint is not null) Invoke(hint, "DismissImmediately");
                Check(Count(window) == 0, "Tray hide guidance does not set an unread badge");

                var detectionTimer = Stopwatch.StartNew();
                File.WriteAllText(Path.Combine(folder, "실제 추가 파일.txt"), "Tray acceptance fixture.");
                await WaitUntil(() => Count(window) == 1 && Popup(window)?.IsVisible == true);
                var detectionMilliseconds = detectionTimer.Elapsed.TotalMilliseconds;
                Check((string?)Get(window, "LastDetectionSource") == "FileSystemEvent" && detectionMilliseconds < 2000,
                    "A filesystem event displays the tray notification before the old two-second polling interval");
                Console.WriteLine($"Tray event-to-popup latency: {detectionMilliseconds:F0} ms");
                var popup = Popup(window)!;
                var entranceObserved = (bool)Get(popup, "IsArriving")!;
                if (SystemParameters.ClientAreaAnimation)
                    Check(entranceObserved && (double)Get(popup, "ArrivalOffset")! > 0,
                        "The installed tray notification begins a real upward entrance animation");
                Check(!window.IsVisible && !IsWindowVisible(mainHandle), "A detected file leaves the main window hidden");
                Check(IsWindowVisible(new WindowInteropHelper(popup).Handle), "A new file creates a visible native Windows notification window");
                Check(!popup.IsActive && popup.Topmost, "The file notification is topmost without taking input focus");
                Check(ContainsFilename(popup, "실제 추가 파일.txt"), "The popup contains the actual added filename");
                Check(Control<Image>(popup, "NotificationGraphic").Source is DrawingImage
                    && Control<Border>(popup, "PopupCountBadge").IsVisible && Control<TextBlock>(popup, "PopupCount").Text == "1",
                    "The installed popup renders vector notification artwork and a red count badge");
                CheckBadge(window, expectedCount: 1);
                await WaitUntil(() => !(bool)Get(popup, "IsArriving")!);
                var settledMilliseconds = detectionTimer.Elapsed.TotalMilliseconds;
                Check((double)Get(popup, "ArrivalOffset")! == 0 && Control<Border>(popup, "ArrivalSurface").Opacity == 1
                    && popup.IsVisible && !popup.IsActive,
                    "The installed notification finishes its slide at full opacity without taking focus");
                SavePreview(popup, Path.Combine(evidence, "file-notification.png"));
                var tray = (Forms.NotifyIcon)Get(window, "tray")!;
                using (var bitmap = tray.Icon!.ToBitmap()) bitmap.Save(Path.Combine(evidence, "selected-unread-tray-icon.png"));

                var iconId = new NotifyIconIdentifier
                {
                    Size = (uint)Marshal.SizeOf<NotifyIconIdentifier>(),
                    Window = ((Forms.NativeWindow)Get(tray, "_window")!).Handle,
                    Id = (uint)Get(tray, "_id")!
                };
                var shellResult = Shell_NotifyIconGetRect(ref iconId, out var shellRect);
                Check(shellResult == 0 && shellRect.Right > shellRect.Left && shellRect.Bottom > shellRect.Top,
                    "Windows Shell reports a registered tray icon rectangle");
                await WaitUntil(() => (bool)Get(popup, "IsDeparting")! || !popup.IsVisible, TimeSpan.FromSeconds(15));
                var departureObserved = (bool)Get(popup, "IsDeparting")!;
                if (SystemParameters.ClientAreaAnimation)
                {
                    Check(departureObserved && IsWindowVisible(new WindowInteropHelper(popup).Handle),
                        "The actual tray popup starts a downward fade while its native window remains visible");
                    await Task.Delay(40);
                    Check((bool)Get(popup, "IsDeparting")! && (double)Get(popup, "ArrivalOffset")! > 0
                        && Control<Border>(popup, "ArrivalSurface").Opacity > 0 && Control<Border>(popup, "ArrivalSurface").Opacity < 1,
                        "The installed tray popup visibly moves down and fades during its exit");
                    SavePreview(popup, Path.Combine(evidence, "departing-notification.png"));
                }
                await WaitUntil(() => !popup.IsVisible);
                Check(!IsWindowVisible(new WindowInteropHelper(popup).Handle), "The native popup automatically hides after its lifetime");
                CheckBadge(window, expectedCount: 1);
                Check(!window.IsVisible, "The app stays in the tray after the popup disappears");

                File.WriteAllText(Path.Combine(folder, "추가 자료 1.txt"), "batch 1");
                File.WriteAllText(Path.Combine(folder, "추가 자료 2.txt"), "batch 2");
                await WaitUntil(() => Count(window) == 3 && popup.IsVisible);
                Check(Control<TextBlock>(window, "FileCount").Text == "3", "Additional real files accumulate while hidden");
                Check((string?)Get(window, "LastDetectionSource") == "FileSystemEvent", "Additional files also use the realtime event path");
                Check(ContainsFilename(popup, "추가 자료 1.txt") && ContainsFilename(popup, "추가 자료 2.txt"),
                    "Both actual batch filenames are displayed in separate notification rows");
                CheckBadge(window, expectedCount: 3);
                Check(Control<TextBlock>(window, "UnreadCountText").Text == "3"
                    && Control<Border>(window, "UnreadCountBadge").Visibility == Visibility.Visible
                    && Control<Image>(window, "InboxGraphic").Source is DrawingImage,
                    "The main notification card retains its graphical unread state while hidden to the tray");
                Click(popup, "OpenAppButton");
                await WaitUntil(() => window.IsVisible && !popup.IsVisible);
                Check(Count(window) == 0 && !(bool)Get(window, "HasUnreadTrayIcon")! && window.TaskbarItemInfo.Overlay is null,
                    "Opening the app from the popup clears the tray and taskbar badges");
                Check(Control<Border>(window, "UnreadCountBadge").Visibility == Visibility.Collapsed,
                    "Acknowledgment also clears the dedicated notification card badge");
                Check(Control<TextBlock>(window, "FileCount").Text == "3", "Acknowledging notifications preserves the detected-file total");
                Check(Get(window, "CurrentPage")!.ToString() == "Activity" && Control<Border>(window, "ActivityCard").IsVisible,
                    "Opening the file list switches the installed app to its file-history screen");
                Click(window, "SettingsNavButton");
                await Task.Delay(100);
                Check(Get(window, "CurrentPage")!.ToString() == "Settings" && Control<Border>(window, "SettingsCard").IsVisible
                    && !Control<Border>(window, "ActivityCard").IsVisible && !Control<Border>(window, "MonitorCard").IsVisible,
                    "The installed sidebar opens a dedicated settings screen");
                SavePreview(window, Path.Combine(evidence, "settings-page.png"));
                Click(window, "MonitorNavButton");
                await Task.Delay(100);
                Check(Get(window, "CurrentPage")!.ToString() == "Monitor" && Control<Border>(window, "MonitorCard").IsVisible,
                    "The installed sidebar returns to the monitoring dashboard");
                Click(window, "ActivityNavButton");
                await Task.Delay(100);
                Check(Get(window, "CurrentPage")!.ToString() == "Activity" && Control<Border>(window, "ActivityCard").IsVisible
                    && !Control<Grid>(window, "RightPanel").IsVisible,
                    "The installed history menu opens the expanded file-history screen");
                SavePreview(window, Path.Combine(evidence, "history-page.png"));
                Check((bool)Get(window, "IsRealtimeMonitoring")! && Control<TextBlock>(window, "FileCount").Text == "3",
                    "Switching every installed sidebar page preserves the active monitor and detected-file total");
                using (var bitmap = tray.Icon!.ToBitmap()) bitmap.Save(Path.Combine(evidence, "selected-read-tray-icon.png"));
                SavePreview(window, Path.Combine(evidence, "acknowledged-window.png"));
                Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyPath))) == assemblyHash
                    && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(corePath))) == coreHash,
                    "The supplied application and core assemblies are unchanged by acceptance tests");
                File.WriteAllText(Path.Combine(evidence, "result.json"), JsonSerializer.Serialize(new
                {
                    success = true, assemblySha256 = assemblyHash, coreAssemblySha256 = coreHash, version = assembly.GetName().Version?.ToString(),
                    passedChecks = passed, shellTrayRectangle = new { shellRect.Left, shellRect.Top, shellRect.Right, shellRect.Bottom },
                    exactInputAssemblies = true, realFilesCreated = 3,
                    detectionMilliseconds, settledMilliseconds, entranceObserved, departureObserved, entranceEnabled = SystemParameters.ClientAreaAnimation,
                    detectionSource = (string?)Get(window, "LastDetectionSource"),
                    nativePopupVisible = true, mainHiddenDuringDetection = true,
                    unreadBadgePersistsAfterPopup = true, acknowledgmentClearsBadge = true,
                    evidenceNote = "Icon PNGs show the icon selected by the running app, not a screenshot of the Windows taskbar.",
                    verifiedAt = DateTimeOffset.Now
                }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"All {passed.Count} application-assembly tray acceptance checks passed.");
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                await (Task)Invoke(window, "StopMonitoringAsync")!;
                if (monitoring is not null) await monitoring;
                Invoke(window, "ExitApp");
                await Task.Delay(200);
                app.Shutdown();
            }
        };
        try
        {
            app.Run(window);
            if (failure is not null) throw new InvalidOperationException("Installed-assembly acceptance failed", failure);
        }
        finally
        {
            var resolved = Path.GetFullPath(fixture);
            if (!resolved.StartsWith(Path.TrimEndingDirectorySeparator(evidence) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe fixture cleanup path");
            Directory.Delete(resolved, recursive: true);
        }
    }

    private static bool ContainsFilename(Window popup, string name)
    {
        var list = Control<ItemsControl>(popup, "FileList");
        foreach (var item in list.Items)
            if ((string?)Get(item, "Name") == name && list.IsVisible) return true;
        return false;
    }

    private static void CheckBadge(Window window, int expectedCount)
    {
        var tray = (Forms.NotifyIcon)Get(window, "tray")!;
        Console.WriteLine($"Tray state: expected={expectedCount}, unread={Count(window)}, unreadIcon={Get(window, "HasUnreadTrayIcon")}, trayVisible={tray.Visible}, mainVisible={window.IsVisible}, mainActive={window.IsActive}, state={window.WindowState}");
        Check(Count(window) == expectedCount && (bool)Get(window, "HasUnreadTrayIcon")! && tray.Visible,
            $"The running tray icon uses its unread badge with count {expectedCount}");
        using var selected = tray.Icon!.ToBitmap();
        var redPixels = 0;
        for (var y = 0; y < selected.Height; y++)
            for (var x = 0; x < selected.Width; x++)
            {
                var color = selected.GetPixel(x, y);
                if (color.A > 128 && color.R > 150 && color.G < 100 && color.B < 100) redPixels++;
            }
        Check(redPixels >= 3 && tray.Text.Contains($"미확인 {expectedCount}개") && window.TaskbarItemInfo.Overlay is not null,
            $"The selected tray icon contains a red badge and the tooltip/taskbar overlay retain {expectedCount}");
    }

    private static int Count(Window window) => (int)Get(window, "UnreadNotificationCount")!;
    private static Window? Popup(Window window) => (Window?)Get(window, "NotificationPopup");
    private static T Control<T>(object target, string name) => (T)Get(target, name)!;
    private static object? Get(object target, string name) => target.GetType().GetProperty(name, Members)?.GetValue(target)
        ?? target.GetType().GetField(name, Members)?.GetValue(target);
    private static object? Invoke(object target, string name) => target.GetType().GetMethod(name, Members)!.Invoke(target, null);
    private static void Click(object target, string name) => Control<Button>(target, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        passed.Add(message);
        Console.WriteLine("PASS " + message);
    }
    private static async Task WaitUntil(Func<bool> condition, TimeSpan? timeout = null)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed > (timeout ?? TimeSpan.FromSeconds(20))) throw new TimeoutException("Expected tray state did not appear");
            await Task.Delay(100);
        }
    }
    private static void SavePreview(Window window, string path)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var image = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        image.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path);
        encoder.Save(file);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier { public uint Size; public IntPtr Window; public uint Id; public Guid Guid; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left; public int Top; public int Right; public int Bottom; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("shell32.dll")]
    private static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out Rect rect);
}
