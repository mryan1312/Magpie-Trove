using System;
using System.Globalization;
using System.Windows.Data;
using MagpieTrove.Models;

namespace MagpieTrove.Converters;

public sealed class TagStateToGlyphConverter : IValueConverter
{
	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		if (value is TagFilterState tagFilterState)
		{
			switch (tagFilterState)
			{
			case TagFilterState.Include:
				return "+";
			case TagFilterState.Exclude:
				return "–";
			}
		}
		return "";
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return Binding.DoNothing;
	}
}
