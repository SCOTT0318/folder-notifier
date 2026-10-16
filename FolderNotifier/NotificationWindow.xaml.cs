using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FolderNotifier;

internal sealed record NotificationFile(string Name, string FullPath)
{
    public string Extension => Path.GetExtension(Name).TrimStart('.').ToUpperInvariant() is { Length: > 0 } extension
        ? extension.Length <= 5 ? extension : "FILE" : "FILE";
    public ImageSource Icon => NotificationArtwork.File(Extension);
}

// An independent window stays visible when the main window is hidden to the tray.
// ShowActivated=False prevents a new file from interrupting the user's typing.
internal partial class NotificationWindow : Window
{
    private readonly DispatcherTimer dismissal;
    private readonly Action openApp;
    private bool disposing;
    private readonly bool animateArrival;
    private int presentation;
    private bool pointerOver;
    internal bool IsArriving { get; private set; }
    internal bool IsDeparting { get; private set; }
    internal double ArrivalOffset => ArrivalTranslation.Y;
    internal IReadOnlyList<NotificationFile> DisplayedFiles { get; private set; } = Array.Empty<NotificationFile>();

    internal NotificationWindow(Action openApp, TimeSpan? duration = null, bool? animationsEnabled = null)
    {
        InitializeComponent();
        NotificationGraphic.Source = NotificationArtwork.UnreadBell;
        this.openApp = openApp;
        animateArrival = animationsEnabled ?? SystemParameters.ClientAreaAnimation;
        dismissal = new DispatcherTimer
        {
            Interval = duration ?? TimeSpan.FromSeconds(8)
        };
        dismissal.Tick += (_, _) => Dismiss();
        SizeChanged += (_, _) => PositionOnDesktop();
        Closed += (_, _) => dismissal.Stop();
    }

    internal void ShowNotification(string title, string message, IReadOnlyList<string>? files = null, string? folder = null, bool isPreview = false)
    {
        var alreadyVisible = IsVisible;
        var reversingExit = alreadyVisible && IsDeparting;
        var startOffset = reversingExit ? ArrivalTranslation.Y : 36;
        var startOpacity = reversingExit ? ArrivalSurface.Opacity : 0;
        pointerOver = IsMouseOver;
        ResetArrival();
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
        OpenActionText.Text = hasFiles ? "파일 목록 보기  →" : "폴더 알림 열기  →";
        PopupCount.Text = hasFiles ? files!.Count.ToString("N0") : "!";
        PopupCountBadge.Visibility = Visibility.Visible;
        // A single popup is reused; bursts update its content and restart its lifetime.
        if ((!alreadyVisible || reversingExit) && animateArrival)
        {
            ArrivalSurface.Opacity = startOpacity;
            ArrivalTranslation.Y = startOffset;
        }
        PositionOnDesktop();
        Show();
        UpdateLayout();
        PositionOnDesktop();
        if ((!alreadyVisible || reversingExit) && animateArrival) BeginArrival(startOffset, startOpacity);
        if (animateArrival)
            BellRotation.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(-7, 7, TimeSpan.FromMilliseconds(110)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3), FillBehavior = FillBehavior.Stop });
        if (!IsArriving && !pointerOver) dismissal.Start();
    }

    private void BeginArrival(double offset, double opacity)
    {
        IsArriving = true;
        var current = presentation;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(320);
        var slide = new DoubleAnimation(offset, 0, duration) { EasingFunction = ease };
        var fade = new DoubleAnimation(opacity, 1, duration) { EasingFunction = ease };
        // Keep the base at the hidden start until WPF's first animation tick; otherwise
        // the final card can flash for one frame before its clock becomes active.
        slide.Completed += (_, _) =>
        {
            if (current != presentation || !IsVisible || disposing) return;
            ArrivalTranslation.Y = 0;
            ArrivalSurface.Opacity = 1;
            ArrivalTranslation.BeginAnimation(TranslateTransform.YProperty, null);
            ArrivalSurface.BeginAnimation(OpacityProperty, null);
            IsArriving = false;
            if (!pointerOver) dismissal.Start();
        };
        ArrivalTranslation.BeginAnimation(TranslateTransform.YProperty, slide);
        ArrivalSurface.BeginAnimation(OpacityProperty, fade);
    }

    private void ResetArrival()
    {
        presentation++;
        IsArriving = false;
        IsDeparting = false;
        ArrivalTranslation.BeginAnimation(TranslateTransform.YProperty, null);
        ArrivalSurface.BeginAnimation(OpacityProperty, null);
        ArrivalTranslation.Y = 0;
        ArrivalSurface.Opacity = 1;
    }

    private void PositionOnDesktop()
    {
        var area = SystemParameters.WorkArea;
        Width = Math.Min(420, Math.Max(1, area.Width - 32));
        MaxHeight = Math.Max(1, area.Height - 24);
        Left = Math.Max(area.Left, area.Right - Width - 12);
        Top = Math.Max(area.Top, area.Bottom - ActualHeight - 12);
    }

    internal void Dismiss()
    {
        dismissal.Stop();
        if (!IsVisible || IsDeparting) return;
        if (!animateArrival) { DismissImmediately(); return; }
        var offset = ArrivalTranslation.Y;
        var opacity = ArrivalSurface.Opacity;
        ResetArrival();
        IsDeparting = true;
        ArrivalTranslation.Y = offset;
        ArrivalSurface.Opacity = opacity;
        var current = presentation;
        var duration = TimeSpan.FromMilliseconds(240);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var slide = new DoubleAnimation(offset, 36, duration) { EasingFunction = ease };
        var fade = new DoubleAnimation(opacity, 0, duration) { EasingFunction = ease };
        slide.Completed += (_, _) =>
        {
            if (current != presentation || disposing) return;
            Hide();
            ResetArrival();
        };
        ArrivalTranslation.BeginAnimation(TranslateTransform.YProperty, slide);
        ArrivalSurface.BeginAnimation(OpacityProperty, fade);
    }
    internal void DismissImmediately() { dismissal.Stop(); ResetArrival(); Hide(); }
    internal void ClosePermanently() { dismissal.Stop(); ResetArrival(); disposing = true; Close(); }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!disposing) { e.Cancel = true; Dismiss(); }
        base.OnClosing(e);
    }
    private void DismissClicked(object sender, RoutedEventArgs e) => Dismiss();
    private void OpenClicked(object sender, RoutedEventArgs e) { Dismiss(); openApp(); }
    private void PauseDismissal(object sender, MouseEventArgs e) { pointerOver = true; dismissal.Stop(); }
    private void ResumeDismissal(object sender, MouseEventArgs e)
    {
        pointerOver = false;
        if (IsVisible && !IsArriving && !IsDeparting) { dismissal.Stop(); dismissal.Start(); }
    }
    private void NotificationKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Dismiss(); e.Handled = true; }
    }

}
