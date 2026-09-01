using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MagpieTrove.Converters;

public sealed class CountToVisibilityConverter : IValueConverter
{
	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return (!(value is int num) || num <= 0) ? Visibility.Collapsed : Visibility.Visible;
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return Binding.DoNothing;
	}
}
