using System.Windows;
using System.Windows.Media;

namespace FolderNotifier;

// Code-native vector artwork stays sharp at Windows display scaling levels.
internal static class NotificationArtwork
{
    internal static ImageSource Bell { get; } = CreateBell(false);
    internal static ImageSource UnreadBell { get; } = CreateBell(true);
    internal static ImageSource Folder { get; } = CreateFolder(false);
    internal static ImageSource ConnectedFolder { get; } = CreateFolder(true);
    private static readonly ImageSource document = CreateFile("#3C75DD", "document");
    private static readonly ImageSource pdf = CreateFile("#DE5A60", "pdf");
    private static readonly ImageSource sheet = CreateFile("#259B75", "sheet");
    private static readonly ImageSource picture = CreateFile("#8C6BDA", "picture");
    private static readonly ImageSource archive = CreateFile("#D79A32", "archive");

    internal static ImageSource File(string extension) => extension.ToUpperInvariant() switch
    {
        "PDF" => pdf,
        "XLS" or "XLSX" or "CSV" or "ODS" => sheet,
        "PNG" or "JPG" or "JPEG" or "GIF" or "WEBP" or "BMP" or "SVG" => picture,
        "ZIP" or "7Z" or "RAR" or "GZ" or "TAR" => archive,
        _ => document
    };

    private static ImageSource CreateFolder(bool connected)
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 120, 112));
            dc.DrawEllipse(Brush("#EEF3FF"), null, new Point(60, 56), 52, 52);
            dc.DrawGeometry(Brush("#A8C2FF"), null, Geometry.Parse("M18,35 Q18,30 23,30 L48,30 57,40 98,40 Q103,40 103,45 L103,86 Q103,91 98,91 L23,91 Q18,91 18,86 Z"));
            dc.DrawRoundedRectangle(Brushes.White, new Pen(Brush("#DAE4FB"), 1), new Rect(36, 24, 45, 58), 5, 5);
            dc.DrawGeometry(null, new Pen(Brush("#A0B9EA"), 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, Geometry.Parse("M45,38 L67,38 M45,47 L70,47 M45,56 L60,56"));
            dc.DrawGeometry(Brush("#3F73EF"), null, Geometry.Parse("M15,53 Q15,48 20,48 L101,48 Q107,48 106,54 L100,88 Q99,94 93,94 L24,94 Q18,94 17,88 Z"));
            dc.DrawGeometry(null, new Pen(Brush("#A6C2FF"), 2.5) { StartLineCap = PenLineCap.Round }, Geometry.Parse("M28,59 L53,59"));
            dc.DrawEllipse(Brushes.White, null, new Point(96, 87), 17, 17);
            dc.DrawEllipse(Brush(connected ? "#2EB596" : "#5C88F4"), null, new Point(96, 87), 13, 13);
            dc.DrawGeometry(null, new Pen(Brushes.White, 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                Geometry.Parse(connected ? "M90,87 L94,91 102,83" : "M90,87 L102,87 M96,81 L96,93"));
        }
        return Freeze(group);
    }

    private static ImageSource CreateBell(bool unread)
    {
        var group = new DrawingGroup();
        var color = Brush(unread ? "#E05B61" : "#3C75DD");
        using (var dc = group.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 80, 80));
            dc.DrawEllipse(Brush(unread ? "#FFF0F0" : "#EDF3FF"), null, new Point(40, 40), 36, 36);
            dc.DrawEllipse(null, new Pen(Brush(unread ? "#F8D7D9" : "#D7E5FC"), 1), new Point(40, 40), 35, 35);
            dc.DrawGeometry(color, null, Geometry.Parse("M24,35 C24,15 56,15 56,35 L56,46 61,53 Q62,56 58,56 L22,56 Q18,56 19,53 L24,46 Z"));
            dc.DrawGeometry(null, new Pen(Brushes.White, 2.3), Geometry.Parse("M31,35 Q31,27 37,26"));
            dc.DrawEllipse(color, null, new Point(40, 62), 5, 4);
            dc.DrawEllipse(color, null, new Point(40, 18), 3, 3);
            dc.DrawGeometry(null, new Pen(color, 2.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                Geometry.Parse("M15,29 Q11,36 14,43 M65,29 Q69,36 66,43"));
            if (unread)
            {
                dc.DrawEllipse(Brushes.White, null, new Point(62, 17), 11, 11);
                dc.DrawEllipse(Brush("#ED4956"), null, new Point(62, 17), 8, 8);
                dc.DrawEllipse(Brushes.White, null, new Point(62, 17), 2.2, 2.2);
            }
        }
        return Freeze(group);
    }

    private static ImageSource CreateFile(string hex, string kind)
    {
        var group = new DrawingGroup();
        var color = Brush(hex);
        using (var dc = group.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 48, 56));
            dc.DrawRoundedRectangle(Brush("#F1F4F9"), null, new Rect(6, 5, 36, 48), 5, 5);
            dc.DrawGeometry(Brushes.White, new Pen(Brush("#D6DEEA"), 1),
                Geometry.Parse("M10,2 L29,2 40,13 40,47 Q40,50 37,50 L10,50 Q7,50 7,47 L7,5 Q7,2 10,2 Z"));
            dc.DrawGeometry(Brush("#E8EDF6"), null, Geometry.Parse("M29,2 L29,11 Q29,13 31,13 L40,13 Z"));
            dc.DrawRoundedRectangle(color, null, new Rect(2, 23, 29, 25), 5, 5);
            var pen = new Pen(Brushes.White, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            var geometry = kind switch
            {
                "sheet" => "M9,29 L24,29 24,42 9,42 Z M9,33 L24,33 M9,37 L24,37 M14,29 L14,42 M19,29 L19,42",
                "picture" => "M8,30 L25,30 25,42 8,42 Z M9,40 L14,35 18,39 21,36 25,40 M20,33 L21,33",
                "archive" => "M13,28 L20,28 M13,32 L20,32 M13,36 L20,36 M14,40 L19,40 19,44 14,44 Z",
                "pdf" => "M11,41 Q20,21 18,29 Q15,39 24,39 Q13,33 9,42",
                _ => "M10,30 L23,30 M10,35 L23,35 M10,40 L19,40"
            };
            dc.DrawGeometry(null, pen, Geometry.Parse(geometry));
        }
        return Freeze(group);
    }

    private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    private static ImageSource Freeze(DrawingGroup group)
    {
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
}
