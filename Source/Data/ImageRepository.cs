using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Data.Sqlite;
using MagpieTrove.Models;
using MagpieTrove.Services;

namespace MagpieTrove.Data;

public static class ImageRepository
{
	public sealed record HashEntry(long Id, string? QuickHash, ulong? PerceptualHash);

	private const string SelectColumns = "i.id, i.path, i.file_name, i.folder, i.file_size, i.width, i.height, i.date_taken, i.date_modified, i.date_added, i.rating, i.missing, i.flag, i.camera_make, i.camera_model, i.lens, i.iso, i.aperture, i.shutter_speed, i.focal_length, i.rotation_override, (SELECT GROUP_CONCAT(COALESCE(t.color, '#4FA3E3'), '|')  FROM image_tags it JOIN tags t ON t.id = it.tag_id WHERE it.image_id = i.id)";

	public static List<ImageItem> Query(FilterQuery filter)
	{
		StringBuilder stringBuilder = new StringBuilder("SELECT i.id, i.path, i.file_name, i.folder, i.file_size, i.width, i.height, i.date_taken, i.date_modified, i.date_added, i.rating, i.missing, i.flag, i.camera_make, i.camera_model, i.lens, i.iso, i.aperture, i.shutter_speed, i.focal_length, i.rotation_override, (SELECT GROUP_CONCAT(COALESCE(t.color, '#4FA3E3'), '|')  FROM image_tags it JOIN tags t ON t.id = it.tag_id WHERE it.image_id = i.id) FROM images i WHERE i.missing = 0");
		List<(string, object)> list = new List<(string, object)>();
		if (!string.IsNullOrWhiteSpace(filter.Search))
		{
			stringBuilder.Append(" AND (i.file_name LIKE $search ESCAPE '\\' OR i.folder LIKE $search ESCAPE '\\')");
			list.Add(("$search", "%" + Escape(filter.Search.Trim()) + "%"));
		}
		if (!string.IsNullOrEmpty(filter.FolderPrefix))
		{
			stringBuilder.Append(" AND (i.folder = $folder OR i.folder LIKE $folderPrefix ESCAPE '\\')");
			list.Add(("$folder", filter.FolderPrefix));
			list.Add(("$folderPrefix", Escape(filter.FolderPrefix.TrimEnd('\\')) + "\\\\%"));
		}
		if (filter.MinRating > 0)
		{
			stringBuilder.Append(" AND i.rating >= $minRating");
			list.Add(("$minRating", filter.MinRating));
		}
		DateTime? dateFrom = filter.DateFrom;
		if (dateFrom.HasValue)
		{
			DateTime valueOrDefault = dateFrom.GetValueOrDefault();
			stringBuilder.Append(" AND COALESCE(i.date_taken, i.date_modified) >= $dateFrom");
			list.Add(("$dateFrom", Database.ToDb(valueOrDefault.Date)));
		}
		dateFrom = filter.DateTo;
		if (dateFrom.HasValue)
		{
			DateTime valueOrDefault2 = dateFrom.GetValueOrDefault();
			stringBuilder.Append(" AND COALESCE(i.date_taken, i.date_modified) < $dateToExclusive");
			list.Add(("$dateToExclusive", Database.ToDb(valueOrDefault2.Date.AddDays(1.0))));
		}
		AddTextFilter(stringBuilder, list, "i.camera_make", "$cameraMake", filter.CameraMake);
		AddTextFilter(stringBuilder, list, "i.camera_model", "$cameraModel", filter.CameraModel);
		AddTextFilter(stringBuilder, list, "i.lens", "$lens", filter.Lens);
		AddRange(stringBuilder, list, "i.iso", "$isoMin", filter.IsoMin, "$isoMax", filter.IsoMax);
		AddRange(stringBuilder, list, "i.aperture", "$apertureMin", filter.ApertureMin, "$apertureMax", filter.ApertureMax);
		AddRange(stringBuilder, list, "i.shutter_speed", "$shutterMin", filter.ShutterSpeedMin, "$shutterMax", filter.ShutterSpeedMax);
		AddRange(stringBuilder, list, "i.focal_length", "$focalMin", filter.FocalLengthMin, "$focalMax", filter.FocalLengthMax);
		switch (filter.Flags)
		{
		case FlagFilter.Picked:
			stringBuilder.Append(" AND i.flag > 0");
			break;
		case FlagFilter.Rejected:
			stringBuilder.Append(" AND i.flag < 0");
			break;
		case FlagFilter.Unflagged:
			stringBuilder.Append(" AND i.flag = 0");
			break;
		case FlagFilter.HideRejected:
			stringBuilder.Append(" AND i.flag >= 0");
			break;
		}
		long? collectionId = filter.CollectionId;
		if (collectionId.HasValue)
		{
			long valueOrDefault3 = collectionId.GetValueOrDefault();
			stringBuilder.Append(" AND EXISTS (SELECT 1 FROM collection_images ci WHERE ci.image_id = i.id AND ci.collection_id = $collectionId)");
			list.Add(("$collectionId", valueOrDefault3));
		}
		if (filter.UntaggedOnly)
		{
			stringBuilder.Append(" AND NOT EXISTS (SELECT 1 FROM image_tags t WHERE t.image_id = i.id)");
		}
		Dictionary<long, List<long>> closure = ((filter.IncludeTagIds.Count > 0 || filter.ExcludeTagIds.Count > 0) ? TagRepository.GetSubtreeClosure() : null);
		if (filter.IncludeTagIds.Count > 0)
		{
			if (filter.MatchAll)
			{
				foreach (long includeTagId in filter.IncludeTagIds)
				{
					stringBuilder.Append(" AND EXISTS (SELECT 1 FROM image_tags t WHERE t.image_id = i.id AND t.tag_id IN (" + InList(Subtree(includeTagId)) + "))");
				}
			}
			else
			{
				IEnumerable<long> ids = filter.IncludeTagIds.SelectMany(Subtree).Distinct();
				stringBuilder.Append(" AND EXISTS (SELECT 1 FROM image_tags t WHERE t.image_id = i.id AND t.tag_id IN (" + InList(ids) + "))");
			}
		}
		if (filter.ExcludeTagIds.Count > 0)
		{
			IEnumerable<long> ids2 = filter.ExcludeTagIds.SelectMany(Subtree).Distinct();
			stringBuilder.Append(" AND NOT EXISTS (SELECT 1 FROM image_tags t WHERE t.image_id = i.id AND t.tag_id IN (" + InList(ids2) + "))");
		}
		stringBuilder.Append(" ORDER BY ").Append(OrderBy(filter));
		using (SqliteConnection sqliteConnection = Database.Open())
		{
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = stringBuilder.ToString();
			foreach (var (parameterName, obj) in list)
			{
				sqliteCommand.Parameters.AddWithValue(parameterName, obj ?? DBNull.Value);
			}
			List<ImageItem> list2 = new List<ImageItem>();
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			while (sqliteDataReader.Read())
			{
				list2.Add(Read(sqliteDataReader));
			}
			return list2;
		}
		List<long> Subtree(long tagId)
		{
			if (closure == null || !closure.TryGetValue(tagId, out List<long> value))
			{
				int num = 1;
				List<long> list3 = new List<long>(num);
				CollectionsMarshal.SetCount(list3, num);
				Span<long> span = CollectionsMarshal.AsSpan(list3);
				int index = 0;
				span[index] = tagId;
				return list3;
			}
			return value;
		}
	}

