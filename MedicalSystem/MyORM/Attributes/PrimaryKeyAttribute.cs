namespace MyORM.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public class PrimaryKeyAttribute : Attribute
{
    public bool AutoIncrement { get; set; } = true;
}
