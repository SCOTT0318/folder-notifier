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
        ("#1D4ED8", "M24 75 Q24 57 42 57 L89 57 Q98 57 104 65 L118 82 L209 82 Q227 82 227 100 L227 187 Q227 207 207 207 L44 207 Q24 207 24 187 Z"),
        ("#93C5FD", "M44 91 L202 91 Q211 91 211 100 L211 125 L35 125 L35 100 Q35 91 44 91 Z"),
        ("#3B82F6", "M37 108 L213 108 Q231 108 229 126 L221 190 Q219 207 201 207 L45 207 Q28 207 26 190 L19 128 Q17 108 37 108 Z"),
        ("#60A5FA", "M37 108 L213 108 Q225 108 228 118 L20 118 Q23 108 37 108 Z"),
        ("#1E3A8A", "M239 178 A58 58 0 1 1 123 178 A58 58 0 1 1 239 178 Z"),
        ("#FBBF24", "M232 178 A51 51 0 1 1 130 178 A51 51 0 1 1 232 178 Z"),
        ("#172554", "M176 147 Q176 141 181 141 Q186 141 186 147 L186 151 Q204 154 204 172 L204 184 Q204 189 211 195 Q214 200 208 200 L154 200 Q148 200 151 195 Q158 189 158 184 L158 172 Q158 154 176 151 Z"),
        ("#172554", "M172 205 L190 205 Q188 215 181 215 Q174 215 172 205 Z")
    ];

    [STAThread]
    private static void Main(string[] args)
    {
        var output = Path.GetFullPath(args.Length > 0 ? args[0] : "FolderNotifier/Assets");
        Directory.CreateDirectory(output);
        var svg = new StringBuilder("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 256 256\" width=\"256\" height=\"256\" role=\"img\" aria-label=\"Folder with notification bell\">\n");
        foreach (var (fill, path) in Shapes) svg.AppendLine($"  <path fill=\"{fill}\" d=\"{path}\"/>");
        svg.AppendLine("</svg>");
        File.WriteAllText(Path.Combine(output, "folder-notifier.svg"), svg.ToString(), new UTF8Encoding(false));
        File.WriteAllBytes(Path.Combine(output, "folder-notifier.png"), Render(512));

        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
        var frames = sizes.Select(Render).ToArray();
        using var writer = new BinaryWriter(File.Create(Path.Combine(output, "folder-notifier.ico")));
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

        // A preview sheet keeps the 16/20/24/32px tray renderings visible at actual size.
        var preview = new DrawingVisual();
        using (var context = preview.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, 640, 340));
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(15, 23, 42)), null, new Rect(320, 0, 320, 340));
            foreach (var left in new[] { 0, 320 })
            {
                DrawAt(context, left + 64, 12, 192);
                var x = left + 36;
                foreach (var size in new[] { 16, 20, 24, 32, 48 })
                {
                    DrawAt(context, x, 250, size);
                    var brush = left == 0 ? Brushes.SlateGray : Brushes.LightSlateGray;
                    context.DrawText(new FormattedText($"{size}px", CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, brush, 1), new Point(x, 306));
                    x += 56;
                }
            }
        }
        SaveDrawing(preview, 640, 340, Path.Combine(output, "icon-preview.png"));
        Console.WriteLine($"Created SVG, PNG, and ICO ({string.Join(", ", sizes)}px): {output}");
    }

    private static void DrawAt(DrawingContext context, double x, double y, int size)
    {
        context.PushTransform(new TranslateTransform(x, y));
        context.PushTransform(new ScaleTransform(size / 256d, size / 256d));
        foreach (var (fill, path) in Shapes)
            context.DrawGeometry((Brush)new BrushConverter().ConvertFromString(fill)!, null, Geometry.Parse(path));
        context.Pop();
        context.Pop();
    }

    private static byte[] Render(int size)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) DrawAt(context, 0, 0, size);
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
