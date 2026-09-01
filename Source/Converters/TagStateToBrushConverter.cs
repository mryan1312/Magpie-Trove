using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using MagpieTrove.Models;

namespace MagpieTrove.Converters;

public sealed class TagStateToBrushConverter : IValueConverter
{
	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		if (value is TagFilterState tagFilterState)
		{
			switch (tagFilterState)
			{
			case TagFilterState.Include:
				return new SolidColorBrush(Color.FromRgb(79, 155, 232));
			case TagFilterState.Exclude:
				return new SolidColorBrush(Color.FromRgb(224, 108, 108));
			}
		}
		return new SolidColorBrush(Color.FromRgb(69, 69, 78));
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return Binding.DoNothing;
	}
}
