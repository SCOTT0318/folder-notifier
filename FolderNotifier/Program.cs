using System.Windows;

namespace FolderNotifier;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var atLogin = args.Contains("--startup", StringComparer.OrdinalIgnoreCase);
        using var instance = new Mutex(true, @"Local\FolderNotifier.Desktop", out var firstInstance);
        if (!firstInstance)
        {
            if (!atLogin)
                MessageBox.Show("이미 실행 중입니다. 작업 표시줄의 숨겨진 아이콘에서 폴더 알림을 열어 주세요.",
                    "폴더 알림", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var app = new App();
        app.InitializeComponent();
        var window = new MainWindow();
        app.SessionEnding += (_, _) => window.PrepareForSessionEnd();
        if (atLogin && window.CanStartInBackground)
        {
            app.MainWindow = window;
            window.StartInBackground();
            app.Run();
        }
        else app.Run(window);
    }
}
