using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using MagpieTrove.Models;

namespace MagpieTrove.Data;

public static class TagRepository
{
	public const char PathSeparator = '/';

	public static List<TagItem> GetAllWithCounts()
	{
		using SqliteConnection sqliteConnection = Database.Open();
		List<TagItem> list = new List<TagItem>();
		using (SqliteCommand sqliteCommand = sqliteConnection.CreateCommand())
		{
			sqliteCommand.CommandText = "SELECT t.id, t.name, t.parent_id, t.color, COUNT(i.id), t.pinned_slot\nFROM tags t\nLEFT JOIN image_tags it ON it.tag_id = t.id\nLEFT JOIN images i      ON i.id = it.image_id AND i.missing = 0\nGROUP BY t.id, t.name, t.parent_id, t.color, t.pinned_slot\nORDER BY t.name COLLATE NOCASE;";
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			while (sqliteDataReader.Read())
			{
				list.Add(new TagItem
				{
					Id = sqliteDataReader.GetInt64(0),
					Name = sqliteDataReader.GetString(1),
					ParentId = (sqliteDataReader.IsDBNull(2) ? ((long?)null) : new long?(sqliteDataReader.GetInt64(2))),
					Color = (sqliteDataReader.IsDBNull(3) ? "#4FA3E3" : sqliteDataReader.GetString(3)),
					Count = sqliteDataReader.GetInt32(4),
					PinnedSlot = (sqliteDataReader.IsDBNull(5) ? ((int?)null) : new int?(sqliteDataReader.GetInt32(5)))
				});
			}
		}
		using (SqliteCommand sqliteCommand2 = sqliteConnection.CreateCommand())
		{
			sqliteCommand2.CommandText = "WITH RECURSIVE subtree(root, id) AS (\n    SELECT id, id FROM tags\n    UNION\n    SELECT s.root, t.id FROM tags t JOIN subtree s ON t.parent_id = s.id\n)\nSELECT s.root, COUNT(DISTINCT it.image_id)\nFROM subtree s\nLEFT JOIN image_tags it ON it.tag_id = s.id\nLEFT JOIN images i      ON i.id = it.image_id AND i.missing = 0\nWHERE i.id IS NOT NULL OR it.image_id IS NULL\nGROUP BY s.root;";
			Dictionary<long, int> dictionary = new Dictionary<long, int>();
			using SqliteDataReader sqliteDataReader2 = sqliteCommand2.ExecuteReader();
			while (sqliteDataReader2.Read())
			{
				dictionary[sqliteDataReader2.GetInt64(0)] = sqliteDataReader2.GetInt32(1);
			}
			foreach (TagItem item in list)
			{
				item.TotalCount = dictionary.GetValueOrDefault(item.Id, item.Count);
			}
		}
		return list;
	}

	public static Dictionary<long, List<long>> GetSubtreeClosure()
	{
		List<(long, long?)> list = new List<(long, long?)>();
		using (SqliteConnection sqliteConnection = Database.Open())
		{
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "SELECT id, parent_id FROM tags;";
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			while (sqliteDataReader.Read())
			{
				list.Add((sqliteDataReader.GetInt64(0), sqliteDataReader.IsDBNull(1) ? ((long?)null) : new long?(sqliteDataReader.GetInt64(1))));
			}
		}
		Dictionary<long, List<long>> dictionary = new Dictionary<long, List<long>>();
		foreach (var (item, num) in list)
		{
			if (num.HasValue)
			{
				long valueOrDefault = num.GetValueOrDefault();
				if (!dictionary.TryGetValue(valueOrDefault, out var value))
				{
					value = (dictionary[valueOrDefault] = new List<long>());
				}
				value.Add(item);
			}
		}
		Dictionary<long, List<long>> dictionary2 = new Dictionary<long, List<long>>(list.Count);
		foreach (var item3 in list)
		{
			long item2 = item3.Item1;
			List<long> list3 = new List<long>();
			Stack<long> stack = new Stack<long>();
			stack.Push(item2);
			while (stack.Count > 0)
			{
				long num2 = stack.Pop();
				list3.Add(num2);
				if (!dictionary.TryGetValue(num2, out var value2))
				{
					continue;
				}
				foreach (long item4 in value2)
				{
					stack.Push(item4);
				}
			}
			dictionary2[item2] = list3;
		}
		return dictionary2;
	}

