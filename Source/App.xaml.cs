using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using MagpieTrove.Data;
using MagpieTrove.Models;
using MagpieTrove.Services;
using MagpieTrove.ViewModels;
using MagpieTrove.Views;

namespace MagpieTrove;

public partial class App : Application
{
	private ThumbnailService? _thumbnails;

	private AppSettings _settings;

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		LegacyMigration.MoveSharedDataDirectory();
		_settings = AppSettingsService.Load();
		Database.ConfigureLibraryDirectory(_settings.CurrentLibrary.Directory);
		try
		{
			Database.Initialize();
			AppSettingsService.Save(_settings);
		}
		catch (Exception ex)
		{
			MessageBox.Show("Magpie Trove could not open its library database.\n\n" + Database.DatabasePath + "\n\n" + ex.Message, "Magpie Trove", MessageBoxButton.OK, MessageBoxImage.Hand);
			Shutdown(1);
			return;
		}
		base.DispatcherUnhandledException += new DispatcherUnhandledExceptionEventHandler(OnUnhandledException);
		OpenMainWindow();
	}

	private void OpenMainWindow()
	{
		_thumbnails = new ThumbnailService(((DispatcherObject)this).Dispatcher);
		ImageItem.ThumbnailSource = _thumbnails;
		base.MainWindow = new MainWindow(new MainViewModel(_thumbnails));
		base.MainWindow.Show();
	}

	public void ApplyLibrarySettings(AppSettings settings)
	{
		settings = AppSettingsService.Normalize(settings);
		LibraryDefinition requested = settings.CurrentLibrary;
		string currentDirectory = Database.DataDirectory;
		if (string.Equals(Path.GetFullPath(requested.Directory), Path.GetFullPath(currentDirectory), StringComparison.OrdinalIgnoreCase))
		{
			_settings = settings;
			AppSettingsService.Save(_settings);
		}
		else
		{
			((DispatcherObject)this).Dispatcher.BeginInvoke((Delegate)(Action)delegate
			{
				SwitchLibrary(settings, requested, currentDirectory);
			}, Array.Empty<object>());
		}
	}

	private void SwitchLibrary(AppSettings settings, LibraryDefinition requested, string previousDirectory)
	{
		MainWindow mainWindow = base.MainWindow as MainWindow;
		ThumbnailService thumbnails = _thumbnails;
		ThumbnailService thumbnailService = null;
		mainWindow?.PrepareForLibrarySwitch();
		try
		{
			Database.ConfigureLibraryDirectory(requested.Directory);
			Database.Initialize();
			thumbnailService = (ThumbnailService)(ImageItem.ThumbnailSource = new ThumbnailService(((DispatcherObject)this).Dispatcher));
			MainWindow mainWindow2 = new MainWindow(new MainViewModel(thumbnailService));
			_settings = settings;
			AppSettingsService.Save(_settings);
			_thumbnails = thumbnailService;
			base.MainWindow = mainWindow2;
			mainWindow2.Show();
			mainWindow?.CloseForLibrarySwitch();
			thumbnails?.Dispose();
		}
		catch (Exception ex)
		{
			thumbnailService?.Dispose();
			ImageItem.ThumbnailSource = thumbnails;
			Database.ConfigureLibraryDirectory(previousDirectory);
			Database.Initialize();
			mainWindow?.CancelLibrarySwitch();
			MessageBox.Show(mainWindow, "Magpie Trove could not open the library.\n\n" + requested.Directory + "\n\n" + ex.Message, "Magpie Trove", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
	}

	private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
	{
		MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message, "Magpie Trove", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		e.Handled = true;
	}

	protected override void OnExit(ExitEventArgs e)
	{
		_thumbnails?.Dispose();
		base.OnExit(e);
	}

}
