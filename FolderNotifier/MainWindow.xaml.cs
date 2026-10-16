using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FolderNotifier.Core;
using Forms = System.Windows.Forms;

namespace FolderNotifier;

internal sealed record ActivityEntry(string Title, string Detail, bool IsFile, DateTime Timestamp)
{
    public string Time => Timestamp.ToString("HH:mm:ss");
    public string Glyph => IsFile ? "\uE8A5" : "\uE946";
    public string IconBackground => IsFile ? "#EAF0FF" : "#F1F3F7";
    public string IconForeground => IsFile ? "#2563EB" : "#78849A";
}

internal partial class MainWindow : Window
{
    private readonly Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon appIcon;
    private readonly System.Drawing.Icon trayIcon;
    private readonly Forms.ToolStripMenuItem trayStart = new("감시 시작");
    private readonly Forms.ToolStripMenuItem trayStop = new("감시 중지") { Enabled = false };
    private readonly Forms.ContextMenuStrip trayMenu = new();
    private readonly AppSettings settings;
    private readonly IStartupRegistration startupRegistration;
    private CancellationTokenSource? cancellation;
    private Task? monitoringTask;
    private string activeFolder = "";
    private int totalAdded;
    private bool exiting;
    private bool closing;
    private bool sessionEnding;
    private bool hideHintShown;
    private bool firstLoad = true;
    private bool changingStartup;

    internal ObservableCollection<ActivityEntry> Activities { get; } = new();

    public MainWindow(string? settingsPath = null, IStartupRegistration? startupRegistration = null)
    {
        InitializeComponent();
        var workArea = SystemParameters.WorkArea;
        MinWidth = Math.Min(900, Math.Max(600, workArea.Width - 32));
        MinHeight = Math.Min(600, Math.Max(400, workArea.Height - 32));
        Width = Math.Min(1100, workArea.Width - 32);
        Height = Math.Min(800, workArea.Height - 32);
        appIcon = AppIcon.Load();
        trayIcon = new System.Drawing.Icon(appIcon, Forms.SystemInformation.SmallIconSize);
        Icon = Imaging.CreateBitmapSourceFromHIcon(appIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        BrandIcon.Source = new BitmapImage(new Uri("pack://application:,,,/FolderNotifier;component/Assets/folder-notifier.png"));
        ActivityList.ItemsSource = Activities;
        trayMenu.Items.Add("창 열기", null, (_, _) => OnUi(ShowWindow));
        trayMenu.Items.Add(trayStart);
        trayMenu.Items.Add(trayStop);
        trayMenu.Items.Add("감시 폴더 열기", null, (_, _) => OnUi(OpenFolder));
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        trayMenu.Items.Add("종료", null, (_, _) => OnUi(ExitApp));
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "폴더 알림 · 대기 중", Visible = true, ContextMenuStrip = trayMenu };
        tray.DoubleClick += (_, _) => OnUi(ShowWindow);
        tray.BalloonTipClicked += (_, _) => OnUi(ShowWindow);
        trayStart.Click += (_, _) => OnUi(async () => await StartMonitoringAsync());
        trayStop.Click += (_, _) => OnUi(async () => await StopMonitoringAsync());
        settings = AppSettings.Load(out var loadError, settingsPath);
        FolderInput.Text = settings.Folder;
        RecursiveSwitch.IsChecked = settings.IncludeSubfolders;
        AutoStartSwitch.IsChecked = settings.StartOnLaunch;
        if (loadError is not null) AddHistory("설정 오류", loadError);
        this.startupRegistration = startupRegistration ?? new StartupRegistration();
        try { LoginStartupSwitch.IsChecked = this.startupRegistration.IsEnabled; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            LoginStartupSwitch.IsEnabled = false;
            StartupHint.Text = "자동 실행 설정 접근 오류";
            AddHistory("자동 실행 확인 실패", ex.Message);
        }
        LoginStartupSwitch.Checked += LoginStartupChanged;
        LoginStartupSwitch.Unchecked += LoginStartupChanged;
        Loaded += async (_, _) =>
        {
            if (!firstLoad) return;
            firstLoad = false;
            if (settings.StartOnLaunch && !string.IsNullOrWhiteSpace(settings.Folder)) await StartMonitoringAsync();
        };
        SourceInitialized += (_, _) => ApplyWindowCorners();
        Closing += OnClosing;
        Closed += (_, _) => { tray.Visible = false; tray.Dispose(); trayMenu.Dispose(); trayIcon.Dispose(); appIcon.Dispose(); };
    }

    internal bool CanStartInBackground => !string.IsNullOrWhiteSpace(settings.Folder);

    internal void StartInBackground()
    {
        firstLoad = false;
        _ = Dispatcher.BeginInvoke(async () => await StartMonitoringAsync());
    }

