using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MagpieTrove.Converters;

public sealed class NullToVisibilityConverter : IValueConverter
{
	public bool Invert { get; set; }

	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return ((value == null || (value is string text && text.Length == 0)) ^ Invert) ? Visibility.Collapsed : Visibility.Visible;
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return Binding.DoNothing;
	}
}
