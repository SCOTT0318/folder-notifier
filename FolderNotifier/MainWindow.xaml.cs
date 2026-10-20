using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Controls;
using FolderNotifier.Core;
using Forms = System.Windows.Forms;

namespace FolderNotifier;

internal enum WorkspacePage { Monitor, Activity, Settings }

internal sealed record ActivityEntry(string Title, string Detail, bool IsFile, DateTime Timestamp)
{
    public string Time => Timestamp.ToString("HH:mm:ss");
    public string Glyph => IsFile ? "\uE8A5" : "\uE946";
    public string IconBackground => IsFile ? "#EAF0FF" : "#F1F3F7";
    public string IconForeground => IsFile ? "#2563EB" : "#78849A";
    public ImageSource Artwork => IsFile ? NotificationArtwork.File(Path.GetExtension(Title).TrimStart('.')) : NotificationArtwork.Bell;
}

internal partial class MainWindow : Window
{
    private readonly Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon appIcon;
    private readonly System.Drawing.Icon trayIcon;
    private readonly System.Drawing.Icon unreadTrayIcon;
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
    private string trayStatus = "대기 중";
    private sealed record NotificationSnapshot(string Title, string Message, IReadOnlyList<string>? Files, string? Folder, bool IsPreview);
    private NotificationSnapshot? latestNotification;

    internal int UnreadNotificationCount { get; private set; }
    internal string TrayTooltip => tray.Text;
    internal bool HasUnreadTrayIcon => ReferenceEquals(tray.Icon, unreadTrayIcon);
    internal bool IsRealtimeMonitoring { get; private set; }
    internal string LastDetectionSource { get; private set; } = "";

    internal NotificationWindow? NotificationPopup { get; private set; }
    internal WorkspacePage CurrentPage { get; private set; } = WorkspacePage.Monitor;

    internal ObservableCollection<ActivityEntry> Activities { get; } = new();

