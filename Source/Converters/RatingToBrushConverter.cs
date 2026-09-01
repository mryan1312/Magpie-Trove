using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MagpieTrove.Converters;

public sealed class RatingToBrushConverter : IValueConverter
{
	public Brush On { get; set; } = new SolidColorBrush(Color.FromRgb(232, 194, 79));

	public Brush Off { get; set; } = new SolidColorBrush(Color.FromRgb(85, 85, 94));

	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		int num = ((value is int num2) ? num2 : 0);
		int num3 = ((parameter != null && int.TryParse(parameter.ToString(), out var result)) ? result : 0);
		if (num < num3)
		{
			return Off;
		}
		return On;
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return Binding.DoNothing;
	}
}
