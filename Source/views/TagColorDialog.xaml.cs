using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace MagpieTrove.Views;

public partial class TagColorDialog : Window
{


	public string ColorValue { get; private set; }

	public TagColorDialog(string current)
	{
		InitializeComponent();
		ColorValue = Normalize(current) ?? "#4FA3E3";
		HexBox.Text = ColorValue;
	}

	private void OnSwatch(object sender, RoutedEventArgs e)
	{
		if (sender is Button { Tag: string tag })
		{
			HexBox.Text = tag;
		}
	}

	private void OnHexChanged(object sender, TextChangedEventArgs e)
	{
		string text = Normalize(HexBox.Text);
		if (text != null)
		{
			Preview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(text));
		}
	}

	private void OnOk(object sender, RoutedEventArgs e)
	{
		string text = Normalize(HexBox.Text);
		if (text == null)
		{
			MessageBox.Show(this, "Enter a colour as #RRGGBB.", "Magpie Trove", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		ColorValue = text;
		base.DialogResult = true;
	}

	private static string? Normalize(string? value)
	{
		value = value?.Trim().ToUpperInvariant();
		if (value == null || value.Length != 7 || value[0] != '#')
		{
			return null;
		}
		string text = value;
		if (!int.TryParse(text.Substring(1, text.Length - 1), NumberStyles.HexNumber, null, out var _))
		{
			return null;
		}
		return value;
	}

}
