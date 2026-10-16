using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class Program
{
    // SVG and Windows raster assets use the same geometry, with a transparent backdrop.
    private static readonly (string Fill, string Path)[] Shapes =
    [
        ("#1D4ED8", "M8 46 Q8 22 32 22 L88 22 Q98 22 106 32 L126 56 L224 56 Q248 56 248 80 L248 212 Q248 240 220 240 L36 240 Q8 240 8 212 Z"),
        ("#93C5FD", "M32 70 L224 70 L224 108 L32 108 Z"),
        ("#2563EB", "M30 88 L226 88 Q248 88 246 112 L238 216 Q236 240 212 240 L44 240 Q20 240 18 216 L10 112 Q8 88 30 88 Z"),
        ("#FFFFFF", "M118 111 Q118 101 128 101 Q138 101 138 111 L138 117 Q168 122 168 153 L168 177 L182 193 Q188 201 176 201 L80 201 Q68 201 74 193 L88 177 L88 153 Q88 122 118 117 Z"),
        ("#FFFFFF", "M112 210 L144 210 Q142 226 128 226 Q114 226 112 210 Z")
    ];

    private static readonly (string Fill, string Path)[] Badge =
    [
        ("#FFFFFF", "M256 48 A48 48 0 1 1 160 48 A48 48 0 1 1 256 48 Z"),
        ("#DC2626", "M248 48 A40 40 0 1 1 168 48 A40 40 0 1 1 248 48 Z"),
        ("#FFFFFF", "M200 22 L216 22 L216 53 L200 53 Z M200 62 L216 62 L216 77 L200 77 Z")
    ];

    [STAThread]
    private static void Main(string[] args)
    {
        var output = Path.GetFullPath(args.Length > 0 ? args[0] : "FolderNotifier/Assets");
        Directory.CreateDirectory(output);
        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
        foreach (var unread in new[] { false, true })
        {
            var name = unread ? "folder-notifier-unread" : "folder-notifier";
            var svg = new StringBuilder("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 256 256\" width=\"256\" height=\"256\" role=\"img\" aria-label=\"Folder notification icon\">\n");
            foreach (var (fill, path) in unread ? Shapes.Concat(Badge) : Shapes)
                svg.AppendLine($"  <path fill=\"{fill}\" d=\"{path}\"/>");
            svg.AppendLine("</svg>");
            File.WriteAllText(Path.Combine(output, name + ".svg"), svg.ToString(), new UTF8Encoding(false));
            File.WriteAllBytes(Path.Combine(output, name + ".png"), Render(512, unread));
            WriteIcon(Path.Combine(output, name + ".ico"), sizes, unread);
        }

        // Normal and unread assets at their real tray sizes on both backgrounds.
        var preview = new DrawingVisual();
        using (var context = preview.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, 640, 360));
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(15, 23, 42)), null, new Rect(320, 0, 320, 360));
            foreach (var left in new[] { 0, 320 })
            {
                DrawAt(context, left + 44, 12, 112);
                DrawAt(context, left + 180, 12, 112, true);
                var x = left + 24;
                foreach (var size in new[] { 16, 20, 24, 32, 48 })
                {
                    DrawAt(context, x, 174, size);
                    DrawAt(context, x, 246, size, true);
                    var brush = left == 0 ? Brushes.SlateGray : Brushes.LightSlateGray;
                    context.DrawText(new FormattedText($"{size}px", CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, brush, 1), new Point(x, 320));
                    x += 58;
                }
            }
        }
        SaveDrawing(preview, 640, 360, Path.Combine(output, "icon-preview.png"));
        Console.WriteLine($"Created normal/unread SVG, PNG, and ICO ({string.Join(", ", sizes)}px): {output}");
    }

    private static void WriteIcon(string path, int[] sizes, bool unread)
    {
        var frames = sizes.Select(size => Render(size, unread)).ToArray();
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)sizes.Length);
        var offset = 6 + sizes.Length * 16;
        for (var i = 0; i < sizes.Length; i++)
        {
            writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
            writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(frames[i].Length);
            writer.Write(offset);
            offset += frames[i].Length;
        }
        foreach (var frame in frames) writer.Write(frame);

    }

    private static void DrawAt(DrawingContext context, double x, double y, int size, bool unread = false)
    {
        context.PushTransform(new TranslateTransform(x, y));
        context.PushTransform(new ScaleTransform(size / 256d, size / 256d));
        foreach (var (fill, path) in unread ? Shapes.Concat(Badge) : Shapes)
            context.DrawGeometry((Brush)new BrushConverter().ConvertFromString(fill)!, null, Geometry.Parse(path));
        context.Pop();
        context.Pop();
    }

    private static byte[] Render(int size, bool unread = false)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) DrawAt(context, 0, 0, size, unread);
        return Encode(visual, size, size);
    }

    private static byte[] Encode(DrawingVisual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void SaveDrawing(DrawingVisual visual, int width, int height, string path) =>
        File.WriteAllBytes(path, Encode(visual, width, height));
}
