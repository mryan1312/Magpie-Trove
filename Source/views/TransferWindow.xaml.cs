using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using Microsoft.Win32;
using MagpieTrove.Models;
using MagpieTrove.Services;
using MagpieTrove.ViewModels;

namespace MagpieTrove.Views;

public partial class TransferWindow : Window
{
	private readonly MainViewModel _viewModel;







	public TransferWindow(MainViewModel viewModel)
	{
		InitializeComponent();
		_viewModel = viewModel;
		SelectedOnlyBox.IsEnabled = viewModel.SelectedImages.Count > 0;
		SelectedOnlyBox.IsChecked = viewModel.SelectedImages.Count > 0;
		ExportPhotosButton.Content = $"Export {viewModel.SelectedImages.Count:N0} selected photo(s)";
		ExportPhotosButton.IsEnabled = viewModel.SelectedImages.Count > 0;
	}

	private void OnBrowseDestination(object sender, RoutedEventArgs e)
	{
		OpenFolderDialog openFolderDialog = new OpenFolderDialog
		{
			Title = "Choose export folder"
		};
		if (Directory.Exists(DestinationBox.Text))
		{
			openFolderDialog.InitialDirectory = DestinationBox.Text;
		}
		if (openFolderDialog.ShowDialog(this) == true)
		{
			DestinationBox.Text = openFolderDialog.FolderName;
		}
	}

	private async void OnExportPhotos(object sender, RoutedEventArgs e)
	{
		if (!int.TryParse(MaxEdgeBox.Text, out var maxEdge) || maxEdge < 0)
		{
			MessageBox.Show(this, "Resize must be 0 or a positive pixel count.", "Magpie Trove", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		if (string.IsNullOrWhiteSpace(DestinationBox.Text))
		{
			OnBrowseDestination(sender, e);
			if (string.IsNullOrWhiteSpace(DestinationBox.Text))
			{
				return;
			}
		}
		string value = (FormatBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Original";
		ExportImageFormat format = Enum.Parse<ExportImageFormat>(value);
		List<ImageItem> images = _viewModel.SelectedImages.ToList();
		await RunAsync(images.Count, async delegate(IProgress<int> progress)
		{
			PhotoExportResult photoExportResult = await TransferService.ExportPhotosAsync(images, new PhotoExportOptions(DestinationBox.Text, maxEdge, PatternBox.Text, format), progress);
			return $"Exported {photoExportResult.Exported:N0}; skipped {photoExportResult.Skipped:N0}; failed {photoExportResult.Failed:N0}.";
		});
	}

	private async void OnExportTags(object sender, RoutedEventArgs e)
	{
		SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Title = "Export tags",
			Filter = "JSON tag file (*.json)|*.json|CSV tag file (*.csv)|*.csv",
			FileName = "magpietrove-tags.json",
			AddExtension = true
		};
		if (saveFileDialog.ShowDialog(this) == true)
		{
			string destination = ((saveFileDialog.FilterIndex == 2) ? Path.ChangeExtension(saveFileDialog.FileName, ".csv") : Path.ChangeExtension(saveFileDialog.FileName, ".json"));
			List<long> ids = ((SelectedOnlyBox.IsChecked == true) ? _viewModel.SelectedImages.Select((ImageItem i) => i.Id).ToList() : null);
			await RunAsync(1, async delegate
			{
				await TransferService.ExportTagsAsync(destination, ids);
				return "Exported tags to " + Path.GetFileName(destination) + ".";
			});
		}
	}

	private async void OnImportTags(object sender, RoutedEventArgs e)
	{
		OpenFileDialog dialog = new OpenFileDialog
		{
			Title = "Import tags",
			Filter = "Tag files (*.json;*.csv)|*.json;*.csv|JSON files (*.json)|*.json|CSV files (*.csv)|*.csv"
		};
		if (dialog.ShowDialog(this) == true)
		{
			await RunAsync(1, async delegate
			{
				TagImportResult result = await TransferService.ImportTagsAsync(dialog.FileName);
				await ((DispatcherObject)this).Dispatcher.InvokeAsync((Action)_viewModel.ReloadTags);
				return $"Matched {result.MatchedImages:N0} image(s), added {result.TagsApplied:N0} tag assignment(s); {result.UnmatchedImages:N0} unmatched.";
			});
		}
	}

	private async Task RunAsync(int total, Func<IProgress<int>, Task<string>> action)
	{
		Tabs.IsEnabled = false;
		Progress.Visibility = Visibility.Visible;
		Progress.IsIndeterminate = total <= 1;
		Progress.Maximum = Math.Max(1, total);
		StatusText.Text = "Working...";
		Progress<int> arg = new Progress<int>(delegate(int value)
		{
			Progress.Value = value;
		});
		try
		{
			TextBlock statusText = StatusText;
			statusText.Text = await action(arg);
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is JsonException) ? 1 : 0) != 0)
		{
			StatusText.Text = "Operation failed.";
			MessageBox.Show(this, ex.Message, "Magpie Trove", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
		finally
		{
			Tabs.IsEnabled = true;
			Progress.Visibility = Visibility.Collapsed;
		}
	}

}
