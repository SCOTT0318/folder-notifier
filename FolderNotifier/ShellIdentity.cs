using System.Runtime.InteropServices;

namespace FolderNotifier;

internal static class ShellIdentity
{
    // Keep this stable across updates and identical to the installer's shortcuts.
    internal const string AppUserModelId = "FolderNotifier.Desktop";

    internal static void Configure() => Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(AppUserModelId));

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
