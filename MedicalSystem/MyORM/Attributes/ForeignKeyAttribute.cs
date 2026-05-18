namespace MyORM.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public class ForeignKeyAttribute : Attribute
{
    public string ReferencedTable { get; }
    public string ReferencedColumn { get; }

    public ForeignKeyAttribute(string referencedTable, string referencedColumn)
    {
        ReferencedTable = referencedTable;
        ReferencedColumn = referencedColumn;
    }
}