    public MainWindow(string? settingsPath = null, IStartupRegistration? startupRegistration = null,
        AppUpdater? updater = null, Action<string>? installUpdate = null, string? updateDownloadRoot = null)
    {
        InitializeComponent();
        this.updater = updater ?? new AppUpdater();
        this.installUpdate = installUpdate ?? (path => { AppUpdater.LaunchInstaller(path); ExitApp(); });
        this.updateDownloadRoot = updateDownloadRoot;
        NavigateTo(WorkspacePage.Monitor);
        var workArea = SystemParameters.WorkArea;
        MinWidth = Math.Min(900, Math.Max(600, workArea.Width - 32));
        MinHeight = Math.Min(600, Math.Max(400, workArea.Height - 32));
        Width = Math.Min(1100, workArea.Width - 32);
        Height = Math.Min(800, workArea.Height - 32);
        appIcon = AppIcon.Load();
        trayIcon = AppIcon.Load(size: Forms.SystemInformation.SmallIconSize);
        unreadTrayIcon = AppIcon.Load(unread: true, size: Forms.SystemInformation.SmallIconSize);
        Icon = Imaging.CreateBitmapSourceFromHIcon(appIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        BrandIcon.Source = new BitmapImage(new Uri("pack://application:,,,/FolderNotifier;component/Assets/folder-notifier.png"));
        InboxGraphic.Source = NotificationArtwork.Bell;
        FolderGraphic.Source = NotificationArtwork.Folder;
        FolderInput.TextChanged += (_, _) => UpdateFolderHeading();
        AppVersionText.Text = $"v{AppUpdater.FormatVersion(AppUpdater.CurrentVersion)}";
        CurrentVersionText.Text = $"현재 {AppVersionText.Text}";
        ActivityList.ItemsSource = Activities;
        trayMenu.Items.Add("창 열기", null, (_, _) => OnUi(ShowWindow));
        trayMenu.Items.Add(trayStart);
        trayMenu.Items.Add(trayStop);
        trayMenu.Items.Add("감시 폴더 열기", null, (_, _) => OnUi(OpenFolder));
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        trayMenu.Items.Add("종료", null, (_, _) => OnUi(ExitApp));
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "폴더 알림 · 대기 중", Visible = true, ContextMenuStrip = trayMenu };
        TaskbarItemInfo = new TaskbarItemInfo { Description = "폴더 알림 · 미확인 알림 없음" };
        tray.DoubleClick += (_, _) => OnUi(ShowWindow);
        trayStart.Click += (_, _) => OnUi(async () => await StartMonitoringAsync());
        trayStop.Click += (_, _) => OnUi(async () => await StopMonitoringAsync());
        settings = AppSettings.Load(out var loadError, settingsPath);
        FolderInput.Text = settings.Folder;
        UpdateFolderHeading();
        RecursiveSwitch.IsChecked = settings.IncludeSubfolders;
        AutoStartSwitch.IsChecked = settings.StartOnLaunch;
        NotificationDurationSelector.ItemsSource = Enumerable.Range(1, 5);
        NotificationDurationSelector.SelectedItem = settings.NotificationDurationSeconds;
        NotificationDurationSelector.SelectionChanged += NotificationDurationChanged;
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
        Activated += (_, _) =>
        {
            // Windows can deliver activation while a tray window is still hidden.
            if (IsVisible && WindowState != WindowState.Minimized) MarkNotificationsRead();
        };
        Closed += (_, _) => { NotificationPopup?.ClosePermanently(); tray.Visible = false; tray.Dispose(); trayMenu.Dispose(); unreadTrayIcon.Dispose(); trayIcon.Dispose(); appIcon.Dispose(); };
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

    private void NotificationDurationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NotificationDurationSelector.SelectedItem is not int seconds) return;
        var previous = settings.NotificationDurationSeconds;
        if (previous == seconds) return;
        settings.NotificationDurationSeconds = seconds;
        try
        {
            settings.Save();
            NotificationPopup?.SetDisplayDuration(seconds);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            settings.NotificationDurationSeconds = previous;
            NotificationDurationSelector.SelectedItem = previous;
            AddHistory("알림 시간 저장 실패", ex.Message);
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
    private void TestNotificationClicked(object sender, RoutedEventArgs e)
    {
        const string message = "예시 파일로 알림 디자인을 확인했습니다. 실제 파일은 추가하지 않았습니다.";
        ShowNotification("새 파일 2개 도착", message, files: new[] { "월간 보고서.pdf", "입고 목록.xlsx" },
            folder: string.IsNullOrWhiteSpace(FolderInput.Text) ? "예시 폴더 · 업무 자료" : FolderInput.Text, isPreview: true);
        AddHistory("알림 미리보기", message);
    }

    private void ShowNotification(string title, string message, int unreadCount = 1,
        IReadOnlyList<string>? files = null, string? folder = null, bool isPreview = false)
    {
        if (closing || sessionEnding) return;
        if (unreadCount > 0)
        {
            latestNotification = new(title, message, files?.ToArray(), folder, isPreview);
            UnreadNotificationCount = (int)Math.Min(int.MaxValue, (long)UnreadNotificationCount + unreadCount);
            UpdateNotificationIndicator();
        }
        NotificationPopup ??= new NotificationWindow(OpenNotificationActivity, TimeSpan.FromSeconds(settings.NotificationDurationSeconds));
        NotificationPopup.ShowNotification(title, message, files, folder, isPreview);
    }
    private void ClearClicked(object sender, RoutedEventArgs e)
    {
        MarkNotificationsRead();
        Activities.Clear(); totalAdded = 0; FileCount.Text = "0"; EmptyState.Visibility = Visibility.Visible;
        latestNotification = null;
        UpdateInboxCard();
    }

    private void MarkReadClicked(object sender, RoutedEventArgs e) => MarkNotificationsRead();

    private void MarkNotificationsRead()
    {
        if (UnreadNotificationCount == 0) return;
        UnreadNotificationCount = 0;
        UpdateNotificationIndicator();
    }

    private void SetTrayStatus(string status) { trayStatus = status; UpdateTrayTooltip(); }

    private void UpdateTrayTooltip() => tray.Text = $"폴더 알림 · {trayStatus}"
        + (UnreadNotificationCount > 0 ? $" · 미확인 {UnreadNotificationCount:N0}개" : "");

    private void UpdateNotificationIndicator()
    {
        var unread = UnreadNotificationCount > 0;
        tray.Icon = unread ? unreadTrayIcon : trayIcon;
        UpdateTrayTooltip();
        MarkReadButton.IsEnabled = unread;
        MarkReadButton.Content = unread ? $"알림 확인 ({(UnreadNotificationCount > 99 ? "99+" : UnreadNotificationCount.ToString("N0"))})" : "알림 확인";
        MarkReadButton.ToolTip = unread ? $"미확인 알림 {UnreadNotificationCount:N0}개 확인" : "미확인 알림이 없습니다";
        TaskbarItemInfo.Description = unread ? $"폴더 알림 · 미확인 {UnreadNotificationCount:N0}개" : "폴더 알림 · 미확인 알림 없음";
        TaskbarItemInfo.Overlay = unread ? CreateUnreadOverlay(UnreadNotificationCount) : null;
        UpdateInboxCard();
    }

    private void UpdateInboxCard()
    {
        var unread = UnreadNotificationCount > 0;
        InboxGraphic.Source = unread ? NotificationArtwork.UnreadBell : NotificationArtwork.Bell;
        NotificationCard.Background = (Brush)new BrushConverter().ConvertFromString(unread ? "#FFF5F5" : "#FFFFFF")!;
        NotificationCard.BorderBrush = (Brush)new BrushConverter().ConvertFromString(unread ? "#F2C3C8" : "#DFE7F2")!;
        UnreadCountBadge.Visibility = unread ? Visibility.Visible : Visibility.Collapsed;
        UnreadCountText.Text = UnreadNotificationCount > 99 ? "99+" : UnreadNotificationCount.ToString("N0");
        UnreadCountBadge.ToolTip = $"미확인 알림 {UnreadNotificationCount:N0}개";
        InboxStatus.Text = unread ? "확인할 알림이 있어요"
            : latestNotification is null ? "새 알림을 기다려요" : "모두 확인했어요";
        InboxSummary.Text = latestNotification?.Files is { Count: > 0 } files
            ? string.Join(" · ", files.Take(2).Select(Path.GetFileName)) + (files.Count > 2 ? $" 외 {files.Count - 2}개" : "")
            : latestNotification?.Title ?? "도착한 파일을 이곳에서 확인하세요";
        InboxSummary.ToolTip = InboxSummary.Text;
        NotificationInboxButton.IsEnabled = latestNotification is not null;
    }

    private void ShowLatestNotification(object sender, RoutedEventArgs e)
    {
        if (closing || sessionEnding || latestNotification is not { } latest) return;
        NotificationPopup ??= new NotificationWindow(OpenNotificationActivity, TimeSpan.FromSeconds(settings.NotificationDurationSeconds));
        NotificationPopup.ShowNotification(latest.Title, latest.Message, latest.Files, latest.Folder, latest.IsPreview);
    }

    private void UpdateFolderHeading()
    {
        var path = FolderInput.Text.Trim();
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        MonitorFolderName.Text = path.Length == 0 ? "폴더를 연결하세요" : string.IsNullOrEmpty(name) ? path : name;
        MonitorFolderName.ToolTip = path;
    }

    private void NavigateTo(WorkspacePage page)
    {
        CurrentPage = page;
        var monitoring = page == WorkspacePage.Monitor;
        var activity = page == WorkspacePage.Activity;
        var settingsPage = page == WorkspacePage.Settings;
        var selected = monitoring ? MonitorNavButton : activity ? ActivityNavButton : SettingsNavButton;
        foreach (var button in new[] { MonitorNavButton, ActivityNavButton, SettingsNavButton })
            button.Tag = button == selected ? "selected" : null;
        PageTitle.Text = monitoring ? "내 파일 모니터" : activity ? "파일 기록" : "환경 설정";
        PageSubtitle.Text = monitoring ? "도착한 파일과 알림을 한곳에서 확인하세요."
            : activity ? "도착한 파일과 감시 상태를 시간순으로 확인하세요." : "감시 범위와 앱 실행 방식을 설정하세요.";
        MonitorCard.Visibility = monitoring ? Visibility.Visible : Visibility.Collapsed;
        ActivityCard.Visibility = settingsPage ? Visibility.Collapsed : Visibility.Visible;
        RightPanel.Visibility = activity ? Visibility.Collapsed : Visibility.Visible;
        NotificationCard.Visibility = monitoring ? Visibility.Visible : Visibility.Collapsed;
        SettingsCard.Visibility = activity ? Visibility.Collapsed : Visibility.Visible;
        WorkspaceLayout.RowDefinitions[3].Height = new GridLength(monitoring ? 18 : 0);
        WorkspaceBody.ColumnDefinitions[1].Width = new GridLength(monitoring ? 16 : 0);
        WorkspaceBody.ColumnDefinitions[2].Width = new GridLength(monitoring ? 264 : 0);
        Grid.SetColumnSpan(ActivityCard, monitoring ? 1 : 3);
        Grid.SetColumn(RightPanel, monitoring ? 2 : 0);
        Grid.SetColumnSpan(RightPanel, monitoring ? 1 : 3);
        RightPanel.RowDefinitions[1].Height = new GridLength(monitoring ? 14 : 0);
        MainSurface.MinHeight = monitoring ? 760 : 520;
        if (settingsPage) MainSurface.ClearValue(HeightProperty);
        else MainSurface.SetBinding(HeightProperty, new System.Windows.Data.Binding("ActualHeight") { Source = RootScroller });
        SettingsCard.Padding = new Thickness(settingsPage ? 28 : 18);
        SettingsHeading.FontSize = settingsPage ? 20 : 16;
        SettingsPageHint.Visibility = settingsPage ? Visibility.Visible : Visibility.Collapsed;
        NotificationDurationRow.Visibility = settingsPage ? Visibility.Visible : Visibility.Collapsed;
        UpdatePanel.Visibility = settingsPage ? Visibility.Visible : Visibility.Collapsed;
        Resources["SettingsLabelSize"] = settingsPage ? 14d : 12d;
        Resources["SettingsDescriptionSize"] = settingsPage ? 12d : 10d;
        RootScroller.ScrollToTop();
    }
    private void NavigateMonitor(object sender, RoutedEventArgs e) => NavigateTo(WorkspacePage.Monitor);
    private void NavigateActivity(object sender, RoutedEventArgs e) => NavigateTo(WorkspacePage.Activity);
    private void NavigateSettings(object sender, RoutedEventArgs e) => NavigateTo(WorkspacePage.Settings);
    private void OpenNotificationActivity() { NavigateTo(WorkspacePage.Activity); ShowWindow(); }

    private static ImageSource CreateUnreadOverlay(int count)
    {
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
        {
            context.DrawEllipse(Brushes.White, null, new Point(8, 8), 8, 8);
            context.DrawEllipse(Brushes.Red, null, new Point(8, 8), 7, 7);
            var text = new FormattedText(count > 99 ? "99+" : count.ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"),
                count > 99 ? 6 : 9, Brushes.White, 1);
            context.DrawText(text, new Point((16 - text.Width) / 2, (16 - text.Height) / 2));
        }
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
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
        var monitor = new RealtimeFolderMonitor(folder, settings.IncludeSubfolders);
        monitoringTask = MonitorLoopAsync(monitor, cancellation.Token);
        await monitoringTask;
    }

    private async Task MonitorLoopAsync(RealtimeFolderMonitor monitor, CancellationToken token)
    {
        var baselineReady = false;
        string? lastError = null;
        string? lastWatcherError = null;
        try
        {
            await foreach (var update in monitor.WatchAsync(token))
            {
                var result = update.Scan;
                token.ThrowIfCancellationRequested();
                IsRealtimeMonitoring = update.IsRealtime;
                if (update.WatcherError is not null && update.WatcherError != lastWatcherError)
                    AddHistory("이벤트 감시 연결 확인", update.WatcherError);
                if (lastWatcherError is not null && update.IsRealtime)
                    AddHistory("실시간 감시 연결 복구", activeFolder);
                lastWatcherError = update.WatcherError;
                if (result.Error is not null)
                {
                    SetStatus("연결 확인", "폴더 연결 또는 접근 권한을 확인해 주세요. 자동으로 재시도합니다.", "#FFF4E8", "#B56712");
                    SetTrayStatus("연결 / 접근 오류");
                    if (result.Error != lastError)
                    {
                        AddHistory("감시 오류", result.Error);
                        ShowNotification("폴더를 확인할 수 없습니다", "경로, 연결 상태와 폴더 접근 권한을 확인해 주세요. 자동으로 재시도합니다.");
                    }
                    lastError = result.Error;
                }
                else
                {
                    if (lastError is not null) AddHistory("연결 복구", activeFolder);
                    lastError = null;
                    if (!baselineReady) { baselineReady = true; AddHistory("감시 시작", activeFolder); }
                    var detail = update.IsRealtime ? "실시간 이벤트 감시 · 누락 재확인 30초" : "이벤트 연결 재시도 · 2초 간격 재확인";
                    SetStatus("감시 중", $"{detail} · 최근 확인 {DateTime.Now:HH:mm:ss}",
                        update.IsRealtime ? "#E9F6EE" : "#FFF4E8", update.IsRealtime ? "#20844D" : "#B56712");
                    SetTrayStatus(update.IsRealtime ? "실시간 감시 중" : "이벤트 연결 재시도");
                    if (result.AddedFiles.Count > 0)
                    {
                        LastDetectionSource = update.Trigger.ToString();
                        NotifyAdded(result.AddedFiles);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            AddHistory("감시 중단", ex.Message);
            SetStatus("중단됨", "감시가 중단되었습니다. 이력을 확인하고 다시 시작해 주세요.", "#FDECEE", "#B42337");
            SetTrayStatus("감시 중단");
            ShowNotification("폴더 감시 중단", ex.Message);
        }
        finally
        {
            cancellation?.Dispose(); cancellation = null;
            IsRealtimeMonitoring = false;
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
        SetTrayStatus("중지됨");
        AddHistory("감시 중지", activeFolder);
    }

    private void SetStatus(string text, string detail, string background, string foreground)
    {
        StatusText.Text = text; StatusDetail.Text = detail;
        StatusBadge.Background = (Brush)new BrushConverter().ConvertFromString(background)!;
        var color = (Brush)new BrushConverter().ConvertFromString(foreground)!;
        StatusDot.Fill = StatusText.Foreground = color;
        FolderGraphic.Source = text == "감시 중" && IsRealtimeMonitoring ? NotificationArtwork.ConnectedFolder : NotificationArtwork.Folder;
    }

    private void NotifyAdded(IReadOnlyList<string> files)
    {
        totalAdded += files.Count; FileCount.Text = totalAdded.ToString("N0");
        foreach (var path in files.TakeLast(500)) AddHistory(Path.GetFileName(path), path, true);
        var title = files.Count == 1 ? "새 파일 도착" : $"새 파일 {files.Count:N0}개 도착";
        ShowNotification(title, "", files.Count, files, activeFolder);
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
    internal void ShowWindow()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        MarkNotificationsRead();
    }
    private void HideToTray()
    {
        Hide();
        if (hideHintShown) return;
        hideHintShown = true;
        ShowNotification("폴더 알림이 실행 중입니다", "트레이 아이콘을 두 번 클릭하면 창이 열립니다. 종료는 트레이 메뉴에서 선택하세요.", unreadCount: 0);
    }
    internal void ExitApp() { exiting = true; Close(); }
    internal void PrepareForSessionEnd() { sessionEnding = exiting = true; cancellation?.Cancel(); updateCancellation?.Cancel(); NotificationPopup?.DismissImmediately(); tray.Visible = false; }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (sessionEnding) return;
        if (closing) { e.Cancel = true; return; }
        if (!exiting) { e.Cancel = true; HideToTray(); return; }
        e.Cancel = true; closing = true; IsEnabled = false;
        updateCancellation?.Cancel();
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
