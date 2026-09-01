using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MagpieTrove.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
	public bool Invert { get; set; }

	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return (!((value is bool && (bool)value) ^ Invert)) ? Visibility.Collapsed : Visibility.Visible;
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return Binding.DoNothing;
	}
}
