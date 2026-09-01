using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using MagpieTrove.Models;

namespace MagpieTrove.Data;

public static class FolderRepository
{
	public static List<FolderItem> GetAll()
	{
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "SELECT id, path, offline FROM folders ORDER BY path COLLATE NOCASE;";
		List<FolderItem> list = new List<FolderItem>();
		using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
		while (sqliteDataReader.Read())
		{
			list.Add(new FolderItem
			{
				Id = sqliteDataReader.GetInt64(0),
				Path = sqliteDataReader.GetString(1),
				IsOffline = (sqliteDataReader.GetInt32(2) != 0)
			});
		}
		return list;
	}

	public static void Add(string path)
	{
		using SqliteConnection cn = Database.Open();
		Database.Exec(cn, "INSERT OR IGNORE INTO folders(path, date_added) VALUES($p, $d);", ("$p", path), ("$d", Database.ToDb(DateTime.Now)));
	}

	public static void SetOffline(string path, bool offline)
	{
		using SqliteConnection cn = Database.Open();
		Database.Exec(cn, "UPDATE folders SET offline = $o WHERE path = $p COLLATE NOCASE;", ("$o", offline ? 1 : 0), ("$p", path));
	}

	public static void Remove(long id, bool removeImages)
	{
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "SELECT path FROM folders WHERE id = $id;";
		sqliteCommand.Parameters.AddWithValue("$id", id);
		string text = sqliteCommand.ExecuteScalar() as string;
		if (removeImages && !string.IsNullOrEmpty(text))
		{
			string text2 = text.TrimEnd('\\').Replace("\\", "\\\\").Replace("%", "\\%")
				.Replace("_", "\\_");
			Database.Exec(sqliteConnection, "DELETE FROM images WHERE folder = $exact OR folder LIKE $prefix ESCAPE '\\';", ("$exact", text), ("$prefix", text2 + "\\\\%"));
		}
		Database.Exec(sqliteConnection, "DELETE FROM folders WHERE id = $id;", ("$id", id));
	}

	public static int CountUnder(string folderPath)
	{
		string text = folderPath.TrimEnd('\\').Replace("\\", "\\\\").Replace("%", "\\%")
			.Replace("_", "\\_");
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "SELECT COUNT(*) FROM images WHERE missing = 0 AND (folder = $exact OR folder LIKE $prefix ESCAPE '\\');";
		sqliteCommand.Parameters.AddWithValue("$exact", folderPath);
		sqliteCommand.Parameters.AddWithValue("$prefix", text + "\\\\%");
		return Convert.ToInt32(sqliteCommand.ExecuteScalar() ?? ((object)0));
	}
}
