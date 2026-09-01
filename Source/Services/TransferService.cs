using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;
using MagpieTrove.Data;
using MagpieTrove.Models;

namespace MagpieTrove.Services;

public static class TransferService
{
	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	public static Task<PhotoExportResult> ExportPhotosAsync(IReadOnlyList<ImageItem> images, PhotoExportOptions options, IProgress<int>? progress = null, CancellationToken token = default(CancellationToken))
	{
		return Task.Run(() => ExportPhotos(images, options, progress, token), token);
	}

	private static PhotoExportResult ExportPhotos(IReadOnlyList<ImageItem> images, PhotoExportOptions options, IProgress<int>? progress, CancellationToken token)
	{
		Directory.CreateDirectory(options.DestinationDirectory);
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		for (int i = 0; i < images.Count; i++)
		{
			token.ThrowIfCancellationRequested();
			ImageItem imageItem = images[i];
			if (!File.Exists(imageItem.Path))
			{
				num2++;
				continue;
			}
			try
			{
				string baseName = ApplyPattern(options.FileNamePattern, imageItem, i + 1);
				string extension = OutputExtension(options.Format, imageItem.Path, options.MaxLongEdge > 0);
				string text = UniquePath(options.DestinationDirectory, baseName, extension);
				if (options.MaxLongEdge <= 0 && options.Format == ExportImageFormat.Original)
				{
					File.Copy(imageItem.Path, text);
				}
				else
				{
					Encode(imageItem.Path, text, options.MaxLongEdge, options.Format);
				}
				num++;
			}
			catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is FileFormatException || ex is ArgumentException) ? 1 : 0) != 0)
			{
				num3++;
			}
			progress?.Report(i + 1);
		}
		return new PhotoExportResult(num, num2, num3);
	}

	public static async Task ExportTagsAsync(string destinationPath, IReadOnlyCollection<long>? imageIds = null, CancellationToken token = default(CancellationToken))
	{
		List<TagTransferRecord> list = await Task.Run(() => ReadTagRecords(imageIds, token), token);
		if (Path.GetExtension(destinationPath).Equals(".csv", StringComparison.OrdinalIgnoreCase))
		{
			await File.WriteAllTextAsync(destinationPath, ToCsv(list), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), token);
		}
		else
		{
			await File.WriteAllTextAsync(destinationPath, JsonSerializer.Serialize(list, JsonOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), token);
		}
	}

	public static async Task<TagImportResult> ImportTagsAsync(string sourcePath, IProgress<int>? progress = null, CancellationToken token = default(CancellationToken))
	{
		string text = await File.ReadAllTextAsync(sourcePath, token);
		List<TagTransferRecord> records = (Path.GetExtension(sourcePath).Equals(".csv", StringComparison.OrdinalIgnoreCase) ? FromCsv(text) : (JsonSerializer.Deserialize<List<TagTransferRecord>>(text, JsonOptions) ?? new List<TagTransferRecord>()));
		return await Task.Run(() => Import(records, progress, token), token);
	}

	private static List<TagTransferRecord> ReadTagRecords(IReadOnlyCollection<long>? imageIds, CancellationToken token)
	{
		List<TagTransferRecord> list = new List<TagTransferRecord>();
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		string text = ((imageIds != null && imageIds.Count > 0) ? ("WHERE i.id IN (" + string.Join(',', imageIds) + ")") : "");
		sqliteCommand.CommandText = "WITH RECURSIVE tag_paths(id, path) AS (\n    SELECT id, name FROM tags WHERE parent_id IS NULL\n    UNION ALL\n    SELECT t.id, p.path || '/' || t.name\n    FROM tags t JOIN tag_paths p ON t.parent_id = p.id\n)\nSELECT i.path, p.path\nFROM images i\nLEFT JOIN image_tags it ON it.image_id = i.id\nLEFT JOIN tag_paths p ON p.id = it.tag_id\n" + text + "\nORDER BY i.id, p.path COLLATE NOCASE;";
		string b = null;
		TagTransferRecord tagTransferRecord = null;
		using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
		while (sqliteDataReader.Read())
		{
			token.ThrowIfCancellationRequested();
			string text2 = sqliteDataReader.GetString(0);
			if (!string.Equals(text2, b, StringComparison.OrdinalIgnoreCase))
			{
				b = text2;
				tagTransferRecord = new TagTransferRecord
				{
					Path = text2,
					ContentHash = (File.Exists(text2) ? ComputeContentHash(text2) : null)
				};
				list.Add(tagTransferRecord);
			}
			if (!sqliteDataReader.IsDBNull(1))
			{
				tagTransferRecord.Tags.Add(sqliteDataReader.GetString(1));
			}
		}
		return list;
	}

	private static TagImportResult Import(IReadOnlyList<TagTransferRecord> records, IProgress<int>? progress, CancellationToken token)
	{
		List<ImageItem> source = ImageRepository.Query(new FilterQuery
		{
			Flags = FlagFilter.All
		});
		Dictionary<string, long> byPath = source.ToDictionary<ImageItem, string, long>((ImageItem i) => Path.GetFullPath(i.Path), (ImageItem i) => i.Id, StringComparer.OrdinalIgnoreCase);
		HashSet<string> hashSet = (from r in records
			where !string.IsNullOrWhiteSpace(r.ContentHash)
			select r.ContentHash).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, List<long>> dictionary = new Dictionary<string, List<long>>(StringComparer.OrdinalIgnoreCase);
		if (records.Any((TagTransferRecord r) => !TryPath(r.Path, byPath, out var _)) && hashSet.Count > 0)
		{
			foreach (ImageItem item in source.Where((ImageItem i) => File.Exists(i.Path)))
			{
				token.ThrowIfCancellationRequested();
				string text = ComputeContentHash(item.Path);
				if (hashSet.Contains(text))
				{
					if (!dictionary.TryGetValue(text, out var value))
					{
						value = (dictionary[text] = new List<long>());
					}
					value.Add(item.Id);
				}
			}
		}
		Dictionary<string, HashSet<long>> dictionary2 = new Dictionary<string, HashSet<long>>(StringComparer.OrdinalIgnoreCase);
		int num = 0;
		int num2 = 0;
		for (int num3 = 0; num3 < records.Count; num3++)
		{
			token.ThrowIfCancellationRequested();
			TagTransferRecord tagTransferRecord = records[num3];
			IReadOnlyCollection<long> readOnlyCollection = null;
			if (TryPath(tagTransferRecord.Path, byPath, out var id))
			{
				readOnlyCollection = [id];
			}
			else
			{
				string contentHash = tagTransferRecord.ContentHash;
				if (contentHash != null && contentHash.Length > 0 && dictionary.TryGetValue(contentHash, out var value2))
				{
					readOnlyCollection = value2;
				}
			}
			if (readOnlyCollection == null || readOnlyCollection.Count == 0)
			{
				num2++;
				continue;
			}
			num++;
			foreach (string item2 in tagTransferRecord.Tags.Where((string t) => !string.IsNullOrWhiteSpace(t)))
			{
				if (!dictionary2.TryGetValue(item2, out var value3))
				{
					value3 = (dictionary2[item2] = new HashSet<long>());
				}
				foreach (long item3 in readOnlyCollection)
				{
					value3.Add(item3);
				}
			}
			progress?.Report(num3 + 1);
		}
		int num4 = 0;
		foreach (var (tagName, imageIds) in dictionary2)
		{
			TagRepository.AddTagToImages(tagName, imageIds, out List<long> newlyTagged);
			num4 += newlyTagged.Count;
		}
		return new TagImportResult(num, num2, num4);
	}

	private static bool TryPath(string path, Dictionary<string, long> byPath, out long id)
	{
		id = 0L;
		try
		{
			return byPath.TryGetValue(Path.GetFullPath(path), out id);
		}
		catch (Exception ex) when (((ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) ? 1 : 0) != 0)
		{
			return false;
		}
	}

	private static string ComputeContentHash(string path)
	{
		using FileStream source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.SequentialScan);
		return Convert.ToHexString(SHA256.HashData(source));
	}

	private static string ApplyPattern(string pattern, ImageItem image, int index)
	{
		string text = (string.IsNullOrWhiteSpace(pattern) ? "{name}" : pattern);
		text = text.Replace("{name}", Path.GetFileNameWithoutExtension(image.FileName), StringComparison.OrdinalIgnoreCase).Replace("{index}", index.ToString("D4", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase).Replace("{date}", image.EffectiveDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
		char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
		foreach (char oldChar in invalidFileNameChars)
		{
			text = text.Replace(oldChar, '_');
		}
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text.Trim();
		}
		return $"image_{index:D4}";
	}

	private static string OutputExtension(ExportImageFormat format, string source, bool reencode)
	{
		switch (format)
		{
		case ExportImageFormat.Jpeg:
			return ".jpg";
		case ExportImageFormat.Png:
			return ".png";
		default:
		{
			string extension = Path.GetExtension(source);
			if (!reencode || extension.Equals(".png", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
			{
				return extension;
			}
			return ".jpg";
		}
		}
	}

	private static string UniquePath(string directory, string baseName, string extension)
	{
		string text = Path.Combine(directory, baseName + extension);
		int num = 2;
		while (File.Exists(text))
		{
			text = Path.Combine(directory, $"{baseName}_{num}{extension}");
			num++;
		}
		return text;
	}

	private static void Encode(string source, string destination, int maxLongEdge, ExportImageFormat format)
	{
		using FileStream bitmapStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
		BitmapFrame bitmapFrame = BitmapFrame.Create(bitmapStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
		BitmapSource source2 = bitmapFrame;
		if (maxLongEdge > 0 && Math.Max(bitmapFrame.PixelWidth, bitmapFrame.PixelHeight) > maxLongEdge)
		{
			double num = (double)maxLongEdge / (double)Math.Max(bitmapFrame.PixelWidth, bitmapFrame.PixelHeight);
			source2 = new TransformedBitmap(bitmapFrame, new ScaleTransform(num, num));
		}
		BitmapEncoder bitmapEncoder = ((((format == ExportImageFormat.Original) ? FormatForExtension(Path.GetExtension(source)) : format) != ExportImageFormat.Png) ? ((BitmapEncoder)new JpegBitmapEncoder
		{
			QualityLevel = 92
		}) : ((BitmapEncoder)new PngBitmapEncoder()));
		BitmapEncoder bitmapEncoder2 = bitmapEncoder;
		bitmapEncoder2.Frames.Add(BitmapFrame.Create(source2));
		using FileStream stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
		bitmapEncoder2.Save(stream);
	}

	private static ExportImageFormat FormatForExtension(string extension)
	{
		if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
		{
			return ExportImageFormat.Jpeg;
		}
		return ExportImageFormat.Png;
	}

	private static string ToCsv(IEnumerable<TagTransferRecord> records)
	{
		StringBuilder stringBuilder = new StringBuilder("Path,ContentHash,Tag\r\n");
		foreach (TagTransferRecord record in records)
		{
			if (record.Tags.Count == 0)
			{
				AppendCsvRow(stringBuilder, record.Path, record.ContentHash ?? "", "");
				continue;
			}
			foreach (string tag in record.Tags)
			{
				AppendCsvRow(stringBuilder, record.Path, record.ContentHash ?? "", tag);
			}
		}
		return stringBuilder.ToString();
	}

	private static void AppendCsvRow(StringBuilder text, params string[] fields)
	{
		text.AppendLine(string.Join(',', fields.Select(CsvField)));
	}

	private static string CsvField(string value)
	{
		if (value.IndexOfAny(new char[4] { ',', '"', '\r', '\n' }) < 0)
		{
			return value;
		}
		return "\"" + value.Replace("\"", "\"\"") + "\"";
	}

	private static List<TagTransferRecord> FromCsv(string text)
	{
		List<List<string>> source = ParseCsv(text);
		Dictionary<(string, string), TagTransferRecord> dictionary = new Dictionary<(string, string), TagTransferRecord>();
		foreach (List<string> item in from r in source.Skip(1)
			where r.Count >= 3
			select r)
		{
			(string, string) key = (item[0], item[1]);
			if (!dictionary.TryGetValue(key, out var value))
			{
				TagTransferRecord obj = new TagTransferRecord
				{
					Path = item[0],
					ContentHash = (string.IsNullOrEmpty(item[1]) ? null : item[1])
				};
				value = obj;
				dictionary[key] = obj;
			}
			if (!string.IsNullOrWhiteSpace(item[2]))
			{
				value.Tags.Add(item[2]);
			}
		}
		return dictionary.Values.ToList();
	}

	private static List<List<string>> ParseCsv(string text)
	{
		List<List<string>> list = new List<List<string>>();
		List<string> list2 = new List<string>();
		StringBuilder stringBuilder = new StringBuilder();
		bool flag = false;
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			if (flag && c == '"' && i + 1 < text.Length && text[i + 1] == '"')
			{
				stringBuilder.Append('"');
				i++;
				continue;
			}
			switch (c)
			{
			case '"':
				flag = !flag;
				continue;
			case ',':
				if (!flag)
				{
					list2.Add(stringBuilder.ToString());
					stringBuilder.Clear();
					continue;
				}
				break;
			}
			if ((c == '\r' || c == '\n') && !flag)
			{
				if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
				{
					i++;
				}
				list2.Add(stringBuilder.ToString());
				stringBuilder.Clear();
				if (list2.Any((string v) => v.Length > 0))
				{
					list.Add(list2);
				}
				list2 = new List<string>();
			}
			else
			{
				stringBuilder.Append(c);
			}
		}
		if (stringBuilder.Length > 0 || list2.Count > 0)
		{
			list2.Add(stringBuilder.ToString());
			list.Add(list2);
		}
		return list;
	}
}