	public static long GetOrCreate(string path)
	{
		using SqliteConnection cn = Database.Open();
		return GetOrCreate(cn, path);
	}

	private static long GetOrCreate(SqliteConnection cn, string path)
	{
		List<string> list = SplitPath(path);
		if (list.Count == 0)
		{
			return 0L;
		}
		if (list.Count == 1)
		{
			long? num = ResolveAlias(cn, list[0]);
			if (num.HasValue)
			{
				return num.GetValueOrDefault();
			}
			List<long> list2 = FindByName(cn, list[0]);
			if (list2.Count == 1)
			{
				return list2[0];
			}
		}
		long? parentId = null;
		foreach (string item in list)
		{
			parentId = GetOrCreateChild(cn, parentId, item);
		}
		return parentId.GetValueOrDefault();
	}

	private static long GetOrCreateChild(SqliteConnection cn, long? parentId, string name)
	{
		using SqliteCommand sqliteCommand = cn.CreateCommand();
		sqliteCommand.CommandText = ((!parentId.HasValue) ? "SELECT id FROM tags WHERE parent_id IS NULL AND name = $n COLLATE NOCASE;" : "SELECT id FROM tags WHERE parent_id = $p AND name = $n COLLATE NOCASE;");
		sqliteCommand.Parameters.AddWithValue("$n", name);
		if (parentId.HasValue)
		{
			sqliteCommand.Parameters.AddWithValue("$p", parentId.Value);
		}
		if (sqliteCommand.ExecuteScalar() is long result)
		{
			return result;
		}
		using SqliteCommand sqliteCommand2 = cn.CreateCommand();
		sqliteCommand2.CommandText = "INSERT INTO tags(name, parent_id) VALUES($n, $p); SELECT last_insert_rowid();";
		sqliteCommand2.Parameters.AddWithValue("$n", name);
		sqliteCommand2.Parameters.AddWithValue("$p", ((object)parentId) ?? DBNull.Value);
		return Convert.ToInt64(sqliteCommand2.ExecuteScalar());
	}

	private static long? ResolveAlias(SqliteConnection cn, string alias)
	{
		using SqliteCommand sqliteCommand = cn.CreateCommand();
		sqliteCommand.CommandText = "SELECT a.tag_id FROM tag_aliases a\nJOIN tags t ON t.id = a.tag_id\nWHERE a.alias = $a COLLATE NOCASE;";
		sqliteCommand.Parameters.AddWithValue("$a", alias);
		return (sqliteCommand.ExecuteScalar() is long value) ? new long?(value) : ((long?)null);
	}

	public static bool AddAlias(long tagId, string alias)
	{
		alias = Normalize(alias);
		if (alias.Length == 0)
		{
			return false;
		}
		using SqliteConnection cn = Database.Open();
		if (FindByName(cn, alias).Count > 0)
		{
			return false;
		}
		try
		{
			Database.Exec(cn, "INSERT OR REPLACE INTO tag_aliases(alias, tag_id) VALUES($a, $t);", ("$a", alias), ("$t", tagId));
			return true;
		}
		catch (SqliteException)
		{
			return false;
		}
	}

	public static void RemoveAlias(string alias)
	{
		using SqliteConnection cn = Database.Open();
		Database.Exec(cn, "DELETE FROM tag_aliases WHERE alias = $a;", ("$a", alias));
	}

	public static List<string> GetAliases(long tagId)
	{
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "SELECT alias FROM tag_aliases WHERE tag_id = $t ORDER BY alias;";
		sqliteCommand.Parameters.AddWithValue("$t", tagId);
		List<string> list = new List<string>();
		using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
		while (sqliteDataReader.Read())
		{
			list.Add(sqliteDataReader.GetString(0));
		}
		return list;
	}

