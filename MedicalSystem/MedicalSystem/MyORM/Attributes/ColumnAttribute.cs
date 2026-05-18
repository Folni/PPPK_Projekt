namespace MyORM.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public class ColumnAttribute : Attribute
{
    public string Name { get; }
    public bool IsNullable { get; set; } = true;
    public bool IsUnique { get; set; } = false;
    public string? Default { get; set; }

    public ColumnAttribute(string name) => Name = name;
}