	private static string OrderBy(FilterQuery filter)
	{
		string text = (filter.SortDescending ? "DESC" : "ASC");
		return filter.SortBy switch
		{
			SortField.FileName => "i.file_name " + text + ", i.id " + text, 
			SortField.DateTaken => "COALESCE(i.date_taken, i.date_modified) " + text + ", i.id " + text, 
			SortField.DateAdded => "i.date_added " + text + ", i.id " + text, 
			SortField.DateModified => "i.date_modified " + text + ", i.id " + text, 
			SortField.FileSize => "i.file_size " + text + ", i.id " + text, 
			SortField.Rating => "i.rating " + text + ", COALESCE(i.date_taken, i.date_modified) DESC", 
			SortField.Folder => "i.folder " + text + ", i.file_name " + text, 
			SortField.Random => "RANDOM()", 
			SortField.CameraMake => "i.camera_make " + text + ", i.camera_model " + text, 
			SortField.CameraModel => "i.camera_model " + text + ", i.id " + text, 
			SortField.Lens => "i.lens " + text + ", i.id " + text, 
			SortField.Iso => "i.iso " + text + ", i.id " + text, 
			SortField.Aperture => "i.aperture " + text + ", i.id " + text, 
			SortField.ShutterSpeed => "i.shutter_speed " + text + ", i.id " + text, 
			SortField.FocalLength => "i.focal_length " + text + ", i.id " + text, 
			_ => "i.file_name " + text, 
		};
	}

