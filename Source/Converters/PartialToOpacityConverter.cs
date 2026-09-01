using System;
using System.Globalization;
using System.Windows.Data;

namespace MagpieTrove.Converters;

public sealed class PartialToOpacityConverter : IValueConverter
{
	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return (value is bool && (bool)value) ? 0.55 : 1.0;
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return Binding.DoNothing;
	}
}