	public static List<long> GetAncestors(long tagId)
	{
		Dictionary<long, long?> dictionary = new Dictionary<long, long?>();
		using (SqliteConnection sqliteConnection = Database.Open())
		{
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "SELECT id, parent_id FROM tags;";
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			while (sqliteDataReader.Read())
			{
				dictionary[sqliteDataReader.GetInt64(0)] = (sqliteDataReader.IsDBNull(1) ? ((long?)null) : new long?(sqliteDataReader.GetInt64(1)));
			}
		}
		List<long> list = new List<long>();
		int num = 0;
		long? valueOrDefault = dictionary.GetValueOrDefault(tagId);
		while (valueOrDefault.HasValue)
		{
			long valueOrDefault2 = valueOrDefault.GetValueOrDefault();
			if (num++ >= 64)
			{
				break;
			}
			list.Add(valueOrDefault2);
			valueOrDefault = dictionary.GetValueOrDefault(valueOrDefault2);
		}
		return list;
	}

	private static List<long> FindByName(SqliteConnection cn, string name)
	{
		using SqliteCommand sqliteCommand = cn.CreateCommand();
		sqliteCommand.CommandText = "SELECT id FROM tags WHERE name = $n COLLATE NOCASE;";
		sqliteCommand.Parameters.AddWithValue("$n", name);
		List<long> list = new List<long>();
		using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
		while (sqliteDataReader.Read())
		{
			list.Add(sqliteDataReader.GetInt64(0));
		}
		return list;
	}

