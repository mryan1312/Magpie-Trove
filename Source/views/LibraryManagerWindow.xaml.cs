using System;
using System.CodeDom.Compiler;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Microsoft.Win32;
using MagpieTrove.Services;

namespace MagpieTrove.Views;

public partial class LibraryManagerWindow : Window
{
	private readonly Guid _currentLibraryId;

	private CancellationTokenSource? _modelDownloadCts;







	public ObservableCollection<LibraryDefinition> Libraries { get; }

	public AppSettings ResultSettings { get; private set; }

	public LibraryManagerWindow(AppSettings settings)
	{
		LibraryManagerWindow libraryManagerWindow = this;
		InitializeComponent();
		base.DataContext = this;
		_currentLibraryId = settings.CurrentLibraryId;
		Libraries = new ObservableCollection<LibraryDefinition>(settings.Libraries);
		ResultSettings = settings;
		DefaultRootBox.Text = settings.DefaultLibraryRoot;
		ModelDirectoryBox.Text = settings.ModelDirectory;
		UpdateModelStatus();
		base.Loaded += delegate
		{
			libraryManagerWindow.LibraryList.SelectedItem = libraryManagerWindow.Libraries.FirstOrDefault((LibraryDefinition l) => l.Id == settings.CurrentLibraryId) ?? libraryManagerWindow.Libraries.FirstOrDefault();
		};
		base.Closed += delegate
		{
			libraryManagerWindow._modelDownloadCts?.Cancel();
		};
	}

	private void OnBrowseDefault(object sender, RoutedEventArgs e)
	{
		OpenFolderDialog openFolderDialog = new OpenFolderDialog
		{
			Title = "Default location for new Magpie Trove libraries",
			InitialDirectory = (Directory.Exists(DefaultRootBox.Text) ? DefaultRootBox.Text : null)
		};
		if (openFolderDialog.ShowDialog(this) == true)
		{
			DefaultRootBox.Text = openFolderDialog.FolderName;
		}
	}

	private void OnBrowseModel(object sender, RoutedEventArgs e)
	{
		OpenFolderDialog openFolderDialog = new OpenFolderDialog
		{
			Title = "Choose the shared visual-search model folder",
			InitialDirectory = (Directory.Exists(ModelDirectoryBox.Text) ? ModelDirectoryBox.Text : null)
		};
		if (openFolderDialog.ShowDialog(this) == true)
		{
			ModelDirectoryBox.Text = openFolderDialog.FolderName;
			UpdateModelStatus();
		}
	}

