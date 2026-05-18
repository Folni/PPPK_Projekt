namespace MyORM.Attributes;

/// <summary>
/// Marks a property as a navigation property (relationship).
/// foreignKeyProperty = name of the property in THIS class that holds the FK value.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class NavigationAttribute : Attribute
{
    public string ForeignKeyProperty { get; }
    public bool LazyLoad { get; set; } = false;

    public NavigationAttribute(string foreignKeyProperty)
    {
        ForeignKeyProperty = foreignKeyProperty;
    }
}
