using System.Globalization;
using System.Windows.Data;

namespace BengaliKeyboard.UI.Controls;

/// <summary>
/// WPF value converter that transforms an emoji Unicode string (e.g., <c>"😂"</c>, <c>"❤️"</c>, <c>"🇧🇩"</c>)
/// into a cached, frozen, full-color <see cref="System.Windows.Media.Imaging.BitmapSource"/> via <see cref="ColorEmojiRenderer"/>.
/// </summary>
public sealed class ColorEmojiConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string emoji || string.IsNullOrWhiteSpace(emoji))
        {
            return null;
        }

        var pixelSize = 64;
        if (parameter is string paramStr && int.TryParse(paramStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            pixelSize = Math.Clamp(parsed, 24, 128);
        }
        else if (parameter is int paramInt)
        {
            pixelSize = Math.Clamp(paramInt, 24, 128);
        }

        return ColorEmojiRenderer.GetOrCreateEmojiBitmap(emoji, pixelSize);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
