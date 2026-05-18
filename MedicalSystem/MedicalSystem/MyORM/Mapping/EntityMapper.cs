using System.Reflection;
using MyORM.Attributes;
using Npgsql;

namespace MyORM.Mapping;

public static class EntityMapper
{
    // ─── Map DataReader rows → List<T> using reflection ───────────────────────
    public static List<T> Map<T>(NpgsqlDataReader reader) where T : new()
    {
        var results = new List<T>();
        var type = typeof(T);
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        while (reader.Read())
        {
            var obj = new T();
            foreach (var prop in properties)
            {
                var colAttr = prop.GetCustomAttribute<ColumnAttribute>();
                if (colAttr == null) continue;

                try
                {
                    var ordinal = reader.GetOrdinal(colAttr.Name);
                    var value = reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal);
                    if (value != null)
                        SetProperty(prop, obj, value);
                }
                catch (IndexOutOfRangeException)
                {
                    // Column not in result set – skip silently
                }
            }
            results.Add(obj);
        }
        return results;
    }

    private static void SetProperty(PropertyInfo prop, object obj, object value)
    {
        var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

        if (targetType.IsEnum)
        {
            prop.SetValue(obj, Enum.Parse(targetType, value.ToString()!));
            return;
        }

        try
        {
            prop.SetValue(obj, Convert.ChangeType(value, targetType));
        }
        catch
        {
            prop.SetValue(obj, value);
        }
    }

    // ─── Generate CREATE TABLE SQL from a class using reflection ──────────────
    public static string GenerateCreateTableSql(Type type)
    {
        var tableAttr = type.GetCustomAttribute<TableAttribute>();
        var tableName = tableAttr?.Name ?? type.Name.ToLower();
        var columns = new List<string>();
        var foreignKeys = new List<string>();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var colAttr = prop.GetCustomAttribute<ColumnAttribute>();
            if (colAttr == null) continue;

            var pkAttr = prop.GetCustomAttribute<PrimaryKeyAttribute>();
            var fkAttr = prop.GetCustomAttribute<ForeignKeyAttribute>();

            string colDef;

            if (pkAttr != null)
            {
                if (pkAttr.AutoIncrement)
                    colDef = $"{colAttr.Name} INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY";
                else
                    colDef = $"{colAttr.Name} {GetSqlType(prop.PropertyType)} PRIMARY KEY";
            }
            else
            {
                colDef = $"{colAttr.Name} {GetSqlType(prop.PropertyType)}";
                if (!colAttr.IsNullable) colDef += " NOT NULL";
                if (colAttr.IsUnique) colDef += " UNIQUE";
                if (colAttr.Default != null) colDef += $" DEFAULT {colAttr.Default}";
            }

            columns.Add(colDef);

            if (fkAttr != null)
            {
                foreignKeys.Add(
                    $"FOREIGN KEY ({colAttr.Name}) REFERENCES {fkAttr.ReferencedTable}({fkAttr.ReferencedColumn}) ON DELETE CASCADE"
                );
            }
        }

        var allDefs = columns.Concat(foreignKeys);
        return $"CREATE TABLE IF NOT EXISTS {tableName} (\n  {string.Join(",\n  ", allDefs)}\n);";
    }

    // ─── Get table name from type ──────────────────────────────────────────────
    public static string GetTableName(Type type)
    {
        var attr = type.GetCustomAttribute<TableAttribute>();
        return attr?.Name ?? type.Name.ToLower();
    }

    // ─── Get column name for a property ───────────────────────────────────────
    public static string? GetColumnName(PropertyInfo prop)
    {
        return prop.GetCustomAttribute<ColumnAttribute>()?.Name;
    }

    // ─── Get primary key property ──────────────────────────────────────────────
    public static PropertyInfo? GetPrimaryKeyProperty(Type type)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => p.GetCustomAttribute<PrimaryKeyAttribute>() != null);
    }

    // ─── Map C# types to PostgreSQL types ─────────────────────────────────────
    public static string GetSqlType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying switch
        {
            _ when underlying == typeof(int) => "INTEGER",
            _ when underlying == typeof(long) => "BIGINT",
            _ when underlying == typeof(decimal) => "DECIMAL(18,2)",
            _ when underlying == typeof(float) => "FLOAT",
            _ when underlying == typeof(double) => "DOUBLE PRECISION",
            _ when underlying == typeof(string) => "VARCHAR(255)",
            _ when underlying == typeof(bool) => "BOOLEAN",
            _ when underlying == typeof(DateTime) => "TIMESTAMP WITHOUT TIME ZONE",
            _ when underlying == typeof(DateTimeOffset) => "TIMESTAMP WITH TIME ZONE",
            _ when underlying == typeof(char) => "CHAR(1)",
            _ when underlying.IsEnum => "VARCHAR(50)",
            _ => "TEXT"
        };
    }

    // ─── Build column→value dict for INSERT/UPDATE (excludes PK if autoincrement) ──
    public static Dictionary<string, object?> GetColumnValues(object entity, bool excludePk = false)
    {
        var result = new Dictionary<string, object?>();
        var type = entity.GetType();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var colAttr = prop.GetCustomAttribute<ColumnAttribute>();
            if (colAttr == null) continue;

            var pkAttr = prop.GetCustomAttribute<PrimaryKeyAttribute>();
            if (excludePk && pkAttr != null && pkAttr.AutoIncrement) continue;

            // Skip navigation properties stored as collections
            if (prop.PropertyType.IsGenericType &&
                prop.PropertyType.GetGenericTypeDefinition() == typeof(List<>)) continue;

            result[colAttr.Name] = prop.GetValue(entity);
        }
        return result;
    }
}