	public static List<string> SplitPath(string path)
	{
		return (from s in path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Normalize)
			where s.Length > 0
			select s).ToList();
	}

	public static bool Reparent(long tagId, long? newParentId)
	{
		if (tagId == newParentId)
		{
			return false;
		}
		if (newParentId.HasValue)
		{
			long valueOrDefault = newParentId.GetValueOrDefault();
			if (GetSubtreeClosure().TryGetValue(tagId, out List<long> value) && value.Contains(valueOrDefault))
			{
				return false;
			}
		}
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = ((!newParentId.HasValue) ? "SELECT COUNT(*) FROM tags WHERE parent_id IS NULL AND id <> $id AND name = (SELECT name FROM tags WHERE id = $id) COLLATE NOCASE;" : "SELECT COUNT(*) FROM tags WHERE parent_id = $p AND id <> $id AND name = (SELECT name FROM tags WHERE id = $id) COLLATE NOCASE;");
		sqliteCommand.Parameters.AddWithValue("$id", tagId);
		if (newParentId.HasValue)
		{
			sqliteCommand.Parameters.AddWithValue("$p", newParentId.Value);
		}
		if (Convert.ToInt32(sqliteCommand.ExecuteScalar() ?? ((object)0)) > 0)
		{
			return false;
		}
		Database.Exec(sqliteConnection, "UPDATE tags SET parent_id = $p WHERE id = $id;", ("$p", newParentId), ("$id", tagId));
		return true;
	}

	public static long AddTagToImages(string tagName, IEnumerable<long> imageIds)
	{
		List<long> newlyTagged;
		return AddTagToImages(tagName, imageIds, out newlyTagged);
	}

	public static int AddTagsToImage(SqliteConnection cn, long imageId, IEnumerable<string> tags)
	{
		int num = 0;
		foreach (string item in tags.Distinct<string>(StringComparer.OrdinalIgnoreCase))
		{
			string text = Normalize(item);
			if (text.Length != 0)
			{
				long orCreate = GetOrCreate(cn, text);
				using SqliteCommand sqliteCommand = cn.CreateCommand();
				sqliteCommand.CommandText = "INSERT OR IGNORE INTO image_tags(image_id, tag_id) VALUES($i, $t);";
				sqliteCommand.Parameters.AddWithValue("$i", imageId);
				sqliteCommand.Parameters.AddWithValue("$t", orCreate);
				num += sqliteCommand.ExecuteNonQuery();
			}
		}
		return num;
	}

	public static long AddTagToImages(string tagName, IEnumerable<long> imageIds, out List<long> newlyTagged)
	{
		newlyTagged = new List<long>();
		List<long> list = imageIds.ToList();
		string text = Normalize(tagName);
		if (text.Length == 0)
		{
			return 0L;
		}
		using SqliteConnection sqliteConnection = Database.Open();
		long orCreate = GetOrCreate(sqliteConnection, text);
		if (list.Count == 0)
		{
			return orCreate;
		}
		HashSet<long> existing = new HashSet<long>();
		using (SqliteCommand sqliteCommand = sqliteConnection.CreateCommand())
		{
			sqliteCommand.CommandText = "SELECT image_id FROM image_tags WHERE tag_id = $t AND image_id IN (" + string.Join(",", list) + ");";
			sqliteCommand.Parameters.AddWithValue("$t", orCreate);
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			while (sqliteDataReader.Read())
			{
				existing.Add(sqliteDataReader.GetInt64(0));
			}
		}
		newlyTagged = list.Where((long id) => !existing.Contains(id)).Distinct().ToList();
		if (newlyTagged.Count == 0)
		{
			return orCreate;
		}
		using SqliteTransaction sqliteTransaction = sqliteConnection.BeginTransaction();
		using SqliteCommand sqliteCommand2 = sqliteConnection.CreateCommand();
		sqliteCommand2.CommandText = "INSERT OR IGNORE INTO image_tags(image_id, tag_id) VALUES($i, $t);";
		SqliteParameter sqliteParameter = sqliteCommand2.Parameters.Add("$i", SqliteType.Integer);
		sqliteCommand2.Parameters.AddWithValue("$t", orCreate);
		foreach (long item in newlyTagged)
		{
			sqliteParameter.Value = item;
			sqliteCommand2.ExecuteNonQuery();
		}
		sqliteTransaction.Commit();
		return orCreate;
	}

	public static void RemoveTagFromImages(long tagId, IEnumerable<long> imageIds)
	{
		RemoveTagFromImages(tagId, imageIds, out List<long> _);
	}

	public static void RemoveTagFromImages(long tagId, IEnumerable<long> imageIds, out List<long> actuallyRemoved)
	{
		actuallyRemoved = new List<long>();
		List<long> list = imageIds.ToList();
		if (list.Count == 0)
		{
			return;
		}
		using SqliteConnection sqliteConnection = Database.Open();
		using (SqliteCommand sqliteCommand = sqliteConnection.CreateCommand())
		{
			sqliteCommand.CommandText = "SELECT image_id FROM image_tags WHERE tag_id = $t AND image_id IN (" + string.Join(",", list) + ");";
			sqliteCommand.Parameters.AddWithValue("$t", tagId);
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			while (sqliteDataReader.Read())
			{
				actuallyRemoved.Add(sqliteDataReader.GetInt64(0));
			}
		}
		if (actuallyRemoved.Count != 0)
		{
			Database.Exec(sqliteConnection, "DELETE FROM image_tags WHERE tag_id = $t AND image_id IN (" + string.Join(",", list) + ");", ("$t", tagId));
		}
	}

	public static void RestoreTagOnImages(long tagId, IReadOnlyList<long> imageIds)
	{
		if (imageIds.Count == 0)
		{
			return;
		}
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteTransaction sqliteTransaction = sqliteConnection.BeginTransaction();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "INSERT OR IGNORE INTO image_tags(image_id, tag_id) VALUES($i, $t);";
		SqliteParameter sqliteParameter = sqliteCommand.Parameters.Add("$i", SqliteType.Integer);
		sqliteCommand.Parameters.AddWithValue("$t", tagId);
		foreach (long imageId in imageIds)
		{
			sqliteParameter.Value = imageId;
			sqliteCommand.ExecuteNonQuery();
		}
		sqliteTransaction.Commit();
	}

	public static List<string> GetTagNames(long imageId)
	{
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "SELECT t.name FROM tags t\nJOIN image_tags it ON it.tag_id = t.id\nWHERE it.image_id = $i\nORDER BY t.name COLLATE NOCASE;";
		sqliteCommand.Parameters.AddWithValue("$i", imageId);
		List<string> list = new List<string>();
		using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
		while (sqliteDataReader.Read())
		{
			list.Add(sqliteDataReader.GetString(0));
		}
		return list;
	}

	public static List<TagChip> GetTagsForSelection(IReadOnlyList<long> imageIds)
	{
		if (imageIds.Count == 0)
		{
			return new List<TagChip>();
		}
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "SELECT t.id, t.name, COALESCE(t.color, '#4FA3E3'), COUNT(*) FROM tags t\nJOIN image_tags it ON it.tag_id = t.id\nWHERE it.image_id IN (" + string.Join(",", imageIds) + ")\nGROUP BY t.id, t.name, t.color\nORDER BY COUNT(*) DESC, t.name COLLATE NOCASE;";
		List<TagChip> list = new List<TagChip>();
		using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
		while (sqliteDataReader.Read())
		{
			list.Add(new TagChip
			{
				Id = sqliteDataReader.GetInt64(0),
				Name = sqliteDataReader.GetString(1),
				Color = sqliteDataReader.GetString(2),
				AppliedCount = sqliteDataReader.GetInt32(3),
				SelectionCount = imageIds.Count
			});
		}
		return list;
	}

	public static Dictionary<long, List<long>> GetTagIdsForImages(IReadOnlyCollection<long> imageIds)
	{
		Dictionary<long, List<long>> dictionary = new Dictionary<long, List<long>>();
		if (imageIds.Count == 0)
		{
			return dictionary;
		}
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "SELECT image_id, tag_id FROM image_tags WHERE image_id IN (" + string.Join(",", imageIds) + ");";
		using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
		while (sqliteDataReader.Read())
		{
			long @int = sqliteDataReader.GetInt64(0);
			if (!dictionary.TryGetValue(@int, out var value))
			{
				value = (dictionary[@int] = new List<long>());
			}
			value.Add(sqliteDataReader.GetInt64(1));
		}
		return dictionary;
	}

	public static HashSet<long> GetTaggedImageIds()
	{
		HashSet<long> hashSet = new HashSet<long>();
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "SELECT DISTINCT it.image_id FROM image_tags it\nJOIN images i ON i.id = it.image_id AND i.missing = 0;";
		using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
		while (sqliteDataReader.Read())
		{
			hashSet.Add(sqliteDataReader.GetInt64(0));
		}
		return hashSet;
	}

	public static HashSet<long> GetImageIdsWithTag(long tagId)
	{
		HashSet<long> hashSet = new HashSet<long>();
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "SELECT it.image_id FROM image_tags it\nJOIN images i ON i.id = it.image_id AND i.missing = 0\nWHERE it.tag_id = $t;";
		sqliteCommand.Parameters.AddWithValue("$t", tagId);
		using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
		while (sqliteDataReader.Read())
		{
			hashSet.Add(sqliteDataReader.GetInt64(0));
		}
		return hashSet;
	}

	public static bool Rename(long tagId, string newName)
	{
		newName = Normalize(newName);
		if (newName.Length == 0)
		{
			return false;
		}
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "SELECT id FROM tags\nWHERE name = $n COLLATE NOCASE AND id <> $id\n  AND COALESCE(parent_id, 0) = (SELECT COALESCE(parent_id, 0) FROM tags WHERE id = $id);";
		sqliteCommand.Parameters.AddWithValue("$n", newName);
		sqliteCommand.Parameters.AddWithValue("$id", tagId);
		if (sqliteCommand.ExecuteScalar() is long targetTagId)
		{
			MergeInto(sqliteConnection, tagId, targetTagId);
			return true;
		}
		Database.Exec(sqliteConnection, "UPDATE tags SET name = $n WHERE id = $id;", ("$n", newName), ("$id", tagId));
		return true;
	}

	public static void Merge(long sourceTagId, long targetTagId)
	{
		if (sourceTagId == targetTagId)
		{
			return;
		}
		using SqliteConnection cn = Database.Open();
		MergeInto(cn, sourceTagId, targetTagId);
	}

	private static void MergeInto(SqliteConnection cn, long sourceTagId, long targetTagId)
	{
		using SqliteTransaction sqliteTransaction = cn.BeginTransaction();
		Database.Exec(cn, "INSERT OR IGNORE INTO image_tags(image_id, tag_id)\nSELECT image_id, $target FROM image_tags WHERE tag_id = $source;", ("$target", targetTagId), ("$source", sourceTagId));
		Database.Exec(cn, "DELETE FROM tags WHERE id = $source;", ("$source", sourceTagId));
		sqliteTransaction.Commit();
	}

	public static void Delete(long tagId)
	{
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteTransaction sqliteTransaction = sqliteConnection.BeginTransaction();
		Database.Exec(sqliteConnection, "UPDATE tags SET parent_id = (SELECT parent_id FROM tags WHERE id = $id)\nWHERE parent_id = $id;", ("$id", tagId));
		Database.Exec(sqliteConnection, "DELETE FROM tags WHERE id = $id;", ("$id", tagId));
		sqliteTransaction.Commit();
	}

	public static void DeleteSubtree(long tagId)
	{
		if (!GetSubtreeClosure().TryGetValue(tagId, out List<long> value) || value.Count == 0)
		{
			return;
		}
		using SqliteConnection cn = Database.Open();
		Database.Exec(cn, "DELETE FROM tags WHERE id IN (" + string.Join(",", value) + ");");
	}

	public static int CountDescendants(long tagId)
	{
		if (!GetSubtreeClosure().TryGetValue(tagId, out List<long> value))
		{
			return 0;
		}
		return value.Count - 1;
	}

	public static void SetPinnedSlot(long tagId, int? slot)
	{
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteTransaction sqliteTransaction = sqliteConnection.BeginTransaction();
		if (slot.HasValue)
		{
			int valueOrDefault = slot.GetValueOrDefault();
			Database.Exec(sqliteConnection, "UPDATE tags SET pinned_slot = NULL WHERE pinned_slot = $s;", ("$s", valueOrDefault));
		}
		Database.Exec(sqliteConnection, "UPDATE tags SET pinned_slot = $s WHERE id = $id;", ("$s", slot), ("$id", tagId));
		sqliteTransaction.Commit();
	}

	public static Dictionary<int, (long Id, string Name)> GetPinned()
	{
		Dictionary<int, (long, string)> dictionary = new Dictionary<int, (long, string)>();
		using SqliteConnection sqliteConnection = Database.Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "SELECT pinned_slot, id, name FROM tags WHERE pinned_slot IS NOT NULL ORDER BY pinned_slot;";
		using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
		while (sqliteDataReader.Read())
		{
			dictionary[sqliteDataReader.GetInt32(0)] = (sqliteDataReader.GetInt64(1), sqliteDataReader.GetString(2));
		}
		return dictionary;
	}

	public static int? NextFreeSlot()
	{
		HashSet<int> hashSet = GetPinned().Keys.ToHashSet();
		for (int i = 1; i <= 9; i++)
		{
			if (!hashSet.Contains(i))
			{
				return i;
			}
		}
		return null;
	}

	public static void SetColor(long tagId, string color)
	{
		using SqliteConnection cn = Database.Open();
		Database.Exec(cn, "UPDATE tags SET color = $c WHERE id = $id;", ("$c", color), ("$id", tagId));
	}

	public static string Normalize(string name)
	{
		return name.Trim().Replace(",", " ").Trim();
	}
}
