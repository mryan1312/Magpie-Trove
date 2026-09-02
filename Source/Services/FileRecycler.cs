using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace MagpieTrove.Services;

/// <summary>
/// Deletes files to the Recycle Bin rather than unlinking them, so a mistake is
/// recoverable outside the application. Nothing here deletes permanently.
/// </summary>
public static class FileRecycler
{
	public sealed record Result(List<long> RecycledIds, List<long> AlreadyGoneIds, List<(string Path, string Reason)> Failures)
	{
		public int Removed => RecycledIds.Count + AlreadyGoneIds.Count;
	}

	/// <summary>
	/// Sends each file to the Recycle Bin. Files that have already disappeared
	/// count as done — the library entry is stale either way. Anything that
	/// genuinely fails is reported and its id withheld, so the caller can leave
	/// it in the library rather than losing track of it.
	/// </summary>
	public static Result Recycle(IEnumerable<(long Id, string Path)> files)
	{
		List<long> recycled = new List<long>();
		List<long> alreadyGone = new List<long>();
		List<(string, string)> failures = new List<(string, string)>();

		foreach ((long id, string path) in files)
		{
			try
			{
				if (!File.Exists(path))
				{
					alreadyGone.Add(id);
					continue;
				}
				FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
				recycled.Add(id);
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is OperationCanceledException)
			{
				failures.Add((path, ex.Message));
			}
		}

		return new Result(recycled, alreadyGone, failures);
	}

	/// <summary>Total size on disk of the files that still exist.</summary>
	public static long TotalSize(IEnumerable<string> paths)
	{
		long total = 0L;
		foreach (string path in paths)
		{
			try
			{
				FileInfo info = new FileInfo(path);
				if (info.Exists)
				{
					total += info.Length;
				}
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
			}
		}
		return total;
	}
}
