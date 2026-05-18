using MyORM.Mapping;
using Npgsql;

namespace MyORM.Core;

/// <summary>
/// Base class for a database context. Manages the connection, DbSets, and ChangeTracker.
/// Inherit from this and define DbSet<T> properties for each entity.
/// </summary>
public abstract class DbContext : IDisposable
{
    private readonly NpgsqlConnection _connection;
    public ChangeTracker ChangeTracker { get; } = new ChangeTracker();

    protected DbContext(string connectionString)
    {
        _connection = new NpgsqlConnection(connectionString);
        _connection.Open();
        InitializeDbSets();
    }

    /// <summary>Initialise all DbSet<T> properties on this context via reflection.</summary>
    private void InitializeDbSets()
    {
        foreach (var prop in GetType().GetProperties())
        {
            if (!prop.PropertyType.IsGenericType) continue;
            if (prop.PropertyType.GetGenericTypeDefinition() != typeof(DbSet<>)) continue;

            var entityType = prop.PropertyType.GetGenericArguments()[0];
            var dbSetInstance = Activator.CreateInstance(
                typeof(DbSet<>).MakeGenericType(entityType), this);
            prop.SetValue(this, dbSetInstance);
        }
    }

    /// <summary>Creates the tables for all registered entity types (if they don't exist).</summary>
    public void EnsureCreated(params Type[] entityTypes)
    {
        foreach (var type in entityTypes)
        {
            var sql = EntityMapper.GenerateCreateTableSql(type);
            Console.WriteLine($"[ORM] EnsureCreated: {sql}");
            using var cmd = CreateCommand(sql);
            cmd.ExecuteNonQuery();
        }
    }

    public NpgsqlCommand CreateCommand(string sql)
    {
        var cmd = new NpgsqlCommand(sql, _connection);
        return cmd;
    }

    public NpgsqlConnection GetConnection() => _connection;

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
    }
}
