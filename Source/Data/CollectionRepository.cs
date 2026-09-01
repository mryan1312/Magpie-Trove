using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MagpieTrove.Models;

namespace MagpieTrove.Data;

public static class CollectionRepository
{
	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = false
	};

	public static List<CollectionItem> GetAll()
	{
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "SELECT c.id, c.name, c.kind, c.rule_json, c.date_created,\n       (SELECT COUNT(*) FROM collection_images ci\n        JOIN images i ON i.id = ci.image_id AND i.missing = 0\n        WHERE ci.collection_id = c.id)\nFROM collections c\nORDER BY c.kind, c.name COLLATE NOCASE;";
		List<CollectionItem> list = new List<CollectionItem>();
		using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
		while (sqliteDataReader.Read())
		{
			CollectionKind @int = (CollectionKind)sqliteDataReader.GetInt32(2);
			FilterQuery rule = null;
			if (!sqliteDataReader.IsDBNull(3))
			{
				try
				{
					rule = JsonSerializer.Deserialize<FilterQuery>(sqliteDataReader.GetString(3), JsonOptions);
				}
				catch (JsonException)
				{
					rule = null;
				}
			}
			list.Add(new CollectionItem
			{
				Id = sqliteDataReader.GetInt64(0),
				Name = sqliteDataReader.GetString(1),
				Kind = @int,
				Rule = rule,
				DateCreated = (Database.FromDb(sqliteDataReader.GetString(4)) ?? DateTime.MinValue),
				Count = ((@int == CollectionKind.Manual) ? sqliteDataReader.GetInt32(5) : 0)
			});
		}
		return list;
	}

	public static long Create(string name, CollectionKind kind, FilterQuery? rule)
	{
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "INSERT INTO collections(name, kind, rule_json, date_created)\nVALUES($n, $k, $r, $d);\nSELECT last_insert_rowid();";
		sqliteCommand.Parameters.AddWithValue("$n", name.Trim());
		sqliteCommand.Parameters.AddWithValue("$k", (int)kind);
		sqliteCommand.Parameters.AddWithValue("$r", (rule == null) ? ((IConvertible)DBNull.Value) : ((IConvertible)JsonSerializer.Serialize(rule, JsonOptions)));
		sqliteCommand.Parameters.AddWithValue("$d", Database.ToDb(DateTime.Now));
		return Convert.ToInt64(sqliteCommand.ExecuteScalar());
	}

	public static void Rename(long id, string name)
	{
		using SqliteConnection cn = Database.Open();
		Database.Exec(cn, "UPDATE collections SET name = $n WHERE id = $id;", ("$n", name.Trim()), ("$id", id));
	}

	public static void UpdateRule(long id, FilterQuery rule)
	{
		using SqliteConnection cn = Database.Open();
		Database.Exec(cn, "UPDATE collections SET rule_json = $r WHERE id = $id;", ("$r", JsonSerializer.Serialize(rule, JsonOptions)), ("$id", id));
	}

	public static void Delete(long id)
	{
		using SqliteConnection cn = Database.Open();
		Database.Exec(cn, "DELETE FROM collections WHERE id = $id;", ("$id", id));
	}

	public static void AddImages(long collectionId, IEnumerable<long> imageIds)
	{
		List<long> list = imageIds.ToList();
		if (list.Count == 0)
		{
			return;
		}
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "SELECT COALESCE(MAX(sort_order), -1) FROM collection_images WHERE collection_id = $c;";
		sqliteCommand.Parameters.AddWithValue("$c", collectionId);
		long num = Convert.ToInt64(sqliteCommand.ExecuteScalar() ?? ((object)(-1L))) + 1;
		using SqliteTransaction sqliteTransaction = sqliteConnection.BeginTransaction();
		using SqliteCommand sqliteCommand2 = sqliteConnection.CreateCommand();
		sqliteCommand2.Transaction = sqliteTransaction;
		sqliteCommand2.CommandText = "INSERT OR IGNORE INTO collection_images(collection_id, image_id, sort_order) VALUES($c, $i, $o);";
		sqliteCommand2.Parameters.AddWithValue("$c", collectionId);
		SqliteParameter sqliteParameter = sqliteCommand2.Parameters.Add("$i", SqliteType.Integer);
		SqliteParameter sqliteParameter2 = sqliteCommand2.Parameters.Add("$o", SqliteType.Integer);
		foreach (long item in list)
		{
			sqliteParameter.Value = item;
			sqliteParameter2.Value = num++;
			sqliteCommand2.ExecuteNonQuery();
		}
		sqliteTransaction.Commit();
	}

	public static void RemoveImages(long collectionId, IEnumerable<long> imageIds)
	{
		List<long> list = imageIds.ToList();
		if (list.Count == 0)
		{
			return;
		}
		using SqliteConnection cn = Database.Open();
		Database.Exec(cn, "DELETE FROM collection_images WHERE collection_id = $c AND image_id IN (" + string.Join(",", list) + ");", ("$c", collectionId));
	}
}
