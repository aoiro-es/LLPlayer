using System;
using System.Globalization;
using System.Windows.Data;

namespace FlyleafLib.Localization;

public sealed class UiValueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => UiText.Describe(value);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
