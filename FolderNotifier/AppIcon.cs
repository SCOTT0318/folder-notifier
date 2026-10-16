using System.Drawing;

namespace FolderNotifier;

internal static class AppIcon
{
    public static Icon Load()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("FolderNotifier.AppIcon.ico")
            ?? throw new InvalidOperationException("앱 아이콘 리소스를 찾을 수 없습니다.");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }
}
