using Microsoft.Win32;

namespace FolderNotifier;

internal interface IStartupRegistration
{
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
}

internal sealed class StartupRegistration : IStartupRegistration
{
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "FolderNotifier";
    private readonly string executablePath;
    private readonly string registryPath;

    public StartupRegistration(string? executablePath = null, string registryPath = RunKey)
    {
        this.executablePath = executablePath ?? Environment.ProcessPath
            ?? throw new IOException("실행 파일 경로를 확인할 수 없습니다.");
        this.registryPath = registryPath;
    }

    internal string Command => $"\"{executablePath}\" --startup";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(registryPath);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            if (Command.Length > 260)
                throw new IOException("실행 파일 경로가 너무 깁니다. 설치 버전에서 자동 실행을 설정해 주세요.");
            using var key = Registry.CurrentUser.CreateSubKey(registryPath, writable: true)
                ?? throw new UnauthorizedAccessException("Windows 자동 실행 설정에 접근할 수 없습니다.");
            key.SetValue(ValueName, Command, RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(registryPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
