using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using MyORM.Attributes;
using MyORM.Mapping;
using MyORM.Query;
using Npgsql;

namespace MyORM.Core;

/// <summary>
/// Represents a queryable/mutable set of entities of type T.
/// Supports CRUD, filtering, sorting, eager loading, and ChangeTracker integration.
/// </summary>
public class DbSet<T> where T : class, new()
{
    private readonly DbContext _context;
    private readonly string _tableName;

    private string? _whereClause;
    private readonly List<(string Name, object? Value)> _whereParams = new();
    private string? _orderByClause;
    private bool _orderByDesc;
    private int? _limitValue;
    private readonly List<string> _includes = new();  // eager load navigation props

    public DbSet(DbContext context)
    {
        _context = context;
        _tableName = EntityMapper.GetTableName(typeof(T));
    }

    // ─── Fluent filtering ───────────────────────────────────────────────────────
    public DbSet<T> Where(Expression<Func<T, bool>> predicate)
    {
        var visitor = new WhereExpressionVisitor();
        visitor.Visit(predicate.Body);
        _whereClause = visitor.GetSql();
        _whereParams.AddRange(visitor.GetParameters());
        return this;
    }

    public DbSet<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector, bool descending = false)
    {
        if (keySelector.Body is MemberExpression member)
        {
            var prop = member.Member as PropertyInfo;
            _orderByClause = prop?.GetCustomAttribute<ColumnAttribute>()?.Name ?? member.Member.Name.ToLower();
            _orderByDesc = descending;
        }
        return this;
    }

    public DbSet<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector) =>
        OrderBy(keySelector, descending: true);

    public DbSet<T> Take(int count) { _limitValue = count; return this; }

    /// <summary>Eager-loads a navigation property by name.</summary>
    public DbSet<T> Include(string navigationPropertyName)
    {
        _includes.Add(navigationPropertyName);
        return this;
    }

    // ─── Terminal operations ────────────────────────────────────────────────────
    public List<T> ToList()
    {
        var sql = BuildSelectSql();
        var result = ExecuteQuery(sql);
        if (_includes.Count > 0) LoadNavigations(result);
        TrackAll(result);
        Reset();
        return result;
    }

    public T? FirstOrDefault()
    {
        _limitValue = 1;
        var list = ToList();
        return list.FirstOrDefault();
    }

    public T? FirstOrDefault(Expression<Func<T, bool>> predicate)
    {
        Where(predicate);
        return FirstOrDefault();
    }

    public int Count()
    {
        var sql = $"SELECT COUNT(*) FROM {_tableName}" + BuildWhere();
        using var cmd = _context.CreateCommand(sql);
        AddWhereParams(cmd);
        var result = cmd.ExecuteScalar();
        Reset();
        return Convert.ToInt32(result);
    }

    public bool Any(Expression<Func<T, bool>> predicate)
    {
        Where(predicate);
        return Count() > 0;
    }

    // ─── CRUD ──────────────────────────────────────────────────────────────────
    public T Add(T entity)
    {
        var colVals = EntityMapper.GetColumnValues(entity, excludePk: true);
        var cols = string.Join(", ", colVals.Keys);
        var paramNames = string.Join(", ", colVals.Keys.Select((k, i) => $"@p{i}"));

        var sql = $"INSERT INTO {_tableName} ({cols}) VALUES ({paramNames}) RETURNING *;";
        using var cmd = _context.CreateCommand(sql);

        int i = 0;
        foreach (var (key, val) in colVals)
            cmd.Parameters.AddWithValue($"@p{i++}", val ?? DBNull.Value);

        using var reader = cmd.ExecuteReader();
        var inserted = EntityMapper.Map<T>(reader).First();
        _context.ChangeTracker.Track(inserted, EntityState.Unchanged);
        return inserted;
    }

    public void Update(T entity)
    {
        var changes = _context.ChangeTracker.GetChanges(entity);
        if (changes.Count == 0) return;

        var pkProp = EntityMapper.GetPrimaryKeyProperty(typeof(T))!;
        var pkCol = EntityMapper.GetColumnName(pkProp)!;
        var pkVal = pkProp.GetValue(entity);

        // Remove pk from changes if present
        changes.Remove(pkCol);
        if (changes.Count == 0) return;

        var setClauses = changes.Keys.Select((k, i) => $"{k} = @p{i}").ToList();
        var sql = $"UPDATE {_tableName} SET {string.Join(", ", setClauses)} WHERE {pkCol} = @pk;";

        using var cmd = _context.CreateCommand(sql);
        int idx = 0;
        foreach (var val in changes.Values)
            cmd.Parameters.AddWithValue($"@p{idx++}", val ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pk", pkVal!);

        cmd.ExecuteNonQuery();
        _context.ChangeTracker.UpdateSnapshot(entity);
    }

    /// <summary>Saves any pending changes detected by the ChangeTracker.</summary>
    public void SaveChanges()
    {
        foreach (var entry in _context.ChangeTracker.GetAll())
        {
            if (entry.Entity is not T typedEntity) continue;

            switch (entry.State)
            {
                case EntityState.Added:
                    Add(typedEntity);
                    break;
                case EntityState.Modified:
                    Update(typedEntity);
                    break;
                case EntityState.Deleted:
                    Delete(typedEntity);
                    break;
            }
        }
    }

    public void Delete(T entity)
    {
        var pkProp = EntityMapper.GetPrimaryKeyProperty(typeof(T))!;
        var pkCol = EntityMapper.GetColumnName(pkProp)!;
        var pkVal = pkProp.GetValue(entity);

        var sql = $"DELETE FROM {_tableName} WHERE {pkCol} = @pk;";
        using var cmd = _context.CreateCommand(sql);
        cmd.Parameters.AddWithValue("@pk", pkVal!);
        cmd.ExecuteNonQuery();
        _context.ChangeTracker.Detach(entity);
    }

    public void DeleteById(int id)
    {
        var pkProp = EntityMapper.GetPrimaryKeyProperty(typeof(T))!;
        var pkCol = EntityMapper.GetColumnName(pkProp)!;
        var sql = $"DELETE FROM {_tableName} WHERE {pkCol} = @pk;";
        using var cmd = _context.CreateCommand(sql);
        cmd.Parameters.AddWithValue("@pk", id);
        cmd.ExecuteNonQuery();
    }

    // ─── Eager Loading ─────────────────────────────────────────────────────────
    private void LoadNavigations(List<T> entities)
    {
        if (entities.Count == 0) return;
        var type = typeof(T);

        foreach (var navName in _includes)
        {
            var navProp = type.GetProperty(navName);
            if (navProp == null) continue;

            var navAttr = navProp.GetCustomAttribute<NavigationAttribute>();
            if (navAttr == null) continue;

            var fkProp = type.GetProperty(navAttr.ForeignKeyProperty);
            if (fkProp == null) continue;

            var navType = navProp.PropertyType;

            // Collection navigation (1:N)
            if (navType.IsGenericType && navType.GetGenericTypeDefinition() == typeof(List<>))
            {
                var elementType = navType.GetGenericArguments()[0];
                LoadCollectionNavigation(entities, navProp, fkProp, elementType);
            }
            // Single navigation (N:1 or 1:1)
            else
            {
                LoadSingleNavigation(entities, navProp, fkProp, navType);
            }
        }
    }

    private void LoadCollectionNavigation(List<T> entities, PropertyInfo navProp, PropertyInfo fkProp, Type elementType)
    {
        var relatedTableName = EntityMapper.GetTableName(elementType);
        var relatedPkProp = EntityMapper.GetPrimaryKeyProperty(elementType);
        if (relatedPkProp == null) return;

        // For collection nav: fkProp on the related entity references this entity's PK
        // We load all related rows where their FK matches any of our entities' PKs
        var ourPkProp = EntityMapper.GetPrimaryKeyProperty(typeof(T));
        if (ourPkProp == null) return;

        var ids = entities.Select(e => ourPkProp.GetValue(e)).Distinct().ToList();
        var fkColOnRelated = fkProp.Name.ToLower(); // Simplified: use property name

        // Find the FK column on related entity pointing to our table
        var relatedFkProp = elementType.GetProperties()
            .FirstOrDefault(p =>
            {
                var fkAttr = p.GetCustomAttribute<ForeignKeyAttribute>();
                return fkAttr != null && fkAttr.ReferencedTable == _tableName;
            });

        if (relatedFkProp == null) return;
        var relatedFkCol = EntityMapper.GetColumnName(relatedFkProp)!;

        var placeholders = string.Join(", ", ids.Select((_, i) => $"@id{i}"));
        var sql = $"SELECT * FROM {relatedTableName} WHERE {relatedFkCol} IN ({placeholders});";

        using var cmd = _context.CreateCommand(sql);
        for (int i = 0; i < ids.Count; i++)
            cmd.Parameters.AddWithValue($"@id{i}", ids[i]!);

        using var reader = cmd.ExecuteReader();
        // Use EntityMapper.Map via reflection for the element type
        var mapMethod = typeof(EntityMapper)
            .GetMethod(nameof(EntityMapper.Map))!
            .MakeGenericMethod(elementType);

        var relatedList = (System.Collections.IList)mapMethod.Invoke(null, new object[] { reader })!;

        // Assign to each entity
        foreach (var entity in entities)
        {
            var myPkVal = ourPkProp.GetValue(entity);
            var matching = relatedList.Cast<object>()
                .Where(r => Equals(relatedFkProp.GetValue(r), myPkVal))
                .ToList();

            var typedList = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
            foreach (var item in matching) typedList.Add(item);
            navProp.SetValue(entity, typedList);
        }
    }

    private void LoadSingleNavigation(List<T> entities, PropertyInfo navProp, PropertyInfo fkProp, Type navType)
    {
        var relatedTableName = EntityMapper.GetTableName(navType);
        var relatedPkProp = EntityMapper.GetPrimaryKeyProperty(navType);
        if (relatedPkProp == null) return;
        var relatedPkCol = EntityMapper.GetColumnName(relatedPkProp)!;

        var fkValues = entities
            .Select(e => fkProp.GetValue(e))
            .Where(v => v != null)
            .Distinct()
            .ToList();

        if (fkValues.Count == 0) return;

        var placeholders = string.Join(", ", fkValues.Select((_, i) => $"@fk{i}"));
        var sql = $"SELECT * FROM {relatedTableName} WHERE {relatedPkCol} IN ({placeholders});";

        using var cmd = _context.CreateCommand(sql);
        for (int i = 0; i < fkValues.Count; i++)
            cmd.Parameters.AddWithValue($"@fk{i}", fkValues[i]!);

        using var reader = cmd.ExecuteReader();
        var mapMethod = typeof(EntityMapper).GetMethod(nameof(EntityMapper.Map))!.MakeGenericMethod(navType);
        var relatedItems = (System.Collections.IList)mapMethod.Invoke(null, new object[] { reader })!;

        var relatedPkValues = relatedItems.Cast<object>()
            .ToDictionary(r => relatedPkProp.GetValue(r)!, r => r);

        foreach (var entity in entities)
        {
            var fkVal = fkProp.GetValue(entity);
            if (fkVal != null && relatedPkValues.TryGetValue(fkVal, out var related))
                navProp.SetValue(entity, related);
        }
    }

    // ─── SQL building helpers ──────────────────────────────────────────────────
    private string BuildSelectSql()
    {
        var sb = new StringBuilder($"SELECT * FROM {_tableName}");
        sb.Append(BuildWhere());
        if (_orderByClause != null)
            sb.Append($" ORDER BY {_orderByClause}{(_orderByDesc ? " DESC" : "")}");
        if (_limitValue.HasValue)
            sb.Append($" LIMIT {_limitValue}");
        sb.Append(';');
        return sb.ToString();
    }

    private string BuildWhere() =>
        _whereClause != null ? $" WHERE {_whereClause}" : "";

    private List<T> ExecuteQuery(string sql)
    {
        using var cmd = _context.CreateCommand(sql);
        AddWhereParams(cmd);
        using var reader = cmd.ExecuteReader();
        return EntityMapper.Map<T>(reader);
    }

    private void AddWhereParams(NpgsqlCommand cmd)
    {
        foreach (var (name, val) in _whereParams)
            cmd.Parameters.AddWithValue(name, val ?? DBNull.Value);
    }

    private void TrackAll(IEnumerable<T> entities)
    {
        foreach (var e in entities)
            _context.ChangeTracker.Track(e, EntityState.Unchanged);
    }

    private void Reset()
    {
        _whereClause = null;
        _whereParams.Clear();
        _orderByClause = null;
        _orderByDesc = false;
        _limitValue = null;
        _includes.Clear();
    }
}
