using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using MagpieTrove.Data;

namespace MagpieTrove.Services;

public static class LibraryScanner
{
	private sealed class MoveReconciliation
	{
		public HashSet<string> NewPaths { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		public HashSet<string> OldPaths { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		public int Count => NewPaths.Count;
	}

	public static Task<IncrementalScanResult> ApplyChangesAsync(IReadOnlyList<LibraryFileChange> changes, CancellationToken token = default(CancellationToken))
	{
		return Task.Run(() => ApplyChanges(changes, token), token);
	}

	private static IncrementalScanResult ApplyChanges(IReadOnlyList<LibraryFileChange> changes, CancellationToken token)
	{
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		bool flag = Database.GetMeta("import_embedded_keywords") == "1";
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteTransaction sqliteTransaction = sqliteConnection.BeginTransaction();
		foreach (LibraryFileChange change in changes)
		{
			token.ThrowIfCancellationRequested();
			if (change.Kind == WatcherChangeTypes.Deleted || !File.Exists(change.Path))
			{
				using (SqliteCommand sqliteCommand = sqliteConnection.CreateCommand())
				{
					sqliteCommand.CommandText = "UPDATE images SET missing = 1 WHERE path = $path;";
					sqliteCommand.Parameters.AddWithValue("$path", change.Path);
					num2 += sqliteCommand.ExecuteNonQuery();
				}
				continue;
			}
			try
			{
				FileInfo fileInfo = new FileInfo(change.Path);
				ImageMetadata imageMetadata = ImageFileInfo.Read(change.Path);
				if ((object)imageMetadata == null)
				{
					num3++;
					continue;
				}
				if (change.Kind == WatcherChangeTypes.Renamed)
				{
					string oldPath = change.OldPath;
					if (oldPath != null)
					{
						long? idByPath = ImageRepository.GetIdByPath(sqliteConnection, oldPath);
						if (idByPath.HasValue)
						{
							long valueOrDefault = idByPath.GetValueOrDefault();
							ImageRepository.RelocateImage(sqliteConnection, valueOrDefault, change.Path, fileInfo.Name, fileInfo.DirectoryName ?? "", fileInfo.LastWriteTime);
						}
					}
				}
				string quickHash = ImageFileInfo.ComputeQuickHash(change.Path, fileInfo.Length);
				ImageRepository.Upsert(sqliteConnection, change.Path, fileInfo.Name, fileInfo.DirectoryName ?? "", fileInfo.Length, imageMetadata, fileInfo.LastWriteTime, quickHash, flag);
				if (flag)
				{
					long? idByPath = ImageRepository.GetIdByPath(sqliteConnection, change.Path);
					if (idByPath.HasValue)
					{
						long valueOrDefault2 = idByPath.GetValueOrDefault();
						TagRepository.AddTagsToImage(sqliteConnection, valueOrDefault2, imageMetadata.Keywords);
					}
				}
				num++;
			}
			catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException) ? 1 : 0) != 0)
			{
				num3++;
			}
		}
		sqliteTransaction.Commit();
		return new IncrementalScanResult(num, num2, num3);
	}

	public static async Task<ScanResult> ScanAsync(IReadOnlyList<string> roots, IProgress<ScanProgress>? progress, CancellationToken token)
	{
		return await Task.Run(() => Scan(roots, progress, token), token).ConfigureAwait(continueOnCapturedContext: false);
	}

	private static ScanResult Scan(IReadOnlyList<string> roots, IProgress<ScanProgress>? progress, CancellationToken token)
	{
		progress?.Report(new ScanProgress("Enumerating files…", 0, 0));
		bool flag = Database.GetMeta("import_embedded_keywords") == "1";
		Dictionary<string, ScanRecord> known = ImageRepository.GetIndexSnapshot();
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<string> files = new List<string>();
		List<string> availableRoots = new List<string>();
		int num = 0;
		foreach (string root in roots)
		{
			token.ThrowIfCancellationRequested();
			bool flag2 = IsRootOffline(root);
			FolderRepository.SetOffline(root, flag2);
			if (flag2)
			{
				num++;
				continue;
			}
			availableRoots.Add(root);
			if (Directory.Exists(root))
			{
				files.AddRange(EnumerateImages(root, token));
			}
		}
		List<string> unknownFiles = files.Where((string f) => !known.ContainsKey(f)).ToList();
		List<ScanRecord> vanished = (from r in known.Values
			where !files.Contains<string>(r.Path, StringComparer.OrdinalIgnoreCase)
			where availableRoots.Any((string root) => IsUnder(r.Path, root))
			select r).ToList();
		MoveReconciliation relocated = ReconcileMoves(unknownFiles, vanished, token);
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		int num5 = 0;
		int count = files.Count;
		using SqliteConnection sqliteConnection = Database.Open();
		SqliteTransaction sqliteTransaction = sqliteConnection.BeginTransaction();
		int num6 = 0;
		try
		{
			for (int num7 = 0; num7 < files.Count; num7++)
			{
				token.ThrowIfCancellationRequested();
				string text = files[num7];
				seen.Add(text);
				if (relocated.NewPaths.Contains(text))
				{
					continue;
				}
				FileInfo fileInfo;
				try
				{
					fileInfo = new FileInfo(text);
				}
				catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
				{
					num5++;
					continue;
				}
				DateTime lastWriteTime = fileInfo.LastWriteTime;
				if (known.TryGetValue(text, out ScanRecord value) && !value.IsMissing && value.ExifScanned && (!flag || value.KeywordsScanned) && value.FileSize == fileInfo.Length && Math.Abs((value.DateModified - lastWriteTime).TotalSeconds) < 1.5)
				{
					num4++;
				}
				else
				{
					ImageMetadata imageMetadata = ImageFileInfo.Read(text);
					if ((object)imageMetadata == null)
					{
						num5++;
					}
					else
					{
						string quickHash = ImageFileInfo.ComputeQuickHash(text, fileInfo.Length);
						ImageRepository.Upsert(sqliteConnection, text, fileInfo.Name, fileInfo.DirectoryName ?? "", fileInfo.Length, imageMetadata, lastWriteTime, quickHash, flag);
						if (flag)
						{
							long? idByPath = ImageRepository.GetIdByPath(sqliteConnection, text);
							if (idByPath.HasValue)
							{
								long valueOrDefault = idByPath.GetValueOrDefault();
								TagRepository.AddTagsToImage(sqliteConnection, valueOrDefault, imageMetadata.Keywords);
							}
						}
						if ((object)value == null)
						{
							num2++;
						}
						else
						{
							num3++;
						}
						num6++;
					}
				}
				if (num6 >= 500)
				{
					sqliteTransaction.Commit();
					sqliteTransaction.Dispose();
					sqliteTransaction = sqliteConnection.BeginTransaction();
					num6 = 0;
				}
				if (num7 % 50 == 0 || num7 == files.Count - 1)
				{
					progress?.Report(new ScanProgress("Indexing " + fileInfo.Name, num7 + 1, count));
				}
			}
			sqliteTransaction.Commit();
		}
		finally
		{
			sqliteTransaction.Dispose();
		}
		int markedMissing = ImageRepository.MarkMissing((from p in known.Keys
			where !seen.Contains(p) && !relocated.OldPaths.Contains(p)
			where availableRoots.Any((string r) => IsUnder(p, r))
			select p).ToList());
		progress?.Report(new ScanProgress("Done", count, count));
		return new ScanResult(num2, num3, num4, num5, markedMissing, relocated.Count, num);
	}

	private static MoveReconciliation ReconcileMoves(List<string> unknownFiles, List<ScanRecord> vanished, CancellationToken token)
	{
		MoveReconciliation moveReconciliation = new MoveReconciliation();
		if (unknownFiles.Count == 0 || vanished.Count == 0)
		{
			return moveReconciliation;
		}
		Dictionary<long, List<ScanRecord>> dictionary = (from r in vanished
			group r by r.FileSize).ToDictionary((IGrouping<long, ScanRecord> g) => g.Key, (IGrouping<long, ScanRecord> g) => g.ToList());
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteTransaction sqliteTransaction = sqliteConnection.BeginTransaction();
		foreach (string unknownFile in unknownFiles)
		{
			token.ThrowIfCancellationRequested();
			FileInfo fileInfo;
			try
			{
				fileInfo = new FileInfo(unknownFile);
			}
			catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
			{
				continue;
			}
			if (!dictionary.TryGetValue(fileInfo.Length, out var value) || value.Count == 0)
			{
				continue;
			}
			string text = ImageFileInfo.ComputeQuickHash(unknownFile, fileInfo.Length);
			if (text == null)
			{
				continue;
			}
			ScanRecord scanRecord = null;
			foreach (ScanRecord item in value)
			{
				if (!((item.QuickHash ?? ImageFileInfo.ComputeQuickHash(item.Path, item.FileSize)) != text))
				{
					scanRecord = item;
					break;
				}
			}
			if ((object)scanRecord != null)
			{
				long? idByPath = ImageRepository.GetIdByPath(sqliteConnection, scanRecord.Path);
				if (idByPath.HasValue)
				{
					ImageRepository.RelocateImage(sqliteConnection, idByPath.Value, unknownFile, fileInfo.Name, fileInfo.DirectoryName ?? "", fileInfo.LastWriteTime);
					value.Remove(scanRecord);
					moveReconciliation.NewPaths.Add(unknownFile);
					moveReconciliation.OldPaths.Add(scanRecord.Path);
				}
			}
		}
		sqliteTransaction.Commit();
		return moveReconciliation;
	}

	private static IEnumerable<string> EnumerateImages(string root, CancellationToken token)
	{
		EnumerationOptions enumerationOptions = new EnumerationOptions
		{
			RecurseSubdirectories = true,
			IgnoreInaccessible = true,
			AttributesToSkip = (FileAttributes.Hidden | FileAttributes.System),
			ReturnSpecialDirectories = false
		};
		IEnumerable<string> enumerable;
		try
		{
			enumerable = Directory.EnumerateFiles(root, "*", enumerationOptions);
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			yield break;
		}
		foreach (string item in enumerable)
		{
			token.ThrowIfCancellationRequested();
			if (ImageFileInfo.IsSupported(item))
			{
				yield return item;
			}
		}
	}

	private static bool IsUnder(string path, string root)
	{
		string text = root.TrimEnd(new char[2] { '\\', '/' });
		if (!path.StartsWith(text + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
		{
			return path.Equals(text, StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	internal static bool IsRootOffline(string root)
	{
		if (Directory.Exists(root))
		{
			return false;
		}
		if (root.StartsWith("\\\\", StringComparison.Ordinal))
		{
			return true;
		}
		try
		{
			string pathRoot = Path.GetPathRoot(Path.GetFullPath(root));
			if (string.IsNullOrEmpty(pathRoot))
			{
				return true;
			}
			return !new DriveInfo(pathRoot).IsReady;
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException) ? 1 : 0) != 0)
		{
			return true;
		}
	}
}
