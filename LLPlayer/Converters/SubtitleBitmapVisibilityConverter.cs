using System.Globalization;
using System.Windows;
using System.Windows.Data;
using LLPlayer.Services;

namespace LLPlayer.Converters;

public class SubtitleBitmapVisibilityConverter : IMultiValueConverter
{
    public bool Absolute { get; set; }
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not bool flag || values[1] is not SubtitleTrackPlacement placement)
            return Visibility.Collapsed;
        bool visible = Absolute ? flag && placement == SubtitleTrackPlacement.Legacy : !SubtitleLayoutPolicy.UseAbsoluteBitmap(flag, placement);
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
