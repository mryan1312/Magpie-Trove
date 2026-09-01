using System;
using System.IO;
using MagpieTrove.Services;
using Microsoft.Data.Sqlite;

namespace MagpieTrove.Data;

public static class Database
{
	private const int SchemaVersion = 7;

	private static string _dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MagpieTrove");

	private static string _connectionString = "";

	public static string DataDirectory => _dataDirectory;

	public static string DatabasePath => Path.Combine(DataDirectory, "magpietrove.db");

	public static void ConfigureLibraryDirectory(string directory)
	{
		if (string.IsNullOrWhiteSpace(directory))
		{
			throw new ArgumentException("A library directory is required.", "directory");
		}
		_dataDirectory = Path.GetFullPath(directory);
	}

	public static void Initialize()
	{
		InitializeAt(DatabasePath);
	}

	internal static void InitializeAt(string databasePath)
	{
		string directoryName = Path.GetDirectoryName(databasePath);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		SqliteConnection.ClearAllPools();
		LegacyMigration.RenameDatabaseFile(databasePath);
		_connectionString = new SqliteConnectionStringBuilder
		{
			DataSource = databasePath,
			Mode = SqliteOpenMode.ReadWriteCreate,
			Cache = SqliteCacheMode.Private,
			Pooling = true
		}.ToString();
		using SqliteConnection cn = Open();
		Exec(cn, "PRAGMA journal_mode=WAL;");
		Exec(cn, "PRAGMA synchronous=NORMAL;");
		Migrate(cn);
		CreateSchema(cn);
	}

	private static void Migrate(SqliteConnection cn)
	{
		if (HasTable(cn, "folders") && !HasColumn(cn, "folders", "offline"))
		{
			Exec(cn, "ALTER TABLE folders ADD COLUMN offline INTEGER NOT NULL DEFAULT 0;");
		}
		if (HasTable(cn, "images") && !HasColumn(cn, "images", "quick_hash"))
		{
			Exec(cn, "ALTER TABLE images ADD COLUMN quick_hash TEXT;");
			Exec(cn, "CREATE INDEX IF NOT EXISTS ix_images_quick_hash ON images(quick_hash);");
		}
		if (HasTable(cn, "images") && !HasColumn(cn, "images", "phash"))
		{
			Exec(cn, "ALTER TABLE images ADD COLUMN phash INTEGER;");
			Exec(cn, "CREATE INDEX IF NOT EXISTS ix_images_phash ON images(phash);");
		}
		if (HasTable(cn, "images") && !HasColumn(cn, "images", "flag"))
		{
			Exec(cn, "ALTER TABLE images ADD COLUMN flag INTEGER NOT NULL DEFAULT 0;");
		}
		if (HasTable(cn, "images"))
		{
			AddColumnIfMissing(cn, "images", "camera_make", "TEXT");
			AddColumnIfMissing(cn, "images", "camera_model", "TEXT");
			AddColumnIfMissing(cn, "images", "lens", "TEXT");
			AddColumnIfMissing(cn, "images", "iso", "INTEGER");
			AddColumnIfMissing(cn, "images", "aperture", "REAL");
			AddColumnIfMissing(cn, "images", "shutter_speed", "REAL");
			AddColumnIfMissing(cn, "images", "focal_length", "REAL");
			AddColumnIfMissing(cn, "images", "exif_scanned", "INTEGER NOT NULL DEFAULT 0");
			AddColumnIfMissing(cn, "images", "rotation_override", "INTEGER NOT NULL DEFAULT 0");
			AddColumnIfMissing(cn, "images", "keywords_scanned", "INTEGER NOT NULL DEFAULT 0");
		}
		if (HasTable(cn, "tags") && !HasColumn(cn, "tags", "pinned_slot"))
		{
			Exec(cn, "ALTER TABLE tags ADD COLUMN pinned_slot INTEGER;");
			Exec(cn, "CREATE UNIQUE INDEX IF NOT EXISTS ux_tags_pinned\n    ON tags(pinned_slot) WHERE pinned_slot IS NOT NULL;");
		}
		if (HasTable(cn, "tags") && !HasColumn(cn, "tags", "parent_id"))
		{
			Exec(cn, "PRAGMA foreign_keys=OFF;");
			using (SqliteTransaction sqliteTransaction = cn.BeginTransaction())
			{
				Exec(cn, "CREATE TABLE tags_migrated (\n    id          INTEGER PRIMARY KEY AUTOINCREMENT,\n    name        TEXT NOT NULL COLLATE NOCASE,\n    parent_id   INTEGER REFERENCES tags(id) ON DELETE SET NULL,\n    color       TEXT,\n    pinned_slot INTEGER\n);\n\nINSERT INTO tags_migrated (id, name, parent_id, color, pinned_slot)\n    SELECT id, name, NULL, color, pinned_slot FROM tags;\n\nDROP TABLE tags;\nALTER TABLE tags_migrated RENAME TO tags;\n\nCREATE UNIQUE INDEX IF NOT EXISTS ux_tags_sibling\n    ON tags(COALESCE(parent_id, 0), name COLLATE NOCASE);\nCREATE INDEX IF NOT EXISTS ix_tags_parent ON tags(parent_id);");
				sqliteTransaction.Commit();
			}
			Exec(cn, "PRAGMA foreign_keys=ON;");
		}
	}

