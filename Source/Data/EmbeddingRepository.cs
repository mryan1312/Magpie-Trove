using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using MagpieTrove.Models;

namespace MagpieTrove.Data;

public static class EmbeddingRepository
{
	public static void Upsert(SqliteConnection cn, long imageId, string model, float[] vector)
	{
		using SqliteCommand sqliteCommand = cn.CreateCommand();
		sqliteCommand.CommandText = "INSERT INTO image_embeddings (image_id, model, dim, vector)\nVALUES ($id, $model, $dim, $vec)\nON CONFLICT(image_id) DO UPDATE SET\n    model  = excluded.model,\n    dim    = excluded.dim,\n    vector = excluded.vector;";
		sqliteCommand.Parameters.AddWithValue("$id", imageId);
		sqliteCommand.Parameters.AddWithValue("$model", model);
		sqliteCommand.Parameters.AddWithValue("$dim", vector.Length);
		sqliteCommand.Parameters.AddWithValue("$vec", ToBlob(vector));
		sqliteCommand.ExecuteNonQuery();
	}

	public static List<ImageItem> GetPending(string model, int limit)
	{
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "SELECT i.id, i.path, i.file_name, i.folder, i.file_size, i.width, i.height,\n       i.date_taken, i.date_modified, i.date_added, i.rating, i.missing\nFROM images i\nLEFT JOIN image_embeddings e ON e.image_id = i.id AND e.model = $model\nWHERE i.missing = 0 AND e.image_id IS NULL\nORDER BY i.date_added DESC, i.id DESC\nLIMIT $limit;";
		sqliteCommand.Parameters.AddWithValue("$model", model);
		sqliteCommand.Parameters.AddWithValue("$limit", limit);
		List<ImageItem> list = new List<ImageItem>();
		using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
		while (sqliteDataReader.Read())
		{
			list.Add(new ImageItem
			{
				Id = sqliteDataReader.GetInt64(0),
				Path = sqliteDataReader.GetString(1),
				FileName = sqliteDataReader.GetString(2),
				Folder = sqliteDataReader.GetString(3),
				FileSize = sqliteDataReader.GetInt64(4),
				Width = sqliteDataReader.GetInt32(5),
				Height = sqliteDataReader.GetInt32(6),
				DateTaken = (sqliteDataReader.IsDBNull(7) ? ((DateTime?)null) : Database.FromDb(sqliteDataReader.GetString(7))),
				DateModified = (Database.FromDb(sqliteDataReader.GetString(8)) ?? DateTime.MinValue),
				DateAdded = (Database.FromDb(sqliteDataReader.GetString(9)) ?? DateTime.MinValue),
				Rating = sqliteDataReader.GetInt32(10),
				IsMissing = (sqliteDataReader.GetInt32(11) != 0)
			});
		}
		return list;
	}

	public static VectorSet LoadAll(string model)
	{
		using SqliteConnection sqliteConnection = Database.Open();
		int @int;
		int int2;
		using (SqliteCommand sqliteCommand = sqliteConnection.CreateCommand())
		{
			sqliteCommand.CommandText = "SELECT COUNT(*), COALESCE(MAX(e.dim), 0)\nFROM image_embeddings e\nJOIN images i ON i.id = e.image_id AND i.missing = 0\nWHERE e.model = $model;";
			sqliteCommand.Parameters.AddWithValue("$model", model);
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			sqliteDataReader.Read();
			@int = sqliteDataReader.GetInt32(0);
			int2 = sqliteDataReader.GetInt32(1);
		}
		if (@int == 0 || int2 == 0)
		{
			return VectorSet.Empty(int2);
		}
		long[] array = new long[@int];
		float[] array2 = new float[(long)@int * (long)int2];
		using SqliteCommand sqliteCommand2 = sqliteConnection.CreateCommand();
		sqliteCommand2.CommandText = "SELECT e.image_id, e.vector, e.dim\nFROM image_embeddings e\nJOIN images i ON i.id = e.image_id AND i.missing = 0\nWHERE e.model = $model\nORDER BY e.image_id;";
		sqliteCommand2.Parameters.AddWithValue("$model", model);
		int num = 0;
		using SqliteDataReader sqliteDataReader2 = sqliteCommand2.ExecuteReader();
		while (sqliteDataReader2.Read() && num < @int)
		{
			if (sqliteDataReader2.GetInt32(2) == int2)
			{
				array[num] = sqliteDataReader2.GetInt64(0);
				MemoryMarshal.Cast<byte, float>((Span<byte>)(byte[])sqliteDataReader2["vector"]).CopyTo(array2.AsSpan(num * int2, int2));
				num++;
			}
		}
		if (num == @int)
		{
			return new VectorSet
			{
				ImageIds = array,
				Data = array2,
				Dimensions = int2
			};
		}
		return new VectorSet
		{
			ImageIds = array[..num],
			Data = array2[..(num * int2)],
			Dimensions = int2
		};
	}

	public static Dictionary<long, float[]> LoadFor(string model, IReadOnlyList<long> imageIds)
	{
		Dictionary<long, float[]> dictionary = new Dictionary<long, float[]>();
		if (imageIds.Count == 0)
		{
			return dictionary;
		}
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "SELECT image_id, vector, dim FROM image_embeddings WHERE model = $model AND image_id IN (" + string.Join(",", imageIds) + ");";
		sqliteCommand.Parameters.AddWithValue("$model", model);
		using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
		while (sqliteDataReader.Read())
		{
			byte[] array = (byte[])sqliteDataReader["vector"];
			dictionary[sqliteDataReader.GetInt64(0)] = MemoryMarshal.Cast<byte, float>((Span<byte>)array).ToArray();
		}
		return dictionary;
	}

	public static (int Embedded, int Pending) GetCoverage(string model)
	{
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "SELECT\n    (SELECT COUNT(*) FROM image_embeddings e\n     JOIN images i ON i.id = e.image_id AND i.missing = 0\n     WHERE e.model = $model),\n    (SELECT COUNT(*) FROM images i\n     LEFT JOIN image_embeddings e ON e.image_id = i.id AND e.model = $model\n     WHERE i.missing = 0 AND e.image_id IS NULL);";
		sqliteCommand.Parameters.AddWithValue("$model", model);
		using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
		sqliteDataReader.Read();
		return (Embedded: sqliteDataReader.GetInt32(0), Pending: sqliteDataReader.GetInt32(1));
	}

	public static void DeleteAll()
	{
		using SqliteConnection cn = Database.Open();
		Database.Exec(cn, "DELETE FROM image_embeddings;");
		Database.Exec(cn, "DELETE FROM tag_probes;");
	}

	private static byte[] ToBlob(float[] vector)
	{
		return MemoryMarshal.AsBytes(vector.AsSpan()).ToArray();
	}

	public static float[] FromBlob(byte[] blob)
	{
		return MemoryMarshal.Cast<byte, float>((Span<byte>)blob).ToArray();
	}
}
