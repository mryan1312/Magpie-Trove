using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MagpieTrove.Services;

public static class AppSettingsService
{
	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	public static string SharedDataDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MagpieTrove");

	public static string SettingsPath => Path.Combine(SharedDataDirectory, "settings.json");

	public static AppSettings Load()
	{
		return LoadFrom(SettingsPath);
	}

	internal static AppSettings LoadFrom(string path)
	{
		AppSettings settings = null;
		try
		{
			if (File.Exists(path))
			{
				settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
			}
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is JsonException) ? 1 : 0) != 0)
		{
		}
		return Normalize(settings);
	}

	public static void Save(AppSettings settings)
	{
		SaveTo(SettingsPath, settings);
	}

	internal static void SaveTo(string path, AppSettings settings)
	{
		settings = Normalize(settings);
		string directoryName = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		string text = path + ".tmp";
		File.WriteAllText(text, JsonSerializer.Serialize(settings, JsonOptions));
		File.Move(text, path, overwrite: true);
	}

	internal static AppSettings Normalize(AppSettings? settings)
	{
		if (settings == null)
		{
			settings = new AppSettings();
		}
		if (string.IsNullOrWhiteSpace(settings.DefaultLibraryRoot))
		{
			settings.DefaultLibraryRoot = Path.Combine(SharedDataDirectory, "Libraries");
		}
		settings.DefaultLibraryRoot = NormalizeDirectory(settings.DefaultLibraryRoot);
		if (string.IsNullOrWhiteSpace(settings.ModelDirectory))
		{
			settings.ModelDirectory = Path.Combine(SharedDataDirectory, "models");
		}
		settings.ModelDirectory = NormalizeDirectory(settings.ModelDirectory);
		settings.Libraries = (from l in settings.Libraries
			where !string.IsNullOrWhiteSpace(l.Directory)
			select l with
			{
				Name = (string.IsNullOrWhiteSpace(l.Name) ? "Library" : l.Name.Trim()),
				Directory = NormalizeDirectory(l.Directory)
			}).DistinctBy<LibraryDefinition, string>((LibraryDefinition l) => l.Directory, StringComparer.OrdinalIgnoreCase).ToList();
		if (settings.Libraries.Count == 0)
		{
			LibraryDefinition libraryDefinition = new LibraryDefinition(Guid.NewGuid(), "Default", SharedDataDirectory);
			settings.Libraries.Add(libraryDefinition);
			settings.CurrentLibraryId = libraryDefinition.Id;
		}
		else if (settings.Libraries.All((LibraryDefinition l) => l.Id != settings.CurrentLibraryId))
		{
			settings.CurrentLibraryId = settings.Libraries[0].Id;
		}
		return settings;
	}

	private static string NormalizeDirectory(string path)
	{
		return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
	}
}
