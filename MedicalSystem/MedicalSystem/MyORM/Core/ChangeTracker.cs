using System.Reflection;
using MyORM.Attributes;

namespace MyORM.Core;

public enum EntityState { Added, Unchanged, Modified, Deleted }

public class TrackedEntity
{
    public object Entity { get; set; } = null!;
    public EntityState State { get; set; }
    public Dictionary<string, object?> OriginalValues { get; set; } = new();
}

/// <summary>
/// Tracks entity snapshots and detects changes.
/// Similar to EF Core's ChangeTracker / DbContext.Entry().
/// </summary>
public class ChangeTracker
{
    private readonly List<TrackedEntity> _tracked = new();

    public void Track(object entity, EntityState state = EntityState.Unchanged)
    {
        if (_tracked.Any(t => ReferenceEquals(t.Entity, entity))) return;

        _tracked.Add(new TrackedEntity
        {
            Entity = entity,
            State = state,
            OriginalValues = Snapshot(entity)
        });
    }

    public void MarkAdded(object entity)
    {
        var entry = GetEntry(entity);
        if (entry != null) entry.State = EntityState.Added;
        else _tracked.Add(new TrackedEntity { Entity = entity, State = EntityState.Added, OriginalValues = Snapshot(entity) });
    }

    public void MarkDeleted(object entity)
    {
        var entry = GetEntry(entity);
        if (entry != null) entry.State = EntityState.Deleted;
    }

    /// <summary>Returns only the column-name→value pairs that changed since tracking began.</summary>
    public Dictionary<string, object?> GetChanges(object entity)
    {
        var entry = GetEntry(entity);
        if (entry == null) return new();

        var changes = new Dictionary<string, object?>();
        var current = Snapshot(entity);

        foreach (var (col, currentVal) in current)
        {
            if (!entry.OriginalValues.TryGetValue(col, out var originalVal) ||
                !Equals(currentVal, originalVal))
            {
                changes[col] = currentVal;
            }
        }
        return changes;
    }

    public IEnumerable<TrackedEntity> GetAll() => _tracked;

    public void UpdateSnapshot(object entity)
    {
        var entry = GetEntry(entity);
        if (entry != null)
        {
            entry.OriginalValues = Snapshot(entity);
            entry.State = EntityState.Unchanged;
        }
    }

    public void Detach(object entity) => _tracked.RemoveAll(t => ReferenceEquals(t.Entity, entity));
    public void Clear() => _tracked.Clear();

    private TrackedEntity? GetEntry(object entity) =>
        _tracked.FirstOrDefault(t => ReferenceEquals(t.Entity, entity));

    private static Dictionary<string, object?> Snapshot(object entity)
    {
        var snap = new Dictionary<string, object?>();
        foreach (var prop in entity.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var colAttr = prop.GetCustomAttribute<ColumnAttribute>();
            if (colAttr == null) continue;
            snap[colAttr.Name] = prop.GetValue(entity);
        }
        return snap;
    }
}