	private async void OnDownloadModel(object sender, RoutedEventArgs e)
	{
		string directory;
		try
		{
			directory = ExpandPath(ModelDirectoryBox.Text);
		}
		catch (Exception ex) when (((ex is ArgumentException || ex is NotSupportedException) ? 1 : 0) != 0)
		{
			MessageBox.Show(this, "The model location is not valid.\n\n" + ex.Message, "Magpie Trove", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		_modelDownloadCts = new CancellationTokenSource();
		SetDownloadState(downloading: true);
		ModelStatusText.Text = "Connecting to Hugging Face…";
		Progress<ModelDownloadProgress> progress = new Progress<ModelDownloadProgress>(delegate(ModelDownloadProgress p)
		{
			ModelProgress.IsIndeterminate = !p.TotalBytes.HasValue;
			ModelProgress.Value = p.Percent;
			TextBlock modelStatusText = ModelStatusText;
			long? totalBytes = p.TotalBytes;
			object text;
			if (totalBytes.HasValue)
			{
				long valueOrDefault = totalBytes.GetValueOrDefault();
				text = $"Downloading {FormatBytes(p.DownloadedBytes)} of {FormatBytes(valueOrDefault)} ({p.Percent:0}%)";
			}
			else
			{
				text = "Downloading " + FormatBytes(p.DownloadedBytes);
			}
			modelStatusText.Text = (string)text;
		});
		try
		{
			await ModelInstallService.DownloadAsync(directory, progress, _modelDownloadCts.Token);
			ModelDirectoryBox.Text = directory;
			ModelStatusText.Text = "Installed and verified: " + ModelInstallService.ModelPath(directory);
			DownloadModelButton.Content = "Reinstall model";
		}
		catch (OperationCanceledException)
		{
			ModelStatusText.Text = "Download cancelled; the partial file was removed.";
		}
		catch (Exception ex3) when (((ex3 is HttpRequestException || ex3 is IOException || ex3 is UnauthorizedAccessException || ex3 is InvalidDataException) ? 1 : 0) != 0)
		{
			ModelStatusText.Text = "Model installation failed.";
			MessageBox.Show(this, ex3.Message, "Magpie Trove", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
		finally
		{
			_modelDownloadCts.Dispose();
			_modelDownloadCts = null;
			SetDownloadState(downloading: false);
		}
	}

	private void OnCancelDownload(object sender, RoutedEventArgs e)
	{
		_modelDownloadCts?.Cancel();
	}

	private void SetDownloadState(bool downloading)
	{
		DownloadModelButton.IsEnabled = !downloading;
		SaveButton.IsEnabled = !downloading;
		CancelDownloadButton.Visibility = ((!downloading) ? Visibility.Collapsed : Visibility.Visible);
		ModelProgress.Visibility = ((!downloading) ? Visibility.Collapsed : Visibility.Visible);
		if (!downloading)
		{
			ModelProgress.IsIndeterminate = false;
		}
	}

	private void UpdateModelStatus()
	{
		try
		{
			string text = ModelInstallService.ModelPath(ExpandPath(ModelDirectoryBox.Text));
			if (File.Exists(text))
			{
				long length = new FileInfo(text).Length;
				ModelStatusText.Text = "Installed (" + FormatBytes(length) + "): " + text;
				DownloadModelButton.Content = "Reinstall model";
			}
			else
			{
				ModelStatusText.Text = "Not installed. Download size is approximately 352 MB.";
				DownloadModelButton.Content = "Download model";
			}
		}
		catch (Exception ex) when (((ex is ArgumentException || ex is NotSupportedException) ? 1 : 0) != 0)
		{
			ModelStatusText.Text = "Choose a valid model folder.";
			DownloadModelButton.Content = "Download model";
		}
	}

	private static string FormatBytes(long bytes)
	{
		if (bytes < 1073741824)
		{
			if (bytes < 1048576)
			{
				if (bytes >= 1024)
				{
					return $"{(double)bytes / 1024.0:0.0} KB";
				}
				return $"{bytes} B";
			}
			return $"{(double)bytes / 1048576.0:0.0} MB";
		}
		return $"{(double)bytes / 1073741824.0:0.0} GB";
	}

	private void OnNew(object sender, RoutedEventArgs e)
	{
		string text = InputDialog.Show("New library", "Library name:", "My library");
		if (!string.IsNullOrWhiteSpace(text))
		{
			string path = ExpandPath(DefaultRootBox.Text);
			string text2 = string.Concat(text.Select((char c) => (!Enumerable.Contains(Path.GetInvalidFileNameChars(), c)) ? c : '_')).Trim();
			if (text2.Length == 0)
			{
				text2 = "Library";
			}
			string directory = Path.Combine(path, text2);
			int num = 2;
			while (Libraries.Any((LibraryDefinition l) => PathsEqual(l.Directory, directory)))
			{
				directory = Path.Combine(path, $"{text2} {num}");
				num++;
			}
			Directory.CreateDirectory(directory);
			LibraryDefinition libraryDefinition = new LibraryDefinition(Guid.NewGuid(), text.Trim(), directory);
			Libraries.Add(libraryDefinition);
			LibraryList.SelectedItem = libraryDefinition;
		}
	}

	private void OnAddExisting(object sender, RoutedEventArgs e)
	{
		OpenFolderDialog openFolderDialog = new OpenFolderDialog
		{
			Title = "Choose an existing Magpie Trove library folder",
			InitialDirectory = (Directory.Exists(DefaultRootBox.Text) ? DefaultRootBox.Text : null)
		};
		if (openFolderDialog.ShowDialog(this) == true)
		{
			string directory = Path.GetFullPath(openFolderDialog.FolderName);
			LibraryDefinition libraryDefinition = Libraries.FirstOrDefault((LibraryDefinition l) => PathsEqual(l.Directory, directory));
			if ((object)libraryDefinition != null)
			{
				LibraryList.SelectedItem = libraryDefinition;
			}
			else if (File.Exists(Path.Combine(directory, "magpietrove.db")) || MessageBox.Show(this, "This folder does not contain magpietrove.db. Add it as a new empty library?", "Magpie Trove", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
			{
				string name = new DirectoryInfo(directory).Name;
				LibraryDefinition libraryDefinition2 = new LibraryDefinition(Guid.NewGuid(), name, directory);
				Libraries.Add(libraryDefinition2);
				LibraryList.SelectedItem = libraryDefinition2;
			}
		}
	}

	private void OnRename(object sender, RoutedEventArgs e)
	{
		if (LibraryList.SelectedItem is LibraryDefinition libraryDefinition)
		{
			string text = InputDialog.Show("Rename library", "Library name:", libraryDefinition.Name);
			if (!string.IsNullOrWhiteSpace(text))
			{
				int index = Libraries.IndexOf(libraryDefinition);
				LibraryDefinition libraryDefinition2 = libraryDefinition with
				{
					Name = text.Trim()
				};
				Libraries[index] = libraryDefinition2;
				LibraryList.SelectedItem = libraryDefinition2;
			}
		}
	}

	private void OnForget(object sender, RoutedEventArgs e)
	{
		if (LibraryList.SelectedItem is LibraryDefinition libraryDefinition && Libraries.Count > 1)
		{
			if (libraryDefinition.Id == _currentLibraryId)
			{
				MessageBox.Show(this, "Open another library before forgetting the current one.", "Magpie Trove", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			}
			else
			{
				Libraries.Remove(libraryDefinition);
			}
		}
	}

	private void OnSave(object sender, RoutedEventArgs e)
	{
		if (!(LibraryList.SelectedItem is LibraryDefinition libraryDefinition))
		{
			MessageBox.Show(this, "Select a library to open.", "Magpie Trove", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		string defaultLibraryRoot;
		string modelDirectory;
		try
		{
			defaultLibraryRoot = ExpandPath(DefaultRootBox.Text);
			modelDirectory = ExpandPath(ModelDirectoryBox.Text);
		}
		catch (Exception ex) when (((ex is ArgumentException || ex is NotSupportedException) ? 1 : 0) != 0)
		{
			MessageBox.Show(this, "The default location is not valid.\n\n" + ex.Message, "Magpie Trove", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		ResultSettings = new AppSettings
		{
			DefaultLibraryRoot = defaultLibraryRoot,
			ModelDirectory = modelDirectory,
			CurrentLibraryId = libraryDefinition.Id,
			Libraries = Libraries.ToList()
		};
		base.DialogResult = true;
	}

	private static string ExpandPath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("Choose a default location.");
		}
		return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
	}

	private static bool PathsEqual(string left, string right)
	{
		return string.Equals(Path.GetFullPath(left).TrimEnd(new char[2] { '\\', '/' }), Path.GetFullPath(right).TrimEnd(new char[2] { '\\', '/' }), StringComparison.OrdinalIgnoreCase);
	}

}
