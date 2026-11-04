using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LLPlayer.Services;

namespace LLPlayer.Converters;

/// <summary>Forced edge placement uses the visible bitmap, excluding transparent full-frame padding.</summary>
public sealed class SubtitleBitmapContentConverter : IMultiValueConverter
{
    private sealed record Result(BitmapSource? Image);
    private static readonly ConditionalWeakTable<BitmapSource, Result> Cache = new();

    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not BitmapSource source || values[1] is not SubtitleTrackPlacement placement) return null;
        return placement == SubtitleTrackPlacement.Legacy ? source : Cache.GetValue(source, s => new Result(Crop(s))).Image;
    }

    public static BitmapSource? Crop(BitmapSource source)
    {
        if (source.Format != PixelFormats.Bgra32 && source.Format != PixelFormats.Pbgra32) return source;
        int stride = checked(source.PixelWidth * 4);
        byte[] pixels = new byte[checked(stride * source.PixelHeight)];
        source.CopyPixels(pixels, stride, 0);
        int left = source.PixelWidth, top = source.PixelHeight, right = -1, bottom = -1;
        for (int y = 0; y < source.PixelHeight; y++)
            for (int x = 0; x < source.PixelWidth; x++)
                if (pixels[y * stride + x * 4 + 3] != 0)
                {
                    left = Math.Min(left, x); right = Math.Max(right, x);
                    top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                }
        if (right < left) return null;
        if (left == 0 && top == 0 && right == source.PixelWidth - 1 && bottom == source.PixelHeight - 1) return source;
        var cropped = new CroppedBitmap(source, new Int32Rect(left, top, right - left + 1, bottom - top + 1));
        cropped.Freeze();
        return cropped;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
