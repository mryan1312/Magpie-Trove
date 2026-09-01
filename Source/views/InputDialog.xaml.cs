using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;

namespace MagpieTrove.Views;

public partial class InputDialog : Window
{


	public string Value => ValueBox.Text.Trim();

	private InputDialog(string title, string prompt, string initial)
	{
		InitializeComponent();
		base.Title = title;
		PromptText.Text = prompt;
		ValueBox.Text = initial;
		base.Loaded += delegate
		{
			ValueBox.Focus();
			ValueBox.SelectAll();
		};
	}

	public static string? Show(string title, string prompt, string initial = "")
	{
		InputDialog inputDialog = new InputDialog(title, prompt, initial)
		{
			Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault((Window w) => w.IsActive)
		};
		if (inputDialog.ShowDialog() != true || inputDialog.Value.Length <= 0)
		{
			return null;
		}
		return inputDialog.Value;
	}

	private void OnOk(object sender, RoutedEventArgs e)
	{
		base.DialogResult = true;
	}

	private void OnCancel(object sender, RoutedEventArgs e)
	{
		base.DialogResult = false;
	}

	private void OnValueKeyDown(object sender, KeyEventArgs e)
	{
		if ((int)e.Key == 6)
		{
			base.DialogResult = true;
		}
	}

}
