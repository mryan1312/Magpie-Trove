using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace MagpieTrove.Services;

/// <summary>
/// One-time moves for data written while the application was called Taggr.
/// Every step is best-effort: if one fails the app still starts, worst case with
/// an empty library the user can re-add. Delete this class once no installation
/// can still be carrying the old layout.
/// </summary>
public static class LegacyMigration
{
	private const string LegacyDirectoryName = "Taggr";

	private const string LegacyDatabaseName = "taggr.db";

	private static string LegacySharedDirectory => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		LegacyDirectoryName);

	/// <summary>
	/// Moves %LOCALAPPDATA%\Taggr to %LOCALAPPDATA%\MagpieTrove and repoints the
	/// absolute paths recorded inside settings.json. Must run before any settings
	/// are read.
	/// </summary>
	public static void MoveSharedDataDirectory()
	{
		string legacy = LegacySharedDirectory;
		string current = AppSettingsService.SharedDataDirectory;

		// Only ever migrate into a clean slate, so a second run cannot clobber
		// data the user has since created under the new name.
		if (!Directory.Exists(legacy) || Directory.Exists(current))
		{
			return;
		}

		try
		{
			Directory.Move(legacy, current);
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			return;
		}

		RepointSettings(legacy, current);
	}

	/// <summary>
	/// The default library lives directly in the shared data directory, and the
	/// model and library-root paths hang off it, so those absolute paths all
	/// still point at the old folder after the move.
	/// </summary>
	private static void RepointSettings(string legacy, string current)
	{
		try
		{
			AppSettings settings = AppSettingsService.Load();
			settings.DefaultLibraryRoot = Repoint(settings.DefaultLibraryRoot, legacy, current);
			settings.ModelDirectory = Repoint(settings.ModelDirectory, legacy, current);
			settings.Libraries = settings.Libraries
				.Select(library => library with { Directory = Repoint(library.Directory, legacy, current) })
				.ToList();
			AppSettingsService.Save(settings);
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
		}
	}

	private static string Repoint(string path, string legacy, string current)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return path;
		}
		if (string.Equals(path, legacy, StringComparison.OrdinalIgnoreCase))
		{
			return current;
		}
		if (path.StartsWith(legacy + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
		{
			return Path.Combine(current, path.Substring(legacy.Length + 1));
		}
		return path;
	}

	/// <summary>
	/// Renames a library's taggr.db to the current name. Libraries can live
	/// anywhere on disk, so this runs per library rather than as a single sweep.
	/// Call with no connection open against either file.
	/// </summary>
	internal static void RenameDatabaseFile(string databasePath)
	{
		string? directory = Path.GetDirectoryName(databasePath);
		if (string.IsNullOrEmpty(directory))
		{
			return;
		}

		string legacy = Path.Combine(directory, LegacyDatabaseName);
		if (File.Exists(databasePath) || !File.Exists(legacy))
		{
			return;
		}

		try
		{
			// Fold the write-ahead log back into the database first. Moving the
			// three files separately risks losing committed transactions that are
			// still only in the log, whereas after a truncating checkpoint the
			// sidecars hold nothing worth keeping.
			Checkpoint(legacy);
			File.Move(legacy, databasePath);

			foreach (string suffix in new[] { "-wal", "-shm" })
			{
				if (File.Exists(legacy + suffix))
				{
					File.Delete(legacy + suffix);
				}
			}
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SqliteException)
		{
		}
	}

	private static void Checkpoint(string databasePath)
	{
		string connectionString = new SqliteConnectionStringBuilder
		{
			DataSource = databasePath,
			Mode = SqliteOpenMode.ReadWrite,
			Pooling = false
		}.ToString();

		using (SqliteConnection cn = new SqliteConnection(connectionString))
		{
			cn.Open();
			using SqliteCommand command = cn.CreateCommand();
			command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
			command.ExecuteNonQuery();
		}

		// Release the handle before the file is moved.
		SqliteConnection.ClearAllPools();
	}
}
