using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace FolderNotifier;

// An independent window stays visible when the main window is hidden to the tray.
// ShowActivated=False prevents a new file from interrupting the user's typing.
internal partial class NotificationWindow : Window
{
    private readonly DispatcherTimer dismissal;
    private readonly Action openApp;
    private bool disposing;

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
    }

    internal void ShowNotification(string title, string message)
    {
        dismissal.Stop();
        NotificationTitle.Text = title;
        NotificationMessage.Text = message;
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
        Width = Math.Min(380, Math.Max(1, area.Width - 24));
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
}
