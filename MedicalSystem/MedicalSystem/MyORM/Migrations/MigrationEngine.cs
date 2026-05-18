using System.Reflection;
using System.Text;
using MyORM.Attributes;
using MyORM.Mapping;
using Npgsql;

namespace MyORM.Migrations;

public class MigrationRecord
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string UpSql { get; set; } = "";
    public string DownSql { get; set; } = "";
    public DateTime AppliedAt { get; set; }
}

/// <summary>
/// Automatically generates migrations by comparing current DB schema with entity class definitions.
/// Tracks applied migrations in the __orm_migrations table.
/// Supports Apply (forward) and Rollback (backward).
/// </summary>
public class MigrationEngine
{
    private readonly NpgsqlConnection _connection;
    private const string MigrationsTable = "__orm_migrations";

    public MigrationEngine(NpgsqlConnection connection)
    {
        _connection = connection;
        EnsureMigrationsTable();
    }

    // ─── Migration table setup ─────────────────────────────────────────────────
    private void EnsureMigrationsTable()
    {
        var sql = $"""
            CREATE TABLE IF NOT EXISTS {MigrationsTable} (
                id INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                name VARCHAR(255) NOT NULL UNIQUE,
                up_sql TEXT NOT NULL,
                down_sql TEXT NOT NULL,
                applied_at TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT NOW()
            );
            """;
        Execute(sql);
    }

    // ─── Generate migration SQL by diffing DB schema vs entity definitions ─────
    public (string upSql, string downSql) GenerateMigration(Type[] entityTypes)
    {
        var upSql = new StringBuilder();
        var downSql = new StringBuilder();

        foreach (var type in entityTypes)
        {
            var tableName = EntityMapper.GetTableName(type);
            var tableExists = TableExists(tableName);

            if (!tableExists)
            {
                // Table doesn't exist → CREATE
                upSql.AppendLine(EntityMapper.GenerateCreateTableSql(type));
                downSql.AppendLine($"DROP TABLE IF EXISTS {tableName} CASCADE;");
            }
            else
            {
                // Table exists → diff columns
                var dbColumns = GetDbColumns(tableName);
                var codeColumns = GetCodeColumns(type);

                // Add missing columns
                foreach (var (colName, colDef) in codeColumns)
                {
                    if (!dbColumns.ContainsKey(colName))
                    {
                        upSql.AppendLine($"ALTER TABLE {tableName} ADD COLUMN {colName} {colDef};");
                        downSql.AppendLine($"ALTER TABLE {tableName} DROP COLUMN IF EXISTS {colName};");
                    }
                }

                // Drop removed columns
                foreach (var colName in dbColumns.Keys)
                {
                    if (!codeColumns.ContainsKey(colName))
                    {
                        upSql.AppendLine($"ALTER TABLE {tableName} DROP COLUMN IF EXISTS {colName};");
                        downSql.AppendLine($"-- Reverse of DROP COLUMN {colName} requires manual intervention");
                    }
                }
            }
        }

        return (upSql.ToString().Trim(), downSql.ToString().Trim());
    }

    // ─── Apply a named migration ───────────────────────────────────────────────
    public void Apply(string migrationName, Type[] entityTypes)
    {
        if (IsMigrationApplied(migrationName))
        {
            Console.WriteLine($"[Migration] '{migrationName}' already applied – skipping.");
            return;
        }

        var (upSql, downSql) = GenerateMigration(entityTypes);

        if (string.IsNullOrWhiteSpace(upSql))
        {
            Console.WriteLine("[Migration] No changes detected – schema is up to date.");
            return;
        }

        Console.WriteLine($"[Migration] Applying '{migrationName}'...");
        Console.WriteLine(upSql);

        Execute(upSql);

        var recordSql = $"""
            INSERT INTO {MigrationsTable} (name, up_sql, down_sql)
            VALUES (@name, @up, @down);
            """;
        using var cmd = new NpgsqlCommand(recordSql, _connection);
        cmd.Parameters.AddWithValue("@name", migrationName);
        cmd.Parameters.AddWithValue("@up", upSql);
        cmd.Parameters.AddWithValue("@down", downSql);
        cmd.ExecuteNonQuery();

        Console.WriteLine($"[Migration] '{migrationName}' applied successfully.");
    }