	private static bool HasTable(SqliteConnection cn, string table)
	{
		using SqliteCommand sqliteCommand = cn.CreateCommand();
		sqliteCommand.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name;";
		sqliteCommand.Parameters.AddWithValue("$name", table);
		return sqliteCommand.ExecuteScalar() != null;
	}

	private static bool HasColumn(SqliteConnection cn, string table, string column)
	{
		using SqliteCommand sqliteCommand = cn.CreateCommand();
		sqliteCommand.CommandText = "PRAGMA table_info(" + table + ");";
		using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
		while (sqliteDataReader.Read())
		{
			if (string.Equals(sqliteDataReader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static void AddColumnIfMissing(SqliteConnection cn, string table, string column, string declaration)
	{
		if (!HasColumn(cn, table, column))
		{
			Exec(cn, $"ALTER TABLE {table} ADD COLUMN {column} {declaration};");
		}
	}

	public static SqliteConnection Open()
	{
		SqliteConnection sqliteConnection = new SqliteConnection(_connectionString);
		sqliteConnection.Open();
		Exec(sqliteConnection, "PRAGMA busy_timeout=8000;");
		Exec(sqliteConnection, "PRAGMA foreign_keys=ON;");
		return sqliteConnection;
	}

	private static void CreateSchema(SqliteConnection cn)
	{
		Exec(cn, "CREATE TABLE IF NOT EXISTS meta (\n    key   TEXT PRIMARY KEY,\n    value TEXT NOT NULL\n);\n\nCREATE TABLE IF NOT EXISTS folders (\n    id         INTEGER PRIMARY KEY AUTOINCREMENT,\n    path       TEXT NOT NULL UNIQUE COLLATE NOCASE,\n    date_added TEXT NOT NULL,\n    offline    INTEGER NOT NULL DEFAULT 0\n);\n\nCREATE TABLE IF NOT EXISTS images (\n    id            INTEGER PRIMARY KEY AUTOINCREMENT,\n    path          TEXT NOT NULL UNIQUE COLLATE NOCASE,\n    file_name     TEXT NOT NULL COLLATE NOCASE,\n    folder        TEXT NOT NULL COLLATE NOCASE,\n    file_size     INTEGER NOT NULL,\n    width         INTEGER NOT NULL DEFAULT 0,\n    height        INTEGER NOT NULL DEFAULT 0,\n    date_taken    TEXT,\n    date_modified TEXT NOT NULL,\n    date_added    TEXT NOT NULL,\n    rating        INTEGER NOT NULL DEFAULT 0,\n    missing       INTEGER NOT NULL DEFAULT 0,\n    -- Cull state, orthogonal to star rating: 1 = pick, -1 = reject, 0 = neither.\n    flag          INTEGER NOT NULL DEFAULT 0,\n    -- Content fingerprint, so a file that moves is recognised as the same image\n    -- and keeps its tags instead of returning as a stranger.\n    quick_hash    TEXT,\n    -- 64-bit perceptual hash: survives resizing and recompression, so visually\n    -- identical files match even when their bytes don't.\n    phash         INTEGER\n    ,camera_make   TEXT\n    ,camera_model  TEXT\n    ,lens          TEXT\n    ,iso           INTEGER\n    ,aperture      REAL\n    ,shutter_speed REAL\n    ,focal_length  REAL\n    ,exif_scanned  INTEGER NOT NULL DEFAULT 0\n    ,rotation_override INTEGER NOT NULL DEFAULT 0\n    ,keywords_scanned INTEGER NOT NULL DEFAULT 0\n);\n\nCREATE INDEX IF NOT EXISTS ix_images_quick_hash ON images(quick_hash);\nCREATE INDEX IF NOT EXISTS ix_images_phash ON images(phash);\n\nCREATE INDEX IF NOT EXISTS ix_images_folder     ON images(folder);\nCREATE INDEX IF NOT EXISTS ix_images_date_taken ON images(date_taken);\nCREATE INDEX IF NOT EXISTS ix_images_file_name  ON images(file_name);\nCREATE INDEX IF NOT EXISTS ix_images_camera     ON images(camera_make, camera_model);\nCREATE INDEX IF NOT EXISTS ix_images_lens       ON images(lens);\n\n-- Tags form a tree. Names are unique among siblings rather than globally, so\n-- 'nature/red' and 'car/red' can coexist; a tag's identity to the user is its\n-- full path.\nCREATE TABLE IF NOT EXISTS tags (\n    id          INTEGER PRIMARY KEY AUTOINCREMENT,\n    name        TEXT NOT NULL COLLATE NOCASE,\n    parent_id   INTEGER REFERENCES tags(id) ON DELETE SET NULL,\n    color       TEXT,\n    -- 1-9: the number key that applies this tag. Null for everything else.\n    pinned_slot INTEGER\n);\n\nCREATE UNIQUE INDEX IF NOT EXISTS ux_tags_pinned\n    ON tags(pinned_slot) WHERE pinned_slot IS NOT NULL;\n\n-- COALESCE because SQLite treats NULLs as distinct in a UNIQUE index, which\n-- would otherwise allow duplicate root tags.\nCREATE UNIQUE INDEX IF NOT EXISTS ux_tags_sibling\n    ON tags(COALESCE(parent_id, 0), name COLLATE NOCASE);\n\nCREATE INDEX IF NOT EXISTS ix_tags_parent ON tags(parent_id);\n\n-- Alternative spellings that resolve to one tag, so 'nyc' and 'new york' don't\n-- become two separate piles of photos.\nCREATE TABLE IF NOT EXISTS tag_aliases (\n    alias  TEXT PRIMARY KEY COLLATE NOCASE,\n    tag_id INTEGER NOT NULL REFERENCES tags(id) ON DELETE CASCADE\n);\n\nCREATE INDEX IF NOT EXISTS ix_tag_aliases_tag ON tag_aliases(tag_id);\n\nCREATE TABLE IF NOT EXISTS image_tags (\n    image_id INTEGER NOT NULL REFERENCES images(id) ON DELETE CASCADE,\n    tag_id   INTEGER NOT NULL REFERENCES tags(id)   ON DELETE CASCADE,\n    PRIMARY KEY (image_id, tag_id)\n);\n\nCREATE INDEX IF NOT EXISTS ix_image_tags_tag ON image_tags(tag_id);\n\nCREATE TABLE IF NOT EXISTS collections (\n    id           INTEGER PRIMARY KEY AUTOINCREMENT,\n    name         TEXT NOT NULL,\n    kind         INTEGER NOT NULL DEFAULT 0,\n    rule_json    TEXT,\n    date_created TEXT NOT NULL\n);\n\nCREATE TABLE IF NOT EXISTS collection_images (\n    collection_id INTEGER NOT NULL REFERENCES collections(id) ON DELETE CASCADE,\n    image_id      INTEGER NOT NULL REFERENCES images(id)      ON DELETE CASCADE,\n    sort_order    INTEGER NOT NULL DEFAULT 0,\n    PRIMARY KEY (collection_id, image_id)\n);\n\nCREATE INDEX IF NOT EXISTS ix_collection_images_image ON collection_images(image_id);\n\n-- Visual feature vectors. 'model' is not bookkeeping: vectors from different\n-- encoders are not comparable, and mixing generations silently produces\n-- nonsense rankings rather than an error, so every read filters on it.\nCREATE TABLE IF NOT EXISTS image_embeddings (\n    image_id INTEGER PRIMARY KEY REFERENCES images(id) ON DELETE CASCADE,\n    model    TEXT NOT NULL,\n    dim      INTEGER NOT NULL,\n    vector   BLOB NOT NULL\n);\n\nCREATE INDEX IF NOT EXISTS ix_image_embeddings_model ON image_embeddings(model);\n\n-- One trained linear probe per tag, learned from the user's own labels.\nCREATE TABLE IF NOT EXISTS tag_probes (\n    tag_id     INTEGER PRIMARY KEY REFERENCES tags(id) ON DELETE CASCADE,\n    model      TEXT NOT NULL,\n    dim        INTEGER NOT NULL,\n    weights    BLOB NOT NULL,\n    bias       REAL NOT NULL,\n    positives  INTEGER NOT NULL,\n    negatives  INTEGER NOT NULL,\n    accuracy   REAL NOT NULL,\n    trained_at TEXT NOT NULL\n);");
		Exec(cn, "INSERT OR REPLACE INTO meta(key, value) VALUES('schema_version', $v);", ("$v", 7.ToString()));
	}

	public static void Exec(SqliteConnection cn, string sql, params (string Name, object? Value)[] args)
	{
		using SqliteCommand sqliteCommand = cn.CreateCommand();
		sqliteCommand.CommandText = sql;
		for (int i = 0; i < args.Length; i++)
		{
			var (parameterName, obj) = args[i];
			sqliteCommand.Parameters.AddWithValue(parameterName, obj ?? DBNull.Value);
		}
		sqliteCommand.ExecuteNonQuery();
	}

	public static string? GetMeta(string key)
	{
		using SqliteConnection sqliteConnection = Open();
		using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
		sqliteCommand.CommandText = "SELECT value FROM meta WHERE key = $k;";
		sqliteCommand.Parameters.AddWithValue("$k", key);
		return sqliteCommand.ExecuteScalar() as string;
	}

	public static void SetMeta(string key, string value)
	{
		using SqliteConnection cn = Open();
		Exec(cn, "INSERT OR REPLACE INTO meta(key, value) VALUES($k, $v);", ("$k", key), ("$v", value));
	}

	public static string ToDb(DateTime dt)
	{
		return dt.ToString("yyyy-MM-dd HH:mm:ss");
	}

	public static DateTime? FromDb(object? value)
	{
		if (!(value is string s) || !DateTime.TryParse(s, out var result))
		{
			return null;
		}
		return result;
	}
}
