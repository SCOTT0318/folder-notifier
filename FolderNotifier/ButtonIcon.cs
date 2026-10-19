using System.Windows;

namespace FolderNotifier;

internal static class ButtonIcon
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.RegisterAttached(
        "Glyph", typeof(string), typeof(ButtonIcon), new FrameworkPropertyMetadata(""));
    public static string GetGlyph(DependencyObject element) => (string)element.GetValue(GlyphProperty);
    public static void SetGlyph(DependencyObject element, string value) => element.SetValue(GlyphProperty, value);
}