    // ─── Rollback the latest migration ────────────────────────────────────────
    public void Rollback()
    {
        var latest = GetLatestMigration();
        if (latest == null)
        {
            Console.WriteLine("[Migration] No migrations to roll back.");
            return;
        }

        Console.WriteLine($"[Migration] Rolling back '{latest.Name}'...");
        Console.WriteLine(latest.DownSql);

        if (!string.IsNullOrWhiteSpace(latest.DownSql))
            Execute(latest.DownSql);

        var deleteSql = $"DELETE FROM {MigrationsTable} WHERE name = @name;";
        using var cmd = new NpgsqlCommand(deleteSql, _connection);
        cmd.Parameters.AddWithValue("@name", latest.Name);
        cmd.ExecuteNonQuery();

        Console.WriteLine($"[Migration] '{latest.Name}' rolled back successfully.");
    }

    // ─── Rollback a specific named migration ──────────────────────────────────
    public void Rollback(string migrationName)
    {
        var sql = $"SELECT name, up_sql, down_sql FROM {MigrationsTable} WHERE name = @name;";
        using var cmd = new NpgsqlCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@name", migrationName);
        using var reader = cmd.ExecuteReader();

        if (!reader.Read())
        {
            Console.WriteLine($"[Migration] Migration '{migrationName}' not found.");
            return;
        }

        var downSql = reader.GetString(2);
        reader.Close();

        if (!string.IsNullOrWhiteSpace(downSql))
            Execute(downSql);

        var deleteSql = $"DELETE FROM {MigrationsTable} WHERE name = @name;";
        using var deleteCmd = new NpgsqlCommand(deleteSql, _connection);
        deleteCmd.Parameters.AddWithValue("@name", migrationName);
        deleteCmd.ExecuteNonQuery();

        Console.WriteLine($"[Migration] '{migrationName}' rolled back.");
    }

    // ─── List all applied migrations ───────────────────────────────────────────
    public List<MigrationRecord> GetAppliedMigrations()
    {
        var sql = $"SELECT id, name, up_sql, down_sql, applied_at FROM {MigrationsTable} ORDER BY id;";
        using var cmd = new NpgsqlCommand(sql, _connection);
        using var reader = cmd.ExecuteReader();

        var records = new List<MigrationRecord>();
        while (reader.Read())
        {
            records.Add(new MigrationRecord
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                UpSql = reader.GetString(2),
                DownSql = reader.GetString(3),
                AppliedAt = reader.GetDateTime(4)
            });
        }
        return records;
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────
    private bool TableExists(string tableName)
    {
        var sql = "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = @t AND table_schema = 'public';";
        using var cmd = new NpgsqlCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@t", tableName);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private Dictionary<string, string> GetDbColumns(string tableName)
    {
        var sql = """
            SELECT column_name, data_type
            FROM information_schema.columns
            WHERE table_name = @t AND table_schema = 'public';
            """;
        using var cmd = new NpgsqlCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@t", tableName);
        using var reader = cmd.ExecuteReader();

        var cols = new Dictionary<string, string>();
        while (reader.Read())
            cols[reader.GetString(0)] = reader.GetString(1);
        return cols;
    }

    private static Dictionary<string, string> GetCodeColumns(Type type)
    {
        var cols = new Dictionary<string, string>();
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var colAttr = prop.GetCustomAttribute<ColumnAttribute>();
            if (colAttr == null) continue;
            cols[colAttr.Name] = EntityMapper.GetSqlType(prop.PropertyType);
        }
        return cols;
    }

    private bool IsMigrationApplied(string name)
    {
        var sql = $"SELECT COUNT(*) FROM {MigrationsTable} WHERE name = @name;";
        using var cmd = new NpgsqlCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@name", name);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private MigrationRecord? GetLatestMigration()
    {
        var sql = $"SELECT id, name, up_sql, down_sql, applied_at FROM {MigrationsTable} ORDER BY id DESC LIMIT 1;";
        using var cmd = new NpgsqlCommand(sql, _connection);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new MigrationRecord
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            UpSql = reader.GetString(2),
            DownSql = reader.GetString(3),
            AppliedAt = reader.GetDateTime(4)
        };
    }

    private void Execute(string sql)
    {
        // Split on semicolons to handle multiple statements
        var statements = sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var stmt in statements)
        {
            if (string.IsNullOrWhiteSpace(stmt)) continue;
            using var cmd = new NpgsqlCommand(stmt + ";", _connection);
            cmd.ExecuteNonQuery();
        }
    }
}
