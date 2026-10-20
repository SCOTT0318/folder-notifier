namespace FolderNotifier;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ShellIdentity.Configure();
        var atLogin = args.Contains("--startup", StringComparer.OrdinalIgnoreCase);
        using var instance = new Mutex(true, @"Local\FolderNotifier.Desktop", out var firstInstance);
        if (!firstInstance)
        {
            if (!atLogin)
                Environment.ExitCode = InstanceActivation.RequestAsync().GetAwaiter().GetResult() ? 0 : 1;
            return;
        }
        var app = new App();
        app.InitializeComponent();
        var window = new MainWindow();
        using var activation = new InstanceActivation(() =>
            app.Dispatcher.BeginInvoke(new Action(window.ShowWindow)));
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
