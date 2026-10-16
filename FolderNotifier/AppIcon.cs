using System.Drawing;

namespace FolderNotifier;

internal static class AppIcon
{
    public static Icon Load(bool unread = false, Size? size = null)
    {
        var resource = unread ? "FolderNotifier.UnreadIcon.ico" : "FolderNotifier.AppIcon.ico";
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("앱 아이콘 리소스를 찾을 수 없습니다.");
        using var icon = size is { } requested ? new Icon(stream, requested) : new Icon(stream);
        return (Icon)icon.Clone();
    }
}