	private static string InList(IEnumerable<long> ids)
	{
		return string.Join(",", ids);
	}

	private static string Escape(string value)
	{
		return value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
	}

	private static void AddTextFilter(StringBuilder sql, List<(string, object?)> args, string column, string parameter, string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(23, 2, sql);
			handler.AppendLiteral(" AND ");
			handler.AppendFormatted(column);
			handler.AppendLiteral(" = ");
			handler.AppendFormatted(parameter);
			handler.AppendLiteral(" COLLATE NOCASE");
			sql.Append(ref handler);
			args.Add((parameter, value.Trim()));
		}
	}

	private static void AddRange<T>(StringBuilder sql, List<(string, object?)> args, string column, string minParameter, T? min, string maxParameter, T? max) where T : struct
	{
		if (min.HasValue)
		{
			StringBuilder stringBuilder = sql;
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(9, 2, stringBuilder);
			handler.AppendLiteral(" AND ");
			handler.AppendFormatted(column);
			handler.AppendLiteral(" >= ");
			handler.AppendFormatted(minParameter);
			stringBuilder2.Append(ref handler);
			args.Add((minParameter, min));
		}
		if (max.HasValue)
		{
			StringBuilder stringBuilder = sql;
			StringBuilder stringBuilder3 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(9, 2, stringBuilder);
			handler.AppendLiteral(" AND ");
			handler.AppendFormatted(column);
			handler.AppendLiteral(" <= ");
			handler.AppendFormatted(maxParameter);
			stringBuilder3.Append(ref handler);
			args.Add((maxParameter, max));
		}
	}

	public static List<string> GetDistinctValues(string column)
	{
		switch (column)
		{
		case "camera_make":
		case "camera_model":
		case "lens":
		{
			using SqliteConnection sqliteConnection = Database.Open();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = $"SELECT DISTINCT {column} FROM images WHERE {column} IS NOT NULL AND {column} <> '' ORDER BY {column} COLLATE NOCASE;";
			List<string> list = new List<string>();
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			while (sqliteDataReader.Read())
			{
				list.Add(sqliteDataReader.GetString(0));
			}
			return list;
		}
		default:
			throw new ArgumentOutOfRangeException("column");
		}
	}

	private static ImageItem Read(SqliteDataReader r)
	{
		return new ImageItem
		{
			Id = r.GetInt64(0),
			Path = r.GetString(1),
			FileName = r.GetString(2),
			Folder = r.GetString(3),
			FileSize = r.GetInt64(4),
			Width = r.GetInt32(5),
			Height = r.GetInt32(6),
			DateTaken = (r.IsDBNull(7) ? ((DateTime?)null) : Database.FromDb(r.GetString(7))),
			DateModified = (Database.FromDb(r.GetString(8)) ?? DateTime.MinValue),
			DateAdded = (Database.FromDb(r.GetString(9)) ?? DateTime.MinValue),
			Rating = r.GetInt32(10),
			IsMissing = (r.GetInt32(11) != 0),
			Flag = r.GetInt32(12),
			CameraMake = (r.IsDBNull(13) ? null : r.GetString(13)),
			CameraModel = (r.IsDBNull(14) ? null : r.GetString(14)),
			Lens = (r.IsDBNull(15) ? null : r.GetString(15)),
			Iso = (r.IsDBNull(16) ? ((int?)null) : new int?(r.GetInt32(16))),
			Aperture = (r.IsDBNull(17) ? ((double?)null) : new double?(r.GetDouble(17))),
			ShutterSpeed = (r.IsDBNull(18) ? ((double?)null) : new double?(r.GetDouble(18))),
			FocalLength = (r.IsDBNull(19) ? ((double?)null) : new double?(r.GetDouble(19))),
			RotationOverride = r.GetInt32(20),
			TagColors = (r.IsDBNull(21) ? Array.Empty<string>() : r.GetString(21).Split('|', StringSplitOptions.RemoveEmptyEntries).Distinct()
				.Take(6)
				.ToArray())
		};
	}

	public static ImageItem? GetById(long id)
	{
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "SELECT i.id, i.path, i.file_name, i.folder, i.file_size, i.width, i.height, i.date_taken, i.date_modified, i.date_added, i.rating, i.missing, i.flag, i.camera_make, i.camera_model, i.lens, i.iso, i.aperture, i.shutter_speed, i.focal_length, i.rotation_override, (SELECT GROUP_CONCAT(COALESCE(t.color, '#4FA3E3'), '|')  FROM image_tags it JOIN tags t ON t.id = it.tag_id WHERE it.image_id = i.id) FROM images i WHERE i.id = $id;";
		sqliteCommand.Parameters.AddWithValue("$id", id);
		using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
		return sqliteDataReader.Read() ? Read(sqliteDataReader) : null;
	}

	public static Dictionary<string, ScanRecord> GetIndexSnapshot()
	{
		Dictionary<string, ScanRecord> dictionary = new Dictionary<string, ScanRecord>(StringComparer.OrdinalIgnoreCase);
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "SELECT path, file_size, date_modified, missing, quick_hash, exif_scanned, keywords_scanned FROM images;";
		using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
		while (sqliteDataReader.Read())
		{
			string text = sqliteDataReader.GetString(0);
			dictionary[text] = new ScanRecord(text, sqliteDataReader.GetInt64(1), Database.FromDb(sqliteDataReader.GetString(2)) ?? DateTime.MinValue, sqliteDataReader.GetInt32(3) != 0, sqliteDataReader.IsDBNull(4) ? null : sqliteDataReader.GetString(4), sqliteDataReader.GetInt32(5) != 0, sqliteDataReader.GetInt32(6) != 0);
		}
		return dictionary;
	}

	public static void RelocateImage(SqliteConnection cn, long imageId, string newPath, string fileName, string folder, DateTime modified)
	{
		using SqliteCommand sqliteCommand = cn.CreateCommand();
		sqliteCommand.CommandText = "UPDATE images\nSET path = $path, file_name = $name, folder = $folder,\n    date_modified = $modified, missing = 0\nWHERE id = $id;";
		sqliteCommand.Parameters.AddWithValue("$path", newPath);
		sqliteCommand.Parameters.AddWithValue("$name", fileName);
		sqliteCommand.Parameters.AddWithValue("$folder", folder);
		sqliteCommand.Parameters.AddWithValue("$modified", Database.ToDb(modified));
		sqliteCommand.Parameters.AddWithValue("$id", imageId);
		sqliteCommand.ExecuteNonQuery();
	}

	public static long? GetIdByPath(SqliteConnection cn, string path)
	{
		using SqliteCommand sqliteCommand = cn.CreateCommand();
		sqliteCommand.CommandText = "SELECT id FROM images WHERE path = $p;";
		sqliteCommand.Parameters.AddWithValue("$p", path);
		return (sqliteCommand.ExecuteScalar() is long value) ? new long?(value) : ((long?)null);
	}

	public static void Upsert(SqliteConnection cn, string path, string fileName, string folder, long size, ImageMetadata metadata, DateTime modified, string? quickHash = null, bool importKeywords = false)
	{
		using SqliteCommand sqliteCommand = cn.CreateCommand();
		sqliteCommand.CommandText = "INSERT INTO images (path, file_name, folder, file_size, width, height,\n                    date_taken, date_modified, date_added, rating, missing, quick_hash,\n                    camera_make, camera_model, lens, iso, aperture, shutter_speed,\n                    focal_length, exif_scanned, keywords_scanned)\nVALUES ($path, $name, $folder, $size, $w, $h, $taken, $modified, $added, 0, 0, $hash,\n        $make, $model, $lens, $iso, $aperture, $shutter, $focal, 1, $keywordsScanned)\nON CONFLICT(path) DO UPDATE SET\n    file_name     = excluded.file_name,\n    folder        = excluded.folder,\n    file_size     = excluded.file_size,\n    width         = excluded.width,\n    height        = excluded.height,\n    date_taken    = excluded.date_taken,\n    camera_make   = excluded.camera_make,\n    camera_model  = excluded.camera_model,\n    lens          = excluded.lens,\n    iso           = excluded.iso,\n    aperture      = excluded.aperture,\n    shutter_speed = excluded.shutter_speed,\n    focal_length  = excluded.focal_length,\n    exif_scanned  = 1,\n    keywords_scanned = CASE WHEN excluded.keywords_scanned = 1 THEN 1 ELSE images.keywords_scanned END,\n    date_modified = excluded.date_modified,\n    quick_hash    = COALESCE(excluded.quick_hash, images.quick_hash),\n    missing       = 0;";
		sqliteCommand.Parameters.AddWithValue("$hash", ((object)quickHash) ?? ((object)DBNull.Value));
		sqliteCommand.Parameters.AddWithValue("$path", path);
		sqliteCommand.Parameters.AddWithValue("$name", fileName);
		sqliteCommand.Parameters.AddWithValue("$folder", folder);
		sqliteCommand.Parameters.AddWithValue("$size", size);
		sqliteCommand.Parameters.AddWithValue("$w", metadata.Width);
		sqliteCommand.Parameters.AddWithValue("$h", metadata.Height);
		sqliteCommand.Parameters.AddWithValue("$taken", metadata.DateTaken.HasValue ? ((IConvertible)Database.ToDb(metadata.DateTaken.Value)) : ((IConvertible)DBNull.Value));
		sqliteCommand.Parameters.AddWithValue("$make", ((object)metadata.CameraMake) ?? ((object)DBNull.Value));
		sqliteCommand.Parameters.AddWithValue("$model", ((object)metadata.CameraModel) ?? ((object)DBNull.Value));
		sqliteCommand.Parameters.AddWithValue("$lens", ((object)metadata.Lens) ?? ((object)DBNull.Value));
		sqliteCommand.Parameters.AddWithValue("$iso", ((object)metadata.Iso) ?? DBNull.Value);
		sqliteCommand.Parameters.AddWithValue("$aperture", ((object)metadata.Aperture) ?? DBNull.Value);
		sqliteCommand.Parameters.AddWithValue("$shutter", ((object)metadata.ShutterSpeed) ?? DBNull.Value);
		sqliteCommand.Parameters.AddWithValue("$focal", ((object)metadata.FocalLength) ?? DBNull.Value);
		sqliteCommand.Parameters.AddWithValue("$keywordsScanned", importKeywords ? 1 : 0);
		sqliteCommand.Parameters.AddWithValue("$modified", Database.ToDb(modified));
		sqliteCommand.Parameters.AddWithValue("$added", Database.ToDb(DateTime.Now));
		sqliteCommand.ExecuteNonQuery();
	}

	public static int MarkMissing(IEnumerable<string> paths)
	{
		List<string> list = paths.ToList();
		if (list.Count == 0)
		{
			return 0;
		}
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteTransaction sqliteTransaction = sqliteConnection.BeginTransaction();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.Transaction = sqliteTransaction;
		sqliteCommand.CommandText = "UPDATE images SET missing = 1 WHERE path = $p;";
		SqliteParameter sqliteParameter = sqliteCommand.Parameters.Add("$p", SqliteType.Text);
		foreach (string item in list)
		{
			sqliteParameter.Value = item;
			sqliteCommand.ExecuteNonQuery();
		}
		sqliteTransaction.Commit();
		return list.Count;
	}

	public static void SetFlag(IEnumerable<long> imageIds, int flag)
	{
		List<long> list = imageIds.ToList();
		if (list.Count == 0)
		{
			return;
		}
		using SqliteConnection cn = Database.Open();
		Database.Exec(cn, "UPDATE images SET flag = $f WHERE id IN (" + InList(list) + ");", ("$f", flag));
	}

	public static void SetRating(IEnumerable<long> imageIds, int rating)
	{
		List<long> list = imageIds.ToList();
		if (list.Count == 0)
		{
			return;
		}
		using SqliteConnection cn = Database.Open();
		Database.Exec(cn, "UPDATE images SET rating = $r WHERE id IN (" + InList(list) + ");", ("$r", rating));
	}

	public static void SetRotationOverride(long imageId, int degrees)
	{
		degrees = (degrees % 360 + 360) % 360;
		bool flag;
		switch (degrees)
		{
		case 0:
		case 90:
		case 180:
		case 270:
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			throw new ArgumentOutOfRangeException("degrees");
		}
		using SqliteConnection cn = Database.Open();
		Database.Exec(cn, "UPDATE images SET rotation_override=$r WHERE id=$id;", ("$r", degrees), ("$id", imageId));
	}

	public static void Remove(IEnumerable<long> imageIds)
	{
		List<long> list = imageIds.ToList();
		if (list.Count == 0)
		{
			return;
		}
		using SqliteConnection cn = Database.Open();
		Database.Exec(cn, "DELETE FROM images WHERE id IN (" + InList(list) + ");");
	}

	public static LibrarySnapshot CaptureSnapshot(IEnumerable<long> imageIds)
	{
		List<long> list = imageIds.ToList();
		LibrarySnapshot librarySnapshot = new LibrarySnapshot
		{
			ImageIds = list
		};
		if (list.Count == 0)
		{
			return librarySnapshot;
		}
		string text = InList(list);
		using SqliteConnection sqliteConnection = Database.Open();
		using (SqliteCommand sqliteCommand = sqliteConnection.CreateCommand())
		{
			sqliteCommand.CommandText = $"SELECT id, path, file_name, folder, file_size, width, height, date_taken, date_modified, date_added, rating, missing, quick_hash, phash, flag, camera_make, camera_model, lens, iso, aperture, shutter_speed, focal_length, exif_scanned, rotation_override, keywords_scanned FROM images WHERE id IN ({text});";
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			while (sqliteDataReader.Read())
			{
				object[] array = new object[sqliteDataReader.FieldCount];
				for (int i = 0; i < array.Length; i++)
				{
					array[i] = (sqliteDataReader.IsDBNull(i) ? null : sqliteDataReader.GetValue(i));
				}
				librarySnapshot.Rows.Add(array);
			}
		}
		using (SqliteCommand sqliteCommand2 = sqliteConnection.CreateCommand())
		{
			sqliteCommand2.CommandText = "SELECT image_id, tag_id FROM image_tags WHERE image_id IN (" + text + ");";
			using SqliteDataReader sqliteDataReader2 = sqliteCommand2.ExecuteReader();
			while (sqliteDataReader2.Read())
			{
				librarySnapshot.TagLinks.Add((sqliteDataReader2.GetInt64(0), sqliteDataReader2.GetInt64(1)));
			}
		}
		using (SqliteCommand sqliteCommand3 = sqliteConnection.CreateCommand())
		{
			sqliteCommand3.CommandText = "SELECT collection_id, image_id, sort_order FROM collection_images WHERE image_id IN (" + text + ");";
			using SqliteDataReader sqliteDataReader3 = sqliteCommand3.ExecuteReader();
			while (sqliteDataReader3.Read())
			{
				librarySnapshot.CollectionLinks.Add((sqliteDataReader3.GetInt64(0), sqliteDataReader3.GetInt64(1), sqliteDataReader3.GetInt64(2)));
			}
		}
		using (SqliteCommand sqliteCommand4 = sqliteConnection.CreateCommand())
		{
			sqliteCommand4.CommandText = "SELECT image_id, model, dim, vector FROM image_embeddings WHERE image_id IN (" + text + ");";
			using SqliteDataReader sqliteDataReader4 = sqliteCommand4.ExecuteReader();
			while (sqliteDataReader4.Read())
			{
				librarySnapshot.Embeddings.Add((sqliteDataReader4.GetInt64(0), sqliteDataReader4.GetString(1), sqliteDataReader4.GetInt32(2), (byte[])sqliteDataReader4["vector"]));
			}
		}
		return librarySnapshot;
	}

	public static void RestoreSnapshot(LibrarySnapshot snapshot)
	{
		if (snapshot.Rows.Count == 0)
		{
			return;
		}
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteTransaction sqliteTransaction = sqliteConnection.BeginTransaction();
		using (SqliteCommand sqliteCommand = sqliteConnection.CreateCommand())
		{
			sqliteCommand.CommandText = "INSERT OR REPLACE INTO images\n    (id, path, file_name, folder, file_size, width, height, date_taken,\n     date_modified, date_added, rating, missing, quick_hash, phash, flag,\n     camera_make, camera_model, lens, iso, aperture, shutter_speed, focal_length,\n     exif_scanned, rotation_override, keywords_scanned)\nVALUES ($id, $path, $name, $folder, $size, $w, $h, $taken, $modified, $added,\n        $rating, $missing, $hash, $phash, $flag, $make, $model, $lens, $iso,\n        $aperture, $shutter, $focal, $exifScanned, $rotation, $keywordsScanned);";
			foreach (object[] row in snapshot.Rows)
			{
				sqliteCommand.Parameters.Clear();
				string[] array = new string[25]
				{
					"$id", "$path", "$name", "$folder", "$size", "$w", "$h", "$taken", "$modified", "$added",
					"$rating", "$missing", "$hash", "$phash", "$flag", "$make", "$model", "$lens", "$iso", "$aperture",
					"$shutter", "$focal", "$exifScanned", "$rotation", "$keywordsScanned"
				};
				for (int i = 0; i < array.Length; i++)
				{
					sqliteCommand.Parameters.AddWithValue(array[i], row[i] ?? DBNull.Value);
				}
				sqliteCommand.ExecuteNonQuery();
			}
		}
		Restore(sqliteConnection, "INSERT OR IGNORE INTO image_tags(image_id, tag_id) VALUES($a, $b);", snapshot.TagLinks.Select(((long ImageId, long TagId) l) => new object[2] { l.ImageId, l.TagId }));
		Restore(sqliteConnection, "INSERT OR IGNORE INTO collection_images(collection_id, image_id, sort_order) VALUES($a, $b, $c);", snapshot.CollectionLinks.Select(((long CollectionId, long ImageId, long SortOrder) l) => new object[3] { l.CollectionId, l.ImageId, l.SortOrder }));
		Restore(sqliteConnection, "INSERT OR REPLACE INTO image_embeddings(image_id, model, dim, vector) VALUES($a, $b, $c, $d);", snapshot.Embeddings.Select<(long, string, int, byte[]), object[]>(((long ImageId, string Model, int Dim, byte[] Vector) e) => new object[4] { e.ImageId, e.Model, e.Dim, e.Vector }));
		sqliteTransaction.Commit();
	}

	private static void Restore(SqliteConnection cn, string sql, IEnumerable<object[]> rows)
	{
		using SqliteCommand sqliteCommand = cn.CreateCommand();
		sqliteCommand.CommandText = sql;
		string[] array = new string[4] { "$a", "$b", "$c", "$d" };
		foreach (object[] row in rows)
		{
			sqliteCommand.Parameters.Clear();
			for (int i = 0; i < row.Length; i++)
			{
				sqliteCommand.Parameters.AddWithValue(array[i], row[i]);
			}
			sqliteCommand.ExecuteNonQuery();
		}
	}

	public static List<ImageItem> GetByIds(IEnumerable<long> imageIds)
	{
		List<long> list = imageIds.Distinct().ToList();
		if (list.Count == 0)
		{
			return new List<ImageItem>();
		}
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = $"SELECT {"i.id, i.path, i.file_name, i.folder, i.file_size, i.width, i.height, i.date_taken, i.date_modified, i.date_added, i.rating, i.missing, i.flag, i.camera_make, i.camera_model, i.lens, i.iso, i.aperture, i.shutter_speed, i.focal_length, i.rotation_override, (SELECT GROUP_CONCAT(COALESCE(t.color, '#4FA3E3'), '|')  FROM image_tags it JOIN tags t ON t.id = it.tag_id WHERE it.image_id = i.id)"} FROM images i WHERE i.id IN ({InList(list)});";
		List<ImageItem> list2 = new List<ImageItem>();
		using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
		while (sqliteDataReader.Read())
		{
			list2.Add(Read(sqliteDataReader));
		}
		return list2;
	}

	public static List<ImageItem> GetImagesWithoutPerceptualHash()
	{
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "SELECT i.id, i.path, i.file_name, i.folder, i.file_size, i.width, i.height, i.date_taken, i.date_modified, i.date_added, i.rating, i.missing, i.flag, i.camera_make, i.camera_model, i.lens, i.iso, i.aperture, i.shutter_speed, i.focal_length, i.rotation_override, (SELECT GROUP_CONCAT(COALESCE(t.color, '#4FA3E3'), '|')  FROM image_tags it JOIN tags t ON t.id = it.tag_id WHERE it.image_id = i.id) FROM images i WHERE i.missing = 0 AND i.phash IS NULL;";
		List<ImageItem> list = new List<ImageItem>();
		using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
		while (sqliteDataReader.Read())
		{
			list.Add(Read(sqliteDataReader));
		}
		return list;
	}

	public static void SetPerceptualHashes(IEnumerable<(long Id, ulong Hash)> hashes)
	{
		List<(long, ulong)> list = hashes.ToList();
		if (list.Count == 0)
		{
			return;
		}
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteTransaction sqliteTransaction = sqliteConnection.BeginTransaction();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "UPDATE images SET phash = $h WHERE id = $id;";
		SqliteParameter sqliteParameter = sqliteCommand.Parameters.Add("$h", SqliteType.Integer);
		SqliteParameter sqliteParameter2 = sqliteCommand.Parameters.Add("$id", SqliteType.Integer);
		foreach (var item3 in list)
		{
			long item = item3.Item1;
			ulong item2 = item3.Item2;
			sqliteParameter.Value = (long)item2;
			sqliteParameter2.Value = item;
			sqliteCommand.ExecuteNonQuery();
		}
		sqliteTransaction.Commit();
	}

	public static List<HashEntry> GetHashIndex()
	{
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "SELECT id, quick_hash, phash FROM images WHERE missing = 0 ORDER BY id;";
		List<HashEntry> list = new List<HashEntry>();
		using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
		while (sqliteDataReader.Read())
		{
			list.Add(new HashEntry(sqliteDataReader.GetInt64(0), sqliteDataReader.IsDBNull(1) ? null : sqliteDataReader.GetString(1), sqliteDataReader.IsDBNull(2) ? ((ulong?)null) : new ulong?((ulong)sqliteDataReader.GetInt64(2))));
		}
		return list;
	}

	public static int CountAll()
	{
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "SELECT COUNT(*) FROM images WHERE missing = 0;";
		return Convert.ToInt32(sqliteCommand.ExecuteScalar() ?? ((object)0));
	}

	public static int CountMissing()
	{
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "SELECT COUNT(*) FROM images WHERE missing = 1;";
		return Convert.ToInt32(sqliteCommand.ExecuteScalar() ?? ((object)0));
	}

	public static void ClearMissingFlag()
	{
		using SqliteConnection cn = Database.Open();
		Database.Exec(cn, "UPDATE images SET missing = 0;");
	}
}
