using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace FolderNotifier;

internal sealed record NotificationFile(string Name, string FullPath)
{
    public string Extension => Path.GetExtension(Name).TrimStart('.').ToUpperInvariant() is { Length: > 0 } extension
        ? extension.Length <= 5 ? extension : "FILE" : "FILE";
}

// An independent window stays visible when the main window is hidden to the tray.
// ShowActivated=False prevents a new file from interrupting the user's typing.
internal partial class NotificationWindow : Window
{
    private readonly DispatcherTimer dismissal;
    private readonly Action openApp;
    private bool disposing;
    internal IReadOnlyList<NotificationFile> DisplayedFiles { get; private set; } = Array.Empty<NotificationFile>();

    internal NotificationWindow(Action openApp, TimeSpan? duration = null)
    {
        InitializeComponent();
        this.openApp = openApp;
        dismissal = new DispatcherTimer
        {
            Interval = duration ?? TimeSpan.FromSeconds(8)
        };
        dismissal.Tick += (_, _) => Dismiss();
        SizeChanged += (_, _) => PositionOnDesktop();
        Closed += (_, _) => dismissal.Stop();
        SourceInitialized += (_, _) =>
        {
            // Windows 11 supplies native corners and shadow without a transparent window.
            var corners = 2;
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 33, ref corners, sizeof(int));
        };
    }

    internal void ShowNotification(string title, string message, IReadOnlyList<string>? files = null, string? folder = null, bool isPreview = false)
    {
        dismissal.Stop();
        NotificationTitle.Text = title;
        NotificationMessage.Text = message;
        DisplayedFiles = files?.Take(3).Select(path => new NotificationFile(Path.GetFileName(path), path)).ToArray()
            ?? Array.Empty<NotificationFile>();
        FileList.ItemsSource = DisplayedFiles;
        var hasFiles = DisplayedFiles.Count > 0;
        FilePanel.Visibility = hasFiles ? Visibility.Visible : Visibility.Collapsed;
        NotificationMessage.Visibility = hasFiles ? Visibility.Collapsed : Visibility.Visible;
        FolderContext.Text = folder ?? "파일 이름과 위치를 한눈에 확인하세요";
        FolderContext.ToolTip = folder;
        OverflowText.Text = files?.Count > 3 ? $"외 {files.Count - 3:N0}개 · 전체 목록은 앱에서 확인" : "";
        OverflowText.Visibility = files?.Count > 3 ? Visibility.Visible : Visibility.Collapsed;
        NotificationKind.Text = isPreview ? "알림 미리보기" : hasFiles ? "감시 폴더에 추가됨" : "안내";
        NotificationTime.Text = DateTime.Now.ToString("HH:mm");
        OpenAppButton.Content = hasFiles ? "파일 목록 보기  →" : "폴더 알림 열기  →";
        // A single popup is reused; bursts update its content and restart its lifetime.
        PositionOnDesktop();
        Show();
        UpdateLayout();
        PositionOnDesktop();
        if (!IsMouseOver) dismissal.Start();
    }

    private void PositionOnDesktop()
    {
        var area = SystemParameters.WorkArea;
        Width = Math.Min(420, Math.Max(1, area.Width - 32));
        MaxHeight = Math.Max(1, area.Height - 24);
        Left = Math.Max(area.Left, area.Right - Width - 12);
        Top = Math.Max(area.Top, area.Bottom - ActualHeight - 12);
    }

    internal void Dismiss() { dismissal.Stop(); Hide(); }
    internal void ClosePermanently() { disposing = true; Close(); }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!disposing) { e.Cancel = true; Dismiss(); }
        base.OnClosing(e);
    }
    private void DismissClicked(object sender, RoutedEventArgs e) => Dismiss();
    private void OpenClicked(object sender, RoutedEventArgs e) { Dismiss(); openApp(); }
    private void PauseDismissal(object sender, MouseEventArgs e) => dismissal.Stop();
    private void ResumeDismissal(object sender, MouseEventArgs e)
    {
        if (IsVisible) { dismissal.Stop(); dismissal.Start(); }
    }
    private void NotificationKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Dismiss(); e.Handled = true; }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