    private void LoginStartupChanged(object sender, RoutedEventArgs e)
    {
        if (changingStartup) return;
        var enabled = LoginStartupSwitch.IsChecked == true;
        try
        {
            // Save the chosen folder before registering login startup, even if
            // the user has not pressed Start or closed this window yet.
            if (enabled)
            {
                var folder = FolderInput.Text.Trim();
                if (!string.IsNullOrWhiteSpace(folder)) folder = Path.GetFullPath(folder);
                settings.Folder = folder;
                settings.IncludeSubfolders = RecursiveSwitch.IsChecked == true;
                settings.StartOnLaunch = AutoStartSwitch.IsChecked == true;
                settings.Save();
            }
            startupRegistration.SetEnabled(enabled);
            StartupHint.Text = "로그인 후 트레이에서 감시";
            AddHistory(enabled ? "Windows 자동 실행 켜짐" : "Windows 자동 실행 꺼짐",
                enabled ? "다음 로그인부터 저장된 폴더 감시를 시작합니다." : "Windows 로그인 자동 실행 등록을 해제했습니다.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            changingStartup = true;
            try { LoginStartupSwitch.IsChecked = !enabled; }
            finally { changingStartup = false; }
            StartupHint.Text = "설정 실패 · 최근 활동 확인";
            AddHistory("자동 실행 설정 실패", ex.Message);
        }
    }

    private void OnUi(Action action)
    {
        if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(action);
    }

    private void ChooseFolder(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFolderDialog { Title = "새 파일을 감시할 폴더를 선택하세요", Multiselect = false };
        if (Directory.Exists(FolderInput.Text)) picker.InitialDirectory = FolderInput.Text;
        if (picker.ShowDialog(this) == true) FolderInput.Text = picker.FolderName;
    }

    private async void StartClicked(object sender, RoutedEventArgs e) => await StartMonitoringAsync();
    private async void StopClicked(object sender, RoutedEventArgs e) => await StopMonitoringAsync();
    private void OpenFolderClicked(object sender, RoutedEventArgs e) => OpenFolder();
    private void HideClicked(object sender, RoutedEventArgs e) => HideToTray();
    private void ExitClicked(object sender, RoutedEventArgs e) => ExitApp();
    private void TestNotificationClicked(object sender, RoutedEventArgs e) =>
        tray.ShowBalloonTip(5000, "폴더 알림 테스트", "새 파일이 추가되면 이런 알림이 표시됩니다.", Forms.ToolTipIcon.Info);
    private void ClearClicked(object sender, RoutedEventArgs e)
    {
        Activities.Clear(); totalAdded = 0; FileCount.Text = "0"; EmptyState.Visibility = Visibility.Visible;
    }

    internal async Task StartMonitoringAsync()
    {
        if (cancellation is not null || closing) return;
        string folder;
        try { folder = Path.GetFullPath(FolderInput.Text.Trim()); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            MessageBox.Show(this, "올바른 폴더 경로를 입력해 주세요.", "폴더 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        activeFolder = folder; FolderInput.Text = folder;
        settings.Folder = folder;
        settings.IncludeSubfolders = RecursiveSwitch.IsChecked == true;
        settings.StartOnLaunch = AutoStartSwitch.IsChecked == true;
        SaveSettings();
        cancellation = new CancellationTokenSource();
        SetRunning(true);
        SetStatus("준비 중", "기존 파일을 확인하고 있습니다…", "#EAF0FF", "#2563EB");
        var monitor = new FolderMonitor(folder, settings.IncludeSubfolders);
        monitoringTask = MonitorLoopAsync(monitor, cancellation.Token);
        await monitoringTask;
    }

    private async Task MonitorLoopAsync(FolderMonitor monitor, CancellationToken token)
    {
        var baselineReady = false;
        string? lastError = null;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var result = await Task.Run(() => monitor.Scan(token), token).WaitAsync(token);
                token.ThrowIfCancellationRequested();
                if (result.Error is not null)
                {
                    SetStatus("연결 확인", "폴더 연결 또는 접근 권한을 확인해 주세요. 자동으로 재시도합니다.", "#FFF4E8", "#B56712");
                    tray.Text = "폴더 알림 · 연결 / 접근 오류";
                    if (result.Error != lastError)
                    {
                        AddHistory("감시 오류", result.Error);
                        tray.ShowBalloonTip(5000, "폴더를 확인할 수 없습니다", "경로, 연결 상태와 폴더 접근 권한을 확인해 주세요. 자동으로 재시도합니다.", Forms.ToolTipIcon.Warning);
                    }
                    lastError = result.Error;
                }
                else
                {
                    if (lastError is not null) AddHistory("연결 복구", activeFolder);
                    lastError = null;
                    if (!baselineReady) { baselineReady = true; AddHistory("감시 시작", activeFolder); }
                    SetStatus("감시 중", $"2초마다 확인 · 마지막 확인 {DateTime.Now:HH:mm:ss}", "#E9F6EE", "#20844D");
                    tray.Text = "폴더 알림 · 감시 중";
                    if (result.AddedFiles.Count > 0) NotifyAdded(result.AddedFiles);
                }
                await Task.Delay(TimeSpan.FromSeconds(2), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            AddHistory("감시 중단", ex.Message);
            SetStatus("중단됨", "감시가 중단되었습니다. 이력을 확인하고 다시 시작해 주세요.", "#FDECEE", "#B42337");
            tray.Text = "폴더 알림 · 감시 중단";
            tray.ShowBalloonTip(5000, "폴더 감시 중단", ex.Message, Forms.ToolTipIcon.Error);
        }
        finally
        {
            cancellation?.Dispose(); cancellation = null;
            SetRunning(false);
        }
    }

    internal async Task StopMonitoringAsync()
    {
        if (cancellation is null) return;
        StopButton.IsEnabled = trayStop.Enabled = false;
        StatusDetail.Text = "감시를 중지하고 있습니다…";
        cancellation.Cancel();
        if (monitoringTask is not null) await monitoringTask;
        SetStatus("중지됨", "다시 시작하면 현재 파일을 기준으로 감시합니다.", "#F0F2F6", "#667085");
        tray.Text = "폴더 알림 · 중지됨";
        AddHistory("감시 중지", activeFolder);
    }

    private void SetStatus(string text, string detail, string background, string foreground)
    {
        StatusText.Text = text; StatusDetail.Text = detail;
        StatusBadge.Background = (Brush)new BrushConverter().ConvertFromString(background)!;
        var color = (Brush)new BrushConverter().ConvertFromString(foreground)!;
        StatusDot.Fill = StatusText.Foreground = color;
    }

    private void NotifyAdded(IReadOnlyList<string> files)
    {
        totalAdded += files.Count; FileCount.Text = totalAdded.ToString("N0");
        foreach (var path in files.TakeLast(500)) AddHistory(Path.GetFileName(path), path, true);
        var title = files.Count == 1 ? "새 파일이 추가되었습니다" : $"새 파일 {files.Count:N0}개가 추가되었습니다";
        var message = string.Join("\n", files.Take(3).Select(Path.GetFileName));
        if (files.Count > 3) message += $"\n외 {files.Count - 3:N0}개 · 앱에서 확인";
        if (message.Length > 240) message = message[..237] + "…";
        tray.ShowBalloonTip(5000, title, message, Forms.ToolTipIcon.Info);
    }

    private void AddHistory(string title, string detail, bool isFile = false)
    {
        Activities.Insert(0, new(title, detail, isFile, DateTime.Now));
        while (Activities.Count > 500) Activities.RemoveAt(Activities.Count - 1);
        EmptyState.Visibility = Visibility.Collapsed;
    }
    private void SetRunning(bool running)
    {
        FolderInput.IsEnabled = BrowseButton.IsEnabled = RecursiveSwitch.IsEnabled = AutoStartSwitch.IsEnabled = !running;
        StartButton.IsEnabled = trayStart.Enabled = !running && !closing;
        StopButton.IsEnabled = trayStop.Enabled = running && !closing;
    }
    private void SaveSettings()
    {
        try { settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AddHistory("설정 저장 실패", ex.Message); }
    }
    private void OpenFolder()
    {
        var path = cancellation is not null ? activeFolder : FolderInput.Text.Trim();
        try
        {
            if (!Directory.Exists(path)) throw new IOException("폴더에 접근할 수 없습니다.");
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true, Verb = "open" });
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "폴더 열기 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    internal void ShowWindow() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void HideToTray()
    {
        Hide();
        if (hideHintShown) return;
        hideHintShown = true;
        tray.ShowBalloonTip(3000, "폴더 알림이 실행 중입니다", "트레이 아이콘을 두 번 클릭하면 창이 열립니다. 종료는 트레이 메뉴에서 선택하세요.", Forms.ToolTipIcon.Info);
    }
    internal void ExitApp() { exiting = true; Close(); }
    internal void PrepareForSessionEnd() { sessionEnding = exiting = true; cancellation?.Cancel(); tray.Visible = false; }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (sessionEnding) return;
        if (closing) { e.Cancel = true; return; }
        if (!exiting) { e.Cancel = true; HideToTray(); return; }
        e.Cancel = true; closing = true; IsEnabled = false;
        await StopMonitoringAsync();
        settings.Folder = FolderInput.Text.Trim();
        settings.IncludeSubfolders = RecursiveSwitch.IsChecked == true;
        settings.StartOnLaunch = AutoStartSwitch.IsChecked == true;
        SaveSettings();
        Closing -= OnClosing;
        _ = Dispatcher.BeginInvoke(Close);
    }
    private void ApplyWindowCorners()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        var corner = 2;
        DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 33, ref corner, sizeof(int));
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
